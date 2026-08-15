namespace MusicMic.App;

/// <summary>One physical recording endpoint reported by the native engine.</summary>
public sealed record MicrophoneDevice(string Id, string DisplayName, bool IsDefault = false);
