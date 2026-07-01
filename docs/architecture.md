# Architektur

## Projektübersicht

```
HyperVGroupManager/
├── src/
│   ├── HyperVGroupManager.App/         WPF, MVVM (CommunityToolkit.Mvvm), DI-Composition-Root
│   ├── HyperVGroupManager.Core/        Models, Interfaces, Results, Exceptions, fachliche Logik
│   └── HyperVGroupManager.PowerShell/  PowerShell-Modul (Backend), JSON-Vertrag
├── tests/
│   └── HyperVGroupManager.Tests/       xUnit, Fakes statt echter PowerShell-Ausführung
└── docs/
```

## Schichten und Abhängigkeitsrichtung

```
Views (XAML)  ->  ViewModels  ->  IHyperVGroupService (Core.Interfaces)
                                        ^
                                        |
                          HyperVGroupService (App.Services)
                                        |
                                        v
                            IPowerShellExecutor (Core.Interfaces)
                                        ^
                                        |
                            PowerShellExecutor (App.Services)
                                        |
                                        v
                       externer powershell.exe-Prozess + HyperVGroupManager-Modul
```

* **Core** kennt weder WPF noch PowerShell. Es enthält nur Models, Interfaces, `PowerShellResult<T>`,
  Exceptions sowie reine, testbare Logik (`VmGroupChangeQueue`, `GroupNameValidator`, `VmGroupRules`,
  `VirtualMachineFilter`, `ConfigurationExportBuilder`).
* **App** implementiert die Interfaces aus Core (`HyperVGroupService`, `PowerShellExecutor`,
  `LogService`) und enthält die WPF-spezifischen Teile (Views, ViewModels, Converters).
  Das **PowerShell-Backend könnte vollständig ausgetauscht werden**, ohne die ViewModels
  anzufassen - sie kennen nur `IHyperVGroupService`.
* **PowerShell** ist ein eigenständiges Modul ohne Abhängigkeit zu C#. Es kommuniziert
  ausschließlich über kompaktes JSON auf stdout (siehe `docs/powershell-json-contract.md`).

## Kommunikation C# <-> PowerShell

1. `PowerShellExecutor.ExecuteAsync<T>` schreibt die Parameter als JSON in eine temporäre Datei.
2. Es wird `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File Invoke-HVGMCommand.ps1
   -ModuleManifestPath ... -CommandName ... -ParametersFilePath ...` gestartet (keine String-Verkettung
   von PowerShell-Code, kein `Invoke-Expression`).
3. `Invoke-HVGMCommand.ps1` importiert das Modul, liest die Parameterdatei, ruft genau die
   angeforderte (aus einer Allow-List stammende) Funktion auf und gibt das Ergebnis als
   `ConvertTo-Json -Depth 10 -Compress` aus.
4. C# deserialisiert die Standardausgabe direkt in `PowerShellResult<T>` (PascalCase, 1:1 zu den
   PowerShell-Eigenschaften). Es werden niemals rohe PowerShell-/CIM-Objekte verarbeitet.
5. Ein Timeout (`appsettings.json: PowerShell.TimeoutSeconds`, Standard 120s) beendet den Prozess
   bei Überschreitung und löst eine `PowerShellExecutionException` aus.

### Multiline-Stdout-Schutz in PowerShellExecutor

`OutputDataReceived` liest stdout zeilenweise. PowerShell 5.1 kann trotz `-Compress` mehrere Zeilen
ausgeben, wenn Fehlertext unescapte Zeilenumbrüche enthält. `PowerShellExecutor` extrahiert daher
die letzte nicht-leere JSON-Zeile (erkennbar an `{` oder `[`):

```csharp
var rawLines = stdOutBuilder.ToString()
    .Split('\n')
    .Select(l => l.Trim('\r', ' '))
    .Where(l => l.Length > 0)
    .ToArray();

var stdOut = rawLines.Length > 1
    ? (rawLines.LastOrDefault(l => l.StartsWith("{") || l.StartsWith("[")) ?? string.Join(string.Empty, rawLines))
    : (rawLines.FirstOrDefault() ?? string.Empty);
```

Bei einem JSON-Fehler wird die vollständige Rohausgabe ins Log geschrieben:
```csharp
logService.LogError($"Ungültiges JSON von PowerShell-Befehl '{commandName}'. Raw output: {rawResult.RawOutput}", ex);
```

## Change-Queue (geplante Änderungen)

`VmGroupChangeQueue` (Core) verwaltet `VmGroupMembershipChange`-Einträge:

* identische Änderungen werden nicht doppelt aufgenommen,
* Add/Remove derselben VM-Gruppe-Kombination heben sich gegenseitig auf,
* CreateGroup/DeleteGroup derselben (noch nicht angewendeten) Gruppe heben sich auf,
* `GetInExecutionOrder()` liefert die Reihenfolge: CreateGroup -> RenameGroup -> AddMembership ->
  RemoveMembership -> DeleteGroup.

`MainViewModel` ist die einzige Stelle, die die Queue befüllt; angewendet wird sie ausschließlich
über `IHyperVGroupService.ApplyChangesAsync`, welches intern `Invoke-HVGMChangeSet` aufruft.

`ApplyChangesAsync` liefert ein `ApplyChangesResult` mit dem Gesamtstatus sowie einem
`ChangeApplicationResult` pro geplanter Änderung (Erfolg/Fehler/Warnungen, korreliert über
ChangeType+VmId+GroupId). Bei einem Teilfehler entfernt `MainViewModel` nur die bereits
erfolgreich angewendeten Änderungen aus der Queue; fehlgeschlagene bzw. wegen eines vorherigen
Fehlers nicht ausgeführte Änderungen bleiben für einen erneuten Anwenden-Versuch erhalten. Der
Ergebnis-Dialog zeigt das Resultat jeder einzelnen Änderung an.

## Bekannte MVP-Einschränkungen

* **Neu erstellte, noch nicht angewendete Gruppen** erhalten lokal eine temporäre Guid (der Server
  vergibt beim tatsächlichen `New-VMGroup` eine eigene Id). Deshalb können einer solchen Gruppe
  erst nach dem Anwenden Mitglieder hinzugefügt werden - die UI verhindert das mit einer
  verständlichen Meldung. Umbenennen/Löschen einer noch nicht angewendeten Gruppe wird lokal in
  die geplante `CreateGroup`-Änderung übernommen bzw. hebt sie über die Queue-Regeln auf.
* Die Prüfung "Gruppe leer?" vor dem Löschen verwendet den zuletzt geladenen `MemberCount`, nicht
  bereits geplante (aber noch nicht angewendete) Remove-Änderungen.

## PowerShell-5.1-Fallstricke

### Nicht-ASCII-Zeichen in String-Literalen

PS 5.1 liest `.ps1`-Dateien ohne UTF-8 BOM als Windows-1252. Nicht-ASCII-Zeichen wie der
En-Dash `–` (UTF-8: `E2 80 93`) werden fehlinterpretiert: Byte `0x93` entspricht in Windows-1252
dem linken Anführungszeichen `"`, was einen String vorzeitig schließt und einen Syntaxfehler
verursacht.

**Regel:** In PowerShell-5.1-Skripten innerhalb von String-Literalen ausschließlich ASCII-Zeichen
verwenden. In Kommentaren sind Nicht-ASCII-Zeichen unproblematisch.

### ConvertTo-Json escapt Zeilenumbrüche in Fehlermeldungen nicht

PS 5.1 escapt `\r\n` in Exception-Meldungen beim Aufruf von `ConvertTo-Json -Compress` nicht
zuverlässig. Enthält eine Exception-Meldung einen Zeilenumbruch, erscheint er als echter Umbruch
in der Ausgabe, was das JSON-Parsing in C# bricht.

**Fix:** Fehlermeldungen vor der JSON-Serialisierung bereinigen:
```powershell
$safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
```
Dieses Muster ist in allen `catch`-Blöcken des Moduls angewendet.

### PS 5.1 kennt ConvertFrom-Json -AsHashtable nicht

`Invoke-HVGMCommand.ps1` übernimmt die PSCustomObject-Eigenschaften manuell in eine Hashtable,
damit sie als benannte Parameter gesplattet werden können (`@parametersTable`).

### PS 5.1 Generic List + New-HVGMResult Binder-Fehler

`Invoke-HVGMChangeSet.ps1` sammelt Ergebnisse in einem einfachen PowerShell-Array (`@()`) statt
`System.Collections.Generic.List[object]`. Letzteres führt unter PS 5.1 beim Aufruf von
`New-HVGMResult -Data @($itemResults)` zu `System.ArgumentException: Argument types do not match`
(bekannter Binder-Fehler in `PSEnumerableBinder.MaybeDebase`). Reine Unit-Tests mit Fakes hätten
diesen Fehler nicht aufgedeckt, da sie das PowerShell-Modul nie wirklich ausführen.

### UTF-8-Kodierung erzwingen

Der Bootstrap-Einstiegspunkt `Invoke-HVGMCommand.ps1` setzt explizit:
```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding            = [System.Text.Encoding]::UTF8
```
Damit kommen deutsche Sonderzeichen in JSON-Ausgaben korrekt beim C#-Prozess an, der mit
`StandardOutputEncoding = Encoding.UTF8` liest.
