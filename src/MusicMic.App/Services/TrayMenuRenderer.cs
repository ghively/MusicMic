using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicMic.App.Services;

/// <summary>Light/dark palette for the notification-area menu, following the Windows 11 flyout menu.</summary>
public sealed record TrayMenuAppearance(bool IsDark, Color Accent)
{
    public Color Surface => IsDark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);

    public Color Border => IsDark ? Color.FromArgb(0x40, 0x40, 0x40) : Color.FromArgb(0xE5, 0xE5, 0xE5);

    public Color Text => IsDark ? Color.FromArgb(0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x1A, 0x1A);

    public Color SecondaryText => IsDark ? Color.FromArgb(0x9A, 0x9A, 0x9A) : Color.FromArgb(0x70, 0x70, 0x70);

    public Color Hover => IsDark ? Color.FromArgb(0x3B, 0x3B, 0x3B) : Color.FromArgb(0xEC, 0xEC, 0xEC);

    public Color Separator => IsDark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE0, 0xE0, 0xE0);
}

/// <summary>
/// Renders the tray menu as a Windows 11 flyout menu: flat surface, hairline border, rounded
/// hover pills and an accent check mark, instead of the default gradient chrome.
/// </summary>
public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int CornerPreferenceRoundSmall = 3;

    public TrayMenuRenderer(TrayMenuAppearance appearance)
        : base(new TrayMenuColorTable(appearance))
    {
        Appearance = appearance;
        RoundedEdges = false;
    }

    public TrayMenuAppearance Appearance { get; }

    /// <summary>Gives a dropdown window the same rounded corners and dark mode the shell uses.</summary>
    public static void ApplyWindowAppearance(IntPtr handle, bool isDark)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int dark = isDark ? 1 : 0;
            int corners = CornerPreferenceRoundSmall;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Pre-Windows 11 hosts keep square menu corners.
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-Windows 11 hosts keep square menu corners.
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Appearance.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Appearance.Border);
        Rectangle bounds = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Windows 11 menus have no shaded gutter.
        using var brush = new SolidBrush(Appearance.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var bounds = new Rectangle(4, 1, e.Item.Bounds.Width - 8, e.Item.Bounds.Height - 2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = RoundedPath(bounds, 4);
        using var brush = new SolidBrush(Appearance.Hover);
        e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = SmoothingMode.Default;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Appearance.Text : Appearance.SecondaryText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? Appearance.SecondaryText : Appearance.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Appearance.Separator);
        int y = e.Item.Bounds.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Bounds.Width - 8, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle bounds = e.ImageRectangle;
        float scale = Math.Max(bounds.Height / 16f, 1f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Appearance.Accent, 1.6f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var points = new[]
        {
            new PointF(bounds.Left + (4f * scale), bounds.Top + (8f * scale)),
            new PointF(bounds.Left + (7f * scale), bounds.Top + (11f * scale)),
            new PointF(bounds.Left + (12f * scale), bounds.Top + (5f * scale)),
        };
        e.Graphics.DrawLines(pen, points);
        e.Graphics.SmoothingMode = SmoothingMode.Default;
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private sealed class TrayMenuColorTable(TrayMenuAppearance appearance) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => appearance.Surface;

        public override Color MenuBorder => appearance.Border;

        public override Color MenuItemBorder => appearance.Hover;

        public override Color MenuItemSelected => appearance.Hover;

        public override Color MenuItemSelectedGradientBegin => appearance.Hover;

        public override Color MenuItemSelectedGradientEnd => appearance.Hover;

        public override Color MenuItemPressedGradientBegin => appearance.Surface;

        public override Color MenuItemPressedGradientMiddle => appearance.Surface;

        public override Color MenuItemPressedGradientEnd => appearance.Surface;

        public override Color ImageMarginGradientBegin => appearance.Surface;

        public override Color ImageMarginGradientMiddle => appearance.Surface;

        public override Color ImageMarginGradientEnd => appearance.Surface;

        public override Color CheckBackground => appearance.Surface;

        public override Color CheckSelectedBackground => appearance.Hover;

        public override Color CheckPressedBackground => appearance.Hover;

        public override Color SeparatorDark => appearance.Separator;

        public override Color SeparatorLight => appearance.Separator;
    }
}
