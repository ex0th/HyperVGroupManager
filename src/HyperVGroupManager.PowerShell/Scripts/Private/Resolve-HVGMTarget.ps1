function Resolve-HVGMTarget {
    <#
        Stellt fest, ob TargetName ein Failover-Cluster oder ein einzelner Hyper-V-Host ist,
        und liefert die zugehörigen Node-Namen. Wird von jeder öffentlichen Funktion als
        erster Schritt aufgerufen, damit Cluster-/Host-Erkennung nicht mehrfach unterschiedlich
        implementiert wird.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    try {
        # Wenn dies erfolgreich ist, handelt es sich um einen Failover-Cluster.
        Get-Cluster -Name $TargetName -ErrorAction Stop | Out-Null
        $nodes = @(Get-HVGMClusterNode -TargetName $TargetName)

        return [pscustomobject]@{
            TargetName = $TargetName
            IsCluster  = $true
            TargetType = 'Cluster'
            Nodes      = $nodes
        }
    }
    catch {
        # Kein Cluster (oder FailoverClusters-Modul fehlt) -> als Einzelhost behandeln.
        return [pscustomobject]@{
            TargetName = $TargetName
            IsCluster  = $false
            TargetType = 'Host'
            Nodes      = @($TargetName)
        }
    }
}
