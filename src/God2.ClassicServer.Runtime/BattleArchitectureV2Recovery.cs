using System.Collections.Concurrent;

namespace God2.ClassicServer.Runtime;

public sealed class ExistingBattleFinalizationPlanFactory : IBattleFinalizationPlanFactory
{
    private readonly IQuestSemanticEventAdapter? _questAdapter;

    public ExistingBattleFinalizationPlanFactory(IQuestSemanticEventAdapter? questAdapter = null)
    {
        _questAdapter = questAdapter;
    }

    public BattleFinalizationPlan Create(BattleInstance battle, DateTimeOffset now)
    {
        var eligibleCharacters = battle.Participants
            .Where(value => value.Side == BattleSide.PlayerSide && value.CharacterId is not null)
            .Select(value => value.CharacterId!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        List<QuestSemanticEvent> questEvents = [];
        if (_questAdapter is not null)
        {
            foreach (var characterId in eligibleCharacters)
            {
                var semanticId = BattleArchitectureV2Hash.StableGuid(
                    battle.BattleInstanceId.ToString("N"),
                    characterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "quest-completed");
                var adapted = _questAdapter.FromBattleCompletion(
                    semanticId,
                    battle.BattleInstanceId,
                    characterId,
                    battle.EncounterDefinitionId,
                    battle.WinnerSide == BattleWinnerSide.PlayerSide,
                    committed: true,
                    BattleArchitectureV2Hash.SafeId($"character:{characterId}"),
                    now,
                    battle.CorrelationId);
                if (adapted.Succeeded && adapted.Value is not null)
                {
                    questEvents.Add(adapted.Value);
                }
            }
        }

        var candidate = new BattleFinalizationPlan(
            BattleArchitectureV2Hash.StableGuid(battle.BattleInstanceId.ToString("N"), "finalization"),
            battle.BattleInstanceId,
            battle.WinnerSide,
            Array.AsReadOnly(eligibleCharacters),
            Array.AsReadOnly(battle.Participants
                .Where(value => value.Side == BattleSide.EnemySide && !value.IsAlive)
                .OrderBy(value => value.ParticipantId)
                .Select(value => $"participant:{value.ParticipantId:N}:monster:{value.MonsterTemplateId?.ToString() ?? "unknown"}")
                .ToArray()),
            RewardEligible: battle.WinnerSide == BattleWinnerSide.PlayerSide,
            ExistingRewardPlanReferences: Array.AsReadOnly(new[]
            {
                $"battle-reward:{battle.BattleInstanceId:N}"
            }),
            QuestSemanticEvents: Array.AsReadOnly(questEvents.ToArray()),
            CharacterProgressionBoundary: "Unsupported",
            CaptureBoundary: "Unsupported",
            ExpectedBattleVersion: battle.BattleVersion,
            IdempotencyKey: $"battle-finalization:{battle.BattleInstanceId:N}",
            PayloadHash: "",
            ContentVersions: new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["battle"] = battle.ContentVersion,
                    ["formula"] = "legacy-frozen-v1"
                }),
            CreatedAtUtc: now,
            CorrelationId: battle.CorrelationId);
        return candidate with { PayloadHash = BattleArchitectureV2Hash.Canonical(candidate) };
    }
}

public sealed class InMemoryBattleWorldResumeCoordinator : IBattleWorldResumeCoordinator
{
    private readonly ConcurrentDictionary<string, byte> _completed = [];

    public int CompletionCount => _completed.Count;

    public Task<BattleActorResultCode> ResumeAsync(
        Guid battleId,
        IReadOnlyList<long> characterIds,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BattleArchitectureV2Hash.SafeId(
            $"{battleId:N}:{idempotencyKey}:{string.Join(",", characterIds.OrderBy(value => value))}");
        return Task.FromResult(
            _completed.TryAdd(key, 0)
                ? BattleActorResultCode.Success
                : BattleActorResultCode.DuplicateCompleted);
    }
}

public sealed class BattleFinalizationCoordinatorV2 : IBattleFinalizationCoordinator
{
    private readonly IBattleInstanceRepository _battles;
    private readonly IBattleRewardPolicy _rewardPolicy;
    private readonly IBattleRewardCoordinator _rewards;
    private readonly IQuestEventRouter? _questRouter;
    private readonly IBattleWorldResumeCoordinator _worldResume;
    private readonly IBattleFinalizationStore _store;
    private readonly BattleArchitectureFailureInjection _failureInjection;
    private readonly AsyncKeyedLock<Guid> _locks = new();

    public BattleFinalizationCoordinatorV2(
        IBattleInstanceRepository battles,
        IBattleRewardPolicy rewardPolicy,
        IBattleRewardCoordinator rewards,
        IBattleWorldResumeCoordinator worldResume,
        IBattleFinalizationStore store,
        IQuestEventRouter? questRouter = null,
        BattleArchitectureFailureInjection? failureInjection = null)
    {
        _battles = battles;
        _rewardPolicy = rewardPolicy;
        _rewards = rewards;
        _questRouter = questRouter;
        _worldResume = worldResume;
        _store = store;
        _failureInjection = failureInjection ?? new BattleArchitectureFailureInjection();
    }

    public async Task<BattleFinalizationResult> FinalizeAsync(
        BattleFinalizationPlan plan,
        CancellationToken cancellationToken)
    {
        var replay = await _store.FindFinalizationAsync(
            plan.BattleId,
            plan.PayloadHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay.Code == BattleActorResultCode.ReplayConflict
                ? replay
                : replay with
                {
                    Code = BattleActorResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
        }

        using var gate = await _locks.AcquireAsync(plan.BattleId, cancellationToken);
        replay = await _store.FindFinalizationAsync(
            plan.BattleId,
            plan.PayloadHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay.Code == BattleActorResultCode.ReplayConflict
                ? replay
                : replay with
                {
                    Code = BattleActorResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
        }

        var battle = await _battles.GetAsync(plan.BattleId, cancellationToken);
        if (battle is null)
        {
            return Failure(plan, "battle.finalization.battle_missing");
        }

        if (battle.BattleVersion > plan.ExpectedBattleVersion + 1)
        {
            return Failure(plan, "battle.finalization.version_conflict", BattleActorResultCode.VersionConflict);
        }

        var rewardCommitted = !plan.RewardEligible;
        if (plan.RewardEligible)
        {
            if (_failureInjection.Point == BattleArchitectureFailurePoint.RewardFinalization)
            {
                return Recovery(plan, "battle.finalization.reward_injected");
            }

            var rewardPlan = _rewardPolicy.CreatePlan(battle, plan.CreatedAtUtc);
            var reward = await _rewards.FinalizeAsync(rewardPlan, cancellationToken);
            if (!reward.Committed && reward.Code != BattleResultCode.DuplicateCompleted)
            {
                return Recovery(
                    plan,
                    string.IsNullOrWhiteSpace(reward.FailureCode)
                        ? "battle.finalization.reward_failed"
                        : reward.FailureCode);
            }

            rewardCommitted = true;
        }

        var questCommitted = true;
        if (_questRouter is not null)
        {
            if (_failureInjection.Point == BattleArchitectureFailurePoint.QuestDispatch)
            {
                return Recovery(plan, "battle.finalization.quest_injected", rewardCommitted);
            }

            foreach (var semanticEvent in plan.QuestSemanticEvents.OrderBy(value => value.SemanticEventId))
            {
                var routed = await _questRouter.RouteAsync(semanticEvent, cancellationToken);
                if (routed.Any(value => value.Code is
                        QuestResultCode.PersistenceFailure or
                        QuestResultCode.RecoveryRequired or
                        QuestResultCode.InternalFailure))
                {
                    questCommitted = false;
                    break;
                }
            }
        }

        if (!questCommitted)
        {
            return Recovery(plan, "battle.finalization.quest_failed", rewardCommitted);
        }

        if (_failureInjection.Point == BattleArchitectureFailurePoint.WorldResume)
        {
            return Recovery(plan, "battle.finalization.world_resume_injected", rewardCommitted, questCommitted);
        }

        var resumed = await _worldResume.ResumeAsync(
            plan.BattleId,
            plan.EligibleCharacterIds,
            $"{plan.IdempotencyKey}:world-resume",
            cancellationToken);
        if (resumed is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
        {
            return Recovery(plan, "battle.finalization.world_resume_failed", rewardCommitted, questCommitted);
        }

        var completed = new BattleFinalizationResult(
            BattleActorResultCode.Success,
            plan.FinalizationPlanId,
            plan.BattleId,
            BattleFinalizationStateCode.Completed,
            rewardCommitted,
            questCommitted,
            true,
            false,
            "",
            DateTimeOffset.UtcNow);
        var saved = await _store.SaveFinalizationAsync(plan, completed, cancellationToken);
        return saved switch
        {
            BattleActorResultCode.Success => completed,
            BattleActorResultCode.DuplicateCompleted => completed with
            {
                Code = BattleActorResultCode.DuplicateCompleted,
                IsDuplicate = true
            },
            BattleActorResultCode.ReplayConflict => completed with
            {
                Code = BattleActorResultCode.ReplayConflict,
                State = BattleFinalizationStateCode.RecoveryRequired,
                FailureCode = "battle.finalization.replay_conflict"
            },
            _ => Recovery(
                plan,
                "battle.finalization.persistence_failed",
                rewardCommitted,
                questCommitted,
                worldResumeCommitted: true)
        };
    }

    private static BattleFinalizationResult Failure(
        BattleFinalizationPlan plan,
        string code,
        BattleActorResultCode resultCode = BattleActorResultCode.BattleNotFound) =>
        new(
            resultCode,
            plan.FinalizationPlanId,
            plan.BattleId,
            BattleFinalizationStateCode.Failed,
            false,
            false,
            false,
            false,
            code,
            DateTimeOffset.UtcNow);

    private static BattleFinalizationResult Recovery(
        BattleFinalizationPlan plan,
        string code,
        bool rewardCommitted = false,
        bool questCommitted = false,
        bool worldResumeCommitted = false) =>
        new(
            BattleActorResultCode.RecoveryRequired,
            plan.FinalizationPlanId,
            plan.BattleId,
            BattleFinalizationStateCode.RecoveryRequired,
            rewardCommitted,
            questCommitted,
            worldResumeCommitted,
            false,
            code,
            DateTimeOffset.UtcNow);
}

public sealed class BattleActorRecoveryCoordinator : IBattleActorRecoveryCoordinator
{
    private readonly IBattleActorRegistry _registry;
    private readonly AsyncKeyedLock<Guid> _locks = new();

    public BattleActorRecoveryCoordinator(IBattleActorRegistry registry)
    {
        _registry = registry;
    }

    public async Task<BattleRecoveryResult> RecoverAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(plan.BattleId, out var actor) || actor is null)
        {
            return new BattleRecoveryResult(
                BattleActorResultCode.BattleNotFound,
                plan.BattleId,
                BattleActorRecoveryStateCode.RecoveryRequired,
                0,
                0,
                BattleActorStateCode.RecoveryRequired,
                false,
                "battle.actor.recovery_actor_missing");
        }

        using var gate = await _locks.AcquireAsync(plan.BattleId, cancellationToken);
        return await actor.RecoverAsync(plan, cancellationToken);
    }
}

public sealed class BattleReplayRunner
{
    public async Task<BattleReplayResult> RunAsync(
        BattleReplayArtifact artifact,
        IBattleReplayExecutor executor,
        CancellationToken cancellationToken)
    {
        if (artifact.SchemaVersion != 1)
        {
            return Failure(artifact, BattleReplayResultCode.UnsupportedSchema, "battle.replay.schema_unsupported");
        }

        if (!string.Equals(
                artifact.RngAlgorithmVersion,
                artifact.InitialRngState.AlgorithmVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                artifact.RngAlgorithmVersion,
                XorShift64StarBattleRandom.Version,
                StringComparison.Ordinal))
        {
            return Failure(artifact, BattleReplayResultCode.VersionUnavailable, "battle.replay.rng_version_unavailable");
        }

        var requiredVersions = new[]
        {
            "skill",
            "status",
            "monster",
            "formation",
            "ai"
        };
        if (requiredVersions.Any(key =>
                !artifact.ContentVersions.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value)))
        {
            return Failure(artifact, BattleReplayResultCode.VersionUnavailable, "battle.replay.content_version_unavailable");
        }

        BattleReplayOutput output;
        try
        {
            output = await executor.ExecuteAsync(artifact, cancellationToken);
        }
        catch (Exception exception)
        {
            return Failure(
                artifact,
                BattleReplayResultCode.ExecutionFailure,
                $"battle.replay.execution_failed:{exception.GetType().Name}");
        }

        var finalMatch = string.Equals(
            artifact.ExpectedFinalSnapshotHash,
            output.FinalSnapshotHash,
            StringComparison.Ordinal);
        var eventMatch = string.Equals(
            artifact.ExpectedEventStreamHash,
            output.EventStreamHash,
            StringComparison.Ordinal);
        var firstDivergence = FindFirstDivergence(
            artifact.ExpectedOrderedEvents,
            output.OrderedEvents);
        return new BattleReplayResult(
            finalMatch && eventMatch && firstDivergence is null
                ? BattleReplayResultCode.Match
                : BattleReplayResultCode.Divergence,
            artifact.FixtureId,
            artifact.ExpectedFinalSnapshotHash,
            output.FinalSnapshotHash,
            artifact.ExpectedEventStreamHash,
            output.EventStreamHash,
            firstDivergence,
            finalMatch && eventMatch && firstDivergence is null
                ? ""
                : "battle.replay.divergence",
            DatabaseMutation: false,
            RewardSideEffect: false,
            QuestSideEffect: false,
            NetworkBytes: 0);
    }

    private static long? FindFirstDivergence(
        IReadOnlyList<BattleEventEnvelope> expected,
        IReadOnlyList<BattleEventEnvelope> actual)
    {
        var maximum = Math.Max(expected.Count, actual.Count);
        for (var index = 0; index < maximum; index++)
        {
            if (index >= expected.Count || index >= actual.Count)
            {
                return index + 1;
            }

            if (!string.Equals(
                    BattleArchitectureV2Hash.Canonical(expected[index]),
                    BattleArchitectureV2Hash.Canonical(actual[index]),
                    StringComparison.Ordinal))
            {
                return Math.Min(expected[index].EventSequence, actual[index].EventSequence);
            }
        }

        return null;
    }

    private static BattleReplayResult Failure(
        BattleReplayArtifact artifact,
        BattleReplayResultCode code,
        string failureCode) =>
        new(
            code,
            artifact.FixtureId,
            artifact.ExpectedFinalSnapshotHash,
            "",
            artifact.ExpectedEventStreamHash,
            "",
            null,
            failureCode,
            false,
            false,
            false,
            0);
}

public sealed class DelegateBattleReplayExecutor : IBattleReplayExecutor
{
    private readonly Func<BattleReplayArtifact, CancellationToken, Task<BattleReplayOutput>> _execute;

    public DelegateBattleReplayExecutor(
        Func<BattleReplayArtifact, CancellationToken, Task<BattleReplayOutput>> execute)
    {
        _execute = execute;
    }

    public Task<BattleReplayOutput> ExecuteAsync(
        BattleReplayArtifact artifact,
        CancellationToken cancellationToken) =>
        _execute(artifact, cancellationToken);
}

public sealed class BattleEngineDifferentialRunner
{
    public async Task<BattleDifferentialResult> RunAsync(
        BattleReplayArtifact artifact,
        IBattleReplayExecutor legacy,
        IBattleReplayExecutor actor,
        CancellationToken cancellationToken)
    {
        var legacyOutput = await legacy.ExecuteAsync(artifact, cancellationToken);
        var actorOutput = await actor.ExecuteAsync(artifact, cancellationToken);
        var finalMatch = string.Equals(
            legacyOutput.FinalSnapshotHash,
            actorOutput.FinalSnapshotHash,
            StringComparison.Ordinal);
        var eventMatch = string.Equals(
            legacyOutput.EventStreamHash,
            actorOutput.EventStreamHash,
            StringComparison.Ordinal);
        var first = FirstDifference(legacyOutput.OrderedEvents, actorOutput.OrderedEvents);
        var divergenceCount = (finalMatch ? 0 : 1) + (eventMatch ? 0 : 1) + (first is null ? 0 : 1);
        return new BattleDifferentialResult(
            divergenceCount == 0 ? BattleReplayResultCode.Match : BattleReplayResultCode.Divergence,
            artifact.FixtureId,
            legacyOutput.FinalSnapshotHash,
            actorOutput.FinalSnapshotHash,
            legacyOutput.EventStreamHash,
            actorOutput.EventStreamHash,
            first,
            !finalMatch ? "FinalStateHash" : !eventMatch ? "OrderedEventHash" : "",
            divergenceCount,
            ShadowExternalSideEffects: false,
            divergenceCount == 0 ? "" : "battle.differential.divergence");
    }

    private static long? FirstDifference(
        IReadOnlyList<BattleEventEnvelope> left,
        IReadOnlyList<BattleEventEnvelope> right)
    {
        var count = Math.Max(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= left.Count || index >= right.Count ||
                !string.Equals(
                    BattleArchitectureV2Hash.Canonical(left[index]),
                    BattleArchitectureV2Hash.Canonical(right[index]),
                    StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return null;
    }
}

public sealed class EvidenceBlockedGod2BattleProtocolAdapter : IGod2BattleProtocolAdapter
{
    public BattleActorResultCode TryCreateSemanticCommand(
        ReadOnlyMemory<byte> packet,
        string clientBuild,
        out BattleActorCommandEnvelope? command,
        out string failureCode)
    {
        command = null;
        failureCode = "battle.protocol.command_blocked_by_evidence";
        return BattleActorResultCode.EvidenceBlocked;
    }

    public BattleActorResultCode TrySerializeEvents(
        IReadOnlyList<BattleEventEnvelope> events,
        string clientBuild,
        out byte[] networkBytes,
        out string failureCode)
    {
        networkBytes = [];
        failureCode = "battle.protocol.serializer_blocked_by_evidence";
        return BattleActorResultCode.EvidenceBlocked;
    }
}

public sealed class InMemoryBattleEventDispatcher : IBattleEventDispatcher
{
    private readonly IBattleEventOutbox _outbox;
    private readonly ConcurrentDictionary<string, BattleEventEnvelope> _dispatched = [];

    public InMemoryBattleEventDispatcher(IBattleEventOutbox outbox)
    {
        _outbox = outbox;
    }

    public int DispatchCount => _dispatched.Count;

    public async Task<BattleActorResultCode> DispatchAsync(
        BattleEventEnvelope battleEvent,
        CancellationToken cancellationToken)
    {
        foreach (var category in battleEvent.DeliveryCategories.OrderBy(value => value))
        {
            var key = $"{battleEvent.BattleId:N}:{battleEvent.EventSequence}:{category}";
            if (!_dispatched.TryAdd(key, battleEvent))
            {
                continue;
            }

            var acknowledged = await _outbox.AcknowledgeAsync(
                battleEvent.BattleId,
                battleEvent.EventSequence,
                category,
                BattleArchitectureV2Hash.Canonical(battleEvent),
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (acknowledged is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
            {
                _dispatched.TryRemove(key, out _);
                return acknowledged;
            }
        }

        return BattleActorResultCode.Success;
    }
}

public sealed class BattleActorInspector : IBattleActorInspectorSource
{
    private readonly IBattleActorRegistry _registry;
    private readonly InMemoryBattleActorDurabilityStore? _memoryStore;
    private readonly ConcurrentQueue<BattleDifferentialResult> _differentials = [];
    private readonly BattleArchitectureFailureInjection _failureInjection;

    public BattleActorInspector(
        IBattleActorRegistry registry,
        InMemoryBattleActorDurabilityStore? memoryStore = null,
        BattleArchitectureFailureInjection? failureInjection = null)
    {
        _registry = registry;
        _memoryStore = memoryStore;
        _failureInjection = failureInjection ?? new BattleArchitectureFailureInjection();
    }

    public void Record(BattleDifferentialResult result) =>
        _differentials.Enqueue(result);

    public BattleActorInspectorSnapshot Capture(BattleActorInspectorQuery query)
    {
        try
        {
            if (_failureInjection.Point == BattleArchitectureFailurePoint.Inspector)
            {
                throw new InvalidOperationException("Injected inspector failure.");
            }

            var limit = Math.Clamp(query.Limit, 1, 500);
            var offset = Math.Max(0, query.Offset);
            var actors = _registry.Snapshot()
                .Where(value => query.BattleId is null || value.BattleId == query.BattleId)
                .Where(value => query.BattleState is null || value.BattleState == query.BattleState)
                .Where(value => !query.ActiveOnly || value.LifecycleState == BattleActorLifecycleState.Running)
                .Where(value => !query.CompletedOnly || value.BattleState == BattleActorStateCode.Completed)
                .Where(value => !query.RecoveryRequiredOnly || value.BattleState == BattleActorStateCode.RecoveryRequired)
                .Where(value => !query.FaultedOnly || value.LifecycleState == BattleActorLifecycleState.Faulted)
                .Where(value => !query.MailboxNearCapacityOnly ||
                                value.MailboxDepth >= Math.Max(1, value.MailboxCapacity * 8 / 10))
                .Where(value => !query.OutboxPendingOnly || value.PendingOutboxCount > 0)
                .Where(value => !query.FinalizationPendingOnly || !value.FinalizationTerminal)
                .ToArray();
            var page = actors.Skip(offset).Take(limit).ToArray();
            var journal = _memoryStore?.JournalSnapshot
                .Where(value => query.BattleId is null || value.BattleId == query.BattleId)
                .OrderBy(value => value.JournalSequence)
                .Take(limit)
                .ToArray() ?? Array.Empty<BattleJournalEntry>();
            var outbox = _memoryStore?.OutboxSnapshot
                .Where(value => query.BattleId is null || value.BattleId == query.BattleId)
                .OrderBy(value => value.EventSequence)
                .Take(limit)
                .ToArray() ?? Array.Empty<BattleEventEnvelope>();
            var differentials = _differentials.ToArray()
                .Where(value => query.BattleId is null ||
                                string.Equals(value.FixtureId, query.BattleId.Value.ToString("N"), StringComparison.Ordinal))
                .Take(limit)
                .ToArray();
            return new BattleActorInspectorSnapshot(
                Array.AsReadOnly(page),
                Array.AsReadOnly(journal),
                Array.AsReadOnly(outbox),
                Array.AsReadOnly(differentials),
                actors.Length,
                "");
        }
        catch (Exception exception)
        {
            return new BattleActorInspectorSnapshot(
                Array.Empty<BattleActorRegistryItem>(),
                Array.Empty<BattleJournalEntry>(),
                Array.Empty<BattleEventEnvelope>(),
                Array.Empty<BattleDifferentialResult>(),
                0,
                $"battle.actor.inspector_failure:{exception.GetType().Name}");
        }
    }
}
