# Benutzung

## Voraussetzungen

* Windows mit installiertem Hyper-V-PowerShell-Modul (RSAT-Hyper-V-Tools für Remote-Verwaltung,
  oder lokale Hyper-V-Rolle).
* Bei Cluster-Betrieb zusätzlich das `FailoverClusters`-PowerShell-Modul.
* Administrative Rechte werden empfohlen (manche Hyper-V-Vorgänge schlagen sonst fehl).
* Windows PowerShell 5.1 muss als `powershell.exe` verfügbar sein.

## Build & Start (Entwicklung)

```powershell
dotnet build HyperVGroupManager.sln
dotnet test HyperVGroupManager.sln
dotnet run --project src\HyperVGroupManager.App
```

## Portable Executable bauen (Self-Contained, Single-File)

Erzeugt eine eigenständige `.exe`, die ohne installiertes .NET auf dem Zielrechner läuft
(ca. 60 MB, da die .NET-10-Runtime eingebettet ist). Die Publish-Einstellungen sind dauerhaft
im `.csproj` hinterlegt - der Befehl braucht keine zusätzlichen `-p:` Flags:

```powershell
dotnet publish src\HyperVGroupManager.App\HyperVGroupManager.App.csproj -c Release -o publish\win-x64
```

### Ergebnis in `publish\win-x64\`

| Datei/Ordner | Erforderlich | Beschreibung |
|---|---|---|
| `HyperVGroupManager.App.exe` | Ja | Anwendung mit eingebetteter .NET-Runtime |
| `PowerShell\` | Ja | PowerShell-Backend-Modul (muss neben der `.exe` liegen) |
| `appsettings.json` | Nein | Konfiguration; fehlt die Datei, gelten Standardwerte |
| `wpfgfx_cor3.dll` u.a. | Ja | Native WPF-Bibliotheken (5 DLLs, von .NET nicht eingebettet) |
| `*.pdb` | Nein | Debug-Symbole, können für die Verteilung gelöscht werden |

Die fünf nativen WPF-DLLs (`wpfgfx_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`,
`vcruntime140_cor3.dll`, `D3DCompiler_47_cor3.dll`) müssen neben der `.exe` bleiben.
Sie lassen sich mit `IncludeNativeLibrariesForSelfExtract=true` in die `.exe` einbetten, was
die Startzeit jedoch erhöht, da sie bei jedem Start ins Temp-Verzeichnis extrahiert werden.

Für andere Architekturen `-r win-x64` durch z. B. `-r win-arm64` ersetzen und in der `.csproj`
`<RuntimeIdentifier>` anpassen.

## Bedienung

1. **Ziel eingeben** (Hostname oder Clustername) und **Verbinden** klicken. Die Statusanzeige
   zeigt Nicht verbunden / Verbinde / Verbunden / Fehler. Warnungen (z. B. fehlende
   Administratorrechte) erscheinen in der Statusleiste unten rechts.
2. **Aktualisieren** lädt VMs und Gruppen erneut vom Zielsystem.
3. **Gruppen verwalten** (linke Spalte): Neue Gruppe, Umbenennen, Löschen. Eine nicht leere Gruppe
   kann nicht gelöscht werden - der Dialog zeigt die Mitgliederzahl und einen Hinweis an.
4. **VMs filtern/suchen** (rechte Spalte): Freitextsuche sowie Filter (Alle VMs, VMs ohne Gruppe,
   VMs der ausgewählten Gruppe, laufende/ausgeschaltete VMs). Mehrfachauswahl im DataGrid ist
   möglich (Strg/Shift-Klick).
5. **Mitgliedschaften planen** (mittlerer Bereich): ausgewählte VMs einer Gruppe hinzufügen oder
   aus ihr entfernen. Dies ändert noch nichts auf dem Server, sondern erzeugt nur einen Eintrag in
   den **geplanten Änderungen** (unterer Bereich).
6. **Änderungen anwenden** führt alle geplanten Änderungen in sinnvoller Reihenfolge aus
   (Gruppen erstellen -> umbenennen -> Mitglieder hinzufügen -> entfernen -> Gruppen löschen) und
   zeigt anschließend ein Ergebnis-Dialogfenster. **Änderungen verwerfen** leert die Liste ohne
   etwas anzuwenden.
7. **Konfiguration exportieren** speichert die aktuell geladenen Gruppen (inkl. Mitglieder) als
   JSON-Datei über einen Speichern-Dialog.

## Logging

Logs liegen unter `%LocalAppData%\HyperVGroupManager\Logs\HyperVGroupManager-yyyy-MM-dd.log`.

Bei einem JSON-Fehler aus dem PowerShell-Prozess wird die vollständige Rohausgabe ins Log
geschrieben (`Raw output: ...`), um die Fehlerursache zu diagnostizieren.

## Konfiguration

`appsettings.json` neben der `.exe`:

```json
{
  "PowerShell": {
    "ExecutablePath": "powershell.exe",
    "ExecutionPolicy": "Bypass",
    "TimeoutSeconds": 120
  },
  "Application": {
    "DefaultGroupPrefix": "VEEAM_",
    "ConfirmBeforeApply": true,
    "PreventDeletingNonEmptyGroups": true
  }
}
```

Fehlt die Datei, verwendet die Anwendung diese Standardwerte und startet trotzdem.

## Manueller PowerShell-Test (ohne UI)

```powershell
cd src\HyperVGroupManager.PowerShell
.\Manual-SmokeTest.ps1 -TargetName "localhost"
```
