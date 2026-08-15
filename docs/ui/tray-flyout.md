# MusicMic UI: notification-area flyout

MusicMic is a notification-area utility. It has no taskbar window; the tray icon is the app, and
the flyout is the whole interface. The reference model is the Windows 11 volume flyout (and
EarTrumpet, which follows it): open from the tray icon, anchored beside it, dismissed by clicking
away.

## Shell behaviour

| Behaviour | Implementation |
| --- | --- |
| Left-click the tray icon | Opens the flyout; clicking again while it is open closes it (`MainWindow.ToggleFlyout`) |
| Right-click the tray icon | Windows 11 style menu: open, status, start/stop, source, microphone, settings, exit |
| Click away, or press Esc | Dismisses the flyout (`MainWindow.DismissOnDeactivation`) |
| Position | Notification-area corner of the display under the pointer, inset 12px from the work area, so it follows the taskbar to whichever edge it is on (`TrayFlyoutPlacement`) |
| Taskbar and Alt+Tab | Absent: `ShowInTaskbar="False"` plus `WS_EX_TOOLWINDOW` |
| Second launch | The Start menu shortcut signals the running instance to open its flyout instead of starting a second copy (`App.ClaimSingleInstance`) |
| First run | Opens once and shows a "MusicMic is running" notification so the tray icon is discoverable |

## Visual language

Everything is a platform value rather than an app-specific one, so MusicMic changes with Windows
instead of alongside it.

- **Backdrop**: DWM transient (acrylic) backdrop with rounded corners — the same
  `DWMSBT_TRANSIENTWINDOW` the shell uses for its own flyouts. Solid `#F3F3F3` / `#202020`
  fallback where DWM cannot supply one, and system colours under high contrast.
- **Colour**: WinUI 3 token values (`CardBackgroundFillColorDefault`, `TextFillColorSecondary`,
  `ControlStrokeColorDefault`, …) in `Themes/Light.xaml` and `Themes/Dark.xaml`.
- **Accent**: read from `HKCU\…\Explorer\Accent\AccentPalette` — the first dark shade on light
  surfaces, the second light shade on dark surfaces, which is how WinUI defines
  `AccentFillColorDefault`. Hover and pressed are the accent at 90% and 80% opacity.
- **Type**: Segoe UI Variable Text. Body 14, Caption 12, Body Strong for titles and status.
- **Controls**: 32px combo boxes and buttons with 4px corners and a control elevation border;
  the WinUI slider (4px track, 20px thumb shell with a 12px accent knob that grows on hover and
  shrinks while dragging); a WinUI toggle switch for "Start MusicMic when I sign in".
- **Tray icon**: drawn to the current small-icon metric in the taskbar's own foreground colour,
  filled with an accent badge while injecting, so it matches the shell's monochrome icons at any
  DPI and in either taskbar theme.

## Layout

```
┌─ MusicMic ───────────────────────── ⚙ ─┐   header, 44px
│ ┌─────────────────────────────────────┐ │
│ │ 🔊  App audio                  70 % │ │   card: label + percentage
│ │ [ Spotify                        ⌄ ] │ │   selector, 32px
│ │ ──────────●───────────────────────  │ │   slider
│ └─────────────────────────────────────┘ │
│ ┌─────────────────────────────────────┐ │
│ │ 🎤  Microphone                 100 % │ │
│ │ [ Microphone (USB Audio)         ⌄ ] │ │
│ │ ────────────────────────────────●─  │ │
│ └─────────────────────────────────────┘ │
│ ●  Ready                                │   status dot + detail
│    Choose what to share, then start…    │
│ [          Start injecting          ]   │   accent button (standard when running)
│ ⓘ  You still hear Spotify normally      │   playback assurance, always visible
│    through your speakers/headphones.    │
└─────────────────────────────────────────┘   width 360, height from content
```

Idle and active differ only in state, not in structure: the status dot turns green and pulses,
the accent "Start injecting" button becomes a standard "Stop injecting" button, the selectors
lock, and the tray icon gains its accent badge.

## Constraints kept from SPEC.md

One window, no sidebar, no dashboard, no mixer surface, and no controls beyond one source
selector, one microphone selector, two volumes, status, and start/stop. The playback assurance
line is always visible, in both states.
