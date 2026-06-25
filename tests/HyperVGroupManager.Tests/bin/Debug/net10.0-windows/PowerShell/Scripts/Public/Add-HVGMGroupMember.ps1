function Add-HVGMGroupMember {
    <#
        Fügt eine VM einer Gruppe hinzu, ohne bestehende Mitgliedschaften in anderen
        Gruppen zu entfernen. Eine bereits bestehende Mitgliedschaft wird nicht als
        Fehler, sondern als Warnung behandelt.
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
        $alreadyMember = @($group.VMMembers | Where-Object { $_.Id -eq $vm.Id })

        if ($alreadyMember.Count -gt 0) {
            $warnings += "VM '$($vm.Name)' ist bereits Mitglied der Gruppe '$($group.Name)'."
        }
        else {
            Add-VMGroupMember -VMGroup $group -VM $vm -ErrorAction Stop
        }

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ VmId = $vm.Id; GroupId = $group.Id }) -Warnings $warnings
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
