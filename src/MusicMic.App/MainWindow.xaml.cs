using System.Windows;

namespace MusicMic.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RefreshBackdrop();
        Activated += (_, _) => RefreshBackdrop();
    }

    public void RefreshBackdrop() => WindowBackdrop.Apply(this, BackdropHost);

    private void OpenSettings(object sender, RoutedEventArgs e) =>
        new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();

    private void CloseToTray(object sender, RoutedEventArgs e) => Close();
}
