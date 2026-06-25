$privatePath = Join-Path -Path $PSScriptRoot -ChildPath 'Scripts\Private'
$publicPath = Join-Path -Path $PSScriptRoot -ChildPath 'Scripts\Public'

Get-ChildItem -Path $privatePath -Filter '*.ps1' -File | ForEach-Object { . $_.FullName }
Get-ChildItem -Path $publicPath -Filter '*.ps1' -File | ForEach-Object { . $_.FullName }

Export-ModuleMember -Function @(
    'Test-HVGMEnvironment',
    'Get-HVGMVirtualMachine',
    'Get-HVGMGroup',
    'New-HVGMGroup',
    'Rename-HVGMGroup',
    'Remove-HVGMGroup',
    'Add-HVGMGroupMember',
    'Remove-HVGMGroupMember',
    'Invoke-HVGMChangeSet',
    'Export-HVGMConfiguration'
)
