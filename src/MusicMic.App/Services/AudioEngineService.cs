using MusicMic.Core;

namespace MusicMic.App.Services;

/// <summary>
/// Serializes every native ABI call on background threads and projects only observed native
/// state to the UI. It never exposes or invokes an application playback-routing operation.
/// </summary>
public sealed class AudioEngineService : IAudioEngineService
{
    private static readonly TimeSpan HealthyPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly INativeAudioApi nativeAudio;
    private readonly IAsyncDelay delay;
    private readonly IDiagnosticLogger diagnostics;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim nativeGate = new(1, 1);
    private readonly object sync = new();
    private Task pendingConfiguration = Task.CompletedTask;
    private Task? monitorTask;
    private Task? disposalTask;
    private AudioEngineSnapshot snapshot = new(
        InjectionSnapshot.FromState(InjectionState.Initializing, false, false, false, false),
        [], [], null, null);
    private bool initialized;
    private bool restoreInjectionOnResume;
    private bool resumeRestorePending;
    private bool disposed;
    private bool sourceSelectionApplied;
    private bool microphoneSelectionApplied;
    private string? appliedSourceId;
    private string? appliedMicrophoneId;
    private float sourceGain = 0.7f;
    private float microphoneGain = 1f;

    public AudioEngineService()
        : this(new NativeAudioApi(), SystemAsyncDelay.Instance, DiagnosticLogger.CreateDefault())
    {
    }

    public AudioEngineService(
        INativeAudioApi nativeAudio,
        IAsyncDelay? delay = null,
        IDiagnosticLogger? diagnostics = null)
    {
        this.nativeAudio = nativeAudio ?? throw new ArgumentNullException(nameof(nativeAudio));
        this.delay = delay ?? SystemAsyncDelay.Instance;
        this.diagnostics = diagnostics ?? NullDiagnosticLogger.Instance;
        this.diagnostics.Write("startup", "MusicMic managed audio service started.");
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await RunNativeAsync(() =>
        {
            if (initialized)
            {
                RefreshCore();
            }
            else
            {
                InitializeCore();
            }
        }, cancellationToken).ConfigureAwait(false);
        StartMonitorLoop();
    }

    public void SelectSource(string? sourceId)
    {
        ThrowIfDisposed();
        if (Snapshot.Injection.IsInjectionActive)
        {
            return;
        }

        string? normalized = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId;
        Publish(Snapshot with { SelectedSourceId = normalized, ErrorMessage = null });
        diagnostics.Write("source-selection", normalized ?? "No source selected.");
        QueueConfiguration();
    }

    public void SelectMicrophone(string? microphoneId)
    {
        ThrowIfDisposed();
        if (Snapshot.Injection.IsInjectionActive)
        {
            return;
        }

        string? normalized = string.IsNullOrWhiteSpace(microphoneId) ? null : microphoneId;
        Publish(Snapshot with { SelectedMicrophoneId = normalized, ErrorMessage = null });
        diagnostics.Write("microphone-selection", normalized ?? "No microphone selected.");
        QueueConfiguration();
    }

    public void SetSourceGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
        ThrowIfDisposed();
        sourceGain = (float)gain;
        QueueConfiguration();
    }

    public void SetMicrophoneGain(double gain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
        ThrowIfDisposed();
        microphoneGain = (float)gain;
        QueueConfiguration();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Start));
        await GetPendingConfiguration().WaitAsync(cancellationToken).ConfigureAwait(false);
        await RunNativeAsync(() =>
        {
            NativeAudioResult result = nativeAudio.StartInjection();
            if (result != NativeAudioResult.Ok)
            {
                restoreInjectionOnResume = false;
                resumeRestorePending = false;
                PublishFailure(result);
                return;
            }

            restoreInjectionOnResume = true;
            resumeRestorePending = false;
            diagnostics.Write("injection-started", "Native engine accepted the injection request.");
            RefreshCore();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Stop));
        await RunNativeAsync(() =>
        {
            restoreInjectionOnResume = false;
            resumeRestorePending = false;
            NativeAudioResult result = nativeAudio.StopInjection();
            if (result != NativeAudioResult.Ok)
            {
                PublishFailure(result);
                return;
            }

            diagnostics.Write("injection-stopped", "Native engine stopped injection.");
            RefreshCore();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes actual native status; it does not guess or replace any device.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return RunNativeAsync(RefreshCore, cancellationToken);
    }

    /// <summary>Re-enumerates after Windows resume and restores only a prior safe injection.</summary>
    public Task HandlePowerResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return RunNativeAsync(() =>
        {
            diagnostics.Write("system-resume", "Rebuilding audio discovery after Windows resume.");
            resumeRestorePending = restoreInjectionOnResume;
            NativeAudioResult resume = nativeAudio.HandleSystemResume();
            if (resume != NativeAudioResult.Ok)
            {
                PublishFailure(resume);
                return;
            }

            appliedSourceId = null;
            appliedMicrophoneId = null;
            sourceSelectionApplied = false;
            microphoneSelectionApplied = false;
            RefreshCore();
            TryRestoreInjectionAfterResumeCore();
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        lifetime.Cancel();
        Task? monitor;
        Task configuration;
        lock (sync)
        {
            disposed = true;
            monitor = monitorTask;
            configuration = pendingConfiguration;
        }

        await IgnoreCancellationAsync(monitor).ConfigureAwait(false);
        await IgnoreCancellationAsync(configuration).ConfigureAwait(false);
        await RunNativeAsync(() =>
        {
            if (initialized)
            {
                NativeAudioResult result = nativeAudio.Shutdown();
                diagnostics.Write("audio-shutdown", $"Native shutdown result: {result}.");
                initialized = false;
            }
        }, CancellationToken.None).ConfigureAwait(false);

        SnapshotChanged = null;
        diagnostics.Write("shutdown", "MusicMic managed audio service stopped.");
        diagnostics.Dispose();
        nativeGate.Dispose();
        lifetime.Dispose();
    }

    private void InitializeCore()
    {
        diagnostics.Write("audio-initialize", "Initializing the native audio engine.");
        NativeAudioResult result = nativeAudio.Initialize();
        if (result != NativeAudioResult.Ok)
        {
            PublishFailure(result);
            return;
        }

        initialized = true;
        appliedSourceId = null;
        appliedMicrophoneId = null;
        sourceSelectionApplied = false;
        microphoneSelectionApplied = false;
        diagnostics.Write("audio-initialize", "Native audio engine initialized.");
        RefreshCore();
    }

    private void RefreshCore()
    {
        if (!initialized)
        {
            return;
        }

        NativeAudioResult refreshResult = nativeAudio.RefreshDevices();
        NativeAudioResult statusResult = nativeAudio.GetStatus(out NativeAudioStatus nativeStatus);
        if (statusResult != NativeAudioResult.Ok)
        {
            PublishFailure(statusResult);
            return;
        }

        NativeAudioResult discoveryResult = DiscoverDevices(
            out IReadOnlyList<AudioApplication> sources,
            out IReadOnlyList<MicrophoneDevice> microphones);
        if (discoveryResult != NativeAudioResult.Ok)
        {
            PublishFailure(discoveryResult);
            return;
        }

        AudioEngineSnapshot previous = Snapshot;
        string? sourceId = ChooseSourceId(previous.SelectedSourceId, sources);
        string? microphoneId = ChooseMicrophoneId(previous.SelectedMicrophoneId, microphones);
        NativeAudioResult configurationResult = ApplySelectionAndGains(sourceId, microphoneId, sources, microphones);
        if (configurationResult != NativeAudioResult.Ok)
        {
            PublishFailure(configurationResult);
            return;
        }

        NativeAudioResult reportResult = refreshResult != NativeAudioResult.Ok ? refreshResult : StateResult(nativeStatus);
        if (reportResult == NativeAudioResult.OutputUnavailable && nativeStatus.InjectionRequested)
        {
            NativeAudioResult stopResult = nativeAudio.StopInjection();
            diagnostics.Write("output-unavailable", $"Stopped injection after output loss: {stopResult}.");
            if (!resumeRestorePending)
            {
                restoreInjectionOnResume = false;
            }

            nativeStatus = nativeStatus with { InjectionRequested = false };
        }

        string? error = reportResult == NativeAudioResult.Ok ? null : GetNativeError(reportResult);
        Publish(previous with
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
        });
    }

    private async Task MonitorLoopAsync()
    {
        int recoveryAttempt = 0;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                bool recovering = !initialized || ShouldRecover(Snapshot.Injection);
                TimeSpan interval = recovering
                    ? ReconnectSchedule.Default.GetDelay(recoveryAttempt++)
                    : HealthyPollInterval;
                await delay.Delay(interval, lifetime.Token).ConfigureAwait(false);
                await RunNativeAsync(() =>
                {
                    if (initialized)
                    {
                        RefreshCore();
                        TryRestoreInjectionAfterResumeCore();
                    }
                    else
                    {
                        diagnostics.Write("reconnect-attempt", "Retrying native audio initialization.");
                        InitializeCore();
                    }
                }, lifetime.Token).ConfigureAwait(false);

                if (initialized && !ShouldRecover(Snapshot.Injection))
                {
                    recoveryAttempt = 0;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private void StartMonitorLoop()
    {
        lock (sync)
        {
            if (disposed || monitorTask is { IsCompleted: false })
            {
                return;
            }

            monitorTask = MonitorLoopAsync();
        }
    }

    private void QueueConfiguration()
    {
        lock (sync)
        {
            if (!initialized || disposed)
            {
                return;
            }

            pendingConfiguration = ApplyConfigurationAsync();
        }
    }

    private async Task ApplyConfigurationAsync()
    {
        try
        {
            await RunNativeAsync(() =>
            {
                AudioEngineSnapshot current = Snapshot;
                NativeAudioResult result = ApplySelectionAndGains(
                    current.SelectedSourceId,
                    current.SelectedMicrophoneId,
                    current.Sources,
                    current.Microphones);
                if (result != NativeAudioResult.Ok)
                {
                    PublishFailure(result);
                }
            }, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private Task GetPendingConfiguration()
    {
        lock (sync)
        {
            return pendingConfiguration;
        }
    }

    private async Task RunNativeAsync(Action operation, CancellationToken cancellationToken)
    {
        await nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            nativeGate.Release();
        }
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
        string message = GetNativeError(result);
        diagnostics.Write("audio-failure", $"{result}: {message}");
        Publish(current with
        {
            Injection = InjectionSnapshot.FromState(
                state,
                current.Injection.IsSourceAvailable,
                current.Injection.IsMicrophoneAvailable,
                result != NativeAudioResult.OutputUnavailable && current.Injection.IsOutputAvailable,
                (result is NativeAudioResult.SourceUnavailable or NativeAudioResult.MicrophoneUnavailable) &&
                    current.Injection.IsInjectionActive),
            ErrorMessage = message,
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
        EventHandler<AudioEngineSnapshot>? handlers;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            snapshot = updated;
            handlers = SnapshotChanged;
        }

        handlers?.Invoke(this, updated);
    }

    private static InjectionSnapshot Project(NativeAudioStatus status, NativeAudioResult result) =>
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
        const uint maximumDeviceCount = 512;
        if (sourceResult != NativeAudioResult.Ok || sourceCount > maximumDeviceCount)
        {
            sources = [];
            microphones = [];
            return sourceResult == NativeAudioResult.Ok ? NativeAudioResult.InternalError : sourceResult;
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
        string? sourceId,
        string? microphoneId,
        IReadOnlyList<AudioApplication> sources,
        IReadOnlyList<MicrophoneDevice> microphones)
    {
        bool canApplySource = string.IsNullOrWhiteSpace(sourceId) ||
            sources.Any(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal));
        if (canApplySource &&
            (!sourceSelectionApplied || !string.Equals(sourceId, appliedSourceId, StringComparison.Ordinal)))
        {
            NativeAudioResult selection = nativeAudio.SelectSource(sourceId ?? string.Empty);
            if (selection != NativeAudioResult.Ok)
            {
                return selection;
            }

            appliedSourceId = sourceId;
            sourceSelectionApplied = true;
        }

        bool canApplyMicrophone = string.IsNullOrWhiteSpace(microphoneId) ||
            microphones.Any(microphone => string.Equals(microphone.Id, microphoneId, StringComparison.Ordinal));
        if (canApplyMicrophone &&
            (!microphoneSelectionApplied || !string.Equals(microphoneId, appliedMicrophoneId, StringComparison.Ordinal)))
        {
            NativeAudioResult selection = nativeAudio.SelectMicrophone(microphoneId ?? string.Empty);
            if (selection != NativeAudioResult.Ok)
            {
                return selection;
            }

            appliedMicrophoneId = microphoneId;
            microphoneSelectionApplied = true;
        }

        NativeAudioResult sourceGainResult = nativeAudio.SetSourceGain(sourceGain);
        return sourceGainResult == NativeAudioResult.Ok
            ? nativeAudio.SetMicrophoneGain(microphoneGain)
            : sourceGainResult;
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

    private void TryRestoreInjectionAfterResumeCore()
    {
        if (!resumeRestorePending || !restoreInjectionOnResume)
        {
            return;
        }

        AudioEngineSnapshot current = Snapshot;
        if (!CanSafelyInject(current.Injection))
        {
            return;
        }

        if (current.Injection.IsInjectionActive)
        {
            resumeRestorePending = false;
            return;
        }

        NativeAudioResult start = nativeAudio.StartInjection();
        if (start != NativeAudioResult.Ok)
        {
            PublishFailure(start);
            return;
        }

        RefreshCore();
        if (Snapshot.Injection.IsInjectionActive)
        {
            resumeRestorePending = false;
        }
    }

    private static bool ShouldRecover(InjectionSnapshot injection) =>
        injection.State is InjectionState.SourceUnavailable or
            InjectionState.MicrophoneUnavailable or
            InjectionState.OutputUnavailable or
            InjectionState.Error;

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
    }

    private void ThrowIfDisposed()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
