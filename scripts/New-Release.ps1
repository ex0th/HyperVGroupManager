[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateNotNullOrEmpty()]
    [string]$Remote = 'origin',

    [string]$CommitMessage,

    [ValidateSet('Auto', 'ArtifactSigning', 'Pfx')]
    [string]$SigningMode = 'Auto',

    [switch]$SkipTests,

    [ValidateRange(1, 120)]
    [int]$ReleaseTimeoutMinutes = 20,

    [switch]$NoWait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [switch]$AllowFailure
    )

    $stderrPath = [IO.Path]::GetTempFileName()
    $previousErrorActionPreference = $ErrorActionPreference
    $previousWhatIfPreference = $WhatIfPreference
    try
    {
        # Windows PowerShell 5.1 can promote native stderr (including harmless Git warnings)
        # to terminating errors when the caller uses Stop.
        $ErrorActionPreference = 'Continue'
        $WhatIfPreference = $false
        $output = @(& git @Arguments 2> $stderrPath)
        $exitCode = $LASTEXITCODE
        $errorOutput = if ((Get-Item -LiteralPath $stderrPath).Length -gt 0)
        {
            @(Get-Content -LiteralPath $stderrPath)
        }
        else
        {
            @()
        }
    }
    finally
    {
        $ErrorActionPreference = $previousErrorActionPreference
        $WhatIfPreference = $previousWhatIfPreference
        [IO.File]::Delete($stderrPath)
    }
    if ($exitCode -ne 0 -and -not $AllowFailure)
    {
        $details = @($output) + @($errorOutput)
        throw "git $($Arguments -join ' ') failed with exit code $exitCode.`n$($details -join [Environment]::NewLine)"
    }

    [PSCustomObject]@{
        ExitCode = $exitCode
        Output = $output
        ErrorOutput = $errorOutput
    }
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue))
    {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Get-GitHubRepository {
    param([Parameter(Mandatory)][string]$RemoteUrl)

    if ($RemoteUrl -match '^https://github\.com/(?<repository>[^/]+/[^/]+?)(?:\.git)?/?$' -or
        $RemoteUrl -match '^git@github\.com:(?<repository>[^/]+/[^/]+?)(?:\.git)?$' -or
        $RemoteUrl -match '^ssh://git@github\.com/(?<repository>[^/]+/[^/]+?)(?:\.git)?/?$')
    {
        return $Matches.repository
    }

    return $null
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [switch]$AllowNotFound
    )

    $headers = @{
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'HyperVGroupManager-ReleaseScript'
        'X-GitHub-Api-Version' = '2026-03-10'
    }

    $token = if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN))
    {
        $env:GH_TOKEN
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN))
    {
        $env:GITHUB_TOKEN
    }
    else
    {
        $null
    }

    if ($null -ne $token)
    {
        $headers.Authorization = "Bearer $token"
    }

    try
    {
        return Invoke-RestMethod -Method Get -Uri $Uri -Headers $headers
    }
    catch
    {
        $response = $_.Exception.Response
        $statusCode = if ($null -ne $response) { [int]$response.StatusCode } else { 0 }
        if ($AllowNotFound -and $statusCode -eq 404)
        {
            return $null
        }

        if ($statusCode -eq 403 -and $null -eq $token)
        {
            throw "GitHub API access was denied or rate-limited. Set GH_TOKEN and retry the release verification. $($_.Exception.Message)"
        }

        throw
    }
}

function Wait-GitHubRelease {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$TagName,
        [Parameter(Mandatory)][string]$CommitSha,
        [Parameter(Mandatory)][string]$ReleaseVersion,
        [Parameter(Mandatory)][int]$TimeoutMinutes
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($TimeoutMinutes)
    $encodedTag = [Uri]::EscapeDataString($TagName)
    $workflowUri = "https://api.github.com/repos/$Repository/actions/workflows/release.yml/runs?event=push&head_sha=$CommitSha&per_page=10"
    $releaseUri = "https://api.github.com/repos/$Repository/releases/tags/$encodedTag"
    $expectedAssets = @(
        "HyperVGroupManager-$ReleaseVersion-win-x64.zip",
        "HyperVGroupManager-$ReleaseVersion-win-x64.msi",
        "HyperVGroupManager-$ReleaseVersion-CodeSigning.cer",
        "HyperVGroupManager-$ReleaseVersion-SIGNING.txt",
        "HyperVGroupManager-$ReleaseVersion-SHA256SUMS.txt"
    )
    $lastStatus = ''

    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        $workflowResponse = Invoke-GitHubApi -Uri $workflowUri
        $workflowRun = @($workflowResponse.workflow_runs |
            Where-Object { $_.head_sha -eq $CommitSha } |
            Sort-Object -Property created_at -Descending |
            Select-Object -First 1)

        if ($workflowRun.Count -gt 0)
        {
            $run = $workflowRun[0]
            $currentStatus = if ($run.status -eq 'completed')
            {
                "completed/$($run.conclusion)"
            }
            else
            {
                [string]$run.status
            }

            if ($currentStatus -ne $lastStatus)
            {
                Write-Host "GitHub release workflow: $currentStatus"
                $lastStatus = $currentStatus
            }

            if ($run.status -eq 'completed' -and $run.conclusion -ne 'success')
            {
                $failureDetails = @()
                try
                {
                    $jobsResponse = Invoke-GitHubApi -Uri $run.jobs_url
                    foreach ($job in @($jobsResponse.jobs | Where-Object { $_.conclusion -ne 'success' }))
                    {
                        $failedSteps = @($job.steps |
                            Where-Object { $_.conclusion -eq 'failure' } |
                            ForEach-Object { $_.name })
                        if ($failedSteps.Count -gt 0)
                        {
                            $failureDetails += "$($job.name): $($failedSteps -join ', ')"
                        }
                        else
                        {
                            $failureDetails += "$($job.name): $($job.conclusion)"
                        }
                    }
                }
                catch
                {
                    $failureDetails += "Job details unavailable: $($_.Exception.Message)"
                }

                $details = if ($failureDetails.Count -gt 0)
                {
                    "`nFailed job/step: $($failureDetails -join '; ')"
                }
                else
                {
                    ''
                }

                throw "GitHub release workflow finished with '$($run.conclusion)'.$details`n$($run.html_url)"
            }

            if ($run.status -eq 'completed' -and $run.conclusion -eq 'success')
            {
                $release = Invoke-GitHubApi -Uri $releaseUri -AllowNotFound
                if ($null -ne $release -and -not $release.draft)
                {
                    $publishedAssets = @($release.assets | ForEach-Object { $_.name })
                    $missingAssets = @($expectedAssets | Where-Object { $_ -notin $publishedAssets })
                    if ($missingAssets.Count -eq 0)
                    {
                        Write-Host "GitHub release published successfully: $($release.html_url)"
                        Write-Host "Published assets: $($expectedAssets -join ', ')"
                        return
                    }
                }
            }
        }

        Start-Sleep -Seconds 20
    }

    throw "Timed out after $TimeoutMinutes minutes while waiting for GitHub release '$TagName'. Check https://github.com/$Repository/actions"
}

function ConvertFrom-SemanticVersion {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?$')
    {
        throw "Version '$Value' is not a supported semantic version."
    }

    $prerelease = if ($Matches.ContainsKey('prerelease')) { $Matches.prerelease } else { '' }

    [PSCustomObject]@{
        Major = [uint64]$Matches.major
        Minor = [uint64]$Matches.minor
        Patch = [uint64]$Matches.patch
        Prerelease = $prerelease
    }
}

function Compare-SemanticVersion {
    param(
        [Parameter(Mandatory)]$Left,
        [Parameter(Mandatory)]$Right
    )

    foreach ($property in @('Major', 'Minor', 'Patch'))
    {
        if ($Left.$property -lt $Right.$property) { return -1 }
        if ($Left.$property -gt $Right.$property) { return 1 }
    }

    $leftIsStable = [string]::IsNullOrWhiteSpace($Left.Prerelease)
    $rightIsStable = [string]::IsNullOrWhiteSpace($Right.Prerelease)
    if ($leftIsStable -and $rightIsStable) { return 0 }
    if ($leftIsStable) { return 1 }
    if ($rightIsStable) { return -1 }

    $leftParts = @($Left.Prerelease -split '\.')
    $rightParts = @($Right.Prerelease -split '\.')
    $partCount = [Math]::Max($leftParts.Count, $rightParts.Count)
    for ($index = 0; $index -lt $partCount; $index++)
    {
        if ($index -ge $leftParts.Count) { return -1 }
        if ($index -ge $rightParts.Count) { return 1 }

        $leftNumber = 0L
        $rightNumber = 0L
        $leftNumeric = [long]::TryParse($leftParts[$index], [ref]$leftNumber)
        $rightNumeric = [long]::TryParse($rightParts[$index], [ref]$rightNumber)

        if ($leftNumeric -and $rightNumeric)
        {
            if ($leftNumber -lt $rightNumber) { return -1 }
            if ($leftNumber -gt $rightNumber) { return 1 }
            continue
        }

        if ($leftNumeric) { return -1 }
        if ($rightNumeric) { return 1 }

        $comparison = [StringComparer]::Ordinal.Compare($leftParts[$index], $rightParts[$index])
        if ($comparison -lt 0) { return -1 }
        if ($comparison -gt 0) { return 1 }
    }

    return 0
}

function Set-ProjectVersion {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ReleaseVersion,
        [Parameter(Mandatory)][string]$NumericVersion
    )

    $content = [IO.File]::ReadAllText($Path)
    $replacements = @{
        Version = $ReleaseVersion
        AssemblyVersion = "$NumericVersion.0"
        FileVersion = "$NumericVersion.0"
    }

    foreach ($entry in $replacements.GetEnumerator())
    {
        $pattern = "(<$($entry.Key)>)[^<]*(</$($entry.Key)>)"
        $matches = [regex]::Matches($content, $pattern)
        if ($matches.Count -ne 1)
        {
            throw "Expected exactly one <$($entry.Key)> element in $Path, found $($matches.Count)."
        }

        $content = [regex]::Replace(
            $content,
            $pattern,
            { param($match) "$($match.Groups[1].Value)$($entry.Value)$($match.Groups[2].Value)" })
    }

    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

Assert-CommandAvailable 'git'
Assert-CommandAvailable 'dotnet'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\HyperVGroupManager.App\HyperVGroupManager.App.csproj'
$solutionPath = Join-Path $repositoryRoot 'HyperVGroupManager.sln'
$installerBuildScript = Join-Path $repositoryRoot 'scripts\Build-Installer.ps1'

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

    $branch = (Invoke-Git -Arguments @('branch', '--show-current')).Output[0].Trim()
    if ([string]::IsNullOrWhiteSpace($branch))
    {
        throw 'Releases cannot be created from a detached HEAD. Check out the release branch first.'
    }

    $conflictedFiles = (Invoke-Git -Arguments @('diff', '--name-only', '--diff-filter=U')).Output
    if ($conflictedFiles.Count -gt 0)
    {
        throw "Resolve all merge conflicts before creating a release.`n$($conflictedFiles -join [Environment]::NewLine)"
    }

    foreach ($operationRef in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'REBASE_HEAD'))
    {
        $operation = Invoke-Git -Arguments @('rev-parse', '--verify', '--quiet', $operationRef) -AllowFailure
        if ($operation.ExitCode -eq 0)
        {
            throw "Git operation '$operationRef' is still active. Finish or abort it before creating a release."
        }
    }

    $untrackedFiles = (Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard')).Output
    $sensitiveFiles = @($untrackedFiles | Where-Object {
        $_ -match '(?i)(^|/)(\.env(?:\..*)?|id_rsa|id_ed25519|[^/]+\.(?:pfx|p12|pem|key))$'
    })
    if ($sensitiveFiles.Count -gt 0)
    {
        throw "Potentially sensitive untracked files will not be committed automatically:`n$($sensitiveFiles -join [Environment]::NewLine)"
    }

    $null = Invoke-Git -Arguments @('fetch', '--tags', $Remote)
    $remoteUrl = (Invoke-Git -Arguments @('remote', 'get-url', $Remote)).Output[0].Trim()
    $githubRepository = Get-GitHubRepository -RemoteUrl $remoteUrl
    if (-not $NoWait -and [string]::IsNullOrWhiteSpace($githubRepository))
    {
        throw "Remote '$Remote' is not a recognized GitHub URL. Use -NoWait to push without GitHub release verification."
    }

    [xml]$project = Get-Content -Raw -LiteralPath $projectPath
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode)
    {
        throw "No <Version> element was found in $projectPath."
    }

    $currentVersion = $versionNode.InnerText.Trim()
    $currentSemVer = ConvertFrom-SemanticVersion -Value $currentVersion

    if ([string]::IsNullOrWhiteSpace($Version))
    {
        $currentVersionTag = Invoke-Git -Arguments @(
            'show-ref', '--verify', '--quiet', "refs/tags/v$currentVersion"
        ) -AllowFailure

        if ($currentVersionTag.ExitCode -ne 0)
        {
            # The project already contains an unreleased version (for example after feature work).
            $Version = $currentVersion
        }
        elseif (-not [string]::IsNullOrWhiteSpace($currentSemVer.Prerelease))
        {
            $Version = "$($currentSemVer.Major).$($currentSemVer.Minor).$($currentSemVer.Patch)"
        }
        else
        {
            if ($currentSemVer.Patch -ge 65535)
            {
                throw "Cannot automatically increment '$currentVersion': the MSI patch limit is 65535. Specify the next version explicitly."
            }

            $Version = "$($currentSemVer.Major).$($currentSemVer.Minor).$($currentSemVer.Patch + 1)"
        }
    }

    $targetSemVer = ConvertFrom-SemanticVersion -Value $Version
    if ((Compare-SemanticVersion -Left $targetSemVer -Right $currentSemVer) -lt 0)
    {
        throw "Target version '$Version' is older than current project version '$currentVersion'."
    }

    if ($targetSemVer.Major -gt 255 -or $targetSemVer.Minor -gt 255 -or $targetSemVer.Patch -gt 65535)
    {
        throw "Version '$Version' exceeds Windows Installer limits (255.255.65535)."
    }

    $numericVersion = "$($targetSemVer.Major).$($targetSemVer.Minor).$($targetSemVer.Patch)"
    $tagName = "v$Version"
    if ([string]::IsNullOrWhiteSpace($CommitMessage))
    {
        $CommitMessage = "chore: release $tagName"
    }

    $remoteTag = (Invoke-Git -Arguments @(
        'ls-remote', '--tags', $Remote, "refs/tags/$tagName", "refs/tags/$tagName^{}"
    )).Output
    if ($remoteTag.Count -gt 0)
    {
        throw "Tag '$tagName' already exists on remote '$Remote'. No release action was taken."
    }

    $remoteBranch = (Invoke-Git -Arguments @('ls-remote', '--heads', $Remote, "refs/heads/$branch")).Output
    if ($remoteBranch.Count -gt 0)
    {
        $remoteHead = ($remoteBranch[0] -split '\s+')[0]
        $ancestorCheck = Invoke-Git -Arguments @('merge-base', '--is-ancestor', $remoteHead, 'HEAD') -AllowFailure
        if ($ancestorCheck.ExitCode -ne 0)
        {
            throw "Remote branch '$Remote/$branch' contains commits that are not in the local branch. Pull or rebase before releasing."
        }
    }

    if (-not $PSCmdlet.ShouldProcess(
        "$Remote/$branch and $Remote/$tagName",
        "Update to $Version, test, commit all changes, push the branch, and publish the release with signing mode $SigningMode"))
    {
        return
    }

    if ($currentVersion -ne $Version)
    {
        Set-ProjectVersion -Path $projectPath -ReleaseVersion $Version -NumericVersion $numericVersion
        Write-Host "Version updated: $currentVersion -> $Version"
    }

    if (-not $SkipTests)
    {
        & dotnet test $solutionPath -c Release
        if ($LASTEXITCODE -ne 0)
        {
            throw "Tests failed with exit code $LASTEXITCODE. No commit or tag was created."
        }
    }

    & $installerBuildScript -SkipTests
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installer validation failed with exit code $LASTEXITCODE. No commit or tag was created."
    }

    $workingTreeCheck = Invoke-Git -Arguments @('diff', '--check') -AllowFailure
    if ($workingTreeCheck.ExitCode -ne 0)
    {
        throw "Whitespace validation failed. No commit or tag was created.`n$($workingTreeCheck.Output -join [Environment]::NewLine)"
    }

    $null = Invoke-Git -Arguments @('add', '--all')
    $stagedCheck = Invoke-Git -Arguments @('diff', '--cached', '--check') -AllowFailure
    if ($stagedCheck.ExitCode -ne 0)
    {
        throw "Staged content validation failed. No commit or tag was created.`n$($stagedCheck.Output -join [Environment]::NewLine)"
    }

    $stagedChanges = Invoke-Git -Arguments @('diff', '--cached', '--quiet') -AllowFailure
    if ($stagedChanges.ExitCode -eq 1)
    {
        $null = Invoke-Git -Arguments @('commit', '--message', $CommitMessage)
    }
    elseif ($stagedChanges.ExitCode -ne 0)
    {
        throw 'Unable to determine whether staged changes exist.'
    }
    else
    {
        Write-Host 'No file changes to commit; the current commit will be released.'
    }

    $head = (Invoke-Git -Arguments @('rev-parse', 'HEAD')).Output[0].Trim()
    $null = Invoke-Git -Arguments @('push', $Remote, "HEAD:refs/heads/$branch")

    $publishedBranch = (Invoke-Git -Arguments @('ls-remote', '--heads', $Remote, "refs/heads/$branch")).Output
    if ($publishedBranch.Count -eq 0 -or ($publishedBranch[0] -split '\s+')[0] -ne $head)
    {
        throw "The release commit $head could not be verified on '$Remote/$branch'. No tag was pushed."
    }

    $localTagCheck = Invoke-Git -Arguments @('show-ref', '--verify', '--quiet', "refs/tags/$tagName") -AllowFailure
    if ($localTagCheck.ExitCode -eq 0)
    {
        $localTaggedCommit = (Invoke-Git -Arguments @('rev-list', '-n', '1', $tagName)).Output[0].Trim()
        if ($localTaggedCommit -ne $head)
        {
            throw "Local tag '$tagName' points to $localTaggedCommit instead of release commit $head."
        }
    }
    else
    {
        $tagMessage = "Hyper-V VM Group Manager $Version`n`nSigning-Mode: $SigningMode"
        $null = Invoke-Git -Arguments @('tag', '--annotate', $tagName, $head, '--message', $tagMessage)
    }

    $null = Invoke-Git -Arguments @('push', $Remote, "refs/tags/$tagName")

    if (-not [string]::IsNullOrWhiteSpace($githubRepository))
    {
        $repositoryUrl = "https://github.com/$githubRepository"
        Write-Host "Release commit and tag '$tagName' were pushed successfully."
        Write-Host "Workflow: $repositoryUrl/actions"
        Write-Host "Release:  $repositoryUrl/releases/tag/$tagName"

        if ($NoWait)
        {
            Write-Host 'The GitHub workflow is creating the ZIP, MSI, checksums, and release.'
        }
        else
        {
            Write-Host 'Waiting for GitHub to publish the portable ZIP, MSI, and checksums ...'
            Wait-GitHubRelease `
                -Repository $githubRepository `
                -TagName $tagName `
                -CommitSha $head `
                -ReleaseVersion $Version `
                -TimeoutMinutes $ReleaseTimeoutMinutes
        }
    }
    else
    {
        Write-Host "Release commit and tag '$tagName' were pushed successfully. Monitor the repository's Actions page."
    }
}
finally
{
    Pop-Location
}
