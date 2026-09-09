using System.Collections.Concurrent;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class TurnBasedBattleCoordinator :
    ITurnBasedBattleCoordinator,
    IBattleRecoveryCoordinator
{
    private readonly IWorldInteractionSessionRegistry _sessions;
    private readonly PlayerCombatRuntimeRegistry _players;
    private readonly IBattleEncounterResolver _encounters;
    private readonly IBattleInstanceFactory _factory;
    private readonly IBattleInstanceRepository _repository;
    private readonly IBattleActionSubmissionStore _actionSubmissions;
    private readonly IBattleIdempotencyStore _idempotency;
    private readonly IWorldBattleReservationRegistry _reservations;
    private readonly IBattleTurnOrderPolicy _turnOrder;
    private readonly IBattleActionResolver _actions;
    private readonly IBattleVictoryPolicy _victory;
    private readonly IBattleRewardPolicy _rewardPolicy;
    private readonly IBattleRewardCoordinator _rewards;
    private readonly IBattleClock _clock;
    private readonly IBattleEventSink _events;
    private readonly IBattleAuditLedger _audit;
    private readonly IBattleCombatStateAuthority? _combatAuthority;
    private readonly IStatusTriggerCoordinator? _statusTriggers;
    private readonly BattleFailureInjection _failureInjection;
    private readonly AsyncKeyedLock<Guid> _battleLocks = new();
    private readonly AsyncKeyedLock<long> _characterLocks = new();

    public TurnBasedBattleCoordinator(
        IWorldInteractionSessionRegistry sessions,
        PlayerCombatRuntimeRegistry players,
        IBattleEncounterResolver encounters,
        IBattleInstanceFactory factory,
        IBattleInstanceRepository repository,
        IBattleActionSubmissionStore actionSubmissions,
        IBattleIdempotencyStore idempotency,
        IWorldBattleReservationRegistry reservations,
        IBattleTurnOrderPolicy turnOrder,
        IBattleActionResolver actions,
        IBattleVictoryPolicy victory,
        IBattleRewardPolicy rewardPolicy,
        IBattleRewardCoordinator rewards,
        IBattleClock clock,
        IBattleEventSink events,
        IBattleAuditLedger audit,
        IBattleCombatStateAuthority? combatAuthority = null,
        BattleFailureInjection? failureInjection = null,
        IStatusTriggerCoordinator? statusTriggers = null)
    {
        _sessions = sessions;
        _players = players;
        _encounters = encounters;
        _factory = factory;
        _repository = repository;
        _actionSubmissions = actionSubmissions;
        _idempotency = idempotency;
        _reservations = reservations;
        _turnOrder = turnOrder;
        _actions = actions;
        _victory = victory;
        _rewardPolicy = rewardPolicy;
        _rewards = rewards;
        _clock = clock;
        _events = events;
        _audit = audit;
        _combatAuthority = combatAuthority;
        _statusTriggers = statusTriggers;
        _failureInjection = failureInjection ?? new BattleFailureInjection();
    }

    public async Task<BattleCreationResult> CreateAsync(
        BattleRequest request,
        CancellationToken cancellationToken)
    {
        var payloadHash = BattleRuntimeHash.Request(request);
        var replay = await _idempotency.FindCreationAsync(
            request.IdempotencyKey,
            payloadHash,
            cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return CreationFailure(request, BattleResultCode.ReplayConflict, "battle.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with
            {
                Code = BattleResultCode.DuplicateCompleted,
                IsDuplicate = true,
                NetworkBytes = []
            };
        }

        using var characterGate = await _characterLocks.AcquireAsync(request.SourceCharacterId, cancellationToken);
        replay = await _idempotency.FindCreationAsync(
            request.IdempotencyKey,
            payloadHash,
            cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return CreationFailure(request, BattleResultCode.ReplayConflict, "battle.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with
            {
                Code = BattleResultCode.DuplicateCompleted,
                IsDuplicate = true,
                NetworkBytes = []
            };
        }

        var committed = await _repository.FindByRequestAsync(
            request.BattleRequestId,
            cancellationToken);
        if (committed is not null)
        {
            if (!string.Equals(
                    committed.IdempotencySafeId,
                    BattleRuntimeHash.SafeId(request.IdempotencyKey),
                    StringComparison.Ordinal))
            {
                return CreationFailure(
                    request,
                    BattleResultCode.ReplayConflict,
                    "battle.request_identity_conflict");
            }

            var reconciled = new BattleCreationResult(
                BattleResultCode.DuplicateCompleted,
                request.BattleRequestId,
                committed.BattleInstanceId,
                committed.IdempotencySafeId,
                "",
                true,
                committed.BattleVersion,
                []);
            await _idempotency.SaveCreationAsync(
                request.IdempotencyKey,
                payloadHash,
                reconciled with { Code = BattleResultCode.Success, IsDuplicate = false },
                cancellationToken);
            return reconciled;
        }

        Publish(
            BattleEventKind.BattleCreationRequested,
            Guid.Empty,
            request.BattleRequestId,
            null,
            null,
            null,
            "Requested",
            "",
            request.CorrelationId);
        var bindingResult = _sessions.Get(request.SourceSessionId);
        if (!bindingResult.Succeeded || bindingResult.Value is null)
        {
            return CreationFailure(request, BattleResultCode.InvalidSession, "battle.invalid_session");
        }

        var binding = bindingResult.Value;
        if (!binding.Session.IsAuthenticated ||
            binding.Session.IsClosing ||
            binding.Session.CharacterId != request.SourceCharacterId ||
            binding.MapSession.CharacterId != request.SourceCharacterId ||
            binding.MapSession.PlayerRuntimeEntityId != request.SourceRuntimeEntityId)
        {
            return CreationFailure(request, BattleResultCode.OwnershipMismatch, "battle.ownership_mismatch");
        }

        if (binding.MapSession.MapId != request.SourceMapId ||
            !string.Equals(binding.WorldInstanceId, request.SourceWorldInstanceId, StringComparison.Ordinal))
        {
            return CreationFailure(request, BattleResultCode.Rejected, "battle.world_binding_mismatch");
        }

        var playerResult = _players.Get(request.SourceRuntimeEntityId);
        if (!playerResult.Succeeded || playerResult.Value is null)
        {
            return CreationFailure(request, BattleResultCode.ParticipantNotFound, "battle.player_runtime_missing");
        }

        var player = playerResult.Value;
        if (player.CharacterId != request.SourceCharacterId)
        {
            return CreationFailure(request, BattleResultCode.OwnershipMismatch, "battle.player_ownership_mismatch");
        }

        if (player.RuntimeVersion != request.ExpectedPlayerRuntimeVersion)
        {
            return CreationFailure(request, BattleResultCode.VersionConflict, "battle.player_version_conflict");
        }

        if (_reservations.IsReserved(request.SourceCharacterId) ||
            await _repository.FindActiveByCharacterAsync(request.SourceCharacterId, cancellationToken) is not null)
        {
            return CreationFailure(request, BattleResultCode.Rejected, "battle.player_already_active");
        }

        var encounter = _failureInjection.Point == BattleFailurePoint.EncounterMissing
            ? null
            : _encounters.Resolve(request);
        if (encounter is null || !encounter.Succeeded || encounter.Value is null)
        {
            return CreationFailure(
                request,
                BattleResultCode.BattlePolicyBlocked,
                encounter?.Error.Code ?? "battle.encounter_missing");
        }

        var created = _factory.Create(request, encounter.Value, binding, player, _clock.UtcNow);
        if (!created.Succeeded || created.Value is null)
        {
            return CreationFailure(request, BattleResultCode.Rejected, created.Error.Code);
        }

        var battle = created.Value;
        var playerParticipant = battle.Participants.Single(value => value.CharacterId == request.SourceCharacterId);
        var reservation = _reservations.Reserve(
            battle.BattleInstanceId,
            playerParticipant.ParticipantId,
            request.SourceCharacterId,
            request.SourceRuntimeEntityId,
            _clock.UtcNow);
        if (!reservation.Succeeded)
        {
            return CreationFailure(request, BattleResultCode.Rejected, reservation.Error.Code);
        }

        var persisted = await _repository.CreateAsync(battle, cancellationToken);
        if (!persisted.Succeeded)
        {
            _reservations.Release(battle.BattleInstanceId, request.SourceCharacterId, _clock.UtcNow);
            return CreationFailure(request, BattleResultCode.PersistenceFailure, persisted.Error.Code);
        }

        _combatAuthority?.Seed(battle.BattleInstanceId, battle.Participants);
        var result = new BattleCreationResult(
            BattleResultCode.Success,
            request.BattleRequestId,
            battle.BattleInstanceId,
            BattleRuntimeHash.SafeId(request.IdempotencyKey),
            "",
            false,
            battle.BattleVersion,
            []);
        await _idempotency.SaveCreationAsync(
            request.IdempotencyKey,
            payloadHash,
            result,
            cancellationToken);
        Publish(
            BattleEventKind.BattleCreated,
            battle.BattleInstanceId,
            request.BattleRequestId,
            null,
            null,
            null,
            BattleState.Active.ToString(),
            "",
            request.CorrelationId);
        foreach (var participant in battle.Participants)
        {
            Publish(
                BattleEventKind.BattleParticipantAdded,
                battle.BattleInstanceId,
                request.BattleRequestId,
                1,
                null,
                participant.ParticipantId,
                participant.ParticipantType.ToString(),
                "",
                request.CorrelationId);
        }

        Publish(
            BattleEventKind.BattleStarted,
            battle.BattleInstanceId,
            request.BattleRequestId,
            1,
            null,
            playerParticipant.ParticipantId,
            BattlePhase.CollectingActions.ToString(),
            "",
            request.CorrelationId);
        AppendAudit(
            battle,
            battle,
            request.BattleRequestId,
            null,
            null,
            playerParticipant,
            null,
            [],
            null,
            result.Code,
            "",
            result.IdempotencySafeId,
            request.SourceSessionId);
        return result;
    }

    public async Task<BattleActionSubmissionResult> SubmitActionAsync(
        BattleActionRequest request,
        CancellationToken cancellationToken)
    {
        var payloadHash = BattleRuntimeHash.Action(request);
        var replay = await _actionSubmissions.FindAsync(
            request.IdempotencyKey,
            payloadHash,
            cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return ActionFailure(request, BattleResultCode.ReplayConflict, "battle.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with
            {
                Code = BattleResultCode.DuplicateCompleted,
                IsDuplicate = true,
                NetworkBytes = []
            };
        }

        using var gate = await _battleLocks.AcquireAsync(request.BattleInstanceId, cancellationToken);
        replay = await _actionSubmissions.FindAsync(
            request.IdempotencyKey,
            payloadHash,
            cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return ActionFailure(request, BattleResultCode.ReplayConflict, "battle.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with
            {
                Code = BattleResultCode.DuplicateCompleted,
                IsDuplicate = true,
                NetworkBytes = []
            };
        }

        var battle = await _repository.GetAsync(request.BattleInstanceId, cancellationToken);
        if (battle is null)
        {
            return ActionFailure(request, BattleResultCode.BattleNotFound, "battle.not_found");
        }

        var committedAction = battle.CompletedRounds
            .Append(battle.CurrentRound)
            .SelectMany(round => round.SubmittedActions)
            .FirstOrDefault(action => action.ActionRequestId == request.ActionRequestId);
        if (committedAction is not null)
        {
            if (!string.Equals(committedAction.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return ActionFailure(
                    request,
                    BattleResultCode.ReplayConflict,
                    "battle.action_request_identity_conflict",
                    battle.BattleVersion);
            }

            var reconciled = new BattleActionSubmissionResult(
                BattleResultCode.DuplicateCompleted,
                request.ActionRequestId,
                battle.BattleInstanceId,
                committedAction.ActionId,
                committedAction.IdempotencySafeId,
                "",
                true,
                battle.BattleVersion,
                committedAction.RoundNumber,
                []);
            await _actionSubmissions.SaveAsync(
                request.IdempotencyKey,
                payloadHash,
                reconciled with { Code = BattleResultCode.Success, IsDuplicate = false },
                cancellationToken);
            return reconciled;
        }

        var validation = ValidateActionRequest(battle, request);
        if (validation is not null)
        {
            Publish(
                BattleEventKind.BattleActionRejected,
                battle.BattleInstanceId,
                battle.BattleRequestId,
                request.RoundNumber,
                request.ActionRequestId,
                request.ParticipantId,
                "Rejected",
                validation.FailureCode,
                request.CorrelationId);
            AppendAudit(
                battle,
                battle,
                battle.BattleRequestId,
                request.RoundNumber,
                request.ActionRequestId,
                battle.Participants.FirstOrDefault(value => value.ParticipantId == request.ParticipantId),
                request.ParticipantId,
                request.TargetParticipantIds,
                request.ActionType,
                validation.Code,
                validation.FailureCode,
                validation.IdempotencySafeId,
                request.SessionId);
            return validation;
        }

        var participant = battle.Participants.Single(value => value.ParticipantId == request.ParticipantId);
        if (battle.CurrentRound.SubmittedActions.Any(value => value.ParticipantId == participant.ParticipantId))
        {
            return ActionFailure(
                request,
                BattleResultCode.ActionAlreadySubmitted,
                "battle.action_already_submitted",
                battle.BattleVersion);
        }

        var submitted = new BattleSubmittedAction(
            Guid.NewGuid(),
            request.ActionRequestId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.ParticipantId,
            request.ActionType,
            BattleCollections.Freeze(request.TargetParticipantIds),
            request.SkillDefinitionId,
            request.ItemReference,
            BattleRuntimeHash.SafeId(request.IdempotencyKey),
            payloadHash,
            BattleActionState.Submitted,
            _clock.UtcNow,
            request.CorrelationId);
        var updatedParticipants = battle.Participants
            .Select(value => value.ParticipantId == participant.ParticipantId
                ? value with
                {
                    HasSubmittedAction = true,
                    CurrentActionState = BattleActionState.Submitted,
                    CombatState = BattleParticipantCombatState.ActionSubmitted
                }
                : value)
            .ToArray();
        var submissions = battle.CurrentRound.SubmittedActions.Append(submitted).ToArray();
        var allReady = battle.CurrentRound.EligibleParticipantIds.All(id =>
            submissions.Any(action => action.ParticipantId == id));
        var updatedRound = battle.CurrentRound with
        {
            State = allReady ? BattleRoundState.ReadyToLock : BattleRoundState.CollectingActions,
            SubmittedActions = BattleCollections.Freeze(submissions),
            RoundVersion = checked(battle.CurrentRound.RoundVersion + 1)
        };
        var updated = battle with
        {
            Participants = BattleCollections.Freeze(updatedParticipants),
            CurrentRound = updatedRound,
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow
        };
        var persisted = await _repository.SaveAsync(updated, battle.BattleVersion, cancellationToken);
        if (!persisted.Succeeded)
        {
            return ActionFailure(
                request,
                persisted.Error.Code == "battle.version_conflict"
                    ? BattleResultCode.VersionConflict
                    : BattleResultCode.PersistenceFailure,
                persisted.Error.Code,
                battle.BattleVersion);
        }

        var result = new BattleActionSubmissionResult(
            BattleResultCode.Success,
            request.ActionRequestId,
            request.BattleInstanceId,
            submitted.ActionId,
            submitted.IdempotencySafeId,
            "",
            false,
            updated.BattleVersion,
            updated.CurrentRoundNumber,
            []);
        await _actionSubmissions.SaveAsync(
            request.IdempotencyKey,
            payloadHash,
            result,
            cancellationToken);
        Publish(
            BattleEventKind.BattleActionSubmitted,
            updated.BattleInstanceId,
            updated.BattleRequestId,
            updated.CurrentRoundNumber,
            submitted.ActionId,
            participant.ParticipantId,
            submitted.ActionType.ToString(),
            "",
            request.CorrelationId);
        AppendAudit(
            battle,
            updated,
            battle.BattleRequestId,
            request.RoundNumber,
            submitted.ActionId,
            participant,
            participant.ParticipantId,
            submitted.TargetParticipantIds,
            submitted.ActionType,
            result.Code,
            "",
            submitted.IdempotencySafeId,
            request.SessionId);
        return result;
    }

    public async Task<BattleRoundResolutionResult> LockAndResolveAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken)
    {
        using var gate = await _battleLocks.AcquireAsync(battleInstanceId, cancellationToken);
        var battle = await _repository.GetAsync(battleInstanceId, cancellationToken);
        if (battle is null)
        {
            return RoundFailure(battleInstanceId, 0, BattleResultCode.BattleNotFound, "battle.not_found");
        }

        if (battle.State is BattleState.Completed or BattleState.Aborted or BattleState.Closed)
        {
            return new BattleRoundResolutionResult(
                BattleResultCode.DuplicateCompleted,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                battle.CurrentRound.ResolutionResults.Count,
                battle.WinnerSide,
                "",
                battle.BattleVersion,
                true,
                []);
        }

        if (battle.CurrentPhase == BattlePhase.ResolvingActions)
        {
            return await ResolveLockedRoundAsync(battle, cancellationToken);
        }

        if (battle.State != BattleState.Active ||
            battle.CurrentPhase != BattlePhase.CollectingActions ||
            battle.CurrentRound.State != BattleRoundState.ReadyToLock)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.InvalidPhase,
                "battle.actions_incomplete",
                battle.BattleVersion);
        }

        var now = _clock.UtcNow;
        var locked = battle.CurrentRound.SubmittedActions
            .Select(action => new BattleLockedAction(
                action.ActionId,
                action.BattleInstanceId,
                action.RoundNumber,
                action.ParticipantId,
                action.ActionType,
                BattleCollections.Freeze(action.TargetParticipantIds),
                action.SkillDefinitionId,
                action.ItemReference,
                action.IdempotencySafeId,
                action.PayloadHash,
                action.SubmittedAtUtc,
                now,
                action.CorrelationId))
            .ToArray();
        var lockCandidateRound = battle.CurrentRound with
        {
            State = BattleRoundState.Locked,
            LockedActions = BattleCollections.Freeze(locked),
            ActionsLockedAtUtc = now,
            RoundVersion = checked(battle.CurrentRound.RoundVersion + 1)
        };
        var order = _failureInjection.Point == BattleFailurePoint.TurnOrderPolicy
            ? OperationResult<BattleTurnOrderResult>.Failure(
                "battle.turn_order_failure",
                "Injected turn order policy failure.")
            : _turnOrder.Resolve(battle, lockCandidateRound, now);
        if (!order.Succeeded || order.Value is null)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                order.Error.Code == "battle.turn_order_evidence_blocked"
                    ? BattleResultCode.TurnOrderEvidenceBlocked
                    : BattleResultCode.BattlePolicyBlocked,
                order.Error.Code,
                battle.BattleVersion);
        }

        var orderedIds = order.Value.OrderedActionIds;
        var orderedLocked = orderedIds
            .Select(id => locked.Single(action => action.ActionId == id))
            .ToArray();
        var participants = battle.Participants
            .Select(value => value.IsAlive
                ? value with
                {
                    CurrentActionState = BattleActionState.Locked,
                    CombatState = BattleParticipantCombatState.ActionLocked
                }
                : value)
            .ToArray();
        var resolvingRound = lockCandidateRound with
        {
            State = BattleRoundState.Resolving,
            LockedActions = BattleCollections.Freeze(orderedLocked),
            TurnOrder = BattleCollections.Freeze(order.Value.OrderedParticipantIds),
            RoundVersion = checked(lockCandidateRound.RoundVersion + 1)
        };
        var resolving = battle with
        {
            CurrentPhase = BattlePhase.ResolvingActions,
            CurrentResolutionIndex = 0,
            Participants = BattleCollections.Freeze(participants),
            CurrentRound = resolvingRound,
            BattleVersion = checked(battle.BattleVersion + 1),
            TurnOrderPolicyStatus = order.Value.PolicyStatus,
            UpdatedAtUtc = now
        };
        var persisted = await _repository.SaveAsync(resolving, battle.BattleVersion, cancellationToken);
        if (!persisted.Succeeded)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.PersistenceFailure,
                persisted.Error.Code,
                battle.BattleVersion);
        }

        Publish(
            BattleEventKind.BattleActionsLocked,
            resolving.BattleInstanceId,
            resolving.BattleRequestId,
            resolving.CurrentRoundNumber,
            null,
            null,
            BattlePhase.ActionsLocked.ToString(),
            "",
            resolving.CorrelationId);
        Publish(
            BattleEventKind.BattleTurnOrderResolved,
            resolving.BattleInstanceId,
            resolving.BattleRequestId,
            resolving.CurrentRoundNumber,
            null,
            null,
            string.Join(',', order.Value.OrderedParticipantIds),
            "",
            resolving.CorrelationId);
        return await ResolveLockedRoundAsync(resolving, cancellationToken);
    }

    public Task<BattleReconnectResult> DisconnectAsync(
        Guid battleInstanceId,
        string sessionId,
        long characterId,
        CancellationToken cancellationToken) =>
        ChangeConnectionAsync(battleInstanceId, sessionId, characterId, false, cancellationToken);

    public Task<BattleReconnectResult> ReconnectAsync(
        Guid battleInstanceId,
        string sessionId,
        long characterId,
        CancellationToken cancellationToken) =>
        ChangeConnectionAsync(battleInstanceId, sessionId, characterId, true, cancellationToken);

    public async Task<BattleRoundResolutionResult> AbortAsync(
        Guid battleInstanceId,
        string reason,
        CancellationToken cancellationToken)
    {
        using var gate = await _battleLocks.AcquireAsync(battleInstanceId, cancellationToken);
        var battle = await _repository.GetAsync(battleInstanceId, cancellationToken);
        if (battle is null)
        {
            return RoundFailure(battleInstanceId, 0, BattleResultCode.BattleNotFound, "battle.not_found");
        }

        if (battle.State is BattleState.Completed or BattleState.Aborted or BattleState.Closed)
        {
            return new BattleRoundResolutionResult(
                BattleResultCode.DuplicateCompleted,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                battle.CurrentRound.ResolutionResults.Count,
                battle.WinnerSide,
                "",
                battle.BattleVersion,
                true,
                []);
        }

        var updated = battle with
        {
            State = BattleState.Aborted,
            CurrentPhase = BattlePhase.Aborted,
            CompletionReason = string.Equals(reason, "Recovery", StringComparison.OrdinalIgnoreCase)
                ? BattleCompletionReason.RecoveryAbort
                : BattleCompletionReason.AdministrativeAbort,
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow,
            CompletedAtUtc = _clock.UtcNow
        };
        var saved = await _repository.SaveAsync(updated, battle.BattleVersion, cancellationToken);
        if (!saved.Succeeded)
        {
            return RoundFailure(
                battleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.PersistenceFailure,
                saved.Error.Code,
                battle.BattleVersion);
        }

        var released = ReleaseReservations(updated);
        if (!released.Succeeded)
        {
            return RoundFailure(
                battleInstanceId,
                updated.CurrentRoundNumber,
                BattleResultCode.RecoveryRequired,
                released.Error.Code,
                updated.BattleVersion);
        }

        Publish(
            BattleEventKind.BattleAborted,
            updated.BattleInstanceId,
            updated.BattleRequestId,
            updated.CurrentRoundNumber,
            null,
            null,
            updated.CompletionReason.ToString(),
            "",
            updated.CorrelationId);
        return new BattleRoundResolutionResult(
            BattleResultCode.Success,
            battleInstanceId,
            updated.CurrentRoundNumber,
            updated.CurrentRound.ResolutionResults.Count,
            updated.WinnerSide,
            "",
            updated.BattleVersion,
            false,
            []);
    }

    public async Task<BattleRoundResolutionResult> RecoverAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken)
    {
        using var gate = await _battleLocks.AcquireAsync(battleInstanceId, cancellationToken);
        var battle = await _repository.GetAsync(battleInstanceId, cancellationToken);
        if (battle is null)
        {
            return RoundFailure(battleInstanceId, 0, BattleResultCode.BattleNotFound, "battle.not_found");
        }

        Publish(
            BattleEventKind.BattleRecoveryStarted,
            battle.BattleInstanceId,
            battle.BattleRequestId,
            battle.CurrentRoundNumber,
            null,
            null,
            battle.CurrentPhase.ToString(),
            "",
            battle.CorrelationId);
        if (_failureInjection.Point == BattleFailurePoint.Recovery)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.RecoveryRequired,
                "battle.recovery_failed",
                battle.BattleVersion);
        }

        _combatAuthority?.Seed(battle.BattleInstanceId, battle.Participants);
        BattleRoundResolutionResult result;
        if (battle.CurrentPhase == BattlePhase.ResolvingActions)
        {
            result = await ResolveLockedRoundAsync(battle, cancellationToken);
        }
        else if (battle.CurrentPhase == BattlePhase.RecoveryRequired &&
                 battle.WinnerSide == BattleWinnerSide.None &&
                 battle.CurrentRound.LockedActions.Count > battle.CurrentResolutionIndex)
        {
            result = await ResolveLockedRoundAsync(
                battle with
                {
                    State = BattleState.Active,
                    CurrentPhase = BattlePhase.ResolvingActions,
                    RecoveryState = BattleRecoveryState.Recovering,
                    CurrentRound = battle.CurrentRound with { State = BattleRoundState.Resolving }
                },
                cancellationToken);
        }
        else if (battle.CurrentPhase is BattlePhase.RewardFinalization or BattlePhase.RecoveryRequired &&
                 battle.WinnerSide != BattleWinnerSide.None)
        {
            result = await CompleteBattleAsync(battle, cancellationToken);
        }
        else if (battle.State == BattleState.Completed)
        {
            var released = ReleaseReservations(battle);
            result = released.Succeeded
                ? new BattleRoundResolutionResult(
                    BattleResultCode.DuplicateCompleted,
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    battle.CurrentRound.ResolutionResults.Count,
                    battle.WinnerSide,
                    "",
                    battle.BattleVersion,
                    true,
                    [])
                : RoundFailure(
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    BattleResultCode.RecoveryRequired,
                    released.Error.Code,
                    battle.BattleVersion);
        }
        else
        {
            result = new BattleRoundResolutionResult(
                BattleResultCode.Success,
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                battle.CurrentRound.ResolutionResults.Count,
                battle.WinnerSide,
                "",
                battle.BattleVersion,
                false,
                []);
        }

        Publish(
            result.Succeeded ? BattleEventKind.BattleRecoveryCompleted : BattleEventKind.BattleRecoveryRequired,
            battle.BattleInstanceId,
            battle.BattleRequestId,
            battle.CurrentRoundNumber,
            null,
            null,
            result.Succeeded ? "Recovered" : "RecoveryRequired",
            result.FailureCode,
            battle.CorrelationId);
        return result;
    }

    private async Task<BattleRoundResolutionResult> ResolveLockedRoundAsync(
        BattleInstance initial,
        CancellationToken cancellationToken)
    {
        var battle = initial;
        if (battle.CurrentResolutionIndex == 0)
        {
            var roundOpening = await ExecuteStatusPhaseAsync(
                battle,
                StatusTriggerPhase.RoundOpening,
                null,
                battle.CorrelationId,
                cancellationToken);
            if (roundOpening.FailureCode.Length > 0)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    roundOpening.FailureCode,
                    cancellationToken);
            }

            battle = roundOpening.Battle;
        }

        for (var index = battle.CurrentResolutionIndex;
             index < battle.CurrentRound.LockedActions.Count;
             index++)
        {
            var action = battle.CurrentRound.LockedActions[index];
            if (battle.CurrentRound.ResolutionResults.Any(value => value.ActionId == action.ActionId))
            {
                battle = battle with
                {
                    CurrentResolutionIndex = index + 1,
                    CurrentRound = battle.CurrentRound with { CurrentResolutionIndex = index + 1 }
                };
                continue;
            }

            Publish(
                BattleEventKind.BattleActionResolving,
                battle.BattleInstanceId,
                battle.BattleRequestId,
                battle.CurrentRoundNumber,
                action.ActionId,
                action.ParticipantId,
                action.ActionType.ToString(),
                "",
                action.CorrelationId);
            var beforeValidation = await ExecuteStatusPhaseAsync(
                battle,
                StatusTriggerPhase.BeforeActionValidation,
                action.ActionId,
                action.CorrelationId,
                cancellationToken);
            if (beforeValidation.FailureCode.Length > 0)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    beforeValidation.FailureCode,
                    cancellationToken);
            }

            battle = beforeValidation.Battle;
            var beforeResolution = await ExecuteStatusPhaseAsync(
                battle,
                StatusTriggerPhase.BeforeActionResolution,
                action.ActionId,
                action.CorrelationId,
                cancellationToken);
            if (beforeResolution.FailureCode.Length > 0)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    beforeResolution.FailureCode,
                    cancellationToken);
            }

            battle = beforeResolution.Battle;
            var result = await _actions.ResolveAsync(battle, action, index, cancellationToken);
            if (_failureInjection.Point == BattleFailurePoint.RuntimeStateUpdate)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    "battle.runtime_state_update_failed",
                    cancellationToken);
            }

            var before = battle;
            var participants = ApplyActionResult(battle.Participants, action, result);
            if (!AuthorityMatches(battle.BattleInstanceId, participants, result, action))
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    "battle.combat_authority_mismatch",
                    cancellationToken);
            }

            if (_failureInjection.Point == BattleFailurePoint.ActionResultPersistence)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    "battle.action_result_persistence_failed",
                    cancellationToken);
            }

            var results = battle.CurrentRound.ResolutionResults.Append(result).ToArray();
            var updatedRound = battle.CurrentRound with
            {
                CurrentResolutionIndex = index + 1,
                ResolutionResults = BattleCollections.Freeze(results),
                RoundVersion = checked(battle.CurrentRound.RoundVersion + 1)
            };
            battle = battle with
            {
                Participants = BattleCollections.Freeze(participants),
                CurrentResolutionIndex = index + 1,
                CurrentRound = updatedRound,
                BattleVersion = checked(battle.BattleVersion + 1),
                UpdatedAtUtc = _clock.UtcNow
            };
            var saved = await _repository.SaveAsync(battle, before.BattleVersion, cancellationToken);
            if (!saved.Succeeded)
            {
                return RoundFailure(
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    BattleResultCode.RecoveryRequired,
                    saved.Error.Code,
                    before.BattleVersion);
            }

            var eventKind = result.Result is BattleActionResolutionCode.SkippedAttackerDead or
                BattleActionResolutionCode.SkippedTargetDead
                ? BattleEventKind.BattleActionSkipped
                : BattleEventKind.BattleActionResolved;
            Publish(
                eventKind,
                battle.BattleInstanceId,
                battle.BattleRequestId,
                battle.CurrentRoundNumber,
                result.ActionId,
                result.ParticipantId,
                result.Result.ToString(),
                result.FailureCode,
                action.CorrelationId);
            IReadOnlyList<BattleParticipantMutation> actionMutations = result.ParticipantMutations is { Count: > 0 }
                ? result.ParticipantMutations
                : result.TargetParticipantIds.Count == 1
                    ? new[]
                    {
                        new BattleParticipantMutation(
                            result.TargetParticipantIds[0],
                            result.Damage,
                            0,
                            result.HpBefore,
                            result.HpAfter,
                            result.TargetDefeated,
                            result.RuntimeVersionBefore,
                            result.RuntimeVersionAfter,
                            result.FailureCode)
                    }
                    : [];
            foreach (var mutation in actionMutations)
            {
                if (mutation.Damage > 0)
                {
                    Publish(
                        BattleEventKind.BattleParticipantDamaged,
                        battle.BattleInstanceId,
                        battle.BattleRequestId,
                        battle.CurrentRoundNumber,
                        result.ActionId,
                        mutation.ParticipantId,
                        $"Hp={mutation.HpAfter}",
                        "",
                        action.CorrelationId);
                }

                if (mutation.Heal > 0)
                {
                    Publish(
                        BattleEventKind.BattleParticipantHealed,
                        battle.BattleInstanceId,
                        battle.BattleRequestId,
                        battle.CurrentRoundNumber,
                        result.ActionId,
                        mutation.ParticipantId,
                        $"Hp={mutation.HpAfter}",
                        "",
                        action.CorrelationId);
                }

                if (mutation.Defeated)
                {
                    Publish(
                        BattleEventKind.BattleParticipantDefeated,
                        battle.BattleInstanceId,
                        battle.BattleRequestId,
                        battle.CurrentRoundNumber,
                        result.ActionId,
                        mutation.ParticipantId,
                        BattleParticipantCombatState.Defeated.ToString(),
                        "",
                        action.CorrelationId);
                }
            }

            var actor = before.Participants.First(value => value.ParticipantId == action.ParticipantId);
            AppendAudit(
                before,
                battle,
                battle.BattleRequestId,
                battle.CurrentRoundNumber,
                result.ActionId,
                actor,
                result.ParticipantId,
                result.TargetParticipantIds,
                result.ActionType,
                result.Result is BattleActionResolutionCode.Success or BattleActionResolutionCode.Passed
                    ? BattleResultCode.Success
                    : BattleResultCode.Rejected,
                result.FailureCode,
                action.IdempotencySafeId,
                actor.SessionId);

            var hasDamage = actionMutations.Any(value => value.Damage > 0);
            var hasHealing = actionMutations.Any(value => value.Heal > 0);
            if (hasDamage)
            {
                var afterDamage = await ExecuteStatusPhaseAsync(
                    battle,
                    StatusTriggerPhase.AfterDamage,
                    action.ActionId,
                    action.CorrelationId,
                    cancellationToken);
                if (afterDamage.FailureCode.Length > 0)
                {
                    return await MarkRecoveryRequiredAsync(
                        battle,
                        afterDamage.FailureCode,
                        cancellationToken);
                }

                battle = afterDamage.Battle;
            }

            if (hasHealing)
            {
                var afterHealing = await ExecuteStatusPhaseAsync(
                    battle,
                    StatusTriggerPhase.AfterHealing,
                    action.ActionId,
                    action.CorrelationId,
                    cancellationToken);
                if (afterHealing.FailureCode.Length > 0)
                {
                    return await MarkRecoveryRequiredAsync(
                        battle,
                        afterHealing.FailureCode,
                        cancellationToken);
                }

                battle = afterHealing.Battle;
            }

            var onApply = await ExecuteStatusPhaseAsync(
                battle,
                StatusTriggerPhase.OnApply,
                action.ActionId,
                action.CorrelationId,
                cancellationToken);
            if (onApply.FailureCode.Length > 0)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    onApply.FailureCode,
                    cancellationToken);
            }

            battle = onApply.Battle;
            var afterResolution = await ExecuteStatusPhaseAsync(
                battle,
                StatusTriggerPhase.AfterActionResolution,
                action.ActionId,
                action.CorrelationId,
                cancellationToken);
            if (afterResolution.FailureCode.Length > 0)
            {
                return await MarkRecoveryRequiredAsync(
                    battle,
                    afterResolution.FailureCode,
                    cancellationToken);
            }

            battle = afterResolution.Battle;
        }

        return await CloseRoundAsync(battle, cancellationToken);
    }

    private async Task<BattleRoundResolutionResult> CloseRoundAsync(
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        if (_failureInjection.Point == BattleFailurePoint.RoundClose)
        {
            return await MarkRecoveryRequiredAsync(battle, "battle.round_close_failed", cancellationToken);
        }

        var roundClosing = await ExecuteStatusPhaseAsync(
            battle,
            StatusTriggerPhase.RoundClosing,
            null,
            battle.CorrelationId,
            cancellationToken);
        if (roundClosing.FailureCode.Length > 0)
        {
            return await MarkRecoveryRequiredAsync(
                battle,
                roundClosing.FailureCode,
                cancellationToken);
        }

        battle = roundClosing.Battle;
        var victory = _failureInjection.Point == BattleFailurePoint.VictoryResolution
            ? null
            : _victory.Evaluate(battle.Participants);
        if (victory is null)
        {
            return await MarkRecoveryRequiredAsync(battle, "battle.victory_resolution_failed", cancellationToken);
        }

        var now = _clock.UtcNow;
        var completedRound = battle.CurrentRound with
        {
            State = BattleRoundState.Completed,
            CompletedAtUtc = now,
            RoundVersion = checked(battle.CurrentRound.RoundVersion + 1)
        };
        if (!victory.IsComplete)
        {
            var nextParticipants = battle.Participants
                .Select(value => value.IsAlive
                    ? value with
                    {
                        HasSubmittedAction = false,
                        CurrentActionState = null,
                        CombatState = value.IsConnected
                            ? BattleParticipantCombatState.WaitingForAction
                            : BattleParticipantCombatState.Disconnected
                    }
                    : value)
                .ToArray();
            var nextRoundNumber = checked(battle.CurrentRoundNumber + 1);
            var nextRound = new BattleRound(
                battle.BattleInstanceId,
                nextRoundNumber,
                BattleRoundState.CollectingActions,
                BattleCollections.Freeze(nextParticipants.Where(value => value.IsAlive).Select(value => value.ParticipantId)),
                [],
                [],
                [],
                0,
                [],
                now,
                null,
                null,
                1,
                battle.CorrelationId);
            var next = battle with
            {
                CurrentPhase = BattlePhase.CollectingActions,
                CurrentRoundNumber = nextRoundNumber,
                CurrentResolutionIndex = 0,
                Participants = BattleCollections.Freeze(nextParticipants),
                CompletedRounds = BattleCollections.Freeze(battle.CompletedRounds.Append(completedRound)),
                CurrentRound = nextRound,
                BattleVersion = checked(battle.BattleVersion + 1),
                UpdatedAtUtc = now
            };
            var saved = await _repository.SaveAsync(next, battle.BattleVersion, cancellationToken);
            if (!saved.Succeeded)
            {
                return RoundFailure(
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    BattleResultCode.RecoveryRequired,
                    saved.Error.Code,
                    battle.BattleVersion);
            }

            Publish(
                BattleEventKind.RoundCompleted,
                next.BattleInstanceId,
                next.BattleRequestId,
                completedRound.RoundNumber,
                null,
                null,
                "Completed",
                "",
                next.CorrelationId);
            Publish(
                BattleEventKind.RoundStarted,
                next.BattleInstanceId,
                next.BattleRequestId,
                next.CurrentRoundNumber,
                null,
                null,
                "CollectingActions",
                "",
                next.CorrelationId);
            return new BattleRoundResolutionResult(
                BattleResultCode.Success,
                next.BattleInstanceId,
                completedRound.RoundNumber,
                completedRound.ResolutionResults.Count,
                BattleWinnerSide.None,
                "",
                next.BattleVersion,
                false,
                []);
        }

        var resolvingVictory = battle with
        {
            State = BattleState.Completing,
            CurrentPhase = BattlePhase.VictoryResolution,
            CompletedRounds = BattleCollections.Freeze(battle.CompletedRounds.Append(completedRound)),
            CurrentRound = completedRound,
            WinnerSide = victory.WinnerSide,
            CompletionReason = victory.CompletionReason,
            VictoryPolicyStatus = victory.PolicyStatus,
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = now
        };
        var victorySaved = await _repository.SaveAsync(
            resolvingVictory,
            battle.BattleVersion,
            cancellationToken);
        if (!victorySaved.Succeeded)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.RecoveryRequired,
                victorySaved.Error.Code,
                battle.BattleVersion);
        }

        Publish(
            BattleEventKind.BattleVictoryResolved,
            resolvingVictory.BattleInstanceId,
            resolvingVictory.BattleRequestId,
            resolvingVictory.CurrentRoundNumber,
            null,
            null,
            victory.WinnerSide.ToString(),
            "",
            resolvingVictory.CorrelationId);
        return await CompleteBattleAsync(resolvingVictory, cancellationToken);
    }

    private async Task<BattleRoundResolutionResult> CompleteBattleAsync(
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var plan = _rewardPolicy.CreatePlan(battle, now);
        var rewardState = plan.Grants.Count == 0
            ? BattleRewardState.NotRequired
            : BattleRewardState.Committing;
        var rewardPhase = battle with
        {
            CurrentPhase = BattlePhase.RewardFinalization,
            RewardState = rewardState,
            RewardPolicyStatus = plan.PolicyStatus,
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = now
        };
        var phaseSaved = await _repository.SaveAsync(rewardPhase, battle.BattleVersion, cancellationToken);
        if (!phaseSaved.Succeeded)
        {
            return RoundFailure(
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                BattleResultCode.RecoveryRequired,
                phaseSaved.Error.Code,
                battle.BattleVersion);
        }

        if (plan.Grants.Count > 0)
        {
            Publish(
                BattleEventKind.BattleRewardFinalizationStarted,
                rewardPhase.BattleInstanceId,
                rewardPhase.BattleRequestId,
                rewardPhase.CurrentRoundNumber,
                null,
                null,
                "Committing",
                "",
                rewardPhase.CorrelationId);
            var reward = await _rewards.FinalizeAsync(plan, cancellationToken);
            if (!reward.Committed)
            {
                var recovery = rewardPhase with
                {
                    State = BattleState.RecoveryRequired,
                    CurrentPhase = BattlePhase.RecoveryRequired,
                    RewardState = BattleRewardState.RecoveryRequired,
                    RecoveryState = BattleRecoveryState.RecoveryRequired,
                    BattleVersion = checked(rewardPhase.BattleVersion + 1),
                    UpdatedAtUtc = _clock.UtcNow
                };
                await _repository.SaveAsync(recovery, rewardPhase.BattleVersion, cancellationToken);
                Publish(
                    BattleEventKind.BattleRewardFailed,
                    recovery.BattleInstanceId,
                    recovery.BattleRequestId,
                    recovery.CurrentRoundNumber,
                    null,
                    null,
                    "RecoveryRequired",
                    reward.FailureCode,
                    recovery.CorrelationId);
                return RoundFailure(
                    recovery.BattleInstanceId,
                    recovery.CurrentRoundNumber,
                    BattleResultCode.RewardFailure,
                    reward.FailureCode,
                    recovery.BattleVersion);
            }

            rewardPhase = rewardPhase with
            {
                RewardState = BattleRewardState.Committed,
                BattleVersion = checked(rewardPhase.BattleVersion + 1),
                UpdatedAtUtc = _clock.UtcNow
            };
            var rewardSaved = await _repository.SaveAsync(
                rewardPhase,
                rewardPhase.BattleVersion - 1,
                cancellationToken);
            if (!rewardSaved.Succeeded)
            {
                return RoundFailure(
                    rewardPhase.BattleInstanceId,
                    rewardPhase.CurrentRoundNumber,
                    BattleResultCode.RecoveryRequired,
                    rewardSaved.Error.Code,
                    rewardPhase.BattleVersion - 1);
            }

            Publish(
                BattleEventKind.BattleRewardCommitted,
                rewardPhase.BattleInstanceId,
                rewardPhase.BattleRequestId,
                rewardPhase.CurrentRoundNumber,
                null,
                null,
                "Committed",
                "",
                rewardPhase.CorrelationId);
        }

        var battleCompletedStatuses = await ExecuteStatusPhaseAsync(
            rewardPhase,
            StatusTriggerPhase.BattleCompleted,
            null,
            rewardPhase.CorrelationId,
            cancellationToken);
        if (battleCompletedStatuses.FailureCode.Length > 0)
        {
            return await MarkRecoveryRequiredAsync(
                rewardPhase,
                battleCompletedStatuses.FailureCode,
                cancellationToken);
        }

        rewardPhase = battleCompletedStatuses.Battle;
        var release = ReleaseReservations(rewardPhase);
        if (!release.Succeeded)
        {
            return await MarkRecoveryRequiredAsync(
                rewardPhase,
                release.Error.Code,
                cancellationToken);
        }

        var completed = rewardPhase with
        {
            State = BattleState.Completed,
            CurrentPhase = BattlePhase.Completed,
            RecoveryState = BattleRecoveryState.NotRequired,
            BattleVersion = checked(rewardPhase.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow,
            CompletedAtUtc = _clock.UtcNow
        };
        var completedSaved = await _repository.SaveAsync(
            completed,
            rewardPhase.BattleVersion,
            cancellationToken);
        if (!completedSaved.Succeeded)
        {
            return RoundFailure(
                completed.BattleInstanceId,
                completed.CurrentRoundNumber,
                BattleResultCode.RecoveryRequired,
                completedSaved.Error.Code,
                rewardPhase.BattleVersion);
        }

        Publish(
            BattleEventKind.BattleCompleted,
            completed.BattleInstanceId,
            completed.BattleRequestId,
            completed.CurrentRoundNumber,
            null,
            null,
            completed.WinnerSide.ToString(),
            "",
            completed.CorrelationId);
        return new BattleRoundResolutionResult(
            BattleResultCode.Success,
            completed.BattleInstanceId,
            completed.CurrentRoundNumber,
            completed.CurrentRound.ResolutionResults.Count,
            completed.WinnerSide,
            "",
            completed.BattleVersion,
            false,
            []);
    }

    private async Task<BattleReconnectResult> ChangeConnectionAsync(
        Guid battleInstanceId,
        string sessionId,
        long characterId,
        bool connected,
        CancellationToken cancellationToken)
    {
        using var gate = await _battleLocks.AcquireAsync(battleInstanceId, cancellationToken);
        var battle = await _repository.GetAsync(battleInstanceId, cancellationToken);
        if (battle is null)
        {
            return ReconnectFailure(battleInstanceId, BattleResultCode.BattleNotFound, "battle.not_found");
        }

        if (battle.State is BattleState.Completed or BattleState.Aborted or BattleState.Closed)
        {
            return ReconnectFailure(
                battleInstanceId,
                BattleResultCode.BattleAlreadyCompleted,
                "battle.already_completed",
                battle);
        }

        var participant = battle.Participants.FirstOrDefault(value =>
            value.CharacterId == characterId &&
            value.ParticipantType == BattleParticipantType.Player);
        if (participant is null)
        {
            return ReconnectFailure(
                battleInstanceId,
                BattleResultCode.OwnershipMismatch,
                "battle.ownership_mismatch",
                battle);
        }

        if (!connected &&
            !string.Equals(participant.SessionId, sessionId, StringComparison.Ordinal))
        {
            return ReconnectFailure(
                battleInstanceId,
                BattleResultCode.InvalidSession,
                "battle.old_session_rejected",
                battle,
                participant.ParticipantId);
        }

        if (connected)
        {
            var binding = _sessions.Get(sessionId);
            if (!binding.Succeeded ||
                binding.Value?.Session.CharacterId != characterId ||
                !binding.Value.Session.IsAuthenticated)
            {
                return ReconnectFailure(
                    battleInstanceId,
                    BattleResultCode.InvalidSession,
                    "battle.invalid_session",
                    battle,
                    participant.ParticipantId);
            }
        }

        if (participant.IsConnected == connected &&
            (!connected || string.Equals(participant.SessionId, sessionId, StringComparison.Ordinal)))
        {
            return new BattleReconnectResult(
                BattleResultCode.Success,
                battle.BattleInstanceId,
                participant.ParticipantId,
                battle.State,
                battle.CurrentPhase,
                battle.CurrentRoundNumber,
                "",
                battle.BattleVersion,
                []);
        }

        var updatedParticipant = participant with
        {
            SessionId = connected ? sessionId : participant.SessionId,
            IsConnected = connected,
            CombatState = participant.IsAlive
                ? connected
                    ? participant.HasSubmittedAction
                        ? BattleParticipantCombatState.ActionSubmitted
                        : BattleParticipantCombatState.WaitingForAction
                    : BattleParticipantCombatState.Disconnected
                : participant.CombatState
        };
        var updated = battle with
        {
            Participants = BattleCollections.Freeze(battle.Participants.Select(value =>
                value.ParticipantId == participant.ParticipantId ? updatedParticipant : value)),
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow
        };
        var saved = await _repository.SaveAsync(updated, battle.BattleVersion, cancellationToken);
        if (!saved.Succeeded)
        {
            return ReconnectFailure(
                battleInstanceId,
                BattleResultCode.PersistenceFailure,
                saved.Error.Code,
                battle,
                participant.ParticipantId);
        }

        Publish(
            connected
                ? BattleEventKind.BattleParticipantReconnected
                : BattleEventKind.BattleParticipantDisconnected,
            updated.BattleInstanceId,
            updated.BattleRequestId,
            updated.CurrentRoundNumber,
            null,
            participant.ParticipantId,
            connected ? "Connected" : "Disconnected",
            "",
            updated.CorrelationId);
        return new BattleReconnectResult(
            BattleResultCode.Success,
            updated.BattleInstanceId,
            participant.ParticipantId,
            updated.State,
            updated.CurrentPhase,
            updated.CurrentRoundNumber,
            "",
            updated.BattleVersion,
            []);
    }

    private BattleActionSubmissionResult? ValidateActionRequest(
        BattleInstance battle,
        BattleActionRequest request)
    {
        if (battle.State != BattleState.Active)
        {
            return ActionFailure(
                request,
                battle.State is BattleState.Completed or BattleState.Aborted or BattleState.Closed
                    ? BattleResultCode.BattleAlreadyCompleted
                    : BattleResultCode.InvalidPhase,
                "battle.not_active",
                battle.BattleVersion);
        }

        if (battle.CurrentPhase != BattlePhase.CollectingActions ||
            battle.CurrentRound.State is BattleRoundState.Locked or BattleRoundState.Resolving)
        {
            return ActionFailure(request, BattleResultCode.ActionLocked, "battle.action_locked", battle.BattleVersion);
        }

        if (request.BattleVersion != battle.BattleVersion)
        {
            return ActionFailure(request, BattleResultCode.VersionConflict, "battle.version_conflict", battle.BattleVersion);
        }

        if (request.RoundNumber != battle.CurrentRoundNumber)
        {
            return ActionFailure(request, BattleResultCode.InvalidRound, "battle.invalid_round", battle.BattleVersion);
        }

        var participant = battle.Participants.FirstOrDefault(value => value.ParticipantId == request.ParticipantId);
        if (participant is null)
        {
            return ActionFailure(
                request,
                BattleResultCode.ParticipantNotFound,
                "battle.participant_not_found",
                battle.BattleVersion);
        }

        if (!participant.IsAlive)
        {
            return ActionFailure(request, BattleResultCode.ParticipantDead, "battle.participant_dead", battle.BattleVersion);
        }

        if (participant.ParticipantType == BattleParticipantType.Player)
        {
            if (participant.CharacterId != request.CharacterId)
            {
                return ActionFailure(
                    request,
                    BattleResultCode.OwnershipMismatch,
                    "battle.ownership_mismatch",
                    battle.BattleVersion);
            }

            if (!participant.IsConnected ||
                !string.Equals(participant.SessionId, request.SessionId, StringComparison.Ordinal))
            {
                return ActionFailure(request, BattleResultCode.InvalidSession, "battle.old_session_rejected", battle.BattleVersion);
            }
        }
        else if (request.Source is not BattleRequestSource.TrustedInternalTest and not BattleRequestSource.Scripted ||
                 request.CharacterId != 0)
        {
            return ActionFailure(
                request,
                BattleResultCode.OwnershipMismatch,
                "battle.system_participant_not_trusted",
                battle.BattleVersion);
        }

        if (request.ActionType is BattleActionType.Item or
            BattleActionType.Flee or BattleActionType.SystemAction or
            BattleActionType.Unknown)
        {
            return ActionFailure(
                request,
                BattleResultCode.UnsupportedAction,
                "battle.action_unsupported",
                battle.BattleVersion);
        }

        if (request.ActionType == BattleActionType.Skill &&
            request.SkillDefinitionId is null)
        {
            return ActionFailure(
                request,
                BattleResultCode.UnsupportedAction,
                "battle.action_unsupported",
                battle.BattleVersion);
        }

        if (request.ActionType == BattleActionType.Skill &&
            (request.SkillDefinitionId <= 0 || request.ItemReference is not null))
        {
            return ActionFailure(
                request,
                BattleResultCode.UnsupportedAction,
                "skill.intent_invalid",
                battle.BattleVersion);
        }

        if (request.ActionType == BattleActionType.Pass && request.TargetParticipantIds.Count != 0)
        {
            return ActionFailure(request, BattleResultCode.InvalidTarget, "battle.pass_target_invalid", battle.BattleVersion);
        }

        if (request.ActionType == BattleActionType.Defend && request.TargetParticipantIds.Count != 0)
        {
            return ActionFailure(request, BattleResultCode.InvalidTarget, "battle.defend_target_invalid", battle.BattleVersion);
        }

        if (request.ActionType == BattleActionType.BasicAttack)
        {
            if (request.TargetParticipantIds.Count != 1)
            {
                return ActionFailure(request, BattleResultCode.InvalidTarget, "battle.target_invalid", battle.BattleVersion);
            }

            var target = battle.Participants.FirstOrDefault(value =>
                value.ParticipantId == request.TargetParticipantIds[0]);
            if (target is null || target.Side == participant.Side)
            {
                return ActionFailure(request, BattleResultCode.InvalidTarget, "battle.target_invalid", battle.BattleVersion);
            }

            if (!target.IsAlive)
            {
                return ActionFailure(request, BattleResultCode.TargetDead, "battle.target_dead", battle.BattleVersion);
            }
        }

        return null;
    }

    private IReadOnlyList<BattleParticipant> ApplyActionResult(
        IReadOnlyList<BattleParticipant> participants,
        BattleLockedAction action,
        BattleActionResult result)
    {
        var now = _clock.UtcNow;
        var mutations = result.ParticipantMutations is { Count: > 0 }
            ? result.ParticipantMutations.ToDictionary(value => value.ParticipantId)
            : result.Result == BattleActionResolutionCode.Success &&
              result.TargetParticipantIds.Count == 1
                ? new[]
                {
                    new BattleParticipantMutation(
                        result.TargetParticipantIds[0],
                        result.Damage,
                        0,
                        result.HpBefore,
                        result.HpAfter,
                        result.TargetDefeated,
                        result.RuntimeVersionBefore,
                        result.RuntimeVersionAfter,
                        result.FailureCode)
                }.ToDictionary(value => value.ParticipantId)
                : new Dictionary<Guid, BattleParticipantMutation>();
        return BattleCollections.Freeze(participants.Select(participant =>
        {
            if (participant.ParticipantId == action.ParticipantId)
            {
                var updatedActor = participant with
                {
                    CurrentActionState = result.Result is BattleActionResolutionCode.Success or BattleActionResolutionCode.Passed
                        ? BattleActionState.Resolved
                        : BattleActionState.Skipped,
                    CombatState = participant.IsAlive
                        ? BattleParticipantCombatState.Ready
                        : participant.CombatState
                };
                if (result.Result == BattleActionResolutionCode.Success &&
                    mutations.TryGetValue(participant.ParticipantId, out var actorMutation))
                {
                    updatedActor = updatedActor with
                    {
                        CurrentHp = actorMutation.HpAfter,
                        IsAlive = !actorMutation.Defeated,
                        CombatState = actorMutation.Defeated
                            ? BattleParticipantCombatState.Defeated
                            : actorMutation.Damage > 0
                                ? BattleParticipantCombatState.Hit
                                : BattleParticipantCombatState.Ready,
                        RuntimeVersion = actorMutation.RuntimeVersionAfter,
                        DefeatedAtUtc = actorMutation.Defeated ? now : participant.DefeatedAtUtc
                    };
                }

                return updatedActor;
            }

            if (result.Result == BattleActionResolutionCode.Success &&
                mutations.TryGetValue(participant.ParticipantId, out var mutation))
            {
                return participant with
                {
                    CurrentHp = mutation.HpAfter,
                    IsAlive = !mutation.Defeated,
                    CombatState = mutation.Defeated
                        ? BattleParticipantCombatState.Defeated
                        : mutation.Damage > 0
                            ? BattleParticipantCombatState.Hit
                            : participant.CombatState,
                    RuntimeVersion = mutation.RuntimeVersionAfter,
                    DefeatedAtUtc = mutation.Defeated ? now : participant.DefeatedAtUtc
                };
            }

            return participant;
        }));
    }

    private bool AuthorityMatches(
        Guid battleInstanceId,
        IReadOnlyList<BattleParticipant> participants,
        BattleActionResult result,
        BattleLockedAction action)
    {
        if (_combatAuthority is null ||
            result.Result != BattleActionResolutionCode.Success ||
            action.ActionType is not (BattleActionType.BasicAttack or BattleActionType.Skill))
        {
            return true;
        }

        foreach (var participantId in result.TargetParticipantIds.Distinct())
        {
            var target = participants.Single(value => value.ParticipantId == participantId);
            var authority = _combatAuthority.Get(battleInstanceId, target.ParticipantId);
            if (!authority.Succeeded ||
                authority.Value is null ||
                authority.Value.CurrentHp != target.CurrentHp ||
                authority.Value.RuntimeVersion != target.RuntimeVersion)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<(BattleInstance Battle, string FailureCode)> ExecuteStatusPhaseAsync(
        BattleInstance battle,
        StatusTriggerPhase phase,
        Guid? sourceActionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_statusTriggers is null)
        {
            return (battle, "");
        }

        var resolution = await _statusTriggers.ResolveAsync(
            battle,
            phase,
            sourceActionId,
            correlationId,
            cancellationToken);
        if (!resolution.Succeeded)
        {
            return (
                battle,
                string.IsNullOrWhiteSpace(resolution.FailureCode)
                    ? "battle.status_trigger_failed"
                    : resolution.FailureCode);
        }

        if (_combatAuthority is null)
        {
            return (battle, "");
        }

        var changed = false;
        var participants = new List<BattleParticipant>(battle.Participants.Count);
        foreach (var participant in battle.Participants)
        {
            var state = _combatAuthority.Get(battle.BattleInstanceId, participant.ParticipantId);
            if (!state.Succeeded || state.Value is null)
            {
                return (battle, state.Error.Code.Length > 0
                    ? state.Error.Code
                    : "battle.combat_authority_missing");
            }

            var isAlive = state.Value.CurrentHp > 0;
            var synchronized = participant with
            {
                MaximumHp = state.Value.MaximumHp,
                CurrentHp = state.Value.CurrentHp,
                IsAlive = isAlive,
                CombatState = isAlive
                    ? participant.CombatState
                    : BattleParticipantCombatState.Defeated,
                RuntimeVersion = state.Value.RuntimeVersion,
                AttackPower = state.Value.AttackPower,
                Defense = state.Value.Defense,
                StatPolicyStatus = state.Value.StatPolicyStatus,
                DefeatedAtUtc = isAlive
                    ? participant.DefeatedAtUtc
                    : participant.DefeatedAtUtc ?? _clock.UtcNow
            };
            changed |= synchronized != participant;
            participants.Add(synchronized);
        }

        if (!changed)
        {
            return (battle, "");
        }

        var updated = battle with
        {
            Participants = BattleCollections.Freeze(participants),
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow
        };
        var saved = await _repository.SaveAsync(
            updated,
            battle.BattleVersion,
            cancellationToken);
        return saved.Succeeded
            ? (updated, "")
            : (battle, saved.Error.Code);
    }

    private async Task<BattleRoundResolutionResult> MarkRecoveryRequiredAsync(
        BattleInstance battle,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var recovery = battle with
        {
            State = BattleState.RecoveryRequired,
            CurrentPhase = BattlePhase.RecoveryRequired,
            RecoveryState = BattleRecoveryState.RecoveryRequired,
            CurrentRound = battle.CurrentRound with { State = BattleRoundState.RecoveryRequired },
            BattleVersion = checked(battle.BattleVersion + 1),
            UpdatedAtUtc = _clock.UtcNow
        };
        var saved = await _repository.SaveAsync(recovery, battle.BattleVersion, cancellationToken);
        Publish(
            BattleEventKind.BattleRecoveryRequired,
            recovery.BattleInstanceId,
            recovery.BattleRequestId,
            recovery.CurrentRoundNumber,
            null,
            null,
            "RecoveryRequired",
            failureCode,
            recovery.CorrelationId);
        return RoundFailure(
            recovery.BattleInstanceId,
            recovery.CurrentRoundNumber,
            BattleResultCode.RecoveryRequired,
            saved.Succeeded ? failureCode : saved.Error.Code,
            saved.Succeeded ? recovery.BattleVersion : battle.BattleVersion);
    }

    private OperationResult ReleaseReservations(BattleInstance battle)
    {
        foreach (var player in battle.Participants.Where(value => value.CharacterId is not null))
        {
            var released = _reservations.Release(
                battle.BattleInstanceId,
                player.CharacterId!.Value,
                _clock.UtcNow);
            if (!released.Succeeded)
            {
                return released;
            }
        }

        return OperationResult.Success;
    }

    private void AppendAudit(
        BattleInstance before,
        BattleInstance after,
        Guid? battleRequestId,
        int? roundNumber,
        Guid? actionId,
        BattleParticipant? participant,
        Guid? attackerParticipantId,
        IReadOnlyList<Guid> targetParticipantIds,
        BattleActionType? actionType,
        BattleResultCode result,
        string failureCode,
        string idempotencySafeId,
        string? sessionId)
    {
        if (_failureInjection.Point == BattleFailurePoint.Audit)
        {
            return;
        }

        BattleActionResult? actionResult = actionId is null
            ? null
            : after.CurrentRound.ResolutionResults.FirstOrDefault(value => value.ActionId == actionId);
        _audit.Append(new BattleAuditRecord(
            Guid.NewGuid(),
            after.BattleInstanceId,
            battleRequestId,
            roundNumber,
            actionId,
            after.State == BattleState.Completed ? Guid.NewGuid() : null,
            idempotencySafeId,
            after.CorrelationId,
            BattleRuntimeHash.SessionSafeId(sessionId),
            participant?.CharacterId,
            participant?.ParticipantId,
            attackerParticipantId,
            BattleCollections.Freeze(targetParticipantIds),
            actionType,
            before.State,
            after.State,
            before.CurrentPhase,
            after.CurrentPhase,
            before.BattleVersion,
            after.BattleVersion,
            participant?.RuntimeVersion,
            participant is null
                ? null
                : after.Participants.FirstOrDefault(value => value.ParticipantId == participant.ParticipantId)?.RuntimeVersion,
            actionResult?.HpBefore,
            actionResult?.Damage,
            actionResult?.HpAfter,
            result,
            failureCode,
            after.TurnOrderPolicyStatus,
            after.FormationPolicyStatus,
            after.VictoryPolicyStatus,
            after.RewardPolicyStatus,
            after.RewardState == BattleRewardState.Committed,
            after.RecoveryState,
            before.UpdatedAtUtc,
            _clock.UtcNow));
    }

    private void Publish(
        BattleEventKind kind,
        Guid battleInstanceId,
        Guid? battleRequestId,
        int? roundNumber,
        Guid? actionId,
        Guid? participantId,
        string state,
        string failureCode,
        string correlationId) =>
        _events.Publish(new BattleEvent(
            Guid.NewGuid(),
            kind,
            battleInstanceId,
            battleRequestId,
            roundNumber,
            actionId,
            participantId,
            state,
            failureCode,
            _clock.UtcNow,
            correlationId));

    private static BattleCreationResult CreationFailure(
        BattleRequest request,
        BattleResultCode code,
        string failureCode) =>
        new(
            code,
            request.BattleRequestId,
            null,
            BattleRuntimeHash.SafeId(request.IdempotencyKey),
            failureCode,
            false,
            request.ExpectedPlayerRuntimeVersion,
            []);

    private static BattleActionSubmissionResult ActionFailure(
        BattleActionRequest request,
        BattleResultCode code,
        string failureCode,
        long? battleVersion = null) =>
        new(
            code,
            request.ActionRequestId,
            request.BattleInstanceId,
            null,
            BattleRuntimeHash.SafeId(request.IdempotencyKey),
            failureCode,
            false,
            battleVersion ?? request.BattleVersion,
            request.RoundNumber,
            []);

    private static BattleRoundResolutionResult RoundFailure(
        Guid battleInstanceId,
        int roundNumber,
        BattleResultCode code,
        string failureCode,
        long battleVersion = 0) =>
        new(
            code,
            battleInstanceId,
            roundNumber,
            0,
            BattleWinnerSide.None,
            failureCode,
            battleVersion,
            false,
            []);

    private static BattleReconnectResult ReconnectFailure(
        Guid battleInstanceId,
        BattleResultCode code,
        string failureCode,
        BattleInstance? battle = null,
        Guid? participantId = null) =>
        new(
            code,
            battleInstanceId,
            participantId ?? Guid.Empty,
            battle?.State ?? BattleState.Faulted,
            battle?.CurrentPhase ?? BattlePhase.NotStarted,
            battle?.CurrentRoundNumber ?? 0,
            failureCode,
            battle?.BattleVersion ?? 0,
            []);
}
