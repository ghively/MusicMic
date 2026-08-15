using Xunit;

namespace MusicMic.Core.Tests;

public sealed class MusicMicSettingsTests
{
    [Fact]
    public void First_launch_defaults_match_the_product_specification()
    {
        MusicMicSettings settings = MusicMicSettings.Default;

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(0.70, settings.SourceVolume);
        Assert.Equal(1.00, settings.MicrophoneVolume);
        Assert.False(settings.StartWithWindows);
        Assert.Null(settings.SelectedSource);
        Assert.Null(settings.SelectedMicrophoneId);
    }

    [Fact]
    public void Changing_a_settings_copy_does_not_mutate_the_original_snapshot()
    {
        MusicMicSettings defaults = MusicMicSettings.Default;

        MusicMicSettings changed = defaults with { SourceVolume = 0.25 };

        Assert.Equal(0.70, defaults.SourceVolume);
        Assert.Equal(0.25, changed.SourceVolume);
    }
}
