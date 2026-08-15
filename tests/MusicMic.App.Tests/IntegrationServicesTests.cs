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

        Assert.Equal("Start injection", TrayMenuState.From(ready).StartStopText);
        Assert.True(TrayMenuState.From(ready).CanStartStop);
        Assert.Equal("Stop injection", TrayMenuState.From(injecting).StartStopText);
    }

    private sealed class FakeNativeAudioApi : INativeAudioApi
    {
        public NativeAudioResult InitializeResult { get; set; } = NativeAudioResult.Ok;

        public NativeAudioStatus Status { get; set; } = new(NativeAudioState.Ready, true, true, true, false, 0, 0, 0);

        public NativeAudioResult RefreshResult { get; set; } = NativeAudioResult.Ok;

        public IReadOnlyList<NativeAudioSource> Sources { get; set; } = [];

        public IReadOnlyList<NativeAudioMicrophone> Microphones { get; set; } = [];

        public string? SelectedSourceId { get; private set; }

        public string? SelectedMicrophoneId { get; private set; }

        public float SourceGain { get; private set; }

        public float MicrophoneGain { get; private set; }

        public string Error { get; set; } = string.Empty;

        public int StartCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public NativeAudioResult Initialize() => InitializeResult;

        public NativeAudioResult Shutdown() => NativeAudioResult.Ok;

        public NativeAudioResult RefreshDevices() => RefreshResult;

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
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult StartInjection()
        {
            StartCalls++;
            Status = Status with { State = NativeAudioState.Injecting, InjectionRequested = true };
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult StopInjection()
        {
            Status = Status with { State = NativeAudioState.Ready, InjectionRequested = false };
            return NativeAudioResult.Ok;
        }

        public NativeAudioResult GetStatus(out NativeAudioStatus status)
        {
            status = Status;
            return NativeAudioResult.Ok;
        }

        public string GetLastError() => Error;
    }

    private sealed class NoWaitAsyncDelay : IAsyncDelay
    {
        public static NoWaitAsyncDelay Instance { get; } = new();

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
