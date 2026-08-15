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

    private static void ApplySystemAccent(ResourceDictionary resources, ThemePreference effectiveTheme)
    {
        if (SystemParameters.HighContrast)
        {
            resources["WindowBackgroundBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["BackdropFallbackBrush"] = System.Windows.SystemColors.WindowBrush;
            resources["PrimaryTextBrush"] = System.Windows.SystemColors.WindowTextBrush;
            resources["SecondaryTextBrush"] = System.Windows.SystemColors.GrayTextBrush;
            resources["AccentBrush"] = System.Windows.SystemColors.HighlightBrush;
            resources["AccentForegroundBrush"] = System.Windows.SystemColors.HighlightTextBrush;
            return;
        }

        const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        if (key?.GetValue("AccentColorMenu") is not int storedColor)
        {
            return;
        }

        uint packed = unchecked((uint)storedColor);
        MediaColor accent = MediaColor.FromRgb(
            (byte)(packed & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)((packed >> 16) & 0xFF));
        resources["AccentBrush"] = FrozenBrush(accent);
        resources["AccentHoverBrush"] = FrozenBrush(ThemeColorUtilities.Blend(
            accent,
            effectiveTheme == ThemePreference.Dark ? Colors.White : Colors.Black,
            effectiveTheme == ThemePreference.Dark ? 0.14 : 0.08));
        resources["AccentPressedBrush"] = FrozenBrush(ThemeColorUtilities.Blend(accent, Colors.Black, 0.18));
        resources["AccentForegroundBrush"] = FrozenBrush(ThemeColorUtilities.ReadableForeground(accent));
        resources["FocusStrokeBrush"] = FrozenBrush(MediaColor.FromArgb(190, accent.R, accent.G, accent.B));
        resources["FocusColor"] = accent;
        resources["AccentSubtleBrush"] = FrozenBrush(MediaColor.FromArgb(
            effectiveTheme == ThemePreference.Dark ? (byte)58 : (byte)28,
            accent.R,
            accent.G,
            accent.B));
    }

    private static SolidColorBrush FrozenBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
