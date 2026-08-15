using MusicMic.App;
using MusicMic.App.Services;
using MusicMic.Core;

namespace MusicMic.App.Tests;

public sealed class IntegrationServicesTests
{
    [Fact]
    public async Task InitializeAsync_ProjectsNativeAvailabilityAndPeaks()
    {
        var native = new FakeNativeAudioApi
        {
            Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0.25f, 0.50f),
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);

        await service.InitializeAsync();

        Assert.Equal(InjectionState.Ready, service.Snapshot.Injection.State);
        Assert.Equal(0.25f, service.Snapshot.SourcePeak);
        Assert.Equal(0.50f, service.Snapshot.MicrophonePeak);
        Assert.Null(service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_EnumeratesNativeDevices_SelectsSpotifyAndDefaultMicrophone_AndAppliesGains()
    {
        var native = new FakeNativeAudioApi
        {
            Sources =
            [
                new NativeAudioSource("browser-stable", "Browser", 4500, false),
                new NativeAudioSource("spotify-stable", "Spotify", 4123, true),
            ],
            Microphones =
            [
                new NativeAudioMicrophone("usb-mic", "USB microphone", false),
                new NativeAudioMicrophone("default-mic", "Default microphone", true),
            ],
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);

        await service.InitializeAsync();
        service.SetSourceGain(0.7);
        service.SetMicrophoneGain(1.0);

        Assert.Collection(
            service.Snapshot.Sources,
            source => Assert.Equal("Browser", source.DisplayName),
            source => Assert.True(source.IsSpotify));
        Assert.Equal("spotify-stable", service.Snapshot.SelectedSourceId);
        Assert.Equal("default-mic", service.Snapshot.SelectedMicrophoneId);
        Assert.Equal("spotify-stable", native.SelectedSourceId);
        Assert.Equal("default-mic", native.SelectedMicrophoneId);
        Assert.Equal(0.7f, native.SourceGain);
        Assert.Equal(1f, native.MicrophoneGain);
    }

    [Fact]
    public async Task InitializeAsync_AppliesPersistedSelectionsToNativeEngine()
    {
        var native = new FakeNativeAudioApi
        {
            Sources =
            [
                new NativeAudioSource("browser-stable", "Browser", 4500, false),
                new NativeAudioSource("spotify-stable", "Spotify", 4123, true),
            ],
            Microphones =
            [
                new NativeAudioMicrophone("default-mic", "Default microphone", true),
                new NativeAudioMicrophone("usb-mic", "USB microphone", false),
            ],
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        service.SelectSource("browser-stable");
        service.SelectMicrophone("usb-mic");

        await service.InitializeAsync();

        Assert.Equal("browser-stable", native.SelectedSourceId);
        Assert.Equal("usb-mic", native.SelectedMicrophoneId);
        Assert.Equal("browser-stable", service.Snapshot.SelectedSourceId);
        Assert.Equal("usb-mic", service.Snapshot.SelectedMicrophoneId);
    }

    [Fact]
    public async Task ClearingSelections_ClearsNativeCaptureIdentities()
    {
        var native = new FakeNativeAudioApi
        {
            Sources = [new NativeAudioSource("spotify", "Spotify", 4123, true)],
            Microphones = [new NativeAudioMicrophone("default-mic", "Default microphone", true)],
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();

        service.SelectSource(null);
        service.SelectMicrophone(null);

        Assert.True(await WaitUntilAsync(
            () => native.SelectedSourceId == string.Empty && native.SelectedMicrophoneId == string.Empty,
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task StartAsync_ReturnsControlBeforeSlowNativeStartCompletes()
    {
        var native = new FakeNativeAudioApi { BlockStart = true };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();
        Task? nativeStartTask = null;

        Task caller = Task.Run((Action)(() => nativeStartTask = service.StartAsync()));
        bool nativeEntered = native.StartEntered.Wait(TimeSpan.FromSeconds(2));
        bool callerReturned = await CompletesWithinAsync(caller, TimeSpan.FromMilliseconds(500));
        bool nativeTaskWasIncomplete = nativeStartTask is { IsCompleted: false };
        native.ReleaseStart.Set();
        if (nativeStartTask is not null)
        {
            await nativeStartTask;
        }

        Assert.True(nativeEntered);
        Assert.True(callerReturned);
        Assert.True(nativeTaskWasIncomplete);
    }

    [Fact]
    public async Task ConcurrentRefreshes_SerializeAllNativeCalls()
    {
        var native = new FakeNativeAudioApi { NativeCallDelay = TimeSpan.FromMilliseconds(20) };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();
        native.ResetConcurrencyObservation();

        await Task.WhenAll(
            Task.Run(() => service.RefreshAsync()),
            Task.Run(() => service.RefreshAsync()),
            Task.Run(() => service.RefreshAsync()));

        Assert.Equal(1, native.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task InitializeAsync_StartsContinuousStatusPolling()
    {
        var delay = new ReleaseOneDelay();
        var native = new FakeNativeAudioApi();
        await using var service = new AudioEngineService(native, delay);
        await service.InitializeAsync();
        int initialStatusCalls = native.StatusCalls;

        delay.Release();

        Assert.True(await WaitUntilAsync(
            () => native.StatusCalls > initialStatusCalls,
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task HandlePowerResumeAsync_InvokesNativeResumeBeforeRefreshingAndRestoring()
    {
        var native = new FakeNativeAudioApi
        {
            Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0, 0),
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();
        await service.StartAsync();

        await service.HandlePowerResumeAsync();

        Assert.Equal(1, native.ResumeCalls);
        Assert.Equal(2, native.StartCalls);
    }

    [Fact]
    public async Task RefreshAsync_MapsSourceAndMicrophoneAvailabilityResultsToUiState()
    {
        var native = new FakeNativeAudioApi
        {
            Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0, 0),
            RefreshResult = NativeAudioResult.SourceUnavailable,
            Error = "Spotify is no longer producing audio.",
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();

        Assert.Equal(InjectionState.SourceUnavailable, service.Snapshot.Injection.State);
        Assert.Equal("Spotify is no longer producing audio.", service.Snapshot.ErrorMessage);

        native.RefreshResult = NativeAudioResult.MicrophoneUnavailable;
        native.Error = "The selected microphone is unavailable.";
        await service.RefreshAsync();

        Assert.Equal(InjectionState.MicrophoneUnavailable, service.Snapshot.Injection.State);
        Assert.Equal("The selected microphone is unavailable.", service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_NativeFailureSurfacesNativeError()
    {
        var native = new FakeNativeAudioApi
        {
            InitializeResult = NativeAudioResult.AudioFailure,
            Error = "The native engine could not start.",
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);

        await service.InitializeAsync();

        Assert.Equal(InjectionState.Error, service.Snapshot.Injection.State);
        Assert.Equal("The native engine could not start.", service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_OutputLossStopsInjectionSafely()
    {
        var native = new FakeNativeAudioApi
        {
            Status = new NativeAudioStatus(NativeAudioState.Injecting, true, true, true, true, 0, 0),
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();
        native.Status = new NativeAudioStatus(NativeAudioState.OutputUnavailable, true, true, false, false, 0, 0);
        native.Error = "VB-CABLE CABLE Input is unavailable.";

        await service.RefreshAsync();

        Assert.Equal(InjectionState.OutputUnavailable, service.Snapshot.Injection.State);
        Assert.False(service.Snapshot.Injection.IsInjectionActive);
        Assert.Equal("VB-CABLE CABLE Input is unavailable.", service.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task HandlePowerResumeAsync_RestoresRequestedInjectionOnlyWhenAllInputsAreAvailable()
    {
        var native = new FakeNativeAudioApi
        {
            Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0),
        };
        await using var service = new AudioEngineService(native, NoWaitAsyncDelay.Instance);
        await service.InitializeAsync();
        await service.StartAsync();
        native.Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0);

        await service.HandlePowerResumeAsync();

        Assert.Equal(2, native.StartCalls);
    }

    [Fact]
    public async Task HandlePowerResumeAsync_RetriesRequestedInjectionWhenDevicesBecomeReadyLater()
    {
        var delay = new ReleaseOneDelay();
        var native = new FakeNativeAudioApi
        {
            Sources = [new NativeAudioSource("spotify", "Spotify", 4123, true)],
            Microphones = [new NativeAudioMicrophone("default-mic", "Default microphone", true)],
            Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0, 0),
            StatusAfterResume = new NativeAudioStatus(
                NativeAudioState.SourceUnavailable, false, true, true, false, 0, 0, 0),
        };
        await using var service = new AudioEngineService(native, delay);
        await service.InitializeAsync();
        await service.StartAsync();

        await service.HandlePowerResumeAsync();
        Assert.Equal(1, native.StartCalls);

        native.Status = new NativeAudioStatus(NativeAudioState.Ready, true, true, true, false, 0, 0, 0);
        delay.Release();

        Assert.True(await WaitUntilAsync(() => native.StartCalls == 2, TimeSpan.FromSeconds(2)));
        Assert.True(service.Snapshot.Injection.IsInjectionActive);
    }

    [Fact]
    public async Task ShutdownCallbackGuard_DropsQueuedWorkAndContainsCallbackExceptions()
    {
        var guard = new ShutdownCallbackGuard();
        int callbackCalls = 0;

        await guard.RunAsync(_ => throw new InvalidOperationException("simulated callback failure"));
        guard.BeginShutdown();
        guard.Run(() => callbackCalls++);
        await guard.RunAsync(_ =>
        {
            callbackCalls++;
            return Task.CompletedTask;
        });

        Assert.Equal(0, callbackCalls);
    }

    [Fact]
    public void SettingsService_SaveThenLoad_RoundTripsLocalSettingsFile()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "MusicMic.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(testDirectory, "settings.json");
        try
        {
            var saved = new MusicMicSettings
            {
                Theme = ThemePreference.Dark,
                SelectedSource = "spotify-stable-id",
                SelectedMicrophoneId = "mic-stable-id",
                SourceVolume = 0.35,
                MicrophoneVolume = 0.8,
                StartWithWindows = true,
            };

            new SettingsService(path).Save(saved);
            MusicMicSettings loaded = new SettingsService(path).Load();

            Assert.Equal(saved, loaded);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void TrayMenuState_UsesStartForReadyAndStopForActiveInjection()
    {
        var ready = new AudioEngineSnapshot(InjectionSnapshot.Ready, [], [], "source", "microphone");
        var injecting = ready with { Injection = InjectionSnapshot.Ready.Start() };

        Assert.Equal("Start Injecting", TrayMenuState.From(ready).StartStopText);
        Assert.True(TrayMenuState.From(ready).CanStartStop);
        Assert.Equal("Stop Injecting", TrayMenuState.From(injecting).StartStopText);
    }

    [Fact]
    public void TrayMenuState_ContainsStatusAndCurrentSourceAndMicrophoneChoices()
    {
        AudioApplication[] sources =
        [
            new("spotify", "Spotify"),
            new("browser", "Browser"),
        ];
        MicrophoneDevice[] microphones =
        [
            new("usb", "USB microphone"),
            new("webcam", "Webcam microphone"),
        ];
        var snapshot = new AudioEngineSnapshot(
            InjectionSnapshot.Ready.Start(), sources, microphones, "spotify", "usb");

        TrayMenuState state = TrayMenuState.From(snapshot);

        Assert.Equal("Injecting", state.StatusText);
        Assert.Collection(
            state.Sources,
            item => Assert.Equal(("spotify", "Spotify", true), (item.Id, item.DisplayName, item.IsSelected)),
            item => Assert.Equal(("browser", "Browser", false), (item.Id, item.DisplayName, item.IsSelected)));
        Assert.Collection(
            state.Microphones,
            item => Assert.Equal(("usb", "USB microphone", true), (item.Id, item.DisplayName, item.IsSelected)),
            item => Assert.Equal(("webcam", "Webcam microphone", false), (item.Id, item.DisplayName, item.IsSelected)));
    }

    [Fact]
    public void StartupService_RegistersAndRemovesCurrentExecutableForWindowsLogon()
    {
        const string executablePath = @"C:\Program Files\MusicMic\MusicMic.exe";
        var store = new InMemoryStartupRegistrationStore();
        var service = new StartupService(store, executablePath);

        service.SetEnabled(true);

        Assert.True(service.IsEnabled);
        Assert.Equal($"\"{executablePath}\"", store.Value);

        service.SetEnabled(false);
        Assert.False(service.IsEnabled);
        Assert.Null(store.Value);
    }

    [Fact]
    public void DiagnosticLogger_RotatesLocalTextLogsAndSanitizesMultilineMessages()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MusicMic.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new DiagnosticLogger(directory, maximumFileBytes: 180, retainedFileCount: 3))
            {
                for (int index = 0; index < 20; index++)
                {
                    logger.Write("test", $"diagnostic {index} with enough text to rotate\r\nsecond line");
                }
            }

            string[] files = Directory.GetFiles(directory, "MusicMic*.log");
            Assert.InRange(files.Length, 2, 3);
            string text = string.Join("", files.Select(File.ReadAllText));
            Assert.Contains("diagnostic 19", text);
            Assert.DoesNotContain("\r\nsecond line", text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout) =>
        await Task.WhenAny(task, Task.Delay(timeout)) == task;

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return predicate();
    }

    private sealed class FakeNativeAudioApi : INativeAudioApi
    {
        private int concurrentCalls;
        private int maximumConcurrentCalls;
        private int statusCalls;

        public NativeAudioResult InitializeResult { get; set; } = NativeAudioResult.Ok;

        public NativeAudioStatus Status { get; set; } = new(NativeAudioState.Ready, true, true, true, false, 0, 0, 0);

        public NativeAudioResult RefreshResult { get; set; } = NativeAudioResult.Ok;

        public NativeAudioStatus? StatusAfterResume { get; set; }

        public IReadOnlyList<NativeAudioSource> Sources { get; set; } = [];

        public IReadOnlyList<NativeAudioMicrophone> Microphones { get; set; } = [];

        public string? SelectedSourceId { get; private set; }

        public string? SelectedMicrophoneId { get; private set; }

        public float SourceGain { get; private set; }

        public float MicrophoneGain { get; private set; }

        public string Error { get; set; } = string.Empty;

        public int StartCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public int StatusCalls => Volatile.Read(ref statusCalls);

        public TimeSpan NativeCallDelay { get; set; }

        public bool BlockStart { get; set; }

        public ManualResetEventSlim StartEntered { get; } = new(false);

        public ManualResetEventSlim ReleaseStart { get; } = new(false);

        public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);

        public NativeAudioResult Initialize() => InitializeResult;

        public NativeAudioResult Shutdown() => NativeAudioResult.Ok;

        public NativeAudioResult RefreshDevices() => Observe(() => RefreshResult);

        public NativeAudioResult GetSourceCount(out uint count)
        {
            count = (uint)Sources.Count;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult GetSourceInfo(uint index, out NativeAudioSource source)
        {
            source = index < Sources.Count ? Sources[(int)index] : default;
            return index < Sources.Count ? NativeAudioResult.Ok : NativeAudioResult.NotFound;
        }

        public NativeAudioResult GetMicrophoneCount(out uint count)
        {
            count = (uint)Microphones.Count;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult GetMicrophoneInfo(uint index, out NativeAudioMicrophone microphone)
        {
            microphone = index < Microphones.Count ? Microphones[(int)index] : default;
            return index < Microphones.Count ? NativeAudioResult.Ok : NativeAudioResult.NotFound;
        }

        public NativeAudioResult SelectSource(string sourceId)
        {
            SelectedSourceId = sourceId;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult SelectMicrophone(string microphoneId)
        {
            SelectedMicrophoneId = microphoneId;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult SetSourceGain(float gain)
        {
            SourceGain = gain;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult SetMicrophoneGain(float gain)
        {
            MicrophoneGain = gain;
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult HandleSystemResume()
        {
            ResumeCalls++;
            Status = StatusAfterResume ?? Status with { State = NativeAudioState.Ready, InjectionRequested = false };
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult StartInjection()
        {
            return Observe(() =>
            {
                StartCalls++;
                StartEntered.Set();
                if (BlockStart)
                {
                    ReleaseStart.Wait();
                }

                Status = Status with { State = NativeAudioState.Injecting, InjectionRequested = true };
                return NativeAudioResult.Ok;
            });
        }

        public NativeAudioResult StopInjection()
        {
            Status = Status with { State = NativeAudioState.Ready, InjectionRequested = false };
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult GetStatus(out NativeAudioStatus status)
        {
            NativeAudioStatus observed = default;
            NativeAudioResult result = Observe(() =>
            {
                Interlocked.Increment(ref statusCalls);
                observed = Status;
                return NativeAudioResult.Ok;
            });
            status = observed;
            return result;
        }

        public string GetLastError() => Error;

        public void ResetConcurrencyObservation() => Volatile.Write(ref maximumConcurrentCalls, 0);

        private T Observe<T>(Func<T> action)
        {
            int current = Interlocked.Increment(ref concurrentCalls);
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumConcurrentCalls);
            }
            while (current > observed &&
                   Interlocked.CompareExchange(ref maximumConcurrentCalls, current, observed) != observed);

            try
            {
                if (NativeCallDelay > TimeSpan.Zero)
                {
                    Thread.Sleep(NativeCallDelay);
                }

                return action();
            }
            finally
            {
                Interlocked.Decrement(ref concurrentCalls);
            }
        }
    }

    private sealed class NoWaitAsyncDelay : IAsyncDelay
    {
        public static NoWaitAsyncDelay Instance { get; } = new();

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class ReleaseOneDelay : IAsyncDelay
    {
        private readonly SemaphoreSlim release = new(0);

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
            release.WaitAsync(cancellationToken);

        public void Release() => release.Release();
    }

    private sealed class InMemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        public string? Value { get; private set; }

        public string? Read() => Value;

        public void Write(string command) => Value = command;

        public void Delete() => Value = null;
    }
}
