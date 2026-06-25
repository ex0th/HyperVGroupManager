function Get-HVGMClusterNode {
    <#
        Zentrale Stelle für Get-ClusterNode. Wird ausschließlich von Resolve-HVGMTarget
        aufgerufen, damit es im gesamten Modul nur eine Variante dieses Aufrufs gibt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    (Get-ClusterNode -Cluster $TargetName -ErrorAction Stop) | ForEach-Object { $_.Name }
}
