# MusicMic

MusicMic is a compact Windows 11 utility that copies one selected application's rendered audio into a microphone feed while leaving the application's normal playback untouched. It mixes that copy with one physical microphone and writes the result to the separately installed VB-CABLE virtual input.

## Non-negotiable routing rule

Starting or stopping MusicMic must never change an application's playback endpoint, session volume, mute state, playback state, or the Windows default playback endpoint. The native engine captures a parallel, process-specific loopback copy.

## Repository status

The repository currently contains the managed domain contract and its unit tests. The WPF application, native WASAPI engine, integration layer, and installer are built in later implementation phases described in `docs/superpowers/plans/2026-08-14-musicmic-v1.md`.

## Prerequisites

- Windows 11 x64
- .NET 10 SDK
- Visual Studio 2022 with Desktop development with C++ for later native/WPF phases
- VB-CABLE installed separately for end-to-end use

## Build and test

```powershell
dotnet build MusicMic.sln -c Debug -p:Platform=x64
dotnet test tests\MusicMic.Core.Tests\MusicMic.Core.Tests.csproj
```

See `SPEC.md` for the authoritative V1 constraints.
