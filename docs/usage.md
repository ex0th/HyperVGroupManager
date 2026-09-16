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

## Vollständiges Release erstellen

Ein einzelner Befehl aktualisiert die Version, testet die Anwendung, baut den MSI-Installer,
committet alle nicht ignorierten Änderungen, pusht den aktuellen Branch und den Release-Tag und
startet dadurch die Veröffentlichung auf GitHub:

```powershell
# Automatische Version
.\scripts\New-Release.ps1

# Explizite Version und optionale Commit-Nachricht
.\scripts\New-Release.ps1 -Version 1.0.0 -CommitMessage "release: version 1.0.0"
```

Existiert für die aktuelle Projektversion noch kein Tag, wird diese Version veröffentlicht.
Andernfalls wird automatisch die Patch-Version erhöht. Bei einer Vorabversion wird nach ihrem Tag
zunächst die stabile Variante derselben Versionsnummer gewählt. Das Skript fragt vor den Änderungen
einmal nach einer Bestätigung; für einen vollständig unbeaufsichtigten Aufruf kann `-Confirm:$false`
verwendet werden.

Vor dem Commit bricht das Skript bei Merge-Konflikten, einem abweichenden Remote-Branch,
vorhandenen Release-Tags sowie verdächtigen unversionierten Schlüssel- oder Umgebungsdateien ab.
Der GitHub-Workflow veröffentlicht anschließend portable ZIP-Datei, MSI und SHA-256-Prüfsummen.
Das Skript wartet standardmäßig bis zu 20 Minuten und meldet erst dann Erfolg, wenn alle drei Dateien
im GitHub Release vorhanden sind. Mit `-NoWait` kehrt es direkt nach dem Tag-Push zurück; über
`-ReleaseTimeoutMinutes 30` kann das Zeitlimit angepasst werden. Für private Repositories muss zur
API-Prüfung `GH_TOKEN` oder `GITHUB_TOKEN` gesetzt sein.

## MSI-Installer bauen und verteilen

Die portable ZIP-Variante bleibt unverändert verfügbar. Zusätzlich kann ein systemweiter
MSI-Installer erzeugt werden:

```powershell
.\scripts\Build-Installer.ps1
```

Das Ergebnis liegt unter `artifacts\release\HyperVGroupManager-<Version>-win-x64.msi`. Der Installer
verwendet standardmäßig `%ProgramFiles%\HyperVGroupManager`, erstellt einen Startmenüeintrag und
bietet in der Funktionsauswahl eine Desktop-Verknüpfung an. Vorhandene neuere
Versionen können nicht versehentlich durch ältere ersetzt werden.

Unbeaufsichtigte Installation und Deinstallation:

```powershell
msiexec.exe /i HyperVGroupManager-<Version>-win-x64.msi /qn /norestart
msiexec.exe /x HyperVGroupManager-<Version>-win-x64.msi /qn /norestart
```

Mit `ADDLOCAL=ALL` wird bei einer unbeaufsichtigten Installation zusätzlich die optionale
Desktop-Verknüpfung installiert. Benutzereinstellungen und Logs unter `%LocalAppData%` gehören nicht
zum MSI und bleiben bei Update oder Deinstallation erhalten.

Das Installer-Projekt ist bewusst auf WiX 5.0.2 festgesetzt. WiX 6 und neuer unterliegen den
Open-Source-Maintenance-Fee-Bedingungen; ein Upgrade der Toolchain sollte deshalb erst nach einer
separaten Lizenzprüfung erfolgen. WiX 5 wird nicht mehr upstream unterstützt; diese Abwägung sollte
vor einem produktiven Rollout erneut geprüft werden.

## Release-Dateien signieren

Der GitHub-Release-Workflow veröffentlicht keine unsignierten Builds. Er signiert zuerst die
portable EXE, baut das MSI mit dieser signierten EXE und signiert danach auch das MSI. Beide
Signaturen werden vor dem Verpacken mit SignTool geprüft.

In den GitHub-Repository-Secrets müssen hinterlegt sein:

* `CODE_SIGNING_CERTIFICATE_BASE64`: Base64-kodierte PFX-Datei mit Authenticode-Zertifikat und
  privatem Schlüssel
* `CODE_SIGNING_CERTIFICATE_PASSWORD`: Kennwort der PFX-Datei, sofern vorhanden

Die temporäre PFX-Datei existiert nur auf dem kurzlebigen GitHub-Runner und wird durch einen
`always()`-Schritt entfernt. Ohne das Zertifikat bricht der Workflow ab. Lokal können Dateien über
`scripts\Sign-Release.ps1` entweder mit einer PFX-Datei oder dem Thumbprint eines Zertifikats im
Windows-Zertifikatsspeicher signiert werden.

Ein lokaler Build kann die portable EXE vor dem Verpacken und anschließend das MSI signieren:

```powershell
$env:CODE_SIGNING_CERTIFICATE_PASSWORD = '<PFX-Kennwort>'
.\scripts\Build-Installer.ps1 -CertificatePath C:\secure\codesigning.pfx
```

## Supportpaket und Log-Aufbewahrung

Im Hilfefenster erzeugt **Supportpaket erstellen** ein ZIP-Archiv mit Laufzeitinformationen,
Signaturstatus und aktuellen Anwendungs- und Crash-Logs. Benutzername, Rechnername, Benutzerpfade,
verbundenes Ziel, E-Mail-Adressen und IP-Adressen werden ersetzt. SMTP-Konfiguration,
Anwendungseinstellungen und Zugangsdaten werden nicht aufgenommen. Das Archiv sollte trotzdem vor
der Weitergabe geprüft werden.

Das Paket enthält höchstens 14 Logdateien, maximal 5 MB je Datei und maximal 20 MB Logdaten
insgesamt. Die normale Log-Aufbewahrung und Größenrotation sind in `appsettings.json` konfigurierbar:

```json
"Application": {
  "LogRetentionDays": 30,
  "MaximumLogFileSizeMegabytes": 10
}
```

## Bedienung

1. **Ziel eingeben** (Hostname oder Clustername) und **Verbinden** klicken. Die Statusanzeige
   zeigt Nicht verbunden / Verbinde / Verbunden / Fehler. Warnungen (z. B. fehlende
   Administratorrechte) erscheinen in der Statusleiste unten rechts.
2. **Aktualisieren** lädt VMs und Gruppen erneut vom Zielsystem.
3. **Gruppen verwalten** (linke Karte): Neue Gruppe, Umbenennen, Löschen. Eine nicht leere Gruppe
   kann erst gelöscht werden, nachdem ihre Mitgliedschaften zur Entfernung geplant wurden.
4. **VMs filtern/suchen** (rechte Spalte): Freitextsuche sowie Filter (Alle VMs, VMs ohne Gruppe,
   VMs der ausgewählten Gruppe, laufende/ausgeschaltete VMs). Mehrfachauswahl im DataGrid ist
   möglich (Strg/Shift-Klick).
5. **Mitgliedschaften planen**: ausgewählte VMs einer Gruppe hinzufügen oder aus ihr entfernen.
   Die Tabellen und Kennzahlen zeigen sofort den erwarteten Zustand, auf dem Server wird noch
   nichts geändert. Auch eine neu geplante Gruppe kann sofort Mitglieder erhalten.
6. **Prüfen und anwenden** validiert alle Änderungen lokal und führt sie anschließend in sinnvoller Reihenfolge aus
   (Gruppen erstellen -> umbenennen -> Mitglieder hinzufügen -> entfernen -> Gruppen löschen) und
   zeigt anschließend ein Ergebnis-Dialogfenster. **Änderungen verwerfen** leert die Liste ohne
   etwas anzuwenden.
7. **Konfiguration exportieren** speichert die aktuell geladenen Gruppen (inkl. Mitglieder) als
   JSON-Datei über einen Speichern-Dialog. Bei einer offenen Queue wird der erwartete Zustand
   exportiert.

Über **Hilfe** oder die Taste `F1` öffnet sich eine integrierte Dokumentationsseite mit
Schnellstart, Sicherheitskonzept, E-Mail-Berichten und Hinweisen zur Fehlersuche. Die Sprache kann
oben rechts jederzeit zwischen **Deutsch** und **English** umgeschaltet werden. Die Auswahl wird in
`%LocalAppData%\HyperVGroupManager\ui-language.txt` gespeichert; der portable Programmordner wird
dabei nicht verändert.

Daneben kann die Darstellung zwischen **System**, **Dunkel** und **Hell** umgeschaltet werden.
`System` folgt der Windows-Einstellung. Die Auswahl wird sofort auf alle Fenster angewendet und in
`%LocalAppData%\HyperVGroupManager\ui-theme.txt` gespeichert.

Während die Anwendung läuft, ist das neue Programmsymbol auch im Windows-Infobereich verfügbar.
Ein Doppelklick oder **Öffnen** im Kontextmenü stellt das Hauptfenster wieder her. **Beenden**
schließt die Anwendung; bei ausstehenden Änderungen bleibt die Sicherheitsabfrage aktiv.

Ein Wechsel zu einem anderen Ziel ist mit offenen Änderungen blockiert. Nach Timeout, Abbruch oder
einer nicht eindeutig zuordenbaren Backend-Antwort sperrt die Anwendung weitere Schreibvorgänge,
bis **Aktualisieren** den tatsächlichen Zustand erfolgreich neu geladen hat.

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
    "ConfirmBeforeApply": true
  }
}
```

Fehlt die Datei, verwendet die Anwendung diese Standardwerte und startet trotzdem.

SMTP-Kennwörter werden in `%LocalAppData%\HyperVGroupManager` benutzergebunden per Windows DPAPI
gespeichert. Eine geplante E-Mail-Aufgabe muss daher unter demselben Windows-Konto laufen.

## Manueller PowerShell-Test (ohne UI)

```powershell
cd src\HyperVGroupManager.PowerShell
.\Manual-SmokeTest.ps1 -TargetName "localhost"
```
