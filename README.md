# MusicMic

MusicMic is a compact Windows 11 utility that copies one selected application's rendered audio into a microphone feed while leaving the application's normal playback untouched. It mixes that copy with one physical microphone and writes the result to the separately installed VB-CABLE virtual input.

## Non-negotiable routing rule

Starting or stopping MusicMic must never change an application's playback endpoint, session volume, mute state, playback state, or the Windows default playback endpoint. The native engine captures a parallel, process-specific loopback copy.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK
- Visual Studio 2022 with Desktop development with C++ for later native/WPF phases
- VB-CABLE installed separately for end-to-end use; MusicMic does not bundle or install audio drivers

## Build and test

```powershell
dotnet test MusicMic.sln -c Release -p:Platform=x64 --nologo
dotnet build MusicMic.sln -c Release -p:Platform=x64 --nologo
powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1 -Configuration Release -Version 1.0.0
```

The installer is written to `installer\output\`. It installs MusicMic only; install [VB-CABLE](https://vb-audio.com/Cable/) independently. In Discord, select **User Settings → Voice & Video → Input Device → CABLE Output (VB-Audio Virtual Cable)**. MusicMic writes to the complementary **CABLE Input** render endpoint.

See `SPEC.md` for authoritative V1 constraints and [the acceptance matrix](docs/acceptance-test-matrix.md) for the required release evidence, including playback-preservation and hardware-only checks.
