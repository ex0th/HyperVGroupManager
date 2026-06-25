function Get-HVGMVmById {
    <#
        Sucht eine VM anhand ihrer Id auf dem richtigen Owner-Node. Im Cluster-Fall werden
        alle Nodes durchsucht, da eine VM zwischenzeitlich live migriert worden sein kann.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target,

        [Parameter(Mandatory)]
        [guid]$VmId
    )

    foreach ($node in $Target.Nodes) {
        $vm = Get-VM -ComputerName $node -Id $VmId -ErrorAction SilentlyContinue
        if ($vm) {
            return $vm
        }
    }

    throw "VM mit ID '$VmId' wurde auf '$($Target.TargetName)' nicht gefunden. Sie wurde möglicherweise verschoben oder gelöscht."
}
