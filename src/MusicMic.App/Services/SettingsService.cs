using MusicMic.Core;

namespace MusicMic.App.Services;

public sealed class SettingsService : ISettingsService
{
    private MusicMicSettings settings = MusicMicSettings.Default;

    public MusicMicSettings Load() => settings;

    public void Save(MusicMicSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.settings = settings;
    }
}
