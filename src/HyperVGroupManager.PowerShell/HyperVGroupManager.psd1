@{
    RootModule        = 'HyperVGroupManager.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = 'b4f3c9a1-7c0a-4a7b-9b8c-5e2f6d3a1c90'
    Author            = 'HyperVGroupManager'
    CompanyName       = 'HyperVGroupManager'
    Description       = 'Verwaltung nativer Hyper-V-VM-Gruppen (VMCollectionType) auf Einzelhosts und Failover-Clustern.'
    PowerShellVersion = '5.1'

    FunctionsToExport = @(
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

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
