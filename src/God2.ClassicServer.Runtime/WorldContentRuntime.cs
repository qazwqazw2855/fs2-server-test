using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum WorldContentMode
{
    FrozenCompatibility,
    MariaDbAuthoritative
}

public enum WorldContentAuthorityKind
{
    TestOnly,
    MariaDb
}

public enum WorldRuntimeEntityType
{
    Player,
    NPC,
    Monster,
    Portal,
    Merchant,
    Object
}

public enum WorldDirection
{
    Unknown,
    North,
    South,
    East,
    West,
    NorthEast,
    NorthWest,
    SouthEast,
    SouthWest
}

public enum SemanticBroadcastKind
{
    EntityEnteredVisibility,
    EntityStateChanged,
    EntityLeftVisibility
}

public enum OfficialSerializerStatus
{
    Ready,
    SerializerBlockedByEvidence
}

public sealed record WorldPosition3(int X, int Y, int Z = 0);

public sealed record MapBounds(int MinX, int MinY, int MaxX, int MaxY);

public static class OfficialClientWorldCoordinateGrid
{
    public const int UnitsPerResourceCell = 21;

    public const int MaximumPackedCoordinate = 0x7FFF;

    public const string ConsumerEvidence =
        "God2_opt+rva-0x0008E14C stores raw 15-bit X/Y; rva-0x00064B90 validates X/21 and Y/21 against resource grid dimensions";

    public static bool TryCreateBounds(int? gridWidth, int? gridHeight, out MapBounds bounds)
    {
        bounds = new MapBounds(0, 0, 0, 0);
        if (gridWidth is null or <= 0 || gridHeight is null or <= 0)
        {
            return false;
        }

        var maximumX = ((long)gridWidth.Value * UnitsPerResourceCell) - 1;
        var maximumY = ((long)gridHeight.Value * UnitsPerResourceCell) - 1;
        if (maximumX > MaximumPackedCoordinate || maximumY > MaximumPackedCoordinate)
        {
            return false;
        }

        bounds = new MapBounds(0, 0, checked((int)maximumX), checked((int)maximumY));
        return true;
    }

    public static (int X, int Y) ToResourceCell(WorldPosition3 position) =>
        (position.X / UnitsPerResourceCell, position.Y / UnitsPerResourceCell);
}

public enum MapIdentityEvidenceStatus
{
    Verified,
    Derived,
    Candidate,
    EvidenceBlocked
}

public sealed record ClientMapIdentity(
    int DatabaseMapId,
    ushort ClientMapId,
    byte ClientAreaId,
    string ResourceIdentity,
    string ClientBuildId,
    decimal CoordinateScaleX,
    decimal CoordinateScaleY,
    decimal CoordinateOffsetX,
    decimal CoordinateOffsetY,
    MapIdentityEvidenceStatus IdentityEvidenceStatus,
    MapIdentityEvidenceStatus CoordinateEvidenceStatus,
    bool ProductionEnabled,
    string Evidence);

public sealed record ClientMapProjection(
    int DatabaseMapId,
    ushort ClientMapId,
    byte ClientAreaId,
    ushort ClientX,
    ushort ClientY,
    string ClientBuildId,
    string ResourceIdentity,
    string Evidence);

public sealed class ClientMapIdentityResolver
{
    public OperationResult<ClientMapProjection> Resolve(
        MapDefinition map,
        WorldPosition3 authoritativePosition,
        string clientBuildId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientBuildId);
        var identity = map.ClientIdentity;
        if (identity is null ||
            !string.Equals(identity.ClientBuildId, clientBuildId, StringComparison.Ordinal) ||
            identity.DatabaseMapId != map.MapId)
        {
            return OperationResult<ClientMapProjection>.Failure(
                "client_map.identity_missing",
                "The authoritative map has no identity mapping for the requested official Client build.",
                map.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!identity.ProductionEnabled ||
            identity.IdentityEvidenceStatus is not (MapIdentityEvidenceStatus.Verified or MapIdentityEvidenceStatus.Derived) ||
            identity.CoordinateEvidenceStatus is not (MapIdentityEvidenceStatus.Verified or MapIdentityEvidenceStatus.Derived))
        {
            return OperationResult<ClientMapProjection>.Failure(
                "client_map.identity_not_promoted",
                "The Client map identity or coordinate transform has not passed the production promotion gate.",
                identity.Evidence);
        }

        if (string.IsNullOrWhiteSpace(identity.ResourceIdentity) ||
            identity.ClientMapId > 1023 ||
            identity.ClientAreaId > 63 ||
            identity.CoordinateScaleX <= 0 ||
            identity.CoordinateScaleY <= 0)
        {
            return OperationResult<ClientMapProjection>.Failure(
                "client_map.transform_invalid",
                "The promoted Client map identity has an invalid resource identity or coordinate transform.",
                identity.Evidence);
        }

        try
        {
            var roundedX = decimal.Round(
                authoritativePosition.X * identity.CoordinateScaleX + identity.CoordinateOffsetX,
                0,
                MidpointRounding.AwayFromZero);
            var roundedY = decimal.Round(
                authoritativePosition.Y * identity.CoordinateScaleY + identity.CoordinateOffsetY,
                0,
                MidpointRounding.AwayFromZero);
            if (roundedX is < 0 or > OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate ||
                roundedY is < 0 or > OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate)
            {
                throw new OverflowException();
            }

            var clientX = checked((ushort)roundedX);
            var clientY = checked((ushort)roundedY);
            return OperationResult<ClientMapProjection>.Success(new ClientMapProjection(
                map.MapId,
                identity.ClientMapId,
                identity.ClientAreaId,
                clientX,
                clientY,
                identity.ClientBuildId,
                identity.ResourceIdentity,
                identity.Evidence));
        }
        catch (OverflowException)
        {
            return OperationResult<ClientMapProjection>.Failure(
                "client_map.coordinate_out_of_range",
                "The authoritative position cannot be represented by the verified official Client coordinate fields.",
                map.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

public sealed record SpawnPoint(string Name, WorldPosition3 Position, WorldDirection Direction, string Evidence);

public sealed record MapDefinition(
    int MapId,
    int? SceneId,
    string Name,
    string? RegionId,
    MapBounds Bounds,
    IReadOnlyList<SpawnPoint> SpawnPoints,
    IReadOnlyList<NpcPlacementRecord> NpcPlacements,
    IReadOnlyList<MonsterSpawnDefinition> MonsterSpawns,
    IReadOnlyList<PortalDefinition> Portals,
    IReadOnlyList<MerchantMapping> MerchantMappings,
    IReadOnlyList<string> ValidationErrors,
    ClientMapIdentity? ClientIdentity = null);

public sealed record NpcPlacementRecord(
    int PlacementId,
    int NpcTemplateId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string SpawnCondition,
    int? MerchantId,
    string Source,
    double Confidence,
    string Evidence,
    string Name = "Unknown",
    string Appearance = "Unknown",
    string InteractionType = "Unknown",
    bool Enabled = true,
    string RawPosition = "",
    string RawMetadata = "{}",
    string ContentVersion = "runtime-content-v1",
    OfficialNpcWireIdentity? WireIdentity = null)
{
    public int TemplateId => NpcTemplateId;

    public int? MerchantBindingId => MerchantId;
}

public sealed record MonsterSpawnDefinition(
    int SpawnId,
    int MonsterTemplateId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    TimeSpan RespawnTime,
    int SpawnRadius,
    int Count,
    string Condition,
    string Evidence,
    string Name = "Unknown",
    bool Enabled = true,
    string RawMetadata = "{}",
    string ContentVersion = "runtime-content-v1",
    int Level = 0,
    long MaximumHp = 0,
    int AttackPower = 0,
    int Defense = 0,
    long MaximumMp = 0,
    int MagicAttackPower = 0,
    int MagicDefense = 0,
    int Metal = 0,
    int Wood = 0,
    int Water = 0,
    int Fire = 0,
    int Earth = 0)
{
    public int TemplateId => MonsterTemplateId;
}

public sealed record PortalEndpoint(int MapId, WorldPosition3 Position);

public sealed record PortalDefinition(
    long PortalId,
    PortalEndpoint Source,
    PortalEndpoint Target,
    string TriggerType,
    string Requirement,
    string Evidence,
    string Name = "Unknown",
    bool Enabled = true,
    string RawMetadata = "{}",
    string ContentVersion = "runtime-content-v1",
    int TriggerRadius = 0)
{
    public int TemplateId => checked((int)(PortalId & 0x7FFFFFFF));
}

public sealed record MerchantItemDefinition(
    int ItemTemplateId,
    long Price,
    string RawMetadata = "{}",
    long? PurchasingPrice = null);

public sealed record MerchantMapping(
    int MerchantId,
    int NpcTemplateId,
    int PlacementId,
    string Evidence,
    string Name = "Unknown",
    string MerchantGroupId = "Unknown",
    string CurrencyType = "Unknown",
    IReadOnlyList<MerchantItemDefinition>? Items = null,
    bool Enabled = true,
    string RawMetadata = "{}",
    string ContentVersion = "runtime-content-v1")
{
    public int MerchantTemplateId => MerchantId;

    public IReadOnlyList<MerchantItemDefinition> RuntimeItems => Items ?? Array.AsReadOnly(Array.Empty<MerchantItemDefinition>());
}

public sealed record WorldContentSnapshot(
    IReadOnlyDictionary<int, MapDefinition> Maps,
    IReadOnlyList<string> ValidationErrors)
{
    public static WorldContentSnapshot Empty { get; } = new(
        new ReadOnlyDictionary<int, MapDefinition>(new Dictionary<int, MapDefinition>()),
        Array.AsReadOnly(Array.Empty<string>()));
}

public sealed class MapCatalog
{
    public MapCatalog(IReadOnlyDictionary<int, MapDefinition> maps)
    {
        Maps = maps;
    }

    public IReadOnlyDictionary<int, MapDefinition> Maps { get; }

    public OperationResult<MapDefinition> Resolve(int mapId) =>
        Maps.TryGetValue(mapId, out var map)
            ? OperationResult<MapDefinition>.Success(map)
            : OperationResult<MapDefinition>.Failure(
                "world_content.map_not_found",
                "Map was not found in the normalized map catalog.",
                mapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public interface IWorldContentRepository
{
    WorldContentAuthorityKind AuthorityKind { get; }

    Task<WorldContentSnapshot> LoadAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryWorldContentRepository : IWorldContentRepository
{
    private readonly WorldContentSnapshot _snapshot;

    public InMemoryWorldContentRepository(WorldContentSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.TestOnly;

    public Task<WorldContentSnapshot> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_snapshot);
}

public sealed record RuntimeEntity(
    long RuntimeEntityId,
    WorldRuntimeEntityType EntityType,
    int TemplateId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string State,
    int VisibilityRadius,
    string DirtyFlags);

public sealed class EntityRegistry
{
    private readonly ConcurrentDictionary<long, RuntimeEntity> _entities = [];

    public IReadOnlyCollection<RuntimeEntity> ActiveEntities => _entities.Values.ToArray();

    public int Count(WorldRuntimeEntityType entityType) =>
        _entities.Values.Count(entity => entity.EntityType == entityType);

    public void Add(RuntimeEntity entity)
    {
        _entities[entity.RuntimeEntityId] = entity;
    }

    public OperationResult<RuntimeEntity> Get(long runtimeEntityId) =>
        _entities.TryGetValue(runtimeEntityId, out var entity)
            ? OperationResult<RuntimeEntity>.Success(entity)
            : OperationResult<RuntimeEntity>.Failure(
                "runtime_entity.not_found",
                "Runtime entity is not active in the registry.",
                runtimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public bool Remove(long runtimeEntityId) =>
        _entities.TryRemove(runtimeEntityId, out _);
}

public sealed class EntityManager
{
    public EntityRegistry Registry { get; } = new();
}

public sealed class WorldRuntimeQueue<T>
{
    private readonly ConcurrentQueue<T> _items = [];
    private readonly object _mutationGate = new();

    public int Count
    {
        get
        {
            lock (_mutationGate)
            {
                return _items.Count;
            }
        }
    }

    public IReadOnlyList<T> Snapshot
    {
        get
        {
            lock (_mutationGate)
            {
                return _items.ToArray();
            }
        }
    }

    public void Enqueue(T item)
    {
        lock (_mutationGate)
        {
            _items.Enqueue(item);
        }
    }

    public int RemoveWhere(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_mutationGate)
        {
            var removed = 0;
            var retained = new List<T>();
            while (_items.TryDequeue(out var item))
            {
                if (predicate(item))
                {
                    removed++;
                }
                else
                {
                    retained.Add(item);
                }
            }

            foreach (var item in retained)
            {
                _items.Enqueue(item);
            }

            return removed;
        }
    }
}

public sealed record SemanticBroadcastEvent(
    long EventId,
    SemanticBroadcastKind Kind,
    long RuntimeEntityId,
    WorldRuntimeEntityType EntityType,
    int MapId,
    OfficialSerializerStatus SerializerStatus,
    string Reason);

public sealed class MapRuntime
{
    private readonly object _initialSpawnGate = new();
    private bool _initialSpawnQueueConsumed;
    private bool _monsterCombatInitialized;

    public MapRuntime(MapDefinition definition, string worldInstanceId = "world:default")
    {
        Definition = definition;
        WorldInstanceId = worldInstanceId;
        var map = new MapObject(
            RuntimeObjectIds.Map(definition.MapId, "MapRuntime"),
            new MapState(
                definition.MapId,
                definition.SceneId,
                definition.Name,
                definition.RegionId,
                definition.Bounds,
                definition.SpawnPoints,
                "RuntimeContent"));
        Objects.Add(map);
    }

    public MapDefinition Definition { get; }

    public string WorldInstanceId { get; }

    public EntityManager EntityManager { get; } = new();

    public EntityRegistry EntityRegistry => EntityManager.Registry;

    public RuntimeObjectRegistry Objects { get; } = new();

    public ReplicationRuntime Replication { get; } = new();

    public MonsterCombatRuntimeRegistry MonsterCombat { get; private set; } = new();

    public WorldRuntimeQueue<RuntimeEntity> SpawnQueue { get; } = new();

    public WorldRuntimeQueue<RuntimeEntity> UpdateQueue { get; } = new();

    public WorldRuntimeQueue<RuntimeEntity> DespawnQueue { get; } = new();

    public WorldRuntimeQueue<SemanticBroadcastEvent> BroadcastEvents { get; } = new();

    public List<MapSession> ActiveSessions { get; } = [];

    public bool InitialSpawnQueueConsumed
    {
        get
        {
            lock (_initialSpawnGate)
            {
                return _initialSpawnQueueConsumed;
            }
        }
    }

    public void InitializeMonsterCombat(MonsterCombatRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_monsterCombatInitialized)
        {
            throw new InvalidOperationException("Monster combat runtime has already been initialized for this map instance.");
        }

        MonsterCombat = registry;
        _monsterCombatInitialized = true;
    }

    public void PublishSemanticEvent(SemanticBroadcastEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MapId != Definition.MapId)
        {
            throw new InvalidOperationException("A semantic event cannot be published across MapRuntime boundaries.");
        }

        // The semantic broadcast collection is the bounded latest-event authority used to
        // validate session-scoped replication, not an append-only transport backlog.
        BroadcastEvents.RemoveWhere(existing =>
            existing.RuntimeEntityId == value.RuntimeEntityId && existing.Kind == value.Kind);
        BroadcastEvents.Enqueue(value);
    }

    /// <summary>
    /// Completes the factory-to-registry lifecycle handoff before a production map is published.
    /// Client replication has a separate, session-scoped queue; this queue only records the
    /// authoritative initial entity activation and must never remain pending indefinitely.
    /// </summary>
    public OperationResult<int> ConsumeInitialSpawnQueue()
    {
        lock (_initialSpawnGate)
        {
            if (_initialSpawnQueueConsumed)
            {
                return OperationResult<int>.Success(0);
            }

            var pending = SpawnQueue.Snapshot;
            foreach (var entity in pending)
            {
                var registered = EntityRegistry.Get(entity.RuntimeEntityId);
                var runtimeObject = Objects.Get(entity.RuntimeEntityId);
                if (!registered.Succeeded || registered.Value is null ||
                    !runtimeObject.Succeeded || runtimeObject.Value is null ||
                    registered.Value.MapId != Definition.MapId ||
                    entity.MapId != Definition.MapId)
                {
                    return OperationResult<int>.Failure(
                        "map_runtime.initial_spawn_invalid",
                        "The initial spawn queue does not match the authoritative map registries.",
                        entity.RuntimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            foreach (var entity in pending)
            {
                if (string.Equals(entity.State, "SpawnPending", StringComparison.Ordinal))
                {
                    EntityRegistry.Add(entity with { State = "Active", DirtyFlags = "None" });
                    var runtimeObject = Objects.Get(entity.RuntimeEntityId).Value;
                    if (runtimeObject is MonsterObject monster)
                    {
                        Objects.Add(monster with { State = monster.State with { Lifecycle = "Active" } });
                    }
                }
            }

            var pendingIds = pending.Select(entity => entity.RuntimeEntityId).ToHashSet();
            var removed = SpawnQueue.RemoveWhere(entity => pendingIds.Contains(entity.RuntimeEntityId));
            if (removed != pending.Count)
            {
                return OperationResult<int>.Failure(
                    "map_runtime.initial_spawn_drain_failed",
                    "The initial spawn queue changed while the production map was being published.",
                    Definition.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            _initialSpawnQueueConsumed = true;
            return OperationResult<int>.Success(removed);
        }
    }
}

public sealed class WorldRuntime
{
    private readonly ConcurrentDictionary<int, MapRuntime> _maps = [];

    public WorldRuntime()
    {
        WorldObject = new WorldObject(
            RuntimeObjectIds.World("WorldRuntime"),
            new WorldState("God2 Classic Runtime", DateTimeOffset.UtcNow, []));
        Objects.Add(WorldObject);
    }

    public WorldObject WorldObject { get; }

    public RuntimeObjectRegistry Objects { get; } = new();

    public IReadOnlyDictionary<int, MapRuntime> ActiveMaps =>
        new ReadOnlyDictionary<int, MapRuntime>(_maps);

    public void Add(MapRuntime mapRuntime)
    {
        _maps[mapRuntime.Definition.MapId] = mapRuntime;
        foreach (var runtimeObject in mapRuntime.Objects.ActiveObjects)
        {
            Objects.Add(runtimeObject);
        }
    }

    public OperationResult<MapRuntime> GetMap(int mapId) =>
        _maps.TryGetValue(mapId, out var runtime)
            ? OperationResult<MapRuntime>.Success(runtime)
            : OperationResult<MapRuntime>.Failure(
                "world_runtime.map_not_loaded",
                "MapRuntime is not loaded in WorldRuntime.",
                mapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public sealed record MapSession(
    string SessionId,
    int MapId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    WorldContentMode ContentMode);

public sealed record MapRuntimeCreationResult(MapRuntime MapRuntime, IReadOnlyList<string> ValidationErrors);

public sealed class MapRuntimeFactory
{
    private long _nextRuntimeEntityId = 1000;
    private long _nextBroadcastEventId;

    public OperationResult<MapRuntimeCreationResult> Create(
        WorldContentSnapshot snapshot,
        int mapId,
        string worldInstanceId = IProductionMapRuntimeRegistry.DefaultInstanceId)
    {
        if (!snapshot.Maps.TryGetValue(mapId, out var definition))
        {
            return OperationResult<MapRuntimeCreationResult>.Failure(
                "world_content.map_not_found",
                "MapRuntime cannot be created because the requested map does not exist in the normalized content snapshot.",
                mapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var runtime = new MapRuntime(definition, worldInstanceId);
        foreach (var placement in definition.NpcPlacements.Where(placement => placement.Enabled))
        {
            EnqueueNpc(runtime, placement);
        }

        foreach (var spawn in definition.MonsterSpawns.Where(spawn => spawn.Enabled))
        {
            for (var index = 0; index < Math.Max(1, spawn.Count); index++)
            {
                EnqueueMonster(runtime, spawn);
            }
        }

        var combatDefinitions = definition.MonsterSpawns
            .Where(spawn => spawn.Enabled)
            .Select(spawn => new MonsterCombatContentMapper().Map(spawn))
            .ToArray();
        var validatedCombat = new MonsterCombatContentValidator().Validate(
            combatDefinitions,
            snapshot.Maps.Keys.ToHashSet());
        runtime.InitializeMonsterCombat(MonsterCombatRuntimeRegistry.Build(
            runtime,
            validatedCombat.Valid,
            DateTimeOffset.UtcNow));

        foreach (var portal in definition.Portals.Where(portal => portal.Enabled))
        {
            EnqueuePortal(runtime, portal);
        }

        foreach (var merchant in definition.MerchantMappings.Where(merchant => merchant.Enabled))
        {
            RegisterMerchant(runtime, merchant);
        }

        var validationErrors = definition.ValidationErrors
            .Concat(validatedCombat.Issues.Select(issue =>
                $"{issue.Code}: spawn={issue.SpawnDefinitionId}, monster={issue.MonsterTemplateId}: {issue.Message}"))
            .ToArray();
        return OperationResult<MapRuntimeCreationResult>.Success(new MapRuntimeCreationResult(runtime, validationErrors));
    }

    private void EnqueueNpc(MapRuntime runtime, NpcPlacementRecord placement)
    {
        var runtimeObjectId = Interlocked.Increment(ref _nextRuntimeEntityId);
        var npc = new NpcObject(
            RuntimeObjectIds.Entity(runtimeObjectId, RuntimeObjectKind.Npc, placement.MapId, placement.NpcTemplateId, placement.Source),
            new NpcState(
                placement.PlacementId,
                placement.NpcTemplateId,
                placement.Name,
                placement.MapId,
                placement.Position,
                placement.Direction,
                placement.Appearance,
                placement.InteractionType,
                placement.SpawnCondition,
                placement.MerchantId,
                "Idle",
                VisibilityRadius: 24,
                placement.Enabled,
                placement.RawMetadata,
                placement.ContentVersion,
                placement.WireIdentity));
        runtime.Objects.Add(npc);
        EnqueueEntity(
            runtime,
            npc,
            WorldRuntimeEntityType.NPC,
            placement.NpcTemplateId,
            placement.MapId,
            placement.Position,
            placement.Direction,
            "Idle");
    }

    private void EnqueueMonster(MapRuntime runtime, MonsterSpawnDefinition spawn)
    {
        var runtimeObjectId = Interlocked.Increment(ref _nextRuntimeEntityId);
        var monster = new MonsterObject(
            RuntimeObjectIds.Entity(runtimeObjectId, RuntimeObjectKind.Monster, spawn.MapId, spawn.MonsterTemplateId, spawn.Evidence),
            new MonsterState(
                spawn.SpawnId,
                spawn.MonsterTemplateId,
                spawn.Name,
                spawn.MapId,
                spawn.Position,
                spawn.Direction,
                spawn.RespawnTime,
                spawn.SpawnRadius,
                spawn.Count,
                spawn.Condition,
                "SpawnPending",
                VisibilityRadius: 24,
                spawn.Enabled,
                spawn.RawMetadata,
                spawn.ContentVersion,
                spawn.Level,
                spawn.MaximumHp,
                spawn.MaximumHp,
                spawn.MaximumHp > 0 ? "ContentBacked" : "EvidenceBlocked",
                null,
                null,
                "EvidenceBlocked",
                spawn.AttackPower,
                spawn.Defense,
                spawn.MaximumHp > 0 ? "ContentBacked" : "EvidenceBlocked"));
        runtime.Objects.Add(monster);
        EnqueueEntity(
            runtime,
            monster,
            WorldRuntimeEntityType.Monster,
            spawn.MonsterTemplateId,
            spawn.MapId,
            spawn.Position,
            spawn.Direction,
            "SpawnPending");
    }

    private void EnqueuePortal(MapRuntime runtime, PortalDefinition portal)
    {
        var runtimeObjectId = Interlocked.Increment(ref _nextRuntimeEntityId);
        var templateId = checked((int)(portal.PortalId & 0x7FFFFFFF));
        var portalObject = new PortalObject(
            RuntimeObjectIds.Entity(runtimeObjectId, RuntimeObjectKind.Portal, portal.Source.MapId, templateId, portal.Evidence),
            new PortalState(
                portal.PortalId,
                portal.Name,
                portal.Source,
                portal.Target,
                portal.TriggerType,
                portal.Requirement,
                IsActive: true,
                portal.RawMetadata,
                portal.ContentVersion,
                portal.TriggerRadius));
        runtime.Objects.Add(portalObject);
        EnqueueEntity(
            runtime,
            portalObject,
            WorldRuntimeEntityType.Portal,
            templateId,
            portal.Source.MapId,
            portal.Source.Position,
            WorldDirection.Unknown,
            "Active");
    }

    private static void RegisterMerchant(MapRuntime runtime, MerchantMapping merchant)
    {
        runtime.Objects.Add(new MerchantObject(
            RuntimeObjectIds.Static(RuntimeObjectKind.Merchant, merchant.MerchantId, merchant.Evidence),
            new MerchantState(
                merchant.MerchantId,
                merchant.NpcTemplateId,
                merchant.PlacementId,
                merchant.Name,
                merchant.MerchantGroupId,
                merchant.CurrencyType,
                merchant.RuntimeItems,
                "AttachedToNpcPlacement",
                merchant.Enabled,
                merchant.RawMetadata,
                merchant.ContentVersion)));
    }

    private void EnqueueEntity(
        MapRuntime runtime,
        IRuntimeObject runtimeObject,
        WorldRuntimeEntityType entityType,
        int templateId,
        int mapId,
        WorldPosition3 position,
        WorldDirection direction,
        string state)
    {
        var entity = new RuntimeEntity(
            runtimeObject.Identity.RuntimeObjectId,
            entityType,
            templateId,
            mapId,
            position,
            direction,
            state,
            VisibilityRadius: 24,
            DirtyFlags: "Spawn");

        runtime.EntityRegistry.Add(entity);
        runtime.SpawnQueue.Enqueue(entity);
        var serializerStatus = runtimeObject switch
        {
            PlayerObject => OfficialSerializerStatus.Ready,
            NpcObject npc when OfficialNpcReplicationWireCodec.Validate(npc.State) is null => OfficialSerializerStatus.Ready,
            _ => OfficialSerializerStatus.SerializerBlockedByEvidence
        };
        runtime.PublishSemanticEvent(new SemanticBroadcastEvent(
            Interlocked.Increment(ref _nextBroadcastEventId),
            SemanticBroadcastKind.EntityEnteredVisibility,
            entity.RuntimeEntityId,
            entity.EntityType,
            entity.MapId,
            serializerStatus,
            serializerStatus == OfficialSerializerStatus.Ready
                ? "Runtime entity has a build-pinned official Client serializer"
                : "Official entity serializer remains blocked by required evidence"));
        runtime.Replication.RecordObjectCreated(
            runtimeObject,
            DateTimeOffset.UtcNow);
    }
}

public sealed record WorldSessionBinding(
    RuntimeSession Session,
    CharacterSummary Character,
    MapSession MapSession,
    MapRuntime MapRuntime,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<OfficialOwnedImmortalWireState>? OwnedImmortals = null,
    long InitialGoldBalance = 0,
    PlayerInventorySnapshot? InitialInventory = null);

public sealed class WorldSessionBinder
{
    private readonly MapRuntimeFactory _factory;

    public WorldSessionBinder(MapRuntimeFactory factory)
    {
        _factory = factory;
    }

    public OperationResult<WorldSessionBinding> Bind(
        RuntimeSession session,
        CharacterSummary character,
        WorldContentSnapshot snapshot,
        WorldContentMode contentMode)
    {
        if (session.CharacterId != character.CharacterId)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.character_mismatch",
                "WorldSession binding requires the selected character from the login session.",
                session.SessionId);
        }

        if (contentMode == WorldContentMode.FrozenCompatibility)
        {
            var definition = new MapDefinition(
                character.MapId,
                null,
                "FrozenCompatibility",
                "official-first-contact-golden",
                new MapBounds(0, 0, 0, 0),
                [new SpawnPoint("FrozenPlayerSpawn", new WorldPosition3(character.PositionX, character.PositionY), WorldDirection.Unknown, "Frozen PlayerSpawnS2C128")],
                [],
                [],
                [],
                [],
                []);
            var runtime = new MapRuntime(definition);
            var player = AddPlayer(runtime, session, character, contentMode);
            var frozenSession = new MapSession(session.SessionId, character.MapId, character.CharacterId, player.RuntimeEntityId, contentMode);
            runtime.ActiveSessions.Add(frozenSession);
            runtime.Replication.RecalculateVisibility(
                frozenSession,
                player.Position,
                runtime.Objects.ActiveObjects,
                player.VisibilityRadius,
                DateTimeOffset.UtcNow);
            return OperationResult<WorldSessionBinding>.Success(new WorldSessionBinding(session, character, frozenSession, runtime, []));
        }

        var create = _factory.Create(snapshot, character.MapId);
        if (!create.Succeeded || create.Value is null)
        {
            return OperationResult<WorldSessionBinding>.Failure(create.Error!.Code, create.Error.Message, create.Error.Source);
        }

        return BindToRuntime(session, character, create.Value.MapRuntime, contentMode, create.Value.ValidationErrors);
    }

    public OperationResult<WorldSessionBinding> BindToRuntime(
        RuntimeSession session,
        CharacterSummary character,
        MapRuntime mapRuntime,
        WorldContentMode contentMode,
        IReadOnlyList<string>? validationErrors = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(mapRuntime);
        if (contentMode != WorldContentMode.MariaDbAuthoritative)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.authority_mode_invalid",
                "A shared production MapRuntime requires MariaDbAuthoritative content mode.",
                contentMode.ToString());
        }

        if (session.CharacterId != character.CharacterId)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.character_mismatch",
                "WorldSession binding requires the selected character from the login session.",
                session.SessionId);
        }

        if (mapRuntime.Definition.MapId != character.MapId)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.map_mismatch",
                "The authoritative character map does not match the loaded MapRuntime.",
                character.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var bounds = mapRuntime.Definition.Bounds;
        if (bounds.MaxX <= bounds.MinX || bounds.MaxY <= bounds.MinY)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.map_bounds_unverified",
                "The selected map does not have verified non-empty coordinate bounds.",
                character.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (character.PositionX < bounds.MinX || character.PositionX > bounds.MaxX ||
            character.PositionY < bounds.MinY || character.PositionY > bounds.MaxY)
        {
            return OperationResult<WorldSessionBinding>.Failure(
                "world_content.character_position_out_of_bounds",
                "The authoritative character position is outside the selected map bounds.",
                character.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        RuntimeEntity playerEntity;
        MapSession mapSession;
        lock (mapRuntime.ActiveSessions)
        {
            if (mapRuntime.ActiveSessions.Any(existing =>
                string.Equals(existing.SessionId, session.SessionId, StringComparison.Ordinal) ||
                existing.CharacterId == character.CharacterId))
            {
                return OperationResult<WorldSessionBinding>.Failure(
                    "world_content.player_already_bound",
                    "The session or character is already attached to the authoritative MapRuntime.",
                    session.SessionId);
            }

            playerEntity = AddPlayer(mapRuntime, session, character, contentMode);
            mapSession = new MapSession(session.SessionId, character.MapId, character.CharacterId, playerEntity.RuntimeEntityId, contentMode);
            mapRuntime.ActiveSessions.Add(mapSession);
        }
        if (contentMode == WorldContentMode.MariaDbAuthoritative)
        {
            mapRuntime.Replication.RecalculateInitialVisibility(
                mapSession,
                playerEntity.Position,
                mapRuntime.Objects.ActiveObjects,
                playerEntity.VisibilityRadius,
                DateTimeOffset.UtcNow);
        }
        else
        {
            mapRuntime.Replication.RecalculateVisibility(
                mapSession,
                playerEntity.Position,
                mapRuntime.Objects.ActiveObjects,
                playerEntity.VisibilityRadius,
                DateTimeOffset.UtcNow);
        }
        return OperationResult<WorldSessionBinding>.Success(new WorldSessionBinding(
            session,
            character,
            mapSession,
            mapRuntime,
            validationErrors ?? []));
    }

    private static RuntimeEntity AddPlayer(MapRuntime mapRuntime, RuntimeSession session, CharacterSummary character, WorldContentMode contentMode)
    {
        var runtimeEntityId = unchecked((long)0x7000_0000 + character.CharacterId);
        var player = new RuntimeEntity(
            runtimeEntityId,
            WorldRuntimeEntityType.Player,
            checked((int)(character.CharacterId & 0x7FFFFFFF)),
            character.MapId,
            new WorldPosition3(character.PositionX, character.PositionY),
            WorldDirection.Unknown,
            contentMode == WorldContentMode.FrozenCompatibility ? "FrozenBootstrap" : "Attached",
            VisibilityRadius: 24,
            DirtyFlags: "PlayerAttached");
        mapRuntime.EntityRegistry.Add(player);
        var playerObject = new PlayerObject(
            RuntimeObjectIds.Entity(
                runtimeEntityId,
                RuntimeObjectKind.Player,
                character.MapId,
                checked((int)(character.CharacterId & 0x7FFFFFFF)),
                $"WorldSession:{session.SessionId}"),
            new PlayerState(
                character.CharacterId,
                session.AccountId,
                character.Name,
                character.Level,
                character.Class,
                character.Gender,
                character.Appearance,
                character.MapId,
                player.Position,
                player.Direction,
                contentMode == WorldContentMode.FrozenCompatibility ? "FrozenBootstrap" : "Attached",
                player.VisibilityRadius,
                InventoryObjectId: null));
        mapRuntime.Objects.Add(playerObject);
        mapRuntime.PublishSemanticEvent(new SemanticBroadcastEvent(
            unchecked(0x5000_0000 + character.CharacterId),
            SemanticBroadcastKind.EntityEnteredVisibility,
            player.RuntimeEntityId,
            player.EntityType,
            player.MapId,
            OfficialSerializerStatus.Ready,
            $"Player attached from session {session.SessionId}."));
        if (contentMode != WorldContentMode.FrozenCompatibility)
        {
            mapRuntime.Replication.RecordObjectCreated(
                playerObject,
                DateTimeOffset.UtcNow);
        }

        return player;
    }
}

public sealed record RuntimeObjectSnapshot(
    string RuntimeType,
    long RuntimeObjectId,
    long RuntimeEntityId,
    RuntimeObjectKind Kind,
    int MapId,
    int TemplateId,
    string StateType,
    WorldPosition3? Position,
    string Lifecycle,
    string LifecycleState,
    string ReplicationState,
    string DirtyFlags,
    int VisibleToPlayerCount,
    int PendingSpawnCount,
    int PendingUpdateCount,
    int PendingDespawnCount,
    OfficialSerializerStatus SerializerStatus,
    string SerializerBlockReason,
    string EvidenceArtifact,
    string ContentVersion);

public sealed record ReplicationQueueSnapshot(
    long EventId,
    ReplicationQueueKind Queue,
    long RuntimeObjectId,
    RuntimeObjectKind ObjectKind,
    int MapId,
    OfficialSerializerStatus SerializerStatus,
    string Reason,
    string? TargetSessionId);

public sealed record WorldRuntimeSnapshot(
    string SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    int MapId,
    int? SceneId,
    string MapName,
    string? RegionId,
    int PlayerCount,
    int NpcCount,
    int MonsterCount,
    int PortalCount,
    int MerchantCount,
    int SpawnQueueCount,
    int UpdateQueueCount,
    int DespawnQueueCount,
    int BroadcastEventCount,
    int SerializerBlockedCount,
    IReadOnlyList<string> DataValidationErrors,
    int RuntimeObjectCount,
    int DirtyObjectCount,
    int VisibilityRecordCount,
    int ReplicationSpawnQueueCount,
    int ReplicationUpdateQueueCount,
    int ReplicationDespawnQueueCount,
    IReadOnlyList<RuntimeObjectSnapshot> Players,
    IReadOnlyList<RuntimeObjectSnapshot> Npcs,
    IReadOnlyList<RuntimeObjectSnapshot> Monsters,
    IReadOnlyList<RuntimeObjectSnapshot> Portals,
    IReadOnlyList<RuntimeObjectSnapshot> Merchants,
    IReadOnlyList<ReplicationQueueSnapshot> SpawnQueue,
    IReadOnlyList<ReplicationQueueSnapshot> UpdateQueue,
    IReadOnlyList<ReplicationQueueSnapshot> DespawnQueue,
    IReadOnlyList<RuntimeSerializerStatusSnapshot> SerializerStatus);

public sealed record WorldRuntimeInspectorQuery(
    RuntimeObjectKind? EntityType = null,
    int? MapId = null,
    string? LifecycleState = null,
    OfficialSerializerStatus? SerializerStatus = null,
    bool EvidenceBlockedOnly = false);

public sealed class WorldRuntimeInspector
{
    public WorldRuntimeSnapshot Capture(MapRuntime runtime, DateTimeOffset now) =>
        Capture(runtime, now, new WorldRuntimeInspectorQuery());

    public WorldRuntimeSnapshot Capture(MapRuntime runtime, DateTimeOffset now, WorldRuntimeInspectorQuery query) =>
        new(
            "world-runtime-snapshot-v1",
            now,
            runtime.Definition.MapId,
            runtime.Definition.SceneId,
            runtime.Definition.Name,
            runtime.Definition.RegionId,
            runtime.EntityRegistry.Count(WorldRuntimeEntityType.Player),
            runtime.EntityRegistry.Count(WorldRuntimeEntityType.NPC),
            runtime.EntityRegistry.Count(WorldRuntimeEntityType.Monster),
            runtime.EntityRegistry.Count(WorldRuntimeEntityType.Portal),
            runtime.Definition.MerchantMappings.Count,
            runtime.SpawnQueue.Count,
            runtime.UpdateQueue.Count,
            runtime.DespawnQueue.Count,
            runtime.BroadcastEvents.Count,
            runtime.BroadcastEvents.Snapshot.Count(ev => ev.SerializerStatus == OfficialSerializerStatus.SerializerBlockedByEvidence),
            runtime.Definition.ValidationErrors,
            runtime.Objects.ActiveObjects.Count,
            runtime.Replication.DirtyTracking.Count,
            runtime.Replication.Visibility.Count,
            runtime.Replication.SpawnQueue.Count,
            runtime.Replication.UpdateQueue.Count,
            runtime.Replication.DespawnQueue.Count,
            ObjectSnapshots(runtime, RuntimeObjectKind.Player, query),
            ObjectSnapshots(runtime, RuntimeObjectKind.Npc, query),
            ObjectSnapshots(runtime, RuntimeObjectKind.Monster, query),
            ObjectSnapshots(runtime, RuntimeObjectKind.Portal, query),
            ObjectSnapshots(runtime, RuntimeObjectKind.Merchant, query),
            QueueSnapshots(runtime.Replication.SpawnQueue),
            QueueSnapshots(runtime.Replication.UpdateQueue),
            QueueSnapshots(runtime.Replication.DespawnQueue),
            RuntimeSerializerCatalog.CurrentStatus);

    public async Task<string> WriteAsync(MapRuntime runtime, string artifactRoot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var runId = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var directory = Path.Combine(artifactRoot, runId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world-runtime-snapshot.json");
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(Capture(runtime, now), new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        File.Move(tempPath, path, overwrite: true);
        return path;
    }

    private static IReadOnlyList<RuntimeObjectSnapshot> ObjectSnapshots(MapRuntime runtime, RuntimeObjectKind kind, WorldRuntimeInspectorQuery query) =>
        runtime.Objects.ActiveObjects
            .Where(runtimeObject => runtimeObject.Identity.Kind == kind)
            .Select(runtimeObject => ObjectSnapshot(runtime, runtimeObject))
            .Where(snapshot => Matches(snapshot, query))
            .OrderBy(snapshot => snapshot.RuntimeObjectId)
            .ToArray();

    private static RuntimeObjectSnapshot ObjectSnapshot(MapRuntime runtime, IRuntimeObject runtimeObject)
    {
        var lifecycle = Lifecycle(runtimeObject.State);
        var serializer = SerializerStatusFor(runtimeObject);
        var visibleCount = runtime.Replication.Visibility.VisibleCount(runtimeObject.Identity.RuntimeObjectId);
        var dirtyFlags = string.Join(
            ',',
            runtime.Replication.DirtyTracking.Snapshot
                .Where(entry => entry.RuntimeObjectId == runtimeObject.Identity.RuntimeObjectId)
                .Select(entry => entry.Reason)
                .Distinct(StringComparer.Ordinal));
        var pendingSpawn = runtime.Replication.PendingCount(ReplicationQueueKind.Spawn, runtimeObject.Identity.RuntimeObjectId);
        var pendingUpdate = runtime.Replication.PendingCount(ReplicationQueueKind.Update, runtimeObject.Identity.RuntimeObjectId);
        var pendingDespawn = runtime.Replication.PendingCount(ReplicationQueueKind.Despawn, runtimeObject.Identity.RuntimeObjectId);
        return new RuntimeObjectSnapshot(
            runtimeObject.Identity.Kind.ToString(),
            runtimeObject.Identity.RuntimeObjectId,
            runtimeObject.Identity.RuntimeObjectId,
            runtimeObject.Identity.Kind,
            runtimeObject.Identity.MapId,
            runtimeObject.Identity.TemplateId,
            runtimeObject.State.GetType().Name,
            runtimeObject.State is IMapPositionedRuntimeState positioned ? positioned.Position : null,
            lifecycle,
            lifecycle,
            ReplicationState(visibleCount, pendingSpawn, pendingUpdate, pendingDespawn),
            string.IsNullOrWhiteSpace(dirtyFlags) ? "None" : dirtyFlags,
            visibleCount,
            pendingSpawn,
            pendingUpdate,
            pendingDespawn,
            serializer.Status,
            serializer.Status == OfficialSerializerStatus.SerializerBlockedByEvidence ? serializer.Reason : string.Empty,
            serializer.GoldenArtifact,
            ContentVersion(runtimeObject.State));
    }

    private static string Lifecycle(IRuntimeState state) =>
        state switch
        {
            EntityState entity => entity.Lifecycle,
            PlayerState player => player.Lifecycle,
            NpcState npc => npc.Lifecycle,
            MonsterState monster => monster.Lifecycle,
            PortalState portal => portal.IsActive ? "Active" : "Inactive",
            MerchantState merchant => merchant.Lifecycle,
            ItemState item => item.Lifecycle,
            ProjectileState projectile => projectile.Lifecycle,
            _ => state.GetType().Name
        };

    private static string ContentVersion(IRuntimeState state) =>
        state switch
        {
            NpcState npc => npc.ContentVersion,
            MonsterState monster => monster.ContentVersion,
            PortalState portal => portal.ContentVersion,
            MerchantState merchant => merchant.ContentVersion,
            _ => "runtime-content-v1"
        };

    private static string ReplicationState(int visibleCount, int pendingSpawn, int pendingUpdate, int pendingDespawn)
    {
        if (pendingDespawn > 0)
        {
            return "PendingDespawn";
        }

        if (pendingSpawn > 0)
        {
            return "PendingSpawn";
        }

        if (pendingUpdate > 0)
        {
            return "PendingUpdate";
        }

        return visibleCount > 0 ? "Visible" : "Registered";
    }

    private static RuntimeSerializerStatusSnapshot SerializerStatusFor(IRuntimeObject runtimeObject)
    {
        var serializer = runtimeObject.Identity.Kind switch
        {
            RuntimeObjectKind.Player => RuntimeSerializerKind.Player,
            RuntimeObjectKind.Npc => RuntimeSerializerKind.Npc,
            RuntimeObjectKind.Monster => RuntimeSerializerKind.Monster,
            RuntimeObjectKind.Portal => RuntimeSerializerKind.Portal,
            RuntimeObjectKind.Merchant => RuntimeSerializerKind.Merchant,
            _ => RuntimeSerializerKind.Npc
        };
        var catalog = RuntimeSerializerCatalog.CurrentStatus.Single(status => status.Serializer == serializer.ToString());
        if (runtimeObject is not NpcObject npc)
        {
            return catalog;
        }

        var result = new NpcSerializer().Serialize(npc.State);
        return catalog with
        {
            Status = result.Status,
            Reason = result.Reason,
            CurrentStatus = result.Status == OfficialSerializerStatus.Ready
                ? SerializerEvidenceStatus.PartiallyVerified
                : SerializerEvidenceStatus.BlockedByEvidence
        };
    }

    private static bool Matches(RuntimeObjectSnapshot snapshot, WorldRuntimeInspectorQuery query)
    {
        if (query.EntityType is not null && snapshot.Kind != query.EntityType)
        {
            return false;
        }

        if (query.MapId is not null && snapshot.MapId != query.MapId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.LifecycleState) &&
            !string.Equals(snapshot.LifecycleState, query.LifecycleState, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.SerializerStatus is not null && snapshot.SerializerStatus != query.SerializerStatus)
        {
            return false;
        }

        return !query.EvidenceBlockedOnly || snapshot.SerializerStatus == OfficialSerializerStatus.SerializerBlockedByEvidence;
    }

    private static IReadOnlyList<ReplicationQueueSnapshot> QueueSnapshots(WorldRuntimeQueue<RuntimeReplicationEvent> queue) =>
        queue.Snapshot
            .OrderBy(entry => entry.EventId)
            .Select(entry => new ReplicationQueueSnapshot(
                entry.EventId,
                entry.Queue,
                entry.RuntimeObjectId,
                entry.ObjectKind,
                entry.MapId,
                entry.SerializerStatus,
                entry.Reason,
                entry.TargetSessionId))
            .ToArray();
}
