using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace MusicMic.App;

public partial class MainWindow : System.Windows.Window
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this, DataContext = DataContext }.ShowDialog();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int backdrop = DwmsbtMainWindow;
            _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Windows versions without the Windows 11 backdrop API use the themed surface fallback.
        }
        catch (EntryPointNotFoundException)
        {
            // Windows versions without the Windows 11 backdrop API use the themed surface fallback.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
