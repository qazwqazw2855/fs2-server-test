using System.Collections.Immutable;

namespace God2.AdvancedHeadlessVerification;

public sealed record VirtualTimerSnapshot(long Id, long DueTicks, long Sequence, string CallbackKey, bool Cancelled);

public sealed class DeterministicVirtualClock
{
    private readonly SortedDictionary<(long DueTicks, long Sequence), VirtualTimerSnapshot> _timers = [];
    private long _sequence;

    public DeterministicVirtualClock(DateTimeOffset initial) => UtcNow = initial;

    public DateTimeOffset UtcNow { get; private set; }
    public int PendingTimerCount => _timers.Values.Count(value => !value.Cancelled);

    public long Schedule(TimeSpan delay, string callbackKey)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackKey);
        var sequence = ++_sequence;
        var timer = new VirtualTimerSnapshot(sequence, checked(UtcNow.UtcTicks + delay.Ticks), sequence, callbackKey, false);
        _timers[(timer.DueTicks, timer.Sequence)] = timer;
        return timer.Id;
    }

    public bool Cancel(long timerId)
    {
        var key = _timers.FirstOrDefault(pair => pair.Value.Id == timerId).Key;
        if (key == default || !_timers.TryGetValue(key, out var timer) || timer.Cancelled)
        {
            return false;
        }
        _timers[key] = timer with { Cancelled = true };
        return true;
    }

    public IReadOnlyList<string> AdvanceBy(TimeSpan duration) => AdvanceTo(UtcNow + duration);

    public IReadOnlyList<string> AdvanceTo(DateTimeOffset target)
    {
        if (target < UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        var fired = new List<string>();
        foreach (var pair in _timers.Where(pair => pair.Key.DueTicks <= target.UtcTicks).ToArray())
        {
            _timers.Remove(pair.Key);
            UtcNow = new DateTimeOffset(pair.Value.DueTicks, TimeSpan.Zero);
            if (!pair.Value.Cancelled)
            {
                fired.Add(pair.Value.CallbackKey);
            }
        }
        UtcNow = target;
        return fired;
    }

    public VerificationClockSnapshot Snapshot() => new(
        UtcNow.UtcTicks,
        _sequence,
        _timers.Values.Select(value => value with { }).ToImmutableArray());

    public static DeterministicVirtualClock Restore(VerificationClockSnapshot snapshot)
    {
        var clock = new DeterministicVirtualClock(new DateTimeOffset(snapshot.UtcTicks, TimeSpan.Zero))
        {
            _sequence = snapshot.Sequence
        };
        foreach (var timer in snapshot.Timers)
        {
            clock._timers[(timer.DueTicks, timer.Sequence)] = timer with { };
        }
        return clock;
    }
}
