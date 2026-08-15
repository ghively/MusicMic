using Xunit;

namespace MusicMic.Core.Tests;

public sealed class InjectionSnapshotTests
{
    [Fact]
    public void Losing_source_during_injection_keeps_microphone_stream_active()
    {
        InjectionSnapshot injecting = InjectionSnapshot.Ready.Start();

        InjectionSnapshot sourceLost = injecting.SetSourceAvailable(false);

        Assert.Equal(InjectionState.SourceUnavailable, sourceLost.State);
        Assert.True(sourceLost.IsInjectionActive);
        Assert.False(sourceLost.IsSourceStreamActive);
        Assert.True(sourceLost.IsMicrophoneStreamActive);
        Assert.True(injecting.IsSourceAvailable);
    }

    [Fact]
    public void Losing_microphone_during_injection_keeps_source_stream_active()
    {
        InjectionSnapshot injecting = InjectionSnapshot.Ready.Start();

        InjectionSnapshot microphoneLost = injecting.SetMicrophoneAvailable(false);

        Assert.Equal(InjectionState.MicrophoneUnavailable, microphoneLost.State);
        Assert.True(microphoneLost.IsInjectionActive);
        Assert.True(microphoneLost.IsSourceStreamActive);
        Assert.False(microphoneLost.IsMicrophoneStreamActive);
        Assert.True(injecting.IsMicrophoneAvailable);
    }

    [Fact]
    public void Stop_is_idempotent_and_deactivates_both_streams()
    {
        InjectionSnapshot stopped = InjectionSnapshot.Ready.Start().Stop().Stop();

        Assert.Equal(InjectionState.Ready, stopped.State);
        Assert.False(stopped.IsInjectionActive);
        Assert.False(stopped.IsSourceStreamActive);
        Assert.False(stopped.IsMicrophoneStreamActive);
    }
}
