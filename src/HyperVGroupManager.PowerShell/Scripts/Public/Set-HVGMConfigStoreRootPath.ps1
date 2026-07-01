function Set-HVGMConfigStoreRootPath {
    <#
        Setzt den ConfigStoreRootPath der Hyper-V-Cluster-Ressource
        "Virtual Machine Cluster WMI". Nur auf Cluster-Knoten verfÃ¼gbar.
        Dieser Pfad bestimmt, wo Hyper-V die VM-Gruppen-Konfigurationsdateien ablegt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Path
    )

    try {
        $target   = Resolve-HVGMTarget -TargetName $TargetName
        $hostName = Get-HVGMGroupHostName -Target $target

        $resource = Get-ClusterResource -Name 'Virtual Machine Cluster WMI' `
            -Cluster $hostName -ErrorAction Stop

        $resource | Set-ClusterParameter -Name ConfigStoreRootPath -Value $Path `
            -Confirm:$false -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ Path = $Path })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
