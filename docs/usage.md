# Benutzung

## Voraussetzungen

* Windows mit installiertem Hyper-V-PowerShell-Modul (RSAT-Hyper-V-Tools für Remote-Verwaltung,
  oder lokale Hyper-V-Rolle).
* Bei Cluster-Betrieb zusätzlich das `FailoverClusters`-PowerShell-Modul.
* Administrative Rechte werden empfohlen (manche Hyper-V-Vorgänge schlagen sonst fehl).
* Windows PowerShell 5.1 muss als `powershell.exe` verfügbar sein.

## Build & Start

```powershell
dotnet build HyperVGroupManager.sln
dotnet test HyperVGroupManager.sln
dotnet run --project src\HyperVGroupManager.App
```

## Portable Executable bauen (Self-Contained, Single-File)

Erzeugt eine eigenständige `.exe`, die ohne installiertes .NET auf dem Zielrechner läuft
(Größe ca. 60 MB, da die .NET-10-Runtime eingebettet ist). `appsettings.json` und der
`PowerShell`-Ordner bleiben als separate Dateien neben der `.exe` - einfach den gesamten
Publish-Ordner kopieren und auf einem anderen Windows-x64-Rechner ausführen.

```powershell
dotnet publish src\HyperVGroupManager.App\HyperVGroupManager.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish\win-x64
```

Ergebnis in `publish\win-x64\`:

* `HyperVGroupManager.App.exe` - einzige benötigte Programmdatei (Runtime eingebettet)
* `appsettings.json` - Konfiguration (optional, sonst Standardwerte)
* `PowerShell\` - das Backend-Modul (zwingend erforderlich, muss neben der `.exe` bleiben)
* `*.pdb` - Debug-Symbole, für die Verteilung nicht erforderlich und können gelöscht werden

Für andere Architekturen `-r win-x64` durch z. B. `-r win-arm64` ersetzen.

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
