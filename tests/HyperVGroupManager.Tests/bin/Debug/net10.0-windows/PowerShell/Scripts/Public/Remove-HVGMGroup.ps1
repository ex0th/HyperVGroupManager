function Remove-HVGMGroup {
    <#
        Löscht eine VM-Gruppe. Eine nicht leere Gruppe wird standardmäßig nicht gelöscht,
        sondern bricht mit einer verständlichen Fehlermeldung ab.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [guid]$GroupId
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $group = Get-HVGMGroupById -Target $target -GroupId $GroupId

        $memberCount = @($group.VMMembers).Count
        if ($memberCount -gt 0) {
            throw "Die Gruppe '$($group.Name)' enthält noch $memberCount Mitglied(er) und kann nicht gelöscht werden. Entfernen Sie zuerst alle Mitgliedschaften."
        }

        Remove-VMGroup -VMGroup $group -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ Id = $GroupId; Name = $group.Name })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
