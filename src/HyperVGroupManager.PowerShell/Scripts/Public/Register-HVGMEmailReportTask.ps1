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
        [ValidateRange(1, 65535)]
        [int]   $SmtpPort          = 587,
        [ValidateSet('None', 'STARTTLS', 'SSL')]
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
        $bootstrapScript = Join-Path $AppDir 'PowerShell\Invoke-HVGMCommand.ps1'
        $moduleManifest  = Join-Path $AppDir 'PowerShell\HyperVGroupManager.psd1'
        if (-not (Test-Path -LiteralPath $bootstrapScript -PathType Leaf) -or
            -not (Test-Path -LiteralPath $moduleManifest -PathType Leaf)) {
            throw 'The application PowerShell files were not found. The scheduled task was not changed.'
        }

        $triggerClock = [datetime]::MinValue
        $validTime = [datetime]::TryParseExact(
            $TriggerTime,
            'HH:mm',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref]$triggerClock)
        if (-not $validTime) {
            throw 'TriggerTime must be a valid time in HH:mm format.'
        }

        $protectedPassword = ''
        if (-not [string]::IsNullOrEmpty($Password)) {
            # ConvertFrom-SecureString uses Windows DPAPI when no key is supplied. The
            # scheduled task therefore has to run as the same Windows user.
            $protectedPassword = ConvertTo-SecureString -String $Password -AsPlainText -Force |
                ConvertFrom-SecureString
        }

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
            ProtectedPassword  = $protectedPassword
            SenderAddress      = $SenderAddress
            SenderDisplayName  = $SenderDisplayName
            RecipientAddresses = @($RecipientAddresses | ForEach-Object { $_.ToString() })
            BodyPrefix         = $BodyPrefix
        } | ConvertTo-Json -Depth 5 | Set-Content -Path $paramsFile -Encoding UTF8

        # 2. Pfade für die Task-Aktion
        $arguments = "-ExecutionPolicy Bypass -NonInteractive -NoProfile " +
                     "-File `"$bootstrapScript`" " +
                     "-ModuleManifestPath `"$moduleManifest`" " +
                     "-CommandName Send-HVGMUntaggedVMsReport " +
                     "-ParametersFilePath `"$paramsFile`""

        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments

        # 3. Trigger (täglich zur gewünschten Uhrzeit)
        $triggerAt      = (Get-Date).Date.AddHours($triggerClock.Hour).AddMinutes($triggerClock.Minute)
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
        $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
        New-HVGMResult -Success $false -Errors @($safeMessage)
    }
}
