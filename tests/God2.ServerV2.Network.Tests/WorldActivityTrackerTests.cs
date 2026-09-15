using God2.ServerV2.Network;

namespace God2.ServerV2.Network.Tests;

public sealed class WorldActivityTrackerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_timeout_is_thirty_seconds()
    {
        var tracker = new WorldActivityTracker(Now);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            tracker.GetRemaining(Now));
    }

    [Fact]
    public void Complete_frame_resets_idle_deadline()
    {
        var tracker = new WorldActivityTracker(Now);

        tracker.RecordActivity(Now.AddSeconds(20));

        Assert.Equal(
            TimeSpan.FromSeconds(25),
            tracker.GetRemaining(Now.AddSeconds(25)));
    }

    [Fact]
    public void Expired_connection_has_no_remaining_time()
    {
        var tracker = new WorldActivityTracker(Now);

        Assert.Equal(
            TimeSpan.Zero,
            tracker.GetRemaining(Now.AddSeconds(30)));
    }

    [Fact]
    public void Activity_time_cannot_move_backwards()
    {
        var tracker = new WorldActivityTracker(Now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tracker.RecordActivity(Now.AddSeconds(-1)));
    }
}
