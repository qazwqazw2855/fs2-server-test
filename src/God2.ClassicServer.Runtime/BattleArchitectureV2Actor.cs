using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace God2.ClassicServer.Runtime;

internal sealed record BattleActorMessage(
    BattleMessageKind Kind,
    BattleActorCommandEnvelope? Command,
    int RoundNumber,
    long CommandWindowVersion,
    string SessionSafeReference,
    long CharacterId,
    long SessionEpoch,
    string Reason,
    BattleRecoveryPlan? RecoveryPlan,
    TaskCompletionSource<object> Completion);

public sealed class BattleInstanceActor : IBattleActor
{
    private readonly BattleStartPlan _startPlan;
    private readonly BattleActorOptions _options;
    private readonly Channel<BattleActorMessage> _mailbox;
    private readonly IBattleActorDurabilityStore _store;
    private readonly BattleActorStateMachine _stateMachine;
    private readonly IBattleStateProjector _projector;
    private readonly IBattleInitiativePlanner _initiative;
    private readonly IBattleActorActionExecutor _actions;
    private readonly IBattleVictoryPolicy _victory;
    private readonly IBattleTimeoutPolicy _timeoutPolicy;
    private readonly IBattleAutoCommandProvider _autoCommands;
    private readonly IBattleFinalizationPlanFactory _finalizationPlans;
    private readonly IBattleFinalizationCoordinator _finalization;
    private readonly BattleArchitectureFailureInjection _failureInjection;
    private readonly IBattleDeterministicRandom _random;
    private readonly Dictionary<(Guid ParticipantId, int ActionSlot), BattleActorCommandEnvelope> _commands = [];
    private readonly Dictionary<Guid, long> _sessionEpochs = [];
    private readonly Dictionary<Guid, string> _sessionSafeReferences = [];
    private readonly Task _consumer;

    private BattleInstance _battle;
    private BattleActorStateCode _actorState = BattleActorStateCode.Created;
    private BattleActorLifecycleState _lifecycleState = BattleActorLifecycleState.Created;
    private long _commandWindowVersion;
    private long _commandWindowBaseBattleVersion;
    private long _journalSequence;
    private long _outboxSequence;
    private long _checkpointVersion;
    private LockedRoundCommandSet? _lockedCommands;
    private RoundResolutionPlan? _resolutionPlan;
    private BattleActionResolutionCursor? _cursor;
    private BattleFinalizationStateCode _finalizationState = BattleFinalizationStateCode.NotStarted;
    private BattleActorRecoveryStateCode _recoveryState = BattleActorRecoveryStateCode.NotRequired;
    private string _failureCode = "";
    private int _mailboxDepth;

    public BattleInstanceActor(
        BattleStartPlan startPlan,
        BattleActorOptions options,
        IBattleActorDurabilityStore store,
        BattleActorStateMachine stateMachine,
        IBattleStateProjector projector,
        IBattleInitiativePlanner initiative,
        IBattleRandomFactory randomFactory,
        IBattleActorActionExecutor actions,
        IBattleVictoryPolicy victory,
        IBattleTimeoutPolicy timeoutPolicy,
        IBattleAutoCommandProvider autoCommands,
        IBattleFinalizationPlanFactory finalizationPlans,
        IBattleFinalizationCoordinator finalization,
        BattleArchitectureFailureInjection? failureInjection = null)
    {
        _startPlan = startPlan;
        _options = options;
        _store = store;
        _stateMachine = stateMachine;
        _projector = projector;
        _initiative = initiative;
        _actions = actions;
        _victory = victory;
        _timeoutPolicy = timeoutPolicy;
        _autoCommands = autoCommands;
        _finalizationPlans = finalizationPlans;
        _finalization = finalization;
        _failureInjection = failureInjection ?? new BattleArchitectureFailureInjection();
        _battle = startPlan.PreparedBattle ??
            throw new ArgumentException("Actor engine requires an immutable prepared battle snapshot.", nameof(startPlan));
        _random = randomFactory.Create(startPlan.RngAlgorithmVersion, startPlan.RngSeed);
        foreach (var participant in _battle.Participants)
        {
            _sessionEpochs[participant.ParticipantId] = 1;
            _sessionSafeReferences[participant.ParticipantId] =
                BattleArchitectureV2Hash.SafeId(participant.SessionId ?? $"internal:{participant.ParticipantId:N}");
        }

        _mailbox = Channel.CreateBounded<BattleActorMessage>(
            new BoundedChannelOptions(options.MailboxCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _consumer = ProcessMailboxAsync();
    }

    public Guid BattleId => _battle.BattleInstanceId;

    public BattleActorLifecycleState LifecycleState => _lifecycleState;

    public int MailboxDepth => Math.Max(0, Volatile.Read(ref _mailboxDepth));

    public int MailboxCapacity => _options.MailboxCapacity;

    public string FailureCode => _failureCode;

    public Task<BattleEngineOperationResult> StartAsync(CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.Start),
            cancellationToken,
            UnavailableResult);

    public Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.SubmitCommand) with { Command = command },
            cancellationToken,
            UnavailableResult);

    public Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.CommandWindowExpired) with
            {
                RoundNumber = roundNumber,
                CommandWindowVersion = commandWindowVersion,
                Reason = correlationId
            },
            cancellationToken,
            UnavailableResult);

    public Task<BattleEngineOperationResult> NotifyDisconnectedAsync(
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.SessionDisconnected) with
            {
                SessionSafeReference = sessionSafeReference,
                CharacterId = characterId
            },
            cancellationToken,
            UnavailableResult);

    public Task<BattleEngineOperationResult> NotifyReconnectedAsync(
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.SessionReconnected) with
            {
                SessionSafeReference = sessionSafeReference,
                CharacterId = characterId,
                SessionEpoch = newSessionEpoch
            },
            cancellationToken,
            UnavailableResult);

    public Task<BattleActorSnapshot> RequestSnapshotAsync(CancellationToken cancellationToken) =>
        PostAsync(
            Message(BattleMessageKind.SnapshotRequested),
            cancellationToken,
            ProjectSnapshot);

    public Task<BattleEngineOperationResult> RequestAbortAsync(
        string reason,
        CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.AbortRequested) with { Reason = reason },
            cancellationToken,
            UnavailableResult);

    public Task<BattleRecoveryResult> RecoverAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken) =>
        PostAsync(
            Message(BattleMessageKind.RecoveryRequested) with { RecoveryPlan = plan },
            cancellationToken,
            () => new BattleRecoveryResult(
                BattleActorResultCode.BattleUnavailable,
                BattleId,
                _recoveryState,
                0,
                0,
                _actorState,
                false,
                "battle.actor.unavailable"));

    public Task<BattleEngineOperationResult> RequestFinalizationAsync(CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.FinalizationRequested),
            cancellationToken,
            UnavailableResult);

    public Task<BattleEngineOperationResult> StopAsync(CancellationToken cancellationToken) =>
        PostAsync<BattleEngineOperationResult>(
            Message(BattleMessageKind.ShutdownRequested),
            cancellationToken,
            UnavailableResult);

    private BattleActorMessage Message(BattleMessageKind kind) =>
        new(
            kind,
            null,
            0,
            0,
            "",
            0,
            0,
            "",
            null,
            new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));

    private async Task<T> PostAsync<T>(
        BattleActorMessage message,
        CancellationToken cancellationToken,
        Func<T> unavailable)
    {
        if (_lifecycleState is BattleActorLifecycleState.Stopped or BattleActorLifecycleState.Faulted)
        {
            return unavailable();
        }

        if (!_mailbox.Writer.TryWrite(message))
        {
            return typeof(T) == typeof(BattleEngineOperationResult)
                ? (T)(object)new BattleEngineOperationResult(
                    BattleActorResultCode.MailboxBackpressure,
                    BattleId,
                    "battle.actor.mailbox_full",
                    false,
                    _battle.BattleVersion,
                    _battle.CurrentRoundNumber,
                    _commandWindowVersion,
                    [])
                : unavailable();
        }

        Interlocked.Increment(ref _mailboxDepth);
        try
        {
            var value = await message.Completion.Task.WaitAsync(cancellationToken);
            return (T)value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task ProcessMailboxAsync()
    {
        try
        {
            await foreach (var message in _mailbox.Reader.ReadAllAsync())
            {
                Interlocked.Decrement(ref _mailboxDepth);
                try
                {
                    var result = await ProcessMessageAsync(message);
                    message.Completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    _failureCode = $"battle.actor.unhandled:{exception.GetType().Name}";
                    _actorState = BattleActorStateCode.Faulted;
                    _lifecycleState = BattleActorLifecycleState.Faulted;
                    message.Completion.TrySetResult(UnavailableResult() with
                    {
                        Code = BattleActorResultCode.InternalFailure,
                        FailureCode = _failureCode
                    });
                }
            }
        }
        finally
        {
            _lifecycleState = _lifecycleState == BattleActorLifecycleState.Faulted
                ? BattleActorLifecycleState.Faulted
                : BattleActorLifecycleState.Stopped;
            while (_mailbox.Reader.TryRead(out var pending))
            {
                Interlocked.Decrement(ref _mailboxDepth);
                pending.Completion.TrySetResult(UnavailableResult());
            }
        }
    }

    private async Task<object> ProcessMessageAsync(BattleActorMessage message)
    {
        if (_lifecycleState is BattleActorLifecycleState.Stopped or BattleActorLifecycleState.Faulted &&
            message.Kind is not BattleMessageKind.SnapshotRequested)
        {
            return UnavailableResult();
        }

        return message.Kind switch
        {
            BattleMessageKind.Start => await HandleStartAsync(),
            BattleMessageKind.SubmitCommand when message.Command is not null =>
                await HandleCommandAsync(message.Command, allowAutomaticLock: true),
            BattleMessageKind.CommandWindowExpired =>
                await HandleWindowExpiredAsync(message.RoundNumber, message.CommandWindowVersion, message.Reason),
            BattleMessageKind.SessionDisconnected =>
                await HandleDisconnectAsync(message.SessionSafeReference, message.CharacterId),
            BattleMessageKind.SessionReconnected =>
                await HandleReconnectAsync(message.SessionSafeReference, message.CharacterId, message.SessionEpoch),
            BattleMessageKind.SnapshotRequested => ProjectSnapshot(),
            BattleMessageKind.AbortRequested => await HandleAbortAsync(message.Reason),
            BattleMessageKind.RecoveryRequested when message.RecoveryPlan is not null =>
                await HandleRecoveryAsync(message.RecoveryPlan),
            BattleMessageKind.FinalizationRequested => await HandleFinalizationAsync(),
            BattleMessageKind.InternalContinueResolution => await ContinueResolutionAsync(),
            BattleMessageKind.ShutdownRequested => await HandleShutdownAsync(),
            _ => UnavailableResult() with
            {
                Code = BattleActorResultCode.BattleUnavailable,
                FailureCode = "battle.actor.unknown_message"
            }
        };
    }

    private async Task<BattleEngineOperationResult> HandleStartAsync()
    {
        if (_lifecycleState == BattleActorLifecycleState.Running)
        {
            return Success(isDuplicate: true);
        }

        if (_failureInjection.Point == BattleArchitectureFailurePoint.ActorCreation)
        {
            return Failure(BattleActorResultCode.InternalFailure, "battle.actor.creation_injected");
        }

        _lifecycleState = BattleActorLifecycleState.Running;
        var preparing = await TransitionAsync(BattleActorStateCode.Preparing, BattleJournalEntryKind.BattlePrepared);
        if (!preparing.Succeeded)
        {
            return preparing;
        }

        var waiting = await TransitionAsync(
            BattleActorStateCode.WaitingForClientReady,
            BattleJournalEntryKind.ClientReadyBoundaryReached);
        if (!waiting.Succeeded)
        {
            return waiting;
        }

        if (!_startPlan.TestOnly)
        {
            return waiting;
        }

        var opening = await TransitionAsync(BattleActorStateCode.RoundOpening, BattleJournalEntryKind.RoundOpened);
        if (!opening.Succeeded)
        {
            return opening;
        }

        OpenCommandWindow();
        var collecting = await TransitionAsync(
            BattleActorStateCode.CollectingCommands,
            BattleJournalEntryKind.RoundOpened);
        if (!collecting.Succeeded)
        {
            return collecting;
        }

        var checkpoint = await SaveCheckpointAsync();
        return checkpoint == BattleActorResultCode.Success
            ? collecting
            : Failure(BattleActorResultCode.PersistenceFailure, "battle.actor.start_checkpoint_failed");
    }

    private async Task<BattleEngineOperationResult> HandleCommandAsync(
        BattleActorCommandEnvelope command,
        bool allowAutomaticLock)
    {
        var validation = ValidateCommand(command);
        if (validation.Code != BattleActorResultCode.Success)
        {
            await AppendJournalAsync(
                BattleJournalEntryKind.CommandRejected,
                command.BattleCommandId,
                command.IdempotencyKey,
                validation.FailureCode);
            return validation;
        }

        var replay = await _store.FindCommandAsync(
            command.IdempotencyKey,
            command.PayloadHash,
            CancellationToken.None);
        if (replay is not null)
        {
            return replay.Code == BattleActorResultCode.ReplayConflict
                ? Failure(BattleActorResultCode.ReplayConflict, replay.FailureCode)
                : Success(isDuplicate: true);
        }

        var key = (command.ParticipantId, command.ActionSlot);
        if (_commands.TryGetValue(key, out var existing))
        {
            return string.Equals(existing.PayloadHash, command.PayloadHash, StringComparison.Ordinal)
                ? Success(isDuplicate: true)
                : Failure(BattleActorResultCode.ReplayConflict, "battle.actor.action_slot_conflict");
        }

        if (_failureInjection.Point == BattleArchitectureFailurePoint.CommandPersistence)
        {
            return Failure(BattleActorResultCode.PersistenceFailure, "battle.actor.command_persistence_injected");
        }

        var result = new BattleCommandResult(
            BattleActorResultCode.Accepted,
            command.BattleCommandId,
            command.BattleId,
            command.ParticipantId,
            BattleArchitectureV2Hash.SafeId(command.IdempotencyKey),
            command.PayloadHash,
            false,
            _battle.BattleVersion + 1,
            command.RoundNumber,
            command.CommandWindowVersion,
            "");
        var saved = await _store.SaveCommandAsync(command, result, CancellationToken.None);
        if (saved is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
        {
            return Failure(saved, "battle.actor.command_persistence_failed");
        }

        _commands[key] = command;
        var legacyAction = new BattleSubmittedAction(
            command.BattleCommandId,
            command.BattleCommandId,
            command.BattleId,
            command.RoundNumber,
            command.ParticipantId,
            ToLegacy(command.CommandType),
            ResolveAuthoritativeTargets(command.ParticipantId),
            command.SkillDefinitionIdCandidate,
            command.ItemReferenceCandidate,
            BattleArchitectureV2Hash.SafeId(command.IdempotencyKey),
            command.PayloadHash,
            BattleActionState.Submitted,
            command.SubmittedAtUtc,
            command.CorrelationId);
        var actions = _battle.CurrentRound.SubmittedActions
            .Append(legacyAction)
            .OrderBy(value => value.ParticipantId)
            .ToArray();
        _battle = _battle with
        {
            CurrentRound = _battle.CurrentRound with
            {
                SubmittedActions = BattleCollections.Freeze(actions),
                RoundVersion = _battle.CurrentRound.RoundVersion + 1
            },
            Participants = BattleCollections.Freeze(_battle.Participants.Select(participant =>
                participant.ParticipantId == command.ParticipantId
                    ? participant with
                    {
                        HasSubmittedAction = true,
                        CurrentActionState = BattleActionState.Submitted,
                        CombatState = BattleParticipantCombatState.ActionSubmitted,
                        RuntimeVersion = participant.RuntimeVersion + 1
                    }
                    : participant)),
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = command.SubmittedAtUtc
        };
        await AppendJournalAsync(
            BattleJournalEntryKind.CommandAccepted,
            command.BattleCommandId,
            command.IdempotencyKey,
            "accepted");
        await AppendOutboxAsync(
            BattleArchitectureEventKind.CommandAccepted,
            command.BattleCommandId,
            command.ParticipantId,
            ResolveAuthoritativeTargets(command.ParticipantId),
            BattleEventDeliveryCategory.Administration,
            command.CorrelationId);

        if (allowAutomaticLock && RequiredParticipantIds().All(id =>
                _commands.Keys.Any(keyValue => keyValue.ParticipantId == id)))
        {
            var locked = await LockPlanAndResolveAsync();
            if (!locked.Succeeded)
            {
                return locked;
            }
        }

        return Success() with
        {
            Code = BattleActorResultCode.Accepted,
            BattleVersion = _battle.BattleVersion
        };
    }

    private BattleEngineOperationResult ValidateCommand(BattleActorCommandEnvelope command)
    {
        if (command.BattleId != BattleId)
        {
            return Failure(BattleActorResultCode.BattleNotFound, "battle.actor.wrong_battle");
        }

        if (!_stateMachine.AcceptsGameplay(_actorState))
        {
            return _actorState switch
            {
                BattleActorStateCode.Completed => Failure(BattleActorResultCode.BattleCompleted, "battle.actor.completed"),
                BattleActorStateCode.Aborted => Failure(BattleActorResultCode.BattleAborted, "battle.actor.aborted"),
                BattleActorStateCode.RecoveryRequired => Failure(BattleActorResultCode.RecoveryRequired, "battle.actor.recovery_required"),
                BattleActorStateCode.LockingCommands or
                BattleActorStateCode.PlanningResolution or
                BattleActorStateCode.ResolvingActions =>
                    Failure(BattleActorResultCode.ActionAlreadyLocked, "battle.actor.command_locked"),
                _ => Failure(BattleActorResultCode.InvalidBattleState, "battle.actor.not_collecting")
            };
        }

        var participant = _battle.Participants.SingleOrDefault(value => value.ParticipantId == command.ParticipantId);
        if (participant is null)
        {
            return Failure(BattleActorResultCode.ParticipantNotFound, "battle.actor.participant_missing");
        }

        if (participant.CharacterId != command.CharacterId)
        {
            return Failure(BattleActorResultCode.OwnershipMismatch, "battle.actor.character_mismatch");
        }

        if (!_sessionEpochs.TryGetValue(command.ParticipantId, out var epoch) ||
            epoch != command.SessionEpoch ||
            !_sessionSafeReferences.TryGetValue(command.ParticipantId, out var safeSession) ||
            !string.Equals(safeSession, command.SessionSafeReference, StringComparison.Ordinal))
        {
            return Failure(BattleActorResultCode.InvalidSession, "battle.actor.session_epoch_mismatch");
        }

        if (!participant.IsAlive)
        {
            return Failure(BattleActorResultCode.ParticipantDefeated, "battle.actor.participant_defeated");
        }

        if (command.RoundNumber < _battle.CurrentRoundNumber)
        {
            return Failure(BattleActorResultCode.StaleRound, "battle.actor.stale_round");
        }

        if (command.RoundNumber > _battle.CurrentRoundNumber)
        {
            return Failure(BattleActorResultCode.FutureRound, "battle.actor.future_round");
        }

        if (command.CommandWindowVersion != _commandWindowVersion)
        {
            return Failure(BattleActorResultCode.StaleCommandWindow, "battle.actor.stale_command_window");
        }

        if (command.ActionSlot != 0)
        {
            return Failure(BattleActorResultCode.InvalidActionSlot, "battle.actor.invalid_action_slot");
        }

        if (command.ExpectedBattleVersion != _commandWindowBaseBattleVersion &&
            command.ExpectedBattleVersion != _battle.BattleVersion)
        {
            return Failure(BattleActorResultCode.VersionConflict, "battle.actor.version_conflict");
        }

        if (!Supported(command.CommandType))
        {
            return command.CommandType is BattleActorCommandType.Item or
                BattleActorCommandType.Flee or
                BattleActorCommandType.Defend
                ? Failure(BattleActorResultCode.UnsupportedAction, "battle.actor.unsupported_action")
                : Failure(BattleActorResultCode.EvidenceBlocked, "battle.actor.action_evidence_blocked");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.PayloadHash))
        {
            return Failure(BattleActorResultCode.Rejected, "battle.actor.command_identity_missing");
        }

        return Success();
    }

    private async Task<BattleEngineOperationResult> HandleWindowExpiredAsync(
        int roundNumber,
        long commandWindowVersion,
        string correlationId)
    {
        if (_actorState != BattleActorStateCode.CollectingCommands)
        {
            return Failure(BattleActorResultCode.InvalidBattleState, "battle.actor.timeout_not_collecting");
        }

        if (roundNumber != _battle.CurrentRoundNumber ||
            commandWindowVersion != _commandWindowVersion)
        {
            return Failure(BattleActorResultCode.StaleCommandWindow, "battle.actor.stale_timeout");
        }

        await AppendJournalAsync(
            BattleJournalEntryKind.CommandWindowExpired,
            null,
            $"timeout:{BattleId:N}:{roundNumber}:{commandWindowVersion}",
            "expired");
        var snapshot = ProjectSnapshot();
        foreach (var participantId in _timeoutPolicy.FindMissingParticipants(snapshot))
        {
            var participant = snapshot.Participants.Single(value => value.ParticipantId == participantId);
            var command = _autoCommands.CreateDefault(snapshot, participant, DateTimeOffset.UtcNow);
            var accepted = await HandleCommandAsync(command, allowAutomaticLock: false);
            if (!accepted.Succeeded)
            {
                return accepted;
            }
        }

        return await LockPlanAndResolveAsync();
    }

    private async Task<BattleEngineOperationResult> LockPlanAndResolveAsync()
    {
        var locking = await TransitionAsync(
            BattleActorStateCode.LockingCommands,
            BattleJournalEntryKind.CommandLocked);
        if (!locking.Succeeded)
        {
            return locking;
        }

        if (_failureInjection.Point == BattleArchitectureFailurePoint.CommandLockPersistence)
        {
            return await EnterRecoveryRequiredAsync("battle.actor.command_lock_persistence_injected");
        }

        var ordered = _commands.Values
            .OrderBy(value => value.ParticipantId)
            .ThenBy(value => value.ActionSlot)
            .Select(value => new LockedBattleCommand(
                value.BattleCommandId,
                value.ParticipantId,
                value.CharacterId,
                value.ActionSlot,
                value.CommandType,
                value.SkillDefinitionIdCandidate,
                value.ItemReferenceCandidate,
                ResolveAuthoritativeTargets(value.ParticipantId),
                BattleArchitectureV2Hash.SafeId(value.IdempotencyKey),
                value.PayloadHash,
                value.SubmittedAtUtc,
                value.CorrelationId))
            .ToArray();
        var lockCandidate = new LockedRoundCommandSet(
            BattleArchitectureV2Hash.StableGuid(BattleId.ToString("N"), _battle.CurrentRoundNumber.ToString(), "lock"),
            BattleId,
            _battle.CurrentRoundNumber,
            _commandWindowVersion,
            Array.AsReadOnly(ordered),
            Array.AsReadOnly(Array.Empty<Guid>()),
            _battle.BattleVersion,
            ContentVersions(),
            "",
            DateTimeOffset.UtcNow,
            _battle.CorrelationId);
        _lockedCommands = lockCandidate with
        {
            CanonicalHash = BattleArchitectureV2Hash.Canonical(lockCandidate)
        };
        var lockSave = await _store.SaveCommandLockAsync(_lockedCommands, CancellationToken.None);
        if (lockSave is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
        {
            return await EnterRecoveryRequiredAsync("battle.actor.command_lock_persistence_failed");
        }

        var planning = await TransitionAsync(
            BattleActorStateCode.PlanningResolution,
            BattleJournalEntryKind.RoundPlanCreated);
        if (!planning.Succeeded)
        {
            return planning;
        }

        if (_failureInjection.Point == BattleArchitectureFailurePoint.RoundPlanPersistence)
        {
            return await EnterRecoveryRequiredAsync("battle.actor.round_plan_persistence_injected");
        }

        var initiative = _initiative.Plan(_battle, _lockedCommands, DateTimeOffset.UtcNow);
        var actionPlans = initiative.OrderedActions.Select(order =>
        {
            var command = _lockedCommands.OrderedCommands.Single(value => value.BattleCommandId == order.CommandId);
            var participant = _battle.Participants.Single(value => value.ParticipantId == command.ParticipantId);
            var targets = ResolveAuthoritativeTargets(command.ParticipantId);
            var versions = new ReadOnlyDictionary<Guid, long>(
                targets.ToDictionary(
                    target => target,
                    target => _battle.Participants.Single(value => value.ParticipantId == target).RuntimeVersion));
            return new BattleActorActionPlan(
                BattleArchitectureV2Hash.StableGuid(
                    BattleId.ToString("N"),
                    _battle.CurrentRoundNumber.ToString(),
                    order.ExecutionOrder.ToString(),
                    command.BattleCommandId.ToString("N")),
                order.ExecutionOrder,
                command.ParticipantId,
                command.CharacterId,
                command.BattleCommandId,
                command.CommandType,
                command.SkillDefinitionId,
                targets,
                participant.RuntimeVersion,
                versions,
                $"existing-skill-cost:{command.BattleCommandId:N}",
                $"existing-status-restriction:{command.ParticipantId:N}",
                order.PolicyStatus);
        }).ToArray();
        var participantVersions = new ReadOnlyDictionary<Guid, long>(
            _battle.Participants
                .OrderBy(value => value.ParticipantId)
                .ToDictionary(value => value.ParticipantId, value => value.RuntimeVersion));
        var planCandidate = new RoundResolutionPlan(
            BattleArchitectureV2Hash.StableGuid(BattleId.ToString("N"), _battle.CurrentRoundNumber.ToString(), "plan"),
            BattleId,
            _battle.CurrentRoundNumber,
            _lockedCommands.CommandLockId,
            _battle.BattleVersion,
            _battle.CurrentRound.RoundVersion,
            participantVersions,
            _lockedCommands,
            initiative,
            Array.AsReadOnly(actionPlans),
            _random.State.AlgorithmVersion,
            _random.State,
            _random.State,
            _startPlan.SkillContentVersion,
            _startPlan.StatusContentVersion,
            _startPlan.MonsterContentVersion,
            _startPlan.FormulaVersion,
            Array.AsReadOnly(new[] { initiative.PolicyStatus }),
            "",
            DateTimeOffset.UtcNow,
            _battle.CorrelationId);
        _resolutionPlan = planCandidate with { PlanHash = BattleArchitectureV2Hash.Canonical(planCandidate) };
        var planSave = await _store.SaveRoundPlanAsync(_resolutionPlan, CancellationToken.None);
        if (planSave is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
        {
            return await EnterRecoveryRequiredAsync("battle.actor.round_plan_persistence_failed");
        }

        _cursor = new BattleActionResolutionCursor(
            BattleId,
            _battle.CurrentRoundNumber,
            _resolutionPlan.RoundResolutionPlanId,
            0,
            actionPlans.FirstOrDefault()?.ActionExecutionId,
            null,
            "",
            _battle.BattleVersion,
            _random.State,
            _recoveryState,
            DateTimeOffset.UtcNow);
        var resolving = await TransitionAsync(
            BattleActorStateCode.ResolvingActions,
            BattleJournalEntryKind.ActionStarted);
        if (!resolving.Succeeded)
        {
            return resolving;
        }

        if (await SaveCheckpointAsync() != BattleActorResultCode.Success)
        {
            return await EnterRecoveryRequiredAsync("battle.actor.plan_checkpoint_failed");
        }

        return await ContinueResolutionAsync();
    }

    private async Task<BattleEngineOperationResult> ContinueResolutionAsync()
    {
        if (_actorState != BattleActorStateCode.ResolvingActions ||
            _resolutionPlan is null ||
            _cursor is null)
        {
            return Failure(BattleActorResultCode.InvalidBattleState, "battle.actor.resolution_not_ready");
        }

        for (var index = _cursor.CurrentActionIndex; index < _resolutionPlan.OrderedActionPlans.Count; index++)
        {
            var actionPlan = _resolutionPlan.OrderedActionPlans[index];
            var persisted = await _store.LoadActionResultAsync(
                BattleId,
                actionPlan.ActionExecutionId,
                CancellationToken.None);
            BattleActionResult result;
            if (persisted is not null)
            {
                result = persisted;
            }
            else
            {
                if (_failureInjection.Point == BattleArchitectureFailurePoint.ActionResolver)
                {
                    return await EnterRecoveryRequiredAsync("battle.actor.action_resolver_injected");
                }

                await AppendJournalAsync(
                    BattleJournalEntryKind.ActionStarted,
                    actionPlan.ActionExecutionId,
                    actionPlan.CommandId.ToString("N"),
                    "started");
                result = await _actions.ExecuteAsync(_battle, actionPlan, CancellationToken.None);
                if (result.Result is BattleActionResolutionCode.RecoveryRequired or
                    BattleActionResolutionCode.CombatFailure or
                    BattleActionResolutionCode.VersionConflict)
                {
                    return await EnterRecoveryRequiredAsync(
                        string.IsNullOrWhiteSpace(result.FailureCode)
                            ? "battle.actor.action_resolution_failed"
                            : result.FailureCode);
                }

                if (_failureInjection.Point == BattleArchitectureFailurePoint.ActionResultPersistence)
                {
                    return await EnterRecoveryRequiredAsync("battle.actor.action_result_persistence_injected");
                }

                var save = await _store.SaveActionResultAsync(
                    BattleId,
                    _battle.CurrentRoundNumber,
                    result,
                    CancellationToken.None);
                if (save is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
                {
                    return await EnterRecoveryRequiredAsync("battle.actor.action_result_persistence_failed");
                }
            }

            ApplyActionResult(result);
            _cursor = _cursor with
            {
                CurrentActionIndex = index + 1,
                CurrentActionExecutionId = index + 1 < _resolutionPlan.OrderedActionPlans.Count
                    ? _resolutionPlan.OrderedActionPlans[index + 1].ActionExecutionId
                    : null,
                LastCommittedActionExecutionId = actionPlan.ActionExecutionId,
                BattleVersion = _battle.BattleVersion,
                RngState = _random.State,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await AppendJournalAsync(
                BattleJournalEntryKind.ActionResultCommitted,
                actionPlan.ActionExecutionId,
                actionPlan.CommandId.ToString("N"),
                result.Result.ToString());
            await AppendActionEventsAsync(result, actionPlan);
            if (_failureInjection.Point == BattleArchitectureFailurePoint.CursorPersistence)
            {
                return await EnterRecoveryRequiredAsync("battle.actor.cursor_persistence_injected");
            }
        }

        var closing = await TransitionAsync(
            BattleActorStateCode.RoundClosing,
            BattleJournalEntryKind.RoundClosingStarted);
        if (!closing.Succeeded)
        {
            return closing;
        }

        var evaluating = await TransitionAsync(
            BattleActorStateCode.EvaluatingCompletion,
            BattleJournalEntryKind.RoundCompleted);
        if (!evaluating.Succeeded)
        {
            return evaluating;
        }

        var victory = _victory.Evaluate(_battle.Participants);
        if (victory.IsComplete)
        {
            _battle = _battle with
            {
                WinnerSide = victory.WinnerSide,
                CompletionReason = victory.CompletionReason,
                CurrentPhase = BattlePhase.VictoryResolution,
                BattleVersion = _battle.BattleVersion + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return await HandleFinalizationAsync();
        }

        CompleteRoundAndOpenNext();
        var opening = await TransitionAsync(
            BattleActorStateCode.RoundOpening,
            BattleJournalEntryKind.RoundOpened);
        if (!opening.Succeeded)
        {
            return opening;
        }

        OpenCommandWindow();
        return await TransitionAsync(
            BattleActorStateCode.CollectingCommands,
            BattleJournalEntryKind.RoundOpened);
    }

    private async Task<BattleEngineOperationResult> HandleFinalizationAsync()
    {
        if (_actorState == BattleActorStateCode.Completed)
        {
            return Success(isDuplicate: true);
        }

        if (_actorState != BattleActorStateCode.Finalizing)
        {
            var transition = await TransitionAsync(
                BattleActorStateCode.Finalizing,
                BattleJournalEntryKind.FinalizationStarted);
            if (!transition.Succeeded)
            {
                return transition;
            }
        }

        _finalizationState = BattleFinalizationStateCode.Planned;
        var plan = _finalizationPlans.Create(_battle, DateTimeOffset.UtcNow);
        var result = await _finalization.FinalizeAsync(plan, CancellationToken.None);
        _finalizationState = result.State;
        if (result.Code is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted) ||
            result.State != BattleFinalizationStateCode.Completed)
        {
            return await EnterRecoveryRequiredAsync(
                string.IsNullOrWhiteSpace(result.FailureCode)
                    ? "battle.actor.finalization_failed"
                    : result.FailureCode);
        }

        await AppendJournalAsync(
            BattleJournalEntryKind.FinalizationCommitted,
            plan.FinalizationPlanId,
            plan.IdempotencyKey,
            "committed");
        await AppendOutboxAsync(
            BattleArchitectureEventKind.BattleCompleted,
            plan.FinalizationPlanId,
            null,
            Array.Empty<Guid>(),
            BattleEventDeliveryCategory.Administration,
            plan.CorrelationId);
        _battle = _battle with
        {
            State = BattleState.Completed,
            CurrentPhase = BattlePhase.Completed,
            RewardState = result.RewardCommitted ? BattleRewardState.Committed : BattleRewardState.NotRequired,
            CompletedAtUtc = result.CompletedAtUtc,
            UpdatedAtUtc = result.CompletedAtUtc,
            BattleVersion = _battle.BattleVersion + 1
        };
        var completed = await TransitionAsync(
            BattleActorStateCode.Completed,
            BattleJournalEntryKind.BattleCompleted);
        if (!completed.Succeeded)
        {
            return completed;
        }

        await SaveCheckpointAsync();
        return completed;
    }

    private async Task<BattleEngineOperationResult> HandleDisconnectAsync(
        string sessionSafeReference,
        long characterId)
    {
        var participant = _battle.Participants.SingleOrDefault(value => value.CharacterId == characterId);
        if (participant is null ||
            !_sessionSafeReferences.TryGetValue(participant.ParticipantId, out var safe) ||
            !string.Equals(safe, sessionSafeReference, StringComparison.Ordinal))
        {
            return Failure(BattleActorResultCode.OwnershipMismatch, "battle.actor.disconnect_ownership");
        }

        _battle = _battle with
        {
            Participants = BattleCollections.Freeze(_battle.Participants.Select(value =>
                value.ParticipantId == participant.ParticipantId
                    ? value with
                    {
                        IsConnected = false,
                        CombatState = value.IsAlive
                            ? BattleParticipantCombatState.Disconnected
                            : value.CombatState,
                        RuntimeVersion = value.RuntimeVersion + 1
                    }
                    : value)),
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await AppendJournalAsync(
            BattleJournalEntryKind.SessionDisconnected,
            participant.ParticipantId,
            sessionSafeReference,
            "disconnected");
        return Success();
    }

    private async Task<BattleEngineOperationResult> HandleReconnectAsync(
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch)
    {
        var participant = _battle.Participants.SingleOrDefault(value => value.CharacterId == characterId);
        if (participant is null || newSessionEpoch <= _sessionEpochs.GetValueOrDefault(participant.ParticipantId))
        {
            return Failure(BattleActorResultCode.InvalidSession, "battle.actor.reconnect_epoch_invalid");
        }

        _sessionEpochs[participant.ParticipantId] = newSessionEpoch;
        _sessionSafeReferences[participant.ParticipantId] = sessionSafeReference;
        _battle = _battle with
        {
            Participants = BattleCollections.Freeze(_battle.Participants.Select(value =>
                value.ParticipantId == participant.ParticipantId
                    ? value with
                    {
                        IsConnected = true,
                        SessionId = sessionSafeReference,
                        CombatState = value.IsAlive
                            ? BattleParticipantCombatState.WaitingForAction
                            : value.CombatState,
                        RuntimeVersion = value.RuntimeVersion + 1
                    }
                    : value)),
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await AppendJournalAsync(
            BattleJournalEntryKind.SessionReconnected,
            participant.ParticipantId,
            sessionSafeReference,
            "reconnected");
        return Success();
    }

    private async Task<BattleEngineOperationResult> HandleAbortAsync(string reason)
    {
        if (_actorState == BattleActorStateCode.Aborted)
        {
            return Success(isDuplicate: true);
        }

        if (_actorState == BattleActorStateCode.Completed)
        {
            return Failure(BattleActorResultCode.BattleCompleted, "battle.actor.completed");
        }

        var aborting = await TransitionAsync(
            BattleActorStateCode.Aborting,
            BattleJournalEntryKind.BattleAborted);
        if (!aborting.Succeeded)
        {
            return aborting;
        }

        _battle = _battle with
        {
            State = BattleState.Aborted,
            CurrentPhase = BattlePhase.Aborted,
            CompletionReason = BattleCompletionReason.AdministrativeAbort,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            BattleVersion = _battle.BattleVersion + 1
        };
        await AppendOutboxAsync(
            BattleArchitectureEventKind.BattleAborted,
            null,
            null,
            Array.Empty<Guid>(),
            BattleEventDeliveryCategory.Administration,
            reason);
        return await TransitionAsync(
            BattleActorStateCode.Aborted,
            BattleJournalEntryKind.BattleAborted);
    }

    private async Task<BattleRecoveryResult> HandleRecoveryAsync(BattleRecoveryPlan plan)
    {
        if (plan.BattleId != BattleId)
        {
            return new BattleRecoveryResult(
                BattleActorResultCode.BattleNotFound,
                plan.BattleId,
                _recoveryState,
                0,
                0,
                _actorState,
                false,
                "battle.actor.recovery_wrong_battle");
        }

        _recoveryState = BattleActorRecoveryStateCode.RestoringCheckpoint;
        await AppendJournalAsync(
            BattleJournalEntryKind.RecoveryStarted,
            plan.RecoveryPlanId,
            plan.PayloadHash,
            "started");
        if (_failureInjection.Point == BattleArchitectureFailurePoint.Recovery)
        {
            _recoveryState = BattleActorRecoveryStateCode.RecoveryRequired;
            return new BattleRecoveryResult(
                BattleActorResultCode.RecoveryRequired,
                BattleId,
                _recoveryState,
                0,
                0,
                _actorState,
                false,
                "battle.actor.recovery_injected");
        }

        var checkpoint = await _store.LoadLatestValidAsync(BattleId, CancellationToken.None);
        if (checkpoint is null)
        {
            _recoveryState = BattleActorRecoveryStateCode.RecoveryRequired;
            return new BattleRecoveryResult(
                BattleActorResultCode.RecoveryRequired,
                BattleId,
                _recoveryState,
                0,
                0,
                _actorState,
                false,
                "battle.actor.checkpoint_missing");
        }

        if (!string.Equals(
                checkpoint.PayloadHash,
                BattleArchitectureV2Hash.Canonical(checkpoint.Snapshot),
                StringComparison.Ordinal))
        {
            _recoveryState = BattleActorRecoveryStateCode.RecoveryRequired;
            return new BattleRecoveryResult(
                BattleActorResultCode.CheckpointCorrupt,
                BattleId,
                _recoveryState,
                checkpoint.CheckpointVersion,
                0,
                _actorState,
                false,
                "battle.actor.checkpoint_corrupt");
        }

        foreach (var content in checkpoint.Snapshot.ContentVersions)
        {
            if (!ContentVersions().TryGetValue(content.Key, out var current) ||
                !string.Equals(current, content.Value, StringComparison.Ordinal))
            {
                _recoveryState = BattleActorRecoveryStateCode.RecoveryRequired;
                return new BattleRecoveryResult(
                    BattleActorResultCode.VersionUnavailable,
                    BattleId,
                    _recoveryState,
                    checkpoint.CheckpointVersion,
                    0,
                    _actorState,
                    false,
                    $"battle.actor.content_version_unavailable:{content.Key}");
            }
        }

        _recoveryState = BattleActorRecoveryStateCode.ReplayingJournal;
        var journal = await _store.ReadAfterAsync(
            BattleId,
            checkpoint.Snapshot.JournalSequence,
            CancellationToken.None);
        _journalSequence = Math.Max(
            checkpoint.Snapshot.JournalSequence,
            journal.LastOrDefault()?.JournalSequence ?? 0);
        _outboxSequence = checkpoint.Snapshot.OutboxSequence;
        _commandWindowVersion = checkpoint.Snapshot.CommandWindowVersion;
        _lockedCommands = checkpoint.Snapshot.LockedCommands;
        _resolutionPlan = checkpoint.Snapshot.CurrentResolutionPlan;
        _cursor = checkpoint.Snapshot.ActionCursor;
        _finalizationState = checkpoint.Snapshot.FinalizationState;
        _actorState = checkpoint.Snapshot.BattleState;
        _recoveryState = BattleActorRecoveryStateCode.Completed;
        await AppendJournalAsync(
            BattleJournalEntryKind.RecoveryCompleted,
            plan.RecoveryPlanId,
            plan.PayloadHash,
            "completed");
        return new BattleRecoveryResult(
            BattleActorResultCode.Success,
            BattleId,
            _recoveryState,
            checkpoint.CheckpointVersion,
            journal.Count,
            _actorState,
            false,
            "");
    }

    private async Task<BattleEngineOperationResult> HandleShutdownAsync()
    {
        if (_lifecycleState == BattleActorLifecycleState.Stopped)
        {
            return Success(isDuplicate: true);
        }

        _lifecycleState = BattleActorLifecycleState.Draining;
        if (!_stateMachine.IsTerminal(_actorState))
        {
            if (_actorState != BattleActorStateCode.Suspended &&
                _actorState is not BattleActorStateCode.Finalizing and
                    not BattleActorStateCode.RecoveryRequired)
            {
                var transition = _stateMachine.Validate(
                    _actorState,
                    BattleActorStateCode.Suspended,
                    out _);
                if (transition == BattleActorResultCode.Success)
                {
                    await TransitionAsync(
                        BattleActorStateCode.Suspended,
                        BattleJournalEntryKind.ActorSuspended);
                }
            }

            await SaveCheckpointAsync();
        }

        await AppendJournalAsync(
            BattleJournalEntryKind.ActorStopped,
            null,
            $"stop:{BattleId:N}",
            "stopped");
        _lifecycleState = BattleActorLifecycleState.Stopped;
        _mailbox.Writer.TryComplete();
        return Success();
    }

    private async Task<BattleEngineOperationResult> TransitionAsync(
        BattleActorStateCode next,
        BattleJournalEntryKind entryKind)
    {
        var validation = _stateMachine.Validate(_actorState, next, out var failureCode);
        if (validation == BattleActorResultCode.DuplicateCompleted)
        {
            return Success(isDuplicate: true);
        }

        if (validation != BattleActorResultCode.Success)
        {
            return Failure(validation, failureCode);
        }

        var appended = await AppendJournalAsync(
            entryKind,
            null,
            $"transition:{_actorState}:{next}:{_battle.BattleVersion + 1}",
            next.ToString());
        if (appended is not (BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted))
        {
            return Failure(BattleActorResultCode.PersistenceFailure, "battle.actor.transition_journal_failed");
        }

        _actorState = next;
        _battle = _battle with
        {
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CurrentPhase = MapPhase(next),
            State = MapState(next)
        };
        return Success();
    }

    private async Task<BattleEngineOperationResult> EnterRecoveryRequiredAsync(string failureCode)
    {
        _failureCode = failureCode;
        _recoveryState = BattleActorRecoveryStateCode.RecoveryRequired;
        if (_actorState != BattleActorStateCode.RecoveryRequired)
        {
            var valid = _stateMachine.Validate(
                _actorState,
                BattleActorStateCode.RecoveryRequired,
                out _);
            if (valid == BattleActorResultCode.Success)
            {
                _actorState = BattleActorStateCode.RecoveryRequired;
                _battle = _battle with
                {
                    State = BattleState.RecoveryRequired,
                    CurrentPhase = BattlePhase.RecoveryRequired,
                    RecoveryState = BattleRecoveryState.RecoveryRequired,
                    BattleVersion = _battle.BattleVersion + 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            }
        }

        await AppendJournalAsync(
            BattleJournalEntryKind.RecoveryRequired,
            null,
            $"recovery:{BattleId:N}:{_battle.BattleVersion}",
            failureCode);
        await AppendOutboxAsync(
            BattleArchitectureEventKind.BattleRecoveryRequired,
            null,
            null,
            Array.Empty<Guid>(),
            BattleEventDeliveryCategory.Administration,
            _battle.CorrelationId);
        return Failure(BattleActorResultCode.RecoveryRequired, failureCode);
    }

    private async Task<BattleActorResultCode> AppendJournalAsync(
        BattleJournalEntryKind kind,
        Guid? actionId,
        string idempotency,
        string result)
    {
        if (_failureInjection.Point == BattleArchitectureFailurePoint.JournalAppend)
        {
            return BattleActorResultCode.PersistenceFailure;
        }

        var sequence = checked(_journalSequence + 1);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            state = _actorState.ToString(),
            result,
            round = _battle.CurrentRoundNumber,
            version = _battle.BattleVersion
        });
        var entry = new BattleJournalEntry(
            BattleId,
            sequence,
            kind,
            _battle.BattleVersion,
            _battle.CurrentRoundNumber,
            _commandWindowVersion,
            _resolutionPlan?.RoundResolutionPlanId,
            actionId,
            result,
            BattleArchitectureV2Hash.SafeId(idempotency),
            1,
            payload,
            BattleArchitectureV2Hash.Canonical(payload),
            DateTimeOffset.UtcNow,
            _battle.CorrelationId);
        var append = await _store.AppendAsync(entry, CancellationToken.None);
        if (append.Code is BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted)
        {
            _journalSequence = sequence;
        }

        return append.Code;
    }

    private async Task<BattleActorResultCode> AppendOutboxAsync(
        BattleArchitectureEventKind eventType,
        Guid? actionId,
        Guid? sourceParticipantId,
        IReadOnlyList<Guid> targets,
        BattleEventDeliveryCategory category,
        string correlationId)
    {
        if (_failureInjection.Point == BattleArchitectureFailurePoint.OutboxWrite)
        {
            return BattleActorResultCode.PersistenceFailure;
        }

        var sequence = checked(_outboxSequence + 1);
        var battleEvent = new BattleEventEnvelope(
            BattleArchitectureV2Hash.StableGuid(BattleId.ToString("N"), sequence.ToString(), eventType.ToString()),
            BattleId,
            sequence,
            eventType,
            _battle.CurrentRoundNumber,
            actionId,
            sourceParticipantId,
            Array.AsReadOnly(targets.OrderBy(value => value).ToArray()),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                battleId = BattleId,
                eventType = eventType.ToString(),
                battleVersion = _battle.BattleVersion
            }),
            1,
            Array.AsReadOnly(new[] { category }),
            BattleOutboxDispatchState.Pending,
            DateTimeOffset.UtcNow,
            correlationId);
        var append = await _store.AppendEventAsync(battleEvent, CancellationToken.None);
        if (append.Code is BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted)
        {
            _outboxSequence = sequence;
        }

        return append.Code;
    }

    private async Task AppendActionEventsAsync(
        BattleActionResult result,
        BattleActorActionPlan actionPlan)
    {
        await AppendOutboxAsync(
            BattleArchitectureEventKind.ActionStarted,
            actionPlan.ActionExecutionId,
            actionPlan.ParticipantId,
            actionPlan.TargetParticipantIds,
            BattleEventDeliveryCategory.ReplayArtifact,
            _battle.CorrelationId);
        if (result.Damage > 0)
        {
            await AppendOutboxAsync(
                BattleArchitectureEventKind.DamageApplied,
                actionPlan.ActionExecutionId,
                actionPlan.ParticipantId,
                actionPlan.TargetParticipantIds,
                BattleEventDeliveryCategory.ReplayArtifact,
                _battle.CorrelationId);
        }

        if (result.TargetDefeated)
        {
            await AppendOutboxAsync(
                BattleArchitectureEventKind.ParticipantDefeated,
                actionPlan.ActionExecutionId,
                actionPlan.ParticipantId,
                actionPlan.TargetParticipantIds,
                BattleEventDeliveryCategory.ReplayArtifact,
                _battle.CorrelationId);
        }
    }

    private async Task<BattleActorResultCode> SaveCheckpointAsync()
    {
        if (_failureInjection.Point == BattleArchitectureFailurePoint.CheckpointWrite)
        {
            return BattleActorResultCode.PersistenceFailure;
        }

        var snapshot = ProjectSnapshot();
        var checkpoint = new BattleRoundCheckpoint(
            BattleArchitectureV2Hash.StableGuid(BattleId.ToString("N"), (_checkpointVersion + 1).ToString(), "checkpoint"),
            BattleId,
            _checkpointVersion + 1,
            snapshot,
            1,
            BattleArchitectureV2Hash.Canonical(snapshot),
            true,
            DateTimeOffset.UtcNow);
        var result = await _store.SaveCheckpointAsync(
            checkpoint,
            _checkpointVersion,
            CancellationToken.None);
        if (result is BattleActorResultCode.Success or BattleActorResultCode.DuplicateCompleted)
        {
            _checkpointVersion = checkpoint.CheckpointVersion;
            return BattleActorResultCode.Success;
        }

        return result;
    }

    private BattleActorSnapshot ProjectSnapshot()
    {
        var snapshot = _projector.Project(
            _battle,
            _startPlan.RequestedMode,
            _lifecycleState,
            _actorState,
            _commandWindowVersion,
            _random.State,
            _journalSequence,
            _outboxSequence,
            _finalizationState,
            _recoveryState,
            _lockedCommands,
            _resolutionPlan,
            _cursor,
            DateTimeOffset.UtcNow);
        var participants = snapshot.Participants.Select(participant => participant with
        {
            SessionEpoch = _sessionEpochs.GetValueOrDefault(participant.ParticipantId, 1),
            SessionSafeReference = _sessionSafeReferences.GetValueOrDefault(
                participant.ParticipantId,
                participant.SessionSafeReference)
        }).ToArray();
        var provisional = snapshot with
        {
            Participants = Array.AsReadOnly(participants),
            CanonicalHash = ""
        };
        return provisional with { CanonicalHash = BattleArchitectureV2Hash.Canonical(provisional) };
    }

    private void ApplyActionResult(BattleActionResult result)
    {
        var mutations = result.ParticipantMutations is { Count: > 0 }
            ? result.ParticipantMutations
            : result.TargetParticipantIds.Select(target => new BattleParticipantMutation(
                target,
                result.Damage,
                0,
                result.HpBefore,
                result.HpAfter,
                result.TargetDefeated,
                result.RuntimeVersionBefore,
                result.RuntimeVersionAfter,
                result.FailureCode)).ToArray();
        var updated = _battle.Participants.Select(participant =>
        {
            var mutation = mutations.FirstOrDefault(value => value.ParticipantId == participant.ParticipantId);
            return mutation is null
                ? participant
                : participant with
                {
                    CurrentHp = mutation.HpAfter,
                    IsAlive = !mutation.Defeated,
                    CombatState = mutation.Defeated
                        ? BattleParticipantCombatState.Defeated
                        : BattleParticipantCombatState.Hit,
                    RuntimeVersion = mutation.RuntimeVersionAfter,
                    DefeatedAtUtc = mutation.Defeated ? result.ResolvedAtUtc : participant.DefeatedAtUtc
                };
        }).ToArray();
        var results = _battle.CurrentRound.ResolutionResults.Append(result).ToArray();
        _battle = _battle with
        {
            Participants = BattleCollections.Freeze(updated),
            CurrentResolutionIndex = result.ResolutionIndex + 1,
            CurrentRound = _battle.CurrentRound with
            {
                CurrentResolutionIndex = result.ResolutionIndex + 1,
                ResolutionResults = BattleCollections.Freeze(results),
                RoundVersion = _battle.CurrentRound.RoundVersion + 1
            },
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = result.ResolvedAtUtc
        };
    }

    private void CompleteRoundAndOpenNext()
    {
        var completed = _battle.CurrentRound with
        {
            State = BattleRoundState.Completed,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            RoundVersion = _battle.CurrentRound.RoundVersion + 1
        };
        var nextNumber = _battle.CurrentRoundNumber + 1;
        var eligible = _battle.Participants
            .Where(value => value.IsAlive)
            .Select(value => value.ParticipantId)
            .OrderBy(value => value)
            .ToArray();
        var next = new BattleRound(
            BattleId,
            nextNumber,
            BattleRoundState.CollectingActions,
            BattleCollections.Freeze(eligible),
            BattleCollections.Freeze(Array.Empty<BattleSubmittedAction>()),
            BattleCollections.Freeze(Array.Empty<BattleLockedAction>()),
            BattleCollections.Freeze(Array.Empty<Guid>()),
            0,
            BattleCollections.Freeze(Array.Empty<BattleActionResult>()),
            DateTimeOffset.UtcNow,
            null,
            null,
            1,
            _battle.CorrelationId);
        _battle = _battle with
        {
            CompletedRounds = BattleCollections.Freeze(_battle.CompletedRounds.Append(completed)),
            CurrentRound = next,
            CurrentRoundNumber = nextNumber,
            CurrentResolutionIndex = 0,
            BattleVersion = _battle.BattleVersion + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _commands.Clear();
        _lockedCommands = null;
        _resolutionPlan = null;
        _cursor = null;
    }

    private void OpenCommandWindow()
    {
        _commandWindowVersion = checked(_commandWindowVersion + 1);
        _commandWindowBaseBattleVersion = _battle.BattleVersion;
        _commands.Clear();
    }

    private IReadOnlyList<Guid> RequiredParticipantIds() =>
        _battle.Participants
            .Where(value => value.IsAlive)
            .Select(value => value.ParticipantId)
            .OrderBy(value => value)
            .ToArray();

    private IReadOnlyList<Guid> ResolveAuthoritativeTargets(Guid sourceParticipantId)
    {
        var source = _battle.Participants.Single(value => value.ParticipantId == sourceParticipantId);
        var target = _battle.Participants
            .Where(value => value.IsAlive && value.Side != source.Side)
            .OrderBy(value => value.FormationSlot)
            .ThenBy(value => value.ParticipantId)
            .FirstOrDefault();
        return target is null ? Array.Empty<Guid>() : new[] { target.ParticipantId };
    }

    private IReadOnlyDictionary<string, string> ContentVersions() =>
        new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["ai"] = _startPlan.AiContentVersion,
                ["battle"] = _battle.ContentVersion,
                ["formation"] = _startPlan.FormationContentVersion,
                ["formula"] = _startPlan.FormulaVersion,
                ["monster"] = _startPlan.MonsterContentVersion,
                ["skill"] = _startPlan.SkillContentVersion,
                ["status"] = _startPlan.StatusContentVersion
            });

    private BattleEngineOperationResult Success(bool isDuplicate = false) =>
        new(
            isDuplicate ? BattleActorResultCode.DuplicateCompleted : BattleActorResultCode.Success,
            BattleId,
            "",
            isDuplicate,
            _battle.BattleVersion,
            _battle.CurrentRoundNumber,
            _commandWindowVersion,
            []);

    private BattleEngineOperationResult Failure(BattleActorResultCode code, string failureCode) =>
        new(
            code,
            BattleId,
            failureCode,
            false,
            _battle.BattleVersion,
            _battle.CurrentRoundNumber,
            _commandWindowVersion,
            []);

    private BattleEngineOperationResult UnavailableResult() =>
        Failure(
            _lifecycleState == BattleActorLifecycleState.Draining
                ? BattleActorResultCode.HostShuttingDown
                : BattleActorResultCode.BattleUnavailable,
            _lifecycleState == BattleActorLifecycleState.Draining
                ? "battle.actor.host_shutting_down"
                : "battle.actor.unavailable");

    private static bool Supported(BattleActorCommandType type) =>
        type is
            BattleActorCommandType.BasicAttack or
            BattleActorCommandType.Skill or
            BattleActorCommandType.Pass or
            BattleActorCommandType.TimeoutDefault or
            BattleActorCommandType.TrustedInternalTest;

    private static BattleActionType ToLegacy(BattleActorCommandType type) =>
        type switch
        {
            BattleActorCommandType.BasicAttack => BattleActionType.BasicAttack,
            BattleActorCommandType.Skill => BattleActionType.Skill,
            BattleActorCommandType.Pass or BattleActorCommandType.TimeoutDefault => BattleActionType.Pass,
            _ => BattleActionType.SystemAction
        };

    private static BattlePhase MapPhase(BattleActorStateCode state) =>
        state switch
        {
            BattleActorStateCode.RoundOpening => BattlePhase.RoundOpening,
            BattleActorStateCode.CollectingCommands => BattlePhase.CollectingActions,
            BattleActorStateCode.LockingCommands => BattlePhase.ActionsLocked,
            BattleActorStateCode.PlanningResolution or BattleActorStateCode.ResolvingActions =>
                BattlePhase.ResolvingActions,
            BattleActorStateCode.RoundClosing => BattlePhase.RoundClosing,
            BattleActorStateCode.EvaluatingCompletion => BattlePhase.VictoryResolution,
            BattleActorStateCode.Finalizing => BattlePhase.RewardFinalization,
            BattleActorStateCode.Completed => BattlePhase.Completed,
            BattleActorStateCode.Aborted => BattlePhase.Aborted,
            BattleActorStateCode.RecoveryRequired => BattlePhase.RecoveryRequired,
            _ => BattlePhase.NotStarted
        };

    private static BattleState MapState(BattleActorStateCode state) =>
        state switch
        {
            BattleActorStateCode.Completed => BattleState.Completed,
            BattleActorStateCode.Finalizing => BattleState.Completing,
            BattleActorStateCode.Aborting => BattleState.Aborting,
            BattleActorStateCode.Aborted => BattleState.Aborted,
            BattleActorStateCode.RecoveryRequired => BattleState.RecoveryRequired,
            BattleActorStateCode.Faulted => BattleState.Faulted,
            BattleActorStateCode.Created => BattleState.Created,
            _ => BattleState.Active
        };
}

public sealed class BattleActorFactory : IBattleActorFactory
{
    private readonly BattleActorOptions _options;
    private readonly IBattleActorDurabilityStore _store;
    private readonly BattleActorStateMachine _stateMachine;
    private readonly IBattleStateProjector _projector;
    private readonly IBattleInitiativePlanner _initiative;
    private readonly IBattleRandomFactory _randomFactory;
    private readonly IBattleActorActionExecutor _actions;
    private readonly IBattleVictoryPolicy _victory;
    private readonly IBattleTimeoutPolicy _timeoutPolicy;
    private readonly IBattleAutoCommandProvider _autoCommands;
    private readonly IBattleFinalizationPlanFactory _finalizationPlans;
    private readonly IBattleFinalizationCoordinator _finalization;
    private readonly BattleArchitectureFailureInjection _failureInjection;

    public BattleActorFactory(
        BattleActorOptions options,
        IBattleActorDurabilityStore store,
        BattleActorStateMachine stateMachine,
        IBattleStateProjector projector,
        IBattleInitiativePlanner initiative,
        IBattleRandomFactory randomFactory,
        IBattleActorActionExecutor actions,
        IBattleVictoryPolicy victory,
        IBattleTimeoutPolicy timeoutPolicy,
        IBattleAutoCommandProvider autoCommands,
        IBattleFinalizationPlanFactory finalizationPlans,
        IBattleFinalizationCoordinator finalization,
        BattleArchitectureFailureInjection? failureInjection = null)
    {
        _options = options;
        _store = store;
        _stateMachine = stateMachine;
        _projector = projector;
        _initiative = initiative;
        _randomFactory = randomFactory;
        _actions = actions;
        _victory = victory;
        _timeoutPolicy = timeoutPolicy;
        _autoCommands = autoCommands;
        _finalizationPlans = finalizationPlans;
        _finalization = finalization;
        _failureInjection = failureInjection ?? new BattleArchitectureFailureInjection();
    }

    public IBattleActor Create(BattleStartPlan plan) =>
        new BattleInstanceActor(
            plan,
            _options,
            _store,
            _stateMachine,
            _projector,
            _initiative,
            _randomFactory,
            _actions,
            _victory,
            _timeoutPolicy,
            _autoCommands,
            _finalizationPlans,
            _finalization,
            _failureInjection);
}

public sealed class BattleActorRegistry : IBattleActorRegistry
{
    private readonly IBattleActorFactory _factory;
    private readonly BattleActorOptions _options;
    private readonly ConcurrentDictionary<Guid, IBattleActor> _actors = [];
    private readonly Queue<KeyValuePair<Guid, IBattleActor>> _stoppedActorOrder = [];
    private readonly HashSet<Guid> _retainedTerminalActorIds = [];
    private readonly object _retentionGate = new();
    private volatile bool _accepting = true;

    public BattleActorRegistry(IBattleActorFactory factory, BattleActorOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public int ActiveCount => _actors.Values.Count(value =>
        value.LifecycleState is BattleActorLifecycleState.Created or BattleActorLifecycleState.Running);

    public bool IsAcceptingNewActors => _accepting;

    public Task<(BattleActorResultCode Code, IBattleActor? Actor, bool IsDuplicate)> GetOrCreateAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accepting)
        {
            return Task.FromResult((
                BattleActorResultCode.HostShuttingDown,
                (IBattleActor?)null,
                false));
        }

        var created = false;
        var actor = _actors.GetOrAdd(plan.BattleId, _ =>
        {
            created = true;
            return _factory.Create(plan);
        });
        return Task.FromResult((
            created ? BattleActorResultCode.Success : BattleActorResultCode.DuplicateCompleted,
            (IBattleActor?)actor,
            !created));
    }

    public bool TryGet(Guid battleId, out IBattleActor? actor) =>
        _actors.TryGetValue(battleId, out actor);

    public async Task<BattleEngineOperationResult> StopAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        if (!_actors.TryGetValue(battleId, out var actor))
        {
            return BattleEngineOperationResult.Failure(
                battleId,
                BattleActorResultCode.BattleNotFound,
                "battle.actor.not_found");
        }

        var result = await actor.StopAsync(cancellationToken);
        if (actor.LifecycleState is BattleActorLifecycleState.Stopped or BattleActorLifecycleState.Faulted)
        {
            RetainTerminalActor(battleId, actor);
        }

        return result;
    }

    public async Task<BattleEngineOperationResult> DrainAsync(CancellationToken cancellationToken)
    {
        _accepting = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownDrainTimeout);
        try
        {
            var actors = _actors.Values.ToArray();
            await Task.WhenAll(actors.Select(actor => actor.StopAsync(timeout.Token)));
            foreach (var actor in actors.Where(value =>
                         value.LifecycleState is BattleActorLifecycleState.Stopped or BattleActorLifecycleState.Faulted))
            {
                RetainTerminalActor(actor.BattleId, actor);
            }
            return new BattleEngineOperationResult(
                BattleActorResultCode.Success,
                Guid.Empty,
                "",
                false,
                0,
                0,
                0,
                []);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return BattleEngineOperationResult.Failure(
                Guid.Empty,
                BattleActorResultCode.BattleBusy,
                "battle.actor.drain_timeout");
        }
    }

    public IReadOnlyList<BattleActorRegistryItem> Snapshot() =>
        _actors.Values
            .OrderBy(value => value.BattleId)
            .Select(value =>
            {
                var snapshot = value.RequestSnapshotAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return new BattleActorRegistryItem(
                    value.BattleId,
                    value.LifecycleState,
                    snapshot.BattleState,
                    snapshot.BattleVersion,
                    value.MailboxDepth,
                    value.MailboxCapacity,
                    snapshot.FinalizationState == BattleFinalizationStateCode.Completed,
                    0,
                    value.FailureCode,
                    snapshot.CreatedAtUtc,
                    snapshot.UpdatedAtUtc);
            })
            .ToArray();

    private void RetainTerminalActor(Guid battleId, IBattleActor actor)
    {
        lock (_retentionGate)
        {
            if (!_retainedTerminalActorIds.Add(battleId))
            {
                return;
            }

            _stoppedActorOrder.Enqueue(new KeyValuePair<Guid, IBattleActor>(battleId, actor));
            while (_stoppedActorOrder.Count > _options.MaximumRetainedStoppedActors &&
                   _stoppedActorOrder.TryDequeue(out var expired))
            {
                ((ICollection<KeyValuePair<Guid, IBattleActor>>)_actors).Remove(expired);
                _retainedTerminalActorIds.Remove(expired.Key);
            }
        }
    }
}

public sealed class ActorBattleExecutionEngine : IBattleExecutionEngine, IBattleLifecycleCoordinator
{
    private readonly IBattleActorRegistry _registry;

    public ActorBattleExecutionEngine(
        IBattleActorRegistry registry,
        BattleEngineMode mode = BattleEngineMode.ActorPrimary)
    {
        if (mode is not (BattleEngineMode.ActorPrimary or BattleEngineMode.ActorPrimaryWithLegacyFallbackDisabled))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _registry = registry;
        Mode = mode;
    }

    public BattleEngineMode Mode { get; }

    public Task<BattleEngineCreateResult> CreateBattleAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken) =>
        CreateAsync(plan with { RequestedMode = Mode }, cancellationToken);

    public async Task<BattleEngineCreateResult> CreateAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.PreparedBattle is null || (!plan.TestOnly && Mode == BattleEngineMode.ActorPrimary))
        {
            return new BattleEngineCreateResult(
                BattleActorResultCode.EvidenceBlocked,
                plan.BattleId,
                Mode,
                "battle.actor.prepared_snapshot_required",
                false,
                0,
                []);
        }

        var registration = await _registry.GetOrCreateAsync(plan, cancellationToken);
        if (registration.Actor is null)
        {
            return new BattleEngineCreateResult(
                registration.Code,
                plan.BattleId,
                Mode,
                "battle.actor.registry_rejected",
                false,
                0,
                []);
        }

        var started = await registration.Actor.StartAsync(cancellationToken);
        return new BattleEngineCreateResult(
            registration.IsDuplicate
                ? BattleActorResultCode.DuplicateCompleted
                : started.Code,
            plan.BattleId,
            Mode,
            started.FailureCode,
            registration.IsDuplicate,
            started.BattleVersion,
            []);
    }

    public Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken) =>
        Try(command.BattleId, actor => actor.SubmitCommandAsync(command, cancellationToken));

    public Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken) =>
        Try(
            battleId,
            actor => actor.NotifyCommandWindowExpiredAsync(
                roundNumber,
                commandWindowVersion,
                correlationId,
                cancellationToken));

    public Task<BattleEngineOperationResult> NotifySessionDisconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken) =>
        Try(
            battleId,
            actor => actor.NotifyDisconnectedAsync(
                sessionSafeReference,
                characterId,
                cancellationToken));

    public Task<BattleEngineOperationResult> NotifySessionReconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken) =>
        Try(
            battleId,
            actor => actor.NotifyReconnectedAsync(
                sessionSafeReference,
                characterId,
                newSessionEpoch,
                cancellationToken));

    public Task<BattleActorSnapshot?> RequestSnapshotAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        _registry.TryGet(battleId, out var actor) && actor is not null
            ? actor.RequestSnapshotAsync(cancellationToken).ContinueWith<BattleActorSnapshot?>(
                task => task.Result,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            : Task.FromResult<BattleActorSnapshot?>(null);

    public Task<BattleEngineOperationResult> RequestAbortAsync(
        Guid battleId,
        string reason,
        CancellationToken cancellationToken) =>
        Try(battleId, actor => actor.RequestAbortAsync(reason, cancellationToken));

    public Task<BattleRecoveryResult> RecoverBattleAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(plan.BattleId, out var actor) || actor is null)
        {
            return Task.FromResult(new BattleRecoveryResult(
                BattleActorResultCode.BattleNotFound,
                plan.BattleId,
                BattleActorRecoveryStateCode.RecoveryRequired,
                0,
                0,
                BattleActorStateCode.RecoveryRequired,
                false,
                "battle.actor.not_found"));
        }

        return actor.RecoverAsync(plan, cancellationToken);
    }

    public Task<BattleEngineOperationResult> StopBattleAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        StopAsync(battleId, cancellationToken);

    public Task<BattleEngineOperationResult> StopAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        _registry.StopAsync(battleId, cancellationToken);

    public Task<BattleEngineOperationResult> DrainAsync(CancellationToken cancellationToken) =>
        _registry.DrainAsync(cancellationToken);

    public async Task<BattleEngineStatus?> GetBattleStatusAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(battleId, out var actor) || actor is null)
        {
            return null;
        }

        var snapshot = await actor.RequestSnapshotAsync(cancellationToken);
        return new BattleEngineStatus(
            battleId,
            Mode,
            actor.LifecycleState,
            snapshot.BattleState,
            snapshot.BattleVersion,
            snapshot.RoundNumber,
            snapshot.CommandWindowVersion,
            actor.MailboxDepth,
            actor.MailboxCapacity,
            snapshot.FinalizationState,
            snapshot.RecoveryState,
            actor.FailureCode);
    }

    private Task<BattleEngineOperationResult> Try(
        Guid battleId,
        Func<IBattleActor, Task<BattleEngineOperationResult>> operation)
    {
        if (!_registry.TryGet(battleId, out var actor) || actor is null)
        {
            return Task.FromResult(BattleEngineOperationResult.Failure(
                battleId,
                BattleActorResultCode.BattleNotFound,
                "battle.actor.not_found"));
        }

        return operation(actor);
    }
}

public sealed class LegacyBattleExecutionEngine : IBattleExecutionEngine
{
    private readonly ITurnBasedBattleCoordinator _coordinator;
    private readonly IBattleInstanceRepository _repository;

    public LegacyBattleExecutionEngine(
        ITurnBasedBattleCoordinator coordinator,
        IBattleInstanceRepository repository)
    {
        _coordinator = coordinator;
        _repository = repository;
    }

    public BattleEngineMode Mode => BattleEngineMode.LegacyPrimary;

    public async Task<BattleEngineCreateResult> CreateBattleAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.CreateAsync(plan.LegacyRequest, cancellationToken);
        return new BattleEngineCreateResult(
            Map(result.Code),
            result.BattleInstanceId ?? plan.BattleId,
            Mode,
            result.FailureCode,
            result.IsDuplicate,
            result.BattleVersion,
            []);
    }

    public async Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        var request = new BattleActionRequest(
            command.BattleCommandId,
            command.IdempotencyKey,
            command.BattleId,
            command.ExpectedBattleVersion,
            command.RoundNumber,
            command.SessionSafeReference,
            command.CharacterId ?? 0,
            command.ParticipantId,
            command.CommandType switch
            {
                BattleActorCommandType.BasicAttack => BattleActionType.BasicAttack,
                BattleActorCommandType.Skill => BattleActionType.Skill,
                BattleActorCommandType.Pass => BattleActionType.Pass,
                _ => BattleActionType.Unknown
            },
            command.TargetParticipantIdsCandidate,
            command.SkillDefinitionIdCandidate,
            command.ItemReferenceCandidate,
            command.ClientSequenceCandidate,
            BattleRequestSource.TrustedInternalTest,
            command.SubmittedAtUtc,
            command.CorrelationId);
        var result = await _coordinator.SubmitActionAsync(request, cancellationToken);
        return new BattleEngineOperationResult(
            Map(result.Code),
            command.BattleId,
            result.FailureCode,
            result.IsDuplicate,
            result.BattleVersion,
            result.RoundNumber,
            command.CommandWindowVersion,
            []);
    }

    public async Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.LockAndResolveAsync(battleId, cancellationToken);
        return Map(result, commandWindowVersion);
    }

    public async Task<BattleEngineOperationResult> NotifySessionDisconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.DisconnectAsync(
            battleId,
            sessionSafeReference,
            characterId,
            cancellationToken);
        return Map(result, 0);
    }

    public async Task<BattleEngineOperationResult> NotifySessionReconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.ReconnectAsync(
            battleId,
            sessionSafeReference,
            characterId,
            cancellationToken);
        return Map(result, 0);
    }

    public async Task<BattleActorSnapshot?> RequestSnapshotAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        var battle = await _repository.GetAsync(battleId, cancellationToken);
        if (battle is null)
        {
            return null;
        }

        var projector = new BattleStateProjector();
        return projector.Project(
            battle,
            Mode,
            BattleActorLifecycleState.Running,
            Map(battle.State),
            0,
            new BattleRandomState(XorShift64StarBattleRandom.Version, 1, 1, 0),
            0,
            0,
            battle.State == BattleState.Completed
                ? BattleFinalizationStateCode.Completed
                : BattleFinalizationStateCode.NotStarted,
            battle.RecoveryState == BattleRecoveryState.RecoveryRequired
                ? BattleActorRecoveryStateCode.RecoveryRequired
                : BattleActorRecoveryStateCode.NotRequired,
            null,
            null,
            null,
            battle.UpdatedAtUtc);
    }

    public async Task<BattleEngineOperationResult> RequestAbortAsync(
        Guid battleId,
        string reason,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.AbortAsync(battleId, reason, cancellationToken);
        return Map(result, 0);
    }

    public async Task<BattleRecoveryResult> RecoverBattleAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.RecoverAsync(plan.BattleId, cancellationToken);
        return new BattleRecoveryResult(
            Map(result.Code),
            plan.BattleId,
            result.Succeeded
                ? BattleActorRecoveryStateCode.Completed
                : BattleActorRecoveryStateCode.RecoveryRequired,
            0,
            0,
            result.Succeeded ? BattleActorStateCode.CollectingCommands : BattleActorStateCode.RecoveryRequired,
            result.IsDuplicate,
            result.FailureCode);
    }

    public Task<BattleEngineOperationResult> StopBattleAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        RequestAbortAsync(battleId, "EngineStop", cancellationToken);

    public async Task<BattleEngineStatus?> GetBattleStatusAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RequestSnapshotAsync(battleId, cancellationToken);
        return snapshot is null
            ? null
            : new BattleEngineStatus(
                battleId,
                Mode,
                BattleActorLifecycleState.Running,
                snapshot.BattleState,
                snapshot.BattleVersion,
                snapshot.RoundNumber,
                snapshot.CommandWindowVersion,
                0,
                0,
                snapshot.FinalizationState,
                snapshot.RecoveryState,
                "");
    }

    private static BattleEngineOperationResult Map(
        BattleRoundResolutionResult result,
        long commandWindowVersion) =>
        new(
            Map(result.Code),
            result.BattleInstanceId,
            result.FailureCode,
            result.IsDuplicate,
            result.BattleVersion,
            result.RoundNumber,
            commandWindowVersion,
            []);

    private static BattleEngineOperationResult Map(
        BattleReconnectResult result,
        long commandWindowVersion) =>
        new(
            Map(result.Code),
            result.BattleInstanceId,
            result.FailureCode,
            false,
            result.BattleVersion,
            result.RoundNumber,
            commandWindowVersion,
            []);

    private static BattleActorResultCode Map(BattleResultCode code) =>
        code switch
        {
            BattleResultCode.Success => BattleActorResultCode.Success,
            BattleResultCode.DuplicateCompleted => BattleActorResultCode.DuplicateCompleted,
            BattleResultCode.ReplayConflict => BattleActorResultCode.ReplayConflict,
            BattleResultCode.InvalidSession => BattleActorResultCode.InvalidSession,
            BattleResultCode.OwnershipMismatch => BattleActorResultCode.OwnershipMismatch,
            BattleResultCode.BattleNotFound => BattleActorResultCode.BattleNotFound,
            BattleResultCode.ParticipantNotFound => BattleActorResultCode.ParticipantNotFound,
            BattleResultCode.ParticipantDead => BattleActorResultCode.ParticipantDefeated,
            BattleResultCode.InvalidRound => BattleActorResultCode.StaleRound,
            BattleResultCode.InvalidPhase => BattleActorResultCode.InvalidBattleState,
            BattleResultCode.ActionAlreadySubmitted => BattleActorResultCode.ActionAlreadySubmitted,
            BattleResultCode.ActionLocked => BattleActorResultCode.ActionAlreadyLocked,
            BattleResultCode.UnsupportedAction => BattleActorResultCode.UnsupportedAction,
            BattleResultCode.TurnOrderEvidenceBlocked or BattleResultCode.BattlePolicyBlocked =>
                BattleActorResultCode.EvidenceBlocked,
            BattleResultCode.VersionConflict => BattleActorResultCode.VersionConflict,
            BattleResultCode.RecoveryRequired => BattleActorResultCode.RecoveryRequired,
            BattleResultCode.BattleAlreadyCompleted => BattleActorResultCode.BattleCompleted,
            BattleResultCode.PersistenceFailure => BattleActorResultCode.PersistenceFailure,
            _ => BattleActorResultCode.InternalFailure
        };

    private static BattleActorStateCode Map(BattleState state) =>
        state switch
        {
            BattleState.Created => BattleActorStateCode.Created,
            BattleState.Completed => BattleActorStateCode.Completed,
            BattleState.Aborting => BattleActorStateCode.Aborting,
            BattleState.Aborted => BattleActorStateCode.Aborted,
            BattleState.RecoveryRequired => BattleActorStateCode.RecoveryRequired,
            BattleState.Faulted => BattleActorStateCode.Faulted,
            _ => BattleActorStateCode.CollectingCommands
        };
}

public sealed class ActorShadowBattleExecutionEngine : IBattleExecutionEngine
{
    private readonly LegacyBattleExecutionEngine _legacy;
    private readonly ActorBattleExecutionEngine _shadow;

    public ActorShadowBattleExecutionEngine(
        LegacyBattleExecutionEngine legacy,
        ActorBattleExecutionEngine shadow)
    {
        _legacy = legacy;
        _shadow = shadow;
    }

    public BattleEngineMode Mode => BattleEngineMode.ActorShadow;

    public async Task<BattleEngineCreateResult> CreateBattleAsync(
        BattleStartPlan plan,
        CancellationToken cancellationToken)
    {
        var formal = await _legacy.CreateBattleAsync(plan, cancellationToken);
        if (plan.PreparedBattle is not null)
        {
            await _shadow.CreateBattleAsync(
                plan with
                {
                    RequestedMode = BattleEngineMode.ActorPrimary,
                    TestOnly = true
                },
                cancellationToken);
        }

        return formal with { EngineMode = Mode, NetworkBytes = [] };
    }

    public async Task<BattleEngineOperationResult> SubmitCommandAsync(
        BattleActorCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        var formal = await _legacy.SubmitCommandAsync(command, cancellationToken);
        await _shadow.SubmitCommandAsync(command, cancellationToken);
        return formal with { NetworkBytes = [] };
    }

    public async Task<BattleEngineOperationResult> NotifyCommandWindowExpiredAsync(
        Guid battleId,
        int roundNumber,
        long commandWindowVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var formal = await _legacy.NotifyCommandWindowExpiredAsync(
            battleId,
            roundNumber,
            commandWindowVersion,
            correlationId,
            cancellationToken);
        await _shadow.NotifyCommandWindowExpiredAsync(
            battleId,
            roundNumber,
            commandWindowVersion,
            correlationId,
            cancellationToken);
        return formal with { NetworkBytes = [] };
    }

    public Task<BattleEngineOperationResult> NotifySessionDisconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        CancellationToken cancellationToken) =>
        _legacy.NotifySessionDisconnectedAsync(
            battleId,
            sessionSafeReference,
            characterId,
            cancellationToken);

    public Task<BattleEngineOperationResult> NotifySessionReconnectedAsync(
        Guid battleId,
        string sessionSafeReference,
        long characterId,
        long newSessionEpoch,
        CancellationToken cancellationToken) =>
        _legacy.NotifySessionReconnectedAsync(
            battleId,
            sessionSafeReference,
            characterId,
            newSessionEpoch,
            cancellationToken);

    public Task<BattleActorSnapshot?> RequestSnapshotAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        _legacy.RequestSnapshotAsync(battleId, cancellationToken);

    public Task<BattleEngineOperationResult> RequestAbortAsync(
        Guid battleId,
        string reason,
        CancellationToken cancellationToken) =>
        _legacy.RequestAbortAsync(battleId, reason, cancellationToken);

    public Task<BattleRecoveryResult> RecoverBattleAsync(
        BattleRecoveryPlan plan,
        CancellationToken cancellationToken) =>
        _legacy.RecoverBattleAsync(plan, cancellationToken);

    public async Task<BattleEngineOperationResult> StopBattleAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        await _shadow.StopBattleAsync(battleId, cancellationToken);
        return await _legacy.StopBattleAsync(battleId, cancellationToken);
    }

    public Task<BattleEngineStatus?> GetBattleStatusAsync(
        Guid battleId,
        CancellationToken cancellationToken) =>
        _legacy.GetBattleStatusAsync(battleId, cancellationToken);
}

public sealed class BattleExecutionEngineFactory : IBattleExecutionEngineFactory
{
    private readonly IReadOnlyDictionary<BattleEngineMode, IBattleExecutionEngine> _engines;

    public BattleExecutionEngineFactory(
        LegacyBattleExecutionEngine legacy,
        ActorShadowBattleExecutionEngine shadow,
        ActorBattleExecutionEngine actorPrimary,
        ActorBattleExecutionEngine actorPrimaryNoFallback)
    {
        _engines = new ReadOnlyDictionary<BattleEngineMode, IBattleExecutionEngine>(
            new Dictionary<BattleEngineMode, IBattleExecutionEngine>
            {
                [BattleEngineMode.LegacyPrimary] = legacy,
                [BattleEngineMode.ActorShadow] = shadow,
                [BattleEngineMode.ActorPrimary] = actorPrimary,
                [BattleEngineMode.ActorPrimaryWithLegacyFallbackDisabled] = actorPrimaryNoFallback
            });
    }

    public IBattleExecutionEngine Create(BattleEngineMode mode) =>
        _engines.TryGetValue(mode, out var engine)
            ? engine
            : throw new ArgumentOutOfRangeException(nameof(mode));
}

public sealed class BattleEngineSelector : IBattleEngineSelector
{
    private readonly BattleEngineSelectionOptions _options;
    private readonly IBattleExecutionEngineFactory _factory;

    public BattleEngineSelector(
        BattleEngineSelectionOptions options,
        IBattleExecutionEngineFactory factory)
    {
        _options = options;
        _factory = factory;
    }

    public BattleEngineMode DefaultMode => _options.Mode;

    public BattleActorResultCode ValidateMode(
        BattleEngineMode mode,
        out string failureCode)
    {
        if (!Enum.IsDefined(mode))
        {
            failureCode = "battle.engine.mode_invalid";
            return BattleActorResultCode.EvidenceBlocked;
        }

        if (mode is BattleEngineMode.ActorPrimary or BattleEngineMode.ActorPrimaryWithLegacyFallbackDisabled &&
            !_options.ActorPrimaryAllowed)
        {
            failureCode = "battle.engine.actor_primary_not_authorized";
            return BattleActorResultCode.EvidenceBlocked;
        }

        if (_options.SilentFallbackAllowed)
        {
            failureCode = "battle.engine.silent_fallback_forbidden";
            return BattleActorResultCode.EvidenceBlocked;
        }

        failureCode = "";
        return BattleActorResultCode.Success;
    }

    public IBattleExecutionEngine Select(BattleEngineMode? requestedMode = null)
    {
        var mode = requestedMode ?? DefaultMode;
        var result = ValidateMode(mode, out var failureCode);
        if (result != BattleActorResultCode.Success)
        {
            throw new InvalidOperationException(failureCode);
        }

        return _factory.Create(mode);
    }
}
