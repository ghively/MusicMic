using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MusicMic.App;

/// <summary>
/// Applies the Windows 11 system backdrop to a borderless window. MusicMic uses the transient
/// (acrylic) backdrop and rounded corners, which is what the shell itself uses for notification
/// area flyouts, so the window is drawn by DWM rather than painted to imitate it.
/// </summary>
internal static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int CornerPreferenceRound = 2;
    private const int BackdropTypeTransientWindow = 3;

    public static void Apply(Window window, Border host)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (SystemParameters.HighContrast)
        {
            UseFallback(host);
            return;
        }

        try
        {
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            int dark = System.Windows.Application.Current?.TryFindResource("IsDarkTheme") is true ? 1 : 0;
            int backdrop = BackdropTypeTransientWindow;
            int corners = CornerPreferenceRound;
            bool applied = DwmExtendFrameIntoClientArea(handle, ref margins) >= 0
                && DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) >= 0
                && DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) >= 0
                && DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int)) >= 0;

            if (applied)
            {
                // Let the system backdrop show through; a painted tint on top would double it.
                host.Background = Brushes.Transparent;
            }
            else
            {
                UseFallback(host);
            }
        }
        catch (DllNotFoundException)
        {
            UseFallback(host);
        }
        catch (EntryPointNotFoundException)
        {
            UseFallback(host);
        }
    }

    private static void UseFallback(Border host) => host.SetResourceReference(Border.BackgroundProperty, "BackdropFallbackBrush");

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
