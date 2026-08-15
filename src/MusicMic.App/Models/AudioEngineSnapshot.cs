using MusicMic.Core;

namespace MusicMic.App;

public sealed record AudioEngineSnapshot(
    InjectionSnapshot Injection,
    IReadOnlyList<AudioApplication> Sources,
    IReadOnlyList<MicrophoneDevice> Microphones,
    string? SelectedSourceId,
    string? SelectedMicrophoneId,
    float SourcePeak = 0,
    float MicrophonePeak = 0,
    float OutputPeak = 0,
    string? ErrorMessage = null);
