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
            OpenMainWindow,
            OpenSettings,
            ExitFromTray);
        engine.SnapshotChanged += (_, snapshot) => tray.Update(snapshot);
        tray.Update(engine.Snapshot);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        tray?.Dispose();
        engine?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    private void ExitFromTray()
    {
        isExiting = true;
        Shutdown();
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && engine is not null)
        {
            await engine.HandlePowerResumeAsync();
        }
    }
}
