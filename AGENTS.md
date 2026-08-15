# MusicMic Agent Instructions

Read SPEC.md before modifying code.

## Product

MusicMic combines:
1. one selected application's audio
2. one physical microphone

and sends the result to a virtual microphone.

## Primary invariant

The selected application's normal playback must NEVER be changed.

Do not:
- change its playback endpoint
- mute it
- change its volume
- make VB-CABLE the system output
- modify Windows default output
- reroute the application

Capture a copy using process-specific loopback.

## Scope

Implement only SPEC.md.

Do not add:
- EQ
- recording
- ducking
- soundboard
- multiple sources
- routing matrices
- Spotify API features
- custom audio drivers
- audio-quality settings

## UI

docs/ui/approved-ui-reference.png is authoritative.

Required:
- compact one-window utility
- approved idle state
- approved active state
- light mode
- dark mode
- system theme

No sidebar.
No dashboard.
No additional mixer surface.

## Work Process

Use subagents according to SPEC.md.

Prefer parallel read/review work.

Avoid parallel writes to overlapping files.

For every phase:
1. inspect
2. implement
3. build
4. test
5. review
6. fix

Do not declare completion while:
- tests fail
- build fails
- acceptance criteria fail
- TODO/FIXME placeholders remain
- playback preservation is unverified

## Completion

The app is done only when the Definition of Done in SPEC.md passes.
