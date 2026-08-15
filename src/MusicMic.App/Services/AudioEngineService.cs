using MusicMic.Core;

namespace MusicMic.App.Services;

/// <summary>
/// Projects the native engine's immutable status into UI state. This class never invokes APIs
/// that mutate application playback; capture and rendering remain exclusively native concerns.
/// </summary>
public sealed class AudioEngineService : IAudioEngineService
{
    private readonly INativeAudioApi nativeAudio;
    private readonly IAsyncDelay delay;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private Task? recoveryTask;
    private AudioEngineSnapshot snapshot = new(
        InjectionSnapshot.FromState(InjectionState.Initializing, false, false, false, false),
        [], [], null, null);
    private bool initialized;
    private bool restoreInjectionOnResume;

    public AudioEngineService()
        : this(new NativeAudioApi(), SystemAsyncDelay.Instance)
    {
    }

    public AudioEngineService(INativeAudioApi nativeAudio, IAsyncDelay? delay = null)
    {
        this.nativeAudio = nativeAudio ?? throw new ArgumentNullException(nameof(nativeAudio));
        this.delay = delay ?? SystemAsyncDelay.Instance;
    }

    public AudioEngineSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public event EventHandler<AudioEngineSnapshot>? SnapshotChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialized)
        {
            return RefreshAsync(cancellationToken);
        }

        NativeAudioResult result = nativeAudio.Initialize();
        if (result != NativeAudioResult.Ok)
        {
            PublishFailure(result);
            StartRecoveryLoop();
            return Task.CompletedTask;
        }

        initialized = true;
        return RefreshAsync(cancellationToken);
    }

    public void SelectSource(string? sourceId) =>
        Publish(Snapshot with { SelectedSourceId = sourceId });

    public void SelectMicrophone(string? microphoneId) =>
        Publish(Snapshot with { SelectedMicrophoneId = microphoneId });

    public void SetSourceGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
    }

    public void SetMicrophoneGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Start));
        NativeAudioResult result = nativeAudio.StartInjection();
        if (result != NativeAudioResult.Ok)
        {
            restoreInjectionOnResume = false;
            PublishFailure(result);
            StartRecoveryLoop();
            return;
        }

        restoreInjectionOnResume = true;
        await RefreshAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Stop));
        restoreInjectionOnResume = false;
        NativeAudioResult result = nativeAudio.StopInjection();
        if (result != NativeAudioResult.Ok)
        {
            PublishFailure(result);
            StartRecoveryLoop();
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    /// <summary>Refreshes actual native status; it does not guess or replace any device.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!initialized)
        {
            return Task.CompletedTask;
        }

        NativeAudioResult refreshResult = nativeAudio.RefreshDevices();
        NativeAudioResult statusResult = nativeAudio.GetStatus(out NativeAudioStatus nativeStatus);
        if (statusResult != NativeAudioResult.Ok)
        {
            PublishFailure(statusResult);
            StartRecoveryLoop();
            return Task.CompletedTask;
        }

        string? error = refreshResult == NativeAudioResult.Ok && nativeStatus.State is NativeAudioState.Ready or NativeAudioState.Injecting
            ? null
            : GetNativeError(refreshResult == NativeAudioResult.Ok ? NativeAudioResult.AudioFailure : refreshResult);
        var updated = Snapshot with
        {
            Injection = Project(nativeStatus),
            SourcePeak = nativeStatus.SourcePeak,
            MicrophonePeak = nativeStatus.MicrophonePeak,
            ErrorMessage = error,
        };
        Publish(updated);
        if (ShouldRecover(updated.Injection))
        {
            StartRecoveryLoop();
        }

        return Task.CompletedTask;
    }

    /// <summary>Re-enumerates after Windows resume and restores only a prior safe injection.</summary>
    public async Task HandlePowerResumeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
        AudioEngineSnapshot current = Snapshot;
        if (!restoreInjectionOnResume || !CanSafelyInject(current.Injection))
        {
            return;
        }

        NativeAudioResult start = nativeAudio.StartInjection();
        if (start != NativeAudioResult.Ok)
        {
            PublishFailure(start);
            StartRecoveryLoop();
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try
        {
            recoveryTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected while disposing the app.
        }

        if (initialized)
        {
            nativeAudio.Shutdown();
            initialized = false;
        }

        lifetime.Dispose();
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }

    private async Task RecoveryLoopAsync()
    {
        int attempt = 0;
        while (!lifetime.IsCancellationRequested)
        {
            await delay.Delay(ReconnectSchedule.Default.GetDelay(attempt++), lifetime.Token);
            await RefreshAsync(lifetime.Token);
            if (!ShouldRecover(Snapshot.Injection))
            {
                return;
            }
        }
    }

    private void StartRecoveryLoop()
    {
        if (lifetime.IsCancellationRequested || recoveryTask is { IsCompleted: false })
        {
            return;
        }

        recoveryTask = RecoveryLoopAsync();
    }

    private void PublishFailure(NativeAudioResult result)
    {
        AudioEngineSnapshot current = Snapshot;
        InjectionState state = result == NativeAudioResult.OutputUnavailable
            ? InjectionState.OutputUnavailable
            : InjectionState.Error;
        Publish(current with
        {
            Injection = InjectionSnapshot.FromState(
                state,
                current.Injection.IsSourceAvailable,
                current.Injection.IsMicrophoneAvailable,
                result != NativeAudioResult.OutputUnavailable && current.Injection.IsOutputAvailable,
                false),
            ErrorMessage = GetNativeError(result),
        });
    }

    private string GetNativeError(NativeAudioResult result)
    {
        string message = nativeAudio.GetLastError();
        return string.IsNullOrWhiteSpace(message)
            ? $"The native audio engine returned {result}."
            : message;
    }

    private void Publish(AudioEngineSnapshot updated)
    {
        lock (sync)
        {
            snapshot = updated;
        }

        SnapshotChanged?.Invoke(this, updated);
    }

    private static InjectionSnapshot Project(NativeAudioStatus status) =>
        InjectionSnapshot.FromState(
            status.State switch
            {
                NativeAudioState.Initializing => InjectionState.Initializing,
                NativeAudioState.Ready => InjectionState.Ready,
                NativeAudioState.Injecting => InjectionState.Injecting,
                NativeAudioState.SourceUnavailable => InjectionState.SourceUnavailable,
                NativeAudioState.MicrophoneUnavailable => InjectionState.MicrophoneUnavailable,
                NativeAudioState.OutputUnavailable => InjectionState.OutputUnavailable,
                _ => InjectionState.Error,
            },
            status.SourceAvailable,
            status.MicrophoneAvailable,
            status.OutputAvailable,
            status.InjectionRequested);

    private static bool CanSafelyInject(InjectionSnapshot injection) =>
        injection.IsSourceAvailable && injection.IsMicrophoneAvailable && injection.IsOutputAvailable;

    private static bool ShouldRecover(InjectionSnapshot injection) =>
        injection.State is InjectionState.SourceUnavailable or
            InjectionState.MicrophoneUnavailable or
            InjectionState.OutputUnavailable or
            InjectionState.Error;
}
