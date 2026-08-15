using MusicMic.Core;

namespace MusicMic.App;

public sealed record AudioEngineSnapshot(
    InjectionSnapshot Injection,
    IReadOnlyList<AudioApplication> Sources,
    IReadOnlyList<MicrophoneDevice> Microphones,
    string? SelectedSourceId,
    string? SelectedMicrophoneId);
