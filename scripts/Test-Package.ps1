[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64'),
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
        $view.Execute($record)
        $result = $view.Fetch()
        $view.Close()
        if ($null -eq $result) { return $null }
        return $result.StringData(1)
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
}

Write-Host "Package smoke test passed: $publishPath"
