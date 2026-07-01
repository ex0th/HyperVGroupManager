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
            # Hyper-V stores the GUID in InstanceId (not Id). Fall back to a deterministic GUID
            # for Windows Server versions where InstanceId is null or empty.
            $instanceId = $group.InstanceId
            $groupId = if ($null -ne $instanceId -and $instanceId -ne [guid]::Empty) { $instanceId.ToString() } else { (Get-HVGMDeterministicGroupGuid $group.Name).ToString() }
            [pscustomobject]@{
                Id            = $groupId
                Name          = $group.Name
                GroupType     = $group.GroupType.ToString()
                MemberCount   = @($group.VMMembers).Count
                MemberVmIds   = @($group.VMMembers | ForEach-Object { if ($null -ne $_.Id) { $_.Id.ToString() } else { '' } })
                MemberVmNames = @($group.VMMembers | ForEach-Object { $_.Name })
            }
        }

        New-HVGMResult -Success $true -Data @($result)
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
