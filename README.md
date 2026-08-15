# MusicMic

MusicMic is a compact Windows 11 utility that copies one selected application's rendered audio into a microphone feed while leaving the application's normal playback untouched. It mixes that copy with one physical microphone and writes the result to the VB-CABLE virtual input included by its setup program.

## Non-negotiable routing rule

Starting or stopping MusicMic must never change an application's playback endpoint, session volume, mute state, playback state, or the Windows default playback endpoint. The native engine captures a parallel, process-specific loopback copy.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK
- Visual Studio 2022 with Desktop development with C++ for later native/WPF phases
- Administrator approval and a restart when the bundled VB-CABLE driver is first installed

## Build and test

```powershell
# Builds the native x64 engine, restores the win-x64 runtime when required,
# runs the managed tests, publishes the app, and validates the publish layout.
powershell -ExecutionPolicy Bypass -File scripts\Publish-WinX64.ps1 -Configuration Release -Version 1.0.0

# Runs the publish flow above, builds the MSI, and checks its required metadata.
powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1 -Configuration Release -Version 1.0.0
```

The self-contained application files are written to `artifacts\publish\win-x64\`; the installers are written to `installer\output\MusicMic.msi` and `installer\output\MusicMicSetup.exe`. The release scripts build the native engine first and pass that exact Release x64 DLL to the managed build, so publishing does not rely on a DLL previously left in the source tree.

`MusicMicSetup.exe` includes the official base VB-CABLE driver from [VB-Audio](https://www.vb-cable.com/). VB-CABLE is donationware; all participations are welcome. Its vendor setup runs first with administrator approval, and Windows may require a reboot before `CABLE Input` and `CABLE Output` become available. MusicMic never changes the Windows default playback device. In Discord, select **User Settings → Voice & Video → Input Device → CABLE Output (VB-Audio Virtual Cable)**. MusicMic writes to the complementary **CABLE Input** render endpoint.

## Release validation

The automated package smoke test checks the self-contained publish layout, the required native DLL, its non-empty PE image, its x64 architecture, and the MSI product metadata. Before release, complete the hardware acceptance matrix as well: source-only, microphone-only, and combined audio; selected-process privacy; source/microphone/sleep recovery; installer/uninstaller/reinstall; and direct verification that Start and Stop preserve the selected app's playback endpoint, volume, mute state, playback state, and Windows defaults. These hardware-dependent checks cannot be simulated by the build scripts.

See `SPEC.md` for authoritative V1 constraints and [the acceptance matrix](docs/acceptance-test-matrix.md) for the required release evidence, including playback-preservation and hardware-only checks.
