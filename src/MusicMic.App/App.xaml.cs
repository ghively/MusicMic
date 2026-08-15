using MusicMic.App.Presentation;
using MusicMic.App.Services;
using Microsoft.Win32;
using System.Windows;

namespace MusicMic.App;

public partial class App : System.Windows.Application
{
    private AudioEngineService? engine;
    private TrayService? tray;
    private MainViewModel? viewModel;
    private ThemeService? themeService;
    private readonly ShutdownCallbackGuard callbackGuard = new();
    private bool isExiting;
    private bool servicesDisposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        engine = new AudioEngineService();
        themeService = new ThemeService();
        themeService.AppearanceChanged += OnAppearanceChanged;
        viewModel = new MainViewModel(engine, new SettingsService(), themeService, new StartupService());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Closing += OnMainWindowClosing;
        window.Show();
        tray = new TrayService();
        tray.Initialize(
            _ => callbackGuard.RunAsync(viewModel.ToggleInjectionAsync),
            id => viewModel.SelectedSource = viewModel.Sources.FirstOrDefault(source => source.Id == id),
            id => viewModel.SelectedMicrophone = viewModel.Microphones.FirstOrDefault(microphone => microphone.Id == id),
            OpenMainWindow,
            OpenSettings,
            ExitFromTray);
        engine.SnapshotChanged += OnEngineSnapshotChanged;
        tray.Update(engine.Snapshot);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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
        base.OnExit(e);
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!isExiting && MainWindow is not null)
        {
            e.Cancel = true;
            MainWindow.Hide();
        }
    }

    private void OpenMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void OpenSettings()
    {
        OpenMainWindow();
        if (MainWindow is not null)
        {
            new SettingsWindow { Owner = MainWindow, DataContext = viewModel }.ShowDialog();
        }
    }

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
            Dispatcher.BeginInvoke(new Action(() => callbackGuard.Run(() => viewModel?.RefreshSystemTheme())));
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => callbackGuard.Run(() =>
        {
            (MainWindow as MainWindow)?.RefreshBackdrop();
            if (MainWindow is not null)
            {
                foreach (Window ownedWindow in MainWindow.OwnedWindows)
                {
                    (ownedWindow as SettingsWindow)?.RefreshBackdrop();
                }
            }
        })));
    }
}
