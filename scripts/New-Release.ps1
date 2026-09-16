[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateNotNullOrEmpty()]
    [string]$Remote = 'origin',

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $output = @(& git @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure)
    {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
    }

    [PSCustomObject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue))
    {
        throw "Required command '$Name' was not found in PATH."
    }
}

Assert-CommandAvailable 'git'
Assert-CommandAvailable 'dotnet'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\HyperVGroupManager.App\HyperVGroupManager.App.csproj'
$solutionPath = Join-Path $repositoryRoot 'HyperVGroupManager.sln'
$tagName = "v$Version"

Push-Location $repositoryRoot
try
{
    $actualRoot = (Invoke-Git -Arguments @('rev-parse', '--show-toplevel')).Output[0].Trim()
    if (-not [IO.Path]::GetFullPath($actualRoot).Equals(
        [IO.Path]::GetFullPath($repositoryRoot),
        [StringComparison]::OrdinalIgnoreCase))
    {
        throw "The script directory is not inside the expected Git repository root: $repositoryRoot"
    }

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode)
    {
        throw "No <Version> element was found in $projectPath."
    }

    $projectVersion = $versionNode.InnerText.Trim()
    if ($projectVersion -ne $Version)
    {
        throw "Requested version '$Version' does not match the project version '$projectVersion'."
    }

    $numericVersion = ($Version -split '-', 2)[0]
    $expectedFileVersion = "$numericVersion.0"
    foreach ($elementName in @('AssemblyVersion', 'FileVersion'))
    {
        $node = $project.SelectSingleNode("/Project/PropertyGroup/$elementName")
        if ($null -eq $node -or $node.InnerText.Trim() -ne $expectedFileVersion)
        {
            $actualValue = if ($null -eq $node) { '<missing>' } else { $node.InnerText.Trim() }
            throw "Project $elementName '$actualValue' does not match expected version '$expectedFileVersion'."
        }
    }

    $workingTree = (Invoke-Git -Arguments @('status', '--porcelain=v1')).Output
    if ($workingTree.Count -gt 0)
    {
        throw "The working tree is not clean. Commit or stash all changes before creating a release.`n$($workingTree -join [Environment]::NewLine)"
    }

    $branch = (Invoke-Git -Arguments @('branch', '--show-current')).Output[0].Trim()
    if ([string]::IsNullOrWhiteSpace($branch))
    {
        throw 'Releases cannot be created from a detached HEAD. Check out the release branch first.'
    }

    $head = (Invoke-Git -Arguments @('rev-parse', 'HEAD')).Output[0].Trim()
    $null = Invoke-Git -Arguments @('fetch', '--tags', $Remote)

    $remoteBranch = (Invoke-Git -Arguments @('ls-remote', '--heads', $Remote, "refs/heads/$branch")).Output
    if ($remoteBranch.Count -eq 0)
    {
        throw "Remote '$Remote' has no branch '$branch'. Push the branch before creating a release."
    }

    $remoteHead = ($remoteBranch[0] -split '\s+')[0]
    if ($remoteHead -ne $head)
    {
        throw "Local HEAD $head is not the commit currently published on $Remote/$branch ($remoteHead). Push or synchronize the branch first."
    }

    $remoteTag = (Invoke-Git -Arguments @(
        'ls-remote', '--tags', $Remote, "refs/tags/$tagName", "refs/tags/$tagName^{}"
    )).Output
    if ($remoteTag.Count -gt 0)
    {
        throw "Tag '$tagName' already exists on remote '$Remote'. No release action was taken."
    }

    $localTagCheck = Invoke-Git -Arguments @('show-ref', '--verify', '--quiet', "refs/tags/$tagName") -AllowFailure
    if ($localTagCheck.ExitCode -eq 0)
    {
        $localTaggedCommit = (Invoke-Git -Arguments @('rev-list', '-n', '1', $tagName)).Output[0].Trim()
        if ($localTaggedCommit -ne $head)
        {
            throw "Local tag '$tagName' points to $localTaggedCommit instead of current HEAD $head."
        }
    }

    if (-not $SkipTests)
    {
        & dotnet test $solutionPath -c Release
        if ($LASTEXITCODE -ne 0)
        {
            throw "Tests failed with exit code $LASTEXITCODE. The release tag was not created."
        }
    }

    if (-not $PSCmdlet.ShouldProcess(
        "$Remote/$tagName at $head",
        'Create and push the release tag that triggers GitHub Actions'))
    {
        return
    }

    if ($localTagCheck.ExitCode -ne 0)
    {
        $null = Invoke-Git -Arguments @('tag', '--annotate', $tagName, $head, '--message', "Hyper-V VM Group Manager $Version")
    }

    $null = Invoke-Git -Arguments @('push', $Remote, "refs/tags/$tagName")

    $remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', $Remote)).Output[0].Trim()
    if ($remoteUrl -match '^https://github\.com/(?<repository>.+?)(?:\.git)?$')
    {
        $repositoryUrl = "https://github.com/$($Matches.repository)"
        Write-Host "Release tag '$tagName' was pushed successfully."
        Write-Host "Workflow: $repositoryUrl/actions"
        Write-Host "Release:  $repositoryUrl/releases/tag/$tagName"
    }
    else
    {
        Write-Host "Release tag '$tagName' was pushed successfully. Monitor the repository's Actions page."
    }
}
finally
{
    Pop-Location
}
