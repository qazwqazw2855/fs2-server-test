namespace God2.ClassicServer.Runtime;

public interface IRuntimeFrameSender
{
    ValueTask SendAsync(string sessionId, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
}

public sealed record RuntimeReplicationBlock(
    long RuntimeObjectId,
    RuntimeObjectKind ObjectKind,
    string Reason);

public sealed record RuntimeReplicationEmissionResult(
    int SentFrames,
    int BlockedFrames,
    IReadOnlyList<RuntimeReplicationBlock> Blocks);

public sealed class RuntimeReplicationEmitter
{
    public int AcknowledgeEmbeddedInitialNpcSpawns(
        MapRuntime runtime,
        string sessionId,
        IReadOnlyCollection<long> runtimeObjectIds)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(runtimeObjectIds);
        if (runtimeObjectIds.Count == 0)
        {
            return 0;
        }

        var embedded = runtimeObjectIds.ToHashSet();
        return runtime.Replication.SpawnQueue.RemoveWhere(entry =>
            entry.ObjectKind == RuntimeObjectKind.Npc &&
            embedded.Contains(entry.RuntimeObjectId) &&
            string.Equals(entry.TargetSessionId, sessionId, StringComparison.Ordinal));
    }

    public RuntimeReplicationEmissionResult PreflightSpawnQueue(MapRuntime runtime, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var prepared = PrepareQueue(runtime, runtime.Replication.SpawnQueue, sessionId, ReplicationQueueKind.Spawn);
        ClearPreparedFrames(prepared.Frames);
        return new RuntimeReplicationEmissionResult(0, prepared.Blocks.Count, prepared.Blocks);
    }

    public RuntimeReplicationEmissionResult PreflightPendingQueues(MapRuntime runtime, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var prepared = PreparePendingQueues(runtime, sessionId);
        ClearPreparedFrames(prepared.Frames);
        return new RuntimeReplicationEmissionResult(
            0,
            prepared.Blocks.Count,
            prepared.Blocks);
    }

    public async Task<RuntimeReplicationEmissionResult> FlushSpawnQueueAsync(
        MapRuntime runtime,
        string sessionId,
        IRuntimeFrameSender sender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sender);

        var prepared = PrepareQueue(runtime, runtime.Replication.SpawnQueue, sessionId, ReplicationQueueKind.Spawn);
        return await FlushPreparedAsync(prepared, sessionId, sender, cancellationToken);
    }

    public async Task<RuntimeReplicationEmissionResult> FlushAsync(
        MapRuntime runtime,
        string sessionId,
        IRuntimeFrameSender sender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sender);
        var prepared = PreparePendingQueues(runtime, sessionId);
        return await FlushPreparedAsync(prepared, sessionId, sender, cancellationToken);
    }

    private static PreparedRuntimeReplication PreparePendingQueues(MapRuntime runtime, string sessionId)
    {
        var results = new[]
        {
            PrepareQueue(runtime, runtime.Replication.SpawnQueue, sessionId, ReplicationQueueKind.Spawn),
            PrepareQueue(runtime, runtime.Replication.UpdateQueue, sessionId, ReplicationQueueKind.Update),
            PrepareQueue(runtime, runtime.Replication.DespawnQueue, sessionId, ReplicationQueueKind.Despawn)
        };
        return new PreparedRuntimeReplication(
            results.SelectMany(result => result.Frames).ToArray(),
            results.SelectMany(result => result.BlockedEvents).ToArray());
    }

    private static PreparedRuntimeReplication PrepareQueue(
        MapRuntime runtime,
        WorldRuntimeQueue<RuntimeReplicationEvent> queue,
        string sessionId,
        ReplicationQueueKind queueKind)
    {
        var frames = new List<PreparedRuntimeFrame>();
        var blockedEvents = new List<PreparedBlockedRuntimeEvent>();
        foreach (var entry in queue.Snapshot.Where(entry =>
            string.Equals(entry.TargetSessionId, sessionId, StringComparison.Ordinal)))
        {
            if (entry.SerializerStatus != OfficialSerializerStatus.Ready)
            {
                blockedEvents.Add(new PreparedBlockedRuntimeEvent(
                    queue,
                    entry,
                    new RuntimeReplicationBlock(entry.RuntimeObjectId, entry.ObjectKind, entry.Reason)));
                continue;
            }

            var runtimeObject = runtime.Objects.Get(entry.RuntimeObjectId);
            if (!runtimeObject.Succeeded || runtimeObject.Value is null)
            {
                blockedEvents.Add(new PreparedBlockedRuntimeEvent(
                    queue,
                    entry,
                    new RuntimeReplicationBlock(entry.RuntimeObjectId, entry.ObjectKind, "Runtime object is no longer registered.")));
                continue;
            }

            if (queueKind == ReplicationQueueKind.Spawn &&
                runtime.BroadcastEvents.Count != 0 &&
                !runtime.BroadcastEvents.Snapshot.Any(broadcast =>
                    broadcast.Kind == SemanticBroadcastKind.EntityEnteredVisibility &&
                    broadcast.RuntimeEntityId == entry.RuntimeObjectId &&
                    broadcast.MapId == entry.MapId))
            {
                blockedEvents.Add(new PreparedBlockedRuntimeEvent(
                    queue,
                    entry,
                    new RuntimeReplicationBlock(
                        entry.RuntimeObjectId,
                        entry.ObjectKind,
                        "Spawn replication has no matching authoritative SemanticBroadcastEvent.")));
                continue;
            }

            var serialized = Serialize(runtimeObject.Value, queueKind);
            if (serialized.Status != OfficialSerializerStatus.Ready || serialized.Frame.Length == 0)
            {
                blockedEvents.Add(new PreparedBlockedRuntimeEvent(
                    queue,
                    entry,
                    new RuntimeReplicationBlock(entry.RuntimeObjectId, entry.ObjectKind, serialized.Reason)));
                continue;
            }

            frames.Add(new PreparedRuntimeFrame(queue, entry, serialized.Frame));
        }

        return new PreparedRuntimeReplication(frames, blockedEvents);
    }

    private static async Task<RuntimeReplicationEmissionResult> FlushPreparedAsync(
        PreparedRuntimeReplication prepared,
        string sessionId,
        IRuntimeFrameSender sender,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(sender);
        var sent = 0;
        try
        {
            foreach (var frame in prepared.Frames)
            {
                await sender.SendAsync(sessionId, frame.Bytes, cancellationToken);
                frame.Queue.RemoveWhere(candidate => candidate.EventId == frame.Event.EventId);
                sent++;
            }

            return new RuntimeReplicationEmissionResult(
                sent,
                prepared.BlockedEvents.Count,
                prepared.BlockedEvents.Select(entry => entry.Block).ToArray());
        }
        finally
        {
            // Evidence-blocked families emit no bytes. Their session-scoped queue entries are
            // acknowledged as quarantined so one unsupported family cannot retain an
            // unbounded queue or suppress independent evidence-ready NPC frames.
            foreach (var blocked in prepared.BlockedEvents)
            {
                blocked.Queue.RemoveWhere(candidate => candidate.EventId == blocked.Event.EventId);
            }

            ClearPreparedFrames(prepared.Frames);
        }
    }

    private static void ClearPreparedFrames(IEnumerable<PreparedRuntimeFrame> frames)
    {
        foreach (var frame in frames)
        {
            Array.Clear(frame.Bytes);
        }
    }

    private static RuntimeSerializationResult Serialize(IRuntimeObject runtimeObject, ReplicationQueueKind queue) =>
        (runtimeObject, queue) switch
        {
            (PlayerObject player, ReplicationQueueKind.Spawn) => new PlayerSerializer().Serialize(player.State),
            (NpcObject npc, ReplicationQueueKind.Spawn) => new NpcSerializer().Serialize(npc.State),
            (NpcObject npc, ReplicationQueueKind.Update) => new NpcPositionUpdateSerializer().Serialize(npc.State),
            (NpcObject npc, ReplicationQueueKind.Despawn) => new NpcDespawnSerializer().Serialize(npc.State),
            (MonsterObject monster, ReplicationQueueKind.Spawn) => new MonsterSerializer().Serialize(monster.State),
            (PortalObject portal, ReplicationQueueKind.Spawn) => new PortalSerializer().Serialize(portal.State),
            (MerchantObject merchant, ReplicationQueueKind.Spawn) => new MerchantSerializer().Serialize(merchant.State),
            _ => RuntimeSerializationResult.Blocked(RuntimeSerializerKind.Npc, $"No serializer is registered for {runtimeObject.Identity.Kind}.")
        };

    private sealed record PreparedRuntimeFrame(
        WorldRuntimeQueue<RuntimeReplicationEvent> Queue,
        RuntimeReplicationEvent Event,
        byte[] Bytes);

    private sealed record PreparedBlockedRuntimeEvent(
        WorldRuntimeQueue<RuntimeReplicationEvent> Queue,
        RuntimeReplicationEvent Event,
        RuntimeReplicationBlock Block);

    private sealed record PreparedRuntimeReplication(
        IReadOnlyList<PreparedRuntimeFrame> Frames,
        IReadOnlyList<PreparedBlockedRuntimeEvent> BlockedEvents)
    {
        public IReadOnlyList<RuntimeReplicationBlock> Blocks =>
            BlockedEvents.Select(entry => entry.Block).ToArray();
    }
}

public sealed class RecordingRuntimeFrameSender : IRuntimeFrameSender
{
    private readonly List<(string SessionId, byte[] Frame)> _frames = [];

    public IReadOnlyList<(string SessionId, byte[] Frame)> Frames => _frames.ToArray();

    public ValueTask SendAsync(string sessionId, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _frames.Add((sessionId, frame.ToArray()));
        return ValueTask.CompletedTask;
    }
}
