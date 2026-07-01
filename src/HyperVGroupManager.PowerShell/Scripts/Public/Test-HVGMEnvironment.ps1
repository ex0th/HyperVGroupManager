function Test-HVGMEnvironment {
    <#
        Prüft die Zielumgebung: Erreichbarkeit, Cluster-Erkennung, Nodes, Modulverfügbarkeit,
        Administratorrechte sowie Lesbarkeit von VMs und VM-Gruppen.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    $warnings = New-Object System.Collections.Generic.List[string]

    try {
        $hyperVModule = Get-Module -ListAvailable -Name 'Hyper-V' | Select-Object -First 1
        $failoverModule = Get-Module -ListAvailable -Name 'FailoverClusters' | Select-Object -First 1

        if (-not $hyperVModule) {
            throw "Das Hyper-V-PowerShell-Modul ist auf diesem System nicht installiert. Bitte installieren Sie die Hyper-V-Verwaltungstools (RSAT)."
        }

        Import-Module -Name 'Hyper-V' -ErrorAction Stop

        $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($currentIdentity)
        $isAdministrator = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

        if (-not $isAdministrator) {
            $warnings.Add("Die Anwendung wird nicht mit administrativen Rechten ausgeführt. Einige Hyper-V-Vorgänge könnten fehlschlagen.")
        }

        $target = Resolve-HVGMTarget -TargetName $TargetName
        $failoverModuleAvailable = [bool]$failoverModule

        if ($target.IsCluster -and -not $failoverModuleAvailable) {
            $warnings.Add("Das FailoverClusters-Modul ist nicht installiert. Cluster-spezifische Informationen könnten unvollständig sein.")
        }
        elseif ($failoverModuleAvailable) {
            Import-Module -Name 'FailoverClusters' -ErrorAction SilentlyContinue
        }

        $vmsReadable = $true
        try {
            foreach ($node in $target.Nodes) {
                Get-VM -ComputerName $node -ErrorAction Stop | Out-Null
            }
        }
        catch {
            $vmsReadable = $false
            $warnings.Add("VMs konnten auf '$TargetName' nicht gelesen werden: $($_.Exception.Message)")
        }

        $groupsReadable = $true
        try {
            $hostName = Get-HVGMGroupHostName -Target $target
            Get-VMGroup -ComputerName $hostName -ErrorAction Stop | Out-Null
        }
        catch {
            $groupsReadable = $false
            $warnings.Add("VM-Gruppen konnten auf '$TargetName' nicht gelesen werden: $($_.Exception.Message)")
        }

        $data = [pscustomobject]@{
            TargetName                       = $TargetName
            TargetType                       = $target.TargetType
            IsCluster                        = $target.IsCluster
            Nodes                            = @($target.Nodes)
            PowerShellVersion                = $PSVersionTable.PSVersion.ToString()
            HyperVModuleAvailable            = [bool]$hyperVModule
            FailoverClustersModuleAvailable  = $failoverModuleAvailable
            IsAdministrator                  = $isAdministrator
            Warnings                         = @($warnings)
        }

        New-HVGMResult -Success $true -Data $data -Warnings @($warnings)
    }
    catch {
        $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
        New-HVGMResult -Success $false -Errors @($safeMessage)
    }
}
