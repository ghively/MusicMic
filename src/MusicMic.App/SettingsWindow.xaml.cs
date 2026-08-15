using System.Windows;

namespace MusicMic.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RefreshBackdrop();
        Activated += (_, _) => RefreshBackdrop();
    }

    public void RefreshBackdrop() => WindowBackdrop.Apply(this, BackdropHost);

    private void CloseSettings(object sender, RoutedEventArgs e) => Close();
}
