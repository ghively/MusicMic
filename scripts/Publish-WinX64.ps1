[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+([\.-][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path $repositoryRoot 'src\MusicMic.App\MusicMic.App.csproj'
$nativeSource = Join-Path $repositoryRoot 'src\MusicMic.Audio'
$nativeBuild = Join-Path $repositoryRoot "artifacts\native\win-x64\$Configuration"
$publishPath = [IO.Path]::GetFullPath($PublishDirectory)

function Invoke-CheckedCommand {
    param([string]$FilePath, [string[]]$Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE: $FilePath $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "MusicMic application project was not found: $appProject"
}

if (-not $SkipTests) {
    Invoke-CheckedCommand dotnet @('test', (Join-Path $repositoryRoot 'MusicMic.sln'), '-c', $Configuration, '-p:Platform=x64', '--nologo')
}

Invoke-CheckedCommand cmake @('-S', $nativeSource, '-B', $nativeBuild, '-A', 'x64', '-DBUILD_TESTING=OFF')
Invoke-CheckedCommand cmake @('--build', $nativeBuild, '--config', $Configuration, '--target', 'MusicMic.Audio')

Invoke-CheckedCommand dotnet @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:Platform=x64',
    "-p:Version=$Version",
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $publishPath,
    '--nologo'
)

$nativeDll = Join-Path $nativeBuild "$Configuration\MusicMic.Audio.dll"
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    throw "Native build did not produce MusicMic.Audio.dll at the expected x64 path: $nativeDll"
}

Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $publishPath 'MusicMic.Audio.dll') -Force
& (Join-Path $PSScriptRoot 'Test-Package.ps1') -PublishDirectory $publishPath

Write-Host "Published MusicMic $Version to $publishPath"
