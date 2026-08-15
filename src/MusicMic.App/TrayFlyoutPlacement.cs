using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MusicMic.App;

/// <summary>
/// Places the flyout in the notification-area corner of the display the pointer is on, the way
/// the shell places its own volume and network flyouts. All arithmetic is done in physical
/// pixels through Win32 so per-monitor DPI and taskbar edges are handled correctly.
/// </summary>
internal static class TrayFlyoutPlacement
{
    private const double EdgeMargin = 12;
    private const double FallbackHeight = 420;

    private const int MonitorDefaultToNearest = 2;
    private const int MonitorDpiTypeEffective = 0;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Keeps the flyout out of Alt+Tab and the taskbar, like a shell flyout.</summary>
    public static void HideFromApplicationSwitcher(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr style = GetWindowLongPtr(handle, GwlExStyle);
            SetWindowLongPtr(handle, GwlExStyle, (IntPtr)(style.ToInt64() | WsExToolWindow));
        }
        catch (EntryPointNotFoundException)
        {
            // Leave the default window styles in place on hosts without the 64-bit entry points.
        }
    }

    public static void MoveToNotificationArea(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        if (handle == IntPtr.Zero || !GetCursorPos(out NativePoint cursor))
        {
            return;
        }

        IntPtr monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        double scale = GetScale(monitor, window);
        double height = window.ActualHeight > 0 ? window.ActualHeight : FallbackHeight;
        int windowWidth = (int)Math.Round(window.Width * scale);
        int windowHeight = (int)Math.Round(height * scale);
        int margin = (int)Math.Round(EdgeMargin * scale);

        // rcWork already excludes the taskbar, so anchoring to its corner puts the flyout exactly
        // where the shell puts its own, whichever edge the taskbar lives on.
        int x = info.Work.Right - windowWidth - margin;
        int y = info.Work.Bottom - windowHeight - margin;
        if (info.Work.Top > info.Monitor.Top)
        {
            y = info.Work.Top + margin;
        }

        if (info.Work.Left > info.Monitor.Left)
        {
            x = info.Work.Left + margin;
        }

        x = Clamp(x, info.Work.Left + margin, info.Work.Right - windowWidth - margin);
        y = Clamp(y, info.Work.Top + margin, info.Work.Bottom - windowHeight - margin);
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static int Clamp(int value, int low, int high) =>
        high < low ? low : value < low ? low : value > high ? high : value;

    private static double GetScale(IntPtr monitor, Window window)
    {
        try
        {
            if (GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96d;
            }
        }
        catch (DllNotFoundException)
        {
            // Fall through to the WPF visual scale below.
        }
        catch (EntryPointNotFoundException)
        {
            // Fall through to the WPF visual scale below.
        }

        double visualScale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        return visualScale > 0 ? visualScale : 1d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
