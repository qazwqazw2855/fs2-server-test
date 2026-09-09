using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Protocol;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class RoadMapM9AProductionWorldTests
{
    [Fact]
    public async Task Production_registry_publishes_once_and_bounds_instance_key_space()
    {
        var registry = new ProductionMapRuntimeRegistry(
            new MariaDbTestWorldRepository(Snapshot()),
            ReadyContentAuthority.Instance);

        var initialized = await registry.InitializeAsync(CancellationToken.None);
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => registry.GetOrCreate(19))));

        Assert.True(initialized.Succeeded);
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Single(results.Select(result => result.Value).Distinct(ReferenceEqualityComparer.Instance));
        Assert.Equal(1, registry.Status.ActiveMapRuntimeCount);
        Assert.Equal(1, registry.Status.EnabledNpcSpawnCount);
        Assert.Equal(1, registry.Status.EnabledMonsterSpawnCount);
        Assert.Equal(0, registry.Status.PendingInitialSpawnCount);
        Assert.NotEmpty(registry.Status.SnapshotIdentity);
        var arbitraryInstance = registry.GetOrCreate(19, "player-controlled-instance");
        Assert.False(arbitraryInstance.Succeeded);
        Assert.Equal("world_registry.instance_not_promoted", arbitraryInstance.Error.Code);
        Assert.Equal(1, registry.Status.ActiveMapRuntimeCount);
    }

    [Fact]
    public async Task Map_factory_creates_only_enabled_entities_and_initializes_monster_combat_authority()
    {
        var registry = new ProductionMapRuntimeRegistry(
            new MariaDbTestWorldRepository(Snapshot()),
            ReadyContentAuthority.Instance);
        Assert.True((await registry.InitializeAsync(CancellationToken.None)).Succeeded);

        var runtime = registry.GetOrCreate(19).Value!;

        Assert.Single(runtime.Objects.OfType<NpcObject>());
        Assert.Single(runtime.Objects.OfType<MonsterObject>());
        Assert.Empty(runtime.Objects.OfType<PortalObject>());
        Assert.Single(runtime.MonsterCombat.Snapshot);
        Assert.True(runtime.InitialSpawnQueueConsumed);
        Assert.Equal(0, runtime.SpawnQueue.Count);
        var monster = Assert.Single(runtime.Objects.OfType<MonsterObject>());
        Assert.Equal(100, monster.State.CurrentHp);
        Assert.Equal(100, monster.State.MaximumHp);
        Assert.Equal("ContentBacked", monster.State.HpPolicy);
        Assert.Equal("EvidenceBlocked", monster.State.MpPolicy);
        Assert.Equal("Active", monster.State.Lifecycle);
        Assert.Equal("Active", runtime.EntityRegistry.Get(monster.Identity.RuntimeObjectId).Value!.State);
        Assert.Contains(runtime.BroadcastEvents.Snapshot, entry =>
            entry.RuntimeEntityId == monster.Identity.RuntimeObjectId &&
            entry.Kind == SemanticBroadcastKind.EntityEnteredVisibility);
    }

    [Fact]
    public async Task Production_registry_fails_closed_when_active_release_is_not_ready()
    {
        var registry = new ProductionMapRuntimeRegistry(
            new MariaDbTestWorldRepository(Snapshot()),
            BlockedContentAuthority.Instance);

        var result = await registry.InitializeAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("content_release.runtime_not_ready", result.Error.Code);
        Assert.False(registry.Status.IsInitialized);
    }

    [Fact]
    public async Task Production_registry_rejects_duplicate_spawn_identity_before_map_creation()
    {
        var snapshot = Snapshot();
        var map = snapshot.Maps[19];
        var duplicate = map.NpcPlacements[0] with { NpcTemplateId = 999 };
        var invalid = snapshot with
        {
            Maps = new Dictionary<int, MapDefinition>
            {
                [19] = map with { NpcPlacements = [.. map.NpcPlacements, duplicate] }
            }
        };
        var registry = new ProductionMapRuntimeRegistry(
            new MariaDbTestWorldRepository(invalid),
            ReadyContentAuthority.Instance);

        var result = await registry.InitializeAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("world_registry.snapshot_identity_invalid", result.Error.Code);
        Assert.False(registry.Status.IsInitialized);
    }

    [Fact]
    public async Task Production_registry_rejects_duplicate_spawn_identity_across_maps()
    {
        var snapshot = Snapshot();
        var first = snapshot.Maps[19];
        var second = first with
        {
            MapId = 3,
            NpcPlacements = [first.NpcPlacements[0] with { MapId = 3 }],
            MonsterSpawns = [],
            Portals = []
        };
        var invalid = snapshot with
        {
            Maps = new Dictionary<int, MapDefinition> { [19] = first, [3] = second }
        };
        var registry = new ProductionMapRuntimeRegistry(
            new MariaDbTestWorldRepository(invalid),
            ReadyContentAuthority.Instance);

        var result = await registry.InitializeAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("world_registry.snapshot_identity_invalid", result.Error.Code);
    }

    [Fact]
    public async Task World_coordinator_allows_exactly_one_session_to_bind_the_same_character()
    {
        var repository = new MariaDbTestWorldRepository(Snapshot());
        var registry = new ProductionMapRuntimeRegistry(repository, ReadyContentAuthority.Instance);
        var coordinator = new WorldSessionCoordinator(repository, mapRegistry: registry);
        var character = Character();
        var first = RuntimeSession.Connected("owner-a", "127.0.0.1:1", DateTimeOffset.UnixEpoch) with
        {
            AccountId = character.AccountId,
            CharacterId = character.CharacterId,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var second = first with { SessionId = "owner-b", ConnectionId = "owner-b" };

        var attempts = await Task.WhenAll(
            coordinator.BindAsync(first, character, CancellationToken.None),
            coordinator.BindAsync(second, character, CancellationToken.None));

        var accepted = Assert.Single(attempts, result => result.Succeeded);
        var rejected = Assert.Single(attempts, result => !result.Succeeded);
        Assert.Equal("world_authority.character_already_bound", rejected.Error.Code);
        Assert.Equal(1, coordinator.ActiveBindingCount);
        var owner = accepted.Value!.Session.SessionId;
        Assert.True(coordinator.Unbind(owner));
        Assert.Equal(0, coordinator.ActiveBindingCount);
    }

    [Fact]
    public async Task Production_monster_target_resolves_from_bound_MapRuntime_and_combat_registry()
    {
        var repository = new MariaDbTestWorldRepository(Snapshot());
        var registry = new ProductionMapRuntimeRegistry(repository, ReadyContentAuthority.Instance);
        Assert.True((await registry.InitializeAsync(CancellationToken.None)).Succeeded);
        var coordinator = new WorldSessionCoordinator(repository, mapRegistry: registry);
        var character = Character();
        var entering = RuntimeSession.Connected("m9a-connection", "127.0.0.1:1", DateTimeOffset.UnixEpoch) with
        {
            AccountId = character.AccountId,
            CharacterId = character.CharacterId,
            IsAuthenticated = true,
            ProtocolStage = ProtocolStage.WorldEntering
        };
        var bound = await coordinator.BindAsync(entering, character, CancellationToken.None);
        Assert.True(bound.Succeeded);
        var inWorld = entering with { ProtocolStage = ProtocolStage.InWorld };
        Assert.True(coordinator.UpdateSession(inWorld).Succeeded);
        var monster = Assert.Single(bound.Value!.MapRuntime.Objects.OfType<MonsterObject>());

        var target = new AuthoritativeWorldMonsterTargetResolver(coordinator)
            .Resolve(inWorld.SessionId, monster.Identity.RuntimeObjectId);

        Assert.True(target.Succeeded);
        Assert.Equal(monster.Identity.RuntimeObjectId, target.Value!.CombatState.RuntimeEntityId);
        Assert.Equal(CombatPolicyStatus.ContentBacked, target.Value.CombatDefinition.StatPolicyStatus);
        Assert.Equal(bound.Value.MapRuntime.WorldInstanceId, target.Value.CombatState.WorldInstanceId);
    }

    [Fact]
    public async Task Evidence_blocked_replication_emits_no_bytes_and_is_drained_without_suppressing_ready_family()
    {
        var runtime = new MapRuntimeFactory().Create(Snapshot(), 19).Value!.MapRuntime;
        var playerSession = new MapSession("m9a-session", 19, 7, 700, WorldContentMode.MariaDbAuthoritative);
        runtime.Replication.RecalculateInitialVisibility(
            playerSession,
            new WorldPosition3(10, 10),
            runtime.Objects.ActiveObjects,
            24,
            DateTimeOffset.UnixEpoch);
        var sender = new RecordingRuntimeFrameSender();

        var result = await new RuntimeReplicationEmitter().FlushSpawnQueueAsync(
            runtime,
            playerSession.SessionId,
            sender,
            CancellationToken.None);

        Assert.Equal(0, result.SentFrames);
        Assert.Equal(2, result.BlockedFrames);
        Assert.Empty(sender.Frames);
        Assert.Equal(0, runtime.Replication.SpawnQueue.Count);
        Assert.Contains(result.Blocks, block => block.ObjectKind == RuntimeObjectKind.Npc);
        Assert.Contains(result.Blocks, block => block.ObjectKind == RuntimeObjectKind.Monster);
    }

    private static CharacterSummary Character() => new(
        7,
        11,
        "M9AHero",
        "Swordsman",
        "Male",
        "None",
        1,
        "Default",
        19,
        10,
        10,
        "Active",
        DateTimeOffset.UnixEpoch,
        null);

    private static WorldContentSnapshot Snapshot()
    {
        var enabledNpc = new NpcPlacementRecord(
            101,
            201,
            19,
            new WorldPosition3(11, 10),
            WorldDirection.South,
            "Always",
            null,
            "MariaDB:npc_spawns.Id=101",
            1,
            "Verified",
            "NPC",
            "model:npc",
            "Dialog",
            true);
        var disabledNpc = enabledNpc with { PlacementId = 102, NpcTemplateId = 202, Enabled = false };
        var monster = new MonsterSpawnDefinition(
            301,
            401,
            19,
            new WorldPosition3(12, 10),
            WorldDirection.West,
            TimeSpan.FromSeconds(30),
            1,
            1,
            "Always",
            "MariaDB:spawns.Id=301",
            "Monster",
            true,
            "{}",
            "m9a-test",
            2,
            100,
            10,
            5);
        var disabledPortal = new PortalDefinition(
            501,
            new PortalEndpoint(19, new WorldPosition3(13, 10)),
            new PortalEndpoint(3, new WorldPosition3(1, 1)),
            "Touch",
            "None",
            "MariaDB:portals.Id=501",
            Enabled: false);
        var map = new MapDefinition(
            19,
            19,
            "Map 19",
            "map:19",
            new MapBounds(0, 0, 100, 100),
            [],
            [enabledNpc, disabledNpc],
            [monster],
            [disabledPortal],
            [],
            []);
        return new WorldContentSnapshot(new Dictionary<int, MapDefinition> { [19] = map }, []);
    }

    private sealed class MariaDbTestWorldRepository : IWorldContentRepository
    {
        private readonly WorldContentSnapshot _snapshot;

        public MariaDbTestWorldRepository(WorldContentSnapshot snapshot) => _snapshot = snapshot;

        public WorldContentAuthorityKind AuthorityKind => WorldContentAuthorityKind.MariaDb;

        public Task<WorldContentSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot);
    }

    private sealed class ReadyContentAuthority : IProductionGameplayContentAuthority
    {
        public static ReadyContentAuthority Instance { get; } = new();
        public string ActiveReleaseId => "m9a-test-release";
        public bool IsReady => true;
        public OperationResult RequireReady() => OperationResult.Success;
        public OperationResult<ProductionQuestContent> ResolveQuest(int questId) => Missing<ProductionQuestContent>();
        public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId) => Missing<ProductionEquipmentSetContent>();
        public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId) => Missing<ProductionPetInnateContent>();
    }

    private sealed class BlockedContentAuthority : IProductionGameplayContentAuthority
    {
        public static BlockedContentAuthority Instance { get; } = new();
        public string ActiveReleaseId => string.Empty;
        public bool IsReady => false;
        public OperationResult RequireReady() => OperationResult.Failure(
            "content_release.runtime_not_ready",
            "Blocked for test.");
        public OperationResult<ProductionQuestContent> ResolveQuest(int questId) => Missing<ProductionQuestContent>();
        public OperationResult<ProductionEquipmentSetContent> ResolveEquipmentSet(int setId) => Missing<ProductionEquipmentSetContent>();
        public OperationResult<ProductionPetInnateContent> ResolvePetInnate(int innateId) => Missing<ProductionPetInnateContent>();
    }

    private static OperationResult<T> Missing<T>() =>
        OperationResult<T>.Failure("test.missing", "Not configured.");
}
