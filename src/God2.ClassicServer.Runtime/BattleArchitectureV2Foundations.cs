using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;

namespace God2.ClassicServer.Runtime;

public sealed class BattleActorStateMachine
{
    private static readonly IReadOnlyDictionary<BattleActorStateCode, IReadOnlySet<BattleActorStateCode>> Allowed =
        new ReadOnlyDictionary<BattleActorStateCode, IReadOnlySet<BattleActorStateCode>>(
            new Dictionary<BattleActorStateCode, IReadOnlySet<BattleActorStateCode>>
            {
                [BattleActorStateCode.Created] = Set(BattleActorStateCode.Preparing, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.Preparing] = Set(BattleActorStateCode.WaitingForClientReady, BattleActorStateCode.Suspended, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.WaitingForClientReady] = Set(BattleActorStateCode.RoundOpening, BattleActorStateCode.Suspended, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.RoundOpening] = Set(BattleActorStateCode.CollectingCommands, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.CollectingCommands] = Set(BattleActorStateCode.LockingCommands, BattleActorStateCode.Suspended, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.LockingCommands] = Set(BattleActorStateCode.PlanningResolution, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.PlanningResolution] = Set(BattleActorStateCode.ResolvingActions, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.ResolvingActions] = Set(BattleActorStateCode.RoundClosing, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.RoundClosing] = Set(BattleActorStateCode.EvaluatingCompletion, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.EvaluatingCompletion] = Set(BattleActorStateCode.RoundOpening, BattleActorStateCode.Finalizing, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.Finalizing] = Set(BattleActorStateCode.Completed, BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Faulted),
                [BattleActorStateCode.Suspended] = Set(BattleActorStateCode.RecoveryRequired, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.RecoveryRequired] = Set(BattleActorStateCode.Preparing, BattleActorStateCode.CollectingCommands, BattleActorStateCode.LockingCommands, BattleActorStateCode.PlanningResolution, BattleActorStateCode.ResolvingActions, BattleActorStateCode.RoundClosing, BattleActorStateCode.Finalizing, BattleActorStateCode.Aborting, BattleActorStateCode.Faulted),
                [BattleActorStateCode.Aborting] = Set(BattleActorStateCode.Aborted, BattleActorStateCode.Faulted),
                [BattleActorStateCode.Completed] = Set(),
                [BattleActorStateCode.Aborted] = Set(),
                [BattleActorStateCode.Faulted] = Set(BattleActorStateCode.RecoveryRequired)
            });

    public BattleActorResultCode Validate(
        BattleActorStateCode current,
        BattleActorStateCode next,
        out string failureCode)
    {
        if (current == next)
        {
            failureCode = "";
            return BattleActorResultCode.DuplicateCompleted;
        }

        if (Allowed.TryGetValue(current, out var allowed) && allowed.Contains(next))
        {
            failureCode = "";
            return BattleActorResultCode.Success;
        }

        failureCode = "battle.actor.invalid_transition";
        return BattleActorResultCode.InvalidBattleState;
    }

    public bool AcceptsGameplay(BattleActorStateCode state) =>
        state == BattleActorStateCode.CollectingCommands;

    public bool IsTerminal(BattleActorStateCode state) =>
        state is BattleActorStateCode.Completed or BattleActorStateCode.Aborted;

    private static IReadOnlySet<BattleActorStateCode> Set(params BattleActorStateCode[] states) =>
        new HashSet<BattleActorStateCode>(states);
}

public sealed class XorShift64StarBattleRandom : IBattleDeterministicRandom
{
    public const string Version = "xorshift64star-v1";
    private readonly ulong _initialSeed;
    private ulong _state;
    private long _drawCount;

    public XorShift64StarBattleRandom(ulong seed)
        : this(new BattleRandomState(Version, seed, Normalize(seed), 0))
    {
    }

    public XorShift64StarBattleRandom(BattleRandomState state)
    {
        if (!string.Equals(state.AlgorithmVersion, Version, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported battle RNG version.", nameof(state));
        }

        if (state.DrawCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        _initialSeed = state.InitialSeed;
        _state = Normalize(state.CurrentState);
        _drawCount = state.DrawCount;
    }

    public BattleRandomState State => new(Version, _initialSeed, _state, _drawCount);

    public ulong NextUInt64()
    {
        var value = _state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        _state = value;
        _drawCount = checked(_drawCount + 1);
        return value * 2685821657736338717UL;
    }

    public int NextInt32(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    private static ulong Normalize(ulong value) =>
        value == 0 ? 0x9e3779b97f4a7c15UL : value;
}

public sealed class BattleRandomFactory : IBattleRandomFactory
{
    public IBattleDeterministicRandom Create(string algorithmVersion, ulong seed)
    {
        if (!string.Equals(algorithmVersion, XorShift64StarBattleRandom.Version, StringComparison.Ordinal))
        {
            throw new NotSupportedException("battle.rng.version_unavailable");
        }

        return new XorShift64StarBattleRandom(seed);
    }

    public IBattleDeterministicRandom Restore(BattleRandomState state) =>
        new XorShift64StarBattleRandom(state);
}

public sealed class LegacyCompatibleBattleInitiativePlanner : IBattleInitiativePlanner
{
    private readonly IBattleTurnOrderPolicy _legacyPolicy;

    public LegacyCompatibleBattleInitiativePlanner(IBattleTurnOrderPolicy legacyPolicy)
    {
        _legacyPolicy = legacyPolicy;
    }

    public BattleInitiativeSnapshot Plan(
        BattleInstance battle,
        LockedRoundCommandSet commands,
        DateTimeOffset now)
    {
        var locked = commands.OrderedCommands
            .Select(command => new BattleLockedAction(
                command.BattleCommandId,
                battle.BattleInstanceId,
                commands.RoundNumber,
                command.ParticipantId,
                ToLegacy(command.CommandType),
                command.TargetParticipantIds,
                command.SkillDefinitionId,
                command.ItemReference,
                command.IdempotencySafeId,
                command.PayloadHash,
                command.SubmittedAtUtc,
                commands.CreatedAtUtc,
                command.CorrelationId))
            .ToArray();
        var round = battle.CurrentRound with
        {
            LockedActions = BattleCollections.Freeze(locked),
            State = BattleRoundState.Locked,
            ActionsLockedAtUtc = commands.CreatedAtUtc
        };
        var legacy = _legacyPolicy.Resolve(battle, round, now);
        var orderedIds = legacy.Succeeded && legacy.Value is not null
            ? legacy.Value.OrderedParticipantIds
            : commands.OrderedCommands
                .OrderBy(value => value.ParticipantId)
                .Select(value => value.ParticipantId)
                .ToArray();
        var entries = orderedIds
            .Select((participantId, index) =>
            {
                var command = commands.OrderedCommands.Single(value => value.ParticipantId == participantId);
                return new BattleActionOrderEntry(
                    participantId,
                    command.BattleCommandId,
                    index,
                    Priority(command.CommandType),
                    battle.Participants.Single(value => value.ParticipantId == participantId).InitiativeCandidate,
                    _legacyPolicy.PolicyStatus.ToString());
            })
            .ToArray();
        var provisional = new BattleInitiativeSnapshot(
            battle.BattleInstanceId,
            commands.RoundNumber,
            Array.AsReadOnly(entries),
            _legacyPolicy.PolicyStatus.ToString(),
            "LegacyCompatibleBattleInitiativePlanner",
            "");
        return provisional with { CanonicalHash = BattleArchitectureV2Hash.Canonical(provisional) };
    }

    private static BattleActionType ToLegacy(BattleActorCommandType commandType) =>
        commandType switch
        {
            BattleActorCommandType.BasicAttack => BattleActionType.BasicAttack,
            BattleActorCommandType.Skill => BattleActionType.Skill,
            BattleActorCommandType.Pass or BattleActorCommandType.TimeoutDefault => BattleActionType.Pass,
            BattleActorCommandType.Defend => BattleActionType.Defend,
            BattleActorCommandType.Item => BattleActionType.Item,
            BattleActorCommandType.Flee => BattleActionType.Flee,
            _ => BattleActionType.Unknown
        };

    private static int Priority(BattleActorCommandType commandType) =>
        commandType switch
        {
            BattleActorCommandType.Pass or BattleActorCommandType.TimeoutDefault => 2,
            _ => 1
        };
}

public sealed class ExistingRuntimeBattleActionExecutor : IBattleActorActionExecutor
{
    private readonly IBattleActionResolver _resolver;

    public ExistingRuntimeBattleActionExecutor(IBattleActionResolver resolver)
    {
        _resolver = resolver;
    }

    public Task<BattleActionResult> ExecuteAsync(
        BattleInstance battle,
        BattleActorActionPlan actionPlan,
        CancellationToken cancellationToken)
    {
        var locked = new BattleLockedAction(
            actionPlan.ActionExecutionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            actionPlan.ParticipantId,
            actionPlan.ActionType switch
            {
                BattleActorCommandType.BasicAttack => BattleActionType.BasicAttack,
                BattleActorCommandType.Skill => BattleActionType.Skill,
                BattleActorCommandType.Pass or BattleActorCommandType.TimeoutDefault => BattleActionType.Pass,
                _ => BattleActionType.Unknown
            },
            actionPlan.TargetParticipantIds,
            actionPlan.SkillDefinitionId,
            null,
            BattleArchitectureV2Hash.SafeId(actionPlan.CommandId.ToString("N")),
            BattleArchitectureV2Hash.Canonical(actionPlan),
            battle.UpdatedAtUtc,
            battle.UpdatedAtUtc,
            battle.CorrelationId);
        return _resolver.ResolveAsync(battle, locked, actionPlan.ExecutionOrder, cancellationToken);
    }
}

public sealed class BattleStateProjector : IBattleStateProjector
{
    public BattleActorSnapshot Project(
        BattleInstance battle,
        BattleEngineMode engineMode,
        BattleActorLifecycleState lifecycleState,
        BattleActorStateCode actorState,
        long commandWindowVersion,
        BattleRandomState randomState,
        long journalSequence,
        long outboxSequence,
        BattleFinalizationStateCode finalizationState,
        BattleActorRecoveryStateCode recoveryState,
        LockedRoundCommandSet? lockedCommands,
        RoundResolutionPlan? resolutionPlan,
        BattleActionResolutionCursor? cursor,
        DateTimeOffset updatedAtUtc)
    {
        var participants = battle.Participants
            .OrderBy(value => value.ParticipantId)
            .Select(value => new BattleActorParticipantSnapshot(
                value.ParticipantId,
                value.CharacterId,
                BattleArchitectureV2Hash.SafeId(value.SessionId ?? "internal"),
                SessionEpoch: 1,
                value.ParticipantType,
                value.Side,
                value.FormationSlot,
                value.CombatState,
                value.CurrentHp,
                value.MaximumHp,
                CurrentSp: 0,
                MaximumSp: 0,
                value.IsConnected,
                !value.IsAlive,
                Array.AsReadOnly(Array.Empty<string>()),
                new ReadOnlyDictionary<int, int>(new Dictionary<int, int>()),
                value.RuntimeVersion,
                cursor?.LastCommittedActionExecutionId))
            .ToArray();
        var submitted = battle.CurrentRound.SubmittedActions
            .Select(value => value.ParticipantId)
            .OrderBy(value => value)
            .ToArray();
        var required = battle.CurrentRound.EligibleParticipantIds.OrderBy(value => value).ToArray();
        var missing = required.Except(submitted).OrderBy(value => value).ToArray();
        var window = actorState == BattleActorStateCode.CollectingCommands
            ? new BattleCommandWindowSnapshot(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                commandWindowVersion,
                battle.CurrentRound.StartedAtUtc,
                null,
                Array.AsReadOnly(required),
                Array.AsReadOnly(submitted),
                Array.AsReadOnly(missing),
                "Open",
                "ServerOwned")
            : null;
        var versions = new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["battle"] = battle.ContentVersion,
                ["encounter"] = battle.EncounterDefinitionId
            });
        var provisional = new BattleActorSnapshot(
            battle.BattleInstanceId,
            engineMode,
            lifecycleState,
            actorState,
            battle.BattleVersion,
            battle.CurrentRoundNumber,
            battle.CurrentRound.RoundVersion,
            commandWindowVersion,
            actorState.ToString(),
            Array.AsReadOnly(participants),
            window,
            lockedCommands,
            resolutionPlan,
            cursor,
            randomState,
            journalSequence,
            outboxSequence,
            finalizationState,
            recoveryState,
            versions,
            "legacy-frozen-v1",
            battle.CreatedAtUtc,
            updatedAtUtc,
            battle.CompletedAtUtc,
            battle.CorrelationId,
            "");
        return provisional with { CanonicalHash = BattleArchitectureV2Hash.Canonical(provisional) };
    }
}

public sealed class BattleReconnectSnapshotProvider : IBattleReconnectSnapshotProvider
{
    public BattleReconnectSnapshot Create(BattleActorSnapshot snapshot)
    {
        var provisional = new BattleReconnectSnapshot(
            snapshot.BattleId,
            snapshot.BattleState,
            snapshot.BattleVersion,
            snapshot.RoundNumber,
            snapshot.RoundVersion,
            snapshot.CommandWindowVersion,
            snapshot.CurrentPhase,
            snapshot.Participants,
            snapshot.CommandWindow,
            snapshot.LockedCommands,
            snapshot.ActionCursor,
            snapshot.OutboxSequence + 1,
            snapshot.OutboxSequence,
            BattleArchitectureV2Hash.SafeId(snapshot.RngState.CanonicalHash),
            snapshot.FinalizationState,
            snapshot.RecoveryState,
            snapshot.ContentVersions,
            snapshot.UpdatedAtUtc,
            "");
        return provisional with { CanonicalHash = BattleArchitectureV2Hash.Canonical(provisional) };
    }
}

public sealed class DeterministicBattleTimeoutPolicy : IBattleTimeoutPolicy
{
    public IReadOnlyList<Guid> FindMissingParticipants(BattleActorSnapshot snapshot) =>
        snapshot.CommandWindow?.MissingParticipantIds ?? Array.Empty<Guid>();
}

public sealed class DeterministicBattleAutoCommandProvider : IBattleAutoCommandProvider
{
    public BattleActorCommandEnvelope CreateDefault(
        BattleActorSnapshot snapshot,
        BattleActorParticipantSnapshot participant,
        DateTimeOffset now)
    {
        var commandId = BattleArchitectureV2Hash.StableGuid(
            snapshot.BattleId.ToString("N"),
            snapshot.RoundNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.CommandWindowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            participant.ParticipantId.ToString("N"),
            "timeout");
        var idempotency = $"timeout:{snapshot.BattleId:N}:{snapshot.RoundNumber}:{snapshot.CommandWindowVersion}:{participant.ParticipantId:N}";
        var candidate = new BattleActorCommandEnvelope(
            commandId,
            snapshot.BattleId,
            participant.ParticipantId,
            participant.CharacterId,
            participant.SessionSafeReference,
            participant.SessionEpoch,
            snapshot.RoundNumber,
            snapshot.CommandWindowVersion,
            0,
            BattleActorCommandType.TimeoutDefault,
            null,
            null,
            Array.AsReadOnly(Array.Empty<Guid>()),
            null,
            snapshot.BattleVersion,
            idempotency,
            "",
            BattleActorCommandSource.TimeoutPolicy,
            now,
            snapshot.CorrelationId,
            """{"policy":"DeterministicPass"}""");
        return candidate with { PayloadHash = BattleArchitectureV2Hash.Canonical(candidate) };
    }
}

public sealed class PerBattleDeadlineScheduler : IBattleDeadlineScheduler
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _scheduled = [];

    public async Task ScheduleAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        DateTimeOffset deadline,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        var key = $"{battleId:N}:{roundNumber}:{commandWindowVersion}";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_scheduled.TryAdd(key, linked))
        {
            return;
        }

        try
        {
            var delay = deadline - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, linked.Token);
            }

            await callback(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            _scheduled.TryRemove(key, out _);
        }
    }
}

public sealed class InMemoryBattleActorDurabilityStore : IBattleActorDurabilityStore
{
    private readonly BattleActorOptions _options;
    private readonly ConcurrentDictionary<(Guid BattleId, long Sequence), BattleJournalEntry> _journal = [];
    private readonly ConcurrentDictionary<Guid, SortedDictionary<long, BattleRoundCheckpoint>> _checkpoints = [];
    private readonly ConcurrentDictionary<(Guid BattleId, long Sequence), BattleEventEnvelope> _outbox = [];
    private readonly ConcurrentDictionary<(Guid BattleId, long Sequence, BattleEventDeliveryCategory Consumer), BattleOutboxDelivery> _deliveries = [];
    private readonly ConcurrentDictionary<string, (string PayloadHash, BattleCommandResult Result)> _commands = [];
    private readonly ConcurrentDictionary<(Guid BattleId, int Round), LockedRoundCommandSet> _locks = [];
    private readonly ConcurrentDictionary<(Guid BattleId, int Round), RoundResolutionPlan> _plans = [];
    private readonly ConcurrentDictionary<(Guid BattleId, Guid ActionId), BattleActionResult> _actionResults = [];
    private readonly ConcurrentDictionary<Guid, (string PayloadHash, BattleFinalizationResult Result)> _finalizations = [];

    public InMemoryBattleActorDurabilityStore(BattleActorOptions? options = null)
    {
        _options = options ?? BattleActorOptions.Default;
    }

    public IReadOnlyList<BattleJournalEntry> JournalSnapshot =>
        _journal.Values.OrderBy(value => value.BattleId).ThenBy(value => value.JournalSequence).ToArray();

    public IReadOnlyList<BattleEventEnvelope> OutboxSnapshot =>
        _outbox.Values.OrderBy(value => value.BattleId).ThenBy(value => value.EventSequence).ToArray();

    public Task<(BattleActorResultCode Code, BattleJournalEntry? Entry)> AppendAsync(
        BattleJournalEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(entry.CanonicalPayload) > _options.MaximumJournalPayloadBytes)
        {
            return Task.FromResult((BattleActorResultCode.PersistenceFailure, (BattleJournalEntry?)null));
        }

        var key = (entry.BattleId, entry.JournalSequence);
        if (_journal.TryGetValue(key, out var current))
        {
            return Task.FromResult(string.Equals(current.PayloadHash, entry.PayloadHash, StringComparison.Ordinal)
                ? (BattleActorResultCode.DuplicateCompleted, (BattleJournalEntry?)current)
                : (BattleActorResultCode.ReplayConflict, (BattleJournalEntry?)current));
        }

        if (_journal.Keys.Any(value => value.BattleId == entry.BattleId && value.Sequence >= entry.JournalSequence))
        {
            return Task.FromResult((BattleActorResultCode.ReplayConflict, (BattleJournalEntry?)null));
        }

        _journal[key] = entry;
        return Task.FromResult((BattleActorResultCode.Success, (BattleJournalEntry?)entry));
    }

    public Task<IReadOnlyList<BattleJournalEntry>> ReadAfterAsync(
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<BattleJournalEntry> result = _journal.Values
            .Where(value => value.BattleId == battleId && value.JournalSequence > sequence)
            .OrderBy(value => value.JournalSequence)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<BattleActorResultCode> SaveCheckpointAsync(
        BattleRoundCheckpoint checkpoint,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(
                System.Text.Json.JsonSerializer.Serialize(checkpoint.Snapshot)) >
            _options.MaximumCheckpointPayloadBytes)
        {
            return Task.FromResult(BattleActorResultCode.PersistenceFailure);
        }

        var checkpoints = _checkpoints.GetOrAdd(checkpoint.BattleId, _ => new SortedDictionary<long, BattleRoundCheckpoint>());
        lock (checkpoints)
        {
            var latest = checkpoints.Count == 0 ? 0 : checkpoints.Keys.Max();
            if (latest != expectedVersion)
            {
                if (checkpoints.TryGetValue(checkpoint.CheckpointVersion, out var duplicate) &&
                    string.Equals(duplicate.PayloadHash, checkpoint.PayloadHash, StringComparison.Ordinal))
                {
                    return Task.FromResult(BattleActorResultCode.DuplicateCompleted);
                }

                return Task.FromResult(BattleActorResultCode.VersionConflict);
            }

            checkpoints[checkpoint.CheckpointVersion] = checkpoint;
        }

        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<BattleRoundCheckpoint?> LoadLatestValidAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_checkpoints.TryGetValue(battleId, out var checkpoints))
        {
            return Task.FromResult<BattleRoundCheckpoint?>(null);
        }

        lock (checkpoints)
        {
            return Task.FromResult<BattleRoundCheckpoint?>(
                checkpoints.Values.Where(value => value.IsValid).LastOrDefault());
        }
    }

    public Task<(BattleActorResultCode Code, BattleEventEnvelope? Event)> AppendEventAsync(
        BattleEventEnvelope battleEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(battleEvent.SemanticPayload) > _options.MaximumOutboxPayloadBytes)
        {
            return Task.FromResult((BattleActorResultCode.PersistenceFailure, (BattleEventEnvelope?)null));
        }

        var key = (battleEvent.BattleId, battleEvent.EventSequence);
        if (_outbox.TryGetValue(key, out var current))
        {
            return Task.FromResult(
                BattleArchitectureV2Hash.Canonical(current) == BattleArchitectureV2Hash.Canonical(battleEvent)
                    ? (BattleActorResultCode.DuplicateCompleted, (BattleEventEnvelope?)current)
                    : (BattleActorResultCode.ReplayConflict, (BattleEventEnvelope?)current));
        }

        if (_outbox.Keys.Any(value => value.BattleId == battleEvent.BattleId && value.Sequence >= battleEvent.EventSequence))
        {
            return Task.FromResult((BattleActorResultCode.ReplayConflict, (BattleEventEnvelope?)null));
        }

        _outbox[key] = battleEvent;
        return Task.FromResult((BattleActorResultCode.Success, (BattleEventEnvelope?)battleEvent));
    }

    public Task<BattleActorResultCode> AcknowledgeAsync(
        Guid battleId,
        long eventSequence,
        BattleEventDeliveryCategory consumer,
        string payloadHash,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_outbox.TryGetValue((battleId, eventSequence), out var battleEvent) ||
            !string.Equals(BattleArchitectureV2Hash.Canonical(battleEvent), payloadHash, StringComparison.Ordinal))
        {
            return Task.FromResult(BattleActorResultCode.ReplayConflict);
        }

        var key = (battleId, eventSequence, consumer);
        var delivery = new BattleOutboxDelivery(
            battleId,
            eventSequence,
            consumer,
            BattleOutboxDispatchState.Dispatched,
            0,
            "",
            acknowledgedAtUtc);
        if (!_deliveries.TryAdd(key, delivery))
        {
            return Task.FromResult(BattleActorResultCode.DuplicateCompleted);
        }

        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<IReadOnlyList<BattleEventEnvelope>> ReadPendingAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<BattleEventEnvelope> result = _outbox.Values
            .Where(value => value.BattleId == battleId)
            .Where(value => value.DeliveryCategories.Any(category =>
                !_deliveries.ContainsKey((battleId, value.EventSequence, category))))
            .OrderBy(value => value.EventSequence)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<BattleCommandResult?> FindCommandAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BattleArchitectureV2Hash.SafeId(idempotencyKey);
        if (!_commands.TryGetValue(key, out var value))
        {
            return Task.FromResult<BattleCommandResult?>(null);
        }

        return Task.FromResult<BattleCommandResult?>(
            string.Equals(value.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? value.Result
                : value.Result with
                {
                    Code = BattleActorResultCode.ReplayConflict,
                    IsDuplicate = false,
                    FailureCode = "battle.actor.command_replay_conflict"
                });
    }

    public Task<BattleActorResultCode> SaveCommandAsync(
        BattleActorCommandEnvelope command,
        BattleCommandResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BattleArchitectureV2Hash.SafeId(command.IdempotencyKey);
        if (_commands.TryGetValue(key, out var current))
        {
            return Task.FromResult(
                string.Equals(current.PayloadHash, command.PayloadHash, StringComparison.Ordinal)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict);
        }

        _commands[key] = (command.PayloadHash, result);
        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<LockedRoundCommandSet?> LoadCommandLockAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_locks.GetValueOrDefault((battleId, roundNumber)));
    }

    public Task<BattleActorResultCode> SaveCommandLockAsync(
        LockedRoundCommandSet commandLock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (commandLock.BattleId, commandLock.RoundNumber);
        if (_locks.TryGetValue(key, out var current))
        {
            return Task.FromResult(
                string.Equals(current.CanonicalHash, commandLock.CanonicalHash, StringComparison.Ordinal)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict);
        }

        _locks[key] = commandLock;
        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<RoundResolutionPlan?> LoadRoundPlanAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_plans.GetValueOrDefault((battleId, roundNumber)));
    }

    public Task<BattleActorResultCode> SaveRoundPlanAsync(
        RoundResolutionPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (plan.BattleId, plan.RoundNumber);
        if (_plans.TryGetValue(key, out var current))
        {
            return Task.FromResult(
                string.Equals(current.PlanHash, plan.PlanHash, StringComparison.Ordinal)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict);
        }

        _plans[key] = plan;
        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<BattleActorResultCode> SaveActionResultAsync(
        Guid battleId,
        int roundNumber,
        BattleActionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (battleId, result.ActionId);
        if (_actionResults.TryGetValue(key, out var current))
        {
            return Task.FromResult(
                BattleArchitectureV2Hash.Canonical(current) == BattleArchitectureV2Hash.Canonical(result)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict);
        }

        _actionResults[key] = result;
        return Task.FromResult(BattleActorResultCode.Success);
    }

    public Task<BattleActionResult?> LoadActionResultAsync(
        Guid battleId,
        Guid actionExecutionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_actionResults.GetValueOrDefault((battleId, actionExecutionId)));
    }

    public Task<BattleFinalizationResult?> FindFinalizationAsync(
        Guid battleId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_finalizations.TryGetValue(battleId, out var current))
        {
            return Task.FromResult<BattleFinalizationResult?>(null);
        }

        return Task.FromResult<BattleFinalizationResult?>(
            string.Equals(current.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? current.Result
                : current.Result with
                {
                    Code = BattleActorResultCode.ReplayConflict,
                    IsDuplicate = false,
                    FailureCode = "battle.actor.finalization_replay_conflict"
                });
    }

    public Task<BattleActorResultCode> SaveFinalizationAsync(
        BattleFinalizationPlan plan,
        BattleFinalizationResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_finalizations.TryGetValue(plan.BattleId, out var current))
        {
            return Task.FromResult(
                string.Equals(current.PayloadHash, plan.PayloadHash, StringComparison.Ordinal)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict);
        }

        _finalizations[plan.BattleId] = (plan.PayloadHash, result);
        return Task.FromResult(BattleActorResultCode.Success);
    }
}
