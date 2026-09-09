namespace God2.ClassicServer.Runtime;

public sealed record BattleInspectorQuery(
    Guid? BattleInstanceId = null,
    long? CharacterId = null,
    string? SessionSafeId = null,
    Guid? ParticipantId = null,
    int? MonsterTemplateId = null,
    int? MapId = null,
    BattleState? BattleState = null,
    BattlePhase? BattlePhase = null,
    int? RoundNumber = null,
    BattleActionType? ActionType = null,
    bool ActiveOnly = false,
    bool CompletedOnly = false,
    bool FailedOnly = false,
    bool RecoveryRequiredOnly = false,
    bool EvidenceBlockedOnly = false,
    int Skip = 0,
    int Take = 100);

public sealed record BattleInspectorItem(
    Guid BattleInstanceId,
    BattleState State,
    BattlePhase Phase,
    string WorldInstanceId,
    int SourceMapId,
    string EncounterDefinitionId,
    int CurrentRoundNumber,
    int CurrentResolutionIndex,
    long BattleVersion,
    int ParticipantCount,
    int AlivePlayerCount,
    int AliveEnemyCount,
    int SubmittedActionCount,
    int LockedActionCount,
    BattleWinnerSide WinnerSide,
    BattleCompletionReason CompletionReason,
    BattleRewardState RewardState,
    BattleRecoveryState RecoveryState,
    CombatPolicyStatus FormationPolicyStatus,
    CombatPolicyStatus TurnOrderPolicyStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BattleParticipantInspectorItem(
    Guid BattleInstanceId,
    Guid ParticipantId,
    BattleParticipantType Type,
    BattleSide Side,
    int FormationSlot,
    long? CharacterId,
    int? MonsterTemplateId,
    long SourceRuntimeEntityId,
    long BattleRuntimeEntityId,
    string SessionSafeId,
    long MaximumHp,
    long CurrentHp,
    bool IsAlive,
    bool IsConnected,
    bool HasSubmittedAction,
    BattleParticipantCombatState CombatState,
    long RuntimeVersion,
    CombatPolicyStatus StatPolicyStatus,
    CombatPolicyStatus FormationPolicyStatus);

public sealed record BattleRoundInspectorItem(
    Guid BattleInstanceId,
    int RoundNumber,
    BattleRoundState State,
    int EligibleParticipantCount,
    int SubmittedActionCount,
    int LockedActionCount,
    IReadOnlyList<Guid> TurnOrder,
    int CurrentResolutionIndex,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record BattleActionInspectorItem(
    Guid BattleInstanceId,
    Guid ActionId,
    int RoundNumber,
    Guid ParticipantId,
    BattleActionType ActionType,
    IReadOnlyList<Guid> TargetParticipantIds,
    BattleActionState State,
    int? ResolutionIndex,
    BattleActionResolutionCode? Result,
    string FailureCode,
    long Damage,
    long HpBefore,
    long HpAfter,
    bool IsDuplicate,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record BattleInspectorSnapshot(
    IReadOnlyList<BattleInspectorItem> Battles,
    IReadOnlyList<BattleParticipantInspectorItem> Participants,
    IReadOnlyList<BattleRoundInspectorItem> Rounds,
    IReadOnlyList<BattleActionInspectorItem> Actions,
    int TotalBeforePaging,
    int Skip,
    int Take,
    DateTimeOffset CapturedAtUtc);

public sealed class BattleRuntimeInspector
{
    private readonly IBattleRuntimeReadModel _readModel;
    private readonly BattleFailureInjection _failureInjection;

    public BattleRuntimeInspector(
        IBattleRuntimeReadModel readModel,
        BattleFailureInjection? failureInjection = null)
    {
        _readModel = readModel;
        _failureInjection = failureInjection ?? new BattleFailureInjection();
    }

    public BattleInspectorSnapshot Capture(
        BattleInspectorQuery query,
        DateTimeOffset capturedAtUtc)
    {
        if (_failureInjection.Point == BattleFailurePoint.Inspector)
        {
            return new BattleInspectorSnapshot([], [], [], [], 0, 0, 0, capturedAtUtc);
        }

        var take = Math.Clamp(query.Take, 1, 500);
        var skip = Math.Max(0, query.Skip);
        var filtered = _readModel.Snapshot
            .Where(battle => query.BattleInstanceId is null || battle.BattleInstanceId == query.BattleInstanceId)
            .Where(battle => query.CharacterId is null ||
                             battle.Participants.Any(value => value.CharacterId == query.CharacterId))
            .Where(battle => string.IsNullOrWhiteSpace(query.SessionSafeId) ||
                             battle.Participants.Any(value =>
                                 string.Equals(
                                     BattleRuntimeHash.SessionSafeId(value.SessionId),
                                     query.SessionSafeId,
                                     StringComparison.Ordinal)))
            .Where(battle => query.ParticipantId is null ||
                             battle.Participants.Any(value => value.ParticipantId == query.ParticipantId))
            .Where(battle => query.MonsterTemplateId is null ||
                             battle.Participants.Any(value => value.MonsterTemplateId == query.MonsterTemplateId))
            .Where(battle => query.MapId is null || battle.SourceMapId == query.MapId)
            .Where(battle => query.BattleState is null || battle.State == query.BattleState)
            .Where(battle => query.BattlePhase is null || battle.CurrentPhase == query.BattlePhase)
            .Where(battle => query.RoundNumber is null || battle.CurrentRoundNumber == query.RoundNumber)
            .Where(battle => query.ActionType is null ||
                             battle.CurrentRound.SubmittedActions.Any(value => value.ActionType == query.ActionType))
            .Where(battle => !query.ActiveOnly || battle.State == BattleState.Active)
            .Where(battle => !query.CompletedOnly || battle.State == BattleState.Completed)
            .Where(battle => !query.FailedOnly ||
                             battle.State is BattleState.Faulted or BattleState.RecoveryRequired ||
                             battle.CurrentRound.ResolutionResults.Any(value =>
                                 value.Result is BattleActionResolutionCode.CombatFailure or
                                     BattleActionResolutionCode.RecoveryRequired))
            .Where(battle => !query.RecoveryRequiredOnly ||
                             battle.RecoveryState == BattleRecoveryState.RecoveryRequired)
            .Where(battle => !query.EvidenceBlockedOnly ||
                             battle.EncounterPolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
                             battle.FormationPolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
                             battle.TurnOrderPolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
                             battle.RewardPolicyStatus == CombatPolicyStatus.EvidenceBlocked)
            .OrderByDescending(value => value.UpdatedAtUtc)
            .ToArray();
        var page = filtered.Skip(skip).Take(take).ToArray();
        var battles = page.Select(battle => new BattleInspectorItem(
            battle.BattleInstanceId,
            battle.State,
            battle.CurrentPhase,
            battle.WorldInstanceId,
            battle.SourceMapId,
            battle.EncounterDefinitionId,
            battle.CurrentRoundNumber,
            battle.CurrentResolutionIndex,
            battle.BattleVersion,
            battle.Participants.Count,
            battle.Participants.Count(value => value.Side == BattleSide.PlayerSide && value.IsAlive),
            battle.Participants.Count(value => value.Side == BattleSide.EnemySide && value.IsAlive),
            battle.CurrentRound.SubmittedActions.Count,
            battle.CurrentRound.LockedActions.Count,
            battle.WinnerSide,
            battle.CompletionReason,
            battle.RewardState,
            battle.RecoveryState,
            battle.FormationPolicyStatus,
            battle.TurnOrderPolicyStatus,
            battle.CreatedAtUtc,
            battle.UpdatedAtUtc)).ToArray();
        var participants = page.SelectMany(battle => battle.Participants.Select(participant =>
            new BattleParticipantInspectorItem(
                battle.BattleInstanceId,
                participant.ParticipantId,
                participant.ParticipantType,
                participant.Side,
                participant.FormationSlot,
                participant.CharacterId,
                participant.MonsterTemplateId,
                participant.SourceRuntimeEntityId,
                participant.BattleRuntimeEntityId,
                BattleRuntimeHash.SessionSafeId(participant.SessionId),
                participant.MaximumHp,
                participant.CurrentHp,
                participant.IsAlive,
                participant.IsConnected,
                participant.HasSubmittedAction,
                participant.CombatState,
                participant.RuntimeVersion,
                participant.StatPolicyStatus,
                participant.FormationPolicyStatus))).ToArray();
        var rounds = page.SelectMany(battle =>
            battle.CompletedRounds
                .Append(battle.CurrentRound)
                .DistinctBy(round => round.RoundNumber)
                .Select(round => new BattleRoundInspectorItem(
                    battle.BattleInstanceId,
                    round.RoundNumber,
                    round.State,
                    round.EligibleParticipantIds.Count,
                    round.SubmittedActions.Count,
                    round.LockedActions.Count,
                    BattleCollections.Freeze(round.TurnOrder),
                    round.CurrentResolutionIndex,
                    round.StartedAtUtc,
                    round.CompletedAtUtc))).ToArray();
        var actions = page.SelectMany(battle =>
            battle.CompletedRounds
                .Append(battle.CurrentRound)
                .DistinctBy(round => round.RoundNumber)
                .SelectMany(round => round.SubmittedActions.Select(action =>
            {
                var result = round.ResolutionResults.FirstOrDefault(value =>
                    value.ActionId == action.ActionId);
                var locked = round.LockedActions.Any(value => value.ActionId == action.ActionId);
                return new BattleActionInspectorItem(
                    battle.BattleInstanceId,
                    action.ActionId,
                    action.RoundNumber,
                    action.ParticipantId,
                    action.ActionType,
                    BattleCollections.Freeze(action.TargetParticipantIds),
                    result is not null
                        ? result.Result is BattleActionResolutionCode.Success or BattleActionResolutionCode.Passed
                            ? BattleActionState.Resolved
                            : BattleActionState.Skipped
                        : locked
                            ? BattleActionState.Locked
                            : action.State,
                    result?.ResolutionIndex,
                    result?.Result,
                    result?.FailureCode ?? "",
                    result?.Damage ?? 0,
                    result?.HpBefore ?? 0,
                    result?.HpAfter ?? 0,
                    result?.IsDuplicate ?? false,
                    action.SubmittedAtUtc,
                    result?.ResolvedAtUtc);
            }))).ToArray();
        return new BattleInspectorSnapshot(
            BattleCollections.Freeze(battles),
            BattleCollections.Freeze(participants),
            BattleCollections.Freeze(rounds),
            BattleCollections.Freeze(actions),
            filtered.Length,
            skip,
            take,
            capturedAtUtc);
    }
}
