namespace MusicMic.App;

/// <summary>One active process-loopback capture candidate reported by the native engine.</summary>
public sealed record AudioApplication(
    string Id,
    string DisplayName,
    uint ProcessId = 0,
    bool IsSpotify = false);
