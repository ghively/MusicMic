# MusicMic V1 Engineering Specification

**Imported authority:** MusicMic Final Engineering Specification v2.0, supplied 2026-08-14. This file is the repository's concise implementation authority; the complete acceptance work is tracked in `docs/superpowers/plans/2026-08-14-musicmic-v1.md`.

## Product

MusicMic selects one audio-producing application and one physical microphone, mixes a process-loopback copy of the application with the microphone, and renders the result to VB-CABLE `CABLE Input` so chat or game software can select `CABLE Output` as its microphone.

The selected application's existing speakers/headphones path must remain audible and unchanged before, during, and after injection.

## Absolute playback invariant

MusicMic must never:

- change the selected application's playback endpoint, volume, mute state, or playback state
- change the Windows default playback endpoint
- move the application to VB-CABLE or capture all system audio by default
- pause, mute, steal, or interrupt the application's render stream
- use Windows “Listen to this device” as its routing mechanism

Capture must be a parallel copy made with Windows process-specific loopback and restricted to the selected process and its children.

## V1 platform and architecture

- Windows 11 x64
- C# WPF on the current supported .NET LTS (.NET 10 for this repository)
- C++20 native DLL using WASAPI and Win32 Core Audio APIs
- a narrow C ABI between WPF and the native engine
- VB-CABLE as a separately installed virtual microphone transport; no custom driver
- stereo 48 kHz floating-point internal mix with bounded output

The native engine owns discovery, capture, normalization, mixing, rendering, COM lifetime, and audio recovery. Managed code owns presentation, persistence, theme/tray/startup behavior, diagnostics, and immutable state presentation. Audio processing must not be duplicated in C#.

## Required V1 behavior

- list applications with active audio sessions and automatically recognize Spotify without using the Spotify API
- list physical recording endpoints and initially select the Windows default microphone
- expose exactly one source selector, one microphone selector, source volume, microphone volume, status, and Start/Stop injection
- default source volume to 70%, microphone volume to 100%, and theme to System
- support light, dark, and system themes plus a minimal tray menu
- persist settings under `%LOCALAPPDATA%\MusicMic\settings.json`
- keep rotating text diagnostics under `%LOCALAPPDATA%\MusicMic\logs\`; never log or record PCM audio and never transmit telemetry
- detect a missing VB-CABLE endpoint and disable injection with a plain error

## State and recovery contract

Managed state values are `Initializing`, `Ready`, `Injecting`, `SourceUnavailable`, `MicrophoneUnavailable`, `OutputUnavailable`, and `Error`.

Reconnect source and microphone with delays of 250 ms, 500 ms, 1 second, 2 seconds, and then a maximum interval of 5 seconds. Re-identify applications by stable executable/session identity rather than PID alone. Never silently substitute an unrelated microphone.

If the source disappears during injection, its contribution becomes silence while the microphone and injection remain active. If the microphone disappears, its contribution becomes silence while source capture remains active. If VB-CABLE disappears, stop injection safely and report output unavailable. On resume, re-enumerate, rebuild captures, and restore prior injection only where safe.

## Explicit non-goals

Do not add recording, EQ, compression, ducking, effects, noise suppression, a soundboard, multiple simultaneous sources, routing matrices, profiles/scenes, sample-rate or quality controls, Spotify authentication/metadata/playback controls, cloud services, telemetry, integrations, ASIO/VST, or a custom audio driver.

## UI authority

`docs/ui/approved-ui-reference.png` is authoritative when present. The interface is a compact one-window utility with idle and active states, no sidebar, dashboard, professional mixer surface, or controls beyond the V1 workflow. It must continuously communicate that normal source playback remains audible.

## Release gates

Completion requires clean x64 Debug and Release builds; passing managed, native, integration, packaging, and acceptance tests; verified source-only, mic-only, and combined audio; verified selected-process privacy; verified source/mic/sleep recovery; successful installer/uninstaller/reinstall; and direct evidence that start/stop never changes the selected application's endpoint, volume, mute state, playback, or Windows defaults. Hardware-dependent gates must be reported honestly rather than simulated.
