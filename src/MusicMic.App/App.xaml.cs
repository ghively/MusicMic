using MusicMic.App.Presentation;
using MusicMic.App.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using DrawingColor = System.Drawing.Color;

namespace MusicMic.App;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\MusicMic.SingleInstance";
    private const string ActivationEventName = @"Local\MusicMic.ShowFlyout";

    private AudioEngineService? engine;
    private TrayService? tray;
    private MainViewModel? viewModel;
    private ThemeService? themeService;
    private MainWindow? flyout;
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;
    private RegisteredWaitHandle? activationRegistration;
    private readonly ShutdownCallbackGuard callbackGuard = new();
    private bool isExiting;
    private bool servicesDisposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // MusicMic lives in the notification area, so it must outlive any window it shows.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        engine = new AudioEngineService();
        themeService = new ThemeService();
        themeService.AppearanceChanged += OnAppearanceChanged;
        viewModel = new MainViewModel(engine, new SettingsService(), themeService, new StartupService());
        flyout = new MainWindow { DataContext = viewModel };
        MainWindow = flyout;
        flyout.Closing += OnFlyoutClosing;
        flyout.PrepareForFlyout();

        tray = new TrayService();
        tray.Initialize(
            _ => callbackGuard.RunAsync(viewModel.ToggleInjectionAsync),
            id => viewModel.SelectedSource = viewModel.Sources.FirstOrDefault(source => source.Id == id),
            id => viewModel.SelectedMicrophone = viewModel.Microphones.FirstOrDefault(microphone => microphone.Id == id),
            ToggleFlyout,
            OpenSettings,
            ExitFromTray);
        tray.UpdateAppearance(CurrentTrayAppearance());
        engine.SnapshotChanged += OnEngineSnapshotChanged;
        tray.Update(engine.Snapshot);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        AnnounceFirstRun();
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        callbackGuard.BeginShutdown();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (themeService is not null)
        {
            themeService.AppearanceChanged -= OnAppearanceChanged;
        }

        if (!servicesDisposed)
        {
            tray?.Dispose();
            engine?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            servicesDisposed = true;
        }

        ReleaseSingleInstance();
        base.OnExit(e);
    }

    private bool ClaimSingleInstance()
    {
        instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Hand the request to the running instance so its flyout opens, then stand down.
            if (EventWaitHandle.TryOpenExisting(ActivationEventName, out EventWaitHandle? running))
            {
                running.Set();
                running.Dispose();
            }

            instanceMutex.Dispose();
            instanceMutex = null;
            return false;
        }

        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowFlyout)),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    private void ReleaseSingleInstance()
    {
        activationRegistration?.Unregister(null);
        activationRegistration = null;
        activationEvent?.Dispose();
        activationEvent = null;
        if (instanceMutex is not null)
        {
            try
            {
                instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was never owned by this thread; disposing is still correct.
            }

            instanceMutex.Dispose();
            instanceMutex = null;
        }
    }

    /// <summary>On the very first run there is no window to notice, so point at the tray icon once.</summary>
    private void AnnounceFirstRun()
    {
        string marker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicMic",
            "first-run.marker");
        try
        {
            if (File.Exists(marker))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, string.Empty);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        ShowFlyout();
        tray?.ShowRunningInBackgroundNotice();
    }

    private static TrayMenuAppearance CurrentTrayAppearance()
    {
        bool taskbarIsDark = TrayIconFactory.TaskbarIsDark();
        System.Windows.Media.Color accent = ThemeService.ResolveAccentColor(taskbarIsDark);
        return new TrayMenuAppearance(taskbarIsDark, DrawingColor.FromArgb(accent.R, accent.G, accent.B));
    }

    private void OnFlyoutClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isExiting)
        {
            return;
        }

        e.Cancel = true;
        flyout?.HideFlyout();
    }

    private void ShowFlyout() => flyout?.ShowFlyout();

    private void ToggleFlyout() => flyout?.ToggleFlyout();

    private void OpenSettings() => flyout?.ShowSettings();

    private async void ExitFromTray()
    {
        isExiting = true;
        callbackGuard.BeginShutdown();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (engine is not null)
        {
            engine.SnapshotChanged -= OnEngineSnapshotChanged;
        }

        tray?.Dispose();
        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        servicesDisposed = true;
        Shutdown();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && engine is not null)
        {
            Dispatcher.BeginInvoke(new Action(
                () => _ = callbackGuard.RunAsync(engine.HandlePowerResumeAsync)));
        }
    }

    private void OnEngineSnapshotChanged(object? sender, AudioEngineSnapshot snapshot)
    {
        if (Dispatcher.CheckAccess())
        {
            callbackGuard.Run(() => tray?.Update(snapshot));
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => callbackGuard.Run(() => tray?.Update(snapshot))));
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(new Action(() => callbackGuard.Run(() =>
            {
                viewModel?.RefreshSystemTheme();
                tray?.UpdateAppearance(CurrentTrayAppearance());
            })));
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => callbackGuard.Run(() =>
        {
            flyout?.RefreshBackdrop();
            if (flyout is not null)
            {
                foreach (Window ownedWindow in flyout.OwnedWindows)
                {
                    (ownedWindow as SettingsWindow)?.RefreshBackdrop();
                }
            }
        })));
    }
}
