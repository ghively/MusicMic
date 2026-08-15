using MusicMic.Core;

namespace MusicMic.App.Services;

public interface IThemeService
{
    event EventHandler? AppearanceChanged;

    ThemePreference CurrentTheme { get; }

    void ApplyTheme(ThemePreference theme);

    void RefreshSystemTheme();
}
