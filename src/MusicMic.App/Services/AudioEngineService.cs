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
    private float sourceGain = 0.7f;
    private float microphoneGain = 1f;

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

    public void SelectSource(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            Publish(Snapshot with { SelectedSourceId = null });
            return;
        }

        if (initialized)
        {
            NativeAudioResult result = nativeAudio.SelectSource(sourceId);
            if (result != NativeAudioResult.Ok)
            {
                PublishFailure(result);
                StartRecoveryLoop();
                return;
            }
        }

        Publish(Snapshot with { SelectedSourceId = sourceId, ErrorMessage = null });
    }

    public void SelectMicrophone(string? microphoneId)
    {
        if (string.IsNullOrWhiteSpace(microphoneId))
        {
            Publish(Snapshot with { SelectedMicrophoneId = null });
            return;
        }

        if (initialized)
        {
            NativeAudioResult result = nativeAudio.SelectMicrophone(microphoneId);
            if (result != NativeAudioResult.Ok)
            {
                PublishFailure(result);
                StartRecoveryLoop();
                return;
            }
        }

        Publish(Snapshot with { SelectedMicrophoneId = microphoneId, ErrorMessage = null });
    }

    public void SetSourceGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
        sourceGain = (float)gain;
        ApplyGainIfInitialized(nativeAudio.SetSourceGain, sourceGain);
    }

    public void SetMicrophoneGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
        microphoneGain = (float)gain;
        ApplyGainIfInitialized(nativeAudio.SetMicrophoneGain, microphoneGain);
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

        NativeAudioResult discoveryResult = DiscoverDevices(out IReadOnlyList<AudioApplication> sources, out IReadOnlyList<MicrophoneDevice> microphones);
        if (discoveryResult != NativeAudioResult.Ok)
        {
            PublishFailure(discoveryResult);
            StartRecoveryLoop();
            return Task.CompletedTask;
        }

        AudioEngineSnapshot previous = Snapshot;
        string? sourceId = ChooseSourceId(previous.SelectedSourceId, sources);
        string? microphoneId = ChooseMicrophoneId(previous.SelectedMicrophoneId, microphones);
        NativeAudioResult configurationResult = ApplySelectionAndGains(previous, sourceId, microphoneId);
        if (configurationResult != NativeAudioResult.Ok)
        {
            PublishFailure(configurationResult);
            StartRecoveryLoop();
            return Task.CompletedTask;
        }

        NativeAudioResult reportResult = refreshResult != NativeAudioResult.Ok ? refreshResult : StateResult(nativeStatus);
        string? error = reportResult == NativeAudioResult.Ok ? null : GetNativeError(reportResult);
        var updated = Snapshot with
        {
            Injection = Project(nativeStatus, reportResult),
            Sources = sources,
            Microphones = microphones,
            SelectedSourceId = sourceId,
            SelectedMicrophoneId = microphoneId,
            SourcePeak = nativeStatus.SourcePeak,
            MicrophonePeak = nativeStatus.MicrophonePeak,
            OutputPeak = nativeStatus.OutputPeak,
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
        cancellationToken.ThrowIfCancellationRequested();
        NativeAudioResult resume = nativeAudio.HandleSystemResume();
        if (resume != NativeAudioResult.Ok)
        {
            PublishFailure(resume);
            StartRecoveryLoop();
            return;
        }

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
        InjectionState state = result switch
        {
            NativeAudioResult.SourceUnavailable => InjectionState.SourceUnavailable,
            NativeAudioResult.MicrophoneUnavailable => InjectionState.MicrophoneUnavailable,
            NativeAudioResult.OutputUnavailable => InjectionState.OutputUnavailable,
            _ => InjectionState.Error,
        };
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

    private static InjectionSnapshot Project(NativeAudioStatus status, NativeAudioResult result = NativeAudioResult.Ok) =>
        InjectionSnapshot.FromState(
            result switch
            {
                NativeAudioResult.SourceUnavailable => InjectionState.SourceUnavailable,
                NativeAudioResult.MicrophoneUnavailable => InjectionState.MicrophoneUnavailable,
                NativeAudioResult.OutputUnavailable => InjectionState.OutputUnavailable,
                NativeAudioResult.AudioFailure or NativeAudioResult.InternalError => InjectionState.Error,
                _ => status.State switch
                {
                    NativeAudioState.Initializing => InjectionState.Initializing,
                    NativeAudioState.Ready => InjectionState.Ready,
                    NativeAudioState.Injecting => InjectionState.Injecting,
                    NativeAudioState.SourceUnavailable => InjectionState.SourceUnavailable,
                    NativeAudioState.MicrophoneUnavailable => InjectionState.MicrophoneUnavailable,
                    NativeAudioState.OutputUnavailable => InjectionState.OutputUnavailable,
                    _ => InjectionState.Error,
                },
            },
            result != NativeAudioResult.SourceUnavailable && status.SourceAvailable,
            result != NativeAudioResult.MicrophoneUnavailable && status.MicrophoneAvailable,
            result != NativeAudioResult.OutputUnavailable && status.OutputAvailable,
            status.InjectionRequested);

    private NativeAudioResult DiscoverDevices(
        out IReadOnlyList<AudioApplication> sources,
        out IReadOnlyList<MicrophoneDevice> microphones)
    {
        NativeAudioResult sourceResult = nativeAudio.GetSourceCount(out uint sourceCount);
        if (sourceResult != NativeAudioResult.Ok)
        {
            sources = [];
            microphones = [];
            return sourceResult;
        }

        const uint maximumDeviceCount = 512;
        if (sourceCount > maximumDeviceCount)
        {
            sources = [];
            microphones = [];
            return NativeAudioResult.InternalError;
        }

        var discoveredSources = new List<AudioApplication>((int)sourceCount);
        for (uint index = 0; index < sourceCount; index++)
        {
            NativeAudioResult result = nativeAudio.GetSourceInfo(index, out NativeAudioSource source);
            if (result != NativeAudioResult.Ok)
            {
                sources = [];
                microphones = [];
                return result;
            }

            if (!string.IsNullOrWhiteSpace(source.Id))
            {
                discoveredSources.Add(new AudioApplication(source.Id, source.DisplayName, source.ProcessId, source.IsSpotify));
            }
        }

        NativeAudioResult microphoneResult = nativeAudio.GetMicrophoneCount(out uint microphoneCount);
        if (microphoneResult != NativeAudioResult.Ok || microphoneCount > maximumDeviceCount)
        {
            sources = [];
            microphones = [];
            return microphoneResult == NativeAudioResult.Ok ? NativeAudioResult.InternalError : microphoneResult;
        }

        var discoveredMicrophones = new List<MicrophoneDevice>((int)microphoneCount);
        for (uint index = 0; index < microphoneCount; index++)
        {
            NativeAudioResult result = nativeAudio.GetMicrophoneInfo(index, out NativeAudioMicrophone microphone);
            if (result != NativeAudioResult.Ok)
            {
                sources = [];
                microphones = [];
                return result;
            }

            if (!string.IsNullOrWhiteSpace(microphone.Id))
            {
                discoveredMicrophones.Add(new MicrophoneDevice(microphone.Id, microphone.DisplayName, microphone.IsDefault));
            }
        }

        sources = discoveredSources;
        microphones = discoveredMicrophones;
        return NativeAudioResult.Ok;
    }

    private NativeAudioResult ApplySelectionAndGains(
        AudioEngineSnapshot previous,
        string? sourceId,
        string? microphoneId)
    {
        if (!string.IsNullOrWhiteSpace(sourceId) && !string.Equals(sourceId, previous.SelectedSourceId, StringComparison.Ordinal))
        {
            NativeAudioResult selection = nativeAudio.SelectSource(sourceId);
            if (selection != NativeAudioResult.Ok)
            {
                return selection;
            }
        }

        if (!string.IsNullOrWhiteSpace(microphoneId) && !string.Equals(microphoneId, previous.SelectedMicrophoneId, StringComparison.Ordinal))
        {
            NativeAudioResult selection = nativeAudio.SelectMicrophone(microphoneId);
            if (selection != NativeAudioResult.Ok)
            {
                return selection;
            }
        }

        NativeAudioResult sourceGainResult = nativeAudio.SetSourceGain(sourceGain);
        if (sourceGainResult != NativeAudioResult.Ok)
        {
            return sourceGainResult;
        }

        return nativeAudio.SetMicrophoneGain(microphoneGain);
    }

    private void ApplyGainIfInitialized(Func<float, NativeAudioResult> setter, float gain)
    {
        if (!initialized)
        {
            return;
        }

        NativeAudioResult result = setter(gain);
        if (result != NativeAudioResult.Ok)
        {
            PublishFailure(result);
            StartRecoveryLoop();
        }
    }

    private static string? ChooseSourceId(string? currentId, IReadOnlyList<AudioApplication> sources) =>
        !string.IsNullOrWhiteSpace(currentId)
            ? currentId
            : sources.FirstOrDefault(source => source.IsSpotify)?.Id ?? sources.FirstOrDefault()?.Id;

    private static string? ChooseMicrophoneId(string? currentId, IReadOnlyList<MicrophoneDevice> microphones) =>
        !string.IsNullOrWhiteSpace(currentId)
            ? currentId
            : microphones.FirstOrDefault(microphone => microphone.IsDefault)?.Id ?? microphones.FirstOrDefault()?.Id;

    private static NativeAudioResult StateResult(NativeAudioStatus status) => status.State switch
    {
        NativeAudioState.SourceUnavailable => NativeAudioResult.SourceUnavailable,
        NativeAudioState.MicrophoneUnavailable => NativeAudioResult.MicrophoneUnavailable,
        NativeAudioState.OutputUnavailable => NativeAudioResult.OutputUnavailable,
        NativeAudioState.Error => NativeAudioResult.AudioFailure,
        _ => NativeAudioResult.Ok,
    };

    private static bool CanSafelyInject(InjectionSnapshot injection) =>
        injection.IsSourceAvailable && injection.IsMicrophoneAvailable && injection.IsOutputAvailable;

    private static bool ShouldRecover(InjectionSnapshot injection) =>
        injection.State is InjectionState.SourceUnavailable or
            InjectionState.MicrophoneUnavailable or
            InjectionState.OutputUnavailable or
            InjectionState.Error;
}
