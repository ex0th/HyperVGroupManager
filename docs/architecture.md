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

## Hinweis: PowerShell-5.1-Fallstrick in Invoke-HVGMChangeSet

`Invoke-HVGMChangeSet.ps1` sammelt die Pro-Änderung-Ergebnisse bewusst in einem einfachen
PowerShell-Array (`$itemResults = @(); $itemResults += ...`) statt in einem
`System.Collections.Generic.List[object]`. Letzteres führte unter Windows PowerShell 5.1 beim
Aufruf von `New-HVGMResult -Data @($itemResults)` zu `System.ArgumentException: Argument types
do not match` (ein bekannter Binder-Fehler in `PSEnumerableBinder.MaybeDebase`). Der Fehler wurde
durch manuellen End-to-End-Test des Bootstrap-Pfads gefunden und behoben - reine Unit-Tests mit
Fakes hätten ihn nicht aufgedeckt, da sie das PowerShell-Modul nie wirklich ausführen.
