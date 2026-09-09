using System.Collections.Concurrent;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed record MariaDbProductionCombatComposition(
    ICombatCoordinator Combat,
    MariaDbProductionMonsterCombatService MonsterCombat,
    IWorldInteractionSessionRegistry InteractionSessions,
    PlayerCombatRuntimeRegistry Players,
    ICombatRewardPolicy RewardPolicy,
    ICombatMutationStore MutationStore,
    ICombatRewardCoordinator RewardCoordinator,
    IMonsterRespawnCoordinator RespawnCoordinator,
    ICombatEventSink Events,
    ICombatAuditLedger Audit);

public static class MariaDbProductionCombatCompositionFactory
{
    public static MariaDbProductionCombatComposition Create(
        DatabaseOptions database,
        MariaDbStaticDataLoader staticData,
        IWorldSessionCoordinator worldSessions,
        IInventoryTransactionCoordinator inventory,
        MapRuntime mapRuntime)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(staticData);
        ArgumentNullException.ThrowIfNull(worldSessions);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(mapRuntime);
        if (worldSessions.AuthorityKind != WorldContentAuthorityKind.MariaDb)
        {
            throw new InvalidOperationException("Production combat composition requires MariaDB world authority.");
        }

        var interactionSessions = new WorldSessionInteractionRegistryAdapter(worldSessions);
        var players = new PlayerCombatRuntimeRegistry();
        var events = new InMemoryCombatEventSink();
        var audit = new InMemoryCombatAuditLedger();
        var mutationStore = new MariaDbCombatMutationStore(database);
        var rewardPolicy = new MariaDbCombatRewardPolicy(staticData);
        var rewardCoordinator = new CombatRewardCoordinator(inventory, new CombatFailureInjection());
        var respawns = new MonsterRespawnCoordinator(
            World(mapRuntime),
            mapRuntime.MonsterCombat,
            mutationStore,
            new ContentBackedMonsterRespawnPolicy(),
            events,
            new CombatFailureInjection());
        var resolver = new RuntimeCombatTargetResolver(
            World(mapRuntime),
            interactionSessions,
            players,
            mapRuntime.MonsterCombat);
        var combat = new CombatCoordinator(
            resolver,
            new RuntimeCombatEligibilityPolicy(new EvidenceBlockedCombatRangePolicy()),
            new RuntimeCombatStatProvider(players, mapRuntime.MonsterCombat),
            new BaselineServerDamagePolicy(),
            mapRuntime.MonsterCombat,
            players,
            mutationStore,
            new MonsterDeathCoordinator(),
            rewardPolicy,
            rewardCoordinator,
            respawns,
            new BaselineCombatCooldownStore(),
            events,
            audit);
        return new MariaDbProductionCombatComposition(
            combat,
            new MariaDbProductionMonsterCombatService(combat, worldSessions, players),
            interactionSessions,
            players,
            rewardPolicy,
            mutationStore,
            rewardCoordinator,
            respawns,
            events,
            audit);
    }

    private static WorldRuntime World(MapRuntime mapRuntime)
    {
        var world = new WorldRuntime();
        world.Add(mapRuntime);
        return world;
    }
}

public sealed class MariaDbProductionMonsterCombatService : IProductionMonsterCombatService
{
    private readonly ICombatCoordinator _combat;
    private readonly IWorldSessionCoordinator _sessions;
    private readonly PlayerCombatRuntimeRegistry _players;

    public MariaDbProductionMonsterCombatService(
        ICombatCoordinator combat,
        IWorldSessionCoordinator sessions,
        PlayerCombatRuntimeRegistry players)
    {
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _players = players ?? throw new ArgumentNullException(nameof(players));
    }

    public async Task<CombatResult> BasicAttackMonsterAsync(
        string sessionId,
        long monsterRuntimeEntityId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var binding = _sessions.GetBinding(sessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return CombatResult.Reject(
                IntentFallback(sessionId, monsterRuntimeEntityId, idempotencyKey, now),
                CombatResultCode.InvalidSession,
                binding.Error.Code);
        }

        var current = binding.Value;
        var player = current.MapRuntime.Objects.Get(current.MapSession.PlayerRuntimeEntityId);
        if (!player.Succeeded || player.Value is not PlayerObject playerObject)
        {
            return CombatResult.Reject(
                IntentFallback(sessionId, monsterRuntimeEntityId, idempotencyKey, now),
                CombatResultCode.AttackerNotFound,
                "combat.player_runtime_missing");
        }

        var monster = current.MapRuntime.MonsterCombat.Get(monsterRuntimeEntityId);
        if (!monster.Succeeded || monster.Value is null)
        {
            return CombatResult.Reject(
                IntentFallback(sessionId, monsterRuntimeEntityId, idempotencyKey, now),
                CombatResultCode.TargetNotFound,
                monster.Error.Code);
        }

        var playerVersion = EnsurePlayerState(current, playerObject, now);
        var intent = new CombatIntent(
            Guid.NewGuid(),
            idempotencyKey,
            sessionId,
            current.Character.CharacterId,
            playerObject.Identity.RuntimeObjectId,
            monsterRuntimeEntityId,
            monster.Value.MonsterTemplateId,
            CombatActionType.BasicAttack,
            playerVersion,
            monster.Value.RuntimeVersion,
            null,
            "MariaDbProductionMonsterCombatService",
            now,
            Guid.NewGuid().ToString("N"));
        return await _combat.ExecuteAsync(intent, cancellationToken);
    }

    private long EnsurePlayerState(WorldSessionBinding binding, PlayerObject player, DateTimeOffset now)
    {
        var existing = _players.Get(player.Identity.RuntimeObjectId);
        if (existing.Succeeded && existing.Value is not null)
        {
            return existing.Value.RuntimeVersion;
        }

        var hp = Math.Max(1, binding.Character.CurrentHitPoints ?? binding.Character.MaximumHitPoints ?? 100);
        var maxHp = Math.Max(hp, binding.Character.MaximumHitPoints ?? hp);
        var attack = Math.Max(1, binding.Character.Strength ?? 40);
        var defense = Math.Max(0, binding.Character.Constitution ?? 2);
        _players.Register(new PlayerCombatRuntimeState(
            player.Identity.RuntimeObjectId,
            binding.Character.CharacterId,
            binding.MapRuntime.WorldInstanceId,
            binding.MapSession.MapId,
            player.State.Position,
            MonsterLifecycleState.Active,
            maxHp,
            hp,
            attack,
            defense,
            0,
            null,
            "Attached",
            CombatPolicyStatus.Baseline,
            now));
        return 0;
    }

    private static CombatIntent IntentFallback(
        string sessionId,
        long monsterRuntimeEntityId,
        string idempotencyKey,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey,
            string.IsNullOrWhiteSpace(sessionId) ? "missing" : sessionId,
            0,
            0,
            monsterRuntimeEntityId,
            null,
            CombatActionType.BasicAttack,
            0,
            0,
            null,
            "MariaDbProductionMonsterCombatService",
            now,
            Guid.NewGuid().ToString("N"));
}

public sealed class MariaDbProductionMonsterCombatServiceFactory : IProductionMonsterCombatServiceFactory
{
    private readonly DatabaseOptions _database;
    private readonly MariaDbStaticDataLoader _staticData;
    private readonly IWorldSessionCoordinator _worldSessions;
    private readonly IInventoryTransactionCoordinator _inventory;
    private readonly ConcurrentDictionary<MapRuntime, Lazy<MariaDbProductionCombatComposition>> _compositions = [];

    public MariaDbProductionMonsterCombatServiceFactory(
        DatabaseOptions database,
        MariaDbStaticDataLoader staticData,
        IWorldSessionCoordinator worldSessions,
        IInventoryTransactionCoordinator inventory)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _staticData = staticData ?? throw new ArgumentNullException(nameof(staticData));
        _worldSessions = worldSessions ?? throw new ArgumentNullException(nameof(worldSessions));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public OperationResult<IProductionMonsterCombatService> CreateForSession(string sessionId)
    {
        var binding = _worldSessions.GetBinding(sessionId);
        if (!binding.Succeeded || binding.Value is null)
        {
            return OperationResult<IProductionMonsterCombatService>.Failure(
                binding.Error.Code,
                binding.Error.Message,
                binding.Error.Source);
        }

        var composition = _compositions.GetOrAdd(
            binding.Value.MapRuntime,
            mapRuntime => new Lazy<MariaDbProductionCombatComposition>(
                () => MariaDbProductionCombatCompositionFactory.Create(
                    _database,
                    _staticData,
                    _worldSessions,
                    _inventory,
                    mapRuntime),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return OperationResult<IProductionMonsterCombatService>.Success(composition.MonsterCombat);
    }
}
