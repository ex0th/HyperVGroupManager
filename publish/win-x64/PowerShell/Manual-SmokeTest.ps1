<#
    Manueller Rauchtest für das HyperVGroupManager-PowerShell-Modul.
    Nicht Teil der automatisierten Unit-Tests (die PowerShell nicht wirklich ausführen).

    Beispielaufruf auf einem Hyper-V-Host oder -Cluster-Node:
        .\Manual-SmokeTest.ps1 -TargetName "localhost"
#>
param(
    [Parameter(Mandatory)]
    [string]$TargetName
)

$modulePath = Join-Path -Path $PSScriptRoot -ChildPath 'HyperVGroupManager.psd1'
Import-Module -Name $modulePath -Force

Write-Host "== Test-HVGMEnvironment ==" -ForegroundColor Cyan
Test-HVGMEnvironment -TargetName $TargetName | ConvertTo-Json -Depth 10

Write-Host "`n== Get-HVGMVirtualMachine ==" -ForegroundColor Cyan
Get-HVGMVirtualMachine -TargetName $TargetName | ConvertTo-Json -Depth 10 -Compress

Write-Host "`n== Get-HVGMGroup ==" -ForegroundColor Cyan
Get-HVGMGroup -TargetName $TargetName | ConvertTo-Json -Depth 10 -Compress
