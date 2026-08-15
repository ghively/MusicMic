using MusicMic.Core;
using System.Drawing;
using System.Windows.Forms;

namespace MusicMic.App.Services;

public sealed record TrayChoice(string Id, string DisplayName, bool IsSelected);

public sealed record TrayMenuState(
    string StatusText,
    string StartStopText,
    bool CanStartStop,
    bool CanChangeSelection,
    IReadOnlyList<TrayChoice> Sources,
    IReadOnlyList<TrayChoice> Microphones)
{
    public static TrayMenuState From(AudioEngineSnapshot snapshot) => new(
        snapshot.Injection.State switch
        {
            InjectionState.Injecting => "Injecting",
            InjectionState.Ready => "Idle",
            InjectionState.SourceUnavailable => "Source unavailable",
            InjectionState.MicrophoneUnavailable => "Microphone unavailable",
            InjectionState.OutputUnavailable => "Output unavailable",
            InjectionState.Initializing => "Initializing",
            _ => "Error",
        },
        snapshot.Injection.IsInjectionActive ? "Stop Injecting" : "Start Injecting",
        snapshot.Injection.IsInjectionActive ||
        (snapshot.Injection.IsOutputAvailable &&
         snapshot.SelectedSourceId is not null &&
         snapshot.SelectedMicrophoneId is not null),
        !snapshot.Injection.IsInjectionActive,
        snapshot.Sources.Select(source => new TrayChoice(
            source.Id,
            source.DisplayName,
            string.Equals(source.Id, snapshot.SelectedSourceId, StringComparison.Ordinal))).ToArray(),
        snapshot.Microphones.Select(microphone => new TrayChoice(
            microphone.Id,
            microphone.DisplayName,
            string.Equals(microphone.Id, snapshot.SelectedMicrophoneId, StringComparison.Ordinal))).ToArray());
}

public interface ITrayService : IDisposable
{
    void Initialize(
        Func<CancellationToken, Task> toggleInjection,
        Action<string> selectSource,
        Action<string> selectMicrophone,
        Action open,
        Action openSettings,
        Action exit);

    void Update(AudioEngineSnapshot snapshot);
}

/// <summary>Minimal notification-area controls for the same one-window workflow.</summary>
public sealed class TrayService : ITrayService
{
    private readonly NotifyIcon notifyIcon = new();
    private readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem toggleItem = new();
    private readonly ToolStripMenuItem sourceItem = new("Audio Source");
    private readonly ToolStripMenuItem microphoneItem = new("Microphone");
    private readonly ToolStripMenuItem openItem = new("Open MusicMic");
    private readonly ToolStripMenuItem settingsItem = new("Settings");
    private readonly ToolStripMenuItem exitItem = new("Exit");
    private Action<string>? selectSource;
    private Action<string>? selectMicrophone;

    public void Initialize(
        Func<CancellationToken, Task> toggleInjection,
        Action<string> selectSource,
        Action<string> selectMicrophone,
        Action open,
        Action openSettings,
        Action exit)
    {
        ArgumentNullException.ThrowIfNull(toggleInjection);
        this.selectSource = selectSource ?? throw new ArgumentNullException(nameof(selectSource));
        this.selectMicrophone = selectMicrophone ?? throw new ArgumentNullException(nameof(selectMicrophone));
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(exit);

        toggleItem.Click += async (_, _) => await toggleInjection(CancellationToken.None);
        openItem.Click += (_, _) => open();
        settingsItem.Click += (_, _) => openSettings();
        exitItem.Click += (_, _) => exit();
        notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            statusItem,
            toggleItem,
            new ToolStripSeparator(),
            sourceItem,
            microphoneItem,
            new ToolStripSeparator(),
            settingsItem,
            openItem,
            exitItem,
        ]);
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "MusicMic";
        notifyIcon.DoubleClick += (_, _) => open();
        notifyIcon.Visible = true;
    }

    public void Update(AudioEngineSnapshot snapshot)
    {
        TrayMenuState state = TrayMenuState.From(snapshot);
        statusItem.Text = state.StatusText == "Injecting" ? "● Injecting" : $"○ {state.StatusText}";
        toggleItem.Text = state.StartStopText;
        toggleItem.Enabled = state.CanStartStop;
        sourceItem.Enabled = state.CanChangeSelection && state.Sources.Count > 0;
        microphoneItem.Enabled = state.CanChangeSelection && state.Microphones.Count > 0;
        RebuildChoices(sourceItem, state.Sources, choice => selectSource?.Invoke(choice.Id));
        RebuildChoices(microphoneItem, state.Microphones, choice => selectMicrophone?.Invoke(choice.Id));
        notifyIcon.Text = state.StatusText == "Injecting" ? "MusicMic — Injecting" : $"MusicMic — {state.StatusText}";
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private static void RebuildChoices(
        ToolStripMenuItem parent,
        IReadOnlyList<TrayChoice> choices,
        Action<TrayChoice> select)
    {
        parent.DropDownItems.Clear();
        foreach (TrayChoice choice in choices)
        {
            var item = new ToolStripMenuItem(choice.DisplayName)
            {
                Checked = choice.IsSelected,
                CheckOnClick = false,
            };
            item.Click += (_, _) => select(choice);
            parent.DropDownItems.Add(item);
        }
    }
}
