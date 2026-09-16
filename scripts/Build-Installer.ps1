[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipTests,
    [string]$CertificatePath,
    [string]$CertificateThumbprint,
    [ValidatePattern('^https?://')]
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-SafeStagingPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedParent
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedParent = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean staging path outside '$resolvedParent': $resolvedPath"
    }
}

if ($null -eq (Get-Command 'dotnet' -ErrorAction SilentlyContinue))
{
    throw "Required command 'dotnet' was not found in PATH."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\HyperVGroupManager.App\HyperVGroupManager.App.csproj'
$installerProjectPath = Join-Path $repositoryRoot 'installer\HyperVGroupManager.Installer.wixproj'
$solutionPath = Join-Path $repositoryRoot 'HyperVGroupManager.sln'
$signingScriptPath = Join-Path $repositoryRoot 'scripts\Sign-Release.ps1'

if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and
    -not [string]::IsNullOrWhiteSpace($CertificateThumbprint))
{
    throw 'Specify either -CertificatePath or -CertificateThumbprint, not both.'
}

$signingRequested = -not [string]::IsNullOrWhiteSpace($CertificatePath) -or
    -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)

if ([string]::IsNullOrWhiteSpace($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\release'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory))
{
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}

[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode)
{
    throw "No <Version> element was found in $projectPath."
}

$version = $versionNode.InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')
{
    throw "Project version '$version' is not a supported semantic version."
}

$msiProductVersion = ($version -split '-', 2)[0]
$versionParts = @($msiProductVersion -split '\.' | ForEach-Object { [int]$_ })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535)
{
    throw "Version '$msiProductVersion' exceeds Windows Installer limits (255.255.65535)."
}

$stagingRoot = Join-Path $repositoryRoot 'artifacts\.installer-staging'
$stagingDirectory = Join-Path $stagingRoot ([guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $stagingDirectory 'publish'
$installerOutputDirectory = Join-Path $stagingDirectory 'installer'

New-Item -ItemType Directory -Path $publishDirectory, $installerOutputDirectory, $OutputDirectory -Force | Out-Null

try
{
    if (-not $SkipTests)
    {
        Invoke-DotNet -Arguments @('test', $solutionPath, '-c', 'Release')
    }

    Invoke-DotNet -Arguments @(
        'publish', $projectPath,
        '-c', 'Release',
        '-o', $publishDirectory
    )

    if ($signingRequested)
    {
        $signingArguments = @{
            Path = Join-Path $publishDirectory 'HyperVGroupManager.App.exe'
            TimestampUrl = $TimestampUrl
        }
        if (-not [string]::IsNullOrWhiteSpace($CertificatePath))
        {
            $signingArguments.CertificatePath = $CertificatePath
        }
        else
        {
            $signingArguments.CertificateThumbprint = $CertificateThumbprint
        }

        & $signingScriptPath @signingArguments
    }

    Invoke-DotNet -Arguments @('restore', $installerProjectPath)
    Invoke-DotNet -Arguments @(
        'build', $installerProjectPath,
        '-c', 'Release',
        '--no-restore',
        "-p:ProductVersion=$msiProductVersion",
        "-p:PublishDir=$publishDirectory",
        "-p:OutputPath=$installerOutputDirectory"
    )

    $installerFiles = @(Get-ChildItem -LiteralPath $installerOutputDirectory -Filter '*.msi' -Recurse -File)
    if ($installerFiles.Count -ne 1)
    {
        throw "Expected exactly one MSI output, found $($installerFiles.Count)."
    }

    $destinationPath = Join-Path $OutputDirectory "HyperVGroupManager-$version-win-x64.msi"
    Copy-Item -LiteralPath $installerFiles[0].FullName -Destination $destinationPath -Force

    if ($signingRequested)
    {
        $signingArguments.Path = $destinationPath
        & $signingScriptPath @signingArguments
    }

    $checksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationPath).Hash.ToLowerInvariant()
    Write-Host "Installer created: $destinationPath"
    Write-Host "SHA-256: $checksum"
}
finally
{
    if (Test-Path -LiteralPath $stagingDirectory)
    {
        Assert-SafeStagingPath -Path $stagingDirectory -ExpectedParent $stagingRoot
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
