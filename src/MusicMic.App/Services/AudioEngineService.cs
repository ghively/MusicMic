using MusicMic.Core;

namespace MusicMic.App.Services;

/// <summary>
/// Presentation-safe engine boundary. Task 4 supplies the native client behind this interface.
/// Until then, it reports the required missing-output state and never changes audio routing.
/// </summary>
public sealed class AudioEngineService : IAudioEngineService
{
    private AudioEngineSnapshot snapshot = new(
        InjectionSnapshot.Ready.SetOutputAvailable(false),
        Array.Empty<AudioApplication>(),
        Array.Empty<MicrophoneDevice>(),
        null,
        null);

    public AudioEngineSnapshot Snapshot => snapshot;

    public event EventHandler<AudioEngineSnapshot>? SnapshotChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Publish(snapshot);
        return Task.CompletedTask;
    }

    public void SelectSource(string? sourceId) =>
        Publish(snapshot with { SelectedSourceId = sourceId });

    public void SelectMicrophone(string? microphoneId) =>
        Publish(snapshot with { SelectedMicrophoneId = microphoneId });

    public void SetSourceGain(double gain) =>
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);

    public void SetMicrophoneGain(double gain) =>
        ArgumentOutOfRangeException.ThrowIfLessThan(gain, 0);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Start));
        Publish(snapshot with { Injection = snapshot.Injection.Start() });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoutingGuard.Validate(RoutingGuard.CreateRequest(InjectionCommand.Stop));
        Publish(snapshot with { Injection = snapshot.Injection.Stop() });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }

    private void Publish(AudioEngineSnapshot updated)
    {
        snapshot = updated;
        SnapshotChanged?.Invoke(this, updated);
    }
}
