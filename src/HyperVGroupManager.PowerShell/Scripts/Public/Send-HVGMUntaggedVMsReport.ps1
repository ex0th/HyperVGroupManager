function Send-HVGMUntaggedVMsReport {
    <#
        Liest alle VMs vom Zielsystem, ermittelt jene ohne Gruppenzuordnung und sendet
        einen HTML-formatierten E-Mail-Bericht via SMTP.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [string]$SmtpHost,

        [int]$SmtpPort = 587,

        # None | STARTTLS | SSL
        [string]$SmtpSecurity = 'STARTTLS',

        [bool]$UseAuthentication = $false,
        [string]$Username = '',
        [string]$Password = '',

        [Parameter(Mandatory)]
        [string]$SenderAddress,

        [string]$SenderDisplayName = 'Hyper-V Group Manager',

        [Parameter(Mandatory)]
        [object[]]$RecipientAddresses,

        [string]$BodyPrefix = ''
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName

        $hostName = Get-HVGMGroupHostName -Target $target
        $groups = @(Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.GroupType -eq 'VMCollectionType' })

        # ID-Menge aller VMs mit mind. einer Gruppe aufbauen
        $taggedIds = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($g in $groups) {
            foreach ($m in $g.VMMembers) { $null = $taggedIds.Add($m.Id.ToString()) }
        }

        # VMs ohne Gruppe sammeln
        $untagged = [System.Collections.Generic.List[pscustomobject]]::new()
        foreach ($node in $target.Nodes) {
            foreach ($vm in (Get-VM -ComputerName $node -ErrorAction Stop)) {
                if (-not $taggedIds.Contains($vm.Id.ToString())) {
                    $untagged.Add([pscustomobject]@{
                        Name      = $vm.Name
                        State     = $vm.State.ToString()
                        OwnerNode = $node
                    })
                }
            }
        }

        $dateStr  = Get-Date -Format 'dd.MM.yyyy'
        $subject  = "Hyper-V VMs ohne Gruppe - $TargetName ($dateStr)"

        # E-Mail-Body (Plain Text)
        $lines = [System.Collections.Generic.List[string]]::new()
        if ($BodyPrefix) { $lines.Add($BodyPrefix); $lines.Add('') }

        if ($untagged.Count -eq 0) {
            $lines.Add("Alle VMs auf $TargetName sind mindestens einer Hyper-V-Gruppe zugeordnet. Kein Handlungsbedarf.")
        }
        else {
            $lines.Add("Auf $TargetName wurden $($untagged.Count) VM(s) ohne Gruppenzuordnung gefunden:")
            $lines.Add('')
            foreach ($vm in $untagged) {
                $lines.Add("  - $($vm.Name)  [Status: $($vm.State), Node: $($vm.OwnerNode)]")
            }
        }

        $lines.Add('')
        $lines.Add("Bericht erstellt am $(Get-Date -Format 'dd.MM.yyyy HH:mm:ss') durch Hyper-V Group Manager.")
        $body = $lines -join "`r`n"

        # SMTP-Parameter zusammenbauen
        $fromField = if ($SenderDisplayName) { "`"$SenderDisplayName`" <$SenderAddress>" } else { $SenderAddress }

        $smtpParams = @{
            SmtpServer = $SmtpHost
            Port       = $SmtpPort
            From       = $fromField
            To         = @($RecipientAddresses | ForEach-Object { $_.ToString() })
            Subject    = $subject
            Body       = $body
            Encoding   = [System.Text.Encoding]::UTF8
        }

        if ($SmtpSecurity -eq 'STARTTLS' -or $SmtpSecurity -eq 'SSL') {
            $smtpParams['UseSsl'] = $true
        }

        if ($UseAuthentication -and -not [string]::IsNullOrWhiteSpace($Username)) {
            $secPwd    = ConvertTo-SecureString -String $Password -AsPlainText -Force
            $cred      = New-Object System.Management.Automation.PSCredential($Username, $secPwd)
            $smtpParams['Credential'] = $cred
        }

        Send-MailMessage @smtpParams -ErrorAction Stop

        $msg = "E-Mail-Bericht gesendet. $($untagged.Count) VM(s) ohne Gruppe auf $TargetName."
        New-HVGMResult -Success $true -Data $msg
    }
    catch {
        $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
        New-HVGMResult -Success $false -Errors @($safeMessage)
    }
}
