function Get-HVGMVirtualMachine {
    <#
        Liest alle VMs vom Zielsystem (Einzelhost oder alle Cluster-Nodes) inklusive
        Owner-Node und aktueller Gruppenmitgliedschaften (eine VM kann in mehreren
        Gruppen Mitglied sein).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName

        $vmEntries = @()
        foreach ($node in $target.Nodes) {
            $nodeVms = Get-VM -ComputerName $node -ErrorAction Stop
            foreach ($vm in $nodeVms) {
                $vmEntries += [pscustomobject]@{
                    NativeVm  = $vm
                    OwnerNode = $node
                }
            }
        }

        $hostName = Get-HVGMGroupHostName -Target $target
        $groups = @(Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.GroupType -eq 'VMCollectionType' })

        # VmId -> Liste der Gruppennamen, in denen die VM Mitglied ist.
        $membershipMap = @{}
        foreach ($group in $groups) {
            foreach ($member in $group.VMMembers) {
                $key = $member.Id.ToString()
                if (-not $membershipMap.ContainsKey($key)) {
                    $membershipMap[$key] = New-Object System.Collections.Generic.List[string]
                }
                $membershipMap[$key].Add($group.Name)
            }
        }

        $result = foreach ($entry in $vmEntries) {
            $vm = $entry.NativeVm
            $key = $vm.Id.ToString()
            $groupNames = if ($membershipMap.ContainsKey($key)) { @($membershipMap[$key]) } else { @() }

            [pscustomobject]@{
                Id           = $vm.Id
                Name         = $vm.Name
                ComputerName = $vm.ComputerName
                OwnerNode    = $entry.OwnerNode
                State        = $vm.State.ToString()
                IsClustered  = $target.IsCluster
                GroupNames   = @($groupNames)
            }
        }

        New-HVGMResult -Success $true -Data @($result)
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
