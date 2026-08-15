namespace MusicMic.App.Services;

public interface IAudioEngineService : IAsyncDisposable
{
    AudioEngineSnapshot Snapshot { get; }

    event EventHandler<AudioEngineSnapshot>? SnapshotChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void SelectSource(string? sourceId);

    void SelectMicrophone(string? microphoneId);

    void SetSourceGain(double gain);

    void SetMicrophoneGain(double gain);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
