namespace MusicMic.Core;

public sealed class ReconnectSchedule
{
    private static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    public static ReconnectSchedule Default { get; } = new(DefaultDelays);

    private readonly TimeSpan[] delays;

    private ReconnectSchedule(IEnumerable<TimeSpan> delays)
    {
        this.delays = delays.ToArray();
    }

    public TimeSpan GetDelay(int attemptIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attemptIndex);

        int boundedIndex = Math.Min(attemptIndex, delays.Length - 1);
        return delays[boundedIndex];
    }
}
