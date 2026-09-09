using System.Collections.Concurrent;
using System.Collections.Frozen;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class InMemorySkillDefinitionRepository : ISkillDefinitionRepository
{
    private readonly IReadOnlyList<SkillDefinitionRecord> _records;

    public InMemorySkillDefinitionRepository(IEnumerable<SkillDefinitionRecord> records)
    {
        _records = SkillCollections.Freeze(records);
    }

    public Task<IReadOnlyList<SkillDefinitionRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records);
    }
}

public sealed class SkillDefinitionMapper : ISkillDefinitionMapper
{
    public OperationResult<SkillDefinition> Map(SkillDefinitionRecord record) =>
        OperationResult<SkillDefinition>.Success(new SkillDefinition(
            record.SkillDefinitionId,
            record.ExternalSkillId,
            record.RawName,
            record.RawDescription,
            record.SkillCategoryCandidate,
            record.ActionCategoryCandidate,
            record.TargetPolicyCandidate,
            record.CostDefinition,
            record.CooldownDefinition,
            record.UsageDefinition,
            SkillCollections.Freeze(record.EffectDefinitions),
            record.MaxRankCandidate,
            record.RequiredLevelCandidate,
            record.Enabled,
            record.ContentVersion,
            record.PolicyStatus,
            record.ProtocolStatus,
            record.RawMetadata));
}

public sealed class SkillDefinitionValidator : ISkillDefinitionValidator
{
    public OperationResult Validate(SkillDefinition definition)
    {
        if (definition.SkillDefinitionId <= 0 ||
            string.IsNullOrWhiteSpace(definition.ExternalSkillId) ||
            string.IsNullOrWhiteSpace(definition.ContentVersion))
        {
            return Failure("skill.definition_invalid", "Skill identity and content version are required.");
        }

        if (definition.MaximumRank is <= 0 ||
            definition.RequiredLevel is < 0 ||
            definition.CostDefinition.Amount < 0 ||
            definition.CooldownDefinition.CooldownRoundsCandidate is < 0 ||
            definition.UsageDefinition.UsageLimitCandidate is <= 0)
        {
            return Failure("skill.definition_value_invalid", "Skill numeric candidates violate runtime invariants.");
        }

        var effects = definition.EffectDefinitions;
        if (effects.Count == 0 &&
            definition.ActionCategory is SkillActionCategory.Damage or SkillActionCategory.Heal or SkillActionCategory.Mixed)
        {
            return Failure("skill.effects_missing", "Executable skill definitions require effects.");
        }

        if (effects.Select(value => value.EffectDefinitionId).Distinct(StringComparer.Ordinal).Count() != effects.Count ||
            effects.Select(value => value.EffectIndex).Distinct().Count() != effects.Count ||
            effects.Where(value => value.SkillDefinitionId != definition.SkillDefinitionId).Any() ||
            !effects.Select(value => value.EffectIndex).SequenceEqual(effects.Select(value => value.EffectIndex).Order()))
        {
            return Failure("skill.effect_order_invalid", "Skill effects require unique stable IDs and indexes.");
        }

        if (effects.Any(value => value.EffectIndex < 0 || value.BaseValueCandidate is < 0))
        {
            return Failure("skill.effect_value_invalid", "Skill effect values violate runtime invariants.");
        }

        if (definition.PolicyStatus != CombatPolicyStatus.TestOnly &&
            definition.PolicyStatus != CombatPolicyStatus.Verified &&
            definition.PolicyStatus != CombatPolicyStatus.ContentBacked &&
            definition.ActionCategory is SkillActionCategory.Damage or SkillActionCategory.Heal or SkillActionCategory.Mixed)
        {
            return Failure("skill.evidence_blocked", "Executable production skill semantics require evidence.");
        }

        return OperationResult.Success;
    }

    private static OperationResult Failure(string code, string message) =>
        OperationResult.Failure(code, message);
}

public sealed class ImmutableSkillDefinitionCatalog : ISkillDefinitionCatalog
{
    private readonly FrozenDictionary<int, SkillDefinition> _definitions;

    private ImmutableSkillDefinitionCatalog(IEnumerable<SkillDefinition> definitions)
    {
        _definitions = definitions.ToFrozenDictionary(value => value.SkillDefinitionId);
        Snapshot = SkillCollections.Freeze(_definitions.Values.OrderBy(value => value.SkillDefinitionId));
    }

    public IReadOnlyList<SkillDefinition> Snapshot { get; }

    public static OperationResult<ImmutableSkillDefinitionCatalog> Create(
        IEnumerable<SkillDefinitionRecord> records,
        ISkillDefinitionMapper mapper,
        ISkillDefinitionValidator validator)
    {
        var definitions = new List<SkillDefinition>();
        foreach (var record in records)
        {
            var mapped = mapper.Map(record);
            if (!mapped.Succeeded || mapped.Value is null)
            {
                return OperationResult<ImmutableSkillDefinitionCatalog>.Failure(
                    mapped.Error.Code,
                    mapped.Error.Message);
            }

            var validated = validator.Validate(mapped.Value);
            if (!validated.Succeeded)
            {
                return OperationResult<ImmutableSkillDefinitionCatalog>.Failure(
                    validated.Error.Code,
                    validated.Error.Message);
            }

            definitions.Add(mapped.Value);
        }

        if (definitions.Select(value => value.SkillDefinitionId).Distinct().Count() != definitions.Count)
        {
            return OperationResult<ImmutableSkillDefinitionCatalog>.Failure(
                "skill.definition_duplicate",
                "Duplicate skill definition ID.");
        }

        return OperationResult<ImmutableSkillDefinitionCatalog>.Success(
            new ImmutableSkillDefinitionCatalog(definitions));
    }

    public OperationResult<SkillDefinition> Get(int skillDefinitionId, BattleRequestSource source)
    {
        if (!_definitions.TryGetValue(skillDefinitionId, out var definition))
        {
            return OperationResult<SkillDefinition>.Failure("skill.not_found", "Skill definition was not found.");
        }

        if (!definition.Enabled)
        {
            return OperationResult<SkillDefinition>.Failure("skill.disabled", "Skill definition is disabled.");
        }

        if (definition.PolicyStatus == CombatPolicyStatus.TestOnly &&
            source != BattleRequestSource.TrustedInternalTest)
        {
            return OperationResult<SkillDefinition>.Failure(
                "skill.test_only_not_available",
                "TestOnly skill is unavailable to the production path.");
        }

        if (definition.PolicyStatus == CombatPolicyStatus.EvidenceBlocked)
        {
            return OperationResult<SkillDefinition>.Failure(
                "skill.evidence_blocked",
                "Skill gameplay semantics are blocked by evidence.");
        }

        return OperationResult<SkillDefinition>.Success(definition);
    }
}

public sealed class SkillAvailabilityPolicy : ISkillAvailabilityPolicy
{
    private readonly FrozenDictionary<(long CharacterId, int SkillDefinitionId), SkillOwnershipRecord> _ownership;

    public SkillAvailabilityPolicy(IEnumerable<SkillOwnershipRecord>? ownership = null)
    {
        _ownership = (ownership ?? []).ToFrozenDictionary(
            value => (value.CharacterId, value.SkillDefinitionId));
    }

    public SkillFailureInjection FailureInjection { get; } = new();

    public OperationResult<int> ResolveRank(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition)
    {
        if (FailureInjection.Point == SkillFailurePoint.Availability)
        {
            return Failure("skill.availability_failed", "Injected skill availability failure.");
        }

        if (battle.State != BattleState.Active ||
            battle.CurrentPhase != BattlePhase.ResolvingActions ||
            battle.CurrentRoundNumber != action.RoundNumber)
        {
            return Failure("skill.invalid_phase", "Skill must resolve from the active locked battle round.");
        }

        var participant = battle.Participants.FirstOrDefault(value => value.ParticipantId == action.ParticipantId);
        if (participant is null)
        {
            return Failure("skill.participant_not_found", "Skill participant was not found.");
        }

        if (!participant.IsAlive)
        {
            return Failure("skill.participant_dead", "Dead participant cannot use a skill.");
        }

        if (participant.CharacterId is null)
        {
            return battle.BattleType == BattleType.InternalTest
                ? OperationResult<int>.Success(1)
                : Failure("skill.ownership_evidence_blocked", "Production monster skill ownership is unknown.");
        }

        if (!_ownership.TryGetValue((participant.CharacterId.Value, definition.SkillDefinitionId), out var owned))
        {
            return battle.BattleType == BattleType.InternalTest
                ? Failure("skill.not_owned", "Test loadout does not contain the skill.")
                : Failure("skill.ownership_evidence_blocked", "Production player skill ownership is unknown.");
        }

        if (!owned.Enabled)
        {
            return Failure("skill.not_owned", "Owned skill is disabled.");
        }

        if (definition.MaximumRank is not null && owned.Rank > definition.MaximumRank.Value)
        {
            return Failure("skill.rank_invalid", "Owned skill rank exceeds the definition.");
        }

        return OperationResult<int>.Success(owned.Rank);
    }

    private static OperationResult<int> Failure(string code, string message) =>
        OperationResult<int>.Failure(code, message);
}

public sealed class SkillTargetResolver : ISkillTargetResolver
{
    public SkillFailureInjection FailureInjection { get; } = new();

    public OperationResult<SkillTargetResolution> Resolve(
        BattleInstance battle,
        BattleParticipant source,
        SkillDefinition definition,
        IReadOnlyList<Guid> requestedTargetParticipantIds)
    {
        if (FailureInjection.Point == SkillFailurePoint.TargetResolution)
        {
            return Failure("skill.target_resolution_failed", "Injected target resolution failure.");
        }

        var stable = battle.Participants
            .OrderBy(value => value.Side)
            .ThenBy(value => value.FormationSlot)
            .ThenBy(value => value.ParticipantId)
            .ToArray();
        IEnumerable<BattleParticipant> selected;
        switch (definition.TargetPolicy)
        {
            case SkillTargetPolicyType.Self:
                selected = [source];
                break;
            case SkillTargetPolicyType.SingleEnemy:
                selected = ResolveSingle(stable, source, requestedTargetParticipantIds, ally: false);
                break;
            case SkillTargetPolicyType.SingleAlly:
                selected = ResolveSingle(stable, source, requestedTargetParticipantIds, ally: true);
                break;
            case SkillTargetPolicyType.AllEnemies:
                selected = stable.Where(value => value.Side != source.Side && value.IsAlive);
                break;
            case SkillTargetPolicyType.AllAllies:
                selected = stable.Where(value => value.Side == source.Side && value.IsAlive);
                break;
            default:
                return Failure("skill.target_policy_blocked", "Target policy is unsupported or evidence-blocked.");
        }

        var targets = selected.ToArray();
        if (targets.Length == 0)
        {
            return Failure("skill.no_valid_target", "No legal skill target exists.");
        }

        if (targets.Any(value => !value.IsAlive))
        {
            return Failure("skill.target_dead", "Dead targets are not supported by this skill runtime.");
        }

        return OperationResult<SkillTargetResolution>.Success(new SkillTargetResolution(
            SkillCollections.Freeze(targets.Select(Snapshot)),
            definition.TargetPolicy,
            definition.PolicyStatus,
            ""));
    }

    private static IEnumerable<BattleParticipant> ResolveSingle(
        IEnumerable<BattleParticipant> participants,
        BattleParticipant source,
        IReadOnlyList<Guid> requested,
        bool ally)
    {
        if (requested.Count != 1)
        {
            return [];
        }

        var target = participants.FirstOrDefault(value => value.ParticipantId == requested[0]);
        return target is not null &&
               target.IsAlive &&
               (ally ? target.Side == source.Side : target.Side != source.Side)
            ? [target]
            : [];
    }

    private static SkillParticipantSnapshot Snapshot(BattleParticipant value) =>
        new(
            value.ParticipantId,
            value.ParticipantType,
            value.Side,
            value.FormationSlot,
            value.CharacterId,
            value.MonsterTemplateId,
            value.MaximumHp,
            value.CurrentHp,
            value.IsAlive,
            value.RuntimeVersion);

    private static OperationResult<SkillTargetResolution> Failure(string code, string message) =>
        OperationResult<SkillTargetResolution>.Failure(code, message);
}

public sealed class SkillCostPolicy : ISkillCostPolicy
{
    public OperationResult Validate(
        SkillDefinition definition,
        BattleParticipant source,
        int roundNumber)
    {
        if (definition.CostDefinition.Amount < 0)
        {
            return OperationResult.Failure("skill.cost_invalid", "Skill cost cannot be negative.");
        }

        return definition.CostDefinition.ResourceType switch
        {
            SkillResourceType.None when definition.CostDefinition.Amount == 0 => OperationResult.Success,
            SkillResourceType.BattleResource
                when definition.CostDefinition.PolicyStatus == CombatPolicyStatus.TestOnly &&
                     roundNumber > 0 => OperationResult.Success,
            SkillResourceType.BattleResource => Failure("skill.cost_evidence_blocked"),
            SkillResourceType.InventoryItem or SkillResourceType.Currency or SkillResourceType.Hp or
                SkillResourceType.Mana or SkillResourceType.Energy or SkillResourceType.Charge or
                SkillResourceType.Unknown => Failure("skill.cost_evidence_blocked"),
            _ => Failure("skill.cost_evidence_blocked")
        };
    }

    private static OperationResult Failure(string code) =>
        OperationResult.Failure(code, "Skill cost policy is unsupported or evidence-blocked.");
}

public sealed class InMemorySkillCostReservationStore : ISkillCostReservationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, long> _available = [];
    private readonly Dictionary<Guid, SkillCostReservation> _reservations = [];

    public SkillFailureInjection FailureInjection { get; } = new();

    public IReadOnlyList<SkillCostReservation> Reservations
    {
        get
        {
            lock (_sync)
            {
                return SkillCollections.Freeze(_reservations.Values.OrderBy(value => value.CreatedAtUtc));
            }
        }
    }

    public void SeedTestResource(Guid participantId, long amount)
    {
        lock (_sync)
        {
            _available[participantId] = amount;
        }
    }

    public long GetAvailableTestResource(Guid participantId)
    {
        lock (_sync)
        {
            return _available.GetValueOrDefault(participantId);
        }
    }

    public OperationResult<SkillCostReservation> Reserve(
        SkillExecutionPlan plan,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            var reservationId = BattleRuntimeHash.DeterministicGuid(
                $"skill-cost:{plan.SkillExecutionId:N}");
            if (_reservations.TryGetValue(reservationId, out var replay))
            {
                return OperationResult<SkillCostReservation>.Success(replay);
            }

            if (FailureInjection.Point == SkillFailurePoint.CostReservation)
            {
                return Failure("skill.cost_reservation_failed", "Injected cost reservation failure.");
            }

            if (plan.CostPlan.ResourceType == SkillResourceType.BattleResource)
            {
                var available = _available.GetValueOrDefault(plan.ParticipantId);
                if (available < plan.CostPlan.Amount)
                {
                    return Failure("skill.insufficient_resource", "Insufficient TestOnly battle resource.");
                }

                _available[plan.ParticipantId] = checked(available - plan.CostPlan.Amount);
            }

            var reservation = new SkillCostReservation(
                reservationId,
                plan.SkillExecutionId,
                plan.BattleInstanceId,
                plan.RoundNumber,
                plan.ParticipantId,
                plan.SkillDefinitionId,
                plan.CostPlan.ResourceType,
                plan.CostPlan.Amount,
                plan.ExpectedParticipantVersion,
                SkillCostReservationState.Reserved,
                now,
                null,
                null,
                BattleRuntimeHash.SafeId(plan.SkillExecutionId.ToString("N")),
                plan.CorrelationId);
            _reservations.Add(reservationId, reservation);
            return OperationResult<SkillCostReservation>.Success(reservation);
        }
    }

    public OperationResult<SkillCostReservation> Commit(Guid reservationId, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!_reservations.TryGetValue(reservationId, out var reservation))
            {
                return Failure("skill.cost_reservation_missing", "Cost reservation was not found.");
            }

            if (reservation.State == SkillCostReservationState.Committed)
            {
                return OperationResult<SkillCostReservation>.Success(reservation);
            }

            if (reservation.State != SkillCostReservationState.Reserved)
            {
                return Failure("skill.cost_reservation_conflict", "Cost reservation cannot be committed.");
            }

            if (FailureInjection.Point == SkillFailurePoint.CostCommit)
            {
                return Failure("skill.cost_commit_failed", "Injected cost commit failure.");
            }

            var committed = reservation with
            {
                State = SkillCostReservationState.Committed,
                CommittedAtUtc = now
            };
            _reservations[reservationId] = committed;
            return OperationResult<SkillCostReservation>.Success(committed);
        }
    }

    public OperationResult<SkillCostReservation> Release(Guid reservationId, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!_reservations.TryGetValue(reservationId, out var reservation))
            {
                return Failure("skill.cost_reservation_missing", "Cost reservation was not found.");
            }

            if (reservation.State == SkillCostReservationState.Released)
            {
                return OperationResult<SkillCostReservation>.Success(reservation);
            }

            if (reservation.State != SkillCostReservationState.Reserved)
            {
                return Failure("skill.cost_reservation_conflict", "Cost reservation cannot be released.");
            }

            if (reservation.ResourceType == SkillResourceType.BattleResource)
            {
                _available[reservation.ParticipantId] = checked(
                    _available.GetValueOrDefault(reservation.ParticipantId) + reservation.Amount);
            }

            var released = reservation with
            {
                State = SkillCostReservationState.Released,
                ReleasedAtUtc = now
            };
            _reservations[reservationId] = released;
            return OperationResult<SkillCostReservation>.Success(released);
        }
    }

    public SkillCostReservation? Get(Guid reservationId)
    {
        lock (_sync)
        {
            return _reservations.GetValueOrDefault(reservationId);
        }
    }

    private static OperationResult<SkillCostReservation> Failure(string code, string message) =>
        OperationResult<SkillCostReservation>.Failure(code, message);
}

public sealed class InMemorySkillUsageStore : ISkillUsageStore
{
    private readonly object _sync = new();
    private readonly Dictionary<
        (Guid BattleInstanceId, Guid ParticipantId, int SkillDefinitionId),
        SkillUsageState> _usage = [];

    public IReadOnlyList<SkillUsageState> Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SkillCollections.Freeze(_usage.Values
                    .OrderBy(value => value.BattleInstanceId)
                    .ThenBy(value => value.ParticipantId)
                    .ThenBy(value => value.SkillDefinitionId));
            }
        }
    }

    public SkillUsageState? Get(
        Guid battleInstanceId,
        Guid participantId,
        int skillDefinitionId)
    {
        lock (_sync)
        {
            return _usage.GetValueOrDefault((battleInstanceId, participantId, skillDefinitionId));
        }
    }

    public OperationResult<SkillUsageState> Save(SkillUsageState state, long expectedVersion)
    {
        lock (_sync)
        {
            var key = (state.BattleInstanceId, state.ParticipantId, state.SkillDefinitionId);
            var current = _usage.GetValueOrDefault(key);
            if ((current?.RuntimeVersion ?? 0) != expectedVersion)
            {
                return OperationResult<SkillUsageState>.Failure(
                    "skill.usage_version_conflict",
                    "Skill usage version changed before commit.");
            }

            _usage[key] = state;
            return OperationResult<SkillUsageState>.Success(state);
        }
    }
}

public sealed class RoundBasedSkillCooldownPolicy : ISkillCooldownPolicy
{
    private readonly ISkillUsageStore _usage;

    public RoundBasedSkillCooldownPolicy(ISkillUsageStore usage)
    {
        _usage = usage;
    }

    public SkillFailureInjection FailureInjection { get; } = new();

    public OperationResult<SkillUsageState> Validate(
        Guid battleInstanceId,
        Guid participantId,
        SkillDefinition definition,
        int roundNumber)
    {
        var cooldown = definition.CooldownDefinition;
        if (cooldown.PolicyStatus == CombatPolicyStatus.EvidenceBlocked)
        {
            return Failure("skill.cooldown_evidence_blocked", "Skill cooldown semantics are blocked.");
        }

        var current = _usage.Get(
            battleInstanceId,
            participantId,
            definition.SkillDefinitionId);
        if (current is not null && roundNumber < current.AvailableAtRound)
        {
            return Failure("skill.cooldown_active", "Skill is unavailable in the current round.");
        }

        if (cooldown.UsageLimitCandidate is not null &&
            (current?.UsageCount ?? 0) >= cooldown.UsageLimitCandidate.Value)
        {
            return Failure("skill.usage_limit_reached", "Skill usage limit was reached.");
        }

        return OperationResult<SkillUsageState>.Success(current ?? new SkillUsageState(
            participantId,
            battleInstanceId,
            definition.SkillDefinitionId,
            null,
            roundNumber,
            0,
            0,
            0,
            "",
            cooldown.PolicyStatus));
    }

    public OperationResult<SkillUsageState> Commit(
        SkillExecutionPlan plan,
        SkillDefinition definition)
    {
        if (FailureInjection.Point == SkillFailurePoint.CooldownCommit)
        {
            return Failure("skill.cooldown_commit_failed", "Injected cooldown commit failure.");
        }

        var current = _usage.Get(
            plan.BattleInstanceId,
            plan.ParticipantId,
            plan.SkillDefinitionId);
        var safeId = BattleRuntimeHash.SafeId(plan.SkillExecutionId.ToString("N"));
        if (current is not null &&
            string.Equals(current.IdempotencySafeId, safeId, StringComparison.Ordinal))
        {
            return OperationResult<SkillUsageState>.Success(current);
        }

        var rounds = definition.CooldownDefinition.CooldownRoundsCandidate ?? 0;
        var updated = new SkillUsageState(
            plan.ParticipantId,
            plan.BattleInstanceId,
            plan.SkillDefinitionId,
            plan.RoundNumber,
            checked(plan.RoundNumber + rounds + (rounds > 0 ? 1 : 0)),
            rounds,
            checked((current?.UsageCount ?? 0) + 1),
            checked((current?.RuntimeVersion ?? 0) + 1),
            safeId,
            definition.CooldownDefinition.PolicyStatus);
        return _usage.Save(updated, current?.RuntimeVersion ?? 0);
    }

    private static OperationResult<SkillUsageState> Failure(string code, string message) =>
        OperationResult<SkillUsageState>.Failure(code, message);
}

public sealed class SkillExecutionPlanner : ISkillExecutionPlanner
{
    public OperationResult<SkillExecutionPlan> Create(
        BattleInstance battle,
        BattleLockedAction action,
        SkillDefinition definition,
        int skillRank,
        SkillTargetResolution targets,
        int resolutionIndex,
        DateTimeOffset now)
    {
        var source = battle.Participants.Single(value => value.ParticipantId == action.ParticipantId);
        var sourceSnapshot = Snapshot(source);
        var executionId = BattleRuntimeHash.DeterministicGuid(
            $"skill-execution:{battle.BattleInstanceId:N}:{battle.CurrentRoundNumber}:{action.ActionId:N}");
        var plans = new List<SkillEffectPlan>();
        var targetOccurrences = new Dictionary<Guid, int>();
        foreach (var effect in definition.EffectDefinitions.OrderBy(value => value.EffectIndex))
        {
            if (effect.BaseValueCandidate is null)
            {
                return OperationResult<SkillExecutionPlan>.Failure(
                    "skill.effect_evidence_blocked",
                    "Executable effect requires an evidence-backed or TestOnly value.");
            }

            for (var targetIndex = 0; targetIndex < targets.Targets.Count; targetIndex++)
            {
                var target = targets.Targets[targetIndex];
                var occurrence = targetOccurrences.GetValueOrDefault(target.ParticipantId);
                targetOccurrences[target.ParticipantId] = occurrence + 1;
                var effectId = BattleRuntimeHash.DeterministicGuid(
                    $"skill-effect:{executionId:N}:{effect.EffectIndex}:{targetIndex}");
                plans.Add(new SkillEffectPlan(
                    effectId,
                    effect.EffectDefinitionId,
                    effect.EffectIndex,
                    target.ParticipantId,
                    targetIndex,
                    effect.EffectType,
                    new SkillValuePlan(
                        effect.ValuePolicy,
                        effect.BaseValueCandidate.Value,
                        effect.ScalingCandidate ?? 0m,
                        effect.PolicyStatus),
                    checked(target.RuntimeVersion + occurrence),
                    $"skill-effect:{executionId:N}:{effect.EffectIndex}:{targetIndex}",
                    SkillEffectExecutionState.Pending,
                    effect.PolicyStatus));
            }
        }

        return OperationResult<SkillExecutionPlan>.Success(new SkillExecutionPlan(
            executionId,
            battle.BattleInstanceId,
            battle.CurrentRoundNumber,
            action.ActionId,
            BattleRuntimeHash.DeterministicGuid($"skill-action:{action.ActionId:N}"),
            action.ParticipantId,
            definition.SkillDefinitionId,
            skillRank,
            sourceSnapshot,
            SkillCollections.Freeze(targets.Targets),
            definition.CostDefinition,
            SkillCollections.Freeze(plans),
            resolutionIndex,
            battle.BattleVersion,
            source.RuntimeVersion,
            SkillCollections.Freeze(new[]
            {
                definition.PolicyStatus,
                definition.CostDefinition.PolicyStatus,
                definition.CooldownDefinition.PolicyStatus,
                targets.PolicyStatus
            }),
            now,
            action.CorrelationId));
    }

    private static SkillParticipantSnapshot Snapshot(BattleParticipant value) =>
        new(
            value.ParticipantId,
            value.ParticipantType,
            value.Side,
            value.FormationSlot,
            value.CharacterId,
            value.MonsterTemplateId,
            value.MaximumHp,
            value.CurrentHp,
            value.IsAlive,
            value.RuntimeVersion);
}

public sealed class SkillEffectHandlerRegistry : ISkillEffectHandlerRegistry
{
    private readonly FrozenDictionary<SkillEffectType, ISkillEffectHandler> _handlers;

    public SkillEffectHandlerRegistry(IEnumerable<ISkillEffectHandler> handlers)
    {
        var values = handlers.ToArray();
        if (values.Select(value => value.EffectType).Distinct().Count() != values.Length)
        {
            throw new ArgumentException("Skill effect handlers require unique effect types.", nameof(handlers));
        }

        _handlers = values.ToFrozenDictionary(value => value.EffectType);
    }

    public OperationResult<ISkillEffectHandler> Get(SkillEffectType effectType) =>
        _handlers.TryGetValue(effectType, out var handler)
            ? OperationResult<ISkillEffectHandler>.Success(handler)
            : OperationResult<ISkillEffectHandler>.Failure(
                effectType is SkillEffectType.ApplyStatus or SkillEffectType.RemoveStatus or
                    SkillEffectType.ResourceRestore or SkillEffectType.ResourceDrain or
                    SkillEffectType.Revive or SkillEffectType.Summon or SkillEffectType.Dispel or
                    SkillEffectType.Scripted
                    ? "skill.effect_evidence_blocked"
                    : "skill.effect_unsupported",
                "Skill effect handler is not implemented.");
}

public sealed class SkillEffectResolver : ISkillEffectResolver
{
    private readonly ISkillEffectHandlerRegistry _handlers;

    public SkillEffectResolver(ISkillEffectHandlerRegistry handlers)
    {
        _handlers = handlers;
    }

    public async Task<SkillEffectResult> ResolveAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken)
    {
        var handler = _handlers.Get(context.EffectPlan.EffectType);
        if (!handler.Succeeded || handler.Value is null)
        {
            return Failed(
                context,
                handler.Error.Code == "skill.effect_evidence_blocked"
                    ? SkillEffectExecutionState.Rejected
                    : SkillEffectExecutionState.Rejected,
                handler.Error.Code);
        }

        return await handler.Value.ExecuteAsync(context, cancellationToken);
    }

    private static SkillEffectResult Failed(
        SkillEffectContext context,
        SkillEffectExecutionState state,
        string failureCode) =>
        new(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            context.EffectPlan.EffectType,
            state,
            context.EffectPlan.ValuePlan.BaseValue,
            0,
            0,
            context.Target.CurrentHp,
            context.Target.CurrentHp,
            context.Target.MaximumHp,
            !context.Target.IsAlive,
            context.Target.RuntimeVersion,
            context.Target.RuntimeVersion,
            context.EffectPlan.PolicyStatus,
            failureCode,
            false,
            context.Now);
}

public sealed class SkillDamageEffectHandler : ISkillEffectHandler
{
    public SkillEffectType EffectType => SkillEffectType.Damage;

    public async Task<SkillEffectResult> ExecuteAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Target.IsAlive)
        {
            return Failed(context, "skill.target_dead");
        }

        var execution = await context.Combat.ExecuteAsync(
            new BattleCombatExecutionRequest(
                context.Battle.BattleInstanceId,
                context.Battle.CurrentRoundNumber,
                context.BattleAction.ActionId,
                context.Plan.TurnResolutionIndex,
                context.Source,
                context.Target,
                context.EffectPlan.EffectIdempotencyKey,
                context.Now,
                context.Plan.CorrelationId,
                context.EffectPlan.ValuePlan.BaseValue,
                context.EffectPlan.ValuePlan.PolicyStatus),
            cancellationToken);
        return new SkillEffectResult(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            SkillEffectType.Damage,
            execution.Code == BattleActionResolutionCode.Success
                ? SkillEffectExecutionState.Committed
                : SkillEffectExecutionState.Rejected,
            context.EffectPlan.ValuePlan.BaseValue,
            execution.Damage,
            0,
            execution.HpBefore,
            execution.HpAfter,
            context.Target.MaximumHp,
            execution.TargetDefeated,
            execution.RuntimeVersionBefore,
            execution.RuntimeVersionAfter,
            execution.PolicyStatus,
            execution.FailureCode,
            execution.IsDuplicate,
            context.Now);
    }

    private static SkillEffectResult Failed(SkillEffectContext context, string failureCode) =>
        new(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            SkillEffectType.Damage,
            SkillEffectExecutionState.Rejected,
            context.EffectPlan.ValuePlan.BaseValue,
            0,
            0,
            context.Target.CurrentHp,
            context.Target.CurrentHp,
            context.Target.MaximumHp,
            true,
            context.Target.RuntimeVersion,
            context.Target.RuntimeVersion,
            context.EffectPlan.PolicyStatus,
            failureCode,
            false,
            context.Now);
}

public sealed class SkillHealingEffectHandler : ISkillEffectHandler
{
    public SkillEffectType EffectType => SkillEffectType.Heal;

    public async Task<SkillEffectResult> ExecuteAsync(
        SkillEffectContext context,
        CancellationToken cancellationToken)
    {
        var result = await context.Health.HealAsync(
            new BattleHealthMutationRequest(
                context.Battle.BattleInstanceId,
                context.Battle.CurrentRoundNumber,
                context.BattleAction.ActionId,
                context.EffectPlan.EffectExecutionId,
                context.Source,
                context.Target,
                context.EffectPlan.ValuePlan.BaseValue,
                context.Target.RuntimeVersion,
                context.EffectPlan.EffectIdempotencyKey,
                context.EffectPlan.PolicyStatus,
                context.Now,
                context.Plan.CorrelationId),
            cancellationToken);
        return new SkillEffectResult(
            context.EffectPlan.EffectExecutionId,
            context.Plan.SkillExecutionId,
            context.EffectPlan.EffectDefinitionId,
            context.EffectPlan.EffectIndex,
            context.EffectPlan.TargetIndex,
            context.Source.ParticipantId,
            context.Target.ParticipantId,
            SkillEffectType.Heal,
            result.ResultCode is SkillResultCode.Success or SkillResultCode.DuplicateCompleted
                ? SkillEffectExecutionState.Committed
                : SkillEffectExecutionState.Rejected,
            result.RequestedHeal,
            0,
            result.EffectiveHeal,
            result.HpBefore,
            result.HpAfter,
            result.MaximumHp,
            false,
            result.RuntimeVersionBefore,
            result.RuntimeVersionAfter,
            result.PolicyStatus,
            result.FailureCode,
            result.IsDuplicate,
            context.Now);
    }
}

public sealed class InMemorySkillExecutionStore :
    ISkillExecutionStore,
    ISkillInspectorSource
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SkillExecutionRecord> _executions = [];
    private readonly ISkillCostReservationStore _costs;
    private readonly ISkillUsageStore _usage;

    public InMemorySkillExecutionStore(
        ISkillCostReservationStore costs,
        ISkillUsageStore usage)
    {
        _costs = costs;
        _usage = usage;
    }

    public SkillFailureInjection FailureInjection { get; } = new();

    public IReadOnlyList<SkillExecutionRecord> Snapshot
    {
        get
        {
            lock (_sync)
            {
                return SkillCollections.Freeze(_executions.Values.OrderBy(value => value.Plan.CreatedAtUtc));
            }
        }
    }

    public IReadOnlyList<SkillExecutionRecord> Executions => Snapshot;

    public IReadOnlyList<SkillCostReservation> CostReservations => _costs.Reservations;

    public IReadOnlyList<SkillUsageState> Usage => _usage.Snapshot;

    public SkillExecutionRecord? Get(Guid skillExecutionId)
    {
        lock (_sync)
        {
            return _executions.GetValueOrDefault(skillExecutionId);
        }
    }

    public SkillExecutionRecord? FindByIdempotency(string idempotencyKey)
    {
        lock (_sync)
        {
            return _executions.Values.FirstOrDefault(value =>
                value.Plan.SkillExecutionId == BattleRuntimeHash.DeterministicGuid(idempotencyKey));
        }
    }

    public OperationResult Save(SkillExecutionRecord record, int expectedCursor)
    {
        lock (_sync)
        {
            if (FailureInjection.Point is SkillFailurePoint.PlanPersistence or
                SkillFailurePoint.EffectPersistence or SkillFailurePoint.ResultPersistence)
            {
                return OperationResult.Failure(
                    "skill.execution_persistence_failed",
                    "Injected skill execution persistence failure.");
            }

            var current = _executions.GetValueOrDefault(record.Plan.SkillExecutionId);
            if ((current?.ResolutionCursor ?? -1) != expectedCursor)
            {
                return OperationResult.Failure(
                    "skill.execution_version_conflict",
                    "Skill execution cursor changed before commit.");
            }

            _executions[record.Plan.SkillExecutionId] = record;
            return OperationResult.Success;
        }
    }
}

public sealed class InMemorySkillEventSink : ISkillEventSink
{
    private readonly ConcurrentQueue<SkillEvent> _events = [];

    public IReadOnlyList<SkillEvent> Snapshot => _events.ToArray();

    public void Publish(SkillEvent runtimeEvent) => _events.Enqueue(runtimeEvent);
}

public sealed class InMemorySkillAuditLedger : ISkillAuditLedger
{
    private readonly ConcurrentQueue<SkillAuditRecord> _records = [];

    public IReadOnlyList<SkillAuditRecord> Snapshot => _records.ToArray();

    public void Append(SkillAuditRecord record) => _records.Enqueue(record);
}
