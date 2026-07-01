function Get-HVGMEmailReportTaskStatus {
    <#
        Gibt den aktuellen Status der registrierten E-Mail-Berichtsaufgabe zurück.
    #>
    [CmdletBinding()]
    param(
        [string]$TaskName = 'HyperVGroupManager_UntaggedVMsReport'
    )

    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

        if ($null -eq $task) {
            New-HVGMResult -Success $true -Data ([pscustomobject]@{
                TaskExists    = $false
                State         = 'NotFound'
                NextRunTime   = $null
                LastRunTime   = $null
                LastRunResult = $null
            })
        }
        else {
            $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue

            $nextRun  = $null
            $lastRun  = $null
            $lastCode = $null

            if ($info) {
                if ($info.NextRunTime -and $info.NextRunTime -ne [DateTime]::MinValue) {
                    $nextRun = $info.NextRunTime.ToString('dd.MM.yyyy HH:mm:ss')
                }
                if ($info.LastRunTime -and $info.LastRunTime -ne [DateTime]::MinValue -and
                    $info.LastRunTime.Year -gt 1999) {
                    $lastRun = $info.LastRunTime.ToString('dd.MM.yyyy HH:mm:ss')
                }
                if ($null -ne $info.LastTaskResult) {
                    $lastCode = $info.LastTaskResult.ToString()
                }
            }

            New-HVGMResult -Success $true -Data ([pscustomobject]@{
                TaskExists    = $true
                State         = $task.State.ToString()
                NextRunTime   = $nextRun
                LastRunTime   = $lastRun
                LastRunResult = $lastCode
            })
        }
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
