using Xunit;

namespace MusicMic.Core.Tests;

public sealed class ReconnectScheduleTests
{
    [Fact]
    public void Retry_delays_follow_the_required_backoff_and_remain_bounded()
    {
        var expected = new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
        };

        for (var attemptIndex = 0; attemptIndex < expected.Length; attemptIndex++)
        {
            Assert.Equal(expected[attemptIndex], ReconnectSchedule.Default.GetDelay(attemptIndex));
        }

        Assert.Equal(TimeSpan.FromSeconds(5), ReconnectSchedule.Default.GetDelay(100));
    }

    [Fact]
    public void Negative_attempt_index_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectSchedule.Default.GetDelay(-1));
    }
}
