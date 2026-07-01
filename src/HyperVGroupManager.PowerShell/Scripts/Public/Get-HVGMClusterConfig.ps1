function Get-HVGMClusterConfig {
    <#
        Liest die Cluster-Konfiguration (ConfigStoreRootPath) vom Zielsystem.
        Gibt IsCluster=$false zurÃ¼ck, wenn der Host kein Cluster-Knoten ist oder
        das FailoverClusters-Modul nicht verfÃ¼gbar ist.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    try {
        $target   = Resolve-HVGMTarget -TargetName $TargetName
        $hostName = Get-HVGMGroupHostName -Target $target

        $clusterModule = Get-Module -Name FailoverClusters -ListAvailable -ErrorAction SilentlyContinue
        if ($null -eq $clusterModule) {
            return New-HVGMResult -Success $true -Data ([pscustomobject]@{
                IsCluster           = $false
                ConfigStoreRootPath = $null
                Message             = 'Das FailoverClusters-Modul ist auf diesem Host nicht verfÃ¼gbar.'
            })
        }

        $resource = Get-ClusterResource -Name 'Virtual Machine Cluster WMI' `
            -Cluster $hostName -ErrorAction SilentlyContinue
        if ($null -eq $resource) {
            return New-HVGMResult -Success $true -Data ([pscustomobject]@{
                IsCluster           = $false
                ConfigStoreRootPath = $null
                Message             = 'Kein Cluster-Knoten oder Ressource "Virtual Machine Cluster WMI" nicht gefunden.'
            })
        }

        $param = $resource | Get-ClusterParameter -Name ConfigStoreRootPath -ErrorAction SilentlyContinue
        $path  = if ($null -ne $param -and -not [string]::IsNullOrEmpty($param.Value)) { [string]$param.Value } else { $null }

        New-HVGMResult -Success $true -Data ([pscustomobject]@{
            IsCluster           = $true
            ConfigStoreRootPath = $path
            Message             = $null
        })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
