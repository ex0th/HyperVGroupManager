# PowerShell-JSON-Vertrag

Jede öffentliche Funktion des Moduls `HyperVGroupManager` gibt **genau ein** PSCustomObject
zurück, das per `ConvertTo-Json -Depth 10 -Compress` auf Standard Output ausgegeben wird:

```json
{
  "Success": true,
  "Data": "<funktionsspezifisch>",
  "Errors": [],
  "Warnings": []
}
```

* `Success` ist `false`, sobald ein nicht behebbarer Fehler aufgetreten ist (siehe `Errors`).
* `Warnings` enthält fachliche Hinweise, die **keinen** Abbruch bedeuten
  (z. B. doppelte Mitgliedschaft, Löschen einer nicht vorhandenen Mitgliedschaft,
  fehlende Administratorrechte).
* C# deserialisiert ausschließlich dieses Vertragsformat (`PowerShellResult<T>`),
  niemals rohe PowerShell-/CIM-Objekte.

## Fehlermeldungs-Sanitierung

Alle `catch`-Blöcke im Modul bereinigen Exception-Meldungen vor der JSON-Serialisierung, da
PowerShell 5.1 Zeilenumbrüche in `ConvertTo-Json -Compress` nicht zuverlässig escapt:

```powershell
$safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
New-HVGMResult -Success $false -Errors @($safeMessage)
```

## UTF-8-Kodierung

`Invoke-HVGMCommand.ps1` erzwingt UTF-8 für stdout, damit Umlaute und Sonderzeichen korrekt
beim C#-Prozess ankommen:

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding            = [System.Text.Encoding]::UTF8
```

## Öffentliche Funktionen

| Funktion | Data-Typ | Beschreibung |
|---|---|---|
| `Test-HVGMEnvironment` | `EnvironmentInfo` | Erreichbarkeit, Cluster-Erkennung, Module, Admin-Status |
| `Get-HVGMVirtualMachine` | `VirtualMachine[]` | Alle VMs mit Gruppeninfo |
| `Get-HVGMGroup` | `VmGroup[]` | Alle Gruppen mit Mitgliedern |
| `New-HVGMGroup` | `VmGroup` | Neue Gruppe erstellen |
| `Rename-HVGMGroup` | `VmGroup` | Gruppe umbenennen |
| `Remove-HVGMGroup` | - | Gruppe löschen (nur wenn leer) |
| `Add-HVGMGroupMember` | - | VM zu Gruppe hinzufügen |
| `Remove-HVGMGroupMember` | - | VM aus Gruppe entfernen |
| `Invoke-HVGMChangeSet` | `ChangeApplicationResult[]` | Änderungsliste atomar anwenden |
| `Export-HVGMConfiguration` | `string` (Pfad) | Konfiguration als JSON exportieren |
| `Get-HVGMClusterConfig` | `ClusterConfig` | Cluster-Knoteninfo |
| `Set-HVGMConfigStoreRootPath` | - | Konfigurationspfad setzen |
| `Send-HVGMUntaggedVMsReport` | `string` (Statusmeldung) | E-Mail-Bericht: VMs ohne Gruppe |
| `Register-HVGMEmailReportTask` | `string` (Statusmeldung) | Geplante Aufgabe registrieren |
| `Unregister-HVGMEmailReportTask` | `string` (Statusmeldung) | Geplante Aufgabe entfernen |
| `Get-HVGMEmailReportTaskStatus` | `TaskStatus` | Status der geplanten Aufgabe |

## Beispiel: Test-HVGMEnvironment (Einzelhost, ohne Admin-Rechte)

```json
{
  "Success": true,
  "Data": {
    "TargetName": "localhost",
    "TargetType": "Host",
    "IsCluster": false,
    "Nodes": ["localhost"],
    "PowerShellVersion": "5.1.26100.8457",
    "HyperVModuleAvailable": true,
    "FailoverClustersModuleAvailable": false,
    "IsAdministrator": false,
    "Warnings": [
      "Die Anwendung wird nicht mit administrativen Rechten ausgefuehrt. Einige Hyper-V-Vorgaenge koennten fehlschlagen."
    ]
  },
  "Errors": [],
  "Warnings": [
    "Die Anwendung wird nicht mit administrativen Rechten ausgefuehrt. Einige Hyper-V-Vorgaenge koennten fehlschlagen."
  ]
}
```

## Beispiel: Get-HVGMVirtualMachine (Cluster)

```json
{
  "Success": true,
  "Data": [
    {
      "Id": "5b1f2e3a-1111-4a2b-9c3d-abcdef123456",
      "Name": "DC01",
      "ComputerName": "HVNODE02",
      "OwnerNode": "HVNODE02",
      "State": "Running",
      "IsClustered": true,
      "GroupNames": ["VEEAM_Backup_Daily", "VEEAM_AppAware"]
    }
  ],
  "Errors": [],
  "Warnings": []
}
```

## Beispiel: Add-HVGMGroupMember (bereits Mitglied -> Warnung statt Fehler)

```json
{
  "Success": true,
  "Data": { "VmId": "5b1f2e3a-1111-4a2b-9c3d-abcdef123456", "GroupId": "..." },
  "Errors": [],
  "Warnings": ["VM 'DC01' ist bereits Mitglied der Gruppe 'VEEAM_Backup_Daily'."]
}
```

## Beispiel: Remove-HVGMGroup (nicht leere Gruppe -> Fehler)

```json
{
  "Success": false,
  "Data": null,
  "Errors": [
    "Die Gruppe 'VEEAM_Backup_Daily' enthaelt noch 3 Mitglied(er) und kann nicht geloescht werden. Entfernen Sie zuerst alle Mitgliedschaften."
  ],
  "Warnings": []
}
```

## Beispiel: Invoke-HVGMChangeSet (zweite Änderung schlägt fehl)

```json
{
  "Success": false,
  "Data": [
    { "ChangeType": "CreateGroup", "Description": "Gruppe VEEAM_Test erstellen", "Success": true, "Error": null, "Warnings": [] },
    { "ChangeType": "AddMembership", "Description": "DC01 zu VEEAM_Test hinzufuegen", "Success": false, "Error": "VM mit ID '...' wurde nicht gefunden." },
    { "ChangeType": "RenameGroup", "Description": "Gruppe VEEAM_Test umbenennen", "Success": false, "Error": "Nicht ausgefuehrt, da eine vorherige Aenderung fehlgeschlagen ist." }
  ],
  "Errors": ["VM mit ID '...' wurde nicht gefunden.", "Nicht ausgefuehrt, da eine vorherige Aenderung fehlgeschlagen ist."],
  "Warnings": []
}
```

## Manueller Test

```powershell
cd src\HyperVGroupManager.PowerShell
.\Manual-SmokeTest.ps1 -TargetName "localhost"
```
