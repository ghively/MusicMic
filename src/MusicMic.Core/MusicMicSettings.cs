namespace MusicMic.Core;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed record MusicMicSettings
{
    public static MusicMicSettings Default => new();

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public string? SelectedSource { get; init; }

    public string? SelectedMicrophoneId { get; init; }

    public double SourceVolume { get; init; } = 0.70;

    public double MicrophoneVolume { get; init; } = 1.00;

    public bool StartWithWindows { get; init; }
}
