namespace MusicMic.App.Services;

public sealed class StartupService : IStartupService
{
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
