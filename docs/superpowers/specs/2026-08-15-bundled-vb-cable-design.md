# Bundled VB-CABLE Installer Design

## Goal

Deliver one `MusicMicSetup.exe` that installs the official base VB-CABLE driver and MusicMic, while preserving VB-Audio's required attribution and donationware notice.

## Architecture

The repository stores a version-pinned copy of the official base VB-CABLE distribution under `installer/third-party/vb-cable/`, with its SHA-256 in a manifest. The WiX Burn bundle packages the vendor installer before the MusicMic MSI. A detect condition skips the driver package when the canonical `CABLE Input` endpoint already exists.

The bootstrapper must name VB-Audio, state that VB-CABLE is donationware, link to `https://www.vb-cable.com/`, and disclose that a reboot may be required. It never changes Windows playback defaults or routing.

## Distribution basis

VB-Audio's licensing page permits bundling the base VB-CABLE package, including silent installation, when the end user can identify VB-Audio and is told that VB-CABLE is donationware. VB-CABLE A+B and C+D are excluded.

## Verification

The build verifies the vendor archive SHA-256, the expected vendor installer path, the chain metadata and attribution, the MusicMic MSI Start-menu shortcut, and the final bootstrapper PE. No custom driver is built.
