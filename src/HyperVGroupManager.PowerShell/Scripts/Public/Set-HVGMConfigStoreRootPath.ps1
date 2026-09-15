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
        if ($Path.Length -gt 1024 -or $Path -match '[\x00-\x1F\x7F]' -or
            -not [System.IO.Path]::IsPathRooted($Path)) {
            throw 'ConfigStoreRootPath must be a fully qualified Windows path and at most 1024 characters long.'
        }

        $target   = Resolve-HVGMTarget -TargetName $TargetName
        if (-not $target.IsCluster) {
            throw 'ConfigStoreRootPath can only be changed for a failover cluster.'
        }
        $hostName = Get-HVGMGroupHostName -Target $target

        $resource = Get-ClusterResource -Name 'Virtual Machine Cluster WMI' `
            -Cluster $hostName -ErrorAction Stop

        $resource | Set-ClusterParameter -Name ConfigStoreRootPath -Value $Path `
            -Confirm:$false -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ Path = $Path })
    }
    catch {
        $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
        New-HVGMResult -Success $false -Errors @($safeMessage)
    }
}
