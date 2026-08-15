[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$MsiPath,
    [string]$BundlePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) 'artifacts\publish\win-x64'
}

$publishPath = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "Publish directory does not exist: $publishPath"
}

$requiredFiles = @('MusicMic.exe', 'MusicMic.deps.json', 'MusicMic.runtimeconfig.json', 'MusicMic.Audio.dll')
foreach ($requiredFile in $requiredFiles) {
    $path = Join-Path $publishPath $requiredFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Package smoke test failed: required published file is missing: $requiredFile"
    }

    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Package smoke test failed: required published file is empty: $requiredFile"
    }
}

$nativeDll = Join-Path $publishPath 'MusicMic.Audio.dll'
$nativeHeader = [IO.File]::ReadAllBytes($nativeDll)
if ($nativeHeader.Length -lt 64 -or $nativeHeader[0] -ne 0x4D -or $nativeHeader[1] -ne 0x5A) {
    throw 'Package smoke test failed: MusicMic.Audio.dll is not a valid Windows PE image.'
}

$peOffset = [BitConverter]::ToInt32($nativeHeader, 0x3C)
if ($peOffset -lt 0 -or ($peOffset + 6) -gt $nativeHeader.Length -or $nativeHeader[$peOffset] -ne 0x50 -or $nativeHeader[$peOffset + 1] -ne 0x45) {
    throw 'Package smoke test failed: MusicMic.Audio.dll has an invalid PE header.'
}

$machine = [BitConverter]::ToUInt16($nativeHeader, $peOffset + 4)
if ($machine -ne 0x8664) {
    throw ('Package smoke test failed: MusicMic.Audio.dll is not x64 (PE machine 0x{0:X4}).' -f $machine)
}

if ($MsiPath) {
    $resolvedMsi = [IO.Path]::GetFullPath($MsiPath)
    if (-not (Test-Path -LiteralPath $resolvedMsi -PathType Leaf)) {
        throw "Installer smoke test failed: MSI does not exist: $resolvedMsi"
    }

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($resolvedMsi, 0)
    $view = $database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`=?")

    function Get-MsiProperty {
        param([string]$Name)

        $record = $installer.CreateRecord(1)
        $record.StringData(1) = $Name
        $null = $view.Execute($record)
        $result = $view.Fetch()
        $view.Close()
        if ($null -eq $result) { return $null }
        return $result.StringData(1).Trim()
    }

    $expectedProperties = @{
        ProductName = 'MusicMic'
        Manufacturer = 'MusicMic'
        ARPNOMODIFY = '1'
        ARPNOREPAIR = '1'
    }

    foreach ($entry in $expectedProperties.GetEnumerator()) {
        $actual = Get-MsiProperty -Name $entry.Key
        if ($actual -ne $entry.Value) {
            throw "Installer smoke test failed: MSI property $($entry.Key) is '$actual', expected '$($entry.Value)'."
        }
    }

    $shortcutView = $database.OpenView("SELECT `Name`, `Target` FROM `Shortcut` WHERE `Shortcut`='MusicMicStartMenuShortcut'")
    $shortcutView.Execute()
    $shortcut = $shortcutView.Fetch()
    $shortcutView.Close()
    if ($null -eq $shortcut -or $shortcut.StringData(1) -ne 'MusicMic' -or [string]::IsNullOrWhiteSpace($shortcut.StringData(2))) {
        throw 'Installer smoke test failed: MSI does not provide the MusicMic Start menu shortcut.'
    }
}

if ($BundlePath) {
    $resolvedBundle = [IO.Path]::GetFullPath($BundlePath)
    if (-not (Test-Path -LiteralPath $resolvedBundle -PathType Leaf)) {
        throw "Installer smoke test failed: bootstrapper executable does not exist: $resolvedBundle"
    }

    if ((Get-Item -LiteralPath $resolvedBundle).Length -eq 0) {
        throw "Installer smoke test failed: bootstrapper executable is empty: $resolvedBundle"
    }

    $bundleHeader = [IO.File]::ReadAllBytes($resolvedBundle)
    if ($bundleHeader.Length -lt 64 -or $bundleHeader[0] -ne 0x4D -or $bundleHeader[1] -ne 0x5A) {
        throw 'Installer smoke test failed: bootstrapper executable is not a valid Windows PE image.'
    }
}

Write-Host "Package smoke test passed: $publishPath"
