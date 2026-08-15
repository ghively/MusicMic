using MusicMic.Core;

namespace MusicMic.App.Services;

public interface IThemeService
{
    ThemePreference CurrentTheme { get; }

    void ApplyTheme(ThemePreference theme);
}
