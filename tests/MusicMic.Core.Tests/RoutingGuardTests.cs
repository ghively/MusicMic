using Xunit;

namespace MusicMic.Core.Tests;

public sealed class RoutingGuardTests
{
    [Theory]
    [InlineData(InjectionCommand.Start)]
    [InlineData(InjectionCommand.Stop)]
    public void Injection_requests_do_not_mutate_normal_playback(InjectionCommand command)
    {
        InjectionRoutingRequest request = RoutingGuard.CreateRequest(command);

        Assert.Equal(PlaybackMutation.None, request.PlaybackMutations);
        RoutingGuard.Validate(request);
    }

    [Theory]
    [InlineData(PlaybackMutation.ApplicationOutputEndpoint)]
    [InlineData(PlaybackMutation.DefaultPlaybackEndpoint)]
    [InlineData(PlaybackMutation.SessionVolume)]
    [InlineData(PlaybackMutation.SessionMute)]
    [InlineData(PlaybackMutation.PlaybackState)]
    public void Playback_mutations_are_rejected(PlaybackMutation mutation)
    {
        var request = new InjectionRoutingRequest(InjectionCommand.Start, mutation);

        Assert.Throws<InvalidOperationException>(() => RoutingGuard.Validate(request));
    }
}
