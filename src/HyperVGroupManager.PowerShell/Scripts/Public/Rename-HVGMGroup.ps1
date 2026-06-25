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
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $group = Get-HVGMGroupById -Target $target -GroupId $GroupId
        $hostName = Get-HVGMGroupHostName -Target $target

        $duplicate = Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $NewName -and $_.Id -ne $GroupId }

        if ($duplicate) {
            throw "Eine Gruppe mit dem Namen '$NewName' existiert bereits."
        }

        Rename-VMGroup -VMGroup $group -NewName $NewName -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{ Id = $GroupId; Name = $NewName })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
