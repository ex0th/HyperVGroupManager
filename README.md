# Hyper-V VM Group Manager

Windows-Desktop-Anwendung (WPF, .NET 10) zur Verwaltung **nativer Hyper-V-VM-Gruppen**
(`VMCollectionType`) auf einem einzelnen Hyper-V-Host oder in einem Failover-Cluster.

Das Bedienkonzept ist an vSphere-Tags angelehnt: Eine VM kann Mitglied mehrerer Gruppen sein,
Mitgliedschaften werden gezielt hinzugefügt/entfernt, ohne andere Gruppenzugehörigkeiten zu
verändern. Die Gruppen sind dafür gedacht, später in Veeam Backup & Replication zur VM-Auswahl
verwendet zu werden - diese Anwendung verändert keine Veeam-Konfiguration.

## Schnellstart

```powershell
dotnet build HyperVGroupManager.sln
dotnet test HyperVGroupManager.sln
dotnet run --project src\HyperVGroupManager.App
```

Details: [docs/usage.md](docs/usage.md) · Architektur: [docs/architecture.md](docs/architecture.md) ·
PowerShell-JSON-Vertrag: [docs/powershell-json-contract.md](docs/powershell-json-contract.md)

## Projektstruktur

* `src/HyperVGroupManager.App` - WPF-Anwendung (MVVM, CommunityToolkit.Mvvm)
* `src/HyperVGroupManager.Core` - Models, Interfaces, fachliche Logik (kein WPF, kein PowerShell)
* `src/HyperVGroupManager.PowerShell` - PowerShell-Backend-Modul (Windows PowerShell 5.1)
* `tests/HyperVGroupManager.Tests` - xUnit-Tests (PowerShell wird in Tests nie wirklich ausgeführt)

## Sicherheitsgrundsätze

* Die Anwendung **verändert keine Veeam-Jobs** und greift nicht auf die Veeam-API zu.
* Sie verwaltet ausschließlich Hyper-V-VM-Gruppen (`VMCollectionType`).
* Kein `Invoke-Expression`, keine String-Verkettung von PowerShell-Code.
* Parameter werden ausschließlich als JSON-Datei übergeben (nie als Kommandozeilenargument).
* Passwörter und geheime Daten werden nicht geloggt.

## MVP-Umfang

Enthalten: native VM-Gruppen (VMCollectionType) auf Einzelhost und Cluster, Mehrfachmitgliedschaft,
geplante Änderungen mit Vorschau, JSON-Export, geplanter E-Mail-Bericht über VMs ohne Gruppe.

Nicht enthalten (außerhalb des Scopes): Veeam-API-Anbindung, ManagementCollectionType,
verschachtelte Gruppen, SCVMM, AD-Anmeldung, Installer, Auto-Update.
