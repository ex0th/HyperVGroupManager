function Rename-HVGMGroup {
    <#
        Benennt eine vorhandene VM-Gruppe um. Verhindert, dass dabei ein bereits
        vergebener Gruppenname entsteht.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [guid]$GroupId,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$NewName
    )

    try {
        Assert-HVGMGroupName -GroupName $NewName
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $group = Get-HVGMGroupById -Target $target -GroupId $GroupId
        $hostName = Get-HVGMGroupHostName -Target $target

        $duplicate = Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $NewName }

        if ($duplicate) {
            throw "Eine Gruppe mit dem Namen '$NewName' existiert bereits."
        }

        Rename-VMGroup -VMGroup $group -NewName $NewName -Confirm:$false -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ Id = $GroupId; Name = $NewName })
    }
    catch {
        $safeMessage = ($_.Exception.Message -replace '[\r\n\t]+', ' ').Trim()
        New-HVGMResult -Success $false -Errors @($safeMessage)
    }
}
