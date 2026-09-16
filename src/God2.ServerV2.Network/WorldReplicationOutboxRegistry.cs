namespace God2.ServerV2.Network;

public enum WorldReplicationEventKind
{
    PlayerEntered,
    PlayerLeft,
    PlayerMoved
}

public readonly record struct WorldReplicationMovement(
    ushort X,
    ushort Y,
    byte Sequence,
    byte State);

public sealed record WorldReplicationEvent(
    long Sequence,
    WorldReplicationEventKind Kind,
    WorldPresence Subject,
    WorldReplicationMovement? Movement,
    DateTimeOffset CreatedAtUtc);

public sealed class WorldReplicationOutboxRegistry
{
    private readonly object _gate = new();
    private readonly int _maximumEventsPerConnection;
    private readonly Dictionary<long, Queue<WorldReplicationEvent>> _outboxes = [];
    private long _nextSequence;

    public WorldReplicationOutboxRegistry(
        int maximumEventsPerConnection = 256)
    {
        if (maximumEventsPerConnection is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEventsPerConnection));
        }

        _maximumEventsPerConnection =
            maximumEventsPerConnection;
    }

    public int ConnectionCount
    {
        get
        {
            lock (_gate)
            {
                return _outboxes.Count;
            }
        }
    }

    public bool TryRegister(long connectionId)
    {
        if (connectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionId));
        }

        lock (_gate)
        {
            return _outboxes.TryAdd(
                connectionId,
                new Queue<WorldReplicationEvent>());
        }
    }

    public bool TryEnqueue(
        long recipientConnectionId,
        WorldReplicationEventKind kind,
        WorldPresence subject,
        DateTimeOffset nowUtc,
        out WorldReplicationEvent? queuedEvent)
    {
        if (kind == WorldReplicationEventKind.PlayerMoved)
        {
            throw new ArgumentException(
                "PlayerMoved requires a movement payload.",
                nameof(kind));
        }

        return TryEnqueueCore(
            recipientConnectionId,
            kind,
            subject,
            movement: null,
            nowUtc,
            out queuedEvent);
    }

    public bool TryEnqueueMovement(
        long recipientConnectionId,
        WorldPresence subject,
        WorldReplicationMovement movement,
        DateTimeOffset nowUtc,
        out WorldReplicationEvent? queuedEvent) =>
        TryEnqueueCore(
            recipientConnectionId,
            WorldReplicationEventKind.PlayerMoved,
            subject,
            movement,
            nowUtc,
            out queuedEvent);

    private bool TryEnqueueCore(
        long recipientConnectionId,
        WorldReplicationEventKind kind,
        WorldPresence subject,
        WorldReplicationMovement? movement,
        DateTimeOffset nowUtc,
        out WorldReplicationEvent? queuedEvent)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (_gate)
        {
            if (!_outboxes.TryGetValue(
                    recipientConnectionId,
                    out var outbox))
            {
                queuedEvent = null;
                return false;
            }

            while (outbox.Count >=
                   _maximumEventsPerConnection)
            {
                outbox.Dequeue();
            }

            queuedEvent = new WorldReplicationEvent(
                checked(++_nextSequence),
                kind,
                subject,
                movement,
                nowUtc);

            outbox.Enqueue(queuedEvent);
            return true;
        }
    }

    public IReadOnlyList<WorldReplicationEvent> Snapshot(
        long connectionId)
    {
        lock (_gate)
        {
            return _outboxes.TryGetValue(
                    connectionId,
                    out var outbox)
                ? outbox.ToArray()
                : [];
        }
    }

    public bool TryRemove(
        long connectionId,
        out IReadOnlyList<WorldReplicationEvent> discardedEvents)
    {
        lock (_gate)
        {
            if (!_outboxes.Remove(
                    connectionId,
                    out var outbox))
            {
                discardedEvents = [];
                return false;
            }

            discardedEvents = outbox.ToArray();
            return true;
        }
    }
}
