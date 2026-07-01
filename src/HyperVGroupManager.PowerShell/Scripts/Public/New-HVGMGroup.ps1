function New-HVGMGroup {
    <#
        Erstellt eine neue native Hyper-V-VM-Gruppe (VMCollectionType). Verhindert
        doppelte Gruppennamen (Groß-/Kleinschreibung wird dabei ignoriert).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$GroupName
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $hostName = Get-HVGMGroupHostName -Target $target

        $existing = Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $GroupName }

        if ($existing) {
            throw "Eine Gruppe mit dem Namen '$GroupName' existiert auf '$TargetName' bereits."
        }

        $null = New-VMGroup -Name $GroupName -GroupType VMCollectionType -ComputerName $hostName -ErrorAction Stop
        # New-VMGroup may return the object with a null Id on some Windows Server versions before
        # the WMI store is flushed. Look up by name to get the committed, non-null Id.
        $newGroup = Get-VMGroup -ComputerName $hostName -Name $GroupName -ErrorAction SilentlyContinue
        if ($null -eq $newGroup) {
            throw "Gruppe '$GroupName' wurde erstellt, konnte aber per Get-VMGroup nicht gefunden werden."
        }
        # Hyper-V stores the GUID in InstanceId (not Id). Fall back to deterministic GUID when empty.
        $instanceId = $newGroup.InstanceId
        $groupId = if ($null -ne $instanceId -and $instanceId -ne [guid]::Empty) { $instanceId.ToString() } else { (Get-HVGMDeterministicGroupGuid $GroupName).ToString() }

        New-HVGMResult -Success $true -Data ([pscustomobject]@{
            Id   = $groupId
            Name = $newGroup.Name
        })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
