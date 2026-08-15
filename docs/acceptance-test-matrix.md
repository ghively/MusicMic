# MusicMic V1 acceptance test matrix

Run the automated rows for every release candidate. Run the hardware rows on a Windows 11 x64 machine with a real physical microphone, a selected audio-producing application, VB-CABLE, and a second application capable of monitoring a microphone input. Do not mark a hardware row as passed from simulated audio, unit tests, or code inspection.

## Required setup

1. Install VB-CABLE separately from its vendor: [VB-Audio Virtual Cable](https://vb-audio.com/Cable/). Run the vendor installer with the required administrator rights and reboot if the vendor installer asks. MusicMic neither installs nor redistributes VB-CABLE or any other driver.
2. Confirm Windows exposes **CABLE Input** as a playback/render endpoint and **CABLE Output** as a recording/microphone endpoint. These are intentionally different ends of the cable.
3. Start the source application and play identifiable audio through its normal speakers or headphones. Leave its Windows endpoint, session volume, mute state, and playback running state visible for comparison.
4. In MusicMic, select the source application and the physical microphone. Do not select a virtual microphone as the physical microphone.
5. In Discord, open **User Settings → Voice & Video → Input Device** and choose **CABLE Output (VB-Audio Virtual Cable)**. Do not choose CABLE Input. Turn off Discord input sensitivity/processing only if needed to make the audible check reliable; that is an environment note, not a MusicMic requirement.

## Automated release checks

| ID | Gate | Procedure | Passing evidence | Current status |
| --- | --- | --- | --- | --- |
| A-01 | Managed tests | `dotnet test MusicMic.sln -c Release -p:Platform=x64 --nologo` | All managed tests pass. | Run per candidate |
| A-02 | Native tests | Configure `src/MusicMic.Audio` with CMake for x64, build `MusicMic.Audio.Tests`, then run `ctest --test-dir <build> -C Release --output-on-failure`. | Native mixing, ABI, and state tests pass. | Run per candidate |
| A-03 | Debug build | `dotnet build MusicMic.sln -c Debug -p:Platform=x64 --nologo` and x64 native build. | Both builds pass. | Run per candidate |
| A-04 | Release build | `dotnet build MusicMic.sln -c Release -p:Platform=x64 --nologo` and x64 native build. | Both builds pass. | Run per candidate |
| A-05 | Publish smoke test | `powershell -ExecutionPolicy Bypass -File scripts\Publish-WinX64.ps1 -Configuration Release -Version 1.0.0` | `MusicMic.exe`, runtime metadata, and `MusicMic.Audio.dll` are present and nonempty. | Run per candidate |
| A-06 | Installer smoke test | `powershell -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1 -Configuration Release -Version 1.0.0` | WiX builds `MusicMic.msi`; package script validates MSI add/remove-program properties. | Run per candidate |
| A-07 | Scope scan | `rg -n --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/out/**' 'TODO|FIXME' .` | No unresolved implementation placeholders. | Run per candidate |

## Hardware and manual acceptance checks

| ID | Gate | Procedure | Passing evidence | Current status |
| --- | --- | --- | --- | --- |
| H-01 | VB-CABLE unavailable | With VB-CABLE absent or disabled, launch MusicMic. | Injection is disabled and reports a plain output-unavailable error; no fallback endpoint is used. | Requires hardware/Windows endpoint test |
| H-02 | Source only | Mute the physical microphone outside MusicMic, start injection, and play the selected app. Monitor Discord with **CABLE Output** selected. | The source is audible in Discord; its normal speakers/headphones continue unchanged. | Requires hardware audio test |
| H-03 | Microphone only | Pause source playback, speak into the selected physical microphone, and monitor Discord. | Speech is audible in Discord. | Requires hardware audio test |
| H-04 | Combined mix | Play source audio and speak simultaneously. | Both are audible at the configured 70% source / 100% microphone defaults without output loss. | Requires hardware audio test |
| H-05 | Selected-process privacy | Play a second, unselected application at the same time as the selected source. | The selected source and microphone are present; unselected application audio is absent from Discord. | Requires hardware audio test |
| H-06 | Playback preservation | Before Start, record the selected app’s endpoint, session volume, mute state, and playing state, plus Windows default playback endpoint. Start, stop, and repeat once. | Every recorded value is identical before, during, and after injection. The source remains normally audible throughout. | Requires hardware/Windows endpoint test |
| H-07 | Source recovery | During injection, close the selected source, then reopen the same executable/session and resume audio. | Source contribution becomes silence while absent, microphone continues, then source reconnects using stable identity. | Requires hardware audio test |
| H-08 | Microphone recovery | During injection, unplug/disable the selected microphone, then reconnect it. | Source continues, microphone becomes silence while unavailable, and only the same microphone is restored. | Requires hardware endpoint test |
| H-09 | Output recovery | During injection, disable/remove VB-CABLE, then restore it. | Injection stops safely with OutputUnavailable; no unrelated endpoint is substituted. | Requires hardware endpoint test |
| H-10 | Sleep/resume | Start injection, put Windows to sleep, resume, and wait through reconnection. | Endpoints are re-enumerated and prior injection returns only when safe. | Requires hardware/power-management test |
| H-11 | Install | Run `installer\output\MusicMic.msi` as an administrator. Launch MusicMic. | App appears in Installed apps, starts, and has no bundled VB-CABLE installer/driver. | Requires clean Windows test machine |
| H-12 | Uninstall | Uninstall MusicMic from Installed apps after H-11. | Program files are removed; the separately installed VB-CABLE remains installed. | Requires clean Windows test machine |
| H-13 | Reinstall/upgrade | Install an older MusicMic MSI, then run a newer MusicMic MSI. | Major upgrade completes; exactly one MusicMic entry remains; VB-CABLE remains untouched. | Requires clean Windows test machine |

## Release record

Record the candidate version, Windows build, .NET SDK, WiX version, VB-CABLE version, physical microphone model, source application/version, and Discord version beside the executed results. A release is blocked until every automated row passes and every hardware row is either passed with direct evidence or explicitly recorded as not executed. Hardware-dependent rows must never be inferred from automated checks.
