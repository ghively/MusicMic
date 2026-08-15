using Microsoft.Win32;
using MusicMic.Core;
using System.Windows;

namespace MusicMic.App.Services;

public sealed class ThemeService : IThemeService
{
    private const string ThemeDictionaryPrefix = "Themes/";

    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public void ApplyTheme(ThemePreference theme)
    {
        CurrentTheme = theme;

        if (System.Windows.Application.Current is null)
        {
            return;
        }

        ThemePreference effectiveTheme = theme == ThemePreference.System
            ? ReadSystemTheme()
            : theme;
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"{ThemeDictionaryPrefix}{effectiveTheme}.xaml", UriKind.Relative),
        };
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
    }

    private static ThemePreference ReadSystemTheme()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0
            ? ThemePreference.Dark
            : ThemePreference.Light;
    }
}
