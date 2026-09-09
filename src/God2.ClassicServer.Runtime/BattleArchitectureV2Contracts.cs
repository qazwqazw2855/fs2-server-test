using God2.ClassicServer.Application.Configuration;

namespace God2.ClassicServer.Runtime;

public enum BattleEngineMode
{
    LegacyPrimary,
    ActorShadow,
    ActorPrimary,
    ActorPrimaryWithLegacyFallbackDisabled
}

public enum BattleActorLifecycleState
{
    Created,
    Running,
    Draining,
    Stopped,
    Faulted
}

public enum BattleActorStateCode
{
    Created,
    Preparing,
    WaitingForClientReady,
    RoundOpening,
    CollectingCommands,
    LockingCommands,
    PlanningResolution,
    ResolvingActions,
    RoundClosing,
    EvaluatingCompletion,
    Finalizing,
    Completed,
    Suspended,
    RecoveryRequired,
    Aborting,
    Aborted,
    Faulted
}

public enum BattleActorResultCode
{
    Accepted,
    Success,
    DuplicateCompleted,
    Rejected,
    ReplayConflict,
    InvalidSession,
    OwnershipMismatch,
    BattleNotFound,
    BattleUnavailable,
    InvalidBattleState,
    ParticipantNotFound,
    ParticipantDefeated,
    StaleRound,
    FutureRound,
    StaleCommandWindow,
    InvalidActionSlot,
    ActionAlreadySubmitted,
    ActionAlreadyLocked,
    UnsupportedAction,
    EvidenceBlocked,
    VersionConflict,
    MailboxBackpressure,
    BattleBusy,
    HostShuttingDown,
    RecoveryRequired,
    BattleCompleted,
    BattleAborted,
    PersistenceFailure,
    VersionUnavailable,
    CheckpointCorrupt,
    Divergence,
    InternalFailure
}

public enum BattleActorCommandType
{
    BasicAttack,
    Skill,
    Item,
    Defend,
    Flee,
    Pass,
    Auto,
    TimeoutDefault,
    MonsterAi,
    TrustedInternalTest,
    Unknown
}

public enum BattleActorCommandSource
{
    OfficialClient,
    GatewayAdapter,
    TimeoutPolicy,
    AutoPolicy,
    TrustedInternalTest,
    Recovery,
    Unknown
}

public enum BattleMessageKind
{
    Start,
    ClientReady,
    SubmitCommand,
    CommandWindowExpired,
    SessionDisconnected,
    SessionReconnected,
    SnapshotRequested,
    AbortRequested,
    RecoveryRequested,
    OutboxDispatchAcknowledged,
    FinalizationRequested,
    ShutdownRequested,
    InternalContinueResolution,
    Unknown
}

public enum BattleJournalEntryKind
{
    BattleCreated,
    BattlePrepared,
    ClientReadyBoundaryReached,
    RoundOpened,
    CommandAccepted,
    CommandRejected,
    CommandWindowExpired,
    CommandLocked,
    RoundPlanCreated,
    ActionStarted,
    ActionResultCommitted,
    RoundClosingStarted,
    RoundCompleted,
    CompletionDetected,
    FinalizationStarted,
    FinalizationCommitted,
    BattleCompleted,
    SessionDisconnected,
    SessionReconnected,
    ActorSuspended,
    RecoveryStarted,
    RecoveryCompleted,
    RecoveryRequired,
    BattleAborted,
    ActorStopped
}

public enum BattleArchitectureEventKind
{
    BattleStarted,
    ParticipantAdded,
    RoundOpened,
    CommandWindowOpened,
    CommandAccepted,
    CommandLocked,
    ActionStarted,
    ResourceSpent,
    DamageApplied,
    HealingApplied,
    StatusApplied,
    StatusRemoved,
    ParticipantDefeated,
    VictoryDetected,
    RoundCompleted,
    BattleFinalizationStarted,
    RewardFinalized,
    QuestSemanticEventReady,
    BattleCompleted,
    BattleAborted,
    BattleRecoveryRequired
}

public enum BattleEventDeliveryCategory
{
    ProtocolAdapter,
    QuestRuntime,
    Administration,
    ReplayArtifact,
    Diagnostics,
    Internal
}

public enum BattleOutboxDispatchState
{
    Pending,
    Dispatching,
    Dispatched,
    RetryRequired,
    DeadLettered
}

public enum BattleFinalizationStateCode
{
    NotStarted,
    Planned,
    RewardPending,
    RewardCommitted,
    QuestPending,
    QuestCommitted,
    WorldResumePending,
    Completed,
    RecoveryRequired,
    Failed
}

public enum BattleActorRecoveryStateCode
{
    NotRequired,
    Planned,
    RestoringCheckpoint,
    ReplayingJournal,
    ReconcilingRuntime,
    Resuming,
    Completed,
    RecoveryRequired,
    Failed
}

public enum BattleReplayResultCode
{
    Match,
    Divergence,
    VersionUnavailable,
    InvalidArtifact,
    UnsupportedSchema,
    ExecutionFailure
}

public enum BattleArchitectureFailurePoint
{
    None,
    ActorCreation,
    RegistryConflict,
    MailboxWrite,
    CommandPersistence,
    CommandLockPersistence,
    RoundPlanPersistence,
    RandomStatePersistence,
    ActionResolver,
    ActionResultPersistence,
    CursorPersistence,
    JournalAppend,
    CheckpointWrite,
    OutboxWrite,
    OutboxDispatch,
    RewardFinalization,
    QuestDispatch,
    WorldResume,
    Recovery,
    Inspector,
    Audit,
    Shutdown
}

public sealed class BattleArchitectureFailureInjection
{
    public BattleArchitectureFailurePoint Point { get; set; }
}

public sealed record BattleActorOptions(
    int MailboxCapacity,
    TimeSpan MessageEnqueueTimeout,
    TimeSpan ShutdownDrainTimeout,
    int MaximumJournalPayloadBytes,
    int MaximumCheckpointPayloadBytes,
    int MaximumOutboxPayloadBytes)
{
    public int MaximumRetainedStoppedActors { get; init; } = 256;

    public static BattleActorOptions Default { get; } = new(
        MailboxCapacity: 128,
        MessageEnqueueTimeout: TimeSpan.FromSeconds(2),
        ShutdownDrainTimeout: TimeSpan.FromSeconds(10),
        MaximumJournalPayloadBytes: 64 * 1024,
        MaximumCheckpointPayloadBytes: 512 * 1024,
        MaximumOutboxPayloadBytes: 64 * 1024);

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (MailboxCapacity is < 1 or > 65_536)
        {
            errors.Add("battle.actor.mailbox_capacity");
        }

        if (MessageEnqueueTimeout <= TimeSpan.Zero || MessageEnqueueTimeout > TimeSpan.FromMinutes(1))
        {
            errors.Add("battle.actor.enqueue_timeout");
        }

        if (ShutdownDrainTimeout <= TimeSpan.Zero || ShutdownDrainTimeout > TimeSpan.FromMinutes(5))
        {
            errors.Add("battle.actor.shutdown_timeout");
        }

        if (MaximumRetainedStoppedActors is < 0 or > 4_096)
        {
            errors.Add("battle.actor.stopped_retention");
        }

        if (MaximumJournalPayloadBytes is < 1 or > 1_048_576 ||
            MaximumCheckpointPayloadBytes is < 1 or > 4_194_304 ||
            MaximumOutboxPayloadBytes is < 1 or > 1_048_576)
        {
            errors.Add("battle.actor.payload_bound");
        }

        return errors.AsReadOnly();
    }
}

public sealed record BattleEngineSelectionOptions(
    BattleEngineMode Mode,
    bool ActorPrimaryAllowed,
    bool SilentFallbackAllowed = false)
{
    public static BattleEngineSelectionOptions FromConfiguration(ServerConfiguration configuration)
    {
        if (!Enum.TryParse<BattleEngineMode>(
                configuration.Server.BattleEngineMode,
                ignoreCase: true,
                out var mode))
        {
            mode = (BattleEngineMode)(-1);
        }

        return new BattleEngineSelectionOptions(
            mode,
            ActorPrimaryAllowed: mode is BattleEngineMode.LegacyPrimary or BattleEngineMode.ActorShadow,
            SilentFallbackAllowed: false);
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (!Enum.IsDefined(Mode))
        {
            errors.Add("battle.engine.mode_invalid");
        }

        if (SilentFallbackAllowed)
        {
            errors.Add("battle.engine.silent_fallback_forbidden");
        }

        if (Mode is BattleEngineMode.ActorPrimary or BattleEngineMode.ActorPrimaryWithLegacyFallbackDisabled &&
            !ActorPrimaryAllowed)
        {
            errors.Add("battle.engine.actor_primary_not_authorized");
        }

        return errors.AsReadOnly();
    }
}

public sealed record BattleStartPlan(
    Guid BattleId,
    BattleRequest LegacyRequest,
    BattleInstance? PreparedBattle,
    BattleEngineMode RequestedMode,
    string ServerBuildVersion,
    string FormulaVersion,
    string SkillContentVersion,
    string StatusContentVersion,
    string MonsterContentVersion,
    string FormationContentVersion,
    string AiContentVersion,
    string RngAlgorithmVersion,
    ulong RngSeed,
    bool TestOnly,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleEngineCreateResult(
    BattleActorResultCode Code,
    Guid BattleId,
    BattleEngineMode EngineMode,
    string FailureCode,
    bool IsDuplicate,
    long BattleVersion,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted;
}

public sealed record BattleEngineOperationResult(
    BattleActorResultCode Code,
    Guid BattleId,
    string FailureCode,
    bool IsDuplicate,
    long BattleVersion,
    int RoundNumber,
    long CommandWindowVersion,
    byte[] NetworkBytes)
{
    public bool Succeeded => Code is
        BattleActorResultCode.Success or
        BattleActorResultCode.Accepted or
        BattleActorResultCode.DuplicateCompleted;

    public static BattleEngineOperationResult Failure(
        Guid battleId,
        BattleActorResultCode code,
        string failureCode) =>
        new(code, battleId, failureCode, false, 0, 0, 0, []);
}

public sealed record BattleEngineStatus(
    Guid BattleId,
    BattleEngineMode EngineMode,
    BattleActorLifecycleState LifecycleState,
    BattleActorStateCode BattleState,
    long BattleVersion,
    int RoundNumber,
    long CommandWindowVersion,
    int MailboxDepth,
    int MailboxCapacity,
    BattleFinalizationStateCode FinalizationState,
    BattleActorRecoveryStateCode RecoveryState,
    string FailureCode);

public sealed record BattleActorCommandEnvelope(
    Guid BattleCommandId,
    Guid BattleId,
    Guid ParticipantId,
    long? CharacterId,
    string SessionSafeReference,
    long SessionEpoch,
    int RoundNumber,
    long CommandWindowVersion,
    int ActionSlot,
    BattleActorCommandType CommandType,
    int? SkillDefinitionIdCandidate,
    string? ItemReferenceCandidate,
    IReadOnlyList<Guid> TargetParticipantIdsCandidate,
    long? ClientSequenceCandidate,
    long ExpectedBattleVersion,
    string IdempotencyKey,
    string PayloadHash,
    BattleActorCommandSource Source,
    DateTimeOffset SubmittedAtUtc,
    string CorrelationId,
    string RawMetadata);

public sealed record BattleCommandResult(
    BattleActorResultCode Code,
    Guid BattleCommandId,
    Guid BattleId,
    Guid ParticipantId,
    string IdempotencySafeId,
    string PayloadHash,
    bool IsDuplicate,
    long BattleVersion,
    int RoundNumber,
    long CommandWindowVersion,
    string FailureCode);

public sealed record BattleActorParticipantSnapshot(
    Guid ParticipantId,
    long? CharacterId,
    string SessionSafeReference,
    long SessionEpoch,
    BattleParticipantType ParticipantType,
    BattleSide Side,
    int FormationSlot,
    BattleParticipantCombatState LifecycleState,
    long CurrentHp,
    long MaximumHp,
    long CurrentSp,
    long MaximumSp,
    bool IsConnected,
    bool IsDefeated,
    IReadOnlyList<string> StatusInstanceIds,
    IReadOnlyDictionary<int, int> SkillCooldowns,
    long RuntimeVersion,
    Guid? LastActionExecutionId);

public sealed record BattleRandomState(
    string AlgorithmVersion,
    ulong InitialSeed,
    ulong CurrentState,
    long DrawCount)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string CanonicalHash => BattleArchitectureV2Hash.Canonical(this);
}

public sealed record BattleActionOrderEntry(
    Guid ParticipantId,
    Guid CommandId,
    int ExecutionOrder,
    int ActionPriority,
    long? ExistingInitiativeCandidate,
    string PolicyStatus);

public sealed record BattleInitiativeSnapshot(
    Guid BattleId,
    int RoundNumber,
    IReadOnlyList<BattleActionOrderEntry> OrderedActions,
    string PolicyStatus,
    string EvidenceReference,
    string CanonicalHash);

public sealed record LockedBattleCommand(
    Guid BattleCommandId,
    Guid ParticipantId,
    long? CharacterId,
    int ActionSlot,
    BattleActorCommandType CommandType,
    int? SkillDefinitionId,
    string? ItemReference,
    IReadOnlyList<Guid> TargetParticipantIds,
    string IdempotencySafeId,
    string PayloadHash,
    DateTimeOffset SubmittedAtUtc,
    string CorrelationId);

public sealed record LockedRoundCommandSet(
    Guid CommandLockId,
    Guid BattleId,
    int RoundNumber,
    long CommandWindowVersion,
    IReadOnlyList<LockedBattleCommand> OrderedCommands,
    IReadOnlyList<Guid> MissingParticipantIds,
    long ExpectedBattleVersion,
    IReadOnlyDictionary<string, string> ContentVersionReferences,
    string CanonicalHash,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleActorActionPlan(
    Guid ActionExecutionId,
    int ExecutionOrder,
    Guid ParticipantId,
    long? CharacterId,
    Guid CommandId,
    BattleActorCommandType ActionType,
    int? SkillDefinitionId,
    IReadOnlyList<Guid> TargetParticipantIds,
    long ExpectedParticipantVersion,
    IReadOnlyDictionary<Guid, long> ExpectedTargetVersions,
    string CostPlanReference,
    string StatusRestrictionSnapshotReference,
    string PolicyStatus);

public sealed record RoundResolutionPlan(
    Guid RoundResolutionPlanId,
    Guid BattleId,
    int RoundNumber,
    Guid CommandLockId,
    long BattleVersionAtPlan,
    long RoundVersionAtPlan,
    IReadOnlyDictionary<Guid, long> ParticipantSnapshotVersions,
    LockedRoundCommandSet LockedCommands,
    BattleInitiativeSnapshot InitiativeSnapshot,
    IReadOnlyList<BattleActorActionPlan> OrderedActionPlans,
    string RngAlgorithmVersion,
    BattleRandomState RngStateBefore,
    BattleRandomState ExpectedRngStateAfterCandidate,
    string SkillContentVersion,
    string StatusContentVersion,
    string MonsterContentVersion,
    string FormulaVersion,
    IReadOnlyList<string> PolicyStatuses,
    string PlanHash,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleActionResolutionCursor(
    Guid BattleId,
    int RoundNumber,
    Guid RoundResolutionPlanId,
    int CurrentActionIndex,
    Guid? CurrentActionExecutionId,
    Guid? LastCommittedActionExecutionId,
    string CurrentSubEffectCursorReference,
    long BattleVersion,
    BattleRandomState RngState,
    BattleActorRecoveryStateCode RecoveryState,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleCommandWindowSnapshot(
    Guid BattleId,
    int RoundNumber,
    long CommandWindowVersion,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? DeadlineCandidate,
    IReadOnlyList<Guid> RequiredParticipantIds,
    IReadOnlyList<Guid> SubmittedParticipantIds,
    IReadOnlyList<Guid> MissingParticipantIds,
    string State,
    string PolicyStatus);

public sealed record BattleActorSnapshot(
    Guid BattleId,
    BattleEngineMode EngineMode,
    BattleActorLifecycleState LifecycleState,
    BattleActorStateCode BattleState,
    long BattleVersion,
    int RoundNumber,
    long RoundVersion,
    long CommandWindowVersion,
    string CurrentPhase,
    IReadOnlyList<BattleActorParticipantSnapshot> Participants,
    BattleCommandWindowSnapshot? CommandWindow,
    LockedRoundCommandSet? LockedCommands,
    RoundResolutionPlan? CurrentResolutionPlan,
    BattleActionResolutionCursor? ActionCursor,
    BattleRandomState RngState,
    long JournalSequence,
    long OutboxSequence,
    BattleFinalizationStateCode FinalizationState,
    BattleActorRecoveryStateCode RecoveryState,
    IReadOnlyDictionary<string, string> ContentVersions,
    string ServerFormulaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string CorrelationId,
    string CanonicalHash);

public sealed record BattleJournalEntry(
    Guid BattleId,
    long JournalSequence,
    BattleJournalEntryKind EntryType,
    long BattleVersion,
    int RoundNumber,
    long CommandWindowVersion,
    Guid? PlanId,
    Guid? ActionExecutionId,
    string ResultReference,
    string SafeIdempotencyIdentifier,
    int PayloadVersion,
    string CanonicalPayload,
    string PayloadHash,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleRoundCheckpoint(
    Guid CheckpointId,
    Guid BattleId,
    long CheckpointVersion,
    BattleActorSnapshot Snapshot,
    int PayloadVersion,
    string PayloadHash,
    bool IsValid,
    DateTimeOffset CreatedAtUtc);

public sealed record BattleEventEnvelope(
    Guid EventId,
    Guid BattleId,
    long EventSequence,
    BattleArchitectureEventKind EventType,
    int RoundNumber,
    Guid? ActionExecutionId,
    Guid? SourceParticipantId,
    IReadOnlyList<Guid> TargetParticipantIds,
    string SemanticPayload,
    int PayloadVersion,
    IReadOnlyList<BattleEventDeliveryCategory> DeliveryCategories,
    BattleOutboxDispatchState DispatchState,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleOutboxDelivery(
    Guid BattleId,
    long EventSequence,
    BattleEventDeliveryCategory Consumer,
    BattleOutboxDispatchState State,
    int RetryCount,
    string LastFailureCode,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleReconnectSnapshot(
    Guid BattleId,
    BattleActorStateCode BattleState,
    long BattleVersion,
    int RoundNumber,
    long RoundVersion,
    long CommandWindowVersion,
    string CurrentPhase,
    IReadOnlyList<BattleActorParticipantSnapshot> Participants,
    BattleCommandWindowSnapshot? CommandWindow,
    LockedRoundCommandSet? LockedCommands,
    BattleActionResolutionCursor? ActionCursor,
    long PendingEventSequence,
    long CommittedEventSequence,
    string RngPublicSafeReference,
    BattleFinalizationStateCode FinalizationState,
    BattleActorRecoveryStateCode RecoveryState,
    IReadOnlyDictionary<string, string> ContentVersionReferences,
    DateTimeOffset LastUpdatedAtUtc,
    string CanonicalHash);

public sealed record BattleFinalizationPlan(
    Guid FinalizationPlanId,
    Guid BattleId,
    BattleWinnerSide Outcome,
    IReadOnlyList<long> EligibleCharacterIds,
    IReadOnlyList<string> MonsterDefeatFacts,
    bool RewardEligible,
    IReadOnlyList<string> ExistingRewardPlanReferences,
    IReadOnlyList<QuestSemanticEvent> QuestSemanticEvents,
    string CharacterProgressionBoundary,
    string CaptureBoundary,
    long ExpectedBattleVersion,
    string IdempotencyKey,
    string PayloadHash,
    IReadOnlyDictionary<string, string> ContentVersions,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleFinalizationResult(
    BattleActorResultCode Code,
    Guid FinalizationPlanId,
    Guid BattleId,
    BattleFinalizationStateCode State,
    bool RewardCommitted,
    bool QuestEventsCommitted,
    bool WorldResumeCommitted,
    bool IsDuplicate,
    string FailureCode,
    DateTimeOffset CompletedAtUtc);

public sealed record BattleRecoveryPlan(
    Guid RecoveryPlanId,
    Guid BattleId,
    long ExpectedCheckpointVersion,
    long ExpectedJournalSequence,
    string PayloadHash,
    DateTimeOffset CreatedAtUtc,
    string CorrelationId);

public sealed record BattleRecoveryResult(
    BattleActorResultCode Code,
    Guid BattleId,
    BattleActorRecoveryStateCode State,
    long RestoredCheckpointVersion,
    long ReplayedJournalEntries,
    BattleActorStateCode ResumeState,
    bool IsDuplicate,
    string FailureCode);

public sealed record BattleReplayArtifact(
    int SchemaVersion,
    string FixtureId,
    BattleActorSnapshot InitialSnapshot,
    BattleStartPlan StartPlan,
    string ServerBuildVersion,
    string FormulaVersion,
    string RngAlgorithmVersion,
    BattleRandomState InitialRngState,
    IReadOnlyDictionary<string, string> ContentVersions,
    IReadOnlyList<LockedRoundCommandSet> LockedCommandSets,
    IReadOnlyList<BattleEventEnvelope> ExpectedOrderedEvents,
    string ExpectedFinalSnapshotHash,
    string ExpectedEventStreamHash);

public sealed record BattleReplayOutput(
    BattleActorSnapshot FinalSnapshot,
    IReadOnlyList<BattleEventEnvelope> OrderedEvents,
    string FinalSnapshotHash,
    string EventStreamHash);

public sealed record BattleReplayResult(
    BattleReplayResultCode Code,
    string FixtureId,
    string ExpectedFinalSnapshotHash,
    string ActualFinalSnapshotHash,
    string ExpectedEventStreamHash,
    string ActualEventStreamHash,
    long? FirstDivergenceSequence,
    string FailureCode,
    bool DatabaseMutation,
    bool RewardSideEffect,
    bool QuestSideEffect,
    int NetworkBytes);

public sealed record BattleDifferentialResult(
    BattleReplayResultCode Code,
    string FixtureId,
    string LegacyFinalHash,
    string ActorFinalHash,
    string LegacyEventHash,
    string ActorEventHash,
    long? FirstDivergenceSequence,
    string FirstDivergenceField,
    int DivergenceCount,
    bool ShadowExternalSideEffects,
    string FailureCode);

public sealed record BattleActorRegistryItem(
    Guid BattleId,
    BattleActorLifecycleState LifecycleState,
    BattleActorStateCode BattleState,
    long BattleVersion,
    int MailboxDepth,
    int MailboxCapacity,
    bool FinalizationTerminal,
    int PendingOutboxCount,
    string FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleActorInspectorQuery(
    Guid? BattleId = null,
    BattleEngineMode? EngineMode = null,
    BattleActorStateCode? BattleState = null,
    int? RoundNumber = null,
    bool ActiveOnly = false,
    bool CompletedOnly = false,
    bool RecoveryRequiredOnly = false,
    bool FaultedOnly = false,
    bool MailboxNearCapacityOnly = false,
    bool OutboxPendingOnly = false,
    bool FinalizationPendingOnly = false,
    int Offset = 0,
    int Limit = 100);

public sealed record BattleActorInspectorSnapshot(
    IReadOnlyList<BattleActorRegistryItem> Actors,
    IReadOnlyList<BattleJournalEntry> Journal,
    IReadOnlyList<BattleEventEnvelope> Outbox,
    IReadOnlyList<BattleDifferentialResult> Differentials,
    int TotalActors,
    string FailureCode);

public interface IBattleExecutionEngine
{
    BattleEngineMode Mode { get; }

    Task<BattleEngineCreateResult> CreateBattleAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifySessionDisconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifySessionReconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken);

    Task<BattleActorSnapshot?> RequestSnapshotAsync(
        Guid battleId,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> RequestAbortAsync(
        Guid battleId,
        string reason,
        CancellationToken cancellationToken);

    Task<BattleRecoveryResult> RecoverBattleAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> StopBattleAsync(
        Guid battleId,
        CancellationToken cancellationToken);

    Task<BattleEngineStatus?> GetBattleStatusAsync(
        Guid battleId,
        CancellationToken cancellationToken);
}

public interface IBattleExecutionEngineFactory
{
    IBattleExecutionEngine Create(BattleEngineMode mode);
}

public interface IBattleEngineSelector
{
    BattleEngineMode DefaultMode { get; }

    BattleActorResultCode ValidateMode(BattleEngineMode mode, out string failureCode);

    IBattleExecutionEngine Select(BattleEngineMode? requestedMode = null);
}

public interface IBattleActor
{
    Guid BattleId { get; }

    BattleActorLifecycleState LifecycleState { get; }

    int MailboxDepth { get; }

    int MailboxCapacity { get; }

    string FailureCode { get; }

    Task<BattleEngineOperationResult> StartAsync(CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifyDisconnectedAsync(
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> NotifyReconnectedAsync(
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken);

    Task<BattleActorSnapshot> RequestSnapshotAsync(CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> RequestAbortAsync(
        string reason,
        CancellationToken cancellationToken);

    Task<BattleRecoveryResult> RecoverAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> RequestFinalizationAsync(CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> StopAsync(CancellationToken cancellationToken);
}

public interface IBattleActorRegistry
{
    int ActiveCount { get; }

    bool IsAcceptingNewActors { get; }

    Task<(BattleActorResultCode Code, IBattleActor? Actor, bool IsDuplicate)> GetOrCreateAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken);

    bool TryGet(Guid battleId, out IBattleActor? actor);

    Task<BattleEngineOperationResult> StopAsync(Guid battleId, CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> DrainAsync(CancellationToken cancellationToken);

    IReadOnlyList<BattleActorRegistryItem> Snapshot();
}

public interface IBattleActorFactory
{
    IBattleActor Create(BattleStartPlan plan);
}

public interface IBattleLifecycleCoordinator
{
    Task<BattleEngineCreateResult> CreateAsync(BattleStartPlan plan, CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> StopAsync(Guid battleId, CancellationToken cancellationToken);

    Task<BattleEngineOperationResult> DrainAsync(CancellationToken cancellationToken);
}

public interface IBattleStateProjector
{
    BattleActorSnapshot Project(
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
        DateTimeOffset updatedAtUtc);
}

public interface IBattleDeadlineScheduler
{
    Task ScheduleAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        DateTimeOffset deadline,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken);
}

public interface IBattleTimeoutPolicy
{
    IReadOnlyList<Guid> FindMissingParticipants(BattleActorSnapshot snapshot);
}

public interface IBattleAutoCommandProvider
{
    BattleActorCommandEnvelope CreateDefault(
        BattleActorSnapshot snapshot,
        BattleActorParticipantSnapshot participant,
        DateTimeOffset now);
}

public interface IBattleInitiativePlanner
{
    BattleInitiativeSnapshot Plan(
        BattleInstance battle,
        LockedRoundCommandSet commands,
        DateTimeOffset now);
}

public interface IBattleDeterministicRandom
{
    BattleRandomState State { get; }

    ulong NextUInt64();

    int NextInt32(int exclusiveMaximum);
}

public interface IBattleRandomFactory
{
    IBattleDeterministicRandom Create(string algorithmVersion, ulong seed);

    IBattleDeterministicRandom Restore(BattleRandomState state);
}

public interface IBattleActorActionExecutor
{
    Task<BattleActionResult> ExecuteAsync(
        BattleInstance battle,
        BattleActorActionPlan actionPlan,
        CancellationToken cancellationToken);
}

public interface IBattleJournal
{
    Task<(BattleActorResultCode Code, BattleJournalEntry? Entry)> AppendAsync(
        BattleJournalEntry entry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BattleJournalEntry>> ReadAfterAsync(
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken);
}

public interface IBattleCheckpointStore
{
    Task<BattleActorResultCode> SaveCheckpointAsync(
        BattleRoundCheckpoint checkpoint,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<BattleRoundCheckpoint?> LoadLatestValidAsync(
        Guid battleId,
        CancellationToken cancellationToken);
}

public interface IBattleEventOutbox
{
    Task<(BattleActorResultCode Code, BattleEventEnvelope? Event)> AppendEventAsync(
        BattleEventEnvelope battleEvent,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> AcknowledgeAsync(
        Guid battleId,
        long eventSequence,
        BattleEventDeliveryCategory consumer,
        string payloadHash,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BattleEventEnvelope>> ReadPendingAsync(
        Guid battleId,
        CancellationToken cancellationToken);
}

public interface IBattleEventDispatcher
{
    Task<BattleActorResultCode> DispatchAsync(
        BattleEventEnvelope battleEvent,
        CancellationToken cancellationToken);
}

public interface IBattleActorDurabilityStore :
    IBattleJournal,
    IBattleCheckpointStore,
    IBattleEventOutbox,
    IBattleFinalizationStore
{
    Task<BattleCommandResult?> FindCommandAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> SaveCommandAsync(
        BattleActorCommandEnvelope command,
        BattleCommandResult result,
        CancellationToken cancellationToken);

    Task<LockedRoundCommandSet?> LoadCommandLockAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> SaveCommandLockAsync(
        LockedRoundCommandSet commandLock,
        CancellationToken cancellationToken);

    Task<RoundResolutionPlan?> LoadRoundPlanAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> SaveRoundPlanAsync(
        RoundResolutionPlan plan,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> SaveActionResultAsync(
        Guid battleId,
        int roundNumber,
        BattleActionResult result,
        CancellationToken cancellationToken);

    Task<BattleActionResult?> LoadActionResultAsync(
        Guid battleId,
        Guid actionExecutionId,
        CancellationToken cancellationToken);
}

public interface IBattleReconnectSnapshotProvider
{
    BattleReconnectSnapshot Create(BattleActorSnapshot snapshot);
}

public interface IBattleReplayExecutor
{
    Task<BattleReplayOutput> ExecuteAsync(
        BattleReplayArtifact artifact,
        CancellationToken cancellationToken);
}

public interface IBattleFinalizationCoordinator
{
    Task<BattleFinalizationResult> FinalizeAsync(
        BattleFinalizationPlan plan,
        CancellationToken cancellationToken);
}

public interface IBattleWorldResumeCoordinator
{
    Task<BattleActorResultCode> ResumeAsync(
        Guid battleId,
        IReadOnlyList<long> characterIds,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IBattleFinalizationPlanFactory
{
    BattleFinalizationPlan Create(BattleInstance battle, DateTimeOffset now);
}

public interface IBattleFinalizationStore
{
    Task<BattleFinalizationResult?> FindFinalizationAsync(
        Guid battleId,
        string payloadHash,
        CancellationToken cancellationToken);

    Task<BattleActorResultCode> SaveFinalizationAsync(
        BattleFinalizationPlan plan,
        BattleFinalizationResult result,
        CancellationToken cancellationToken);
}

public interface IBattleActorRecoveryCoordinator
{
    Task<BattleRecoveryResult> RecoverAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken);
}

public interface IGod2BattleProtocolAdapter
{
    BattleActorResultCode TryCreateSemanticCommand(
        ReadOnlyMemory<byte> packet,
        string clientBuild,
        out BattleActorCommandEnvelope? command,
        out string failureCode);

    BattleActorResultCode TrySerializeEvents(
        IReadOnlyList<BattleEventEnvelope> events,
        string clientBuild,
        out byte[] networkBytes,
        out string failureCode);
}

public interface IBattleActorInspectorSource
{
    BattleActorInspectorSnapshot Capture(BattleActorInspectorQuery query);
}

public static class BattleArchitectureV2Hash
{
    public static string Canonical<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            value,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string SafeId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    public static Guid StableGuid(params string[] parts)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\u001f", parts)));
        Span<byte> guid = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guid);
        return new Guid(guid);
    }
}
