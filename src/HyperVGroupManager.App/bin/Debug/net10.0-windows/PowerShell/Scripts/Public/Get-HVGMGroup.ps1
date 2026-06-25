function Get-HVGMGroup {
    <#
        Liest alle nativen Hyper-V-VM-Gruppen vom Typ VMCollectionType vom Zielsystem.
        ManagementCollectionType und verschachtelte Gruppen sind im MVP nicht Bestandteil.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $hostName = Get-HVGMGroupHostName -Target $target

        $groups = Get-VMGroup -ComputerName $hostName -ErrorAction Stop |
            Where-Object { $_.GroupType -eq 'VMCollectionType' }

        $result = foreach ($group in $groups) {
            [pscustomobject]@{
                Id            = $group.Id
                Name          = $group.Name
                GroupType     = $group.GroupType.ToString()
                MemberCount   = @($group.VMMembers).Count
                MemberVmIds   = @($group.VMMembers | ForEach-Object { $_.Id })
                MemberVmNames = @($group.VMMembers | ForEach-Object { $_.Name })
            }
        }

        New-HVGMResult -Success $true -Data @($result)
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
