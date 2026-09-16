using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

public sealed record NpcInteractionSession(
    long ConnectionId,
    long CharacterId,
    long MapId,
    long SpawnId,
    uint ClientEntityHandle,
    DateTimeOffset OpenedAtUtc);

public enum NpcInteractionOpenStatus
{
    Opened,
    ConnectionAlreadyInteracting,
    TargetNotVisible
}

public readonly record struct NpcInteractionOpenResult(
    NpcInteractionOpenStatus Status,
    NpcInteractionSession? Session)
{
    public bool Succeeded =>
        Status == NpcInteractionOpenStatus.Opened;
}

public enum NpcInteractionCloseStatus
{
    Closed,
    NoActiveInteraction,
    HandleMismatch
}

public sealed class NpcInteractionSessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<long, NpcInteractionSession>
        _byConnection = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byConnection.Count;
            }
        }
    }

    public NpcInteractionOpenResult TryOpen(
        long connectionId,
        long characterId,
        long mapId,
        uint clientEntityHandle,
        IReadOnlyList<NpcSnapshotEntry> visibleNpcs,
        DateTimeOffset nowUtc)
    {
        if (connectionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionId));
        }

        if (characterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(characterId));
        }

        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mapId));
        }

        ArgumentNullException.ThrowIfNull(visibleNpcs);

        lock (_gate)
        {
            if (_byConnection.TryGetValue(
                    connectionId,
                    out var existing))
            {
                return new NpcInteractionOpenResult(
                    NpcInteractionOpenStatus
                        .ConnectionAlreadyInteracting,
                    existing);
            }

            var target = visibleNpcs.SingleOrDefault(
                npc =>
                    npc.MapId == mapId &&
                    npc.ClientEntityHandle ==
                        clientEntityHandle);

            if (target is null)
            {
                return new NpcInteractionOpenResult(
                    NpcInteractionOpenStatus.TargetNotVisible,
                    null);
            }

            var session = new NpcInteractionSession(
                connectionId,
                characterId,
                mapId,
                target.SpawnId,
                clientEntityHandle,
                nowUtc);

            _byConnection.Add(connectionId, session);

            return new NpcInteractionOpenResult(
                NpcInteractionOpenStatus.Opened,
                session);
        }
    }

    public NpcInteractionCloseStatus TryClose(
        long connectionId,
        uint clientEntityHandle,
        out NpcInteractionSession? closedSession)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(
                    connectionId,
                    out var existing))
            {
                closedSession = null;
                return NpcInteractionCloseStatus
                    .NoActiveInteraction;
            }

            if (existing.ClientEntityHandle !=
                clientEntityHandle)
            {
                closedSession = null;
                return NpcInteractionCloseStatus
                    .HandleMismatch;
            }

            _byConnection.Remove(connectionId);
            closedSession = existing;

            return NpcInteractionCloseStatus.Closed;
        }
    }

    public bool Remove(
        long connectionId,
        out NpcInteractionSession? removedSession)
    {
        lock (_gate)
        {
            return _byConnection.Remove(
                connectionId,
                out removedSession);
        }
    }
}
