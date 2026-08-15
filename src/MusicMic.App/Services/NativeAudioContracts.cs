namespace MusicMic.App.Services;

/// <summary>Result values returned from the intentionally narrow native audio ABI.</summary>
public enum NativeAudioResult
{
    Ok = 0,
    NotInitialized = 1,
    InvalidArgument = 2,
    BufferTooSmall = 3,
    NotFound = 4,
    OutputUnavailable = 5,
    AudioFailure = 6,
    InternalError = 7,
    SourceUnavailable = 8,
    MicrophoneUnavailable = 9,
}

public enum NativeAudioState
{
    Initializing = 0,
    Ready = 1,
    Injecting = 2,
    SourceUnavailable = 3,
    MicrophoneUnavailable = 4,
    OutputUnavailable = 5,
    Error = 6,
}

public readonly record struct NativeAudioStatus(
    NativeAudioState State,
    bool SourceAvailable,
    bool MicrophoneAvailable,
    bool OutputAvailable,
    bool InjectionRequested,
    float SourcePeak,
    float MicrophonePeak,
    float OutputPeak = 0);

/// <summary>Managed representation of <c>MM_SourceInfo</c>; IDs are stable native identities.</summary>
public readonly record struct NativeAudioSource(
    string Id,
    string DisplayName,
    uint ProcessId,
    bool IsSpotify);

/// <summary>Managed representation of <c>MM_MicrophoneInfo</c>.</summary>
public readonly record struct NativeAudioMicrophone(
    string Id,
    string DisplayName,
    bool IsDefault);

/// <summary>
/// Injectable seam around the native ABI. It deliberately exposes no playback-routing operation.
/// The native engine is the sole owner of capture, mix, output, and device recovery.
/// </summary>
public interface INativeAudioApi
{
    NativeAudioResult Initialize();

    NativeAudioResult Shutdown();

    NativeAudioResult RefreshDevices();

    NativeAudioResult GetSourceCount(out uint count);

    NativeAudioResult GetSourceInfo(uint index, out NativeAudioSource source);

    NativeAudioResult GetMicrophoneCount(out uint count);

    NativeAudioResult GetMicrophoneInfo(uint index, out NativeAudioMicrophone microphone);

    NativeAudioResult SelectSource(string sourceId);

    NativeAudioResult SelectMicrophone(string microphoneId);

    NativeAudioResult SetSourceGain(float gain);

    NativeAudioResult SetMicrophoneGain(float gain);

    NativeAudioResult StartInjection();

    NativeAudioResult StopInjection();

    NativeAudioResult HandleSystemResume();

    NativeAudioResult GetStatus(out NativeAudioStatus status);

    string GetLastError();
}

public interface IAsyncDelay
{
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public static SystemAsyncDelay Instance { get; } = new();

    private SystemAsyncDelay() { }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
