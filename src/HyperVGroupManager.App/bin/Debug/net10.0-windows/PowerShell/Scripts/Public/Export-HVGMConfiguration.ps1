function Export-HVGMConfiguration {
    <#
        Liefert die aktuelle Gruppenkonfiguration als exportierbares Objekt
        (targetName/exportedAt/groups[].members[]).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    try {
        $groupsResult = Get-HVGMGroup -TargetName $TargetName
        if (-not $groupsResult.Success) {
            throw ($groupsResult.Errors -join '; ')
        }

        $exportGroups = foreach ($group in $groupsResult.Data) {
            $members = for ($i = 0; $i -lt $group.MemberVmIds.Count; $i++) {
                [pscustomobject]@{
                    Id   = $group.MemberVmIds[$i]
                    Name = $group.MemberVmNames[$i]
                }
            }

            [pscustomobject]@{
                Id        = $group.Id
                Name      = $group.Name
                GroupType = $group.GroupType
                Members   = @($members)
            }
        }

        $export = [pscustomobject]@{
            TargetName = $TargetName
            ExportedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            Groups     = @($exportGroups)
        }

        New-HVGMResult -Success $true -Data $export
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
