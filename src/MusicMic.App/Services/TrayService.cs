using System.Drawing;
using System.Windows.Forms;

namespace MusicMic.App.Services;

public sealed record TrayMenuState(string StartStopText, bool CanStartStop)
{
    public static TrayMenuState From(AudioEngineSnapshot snapshot) => new(
        snapshot.Injection.IsInjectionActive ? "Stop injection" : "Start injection",
        snapshot.Injection.IsInjectionActive ||
        (snapshot.Injection.IsOutputAvailable &&
         snapshot.SelectedSourceId is not null &&
         snapshot.SelectedMicrophoneId is not null));
}

public interface ITrayService : IDisposable
{
    void Initialize(
        Func<CancellationToken, Task> toggleInjection,
        Action open,
        Action openSettings,
        Action exit);

    void Update(AudioEngineSnapshot snapshot);
}

/// <summary>Minimal notification-area controls for the same one-window workflow.</summary>
public sealed class TrayService : ITrayService
{
    private readonly NotifyIcon notifyIcon = new();
    private readonly ToolStripMenuItem toggleItem = new();
    private readonly ToolStripMenuItem openItem = new("Open MusicMic");
    private readonly ToolStripMenuItem settingsItem = new("Settings");
    private readonly ToolStripMenuItem exitItem = new("Exit");
    private Func<CancellationToken, Task>? toggleInjection;

    public void Initialize(
        Func<CancellationToken, Task> toggleInjection,
        Action open,
        Action openSettings,
        Action exit)
    {
        this.toggleInjection = toggleInjection ?? throw new ArgumentNullException(nameof(toggleInjection));
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(exit);

        toggleItem.Click += async (_, _) => await this.toggleInjection(CancellationToken.None);
        openItem.Click += (_, _) => open();
        settingsItem.Click += (_, _) => openSettings();
        exitItem.Click += (_, _) => exit();
        notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        notifyIcon.ContextMenuStrip.Items.AddRange([toggleItem, new ToolStripSeparator(), openItem, settingsItem, exitItem]);
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "MusicMic";
        notifyIcon.DoubleClick += (_, _) => open();
        notifyIcon.Visible = true;
    }

    public void Update(AudioEngineSnapshot snapshot)
    {
        TrayMenuState state = TrayMenuState.From(snapshot);
        toggleItem.Text = state.StartStopText;
        toggleItem.Enabled = state.CanStartStop;
        notifyIcon.Text = snapshot.Injection.IsInjectionActive ? "MusicMic — Injecting" : "MusicMic";
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
