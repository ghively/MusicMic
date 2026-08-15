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

        public NativeAudioStatus Status { get; set; } = new(NativeAudioState.Ready, true, true, true, false, 0, 0);

        public string Error { get; set; } = string.Empty;

        public int StartCalls { get; private set; }

        public NativeAudioResult Initialize() => InitializeResult;

        public NativeAudioResult Shutdown() => NativeAudioResult.Ok;

        public NativeAudioResult RefreshDevices() => NativeAudioResult.Ok;

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
