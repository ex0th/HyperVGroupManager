function New-HVGMResult {
    <#
        Baut das einheitliche Rückgabeobjekt, das von allen öffentlichen HVGM-Funktionen
        als ConvertTo-Json -Depth 10 -Compress an C# zurückgegeben wird.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [bool]$Success,

        [object]$Data = $null,

        [string[]]$Errors = @(),

        [string[]]$Warnings = @()
    )

    [pscustomobject]@{
        Success  = $Success
        Data     = $Data
        Errors   = @($Errors)
        Warnings = @($Warnings)
    }
}
