namespace God2.ClassicServer.Runtime;

public sealed record SkillInspectorQuery(
    Guid? BattleInstanceId = null,
    int? RoundNumber = null,
    long? CharacterId = null,
    Guid? ParticipantId = null,
    Guid? TargetParticipantId = null,
    int? SkillDefinitionId = null,
    SkillCategory? SkillCategory = null,
    SkillEffectType? EffectType = null,
    SkillExecutionState? ExecutionState = null,
    SkillResultCode? Result = null,
    bool FailedOnly = false,
    bool PendingOnly = false,
    bool RecoveryRequiredOnly = false,
    bool EvidenceBlockedOnly = false,
    bool TestOnlyOnly = false,
    bool DuplicateOnly = false,
    int Offset = 0,
    int Limit = 100);

public sealed record SkillDefinitionInspectorItem(
    int SkillDefinitionId,
    string Name,
    SkillCategory Category,
    SkillActionCategory ActionCategory,
    SkillTargetPolicyType TargetPolicy,
    SkillResourceType CostType,
    CombatPolicyStatus CostPolicyStatus,
    CombatPolicyStatus CooldownPolicyStatus,
    int EffectCount,
    IReadOnlyList<SkillEffectType> EffectTypes,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus);

public sealed record SkillExecutionInspectorItem(
    Guid SkillExecutionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    int SkillDefinitionId,
    SkillExecutionState State,
    SkillResultCode? Result,
    string FailureCode,
    int TargetCount,
    int EffectCount,
    int CurrentEffectIndex,
    int CurrentTargetIndex,
    SkillCostReservationState CostState,
    bool CooldownCommitted,
    bool IsDuplicate,
    SkillRecoveryState RecoveryState,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SkillEffectInspectorItem(
    Guid EffectExecutionId,
    Guid SkillExecutionId,
    int EffectIndex,
    int TargetIndex,
    Guid TargetParticipantId,
    SkillEffectType EffectType,
    SkillEffectExecutionState State,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public sealed record SkillCostInspectorItem(
    Guid ReservationId,
    Guid ParticipantId,
    int SkillDefinitionId,
    SkillResourceType ResourceType,
    long Amount,
    SkillCostReservationState State,
    CombatPolicyStatus PolicyStatus,
    string SafeIdempotencyId);

public sealed record SkillInspectorSnapshot(
    IReadOnlyList<SkillDefinitionInspectorItem> Definitions,
    IReadOnlyList<SkillExecutionInspectorItem> Executions,
    IReadOnlyList<SkillEffectInspectorItem> Effects,
    IReadOnlyList<SkillUsageState> Usage,
    IReadOnlyList<SkillCostInspectorItem> Costs,
    int TotalExecutions,
    string FailureCode,
    DateTimeOffset GeneratedAtUtc);

public sealed class SkillRuntimeInspector
{
    private readonly ISkillDefinitionCatalog _catalog;
    private readonly ISkillInspectorSource _source;
    private readonly IBattleClock _clock;

    public SkillRuntimeInspector(
        ISkillDefinitionCatalog catalog,
        ISkillInspectorSource source,
        IBattleClock clock)
    {
        _catalog = catalog;
        _source = source;
        _clock = clock;
    }

    public SkillFailureInjection FailureInjection { get; } = new();

    public SkillInspectorSnapshot Query(SkillInspectorQuery query)
    {
        if (FailureInjection.Point == SkillFailurePoint.Inspector)
        {
            return new SkillInspectorSnapshot(
                [],
                [],
                [],
                [],
                [],
                0,
                "skill.inspector_failed",
                _clock.UtcNow);
        }

        var definitions = _catalog.Snapshot
            .Where(value => query.SkillDefinitionId is null ||
                            value.SkillDefinitionId == query.SkillDefinitionId)
            .Where(value => query.SkillCategory is null ||
                            value.SkillCategory == query.SkillCategory)
            .Where(value => !query.EvidenceBlockedOnly ||
                            value.PolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
                            value.ProtocolStatus == CombatPolicyStatus.EvidenceBlocked)
            .Where(value => !query.TestOnlyOnly ||
                            value.PolicyStatus == CombatPolicyStatus.TestOnly)
            .Select(value => new SkillDefinitionInspectorItem(
                value.SkillDefinitionId,
                value.Name,
                value.SkillCategory,
                value.ActionCategory,
                value.TargetPolicy,
                value.CostDefinition.ResourceType,
                value.CostDefinition.PolicyStatus,
                value.CooldownDefinition.PolicyStatus,
                value.EffectDefinitions.Count,
                SkillCollections.Freeze(value.EffectDefinitions.Select(effect => effect.EffectType)),
                value.Enabled,
                value.ContentVersion,
                value.PolicyStatus,
                value.ProtocolStatus))
            .ToArray();
        var source = _source.Executions
            .Where(value => query.BattleInstanceId is null ||
                            value.Plan.BattleInstanceId == query.BattleInstanceId)
            .Where(value => query.RoundNumber is null ||
                            value.Plan.RoundNumber == query.RoundNumber)
            .Where(value => query.CharacterId is null ||
                            value.Plan.SourceParticipantSnapshot.CharacterId == query.CharacterId)
            .Where(value => query.ParticipantId is null ||
                            value.Plan.ParticipantId == query.ParticipantId)
            .Where(value => query.TargetParticipantId is null ||
                            value.Plan.TargetParticipantSnapshots.Any(
                                target => target.ParticipantId == query.TargetParticipantId))
            .Where(value => query.SkillDefinitionId is null ||
                            value.Plan.SkillDefinitionId == query.SkillDefinitionId)
            .Where(value => query.EffectType is null ||
                            value.Plan.EffectPlans.Any(effect => effect.EffectType == query.EffectType))
            .Where(value => query.ExecutionState is null ||
                            value.State == query.ExecutionState)
            .Where(value => query.Result is null ||
                            value.Result?.ResultCode == query.Result)
            .Where(value => !query.FailedOnly ||
                            value.State is SkillExecutionState.Rejected or SkillExecutionState.Faulted or
                                SkillExecutionState.RecoveryRequired)
            .Where(value => !query.PendingOnly ||
                            value.State is not (
                                SkillExecutionState.Completed or
                                SkillExecutionState.Rejected or
                                SkillExecutionState.Faulted))
            .Where(value => !query.RecoveryRequiredOnly ||
                            value.RecoveryState == SkillRecoveryState.RecoveryRequired)
            .Where(value => !query.DuplicateOnly ||
                            value.Result?.IsDuplicate == true)
            .OrderByDescending(value => value.UpdatedAtUtc)
            .ToArray();
        var total = source.Length;
        var page = source
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToArray();
        var executions = page.Select(value =>
        {
            var current = value.Plan.EffectPlans.ElementAtOrDefault(value.ResolutionCursor);
            return new SkillExecutionInspectorItem(
                value.Plan.SkillExecutionId,
                value.Plan.BattleInstanceId,
                value.Plan.RoundNumber,
                value.Plan.ParticipantId,
                value.Plan.SkillDefinitionId,
                value.State,
                value.Result?.ResultCode,
                value.Result?.FailureCode ?? "",
                value.Plan.TargetParticipantSnapshots.Count,
                value.Plan.EffectPlans.Count,
                current?.EffectIndex ?? value.Plan.EffectPlans.Count,
                current?.TargetIndex ?? 0,
                value.CostReservation?.State ?? SkillCostReservationState.Created,
                value.State is SkillExecutionState.CooldownCommitted or SkillExecutionState.Completed,
                value.Result?.IsDuplicate ?? false,
                value.RecoveryState,
                value.Plan.CreatedAtUtc,
                value.Result?.CompletedAtUtc);
        }).ToArray();
        var effects = page
            .SelectMany(value => value.EffectResults)
            .Select(value => new SkillEffectInspectorItem(
                value.EffectExecutionId,
                value.SkillExecutionId,
                value.EffectIndex,
                value.TargetIndex,
                value.TargetParticipantId,
                value.EffectType,
                value.State,
                value.Damage,
                value.Heal,
                value.HpBefore,
                value.HpAfter,
                value.PolicyStatus,
                value.FailureCode))
            .ToArray();
        var usage = _source.Usage
            .Where(value => query.ParticipantId is null || value.ParticipantId == query.ParticipantId)
            .Where(value => query.SkillDefinitionId is null ||
                            value.SkillDefinitionId == query.SkillDefinitionId)
            .ToArray();
        var policyBySkill = _catalog.Snapshot.ToDictionary(
            value => value.SkillDefinitionId,
            value => value.CostDefinition.PolicyStatus);
        var costs = _source.CostReservations
            .Where(value => query.ParticipantId is null || value.ParticipantId == query.ParticipantId)
            .Where(value => query.SkillDefinitionId is null ||
                            value.SkillDefinitionId == query.SkillDefinitionId)
            .Select(value => new SkillCostInspectorItem(
                value.ReservationId,
                value.ParticipantId,
                value.SkillDefinitionId,
                value.ResourceType,
                value.Amount,
                value.State,
                policyBySkill.GetValueOrDefault(
                    value.SkillDefinitionId,
                    CombatPolicyStatus.EvidenceBlocked),
                value.IdempotencySafeId))
            .ToArray();
        return new SkillInspectorSnapshot(
            SkillCollections.Freeze(definitions),
            SkillCollections.Freeze(executions),
            SkillCollections.Freeze(effects),
            SkillCollections.Freeze(usage),
            SkillCollections.Freeze(costs),
            total,
            "",
            _clock.UtcNow);
    }
}
