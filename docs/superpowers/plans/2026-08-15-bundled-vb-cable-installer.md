# Bundled VB-CABLE Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce one MusicMic setup executable that installs the official base VB-CABLE driver and MusicMic with required vendor attribution.

**Architecture:** A checksum-pinned official VB-CABLE package is validated and passed to the existing WiX Burn bundle. Burn conditionally runs the driver before the MusicMic MSI and visibly identifies the driver as VB-Audio donationware.

**Tech Stack:** PowerShell, WiX Toolset 5 Burn, Windows Installer COM, SHA-256.

**Spec:** `docs/superpowers/specs/2026-08-15-bundled-vb-cable-design.md`

## Global Constraints

- Bundle only the official base VB-CABLE package; do not include A+B or C+D.
- Preserve the selected application's normal playback endpoint, volume, mute, playback, and Windows defaults.
- Display `https://www.vb-cable.com/` and state that VB-CABLE is donationware.
- Do not add a custom driver or alter normal audio routing.
- State that a reboot may be required after driver installation.

---

### Task 1: Pin and validate the vendor package

**Files:** `installer/third-party/vb-cable/manifest.json`, `scripts/Build-Installer.ps1`, `scripts/Test-Package.ps1`.

- [ ] Write a failing `Test-Package.ps1` assertion that a supplied VB-CABLE setup path exists and is a non-empty PE.
- [ ] Run it with a nonexistent path and confirm the expected missing-prerequisite failure.
- [ ] Download the official base package, record its version, URL, setup filename, and SHA-256 in the manifest, and validate the checksum in `Build-Installer.ps1`.
- [ ] Run the test with the validated vendor setup path and confirm it passes.
- [ ] Commit the pinned prerequisite files and validation.

### Task 2: Chain VB-CABLE in Burn

**Files:** `installer/Bundle.wxs`, `installer/MusicMic.Bundle.wixproj`, `scripts/Build-Installer.ps1`, `scripts/Test-Package.ps1`.

- [ ] Write a failing bundle-content assertion for `VB-Audio`, `donationware`, and `www.vb-cable.com`.
- [ ] Build the present bundle and confirm that assertion fails.
- [ ] Pass the validated setup path as a WiX define, add a conditional `ExePackage` before `MsiPackage`, and include the required attribution and reboot disclosure in the bootstrapper UI.
- [ ] Rebuild with `Build-Installer.ps1 -Configuration Release -Version 1.0.0 -SkipTests`; run the package smoke test and confirm it passes.
- [ ] Commit the chain and packaging changes.

### Task 3: Full release verification

**Files:** `README.md`, `scripts/Build-Installer.ps1`.

- [ ] Document that the setup contains the official base VB-CABLE driver, its VB-Audio origin/donationware status, and possible reboot.
- [ ] Run `Build-Installer.ps1 -Configuration Release -Version 1.0.0`.
- [ ] Confirm native CTest, managed tests, publish, MSI, bundle, and package smoke all pass.
- [ ] Commit documentation.
