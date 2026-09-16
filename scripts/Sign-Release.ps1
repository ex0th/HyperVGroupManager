[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$Path,

    [string]$CertificatePath,

    [string]$CertificateThumbprint,

    [ValidatePattern('^https?://')]
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-SignTool {
    $command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command)
    {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot)
    {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Sort-Object -Property Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($candidate))
        {
            return $candidate
        }
    }

    throw 'SignTool.exe was not found. Install the Windows SDK or add SignTool to PATH.'
}

if ([string]::IsNullOrWhiteSpace($CertificatePath) -eq [string]::IsNullOrWhiteSpace($CertificateThumbprint))
{
    throw 'Specify exactly one signing identity: -CertificatePath or -CertificateThumbprint.'
}

$resolvedCertificatePath = $null
if (-not [string]::IsNullOrWhiteSpace($CertificatePath))
{
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf))
    {
        throw "The signing certificate file does not exist: $CertificatePath"
    }

    $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
}

$resolvedPaths = foreach ($item in $Path)
{
    if (-not (Test-Path -LiteralPath $item -PathType Leaf))
    {
        throw "The release file to sign does not exist: $item"
    }

    (Resolve-Path -LiteralPath $item).Path
}

$signTool = Find-SignTool
foreach ($resolvedPath in $resolvedPaths)
{
    $signArguments = @(
        'sign',
        '/fd', 'SHA256',
        '/tr', $TimestampUrl,
        '/td', 'SHA256',
        '/d', 'Hyper-V VM Group Manager'
    )

    if ($null -ne $resolvedCertificatePath)
    {
        $signArguments += @('/f', $resolvedCertificatePath)
        if (-not [string]::IsNullOrEmpty($env:CODE_SIGNING_CERTIFICATE_PASSWORD))
        {
            $signArguments += @('/p', $env:CODE_SIGNING_CERTIFICATE_PASSWORD)
        }
    }
    else
    {
        $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Trim()
        if ($normalizedThumbprint -notmatch '^[0-9A-Fa-f]{40}$')
        {
            throw 'The certificate thumbprint is not valid.'
        }

        $signArguments += @('/sha1', $normalizedThumbprint)
    }

    $signArguments += $resolvedPath
    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "Authenticode signing failed for '$resolvedPath' with exit code $LASTEXITCODE."
    }

    & $signTool verify /pa /all /v $resolvedPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Authenticode verification failed for '$resolvedPath' with exit code $LASTEXITCODE."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $resolvedPath
    if ($signature.Status -ne 'Valid')
    {
        throw "Authenticode verification returned '$($signature.Status)' for '$resolvedPath'."
    }

    Write-Host "Signed and verified: $resolvedPath"
}
