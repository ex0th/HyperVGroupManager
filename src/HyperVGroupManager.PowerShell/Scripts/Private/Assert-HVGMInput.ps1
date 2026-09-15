function Assert-HVGMTargetName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TargetName)

    if ([string]::IsNullOrWhiteSpace($TargetName) -or $TargetName.Length -gt 255 -or
        $TargetName -match '[\x00-\x1F\x7F]' -or $TargetName -ne $TargetName.Trim()) {
        throw 'The target name is empty, too long, or contains invalid whitespace/control characters.'
    }
}

function Assert-HVGMGroupName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$GroupName)

    if ([string]::IsNullOrWhiteSpace($GroupName) -or $GroupName.Length -gt 256 -or
        $GroupName -match '[\x00-\x1F\x7F]' -or $GroupName -ne $GroupName.Trim()) {
        throw 'The group name is empty, too long, or contains invalid whitespace/control characters.'
    }
}
