function Unregister-HVGMEmailReportTask {
    <#
        Entfernt die registrierte Windows-Aufgabenplanung für den E-Mail-Bericht.
    #>
    [CmdletBinding()]
    param(
        [string]$TaskName = 'HyperVGroupManager_UntaggedVMsReport'
    )

    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

        if ($null -eq $task) {
            New-HVGMResult -Success $true -Data "Aufgabe '$TaskName' wurde nicht gefunden (bereits entfernt oder nie registriert)."
        }
        else {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
            New-HVGMResult -Success $true -Data "Aufgabe '$TaskName' wurde erfolgreich entfernt."
        }
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
