function Remove-HVGMGroupMember {
    <#
        Entfernt eine konkrete VM-Gruppe-Mitgliedschaft. Eine nicht vorhandene
        Mitgliedschaft wird nicht als Fehler, sondern als Warnung behandelt.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [guid]$VmId,

        [Parameter(Mandatory)]
        [guid]$GroupId
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $vm = Get-HVGMVmById -Target $target -VmId $VmId
        $group = Get-HVGMGroupById -Target $target -GroupId $GroupId

        $warnings = @()
        $isMember = @($group.VMMembers | Where-Object { $_.Id -eq $vm.Id })

        if ($isMember.Count -eq 0) {
            $warnings += "VM '$($vm.Name)' ist kein Mitglied der Gruppe '$($group.Name)'."
        }
        else {
            Remove-VMGroupMember -VMGroup $group -VM $vm -ErrorAction Stop
        }

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ VmId = $vm.Id; GroupId = $group.Id }) -Warnings $warnings
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
