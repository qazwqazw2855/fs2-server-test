using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum SkillCategory
{
    Active,
    Passive,
    Basic,
    Offensive,
    Defensive,
    Healing,
    Support,
    ItemGranted,
    Scripted,
    Unknown
}

public enum SkillActionCategory
{
    Damage,
    Heal,
    Mixed,
    Status,
    Summon,
    Revive,
    Utility,
    Unknown
}

public enum SkillTargetPolicyType
{
    Self,
    SingleEnemy,
    SingleAlly,
    SingleAny,
    AllEnemies,
    AllAllies,
    AllParticipants,
    RandomEnemy,
    RandomAlly,
    DeadAlly,
    FormationRow,
    FormationColumn,
    Adjacent,
    Scripted,
    Unknown
}

public enum SkillResourceType
{
    None,
    BattleResource,
    Mana,
    Energy,
    Hp,
    InventoryItem,
    Currency,
    Charge,
    Unknown
}

public enum SkillEffectType
{
    Damage,
    Heal,
    ApplyStatus,
    RemoveStatus,
    ResourceRestore,
    ResourceDrain,
    Revive,
    Summon,
    Dispel,
    Scripted,
    NoOp,
    Unknown
}

public enum SkillExecutionState
{
    Created,
    Validating,
    TargetsResolved,
    CostValidated,
    CostReserved,
    Planned,
    Executing,
    EffectsCompleted,
    CostCommitted,
    CooldownCommitted,
    Completed,
    RecoveryRequired,
    Rejected,
    Faulted
}

public enum SkillEffectExecutionState
{
    Pending,
    Executing,
    Committed,
    Skipped,
    Rejected,
    RecoveryRequired,
    Faulted
}

public enum SkillCostReservationState
{
    Created,
    Validated,
    Reserved,
    Committed,
    Released,
    RecoveryRequired,
    Faulted
}

public enum SkillRecoveryState
{
    NotRequired,
    Pending,
    Recovering,
    Recovered,
    RecoveryRequired,
    Failed
}

public enum SkillResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    BattleNotFound,
    InvalidSession,
    OwnershipMismatch,
    ParticipantNotFound,
    ParticipantDead,
    InvalidRound,
    InvalidPhase,
    SkillNotFound,
    SkillDisabled,
    SkillNotOwned,
    SkillRankInvalid,
    SkillEvidenceBlocked,
    InvalidTarget,
    NoValidTarget,
    TargetDead,
    TargetPolicyBlocked,
    InsufficientResource,
    CostEvidenceBlocked,
    CostReservationConflict,
    CooldownActive,
    CooldownEvidenceBlocked,
    UsageLimitReached,
    UnsupportedEffect,
    EffectEvidenceBlocked,
    ReplayConflict,
    VersionConflict,
    CombatResolutionFailure,
    HealthMutationFailure,
    PersistenceFailure,
    RecoveryRequired,
    InternalFailure
}

public enum SkillEventKind
{
    SkillActionReceived,
    SkillActionRejected,
    SkillDefinitionResolved,
    SkillTargetsResolved,
    SkillCostValidated,
    SkillCostReserved,
    SkillExecutionPlanned,
    SkillExecutionStarted,
    SkillEffectResolving,
    SkillDamageApplied,
    SkillHealingApplied,
    SkillEffectSkipped,
    SkillEffectCommitted,
    SkillEffectsCompleted,
    SkillCostCommitted,
    SkillCostReleased,
    SkillCooldownCommitted,
    SkillExecutionCompleted,
    SkillRecoveryStarted,
    SkillRecoveryCompleted,
    SkillRecoveryRequired
}

public enum SkillFailurePoint
{
    None,
    Availability,
    TargetResolution,
    CostReservation,
    PlanPersistence,
    DamageEffect,
    HealingEffect,
    EffectPersistence,
    AfterFirstEffect,
    CostCommit,
    CooldownCommit,
    ResultPersistence,
    Audit,
    Inspector,
    Recovery
}

public sealed class SkillFailureInjection
{
    public SkillFailurePoint Point { get; set; }
}

public sealed record SkillCostDefinition(
    SkillResourceType ResourceType,
    long Amount,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record SkillCooldownDefinition(
    int? CooldownRoundsCandidate,
    string? SharedCooldownGroupCandidate,
    int? InitialCooldownCandidate,
    int? UsageLimitCandidate,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record SkillUsageDefinition(
    int? UsageLimitCandidate,
    CombatPolicyStatus PolicyStatus,
    string RawMetadata);

public sealed record SkillEffectDefinition(
    string EffectDefinitionId,
    int SkillDefinitionId,
    int EffectIndex,
    SkillEffectType EffectType,
    SkillTargetPolicyType TargetScope,
    string ValuePolicy,
    long? BaseValueCandidate,
    decimal? ScalingCandidate,
    CombatDamageType DamageType,
    bool CanTargetDead,
    bool CanTargetSelf,
    bool CanTargetAlly,
    bool CanTargetEnemy,
    int? MaximumTargetCountCandidate,
    CombatPolicyStatus PolicyStatus,
    string ContentVersion,
    string RawMetadata);

public sealed record SkillDefinitionRecord(
    int SkillDefinitionId,
    string ExternalSkillId,
    string RawName,
    string RawDescription,
    int? MaxRankCandidate,
    int? RequiredLevelCandidate,
    SkillCategory SkillCategoryCandidate,
    SkillActionCategory ActionCategoryCandidate,
    SkillTargetPolicyType TargetPolicyCandidate,
    SkillCostDefinition CostDefinition,
    SkillCooldownDefinition CooldownDefinition,
    SkillUsageDefinition UsageDefinition,
    IReadOnlyList<SkillEffectDefinition> EffectDefinitions,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus,
    string RawMetadata);

public sealed record SkillDefinition(
    int SkillDefinitionId,
    string ExternalSkillId,
    string Name,
    string Description,
    SkillCategory SkillCategory,
    SkillActionCategory ActionCategory,
    SkillTargetPolicyType TargetPolicy,
    SkillCostDefinition CostDefinition,
    SkillCooldownDefinition CooldownDefinition,
    SkillUsageDefinition UsageDefinition,
    IReadOnlyList<SkillEffectDefinition> EffectDefinitions,
    int? MaximumRank,
    int? RequiredLevel,
    bool Enabled,
    string ContentVersion,
    CombatPolicyStatus PolicyStatus,
    CombatPolicyStatus ProtocolStatus,
    string RawMetadata);

public sealed record SkillOwnershipRecord(
    long CharacterId,
    int SkillDefinitionId,
    int Rank,
    bool Enabled,
    CombatPolicyStatus PolicyStatus,
    string ContentVersion,
    string RawMetadata);

public sealed record SkillActionRequest(
    Guid SkillActionId,
    string IdempotencyKey,
    Guid BattleInstanceId,
    long BattleVersion,
    int RoundNumber,
    Guid BattleActionId,
    string SessionId,
    long CharacterId,
    Guid ParticipantId,
    int SkillDefinitionId,
    int? SkillRankCandidate,
    IReadOnlyList<Guid> RequestedTargetParticipantIds,
    long? ClientSequenceCandidate,
    BattleRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record SkillParticipantSnapshot(
    Guid ParticipantId,
    BattleParticipantType ParticipantType,
    BattleSide Side,
    int FormationSlot,
    long? CharacterId,
    int? MonsterTemplateId,
    long MaximumHp,
    long CurrentHp,
    bool IsAlive,
    long RuntimeVersion);

public sealed record SkillTargetResolution(
    IReadOnlyList<SkillParticipantSnapshot> Targets,
    SkillTargetPolicyType PolicyType,
    CombatPolicyStatus PolicyStatus,
    string FailureCode);

public sealed record SkillCostReservation(
    Guid ReservationId,
    Guid SkillExecutionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    int SkillDefinitionId,
    SkillResourceType ResourceType,
    long Amount,
    long RuntimeVersionBefore,
    SkillCostReservationState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CommittedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    string IdempotencySafeId,
    string CorrelationId);

public sealed record SkillUsageState(
    Guid ParticipantId,
    Guid BattleInstanceId,
    int SkillDefinitionId,
    int? LastUsedRound,
    int AvailableAtRound,
    int RemainingRounds,
    int UsageCount,
    long RuntimeVersion,
    string IdempotencySafeId,
    CombatPolicyStatus PolicyStatus);

public sealed record SkillValuePlan(
    string ValuePolicy,
    long BaseValue,
    decimal Scaling,
    CombatPolicyStatus PolicyStatus);

public sealed record SkillEffectPlan(
    Guid EffectExecutionId,
    string EffectDefinitionId,
    int EffectIndex,
    Guid TargetParticipantId,
    int TargetIndex,
    SkillEffectType EffectType,
    SkillValuePlan ValuePlan,
    long ExpectedTargetVersion,
    string EffectIdempotencyKey,
    SkillEffectExecutionState State,
    CombatPolicyStatus PolicyStatus);

public sealed record SkillExecutionPlan(
    Guid SkillExecutionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid BattleActionId,
    Guid SkillActionId,
    Guid ParticipantId,
    int SkillDefinitionId,
    int SkillRank,
    SkillParticipantSnapshot SourceParticipantSnapshot,
    IReadOnlyList<SkillParticipantSnapshot> TargetParticipantSnapshots,
    SkillCostDefinition CostPlan,
    IReadOnlyList<SkillEffectPlan> EffectPlans,
    int TurnResolutionIndex,
    long ExpectedBattleVersion,
    long ExpectedParticipantVersion,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record SkillEffectResult(
    Guid EffectExecutionId,
    Guid SkillExecutionId,
    string EffectDefinitionId,
    int EffectIndex,
    int TargetIndex,
    Guid SourceParticipantId,
    Guid TargetParticipantId,
    SkillEffectType EffectType,
    SkillEffectExecutionState State,
    long RequestedValue,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    long MaximumHp,
    bool TargetDefeated,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    CombatPolicyStatus PolicyStatus,
    string FailureCode,
    bool IsDuplicate,
    DateTimeOffset CompletedAtUtc);

public sealed record SkillTargetResult(
    Guid TargetParticipantId,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    bool TargetDefeated,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string FailureCode);

public sealed record SkillCostResult(
    Guid? ReservationId,
    SkillResourceType ResourceType,
    long Amount,
    SkillCostReservationState State,
    string FailureCode);

public sealed record SkillCooldownResult(
    int AvailableAtRound,
    int RemainingRounds,
    int UsageCount,
    bool Committed,
    string FailureCode,
    CombatPolicyStatus PolicyStatus);

public sealed record SkillActionResult(
    Guid SkillExecutionId,
    Guid SkillActionId,
    Guid BattleActionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    int SkillDefinitionId,
    SkillResultCode ResultCode,
    string FailureCode,
    bool IsDuplicate,
    SkillCostResult CostResult,
    SkillCooldownResult CooldownResult,
    IReadOnlyList<SkillTargetResult> TargetResults,
    IReadOnlyList<SkillEffectResult> EffectResults,
    long BattleVersionBefore,
    long BattleVersionAfter,
    long ParticipantVersionBefore,
    long ParticipantVersionAfter,
    int ResolutionCursor,
    SkillRecoveryState RecoveryState,
    byte[] NetworkBytes,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public bool Succeeded => ResultCode is SkillResultCode.Success or SkillResultCode.DuplicateCompleted;
}

public sealed record SkillExecutionRecord(
    SkillExecutionPlan Plan,
    SkillExecutionState State,
    int ResolutionCursor,
    SkillCostReservation? CostReservation,
    IReadOnlyList<SkillEffectResult> EffectResults,
    SkillActionResult? Result,
    string PayloadHash,
    SkillRecoveryState RecoveryState,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleHealthMutationRequest(
    Guid BattleInstanceId,
    int RoundNumber,
    Guid BattleActionId,
    Guid EffectExecutionId,
    BattleParticipant Source,
    BattleParticipant Target,
    long HealAmount,
    long ExpectedTargetVersion,
    string StableIdempotencyKey,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleHealthMutationResult(
    SkillResultCode ResultCode,
    long RequestedHeal,
    long EffectiveHeal,
    long HpBefore,
    long HpAfter,
    long MaximumHp,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string FailureCode,
    bool IsDuplicate,
    CombatPolicyStatus PolicyStatus);

public sealed record SkillEvent(
    Guid EventId,
    SkillEventKind Kind,
    Guid SkillExecutionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid? EffectExecutionId,
    Guid ParticipantId,
    Guid? TargetParticipantId,
    int SkillDefinitionId,
    int ResolutionCursor,
    string State,
    string FailureCode,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record SkillAuditRecord(
    Guid AuditId,
    Guid SkillExecutionId,
    Guid SkillActionId,
    Guid BattleActionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid? EffectExecutionId,
    int? EffectIndex,
    int? TargetIndex,
    string IdempotencySafeId,
    string CorrelationId,
    string SessionSafeId,
    long CharacterId,
    Guid ParticipantId,
    Guid? TargetParticipantId,
    int SkillDefinitionId,
    int SkillRank,
    SkillEffectType? EffectType,
    SkillResourceType CostType,
    long CostAmount,
    SkillCostReservationState CostState,
    int AvailableAtRound,
    long? HpBefore,
    long? Damage,
    long? Heal,
    long? HpAfter,
    long? RuntimeVersionBefore,
    long? RuntimeVersionAfter,
    SkillResultCode Result,
    string FailureCode,
    IReadOnlyList<CombatPolicyStatus> PolicyStatuses,
    int ResolutionCursor,
    SkillRecoveryState RecoveryState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public interface ISkillDefinitionRepository
{
    Task<IReadOnlyList<SkillDefinitionRecord>> LoadAsync(CancellationToken cancellationToken);
}

public interface ISkillDefinitionMapper
{
    OperationResult<SkillDefinition> Map(SkillDefinitionRecord record);
}

public interface ISkillDefinitionValidator
{
    OperationResult Validate(SkillDefinition definition);
}

public interface ISkillDefinitionCatalog
{
    IReadOnlyList<SkillDefinition> Snapshot { get; }

    OperationResult<SkillDefinition> Get(int skillDefinitionId, BattleRequestSource source);
}

public interface ISkillActionResolver
{
    Task<SkillActionResult> ResolveAsync(
        BattleInstance battle,
        BattleLockedAction action,
        int resolutionIndex,
        CancellationToken cancellationToken);
}

public interface ISkillAvailabilityPolicy
{
    OperationResult<int> ResolveRank(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition);
}

public interface ISkillTargetPolicy
{
    OperationResult<SkillTargetResolution> Resolve(
        BattleInstance battle,
        BattleParticipant source,
        SkillDefinition definition,
        IReadOnlyList<Guid> requestedTargetParticipantIds);
}

public interface ISkillTargetResolver : ISkillTargetPolicy;

public interface ISkillCostPolicy
{
    OperationResult Validate(
        SkillDefinition definition,
        BattleParticipant source,
        int roundNumber);
}

public interface ISkillCostReservationStore
{
    OperationResult<SkillCostReservation> Reserve(
        SkillExecutionPlan plan,
        DateTimeOffset now);

    OperationResult<SkillCostReservation> Commit(Guid reservationId, DateTimeOffset now);

    OperationResult<SkillCostReservation> Release(Guid reservationId, DateTimeOffset now);

    SkillCostReservation? Get(Guid reservationId);

    void SeedTestResource(Guid participantId, long amount);

    long GetAvailableTestResource(Guid participantId);

    IReadOnlyList<SkillCostReservation> Reservations { get; }
}

public interface ISkillCooldownPolicy
{
    OperationResult<SkillUsageState> Validate(
        Guid battleInstanceId,
        Guid participantId,
        SkillDefinition definition,
        int roundNumber);

    OperationResult<SkillUsageState> Commit(
        SkillExecutionPlan plan,
        SkillDefinition definition);
}

public interface ISkillUsageStore
{
    SkillUsageState? Get(
        Guid battleInstanceId,
        Guid participantId,
        int skillDefinitionId);

    OperationResult<SkillUsageState> Save(
        SkillUsageState state,
        long expectedVersion);

    IReadOnlyList<SkillUsageState> Snapshot { get; }
}

public interface ISkillExecutionPlanner
{
    OperationResult<SkillExecutionPlan> Create(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        int skillRank,
        SkillTargetResolution targets,
        int resolutionIndex,
        DateTimeOffset now);
}

public interface ISkillEffectResolver
{
    Task<SkillEffectResult> ResolveAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken);
}

public interface ISkillEffectHandler
{
    SkillEffectType EffectType { get; }

    Task<SkillEffectResult> ExecuteAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken);
}

public interface ISkillEffectHandlerRegistry
{
    OperationResult<ISkillEffectHandler> Get(SkillEffectType effectType);
}

public interface ISkillExecutionStore
{
    SkillExecutionRecord? Get(Guid skillExecutionId);

    SkillExecutionRecord? FindByIdempotency(string idempotencyKey);

    OperationResult Save(SkillExecutionRecord record, int expectedCursor);

    IReadOnlyList<SkillExecutionRecord> Snapshot { get; }
}

public interface ISkillRecoveryCoordinator
{
    Task<SkillActionResult> RecoverAsync(
        Guid skillExecutionId,
        BattleInstance battle,
        CancellationToken cancellationToken);
}

public interface ISkillEventSink
{
    void Publish(SkillEvent runtimeEvent);

    IReadOnlyList<SkillEvent> Snapshot { get; }
}

public interface ISkillAuditLedger
{
    void Append(SkillAuditRecord record);

    IReadOnlyList<SkillAuditRecord> Snapshot { get; }
}

public interface ISkillInspectorSource
{
    IReadOnlyList<SkillExecutionRecord> Executions { get; }

    IReadOnlyList<SkillCostReservation> CostReservations { get; }

    IReadOnlyList<SkillUsageState> Usage { get; }
}

public interface IBattleHealthMutationPort
{
    Task<BattleHealthMutationResult> HealAsync(
        BattleHealthMutationRequest request,
        CancellationToken cancellationToken);
}

public interface IStatusEffectResolver;

public interface IStatusEffectStore;

public interface IStatusEffectPolicy;

public sealed record SkillEffectContext(
    BattleInstance Battle,
    BattleLockedAction BattleAction,
    SkillDefinition Definition,
    SkillExecutionPlan Plan,
    SkillEffectPlan EffectPlan,
    BattleParticipant Source,
    BattleParticipant Target,
    IBattleCombatExecutionPort Combat,
    IBattleHealthMutationPort Health,
    DateTimeOffset Now);

internal static class SkillCollections
{
    public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}
