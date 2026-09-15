namespace God2.ServerV2.Network;

public sealed class WorldActivityTracker
{
    public static readonly TimeSpan DefaultIdleTimeout =
        TimeSpan.FromSeconds(30);

    private readonly TimeSpan _idleTimeout;
    private DateTimeOffset _lastActivityUtc;

    public WorldActivityTracker(
        DateTimeOffset startedAtUtc,
        TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;

        if (_idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        _lastActivityUtc = startedAtUtc;
    }

    public void RecordActivity(DateTimeOffset activityAtUtc)
    {
        if (activityAtUtc < _lastActivityUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(activityAtUtc));
        }

        _lastActivityUtc = activityAtUtc;
    }

    public TimeSpan GetRemaining(DateTimeOffset nowUtc)
    {
        var remaining = _idleTimeout - (nowUtc - _lastActivityUtc);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
