[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
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
$relativePublishPath = [IO.Path]::GetRelativePath($repositoryRoot, $publishPath)
if ($relativePublishPath.StartsWith('..') -or [IO.Path]::IsPathRooted($relativePublishPath)) {
    throw "Publish directory must stay inside the repository: $publishPath"
}

$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceRevision)) {
    throw 'Unable to determine the Git source revision for release provenance.'
}
$sourceChanges = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to verify the Git worktree before publishing.'
}
if ($sourceChanges.Count -ne 0) {
    throw 'Release publishing requires a clean Git worktree so the embedded source revision identifies the exact packaged source.'
}

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

if (Test-Path -LiteralPath $publishPath -PathType Container) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

Invoke-CheckedCommand dotnet @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:Platform=x64',
    "-p:NativeAudioLibraryPath=$nativeDll",
    "-p:Version=$Version",
    "-p:SourceRevisionId=$sourceRevision",
    '-p:ContinuousIntegrationBuild=true',
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:UseSharedCompilation=false',
    '-m:1',
    '-o', $publishPath,
    '--nologo'
)

$releaseFiles = [ordered]@{}
foreach ($binaryName in @('MusicMic.exe', 'MusicMic.dll', 'MusicMic.Audio.dll')) {
    $binaryPath = Join-Path $publishPath $binaryName
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
        throw "Published release binary is missing: $binaryName"
    }
    $releaseFiles[$binaryName] = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash
}
[ordered]@{
    version = $Version
    sourceRevision = $sourceRevision
    configuration = $Configuration
    files = $releaseFiles
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $publishPath 'release-manifest.json') -Encoding utf8NoBOM

& (Join-Path $PSScriptRoot 'Test-Package.ps1') -PublishDirectory $publishPath -ExpectedVersion $Version -ExpectedSourceRevision $sourceRevision

Write-Host "Published MusicMic $Version to $publishPath"
