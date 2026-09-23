using God2.ServerV2.Application;

namespace God2.ServerV2.Network;

/// <summary>
/// Authoritative in-memory view of NPC spawns that belong to loaded maps.
///
/// NPC lifetime is map-scoped, not connection-scoped. A player entering or
/// leaving the world must not create or destroy the authoritative NPC state.
/// Wire eligibility remains a separate concern handled by the protocol layer.
/// </summary>
public sealed class WorldNpcRegistry
{
    private readonly object _gate = new();

    private readonly Dictionary<long, IReadOnlyList<NpcSnapshotEntry>>
        _byMap = [];

    public int LoadedMapCount
    {
        get
        {
            lock (_gate)
            {
                return _byMap.Count;
            }
        }
    }

    public int NpcCount
    {
        get
        {
            lock (_gate)
            {
                return _byMap.Values.Sum(entries => entries.Count);
            }
        }
    }

    /// <summary>
    /// Publishes the authoritative snapshot for a map.
    ///
    /// Re-publishing the same map replaces its previous snapshot atomically.
    /// This is intentionally independent of player connections.
    /// </summary>
    public void PublishMap(
        long mapId,
        IReadOnlyList<NpcSnapshotEntry> entries)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Any(entry => entry.MapId != mapId))
        {
            throw new ArgumentException(
                "All NPC entries must belong to the published map.",
                nameof(entries));
        }

        if (entries
            .GroupBy(entry => entry.SpawnId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Duplicate NPC spawn IDs are not allowed within a map.",
                nameof(entries));
        }

        if (entries
            .Where(entry => entry.ClientEntityHandle.HasValue)
            .GroupBy(entry => entry.ClientEntityHandle!.Value)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Duplicate NPC client entity handles are not allowed within a map.",
                nameof(entries));
        }

        var snapshot = entries
            .OrderBy(entry => entry.SpawnId)
            .ToArray();

        lock (_gate)
        {
            _byMap[mapId] = snapshot;
        }
    }

    public bool IsMapLoaded(long mapId)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        lock (_gate)
        {
            return _byMap.ContainsKey(mapId);
        }
    }

    public IReadOnlyList<NpcSnapshotEntry> SnapshotMap(long mapId)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        lock (_gate)
        {
            return _byMap.TryGetValue(mapId, out var entries)
                ? entries.ToArray()
                : [];
        }
    }

    public bool TryGetSpawn(
        long spawnId,
        out NpcSnapshotEntry? entry)
    {
        if (spawnId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawnId));
        }

        lock (_gate)
        {
            foreach (var entries in _byMap.Values)
            {
                var match = entries.FirstOrDefault(
                    candidate => candidate.SpawnId == spawnId);

                if (match is not null)
                {
                    entry = match;
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }

    public bool TryGetByClientEntityHandle(
        long mapId,
        uint clientEntityHandle,
        out NpcSnapshotEntry? entry)
    {
        if (mapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId));
        }

        lock (_gate)
        {
            if (_byMap.TryGetValue(mapId, out var entries))
            {
                var match = entries.FirstOrDefault(
                    candidate =>
                        candidate.ClientEntityHandle ==
                        clientEntityHandle);

                if (match is not null)
                {
                    entry = match;
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }

    public IReadOnlyList<long> LoadedMapIds()
    {
        lock (_gate)
        {
            return _byMap.Keys
                .OrderBy(mapId => mapId)
                .ToArray();
        }
    }
}
