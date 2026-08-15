namespace MusicMic.Core;

public enum InjectionState
{
    Initializing,
    Ready,
    Injecting,
    SourceUnavailable,
    MicrophoneUnavailable,
    OutputUnavailable,
    Error,
}
