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

    void UpdateAppearance(TrayMenuAppearance appearance);

    void ShowRunningInBackgroundNotice();
}

/// <summary>
/// The notification-area presence: a monochrome shell-style icon, a Windows 11 style menu, and
/// left-click access to the flyout. This is MusicMic's primary entry point — the app has no
/// taskbar window of its own.
/// </summary>
public sealed class TrayService : ITrayService
{
    private readonly NotifyIcon notifyIcon = new();
    private readonly ToolStripMenuItem openItem = new("Open MusicMic");
    private readonly ToolStripMenuItem statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem toggleItem = new();
    private readonly ToolStripMenuItem sourceItem = new("Audio source");
    private readonly ToolStripMenuItem microphoneItem = new("Microphone");
    private readonly ToolStripMenuItem settingsItem = new("Settings");
    private readonly ToolStripMenuItem exitItem = new("Exit");
    private Action<string>? selectSource;
    private Action<string>? selectMicrophone;
    private TrayMenuAppearance appearance = new(TrayIconFactory.TaskbarIsDark(), SystemColors.Highlight);
    private Icon? currentIcon;
    private bool isInjecting;

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

        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(appearance),
            ShowCheckMargin = false,
            ShowImageMargin = true,
            Padding = new Padding(0, 4, 0, 4),
        };
        Font? menuFont = TryCreateMenuFont();
        if (menuFont is not null)
        {
            menu.Font = menuFont;
        }

        menu.Items.AddRange(
        [
            openItem,
            new ToolStripSeparator(),
            statusItem,
            toggleItem,
            new ToolStripSeparator(),
            sourceItem,
            microphoneItem,
            new ToolStripSeparator(),
            settingsItem,
            exitItem,
        ]);
        openItem.Font = new Font(menu.Font, FontStyle.Bold);
        menu.Opened += (_, _) => TrayMenuRenderer.ApplyWindowAppearance(menu.Handle, appearance.IsDark);
        sourceItem.DropDownOpened += (_, _) => ApplyDropDownAppearance(sourceItem);
        microphoneItem.DropDownOpened += (_, _) => ApplyDropDownAppearance(microphoneItem);

        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.Text = "MusicMic";
        notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                open();
            }
        };
        RefreshIcon();
        notifyIcon.Visible = true;
    }

    public void Update(AudioEngineSnapshot snapshot)
    {
        TrayMenuState state = TrayMenuState.From(snapshot);
        bool wasInjecting = isInjecting;
        isInjecting = snapshot.Injection.IsInjectionActive;

        statusItem.Text = isInjecting ? "Injecting" : state.StatusText;
        toggleItem.Text = state.StartStopText;
        toggleItem.Enabled = state.CanStartStop;
        sourceItem.Enabled = state.CanChangeSelection && state.Sources.Count > 0;
        microphoneItem.Enabled = state.CanChangeSelection && state.Microphones.Count > 0;
        RebuildChoices(sourceItem, state.Sources, choice => selectSource?.Invoke(choice.Id));
        RebuildChoices(microphoneItem, state.Microphones, choice => selectMicrophone?.Invoke(choice.Id));
        notifyIcon.Text = isInjecting ? "MusicMic — Injecting" : $"MusicMic — {state.StatusText}";

        if (wasInjecting != isInjecting)
        {
            RefreshIcon();
        }
    }

    public void UpdateAppearance(TrayMenuAppearance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        appearance = value;
        if (notifyIcon.ContextMenuStrip is not null)
        {
            notifyIcon.ContextMenuStrip.Renderer = new TrayMenuRenderer(appearance);
        }

        RefreshIcon();
    }

    public void ShowRunningInBackgroundNotice()
    {
        notifyIcon.BalloonTipTitle = "MusicMic is running";
        notifyIcon.BalloonTipText = "Select the MusicMic icon in the notification area to choose an app and microphone.";
        notifyIcon.BalloonTipIcon = ToolTipIcon.None;
        notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
        currentIcon?.Dispose();
        currentIcon = null;
    }

    private void RefreshIcon()
    {
        Icon icon = TrayIconFactory.Create(isInjecting, appearance.Accent, appearance.IsDark);
        Icon? previous = currentIcon;
        currentIcon = icon;
        notifyIcon.Icon = icon;
        previous?.Dispose();
    }

    private void ApplyDropDownAppearance(ToolStripMenuItem parent)
    {
        parent.DropDown.Renderer = new TrayMenuRenderer(appearance);
        TrayMenuRenderer.ApplyWindowAppearance(parent.DropDown.Handle, appearance.IsDark);
    }

    private static Font? TryCreateMenuFont()
    {
        foreach (string family in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            try
            {
                // The family instance is kept alive by the font for the lifetime of the menu.
                var candidate = new FontFamily(family);
                return new Font(candidate, 9f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                // The family is unavailable on this host; try the next one.
            }
        }

        return null;
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
