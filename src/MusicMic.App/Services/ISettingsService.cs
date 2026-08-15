using MusicMic.Core;

namespace MusicMic.App.Services;

public interface ISettingsService
{
    MusicMicSettings Load();

    void Save(MusicMicSettings settings);
}
