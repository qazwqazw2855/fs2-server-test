using System.Collections.Concurrent;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum RuntimeObjectKind
{
    World,
    Map,
    Entity,
    Player,
    Npc,
    Monster,
    Portal,
    Merchant,
    Item,
    Inventory,
    Quest,
    Skill,
    Projectile,
    Effect
}

public sealed record RuntimeObjectIdentity(
    long RuntimeObjectId,
    RuntimeObjectKind Kind,
    int MapId,
    int TemplateId,
    string Source);

public interface IRuntimeState;

public interface IMapPositionedRuntimeState : IRuntimeState
{
    int MapId { get; }

    WorldPosition3 Position { get; }

    WorldDirection Direction { get; }
}

public interface IRuntimeObject
{
    RuntimeObjectIdentity Identity { get; }

    IRuntimeState State { get; }
}

public interface IRuntimeObject<out TState> : IRuntimeObject
    where TState : IRuntimeState
{
    new TState State { get; }
}

public abstract record RuntimeObject<TState>(
    RuntimeObjectIdentity Identity,
    TState State) : IRuntimeObject<TState>
    where TState : IRuntimeState
{
    IRuntimeState IRuntimeObject.State => State;
}

public sealed record WorldState(
    string Name,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<int> LoadedMapIds) : IRuntimeState;

public sealed record MapState(
    int MapId,
    int? SceneId,
    string Name,
    string? RegionId,
    MapBounds Bounds,
    IReadOnlyList<SpawnPoint> SpawnPoints,
    string ContentMode) : IRuntimeState;

public sealed record EntityState(
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string Lifecycle,
    int VisibilityRadius) : IMapPositionedRuntimeState;

public sealed record PlayerState(
    long CharacterId,
    long? AccountId,
    string Name,
    int Level,
    string Class,
    string Gender,
    string Appearance,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string Lifecycle,
    int VisibilityRadius,
    long? InventoryObjectId) : IMapPositionedRuntimeState;

public sealed record OfficialNpcWireIdentity(
    string ClientBuildId,
    uint EntityHandle,
    byte ResourceType,
    byte ResourceOrdinal,
    byte SelectorHighBits,
    byte DirectionCode,
    byte StateCode,
    string SpawnMessageSha256,
    string EvidenceStatus,
    string EvidenceReference,
    string OpaqueTemplateSha256);

public sealed record NpcState(
    int PlacementId,
    int NpcTemplateId,
    string Name,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string Appearance,
    string InteractionType,
    string SpawnCondition,
    int? MerchantId,
    string Lifecycle,
    int VisibilityRadius,
    bool Enabled,
    string RawMetadata,
    string ContentVersion,
    OfficialNpcWireIdentity? WireIdentity = null) : IMapPositionedRuntimeState;

public sealed record MonsterState(
    int SpawnId,
    int MonsterTemplateId,
    string Name,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    TimeSpan RespawnTime,
    int SpawnRadius,
    int Count,
    string SpawnCondition,
    string Lifecycle,
    int VisibilityRadius,
    bool Enabled,
    string RawMetadata,
    string ContentVersion,
    int Level = 0,
    long CurrentHp = 0,
    long MaximumHp = 0,
    string HpPolicy = "EvidenceBlocked",
    long? CurrentMp = null,
    long? MaximumMp = null,
    string MpPolicy = "EvidenceBlocked",
    long AttackPower = 0,
    long Defense = 0,
    string StatPolicy = "EvidenceBlocked",
    string AiFamily = "EvidenceBlocked",
    string EncounterGroup = "EvidenceBlocked",
    string FormationGroup = "EvidenceBlocked",
    string RewardPolicy = "EvidenceBlocked",
    string DropPolicy = "EvidenceBlocked") : IMapPositionedRuntimeState;

public sealed record PortalState(
    long PortalId,
    string Name,
    PortalEndpoint Source,
    PortalEndpoint Target,
    string TriggerType,
    string Requirement,
    bool IsActive,
    string RawMetadata,
    string ContentVersion,
    int TriggerRadius = 0) : IMapPositionedRuntimeState
{
    public int MapId => Source.MapId;

    public WorldPosition3 Position => Source.Position;

    public WorldDirection Direction => WorldDirection.Unknown;
}

public sealed record MerchantState(
    int MerchantId,
    int NpcTemplateId,
    int PlacementId,
    string Name,
    string MerchantGroupId,
    string CurrencyType,
    IReadOnlyList<MerchantItemDefinition> Items,
    string Lifecycle,
    bool Enabled,
    string RawMetadata,
    string ContentVersion) : IRuntimeState;

public sealed record ItemState(
    long ItemInstanceId,
    int ItemTemplateId,
    int Quantity,
    string Lifecycle) : IRuntimeState;

public sealed record InventoryState(
    long OwnerRuntimeObjectId,
    int Capacity,
    IReadOnlyList<long> ItemObjectIds) : IRuntimeState;

public sealed record QuestState(
    int QuestId,
    long OwnerRuntimeObjectId,
    string ProgressState,
    IReadOnlyList<string> Objectives) : IRuntimeState;

public sealed record SkillState(
    int SkillId,
    long OwnerRuntimeObjectId,
    int Level,
    TimeSpan CooldownRemaining) : IRuntimeState;

public sealed record ProjectileState(
    long OwnerRuntimeObjectId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string Lifecycle,
    int VisibilityRadius) : IMapPositionedRuntimeState;

public sealed record EffectState(
    long OwnerRuntimeObjectId,
    int MapId,
    WorldPosition3 Position,
    WorldDirection Direction,
    string EffectKind,
    TimeSpan Remaining,
    int VisibilityRadius) : IMapPositionedRuntimeState;

public sealed record WorldObject(
    RuntimeObjectIdentity Identity,
    WorldState State) : RuntimeObject<WorldState>(Identity, State);

public sealed record MapObject(
    RuntimeObjectIdentity Identity,
    MapState State) : RuntimeObject<MapState>(Identity, State);

public sealed record EntityObject(
    RuntimeObjectIdentity Identity,
    EntityState State) : RuntimeObject<EntityState>(Identity, State);

public sealed record PlayerObject(
    RuntimeObjectIdentity Identity,
    PlayerState State) : RuntimeObject<PlayerState>(Identity, State);

public sealed record NpcObject(
    RuntimeObjectIdentity Identity,
    NpcState State) : RuntimeObject<NpcState>(Identity, State);

public sealed record MonsterObject(
    RuntimeObjectIdentity Identity,
    MonsterState State) : RuntimeObject<MonsterState>(Identity, State);

public sealed record PortalObject(
    RuntimeObjectIdentity Identity,
    PortalState State) : RuntimeObject<PortalState>(Identity, State);

public sealed record MerchantObject(
    RuntimeObjectIdentity Identity,
    MerchantState State) : RuntimeObject<MerchantState>(Identity, State);

public sealed record ItemObject(
    RuntimeObjectIdentity Identity,
    ItemState State) : RuntimeObject<ItemState>(Identity, State);

public sealed record InventoryObject(
    RuntimeObjectIdentity Identity,
    InventoryState State) : RuntimeObject<InventoryState>(Identity, State);

public sealed record QuestObject(
    RuntimeObjectIdentity Identity,
    QuestState State) : RuntimeObject<QuestState>(Identity, State);

public sealed record SkillObject(
    RuntimeObjectIdentity Identity,
    SkillState State) : RuntimeObject<SkillState>(Identity, State);

public sealed record ProjectileObject(
    RuntimeObjectIdentity Identity,
    ProjectileState State) : RuntimeObject<ProjectileState>(Identity, State);

public sealed record EffectObject(
    RuntimeObjectIdentity Identity,
    EffectState State) : RuntimeObject<EffectState>(Identity, State);

public sealed class RuntimeObjectRegistry
{
    private readonly ConcurrentDictionary<long, IRuntimeObject> _objects = [];

    public IReadOnlyCollection<IRuntimeObject> ActiveObjects => _objects.Values.ToArray();

    public int Count(RuntimeObjectKind kind) =>
        _objects.Values.Count(runtimeObject => runtimeObject.Identity.Kind == kind);

    public IReadOnlyList<TObject> OfType<TObject>()
        where TObject : class, IRuntimeObject =>
        _objects.Values.OfType<TObject>().ToArray();

    public OperationResult<IRuntimeObject> Get(long runtimeObjectId) =>
        _objects.TryGetValue(runtimeObjectId, out var runtimeObject)
            ? OperationResult<IRuntimeObject>.Success(runtimeObject)
            : OperationResult<IRuntimeObject>.Failure(
                "runtime_object.not_found",
                "Runtime object is not active in the registry.",
                runtimeObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Add(IRuntimeObject runtimeObject)
    {
        _objects[runtimeObject.Identity.RuntimeObjectId] = runtimeObject;
    }

    public bool Remove(long runtimeObjectId) =>
        _objects.TryRemove(runtimeObjectId, out _);
}

public static class RuntimeObjectIds
{
    public static RuntimeObjectIdentity World(string source) =>
        new(1, RuntimeObjectKind.World, 0, 0, source);

    public static RuntimeObjectIdentity Map(int mapId, string source) =>
        new(Compose(RuntimeObjectKind.Map, mapId), RuntimeObjectKind.Map, mapId, mapId, source);

    public static RuntimeObjectIdentity Entity(long runtimeObjectId, RuntimeObjectKind kind, int mapId, int templateId, string source) =>
        new(runtimeObjectId, kind, mapId, templateId, source);

    public static RuntimeObjectIdentity Static(RuntimeObjectKind kind, int templateId, string source) =>
        new(Compose(kind, templateId), kind, 0, templateId, source);

    private static long Compose(RuntimeObjectKind kind, int discriminator) =>
        ((long)kind << 56) | (uint)discriminator;
}
