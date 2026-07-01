function Get-HVGMGroupById {
    <#
        Sucht eine VM-Gruppe mit dreistufigem Fallback:
        1. Get-VMGroup -Id (funktioniert wenn Hyper-V GUIDs vergibt)
        2. Get-VMGroup -Name (wenn GroupName übergeben wurde)
        3. Alle Gruppen scannen und deterministischen GUID vergleichen
           (für Rename/Delete wenn Hyper-V keine GUIDs vergibt)
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target,

        [Parameter(Mandatory)]
        [guid]$GroupId,

        [string]$GroupName = $null
    )

    $hostName = Get-HVGMGroupHostName -Target $Target

    $group = Get-VMGroup -ComputerName $hostName -Id $GroupId -ErrorAction SilentlyContinue
    if ($group) { return $group }

    if (-not [string]::IsNullOrEmpty($GroupName)) {
        $group = Get-VMGroup -ComputerName $hostName -Name $GroupName -ErrorAction SilentlyContinue
        if ($group) { return $group }
    }

    # Scan all groups and reverse the deterministic GUID to find the matching group.
    $allGroups = Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
        Where-Object { $_.GroupType -eq 'VMCollectionType' }
    foreach ($g in $allGroups) {
        if ((Get-HVGMDeterministicGroupGuid $g.Name) -eq $GroupId) {
            return $g
        }
    }

    $identifier = if ([string]::IsNullOrEmpty($GroupName)) { "ID '$GroupId'" } else { "Name '$GroupName' (ID: $GroupId)" }
    throw "Gruppe mit $identifier wurde nicht gefunden. Sie wurde möglicherweise zwischenzeitlich gelöscht."
}
