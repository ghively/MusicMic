[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+([\.-][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [string]$PublishDirectory,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path $repositoryRoot 'src\MusicMic.App\MusicMic.App.csproj'
$nativeSource = Join-Path $repositoryRoot 'src\MusicMic.Audio'
$nativeBuild = Join-Path $repositoryRoot "artifacts\native\win-x64\$Configuration"
$buildTesting = if ($SkipTests) { 'OFF' } else { 'ON' }
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
}
$publishPath = [IO.Path]::GetFullPath($PublishDirectory)

function Invoke-CheckedCommand {
    param([string]$FilePath, [string[]]$Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "MusicMic application project was not found: $appProject"
}

# Some shell hosts provide both Path and PATH. MSBuild treats these as duplicate
# environment keys when it launches cl.exe, so normalize the current script
# process before entering the Visual Studio developer environment.
$processPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
[Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $processPath, 'Process')

$cmakePath = (Get-Command cmake -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
if ([string]::IsNullOrWhiteSpace($cmakePath)) {
    $bundledCmake = Join-Path ${env:ProgramFiles} 'CMake\bin\cmake.exe'
    if (Test-Path -LiteralPath $bundledCmake -PathType Leaf) {
        $cmakePath = $bundledCmake
    }
}
if ([string]::IsNullOrWhiteSpace($cmakePath)) {
    throw 'CMake was not found. Install CMake 3.25 or newer, or add it to PATH.'
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsInstallPath = if (Test-Path -LiteralPath $vswherePath -PathType Leaf) {
    & $vswherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if ([string]::IsNullOrWhiteSpace($vsInstallPath)) {
    throw 'Visual Studio Build Tools with the MSVC x64 compiler were not found.'
}

$vsDevCmd = Join-Path $vsInstallPath 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $vsDevCmd -PathType Leaf)) {
    throw "Visual Studio developer command prompt was not found: $vsDevCmd"
}

function Invoke-NativeCmake {
    param([string[]]$Arguments)

    $quotedArguments = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    & cmd.exe /d /s /c "call `"$vsDevCmd`" -arch=x64 -host_arch=x64 >nul && `"$cmakePath`" $quotedArguments"
    if ($LASTEXITCODE -ne 0) {
        throw "Native CMake command failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

Invoke-NativeCmake @('-S', $nativeSource, '-B', $nativeBuild, '-A', 'x64', "-DBUILD_TESTING=$buildTesting")
Invoke-NativeCmake @('--build', $nativeBuild, '--config', $Configuration, '--target', 'MusicMic.Audio')

$nativeDll = Join-Path $nativeBuild "$Configuration\MusicMic.Audio.dll"
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) {
    throw "Native build did not produce MusicMic.Audio.dll at the expected x64 path: $nativeDll"
}

if (-not $SkipTests) {
    Invoke-NativeCmake @('--build', $nativeBuild, '--config', $Configuration, '--target', 'MusicMic.Audio.Tests')
    $ctestPath = Join-Path (Split-Path -Parent $cmakePath) 'ctest.exe'
    if (-not (Test-Path -LiteralPath $ctestPath -PathType Leaf)) {
        throw "CTest was not found beside CMake: $ctestPath"
    }
    Invoke-CheckedCommand $ctestPath @('--test-dir', $nativeBuild, '-C', $Configuration, '--output-on-failure')

    Invoke-CheckedCommand dotnet @(
        'test', (Join-Path $repositoryRoot 'MusicMic.sln'),
        '-c', $Configuration,
        '-p:Platform=x64',
        "-p:NativeAudioLibraryPath=$nativeDll",
        '-p:RestoreBuildInParallel=false',
        '-p:UseSharedCompilation=false',
        '-m:1',
        '--nologo'
    )
}

Invoke-CheckedCommand dotnet @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:Platform=x64',
    "-p:NativeAudioLibraryPath=$nativeDll",
    "-p:Version=$Version",
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:UseSharedCompilation=false',
    '-m:1',
    '-o', $publishPath,
    '--nologo'
)

& (Join-Path $PSScriptRoot 'Test-Package.ps1') -PublishDirectory $publishPath

Write-Host "Published MusicMic $Version to $publishPath"
