using System.Collections.Concurrent;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class SystemBattleClock : IBattleClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class DeterministicTestBattleClock : IBattleClock
{
    public DeterministicTestBattleClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}

public sealed class InMemoryBattleEventSink : IBattleEventSink
{
    private readonly ConcurrentQueue<BattleEvent> _events = [];

    public IReadOnlyList<BattleEvent> Events => _events.ToArray();

    public void Publish(BattleEvent battleEvent) => _events.Enqueue(battleEvent);
}

public sealed class InMemoryBattleAuditLedger : IBattleAuditLedger
{
    private readonly ConcurrentQueue<BattleAuditRecord> _records = [];

    public IReadOnlyList<BattleAuditRecord> Records => _records.ToArray();

    public void Append(BattleAuditRecord record) => _records.Enqueue(record);
}

public sealed class InMemoryWorldBattleReservationRegistry : IWorldBattleReservationRegistry
{
    private readonly ConcurrentDictionary<long, BattleWorldReservation> _byCharacter = [];

    public BattleFailureInjection FailureInjection { get; } = new();

    public IReadOnlyList<BattleWorldReservation> Snapshot => _byCharacter.Values
        .OrderBy(value => value.CharacterId)
        .ToArray();

    public OperationResult<BattleWorldReservation> Reserve(
        Guid battleInstanceId,
        Guid participantId,
        long characterId,
        long runtimeEntityId,
        DateTimeOffset now)
    {
        if (FailureInjection.Point == BattleFailurePoint.WorldReservation)
        {
            return OperationResult<BattleWorldReservation>.Failure(
                "battle.reservation_failed",
                "Injected world reservation failure.");
        }

        var reservation = new BattleWorldReservation(
            Guid.NewGuid(),
            battleInstanceId,
            participantId,
            characterId,
            runtimeEntityId,
            WorldBattleReservationState.InBattle,
            now,
            null);
        if (!_byCharacter.TryAdd(characterId, reservation))
        {
            return OperationResult<BattleWorldReservation>.Failure(
                "battle.player_already_reserved",
                "Character already belongs to an active mutually-exclusive battle.");
        }

        return OperationResult<BattleWorldReservation>.Success(reservation);
    }

    public OperationResult<BattleWorldReservation> GetByCharacter(long characterId) =>
        _byCharacter.TryGetValue(characterId, out var reservation)
            ? OperationResult<BattleWorldReservation>.Success(reservation)
            : OperationResult<BattleWorldReservation>.Failure(
                "battle.reservation_missing",
                "Character has no active battle reservation.");

    public OperationResult Release(Guid battleInstanceId, long characterId, DateTimeOffset now)
    {
        if (FailureInjection.Point == BattleFailurePoint.ReservationRelease)
        {
            return OperationResult.Failure(
                "battle.reservation_release_failed",
                "Injected world reservation release failure.");
        }

        if (!_byCharacter.TryGetValue(characterId, out var current))
        {
            return OperationResult.Success;
        }

        if (current.BattleInstanceId != battleInstanceId)
        {
            return OperationResult.Failure(
                "battle.reservation_ownership_mismatch",
                "Reservation belongs to another battle.");
        }

        _byCharacter.TryRemove(characterId, out _);
        return OperationResult.Success;
    }

    public bool IsReserved(long characterId) => _byCharacter.ContainsKey(characterId);
}

public sealed class BattleEncounterCatalog : IBattleEncounterResolver
{
    private readonly IReadOnlyDictionary<string, BattleEncounterDefinition> _encounters;
    private readonly IReadOnlyDictionary<int, MonsterCombatDefinition> _monsterTemplates;

    public BattleEncounterCatalog(
        IEnumerable<BattleEncounterDefinition>? encounters,
        IEnumerable<MonsterCombatDefinition> monsterTemplates)
    {
        _encounters = (encounters ?? [])
            .GroupBy(value => value.EncounterDefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        _monsterTemplates = monsterTemplates
            .GroupBy(value => value.MonsterTemplateId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public OperationResult<BattleEncounterDefinition> Resolve(BattleRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EncounterDefinitionId))
        {
            if (!_encounters.TryGetValue(request.EncounterDefinitionId, out var encounter))
            {
                return Failure("battle.encounter_missing", "Encounter definition was not found.");
            }

            if (!encounter.Enabled)
            {
                return Failure("battle.encounter_disabled", "Encounter definition is disabled.");
            }

            if (encounter.MapId != request.SourceMapId)
            {
                return Failure("battle.encounter_map_mismatch", "Encounter does not belong to the player's source map.");
            }

            if (request.Source == BattleRequestSource.OfficialClient &&
                encounter.PolicyStatus is CombatPolicyStatus.TestOnly or CombatPolicyStatus.EvidenceBlocked)
            {
                return Failure("battle.encounter_policy_blocked", "Encounter is not accepted for the official client path.");
            }

            return OperationResult<BattleEncounterDefinition>.Success(encounter);
        }

        if (request.Source != BattleRequestSource.TrustedInternalTest)
        {
            return Failure(
                "battle.encounter_evidence_blocked",
                "A server-side encounter definition is required for the production path.");
        }

        if (request.RequestedOpponentTemplateIds.Count == 0)
        {
            return Failure("battle.encounter_missing", "Internal test encounter requires at least one opponent template.");
        }

        var participants = new List<BattleEncounterParticipantDefinition>();
        for (var index = 0; index < request.RequestedOpponentTemplateIds.Count; index++)
        {
            var templateId = request.RequestedOpponentTemplateIds[index];
            if (!_monsterTemplates.TryGetValue(templateId, out var monster))
            {
                return Failure("battle.monster_template_missing", $"Monster template {templateId} is not available.");
            }

            participants.Add(new BattleEncounterParticipantDefinition(
                $"test-monster:{templateId}:{index}",
                BattleParticipantType.Monster,
                BattleSide.EnemySide,
                100 + index,
                templateId,
                null,
                $"Monster {templateId}",
                monster.Level,
                monster.MaximumHp,
                monster.AttackPower,
                monster.Defense,
                null,
                monster.StatPolicyStatus,
                CombatPolicyStatus.TestOnly,
                monster.RawMetadata));
        }

        return OperationResult<BattleEncounterDefinition>.Success(new BattleEncounterDefinition(
            $"internal-test:{request.BattleRequestId:N}",
            BattleEncounterType.InternalTest,
            request.SourceMapId,
            "test-only",
            BattleCollections.Freeze(participants),
            "DeterministicTestFormation",
            "DeterministicTestBattleRewardPolicy",
            true,
            CombatPolicyStatus.TestOnly,
            "turn-based-test-v1",
            "{\"scope\":\"TestOnly\"}"));
    }

    private static OperationResult<BattleEncounterDefinition> Failure(string code, string message) =>
        OperationResult<BattleEncounterDefinition>.Failure(code, message);
}

public sealed class BattleParticipantFactory : IBattleParticipantFactory
{
    private static long _nextBattleRuntimeEntityId = 50_000_000;

    public BattleFailureInjection FailureInjection { get; } = new();

    public OperationResult<BattleParticipant> CreatePlayer(
        Guid battleInstanceId,
        InteractionSessionBinding binding,
        PlayerCombatRuntimeState playerState,
        int formationSlot,
        CombatPolicyStatus formationPolicyStatus,
        DateTimeOffset now)
    {
        if (FailureInjection.Point == BattleFailurePoint.ParticipantFactory)
        {
            return Failure("battle.participant_factory_failed", "Injected participant factory failure.");
        }

        if (binding.Session.CharacterId is null ||
            binding.Session.CharacterId.Value != playerState.CharacterId ||
            binding.MapSession.PlayerRuntimeEntityId != playerState.RuntimeEntityId)
        {
            return Failure("battle.player_binding_mismatch", "Player runtime and session binding do not match.");
        }

        return OperationResult<BattleParticipant>.Success(new BattleParticipant(
            Guid.NewGuid(),
            battleInstanceId,
            BattleParticipantType.Player,
            BattleSide.PlayerSide,
            formationSlot,
            playerState.CharacterId,
            null,
            playerState.RuntimeEntityId,
            AllocateBattleRuntimeEntityId(),
            binding.Session.SessionId,
            $"Character {playerState.CharacterId}",
            0,
            playerState.MaximumHp,
            playerState.CurrentHp,
            playerState.CurrentHp > 0,
            true,
            true,
            false,
            null,
            BattleParticipantCombatState.WaitingForAction,
            playerState.RuntimeVersion,
            playerState.AttackPower,
            playerState.Defense,
            null,
            playerState.StatPolicyStatus,
            formationPolicyStatus,
            now,
            null,
            "{}"));
    }

    public OperationResult<BattleParticipant> CreateOpponent(
        Guid battleInstanceId,
        BattleEncounterParticipantDefinition definition,
        DateTimeOffset now)
    {
        if (FailureInjection.Point == BattleFailurePoint.ParticipantFactory)
        {
            return Failure("battle.participant_factory_failed", "Injected participant factory failure.");
        }

        if (definition.ParticipantType != BattleParticipantType.Monster ||
            definition.MonsterTemplateId is null ||
            definition.MaximumHp <= 0 ||
            definition.AttackPower < 0 ||
            definition.Defense < 0)
        {
            return Failure("battle.participant_definition_invalid", "Opponent definition violates battle invariants.");
        }

        return OperationResult<BattleParticipant>.Success(new BattleParticipant(
            Guid.NewGuid(),
            battleInstanceId,
            definition.ParticipantType,
            definition.Side,
            definition.FormationSlot,
            null,
            definition.MonsterTemplateId,
            definition.SourceRuntimeEntityId ?? 0,
            AllocateBattleRuntimeEntityId(),
            null,
            definition.DisplayName,
            definition.Level,
            definition.MaximumHp,
            definition.MaximumHp,
            true,
            true,
            true,
            false,
            null,
            BattleParticipantCombatState.WaitingForAction,
            0,
            definition.AttackPower,
            definition.Defense,
            definition.InitiativeCandidate,
            definition.StatPolicyStatus,
            definition.FormationPolicyStatus,
            now,
            null,
            definition.RawMetadata));
    }

    private static OperationResult<BattleParticipant> Failure(string code, string message) =>
        OperationResult<BattleParticipant>.Failure(code, message);

    private static long AllocateBattleRuntimeEntityId() =>
        Interlocked.Increment(ref _nextBattleRuntimeEntityId);
}

public sealed class BattleInstanceFactory : IBattleInstanceFactory
{
    private readonly IBattleParticipantFactory _participants;

    public BattleInstanceFactory(IBattleParticipantFactory participants)
    {
        _participants = participants;
    }

    public BattleFailureInjection FailureInjection { get; } = new();

    public OperationResult<BattleInstance> Create(
        BattleRequest request,
        BattleEncounterDefinition encounter,
        InteractionSessionBinding binding,
        PlayerCombatRuntimeState playerState,
        DateTimeOffset now)
    {
        var battleInstanceId = Guid.NewGuid();
        var player = _participants.CreatePlayer(
            battleInstanceId,
            binding,
            playerState,
            0,
            encounter.EncounterType == BattleEncounterType.InternalTest
                ? CombatPolicyStatus.TestOnly
                : CombatPolicyStatus.EvidenceBlocked,
            now);
        if (!player.Succeeded || player.Value is null)
        {
            return Failure(player.Error.Code, player.Error.Message);
        }

        var all = new List<BattleParticipant> { player.Value };
        foreach (var definition in encounter.ParticipantDefinitions)
        {
            var opponent = _participants.CreateOpponent(battleInstanceId, definition, now);
            if (!opponent.Succeeded || opponent.Value is null)
            {
                return Failure(opponent.Error.Code, opponent.Error.Message);
            }

            all.Add(opponent.Value);
        }

        if (FailureInjection.Point == BattleFailurePoint.DuplicateParticipant ||
            all.Select(value => value.ParticipantId).Distinct().Count() != all.Count ||
            all.Select(value => value.BattleRuntimeEntityId).Distinct().Count() != all.Count)
        {
            return Failure("battle.participant_duplicate", "Battle participant identity must be unique.");
        }

        if (FailureInjection.Point == BattleFailurePoint.FormationConflict ||
            all.GroupBy(value => (value.Side, value.FormationSlot)).Any(group => group.Count() > 1))
        {
            return Failure("battle.formation_conflict", "Formation slot is duplicated within a side.");
        }

        if (!all.Any(value => value.Side == BattleSide.PlayerSide) ||
            !all.Any(value => value.Side == BattleSide.EnemySide))
        {
            return Failure("battle.sides_invalid", "Battle requires player and enemy participants.");
        }

        var eligible = BattleCollections.Freeze(all.Where(value => value.IsAlive).Select(value => value.ParticipantId));
        var round = new BattleRound(
            battleInstanceId,
            1,
            BattleRoundState.CollectingActions,
            eligible,
            [],
            [],
            [],
            0,
            [],
            now,
            null,
            null,
            1,
            request.CorrelationId);
        var formationStatus = all.All(value => value.FormationPolicyStatus != CombatPolicyStatus.EvidenceBlocked)
            ? all.Max(value => value.FormationPolicyStatus)
            : CombatPolicyStatus.EvidenceBlocked;
        return OperationResult<BattleInstance>.Success(new BattleInstance(
            battleInstanceId,
            request.BattleRequestId,
            BattleRuntimeHash.SafeId(request.IdempotencyKey),
            request.SourceWorldInstanceId,
            request.SourceMapId,
            encounter.EncounterDefinitionId,
            encounter.EncounterType == BattleEncounterType.InternalTest ? BattleType.InternalTest : BattleType.PlayerVersusEnvironment,
            BattleState.Active,
            BattlePhase.CollectingActions,
            1,
            0,
            BattleCollections.Freeze(all),
            [],
            round,
            1,
            BattleWinnerSide.None,
            BattleCompletionReason.None,
            BattleRewardState.Pending,
            BattleRecoveryState.NotRequired,
            now,
            now,
            now,
            null,
            request.CorrelationId,
            encounter.PolicyStatus,
            formationStatus,
            CombatPolicyStatus.EvidenceBlocked,
            CombatPolicyStatus.Baseline,
            CombatPolicyStatus.EvidenceBlocked,
            encounter.ContentVersion,
            encounter.RawMetadata));
    }

    private static OperationResult<BattleInstance> Failure(string code, string message) =>
        OperationResult<BattleInstance>.Failure(code, message);
}

public sealed class InMemoryBattleStore :
    IBattleInstanceRepository,
    IBattleActionSubmissionStore,
    IBattleIdempotencyStore,
    IBattleRuntimeReadModel
{
    private readonly ConcurrentDictionary<Guid, BattleInstance> _battles = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, BattleCreationResult Result)> _creations = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, BattleActionSubmissionResult Result)> _actions = [];

    public BattleFailureInjection FailureInjection { get; } = new();

    public IReadOnlyList<BattleInstance> Snapshot => _battles.Values
        .OrderBy(value => value.CreatedAtUtc)
        .ToArray();

    public Task<BattleInstance?> GetAsync(Guid battleInstanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _battles.TryGetValue(battleInstanceId, out var battle);
        return Task.FromResult(battle);
    }

    public Task<BattleInstance?> FindByRequestAsync(
        Guid battleRequestId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_battles.Values.FirstOrDefault(value =>
            value.BattleRequestId == battleRequestId));
    }

    public Task<BattleInstance?> FindActiveByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var battle = _battles.Values.FirstOrDefault(value =>
            value.State is not BattleState.Completed and not BattleState.Aborted and not BattleState.Closed &&
            value.Participants.Any(participant => participant.CharacterId == characterId));
        return Task.FromResult(battle);
    }

    public Task<OperationResult> CreateAsync(BattleInstance battle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point == BattleFailurePoint.CreationPersistence)
        {
            return Task.FromResult(OperationResult.Failure(
                "battle.persistence_failure",
                "Injected battle creation persistence failure."));
        }

        return Task.FromResult(_battles.TryAdd(battle.BattleInstanceId, battle)
            ? OperationResult.Success
            : OperationResult.Failure("battle.duplicate", "Battle instance already exists."));
    }

    public Task<OperationResult> SaveAsync(
        BattleInstance battle,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailureInjection.Point is BattleFailurePoint.ActionLockPersistence or
            BattleFailurePoint.ActionResultPersistence or
            BattleFailurePoint.CompletionPersistence)
        {
            return Task.FromResult(OperationResult.Failure(
                "battle.persistence_failure",
                $"Injected battle persistence failure: {FailureInjection.Point}."));
        }

        while (true)
        {
            if (!_battles.TryGetValue(battle.BattleInstanceId, out var current))
            {
                return Task.FromResult(OperationResult.Failure("battle.not_found", "Battle instance was not found."));
            }

            if (current.BattleVersion != expectedVersion)
            {
                return Task.FromResult(OperationResult.Failure(
                    "battle.version_conflict",
                    "Persisted battle version changed before commit."));
            }

            if (_battles.TryUpdate(battle.BattleInstanceId, battle, current))
            {
                return Task.FromResult(OperationResult.Success);
            }
        }
    }

    public Task<BattleActionReplay> FindAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_actions.TryGetValue(idempotencyKey, out var value))
        {
            return Task.FromResult(new BattleActionReplay(false, true, null));
        }

        return Task.FromResult(new BattleActionReplay(
            true,
            string.Equals(value.PayloadHash, payloadHash, StringComparison.Ordinal),
            value.Result));
    }

    public Task SaveAsync(
        string idempotencyKey,
        string payloadHash,
        BattleActionSubmissionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _actions.TryAdd(idempotencyKey, (payloadHash, result));
        return Task.CompletedTask;
    }

    public Task<BattleCreationReplay> FindCreationAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_creations.TryGetValue(idempotencyKey, out var value))
        {
            return Task.FromResult(new BattleCreationReplay(false, true, null));
        }

        return Task.FromResult(new BattleCreationReplay(
            true,
            string.Equals(value.PayloadHash, payloadHash, StringComparison.Ordinal),
            value.Result));
    }

    public Task SaveCreationAsync(
        string idempotencyKey,
        string payloadHash,
        BattleCreationResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _creations.TryAdd(idempotencyKey, (payloadHash, result));
        return Task.CompletedTask;
    }
}

public sealed class EvidenceBlockedBattleTurnOrderPolicy : IBattleTurnOrderPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.EvidenceBlocked;

    public OperationResult<BattleTurnOrderResult> Resolve(
        BattleInstance battle,
        BattleRound round,
        DateTimeOffset now) =>
        OperationResult<BattleTurnOrderResult>.Failure(
            "battle.turn_order_evidence_blocked",
            "Official speed, initiative, and tie-break evidence is unavailable.");
}

public sealed class DeterministicTestBattleTurnOrderPolicy : IBattleTurnOrderPolicy
{
    public DeterministicTestBattleTurnOrderPolicy(int? seed = null)
    {
        Seed = seed;
    }

    public int? Seed { get; }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public OperationResult<BattleTurnOrderResult> Resolve(
        BattleInstance battle,
        BattleRound round,
        DateTimeOffset now)
    {
        var participants = battle.Participants.ToDictionary(value => value.ParticipantId);
        if (round.LockedActions.Any(action => !participants.ContainsKey(action.ParticipantId)))
        {
            return OperationResult<BattleTurnOrderResult>.Failure(
                "battle.turn_order_participant_missing",
                "Locked action references a missing participant.");
        }

        var ordered = round.LockedActions
            .OrderByDescending(action => participants[action.ParticipantId].InitiativeCandidate ?? 0)
            .ThenBy(action => participants[action.ParticipantId].FormationSlot)
            .ThenBy(action => action.ParticipantId)
            .ToArray();
        return OperationResult<BattleTurnOrderResult>.Success(new BattleTurnOrderResult(
            round.RoundNumber,
            BattleCollections.Freeze(ordered.Select(value => value.ActionId)),
            BattleCollections.Freeze(ordered.Select(value => value.ParticipantId)),
            PolicyStatus,
            Seed,
            null,
            now));
    }
}

public sealed class BaselineServerBattleVictoryPolicy : IBattleVictoryPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.Baseline;

    public BattleVictoryResult Evaluate(IReadOnlyList<BattleParticipant> participants)
    {
        var playerAlive = participants.Any(value => value.Side == BattleSide.PlayerSide && value.IsAlive);
        var enemyAlive = participants.Any(value => value.Side == BattleSide.EnemySide && value.IsAlive);
        return (playerAlive, enemyAlive) switch
        {
            (true, true) => new(false, BattleWinnerSide.None, BattleCompletionReason.None, PolicyStatus),
            (true, false) => new(true, BattleWinnerSide.PlayerSide, BattleCompletionReason.PlayerVictory, PolicyStatus),
            (false, true) => new(true, BattleWinnerSide.EnemySide, BattleCompletionReason.EnemyVictory, PolicyStatus),
            (false, false) => new(true, BattleWinnerSide.Draw, BattleCompletionReason.Draw, PolicyStatus)
        };
    }
}

public sealed record BattleCombatAuthorityState(
    Guid BattleInstanceId,
    Guid ParticipantId,
    BattleParticipantType ParticipantType,
    long MaximumHp,
    long CurrentHp,
    long AttackPower,
    long Defense,
    long RuntimeVersion,
    CombatPolicyStatus StatPolicyStatus);

public interface IBattleCombatStateAuthority
{
    void Seed(Guid battleInstanceId, IEnumerable<BattleParticipant> participants);

    OperationResult<BattleCombatAuthorityState> Get(Guid battleInstanceId, Guid participantId);
}

public sealed class BattleScopedCombatExecutionPort :
    IBattleCombatExecutionPort,
    IBattleCombatStateAuthority,
    IBattleHealthMutationPort
{
    private readonly ICombatDamagePolicy _damagePolicy;
    private readonly IBattleDamageModifierPort? _damageModifiers;
    private readonly IBattleHealingModifierPort? _healingModifiers;
    private readonly ConcurrentDictionary<(Guid BattleId, Guid ParticipantId), BattleCombatAuthorityState> _states = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, BattleCombatExecutionResult Result)> _completed = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, BattleHealthMutationResult Result)> _completedHealing = [];
    private readonly ConcurrentDictionary<(Guid BattleId, Guid ParticipantId), object> _stateLocks = [];

    public BattleScopedCombatExecutionPort(
        ICombatDamagePolicy damagePolicy,
        IBattleDamageModifierPort? damageModifiers = null,
        IBattleHealingModifierPort? healingModifiers = null)
    {
        _damagePolicy = damagePolicy;
        _damageModifiers = damageModifiers;
        _healingModifiers = healingModifiers;
    }

    public BattleFailureInjection FailureInjection { get; } = new();

    public void Seed(Guid battleInstanceId, IEnumerable<BattleParticipant> participants)
    {
        foreach (var participant in participants)
        {
            _states.TryAdd((battleInstanceId, participant.ParticipantId), new BattleCombatAuthorityState(
                battleInstanceId,
                participant.ParticipantId,
                participant.ParticipantType,
                participant.MaximumHp,
                participant.CurrentHp,
                participant.AttackPower,
                participant.Defense,
                participant.RuntimeVersion,
                participant.StatPolicyStatus));
        }
    }

    public OperationResult<BattleCombatAuthorityState> Get(Guid battleInstanceId, Guid participantId) =>
        _states.TryGetValue((battleInstanceId, participantId), out var state)
            ? OperationResult<BattleCombatAuthorityState>.Success(state)
            : OperationResult<BattleCombatAuthorityState>.Failure(
                "battle.combat_authority_missing",
                "Battle combat authority state is missing.");

    public Task<BattleCombatExecutionResult> ExecuteAsync(
        BattleCombatExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _stateLocks.GetOrAdd(
            (request.BattleInstanceId, request.Target.ParticipantId),
            _ => new object());
        lock (gate)
        {
            return Task.FromResult(ExecuteCore(request));
        }
    }

    private BattleCombatExecutionResult ExecuteCore(BattleCombatExecutionRequest request)
    {
        var payloadHash = $"{request.BattleInstanceId:N}:{request.RoundNumber}:{request.ActionId:N}:{request.ResolutionIndex}:" +
                          $"{request.Attacker.ParticipantId:N}:{request.Target.ParticipantId:N}:" +
                          $"{request.Attacker.RuntimeVersion}:{request.Target.RuntimeVersion}:" +
                          $"{request.SkillBaseDamage}:{request.SkillValuePolicyStatus}:{request.TargetIsDefending}";
        if (_completed.TryGetValue(request.StableIdempotencyKey, out var replay))
        {
            return string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? replay.Result with { IsDuplicate = true }
                : Failure(request, BattleActionResolutionCode.VersionConflict, "battle.combat_replay_conflict");
        }

        if (FailureInjection.Point == BattleFailurePoint.CombatExecution)
        {
            return Failure(request, BattleActionResolutionCode.CombatFailure, "battle.combat_execution_failed");
        }

        if (!_states.TryGetValue((request.BattleInstanceId, request.Attacker.ParticipantId), out var attacker) ||
            !_states.TryGetValue((request.BattleInstanceId, request.Target.ParticipantId), out var target))
        {
            return Failure(request, BattleActionResolutionCode.CombatFailure, "battle.combat_authority_missing");
        }

        if (attacker.RuntimeVersion != request.Attacker.RuntimeVersion ||
            target.RuntimeVersion != request.Target.RuntimeVersion)
        {
            return Failure(request, BattleActionResolutionCode.VersionConflict, "battle.combat_version_conflict");
        }

        var computed = _damagePolicy.Compute(ToStats(attacker), ToStats(target));
        if (!computed.Succeeded || computed.FinalDamage <= 0)
        {
            return Failure(
                request,
                BattleActionResolutionCode.CombatFailure,
                computed.FailureCode,
                computed.PolicyStatus);
        }

        var finalDamage = computed.FinalDamage;
        long hpAfter;
        try
        {
            if (request.SkillBaseDamage is not null)
            {
                if (request.SkillBaseDamage.Value < 0 ||
                    request.SkillValuePolicyStatus is not (
                        CombatPolicyStatus.Verified or
                        CombatPolicyStatus.ContentBacked or
                        CombatPolicyStatus.Baseline or
                        CombatPolicyStatus.TestOnly))
                {
                    return Failure(
                        request,
                        BattleActionResolutionCode.CombatFailure,
                        "battle.skill_damage_evidence_blocked");
                }

                finalDamage = checked(computed.FinalDamage + request.SkillBaseDamage.Value);
            }

            if (request.TargetIsDefending)
            {
                finalDamage = ApplyDefendingReduction(finalDamage, target.Defense);
            }

            if (_damageModifiers is not null)
            {
                var modified = _damageModifiers.ApplyDamageModifiers(
                    new StatusDamageModifierRequest(
                        request.BattleInstanceId,
                        request.RoundNumber,
                        request.Attacker.ParticipantId,
                        request.Target.ParticipantId,
                        finalDamage,
                        request.CreatedAtUtc));
                if (modified.ResultCode != StatusResultCode.Success)
                {
                    return Failure(
                        request,
                        BattleActionResolutionCode.CombatFailure,
                        modified.FailureCode,
                        modified.PolicyStatus);
                }

                finalDamage = modified.FinalValue;
            }

            hpAfter = Math.Max(0, checked(target.CurrentHp - finalDamage));
        }
        catch (OverflowException)
        {
            return Failure(request, BattleActionResolutionCode.CombatFailure, "battle.damage_overflow");
        }

        var updated = target with
        {
            CurrentHp = hpAfter,
            RuntimeVersion = checked(target.RuntimeVersion + 1)
        };
        _states[(request.BattleInstanceId, target.ParticipantId)] = updated;
        var result = new BattleCombatExecutionResult(
            BattleActionResolutionCode.Success,
            Guid.NewGuid(),
            target.CurrentHp,
            finalDamage,
            hpAfter,
            hpAfter == 0,
            target.RuntimeVersion,
            updated.RuntimeVersion,
            "",
            false,
            computed.PolicyStatus,
            []);
        _completed.TryAdd(request.StableIdempotencyKey, (payloadHash, result));
        return result;
    }

    private static long ApplyDefendingReduction(long finalDamage, long targetDefense)
    {
        if (finalDamage <= 1 || targetDefense <= 1)
        {
            return finalDamage;
        }

        var extraDefense = targetDefense / 2;
        return Math.Max(1, finalDamage - extraDefense);
    }

    public Task<BattleHealthMutationResult> HealAsync(
        BattleHealthMutationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _stateLocks.GetOrAdd(
            (request.BattleInstanceId, request.Target.ParticipantId),
            _ => new object());
        lock (gate)
        {
            return Task.FromResult(HealCore(request));
        }
    }

    private BattleHealthMutationResult HealCore(BattleHealthMutationRequest request)
    {
        var payloadHash = $"{request.BattleInstanceId:N}:{request.RoundNumber}:{request.BattleActionId:N}:" +
                          $"{request.EffectExecutionId:N}:{request.Source.ParticipantId:N}:{request.Target.ParticipantId:N}:" +
                          $"{request.HealAmount}:{request.ExpectedTargetVersion}:{request.PolicyStatus}";
        if (_completedHealing.TryGetValue(request.StableIdempotencyKey, out var replay))
        {
            return string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? replay.Result with
                {
                    ResultCode = SkillResultCode.DuplicateCompleted,
                    IsDuplicate = true
                }
                : HealingFailure(request, SkillResultCode.ReplayConflict, "skill.heal_replay_conflict");
        }

        if (!_states.TryGetValue((request.BattleInstanceId, request.Target.ParticipantId), out var target))
        {
            return HealingFailure(request, SkillResultCode.HealthMutationFailure, "skill.health_authority_missing");
        }

        if (!request.Target.IsAlive || target.CurrentHp <= 0)
        {
            return HealingFailure(request, SkillResultCode.TargetDead, "skill.target_dead", target);
        }

        if (request.HealAmount < 0)
        {
            return HealingFailure(request, SkillResultCode.HealthMutationFailure, "skill.heal_invalid", target);
        }

        if (request.PolicyStatus is not (
                CombatPolicyStatus.Verified or
                CombatPolicyStatus.ContentBacked or
                CombatPolicyStatus.TestOnly))
        {
            return HealingFailure(request, SkillResultCode.EffectEvidenceBlocked, "skill.heal_evidence_blocked", target);
        }

        if (target.RuntimeVersion != request.ExpectedTargetVersion ||
            target.RuntimeVersion != request.Target.RuntimeVersion)
        {
            return HealingFailure(request, SkillResultCode.VersionConflict, "skill.heal_version_conflict", target);
        }

        var healAmount = request.HealAmount;
        if (_healingModifiers is not null)
        {
            var modified = _healingModifiers.ApplyHealingModifiers(
                new StatusHealingModifierRequest(
                    request.BattleInstanceId,
                    request.RoundNumber,
                    request.Source.ParticipantId,
                    request.Target.ParticipantId,
                    healAmount,
                    request.CreatedAtUtc));
            if (modified.ResultCode != StatusResultCode.Success)
            {
                return HealingFailure(
                    request,
                    SkillResultCode.HealthMutationFailure,
                    modified.FailureCode,
                    target);
            }

            healAmount = modified.FinalValue;
        }

        long hpAfter;
        try
        {
            hpAfter = Math.Min(target.MaximumHp, checked(target.CurrentHp + healAmount));
        }
        catch (OverflowException)
        {
            hpAfter = target.MaximumHp;
        }

        var effective = hpAfter - target.CurrentHp;
        var updatedVersion = effective > 0
            ? checked(target.RuntimeVersion + 1)
            : target.RuntimeVersion;
        var updated = target with
        {
            CurrentHp = hpAfter,
            RuntimeVersion = updatedVersion
        };
        _states[(request.BattleInstanceId, request.Target.ParticipantId)] = updated;
        var result = new BattleHealthMutationResult(
            SkillResultCode.Success,
            request.HealAmount,
            effective,
            target.CurrentHp,
            hpAfter,
            target.MaximumHp,
            target.RuntimeVersion,
            updatedVersion,
            "",
            false,
            request.PolicyStatus);
        _completedHealing.TryAdd(request.StableIdempotencyKey, (payloadHash, result));
        return result;
    }

    private static BattleHealthMutationResult HealingFailure(
        BattleHealthMutationRequest request,
        SkillResultCode code,
        string failureCode,
        BattleCombatAuthorityState? state = null) =>
        new(
            code,
            request.HealAmount,
            0,
            state?.CurrentHp ?? request.Target.CurrentHp,
            state?.CurrentHp ?? request.Target.CurrentHp,
            state?.MaximumHp ?? request.Target.MaximumHp,
            state?.RuntimeVersion ?? request.Target.RuntimeVersion,
            state?.RuntimeVersion ?? request.Target.RuntimeVersion,
            failureCode,
            false,
            request.PolicyStatus);

    private static CombatStatSnapshot ToStats(BattleCombatAuthorityState state) =>
        new(
            state.ParticipantId.GetHashCode(),
            state.ParticipantType == BattleParticipantType.Player ? CombatEntityType.Player : CombatEntityType.Monster,
            0,
            state.MaximumHp,
            state.CurrentHp,
            0,
            0,
            state.AttackPower,
            state.Defense,
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
            state.RuntimeVersion,
            state.StatPolicyStatus,
            "battle-scoped-authority-v1");

    private BattleCombatExecutionResult Failure(
        BattleCombatExecutionRequest request,
        BattleActionResolutionCode code,
        string failureCode,
        CombatPolicyStatus? policyStatus = null) =>
        new(
            code,
            null,
            request.Target.CurrentHp,
            0,
            request.Target.CurrentHp,
            false,
            request.Target.RuntimeVersion,
            request.Target.RuntimeVersion,
            failureCode,
            false,
            policyStatus ?? _damagePolicy.PolicyStatus,
            []);
}

public sealed class ExistingCombatRuntimeBattleExecutionPort : IBattleCombatExecutionPort
{
    private readonly ICombatCoordinator _combat;

    public ExistingCombatRuntimeBattleExecutionPort(ICombatCoordinator combat)
    {
        _combat = combat;
    }

    public async Task<BattleCombatExecutionResult> ExecuteAsync(
        BattleCombatExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Attacker.CharacterId is null ||
            request.Attacker.SessionId is null ||
            request.Attacker.SourceRuntimeEntityId <= 0 ||
            request.Target.SourceRuntimeEntityId <= 0)
        {
            return Failure(request, "battle.combat_runtime_identity_unavailable");
        }

        var intent = new CombatIntent(
            Guid.NewGuid(),
            request.StableIdempotencyKey,
            request.Attacker.SessionId,
            request.Attacker.CharacterId.Value,
            request.Attacker.SourceRuntimeEntityId,
            request.Target.SourceRuntimeEntityId,
            request.Target.MonsterTemplateId,
            CombatActionType.BasicAttack,
            request.Attacker.RuntimeVersion,
            request.Target.RuntimeVersion,
            null,
            "TurnBasedBattleRuntime",
            request.CreatedAtUtc,
            request.CorrelationId);
        var result = await _combat.ExecuteAsync(intent, cancellationToken);
        return new BattleCombatExecutionResult(
            result.Succeeded ? BattleActionResolutionCode.Success : BattleActionResolutionCode.CombatFailure,
            result.CombatIntentId,
            result.HpBefore,
            result.Damage,
            result.HpAfter,
            result.DeathCommitted,
            result.TargetVersionBefore,
            result.TargetVersionAfter,
            result.FailureCode,
            result.IsDuplicate,
            result.DamagePolicyStatus,
            []);
    }

    private static BattleCombatExecutionResult Failure(
        BattleCombatExecutionRequest request,
        string failureCode) =>
        new(
            BattleActionResolutionCode.CombatFailure,
            null,
            request.Target.CurrentHp,
            0,
            request.Target.CurrentHp,
            false,
            request.Target.RuntimeVersion,
            request.Target.RuntimeVersion,
            failureCode,
            false,
            CombatPolicyStatus.EvidenceBlocked,
            []);
}

public sealed class BattleActionResolver : IBattleActionResolver
{
    private readonly IBattleCombatExecutionPort _combat;
    private readonly IBattleClock _clock;
    private readonly ISkillActionResolver? _skills;
    private readonly IBattleActionRestrictionPort? _restrictions;

    public BattleActionResolver(
        IBattleCombatExecutionPort combat,
        IBattleClock clock,
        ISkillActionResolver? skills = null,
        IBattleActionRestrictionPort? restrictions = null)
    {
        _combat = combat;
        _clock = clock;
        _skills = skills;
        _restrictions = restrictions;
    }

    public async Task<BattleActionResult> ResolveAsync(
        BattleInstance battle,
        BattleLockedAction action,
        int resolutionIndex,
        CancellationToken cancellationToken)
    {
        var attacker = battle.Participants.FirstOrDefault(value => value.ParticipantId == action.ParticipantId);
        if (attacker is null)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.CombatFailure, "battle.participant_not_found");
        }

        if (!attacker.IsAlive)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.SkippedAttackerDead, "battle.attacker_dead");
        }

        if (_restrictions is not null)
        {
            var restriction = _restrictions.Evaluate(battle, action, _clock.UtcNow);
            if (!restriction.Allowed)
            {
                return Failure(
                    action,
                    resolutionIndex,
                    BattleActionResolutionCode.ActionRestricted,
                    restriction.FailureCode);
            }
        }

        if (action.ActionType == BattleActionType.Pass)
        {
            return new BattleActionResult(
                action.ActionId,
                action.ParticipantId,
                [],
                action.ActionType,
                resolutionIndex,
                null,
                0,
                0,
                0,
                false,
                BattleActionResolutionCode.Passed,
                "",
                attacker.RuntimeVersion,
                attacker.RuntimeVersion,
                _clock.UtcNow,
                false,
                []);
        }

        if (action.ActionType == BattleActionType.Defend)
        {
            return new BattleActionResult(
                action.ActionId,
                action.ParticipantId,
                [],
                action.ActionType,
                resolutionIndex,
                null,
                0,
                attacker.CurrentHp,
                attacker.CurrentHp,
                false,
                BattleActionResolutionCode.Success,
                "",
                attacker.RuntimeVersion,
                attacker.RuntimeVersion,
                _clock.UtcNow,
                false,
                []);
        }

        if (action.ActionType == BattleActionType.Skill)
        {
            if (_skills is null)
            {
                return Failure(
                    action,
                    resolutionIndex,
                    BattleActionResolutionCode.UnsupportedAction,
                    "skill.runtime_unwired");
            }

            var skill = await _skills.ResolveAsync(battle, action, resolutionIndex, cancellationToken);
            var mutations = SkillCollections.Freeze(skill.TargetResults.Select(value =>
                new BattleParticipantMutation(
                    value.TargetParticipantId,
                    value.Damage,
                    value.Heal,
                    value.HpBefore,
                    value.HpAfter,
                    value.TargetDefeated,
                    value.RuntimeVersionBefore,
                    value.RuntimeVersionAfter,
                    value.FailureCode)));
            var primary = skill.TargetResults.FirstOrDefault();
            return new BattleActionResult(
                action.ActionId,
                action.ParticipantId,
                SkillCollections.Freeze(skill.TargetResults.Select(value => value.TargetParticipantId)),
                action.ActionType,
                resolutionIndex,
                null,
                skill.TargetResults.Sum(value => value.Damage),
                primary?.HpBefore ?? 0,
                primary?.HpAfter ?? 0,
                skill.TargetResults.Any(value => value.TargetDefeated),
                MapSkillResult(skill.ResultCode),
                skill.FailureCode,
                primary?.RuntimeVersionBefore ?? 0,
                primary?.RuntimeVersionAfter ?? 0,
                skill.CompletedAtUtc ?? _clock.UtcNow,
                skill.IsDuplicate,
                [],
                mutations);
        }

        if (action.ActionType != BattleActionType.BasicAttack)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.UnsupportedAction, "battle.action_unsupported");
        }

        if (action.TargetParticipantIds.Count != 1)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.InvalidTarget, "battle.target_invalid");
        }

        var target = battle.Participants.FirstOrDefault(value => value.ParticipantId == action.TargetParticipantIds[0]);
        if (target is null || target.Side == attacker.Side)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.InvalidTarget, "battle.target_invalid");
        }

        if (!target.IsAlive)
        {
            return Failure(action, resolutionIndex, BattleActionResolutionCode.SkippedTargetDead, "battle.target_dead");
        }

        var execution = await _combat.ExecuteAsync(
            new BattleCombatExecutionRequest(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                action.ActionId,
                resolutionIndex,
                attacker,
                target,
                $"{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{action.ActionId:N}:{resolutionIndex}",
                _clock.UtcNow,
                action.CorrelationId,
                TargetIsDefending: TargetIsDefending(battle, target.ParticipantId)),
            cancellationToken);
        return new BattleActionResult(
            action.ActionId,
            action.ParticipantId,
            action.TargetParticipantIds,
            action.ActionType,
            resolutionIndex,
            execution.CombatIntentId,
            execution.Damage,
            execution.HpBefore,
            execution.HpAfter,
            execution.TargetDefeated,
            execution.Code,
            execution.FailureCode,
            execution.RuntimeVersionBefore,
            execution.RuntimeVersionAfter,
            _clock.UtcNow,
            execution.IsDuplicate,
            []);
    }

    private static bool TargetIsDefending(BattleInstance battle, Guid targetParticipantId) =>
        battle.CurrentRound.LockedActions.Any(value =>
            value.ParticipantId == targetParticipantId &&
            value.ActionType == BattleActionType.Defend);

    private static BattleActionResolutionCode MapSkillResult(SkillResultCode code) =>
        code switch
        {
            SkillResultCode.Success or SkillResultCode.DuplicateCompleted => BattleActionResolutionCode.Success,
            SkillResultCode.InvalidTarget or SkillResultCode.NoValidTarget => BattleActionResolutionCode.InvalidTarget,
            SkillResultCode.TargetDead => BattleActionResolutionCode.SkippedTargetDead,
            SkillResultCode.VersionConflict => BattleActionResolutionCode.VersionConflict,
            SkillResultCode.RecoveryRequired => BattleActionResolutionCode.RecoveryRequired,
            SkillResultCode.UnsupportedEffect or SkillResultCode.EffectEvidenceBlocked or
                SkillResultCode.SkillEvidenceBlocked or SkillResultCode.TargetPolicyBlocked or
                SkillResultCode.CostEvidenceBlocked or SkillResultCode.CooldownEvidenceBlocked =>
                BattleActionResolutionCode.UnsupportedAction,
            _ => BattleActionResolutionCode.CombatFailure
        };

    private BattleActionResult Failure(
        BattleLockedAction action,
        int resolutionIndex,
        BattleActionResolutionCode code,
        string failureCode) =>
        new(
            action.ActionId,
            action.ParticipantId,
            action.TargetParticipantIds,
            action.ActionType,
            resolutionIndex,
            null,
            0,
            0,
            0,
            false,
            code,
            failureCode,
            0,
            0,
            _clock.UtcNow,
            false,
            []);
}

public sealed class NoBattleRewardPolicy : IBattleRewardPolicy
{
    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.EvidenceBlocked;

    public BattleRewardPlan CreatePlan(BattleInstance battle, DateTimeOffset now) =>
        new(
            BattleRuntimeHash.DeterministicGuid($"battle-reward:{battle.BattleInstanceId:N}"),
            battle.BattleInstanceId,
            $"battle-reward:{battle.BattleInstanceId:N}",
            [],
            PolicyStatus,
            now,
            battle.CorrelationId);
}

public sealed class DeterministicTestBattleRewardPolicy : IBattleRewardPolicy
{
    private readonly IReadOnlyList<CombatItemGrant> _items;
    private readonly IReadOnlyList<CombatCurrencyGrant> _currencies;

    public DeterministicTestBattleRewardPolicy(
        IReadOnlyList<CombatItemGrant>? items = null,
        IReadOnlyList<CombatCurrencyGrant>? currencies = null)
    {
        _items = items ?? [];
        _currencies = currencies ?? [];
    }

    public CombatPolicyStatus PolicyStatus => CombatPolicyStatus.TestOnly;

    public BattleRewardPlan CreatePlan(BattleInstance battle, DateTimeOffset now)
    {
        var grants = battle.WinnerSide == BattleWinnerSide.PlayerSide
            ? battle.Participants
                .Where(value => value.Side == BattleSide.PlayerSide && value.CharacterId is not null)
                .Select(value => new BattleRewardGrant(value.CharacterId!.Value, _items, _currencies))
                .ToArray()
            : [];
        return new BattleRewardPlan(
            BattleRuntimeHash.DeterministicGuid($"battle-reward:{battle.BattleInstanceId:N}"),
            battle.BattleInstanceId,
            $"battle-reward:{battle.BattleInstanceId:N}",
            BattleCollections.Freeze(grants),
            PolicyStatus,
            now,
            battle.CorrelationId);
    }
}

public sealed class BattleRewardCoordinator : IBattleRewardCoordinator
{
    private readonly IInventoryTransactionCoordinator _inventory;
    private readonly ConcurrentDictionary<Guid, BattleRewardResult> _completed = [];

    public BattleRewardCoordinator(IInventoryTransactionCoordinator inventory)
    {
        _inventory = inventory;
    }

    public BattleFailureInjection FailureInjection { get; } = new();

    public async Task<BattleRewardResult> FinalizeAsync(
        BattleRewardPlan plan,
        CancellationToken cancellationToken)
    {
        if (_completed.TryGetValue(plan.RewardPlanId, out var completed))
        {
            return completed with { Code = BattleResultCode.DuplicateCompleted, IsDuplicate = true };
        }

        if (FailureInjection.Point == BattleFailurePoint.RewardTransaction)
        {
            return new BattleRewardResult(
                BattleResultCode.RewardFailure,
                plan.RewardPlanId,
                false,
                false,
                "battle.reward_transaction_failed",
                plan.CreatedAtUtc);
        }

        foreach (var grant in plan.Grants)
        {
            long currency;
            try
            {
                currency = grant.Currencies.Aggregate(
                    0L,
                    (current, item) => checked(current + item.Amount));
            }
            catch (OverflowException)
            {
                return new BattleRewardResult(
                    BattleResultCode.RewardFailure,
                    plan.RewardPlanId,
                    false,
                    false,
                    "battle.reward_currency_overflow",
                    plan.CreatedAtUtc);
            }

            if (grant.Currencies.Any(value =>
                    value.Amount < 0 ||
                    !string.Equals(value.CurrencyType, "Gold", StringComparison.OrdinalIgnoreCase)))
            {
                return new BattleRewardResult(
                    BattleResultCode.RewardFailure,
                    plan.RewardPlanId,
                    false,
                    false,
                    "battle.reward_currency_invalid",
                    plan.CreatedAtUtc);
            }

            var version = await _inventory.GetCurrentVersionAsync(grant.CharacterId, cancellationToken);
            if (!version.Succeeded)
            {
                return new BattleRewardResult(
                    BattleResultCode.RewardFailure,
                    plan.RewardPlanId,
                    false,
                    false,
                    version.Error.Code,
                    DateTimeOffset.UtcNow);
            }

            var result = await _inventory.ExecuteAsync(
                new InventoryTransactionRequest(
                    plan.RewardPlanId,
                    $"{plan.IdempotencyKey}:{grant.CharacterId}",
                    grant.CharacterId,
                    $"battle:{plan.BattleInstanceId:N}",
                    null,
                    InventoryOperationType.SystemGrant,
                    version.Value,
                    "TurnBasedBattleReward",
                    grant.Items.Select(value =>
                        new InventoryMutationRequest(ItemTemplateId: value.ItemTemplateId, Quantity: value.Quantity)).ToArray(),
                    currency,
                    null,
                    plan.CreatedAtUtc,
                    InventoryMutationAuthorityKind.TrustedServer),
                cancellationToken);
            if (!result.Succeeded)
            {
                return new BattleRewardResult(
                    BattleResultCode.RewardFailure,
                    plan.RewardPlanId,
                    false,
                    false,
                    result.FailureCode,
                    DateTimeOffset.UtcNow);
            }
        }

        var final = new BattleRewardResult(
            BattleResultCode.Success,
            plan.RewardPlanId,
            true,
            false,
            "",
            DateTimeOffset.UtcNow);
        _completed.TryAdd(plan.RewardPlanId, final);
        return final;
    }
}
