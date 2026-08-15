[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
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
$vbCableSourceDirectory = Join-Path $repositoryRoot 'installer\third-party\vb-cable'
$vbCableManifestPath = Join-Path $vbCableSourceDirectory 'manifest.json'
$vbCableBuildDirectory = Join-Path $repositoryRoot 'artifacts\third-party\vb-cable'
$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()

if (-not (Test-Path -LiteralPath $vbCableManifestPath -PathType Leaf)) {
    throw "VB-CABLE manifest is missing: $vbCableManifestPath"
}

$vbCableManifest = Get-Content -Raw -LiteralPath $vbCableManifestPath | ConvertFrom-Json
$vbCableArchive = Join-Path $vbCableSourceDirectory $vbCableManifest.archive
if (-not (Test-Path -LiteralPath $vbCableArchive -PathType Leaf)) {
    throw "VB-CABLE archive is missing: $vbCableArchive"
}

$vbCableHash = (Get-FileHash -LiteralPath $vbCableArchive -Algorithm SHA256).Hash
if ($vbCableHash -ne $vbCableManifest.sha256) {
    throw 'VB-CABLE archive SHA-256 does not match the pinned manifest.'
}

Expand-Archive -LiteralPath $vbCableArchive -DestinationPath $vbCableBuildDirectory -Force
$vbCableSetup = Join-Path $vbCableBuildDirectory $vbCableManifest.setupExecutable
if (-not (Test-Path -LiteralPath $vbCableSetup -PathType Leaf)) {
    throw "VB-CABLE setup executable is missing from the official archive: $vbCableSetup"
}

$vbCableSignature = Get-AuthenticodeSignature -LiteralPath $vbCableSetup
if ($vbCableSignature.Status -ne 'Valid' -or $vbCableSignature.SignerCertificate.Subject -notmatch 'BUREL VINCENT') {
    throw 'VB-CABLE setup executable does not have the expected valid VB-Audio signature.'
}

& (Join-Path $PSScriptRoot 'Publish-WinX64.ps1') -Configuration $Configuration -Version $Version -PublishDirectory $publishDirectory -SkipTests:$SkipTests
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; installer build was not started.' }

foreach ($staleOutput in @('MusicMic.msi', 'MusicMic.wixpdb', 'MusicMicSetup.exe', 'MusicMicSetup.wixpdb')) {
    $stalePath = Join-Path $installerOutput $staleOutput
    if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}

& dotnet build $installerProject -c $Configuration -p:Platform=x64 --nologo "-p:ProductVersion=$Version" "-p:PublishDir=$publishDirectory" '-p:SuppressValidation=true'
if ($LASTEXITCODE -ne 0) { throw "WiX installer build failed with exit code $LASTEXITCODE." }

$msi = Get-Item -LiteralPath (Join-Path $installerOutput 'MusicMic.msi') -ErrorAction SilentlyContinue
if ($null -eq $msi) { throw "WiX build completed but MusicMic.msi was not found below $installerOutput" }

& dotnet build $bundleProject -c $Configuration -p:Platform=x64 --nologo "-p:ProductVersion=$Version" "-p:MsiPath=$($msi.FullName)" "-p:VbCableSetupPath=$vbCableSetup" "-p:VbCablePayloadDirectory=$vbCableBuildDirectory"
if ($LASTEXITCODE -ne 0) { throw "WiX bootstrapper build failed with exit code $LASTEXITCODE." }

$bundle = Get-Item -LiteralPath (Join-Path $installerOutput 'MusicMicSetup.exe') -ErrorAction SilentlyContinue
if ($null -eq $bundle) { throw "WiX bootstrapper build completed but MusicMicSetup.exe was not found below $installerOutput" }

& (Join-Path $PSScriptRoot 'Test-Package.ps1') -PublishDirectory $publishDirectory -MsiPath $msi.FullName -BundlePath $bundle.FullName -VbCableSetupPath $vbCableSetup -VbCableNoticePath (Join-Path $repositoryRoot 'installer\VBCABLE-Notice.rtf') -ExpectedVersion $Version -ExpectedSourceRevision $sourceRevision
if ($LASTEXITCODE -ne 0) { throw 'Installer smoke test failed.' }

Write-Host "Installer created: $($msi.FullName)"
Write-Host "Setup bootstrapper created: $($bundle.FullName)"
