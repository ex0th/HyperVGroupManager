# PowerShell-JSON-Vertrag

Jede öffentliche Funktion des Moduls `HyperVGroupManager` gibt **genau ein** PSCustomObject
zurück, das per `ConvertTo-Json -Depth 10 -Compress` auf Standard Output ausgegeben wird:

```json
{
  "Success": true,
  "Data": <funktionsspezifisch>,
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
      "Die Anwendung wird nicht mit administrativen Rechten ausgeführt. Einige Hyper-V-Vorgänge könnten fehlschlagen."
    ]
  },
  "Errors": [],
  "Warnings": [
    "Die Anwendung wird nicht mit administrativen Rechten ausgeführt. Einige Hyper-V-Vorgänge könnten fehlschlagen."
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
    "Die Gruppe 'VEEAM_Backup_Daily' enthält noch 3 Mitglied(er) und kann nicht gelöscht werden. Entfernen Sie zuerst alle Mitgliedschaften."
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
    { "ChangeType": "AddMembership", "Description": "DC01 zu VEEAM_Test hinzufügen", "Success": false, "Error": "VM mit ID '...' wurde nicht gefunden." },
    { "ChangeType": "RenameGroup", "Description": "Gruppe VEEAM_Test umbenennen", "Success": false, "Error": "Nicht ausgeführt, da eine vorherige Änderung fehlgeschlagen ist." }
  ],
  "Errors": ["VM mit ID '...' wurde nicht gefunden.", "Nicht ausgeführt, da eine vorherige Änderung fehlgeschlagen ist."],
  "Warnings": []
}
```

## Manueller Test

```powershell
cd src\HyperVGroupManager.PowerShell
.\Manual-SmokeTest.ps1 -TargetName "localhost"
```
