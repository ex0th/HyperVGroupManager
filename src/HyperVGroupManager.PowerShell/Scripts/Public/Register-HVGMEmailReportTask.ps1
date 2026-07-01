function Register-HVGMEmailReportTask {
    <#
        Erstellt oder aktualisiert eine Windows-Aufgabenplanung, die täglich
        Send-HVGMUntaggedVMsReport über das Bootstrap-Skript aufruft.

        Die E-Mail-Parameter werden in %LOCALAPPDATA%\HyperVGroupManager\email-report-params.json
        gespeichert. Diese Datei liest der Scheduled Task beim Ausführen.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AppDir,

        [string]$TaskName = 'HyperVGroupManager_UntaggedVMsReport',

        [string]$TriggerTime = '08:00',

        # E-Mail / SMTP-Parameter, die in der persistenten Params-Datei abgelegt werden
        [string]$TargetName        = '',
        [string]$SmtpHost          = '',
        [int]   $SmtpPort          = 587,
        [string]$SmtpSecurity      = 'STARTTLS',
        [bool]  $UseAuthentication = $false,
        [string]$Username          = '',
        [string]$Password          = '',
        [string]$SenderAddress     = '',
        [string]$SenderDisplayName = 'Hyper-V Group Manager',
        [object[]]$RecipientAddresses = @(),
        [string]$BodyPrefix        = ''
    )

    try {
        # 1. Persistente Params-Datei schreiben
        $configDir  = Join-Path $env:LOCALAPPDATA 'HyperVGroupManager'
        $null       = New-Item -ItemType Directory -Path $configDir -Force
        $paramsFile = Join-Path $configDir 'email-report-params.json'

        [pscustomobject]@{
            TargetName         = $TargetName
            SmtpHost           = $SmtpHost
            SmtpPort           = $SmtpPort
            SmtpSecurity       = $SmtpSecurity
            UseAuthentication  = $UseAuthentication
            Username           = $Username
            Password           = $Password
            SenderAddress      = $SenderAddress
            SenderDisplayName  = $SenderDisplayName
            RecipientAddresses = @($RecipientAddresses | ForEach-Object { $_.ToString() })
            BodyPrefix         = $BodyPrefix
        } | ConvertTo-Json -Depth 5 | Set-Content -Path $paramsFile -Encoding UTF8

        # 2. Pfade für die Task-Aktion
        $bootstrapScript = Join-Path $AppDir 'PowerShell\Invoke-HVGMCommand.ps1'
        $moduleManifest  = Join-Path $AppDir 'PowerShell\HyperVGroupManager.psd1'

        $arguments = "-ExecutionPolicy Bypass -NonInteractive -NoProfile " +
                     "-File `"$bootstrapScript`" " +
                     "-ModuleManifestPath `"$moduleManifest`" " +
                     "-CommandName Send-HVGMUntaggedVMsReport " +
                     "-ParametersFilePath `"$paramsFile`""

        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments

        # 3. Trigger (täglich zur gewünschten Uhrzeit)
        $timeParts      = $TriggerTime -split ':'
        $hour           = [int]$timeParts[0]
        $minute         = if ($timeParts.Count -gt 1) { [int]$timeParts[1] } else { 0 }
        $triggerAt      = (Get-Date).Date.AddHours($hour).AddMinutes($minute)
        $trigger        = New-ScheduledTaskTrigger -Daily -At $triggerAt

        $settings = New-ScheduledTaskSettingsSet `
            -ExecutionTimeLimit (New-TimeSpan -Minutes 10) `
            -StartWhenAvailable

        # 4. Aufgabe registrieren (überschreibt vorhandene)
        Register-ScheduledTask `
            -TaskName  $TaskName `
            -Action    $action `
            -Trigger   $trigger `
            -Settings  $settings `
            -RunLevel  Highest `
            -Force | Out-Null

        New-HVGMResult -Success $true -Data "Aufgabe '$TaskName' erfolgreich registriert (täglich um $TriggerTime Uhr)."
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
