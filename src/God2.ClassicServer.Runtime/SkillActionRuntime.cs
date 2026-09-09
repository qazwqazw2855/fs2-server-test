using System.Collections.Concurrent;

namespace God2.ClassicServer.Runtime;

public sealed class SkillActionResolver :
    ISkillActionResolver,
    ISkillRecoveryCoordinator
{
    private readonly ISkillDefinitionCatalog _catalog;
    private readonly ISkillAvailabilityPolicy _availability;
    private readonly ISkillTargetResolver _targets;
    private readonly ISkillCostPolicy _costPolicy;
    private readonly ISkillCostReservationStore _costs;
    private readonly ISkillCooldownPolicy _cooldown;
    private readonly ISkillExecutionPlanner _planner;
    private readonly ISkillEffectResolver _effects;
    private readonly ISkillExecutionStore _store;
    private readonly IBattleCombatExecutionPort _combat;
    private readonly IBattleHealthMutationPort _health;
    private readonly ISkillEventSink _events;
    private readonly ISkillAuditLedger _audit;
    private readonly IBattleClock _clock;
    private readonly AsyncKeyedLock<Guid> _locks = new();

    public SkillActionResolver(
        ISkillDefinitionCatalog catalog,
        ISkillAvailabilityPolicy availability,
        ISkillTargetResolver targets,
        ISkillCostPolicy costPolicy,
        ISkillCostReservationStore costs,
        ISkillCooldownPolicy cooldown,
        ISkillExecutionPlanner planner,
        ISkillEffectResolver effects,
        ISkillExecutionStore store,
        IBattleCombatExecutionPort combat,
        IBattleHealthMutationPort health,
        ISkillEventSink events,
        ISkillAuditLedger audit,
        IBattleClock clock)
    {
        _catalog = catalog;
        _availability = availability;
        _targets = targets;
        _costPolicy = costPolicy;
        _costs = costs;
        _cooldown = cooldown;
        _planner = planner;
        _effects = effects;
        _store = store;
        _combat = combat;
        _health = health;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public SkillFailureInjection FailureInjection { get; } = new();

    public async Task<SkillActionResult> ResolveAsync(
        BattleInstance battle,
        BattleLockedAction action,
        int resolutionIndex,
        CancellationToken cancellationToken)
    {
        var skillActionId = BattleRuntimeHash.DeterministicGuid($"skill-action:{action.ActionId:N}");
        var executionId = BattleRuntimeHash.DeterministicGuid(
            $"skill-execution:{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{action.ActionId:N}");
        using var gate = await _locks.AcquireAsync(executionId, cancellationToken);
        Publish(
            SkillEventKind.SkillActionReceived,
            executionId,
            battle,
            action,
            null,
            0,
            SkillExecutionState.Created.ToString(),
            "");
        if (action.ActionType != BattleActionType.Skill || action.SkillDefinitionId is null)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                action.SkillDefinitionId ?? 0,
                SkillResultCode.Rejected,
                "skill.action_invalid");
        }

        if (action.BattleInstanceId != battle.BattleInstanceId)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                action.SkillDefinitionId.Value,
                SkillResultCode.BattleNotFound,
                "skill.battle_mismatch");
        }

        if (action.RoundNumber != battle.CurrentRoundNumber)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                action.SkillDefinitionId.Value,
                SkillResultCode.InvalidRound,
                "skill.invalid_round");
        }

        var payloadHash = PayloadHash(battle, action);
        var existing = _store.Get(executionId);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return Failed(
                    executionId,
                    skillActionId,
                    battle,
                    action,
                    action.SkillDefinitionId.Value,
                    SkillResultCode.ReplayConflict,
                    "skill.replay_conflict");
            }

            if (existing.Result is not null &&
                existing.State == SkillExecutionState.Completed)
            {
                return existing.Result with
                {
                    ResultCode = SkillResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
            }

            var persistedDefinition = ResolveDefinition(
                existing.Plan.SkillDefinitionId,
                battle);
            if (persistedDefinition is null)
            {
                return MissingRecovery(
                    executionId,
                    battle,
                    "skill.persisted_definition_unavailable");
            }

            return await ExecuteAsync(
                battle,
                action,
                persistedDefinition,
                existing,
                cancellationToken);
        }

        var definitionResult = _catalog.Get(
            action.SkillDefinitionId.Value,
            battle.BattleType == BattleType.InternalTest
                ? BattleRequestSource.TrustedInternalTest
                : BattleRequestSource.OfficialClient);
        if (!definitionResult.Succeeded || definitionResult.Value is null)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                action.SkillDefinitionId.Value,
                MapDefinitionFailure(definitionResult.Error.Code),
                definitionResult.Error.Code);
        }

        var definition = definitionResult.Value;
        Publish(
            SkillEventKind.SkillDefinitionResolved,
            executionId,
            battle,
            action,
            null,
            0,
            definition.ActionCategory.ToString(),
            "");
        var rank = _availability.ResolveRank(battle, action, definition);
        if (!rank.Succeeded || rank.Value <= 0)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                MapAvailabilityFailure(rank.Error.Code),
                rank.Error.Code);
        }

        var source = battle.Participants.FirstOrDefault(value => value.ParticipantId == action.ParticipantId);
        if (source is null)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                SkillResultCode.ParticipantNotFound,
                "skill.participant_not_found");
        }

        var targetResult = _targets.Resolve(
            battle,
            source,
            definition,
            action.TargetParticipantIds);
        if (!targetResult.Succeeded || targetResult.Value is null)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                MapTargetFailure(targetResult.Error.Code),
                targetResult.Error.Code);
        }

        Publish(
            SkillEventKind.SkillTargetsResolved,
            executionId,
            battle,
            action,
            null,
            0,
            $"Targets={targetResult.Value.Targets.Count}",
            "");
        var costValidation = _costPolicy.Validate(definition, source, battle.CurrentRoundNumber);
        if (!costValidation.Succeeded)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                SkillResultCode.CostEvidenceBlocked,
                costValidation.Error.Code);
        }

        var cooldownValidation = _cooldown.Validate(
            battle.BattleInstanceId,
            source.ParticipantId,
            definition,
            battle.CurrentRoundNumber);
        if (!cooldownValidation.Succeeded)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                MapCooldownFailure(cooldownValidation.Error.Code),
                cooldownValidation.Error.Code);
        }

        var planned = _planner.Create(
            battle,
            action,
            definition,
            rank.Value,
            targetResult.Value,
            resolutionIndex,
            _clock.UtcNow);
        if (!planned.Succeeded || planned.Value is null)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                SkillResultCode.EffectEvidenceBlocked,
                planned.Error.Code);
        }

        var record = new SkillExecutionRecord(
            planned.Value,
            SkillExecutionState.Planned,
            0,
            null,
            [],
            null,
            payloadHash,
            SkillRecoveryState.NotRequired,
            _clock.UtcNow);
        if (FailureInjection.Point == SkillFailurePoint.PlanPersistence ||
            !_store.Save(record, -1).Succeeded)
        {
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                SkillResultCode.PersistenceFailure,
                "skill.plan_persistence_failed");
        }

        Publish(
            SkillEventKind.SkillExecutionPlanned,
            executionId,
            battle,
            action,
            null,
            0,
            SkillExecutionState.Planned.ToString(),
            "");
        var reserved = _costs.Reserve(planned.Value, _clock.UtcNow);
        if (!reserved.Succeeded || reserved.Value is null)
        {
            return await RejectPersistedAsync(
                battle,
                action,
                definition,
                record,
                reserved.Error.Code == "skill.insufficient_resource"
                    ? SkillResultCode.InsufficientResource
                    : SkillResultCode.CostReservationConflict,
                reserved.Error.Code,
                cancellationToken);
        }

        record = record with
        {
            State = SkillExecutionState.CostReserved,
            CostReservation = reserved.Value,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (!_store.Save(record, 0).Succeeded)
        {
            _ = _costs.Release(reserved.Value.ReservationId, _clock.UtcNow);
            return Failed(
                executionId,
                skillActionId,
                battle,
                action,
                definition.SkillDefinitionId,
                SkillResultCode.PersistenceFailure,
                "skill.reservation_persistence_failed");
        }

        Publish(
            SkillEventKind.SkillCostReserved,
            executionId,
            battle,
            action,
            null,
            0,
            SkillCostReservationState.Reserved.ToString(),
            "");
        return await ExecuteAsync(battle, action, definition, record, cancellationToken);
    }

    public async Task<SkillActionResult> RecoverAsync(
        Guid skillExecutionId,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        using var gate = await _locks.AcquireAsync(skillExecutionId, cancellationToken);
        var record = _store.Get(skillExecutionId);
        if (record is null)
        {
            return MissingRecovery(skillExecutionId, battle, "skill.execution_not_found");
        }

        if (record.Plan.BattleInstanceId != battle.BattleInstanceId ||
            record.Plan.RoundNumber != battle.CurrentRoundNumber)
        {
            return MissingRecovery(skillExecutionId, battle, "skill.recovery_battle_mismatch");
        }

        var definition = ResolveDefinition(record.Plan.SkillDefinitionId, battle);
        if (definition is null)
        {
            return MissingRecovery(
                skillExecutionId,
                battle,
                "skill.persisted_definition_unavailable");
        }

        if (FailureInjection.Point == SkillFailurePoint.Recovery)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                RecreateAction(record),
                definition,
                record,
                "skill.recovery_failed",
                cancellationToken);
        }

        Publish(
            SkillEventKind.SkillRecoveryStarted,
            skillExecutionId,
            battle,
            RecreateAction(record),
            null,
            record.ResolutionCursor,
            record.State.ToString(),
            "");
        var result = await ExecuteAsync(
            battle,
            RecreateAction(record),
            definition,
            record with { RecoveryState = SkillRecoveryState.Recovering },
            cancellationToken);
        Publish(
            result.Succeeded
                ? SkillEventKind.SkillRecoveryCompleted
                : SkillEventKind.SkillRecoveryRequired,
            skillExecutionId,
            battle,
            RecreateAction(record),
            null,
            result.ResolutionCursor,
            result.RecoveryState.ToString(),
            result.FailureCode);
        return result;
    }

    private async Task<SkillActionResult> ExecuteAsync(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord initial,
        CancellationToken cancellationToken)
    {
        var record = initial;
        var participants = ReconcileParticipants(battle.Participants, record.EffectResults);
        if (_combat is IBattleCombatStateAuthority authority)
        {
            authority.Seed(battle.BattleInstanceId, participants);
        }

        Publish(
            SkillEventKind.SkillExecutionStarted,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillExecutionState.Executing.ToString(),
            "");
        for (var cursor = record.ResolutionCursor; cursor < record.Plan.EffectPlans.Count; cursor++)
        {
            var effectPlan = record.Plan.EffectPlans[cursor];
            var existing = record.EffectResults.FirstOrDefault(
                value => value.EffectExecutionId == effectPlan.EffectExecutionId);
            if (existing is not null &&
                existing.State == SkillEffectExecutionState.Committed)
            {
                record = record with { ResolutionCursor = cursor + 1 };
                continue;
            }

            var source = participants.Single(value => value.ParticipantId == record.Plan.ParticipantId);
            var target = participants.Single(value => value.ParticipantId == effectPlan.TargetParticipantId);
            if (!target.IsAlive)
            {
                var dead = RejectedEffect(
                    record,
                    effectPlan,
                    source,
                    target,
                    "skill.target_dead",
                    _clock.UtcNow);
                return record.EffectResults.Any(value => value.State == SkillEffectExecutionState.Committed)
                    ? await MarkRecoveryRequiredAsync(
                        battle,
                        action,
                        definition,
                        record,
                        dead.FailureCode,
                        cancellationToken)
                    : await RejectAndReleaseAsync(
                        battle,
                        action,
                        definition,
                        record,
                        dead,
                        SkillResultCode.TargetDead,
                        cancellationToken);
            }

            Publish(
                SkillEventKind.SkillEffectResolving,
                record.Plan.SkillExecutionId,
                battle,
                action,
                effectPlan.EffectExecutionId,
                cursor,
                effectPlan.EffectType.ToString(),
                "");
            SkillEffectResult effectResult;
            if ((FailureInjection.Point == SkillFailurePoint.DamageEffect &&
                 effectPlan.EffectType == SkillEffectType.Damage) ||
                (FailureInjection.Point == SkillFailurePoint.HealingEffect &&
                 effectPlan.EffectType == SkillEffectType.Heal))
            {
                effectResult = RejectedEffect(
                    record,
                    effectPlan,
                    source,
                    target,
                    "skill.effect_injected_failure",
                    _clock.UtcNow);
            }
            else
            {
                effectResult = await _effects.ResolveAsync(
                    new SkillEffectContext(
                        battle with { Participants = participants },
                        action,
                        definition,
                        record.Plan,
                        effectPlan,
                        source,
                        target,
                        _combat,
                        _health,
                        _clock.UtcNow),
                    cancellationToken);
            }

            if (effectResult.State != SkillEffectExecutionState.Committed)
            {
                if (effectResult.State == SkillEffectExecutionState.RecoveryRequired)
                {
                    return await MarkRecoveryRequiredAsync(
                        battle,
                        action,
                        definition,
                        record,
                        effectResult.FailureCode,
                        cancellationToken);
                }

                var code = effectResult.FailureCode == "skill.effect_evidence_blocked"
                    ? SkillResultCode.EffectEvidenceBlocked
                    : effectResult.FailureCode == "skill.effect_unsupported"
                        ? SkillResultCode.UnsupportedEffect
                        : effectResult.FailureCode.Contains("version", StringComparison.Ordinal)
                            ? SkillResultCode.VersionConflict
                            : SkillResultCode.CombatResolutionFailure;
                return record.EffectResults.Any(value => value.State == SkillEffectExecutionState.Committed)
                    ? await MarkRecoveryRequiredAsync(
                        battle,
                        action,
                        definition,
                        record,
                        effectResult.FailureCode,
                        cancellationToken)
                    : await RejectAndReleaseAsync(
                        battle,
                        action,
                        definition,
                        record,
                        effectResult,
                        code,
                        cancellationToken);
            }

            participants = ApplyEffect(participants, effectResult);
            var effectResults = record.EffectResults.Append(effectResult).ToArray();
            var next = record with
            {
                State = SkillExecutionState.Executing,
                ResolutionCursor = cursor + 1,
                EffectResults = SkillCollections.Freeze(effectResults),
                UpdatedAtUtc = _clock.UtcNow
            };
            if (FailureInjection.Point == SkillFailurePoint.EffectPersistence ||
                !_store.Save(next, record.ResolutionCursor).Succeeded)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    action,
                    definition,
                    record with { EffectResults = SkillCollections.Freeze(effectResults) },
                    "skill.effect_persistence_failed",
                    cancellationToken);
            }

            record = next;
            Publish(
                effectResult.EffectType == SkillEffectType.Damage
                    ? SkillEventKind.SkillDamageApplied
                    : SkillEventKind.SkillHealingApplied,
                record.Plan.SkillExecutionId,
                battle,
                action,
                effectResult.EffectExecutionId,
                record.ResolutionCursor,
                $"Hp={effectResult.HpAfter}",
                "");
            AppendEffectAudit(battle, action, definition, record, effectResult);
            if (FailureInjection.Point == SkillFailurePoint.AfterFirstEffect &&
                record.ResolutionCursor == 1)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    action,
                    definition,
                    record,
                    "skill.crash_after_first_effect",
                    cancellationToken);
            }
        }

        record = record with
        {
            State = SkillExecutionState.EffectsCompleted,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.Save(record, record.ResolutionCursor);
        Publish(
            SkillEventKind.SkillEffectsCompleted,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillExecutionState.EffectsCompleted.ToString(),
            "");
        var reservation = record.CostReservation;
        if (reservation is null)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                action,
                definition,
                record,
                "skill.cost_reservation_missing",
                cancellationToken);
        }

        var committed = _costs.Commit(reservation.ReservationId, _clock.UtcNow);
        if (!committed.Succeeded || committed.Value is null)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                action,
                definition,
                record,
                committed.Error.Code,
                cancellationToken);
        }

        record = record with
        {
            State = SkillExecutionState.CostCommitted,
            CostReservation = committed.Value,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.Save(record, record.ResolutionCursor);
        Publish(
            SkillEventKind.SkillCostCommitted,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillCostReservationState.Committed.ToString(),
            "");
        var usage = _cooldown.Commit(record.Plan, definition);
        if (!usage.Succeeded || usage.Value is null)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                action,
                definition,
                record,
                usage.Error.Code,
                cancellationToken);
        }

        record = record with
        {
            State = SkillExecutionState.CooldownCommitted,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.Save(record, record.ResolutionCursor);
        Publish(
            SkillEventKind.SkillCooldownCommitted,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            $"AvailableAtRound={usage.Value.AvailableAtRound}",
            "");
        var targetResults = AggregateTargets(record.EffectResults);
        var result = new SkillActionResult(
            record.Plan.SkillExecutionId,
            record.Plan.SkillActionId,
            record.Plan.BattleActionId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.ParticipantId,
            record.Plan.SkillDefinitionId,
            SkillResultCode.Success,
            "",
            false,
            CostResult(committed.Value),
            new SkillCooldownResult(
                usage.Value.AvailableAtRound,
                usage.Value.RemainingRounds,
                usage.Value.UsageCount,
                true,
                "",
                usage.Value.PolicyStatus),
            targetResults,
            record.EffectResults,
            battle.BattleVersion,
            checked(battle.BattleVersion + 1),
            record.Plan.ExpectedParticipantVersion,
            record.Plan.ExpectedParticipantVersion,
            record.ResolutionCursor,
            SkillRecoveryState.NotRequired,
            [],
            record.Plan.CreatedAtUtc,
            _clock.UtcNow);
        var completed = record with
        {
            State = SkillExecutionState.Completed,
            Result = result,
            RecoveryState = SkillRecoveryState.NotRequired,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (FailureInjection.Point == SkillFailurePoint.ResultPersistence ||
            !_store.Save(completed, record.ResolutionCursor).Succeeded)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                action,
                definition,
                record,
                "skill.result_persistence_failed",
                cancellationToken);
        }

        AppendCompletionAudit(battle, action, definition, completed, result);
        Publish(
            SkillEventKind.SkillExecutionCompleted,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillExecutionState.Completed.ToString(),
            "");
        return result;
    }

    private async Task<SkillActionResult> RejectAndReleaseAsync(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord record,
        SkillEffectResult effect,
        SkillResultCode code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.CostReservation is not null)
        {
            _ = _costs.Release(record.CostReservation.ReservationId, _clock.UtcNow);
            Publish(
                SkillEventKind.SkillCostReleased,
                record.Plan.SkillExecutionId,
                battle,
                action,
                effect.EffectExecutionId,
                record.ResolutionCursor,
                SkillCostReservationState.Released.ToString(),
                effect.FailureCode);
        }

        return await RejectPersistedAsync(
            battle,
            action,
            definition,
            record with { EffectResults = SkillCollections.Freeze(record.EffectResults.Append(effect)) },
            code,
            effect.FailureCode,
            cancellationToken);
    }

    private async Task<SkillActionResult> RejectPersistedAsync(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord record,
        SkillResultCode code,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var reservation = record.CostReservation;
        var result = new SkillActionResult(
            record.Plan.SkillExecutionId,
            record.Plan.SkillActionId,
            record.Plan.BattleActionId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.ParticipantId,
            record.Plan.SkillDefinitionId,
            code,
            failureCode,
            false,
            reservation is null
                ? EmptyCost(definition)
                : CostResult(_costs.Get(reservation.ReservationId) ?? reservation),
            EmptyCooldown(definition),
            AggregateTargets(record.EffectResults),
            record.EffectResults,
            battle.BattleVersion,
            battle.BattleVersion,
            record.Plan.ExpectedParticipantVersion,
            record.Plan.ExpectedParticipantVersion,
            record.ResolutionCursor,
            SkillRecoveryState.NotRequired,
            [],
            record.Plan.CreatedAtUtc,
            _clock.UtcNow);
        var rejected = record with
        {
            State = SkillExecutionState.Rejected,
            Result = result,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.Save(rejected, record.ResolutionCursor);
        Publish(
            SkillEventKind.SkillActionRejected,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillExecutionState.Rejected.ToString(),
            failureCode);
        return result;
    }

    private async Task<SkillActionResult> MarkRecoveryRequiredAsync(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord record,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var result = new SkillActionResult(
            record.Plan.SkillExecutionId,
            record.Plan.SkillActionId,
            record.Plan.BattleActionId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.ParticipantId,
            record.Plan.SkillDefinitionId,
            SkillResultCode.RecoveryRequired,
            failureCode,
            false,
            record.CostReservation is null
                ? EmptyCost(definition)
                : CostResult(_costs.Get(record.CostReservation.ReservationId) ?? record.CostReservation),
            EmptyCooldown(definition),
            AggregateTargets(record.EffectResults),
            record.EffectResults,
            battle.BattleVersion,
            battle.BattleVersion,
            record.Plan.ExpectedParticipantVersion,
            record.Plan.ExpectedParticipantVersion,
            record.ResolutionCursor,
            SkillRecoveryState.RecoveryRequired,
            [],
            record.Plan.CreatedAtUtc,
            _clock.UtcNow);
        var recovery = record with
        {
            State = SkillExecutionState.RecoveryRequired,
            Result = result,
            RecoveryState = SkillRecoveryState.RecoveryRequired,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.Save(recovery, record.ResolutionCursor);
        Publish(
            SkillEventKind.SkillRecoveryRequired,
            record.Plan.SkillExecutionId,
            battle,
            action,
            null,
            record.ResolutionCursor,
            SkillExecutionState.RecoveryRequired.ToString(),
            failureCode);
        return result;
    }

    private SkillDefinition? ResolveDefinition(int skillDefinitionId, BattleInstance battle)
    {
        var result = _catalog.Get(
            skillDefinitionId,
            battle.BattleType == BattleType.InternalTest
                ? BattleRequestSource.TrustedInternalTest
                : BattleRequestSource.OfficialClient);
        return result.Succeeded ? result.Value : null;
    }

    private static IReadOnlyList<BattleParticipant> ReconcileParticipants(
        IReadOnlyList<BattleParticipant> participants,
        IReadOnlyList<SkillEffectResult> effects)
    {
        var current = participants.ToDictionary(value => value.ParticipantId);
        foreach (var effect in effects
                     .Where(value => value.State == SkillEffectExecutionState.Committed)
                     .OrderBy(value => value.EffectIndex)
                     .ThenBy(value => value.TargetIndex))
        {
            var participant = current[effect.TargetParticipantId];
            current[effect.TargetParticipantId] = participant with
            {
                CurrentHp = effect.HpAfter,
                IsAlive = !effect.TargetDefeated,
                RuntimeVersion = effect.RuntimeVersionAfter,
                CombatState = effect.TargetDefeated
                    ? BattleParticipantCombatState.Defeated
                    : effect.EffectType == SkillEffectType.Damage
                        ? BattleParticipantCombatState.Hit
                        : participant.CombatState,
                DefeatedAtUtc = effect.TargetDefeated
                    ? effect.CompletedAtUtc
                    : participant.DefeatedAtUtc
            };
        }

        return SkillCollections.Freeze(participants.Select(value => current[value.ParticipantId]));
    }

    private static IReadOnlyList<BattleParticipant> ApplyEffect(
        IReadOnlyList<BattleParticipant> participants,
        SkillEffectResult effect) =>
        SkillCollections.Freeze(participants.Select(value =>
            value.ParticipantId != effect.TargetParticipantId
                ? value
                : value with
                {
                    CurrentHp = effect.HpAfter,
                    IsAlive = !effect.TargetDefeated,
                    RuntimeVersion = effect.RuntimeVersionAfter,
                    CombatState = effect.TargetDefeated
                        ? BattleParticipantCombatState.Defeated
                        : effect.EffectType == SkillEffectType.Damage
                            ? BattleParticipantCombatState.Hit
                            : value.CombatState,
                    DefeatedAtUtc = effect.TargetDefeated
                        ? effect.CompletedAtUtc
                        : value.DefeatedAtUtc
                }));

    private static IReadOnlyList<SkillTargetResult> AggregateTargets(
        IReadOnlyList<SkillEffectResult> effects) =>
        SkillCollections.Freeze(effects
            .Where(value => value.State == SkillEffectExecutionState.Committed)
            .GroupBy(value => value.TargetParticipantId)
            .Select(group =>
            {
                var ordered = group.OrderBy(value => value.EffectIndex).ThenBy(value => value.TargetIndex).ToArray();
                return new SkillTargetResult(
                    group.Key,
                    ordered.Sum(value => value.Damage),
                    ordered.Sum(value => value.Heal),
                    ordered[0].HpBefore,
                    ordered[^1].HpAfter,
                    ordered[^1].TargetDefeated,
                    ordered[0].RuntimeVersionBefore,
                    ordered[^1].RuntimeVersionAfter,
                    ordered.FirstOrDefault(value => !string.IsNullOrEmpty(value.FailureCode))?.FailureCode ?? "");
            })
            .OrderBy(value => value.TargetParticipantId));

    private static SkillEffectResult RejectedEffect(
        SkillExecutionRecord record,
        SkillEffectPlan plan,
        BattleParticipant source,
        BattleParticipant target,
        string failureCode,
        DateTimeOffset now) =>
        new(
            plan.EffectExecutionId,
            record.Plan.SkillExecutionId,
            plan.EffectDefinitionId,
            plan.EffectIndex,
            plan.TargetIndex,
            source.ParticipantId,
            target.ParticipantId,
            plan.EffectType,
            SkillEffectExecutionState.Rejected,
            plan.ValuePlan.BaseValue,
            0,
            0,
            target.CurrentHp,
            target.CurrentHp,
            target.MaximumHp,
            !target.IsAlive,
            target.RuntimeVersion,
            target.RuntimeVersion,
            plan.PolicyStatus,
            failureCode,
            false,
            now);

    private static BattleLockedAction RecreateAction(SkillExecutionRecord record) =>
        new(
            record.Plan.BattleActionId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.ParticipantId,
            BattleActionType.Skill,
            SkillCollections.Freeze(record.Plan.TargetParticipantSnapshots.Select(value => value.ParticipantId)),
            record.Plan.SkillDefinitionId,
            null,
            BattleRuntimeHash.SafeId(record.Plan.SkillActionId.ToString("N")),
            record.PayloadHash,
            record.Plan.CreatedAtUtc,
            record.Plan.CreatedAtUtc,
            record.Plan.CorrelationId);

    private static string PayloadHash(BattleInstance battle, BattleLockedAction action) =>
        BattleRuntimeHash.PersistenceKey(string.Join(
            '\u001f',
            battle.BattleInstanceId,
            action.RoundNumber,
            action.ActionId,
            action.ParticipantId,
            action.SkillDefinitionId,
            string.Join(',', action.TargetParticipantIds),
            action.PayloadHash));

    private static SkillResultCode MapDefinitionFailure(string code) =>
        code switch
        {
            "skill.not_found" => SkillResultCode.SkillNotFound,
            "skill.disabled" => SkillResultCode.SkillDisabled,
            _ => SkillResultCode.SkillEvidenceBlocked
        };

    private static SkillResultCode MapAvailabilityFailure(string code) =>
        code switch
        {
            "skill.participant_not_found" => SkillResultCode.ParticipantNotFound,
            "skill.participant_dead" => SkillResultCode.ParticipantDead,
            "skill.not_owned" => SkillResultCode.SkillNotOwned,
            "skill.rank_invalid" => SkillResultCode.SkillRankInvalid,
            "skill.invalid_phase" => SkillResultCode.InvalidPhase,
            _ => SkillResultCode.SkillEvidenceBlocked
        };

    private static SkillResultCode MapTargetFailure(string code) =>
        code switch
        {
            "skill.target_dead" => SkillResultCode.TargetDead,
            "skill.no_valid_target" => SkillResultCode.NoValidTarget,
            "skill.target_policy_blocked" => SkillResultCode.TargetPolicyBlocked,
            _ => SkillResultCode.InvalidTarget
        };

    private static SkillResultCode MapCooldownFailure(string code) =>
        code switch
        {
            "skill.cooldown_active" => SkillResultCode.CooldownActive,
            "skill.usage_limit_reached" => SkillResultCode.UsageLimitReached,
            _ => SkillResultCode.CooldownEvidenceBlocked
        };

    private static SkillCostResult CostResult(SkillCostReservation reservation) =>
        new(
            reservation.ReservationId,
            reservation.ResourceType,
            reservation.Amount,
            reservation.State,
            "");

    private static SkillCostResult EmptyCost(SkillDefinition definition) =>
        new(
            null,
            definition.CostDefinition.ResourceType,
            definition.CostDefinition.Amount,
            SkillCostReservationState.Created,
            "");

    private static SkillCooldownResult EmptyCooldown(SkillDefinition definition) =>
        new(
            0,
            0,
            0,
            false,
            "",
            definition.CooldownDefinition.PolicyStatus);

    private static SkillActionResult MissingRecovery(
        Guid executionId,
        BattleInstance battle,
        string failureCode) =>
        new(
            executionId,
            Guid.Empty,
            Guid.Empty,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            Guid.Empty,
            0,
            SkillResultCode.RecoveryRequired,
            failureCode,
            false,
            new SkillCostResult(null, SkillResourceType.None, 0, SkillCostReservationState.Created, failureCode),
            new SkillCooldownResult(0, 0, 0, false, failureCode, CombatPolicyStatus.EvidenceBlocked),
            [],
            [],
            battle.BattleVersion,
            battle.BattleVersion,
            0,
            0,
            0,
            SkillRecoveryState.RecoveryRequired,
            [],
            battle.UpdatedAtUtc,
            null);

    private SkillActionResult Failed(
        Guid executionId,
        Guid skillActionId,
        BattleInstance battle,
        BattleLockedAction action,
        int skillDefinitionId,
        SkillResultCode code,
        string failureCode)
    {
        Publish(
            SkillEventKind.SkillActionRejected,
            executionId,
            battle,
            action,
            null,
            0,
            SkillExecutionState.Rejected.ToString(),
            failureCode);
        return new SkillActionResult(
            executionId,
            skillActionId,
            action.ActionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            action.ParticipantId,
            skillDefinitionId,
            code,
            failureCode,
            false,
            new SkillCostResult(null, SkillResourceType.None, 0, SkillCostReservationState.Created, failureCode),
            new SkillCooldownResult(0, 0, 0, false, failureCode, CombatPolicyStatus.EvidenceBlocked),
            [],
            [],
            battle.BattleVersion,
            battle.BattleVersion,
            0,
            0,
            0,
            SkillRecoveryState.NotRequired,
            [],
            _clock.UtcNow,
            _clock.UtcNow);
    }

    private void Publish(
        SkillEventKind kind,
        Guid executionId,
        BattleInstance battle,
        BattleLockedAction action,
        Guid? effectExecutionId,
        int cursor,
        string state,
        string failureCode) =>
        _events.Publish(new SkillEvent(
            Guid.NewGuid(),
            kind,
            executionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            effectExecutionId,
            action.ParticipantId,
            null,
            action.SkillDefinitionId ?? 0,
            cursor,
            state,
            failureCode,
            _clock.UtcNow,
            action.CorrelationId));

    private void AppendEffectAudit(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord record,
        SkillEffectResult effect)
    {
        if (FailureInjection.Point == SkillFailurePoint.Audit)
        {
            return;
        }

        var participant = battle.Participants.Single(value => value.ParticipantId == action.ParticipantId);
        _audit.Append(new SkillAuditRecord(
            Guid.NewGuid(),
            record.Plan.SkillExecutionId,
            record.Plan.SkillActionId,
            action.ActionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            effect.EffectExecutionId,
            effect.EffectIndex,
            effect.TargetIndex,
            BattleRuntimeHash.SafeId(record.Plan.SkillExecutionId.ToString("N")),
            action.CorrelationId,
            BattleRuntimeHash.SessionSafeId(participant.SessionId),
            participant.CharacterId ?? 0,
            participant.ParticipantId,
            effect.TargetParticipantId,
            definition.SkillDefinitionId,
            record.Plan.SkillRank,
            effect.EffectType,
            definition.CostDefinition.ResourceType,
            definition.CostDefinition.Amount,
            record.CostReservation?.State ?? SkillCostReservationState.Created,
            0,
            effect.HpBefore,
            effect.Damage,
            effect.Heal,
            effect.HpAfter,
            effect.RuntimeVersionBefore,
            effect.RuntimeVersionAfter,
            string.IsNullOrEmpty(effect.FailureCode) ? SkillResultCode.Success : SkillResultCode.Rejected,
            effect.FailureCode,
            record.Plan.PolicyStatuses,
            record.ResolutionCursor,
            record.RecoveryState,
            record.Plan.CreatedAtUtc,
            _clock.UtcNow));
    }

    private void AppendCompletionAudit(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        SkillExecutionRecord record,
        SkillActionResult result)
    {
        if (FailureInjection.Point == SkillFailurePoint.Audit)
        {
            return;
        }

        var participant = battle.Participants.Single(value => value.ParticipantId == action.ParticipantId);
        _audit.Append(new SkillAuditRecord(
            Guid.NewGuid(),
            record.Plan.SkillExecutionId,
            record.Plan.SkillActionId,
            action.ActionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            null,
            null,
            null,
            BattleRuntimeHash.SafeId(record.Plan.SkillExecutionId.ToString("N")),
            action.CorrelationId,
            BattleRuntimeHash.SessionSafeId(participant.SessionId),
            participant.CharacterId ?? 0,
            participant.ParticipantId,
            null,
            definition.SkillDefinitionId,
            record.Plan.SkillRank,
            null,
            definition.CostDefinition.ResourceType,
            definition.CostDefinition.Amount,
            record.CostReservation?.State ?? SkillCostReservationState.Created,
            result.CooldownResult.AvailableAtRound,
            null,
            result.TargetResults.Sum(value => value.Damage),
            result.TargetResults.Sum(value => value.Heal),
            null,
            null,
            null,
            result.ResultCode,
            result.FailureCode,
            record.Plan.PolicyStatuses,
            result.ResolutionCursor,
            result.RecoveryState,
            result.StartedAtUtc,
            result.CompletedAtUtc ?? _clock.UtcNow));
    }
}
