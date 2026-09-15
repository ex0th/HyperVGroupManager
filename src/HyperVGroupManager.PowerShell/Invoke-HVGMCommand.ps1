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

$allowedCommands = @(
    'Test-HVGMEnvironment',
    'Get-HVGMVirtualMachine',
    'Get-HVGMGroup',
    'New-HVGMGroup',
    'Rename-HVGMGroup',
    'Remove-HVGMGroup',
    'Add-HVGMGroupMember',
    'Remove-HVGMGroupMember',
    'Invoke-HVGMChangeSet',
    'Export-HVGMConfiguration',
    'Get-HVGMClusterConfig',
    'Set-HVGMConfigStoreRootPath',
    'Send-HVGMUntaggedVMsReport',
    'Register-HVGMEmailReportTask',
    'Unregister-HVGMEmailReportTask',
    'Get-HVGMEmailReportTaskStatus'
)

# Warning/Verbose/Debug/Information/Progress der zugrunde liegenden Hyper-V-/Cluster-Cmdlets
# unterdrücken. Bei -File mit umgeleitetem stdout schreibt PowerShell diese Streams ebenfalls
# auf stdout, teils asynchron beim Aufräumen von CIM/WMI-Sitzungen (z. B. bei Cluster-Verbindungen)
# - dadurch kann Text mitten in die per ConvertTo-Json erzeugte JSON-Zeile geschrieben werden
# und sie strukturell zerstören. Nur der finale $result-Aufruf soll auf stdout landen.
$WarningPreference     = 'SilentlyContinue'
$VerbosePreference     = 'SilentlyContinue'
$DebugPreference       = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
$ProgressPreference    = 'SilentlyContinue'

# UTF-8 ohne BOM erzwingen, damit deutsche Sonderzeichen in JSON-Ausgaben korrekt ankommen
# und keine BOM-Bytes vor der JSON-Ausgabe landen (Encoding.UTF8 hat eine Preamble).
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding            = $utf8NoBom

try {
    if ($CommandName -notin $allowedCommands) {
        throw "Command '$CommandName' is not allowed."
    }

    if (-not (Test-Path -LiteralPath $ModuleManifestPath -PathType Leaf)) {
        throw "The module manifest was not found."
    }

    if (-not (Test-Path -LiteralPath $ParametersFilePath -PathType Leaf)) {
        throw "The parameters file was not found."
    }

    $parametersFile = Get-Item -LiteralPath $ParametersFilePath
    if ($parametersFile.Length -gt 10MB) {
        throw "The parameters file exceeds the 10 MB safety limit."
    }

    Import-Module -Name $ModuleManifestPath -Force

    $parametersJson = Get-Content -LiteralPath $ParametersFilePath -Raw -Encoding UTF8
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
    # Zeilenumbrüche im Fehlertext entfernen – PS 5.1 escapt sie in ConvertTo-Json
    # nicht zuverlässig, was das JSON aufbricht.
    $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
    [pscustomobject]@{
        Success  = $false
        Data     = $null
        Errors   = @($safeMessage)
        Warnings = @()
    } | ConvertTo-Json -Depth 10 -Compress
}
