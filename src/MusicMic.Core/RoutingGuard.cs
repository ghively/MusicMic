namespace MusicMic.Core;

public enum InjectionCommand
{
    Start,
    Stop,
}

[Flags]
public enum PlaybackMutation
{
    None = 0,
    ApplicationOutputEndpoint = 1 << 0,
    DefaultPlaybackEndpoint = 1 << 1,
    SessionVolume = 1 << 2,
    SessionMute = 1 << 3,
    PlaybackState = 1 << 4,
}

public sealed record InjectionRoutingRequest(
    InjectionCommand Command,
    PlaybackMutation PlaybackMutations);

public static class RoutingGuard
{
    public static InjectionRoutingRequest CreateRequest(InjectionCommand command) =>
        new(command, PlaybackMutation.None);

    public static void Validate(InjectionRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PlaybackMutations != PlaybackMutation.None)
        {
            throw new InvalidOperationException(
                "Injection requests must not modify the application's normal playback path.");
        }
    }
}
