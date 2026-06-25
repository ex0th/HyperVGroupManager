function New-HVGMGroup {
    <#
        Erstellt eine neue native Hyper-V-VM-Gruppe (VMCollectionType). Verhindert
        doppelte Gruppennamen (Groß-/Kleinschreibung wird dabei ignoriert).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$TargetName,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$GroupName
    )

    try {
        $target = Resolve-HVGMTarget -TargetName $TargetName
        $hostName = Get-HVGMGroupHostName -Target $target

        $existing = Get-VMGroup -ComputerName $hostName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $GroupName }

        if ($existing) {
            throw "Eine Gruppe mit dem Namen '$GroupName' existiert auf '$TargetName' bereits."
        }

        $newGroup = New-VMGroup -Name $GroupName -GroupType VMCollectionType -ComputerName $hostName -ErrorAction Stop

        New-HVGMResult -Success $true -Data ([pscustomobject]@{
            Id   = $newGroup.Id
            Name = $newGroup.Name
        })
    }
    catch {
        New-HVGMResult -Success $false -Errors @($_.Exception.Message)
    }
}
