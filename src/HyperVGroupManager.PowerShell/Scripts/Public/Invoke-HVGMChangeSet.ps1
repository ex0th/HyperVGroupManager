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
    # Maps client-side placeholder GUIDs to real Hyper-V GUIDs. Needed when a CreateGroup and
    # subsequent AddMembership/RemoveMembership/RenameGroup/DeleteGroup for the same group are in
    # the same changeset: Hyper-V assigns its own GUID during New-VMGroup, which differs from the
    # GUID the C# side generated as a placeholder before the apply run.
    $groupIdMap = @{}

    # Validate the complete payload before the first mutating command runs. Keep string
    # literals ASCII-only because Windows PowerShell 5.1 may read BOM-less files as ANSI.
    if ($Changes.Count -eq 0) {
        return (New-HVGMResult -Success $false -Errors @('The change set is empty.'))
    }
    if ($Changes.Count -gt 5000) {
        return (New-HVGMResult -Success $false -Errors @('The change set exceeds the limit of 5000 entries.'))
    }

    $executionOrder = @{
        'CreateGroup'      = 0
        'RenameGroup'      = 1
        'AddMembership'    = 2
        'RemoveMembership' = 3
        'DeleteGroup'      = 4
    }
    $seenOperations = @{}
    $previousOrder = -1
    $validationErrors = @()

    for ($index = 0; $index -lt $Changes.Count; $index++) {
        $change = $Changes[$index]
        $label = "Change $($index + 1)"
        if ($null -eq $change) {
            $validationErrors += "$label is null."
            continue
        }

        $changeType = [string]$change.ChangeType
        if (-not $executionOrder.ContainsKey($changeType)) {
            $validationErrors += "$label has an unknown change type."
            continue
        }
        if ($executionOrder[$changeType] -lt $previousOrder) {
            $validationErrors += "$label is not in the required execution order."
        }
        $previousOrder = $executionOrder[$changeType]

        $groupId = [guid]::Empty
        if (-not [guid]::TryParse([string]$change.GroupId, [ref]$groupId) -or $groupId -eq [guid]::Empty) {
            $validationErrors += "$label has an invalid group ID."
        }

        $groupName = [string]$change.GroupName
        if ([string]::IsNullOrWhiteSpace($groupName) -or $groupName.Length -gt 256 -or $groupName -match '[\x00-\x1F\x7F]') {
            $validationErrors += "$label has an invalid group name."
        }
        elseif ($groupName -ne $groupName.Trim()) {
            $validationErrors += "$label has whitespace around the group name."
        }

        $vmIdText = [string]$change.VmId
        if ($changeType -eq 'AddMembership' -or $changeType -eq 'RemoveMembership') {
            $vmId = [guid]::Empty
            if (-not [guid]::TryParse($vmIdText, [ref]$vmId) -or $vmId -eq [guid]::Empty) {
                $validationErrors += "$label has an invalid VM ID."
            }
        }

        $operationKey = "$changeType|$vmIdText|$([string]$change.GroupId)"
        if ($seenOperations.ContainsKey($operationKey)) {
            $validationErrors += "$label duplicates an earlier operation."
        }
        else {
            $seenOperations[$operationKey] = $true
        }
    }

    if ($validationErrors.Count -gt 0) {
        return (New-HVGMResult -Success $false -Errors $validationErrors)
    }

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
                    if ($itemResult.Success -and $null -ne $itemResult.Data -and $null -ne $itemResult.Data.Id) {
                        $clientId = [string]$change.GroupId
                        $realId   = [string]$itemResult.Data.Id
                        if ($clientId -ne $realId) {
                            $groupIdMap[$clientId] = $realId
                        }
                    }
                }
                'RenameGroup' {
                    $resolvedId = if ($groupIdMap.ContainsKey([string]$change.GroupId)) { $groupIdMap[[string]$change.GroupId] } else { $change.GroupId }
                    $itemResult = Rename-HVGMGroup -TargetName $TargetName -GroupId $resolvedId -NewName $change.GroupName
                }
                'AddMembership' {
                    $resolvedId = if ($groupIdMap.ContainsKey([string]$change.GroupId)) { $groupIdMap[[string]$change.GroupId] } else { $change.GroupId }
                    $itemResult = Add-HVGMGroupMember -TargetName $TargetName -VmId $change.VmId -GroupId $resolvedId -GroupName $change.GroupName
                }
                'RemoveMembership' {
                    $resolvedId = if ($groupIdMap.ContainsKey([string]$change.GroupId)) { $groupIdMap[[string]$change.GroupId] } else { $change.GroupId }
                    $itemResult = Remove-HVGMGroupMember -TargetName $TargetName -VmId $change.VmId -GroupId $resolvedId -GroupName $change.GroupName
                }
                'DeleteGroup' {
                    $resolvedId = if ($groupIdMap.ContainsKey([string]$change.GroupId)) { $groupIdMap[[string]$change.GroupId] } else { $change.GroupId }
                    $itemResult = Remove-HVGMGroup -TargetName $TargetName -GroupId $resolvedId -GroupName $change.GroupName
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
            $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
            $itemResults += [pscustomobject]@{
                ChangeType  = $change.ChangeType
                VmId        = $change.VmId
                GroupId     = $change.GroupId
                Description = $change.Description
                Success     = $false
                Error       = $safeMessage
                Warnings    = @()
            }
        }
    }

    $failedMessages = @($itemResults | Where-Object { -not $_.Success } | ForEach-Object { $_.Error })
    $overallSuccess = ($failedMessages.Count -eq 0)

    New-HVGMResult -Success $overallSuccess -Data $itemResults -Errors $failedMessages
}
