using System.Windows;

namespace MusicMic.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() => InitializeComponent();

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();
    }
}
