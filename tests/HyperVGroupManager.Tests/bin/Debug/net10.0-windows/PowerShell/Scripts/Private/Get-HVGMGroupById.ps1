function Get-HVGMGroupById {
    <#
        Sucht eine VM-Gruppe anhand ihrer Id über Get-HVGMGroupHostName, damit Gruppen-Lookups
        im gesamten Modul konsistent gegen denselben Host ausgeführt werden.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Target,

        [Parameter(Mandatory)]
        [guid]$GroupId
    )

    $hostName = Get-HVGMGroupHostName -Target $Target
    $group = Get-VMGroup -ComputerName $hostName -Id $GroupId -ErrorAction SilentlyContinue

    if (-not $group) {
        throw "Gruppe mit ID '$GroupId' wurde nicht gefunden. Sie wurde möglicherweise zwischenzeitlich gelöscht."
    }

    return $group
}
