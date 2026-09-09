using System.Collections.Concurrent;

namespace God2.ClassicServer.Runtime;

public enum ReplicationQueueKind
{
    Spawn,
    Update,
    Despawn
}

public sealed record DirtyRuntimeObject(
    long RuntimeObjectId,
    RuntimeObjectKind ObjectKind,
    int MapId,
    string Reason,
    DateTimeOffset ChangedAtUtc);

public sealed class DirtyTracker
{
    private readonly ConcurrentDictionary<long, DirtyRuntimeObject> _dirty = [];

    public int Count => _dirty.Count;

    public IReadOnlyList<DirtyRuntimeObject> Snapshot => _dirty.Values
        .OrderBy(entry => entry.ChangedAtUtc)
        .ThenBy(entry => entry.RuntimeObjectId)
        .ToArray();

    public void MarkDirty(IRuntimeObject runtimeObject, string reason, DateTimeOffset changedAtUtc)
    {
        _dirty[runtimeObject.Identity.RuntimeObjectId] = new DirtyRuntimeObject(
            runtimeObject.Identity.RuntimeObjectId,
            runtimeObject.Identity.Kind,
            runtimeObject.Identity.MapId,
            reason,
            changedAtUtc);
    }

    public IReadOnlyList<DirtyRuntimeObject> Drain()
    {
        var entries = Snapshot;
        foreach (var entry in entries)
        {
            _dirty.TryRemove(entry.RuntimeObjectId, out _);
        }

        return entries;
    }
}

public sealed record AoiQuery(
    int MapId,
    WorldPosition3 Center,
    int Radius);

public sealed class AreaOfInterestRuntime
{
    public bool Contains(AoiQuery query, IRuntimeObject runtimeObject)
    {
        if (runtimeObject.State is not IMapPositionedRuntimeState state || state.MapId != query.MapId)
        {
            return false;
        }

        var dx = state.Position.X - query.Center.X;
        var dy = state.Position.Y - query.Center.Y;
        return checked((dx * dx) + (dy * dy)) <= checked(query.Radius * query.Radius);
    }
}

public sealed record VisibilityRecord(
    string SessionId,
    long RuntimeObjectId,
    RuntimeObjectKind ObjectKind,
    int MapId,
    bool IsVisible,
    string Reason,
    DateTimeOffset ObservedAtUtc);

public sealed class VisibilityRuntime
{
    private readonly ConcurrentDictionary<string, VisibilityRecord> _records = [];
    private readonly AreaOfInterestRuntime _aoi;

    public VisibilityRuntime(AreaOfInterestRuntime? aoi = null)
    {
        _aoi = aoi ?? new AreaOfInterestRuntime();
    }

    public int Count => _records.Count;

    public IReadOnlyList<VisibilityRecord> Snapshot => _records.Values
        .OrderBy(record => record.SessionId, StringComparer.Ordinal)
        .ThenBy(record => record.RuntimeObjectId)
        .ToArray();

    public int VisibleCount(long runtimeObjectId) =>
        _records.Values.Count(record => record.RuntimeObjectId == runtimeObjectId && record.IsVisible);

    public IReadOnlyList<VisibilityRecord> Recalculate(
        MapSession session,
        WorldPosition3 observerPosition,
        IEnumerable<IRuntimeObject> objects,
        int radius,
        DateTimeOffset observedAtUtc,
        Func<IRuntimeObject, bool>? initialSnapshotOverride = null)
    {
        var query = new AoiQuery(session.MapId, observerPosition, radius);
        var changes = new List<VisibilityRecord>();
        foreach (var runtimeObject in objects)
        {
            if (runtimeObject.Identity.RuntimeObjectId == session.PlayerRuntimeEntityId ||
                runtimeObject.Identity.Kind is RuntimeObjectKind.World or RuntimeObjectKind.Map or RuntimeObjectKind.Merchant)
            {
                continue;
            }

            var key = $"{session.SessionId}\u001f{runtimeObject.Identity.RuntimeObjectId}";
            var initialSnapshotVisible = initialSnapshotOverride?.Invoke(runtimeObject) == true;
            var visible = initialSnapshotVisible || _aoi.Contains(query, runtimeObject);
            var record = new VisibilityRecord(
                session.SessionId,
                runtimeObject.Identity.RuntimeObjectId,
                runtimeObject.Identity.Kind,
                runtimeObject.Identity.MapId,
                visible,
                initialSnapshotVisible
                    ? "Build-pinned initial world snapshot"
                    : visible ? "Inside AOI" : "Outside AOI",
                observedAtUtc);

            if (!_records.TryGetValue(key, out var previous) || previous.IsVisible != visible)
            {
                changes.Add(record);
            }

            _records[key] = record;
        }

        return changes;
    }

    public int ClearSession(string sessionId)
    {
        var removed = 0;
        foreach (var key in _records.Keys.Where(key => key.StartsWith(sessionId + "\u001f", StringComparison.Ordinal)).ToArray())
        {
            if (_records.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }
}

public sealed record RuntimeReplicationEvent(
    long EventId,
    ReplicationQueueKind Queue,
    long RuntimeObjectId,
    RuntimeObjectKind ObjectKind,
    int MapId,
    OfficialSerializerStatus SerializerStatus,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    string? TargetSessionId = null);

public sealed class ReplicationRuntime
{
    private long _nextEventId;
    private readonly ConcurrentDictionary<string, byte> _spawnedBySessionObject = [];

    public DirtyTracker DirtyTracking { get; } = new();

    public VisibilityRuntime Visibility { get; } = new();

    public WorldRuntimeQueue<RuntimeReplicationEvent> SpawnQueue { get; } = new();

    public WorldRuntimeQueue<RuntimeReplicationEvent> UpdateQueue { get; } = new();

    public WorldRuntimeQueue<RuntimeReplicationEvent> DespawnQueue { get; } = new();

    public void RecordObjectCreated(IRuntimeObject runtimeObject, DateTimeOffset now)
    {
        DirtyTracking.MarkDirty(runtimeObject, "ObjectCreated", now);
    }

    public void RecordSpawn(
        IRuntimeObject runtimeObject,
        OfficialSerializerStatus serializerStatus,
        string reason,
        DateTimeOffset now)
    {
        DirtyTracking.MarkDirty(runtimeObject, "Spawn", now);
        EnqueueForVisibleSessions(SpawnQueue, ReplicationQueueKind.Spawn, runtimeObject, serializerStatus, reason, now, removeSpawnMarker: false);
    }

    public void RecordUpdate(
        IRuntimeObject runtimeObject,
        OfficialSerializerStatus serializerStatus,
        string reason,
        DateTimeOffset now)
    {
        DirtyTracking.MarkDirty(runtimeObject, "Update", now);
        EnqueueForVisibleSessions(UpdateQueue, ReplicationQueueKind.Update, runtimeObject, serializerStatus, reason, now, removeSpawnMarker: false);
    }

    public void RecordDespawn(
        IRuntimeObject runtimeObject,
        OfficialSerializerStatus serializerStatus,
        string reason,
        DateTimeOffset now)
    {
        DirtyTracking.MarkDirty(runtimeObject, "Despawn", now);
        EnqueueForVisibleSessions(DespawnQueue, ReplicationQueueKind.Despawn, runtimeObject, serializerStatus, reason, now, removeSpawnMarker: true);
    }

    public IReadOnlyList<VisibilityRecord> RecalculateVisibility(
        MapSession session,
        WorldPosition3 observerPosition,
        IEnumerable<IRuntimeObject> runtimeObjects,
        int radius,
        DateTimeOffset now)
    {
        return RecalculateVisibilityCore(session, observerPosition, runtimeObjects, radius, now, null);
    }

    public IReadOnlyList<VisibilityRecord> RecalculateInitialVisibility(
        MapSession session,
        WorldPosition3 observerPosition,
        IEnumerable<IRuntimeObject> runtimeObjects,
        int radius,
        DateTimeOffset now) =>
        RecalculateVisibilityCore(
            session,
            observerPosition,
            runtimeObjects,
            radius,
            now,
            runtimeObject => runtimeObject is NpcObject npc &&
                npc.State.MapId == session.MapId &&
                OfficialNpcReplicationWireCodec.Validate(npc.State) is null);

    private IReadOnlyList<VisibilityRecord> RecalculateVisibilityCore(
        MapSession session,
        WorldPosition3 observerPosition,
        IEnumerable<IRuntimeObject> runtimeObjects,
        int radius,
        DateTimeOffset now,
        Func<IRuntimeObject, bool>? initialSnapshotOverride)
    {
        var objects = runtimeObjects.ToArray();
        var objectsById = objects.ToDictionary(entry => entry.Identity.RuntimeObjectId);
        var changes = Visibility.Recalculate(session, observerPosition, objects, radius, now, initialSnapshotOverride);
        foreach (var change in changes.Where(change => change.IsVisible))
        {
            var serializerStatus = objectsById.TryGetValue(change.RuntimeObjectId, out var runtimeObject)
                ? SerializerStatusFor(runtimeObject)
                : OfficialSerializerStatus.SerializerBlockedByEvidence;
            if (TryMarkSpawned(session.SessionId, change.RuntimeObjectId))
            {
                SpawnQueue.Enqueue(new RuntimeReplicationEvent(
                    Interlocked.Increment(ref _nextEventId),
                    ReplicationQueueKind.Spawn,
                    change.RuntimeObjectId,
                    change.ObjectKind,
                    change.MapId,
                    serializerStatus,
                    change.Reason,
                    now,
                    session.SessionId));
            }

        }

        foreach (var change in changes.Where(change => !change.IsVisible))
        {
            var serializerStatus = objectsById.TryGetValue(change.RuntimeObjectId, out var runtimeObject)
                ? SerializerStatusFor(runtimeObject)
                : OfficialSerializerStatus.SerializerBlockedByEvidence;
            if (TryMarkDespawned(session.SessionId, change.RuntimeObjectId))
            {
                DespawnQueue.Enqueue(new RuntimeReplicationEvent(
                    Interlocked.Increment(ref _nextEventId),
                    ReplicationQueueKind.Despawn,
                    change.RuntimeObjectId,
                    change.ObjectKind,
                    change.MapId,
                    serializerStatus,
                    change.Reason,
                    now,
                    session.SessionId));
            }
        }

        return changes;
    }

    public int ClearSession(string sessionId)
    {
        var removed = Visibility.ClearSession(sessionId);
        foreach (var key in _spawnedBySessionObject.Keys.Where(key => key.StartsWith(sessionId + "\u001f", StringComparison.Ordinal)).ToArray())
        {
            if (_spawnedBySessionObject.TryRemove(key, out _))
            {
                removed++;
            }
        }

        removed += SpawnQueue.RemoveWhere(entry => string.Equals(entry.TargetSessionId, sessionId, StringComparison.Ordinal));
        removed += UpdateQueue.RemoveWhere(entry => string.Equals(entry.TargetSessionId, sessionId, StringComparison.Ordinal));
        removed += DespawnQueue.RemoveWhere(entry => string.Equals(entry.TargetSessionId, sessionId, StringComparison.Ordinal));
        return removed;
    }

    public int PendingCount(ReplicationQueueKind queue, long runtimeObjectId)
    {
        var events = queue switch
        {
            ReplicationQueueKind.Spawn => SpawnQueue.Snapshot,
            ReplicationQueueKind.Update => UpdateQueue.Snapshot,
            ReplicationQueueKind.Despawn => DespawnQueue.Snapshot,
            _ => []
        };
        return events.Count(entry => entry.RuntimeObjectId == runtimeObjectId);
    }

    private void EnqueueForVisibleSessions(
        WorldRuntimeQueue<RuntimeReplicationEvent> targetQueue,
        ReplicationQueueKind queue,
        IRuntimeObject runtimeObject,
        OfficialSerializerStatus serializerStatus,
        string reason,
        DateTimeOffset now,
        bool removeSpawnMarker)
    {
        var sessionIds = Visibility.Snapshot
            .Where(record =>
                record.RuntimeObjectId == runtimeObject.Identity.RuntimeObjectId &&
                record.MapId == runtimeObject.Identity.MapId &&
                record.IsVisible)
            .Select(record => record.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var sessionId in sessionIds)
        {
            var markerKey = $"{sessionId}\u001f{runtimeObject.Identity.RuntimeObjectId}";
            if (removeSpawnMarker)
            {
                if (!_spawnedBySessionObject.TryRemove(markerKey, out _))
                {
                    continue;
                }
            }
            else if (!_spawnedBySessionObject.ContainsKey(markerKey))
            {
                continue;
            }

            if (queue == ReplicationQueueKind.Spawn && targetQueue.Snapshot.Any(candidate =>
                candidate.RuntimeObjectId == runtimeObject.Identity.RuntimeObjectId &&
                string.Equals(candidate.TargetSessionId, sessionId, StringComparison.Ordinal)))
            {
                continue;
            }

            targetQueue.Enqueue(new RuntimeReplicationEvent(
                Interlocked.Increment(ref _nextEventId),
                queue,
                runtimeObject.Identity.RuntimeObjectId,
                runtimeObject.Identity.Kind,
                runtimeObject.Identity.MapId,
                serializerStatus,
                reason,
                now,
                sessionId));
        }
    }

    private bool TryMarkSpawned(string sessionId, long runtimeObjectId) =>
        _spawnedBySessionObject.TryAdd($"{sessionId}\u001f{runtimeObjectId}", 0);

    private bool TryMarkDespawned(string sessionId, long runtimeObjectId) =>
        _spawnedBySessionObject.TryRemove($"{sessionId}\u001f{runtimeObjectId}", out _);

    private static OfficialSerializerStatus SerializerStatusFor(IRuntimeObject runtimeObject) =>
        runtimeObject switch
        {
            PlayerObject => OfficialSerializerStatus.Ready,
            NpcObject npc when OfficialNpcReplicationWireCodec.Validate(npc.State) is null => OfficialSerializerStatus.Ready,
            _ => OfficialSerializerStatus.SerializerBlockedByEvidence
        };
}
