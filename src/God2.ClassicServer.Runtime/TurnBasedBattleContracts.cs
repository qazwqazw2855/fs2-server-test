using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum BattleRequestSource
{
    OfficialClient,
    ContentBacked,
    Scripted,
    TrustedInternalTest,
    Recovery
}

public enum BattleEncounterType
{
    Scripted,
    Random,
    NpcTriggered,
    PortalTriggered,
    QuestTriggered,
    InternalTest,
    Unknown
}

public enum BattleType
{
    PlayerVersusEnvironment,
    Scripted,
    InternalTest,
    Unknown
}

public enum BattleState
{
    Created,
    Initializing,
    WaitingForParticipants,
    Active,
    Completing,
    Completed,
    Aborting,
    Aborted,
    RecoveryRequired,
    Faulted,
    Closed
}

public enum BattlePhase
{
    NotStarted,
    RoundOpening,
    CollectingActions,
    ActionsLocked,
    ResolvingActions,
    RoundClosing,
    VictoryResolution,
    RewardFinalization,
    Completed,
    Aborted,
    RecoveryRequired
}

public enum BattleRoundState
{
    Created,
    Opening,
    CollectingActions,
    ReadyToLock,
    Locked,
    Resolving,
    Closing,
    Completed,
    RecoveryRequired,
    Faulted
}

public enum BattleParticipantType
{
    Player,
    Monster,
    Companion,
    Summon,
    Npc,
    Unknown
}

public enum BattleSide
{
    PlayerSide,
    EnemySide,
    Neutral,
    Unknown
}

public enum BattleWinnerSide
{
    None,
    PlayerSide,
    EnemySide,
    Draw
}

public enum BattleParticipantCombatState
{
    Ready,
    WaitingForAction,
    ActionSubmitted,
    ActionLocked,
    Resolving,
    Hit,
    Dead,
    Defeated,
    Disconnected,
    RecoveryRequired
}

public enum BattleActionType
{
    BasicAttack,
    Pass,
    Skill,
    Defend,
    Item,
    Flee,
    SystemAction,
    Unknown
}

public enum BattleActionState
{
    Submitted,
    Locked,
    Resolving,
    Resolved,
    Skipped,
    Rejected,
    RecoveryRequired
}

public enum BattleCompletionReason
{
    None,
    PlayerVictory,
    EnemyVictory,
    Draw,
    AdministrativeAbort,
    RecoveryAbort,
    Faulted
}

public enum BattleRewardState
{
    NotRequired,
    Pending,
    Preparing,
    Committing,
    Committed,
    Failed,
    RecoveryRequired
}

public enum BattleRecoveryState
{
    NotRequired,
    Pending,
    Recovering,
    Recovered,
    RecoveryRequired,
    Failed
}

public enum WorldBattleReservationState
{
    EnteringBattle,
    InBattle,
    LeavingBattle,
    Released,
    RecoveryRequired
}

public enum BattleResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    BattleNotFound,
    BattleAlreadyCompleted,
    InvalidSession,
    OwnershipMismatch,
    ParticipantNotFound,
    ParticipantInactive,
    ParticipantDead,
    InvalidRound,
    InvalidPhase,
    ActionAlreadySubmitted,
    ActionLocked,
    InvalidTarget,
    TargetDead,
    UnsupportedAction,
    TurnOrderEvidenceBlocked,
    BattlePolicyBlocked,
    CooldownActive,
    ReplayConflict,
    VersionConflict,
    CombatResolutionFailure,
    RewardFailure,
    PersistenceFailure,
    RecoveryRequired,
    InternalFailure
}

public enum BattleActionResolutionCode
{
    Success,
    Passed,
    SkippedAttackerDead,
    SkippedTargetDead,
    ActionRestricted,
    InvalidTarget,
    UnsupportedAction,
    VersionConflict,
    CombatFailure,
    RecoveryRequired
}

public enum BattleEventKind
{
    BattleCreationRequested,
    BattleCreated,
    BattleParticipantAdded,
    BattleParticipantRejected,
    BattleStarted,
    RoundOpening,
    RoundStarted,
    BattleActionSubmitted,
    BattleActionRejected,
    BattleActionsLocked,
    BattleTurnOrderResolved,
    BattleActionResolving,
    BattleActionResolved,
    BattleActionSkipped,
    BattleParticipantDamaged,
    BattleParticipantHealed,
    BattleParticipantDefeated,
    RoundClosing,
    RoundCompleted,
    BattleVictoryResolved,
    BattleRewardFinalizationStarted,
    BattleRewardCommitted,
    BattleRewardFailed,
    BattleCompleted,
    BattleAborted,
    BattleParticipantDisconnected,
    BattleParticipantReconnected,
    BattleRecoveryStarted,
    BattleRecoveryCompleted,
    BattleRecoveryRequired
}

public enum BattleFailurePoint
{
    None,
    CreationPersistence,
    EncounterMissing,
    ParticipantFactory,
    DuplicateParticipant,
    FormationConflict,
    WorldReservation,
    ActionLockPersistence,
    TurnOrderPolicy,
    CombatExecution,
    ActionResultPersistence,
    RuntimeStateUpdate,
    RoundClose,
    VictoryResolution,
    RewardTransaction,
    CompletionPersistence,
    ReservationRelease,
    Audit,
    Inspector,
    Recovery
}

public sealed class BattleFailureInjection
{
    public BattleFailurePoint Point { get; set; }
}

public sealed record BattleRequest(
    Guid BattleRequestId,
    string IdempotencyKey,
    string SourceSessionId,
    long SourceCharacterId,
    long SourceRuntimeEntityId,
    string? EncounterDefinitionId,
    IReadOnlyList<int> RequestedOpponentTemplateIds,
    int SourceMapId,
    string SourceWorldInstanceId,
    long ExpectedPlayerRuntimeVersion,
    BattleRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleActionRequest(
    Guid ActionRequestId,
    string IdempotencyKey,
    Guid BattleInstanceId,
    long BattleVersion,
    int RoundNumber,
    string SessionId,
    long CharacterId,
    Guid ParticipantId,
    BattleActionType ActionType,
    IReadOnlyList<Guid> TargetParticipantIds,
    int? SkillDefinitionId,
    string? ItemReference,
    long? ClientSequenceCandidate,
    BattleRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleEncounterParticipantDefinition(
    string ParticipantDefinitionId,
    BattleParticipantType ParticipantType,
    BattleSide Side,
    int FormationSlot,
    int? MonsterTemplateId,
    long? SourceRuntimeEntityId,
    string DisplayName,
    int Level,
    long MaximumHp,
    long AttackPower,
    long Defense,
    long? InitiativeCandidate,
    CombatPolicyStatus StatPolicyStatus,
    CombatPolicyStatus FormationPolicyStatus,
    string RawMetadata);

public sealed record BattleEncounterDefinition(
    string EncounterDefinitionId,
    BattleEncounterType EncounterType,
    int MapId,
    string MonsterGroupId,
    IReadOnlyList<BattleEncounterParticipantDefinition> ParticipantDefinitions,
    string? FormationDefinitionId,
    string? RewardPolicyReference,
    bool Enabled,
    CombatPolicyStatus PolicyStatus,
    string ContentVersion,
    string RawMetadata);

public sealed record BattleParticipant(
    Guid ParticipantId,
    Guid BattleInstanceId,
    BattleParticipantType ParticipantType,
    BattleSide Side,
    int FormationSlot,
    long? CharacterId,
    int? MonsterTemplateId,
    long SourceRuntimeEntityId,
    long BattleRuntimeEntityId,
    string? SessionId,
    string DisplayName,
    int Level,
    long MaximumHp,
    long CurrentHp,
    bool IsAlive,
    bool IsConnected,
    bool IsReady,
    bool HasSubmittedAction,
    BattleActionState? CurrentActionState,
    BattleParticipantCombatState CombatState,
    long RuntimeVersion,
    long AttackPower,
    long Defense,
    long? InitiativeCandidate,
    CombatPolicyStatus StatPolicyStatus,
    CombatPolicyStatus FormationPolicyStatus,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? DefeatedAtUtc,
    string RawMetadata);

public sealed record BattleSubmittedAction(
    Guid ActionId,
    Guid ActionRequestId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    BattleActionType ActionType,
    IReadOnlyList<Guid> TargetParticipantIds,
    int? SkillDefinitionId,
    string? ItemReference,
    string IdempotencySafeId,
    string PayloadHash,
    BattleActionState State,
    DateTimeOffset SubmittedAtUtc,
    string CorrelationId);

public sealed record BattleLockedAction(
    Guid ActionId,
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ParticipantId,
    BattleActionType ActionType,
    IReadOnlyList<Guid> TargetParticipantIds,
    int? SkillDefinitionId,
    string? ItemReference,
    string IdempotencySafeId,
    string PayloadHash,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset LockedAtUtc,
    string CorrelationId);

public sealed record BattleActionResult(
    Guid ActionId,
    Guid ParticipantId,
    IReadOnlyList<Guid> TargetParticipantIds,
    BattleActionType ActionType,
    int ResolutionIndex,
    Guid? CombatIntentId,
    long Damage,
    long HpBefore,
    long HpAfter,
    bool TargetDefeated,
    BattleActionResolutionCode Result,
    string FailureCode,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    DateTimeOffset ResolvedAtUtc,
    bool IsDuplicate,
    byte[] NetworkBytes,
    IReadOnlyList<BattleParticipantMutation>? ParticipantMutations = null);

public sealed record BattleParticipantMutation(
    Guid ParticipantId,
    long Damage,
    long Heal,
    long HpBefore,
    long HpAfter,
    bool Defeated,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string FailureCode);

public sealed record BattleRound(
    Guid BattleInstanceId,
    int RoundNumber,
    BattleRoundState State,
    IReadOnlyList<Guid> EligibleParticipantIds,
    IReadOnlyList<BattleSubmittedAction> SubmittedActions,
    IReadOnlyList<BattleLockedAction> LockedActions,
    IReadOnlyList<Guid> TurnOrder,
    int CurrentResolutionIndex,
    IReadOnlyList<BattleActionResult> ResolutionResults,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ActionsLockedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long RoundVersion,
    string CorrelationId);

public sealed record BattleInstance(
    Guid BattleInstanceId,
    Guid BattleRequestId,
    string IdempotencySafeId,
    string WorldInstanceId,
    int SourceMapId,
    string EncounterDefinitionId,
    BattleType BattleType,
    BattleState State,
    BattlePhase CurrentPhase,
    int CurrentRoundNumber,
    int CurrentResolutionIndex,
    IReadOnlyList<BattleParticipant> Participants,
    IReadOnlyList<BattleRound> CompletedRounds,
    BattleRound CurrentRound,
    long BattleVersion,
    BattleWinnerSide WinnerSide,
    BattleCompletionReason CompletionReason,
    BattleRewardState RewardState,
    BattleRecoveryState RecoveryState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string CorrelationId,
    CombatPolicyStatus EncounterPolicyStatus,
    CombatPolicyStatus FormationPolicyStatus,
    CombatPolicyStatus TurnOrderPolicyStatus,
    CombatPolicyStatus VictoryPolicyStatus,
    CombatPolicyStatus RewardPolicyStatus,
    string ContentVersion,
    string RawMetadata);

public sealed record BattleTurnOrderResult(
    int RoundNumber,
    IReadOnlyList<Guid> OrderedActionIds,
    IReadOnlyList<Guid> OrderedParticipantIds,
    CombatPolicyStatus PolicyStatus,
    int? DeterministicSeed,
    string? EvidenceReference,
    DateTimeOffset CreatedAtUtc);

public sealed record BattleCreationResult(
    BattleResultCode Code,
    Guid BattleRequestId,
    Guid? BattleInstanceId,
    string IdempotencySafeId,
    string FailureCode,
    bool IsDuplicate,
    long BattleVersion,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is BattleResultCode.Success or BattleResultCode.DuplicateCompleted;
}

public sealed record BattleActionSubmissionResult(
    BattleResultCode Code,
    Guid ActionRequestId,
    Guid BattleInstanceId,
    Guid? ActionId,
    string IdempotencySafeId,
    string FailureCode,
    bool IsDuplicate,
    long BattleVersion,
    int RoundNumber,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is BattleResultCode.Success or BattleResultCode.DuplicateCompleted;
}

public sealed record BattleRoundResolutionResult(
    BattleResultCode Code,
    Guid BattleInstanceId,
    int RoundNumber,
    int ResolvedActionCount,
    BattleWinnerSide WinnerSide,
    string FailureCode,
    long BattleVersion,
    bool IsDuplicate,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is BattleResultCode.Success or BattleResultCode.DuplicateCompleted;
}

public sealed record BattleReconnectResult(
    BattleResultCode Code,
    Guid BattleInstanceId,
    Guid ParticipantId,
    BattleState State,
    BattlePhase Phase,
    int RoundNumber,
    string FailureCode,
    long BattleVersion,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code == BattleResultCode.Success;
}

public sealed record BattleCombatExecutionRequest(
    Guid BattleInstanceId,
    int RoundNumber,
    Guid ActionId,
    int ResolutionIndex,
    BattleParticipant Attacker,
    BattleParticipant Target,
    string StableIdempotencyKey,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId,
    long? SkillBaseDamage = null,
    CombatPolicyStatus SkillValuePolicyStatus = CombatPolicyStatus.EvidenceBlocked,
    bool TargetIsDefending = false);

public sealed record BattleCombatExecutionResult(
    BattleActionResolutionCode Code,
    Guid? CombatIntentId,
    long HpBefore,
    long Damage,
    long HpAfter,
    bool TargetDefeated,
    long RuntimeVersionBefore,
    long RuntimeVersionAfter,
    string FailureCode,
    bool IsDuplicate,
    CombatPolicyStatus PolicyStatus,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code == BattleActionResolutionCode.Success;
}

public sealed record BattleVictoryResult(
    bool IsComplete,
    BattleWinnerSide WinnerSide,
    BattleCompletionReason CompletionReason,
    CombatPolicyStatus PolicyStatus);

public sealed record BattleRewardGrant(
    long CharacterId,
    IReadOnlyList<CombatItemGrant> Items,
    IReadOnlyList<CombatCurrencyGrant> Currencies);

public sealed record BattleRewardPlan(
    Guid RewardPlanId,
    Guid BattleInstanceId,
    string IdempotencyKey,
    IReadOnlyList<BattleRewardGrant> Grants,
    CombatPolicyStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleRewardResult(
    BattleResultCode Code,
    Guid RewardPlanId,
    bool Committed,
    bool IsDuplicate,
    string FailureCode,
    DateTimeOffset CompletedAtUtc);

public sealed record BattleWorldReservation(
    Guid ReservationId,
    Guid BattleInstanceId,
    Guid ParticipantId,
    long CharacterId,
    long RuntimeEntityId,
    WorldBattleReservationState State,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset? ReleasedAtUtc);

public sealed record BattleEvent(
    Guid EventId,
    BattleEventKind Kind,
    Guid BattleInstanceId,
    Guid? BattleRequestId,
    int? RoundNumber,
    Guid? ActionId,
    Guid? ParticipantId,
    string State,
    string FailureCode,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record BattleAuditRecord(
    Guid AuditId,
    Guid BattleInstanceId,
    Guid? BattleRequestId,
    int? RoundNumber,
    Guid? ActionId,
    Guid? CompletionId,
    string IdempotencySafeId,
    string CorrelationId,
    string SessionSafeId,
    long? CharacterId,
    Guid? ParticipantId,
    Guid? AttackerParticipantId,
    IReadOnlyList<Guid> TargetParticipantIds,
    BattleActionType? ActionType,
    BattleState BattleStateBefore,
    BattleState BattleStateAfter,
    BattlePhase PhaseBefore,
    BattlePhase PhaseAfter,
    long BattleVersionBefore,
    long BattleVersionAfter,
    long? ParticipantVersionBefore,
    long? ParticipantVersionAfter,
    long? HpBefore,
    long? Damage,
    long? HpAfter,
    BattleResultCode Result,
    string FailureCode,
    CombatPolicyStatus TurnOrderPolicyStatus,
    CombatPolicyStatus FormationPolicyStatus,
    CombatPolicyStatus VictoryPolicyStatus,
    CombatPolicyStatus RewardPolicyStatus,
    bool RewardCommitted,
    BattleRecoveryState RecoveryState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record BattleCreationReplay(
    bool Found,
    bool PayloadMatches,
    BattleCreationResult? Result);

public sealed record BattleActionReplay(
    bool Found,
    bool PayloadMatches,
    BattleActionSubmissionResult? Result);

public interface ITurnBasedBattleCoordinator
{
    Task<BattleCreationResult> CreateAsync(BattleRequest request, CancellationToken cancellationToken);

    Task<BattleActionSubmissionResult> SubmitActionAsync(
        BattleActionRequest request,
        CancellationToken cancellationToken);

    Task<BattleRoundResolutionResult> LockAndResolveAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken);

    Task<BattleReconnectResult> DisconnectAsync(
        Guid battleInstanceId,
        string sessionId,
        long characterId,
        CancellationToken cancellationToken);

    Task<BattleReconnectResult> ReconnectAsync(
        Guid battleInstanceId,
        string sessionId,
        long characterId,
        CancellationToken cancellationToken);

    Task<BattleRoundResolutionResult> AbortAsync(
        Guid battleInstanceId,
        string reason,
        CancellationToken cancellationToken);

    Task<BattleRoundResolutionResult> RecoverAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken);
}

public interface IBattleInstanceRepository
{
    Task<BattleInstance?> GetAsync(Guid battleInstanceId, CancellationToken cancellationToken);

    Task<BattleInstance?> FindByRequestAsync(Guid battleRequestId, CancellationToken cancellationToken);

    Task<BattleInstance?> FindActiveByCharacterAsync(long characterId, CancellationToken cancellationToken);

    Task<OperationResult> CreateAsync(BattleInstance battle, CancellationToken cancellationToken);

    Task<OperationResult> SaveAsync(
        BattleInstance battle,
        long expectedVersion,
        CancellationToken cancellationToken);
}

public interface IBattleRuntimeReadModel
{
    IReadOnlyList<BattleInstance> Snapshot { get; }
}

public interface IBattleInstanceFactory
{
    OperationResult<BattleInstance> Create(
        BattleRequest request,
        BattleEncounterDefinition encounter,
        InteractionSessionBinding binding,
        PlayerCombatRuntimeState playerState,
        DateTimeOffset now);
}

public interface IBattleParticipantFactory
{
    OperationResult<BattleParticipant> CreatePlayer(
        Guid battleInstanceId,
        InteractionSessionBinding binding,
        PlayerCombatRuntimeState playerState,
        int formationSlot,
        CombatPolicyStatus formationPolicyStatus,
        DateTimeOffset now);

    OperationResult<BattleParticipant> CreateOpponent(
        Guid battleInstanceId,
        BattleEncounterParticipantDefinition definition,
        DateTimeOffset now);
}

public interface IBattleActionSubmissionStore
{
    Task<BattleActionReplay> FindAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken);

    Task SaveAsync(
        string idempotencyKey,
        string payloadHash,
        BattleActionSubmissionResult result,
        CancellationToken cancellationToken);
}

public interface IBattleActionResolver
{
    Task<BattleActionResult> ResolveAsync(
        BattleInstance battle,
        BattleLockedAction action,
        int resolutionIndex,
        CancellationToken cancellationToken);
}

public interface IBattleTurnOrderPolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    OperationResult<BattleTurnOrderResult> Resolve(
        BattleInstance battle,
        BattleRound round,
        DateTimeOffset now);
}

public interface IBattleVictoryPolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    BattleVictoryResult Evaluate(IReadOnlyList<BattleParticipant> participants);
}

public interface IBattleRewardPolicy
{
    CombatPolicyStatus PolicyStatus { get; }

    BattleRewardPlan CreatePlan(BattleInstance battle, DateTimeOffset now);
}

public interface IBattleRewardCoordinator
{
    Task<BattleRewardResult> FinalizeAsync(
        BattleRewardPlan plan,
        CancellationToken cancellationToken);
}

public interface IBattleClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IBattleIdempotencyStore
{
    Task<BattleCreationReplay> FindCreationAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken);

    Task SaveCreationAsync(
        string idempotencyKey,
        string payloadHash,
        BattleCreationResult result,
        CancellationToken cancellationToken);
}

public interface IBattleEventSink
{
    IReadOnlyList<BattleEvent> Events { get; }

    void Publish(BattleEvent battleEvent);
}

public interface IBattleAuditLedger
{
    IReadOnlyList<BattleAuditRecord> Records { get; }

    void Append(BattleAuditRecord record);
}

public interface IBattleRecoveryCoordinator
{
    Task<BattleRoundResolutionResult> RecoverAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken);
}

public interface IBattleCombatExecutionPort
{
    Task<BattleCombatExecutionResult> ExecuteAsync(
        BattleCombatExecutionRequest request,
        CancellationToken cancellationToken);
}

public interface IBattleEncounterResolver
{
    OperationResult<BattleEncounterDefinition> Resolve(BattleRequest request);
}

public interface IWorldBattleReservationRegistry
{
    OperationResult<BattleWorldReservation> Reserve(
        Guid battleInstanceId,
        Guid participantId,
        long characterId,
        long runtimeEntityId,
        DateTimeOffset now);

    OperationResult<BattleWorldReservation> GetByCharacter(long characterId);

    OperationResult Release(Guid battleInstanceId, long characterId, DateTimeOffset now);

    bool IsReserved(long characterId);

    IReadOnlyList<BattleWorldReservation> Snapshot { get; }
}

public static class BattleRuntimeHash
{
    public static string SafeId(string value) => Hash(value)[..16];

    public static string PersistenceKey(string value) => Hash(value);

    public static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public static string Request(BattleRequest request) =>
        Hash(string.Join(
            '\u001f',
            request.BattleRequestId,
            request.SourceSessionId,
            request.SourceCharacterId,
            request.SourceRuntimeEntityId,
            request.EncounterDefinitionId,
            string.Join(',', request.RequestedOpponentTemplateIds),
            request.SourceMapId,
            request.SourceWorldInstanceId,
            request.ExpectedPlayerRuntimeVersion,
            request.Source));

    public static string Action(BattleActionRequest request) =>
        Hash(string.Join(
            '\u001f',
            request.ActionRequestId,
            request.BattleInstanceId,
            request.BattleVersion,
            request.RoundNumber,
            request.SessionId,
            request.CharacterId,
            request.ParticipantId,
            request.ActionType,
            string.Join(',', request.TargetParticipantIds),
            request.SkillDefinitionId,
            request.ItemReference,
            request.Source));

    public static string SessionSafeId(string? sessionId) =>
        string.IsNullOrEmpty(sessionId) ? "" : SafeId(sessionId);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal static class BattleCollections
{
    public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}
