using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicMic.App.Services;

/// <summary>
/// Draws the notification-area icon the way the shell draws its own: a monochrome glyph in the
/// taskbar's foreground colour, sized to the current small-icon metric, with an accent badge
/// while injection is running.
/// </summary>
internal static class TrayIconFactory
{
    public static Icon Create(bool isInjecting, Color accent, bool taskbarIsDark)
    {
        int size = SystemInformation.SmallIconSize.Width;
        if (size < 16)
        {
            size = 16;
        }

        Color foreground = taskbarIsDark ? Color.White : Color.FromArgb(0xE4, 0, 0, 0);
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            float scale = size / 16f;
            DrawMicrophone(graphics, scale, foreground, isInjecting);
            if (isInjecting)
            {
                DrawBadge(graphics, scale, accent, taskbarIsDark);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var unowned = Icon.FromHandle(handle);
            return (Icon)unowned.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Reads the taskbar/system theme, which is tracked separately from the app theme.</summary>
    public static bool TaskbarIsDark()
    {
        using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is not int value || value == 0;
    }

    private static void DrawMicrophone(Graphics graphics, float scale, Color foreground, bool filled)
    {
        using var brush = new SolidBrush(foreground);
        using var pen = new Pen(foreground, 1.5f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        // Capsule.
        var capsule = new RectangleF(6f * scale, 1.8f * scale, 4f * scale, 7.4f * scale);
        using (GraphicsPath path = RoundedPath(capsule, 2f * scale))
        {
            if (filled)
            {
                graphics.FillPath(brush, path);
            }
            else
            {
                graphics.DrawPath(pen, path);
            }
        }

        // Cradle: the lower half of a circle around the capsule.
        var cradle = new RectangleF(4.1f * scale, 4.1f * scale, 7.8f * scale, 7.8f * scale);
        graphics.DrawArc(pen, cradle, 20f, 140f);

        // Stand and base.
        graphics.DrawLine(pen, 8f * scale, 11.9f * scale, 8f * scale, 13.4f * scale);
        graphics.DrawLine(pen, 5.6f * scale, 13.9f * scale, 10.4f * scale, 13.9f * scale);
    }

    private static void DrawBadge(Graphics graphics, float scale, Color accent, bool taskbarIsDark)
    {
        float diameter = 5.6f * scale;
        var bounds = new RectangleF(16f * scale - diameter, 16f * scale - diameter, diameter, diameter);
        using var halo = new SolidBrush(taskbarIsDark ? Color.FromArgb(0, 0, 0) : Color.FromArgb(255, 255, 255));
        using var fill = new SolidBrush(accent);
        graphics.FillEllipse(halo, RectangleF.Inflate(bounds, 1f * scale, 1f * scale));
        graphics.FillEllipse(fill, bounds);
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
