function Invoke-HVGMChangeSet {
    <#
        Führt eine Liste geplanter Änderungen (bereits in sinnvoller Reihenfolge vom
        Aufrufer sortiert) sequenziell aus. Bricht die Verarbeitung nach dem ersten
        Fehler ab, protokolliert aber das Ergebnis jeder einzelnen Änderung, damit
        bereits erfolgreiche Schritte erkennbar bleiben.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [object[]]$Changes
    )

    # Bewusst ein einfaches PowerShell-Array statt System.Collections.Generic.List[object]:
    # Letzteres löst unter Windows PowerShell 5.1 in Kombination mit "@(...)" einen bekannten
    # Binder-Fehler ("Argument types do not match") beim Aufruf von New-HVGMResult aus.
    $itemResults = @()
    $stopProcessing = $false

    foreach ($change in $Changes) {
        if ($stopProcessing) {
            $itemResults += [pscustomobject]@{
                ChangeType  = $change.ChangeType
                VmId        = $change.VmId
                GroupId     = $change.GroupId
                Description = $change.Description
                Success     = $false
                Error       = "Nicht ausgeführt, da eine vorherige Änderung fehlgeschlagen ist."
                Warnings    = @()
            }
            continue
        }

        try {
            switch ($change.ChangeType) {
                'CreateGroup' {
                    $itemResult = New-HVGMGroup -TargetName $TargetName -GroupName $change.GroupName
                }
                'RenameGroup' {
                    $itemResult = Rename-HVGMGroup -TargetName $TargetName -GroupId $change.GroupId -NewName $change.GroupName
                }
                'AddMembership' {
                    $itemResult = Add-HVGMGroupMember -TargetName $TargetName -VmId $change.VmId -GroupId $change.GroupId
                }
                'RemoveMembership' {
                    $itemResult = Remove-HVGMGroupMember -TargetName $TargetName -VmId $change.VmId -GroupId $change.GroupId
                }
                'DeleteGroup' {
                    $itemResult = Remove-HVGMGroup -TargetName $TargetName -GroupId $change.GroupId
                }
                default {
                    throw "Unbekannter Änderungstyp '$($change.ChangeType)'."
                }
            }

            if ($itemResult.Success) {
                $itemResults += [pscustomobject]@{
                    ChangeType  = $change.ChangeType
                    VmId        = $change.VmId
                    GroupId     = $change.GroupId
                    Description = $change.Description
                    Success     = $true
                    Error       = $null
                    Warnings    = @($itemResult.Warnings)
                }
            }
            else {
                $stopProcessing = $true
                $itemResults += [pscustomobject]@{
                    ChangeType  = $change.ChangeType
                    VmId        = $change.VmId
                    GroupId     = $change.GroupId
                    Description = $change.Description
                    Success     = $false
                    Error       = ($itemResult.Errors -join '; ')
                    Warnings    = @()
                }
            }
        }
        catch {
            $stopProcessing = $true
            $itemResults += [pscustomobject]@{
                ChangeType  = $change.ChangeType
                VmId        = $change.VmId
                GroupId     = $change.GroupId
                Description = $change.Description
                Success     = $false
                Error       = $_.Exception.Message
                Warnings    = @()
            }
        }
    }

    $failedMessages = @($itemResults | Where-Object { -not $_.Success } | ForEach-Object { $_.Error })
    $overallSuccess = ($failedMessages.Count -eq 0)

    New-HVGMResult -Success $overallSuccess -Data $itemResults -Errors $failedMessages
}
