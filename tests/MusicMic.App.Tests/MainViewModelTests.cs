using MusicMic.App.Presentation;
using MusicMic.App.Services;
using MusicMic.Core;

namespace MusicMic.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_UsesProductDefaults()
    {
        var viewModel = CreateViewModel(FakeAudioEngine.Ready());

        Assert.Equal(70, viewModel.SourcePercentage);
        Assert.Equal(100, viewModel.MicrophonePercentage);
        Assert.Equal(ThemePreference.System, viewModel.Theme);
    }

    [Fact]
    public void OutputUnavailable_DisablesStartAndExplainsHowToRecover()
    {
        var viewModel = CreateViewModel(FakeAudioEngine.OutputUnavailable());

        Assert.False(viewModel.CanToggleInjection);
        Assert.Equal("VB-CABLE not found", viewModel.StatusText);
        Assert.Contains("Install VB-CABLE", viewModel.StatusDetail);
    }

    [Fact]
    public void SelectedSource_AppearsInNormalPlaybackAssurance()
    {
        var viewModel = CreateViewModel(FakeAudioEngine.Ready());

        viewModel.SelectedSource = viewModel.Sources.Single(source => source.DisplayName == "Spotify");

        Assert.Equal(
            "You still hear Spotify normally through your speakers/headphones.",
            viewModel.PlaybackAssuranceText);
    }

    [Fact]
    public async Task ToggleInjection_PresentsActiveThenIdleState()
    {
        var viewModel = CreateViewModel(FakeAudioEngine.Ready());

        await viewModel.ToggleInjectionAsync();

        Assert.True(viewModel.IsInjecting);
        Assert.Equal("STOP", viewModel.PrimaryActionText);
        Assert.Equal("Injecting", viewModel.StatusText);

        await viewModel.ToggleInjectionAsync();

        Assert.False(viewModel.IsInjecting);
        Assert.Equal("START INJECTING", viewModel.PrimaryActionText);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task Injecting_DisablesSourceAndMicrophoneSelectionAndIgnoresRebinds()
    {
        FakeAudioEngine engine = FakeAudioEngine.Ready();
        var viewModel = CreateViewModel(engine);
        await viewModel.ToggleInjectionAsync();
        int sourceSelections = engine.SourceSelectionCalls;
        int microphoneSelections = engine.MicrophoneSelectionCalls;

        viewModel.SelectedSource = viewModel.Sources.Single(source => source.DisplayName == "Browser");
        viewModel.SelectedMicrophone = null;

        Assert.False(viewModel.CanChangeSelection);
        Assert.Equal(sourceSelections, engine.SourceSelectionCalls);
        Assert.Equal(microphoneSelections, engine.MicrophoneSelectionCalls);
        Assert.Equal("Spotify", viewModel.SelectedSource?.DisplayName);
        Assert.NotNull(viewModel.SelectedMicrophone);
    }

    [Fact]
    public async Task Injecting_KeepsSourceAndMicrophoneGainAdjustmentEnabled()
    {
        var viewModel = CreateViewModel(FakeAudioEngine.Ready());

        await viewModel.ToggleInjectionAsync();

        Assert.True(viewModel.IsInjecting);
        Assert.True(viewModel.CanAdjustGains);
    }

    [Fact]
    public async Task SnapshotRaisedOffUiThread_IsPostedToUiDispatcherBeforeUpdatingCollections()
    {
        FakeAudioEngine engine = FakeAudioEngine.Ready();
        var dispatcher = new QueuedUiDispatcher();
        var viewModel = new MainViewModel(
            engine,
            new TestSettingsService(),
            new ThemeService(),
            new TestStartupService(),
            dispatcher);
        dispatcher.HasAccess = false;

        await Task.Run(() => engine.PublishUnavailableSource());

        Assert.Equal(InjectionState.Ready, viewModel.State);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.HasAccess = true;
        dispatcher.Drain();
        Assert.Equal(InjectionState.SourceUnavailable, viewModel.State);
    }

    [Fact]
    public void ThemeChange_IsAppliedImmediately()
    {
        var themeService = new ThemeService();
        var viewModel = CreateViewModel(FakeAudioEngine.Ready(), themeService);

        viewModel.Theme = ThemePreference.Dark;

        Assert.Equal(ThemePreference.Dark, themeService.CurrentTheme);
        Assert.Equal(ThemePreference.Dark, viewModel.Theme);
    }

    private static MainViewModel CreateViewModel(
        FakeAudioEngine engine,
        ThemeService? themeService = null) =>
        new(
            engine,
            new TestSettingsService(),
            themeService ?? new ThemeService(),
            new TestStartupService());

    private sealed class TestSettingsService : ISettingsService
    {
        private MusicMicSettings settings = MusicMicSettings.Default;

        public MusicMicSettings Load() => settings;

        public void Save(MusicMicSettings settings) => this.settings = settings;
    }

    private sealed class FakeAudioEngine : IAudioEngineService
    {
        private AudioEngineSnapshot snapshot;

        private FakeAudioEngine(AudioEngineSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public AudioEngineSnapshot Snapshot => snapshot;

        public event EventHandler<AudioEngineSnapshot>? SnapshotChanged;

        public int SourceSelectionCalls { get; private set; }

        public int MicrophoneSelectionCalls { get; private set; }

        public static FakeAudioEngine Ready()
        {
            var sources = new[]
            {
                new AudioApplication("spotify.exe", "Spotify"),
                new AudioApplication("browser.exe", "Browser"),
            };
            var microphones = new[] { new MicrophoneDevice("default-mic", "Default microphone") };
            return new FakeAudioEngine(new AudioEngineSnapshot(
                InjectionSnapshot.Ready,
                sources,
                microphones,
                sources[0].Id,
                microphones[0].Id));
        }

        public static FakeAudioEngine OutputUnavailable()
        {
            var ready = Ready().snapshot;
            return new FakeAudioEngine(ready with
            {
                Injection = ready.Injection.SetOutputAvailable(false),
            });
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SelectSource(string? sourceId)
        {
            SourceSelectionCalls++;
            Publish(snapshot with { SelectedSourceId = sourceId });
        }

        public void SelectMicrophone(string? microphoneId)
        {
            MicrophoneSelectionCalls++;
            Publish(snapshot with { SelectedMicrophoneId = microphoneId });
        }

        public void SetSourceGain(double gain) { }

        public void SetMicrophoneGain(double gain) { }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Publish(snapshot with { Injection = snapshot.Injection.Start() });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Publish(snapshot with { Injection = snapshot.Injection.Stop() });
            return Task.CompletedTask;
        }

        public Task HandlePowerResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void PublishUnavailableSource() => Publish(snapshot with
        {
            Injection = InjectionSnapshot.FromState(
                InjectionState.SourceUnavailable,
                false,
                snapshot.Injection.IsMicrophoneAvailable,
                snapshot.Injection.IsOutputAvailable,
                snapshot.Injection.IsInjectionActive),
        });

        private void Publish(AudioEngineSnapshot updated)
        {
            snapshot = updated;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class TestStartupService : IStartupService
    {
        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class QueuedUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> actions = new();

        public bool HasAccess { get; set; } = true;

        public int PendingCount => actions.Count;

        public bool CheckAccess() => HasAccess;

        public void Post(Action action) => actions.Enqueue(action);

        public void Drain()
        {
            while (actions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
