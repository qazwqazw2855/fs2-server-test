using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum StatusCategory
{
    Buff,
    Debuff,
    Control,
    PeriodicDamage,
    PeriodicHealing,
    Modifier,
    Protection,
    Utility,
    Passive,
    Scripted,
    Unknown
}

public enum StatusPolarity
{
    Positive,
    Negative,
    Neutral,
    Unknown
}

public enum StatusApplicationPolicyType
{
    ServerOwned,
    TestOnly,
    ContentBacked,
    EvidenceBlocked,
    Unknown
}

public enum StatusStackPolicyType
{
    RejectDuplicate,
    RefreshDuration,
    AddStacks,
    ReplaceExisting,
    IndependentInstances,
    HighestValueWins,
    LatestWins,
    SourceScoped,
    Unknown
}

public enum StatusDurationPolicyType
{
    Rounds,
    UntilBattleEnd,
    UntilRemoved,
    UntilSourceDefeated,
    UntilTargetDefeated,
    ActionCount,
    TriggerCount,
    WallClock,
    Permanent,
    Unknown
}

public enum StatusTriggerPhase
{
    OnApply,
    RoundOpening,
    BeforeActionValidation,
    BeforeActionResolution,
    BeforeEffectResolution,
    AfterDamage,
    AfterHealing,
    AfterEffectResolution,
    AfterActionResolution,
    RoundClosing,
    OnExpire,
    OnRemove,
    ParticipantDefeated,
    BattleCompleted,
    Recovery,
    Unknown
}

public enum StatusTriggerEffectType
{
    None,
    PeriodicDamage,
    PeriodicHealing,
    RemoveStatus,
    Scripted,
    Unknown
}

public enum StatusModifierType
{
    OutgoingDamageFlat,
    OutgoingDamageMultiplier,
    IncomingDamageFlat,
    IncomingDamageMultiplier,
    OutgoingHealingFlat,
    OutgoingHealingMultiplier,
    IncomingHealingFlat,
    IncomingHealingMultiplier,
    PreventAllAction,
    PreventSkillAction,
    PreventBasicAttack,
    PreventTargeting,
    InitiativeModifier,
    AccuracyModifier,
    DefenseModifier,
    AttackModifier,
    MaximumHpModifier,
    Unknown
}

public enum StatusActionRestrictionType
{
    PreventAllAction,
    PreventSkillAction,
    PreventBasicAttack,
    PreventPass,
    PreventItem,
    PreventDefend,
    PreventFlee,
    PreventTargeting,
    ForceAction,
    RedirectTarget,
    Unknown
}

public enum StatusLifecycleState
{
    Created,
    Validating,
    Active,
    TriggerPending,
    Triggering,
    ExpirationPending,
    Expired,
    RemovalPending,
    Removed,
    RecoveryRequired,
    Faulted
}

public enum StatusApplicationOperation
{
    AddNew,
    RejectDuplicate,
    RefreshDuration,
    AddStack,
    ReplaceExisting,
    IgnoreWeaker,
    EvidenceBlocked
}

public enum StatusRemovalReason
{
    Expired,
    Dispelled,
    Replaced,
    BattleCompleted,
    SourceRemoved,
    TargetDefeated,
    Scripted,
    RecoveryCleanup,
    AdministrativeTest,
    Unknown
}

public enum StatusRequestSource
{
    SkillEffect,
    RoundTrigger,
    BattleRuntime,
    TrustedInternalTest,
    OfficialClient,
    Recovery,
    AdministrativeTest,
    Unknown
}

public enum StatusResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    BattleNotFound,
    InvalidSession,
    OwnershipMismatch,
    ParticipantNotFound,
    ParticipantDead,
    StatusNotFound,
    StatusDisabled,
    StatusEvidenceBlocked,
    InvalidSource,
    InvalidTarget,
    InvalidRound,
    InvalidPhase,
    StackLimitReached,
    DuplicateStatusRejected,
    DurationEvidenceBlocked,
    StackPolicyEvidenceBlocked,
    TriggerEvidenceBlocked,
    ModifierEvidenceBlocked,
    ActionRestricted,
    DispelEvidenceBlocked,
    ReplayConflict,
    VersionConflict,
    PersistenceFailure,
    TriggerExecutionFailure,
    CombatResolutionFailure,
    HealthMutationFailure,
    RecoveryRequired,
    InternalFailure
}

public enum StatusTriggerExecutionState
{
    Pending,
    Executing,
    Committed,
    Skipped,
    RecoveryRequired,
    Failed
}

public enum StatusRecoveryState
{
    NotRequired,
    Pending,
    Recovering,
    Recovered,
    RecoveryRequired,
    Failed
}

public enum StatusMutationState
{
    Planned,
    PersistenceCommitted,
    RuntimeCommitted,
    Completed,
    RecoveryRequired,
    Failed
}

public enum StatusEventKind
{
    StatusApplicationRequested,
    StatusApplicationRejected,
    StatusDefinitionResolved,
    StatusApplied,
    StatusStackAdded,
    StatusDurationRefreshed,
    StatusReplaced,
    StatusRemovalRequested,
    StatusRemoved,
    StatusExpired,
    StatusTriggerPlanCreated,
    StatusTriggerStarted,
    StatusTriggerCompleted,
    StatusTriggerSkipped,
    StatusPeriodicDamageApplied,
    StatusPeriodicHealingApplied,
    StatusModifierSnapshotCreated,
    BattleActionRestricted,
    StatusRecoveryStarted,
    StatusRecoveryCompleted,
    StatusRecoveryRequired,
    BattleStatusCleanupStarted,
    BattleStatusCleanupCompleted
}

public enum StatusFailurePoint
{
    None,
    DefinitionMissing,
    DefinitionDisabled,
    EvidenceBlocked,
    InvalidSource,
    InvalidTarget,
    TargetDead,
    WrongBattle,
    WrongRound,
    WrongPhase,
    ApplicationPersistence,
    RuntimeAdd,
    StackPersistence,
    DurationRefresh,
    RemovalPersistence,
    TriggerPlanPersistence,
    TriggerOrdering,
    TriggerResultPersistence,
    PeriodicDamage,
    PeriodicHealing,
    ModifierOverflow,
    ActionRestriction,
    TriggerCursor,
    BattleCompletionCleanup,
    Audit,
    Inspector,
    Recovery,
    MissingPersistedDefinition
}

public sealed class StatusFailureInjection
{
    public StatusFailurePoint Point { get; set; }
}

public sealed record StatusStackDefinition(
    StatusStackPolicyType PolicyType,
    int? MaximumStacks,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusDurationDefinition(
    StatusDurationPolicyType PolicyType,
    int? DurationRounds,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusTriggerDefinition(
    string TriggerDefinitionId,
    int StatusDefinitionId,
    int TriggerIndex,
    StatusTriggerPhase TriggerPhase,
    int? PriorityCandidate,
    StatusTriggerEffectType EffectType,
    string ValuePolicy,
    long? BaseValueCandidate,
    string TargetPolicy,
    int? MaximumTriggersCandidate,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusModifierDefinition(
    string ModifierDefinitionId,
    int StatusDefinitionId,
    int ModifierIndex,
    StatusModifierType ModifierType,
    long FlatValue,
    int MultiplierBasisPoints,
    bool ApplyPerStack,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusActionRestrictionDefinition(
    string RestrictionDefinitionId,
    int RestrictionIndex,
    StatusActionRestrictionType RestrictionType,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusDispelDefinition(
    bool ExactInstanceRemovalAllowed,
    bool ExactDefinitionRemovalAllowed,
    string? CategoryCandidate,
    bool? ProtectedCandidate,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record StatusDefinitionRecord(
    int StatusDefinitionId,
    string ExternalStatusId,
    string RawName,
    string RawDescription,
    StatusCategory StatusCategory,
    StatusPolarity StatusPolarity,
    StatusApplicationPolicyType ApplicationPolicy,
    StatusStackDefinition StackPolicy,
    StatusDurationDefinition DurationPolicy,
    IReadOnlyList<StatusTriggerDefinition> TriggerDefinitions,
    IReadOnlyList<StatusModifierDefinition> ModifierDefinitions,
    IReadOnlyList<StatusActionRestrictionDefinition> ActionRestrictionDefinitions,
    StatusDispelDefinition DispelDefinition,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus,
    string RawMetadata);

public sealed record StatusDefinition(
    int StatusDefinitionId,
    string ExternalStatusId,
    string Name,
    string Description,
    StatusCategory StatusCategory,
    StatusPolarity StatusPolarity,
    StatusApplicationPolicyType ApplicationPolicy,
    StatusStackDefinition StackPolicy,
    StatusDurationDefinition DurationPolicy,
    IReadOnlyList<StatusTriggerDefinition> TriggerDefinitions,
    IReadOnlyList<StatusModifierDefinition> ModifierDefinitions,
    IReadOnlyList<StatusActionRestrictionDefinition> ActionRestrictionDefinitions,
    StatusDispelDefinition DispelDefinition,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus,
    string RawMetadata);

public sealed record StatusTriggerCursor(
    int CurrentTriggerIndex,
    int CurrentExecutionOrder,
    Guid? LastCommittedTriggerExecutionId,
    StatusRecoveryState RecoveryState,
    long RuntimeVersion);

public sealed record StatusInstance(
    Guid StatusInstanceId,
    int StatusDefinitionId,
    Guid BattleInstanceId,
    Guid TargetParticipantId,
    Guid SourceParticipantId,
    Guid SourceActionId,
    Guid? SourceSkillExecutionId,
    Guid? SourceEffectExecutionId,
    int StackCount,
    int MaximumStacks,
    int AppliedRound,
    int LastRefreshedRound,
    int? ExpiresAfterRound,
    int? RemainingRounds,
    StatusLifecycleState LifecycleState,
    StatusTriggerCursor TriggerCursor,
    long RuntimeVersion,
    string DefinitionContentVersion,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset AppliedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RemovedAtUtc,
    StatusRemovalReason? RemovalReason,
    string CorrelationId,
    string RawMetadata)
{
    public bool IsActive => LifecycleState is
        StatusLifecycleState.Active or
        StatusLifecycleState.TriggerPending or
        StatusLifecycleState.Triggering or
        StatusLifecycleState.ExpirationPending;
}

public sealed record BattleStatusSnapshot(
    Guid BattleInstanceId,
    Guid ParticipantId,
    long StatusVersion,
    IReadOnlyList<StatusInstance> Statuses,
    DateTimeOffset CapturedAtUtc);

public sealed record StatusApplicationRequest(
    Guid StatusApplicationId,
    string IdempotencyKey,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceActionId,
    Guid? SourceSkillExecutionId,
    Guid? SourceEffectExecutionId,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    int StatusDefinitionId,
    int? RequestedStackCandidate,
    int? RequestedDurationCandidate,
    long ExpectedBattleVersion,
    long ExpectedTargetVersion,
    StatusRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusRemovalRequest(
    Guid StatusRemovalId,
    string IdempotencyKey,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceActionId,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    Guid? StatusInstanceId,
    int? StatusDefinitionId,
    StatusRemovalReason RemovalReason,
    long ExpectedTargetVersion,
    StatusRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusStackDecision(
    StatusApplicationOperation Operation,
    int StackBefore,
    int StackAfter,
    StatusResultCode ResultCode,
    string FailureCode,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusDurationDecision(
    int? DurationBefore,
    int? DurationAfter,
    int? ExpiresAfterRound,
    int? RemainingRounds,
    StatusResultCode ResultCode,
    string FailureCode,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusApplicationPlan(
    Guid StatusApplicationPlanId,
    Guid StatusApplicationId,
    Guid StatusInstanceId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    int StatusDefinitionId,
    Guid? ExistingStatusInstanceId,
    StatusApplicationOperation Operation,
    int StackBefore,
    int StackAfter,
    int? DurationBefore,
    int? DurationAfter,
    long ExpectedTargetVersion,
    long ExpectedStatusVersion,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusRemovalPlan(
    Guid StatusRemovalPlanId,
    Guid StatusRemovalId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    Guid StatusInstanceId,
    int StatusDefinitionId,
    StatusRemovalReason RemovalReason,
    long ExpectedTargetVersion,
    long ExpectedStatusVersion,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusApplicationResult(
    Guid StatusApplicationId,
    Guid? StatusApplicationPlanId,
    Guid? StatusInstanceId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid TargetParticipantId,
    int StatusDefinitionId,
    StatusResultCode ResultCode,
    string FailureCode,
    bool IsDuplicate,
    StatusApplicationOperation Operation,
    int StackBefore,
    int StackAfter,
    int? DurationBefore,
    int? DurationAfter,
    long TargetVersionBefore,
    long TargetVersionAfter,
    long StatusVersionBefore,
    long StatusVersionAfter,
    StatusRecoveryState RecoveryState,
    byte[] NetworkBytes,
    DateTimeOffset CompletedAtUtc)
{
    public bool Succeeded => ResultCode is StatusResultCode.Success or StatusResultCode.DuplicateCompleted;
}

public sealed record StatusRemovalResult(
    Guid StatusRemovalId,
    Guid? StatusRemovalPlanId,
    Guid? StatusInstanceId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid TargetParticipantId,
    int? StatusDefinitionId,
    StatusResultCode ResultCode,
    string FailureCode,
    bool IsDuplicate,
    StatusRemovalReason RemovalReason,
    long TargetVersionBefore,
    long TargetVersionAfter,
    long StatusVersionBefore,
    long StatusVersionAfter,
    StatusRecoveryState RecoveryState,
    byte[] NetworkBytes,
    DateTimeOffset CompletedAtUtc)
{
    public bool Succeeded => ResultCode is StatusResultCode.Success or StatusResultCode.DuplicateCompleted;
}

public sealed record StatusApplicationRecord(
    StatusApplicationRequest Request,
    StatusApplicationPlan Plan,
    StatusInstance ProposedInstance,
    string PayloadHash,
    StatusMutationState State,
    StatusApplicationResult? Result,
    StatusRecoveryState RecoveryState,
    DateTimeOffset UpdatedAtUtc);

public sealed record StatusRemovalRecord(
    StatusRemovalRequest Request,
    StatusRemovalPlan Plan,
    string PayloadHash,
    StatusMutationState State,
    StatusRemovalResult? Result,
    StatusRecoveryState RecoveryState,
    DateTimeOffset UpdatedAtUtc);

public sealed record StatusTriggerValuePlan(
    string ValuePolicy,
    long BaseValue,
    int StackSnapshot,
    long FinalValue,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusTriggerExecution(
    Guid TriggerExecutionId,
    Guid StatusInstanceId,
    int StatusDefinitionId,
    string TriggerDefinitionId,
    int TriggerIndex,
    int StatusStackSnapshot,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    StatusTriggerEffectType EffectType,
    StatusTriggerValuePlan ValuePlan,
    int ExecutionOrder,
    long ExpectedStatusVersion,
    long ExpectedTargetVersion,
    string IdempotencyKey,
    StatusTriggerExecutionState State,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusTriggerExecutionPlan(
    Guid StatusTriggerExecutionPlanId,
    Guid BattleInstanceId,
    int RoundNumber,
    StatusTriggerPhase TriggerPhase,
    Guid? SourceActionId,
    IReadOnlyList<StatusTriggerExecution> TriggerExecutions,
    long ExpectedBattleVersion,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusTriggerExecutionResult(
    Guid TriggerExecutionId,
    Guid StatusTriggerExecutionPlanId,
    Guid StatusInstanceId,
    Guid BattleInstanceId,
    int RoundNumber,
    StatusTriggerPhase TriggerPhase,
    int TriggerIndex,
    int ExecutionOrder,
    StatusTriggerExecutionState State,
    StatusResultCode ResultCode,
    string FailureCode,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    bool IsDuplicate,
    StatusRecoveryState RecoveryState,
    DateTimeOffset CompletedAtUtc);

public sealed record StatusTriggerPlanRecord(
    StatusTriggerExecutionPlan Plan,
    int ResolutionCursor,
    IReadOnlyList<StatusTriggerExecutionResult> Results,
    StatusMutationState State,
    StatusRecoveryState RecoveryState,
    DateTimeOffset UpdatedAtUtc);

public sealed record StatusTriggerResolutionResult(
    Guid StatusTriggerExecutionPlanId,
    Guid BattleInstanceId,
    int RoundNumber,
    StatusTriggerPhase TriggerPhase,
    StatusResultCode ResultCode,
    string FailureCode,
    int ResolutionCursor,
    IReadOnlyList<StatusTriggerExecutionResult> Results,
    StatusRecoveryState RecoveryState,
    byte[] NetworkBytes,
    DateTimeOffset CompletedAtUtc)
{
    public bool Succeeded => ResultCode is StatusResultCode.Success or StatusResultCode.DuplicateCompleted;
}

public sealed record StatusModifierContribution(
    Guid StatusInstanceId,
    int StatusDefinitionId,
    string ModifierDefinitionId,
    int ModifierIndex,
    StatusModifierType ModifierType,
    long FlatValue,
    int MultiplierBasisPoints,
    int StackSnapshot,
    int ExecutionOrder,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusModifierSnapshot(
    Guid ModifierSnapshotId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    IReadOnlyList<Guid> SourceStatusInstances,
    IReadOnlyList<Guid> TargetStatusInstances,
    IReadOnlyList<StatusModifierContribution> OrderedModifiers,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    long BaseValue,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusModifierResult(
    Guid ModifierSnapshotId,
    long BaseValue,
    long OutgoingFlat,
    int OutgoingMultiplierBasisPoints,
    long IncomingFlat,
    int IncomingMultiplierBasisPoints,
    long FinalValue,
    StatusResultCode ResultCode,
    string FailureCode,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusDamageModifierRequest(
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    long BaseDamageCandidate,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusHealingModifierRequest(
    Guid BattleInstanceId,
    int RoundNumber,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    long BaseHealingCandidate,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusActionRestrictionResult(
    Guid BattleActionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    BattleActionType ActionType,
    bool Allowed,
    StatusActionRestrictionType? RestrictionType,
    Guid? StatusInstanceId,
    StatusResultCode ResultCode,
    string FailureCode,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusDispelCandidate(
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    StatusPolarity? Polarity,
    StatusCategory? Category,
    int? MaximumRemovalCountCandidate,
    int? PriorityCandidate,
    bool? CanRemoveProtected,
    CombatPolicyStatus PolicyStatus);

public sealed record StatusEvent(
    Guid EventId,
    StatusEventKind Kind,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid? StatusApplicationId,
    Guid? StatusRemovalId,
    Guid? StatusTriggerExecutionPlanId,
    Guid? TriggerExecutionId,
    Guid? StatusInstanceId,
    int? StatusDefinitionId,
    Guid? SourceParticipantId,
    Guid? TargetParticipantId,
    string State,
    string FailureCode,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record StatusAuditRecord(
    Guid AuditId,
    Guid? StatusApplicationId,
    Guid? StatusRemovalId,
    Guid? StatusTriggerExecutionPlanId,
    Guid? TriggerExecutionId,
    Guid? StatusInstanceId,
    int? StatusDefinitionId,
    Guid BattleInstanceId,
    int RoundNumber,
    StatusTriggerPhase? TriggerPhase,
    int? TriggerIndex,
    int? ExecutionOrder,
    string IdempotencySafeId,
    string CorrelationId,
    Guid? SourceParticipantId,
    Guid? TargetParticipantId,
    Guid? SourceActionId,
    Guid? SourceSkillExecutionId,
    Guid? SourceEffectExecutionId,
    string Operation,
    int? StackBefore,
    int? StackAfter,
    int? DurationBefore,
    int? DurationAfter,
    long? RuntimeVersionBefore,
    long? RuntimeVersionAfter,
    Guid? ModifierSnapshotId,
    long? HpBefore,
    long? Damage,
    long? Heal,
    long? HpAfter,
    StatusResultCode Result,
    string FailureCode,
    StatusRemovalReason? RemovalReason,
    StatusRecoveryState RecoveryState,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record StatusInspectorQuery(
    Guid? BattleInstanceId = null,
    int? RoundNumber = null,
    Guid? ParticipantId = null,
    Guid? SourceParticipantId = null,
    Guid? TargetParticipantId = null,
    int? StatusDefinitionId = null,
    Guid? StatusInstanceId = null,
    StatusCategory? Category = null,
    StatusPolarity? Polarity = null,
    StatusLifecycleState? LifecycleState = null,
    StatusTriggerPhase? TriggerPhase = null,
    StatusModifierType? ModifierType = null,
    bool ActiveOnly = false,
    bool ExpiredOnly = false,
    bool RemovedOnly = false,
    bool FailedOnly = false,
    bool PendingOnly = false,
    bool RecoveryRequiredOnly = false,
    bool EvidenceBlockedOnly = false,
    bool TestOnlyOnly = false,
    bool DuplicateOnly = false,
    int Offset = 0,
    int Limit = 100);

public sealed record StatusDefinitionInspectorItem(
    int StatusDefinitionId,
    string Name,
    StatusCategory Category,
    StatusPolarity Polarity,
    StatusStackPolicyType StackPolicy,
    StatusDurationPolicyType DurationPolicy,
    int TriggerCount,
    int ModifierCount,
    int RestrictionCount,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus);

public sealed record StatusInstanceInspectorItem(
    Guid StatusInstanceId,
    Guid BattleInstanceId,
    Guid TargetParticipantId,
    Guid SourceParticipantId,
    int StatusDefinitionId,
    StatusLifecycleState LifecycleState,
    int StackCount,
    int MaximumStacks,
    int AppliedRound,
    int LastRefreshedRound,
    int? ExpiresAfterRound,
    int? RemainingRounds,
    long RuntimeVersion,
    StatusTriggerCursor TriggerCursor,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset AppliedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RemovedAtUtc,
    StatusRemovalReason? RemovalReason);

public sealed record StatusTriggerInspectorItem(
    Guid TriggerExecutionId,
    Guid StatusInstanceId,
    Guid BattleInstanceId,
    int RoundNumber,
    StatusTriggerPhase TriggerPhase,
    int TriggerIndex,
    int ExecutionOrder,
    StatusTriggerExecutionState State,
    StatusResultCode Result,
    string FailureCode,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    StatusRecoveryState RecoveryState);

public sealed record StatusModifierInspectorItem(
    Guid ModifierSnapshotId,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    IReadOnlyList<StatusModifierType> ModifierTypes,
    IReadOnlyList<Guid> StatusInstanceIds,
    long BaseValue,
    long FinalValue,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusRestrictionInspectorItem(
    Guid BattleActionId,
    Guid ParticipantId,
    BattleActionType ActionType,
    StatusActionRestrictionType? RestrictionType,
    Guid? StatusInstanceId,
    StatusResultCode Result,
    string FailureCode,
    DateTimeOffset CreatedAtUtc);

public sealed record StatusInspectorSnapshot(
    IReadOnlyList<StatusDefinitionInspectorItem> Definitions,
    IReadOnlyList<StatusInstanceInspectorItem> Instances,
    IReadOnlyList<StatusTriggerInspectorItem> Triggers,
    IReadOnlyList<StatusModifierInspectorItem> Modifiers,
    IReadOnlyList<StatusRestrictionInspectorItem> Restrictions,
    IReadOnlyList<StatusAuditRecord> Audit,
    int Offset,
    int Limit,
    string FailureCode,
    DateTimeOffset CapturedAtUtc);

public interface IStatusDefinitionCatalog
{
    IReadOnlyList<StatusDefinition> Snapshot { get; }

    OperationResult<StatusDefinition> Get(int statusDefinitionId, StatusRequestSource source);
}

public interface IStatusDefinitionRepository
{
    Task<IReadOnlyList<StatusDefinitionRecord>> LoadAsync(CancellationToken cancellationToken);
}

public interface IStatusDefinitionMapper
{
    OperationResult<StatusDefinition> Map(StatusDefinitionRecord record);
}

public interface IStatusDefinitionValidator
{
    OperationResult Validate(StatusDefinition definition);
}

public interface IStatusEffectCoordinator
{
    Task<StatusApplicationResult> ApplyAsync(
        BattleInstance battle,
        StatusApplicationRequest request,
        CancellationToken cancellationToken);

    Task<StatusRemovalResult> RemoveAsync(
        BattleInstance battle,
        StatusRemovalRequest request,
        CancellationToken cancellationToken);

    Task<StatusRemovalResult> CleanupAsync(
        BattleInstance battle,
        StatusInstance status,
        StatusRemovalReason reason,
        CancellationToken cancellationToken);
}

public interface IStatusApplicationPlanner
{
    OperationResult<(StatusApplicationPlan Plan, StatusInstance ProposedInstance)> Create(
        StatusApplicationRequest request,
        StatusDefinition definition,
        StatusInstance? existing,
        StatusStackDecision stack,
        StatusDurationDecision duration,
        DateTimeOffset now);
}

public interface IStatusApplicationStore
{
    StatusApplicationRecord? GetApplication(string idempotencySafeId);

    StatusRemovalRecord? GetRemoval(string idempotencySafeId);

    OperationResult SaveApplication(StatusApplicationRecord record, StatusMutationState expectedState);

    OperationResult SaveRemoval(StatusRemovalRecord record, StatusMutationState expectedState);

    IReadOnlyList<StatusApplicationRecord> Applications { get; }

    IReadOnlyList<StatusRemovalRecord> Removals { get; }
}

public interface IStatusInstanceStore
{
    StatusInstance? GetStatus(Guid statusInstanceId);

    IReadOnlyList<StatusInstance> LoadBattle(Guid battleInstanceId);
}

public interface IStatusStackPolicy
{
    StatusStackDecision Evaluate(StatusDefinition definition, StatusInstance? existing);
}

public interface IStatusDurationPolicy
{
    StatusDurationDecision Evaluate(
        StatusDefinition definition,
        StatusInstance? existing,
        int roundNumber);

    StatusDurationDecision CloseRound(StatusDefinition definition, StatusInstance instance, int roundNumber);
}

public interface IStatusTriggerCoordinator
{
    Task<StatusTriggerResolutionResult> ResolveAsync(
        BattleInstance battle,
        StatusTriggerPhase phase,
        Guid? sourceActionId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<StatusTriggerResolutionResult> RecoverAsync(
        Guid statusTriggerExecutionPlanId,
        BattleInstance battle,
        CancellationToken cancellationToken);
}

public interface IStatusTriggerPolicy
{
    OperationResult Validate(StatusDefinition definition, StatusTriggerDefinition trigger);
}

public interface IStatusTriggerPlanner
{
    OperationResult<StatusTriggerExecutionPlan> Create(
        BattleInstance battle,
        StatusTriggerPhase phase,
        Guid? sourceActionId,
        IEnumerable<(StatusInstance Instance, StatusDefinition Definition)> eligible,
        string correlationId,
        DateTimeOffset now);
}

public interface IStatusTriggerExecutionStore
{
    StatusTriggerPlanRecord? GetTriggerPlan(Guid planId);

    StatusTriggerExecutionResult? GetTriggerResult(Guid triggerExecutionId);

    OperationResult SaveTriggerPlan(StatusTriggerPlanRecord record, int expectedCursor);

    IReadOnlyList<StatusTriggerPlanRecord> TriggerPlans { get; }
}

public interface IStatusModifierProvider
{
    StatusModifierSnapshot CreateSnapshot(
        Guid battleInstanceId,
        int roundNumber,
        Guid sourceParticipantId,
        Guid targetParticipantId,
        long baseValue,
        DateTimeOffset now);
}

public interface IStatusModifierAggregator
{
    StatusModifierResult AggregateDamage(StatusModifierSnapshot snapshot);

    StatusModifierResult AggregateHealing(StatusModifierSnapshot snapshot);
}

public interface IStatusActionRestrictionPolicy
{
    StatusActionRestrictionResult Evaluate(
        BattleInstance battle,
        BattleLockedAction action,
        DateTimeOffset now);
}

public interface IStatusDispelPolicy
{
    OperationResult<IReadOnlyList<StatusInstance>> Select(
        BattleStatusSnapshot snapshot,
        StatusDispelCandidate candidate,
        Guid? exactStatusInstanceId,
        int? exactStatusDefinitionId);
}

public interface IStatusRecoveryCoordinator
{
    Task<StatusApplicationResult> RecoverApplicationAsync(
        Guid statusApplicationId,
        BattleInstance battle,
        CancellationToken cancellationToken);

    Task<StatusRemovalResult> RecoverRemovalAsync(
        Guid statusRemovalId,
        BattleInstance battle,
        CancellationToken cancellationToken);
}

public interface IStatusEventSink
{
    void Publish(StatusEvent runtimeEvent);

    IReadOnlyList<StatusEvent> Snapshot { get; }
}

public interface IStatusAuditLedger
{
    void Append(StatusAuditRecord record);

    IReadOnlyList<StatusAuditRecord> Snapshot { get; }
}

public interface IStatusInspectorSource
{
    IReadOnlyList<StatusInstance> StatusInstances { get; }

    IReadOnlyList<StatusTriggerPlanRecord> StatusTriggerPlans { get; }

    IReadOnlyList<StatusModifierInspectorItem> StatusModifierSnapshots { get; }

    IReadOnlyList<StatusRestrictionInspectorItem> StatusRestrictionResults { get; }
}

public interface IBattleStatusAuthority
{
    BattleStatusSnapshot GetParticipantSnapshot(
        Guid battleInstanceId,
        Guid participantId,
        DateTimeOffset capturedAtUtc);

    StatusInstance? GetStatusInstance(Guid statusInstanceId);

    OperationResult<StatusInstance> AddStatusInstance(
        StatusInstance instance,
        long expectedParticipantStatusVersion,
        string idempotencySafeId);

    OperationResult<StatusInstance> UpdateStatusInstance(
        StatusInstance instance,
        long expectedStatusVersion,
        long expectedParticipantStatusVersion,
        string idempotencySafeId);

    OperationResult<StatusInstance> RemoveStatusInstance(
        Guid statusInstanceId,
        long expectedStatusVersion,
        long expectedParticipantStatusVersion,
        StatusRemovalReason reason,
        DateTimeOffset now,
        string idempotencySafeId);

    OperationResult Reload(Guid battleInstanceId, IEnumerable<StatusInstance> statuses);
}

public interface IBattleDamageModifierPort
{
    StatusModifierResult ApplyDamageModifiers(StatusDamageModifierRequest request);
}

public interface IBattleHealingModifierPort
{
    StatusModifierResult ApplyHealingModifiers(StatusHealingModifierRequest request);
}

public interface IBattleActionRestrictionPort
{
    StatusActionRestrictionResult Evaluate(
        BattleInstance battle,
        BattleLockedAction action,
        DateTimeOffset now);
}

internal static class StatusRuntimeCollections
{
    public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}
