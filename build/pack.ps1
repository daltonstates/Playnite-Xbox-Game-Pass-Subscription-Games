[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ToolboxPath,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'SubscriptionLibraries.sln'
$buildOutput = Join-Path $repositoryRoot "SubscriptionLibraries\bin\$Configuration\net462"
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$stagingDirectory = Join-Path $artifactsDirectory 'SubscriptionLibraries'

if (-not $SkipBuild) {
    & dotnet build $solutionPath --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "The solution build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $buildOutput -PathType Container)) {
    throw "Build output does not exist: $buildOutput"
}

$artifactsFullPath = [IO.Path]::GetFullPath($artifactsDirectory)
$stagingFullPath = [IO.Path]::GetFullPath($stagingDirectory)
if (-not $stagingFullPath.StartsWith(
        $artifactsFullPath + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a staging directory outside the repository artifacts directory."
}

if (Test-Path -LiteralPath $stagingFullPath) {
    Remove-Item -LiteralPath $stagingFullPath -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingFullPath -Force | Out-Null

$packageFiles = @(
    'extension.yaml',
    'SubscriptionLibraries.dll',
    'SubscriptionLibraries.Core.dll'
)

foreach ($fileName in $packageFiles) {
    $sourcePath = Join-Path $buildOutput $fileName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required package file is missing: $sourcePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination $stagingFullPath
}

if ([string]::IsNullOrWhiteSpace($ToolboxPath)) {
    $toolboxCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Playnite\Toolbox.exe'),
        (Join-Path $env:ProgramFiles 'Playnite\Toolbox.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Playnite\Toolbox.exe')
    )
    $ToolboxPath = $toolboxCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($ToolboxPath) -or
    -not (Test-Path -LiteralPath $ToolboxPath -PathType Leaf)) {
    throw 'Playnite Toolbox.exe was not found. Pass -ToolboxPath with the path from a current Playnite installation.'
}

$resolvedToolboxPath = (Resolve-Path -LiteralPath $ToolboxPath).Path
New-Item -ItemType Directory -Path $artifactsFullPath -Force | Out-Null
& $resolvedToolboxPath pack $stagingFullPath $artifactsFullPath
if ($LASTEXITCODE -ne 0) {
    throw "Playnite Toolbox packaging failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem -LiteralPath $artifactsFullPath -Filter '*.pext' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $package) {
    throw 'Playnite Toolbox completed without producing a .pext package.'
}

Write-Host "Package created: $($package.FullName)"
