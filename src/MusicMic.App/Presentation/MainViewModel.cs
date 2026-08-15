using MusicMic.App.Services;
using MusicMic.Core;
using System.Collections.ObjectModel;

namespace MusicMic.App.Presentation;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAudioEngineService audioEngine;
    private readonly ISettingsService settingsService;
    private readonly IThemeService themeService;
    private readonly IStartupService startupService;
    private readonly IUiDispatcher uiDispatcher;
    private AudioApplication? selectedSource;
    private MicrophoneDevice? selectedMicrophone;
    private double sourcePercentage;
    private double microphonePercentage;
    private ThemePreference theme;
    private bool startWithWindows;
    private InjectionSnapshot injection = InjectionSnapshot.Ready.SetOutputAvailable(false);
    private bool isApplyingSnapshot;

    public MainViewModel(
        IAudioEngineService audioEngine,
        ISettingsService settingsService,
        IThemeService themeService,
        IStartupService? startupService = null,
        IUiDispatcher? uiDispatcher = null)
    {
        this.audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        this.startupService = startupService ?? new StartupService();
        this.uiDispatcher = uiDispatcher ?? new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);

        MusicMicSettings settings = settingsService.Load();
        sourcePercentage = settings.SourceVolume * 100;
        microphonePercentage = settings.MicrophoneVolume * 100;
        theme = settings.Theme;
        startWithWindows = settings.StartWithWindows;
        ToggleInjectionCommand = new AsyncRelayCommand(() => ToggleInjectionAsync(), () => CanToggleInjection);

        audioEngine.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(audioEngine.Snapshot, settings.SelectedSource, settings.SelectedMicrophoneId);
        themeService.ApplyTheme(theme);
        // Cache persisted stable identities before native initialization. The engine applies them
        // after discovery and never substitutes an unrelated source or microphone.
        if (!string.IsNullOrWhiteSpace(settings.SelectedSource))
        {
            audioEngine.SelectSource(settings.SelectedSource);
        }

        if (!string.IsNullOrWhiteSpace(settings.SelectedMicrophoneId))
        {
            audioEngine.SelectMicrophone(settings.SelectedMicrophoneId);
        }

        audioEngine.SetSourceGain(settings.SourceVolume);
        audioEngine.SetMicrophoneGain(settings.MicrophoneVolume);
        this.startupService.SetEnabled(startWithWindows);
    }

    public ObservableCollection<AudioApplication> Sources { get; } = [];

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public IReadOnlyList<ThemePreference> Themes { get; } = Enum.GetValues<ThemePreference>();

    public AsyncRelayCommand ToggleInjectionCommand { get; }

    public AudioApplication? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (!isApplyingSnapshot && IsInjecting)
            {
                return;
            }

            if (!SetProperty(ref selectedSource, value))
            {
                return;
            }

            if (!isApplyingSnapshot)
            {
                audioEngine.SelectSource(value?.Id);
                SaveSettings();
            }

            OnPropertyChanged(nameof(PlaybackAssuranceText));
            NotifyActionStateChanged();
        }
    }

    public MicrophoneDevice? SelectedMicrophone
    {
        get => selectedMicrophone;
        set
        {
            if (!isApplyingSnapshot && IsInjecting)
            {
                return;
            }

            if (!SetProperty(ref selectedMicrophone, value))
            {
                return;
            }

            if (!isApplyingSnapshot)
            {
                audioEngine.SelectMicrophone(value?.Id);
                SaveSettings();
            }

            NotifyActionStateChanged();
        }
    }

    public double SourcePercentage
    {
        get => sourcePercentage;
        set
        {
            double bounded = Math.Clamp(value, 0, 100);
            if (SetProperty(ref sourcePercentage, bounded))
            {
                audioEngine.SetSourceGain(bounded / 100);
                SaveSettings();
                OnPropertyChanged(nameof(SourcePercentageLabel));
            }
        }
    }

    public string SourcePercentageLabel => $"{SourcePercentage:0}%";

    public double MicrophonePercentage
    {
        get => microphonePercentage;
        set
        {
            double bounded = Math.Clamp(value, 0, 100);
            if (SetProperty(ref microphonePercentage, bounded))
            {
                audioEngine.SetMicrophoneGain(bounded / 100);
                SaveSettings();
                OnPropertyChanged(nameof(MicrophonePercentageLabel));
            }
        }
    }

    public string MicrophonePercentageLabel => $"{MicrophonePercentage:0}%";

    public ThemePreference Theme
    {
        get => theme;
        set
        {
            if (SetProperty(ref theme, value))
            {
                themeService.ApplyTheme(value);
                SaveSettings();
            }
        }
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (SetProperty(ref startWithWindows, value))
            {
                startupService.SetEnabled(value);
                SaveSettings();
            }
        }
    }

    public InjectionState State => injection.State;

    public bool IsInjecting => injection.IsInjectionActive;

    public bool CanChangeSelection => !IsInjecting;

    public bool CanToggleInjection => IsInjecting ||
        (injection.IsOutputAvailable && SelectedSource is not null && SelectedMicrophone is not null);

    public string PrimaryActionText => IsInjecting ? "STOP" : "START INJECTING";

    public string ActiveSourceName => SelectedSource?.DisplayName ?? "Source reconnecting";

    public string ActiveMicrophoneName => SelectedMicrophone?.DisplayName ?? "Microphone reconnecting";

    public string SourceStatusText => injection.IsSourceAvailable
        ? $"{ActiveSourceName} audio detected"
        : $"{ActiveSourceName} unavailable — reconnecting";

    public string MicrophoneStatusText => injection.IsMicrophoneAvailable
        ? $"{ActiveMicrophoneName} ready"
        : $"{ActiveMicrophoneName} unavailable — reconnecting";

    public string StatusText => State switch
    {
        InjectionState.Initializing => "Starting",
        InjectionState.Ready => "Ready",
        InjectionState.Injecting => "Injecting",
        InjectionState.SourceUnavailable => "Source unavailable",
        InjectionState.MicrophoneUnavailable => "Microphone unavailable",
        InjectionState.OutputUnavailable => "VB-CABLE not found",
        _ => "Something went wrong",
    };

    public string StatusDetail => State switch
    {
        InjectionState.Initializing => "Looking for audio applications and devices.",
        InjectionState.Ready => "Choose what to share, then start injection.",
        InjectionState.Injecting => "Your mix is available from CABLE Output.",
        InjectionState.SourceUnavailable => "The microphone remains connected while MusicMic waits for the app.",
        InjectionState.MicrophoneUnavailable => "The app copy remains connected while MusicMic waits for the microphone.",
        InjectionState.OutputUnavailable => "Install VB-CABLE, then reopen MusicMic to start injection.",
        _ => "Stop and start injection. Check the local log if the problem continues.",
    };

    public string PlaybackAssuranceText => SelectedSource is null
        ? "You still hear your selected app normally through your speakers/headphones."
        : $"You still hear {SelectedSource.DisplayName} normally through your speakers/headphones.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await audioEngine.InitializeAsync(cancellationToken);

    public void RefreshSystemTheme() => themeService.RefreshSystemTheme();

    public async Task ToggleInjectionAsync(CancellationToken cancellationToken = default)
    {
        if (IsInjecting)
        {
            await audioEngine.StopAsync(cancellationToken);
        }
        else if (CanToggleInjection)
        {
            await audioEngine.StartAsync(cancellationToken);
        }
    }

    private void OnSnapshotChanged(object? sender, AudioEngineSnapshot snapshot)
    {
        if (uiDispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            uiDispatcher.Post(() => ApplySnapshot(snapshot));
        }
    }

    private void ApplySnapshot(
        AudioEngineSnapshot snapshot,
        string? preferredSourceId = null,
        string? preferredMicrophoneId = null)
    {
        isApplyingSnapshot = true;
        try
        {
            Replace(Sources, snapshot.Sources);
            Replace(Microphones, snapshot.Microphones);

            string? sourceId = preferredSourceId ?? snapshot.SelectedSourceId;
            string? microphoneId = preferredMicrophoneId ?? snapshot.SelectedMicrophoneId;
            SelectedSource = sourceId is null ? null : Sources.FirstOrDefault(item => item.Id == sourceId);
            SelectedMicrophone = microphoneId is null ? null : Microphones.FirstOrDefault(item => item.Id == microphoneId);
            injection = snapshot.Injection;
        }
        finally
        {
            isApplyingSnapshot = false;
        }

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(PlaybackAssuranceText));
        OnPropertyChanged(nameof(ActiveSourceName));
        OnPropertyChanged(nameof(ActiveMicrophoneName));
        OnPropertyChanged(nameof(SourceStatusText));
        OnPropertyChanged(nameof(MicrophoneStatusText));
        NotifyActionStateChanged();
    }

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(IsInjecting));
        OnPropertyChanged(nameof(CanChangeSelection));
        OnPropertyChanged(nameof(CanToggleInjection));
        OnPropertyChanged(nameof(PrimaryActionText));
        ToggleInjectionCommand.RaiseCanExecuteChanged();
    }

    private void SaveSettings() => settingsService.Save(new MusicMicSettings
    {
        Theme = Theme,
        SelectedSource = SelectedSource?.Id,
        SelectedMicrophoneId = SelectedMicrophone?.Id,
        SourceVolume = SourcePercentage / 100,
        MicrophoneVolume = MicrophonePercentage / 100,
        StartWithWindows = StartWithWindows,
    });

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }
}
