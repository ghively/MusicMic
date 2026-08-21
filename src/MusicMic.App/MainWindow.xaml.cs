using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

// The app enables both WPF and Windows Forms, so implicit usings bring in two
// KeyEventArgs. WPF's markup-compilation pass does not honour this file's own using
// directive, so name the input one outright rather than let that pass fail.
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MusicMic.App;

/// <summary>
/// The MusicMic flyout. It behaves like a shell notification-area flyout rather than a normal
/// window: it is opened from the tray icon, anchored beside it, and dismissed as soon as it
/// loses activation or Esc is pressed.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);

    private DateTime lastDismissedUtc = DateTime.MinValue;
    private bool isPrepared;
    private bool isPreparing;
    private bool suppressDismiss;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            TrayFlyoutPlacement.HideFromApplicationSwitcher(this);
            RefreshBackdrop();
        };
        Activated += (_, _) => RefreshBackdrop();
        Deactivated += (_, _) => DismissOnDeactivation();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>True while the flyout was dismissed moments ago, so a tray click that caused the
    /// dismissal toggles the flyout closed instead of immediately reopening it.</summary>
    public bool WasJustDismissed => DateTime.UtcNow - lastDismissedUtc < ReopenGuard;

    public void RefreshBackdrop() => WindowBackdrop.Apply(this, BackdropHost);

    /// <summary>Lays the flyout out once, off-screen and without activation, so its measured
    /// height is known before it is ever positioned next to the notification area.</summary>
    public void PrepareForFlyout()
    {
        if (isPrepared)
        {
            return;
        }

        isPrepared = true;
        isPreparing = true;
        try
        {
            ShowActivated = false;
            Left = -32000;
            Top = -32000;
            Show();
            UpdateLayout();
            Hide();
        }
        finally
        {
            ShowActivated = true;
            isPreparing = false;
            lastDismissedUtc = DateTime.MinValue;
        }
    }

    public void ShowFlyout()
    {
        PrepareForFlyout();
        TrayFlyoutPlacement.MoveToNotificationArea(this);

        if (!IsVisible)
        {
            Show();
        }

        Topmost = true;
        Activate();
        Focus();
        PlayEntranceAnimation();
    }

    public void HideFlyout()
    {
        if (!IsVisible)
        {
            return;
        }

        lastDismissedUtc = DateTime.UtcNow;
        Hide();
    }

    /// <summary>Tray-click behaviour: open when hidden, close when already showing.</summary>
    public void ToggleFlyout()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        if (WasJustDismissed)
        {
            return;
        }

        ShowFlyout();
    }

    private void PlayEntranceAnimation()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BackdropHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        EntranceTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = easing });
    }

    private void DismissOnDeactivation()
    {
        if (isPreparing || suppressDismiss || !IsVisible)
        {
            return;
        }

        // A selector dropdown is its own top-level surface; keep the flyout open behind it.
        if (SourceSelector.IsDropDownOpen || MicrophoneSelector.IsDropDownOpen)
        {
            return;
        }

        HideFlyout();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (SourceSelector.IsDropDownOpen || MicrophoneSelector.IsDropDownOpen)
        {
            return;
        }

        e.Handled = true;
        HideFlyout();
    }

    private void OpenSettings(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>Opens settings as a modal dialog owned by the flyout, holding the flyout open
    /// while the dialog has focus.</summary>
    public void ShowSettings()
    {
        if (!IsVisible)
        {
            ShowFlyout();
        }

        suppressDismiss = true;
        try
        {
            new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();
        }
        finally
        {
            suppressDismiss = false;
        }

        if (IsVisible)
        {
            Activate();
        }
    }
}
