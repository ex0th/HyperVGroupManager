<#
    Fester Bootstrap-Einstiegspunkt, den der externe powershell.exe-Prozess über -File aufruft.
    Importiert das Modul, liest die Parameter aus einer JSON-Datei und ruft genau eine
    bekannte Funktion auf. Kein Invoke-Expression, keine String-Verkettung von Code.
#>
param(
    [Parameter(Mandatory)]
    [string]$ModuleManifestPath,

    [Parameter(Mandatory)]
    [string]$CommandName,

    [Parameter(Mandatory)]
    [string]$ParametersFilePath
)

$ErrorActionPreference = 'Stop'

try {
    Import-Module -Name $ModuleManifestPath -Force

    $parametersJson = Get-Content -Path $ParametersFilePath -Raw -Encoding UTF8
    $parametersObject = $parametersJson | ConvertFrom-Json

    # PS 5.1 kennt ConvertFrom-Json -AsHashtable nicht; Eigenschaften manuell in eine
    # Hashtable übernehmen, damit sie als benannte Parameter gesplattet werden können.
    $parametersTable = @{}
    if ($null -ne $parametersObject) {
        foreach ($property in $parametersObject.PSObject.Properties) {
            $parametersTable[$property.Name] = $property.Value
        }
    }

    $result = & $CommandName @parametersTable
    $result | ConvertTo-Json -Depth 10 -Compress
}
catch {
    [pscustomobject]@{
        Success  = $false
        Data     = $null
        Errors   = @($_.Exception.Message)
        Warnings = @()
    } | ConvertTo-Json -Depth 10 -Compress
}
