using System.Collections.ObjectModel;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public enum QuestContentStatus
{
    Verified,
    ContentBacked,
    Candidate,
    Unknown,
    VisualOnly,
    TestOnly,
    EvidenceBlocked
}

public enum QuestCategory
{
    Main,
    Side,
    Tutorial,
    Daily,
    Weekly,
    Repeatable,
    Event,
    Class,
    Profession,
    Hidden,
    Scripted,
    Unknown
}

public enum QuestType
{
    Standard,
    AutoAccept,
    AutoComplete,
    NpcAccepted,
    NpcTurnIn,
    SystemGranted,
    Chain,
    Repeatable,
    Unknown
}

public enum QuestIntentType
{
    Accept,
    Abandon,
    TurnIn,
    ClaimReward,
    Inspect,
    InternalProgress,
    Unknown
}

public enum QuestRequestSource
{
    ProductionClient,
    ServerRuntime,
    Recovery,
    Inspector,
    TestOnly
}

public enum QuestResultCode
{
    Success,
    DuplicateCompleted,
    Rejected,
    InvalidSession,
    OwnershipMismatch,
    QuestNotFound,
    QuestDisabled,
    QuestEvidenceBlocked,
    QuestNotAvailable,
    QuestAlreadyActive,
    QuestAlreadyCompleted,
    QuestNotActive,
    QuestNotReady,
    InvalidNpcBinding,
    PrerequisiteNotMet,
    EligibilityEvidenceBlocked,
    ObjectiveEvidenceBlocked,
    UnsupportedObjective,
    ProgressReplayConflict,
    ProgressVersionConflict,
    CompletionConflict,
    RewardEvidenceBlocked,
    UnsupportedReward,
    InventoryTransactionRejected,
    PersistenceFailure,
    RecoveryRequired,
    InternalFailure
}

public enum QuestNpcBindingType
{
    Accept,
    TurnIn,
    AcceptAndTurnIn,
    DialogOnly,
    Progress,
    Scripted,
    Unknown
}

public enum QuestPrerequisiteType
{
    QuestCompleted,
    QuestActive,
    QuestNotCompleted,
    CharacterLevel,
    CharacterClass,
    ItemOwned,
    CurrencyOwned,
    MapReached,
    Scripted,
    Unknown
}

public enum QuestRepeatPolicyType
{
    Once,
    Repeatable,
    Daily,
    Weekly,
    LimitedCount,
    EventWindow,
    Unknown
}

public enum QuestObjectiveType
{
    KillMonster,
    OwnItem,
    AcquireItem,
    SubmitItem,
    InteractNpc,
    VisitMap,
    UsePortal,
    CompleteBattle,
    UseSkill,
    ApplyStatus,
    ReachLevel,
    SpendCurrency,
    CraftItem,
    Escort,
    Protect,
    Scripted,
    Unknown
}

public enum QuestObjectiveStateCode
{
    Pending,
    Active,
    InProgress,
    Completed,
    Failed,
    EvidenceBlocked,
    RecoveryRequired
}

public enum QuestProgressMode
{
    Cumulative,
    CurrentOwnership,
    UniqueEvent,
    Boolean,
    Ordered,
    Unknown
}

public enum QuestCompletionPolicyType
{
    AllRequired,
    AnyRequired,
    Ordered,
    Scripted,
    Unknown
}

public enum QuestInstanceState
{
    Created,
    Accepting,
    Active,
    ReadyToComplete,
    Completing,
    RewardPending,
    Completed,
    Abandoning,
    Abandoned,
    RecoveryRequired,
    Faulted,
    Closed
}

public enum QuestRewardState
{
    None,
    Planned,
    Committing,
    Committed,
    Failed,
    EvidenceBlocked,
    RecoveryRequired
}

public enum QuestRecoveryState
{
    None,
    Pending,
    Reloaded,
    Reconciled,
    RecoveryRequired,
    Failed
}

public enum QuestRewardType
{
    NoReward,
    Item,
    Currency,
    Experience,
    Skill,
    Reputation,
    Title,
    Pet,
    ClassChange,
    PlayerChoice,
    RandomChoice,
    Unknown
}

public enum QuestSemanticEventType
{
    MonsterDefeated,
    BattleCompleted,
    InventoryCommitted,
    ItemOwnershipChanged,
    NpcInteractionCompleted,
    PortalTransitionCommitted,
    MapEntered,
    SkillExecuted,
    StatusApplied,
    CharacterLevelChanged,
    CraftingCommitted,
    Scripted,
    Unknown
}

public enum QuestEventKind
{
    QuestAcceptRequested,
    QuestAcceptRejected,
    QuestAccepted,
    QuestAbandonRequested,
    QuestAbandoned,
    QuestSemanticEventReceived,
    QuestObjectiveMatched,
    QuestProgressPlanned,
    QuestProgressCommitted,
    QuestObjectiveCompleted,
    QuestReadyToComplete,
    QuestTurnInRequested,
    QuestCompletionStarted,
    QuestRewardPlanned,
    QuestRewardCommitted,
    QuestRewardFailed,
    QuestCompleted,
    QuestRecoveryStarted,
    QuestRecoveryCompleted,
    QuestRecoveryRequired
}

public enum QuestFailurePoint
{
    None,
    AcceptancePersistence,
    AcceptanceRuntime,
    AbandonPersistence,
    ProgressPersistence,
    ProgressRuntime,
    CompletionEvaluation,
    RewardTransaction,
    CompletionPersistence,
    Audit,
    Inspector,
    Recovery
}

public sealed class QuestFailureInjection
{
    public QuestFailurePoint Point { get; set; }
}

public sealed record QuestNpcBinding(
    int QuestDefinitionId,
    int NpcTemplateId,
    QuestNpcBindingType BindingType,
    int? MapIdCandidate,
    string InteractionType,
    bool Enabled,
    QuestContentStatus PolicyStatus,
    string RawMetadata);

public sealed record QuestPrerequisiteDefinition(
    string PrerequisiteDefinitionId,
    QuestPrerequisiteType Type,
    int? RequiredQuestDefinitionId,
    int? ItemTemplateId,
    long RequiredValue,
    QuestContentStatus PolicyStatus,
    string RawMetadata);

public sealed record QuestRepeatPolicy(
    QuestRepeatPolicyType Type,
    int? MaximumIterations,
    QuestContentStatus PolicyStatus,
    string RawMetadata);

public sealed record QuestObjectiveDefinition(
    string ObjectiveDefinitionId,
    int QuestDefinitionId,
    int ObjectiveIndex,
    QuestObjectiveType ObjectiveType,
    int? TargetTemplateId,
    string TargetRuntimeType,
    long RequiredCount,
    QuestProgressMode ProgressMode,
    string CompletionMode,
    bool Required,
    QuestContentStatus PolicyStatus,
    string ContentVersion,
    string RawMetadata);

public sealed record QuestItemReward(int ItemTemplateId, int Quantity);

public sealed record QuestCurrencyReward(string CurrencyType, long Amount);

public sealed record QuestRewardDefinition(
    string RewardDefinitionId,
    IReadOnlyList<QuestItemReward> ItemRewards,
    IReadOnlyList<QuestCurrencyReward> CurrencyRewards,
    long? ExperienceRewardCandidate,
    int? SkillRewardCandidate,
    IReadOnlyList<string> OtherRewardCandidates,
    QuestContentStatus PolicyStatus,
    string RawMetadata)
{
    public bool IsNoReward =>
        ItemRewards.Count == 0 &&
        CurrencyRewards.Count == 0 &&
        ExperienceRewardCandidate is null &&
        SkillRewardCandidate is null &&
        OtherRewardCandidates.Count == 0;
}

public sealed record QuestDefinitionRecord(
    int QuestDefinitionId,
    string ExternalQuestId,
    string RawName,
    string RawDescription,
    QuestCategory QuestCategory,
    QuestType QuestType,
    string AcceptPolicy,
    QuestCompletionPolicyType CompletionPolicy,
    QuestRepeatPolicy RepeatPolicy,
    IReadOnlyList<QuestPrerequisiteDefinition> Prerequisites,
    IReadOnlyList<QuestObjectiveDefinition> ObjectiveDefinitions,
    QuestRewardDefinition? RewardDefinition,
    IReadOnlyList<QuestNpcBinding> NpcBindings,
    bool Enabled,
    string ContentVersion,
    QuestContentStatus PolicyStatus,
    QuestContentStatus ProtocolStatus,
    string RawMetadata);

public sealed record QuestDefinition(
    int QuestDefinitionId,
    string ExternalQuestId,
    string Name,
    string Description,
    QuestCategory QuestCategory,
    QuestType QuestType,
    string AcceptPolicy,
    QuestCompletionPolicyType CompletionPolicy,
    QuestRepeatPolicy RepeatPolicy,
    IReadOnlyList<QuestPrerequisiteDefinition> Prerequisites,
    IReadOnlyList<QuestObjectiveDefinition> ObjectiveDefinitions,
    QuestRewardDefinition RewardDefinition,
    IReadOnlyList<QuestNpcBinding> NpcBindings,
    bool Enabled,
    string ContentVersion,
    QuestContentStatus PolicyStatus,
    QuestContentStatus ProtocolStatus,
    string RawMetadata);

public sealed record QuestIntent(
    Guid QuestIntentId,
    string IdempotencyKey,
    string SessionId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    int QuestDefinitionId,
    long? SourceNpcRuntimeEntityId,
    int? SourceNpcTemplateId,
    QuestIntentType IntentType,
    long? ExpectedQuestVersion,
    long ExpectedCharacterRuntimeVersion,
    QuestRequestSource Source,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestSessionSnapshot(
    string SessionId,
    long AccountId,
    long CharacterId,
    long PlayerRuntimeEntityId,
    long CharacterRuntimeVersion,
    int MapId,
    bool Authenticated,
    bool CharacterActive);

public sealed record QuestObjectiveState(
    string ObjectiveDefinitionId,
    int ObjectiveIndex,
    QuestObjectiveType ObjectiveType,
    int? TargetTemplateId,
    long RequiredCount,
    long CurrentProgress,
    QuestObjectiveStateCode State,
    long ObjectiveVersion,
    string? LastSemanticEventSafeId,
    QuestContentStatus PolicyStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record QuestInstance(
    Guid QuestInstanceId,
    long CharacterId,
    int QuestDefinitionId,
    QuestInstanceState State,
    DateTimeOffset AcceptedAtUtc,
    int? AcceptedFromNpcTemplateId,
    int AcceptedMapId,
    IReadOnlyList<QuestObjectiveState> ObjectiveStates,
    long QuestVersion,
    string DefinitionContentVersion,
    int RepeatIteration,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? AbandonedAtUtc,
    QuestRewardState RewardState,
    QuestRecoveryState RecoveryState,
    DateTimeOffset? LastProgressAtUtc,
    string CorrelationId,
    string RawMetadata);

public sealed record QuestAcceptancePlan(
    Guid AcceptancePlanId,
    Guid QuestIntentId,
    Guid QuestInstanceId,
    long CharacterId,
    int QuestDefinitionId,
    int? SourceNpcTemplateId,
    IReadOnlyList<QuestObjectiveState> InitialObjectiveStates,
    long ExpectedQuestVersion,
    string DefinitionContentVersion,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestAbandonPlan(
    Guid AbandonPlanId,
    Guid QuestIntentId,
    Guid QuestInstanceId,
    long CharacterId,
    int QuestDefinitionId,
    long ExpectedQuestVersion,
    IReadOnlyList<QuestObjectiveState> ProgressSnapshot,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestSemanticEvent(
    Guid SemanticEventId,
    QuestSemanticEventType EventType,
    string SourceRuntime,
    long CharacterId,
    string AccountSafeReference,
    Guid? BattleInstanceId,
    int? RoundNumber,
    long? MonsterRuntimeEntityId,
    int? MonsterTemplateId,
    Guid? DeathId,
    Guid? InventoryTransactionId,
    int? ItemTemplateId,
    long? QuantityBefore,
    long? QuantityAfter,
    Guid? WorldInteractionId,
    long? TargetRuntimeEntityId,
    int? TargetTemplateId,
    Guid? PortalTransitionId,
    int? PortalTemplateId,
    int? SourceMapId,
    int? TargetMapId,
    string? EncounterDefinitionId,
    bool? BattleWon,
    bool Committed,
    long EventVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string RawMetadata);

public sealed record QuestProgressMutationPlan(
    Guid ProgressMutationId,
    Guid SemanticEventId,
    string SemanticPayloadHash,
    Guid QuestInstanceId,
    int QuestDefinitionId,
    string ObjectiveDefinitionId,
    int ObjectiveIndex,
    long CharacterId,
    long ProgressBefore,
    long ProgressDelta,
    long ProgressAfter,
    QuestObjectiveStateCode ObjectiveStateBefore,
    QuestObjectiveStateCode ObjectiveStateAfter,
    QuestInstanceState QuestInstanceStateBefore,
    QuestInstanceState QuestInstanceStateAfter,
    long ExpectedQuestVersion,
    long ExpectedObjectiveVersion,
    QuestContentStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestProgressResult(
    QuestResultCode Code,
    Guid ProgressMutationId,
    Guid SemanticEventId,
    Guid QuestInstanceId,
    string ObjectiveDefinitionId,
    long ProgressBefore,
    long ProgressAfter,
    long QuestVersion,
    long ObjectiveVersion,
    bool ObjectiveCompleted,
    bool QuestReady,
    bool IsDuplicate,
    string FailureCode);

public sealed record QuestRewardPlan(
    Guid QuestRewardPlanId,
    Guid CompletionPlanId,
    Guid QuestInstanceId,
    int QuestDefinitionId,
    long CharacterId,
    IReadOnlyList<QuestItemReward> ItemRewards,
    IReadOnlyList<QuestCurrencyReward> CurrencyRewards,
    long? ExperienceRewardCandidate,
    int? SkillRewardCandidate,
    IReadOnlyList<string> OtherRewardCandidates,
    string IdempotencyKeyHash,
    QuestContentStatus PolicyStatus,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestRewardResult(
    QuestResultCode Code,
    Guid QuestRewardPlanId,
    bool ItemCommitted,
    bool CurrencyCommitted,
    bool IsDuplicate,
    string FailureCode,
    InventoryTransactionResult? InventoryResult);

public sealed record QuestCompletionPlan(
    Guid CompletionPlanId,
    Guid QuestIntentId,
    Guid QuestInstanceId,
    int QuestDefinitionId,
    long CharacterId,
    int? TurnInNpcTemplateId,
    IReadOnlyList<QuestObjectiveState> ObjectiveFinalStates,
    QuestRewardPlan RewardPlan,
    long ExpectedQuestVersion,
    string DefinitionContentVersion,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record QuestOperationResult(
    QuestResultCode Code,
    Guid QuestIntentId,
    Guid? QuestInstanceId,
    long QuestVersion,
    bool IsDuplicate,
    string FailureCode,
    QuestInstance? Instance,
    QuestRewardResult? RewardResult,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is QuestResultCode.Success or QuestResultCode.DuplicateCompleted;
}

public sealed record QuestReplayLookup(
    bool Found,
    bool PayloadMatches,
    QuestOperationResult? Result);

public sealed record QuestProgressReplayLookup(
    bool Found,
    bool PayloadMatches,
    QuestProgressResult? Result);

public sealed record QuestRewardReplayLookup(
    bool Found,
    bool PayloadMatches,
    QuestRewardResult? Result);

public sealed record QuestEligibilityContext(
    QuestIntent Intent,
    QuestDefinition Definition,
    QuestSessionSnapshot Session,
    IReadOnlyList<QuestInstance> ExistingInstances,
    IReadOnlySet<int> CompletedQuestDefinitionIds,
    IReadOnlyDictionary<int, long> OwnedItemQuantities,
    bool ProductionPath);

public sealed record QuestBindingContext(
    QuestDefinition Definition,
    QuestNpcBindingType RequiredBindingType,
    long? NpcRuntimeEntityId,
    int? NpcTemplateId,
    int CurrentMapId,
    bool InteractionValidated,
    bool ProductionPath);

public sealed record QuestEligibilityResult(
    QuestResultCode Code,
    string FailureCode)
{
    public bool Succeeded => Code == QuestResultCode.Success;
}

public sealed record QuestEvent(
    Guid EventId,
    QuestEventKind Kind,
    Guid? QuestIntentId,
    Guid? QuestInstanceId,
    int? QuestDefinitionId,
    string? ObjectiveDefinitionId,
    Guid? SemanticEventId,
    Guid? CompletionPlanId,
    Guid? RewardPlanId,
    long CharacterId,
    string State,
    string FailureCode,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    int NetworkBytes);

public sealed record QuestAuditRecord(
    Guid AuditId,
    Guid? QuestIntentId,
    Guid? QuestInstanceId,
    int? QuestDefinitionId,
    string? ObjectiveDefinitionId,
    Guid? ProgressMutationId,
    Guid? SemanticEventId,
    Guid? CompletionPlanId,
    Guid? RewardPlanId,
    string SafeIdempotencyIdentifier,
    string CorrelationId,
    string SessionSafeHash,
    long CharacterId,
    int? SourceNpcTemplateId,
    string EventType,
    string Operation,
    QuestInstanceState? QuestInstanceStateBefore,
    QuestInstanceState? QuestInstanceStateAfter,
    QuestObjectiveStateCode? ObjectiveStateBefore,
    QuestObjectiveStateCode? ObjectiveStateAfter,
    long? ProgressBefore,
    long? ProgressDelta,
    long? ProgressAfter,
    long? QuestVersionBefore,
    long? QuestVersionAfter,
    long? ObjectiveVersionBefore,
    long? ObjectiveVersionAfter,
    QuestRewardState RewardState,
    bool RewardCommitted,
    QuestResultCode Result,
    string FailureCode,
    QuestRecoveryState RecoveryState,
    IReadOnlyList<QuestContentStatus> PolicyStatuses,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record QuestDefinitionInspectorItem(
    int QuestDefinitionId,
    string Name,
    QuestCategory Category,
    QuestType QuestType,
    int ObjectiveCount,
    IReadOnlyList<QuestRewardType> RewardTypes,
    int AcceptBindingCount,
    int TurnInBindingCount,
    QuestRepeatPolicyType RepeatPolicy,
    bool Enabled,
    string ContentVersion,
    QuestContentStatus PolicyStatus,
    QuestContentStatus ProtocolStatus);

public sealed record QuestInstanceInspectorItem(
    Guid QuestInstanceId,
    long CharacterId,
    int QuestDefinitionId,
    QuestInstanceState State,
    long QuestVersion,
    int ObjectiveCount,
    int CompletedObjectiveCount,
    QuestRewardState RewardState,
    int RepeatIteration,
    QuestRecoveryState RecoveryState,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? AbandonedAtUtc);

public sealed record QuestObjectiveInspectorItem(
    Guid QuestInstanceId,
    string ObjectiveDefinitionId,
    int ObjectiveIndex,
    QuestObjectiveType ObjectiveType,
    int? TargetTemplateId,
    long RequiredCount,
    long CurrentProgress,
    QuestObjectiveStateCode State,
    long ObjectiveVersion,
    string LastSemanticEventSafeDisplay,
    QuestContentStatus PolicyStatus,
    DateTimeOffset UpdatedAtUtc);

public sealed record QuestProgressInspectorItem(
    Guid ProgressMutationId,
    string SemanticEventSafeDisplay,
    Guid QuestInstanceId,
    string ObjectiveDefinitionId,
    long ProgressBefore,
    long ProgressDelta,
    long ProgressAfter,
    QuestResultCode Result,
    string FailureCode,
    DateTimeOffset CreatedAtUtc);

public sealed record QuestRewardInspectorItem(
    Guid QuestRewardPlanId,
    Guid QuestInstanceId,
    int ItemRewardCount,
    int CurrencyRewardCount,
    QuestRewardState State,
    QuestResultCode Result,
    string FailureCode,
    string SafeIdempotencyId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record QuestInspectorQuery(
    long? CharacterId = null,
    int? QuestDefinitionId = null,
    Guid? QuestInstanceId = null,
    QuestInstanceState? QuestInstanceState = null,
    QuestObjectiveType? ObjectiveType = null,
    QuestObjectiveStateCode? ObjectiveState = null,
    int? SourceNpcTemplateId = null,
    QuestSemanticEventType? EventType = null,
    bool ActiveOnly = false,
    bool ReadyOnly = false,
    bool CompletedOnly = false,
    bool AbandonedOnly = false,
    bool FailedOnly = false,
    bool RecoveryRequiredOnly = false,
    bool EvidenceBlockedOnly = false,
    bool TestOnlyOnly = false,
    bool DuplicateOnly = false,
    int Offset = 0,
    int Limit = 100);

public sealed record QuestInspectorSnapshot(
    IReadOnlyList<QuestDefinitionInspectorItem> Definitions,
    IReadOnlyList<QuestInstanceInspectorItem> Instances,
    IReadOnlyList<QuestObjectiveInspectorItem> Objectives,
    IReadOnlyList<QuestProgressInspectorItem> Progress,
    IReadOnlyList<QuestRewardInspectorItem> Rewards,
    IReadOnlyList<QuestAuditRecord> Audit,
    int Offset,
    int Limit,
    string FailureCode,
    DateTimeOffset CapturedAtUtc);

public sealed record QuestContentEvidenceSummary(
    int QuestDefinitionRecordCount,
    int QuestObjectiveRecordCount,
    int QuestRewardRecordCount,
    int NpcQuestBindingCount,
    int QuestChainPrerequisiteCount,
    int RepeatableQuestCandidateCount,
    int TimedQuestCandidateCount,
    int DialogQuestReferenceCount,
    int QuestItemCandidateCount,
    int MonsterQuestReferenceCount,
    QuestContentStatus ProductionStatus,
    string Source,
    string EvidenceHash);

public interface IQuestDefinitionCatalog
{
    IReadOnlyList<QuestDefinition> Snapshot { get; }

    OperationResult<QuestDefinition> Get(int questDefinitionId, QuestRequestSource source);
}

public interface IQuestDefinitionRepository
{
    Task<IReadOnlyList<QuestDefinitionRecord>> LoadAsync(CancellationToken cancellationToken);
}

public interface IQuestDefinitionMapper
{
    OperationResult<QuestDefinition> Map(QuestDefinitionRecord record);
}

public interface IQuestDefinitionValidator
{
    OperationResult Validate(QuestDefinition definition);

    OperationResult ValidateCatalog(IReadOnlyList<QuestDefinition> definitions);
}

public interface IQuestBindingResolver
{
    OperationResult<QuestNpcBinding> Resolve(QuestBindingContext context);
}

public interface IQuestEligibilityPolicy
{
    QuestEligibilityResult Evaluate(QuestEligibilityContext context);
}

public interface IQuestSessionValidator
{
    OperationResult<QuestSessionSnapshot> Validate(QuestIntent intent);
}

public interface IQuestInventorySnapshotProvider
{
    Task<long> GetOwnedQuantityAsync(
        long characterId,
        int itemTemplateId,
        CancellationToken cancellationToken);
}

public interface IQuestInstanceAuthority
{
    IReadOnlyList<QuestInstance> Snapshot { get; }

    QuestInstance? Get(Guid questInstanceId);

    IReadOnlyList<QuestInstance> GetCharacter(long characterId);

    OperationResult<QuestInstance> Add(QuestInstance instance);

    OperationResult<QuestInstance> Replace(QuestInstance instance, long expectedVersion);

    OperationResult Reload(long characterId, IEnumerable<QuestInstance> instances);
}

public interface IQuestCoordinator
{
    Task<QuestOperationResult> AcceptAsync(QuestIntent intent, CancellationToken cancellationToken);

    Task<QuestOperationResult> AbandonAsync(QuestIntent intent, CancellationToken cancellationToken);

    Task<QuestOperationResult> TurnInAsync(QuestIntent intent, CancellationToken cancellationToken);
}

public interface IQuestAcceptanceStore
{
    Task<QuestReplayLookup> FindOperationAsync(
        string operation,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitAcceptanceAsync(
        QuestAcceptancePlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);
}

public interface IQuestObjectiveHandler
{
    QuestObjectiveType ObjectiveType { get; }

    OperationResult<QuestProgressMutationPlan?> CreatePlan(
        QuestDefinition definition,
        QuestInstance instance,
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent);
}

public interface IQuestObjectiveHandlerRegistry
{
    OperationResult<IQuestObjectiveHandler> Resolve(QuestObjectiveType objectiveType);
}

public interface IQuestSemanticEventAdapter
{
    OperationResult<QuestSemanticEvent> FromMonsterDeath(
        MonsterDeathRecord death,
        long characterId,
        string accountSafeReference);

    OperationResult<QuestSemanticEvent> FromInventoryCommit(
        InventoryTransactionResult result,
        InventoryTransactionRequest request,
        int itemTemplateId,
        long quantityBefore,
        long quantityAfter,
        string accountSafeReference,
        string correlationId);

    OperationResult<QuestSemanticEvent> FromNpcInteraction(
        WorldInteractionEvent runtimeEvent,
        int npcTemplateId,
        bool interactionSucceeded,
        string accountSafeReference);

    OperationResult<QuestSemanticEvent> FromPortalTransition(
        PortalTransitionResult result,
        string accountSafeReference);

    OperationResult<QuestSemanticEvent> FromBattleCompletion(
        Guid semanticEventId,
        Guid battleInstanceId,
        long characterId,
        string encounterDefinitionId,
        bool won,
        bool committed,
        string accountSafeReference,
        DateTimeOffset occurredAtUtc,
        string correlationId);

    OperationResult<QuestSemanticEvent> FromCraftingCommit(
        Guid semanticEventId,
        CraftingResult result,
        long characterId,
        int resultItemTemplateId,
        string accountSafeReference,
        DateTimeOffset occurredAtUtc,
        string correlationId);
}

public interface IQuestEventRouter
{
    Task<IReadOnlyList<QuestProgressResult>> RouteAsync(
        QuestSemanticEvent semanticEvent,
        CancellationToken cancellationToken);
}

public interface IQuestProgressCoordinator
{
    Task<QuestProgressResult> CommitAsync(
        QuestProgressMutationPlan plan,
        CancellationToken cancellationToken);
}

public interface IQuestCompletionPolicy
{
    OperationResult<bool> IsReady(QuestDefinition definition, QuestInstance instance);
}

public interface IQuestRewardCoordinator
{
    Task<QuestRewardResult> CommitAsync(
        QuestRewardPlan plan,
        string sessionId,
        CancellationToken cancellationToken);
}

public interface IQuestRewardStore
{
    Task<QuestRewardReplayLookup> FindRewardAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitRewardAsync(
        QuestRewardPlan plan,
        QuestRewardResult result,
        string payloadHash,
        CancellationToken cancellationToken);
}

public interface IQuestRuntimeStore : IQuestAcceptanceStore, IQuestRewardStore
{
    Task<IReadOnlyList<QuestInstance>> LoadCharacterAsync(long characterId, CancellationToken cancellationToken);

    Task<QuestInstance?> LoadInstanceAsync(Guid questInstanceId, CancellationToken cancellationToken);

    Task<QuestProgressReplayLookup> FindProgressAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitProgressAsync(
        QuestProgressMutationPlan plan,
        QuestInstance instance,
        QuestProgressResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitAbandonmentAsync(
        QuestAbandonPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> CommitCompletionAsync(
        QuestCompletionPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<OperationResult> SaveRecoveryAsync(
        QuestInstance instance,
        string failureCode,
        CancellationToken cancellationToken);

    IReadOnlyList<QuestProgressMutationPlan> ProgressPlans { get; }

    IReadOnlyList<(QuestRewardPlan Plan, QuestRewardResult Result)> RewardRecords { get; }
}

public interface IQuestRecoveryCoordinator
{
    Task<QuestOperationResult> RecoverAsync(
        long characterId,
        Guid? questInstanceId,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface IQuestEventSink
{
    void Publish(QuestEvent runtimeEvent);

    IReadOnlyList<QuestEvent> Snapshot { get; }
}

public interface IQuestAuditLedger
{
    void Append(QuestAuditRecord record);

    IReadOnlyList<QuestAuditRecord> Snapshot { get; }
}

public interface IQuestInspectorSource
{
    QuestInspectorSnapshot Capture(QuestInspectorQuery query);
}

internal static class QuestRuntimeCollections
{
    public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());

    public static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(
        IDictionary<TKey, TValue> values)
        where TKey : notnull =>
        new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(values));
}
