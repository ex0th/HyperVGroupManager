function Get-HVGMGroupHostName {
    <#
        Liefert den Computernamen, gegen den Get-/New-/Rename-/Remove-VMGroup ausgeführt werden soll.
        Native Hyper-V-VM-Gruppen (VMCollectionType) werden bei geclusterten Hosts clusterweit
        freigegeben, daher reicht im Cluster-Fall ein beliebiger Node. Zentral an einer Stelle,
        damit nicht mehrere Varianten von "welcher Host für VMGroup-Aufrufe" im Modul entstehen.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target
    )

    if ($Target.IsCluster) {
        return $Target.Nodes[0]
    }

    return $Target.TargetName
}
