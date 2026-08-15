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
    float MicrophonePeak);

/// <summary>
/// Injectable seam around the native ABI. It deliberately exposes no playback-routing operation.
/// The native engine is the sole owner of capture, mix, output, and device recovery.
/// </summary>
public interface INativeAudioApi
{
    NativeAudioResult Initialize();

    NativeAudioResult Shutdown();

    NativeAudioResult RefreshDevices();

    NativeAudioResult StartInjection();

    NativeAudioResult StopInjection();

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
