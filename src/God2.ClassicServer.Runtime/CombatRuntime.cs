using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum CombatActionType
{
    BasicAttack,
    MonsterBasicAttack,
    Skill,
    ItemEffect,
    Environmental,
    SystemDamage,
    Unknown
}

public enum CombatResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    InvalidSession,
    OwnershipMismatch,
    AttackerNotFound,
    TargetNotFound,
    AttackerInactive,
    TargetInactive,
    AttackerDead,
    TargetDead,
    DifferentMap,
    DifferentInstance,
    OutOfRange,
    RangeEvidenceBlocked,
    UnsupportedAction,
    CooldownActive,
    ReplayConflict,
    VersionConflict,
    DamagePolicyBlocked,
    PersistenceFailure,
    RewardFailure,
    RuntimeMutationFailure,
    InternalFailure
}

public enum CombatPolicyStatus
{
    Verified,
    ContentBacked,
    Baseline,
    TestOnly,
    EvidenceBlocked
}

public enum CombatEntityType
{
    Player,
    Monster
}

public enum CombatDamageType
{
    Physical,
    Magic,
    True,
    Environmental,
    Unknown
}

public enum MonsterLifecycleState
{
    Created,
    Loading,
    Active,
    Inactive,
    DespawnPending,
    Destroyed,
    Faulted
}

public enum MonsterCombatStateKind
{
    Idle,
    Engaged,
    Attacking,
    Hit,
    Dead,
    RespawnPending,
    Respawning,
    Disabled,
    Faulted
}

public enum MonsterDeathState
{
    Alive,
    LethalDamagePending,
    Dead,
    RewardResolving,
    DespawnPending,
    RespawnPending,
    Respawning,
    RewardFailed,
    PersistenceFailed,
    RecoveryRequired,
    Faulted
}

public enum MonsterRespawnState
{
    NotScheduled,
    Scheduled,
    Due,
    Respawning,
    Registered,
    Active,
    BlockedByPolicy,
    TargetMapUnavailable,
    RegistryConflict,
    PersistenceFailure,
    RecoveryRequired,
    Faulted
}

public enum CombatRewardResultCode
{
    NoReward,
    Committed,
    DuplicateCompleted,
    InventoryFull,
    TransactionFailed,
    BlockedByPolicy
}

public enum CombatEventKind
{
    CombatIntentReceived,
    CombatIntentRejected,
    CombatStarted,
    DamagePlanned,
    DamageApplied,
    HpChanged,
    CombatCompleted,
    MonsterEngaged,
    MonsterHit,
    MonsterLethalDamage,
    MonsterDied,
    RewardResolutionStarted,
    RewardCommitted,
    RewardFailed,
    MonsterDespawnPending,
    MonsterRespawnScheduled,
    MonsterRespawning,
    MonsterRespawned,
    CombatRecoveryRequired
}

public enum CombatFailurePoint
{
    None,
    TargetDestroyedDuringValidation,
    DamageOverflow,
    PersistenceFailureBeforeHpMutation,
    PersistenceFailureAfterHpMutation,
    RuntimeFailureAfterDatabaseCommit,
    DisconnectDuringCombat,
    ReconnectDuringCombat,
    DeathCommitFailure,
    RewardTransactionFailure,
    AuditFailure,
    RespawnScheduleFailure,
    RespawnRegistryConflict,
    RespawnTargetMapMissing,
    RecoveryFailure
}

public sealed class CombatFailureInjection
{
    public CombatFailurePoint Point { get; set; }

    public bool RecoveryFailureEnabled { get; set; }
}

public sealed record CombatIntent(
    Guid CombatIntentId,
    string IdempotencyKey,
    string SessionId,
    long CharacterId,
    long AttackerRuntimeEntityId,
    long TargetRuntimeEntityId,
    int? TargetTemplateId,
    CombatActionType CombatActionType,
    long ExpectedAttackerVersion,
    long ExpectedTargetVersion,
    long? ClientSequenceCandidate,
    string Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record MonsterCombatDefinition(
    int MonsterTemplateId,
    int SpawnDefinitionId,
    string SpawnGroupId,
    int MapId,
    WorldPosition3 RawSpawnPosition,
    WorldDirection Direction,
    TimeSpan RespawnTime,
    int Level,
    long MaximumHp,
    long AttackPower,
    long Defense,
    bool Enabled,
    CombatPolicyStatus StatPolicyStatus,
    CombatPolicyStatus RespawnPolicyStatus,
    string ContentVersion,
    string RawMetadata,
    string Source,
    long MaximumMp = 0,
    long MagicAttackPower = 0,
    long MagicDefense = 0,
    int Metal = 0,
    int Wood = 0,
    int Water = 0,
    int Fire = 0,
    int Earth = 0);

public sealed record CombatContentValidationIssue(
    string Code,
    int MonsterTemplateId,
    int SpawnDefinitionId,
    string Message,
    string Source);

public sealed record ValidatedMonsterCombatDefinitions(
    IReadOnlyList<MonsterCombatDefinition> Valid,
    IReadOnlyList<MonsterCombatDefinition> Quarantined,
    IReadOnlyList<CombatContentValidationIssue> Issues);

public sealed class MonsterCombatContentMapper
{
    public MonsterCombatDefinition Map(MonsterSpawnDefinition definition)
    {
        var contentBacked = definition.MaximumHp > 0 &&
                            definition.Level >= 0 &&
                            definition.AttackPower >= 0 &&
                            definition.Defense >= 0;
        return new MonsterCombatDefinition(
            definition.MonsterTemplateId,
            definition.SpawnId,
            $"spawn:{definition.SpawnId}",
            definition.MapId,
            definition.Position,
            definition.Direction,
            definition.RespawnTime,
            definition.Level,
            definition.MaximumHp,
            definition.AttackPower,
            definition.Defense,
            definition.Enabled,
            contentBacked ? CombatPolicyStatus.ContentBacked : CombatPolicyStatus.EvidenceBlocked,
            definition.RespawnTime > TimeSpan.Zero ? CombatPolicyStatus.ContentBacked : CombatPolicyStatus.EvidenceBlocked,
            definition.ContentVersion,
            definition.RawMetadata,
            definition.Evidence,
            definition.MaximumMp,
            definition.MagicAttackPower,
            definition.MagicDefense,
            definition.Metal,
            definition.Wood,
            definition.Water,
            definition.Fire,
            definition.Earth);
    }
}

public sealed class MonsterCombatContentValidator
{
    public ValidatedMonsterCombatDefinitions Validate(
        IEnumerable<MonsterCombatDefinition> definitions,
        IReadOnlySet<int> mapIds)
    {
        var valid = new List<MonsterCombatDefinition>();
        var quarantined = new List<MonsterCombatDefinition>();
        var issues = new List<CombatContentValidationIssue>();
        var duplicateSpawnIds = definitions
            .GroupBy(value => value.SpawnDefinitionId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var definition in definitions)
        {
            var local = new List<CombatContentValidationIssue>();
            if (!definition.Enabled)
            {
                local.Add(Issue("combat_content.disabled", definition, "Monster combat content is disabled."));
            }

            if (!mapIds.Contains(definition.MapId))
            {
                local.Add(Issue("combat_content.map_missing", definition, "Monster combat definition references a missing map."));
            }

            if (duplicateSpawnIds.Contains(definition.SpawnDefinitionId))
            {
                local.Add(Issue("combat_content.spawn_duplicate", definition, "Monster spawn definition ID is duplicated."));
            }

            if (definition.MaximumHp <= 0)
            {
                local.Add(Issue("combat_content.maximum_hp_invalid", definition, "Maximum HP must be positive."));
            }

            if (definition.Level < 0 ||
                definition.AttackPower < 0 ||
                definition.Defense < 0 ||
                definition.MaximumMp < 0 ||
                definition.MagicAttackPower < 0 ||
                definition.MagicDefense < 0 ||
                definition.Metal < 0 ||
                definition.Wood < 0 ||
                definition.Water < 0 ||
                definition.Fire < 0 ||
                definition.Earth < 0)
            {
                local.Add(Issue("combat_content.stat_negative", definition, "Level, attack, and defense cannot be negative."));
            }

            if (local.Count == 0)
            {
                valid.Add(definition);
            }
            else
            {
                quarantined.Add(definition);
                issues.AddRange(local);
            }
        }

        return new ValidatedMonsterCombatDefinitions(
            Array.AsReadOnly(valid.ToArray()),
            Array.AsReadOnly(quarantined.ToArray()),
            Array.AsReadOnly(issues.ToArray()));
    }

    private static CombatContentValidationIssue Issue(
        string code,
        MonsterCombatDefinition definition,
        string message) =>
        new(code, definition.MonsterTemplateId, definition.SpawnDefinitionId, message, definition.Source);
}

public sealed record MonsterCombatRuntimeState(
    long RuntimeEntityId,
    int MonsterTemplateId,
    int SpawnDefinitionId,
    string SpawnGroupId,
    string WorldInstanceId,
    int MapId,
    WorldPosition3 RawPosition,
    WorldDirection Direction,
    MonsterLifecycleState LifecycleState,
    MonsterCombatStateKind CombatState,
    int Level,
    long MaximumHp,
    long CurrentHp,
    long AttackPower,
    long Defense,
    long? Accuracy,
    long? Evasion,
    string AttackIntervalPolicy,
    string AggroState,
    long? CurrentTargetRuntimeEntityId,
    long RuntimeVersion,
    string DirtyFlags,
    DateTimeOffset SpawnedAtUtc,
    DateTimeOffset? LastCombatAtUtc,
    DateTimeOffset? DiedAtUtc,
    DateTimeOffset? RespawnDueAtUtc,
    CombatPolicyStatus StatPolicyStatus,
    string ContentVersion,
    string RawMetadata,
    long MaximumMp = 0,
    long CurrentMp = 0,
    long MagicAttackPower = 0,
    long MagicDefense = 0,
    int Metal = 0,
    int Wood = 0,
    int Water = 0,
    int Fire = 0,
    int Earth = 0);

public sealed record PlayerCombatRuntimeState(
    long RuntimeEntityId,
    long CharacterId,
    string WorldInstanceId,
    int MapId,
    WorldPosition3 RawPosition,
    MonsterLifecycleState LifecycleState,
    long MaximumHp,
    long CurrentHp,
    long AttackPower,
    long Defense,
    long RuntimeVersion,
    long? CurrentTargetRuntimeEntityId,
    string DirtyFlags,
    CombatPolicyStatus StatPolicyStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed class MonsterCombatRuntimeRegistry
{
    private readonly ConcurrentDictionary<long, MonsterCombatRuntimeState> _states = [];
    private readonly ConcurrentDictionary<int, MonsterCombatDefinition> _definitionsBySpawn = [];

    public IReadOnlyList<MonsterCombatRuntimeState> Snapshot => _states.Values
        .OrderBy(value => value.MapId)
        .ThenBy(value => value.RuntimeEntityId)
        .ToArray();

    public void Register(MonsterCombatRuntimeState state, MonsterCombatDefinition definition)
    {
        if (state.MaximumHp <= 0 ||
            state.CurrentHp < 0 ||
            state.CurrentHp > state.MaximumHp ||
            state.MaximumMp < 0 ||
            state.CurrentMp < 0 ||
            state.CurrentMp > state.MaximumMp)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Monster HP invariant is invalid.");
        }

        if (!_states.TryAdd(state.RuntimeEntityId, state))
        {
            throw new InvalidOperationException($"Monster combat runtime entity {state.RuntimeEntityId} is already registered.");
        }

        _definitionsBySpawn[state.SpawnDefinitionId] = definition;
    }

    public OperationResult<MonsterCombatRuntimeState> Get(long runtimeEntityId) =>
        _states.TryGetValue(runtimeEntityId, out var state)
            ? OperationResult<MonsterCombatRuntimeState>.Success(state)
            : OperationResult<MonsterCombatRuntimeState>.Failure(
                "combat.monster_state_missing",
                "Monster combat state is not registered.",
                runtimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public OperationResult<MonsterCombatDefinition> GetDefinition(int spawnDefinitionId) =>
        _definitionsBySpawn.TryGetValue(spawnDefinitionId, out var definition)
            ? OperationResult<MonsterCombatDefinition>.Success(definition)
            : OperationResult<MonsterCombatDefinition>.Failure(
                "combat.monster_definition_missing",
                "Monster combat definition is not registered.",
                spawnDefinitionId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Replace(MonsterCombatRuntimeState state) => _states[state.RuntimeEntityId] = state;

    public bool Remove(long runtimeEntityId) => _states.TryRemove(runtimeEntityId, out _);

    public static MonsterCombatRuntimeRegistry Build(
        MapRuntime map,
        IEnumerable<MonsterCombatDefinition> definitions,
        DateTimeOffset now)
    {
        var registry = new MonsterCombatRuntimeRegistry();
        var definitionsBySpawn = definitions.ToDictionary(value => value.SpawnDefinitionId);
        foreach (var monster in map.Objects.OfType<MonsterObject>())
        {
            if (!definitionsBySpawn.TryGetValue(monster.State.SpawnId, out var definition))
            {
                continue;
            }

            registry.Register(
                new MonsterCombatRuntimeState(
                    monster.Identity.RuntimeObjectId,
                    monster.State.MonsterTemplateId,
                    monster.State.SpawnId,
                    definition.SpawnGroupId,
                    map.WorldInstanceId,
                    monster.State.MapId,
                    monster.State.Position,
                    monster.State.Direction,
                    MonsterLifecycleState.Active,
                    MonsterCombatStateKind.Idle,
                    definition.Level,
                    definition.MaximumHp,
                    definition.MaximumHp,
                    definition.AttackPower,
                    definition.Defense,
                    null,
                    null,
                    "EvidenceBlocked",
                    "None",
                    null,
                    0,
                    "Spawned",
                    now,
                    null,
                    null,
                    null,
                    definition.StatPolicyStatus,
                    definition.ContentVersion,
                    definition.RawMetadata,
                    definition.MaximumMp,
                    definition.MaximumMp,
                    definition.MagicAttackPower,
                    definition.MagicDefense,
                    definition.Metal,
                    definition.Wood,
                    definition.Water,
                    definition.Fire,
                    definition.Earth),
                definition);
        }

        return registry;
    }
}

public sealed class PlayerCombatRuntimeRegistry
{
    private readonly ConcurrentDictionary<long, PlayerCombatRuntimeState> _states = [];

    public IReadOnlyList<PlayerCombatRuntimeState> Snapshot => _states.Values
        .OrderBy(value => value.RuntimeEntityId)
        .ToArray();

    public void Register(PlayerCombatRuntimeState state)
    {
        if (state.MaximumHp <= 0 || state.CurrentHp < 0 || state.CurrentHp > state.MaximumHp)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Player HP invariant is invalid.");
        }

        _states[state.RuntimeEntityId] = state;
    }

    public OperationResult<PlayerCombatRuntimeState> Get(long runtimeEntityId) =>
        _states.TryGetValue(runtimeEntityId, out var state)
            ? OperationResult<PlayerCombatRuntimeState>.Success(state)
            : OperationResult<PlayerCombatRuntimeState>.Failure(
                "combat.player_state_missing",
                "Player combat state is not registered.",
                runtimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Replace(PlayerCombatRuntimeState state) => _states[state.RuntimeEntityId] = state;

    public bool Remove(long runtimeEntityId) => _states.TryRemove(runtimeEntityId, out _);
}

public sealed record CombatStatSnapshot(
    long RuntimeEntityId,
    CombatEntityType EntityType,
    int Level,
    long MaximumHp,
    long CurrentHp,
    long MaximumMp,
    long CurrentMp,
    long AttackPower,
    long Defense,
    long MagicAttackPower,
    long MagicDefense,
    int Metal,
    int Wood,
    int Water,
    int Fire,
    int Earth,
    long? Accuracy,
    long? Evasion,
    decimal? CriticalChance,
    decimal? CriticalMultiplier,
    decimal? DamageReduction,
    long RuntimeVersion,
    CombatPolicyStatus PolicyStatus,
    string ContentVersion);

public interface ICombatStatProvider
{
    OperationResult<CombatStatSnapshot> GetPlayer(long runtimeEntityId);

    OperationResult<CombatStatSnapshot> GetMonster(long runtimeEntityId);
}

public sealed class RuntimeCombatStatProvider : ICombatStatProvider
{
    private readonly PlayerCombatRuntimeRegistry _players;
    private readonly MonsterCombatRuntimeRegistry _monsters;

    public RuntimeCombatStatProvider(
        PlayerCombatRuntimeRegistry players,
        MonsterCombatRuntimeRegistry monsters)
    {
        _players = players;
        _monsters = monsters;
    }

    public OperationResult<CombatStatSnapshot> GetPlayer(long runtimeEntityId)
    {
        var result = _players.Get(runtimeEntityId);
        return !result.Succeeded || result.Value is null
            ? OperationResult<CombatStatSnapshot>.Failure(result.Error.Code, result.Error.Message, result.Error.Source)
            : Validate(new CombatStatSnapshot(
                result.Value.RuntimeEntityId,
                CombatEntityType.Player,
                0,
                result.Value.MaximumHp,
                result.Value.CurrentHp,
                0,
                0,
                result.Value.AttackPower,
                result.Value.Defense,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                null,
                result.Value.RuntimeVersion,
                result.Value.StatPolicyStatus,
                "player-combat-component-v1"));
    }

    public OperationResult<CombatStatSnapshot> GetMonster(long runtimeEntityId)
    {
        var result = _monsters.Get(runtimeEntityId);
        return !result.Succeeded || result.Value is null
            ? OperationResult<CombatStatSnapshot>.Failure(result.Error.Code, result.Error.Message, result.Error.Source)
            : Validate(new CombatStatSnapshot(
                result.Value.RuntimeEntityId,
                CombatEntityType.Monster,
                result.Value.Level,
                result.Value.MaximumHp,
                result.Value.CurrentHp,
                result.Value.MaximumMp,
                result.Value.CurrentMp,
                result.Value.AttackPower,
                result.Value.Defense,
                result.Value.MagicAttackPower,
                result.Value.MagicDefense,
                result.Value.Metal,
                result.Value.Wood,
                result.Value.Water,
                result.Value.Fire,
                result.Value.Earth,
                result.Value.Accuracy,
                result.Value.Evasion,
                null,
                null,
                null,
                result.Value.RuntimeVersion,
                result.Value.StatPolicyStatus,
                result.Value.ContentVersion));
    }

    private static OperationResult<CombatStatSnapshot> Validate(CombatStatSnapshot snapshot)
    {
        if (snapshot.MaximumHp <= 0 ||
            snapshot.CurrentHp < 0 ||
            snapshot.CurrentHp > snapshot.MaximumHp ||
            snapshot.MaximumMp < 0 ||
            snapshot.CurrentMp < 0 ||
            snapshot.CurrentMp > snapshot.MaximumMp ||
            snapshot.AttackPower < 0 ||
            snapshot.Defense < 0 ||
            snapshot.MagicAttackPower < 0 ||
            snapshot.MagicDefense < 0 ||
            snapshot.Metal < 0 ||
            snapshot.Wood < 0 ||
            snapshot.Water < 0 ||
            snapshot.Fire < 0 ||
            snapshot.Earth < 0)
        {
            return OperationResult<CombatStatSnapshot>.Failure(
                "combat.stats_invalid",
                "Combat stat snapshot violates HP or non-negative stat invariants.");
        }

        return OperationResult<CombatStatSnapshot>.Success(snapshot);
    }
}

public sealed record ResolvedCombatTarget(
    InteractionSessionBinding SessionBinding,
    MapRuntime AttackerMap,
    MapRuntime TargetMap,
    IRuntimeObject AttackerObject,
    IRuntimeObject TargetObject,
    PlayerCombatRuntimeState? AttackerPlayerState,
    MonsterCombatRuntimeState? AttackerMonsterState,
    PlayerCombatRuntimeState? TargetPlayerState,
    MonsterCombatRuntimeState? TargetMonsterState);

public interface ICombatTargetResolver
{
    OperationResult<ResolvedCombatTarget> Resolve(CombatIntent intent);
}

public sealed class RuntimeCombatTargetResolver : ICombatTargetResolver
{
    private readonly WorldRuntime _world;
    private readonly IWorldInteractionSessionRegistry _sessions;
    private readonly PlayerCombatRuntimeRegistry _players;
    private readonly MonsterCombatRuntimeRegistry _monsters;

    public RuntimeCombatTargetResolver(
        WorldRuntime world,
        IWorldInteractionSessionRegistry sessions,
        PlayerCombatRuntimeRegistry players,
        MonsterCombatRuntimeRegistry monsters)
    {
        _world = world;
        _sessions = sessions;
        _players = players;
        _monsters = monsters;
    }

    public OperationResult<ResolvedCombatTarget> Resolve(CombatIntent intent)
    {
        var bindingResult = _sessions.Get(intent.SessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return Failure("combat.invalid_session", "Combat session is not bound.");
        }

        var binding = bindingResult.Value;
        if (!binding.Session.IsAuthenticated ||
            binding.Session.IsClosing ||
            binding.Session.CharacterId != intent.CharacterId)
        {
            return Failure("combat.ownership_mismatch", "Combat session does not own the character.");
        }

        var attacker = Find(intent.AttackerRuntimeEntityId);
        if (attacker.Object is null || attacker.Map is null)
        {
            return Failure("combat.attacker_not_found", "Combat attacker runtime object is missing.");
        }

        var target = Find(intent.TargetRuntimeEntityId);
        if (target.Object is null || target.Map is null)
        {
            return Failure("combat.target_not_found", "Combat target runtime object is missing.");
        }

        if (intent.TargetTemplateId is not null &&
            intent.TargetTemplateId.Value != target.Object.Identity.TemplateId)
        {
            return Failure("combat.target_template_mismatch", "Target template cross-check failed.");
        }

        PlayerCombatRuntimeState? attackerPlayer = null;
        MonsterCombatRuntimeState? attackerMonster = null;
        PlayerCombatRuntimeState? targetPlayer = null;
        MonsterCombatRuntimeState? targetMonster = null;
        if (attacker.Object is PlayerObject)
        {
            var state = _players.Get(attacker.Object.Identity.RuntimeObjectId);
            if (!state.Succeeded || state.Value is null)
            {
                return Failure("combat.attacker_state_missing", "Player combat state is not registered.");
            }

            attackerPlayer = state.Value;
        }
        else if (attacker.Object is MonsterObject)
        {
            var state = _monsters.Get(attacker.Object.Identity.RuntimeObjectId);
            if (!state.Succeeded || state.Value is null)
            {
                return Failure("combat.attacker_state_missing", "Monster combat state is not registered.");
            }

            attackerMonster = state.Value;
        }
        else
        {
            return Failure("combat.attacker_type_unsupported", "Combat attacker type is not supported.");
        }

        if (target.Object is PlayerObject)
        {
            var state = _players.Get(target.Object.Identity.RuntimeObjectId);
            if (!state.Succeeded || state.Value is null)
            {
                return Failure("combat.target_state_missing", "Player combat state is not registered.");
            }

            targetPlayer = state.Value;
        }
        else if (target.Object is MonsterObject)
        {
            var state = _monsters.Get(target.Object.Identity.RuntimeObjectId);
            if (!state.Succeeded || state.Value is null)
            {
                return Failure("combat.target_state_missing", "Monster combat state is not registered.");
            }

            targetMonster = state.Value;
        }
        else
        {
            return Failure("combat.target_type_unsupported", "Combat target type is not supported.");
        }

        if (intent.CombatActionType == CombatActionType.BasicAttack &&
            (attacker.Object is not PlayerObject ||
             binding.MapSession.PlayerRuntimeEntityId != attacker.Object.Identity.RuntimeObjectId ||
             attackerPlayer?.CharacterId != intent.CharacterId))
        {
            return Failure("combat.attacker_ownership_mismatch", "Basic attack attacker is not the session player.");
        }

        if (intent.CombatActionType == CombatActionType.MonsterBasicAttack &&
            (!intent.Source.StartsWith("TrustedInternal", StringComparison.Ordinal) ||
             attacker.Object is not MonsterObject ||
             target.Object.Identity.RuntimeObjectId != binding.MapSession.PlayerRuntimeEntityId))
        {
            return Failure("combat.monster_attack_not_trusted", "Monster attack requires trusted internal orchestration.");
        }

        return OperationResult<ResolvedCombatTarget>.Success(new ResolvedCombatTarget(
            binding,
            attacker.Map,
            target.Map,
            attacker.Object,
            target.Object,
            attackerPlayer,
            attackerMonster,
            targetPlayer,
            targetMonster));
    }

    private (MapRuntime? Map, IRuntimeObject? Object) Find(long runtimeEntityId)
    {
        foreach (var map in _world.ActiveMaps.Values)
        {
            var result = map.Objects.Get(runtimeEntityId);
            if (result.Succeeded && result.Value is not null)
            {
                return (map, result.Value);
            }
        }

        return (null, null);
    }

    private static OperationResult<ResolvedCombatTarget> Failure(string code, string message) =>
        OperationResult<ResolvedCombatTarget>.Failure(code, message);
}

public sealed record CombatRangeEvaluation(
    bool Allowed,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public interface ICombatRangePolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    CombatRangeEvaluation Evaluate(IMapPositionedRuntimeState attacker, IMapPositionedRuntimeState target);
}

public sealed class EvidenceBlockedCombatRangePolicy : ICombatRangePolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.EvidenceBlocked;

    public CombatRangeEvaluation Evaluate(IMapPositionedRuntimeState attacker, IMapPositionedRuntimeState target) =>
        new(false, PolicyStatus, "combat.range_evidence_blocked");
}

public sealed class DeterministicTestCombatRangePolicy : ICombatRangePolicy
{
    private readonly int _maximumRawDistance;

    public DeterministicTestCombatRangePolicy(int maximumRawDistance)
    {
        if (maximumRawDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRawDistance));
        }

        _maximumRawDistance = maximumRawDistance;
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public CombatRangeEvaluation Evaluate(IMapPositionedRuntimeState attacker, IMapPositionedRuntimeState target)
    {
        try
        {
            var dx = checked((long)target.Position.X - attacker.Position.X);
            var dy = checked((long)target.Position.Y - attacker.Position.Y);
            var maximum = checked((long)_maximumRawDistance * _maximumRawDistance);
            return checked((dx * dx) + (dy * dy)) <= maximum
                ? new CombatRangeEvaluation(true, PolicyStatus, "")
                : new CombatRangeEvaluation(false, PolicyStatus, "combat.out_of_range");
        }
        catch (OverflowException)
        {
            return new CombatRangeEvaluation(false, PolicyStatus, "combat.range_overflow");
        }
    }
}

public sealed record CombatEligibilityResult(
    bool Allowed,
    CombatResultCode ResultCode,
    string FailureCode,
    CombatPolicyStatus RangePolicyStatus);

public interface ICombatEligibilityPolicy
{
    CombatEligibilityResult Evaluate(CombatIntent intent, ResolvedCombatTarget target);
}

public sealed class RuntimeCombatEligibilityPolicy : ICombatEligibilityPolicy
{
    private readonly ICombatRangePolicy _range;

    public RuntimeCombatEligibilityPolicy(ICombatRangePolicy range)
    {
        _range = range;
    }

    public CombatEligibilityResult Evaluate(CombatIntent intent, ResolvedCombatTarget target)
    {
        if (target.SessionBinding.IsTransitioning)
        {
            return Reject(CombatResultCode.AttackerInactive, "combat.player_transitioning");
        }

        if (target.AttackerObject.State is not IMapPositionedRuntimeState attackerPosition ||
            target.TargetObject.State is not IMapPositionedRuntimeState targetPosition)
        {
            return Reject(CombatResultCode.Rejected, "combat.position_state_missing");
        }

        if (attackerPosition.MapId != targetPosition.MapId)
        {
            return Reject(CombatResultCode.DifferentMap, "combat.different_map");
        }

        if (!string.Equals(target.SessionBinding.WorldInstanceId, target.AttackerMap.WorldInstanceId, StringComparison.Ordinal) ||
            !string.Equals(target.AttackerMap.WorldInstanceId, target.TargetMap.WorldInstanceId, StringComparison.Ordinal))
        {
            return Reject(CombatResultCode.DifferentInstance, "combat.different_instance");
        }

        if (intent.CombatActionType is not (CombatActionType.BasicAttack or CombatActionType.MonsterBasicAttack or CombatActionType.SystemDamage))
        {
            return Reject(CombatResultCode.UnsupportedAction, "combat.action_unsupported");
        }

        if (!Alive(target.AttackerPlayerState, target.AttackerMonsterState))
        {
            return Reject(CombatResultCode.AttackerDead, "combat.attacker_dead");
        }

        if (!Alive(target.TargetPlayerState, target.TargetMonsterState))
        {
            return Reject(CombatResultCode.TargetDead, "combat.target_dead");
        }

        if (!Active(target.AttackerPlayerState, target.AttackerMonsterState))
        {
            return Reject(CombatResultCode.AttackerInactive, "combat.attacker_inactive");
        }

        if (!Active(target.TargetPlayerState, target.TargetMonsterState))
        {
            return Reject(CombatResultCode.TargetInactive, "combat.target_inactive");
        }

        var range = _range.Evaluate(attackerPosition, targetPosition);
        if (!range.Allowed)
        {
            return new CombatEligibilityResult(
                false,
                range.PolicyStatus == CombatPolicyStatus.EvidenceBlocked
                    ? CombatResultCode.RangeEvidenceBlocked
                    : CombatResultCode.OutOfRange,
                range.FailureCode,
                range.PolicyStatus);
        }

        return new CombatEligibilityResult(true, CombatResultCode.Success, "", range.PolicyStatus);
    }

    private CombatEligibilityResult Reject(CombatResultCode code, string failureCode) =>
        new(false, code, failureCode, _range.PolicyStatus);

    private static bool Alive(
        PlayerCombatRuntimeState? player,
        MonsterCombatRuntimeState? monster) =>
        player is not null
            ? player.CurrentHp > 0
            : monster is not null && monster.CombatState != MonsterCombatStateKind.Dead && monster.CurrentHp > 0;

    private static bool Active(
        PlayerCombatRuntimeState? player,
        MonsterCombatRuntimeState? monster) =>
        player is not null
            ? player.LifecycleState == MonsterLifecycleState.Active
            : monster is not null &&
              monster.LifecycleState == MonsterLifecycleState.Active &&
              monster.CombatState is not (MonsterCombatStateKind.Disabled or MonsterCombatStateKind.Faulted);
}

public sealed record CombatDamageComputation(
    bool Succeeded,
    long BaseDamage,
    long FinalDamage,
    CombatDamageType DamageType,
    bool IsCritical,
    bool IsBlocked,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public interface ICombatDamagePolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    CombatDamageComputation Compute(CombatStatSnapshot attacker, CombatStatSnapshot target);
}

public sealed class BaselineServerDamagePolicy : ICombatDamagePolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.Baseline;

    public CombatDamageComputation Compute(CombatStatSnapshot attacker, CombatStatSnapshot target)
    {
        try
        {
            var baseline = BaselineBasicPhysicalAttackFormula.Compute(
                attacker.AttackPower,
                target.Defense);
            return new CombatDamageComputation(
                true,
                attacker.AttackPower,
                baseline.FinalDamage,
                CombatDamageType.Physical,
                false,
                false,
                PolicyStatus,
                "");
        }
        catch (OverflowException)
        {
            return new CombatDamageComputation(
                false,
                0,
                0,
                CombatDamageType.Unknown,
                false,
                false,
                PolicyStatus,
                "combat.damage_overflow");
        }
    }
}

public sealed class DeterministicTestDamagePolicy : ICombatDamagePolicy
{
    private readonly long _damage;

    public DeterministicTestDamagePolicy(long damage)
    {
        _damage = damage;
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public CombatDamageComputation Compute(CombatStatSnapshot attacker, CombatStatSnapshot target) =>
        _damage <= 0
            ? new CombatDamageComputation(false, _damage, 0, CombatDamageType.Physical, false, false, PolicyStatus, "combat.damage_invalid")
            : new CombatDamageComputation(true, _damage, _damage, CombatDamageType.Physical, false, false, PolicyStatus, "");
}

public sealed record CombatDamagePlan(
    Guid DamagePlanId,
    Guid CombatIntentId,
    long AttackerRuntimeEntityId,
    long TargetRuntimeEntityId,
    CombatActionType ActionType,
    CombatStatSnapshot AttackerStats,
    CombatStatSnapshot TargetStats,
    long BaseDamage,
    long FinalDamage,
    CombatDamageType DamageType,
    bool IsCritical,
    bool IsBlocked,
    long HpBefore,
    long HpAfter,
    long AttackerVersionBefore,
    long TargetVersionBefore,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record MonsterDeathRecord(
    Guid DeathId,
    Guid CombatIntentId,
    long MonsterRuntimeEntityId,
    int MonsterTemplateId,
    long KillerRuntimeEntityId,
    long KillerCharacterId,
    int MapId,
    int SpawnDefinitionId,
    long HpBefore,
    long FinalDamage,
    DateTimeOffset DiedAtUtc,
    CombatPolicyStatus RewardPolicyStatus,
    CombatPolicyStatus DropPolicyStatus,
    CombatPolicyStatus RespawnPolicyStatus,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string CorrelationId);

public sealed record CombatItemGrant(int ItemTemplateId, int Quantity);

public sealed record CombatCurrencyGrant(string CurrencyType, long Amount);

public sealed record CombatDropRewardDefinition(
    int DropTableId,
    int MonsterTemplateId,
    int ItemTemplateId,
    int MinimumQuantity,
    int MaximumQuantity,
    decimal DropRate,
    bool IsGuaranteed,
    CombatPolicyStatus PolicyStatus,
    string Source);

public sealed record CombatRewardPlan(
    Guid RewardPlanId,
    Guid DeathId,
    long CharacterId,
    int MonsterTemplateId,
    int? DropTableId,
    IReadOnlyList<CombatItemGrant> ItemGrants,
    IReadOnlyList<CombatCurrencyGrant> CurrencyGrants,
    long ExperienceGrantBoundary,
    string IdempotencyKey,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId,
    long ExpectedInventoryVersion);

public sealed record CombatRewardResult(
    CombatRewardResultCode Code,
    Guid RewardPlanId,
    bool ItemCommitted,
    bool CurrencyCommitted,
    int ItemGrantCount,
    int CurrencyGrantCount,
    string FailureCode,
    InventoryTransactionResult? InventoryResult)
{
    public bool Succeeded => Code is CombatRewardResultCode.NoReward or CombatRewardResultCode.Committed or CombatRewardResultCode.DuplicateCompleted;
}

public sealed record MonsterRespawnPlan(
    Guid RespawnId,
    Guid DeathId,
    int MonsterTemplateId,
    int SpawnDefinitionId,
    string SpawnGroupId,
    string WorldInstanceId,
    int MapId,
    WorldPosition3 RawSpawnPosition,
    WorldDirection Direction,
    DateTimeOffset RespawnDueAtUtc,
    CombatPolicyStatus PolicyStatus,
    MonsterRespawnState State,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId,
    long PreviousRuntimeEntityId);

public sealed record MonsterRespawnResult(
    Guid RespawnId,
    MonsterRespawnState State,
    long? NewRuntimeEntityId,
    string FailureCode,
    DateTimeOffset CompletedAtUtc);

public sealed record CombatResult(
    CombatResultCode Code,
    Guid CombatIntentId,
    string IdempotencySafeId,
    CombatDamagePlan? DamagePlan,
    string FailureCode,
    bool IsDuplicate,
    bool DeathCommitted,
    Guid? DeathId,
    bool RewardCommitted,
    Guid? RewardPlanId,
    bool RespawnScheduled,
    Guid? RespawnId,
    long AttackerVersionBefore,
    long AttackerVersionAfter,
    long TargetVersionBefore,
    long TargetVersionAfter,
    long HpBefore,
    long Damage,
    long HpAfter,
    CombatPolicyStatus DamagePolicyStatus,
    CombatPolicyStatus RangePolicyStatus,
    CombatRewardResult? RewardResult,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is CombatResultCode.Success or CombatResultCode.DuplicateCompleted;

    public static CombatResult Reject(
        CombatIntent intent,
        CombatResultCode code,
        string failureCode,
        CombatPolicyStatus damageStatus = CombatPolicyStatus.EvidenceBlocked,
        CombatPolicyStatus rangeStatus = CombatPolicyStatus.EvidenceBlocked) =>
        new(
            code,
            intent.CombatIntentId,
            CombatHash.SafeId(intent.IdempotencyKey),
            null,
            failureCode,
            false,
            false,
            null,
            false,
            null,
            false,
            null,
            intent.ExpectedAttackerVersion,
            intent.ExpectedAttackerVersion,
            intent.ExpectedTargetVersion,
            intent.ExpectedTargetVersion,
            0,
            0,
            0,
            damageStatus,
            rangeStatus,
            null,
            []);
}

public sealed record CombatReplayLookup(
    bool Found,
    bool PayloadMatches,
    CombatResult? Result,
    CombatRewardPlan? PendingRewardPlan);

public interface ICombatIdempotencyStore
{
    Task<CombatReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken);
}

public sealed record CombatPersistenceCommit(
    CombatIntent Intent,
    string PayloadHash,
    MonsterCombatRuntimeState MonsterBefore,
    MonsterCombatRuntimeState MonsterAfter,
    CombatResult Result,
    MonsterDeathRecord? Death,
    CombatRewardPlan? RewardPlan,
    MonsterRespawnPlan? RespawnPlan,
    CombatAuditRecord Audit);

public interface ICombatMutationStore : ICombatIdempotencyStore
{
    Task<MonsterCombatRuntimeState> LoadMonsterAsync(
        long runtimeEntityId,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitAsync(
        CombatPersistenceCommit commit,
        CancellationToken cancellationToken);

    Task UpdateResultAsync(
        string idempotencyKey,
        CombatResult result,
        CancellationToken cancellationToken);

    Task MarkRespawnCompletedAsync(
        MonsterRespawnPlan plan,
        MonsterRespawnResult result,
        MonsterCombatRuntimeState? activeState,
        CancellationToken cancellationToken);
}

public sealed class InMemoryCombatMutationStore : ICombatMutationStore
{
    private readonly ConcurrentDictionary<long, MonsterCombatRuntimeState> _monsters = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, CombatResult Result, CombatRewardPlan? Reward)> _completed = [];
    private readonly ConcurrentDictionary<Guid, MonsterDeathRecord> _deaths = [];
    private readonly ConcurrentDictionary<Guid, (MonsterRespawnPlan Plan, MonsterRespawnResult? Result)> _respawns = [];

    public CombatFailureInjection FailureInjection { get; } = new();

    public IReadOnlyList<MonsterDeathRecord> Deaths => _deaths.Values.ToArray();

    public IReadOnlyList<MonsterRespawnPlan> Respawns => _respawns.Values.Select(value => value.Plan).ToArray();

    public void Seed(MonsterCombatRuntimeState state) => _monsters[state.RuntimeEntityId] = state;

    public Task<MonsterCombatRuntimeState> LoadMonsterAsync(
        long runtimeEntityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_monsters.TryGetValue(runtimeEntityId, out var state)
            ? state
            : throw new InvalidOperationException("Monster combat persistence state is missing."));
    }

    public Task<CombatReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_completed.TryGetValue(idempotencyKey, out var value))
        {
            return Task.FromResult(new CombatReplayLookup(false, true, null, null));
        }

        return Task.FromResult(new CombatReplayLookup(
            true,
            string.Equals(value.PayloadHash, payloadHash, StringComparison.Ordinal),
            value.Result,
            value.Result.DeathCommitted && !value.Result.RewardCommitted ? value.Reward : null));
    }

    public Task<OperationResult> CommitAsync(
        CombatPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point is CombatFailurePoint.PersistenceFailureBeforeHpMutation or
            CombatFailurePoint.DisconnectDuringCombat)
        {
            return Task.FromResult(OperationResult.Failure(
                "combat.persistence_failure",
                $"Injected failure: {FailureInjection.Point}."));
        }

        if (!_monsters.TryGetValue(commit.MonsterBefore.RuntimeEntityId, out var current) ||
            current.RuntimeVersion != commit.MonsterBefore.RuntimeVersion)
        {
            return Task.FromResult(OperationResult.Failure(
                "combat.version_conflict",
                "Persisted monster runtime version changed before combat commit."));
        }

        if (_completed.TryGetValue(commit.Intent.IdempotencyKey, out var existing))
        {
            return Task.FromResult(string.Equals(existing.PayloadHash, commit.PayloadHash, StringComparison.Ordinal)
                ? OperationResult.Success
                : OperationResult.Failure("combat.replay_conflict", "Combat key was reused with another payload."));
        }

        if (FailureInjection.Point == CombatFailurePoint.DeathCommitFailure && commit.Death is not null)
        {
            return Task.FromResult(OperationResult.Failure("combat.death_commit_failed", "Injected death commit failure."));
        }

        if (FailureInjection.Point == CombatFailurePoint.PersistenceFailureAfterHpMutation)
        {
            _monsters[commit.MonsterAfter.RuntimeEntityId] = commit.MonsterAfter;
            _monsters[commit.MonsterBefore.RuntimeEntityId] = commit.MonsterBefore;
            return Task.FromResult(OperationResult.Failure(
                "combat.persistence_failure_after_hp_rolled_back",
                "Injected post-mutation persistence failure was rolled back."));
        }

        _monsters[commit.MonsterAfter.RuntimeEntityId] = commit.MonsterAfter;
        if (commit.Death is not null)
        {
            _deaths.TryAdd(commit.Death.DeathId, commit.Death);
        }

        if (commit.RespawnPlan is not null)
        {
            _respawns.TryAdd(commit.RespawnPlan.RespawnId, (commit.RespawnPlan, null));
        }

        _completed[commit.Intent.IdempotencyKey] = (commit.PayloadHash, commit.Result, commit.RewardPlan);
        return Task.FromResult(OperationResult.Success);
    }

    public Task UpdateResultAsync(
        string idempotencyKey,
        CombatResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_completed.TryGetValue(idempotencyKey, out var current))
        {
            _completed[idempotencyKey] = (current.PayloadHash, result, current.Reward);
        }

        return Task.CompletedTask;
    }

    public Task MarkRespawnCompletedAsync(
        MonsterRespawnPlan plan,
        MonsterRespawnResult result,
        MonsterCombatRuntimeState? activeState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _respawns[plan.RespawnId] = (plan with { State = result.State }, result);
        if (activeState is not null)
        {
            _monsters[activeState.RuntimeEntityId] = activeState;
        }

        return Task.CompletedTask;
    }
}

public sealed class CombatRuntimeRecoveryService
{
    private readonly ICombatMutationStore _persistence;
    private readonly MonsterCombatRuntimeRegistry _monsters;
    private readonly PlayerCombatRuntimeRegistry _players;

    public CombatRuntimeRecoveryService(
        ICombatMutationStore persistence,
        MonsterCombatRuntimeRegistry monsters,
        PlayerCombatRuntimeRegistry players)
    {
        _persistence = persistence;
        _monsters = monsters;
        _players = players;
    }

    public void DisconnectPlayer(long playerRuntimeEntityId) =>
        _players.Remove(playerRuntimeEntityId);

    public void ReconnectPlayer(PlayerCombatRuntimeState state) =>
        _players.Register(state);

    public async Task<OperationResult<MonsterCombatRuntimeState>> RestoreMonsterAsync(
        long monsterRuntimeEntityId,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _persistence.LoadMonsterAsync(monsterRuntimeEntityId, cancellationToken);
            _monsters.Replace(state);
            return OperationResult<MonsterCombatRuntimeState>.Success(state);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return OperationResult<MonsterCombatRuntimeState>.Failure(
                "combat.recovery_failed",
                exception.Message,
                monsterRuntimeEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

public interface ICombatRewardPolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    CombatRewardPlan Resolve(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        long expectedInventoryVersion,
        DateTimeOffset now);
}

public sealed class NoCombatRewardPolicy : ICombatRewardPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.EvidenceBlocked;

    public CombatRewardPlan Resolve(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        long expectedInventoryVersion,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            death.DeathId,
            death.KillerCharacterId,
            death.MonsterTemplateId,
            null,
            [],
            [],
            0,
            $"combat-reward:{death.DeathId:N}",
            PolicyStatus,
            now,
            death.CorrelationId,
            expectedInventoryVersion);
}

public sealed class DeterministicTestCombatRewardPolicy : ICombatRewardPolicy
{
    private readonly IReadOnlyList<CombatItemGrant> _items;
    private readonly IReadOnlyList<CombatCurrencyGrant> _currencies;

    public DeterministicTestCombatRewardPolicy(
        IReadOnlyList<CombatItemGrant>? items = null,
        IReadOnlyList<CombatCurrencyGrant>? currencies = null)
    {
        _items = items ?? [];
        _currencies = currencies ?? [];
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public CombatRewardPlan Resolve(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        long expectedInventoryVersion,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            death.DeathId,
            death.KillerCharacterId,
            death.MonsterTemplateId,
            null,
            _items,
            _currencies,
            0,
            $"combat-reward:{death.DeathId:N}",
            PolicyStatus,
            now,
            death.CorrelationId,
            expectedInventoryVersion);
}

public sealed class CatalogBackedCombatRewardPolicy : ICombatRewardPolicy
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<CombatDropRewardDefinition>> _dropsByMonster;

    public CatalogBackedCombatRewardPolicy(IEnumerable<CombatDropRewardDefinition> drops)
    {
        ArgumentNullException.ThrowIfNull(drops);
        _dropsByMonster = drops
            .Where(drop =>
                drop.MonsterTemplateId > 0 &&
                drop.ItemTemplateId > 0 &&
                drop.MinimumQuantity > 0 &&
                drop.MaximumQuantity >= drop.MinimumQuantity &&
                (drop.IsGuaranteed || drop.DropRate > 0m))
            .GroupBy(drop => drop.MonsterTemplateId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CombatDropRewardDefinition>)group
                    .OrderBy(drop => drop.DropTableId)
                    .ToArray());
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.ContentBacked;

    public CombatRewardPlan Resolve(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        long expectedInventoryVersion,
        DateTimeOffset now)
    {
        _dropsByMonster.TryGetValue(death.MonsterTemplateId, out var drops);
        drops ??= [];
        var selected = drops
            .Where(drop => drop.IsGuaranteed || Roll(death.DeathId, drop.DropTableId) < Math.Clamp(drop.DropRate, 0m, 1m))
            .Select(drop => new CombatItemGrant(drop.ItemTemplateId, Quantity(death.DeathId, drop)))
            .ToArray();
        return new CombatRewardPlan(
            Guid.NewGuid(),
            death.DeathId,
            death.KillerCharacterId,
            death.MonsterTemplateId,
            drops.Count == 1 ? drops[0].DropTableId : null,
            selected,
            [],
            0,
            $"combat-reward:{death.DeathId:N}",
            drops.Count == 0 ? CombatPolicyStatus.EvidenceBlocked : PolicyStatus,
            now,
            death.CorrelationId,
            expectedInventoryVersion);
    }

    private static int Quantity(Guid deathId, CombatDropRewardDefinition drop)
    {
        if (drop.MinimumQuantity == drop.MaximumQuantity)
        {
            return drop.MinimumQuantity;
        }

        var span = checked(drop.MaximumQuantity - drop.MinimumQuantity + 1);
        return checked(drop.MinimumQuantity + (int)(RollBasis(deathId, drop.DropTableId, "quantity") % (uint)span));
    }

    private static decimal Roll(Guid deathId, int dropTableId) =>
        RollBasis(deathId, dropTableId, "rate") / (decimal)uint.MaxValue;

    private static uint RollBasis(Guid deathId, int dropTableId, string purpose)
    {
        Span<byte> bytes = stackalloc byte[28];
        deathId.TryWriteBytes(bytes[..16]);
        BitConverter.TryWriteBytes(bytes.Slice(16, sizeof(int)), dropTableId);
        Encoding.ASCII.GetBytes(purpose, bytes.Slice(20, 8));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return BitConverter.ToUInt32(hash[..sizeof(uint)]);
    }
}

public interface ICombatRewardCoordinator
{
    Task<CombatRewardResult> ExecuteAsync(
        CombatRewardPlan plan,
        string sessionId,
        long? accountId,
        CancellationToken cancellationToken);
}

public sealed class CombatRewardCoordinator : ICombatRewardCoordinator
{
    private readonly IInventoryTransactionCoordinator _inventory;
    private readonly CombatFailureInjection _failureInjection;

    public CombatRewardCoordinator(
        IInventoryTransactionCoordinator inventory,
        CombatFailureInjection? failureInjection = null)
    {
        _inventory = inventory;
        _failureInjection = failureInjection ?? new CombatFailureInjection();
    }

    public async Task<CombatRewardResult> ExecuteAsync(
        CombatRewardPlan plan,
        string sessionId,
        long? accountId,
        CancellationToken cancellationToken)
    {
        if (plan.ItemGrants.Count == 0 && plan.CurrencyGrants.Count == 0)
        {
            return new CombatRewardResult(
                CombatRewardResultCode.NoReward,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                "",
                null);
        }

        if (_failureInjection.Point == CombatFailurePoint.RewardTransactionFailure)
        {
            return new CombatRewardResult(
                CombatRewardResultCode.TransactionFailed,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                "combat.reward_transaction_failed",
                null);
        }

        if (plan.CurrencyGrants.Any(value =>
                !string.Equals(value.CurrencyType, "Gold", StringComparison.OrdinalIgnoreCase) ||
                value.Amount < 0))
        {
            return new CombatRewardResult(
                CombatRewardResultCode.TransactionFailed,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                "combat.reward_currency_invalid",
                null);
        }

        long currency;
        try
        {
            currency = plan.CurrencyGrants.Aggregate(
                0L,
                (current, value) => checked(current + value.Amount));
        }
        catch (OverflowException)
        {
            return new CombatRewardResult(
                CombatRewardResultCode.TransactionFailed,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                "combat.reward_currency_overflow",
                null);
        }

        var currentInventoryVersion = await _inventory.GetCurrentVersionAsync(plan.CharacterId, cancellationToken);
        if (!currentInventoryVersion.Succeeded)
        {
            return new CombatRewardResult(
                CombatRewardResultCode.TransactionFailed,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                currentInventoryVersion.Error.Code,
                null);
        }

        var request = new InventoryTransactionRequest(
            plan.RewardPlanId,
            plan.IdempotencyKey,
            plan.CharacterId,
            sessionId,
            null,
            InventoryOperationType.SystemGrant,
            currentInventoryVersion.Value,
            "CombatReward",
            plan.ItemGrants.Select(value =>
                new InventoryMutationRequest(ItemTemplateId: value.ItemTemplateId, Quantity: value.Quantity)).ToArray(),
            currency,
            null,
            plan.CreatedAtUtc,
            InventoryMutationAuthorityKind.TrustedServer);
        var result = await _inventory.ExecuteAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return new CombatRewardResult(
                result.Code == InventoryTransactionResultCode.InsufficientCapacity
                    ? CombatRewardResultCode.InventoryFull
                    : CombatRewardResultCode.TransactionFailed,
                plan.RewardPlanId,
                false,
                false,
                0,
                0,
                result.FailureCode,
                result);
        }

        return new CombatRewardResult(
            result.Code == InventoryTransactionResultCode.DuplicateCompleted
                ? CombatRewardResultCode.DuplicateCompleted
                : CombatRewardResultCode.Committed,
            plan.RewardPlanId,
            plan.ItemGrants.Count > 0,
            plan.CurrencyGrants.Count > 0,
            plan.ItemGrants.Count,
            plan.CurrencyGrants.Count,
            "",
            result);
    }
}

public interface IMonsterRespawnPolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    OperationResult<DateTimeOffset> ResolveDueAt(
        MonsterCombatDefinition definition,
        DateTimeOffset diedAtUtc);
}

public sealed class EvidenceBlockedMonsterRespawnPolicy : IMonsterRespawnPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.EvidenceBlocked;

    public OperationResult<DateTimeOffset> ResolveDueAt(
        MonsterCombatDefinition definition,
        DateTimeOffset diedAtUtc) =>
        OperationResult<DateTimeOffset>.Failure(
            "combat.respawn_policy_blocked",
            "Respawn scheduling is blocked because no accepted policy is configured.");
}

public sealed class ContentBackedMonsterRespawnPolicy : IMonsterRespawnPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.ContentBacked;

    public OperationResult<DateTimeOffset> ResolveDueAt(
        MonsterCombatDefinition definition,
        DateTimeOffset diedAtUtc) =>
        definition.RespawnTime > TimeSpan.Zero
            ? OperationResult<DateTimeOffset>.Success(diedAtUtc.Add(definition.RespawnTime))
            : OperationResult<DateTimeOffset>.Failure(
                "combat.respawn_policy_blocked",
                "Content does not define a positive respawn interval.");
}

public sealed class DeterministicTestMonsterRespawnPolicy : IMonsterRespawnPolicy
{
    private readonly TimeSpan _delay;

    public DeterministicTestMonsterRespawnPolicy(TimeSpan delay)
    {
        _delay = delay;
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public OperationResult<DateTimeOffset> ResolveDueAt(
        MonsterCombatDefinition definition,
        DateTimeOffset diedAtUtc) =>
        _delay >= TimeSpan.Zero
            ? OperationResult<DateTimeOffset>.Success(diedAtUtc.Add(_delay))
            : OperationResult<DateTimeOffset>.Failure("combat.respawn_delay_invalid", "Test respawn delay cannot be negative.");
}

public interface IMonsterDeathCoordinator
{
    MonsterDeathRecord Create(
        CombatIntent intent,
        MonsterCombatRuntimeState before,
        MonsterCombatRuntimeState after,
        CombatDamagePlan damage,
        CombatPolicyStatus rewardPolicy,
        CombatPolicyStatus respawnPolicy);
}

public sealed class MonsterDeathCoordinator : IMonsterDeathCoordinator
{
    public MonsterDeathRecord Create(
        CombatIntent intent,
        MonsterCombatRuntimeState before,
        MonsterCombatRuntimeState after,
        CombatDamagePlan damage,
        CombatPolicyStatus rewardPolicy,
        CombatPolicyStatus respawnPolicy) =>
        new(
            Guid.NewGuid(),
            intent.CombatIntentId,
            before.RuntimeEntityId,
            before.MonsterTemplateId,
            intent.AttackerRuntimeEntityId,
            intent.CharacterId,
            before.MapId,
            before.SpawnDefinitionId,
            damage.HpBefore,
            damage.FinalDamage,
            intent.CreatedAtUtc,
            rewardPolicy,
            CombatPolicyStatus.EvidenceBlocked,
            respawnPolicy,
            before.RuntimeVersion,
            after.RuntimeVersion,
            intent.CorrelationId);
}

public interface IMonsterRespawnCoordinator
{
    IReadOnlyList<MonsterRespawnPlan> Plans { get; }

    OperationResult<MonsterRespawnPlan> CreatePlan(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        MonsterCombatRuntimeState deadState);

    void CommitSchedule(MonsterRespawnPlan plan, Guid combatIntentId);

    Task<MonsterRespawnResult> ProcessDueAsync(
        MonsterRespawnPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class MonsterRespawnCoordinator : IMonsterRespawnCoordinator
{
    private static long _nextRuntimeEntityId = 20_000_000;
    private readonly WorldRuntime _world;
    private readonly MonsterCombatRuntimeRegistry _registry;
    private readonly ICombatMutationStore _persistence;
    private readonly IMonsterRespawnPolicy _policy;
    private readonly ICombatEventSink _events;
    private readonly CombatFailureInjection _failureInjection;
    private readonly ConcurrentDictionary<Guid, MonsterRespawnPlan> _plans = [];

    public MonsterRespawnCoordinator(
        WorldRuntime world,
        MonsterCombatRuntimeRegistry registry,
        ICombatMutationStore persistence,
        IMonsterRespawnPolicy policy,
        ICombatEventSink events,
        CombatFailureInjection? failureInjection = null)
    {
        _world = world;
        _registry = registry;
        _persistence = persistence;
        _policy = policy;
        _events = events;
        _failureInjection = failureInjection ?? new CombatFailureInjection();
    }

    public IReadOnlyList<MonsterRespawnPlan> Plans => _plans.Values
        .OrderBy(value => value.RespawnDueAtUtc)
        .ToArray();

    public OperationResult<MonsterRespawnPlan> CreatePlan(
        MonsterDeathRecord death,
        MonsterCombatDefinition definition,
        MonsterCombatRuntimeState deadState)
    {
        if (_failureInjection.Point == CombatFailurePoint.RespawnScheduleFailure)
        {
            return OperationResult<MonsterRespawnPlan>.Failure(
                "combat.respawn_schedule_failed",
                "Injected respawn schedule failure.");
        }

        var due = _policy.ResolveDueAt(definition, death.DiedAtUtc);
        if (!due.Succeeded || due.Value == default)
        {
            return OperationResult<MonsterRespawnPlan>.Failure(due.Error.Code, due.Error.Message, due.Error.Source);
        }

        var plan = new MonsterRespawnPlan(
            Guid.NewGuid(),
            death.DeathId,
            definition.MonsterTemplateId,
            definition.SpawnDefinitionId,
            definition.SpawnGroupId,
            deadState.WorldInstanceId,
            definition.MapId,
            definition.RawSpawnPosition,
            definition.Direction,
            due.Value,
            _policy.PolicyStatus,
            MonsterRespawnState.Scheduled,
            death.DiedAtUtc,
            death.CorrelationId,
            deadState.RuntimeEntityId);
        return OperationResult<MonsterRespawnPlan>.Success(plan);
    }

    public void CommitSchedule(MonsterRespawnPlan plan, Guid combatIntentId)
    {
        if (!_plans.TryAdd(plan.RespawnId, plan))
        {
            return;
        }

        _events.Publish(CombatEvent.Create(
            CombatEventKind.MonsterRespawnScheduled,
            combatIntentId,
            plan.PreviousRuntimeEntityId,
            plan.PreviousRuntimeEntityId,
            plan.MapId,
            plan.RespawnId.ToString("N"),
            "",
            plan.CorrelationId));
    }

    public async Task<MonsterRespawnResult> ProcessDueAsync(
        MonsterRespawnPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (now < plan.RespawnDueAtUtc)
        {
            return new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.Scheduled, null, "combat.respawn_not_due", now);
        }

        if (_failureInjection.Point == CombatFailurePoint.RespawnTargetMapMissing)
        {
            return await Complete(
                plan,
                new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.TargetMapUnavailable, null, "combat.respawn_map_missing", now),
                null,
                cancellationToken);
        }

        var mapResult = _world.GetMap(plan.MapId);
        if (!mapResult.Succeeded || mapResult.Value is null)
        {
            return await Complete(
                plan,
                new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.TargetMapUnavailable, null, "combat.respawn_map_missing", now),
                null,
                cancellationToken);
        }

        if (_failureInjection.Point == CombatFailurePoint.RespawnRegistryConflict)
        {
            return await Complete(
                plan,
                new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.RegistryConflict, null, "combat.respawn_registry_conflict", now),
                null,
                cancellationToken);
        }

        var definitionResult = _registry.GetDefinition(plan.SpawnDefinitionId);
        if (!definitionResult.Succeeded || definitionResult.Value is null)
        {
            return await Complete(
                plan,
                new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.Faulted, null, "combat.respawn_definition_missing", now),
                null,
                cancellationToken);
        }

        var map = mapResult.Value;
        if (!string.Equals(map.WorldInstanceId, plan.WorldInstanceId, StringComparison.Ordinal))
        {
            return await Complete(
                plan,
                new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.TargetMapUnavailable, null, "combat.respawn_instance_mismatch", now),
                null,
                cancellationToken);
        }

        var definition = definitionResult.Value;
        map.Objects.Remove(plan.PreviousRuntimeEntityId);
        map.EntityRegistry.Remove(plan.PreviousRuntimeEntityId);
        _registry.Remove(plan.PreviousRuntimeEntityId);

        _events.Publish(CombatEvent.Create(
            CombatEventKind.MonsterRespawning,
            Guid.Empty,
            plan.PreviousRuntimeEntityId,
            plan.PreviousRuntimeEntityId,
            plan.MapId,
            MonsterRespawnState.Respawning.ToString(),
            "",
            plan.CorrelationId));
        var runtimeEntityId = AllocateRuntimeEntityId();
        var monsterObject = new MonsterObject(
            RuntimeObjectIds.Entity(
                runtimeEntityId,
                RuntimeObjectKind.Monster,
                plan.MapId,
                plan.MonsterTemplateId,
                $"CombatRespawn:{plan.RespawnId:N}"),
            new MonsterState(
                plan.SpawnDefinitionId,
                plan.MonsterTemplateId,
                $"Monster {plan.MonsterTemplateId}",
                plan.MapId,
                plan.RawSpawnPosition,
                plan.Direction,
                definition.RespawnTime,
                0,
                1,
                "CombatRespawn",
                "Active",
                24,
                true,
                definition.RawMetadata,
                definition.ContentVersion,
                definition.Level,
                definition.MaximumHp,
                definition.MaximumHp,
                definition.MaximumHp > 0 ? "ContentBacked" : "EvidenceBlocked",
                null,
                null,
                "EvidenceBlocked",
                definition.AttackPower,
                definition.Defense,
                definition.StatPolicyStatus.ToString()));
        var combatState = new MonsterCombatRuntimeState(
            runtimeEntityId,
            plan.MonsterTemplateId,
            plan.SpawnDefinitionId,
            plan.SpawnGroupId,
            plan.WorldInstanceId,
            plan.MapId,
            plan.RawSpawnPosition,
            plan.Direction,
            MonsterLifecycleState.Active,
            MonsterCombatStateKind.Idle,
            definition.Level,
            definition.MaximumHp,
            definition.MaximumHp,
            definition.AttackPower,
            definition.Defense,
            null,
            null,
            "EvidenceBlocked",
            "None",
            null,
            0,
            "Respawned",
            now,
            null,
            null,
            null,
            definition.StatPolicyStatus,
            definition.ContentVersion,
            definition.RawMetadata,
            definition.MaximumMp,
            definition.MaximumMp,
            definition.MagicAttackPower,
            definition.MagicDefense,
            definition.Metal,
            definition.Wood,
            definition.Water,
            definition.Fire,
            definition.Earth);
        map.Objects.Add(monsterObject);
        map.EntityRegistry.Add(new RuntimeEntity(
            runtimeEntityId,
            WorldRuntimeEntityType.Monster,
            plan.MonsterTemplateId,
            plan.MapId,
            plan.RawSpawnPosition,
            plan.Direction,
            "Active",
            24,
            "Respawned"));
        _registry.Register(combatState, definition);
        MapSession[] activeSessions;
        lock (map.ActiveSessions)
        {
            activeSessions = map.ActiveSessions.ToArray();
        }

        foreach (var activeSession in activeSessions)
        {
            var observer = map.Objects.Get(activeSession.PlayerRuntimeEntityId);
            if (observer.Succeeded && observer.Value is PlayerObject player)
            {
                map.Replication.RecalculateVisibility(
                    activeSession,
                    player.State.Position,
                    map.Objects.ActiveObjects,
                    player.State.VisibilityRadius,
                    now);
            }
        }

        map.Replication.RecordSpawn(
            monsterObject,
            OfficialSerializerStatus.SerializerBlockedByEvidence,
            "Monster respawn semantic spawn; serializer evidence blocked.",
            now);
        _events.Publish(CombatEvent.Create(
            CombatEventKind.MonsterRespawned,
            Guid.Empty,
            runtimeEntityId,
            runtimeEntityId,
            plan.MapId,
            MonsterRespawnState.Active.ToString(),
            "",
            plan.CorrelationId));
        return await Complete(
            plan,
            new MonsterRespawnResult(plan.RespawnId, MonsterRespawnState.Active, runtimeEntityId, "", now),
            combatState,
            cancellationToken);
    }

    private async Task<MonsterRespawnResult> Complete(
        MonsterRespawnPlan plan,
        MonsterRespawnResult result,
        MonsterCombatRuntimeState? activeState,
        CancellationToken cancellationToken)
    {
        _plans[plan.RespawnId] = plan with { State = result.State };
        await _persistence.MarkRespawnCompletedAsync(plan, result, activeState, cancellationToken);
        return result;
    }

    private long AllocateRuntimeEntityId()
    {
        while (true)
        {
            var candidate = Interlocked.Increment(ref _nextRuntimeEntityId);
            var exists = _world.ActiveMaps.Values.Any(map =>
                map.Objects.Get(candidate).Succeeded ||
                map.EntityRegistry.Get(candidate).Succeeded);
            if (!exists)
            {
                return candidate;
            }
        }
    }
}

public sealed record CombatEvent(
    Guid EventId,
    CombatEventKind Kind,
    Guid CombatIntentId,
    long AttackerRuntimeEntityId,
    long TargetRuntimeEntityId,
    int MapId,
    string State,
    string FailureCode,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId)
{
    public static CombatEvent Create(
        CombatEventKind kind,
        Guid intentId,
        long attackerId,
        long targetId,
        int mapId,
        string state,
        string failureCode,
        string correlationId) =>
        new(Guid.NewGuid(), kind, intentId, attackerId, targetId, mapId, state, failureCode, DateTimeOffset.UtcNow, correlationId);
}

public interface ICombatEventSink
{
    IReadOnlyList<CombatEvent> Events { get; }

    void Publish(CombatEvent combatEvent);
}

public sealed class InMemoryCombatEventSink : ICombatEventSink
{
    private readonly ConcurrentQueue<CombatEvent> _events = [];

    public IReadOnlyList<CombatEvent> Events => _events.ToArray();

    public void Publish(CombatEvent combatEvent) => _events.Enqueue(combatEvent);
}

public sealed record CombatAuditRecord(
    Guid AuditId,
    Guid CombatIntentId,
    Guid? DamagePlanId,
    Guid? DeathId,
    Guid? RewardPlanId,
    Guid? RespawnId,
    string IdempotencySafeId,
    string CorrelationId,
    string SessionId,
    long CharacterId,
    long AttackerRuntimeEntityId,
    long TargetRuntimeEntityId,
    int AttackerTemplateId,
    int TargetTemplateId,
    CombatActionType ActionType,
    int MapId,
    long AttackerVersionBefore,
    long AttackerVersionAfter,
    long TargetVersionBefore,
    long TargetVersionAfter,
    long HpBefore,
    long Damage,
    long HpAfter,
    CombatResultCode Result,
    string FailureCode,
    bool DeathCommitted,
    bool RewardCommitted,
    bool RespawnScheduled,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public interface ICombatAuditLedger
{
    IReadOnlyList<CombatAuditRecord> Records { get; }

    void Append(CombatAuditRecord record);
}

public sealed class InMemoryCombatAuditLedger : ICombatAuditLedger
{
    private readonly ConcurrentQueue<CombatAuditRecord> _records = [];

    public IReadOnlyList<CombatAuditRecord> Records => _records.ToArray();

    public void Append(CombatAuditRecord record) => _records.Enqueue(record);
}

public interface ICombatCooldownStore
{
    bool IsAllowed(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now);

    void Record(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now);
}

public sealed class BaselineCombatCooldownStore : ICombatCooldownStore
{
    public bool IsAllowed(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now) => true;

    public void Record(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now)
    {
    }
}

public sealed class DeterministicTestCombatCooldownStore : ICombatCooldownStore
{
    private readonly TimeSpan _interval;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = [];

    public DeterministicTestCombatCooldownStore(TimeSpan interval)
    {
        _interval = interval;
    }

    public bool IsAllowed(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now) =>
        !_last.TryGetValue(Key(attackerRuntimeEntityId, targetRuntimeEntityId), out var last) ||
        now - last >= _interval;

    public void Record(long attackerRuntimeEntityId, long targetRuntimeEntityId, DateTimeOffset now) =>
        _last[Key(attackerRuntimeEntityId, targetRuntimeEntityId)] = now;

    private static string Key(long attacker, long target) => $"{attacker}\u001f{target}";
}

public interface ICombatCoordinator
{
    Task<CombatResult> ExecuteAsync(CombatIntent intent, CancellationToken cancellationToken);
}

public sealed class CombatCoordinator : ICombatCoordinator
{
    private readonly ICombatTargetResolver _targets;
    private readonly ICombatEligibilityPolicy _eligibility;
    private readonly ICombatStatProvider _stats;
    private readonly ICombatDamagePolicy _damage;
    private readonly MonsterCombatRuntimeRegistry _monsters;
    private readonly PlayerCombatRuntimeRegistry _players;
    private readonly ICombatMutationStore _persistence;
    private readonly IMonsterDeathCoordinator _deaths;
    private readonly ICombatRewardPolicy _rewardPolicy;
    private readonly ICombatRewardCoordinator _rewards;
    private readonly IMonsterRespawnCoordinator _respawns;
    private readonly ICombatCooldownStore _cooldown;
    private readonly ICombatEventSink _events;
    private readonly ICombatAuditLedger _audit;
    private readonly CombatFailureInjection _failureInjection;
    private readonly IWorldBattleReservationRegistry? _battleReservations;
    private readonly AsyncKeyedLock<long> _entityLocks = new();

    public CombatCoordinator(
        ICombatTargetResolver targets,
        ICombatEligibilityPolicy eligibility,
        ICombatStatProvider stats,
        ICombatDamagePolicy damage,
        MonsterCombatRuntimeRegistry monsters,
        PlayerCombatRuntimeRegistry players,
        ICombatMutationStore persistence,
        IMonsterDeathCoordinator deaths,
        ICombatRewardPolicy rewardPolicy,
        ICombatRewardCoordinator rewards,
        IMonsterRespawnCoordinator respawns,
        ICombatCooldownStore cooldown,
        ICombatEventSink events,
        ICombatAuditLedger audit,
        CombatFailureInjection? failureInjection = null,
        IWorldBattleReservationRegistry? battleReservations = null)
    {
        _targets = targets;
        _eligibility = eligibility;
        _stats = stats;
        _damage = damage;
        _monsters = monsters;
        _players = players;
        _persistence = persistence;
        _deaths = deaths;
        _rewardPolicy = rewardPolicy;
        _rewards = rewards;
        _respawns = respawns;
        _cooldown = cooldown;
        _events = events;
        _audit = audit;
        _failureInjection = failureInjection ?? new CombatFailureInjection();
        _battleReservations = battleReservations;
    }

    public async Task<CombatResult> ExecuteAsync(
        CombatIntent intent,
        CancellationToken cancellationToken)
    {
        if (_battleReservations?.IsReserved(intent.CharacterId) == true &&
            !string.Equals(intent.Source, "TurnBasedBattleRuntime", StringComparison.Ordinal))
        {
            return RejectAndAudit(
                intent,
                null,
                CombatResultCode.Rejected,
                "combat.player_in_battle");
        }

        var payloadHash = CombatHash.Intent(intent);
        var replay = await _persistence.FindCompletedAsync(intent.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return RejectAndAudit(intent, null, CombatResultCode.ReplayConflict, "combat.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return await ReplayAndAuditAsync(intent, replay, cancellationToken);
        }

        var lockIds = new[] { intent.AttackerRuntimeEntityId, intent.TargetRuntimeEntityId }
            .Distinct()
            .Order()
            .ToArray();
        var leases = new List<IDisposable>(lockIds.Length);
        try
        {
            foreach (var lockId in lockIds)
            {
                leases.Add(await _entityLocks.AcquireAsync(lockId, cancellationToken));
            }

            replay = await _persistence.FindCompletedAsync(intent.IdempotencyKey, payloadHash, cancellationToken);
            if (replay is { Found: true, PayloadMatches: false })
            {
                return RejectAndAudit(intent, null, CombatResultCode.ReplayConflict, "combat.replay_conflict");
            }

            if (replay is { Found: true, Result: not null })
            {
                return await ReplayAndAuditAsync(intent, replay, cancellationToken);
            }

            return await ExecuteLockedAsync(intent, payloadHash, cancellationToken);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }
        }
    }

    private async Task<CombatResult> ExecuteLockedAsync(
        CombatIntent intent,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        Publish(CombatEventKind.CombatIntentReceived, intent, 0, "Received", "");
        var resolved = _targets.Resolve(intent);
        if (!resolved.Succeeded || resolved.Value is null)
        {
            var rejected = ResolutionFailure(intent, resolved.Error.Code);
            Publish(CombatEventKind.CombatIntentRejected, intent, 0, "Rejected", rejected.FailureCode);
            AppendAudit(intent, null, rejected);
            return rejected;
        }

        var target = resolved.Value;
        if (_failureInjection.Point == CombatFailurePoint.TargetDestroyedDuringValidation)
        {
            return RejectAndAudit(
                intent,
                target,
                CombatResultCode.TargetInactive,
                "combat.target_destroyed_during_validation");
        }

        var eligibility = _eligibility.Evaluate(intent, target);
        if (!eligibility.Allowed)
        {
            var rejected = CombatResult.Reject(
                intent,
                eligibility.ResultCode,
                eligibility.FailureCode,
                _damage.PolicyStatus,
                eligibility.RangePolicyStatus);
            return RejectAndAudit(intent, target, rejected);
        }

        Publish(CombatEventKind.CombatStarted, intent, 0, "Started", "");
        if (target.TargetMonsterState is not null)
        {
            Publish(CombatEventKind.MonsterEngaged, intent, target.TargetMonsterState.CurrentHp, "Engaged", "");
        }

        if (!_cooldown.IsAllowed(intent.AttackerRuntimeEntityId, intent.TargetRuntimeEntityId, intent.CreatedAtUtc))
        {
            return RejectAndAudit(intent, target, CombatResultCode.CooldownActive, "combat.cooldown_active");
        }

        var attackerStats = target.AttackerObject is PlayerObject
            ? _stats.GetPlayer(intent.AttackerRuntimeEntityId)
            : _stats.GetMonster(intent.AttackerRuntimeEntityId);
        var targetStats = target.TargetObject is PlayerObject
            ? _stats.GetPlayer(intent.TargetRuntimeEntityId)
            : _stats.GetMonster(intent.TargetRuntimeEntityId);
        if (!attackerStats.Succeeded || attackerStats.Value is null ||
            !targetStats.Succeeded || targetStats.Value is null)
        {
            return RejectAndAudit(intent, target, CombatResultCode.Rejected, "combat.stats_invalid");
        }

        if (attackerStats.Value.RuntimeVersion != intent.ExpectedAttackerVersion ||
            targetStats.Value.RuntimeVersion != intent.ExpectedTargetVersion)
        {
            return RejectAndAudit(intent, target, CombatResultCode.VersionConflict, "combat.version_conflict");
        }

        var computation = _failureInjection.Point == CombatFailurePoint.DamageOverflow
            ? new CombatDamageComputation(false, 0, 0, CombatDamageType.Unknown, false, false, _damage.PolicyStatus, "combat.damage_overflow")
            : _damage.Compute(attackerStats.Value, targetStats.Value);
        if (!computation.Succeeded || computation.FinalDamage <= 0)
        {
            return RejectAndAudit(
                intent,
                target,
                CombatResultCode.DamagePolicyBlocked,
                computation.FailureCode,
                computation.PolicyStatus,
                eligibility.RangePolicyStatus);
        }

        long hpAfter;
        try
        {
            hpAfter = Math.Max(0, checked(targetStats.Value.CurrentHp - computation.FinalDamage));
        }
        catch (OverflowException)
        {
            return RejectAndAudit(intent, target, CombatResultCode.DamagePolicyBlocked, "combat.damage_overflow");
        }

        var plan = new CombatDamagePlan(
            Guid.NewGuid(),
            intent.CombatIntentId,
            intent.AttackerRuntimeEntityId,
            intent.TargetRuntimeEntityId,
            intent.CombatActionType,
            attackerStats.Value,
            targetStats.Value,
            computation.BaseDamage,
            computation.FinalDamage,
            computation.DamageType,
            computation.IsCritical,
            computation.IsBlocked,
            targetStats.Value.CurrentHp,
            hpAfter,
            attackerStats.Value.RuntimeVersion,
            targetStats.Value.RuntimeVersion,
            computation.PolicyStatus,
            intent.CreatedAtUtc,
            intent.CorrelationId);
        Publish(CombatEventKind.DamagePlanned, intent, targetStats.Value.CurrentHp, "Planned", "");

        if (target.TargetMonsterState is null)
        {
            return RejectAndAudit(intent, target, CombatResultCode.UnsupportedAction, "combat.player_hp_mutation_deferred");
        }

        var before = target.TargetMonsterState;
        var lethal = hpAfter == 0;
        var after = before with
        {
            CurrentHp = hpAfter,
            LifecycleState = lethal ? MonsterLifecycleState.Inactive : MonsterLifecycleState.Active,
            CombatState = lethal ? MonsterCombatStateKind.Dead : MonsterCombatStateKind.Hit,
            CurrentTargetRuntimeEntityId = intent.AttackerRuntimeEntityId,
            RuntimeVersion = checked(before.RuntimeVersion + 1),
            DirtyFlags = lethal ? "HP|Combat|Lifecycle|Death" : "HP|Combat",
            LastCombatAtUtc = intent.CreatedAtUtc,
            DiedAtUtc = lethal ? intent.CreatedAtUtc : null
        };
        MonsterDeathRecord? death = null;
        CombatRewardPlan? rewardPlan = null;
        MonsterRespawnPlan? respawnPlan = null;
        var definition = _monsters.GetDefinition(before.SpawnDefinitionId).Value;
        if (lethal && definition is not null)
        {
            death = _deaths.Create(
                intent,
                before,
                after,
                plan,
                _rewardPolicy.PolicyStatus,
                definition.RespawnPolicyStatus);
            rewardPlan = _rewardPolicy.Resolve(death, definition, 0, intent.CreatedAtUtc);
            var scheduled = _respawns.CreatePlan(death, definition, after);
            if (scheduled.Succeeded && scheduled.Value is not null)
            {
                respawnPlan = scheduled.Value;
                after = after with
                {
                    CombatState = MonsterCombatStateKind.RespawnPending,
                    RespawnDueAtUtc = respawnPlan.RespawnDueAtUtc,
                    DirtyFlags = after.DirtyFlags + "|Respawn"
                };
            }
        }

        var provisional = new CombatResult(
            CombatResultCode.Success,
            intent.CombatIntentId,
            CombatHash.SafeId(intent.IdempotencyKey),
            plan,
            "",
            false,
            death is not null,
            death?.DeathId,
            rewardPlan is null || rewardPlan.ItemGrants.Count == 0 && rewardPlan.CurrencyGrants.Count == 0,
            rewardPlan?.RewardPlanId,
            respawnPlan is not null,
            respawnPlan?.RespawnId,
            attackerStats.Value.RuntimeVersion,
            attackerStats.Value.RuntimeVersion,
            before.RuntimeVersion,
            after.RuntimeVersion,
            plan.HpBefore,
            plan.FinalDamage,
            plan.HpAfter,
            plan.PolicyStatus,
            eligibility.RangePolicyStatus,
            null,
            []);
        var audit = BuildAudit(intent, target, provisional);
        if (_failureInjection.Point == CombatFailurePoint.PersistenceFailureBeforeHpMutation)
        {
            return RejectAndAudit(intent, target, provisional with
            {
                Code = CombatResultCode.PersistenceFailure,
                FailureCode = "combat.persistence_failure_before_hp",
                TargetVersionAfter = before.RuntimeVersion,
                HpAfter = before.CurrentHp,
                DeathCommitted = false,
                RespawnScheduled = false,
                RespawnId = null
            });
        }

        var commit = await _persistence.CommitAsync(
            new CombatPersistenceCommit(intent, payloadHash, before, after, provisional, death, rewardPlan, respawnPlan, audit),
            cancellationToken);
        if (!commit.Succeeded)
        {
            return RejectAndAudit(intent, target, provisional with
            {
                Code = commit.Error.Code == "combat.version_conflict"
                    ? CombatResultCode.VersionConflict
                    : CombatResultCode.PersistenceFailure,
                FailureCode = commit.Error.Code,
                TargetVersionAfter = before.RuntimeVersion,
                HpAfter = before.CurrentHp,
                DeathCommitted = false,
                DeathId = null,
                RewardCommitted = false,
                RewardPlanId = null,
                RespawnScheduled = false,
                RespawnId = null
            });
        }

        if (respawnPlan is not null)
        {
            _respawns.CommitSchedule(respawnPlan, intent.CombatIntentId);
        }

        if (_failureInjection.Point == CombatFailurePoint.RuntimeFailureAfterDatabaseCommit)
        {
            try
            {
                if (_failureInjection.RecoveryFailureEnabled)
                {
                    throw new InvalidOperationException("Injected combat recovery failure.");
                }

                var recovered = await _persistence.LoadMonsterAsync(before.RuntimeEntityId, cancellationToken);
                _monsters.Replace(recovered);
                UpdateRuntimeMonster(target.TargetMap, target.TargetObject, recovered, recovered.CurrentHp == 0, intent.CreatedAtUtc);
                var recovery = provisional with
                {
                    Code = CombatResultCode.RuntimeMutationFailure,
                    FailureCode = "combat.runtime_commit_failed_reloaded"
                };
                Publish(CombatEventKind.CombatRecoveryRequired, intent, recovered.CurrentHp, "Recovered", recovery.FailureCode);
                AppendAudit(intent, target, recovery);
                await _persistence.UpdateResultAsync(intent.IdempotencyKey, recovery, cancellationToken);
                return recovery;
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                var recovery = provisional with
                {
                    Code = CombatResultCode.RuntimeMutationFailure,
                    FailureCode = "combat.recovery_failed"
                };
                Publish(CombatEventKind.CombatRecoveryRequired, intent, before.CurrentHp, "RecoveryRequired", recovery.FailureCode);
                AppendAudit(intent, target, recovery);
                await _persistence.UpdateResultAsync(intent.IdempotencyKey, recovery, cancellationToken);
                return recovery;
            }
        }

        _monsters.Replace(after);
        UpdateRuntimeMonster(target.TargetMap, target.TargetObject, after, lethal, intent.CreatedAtUtc);
        _cooldown.Record(intent.AttackerRuntimeEntityId, intent.TargetRuntimeEntityId, intent.CreatedAtUtc);
        Publish(CombatEventKind.DamageApplied, intent, hpAfter, "Applied", "");
        Publish(CombatEventKind.HpChanged, intent, hpAfter, "Applied", "");
        Publish(CombatEventKind.MonsterHit, intent, hpAfter, "Hit", "");
        if (lethal)
        {
            Publish(CombatEventKind.MonsterLethalDamage, intent, hpAfter, "LethalDamagePending", "");
            Publish(CombatEventKind.MonsterDied, intent, hpAfter, "Dead", "");
            Publish(CombatEventKind.MonsterDespawnPending, intent, hpAfter, "DespawnPending", "");
        }

        var final = provisional;
        if (rewardPlan is not null)
        {
            final = await ResolveRewardAsync(intent, provisional, rewardPlan, target.SessionBinding.Session.AccountId, cancellationToken);
        }

        if (!AppendAudit(intent, target, final))
        {
            final = final with
            {
                Code = CombatResultCode.InternalFailure,
                FailureCode = "combat.audit_failed"
            };
            Publish(CombatEventKind.CombatRecoveryRequired, intent, final.HpAfter, "RecoveryRequired", final.FailureCode);
        }

        await _persistence.UpdateResultAsync(intent.IdempotencyKey, final, cancellationToken);
        Publish(CombatEventKind.CombatCompleted, intent, final.HpAfter, "Completed", final.FailureCode);
        return final;
    }

    private async Task<CombatResult> ReplayAndAuditAsync(
        CombatIntent intent,
        CombatReplayLookup replay,
        CancellationToken cancellationToken)
    {
        var result = await ReplayAsync(intent, replay, cancellationToken);
        var resolved = _targets.Resolve(intent);
        AppendAudit(intent, resolved.Succeeded ? resolved.Value : null, result);
        return result;
    }

    private async Task<CombatResult> ReplayAsync(
        CombatIntent intent,
        CombatReplayLookup replay,
        CancellationToken cancellationToken)
    {
        var result = replay.Result!;
        if (replay.PendingRewardPlan is not null)
        {
            result = await ResolveRewardAsync(
                intent,
                result,
                replay.PendingRewardPlan,
                null,
                cancellationToken);
            await _persistence.UpdateResultAsync(intent.IdempotencyKey, result, cancellationToken);
        }

        return result with
        {
            Code = result.Succeeded ? CombatResultCode.DuplicateCompleted : result.Code,
            IsDuplicate = true
        };
    }

    private async Task<CombatResult> ResolveRewardAsync(
        CombatIntent intent,
        CombatResult result,
        CombatRewardPlan plan,
        long? accountId,
        CancellationToken cancellationToken)
    {
        Publish(CombatEventKind.RewardResolutionStarted, intent, result.HpAfter, "RewardResolving", "");
        var reward = await _rewards.ExecuteAsync(plan, intent.SessionId, accountId, cancellationToken);
        if (!reward.Succeeded)
        {
            Publish(CombatEventKind.RewardFailed, intent, result.HpAfter, "RewardFailed", reward.FailureCode);
            return result with
            {
                Code = CombatResultCode.RewardFailure,
                FailureCode = reward.FailureCode,
                RewardCommitted = false,
                RewardResult = reward
            };
        }

        Publish(CombatEventKind.RewardCommitted, intent, result.HpAfter, "RewardCommitted", "");
        return result with
        {
            RewardCommitted = true,
            RewardResult = reward
        };
    }

    private static void UpdateRuntimeMonster(
        MapRuntime map,
        IRuntimeObject targetObject,
        MonsterCombatRuntimeState state,
        bool lethal,
        DateTimeOffset now)
    {
        if (targetObject is not MonsterObject monster)
        {
            return;
        }

        var updated = monster with
        {
            State = monster.State with
            {
                Lifecycle = lethal ? "Dead" : "Hit"
            }
        };
        map.Objects.Add(updated);
        var entity = map.EntityRegistry.Get(monster.Identity.RuntimeObjectId).Value;
        if (entity is not null)
        {
            map.EntityRegistry.Add(entity with
            {
                State = lethal ? "Dead" : "Hit",
                DirtyFlags = state.DirtyFlags
            });
        }

        if (lethal)
        {
            map.Replication.RecordDespawn(
                updated,
                OfficialSerializerStatus.SerializerBlockedByEvidence,
                "Monster death semantic despawn; official protocol evidence blocked.",
                now);
        }
        else
        {
            map.Replication.RecordUpdate(
                updated,
                OfficialSerializerStatus.SerializerBlockedByEvidence,
                "Monster HP semantic update; official protocol evidence blocked.",
                now);
        }
    }

    private CombatResult RejectAndAudit(
        CombatIntent intent,
        ResolvedCombatTarget? target,
        CombatResultCode code,
        string failureCode,
        CombatPolicyStatus damageStatus = CombatPolicyStatus.EvidenceBlocked,
        CombatPolicyStatus rangeStatus = CombatPolicyStatus.EvidenceBlocked) =>
        RejectAndAudit(
            intent,
            target,
            CombatResult.Reject(intent, code, failureCode, damageStatus, rangeStatus));

    private CombatResult RejectAndAudit(
        CombatIntent intent,
        ResolvedCombatTarget? target,
        CombatResult result)
    {
        Publish(CombatEventKind.CombatIntentRejected, intent, result.HpAfter, "Rejected", result.FailureCode);
        AppendAudit(intent, target, result);
        return result;
    }

    private bool AppendAudit(
        CombatIntent intent,
        ResolvedCombatTarget? target,
        CombatResult result)
    {
        if (_failureInjection.Point == CombatFailurePoint.AuditFailure)
        {
            return false;
        }

        _audit.Append(BuildAudit(intent, target, result));
        return true;
    }

    private static CombatAuditRecord BuildAudit(
        CombatIntent intent,
        ResolvedCombatTarget? target,
        CombatResult result) =>
        new(
            Guid.NewGuid(),
            intent.CombatIntentId,
            result.DamagePlan?.DamagePlanId,
            result.DeathId,
            result.RewardPlanId,
            result.RespawnId,
            result.IdempotencySafeId,
            intent.CorrelationId,
            intent.SessionId,
            intent.CharacterId,
            intent.AttackerRuntimeEntityId,
            intent.TargetRuntimeEntityId,
            target?.AttackerObject.Identity.TemplateId ?? 0,
            target?.TargetObject.Identity.TemplateId ?? intent.TargetTemplateId ?? 0,
            intent.CombatActionType,
            target?.TargetObject.Identity.MapId ?? 0,
            result.AttackerVersionBefore,
            result.AttackerVersionAfter,
            result.TargetVersionBefore,
            result.TargetVersionAfter,
            result.HpBefore,
            result.Damage,
            result.HpAfter,
            result.Code,
            result.FailureCode,
            result.DeathCommitted,
            result.RewardCommitted,
            result.RespawnScheduled,
            result.DamagePolicyStatus,
            intent.CreatedAtUtc,
            DateTimeOffset.UtcNow);

    private void Publish(
        CombatEventKind kind,
        CombatIntent intent,
        long hp,
        string state,
        string failureCode) =>
        _events.Publish(CombatEvent.Create(
            kind,
            intent.CombatIntentId,
            intent.AttackerRuntimeEntityId,
            intent.TargetRuntimeEntityId,
            0,
            $"{state};Hp={hp}",
            failureCode,
            intent.CorrelationId));

    private static CombatResult ResolutionFailure(CombatIntent intent, string code) =>
        code switch
        {
            "combat.invalid_session" => CombatResult.Reject(intent, CombatResultCode.InvalidSession, code),
            "combat.ownership_mismatch" or "combat.attacker_ownership_mismatch" =>
                CombatResult.Reject(intent, CombatResultCode.OwnershipMismatch, code),
            "combat.attacker_not_found" or "combat.attacker_state_missing" =>
                CombatResult.Reject(intent, CombatResultCode.AttackerNotFound, code),
            "combat.target_not_found" or "combat.target_state_missing" =>
                CombatResult.Reject(intent, CombatResultCode.TargetNotFound, code),
            "combat.attacker_type_unsupported" or "combat.target_type_unsupported" or "combat.monster_attack_not_trusted" =>
                CombatResult.Reject(intent, CombatResultCode.UnsupportedAction, code),
            _ => CombatResult.Reject(intent, CombatResultCode.Rejected, code)
        };
}

public sealed record CombatInspectorQuery(
    long? CharacterId = null,
    long? AttackerRuntimeEntityId = null,
    long? TargetRuntimeEntityId = null,
    int? MonsterTemplateId = null,
    int? MapId = null,
    MonsterCombatStateKind? CombatState = null,
    CombatResultCode? Result = null,
    bool DeadOnly = false,
    bool RespawnPendingOnly = false,
    bool FailedOnly = false,
    bool PendingOnly = false,
    bool EvidenceBlockedOnly = false,
    int Skip = 0,
    int Take = 100);

public sealed record CombatInspectorItem(
    Guid CombatIntentId,
    long CharacterId,
    long AttackerRuntimeEntityId,
    long TargetRuntimeEntityId,
    CombatActionType ActionType,
    string State,
    CombatResultCode Result,
    string FailureCode,
    long Damage,
    long HpBefore,
    long HpAfter,
    CombatPolicyStatus DamagePolicyStatus,
    CombatPolicyStatus RangePolicyStatus,
    bool IsDuplicate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record MonsterCombatInspectorItem(
    long MonsterRuntimeEntityId,
    int MonsterTemplateId,
    int SpawnDefinitionId,
    int MapId,
    MonsterLifecycleState LifecycleState,
    MonsterCombatStateKind CombatState,
    long MaximumHp,
    long CurrentHp,
    long MaximumMp,
    long CurrentMp,
    long AttackPower,
    long Defense,
    long MagicAttackPower,
    long MagicDefense,
    int Metal,
    int Wood,
    int Water,
    int Fire,
    int Earth,
    long? CurrentTargetRuntimeEntityId,
    long RuntimeVersion,
    string DirtyFlags,
    DateTimeOffset? DiedAtUtc,
    DateTimeOffset? RespawnDueAtUtc,
    CombatPolicyStatus StatPolicyStatus,
    string ProtocolStatus);

public sealed record CombatDeathRewardInspectorItem(
    Guid DeathId,
    long KillerCharacterId,
    long MonsterRuntimeEntityId,
    Guid? RewardPlanId,
    CombatPolicyStatus RewardPolicyStatus,
    string RewardResult,
    int? DropTableId,
    int ItemGrantCount,
    int CurrencyGrantCount,
    string FailureCode);

public sealed record CombatRespawnInspectorItem(
    Guid RespawnId,
    int MonsterTemplateId,
    int SpawnDefinitionId,
    int MapId,
    MonsterRespawnState State,
    DateTimeOffset RespawnDueAtUtc,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public sealed record CombatInspectorSnapshot(
    string SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<CombatInspectorItem> Combat,
    IReadOnlyList<MonsterCombatInspectorItem> Monsters,
    IReadOnlyList<CombatDeathRewardInspectorItem> DeathRewards,
    IReadOnlyList<CombatRespawnInspectorItem> Respawns);

public sealed class CombatRuntimeInspector
{
    private readonly ICombatAuditLedger _audit;
    private readonly MonsterCombatRuntimeRegistry _monsters;
    private readonly IMonsterRespawnCoordinator _respawns;
    private readonly InMemoryCombatMutationStore? _inMemoryStore;
    private readonly CombatPolicyStatus _rangeStatus;

    public CombatRuntimeInspector(
        ICombatAuditLedger audit,
        MonsterCombatRuntimeRegistry monsters,
        IMonsterRespawnCoordinator respawns,
        ICombatMutationStore persistence,
        ICombatRangePolicy range)
    {
        _audit = audit;
        _monsters = monsters;
        _respawns = respawns;
        _inMemoryStore = persistence as InMemoryCombatMutationStore;
        _rangeStatus = range.PolicyStatus;
    }

    public CombatInspectorSnapshot Capture(CombatInspectorQuery query, DateTimeOffset now)
    {
        var audit = _audit.Records.AsEnumerable();
        if (query.CharacterId is not null)
        {
            audit = audit.Where(value => value.CharacterId == query.CharacterId.Value);
        }

        if (query.AttackerRuntimeEntityId is not null)
        {
            audit = audit.Where(value => value.AttackerRuntimeEntityId == query.AttackerRuntimeEntityId.Value);
        }

        if (query.TargetRuntimeEntityId is not null)
        {
            audit = audit.Where(value => value.TargetRuntimeEntityId == query.TargetRuntimeEntityId.Value);
        }

        if (query.MapId is not null)
        {
            audit = audit.Where(value => value.MapId == query.MapId.Value);
        }

        if (query.Result is not null)
        {
            audit = audit.Where(value => value.Result == query.Result.Value);
        }

        if (query.FailedOnly)
        {
            audit = audit.Where(value => value.Result is not (CombatResultCode.Success or CombatResultCode.DuplicateCompleted));
        }

        if (query.EvidenceBlockedOnly)
        {
            audit = audit.Where(value =>
                value.PolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
                _rangeStatus == CombatPolicyStatus.EvidenceBlocked);
        }

        var combat = audit
            .OrderByDescending(value => value.CompletedAtUtc)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 500))
            .Select(value => new CombatInspectorItem(
                value.CombatIntentId,
                value.CharacterId,
                value.AttackerRuntimeEntityId,
                value.TargetRuntimeEntityId,
                value.ActionType,
                value.Result is CombatResultCode.Success or CombatResultCode.DuplicateCompleted ? "Completed" : "Rejected",
                value.Result,
                value.FailureCode,
                value.Damage,
                value.HpBefore,
                value.HpAfter,
                value.PolicyStatus,
                _rangeStatus,
                value.Result == CombatResultCode.DuplicateCompleted,
                value.CreatedAtUtc,
                value.CompletedAtUtc))
            .ToArray();

        var monsters = _monsters.Snapshot
            .Where(value => query.MonsterTemplateId is null || value.MonsterTemplateId == query.MonsterTemplateId.Value)
            .Where(value => query.MapId is null || value.MapId == query.MapId.Value)
            .Where(value => query.CombatState is null || value.CombatState == query.CombatState.Value)
            .Where(value => !query.DeadOnly || value.CurrentHp == 0)
            .Where(value => !query.RespawnPendingOnly || value.CombatState == MonsterCombatStateKind.RespawnPending)
            .Select(value => new MonsterCombatInspectorItem(
                value.RuntimeEntityId,
                value.MonsterTemplateId,
                value.SpawnDefinitionId,
                value.MapId,
                value.LifecycleState,
                value.CombatState,
                value.MaximumHp,
                value.CurrentHp,
                value.MaximumMp,
                value.CurrentMp,
                value.AttackPower,
                value.Defense,
                value.MagicAttackPower,
                value.MagicDefense,
                value.Metal,
                value.Wood,
                value.Water,
                value.Fire,
                value.Earth,
                value.CurrentTargetRuntimeEntityId,
                value.RuntimeVersion,
                value.DirtyFlags,
                value.DiedAtUtc,
                value.RespawnDueAtUtc,
                value.StatPolicyStatus,
                "SerializerBlockedByEvidence"))
            .ToArray();

        var deaths = (_inMemoryStore?.Deaths ?? [])
            .Select(value => new CombatDeathRewardInspectorItem(
                value.DeathId,
                value.KillerCharacterId,
                value.MonsterRuntimeEntityId,
                null,
                value.RewardPolicyStatus,
                value.RewardPolicyStatus == CombatPolicyStatus.EvidenceBlocked ? "NoReward" : "TrackedByCombatResult",
                null,
                0,
                0,
                ""))
            .ToArray();
        var respawns = _respawns.Plans
            .Where(value => query.MapId is null || value.MapId == query.MapId.Value)
            .Select(value => new CombatRespawnInspectorItem(
                value.RespawnId,
                value.MonsterTemplateId,
                value.SpawnDefinitionId,
                value.MapId,
                value.State,
                value.RespawnDueAtUtc,
                value.PolicyStatus,
                ""))
            .ToArray();
        return new CombatInspectorSnapshot(
            "combat-runtime-inspector-v1",
            now,
            Array.AsReadOnly(combat),
            Array.AsReadOnly(monsters),
            Array.AsReadOnly(deaths),
            Array.AsReadOnly(respawns));
    }
}

internal static class CombatHash
{
    public static string Intent(CombatIntent intent) =>
        Hash(string.Join(
            "|",
            intent.SessionId,
            intent.CharacterId,
            intent.AttackerRuntimeEntityId,
            intent.TargetRuntimeEntityId,
            intent.TargetTemplateId,
            intent.CombatActionType,
            intent.ExpectedAttackerVersion,
            intent.ExpectedTargetVersion,
            intent.Source));

    public static string SafeId(string idempotencyKey) => Hash(idempotencyKey)[..16];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
