import os

OUT = os.path.dirname(os.path.abspath(__file__))

VOLUME = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><path d="M8.5 2.5 5 5.3H2.5v5.4H5l3.5 2.8z" stroke-linejoin="round"/><path d="M11 5.6a3.4 3.4 0 0 1 0 4.8M13.1 3.4a6.4 6.4 0 0 1 0 9.2"/></svg>'
MIC = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><rect x="6" y="1.8" width="4" height="7.4" rx="2"/><path d="M4 7.6a4 4 0 0 0 8 0M8 11.7v2.1M5.8 13.9h4.4" stroke-linecap="round"/></svg>'
GEAR = '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><circle cx="8" cy="8" r="2.3"/><path d="M8 1.4v1.7M8 12.9v1.7M14.6 8h-1.7M3.1 8H1.4M12.7 3.3l-1.2 1.2M4.5 11.5l-1.2 1.2M12.7 12.7l-1.2-1.2M4.5 4.5 3.3 3.3"/></svg>'
INFO = '<svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><circle cx="8" cy="8" r="6.3"/><path d="M8 7.2v4M8 4.9v.9" stroke-linecap="round"/></svg>'
BRUSH = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><path d="M3 13c1.9 0 2.6-1 2.6-2.2A2.2 2.2 0 0 0 3 8.7z" stroke-linejoin="round"/><path d="M6.3 9.6 13 3.2a1.3 1.3 0 0 0-1.8-1.8L4.7 8"/></svg>'
POWER = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3"><path d="M8 2v5.6" stroke-linecap="round"/><path d="M4.9 4.2a5 5 0 1 0 6.2 0"/></svg>'
NET = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" opacity=".85"><path d="M2 11.5V6.2M5.5 13V4.6M9 13V2.4M12.5 13V6.9" stroke-linecap="round"/></svg>'
SPEAKER = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" opacity=".85"><path d="M8.5 3 5.4 5.4H3v5.2h2.4L8.5 13z" stroke-linejoin="round"/><path d="M11 6.2a2.9 2.9 0 0 1 0 3.6"/></svg>'


def tray_icon(active):
    fill = 'currentColor' if active else 'none'
    badge = '<span class="badge"></span>' if active else ''
    return (f'<span class="mm"><svg width="16" height="16" viewBox="0 0 16 16" fill="{fill}" stroke="currentColor" '
            f'stroke-width="1.5"><rect x="6" y="1.8" width="4" height="7.4" rx="2"/>'
            f'<path d="M4.1 7.7a3.9 3.9 0 0 0 7.8 0M8 11.9v1.5M5.6 13.9h4.8" fill="none" stroke-linecap="round"/>'
            f'</svg>{badge}</span>')


def taskbar(active):
    return f'''<div class="taskbar">
      <div class="tray">{NET}{SPEAKER}{tray_icon(active)}</div>
      <div class="clock">2:14 PM<br>15/08/2026</div>
    </div>'''


def flyout(theme, injecting):
    disabled = ' disabled' if injecting else ''
    return f'''<div class="surface flyout {theme}-mm">
      <div class="fly-head"><span class="fly-title">MusicMic</span><span class="gear">{GEAR}</span></div>
      <div class="fly-body">
        <div class="card">
          <div class="card-top">{VOLUME}<span class="lbl">App audio</span><span class="pct">70&nbsp;%</span></div>
          <div class="combo{disabled}"><span>Spotify</span><span class="chev">&#9662;</span></div>
          <div class="slider"><span class="track"></span><span class="fill" style="width:70%"></span><span class="thumb" style="left:70%"></span></div>
        </div>
        <div class="card">
          <div class="card-top">{MIC}<span class="lbl">Microphone</span><span class="pct">100&nbsp;%</span></div>
          <div class="combo{disabled}"><span>Microphone (USB Audio Device)</span><span class="chev">&#9662;</span></div>
          <div class="slider"><span class="track"></span><span class="fill" style="width:100%"></span><span class="thumb" style="left:100%"></span></div>
        </div>
        <div class="status">
          <span class="dot{' live' if injecting else ''}"></span>
          <span><span class="head">{'Injecting' if injecting else 'Ready'}</span><br>
          <span class="sub">{'Your mix is available from CABLE Output.' if injecting else 'Choose what to share, then start injection.'}</span></span>
        </div>
        <div class="btn {'standard' if injecting else 'accent'}">{'Stop injecting' if injecting else 'Start injecting'}</div>
        <div class="assure">{INFO}<span>You still hear Spotify normally through your speakers/headphones.</span></div>
      </div>
    </div>'''


def menu(theme, injecting):
    return f'''<div class="surface menu {theme}-mm">
      <div class="item bold">Open MusicMic</div>
      <div class="rule"></div>
      <div class="item dim">{'Injecting' if injecting else 'Idle'}</div>
      <div class="item">{'Stop Injecting' if injecting else 'Start Injecting'}</div>
      <div class="rule"></div>
      <div class="item hover">Audio source<span class="sub">&#9656;</span></div>
      <div class="item">Microphone<span class="sub">&#9656;</span></div>
      <div class="rule"></div>
      <div class="item">Settings</div>
      <div class="item">Exit</div>
    </div>'''


def settings(theme):
    return f'''<div class="surface flyout settings {theme}-mm">
      <div class="fly-head"><span class="fly-title">Settings</span><span class="gear">&#10005;</span></div>
      <div class="fly-body">
        <div class="card tight">
          <div class="row">{BRUSH}<span class="name">App theme</span>
            <span class="combo inline"><span>System</span><span class="chev">&#9662;</span></span></div>
        </div>
        <div class="card tight">
          <div class="row">{POWER}<span class="name">Start MusicMic when I sign in</span><span class="switch"></span></div>
        </div>
        <div class="assure">{INFO}<span>MusicMic keeps running in the notification area. Select its icon to open the
        flyout, or right-click it to exit.</span></div>
      </div>
    </div>'''


def page(width, height, theme, body):
    return f'''<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="shot.css">
</head><body><div class="corner {theme}">{body}</div></body></html>'''


SHOTS = {
    'flyout-light': (620, 616, 'light', flyout('light', False) + taskbar(False)),
    'flyout-dark': (620, 616, 'dark', flyout('dark', True) + taskbar(True)),
    'tray-menu': (620, 448, 'dark', menu('dark', True) + taskbar(True)),
    'settings': (620, 404, 'light', settings('light') + taskbar(False)),
}

for name, (w, h, theme, body) in SHOTS.items():
    with open(os.path.join(OUT, f'{name}.html'), 'w') as handle:
        handle.write(page(w, h, theme, body))
    print(name, w, h)
