using MusicMic.App.Presentation;
using MusicMic.App.Services;
using System.Windows;

namespace MusicMic.App;

public partial class App : System.Windows.Application
{
    private AudioEngineService? engine;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        engine = new AudioEngineService();
        var viewModel = new MainViewModel(engine, new SettingsService(), new ThemeService(), new StartupService());
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        engine?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
