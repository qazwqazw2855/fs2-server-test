using System.Collections.Concurrent;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class DeterministicTestStatusTriggerPolicy : IStatusTriggerPolicy
{
    public OperationResult Validate(StatusDefinition definition, StatusTriggerDefinition trigger)
    {
        if (definition.PolicyStatus is not (
                CombatPolicyStatus.TestOnly or
                CombatPolicyStatus.Verified or
                CombatPolicyStatus.ContentBacked))
        {
            return OperationResult.Failure(
                "status.trigger_evidence_blocked",
                "Trigger definition is not executable.");
        }

        if (trigger.PolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
            trigger.TriggerPhase == StatusTriggerPhase.Unknown ||
            trigger.EffectType is StatusTriggerEffectType.Scripted or StatusTriggerEffectType.Unknown)
        {
            return OperationResult.Failure(
                "status.trigger_evidence_blocked",
                "Trigger semantics are blocked by evidence.");
        }

        if (trigger.EffectType is StatusTriggerEffectType.PeriodicDamage or
            StatusTriggerEffectType.PeriodicHealing &&
            trigger.BaseValueCandidate is not > 0)
        {
            return OperationResult.Failure(
                "status.trigger_value_invalid",
                "Periodic triggers require a positive server-owned value.");
        }

        return OperationResult.Success;
    }
}

public sealed class StatusTriggerPlanner : IStatusTriggerPlanner
{
    private readonly IStatusTriggerPolicy _policy;

    public StatusTriggerPlanner(IStatusTriggerPolicy policy)
    {
        _policy = policy;
    }

    public OperationResult<StatusTriggerExecutionPlan> Create(
        BattleInstance battle,
        StatusTriggerPhase phase,
        Guid? sourceActionId,
        IEnumerable<(StatusInstance Instance, StatusDefinition Definition)> eligible,
        string correlationId,
        DateTimeOffset now)
    {
        var ordered = eligible
            .SelectMany(pair => pair.Definition.TriggerDefinitions
                .Where(trigger => trigger.TriggerPhase == phase)
                .Select(trigger => (pair.Instance, pair.Definition, Trigger: trigger)))
            .OrderBy(value => value.Trigger.TriggerPhase)
            .ThenBy(value => value.Trigger.TriggerIndex)
            .ThenBy(value => value.Instance.AppliedRound)
            .ThenBy(value => value.Instance.StatusInstanceId)
            .ThenBy(value => value.Trigger.TriggerDefinitionId, StringComparer.Ordinal)
            .ThenBy(value => value.Instance.TargetParticipantId)
            .ToArray();
        var executions = new List<StatusTriggerExecution>(ordered.Length);
        var statusOccurrences = new Dictionary<Guid, int>();
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var validation = _policy.Validate(item.Definition, item.Trigger);
            if (!validation.Succeeded)
            {
                return OperationResult<StatusTriggerExecutionPlan>.Failure(
                    validation.Error.Code,
                    validation.Error.Message);
            }

            var occurrence = statusOccurrences.GetValueOrDefault(item.Instance.StatusInstanceId);
            statusOccurrences[item.Instance.StatusInstanceId] = occurrence + 1;
            long finalValue;
            try
            {
                finalValue = checked(
                    (item.Trigger.BaseValueCandidate ?? 0) *
                    item.Instance.StackCount);
            }
            catch (OverflowException)
            {
                return OperationResult<StatusTriggerExecutionPlan>.Failure(
                    "status.trigger_value_overflow",
                    "Trigger value overflowed checked arithmetic.");
            }

            var executionId = BattleRuntimeHash.DeterministicGuid(
                $"status-trigger:{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{phase}:" +
                $"{sourceActionId:N}:{item.Instance.StatusInstanceId:N}:{item.Trigger.TriggerIndex}:{index}");
            var target = battle.Participants.FirstOrDefault(
                value => value.ParticipantId == item.Instance.TargetParticipantId);
            executions.Add(new StatusTriggerExecution(
                executionId,
                item.Instance.StatusInstanceId,
                item.Instance.StatusDefinitionId,
                item.Trigger.TriggerDefinitionId,
                item.Trigger.TriggerIndex,
                item.Instance.StackCount,
                item.Instance.SourceParticipantId,
                item.Instance.TargetParticipantId,
                item.Trigger.EffectType,
                new StatusTriggerValuePlan(
                    item.Trigger.ValuePolicy,
                    item.Trigger.BaseValueCandidate ?? 0,
                    item.Instance.StackCount,
                    finalValue,
                    item.Trigger.PolicyStatus),
                index,
                checked(item.Instance.RuntimeVersion + occurrence),
                target?.RuntimeVersion ?? 0,
                $"status-trigger:{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{phase}:" +
                $"{item.Instance.StatusInstanceId:N}:{item.Trigger.TriggerIndex}:{index}",
                StatusTriggerExecutionState.Pending,
                item.Trigger.PolicyStatus));
        }

        var planId = BattleRuntimeHash.DeterministicGuid(
            $"status-trigger-plan:{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{phase}:{sourceActionId:N}");
        return OperationResult<StatusTriggerExecutionPlan>.Success(
            new StatusTriggerExecutionPlan(
                planId,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                phase,
                sourceActionId,
                StatusRuntimeCollections.Freeze(executions),
                battle.BattleVersion,
                now,
                correlationId));
    }
}

public sealed class StatusModifierPipeline :
    IStatusModifierProvider,
    IStatusModifierAggregator,
    IBattleDamageModifierPort,
    IBattleHealingModifierPort
{
    private const int BasisPointScale = 10_000;
    private readonly IStatusDefinitionCatalog _catalog;
    private readonly IBattleStatusAuthority _authority;
    private readonly InMemoryStatusRuntimeStore? _inspectorStore;

    public StatusModifierPipeline(
        IStatusDefinitionCatalog catalog,
        IBattleStatusAuthority authority,
        InMemoryStatusRuntimeStore? inspectorStore = null)
    {
        _catalog = catalog;
        _authority = authority;
        _inspectorStore = inspectorStore;
    }

    public StatusFailureInjection FailureInjection { get; } = new();

    public StatusModifierSnapshot CreateSnapshot(
        Guid battleInstanceId,
        int roundNumber,
        Guid sourceParticipantId,
        Guid targetParticipantId,
        long baseValue,
        DateTimeOffset now)
    {
        var source = _authority.GetParticipantSnapshot(
            battleInstanceId,
            sourceParticipantId,
            now).Statuses.Where(value => value.IsActive).ToArray();
        var target = _authority.GetParticipantSnapshot(
            battleInstanceId,
            targetParticipantId,
            now).Statuses.Where(value => value.IsActive).ToArray();
        var contributions = new List<StatusModifierContribution>();
        AddContributions(source, isSource: true, contributions);
        AddContributions(target, isSource: false, contributions);
        var ordered = contributions
            .OrderBy(value => ModifierOrder(value.ModifierType))
            .ThenBy(value => value.ExecutionOrder)
            .ThenBy(value => value.StatusInstanceId)
            .ThenBy(value => value.ModifierDefinitionId, StringComparer.Ordinal)
            .Select((value, index) => value with { ExecutionOrder = index })
            .ToArray();
        return new StatusModifierSnapshot(
            BattleRuntimeHash.DeterministicGuid(
                $"status-modifier:{battleInstanceId:N}:{roundNumber}:{sourceParticipantId:N}:" +
                $"{targetParticipantId:N}:{baseValue}:{string.Join(',', ordered.Select(value => value.StatusInstanceId))}"),
            battleInstanceId,
            roundNumber,
            sourceParticipantId,
            targetParticipantId,
            StatusRuntimeCollections.Freeze(source.Select(value => value.StatusInstanceId)),
            StatusRuntimeCollections.Freeze(target.Select(value => value.StatusInstanceId)),
            StatusRuntimeCollections.Freeze(ordered),
            StatusRuntimeCollections.Freeze(
                ordered.Select(value => value.PolicyStatus).Distinct()),
            baseValue,
            now);
    }

    public StatusModifierResult AggregateDamage(StatusModifierSnapshot snapshot) =>
        Aggregate(
            snapshot,
            StatusModifierType.OutgoingDamageFlat,
            StatusModifierType.OutgoingDamageMultiplier,
            StatusModifierType.IncomingDamageFlat,
            StatusModifierType.IncomingDamageMultiplier);

    public StatusModifierResult AggregateHealing(StatusModifierSnapshot snapshot) =>
        Aggregate(
            snapshot,
            StatusModifierType.OutgoingHealingFlat,
            StatusModifierType.OutgoingHealingMultiplier,
            StatusModifierType.IncomingHealingFlat,
            StatusModifierType.IncomingHealingMultiplier);

    public StatusModifierResult ApplyDamageModifiers(StatusDamageModifierRequest request)
    {
        var snapshot = CreateSnapshot(
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceParticipantId,
            request.TargetParticipantId,
            request.BaseDamageCandidate,
            request.CreatedAtUtc);
        var result = AggregateDamage(snapshot);
        Record(snapshot, result);
        return result;
    }

    public StatusModifierResult ApplyHealingModifiers(StatusHealingModifierRequest request)
    {
        var snapshot = CreateSnapshot(
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceParticipantId,
            request.TargetParticipantId,
            request.BaseHealingCandidate,
            request.CreatedAtUtc);
        var result = AggregateHealing(snapshot);
        Record(snapshot, result);
        return result;
    }

    private void AddContributions(
        IEnumerable<StatusInstance> instances,
        bool isSource,
        ICollection<StatusModifierContribution> output)
    {
        foreach (var instance in instances
                     .OrderBy(value => value.AppliedRound)
                     .ThenBy(value => value.StatusInstanceId))
        {
            var definition = _catalog.Snapshot.FirstOrDefault(
                value => value.StatusDefinitionId == instance.StatusDefinitionId);
            if (definition is null)
            {
                continue;
            }

            foreach (var modifier in definition.ModifierDefinitions)
            {
                if (IsSourceModifier(modifier.ModifierType) != isSource)
                {
                    continue;
                }

                output.Add(new StatusModifierContribution(
                    instance.StatusInstanceId,
                    instance.StatusDefinitionId,
                    modifier.ModifierDefinitionId,
                    modifier.ModifierIndex,
                    modifier.ModifierType,
                    modifier.FlatValue,
                    modifier.MultiplierBasisPoints,
                    modifier.ApplyPerStack ? instance.StackCount : 1,
                    modifier.ModifierIndex,
                    modifier.PolicyStatus));
            }
        }
    }

    private StatusModifierResult Aggregate(
        StatusModifierSnapshot snapshot,
        StatusModifierType outgoingFlatType,
        StatusModifierType outgoingMultiplierType,
        StatusModifierType incomingFlatType,
        StatusModifierType incomingMultiplierType)
    {
        if (FailureInjection.Point == StatusFailurePoint.ModifierOverflow)
        {
            return Failure(snapshot, "status.modifier_overflow");
        }

        try
        {
            var outgoingFlat = SumFlat(snapshot, outgoingFlatType);
            var incomingFlat = SumFlat(snapshot, incomingFlatType);
            var outgoingMultiplier = CombineMultiplier(snapshot, outgoingMultiplierType);
            var incomingMultiplier = CombineMultiplier(snapshot, incomingMultiplierType);
            var value = checked(snapshot.BaseValue + outgoingFlat);
            value = ApplyBasisPoints(value, outgoingMultiplier);
            value = checked(value + incomingFlat);
            value = ApplyBasisPoints(value, incomingMultiplier);
            value = Math.Max(0, value);
            var policy = snapshot.OrderedModifiers.Count == 0
                ? CombatPolicyStatus.Baseline
                : snapshot.OrderedModifiers.Any(value => value.PolicyStatus == CombatPolicyStatus.EvidenceBlocked)
                    ? CombatPolicyStatus.EvidenceBlocked
                    : snapshot.OrderedModifiers.All(value => value.PolicyStatus == CombatPolicyStatus.TestOnly)
                        ? CombatPolicyStatus.TestOnly
                        : CombatPolicyStatus.ContentBacked;
            if (policy == CombatPolicyStatus.EvidenceBlocked)
            {
                return new StatusModifierResult(
                    snapshot.ModifierSnapshotId,
                    snapshot.BaseValue,
                    outgoingFlat,
                    outgoingMultiplier,
                    incomingFlat,
                    incomingMultiplier,
                    snapshot.BaseValue,
                    StatusResultCode.ModifierEvidenceBlocked,
                    "status.modifier_evidence_blocked",
                    policy);
            }

            return new StatusModifierResult(
                snapshot.ModifierSnapshotId,
                snapshot.BaseValue,
                outgoingFlat,
                outgoingMultiplier,
                incomingFlat,
                incomingMultiplier,
                value,
                StatusResultCode.Success,
                "",
                policy);
        }
        catch (OverflowException)
        {
            return Failure(snapshot, "status.modifier_overflow");
        }
    }

    private static long SumFlat(StatusModifierSnapshot snapshot, StatusModifierType type)
    {
        long value = 0;
        foreach (var contribution in snapshot.OrderedModifiers.Where(value => value.ModifierType == type))
        {
            value = checked(value + checked(contribution.FlatValue * contribution.StackSnapshot));
        }

        return value;
    }

    private static int CombineMultiplier(StatusModifierSnapshot snapshot, StatusModifierType type)
    {
        var value = BasisPointScale;
        foreach (var contribution in snapshot.OrderedModifiers.Where(value => value.ModifierType == type))
        {
            var delta = checked(contribution.MultiplierBasisPoints - BasisPointScale);
            value = checked(value + checked(delta * contribution.StackSnapshot));
        }

        return value;
    }

    private static long ApplyBasisPoints(long value, int basisPoints) =>
        checked(checked(value * basisPoints) / BasisPointScale);

    private static StatusModifierResult Failure(StatusModifierSnapshot snapshot, string failureCode) =>
        new(
            snapshot.ModifierSnapshotId,
            snapshot.BaseValue,
            0,
            BasisPointScale,
            0,
            BasisPointScale,
            snapshot.BaseValue,
            StatusResultCode.ModifierEvidenceBlocked,
            failureCode,
            CombatPolicyStatus.EvidenceBlocked);

    private void Record(StatusModifierSnapshot snapshot, StatusModifierResult result)
    {
        _inspectorStore?.RecordModifier(new StatusModifierInspectorItem(
            snapshot.ModifierSnapshotId,
            snapshot.SourceParticipantId,
            snapshot.TargetParticipantId,
            StatusRuntimeCollections.Freeze(
                snapshot.OrderedModifiers.Select(value => value.ModifierType).Distinct()),
            StatusRuntimeCollections.Freeze(
                snapshot.OrderedModifiers.Select(value => value.StatusInstanceId).Distinct()),
            snapshot.BaseValue,
            result.FinalValue,
            snapshot.PolicyStatuses,
            snapshot.CreatedAtUtc));
    }

    private static bool IsSourceModifier(StatusModifierType type) =>
        type is
            StatusModifierType.OutgoingDamageFlat or
            StatusModifierType.OutgoingDamageMultiplier or
            StatusModifierType.OutgoingHealingFlat or
            StatusModifierType.OutgoingHealingMultiplier;

    private static int ModifierOrder(StatusModifierType type) =>
        type switch
        {
            StatusModifierType.OutgoingDamageFlat or StatusModifierType.OutgoingHealingFlat => 0,
            StatusModifierType.OutgoingDamageMultiplier or StatusModifierType.OutgoingHealingMultiplier => 1,
            StatusModifierType.IncomingDamageFlat or StatusModifierType.IncomingHealingFlat => 2,
            StatusModifierType.IncomingDamageMultiplier or StatusModifierType.IncomingHealingMultiplier => 3,
            _ => 4
        };
}

public sealed class StatusActionRestrictionPolicy :
    IStatusActionRestrictionPolicy,
    IBattleActionRestrictionPort
{
    private readonly IStatusDefinitionCatalog _catalog;
    private readonly IBattleStatusAuthority _authority;
    private readonly InMemoryStatusRuntimeStore? _inspectorStore;

    public StatusActionRestrictionPolicy(
        IStatusDefinitionCatalog catalog,
        IBattleStatusAuthority authority,
        InMemoryStatusRuntimeStore? inspectorStore = null)
    {
        _catalog = catalog;
        _authority = authority;
        _inspectorStore = inspectorStore;
    }

    public StatusFailureInjection FailureInjection { get; } = new();

    public StatusActionRestrictionResult Evaluate(
        BattleInstance battle,
        BattleLockedAction action,
        DateTimeOffset now)
    {
        if (FailureInjection.Point == StatusFailurePoint.ActionRestriction)
        {
            return Record(new StatusActionRestrictionResult(
                action.ActionId,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                action.ParticipantId,
                action.ActionType,
                false,
                null,
                null,
                StatusResultCode.InternalFailure,
                "status.action_restriction_failed",
                now));
        }

        var statuses = _authority.GetParticipantSnapshot(
                battle.BattleInstanceId,
                action.ParticipantId,
                now)
            .Statuses
            .Where(value => value.IsActive)
            .OrderBy(value => value.AppliedRound)
            .ThenBy(value => value.StatusInstanceId);
        foreach (var status in statuses)
        {
            var definition = _catalog.Snapshot.FirstOrDefault(
                value => value.StatusDefinitionId == status.StatusDefinitionId);
            if (definition is null)
            {
                continue;
            }

            foreach (var restriction in definition.ActionRestrictionDefinitions
                         .OrderBy(value => value.RestrictionIndex)
                         .ThenBy(value => value.RestrictionDefinitionId, StringComparer.Ordinal))
            {
                var restricted = restriction.RestrictionType switch
                {
                    StatusActionRestrictionType.PreventAllAction => true,
                    StatusActionRestrictionType.PreventSkillAction => action.ActionType == BattleActionType.Skill,
                    StatusActionRestrictionType.PreventBasicAttack => action.ActionType == BattleActionType.BasicAttack,
                    _ => false
                };
                if (!restricted)
                {
                    continue;
                }

                return Record(new StatusActionRestrictionResult(
                    action.ActionId,
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    action.ParticipantId,
                    action.ActionType,
                    false,
                    restriction.RestrictionType,
                    status.StatusInstanceId,
                    StatusResultCode.ActionRestricted,
                    "status.action_restricted",
                    now));
            }
        }

        return Record(new StatusActionRestrictionResult(
            action.ActionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            action.ParticipantId,
            action.ActionType,
            true,
            null,
            null,
            StatusResultCode.Success,
            "",
            now));
    }

    private StatusActionRestrictionResult Record(StatusActionRestrictionResult result)
    {
        _inspectorStore?.RecordRestriction(new StatusRestrictionInspectorItem(
            result.BattleActionId,
            result.ParticipantId,
            result.ActionType,
            result.RestrictionType,
            result.StatusInstanceId,
            result.ResultCode,
            result.FailureCode,
            result.CreatedAtUtc));
        return result;
    }
}

public sealed class ExactTestStatusDispelPolicy : IStatusDispelPolicy
{
    public OperationResult<IReadOnlyList<StatusInstance>> Select(
        BattleStatusSnapshot snapshot,
        StatusDispelCandidate candidate,
        Guid? exactStatusInstanceId,
        int? exactStatusDefinitionId)
    {
        if (candidate.PolicyStatus != CombatPolicyStatus.TestOnly)
        {
            return OperationResult<IReadOnlyList<StatusInstance>>.Failure(
                "status.dispel_evidence_blocked",
                "Production dispel selection is blocked by evidence.");
        }

        var selected = snapshot.Statuses
            .Where(value => value.IsActive)
            .Where(value =>
                exactStatusInstanceId is not null
                    ? value.StatusInstanceId == exactStatusInstanceId
                    : exactStatusDefinitionId is not null &&
                      value.StatusDefinitionId == exactStatusDefinitionId)
            .OrderBy(value => value.AppliedRound)
            .ThenBy(value => value.StatusInstanceId)
            .Take(candidate.MaximumRemovalCountCandidate ?? 1)
            .ToArray();
        return OperationResult<IReadOnlyList<StatusInstance>>.Success(
            StatusRuntimeCollections.Freeze(selected));
    }
}

public sealed class StatusPeriodicDamageTriggerHandler
{
    private readonly IBattleCombatExecutionPort _combat;

    public StatusPeriodicDamageTriggerHandler(IBattleCombatExecutionPort combat)
    {
        _combat = combat;
    }

    public async Task<StatusTriggerExecutionResult> ExecuteAsync(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        BattleInstance battle,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = CurrentParticipant(battle, execution.SourceParticipantId, _combat);
        var target = CurrentParticipant(battle, execution.TargetParticipantId, _combat);
        if (source is null || target is null)
        {
            return Failure(plan, execution, "status.trigger_participant_not_found", now);
        }

        if (!target.IsAlive)
        {
            return Skipped(plan, execution, target, "status.trigger_target_dead", now);
        }

        var triggerDamage = string.Equals(
            execution.ValuePlan.ValuePolicy,
            "current_hp_div_10_user_formula",
            StringComparison.Ordinal)
            ? target.CurrentHp / 10
            : execution.ValuePlan.FinalValue;
        if (triggerDamage <= 0)
        {
            return Skipped(plan, execution, target, "status.periodic_damage_zero", now);
        }

        var result = await _combat.ExecuteAsync(
            new BattleCombatExecutionRequest(
                plan.BattleInstanceId,
                plan.RoundNumber,
                execution.TriggerExecutionId,
                execution.ExecutionOrder,
                source,
                target,
                execution.IdempotencyKey,
                now,
                plan.CorrelationId,
                triggerDamage,
                execution.ValuePlan.PolicyStatus),
            cancellationToken);
        return new StatusTriggerExecutionResult(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            result.Succeeded
                ? StatusTriggerExecutionState.Committed
                : StatusTriggerExecutionState.RecoveryRequired,
            result.Succeeded
                ? StatusResultCode.Success
                : StatusResultCode.CombatResolutionFailure,
            result.FailureCode,
            result.Damage,
            0,
            result.HpBefore,
            result.HpAfter,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            result.IsDuplicate,
            result.Succeeded
                ? StatusRecoveryState.NotRequired
                : StatusRecoveryState.RecoveryRequired,
            now);
    }

    private static BattleParticipant? CurrentParticipant(
        BattleInstance battle,
        Guid participantId,
        IBattleCombatExecutionPort combat)
    {
        var participant = battle.Participants.FirstOrDefault(
            value => value.ParticipantId == participantId);
        if (participant is null ||
            combat is not IBattleCombatStateAuthority authority)
        {
            return participant;
        }

        var state = authority.Get(battle.BattleInstanceId, participantId);
        return state.Succeeded && state.Value is not null
            ? participant with
            {
                CurrentHp = state.Value.CurrentHp,
                RuntimeVersion = state.Value.RuntimeVersion,
                IsAlive = state.Value.CurrentHp > 0
            }
            : participant;
    }

    private static StatusTriggerExecutionResult Failure(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        string failureCode,
        DateTimeOffset now) =>
        new(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            StatusTriggerExecutionState.RecoveryRequired,
            StatusResultCode.CombatResolutionFailure,
            failureCode,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            StatusRecoveryState.RecoveryRequired,
            now);

    private static StatusTriggerExecutionResult Skipped(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        BattleParticipant target,
        string failureCode,
        DateTimeOffset now) =>
        new(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            StatusTriggerExecutionState.Skipped,
            StatusResultCode.Success,
            failureCode,
            0,
            0,
            target.CurrentHp,
            target.CurrentHp,
            target.RuntimeVersion,
            target.RuntimeVersion,
            false,
            StatusRecoveryState.NotRequired,
            now);
}

public sealed class StatusPeriodicHealingTriggerHandler
{
    private readonly IBattleHealthMutationPort _health;

    public StatusPeriodicHealingTriggerHandler(IBattleHealthMutationPort health)
    {
        _health = health;
    }

    public async Task<StatusTriggerExecutionResult> ExecuteAsync(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        BattleInstance battle,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = battle.Participants.FirstOrDefault(
            value => value.ParticipantId == execution.SourceParticipantId);
        var target = battle.Participants.FirstOrDefault(
            value => value.ParticipantId == execution.TargetParticipantId);
        if (source is null || target is null)
        {
            return Failure(plan, execution, "status.trigger_participant_not_found", now);
        }

        if (_health is IBattleCombatStateAuthority authority)
        {
            var current = authority.Get(battle.BattleInstanceId, target.ParticipantId);
            if (current.Succeeded && current.Value is not null)
            {
                target = target with
                {
                    CurrentHp = current.Value.CurrentHp,
                    RuntimeVersion = current.Value.RuntimeVersion,
                    IsAlive = current.Value.CurrentHp > 0
                };
            }
        }

        if (!target.IsAlive)
        {
            return Failure(plan, execution, "status.trigger_target_dead", now);
        }

        var result = await _health.HealAsync(
            new BattleHealthMutationRequest(
                plan.BattleInstanceId,
                plan.RoundNumber,
                execution.TriggerExecutionId,
                execution.TriggerExecutionId,
                source,
                target,
                execution.ValuePlan.FinalValue,
                target.RuntimeVersion,
                execution.IdempotencyKey,
                execution.ValuePlan.PolicyStatus,
                now,
                plan.CorrelationId),
            cancellationToken);
        var succeeded = result.ResultCode is SkillResultCode.Success or SkillResultCode.DuplicateCompleted;
        return new StatusTriggerExecutionResult(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            succeeded
                ? StatusTriggerExecutionState.Committed
                : StatusTriggerExecutionState.RecoveryRequired,
            succeeded
                ? StatusResultCode.Success
                : StatusResultCode.HealthMutationFailure,
            result.FailureCode,
            0,
            result.EffectiveHeal,
            result.HpBefore,
            result.HpAfter,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            result.IsDuplicate,
            succeeded
                ? StatusRecoveryState.NotRequired
                : StatusRecoveryState.RecoveryRequired,
            now);
    }

    private static StatusTriggerExecutionResult Failure(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        string failureCode,
        DateTimeOffset now) =>
        new(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            StatusTriggerExecutionState.RecoveryRequired,
            StatusResultCode.HealthMutationFailure,
            failureCode,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            StatusRecoveryState.RecoveryRequired,
            now);
}

public sealed class StatusTriggerCoordinator : IStatusTriggerCoordinator
{
    private readonly IStatusDefinitionCatalog _catalog;
    private readonly IBattleStatusAuthority _authority;
    private readonly IStatusTriggerPlanner _planner;
    private readonly IStatusTriggerExecutionStore _store;
    private readonly StatusPeriodicDamageTriggerHandler _damage;
    private readonly StatusPeriodicHealingTriggerHandler _healing;
    private readonly IStatusDurationPolicy _duration;
    private readonly IStatusEffectCoordinator _effects;
    private readonly IStatusEventSink _events;
    private readonly IStatusAuditLedger _audit;
    private readonly IBattleClock _clock;
    private readonly AsyncKeyedLock<Guid> _locks = new();

    public StatusTriggerCoordinator(
        IStatusDefinitionCatalog catalog,
        IBattleStatusAuthority authority,
        IStatusTriggerPlanner planner,
        IStatusTriggerExecutionStore store,
        StatusPeriodicDamageTriggerHandler damage,
        StatusPeriodicHealingTriggerHandler healing,
        IStatusDurationPolicy duration,
        IStatusEffectCoordinator effects,
        IStatusEventSink events,
        IStatusAuditLedger audit,
        IBattleClock clock)
    {
        _catalog = catalog;
        _authority = authority;
        _planner = planner;
        _store = store;
        _damage = damage;
        _healing = healing;
        _duration = duration;
        _effects = effects;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public StatusFailureInjection FailureInjection { get; } = new();

    public async Task<StatusTriggerResolutionResult> ResolveAsync(
        BattleInstance battle,
        StatusTriggerPhase phase,
        Guid? sourceActionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (battle.State != BattleState.Active && phase != StatusTriggerPhase.BattleCompleted)
        {
            return Failure(
                Guid.Empty,
                battle,
                phase,
                StatusResultCode.InvalidPhase,
                "status.trigger_battle_not_active");
        }

        var active = battle.Participants
            .SelectMany(participant => _authority
                .GetParticipantSnapshot(
                    battle.BattleInstanceId,
                    participant.ParticipantId,
                    _clock.UtcNow)
                .Statuses)
            .Where(value =>
                phase == StatusTriggerPhase.OnRemove
                    ? value.LifecycleState == StatusLifecycleState.Removed
                    : value.IsActive)
            .Where(value =>
                phase != StatusTriggerPhase.OnApply ||
                sourceActionId is null ||
                value.SourceActionId == sourceActionId)
            .Where(value =>
                phase is not (StatusTriggerPhase.OnExpire or StatusTriggerPhase.OnRemove) ||
                sourceActionId is null ||
                value.StatusInstanceId == sourceActionId)
            .ToArray();
        var missingDefinition = active.FirstOrDefault(value =>
            _catalog.Snapshot.All(
                definition => definition.StatusDefinitionId != value.StatusDefinitionId));
        if (missingDefinition is not null)
        {
            return Failure(
                Guid.Empty,
                battle,
                phase,
                StatusResultCode.RecoveryRequired,
                "status.persisted_definition_unavailable",
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        var all = active
            .Select(value => (
                Instance: value,
                Definition: _catalog.Snapshot.FirstOrDefault(
                    definition => definition.StatusDefinitionId == value.StatusDefinitionId)))
            .Select(value => (value.Instance, value.Definition!))
            .ToArray();
        var planned = _planner.Create(
            battle,
            phase,
            sourceActionId,
            all,
            correlationId,
            _clock.UtcNow);
        if (!planned.Succeeded || planned.Value is null)
        {
            return Failure(
                Guid.Empty,
                battle,
                phase,
                StatusResultCode.TriggerEvidenceBlocked,
                planned.Error.Code);
        }

        using var gate = await _locks.AcquireAsync(
            planned.Value.StatusTriggerExecutionPlanId,
            cancellationToken);
        var record = _store.GetTriggerPlan(planned.Value.StatusTriggerExecutionPlanId);
        if (record is null)
        {
            record = new StatusTriggerPlanRecord(
                planned.Value,
                0,
                [],
                StatusMutationState.Planned,
                StatusRecoveryState.NotRequired,
                _clock.UtcNow);
            if (FailureInjection.Point == StatusFailurePoint.TriggerPlanPersistence ||
                !_store.SaveTriggerPlan(record, -1).Succeeded)
            {
                return Failure(
                    planned.Value.StatusTriggerExecutionPlanId,
                    battle,
                    phase,
                    StatusResultCode.PersistenceFailure,
                    "status.trigger_plan_persistence_failed");
            }

            Publish(
                StatusEventKind.StatusTriggerPlanCreated,
                planned.Value,
                null,
                "",
                correlationId);
        }

        return await ExecuteAsync(record, battle, cancellationToken);
    }

    public async Task<StatusTriggerResolutionResult> RecoverAsync(
        Guid statusTriggerExecutionPlanId,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        using var gate = await _locks.AcquireAsync(statusTriggerExecutionPlanId, cancellationToken);
        var record = _store.GetTriggerPlan(statusTriggerExecutionPlanId);
        if (record is null)
        {
            return Failure(
                statusTriggerExecutionPlanId,
                battle,
                StatusTriggerPhase.Recovery,
                StatusResultCode.StatusNotFound,
                "status.trigger_plan_not_found");
        }

        if (FailureInjection.Point == StatusFailurePoint.Recovery)
        {
            return Failure(
                statusTriggerExecutionPlanId,
                battle,
                record.Plan.TriggerPhase,
                StatusResultCode.RecoveryRequired,
                "status.trigger_recovery_failed",
                record.ResolutionCursor,
                record.Results,
                StatusRecoveryState.RecoveryRequired);
        }

        return await ExecuteAsync(
            record with { RecoveryState = StatusRecoveryState.Recovering },
            battle,
            cancellationToken);
    }

    private async Task<StatusTriggerResolutionResult> ExecuteAsync(
        StatusTriggerPlanRecord record,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        var results = record.Results.ToList();
        for (var cursor = record.ResolutionCursor;
             cursor < record.Plan.TriggerExecutions.Count;
             cursor++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var execution = record.Plan.TriggerExecutions[cursor];
            var completed = _store.GetTriggerResult(execution.TriggerExecutionId);
            if (completed is not null &&
                completed.RecoveryState != StatusRecoveryState.RecoveryRequired &&
                completed.State is StatusTriggerExecutionState.Committed or StatusTriggerExecutionState.Skipped)
            {
                if (results.All(value => value.TriggerExecutionId != completed.TriggerExecutionId))
                {
                    results.Add(completed);
                }

                record = record with
                {
                    ResolutionCursor = cursor + 1,
                    Results = StatusRuntimeCollections.Freeze(results),
                    UpdatedAtUtc = _clock.UtcNow
                };
                _ = _store.SaveTriggerPlan(record, cursor);
                continue;
            }

            if (completed?.RecoveryState == StatusRecoveryState.RecoveryRequired)
            {
                results.RemoveAll(value => value.TriggerExecutionId == completed.TriggerExecutionId);
            }

            var currentStatus = _authority.GetStatusInstance(execution.StatusInstanceId);
            StatusTriggerExecutionResult result;
            if (currentStatus is null ||
                (!currentStatus.IsActive &&
                 record.Plan.TriggerPhase != StatusTriggerPhase.OnRemove))
            {
                result = Skipped(record.Plan, execution, "status.trigger_status_inactive");
            }
            else
            {
                Publish(
                    StatusEventKind.StatusTriggerStarted,
                    record.Plan,
                    execution,
                    "",
                    record.Plan.CorrelationId);
                if (FailureInjection.Point == StatusFailurePoint.PeriodicDamage &&
                    execution.EffectType == StatusTriggerEffectType.PeriodicDamage)
                {
                    result = TriggerFailure(record.Plan, execution, "status.periodic_damage_failed");
                }
                else if (FailureInjection.Point == StatusFailurePoint.PeriodicHealing &&
                         execution.EffectType == StatusTriggerEffectType.PeriodicHealing)
                {
                    result = TriggerFailure(record.Plan, execution, "status.periodic_healing_failed");
                }
                else
                {
                    result = execution.EffectType switch
                    {
                        StatusTriggerEffectType.PeriodicDamage =>
                            await _damage.ExecuteAsync(
                                record.Plan,
                                execution,
                                battle,
                                _clock.UtcNow,
                                cancellationToken),
                        StatusTriggerEffectType.PeriodicHealing =>
                            await _healing.ExecuteAsync(
                                record.Plan,
                                execution,
                                battle,
                                _clock.UtcNow,
                                cancellationToken),
                        StatusTriggerEffectType.None =>
                            Committed(record.Plan, execution),
                        _ => TriggerFailure(
                            record.Plan,
                            execution,
                            "status.trigger_effect_evidence_blocked")
                    };
                }
            }

            if (result.RecoveryState == StatusRecoveryState.RecoveryRequired)
            {
                var failed = record with
                {
                    Results = StatusRuntimeCollections.Freeze(results.Append(result)),
                    State = StatusMutationState.RecoveryRequired,
                    RecoveryState = StatusRecoveryState.RecoveryRequired,
                    UpdatedAtUtc = _clock.UtcNow
                };
                _ = _store.SaveTriggerPlan(failed, cursor);
                Publish(
                    StatusEventKind.StatusRecoveryRequired,
                    record.Plan,
                    execution,
                    result.FailureCode,
                    record.Plan.CorrelationId);
                return Failure(
                    record.Plan.StatusTriggerExecutionPlanId,
                    battle,
                    record.Plan.TriggerPhase,
                    StatusResultCode.RecoveryRequired,
                    result.FailureCode,
                    cursor,
                    failed.Results,
                    StatusRecoveryState.RecoveryRequired);
            }

            results.Add(result);
            var progressed = record with
            {
                ResolutionCursor = cursor + 1,
                Results = StatusRuntimeCollections.Freeze(results),
                State = StatusMutationState.RuntimeCommitted,
                RecoveryState = StatusRecoveryState.NotRequired,
                UpdatedAtUtc = _clock.UtcNow
            };
            if (FailureInjection.Point is StatusFailurePoint.TriggerResultPersistence or StatusFailurePoint.TriggerCursor ||
                !_store.SaveTriggerPlan(progressed, cursor).Succeeded)
            {
                return Failure(
                    record.Plan.StatusTriggerExecutionPlanId,
                    battle,
                    record.Plan.TriggerPhase,
                    StatusResultCode.RecoveryRequired,
                    "status.trigger_cursor_persistence_failed",
                    cursor,
                    StatusRuntimeCollections.Freeze(results),
                    StatusRecoveryState.RecoveryRequired);
            }

            record = progressed;
            Publish(
                result.State == StatusTriggerExecutionState.Skipped
                    ? StatusEventKind.StatusTriggerSkipped
                    : result.Damage > 0
                        ? StatusEventKind.StatusPeriodicDamageApplied
                        : result.Heal > 0
                            ? StatusEventKind.StatusPeriodicHealingApplied
                            : StatusEventKind.StatusTriggerCompleted,
                record.Plan,
                execution,
                result.FailureCode,
                record.Plan.CorrelationId);
            AppendAudit(record.Plan, execution, result);
        }

        if (record.Plan.TriggerPhase == StatusTriggerPhase.RoundClosing)
        {
            var failureCode = await CloseDurationsAsync(battle, cancellationToken);
            if (failureCode.Length > 0)
            {
                return MarkPlanRecoveryRequired(record, battle, results, failureCode);
            }
        }
        else if (record.Plan.TriggerPhase == StatusTriggerPhase.BattleCompleted)
        {
            var failureCode = await CleanupBattleAsync(battle, cancellationToken);
            if (failureCode.Length > 0)
            {
                return MarkPlanRecoveryRequired(record, battle, results, failureCode);
            }
        }

        var completedRecord = record with
        {
            State = StatusMutationState.Completed,
            RecoveryState = record.RecoveryState == StatusRecoveryState.Recovering
                ? StatusRecoveryState.Recovered
                : StatusRecoveryState.NotRequired,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (record.State != StatusMutationState.Completed)
        {
            _ = _store.SaveTriggerPlan(completedRecord, record.ResolutionCursor);
        }

        return new StatusTriggerResolutionResult(
            record.Plan.StatusTriggerExecutionPlanId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.TriggerPhase,
            StatusResultCode.Success,
            "",
            record.Plan.TriggerExecutions.Count,
            StatusRuntimeCollections.Freeze(results),
            completedRecord.RecoveryState,
            [],
            _clock.UtcNow);
    }

    private async Task<string> CloseDurationsAsync(
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        foreach (var participant in battle.Participants)
        {
            var snapshot = _authority.GetParticipantSnapshot(
                battle.BattleInstanceId,
                participant.ParticipantId,
                _clock.UtcNow);
            foreach (var status in snapshot.Statuses.Where(value => value.IsActive))
            {
                var definition = _catalog.Snapshot.FirstOrDefault(
                    value => value.StatusDefinitionId == status.StatusDefinitionId);
                if (definition is null ||
                    definition.DurationPolicy.PolicyType != StatusDurationPolicyType.Rounds)
                {
                    continue;
                }

                var decision = _duration.CloseRound(
                    definition,
                    status,
                    battle.CurrentRoundNumber);
                if (decision.RemainingRounds == 0)
                {
                    var expiration = await ResolveAsync(
                        battle,
                        StatusTriggerPhase.OnExpire,
                        status.StatusInstanceId,
                        $"status-expire:{status.StatusInstanceId:N}",
                        cancellationToken);
                    if (!expiration.Succeeded)
                    {
                        return expiration.FailureCode;
                    }

                    var cleanup = await _effects.CleanupAsync(
                        battle,
                        status,
                        StatusRemovalReason.Expired,
                        cancellationToken);
                    if (!cleanup.Succeeded)
                    {
                        return cleanup.FailureCode;
                    }

                    continue;
                }

                var latest = _authority.GetParticipantSnapshot(
                    battle.BattleInstanceId,
                    participant.ParticipantId,
                    _clock.UtcNow);
                var current = _authority.GetStatusInstance(status.StatusInstanceId);
                if (current is null || !current.IsActive)
                {
                    continue;
                }

                _ = _authority.UpdateStatusInstance(
                    current with
                    {
                        RemainingRounds = decision.RemainingRounds,
                        UpdatedAtUtc = _clock.UtcNow
                    },
                    current.RuntimeVersion,
                    latest.StatusVersion,
                    BattleRuntimeHash.SafeId(
                        $"status-duration-close:{battle.BattleInstanceId:N}:" +
                        $"{battle.CurrentRoundNumber}:{current.StatusInstanceId:N}"));
            }
        }

        return "";
    }

    private async Task<string> CleanupBattleAsync(
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        Publish(
            StatusEventKind.BattleStatusCleanupStarted,
            new StatusTriggerExecutionPlan(
                Guid.Empty,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                StatusTriggerPhase.BattleCompleted,
                null,
                [],
                battle.BattleVersion,
                _clock.UtcNow,
                battle.CorrelationId),
            null,
            "",
            battle.CorrelationId);
        foreach (var participant in battle.Participants)
        {
            var statuses = _authority.GetParticipantSnapshot(
                    battle.BattleInstanceId,
                    participant.ParticipantId,
                    _clock.UtcNow)
                .Statuses
                .Where(value => value.IsActive)
                .ToArray();
            foreach (var status in statuses)
            {
                var cleanup = await _effects.CleanupAsync(
                    battle,
                    status,
                    StatusRemovalReason.BattleCompleted,
                    cancellationToken);
                if (!cleanup.Succeeded)
                {
                    return cleanup.FailureCode;
                }

                var removal = await ResolveAsync(
                    battle,
                    StatusTriggerPhase.OnRemove,
                    status.StatusInstanceId,
                    $"status-battle-remove:{status.StatusInstanceId:N}",
                    cancellationToken);
                if (!removal.Succeeded)
                {
                    return removal.FailureCode;
                }
            }
        }

        Publish(
            StatusEventKind.BattleStatusCleanupCompleted,
            new StatusTriggerExecutionPlan(
                Guid.Empty,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                StatusTriggerPhase.BattleCompleted,
                null,
                [],
                battle.BattleVersion,
                _clock.UtcNow,
                battle.CorrelationId),
            null,
            "",
            battle.CorrelationId);
        return "";
    }

    private StatusTriggerResolutionResult MarkPlanRecoveryRequired(
        StatusTriggerPlanRecord record,
        BattleInstance battle,
        IReadOnlyList<StatusTriggerExecutionResult> results,
        string failureCode)
    {
        var failed = record with
        {
            Results = StatusRuntimeCollections.Freeze(results),
            State = StatusMutationState.RecoveryRequired,
            RecoveryState = StatusRecoveryState.RecoveryRequired,
            UpdatedAtUtc = _clock.UtcNow
        };
        _ = _store.SaveTriggerPlan(failed, record.ResolutionCursor);
        Publish(
            StatusEventKind.StatusRecoveryRequired,
            record.Plan,
            null,
            failureCode,
            record.Plan.CorrelationId);
        return Failure(
            record.Plan.StatusTriggerExecutionPlanId,
            battle,
            record.Plan.TriggerPhase,
            StatusResultCode.RecoveryRequired,
            failureCode,
            record.ResolutionCursor,
            failed.Results,
            StatusRecoveryState.RecoveryRequired);
    }

    private StatusTriggerResolutionResult Failure(
        Guid planId,
        BattleInstance battle,
        StatusTriggerPhase phase,
        StatusResultCode code,
        string failureCode,
        int cursor = 0,
        IReadOnlyList<StatusTriggerExecutionResult>? results = null,
        StatusRecoveryState recoveryState = StatusRecoveryState.NotRequired) =>
        new(
            planId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            phase,
            code,
            failureCode,
            cursor,
            results ?? [],
            recoveryState,
            [],
            _clock.UtcNow);

    private StatusTriggerExecutionResult Committed(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution) =>
        new(
            execution.TriggerExecutionId,
            plan.StatusTriggerExecutionPlanId,
            execution.StatusInstanceId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            StatusTriggerExecutionState.Committed,
            StatusResultCode.Success,
            "",
            0,
            0,
            0,
            0,
            execution.ExpectedTargetVersion,
            execution.ExpectedTargetVersion,
            false,
            StatusRecoveryState.NotRequired,
            _clock.UtcNow);

    private StatusTriggerExecutionResult Skipped(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        string failureCode) =>
        Committed(plan, execution) with
        {
            State = StatusTriggerExecutionState.Skipped,
            FailureCode = failureCode
        };

    private StatusTriggerExecutionResult TriggerFailure(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        string failureCode) =>
        Committed(plan, execution) with
        {
            State = StatusTriggerExecutionState.RecoveryRequired,
            ResultCode = StatusResultCode.TriggerExecutionFailure,
            FailureCode = failureCode,
            RecoveryState = StatusRecoveryState.RecoveryRequired
        };

    private void Publish(
        StatusEventKind kind,
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution? execution,
        string failureCode,
        string correlationId)
    {
        _events.Publish(new StatusEvent(
            Guid.NewGuid(),
            kind,
            plan.BattleInstanceId,
            plan.RoundNumber,
            null,
            null,
            plan.StatusTriggerExecutionPlanId == Guid.Empty
                ? null
                : plan.StatusTriggerExecutionPlanId,
            execution?.TriggerExecutionId,
            execution?.StatusInstanceId,
            execution?.StatusDefinitionId,
            execution?.SourceParticipantId,
            execution?.TargetParticipantId,
            execution?.State.ToString() ?? "",
            failureCode,
            _clock.UtcNow,
            correlationId));
    }

    private void AppendAudit(
        StatusTriggerExecutionPlan plan,
        StatusTriggerExecution execution,
        StatusTriggerExecutionResult result)
    {
        if (FailureInjection.Point == StatusFailurePoint.Audit)
        {
            return;
        }

        _audit.Append(new StatusAuditRecord(
            Guid.NewGuid(),
            null,
            null,
            plan.StatusTriggerExecutionPlanId,
            execution.TriggerExecutionId,
            execution.StatusInstanceId,
            execution.StatusDefinitionId,
            plan.BattleInstanceId,
            plan.RoundNumber,
            plan.TriggerPhase,
            execution.TriggerIndex,
            execution.ExecutionOrder,
            BattleRuntimeHash.SafeId(execution.IdempotencyKey),
            plan.CorrelationId,
            execution.SourceParticipantId,
            execution.TargetParticipantId,
            plan.SourceActionId,
            null,
            null,
            execution.EffectType.ToString(),
            execution.StatusStackSnapshot,
            execution.StatusStackSnapshot,
            null,
            null,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            null,
            result.HpBefore,
            result.Damage,
            result.Heal,
            result.HpAfter,
            result.ResultCode,
            result.FailureCode,
            null,
            result.RecoveryState,
            StatusRuntimeCollections.Freeze(new[] { execution.PolicyStatus }),
            plan.CreatedAtUtc,
            result.CompletedAtUtc));
    }
}

public sealed class SkillApplyStatusEffectHandler : ISkillEffectHandler
{
    private readonly IStatusEffectCoordinator _coordinator;
    private readonly IBattleStatusAuthority _authority;
    private readonly IStatusTriggerCoordinator? _triggers;

    public SkillApplyStatusEffectHandler(
        IStatusEffectCoordinator coordinator,
        IBattleStatusAuthority authority,
        IStatusTriggerCoordinator? triggers = null)
    {
        _coordinator = coordinator;
        _authority = authority;
        _triggers = triggers;
    }

    public SkillEffectType EffectType => SkillEffectType.ApplyStatus;

    public async Task<SkillEffectResult> ExecuteAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken)
    {
        if (context.EffectPlan.ValuePlan.BaseValue is <= 0 or > int.MaxValue)
        {
            return Failed(context, "status.definition_id_invalid");
        }

        var definitionId = checked((int)context.EffectPlan.ValuePlan.BaseValue);
        var snapshot = _authority.GetParticipantSnapshot(
            context.Battle.BattleInstanceId,
            context.Target.ParticipantId,
            context.Now);
        var applicationId = BattleRuntimeHash.DeterministicGuid(
            $"status-application:skill:{context.EffectPlan.EffectExecutionId:N}");
        var result = await _coordinator.ApplyAsync(
            context.Battle,
            new StatusApplicationRequest(
                applicationId,
                $"status-application:skill:{context.EffectPlan.EffectExecutionId:N}",
                context.Battle.BattleInstanceId,
                context.Battle.CurrentRoundNumber,
                context.BattleAction.ActionId,
                context.Plan.SkillExecutionId,
                context.EffectPlan.EffectExecutionId,
                context.Source.ParticipantId,
                context.Target.ParticipantId,
                definitionId,
                null,
                null,
                context.Battle.BattleVersion,
                snapshot.StatusVersion,
                context.Battle.BattleType == BattleType.InternalTest
                    ? StatusRequestSource.TrustedInternalTest
                    : StatusRequestSource.SkillEffect,
                context.Now,
                context.Plan.CorrelationId),
            cancellationToken);
        if (result.Succeeded && _triggers is not null)
        {
            var trigger = await _triggers.ResolveAsync(
                context.Battle,
                StatusTriggerPhase.OnApply,
                context.BattleAction.ActionId,
                context.Plan.CorrelationId,
                cancellationToken);
            if (!trigger.Succeeded)
            {
                return ToSkillResult(
                    context,
                    false,
                    false,
                    trigger.FailureCode,
                    trigger,
                    true);
            }

            return ToSkillResult(
                context,
                result.Succeeded,
                result.IsDuplicate,
                result.FailureCode,
                trigger);
        }

        return ToSkillResult(context, result.Succeeded, result.IsDuplicate, result.FailureCode);
    }

    private static SkillEffectResult ToSkillResult(
        SkillEffectContext context,
        bool succeeded,
        bool duplicate,
        string failureCode,
        StatusTriggerResolutionResult? trigger = null,
        bool recoveryRequired = false)
    {
        var committed = trigger?.Results
            .Where(value => value.State == StatusTriggerExecutionState.Committed)
            .OrderBy(value => value.ExecutionOrder)
            .ToArray() ?? [];
        var first = committed.FirstOrDefault();
        var last = committed.LastOrDefault();
        var damage = committed.Aggregate(0L, (total, value) => checked(total + value.Damage));
        var heal = committed.Aggregate(0L, (total, value) => checked(total + value.Heal));
        var hpAfter = last?.HpAfter ?? context.Target.CurrentHp;
        return new SkillEffectResult(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            SkillEffectType.ApplyStatus,
            succeeded
                ? SkillEffectExecutionState.Committed
                : recoveryRequired
                    ? SkillEffectExecutionState.RecoveryRequired
                    : SkillEffectExecutionState.Rejected,
            context.EffectPlan.ValuePlan.BaseValue,
            damage,
            heal,
            first?.HpBefore ?? context.Target.CurrentHp,
            hpAfter,
            context.Target.MaximumHp,
            hpAfter <= 0,
            first?.RuntimeVersionBefore ?? context.Target.RuntimeVersion,
            last?.RuntimeVersionAfter ?? context.Target.RuntimeVersion,
            context.EffectPlan.PolicyStatus,
            failureCode,
            duplicate,
            context.Now);
    }

    private static SkillEffectResult Failed(SkillEffectContext context, string failureCode) =>
        ToSkillResult(context, false, false, failureCode);
}

public sealed class SkillRemoveStatusEffectHandler : ISkillEffectHandler
{
    private readonly IStatusEffectCoordinator _coordinator;
    private readonly IBattleStatusAuthority _authority;
    private readonly IStatusTriggerCoordinator? _triggers;

    public SkillRemoveStatusEffectHandler(
        IStatusEffectCoordinator coordinator,
        IBattleStatusAuthority authority,
        IStatusTriggerCoordinator? triggers = null)
    {
        _coordinator = coordinator;
        _authority = authority;
        _triggers = triggers;
    }

    public SkillEffectType EffectType => SkillEffectType.RemoveStatus;

    public async Task<SkillEffectResult> ExecuteAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken)
    {
        if (context.EffectPlan.ValuePlan.BaseValue is <= 0 or > int.MaxValue)
        {
            return Failed(context, "status.definition_id_invalid");
        }

        var definitionId = checked((int)context.EffectPlan.ValuePlan.BaseValue);
        var snapshot = _authority.GetParticipantSnapshot(
            context.Battle.BattleInstanceId,
            context.Target.ParticipantId,
            context.Now);
        var status = snapshot.Statuses
            .Where(value => value.IsActive && value.StatusDefinitionId == definitionId)
            .OrderBy(value => value.AppliedRound)
            .ThenBy(value => value.StatusInstanceId)
            .FirstOrDefault();
        if (status is null)
        {
            return Failed(context, "status.instance_not_found");
        }

        var removalId = BattleRuntimeHash.DeterministicGuid(
            $"status-removal:skill:{context.EffectPlan.EffectExecutionId:N}");
        var result = await _coordinator.RemoveAsync(
            context.Battle,
            new StatusRemovalRequest(
                removalId,
                $"status-removal:skill:{context.EffectPlan.EffectExecutionId:N}",
                context.Battle.BattleInstanceId,
                context.Battle.CurrentRoundNumber,
                context.BattleAction.ActionId,
                context.Source.ParticipantId,
                context.Target.ParticipantId,
                status.StatusInstanceId,
                definitionId,
                StatusRemovalReason.Dispelled,
                snapshot.StatusVersion,
                context.Battle.BattleType == BattleType.InternalTest
                    ? StatusRequestSource.TrustedInternalTest
                    : StatusRequestSource.SkillEffect,
                context.Now,
                context.Plan.CorrelationId),
            cancellationToken);
        if (result.Succeeded && _triggers is not null)
        {
            var trigger = await _triggers.ResolveAsync(
                context.Battle,
                StatusTriggerPhase.OnRemove,
                status.StatusInstanceId,
                context.Plan.CorrelationId,
                cancellationToken);
            if (!trigger.Succeeded)
            {
                return ToSkillResult(
                    context,
                    false,
                    false,
                    trigger.FailureCode,
                    trigger,
                    true);
            }

            return ToSkillResult(
                context,
                result.Succeeded,
                result.IsDuplicate,
                result.FailureCode,
                trigger);
        }

        return ToSkillResult(context, result.Succeeded, result.IsDuplicate, result.FailureCode);
    }

    private static SkillEffectResult ToSkillResult(
        SkillEffectContext context,
        bool succeeded,
        bool duplicate,
        string failureCode,
        StatusTriggerResolutionResult? trigger = null,
        bool recoveryRequired = false)
    {
        var committed = trigger?.Results
            .Where(value => value.State == StatusTriggerExecutionState.Committed)
            .OrderBy(value => value.ExecutionOrder)
            .ToArray() ?? [];
        var first = committed.FirstOrDefault();
        var last = committed.LastOrDefault();
        var damage = committed.Aggregate(0L, (total, value) => checked(total + value.Damage));
        var heal = committed.Aggregate(0L, (total, value) => checked(total + value.Heal));
        var hpAfter = last?.HpAfter ?? context.Target.CurrentHp;
        return new SkillEffectResult(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            SkillEffectType.RemoveStatus,
            succeeded
                ? SkillEffectExecutionState.Committed
                : recoveryRequired
                    ? SkillEffectExecutionState.RecoveryRequired
                    : SkillEffectExecutionState.Rejected,
            context.EffectPlan.ValuePlan.BaseValue,
            damage,
            heal,
            first?.HpBefore ?? context.Target.CurrentHp,
            hpAfter,
            context.Target.MaximumHp,
            hpAfter <= 0,
            first?.RuntimeVersionBefore ?? context.Target.RuntimeVersion,
            last?.RuntimeVersionAfter ?? context.Target.RuntimeVersion,
            context.EffectPlan.PolicyStatus,
            failureCode,
            duplicate,
            context.Now);
    }

    private static SkillEffectResult Failed(SkillEffectContext context, string failureCode) =>
        ToSkillResult(context, false, false, failureCode);
}

public sealed class StatusRuntimeInspector
{
    private readonly IStatusDefinitionCatalog _catalog;
    private readonly IStatusInspectorSource _source;
    private readonly IStatusAuditLedger _audit;
    private readonly IBattleClock _clock;

    public StatusRuntimeInspector(
        IStatusDefinitionCatalog catalog,
        IStatusInspectorSource source,
        IStatusAuditLedger audit,
        IBattleClock clock)
    {
        _catalog = catalog;
        _source = source;
        _audit = audit;
        _clock = clock;
    }

    public StatusFailureInjection FailureInjection { get; } = new();

    public StatusInspectorSnapshot Query(StatusInspectorQuery query)
    {
        if (FailureInjection.Point == StatusFailurePoint.Inspector)
        {
            return new StatusInspectorSnapshot(
                [],
                [],
                [],
                [],
                [],
                [],
                Math.Max(0, query.Offset),
                Math.Clamp(query.Limit, 1, 500),
                "status.inspector_failed",
                _clock.UtcNow);
        }

        try
        {
            var offset = Math.Max(0, query.Offset);
            var limit = Math.Clamp(query.Limit, 1, 500);
            var definitions = _catalog.Snapshot
                .Where(value => query.StatusDefinitionId is null ||
                                value.StatusDefinitionId == query.StatusDefinitionId)
                .Where(value => query.Category is null || value.StatusCategory == query.Category)
                .Where(value => query.Polarity is null || value.StatusPolarity == query.Polarity)
                .Where(value => !query.EvidenceBlockedOnly ||
                                value.PolicyStatus == CombatPolicyStatus.EvidenceBlocked)
                .Where(value => !query.TestOnlyOnly ||
                                value.PolicyStatus == CombatPolicyStatus.TestOnly)
                .Select(value => new StatusDefinitionInspectorItem(
                    value.StatusDefinitionId,
                    value.Name,
                    value.StatusCategory,
                    value.StatusPolarity,
                    value.StackPolicy.PolicyType,
                    value.DurationPolicy.PolicyType,
                    value.TriggerDefinitions.Count,
                    value.ModifierDefinitions.Count,
                    value.ActionRestrictionDefinitions.Count,
                    value.Enabled,
                    value.ContentVersion,
                    value.PolicyStatus,
                    value.ProtocolStatus))
                .Skip(offset)
                .Take(limit)
                .ToArray();
            var instances = _source.StatusInstances
                .Where(value => query.BattleInstanceId is null ||
                                value.BattleInstanceId == query.BattleInstanceId)
                .Where(value => query.ParticipantId is null ||
                                value.TargetParticipantId == query.ParticipantId)
                .Where(value => query.SourceParticipantId is null ||
                                value.SourceParticipantId == query.SourceParticipantId)
                .Where(value => query.TargetParticipantId is null ||
                                value.TargetParticipantId == query.TargetParticipantId)
                .Where(value => query.StatusDefinitionId is null ||
                                value.StatusDefinitionId == query.StatusDefinitionId)
                .Where(value => query.StatusInstanceId is null ||
                                value.StatusInstanceId == query.StatusInstanceId)
                .Where(value => query.LifecycleState is null ||
                                value.LifecycleState == query.LifecycleState)
                .Where(value => !query.ActiveOnly || value.IsActive)
                .Where(value => !query.ExpiredOnly ||
                                value.LifecycleState == StatusLifecycleState.Expired)
                .Where(value => !query.RemovedOnly ||
                                value.LifecycleState == StatusLifecycleState.Removed)
                .Where(value => !query.RecoveryRequiredOnly ||
                                value.LifecycleState == StatusLifecycleState.RecoveryRequired)
                .Select(value => new StatusInstanceInspectorItem(
                    value.StatusInstanceId,
                    value.BattleInstanceId,
                    value.TargetParticipantId,
                    value.SourceParticipantId,
                    value.StatusDefinitionId,
                    value.LifecycleState,
                    value.StackCount,
                    value.MaximumStacks,
                    value.AppliedRound,
                    value.LastRefreshedRound,
                    value.ExpiresAfterRound,
                    value.RemainingRounds,
                    value.RuntimeVersion,
                    value.TriggerCursor,
                    value.PolicyStatus,
                    value.AppliedAtUtc,
                    value.UpdatedAtUtc,
                    value.RemovedAtUtc,
                    value.RemovalReason))
                .Skip(offset)
                .Take(limit)
                .ToArray();
            var triggers = _source.StatusTriggerPlans
                .SelectMany(record => record.Results.Select(result => (record.Plan, Result: result)))
                .Where(value => query.BattleInstanceId is null ||
                                value.Plan.BattleInstanceId == query.BattleInstanceId)
                .Where(value => query.RoundNumber is null ||
                                value.Plan.RoundNumber == query.RoundNumber)
                .Where(value => query.StatusInstanceId is null ||
                                value.Result.StatusInstanceId == query.StatusInstanceId)
                .Where(value => query.TriggerPhase is null ||
                                value.Result.TriggerPhase == query.TriggerPhase)
                .Where(value => !query.FailedOnly ||
                                value.Result.ResultCode != StatusResultCode.Success)
                .Where(value => !query.PendingOnly ||
                                value.Result.State == StatusTriggerExecutionState.Pending)
                .Where(value => !query.RecoveryRequiredOnly ||
                                value.Result.RecoveryState == StatusRecoveryState.RecoveryRequired)
                .Select(value => new StatusTriggerInspectorItem(
                    value.Result.TriggerExecutionId,
                    value.Result.StatusInstanceId,
                    value.Result.BattleInstanceId,
                    value.Result.RoundNumber,
                    value.Result.TriggerPhase,
                    value.Result.TriggerIndex,
                    value.Result.ExecutionOrder,
                    value.Result.State,
                    value.Result.ResultCode,
                    value.Result.FailureCode,
                    value.Result.Damage,
                    value.Result.Heal,
                    value.Result.HpBefore,
                    value.Result.HpAfter,
                    value.Result.RecoveryState))
                .Skip(offset)
                .Take(limit)
                .ToArray();
            var modifiers = _source.StatusModifierSnapshots
                .Where(value => query.ParticipantId is null ||
                                value.SourceParticipantId == query.ParticipantId ||
                                value.TargetParticipantId == query.ParticipantId)
                .Where(value => query.ModifierType is null ||
                                value.ModifierTypes.Contains(query.ModifierType.Value))
                .Skip(offset)
                .Take(limit)
                .ToArray();
            var restrictions = _source.StatusRestrictionResults
                .Where(value => query.ParticipantId is null ||
                                value.ParticipantId == query.ParticipantId)
                .Where(value => !query.FailedOnly ||
                                value.Result != StatusResultCode.Success)
                .Skip(offset)
                .Take(limit)
                .ToArray();
            var audit = _audit.Snapshot
                .Where(value => query.BattleInstanceId is null ||
                                value.BattleInstanceId == query.BattleInstanceId)
                .Where(value => query.RoundNumber is null ||
                                value.RoundNumber == query.RoundNumber)
                .Where(value => query.StatusDefinitionId is null ||
                                value.StatusDefinitionId == query.StatusDefinitionId)
                .Where(value => query.StatusInstanceId is null ||
                                value.StatusInstanceId == query.StatusInstanceId)
                .Where(value => !query.DuplicateOnly ||
                                value.Result == StatusResultCode.DuplicateCompleted)
                .Skip(offset)
                .Take(limit)
                .ToArray();
            return new StatusInspectorSnapshot(
                StatusRuntimeCollections.Freeze(definitions),
                StatusRuntimeCollections.Freeze(instances),
                StatusRuntimeCollections.Freeze(triggers),
                StatusRuntimeCollections.Freeze(modifiers),
                StatusRuntimeCollections.Freeze(restrictions),
                StatusRuntimeCollections.Freeze(audit),
                offset,
                limit,
                "",
                _clock.UtcNow);
        }
        catch
        {
            return new StatusInspectorSnapshot(
                [],
                [],
                [],
                [],
                [],
                [],
                Math.Max(0, query.Offset),
                Math.Clamp(query.Limit, 1, 500),
                "status.inspector_failed",
                _clock.UtcNow);
        }
    }
}
