using Microsoft.Win32;
using MusicMic.Core;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace MusicMic.App.Services;

public sealed class ThemeService : IThemeService
{
    private const string ThemeDictionaryPrefix = "Themes/";

    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public event EventHandler? AppearanceChanged;

    public void ApplyTheme(ThemePreference theme)
    {
        CurrentTheme = theme;

        if (System.Windows.Application.Current is null)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        ThemePreference effectiveTheme = theme == ThemePreference.System
            ? ReadSystemTheme()
            : theme;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"{ThemeDictionaryPrefix}{effectiveTheme}.xaml", UriKind.Relative),
        };
        ApplySystemAccent(replacement, effectiveTheme);
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(
            dictionary => dictionary.Source?.OriginalString.StartsWith(
                ThemeDictionaryPrefix,
                StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Insert(0, replacement);
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemTheme()
    {
        ApplyTheme(CurrentTheme);
    }

    private static ThemePreference ReadSystemTheme()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0
            ? ThemePreference.Dark
            : ThemePreference.Light;
    }

    /// <summary>
    /// Resolves the accent shade Windows itself would use on the given surface: the first dark
    /// shade on light surfaces and the second light shade on dark surfaces, exactly as the WinUI
    /// AccentFillColorDefault resource is defined.
    /// </summary>
    public static MediaColor ResolveAccentColor(bool forDarkSurface)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);

        // AccentPalette holds eight RGBA shades: light3, light2, light1, accent, dark1..dark3.
        if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
        {
            int offset = forDarkSurface ? 4 : 16;
            return MediaColor.FromRgb(palette[offset], palette[offset + 1], palette[offset + 2]);
        }

        if (key?.GetValue("AccentColorMenu") is int storedColor)
        {
            uint packed = unchecked((uint)storedColor);
            return MediaColor.FromRgb(
                (byte)(packed & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)((packed >> 16) & 0xFF));
        }

        return forDarkSurface ? MediaColor.FromRgb(0x60, 0xCD, 0xFF) : MediaColor.FromRgb(0x00, 0x5F, 0xB8);
    }

    private static void ApplySystemAccent(ResourceDictionary resources, ThemePreference effectiveTheme)
    {
        if (SystemParameters.HighContrast)
        {
            resources["WindowBackgroundBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["BackdropFallbackBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["CardFillBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["PopupFillBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["ControlFillBrush"] = System.Windows.SystemColors.ControlBrush;
            resources["PrimaryTextBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["SecondaryTextBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["TertiaryTextBrush"] = System.Windows.SystemColors.GrayTextBrush;
            resources["OuterBorderBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["CardStrokeBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["DividerBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["FocusStrokeBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["AccentBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["AccentHoverBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["AccentPressedBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["AccentForegroundBrush"] = System.Windows.SystemColors.HighlightTextBrush;
            resources["AccentTextBrush"] = System.Windows.SystemColors.HotTrackBrush;
            return;
        }

        bool isDark = effectiveTheme == ThemePreference.Dark;
        MediaColor accent = ResolveAccentColor(isDark);
        resources["AccentBrush"] = FrozenBrush(accent);

        // WinUI derives the hover and pressed shades by lowering the accent's opacity.
        resources["AccentHoverBrush"] = FrozenBrush(MediaColor.FromArgb(230, accent.R, accent.G, accent.B));
        resources["AccentPressedBrush"] = FrozenBrush(MediaColor.FromArgb(204, accent.R, accent.G, accent.B));
        resources["AccentForegroundBrush"] = FrozenBrush(ThemeColorUtilities.ReadableForeground(accent));
        resources["AccentSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(
            isDark ? (byte)38 : (byte)26,
            accent.R,
            accent.G,
            accent.B));
        resources["AccentTextBrush"] = FrozenBrush(ThemeColorUtilities.Blend(
            accent,
            isDark ? Colors.White : Colors.Black,
            0.35));
        resources["FocusColor"] = accent;
    }

    private static SolidColorBrush FrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
