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
    private bool isExiting;
    private bool servicesDisposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        engine = new AudioEngineService();
        viewModel = new MainViewModel(engine, new SettingsService(), new ThemeService(), new StartupService());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Closing += OnMainWindowClosing;
        window.Show();
        tray = new TrayService();
        tray.Initialize(
            _ => viewModel.ToggleInjectionAsync(),
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
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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
            Dispatcher.BeginInvoke(new Action(async () => await engine.HandlePowerResumeAsync()));
        }
    }

    private void OnEngineSnapshotChanged(object? sender, AudioEngineSnapshot snapshot)
    {
        if (Dispatcher.CheckAccess())
        {
            tray?.Update(snapshot);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => tray?.Update(snapshot)));
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.BeginInvoke(new Action(() => viewModel?.RefreshSystemTheme()));
        }
    }
}
