[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+([\.-][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
$installerProject = Join-Path $repositoryRoot 'installer\MusicMic.Installer.wixproj'
$bundleProject = Join-Path $repositoryRoot 'installer\MusicMic.Bundle.wixproj'
$installerOutput = Join-Path $repositoryRoot 'installer\output'

& (Join-Path $PSScriptRoot 'Publish-WinX64.ps1') -Configuration $Configuration -Version $Version -PublishDirectory $publishDirectory -SkipTests:$SkipTests
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; installer build was not started.' }

& dotnet build $installerProject -c $Configuration -p:Platform=x64 --nologo "-p:ProductVersion=$Version" "-p:PublishDir=$publishDirectory" '-p:SuppressValidation=true'
if ($LASTEXITCODE -ne 0) { throw "WiX installer build failed with exit code $LASTEXITCODE." }

$msi = Get-ChildItem -LiteralPath $installerOutput -Recurse -Filter 'MusicMic.msi' -File | Select-Object -First 1
if ($null -eq $msi) { throw "WiX build completed but MusicMic.msi was not found below $installerOutput" }

& dotnet build $bundleProject -c $Configuration -p:Platform=x64 --nologo "-p:ProductVersion=$Version" "-p:MsiPath=$($msi.FullName)"
if ($LASTEXITCODE -ne 0) { throw "WiX bootstrapper build failed with exit code $LASTEXITCODE." }

$bundle = Get-ChildItem -LiteralPath $installerOutput -Recurse -Filter 'MusicMicSetup.exe' -File | Select-Object -First 1
if ($null -eq $bundle) { throw "WiX bootstrapper build completed but MusicMicSetup.exe was not found below $installerOutput" }

& (Join-Path $PSScriptRoot 'Test-Package.ps1') -PublishDirectory $publishDirectory -MsiPath $msi.FullName -BundlePath $bundle.FullName
if ($LASTEXITCODE -ne 0) { throw 'Installer smoke test failed.' }

Write-Host "Installer created: $($msi.FullName)"
Write-Host "Setup bootstrapper created: $($bundle.FullName)"
