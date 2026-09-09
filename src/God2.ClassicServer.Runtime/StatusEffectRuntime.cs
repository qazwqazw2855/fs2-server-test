using System.Collections.Concurrent;
using System.Collections.Frozen;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class InMemoryStatusDefinitionRepository : IStatusDefinitionRepository
{
    private readonly IReadOnlyList<StatusDefinitionRecord> _records;

    public InMemoryStatusDefinitionRepository(IEnumerable<StatusDefinitionRecord> records)
    {
        _records = StatusRuntimeCollections.Freeze(records);
    }

    public Task<IReadOnlyList<StatusDefinitionRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records);
    }
}

public sealed class StatusDefinitionMapper : IStatusDefinitionMapper
{
    public OperationResult<StatusDefinition> Map(StatusDefinitionRecord record)
    {
        var triggers = StatusRuntimeCollections.Freeze(
            record.TriggerDefinitions
                .OrderBy(value => value.TriggerIndex)
                .ThenBy(value => value.TriggerDefinitionId, StringComparer.Ordinal));
        var modifiers = StatusRuntimeCollections.Freeze(
            record.ModifierDefinitions
                .OrderBy(value => value.ModifierIndex)
                .ThenBy(value => value.ModifierDefinitionId, StringComparer.Ordinal));
        var restrictions = StatusRuntimeCollections.Freeze(
            record.ActionRestrictionDefinitions
                .OrderBy(value => value.RestrictionIndex)
                .ThenBy(value => value.RestrictionDefinitionId, StringComparer.Ordinal));
        return OperationResult<StatusDefinition>.Success(new StatusDefinition(
            record.StatusDefinitionId,
            record.ExternalStatusId,
            record.RawName,
            record.RawDescription,
            record.StatusCategory,
            record.StatusPolarity,
            record.ApplicationPolicy,
            record.StackPolicy,
            record.DurationPolicy,
            triggers,
            modifiers,
            restrictions,
            record.DispelDefinition,
            record.Enabled,
            record.ContentVersion,
            record.PolicyStatus,
            record.ProtocolStatus,
            record.RawMetadata));
    }
}

public sealed class StatusDefinitionValidator : IStatusDefinitionValidator
{
    public OperationResult Validate(StatusDefinition definition)
    {
        if (definition.StatusDefinitionId <= 0 ||
            string.IsNullOrWhiteSpace(definition.ExternalStatusId) ||
            string.IsNullOrWhiteSpace(definition.Name) ||
            string.IsNullOrWhiteSpace(definition.ContentVersion))
        {
            return Failure("status.definition_invalid", "Status identity, name, and content version are required.");
        }

        if (definition.StackPolicy.MaximumStacks is <= 0 ||
            definition.DurationPolicy.DurationRounds is < 0)
        {
            return Failure("status.definition_value_invalid", "Status stack and duration values violate runtime invariants.");
        }

        if (definition.StackPolicy.PolicyType == StatusStackPolicyType.AddStacks &&
            definition.StackPolicy.MaximumStacks is null)
        {
            return Failure("status.stack_maximum_missing", "AddStacks requires a server-owned maximum stack count.");
        }

        if (definition.DurationPolicy.PolicyType == StatusDurationPolicyType.Rounds &&
            definition.DurationPolicy.DurationRounds is not > 0)
        {
            return Failure("status.duration_rounds_invalid", "Round duration requires a positive round count.");
        }

        if (definition.DurationPolicy.PolicyType == StatusDurationPolicyType.WallClock)
        {
            return Failure("status.wall_clock_unsupported", "Wall-clock status duration is outside the round runtime.");
        }

        if (definition.TriggerDefinitions.Select(value => value.TriggerIndex).Distinct().Count() !=
            definition.TriggerDefinitions.Count ||
            definition.TriggerDefinitions.Select(value => value.TriggerDefinitionId)
                .Distinct(StringComparer.Ordinal).Count() != definition.TriggerDefinitions.Count ||
            definition.TriggerDefinitions.Any(value =>
                value.StatusDefinitionId != definition.StatusDefinitionId ||
                value.TriggerIndex < 0 ||
                value.MaximumTriggersCandidate is < 0 ||
                value.BaseValueCandidate is < 0 ||
                value.TriggerPhase == StatusTriggerPhase.Unknown))
        {
            return Failure("status.trigger_definition_invalid", "Status triggers require unique stable indexes and valid values.");
        }

        if (definition.ModifierDefinitions.Select(value => value.ModifierIndex).Distinct().Count() !=
            definition.ModifierDefinitions.Count ||
            definition.ModifierDefinitions.Select(value => value.ModifierDefinitionId)
                .Distinct(StringComparer.Ordinal).Count() != definition.ModifierDefinitions.Count ||
            definition.ModifierDefinitions.Any(value =>
                value.StatusDefinitionId != definition.StatusDefinitionId ||
                value.ModifierIndex < 0 ||
                value.MultiplierBasisPoints < 0 ||
                value.ModifierType == StatusModifierType.Unknown))
        {
            return Failure("status.modifier_definition_invalid", "Status modifiers require unique stable indexes and checked values.");
        }

        if (definition.ActionRestrictionDefinitions.Select(value => value.RestrictionIndex).Distinct().Count() !=
            definition.ActionRestrictionDefinitions.Count ||
            definition.ActionRestrictionDefinitions.Any(value =>
                value.RestrictionIndex < 0 ||
                value.RestrictionType == StatusActionRestrictionType.Unknown))
        {
            return Failure("status.restriction_definition_invalid", "Status restrictions require unique valid indexes.");
        }

        if (definition.StatusCategory == StatusCategory.PeriodicDamage &&
            !definition.TriggerDefinitions.Any(value =>
                value.EffectType == StatusTriggerEffectType.PeriodicDamage &&
                value.BaseValueCandidate is >= 0))
        {
            return Failure("status.periodic_damage_policy_missing", "Periodic damage requires a server-owned damage trigger.");
        }

        if (definition.StatusCategory == StatusCategory.PeriodicHealing &&
            !definition.TriggerDefinitions.Any(value =>
                value.EffectType == StatusTriggerEffectType.PeriodicHealing &&
                value.BaseValueCandidate is >= 0))
        {
            return Failure("status.periodic_healing_policy_missing", "Periodic healing requires a server-owned healing trigger.");
        }

        if (definition.TriggerDefinitions.Any(value =>
                value.EffectType is StatusTriggerEffectType.Scripted or StatusTriggerEffectType.Unknown))
        {
            return Failure("status.trigger_effect_unsupported", "Unsupported trigger effects cannot enter the executable catalog.");
        }

        if (definition.PolicyStatus == CombatPolicyStatus.TestOnly &&
            definition.ApplicationPolicy != StatusApplicationPolicyType.TestOnly)
        {
            return Failure("status.test_only_marker_missing", "TestOnly definitions require an explicit TestOnly application policy.");
        }

        if (definition.PolicyStatus is CombatPolicyStatus.Verified or CombatPolicyStatus.ContentBacked &&
            definition.ApplicationPolicy == StatusApplicationPolicyType.TestOnly)
        {
            return Failure("status.production_uses_test_policy", "Production definitions cannot use TestOnly application policy.");
        }

        if (definition.PolicyStatus != CombatPolicyStatus.TestOnly &&
            definition.ModifierDefinitions.Any(value => value.PolicyStatus == CombatPolicyStatus.TestOnly))
        {
            return Failure("status.production_uses_test_modifier", "Production definitions cannot use TestOnly modifiers.");
        }

        if (definition.StatusCategory == StatusCategory.Unknown &&
            definition.PolicyStatus is CombatPolicyStatus.Verified or CombatPolicyStatus.ContentBacked)
        {
            return Failure("status.unknown_marked_verified", "Unknown status content cannot be marked verified.");
        }

        if (definition.PolicyStatus != CombatPolicyStatus.EvidenceBlocked &&
            definition.TriggerDefinitions.Count == 0 &&
            definition.ModifierDefinitions.Count == 0 &&
            definition.ActionRestrictionDefinitions.Count == 0)
        {
            return Failure("status.empty_definition", "Executable status definitions require a trigger, modifier, or restriction.");
        }

        if (definition.PolicyStatus is not (
                CombatPolicyStatus.TestOnly or
                CombatPolicyStatus.Verified or
                CombatPolicyStatus.ContentBacked or
                CombatPolicyStatus.EvidenceBlocked))
        {
            return Failure("status.policy_invalid", "Status definitions require explicit evidence or TestOnly policy.");
        }

        return OperationResult.Success;
    }

    private static OperationResult Failure(string code, string message) =>
        OperationResult.Failure(code, message);
}

public sealed class ImmutableStatusDefinitionCatalog : IStatusDefinitionCatalog
{
    private readonly FrozenDictionary<int, StatusDefinition> _definitions;

    private ImmutableStatusDefinitionCatalog(IEnumerable<StatusDefinition> definitions)
    {
        _definitions = definitions.ToFrozenDictionary(value => value.StatusDefinitionId);
        Snapshot = StatusRuntimeCollections.Freeze(
            _definitions.Values.OrderBy(value => value.StatusDefinitionId));
    }

    public IReadOnlyList<StatusDefinition> Snapshot { get; }

    public static OperationResult<ImmutableStatusDefinitionCatalog> Create(
        IEnumerable<StatusDefinitionRecord> records,
        IStatusDefinitionMapper mapper,
        IStatusDefinitionValidator validator)
    {
        var definitions = new List<StatusDefinition>();
        foreach (var record in records)
        {
            var mapped = mapper.Map(record);
            if (!mapped.Succeeded || mapped.Value is null)
            {
                return OperationResult<ImmutableStatusDefinitionCatalog>.Failure(
                    mapped.Error.Code,
                    mapped.Error.Message);
            }

            var validation = validator.Validate(mapped.Value);
            if (!validation.Succeeded)
            {
                return OperationResult<ImmutableStatusDefinitionCatalog>.Failure(
                    validation.Error.Code,
                    validation.Error.Message);
            }

            definitions.Add(mapped.Value);
        }

        if (definitions.Select(value => value.StatusDefinitionId).Distinct().Count() != definitions.Count)
        {
            return OperationResult<ImmutableStatusDefinitionCatalog>.Failure(
                "status.definition_duplicate",
                "Duplicate status definition ID.");
        }

        return OperationResult<ImmutableStatusDefinitionCatalog>.Success(
            new ImmutableStatusDefinitionCatalog(definitions));
    }

    public OperationResult<StatusDefinition> Get(int statusDefinitionId, StatusRequestSource source)
    {
        if (!_definitions.TryGetValue(statusDefinitionId, out var definition))
        {
            return OperationResult<StatusDefinition>.Failure(
                "status.not_found",
                "Status definition was not found.");
        }

        if (!definition.Enabled)
        {
            return OperationResult<StatusDefinition>.Failure(
                "status.disabled",
                "Status definition is disabled.");
        }

        if (definition.PolicyStatus == CombatPolicyStatus.TestOnly &&
            source is not (StatusRequestSource.TrustedInternalTest or StatusRequestSource.AdministrativeTest))
        {
            return OperationResult<StatusDefinition>.Failure(
                "status.test_only_not_available",
                "TestOnly status is unavailable to the production path.");
        }

        if (definition.PolicyStatus == CombatPolicyStatus.EvidenceBlocked ||
            definition.ApplicationPolicy is StatusApplicationPolicyType.EvidenceBlocked or StatusApplicationPolicyType.Unknown)
        {
            return OperationResult<StatusDefinition>.Failure(
                "status.evidence_blocked",
                "Status gameplay semantics are blocked by content evidence.");
        }

        return OperationResult<StatusDefinition>.Success(definition);
    }
}

public sealed class InMemoryBattleStatusAuthority :
    IBattleStatusAuthority,
    IStatusInstanceStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, StatusInstance> _instances = [];
    private readonly Dictionary<(Guid BattleId, Guid ParticipantId), long> _participantVersions = [];
    private readonly Dictionary<string, Guid> _mutations = new(StringComparer.Ordinal);

    public BattleStatusSnapshot GetParticipantSnapshot(
        Guid battleInstanceId,
        Guid participantId,
        DateTimeOffset capturedAtUtc)
    {
        lock (_sync)
        {
            var statuses = _instances.Values
                .Where(value =>
                    value.BattleInstanceId == battleInstanceId &&
                    value.TargetParticipantId == participantId)
                .OrderBy(value => value.AppliedRound)
                .ThenBy(value => value.StatusInstanceId)
                .ToArray();
            var version = _participantVersions.GetValueOrDefault((battleInstanceId, participantId));
            return new BattleStatusSnapshot(
                battleInstanceId,
                participantId,
                version,
                StatusRuntimeCollections.Freeze(statuses),
                capturedAtUtc);
        }
    }

    public StatusInstance? GetStatusInstance(Guid statusInstanceId)
    {
        lock (_sync)
        {
            return _instances.GetValueOrDefault(statusInstanceId);
        }
    }

    public StatusInstance? GetStatus(Guid statusInstanceId) => GetStatusInstance(statusInstanceId);

    public IReadOnlyList<StatusInstance> LoadBattle(Guid battleInstanceId)
    {
        lock (_sync)
        {
            return StatusRuntimeCollections.Freeze(
                _instances.Values
                    .Where(value => value.BattleInstanceId == battleInstanceId)
                    .OrderBy(value => value.TargetParticipantId)
                    .ThenBy(value => value.AppliedRound)
                    .ThenBy(value => value.StatusInstanceId));
        }
    }

    public OperationResult<StatusInstance> AddStatusInstance(
        StatusInstance instance,
        long expectedParticipantStatusVersion,
        string idempotencySafeId)
    {
        lock (_sync)
        {
            if (_mutations.TryGetValue(idempotencySafeId, out var priorId) &&
                _instances.TryGetValue(priorId, out var prior))
            {
                return OperationResult<StatusInstance>.Success(prior);
            }

            var participantKey = (instance.BattleInstanceId, instance.TargetParticipantId);
            var participantVersion = _participantVersions.GetValueOrDefault(participantKey);
            if (participantVersion != expectedParticipantStatusVersion)
            {
                return Failure("status.authority_version_conflict", "Participant status version changed.");
            }

            if (_instances.ContainsKey(instance.StatusInstanceId))
            {
                return Failure("status.instance_duplicate", "Status instance already exists.");
            }

            var committed = instance with
            {
                LifecycleState = StatusLifecycleState.Active,
                RuntimeVersion = checked(instance.RuntimeVersion + 1)
            };
            _instances.Add(committed.StatusInstanceId, committed);
            _participantVersions[participantKey] = checked(participantVersion + 1);
            _mutations[idempotencySafeId] = committed.StatusInstanceId;
            return OperationResult<StatusInstance>.Success(committed);
        }
    }

    public OperationResult<StatusInstance> UpdateStatusInstance(
        StatusInstance instance,
        long expectedStatusVersion,
        long expectedParticipantStatusVersion,
        string idempotencySafeId)
    {
        lock (_sync)
        {
            if (_mutations.TryGetValue(idempotencySafeId, out var priorId) &&
                _instances.TryGetValue(priorId, out var prior))
            {
                return OperationResult<StatusInstance>.Success(prior);
            }

            if (!_instances.TryGetValue(instance.StatusInstanceId, out var current))
            {
                return Failure("status.instance_not_found", "Status instance was not found.");
            }

            var participantKey = (instance.BattleInstanceId, instance.TargetParticipantId);
            var participantVersion = _participantVersions.GetValueOrDefault(participantKey);
            if (current.RuntimeVersion != expectedStatusVersion ||
                participantVersion != expectedParticipantStatusVersion)
            {
                return Failure("status.authority_version_conflict", "Status or participant version changed.");
            }

            if (!current.IsActive || instance.RuntimeVersion < current.RuntimeVersion)
            {
                return Failure("status.lifecycle_invalid", "Removed status cannot return to active or regress version.");
            }

            var committed = instance with
            {
                RuntimeVersion = checked(current.RuntimeVersion + 1)
            };
            _instances[committed.StatusInstanceId] = committed;
            _participantVersions[participantKey] = checked(participantVersion + 1);
            _mutations[idempotencySafeId] = committed.StatusInstanceId;
            return OperationResult<StatusInstance>.Success(committed);
        }
    }

    public OperationResult<StatusInstance> RemoveStatusInstance(
        Guid statusInstanceId,
        long expectedStatusVersion,
        long expectedParticipantStatusVersion,
        StatusRemovalReason reason,
        DateTimeOffset now,
        string idempotencySafeId)
    {
        lock (_sync)
        {
            if (_mutations.TryGetValue(idempotencySafeId, out var priorId) &&
                _instances.TryGetValue(priorId, out var prior))
            {
                return OperationResult<StatusInstance>.Success(prior);
            }

            if (!_instances.TryGetValue(statusInstanceId, out var current))
            {
                return Failure("status.instance_not_found", "Status instance was not found.");
            }

            if (!current.IsActive)
            {
                return OperationResult<StatusInstance>.Success(current);
            }

            var participantKey = (current.BattleInstanceId, current.TargetParticipantId);
            var participantVersion = _participantVersions.GetValueOrDefault(participantKey);
            if (current.RuntimeVersion != expectedStatusVersion ||
                participantVersion != expectedParticipantStatusVersion)
            {
                return Failure("status.authority_version_conflict", "Status or participant version changed.");
            }

            var lifecycle = reason == StatusRemovalReason.Expired
                ? StatusLifecycleState.Expired
                : StatusLifecycleState.Removed;
            var committed = current with
            {
                LifecycleState = lifecycle,
                RuntimeVersion = checked(current.RuntimeVersion + 1),
                UpdatedAtUtc = now,
                RemovedAtUtc = now,
                RemovalReason = reason
            };
            _instances[statusInstanceId] = committed;
            _participantVersions[participantKey] = checked(participantVersion + 1);
            _mutations[idempotencySafeId] = statusInstanceId;
            return OperationResult<StatusInstance>.Success(committed);
        }
    }

    public OperationResult Reload(Guid battleInstanceId, IEnumerable<StatusInstance> statuses)
    {
        lock (_sync)
        {
            var incoming = statuses.ToArray();
            if (incoming.Any(value => value.BattleInstanceId != battleInstanceId) ||
                incoming.Select(value => value.StatusInstanceId).Distinct().Count() != incoming.Length)
            {
                return OperationResult.Failure(
                    "status.reload_invalid",
                    "Reload status identities do not match the battle.");
            }

            foreach (var id in _instances.Values
                         .Where(value => value.BattleInstanceId == battleInstanceId)
                         .Select(value => value.StatusInstanceId)
                         .ToArray())
            {
                _instances.Remove(id);
            }

            foreach (var status in incoming)
            {
                _instances[status.StatusInstanceId] = status;
            }

            foreach (var group in incoming.GroupBy(value => value.TargetParticipantId))
            {
                _participantVersions[(battleInstanceId, group.Key)] =
                    group.Select(value => value.RuntimeVersion).DefaultIfEmpty(0).Max();
            }

            return OperationResult.Success;
        }
    }

    private static OperationResult<StatusInstance> Failure(string code, string message) =>
        OperationResult<StatusInstance>.Failure(code, message);
}

public sealed class InMemoryStatusRuntimeStore :
    IStatusApplicationStore,
    IStatusTriggerExecutionStore,
    IStatusInstanceStore,
    IStatusInspectorSource
{
    private readonly object _sync = new();
    private readonly IStatusInstanceStore _instances;
    private readonly Dictionary<string, StatusApplicationRecord> _applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatusRemovalRecord> _removals = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, StatusTriggerPlanRecord> _triggerPlans = [];
    private readonly List<StatusModifierInspectorItem> _modifiers = [];
    private readonly List<StatusRestrictionInspectorItem> _restrictions = [];

    public InMemoryStatusRuntimeStore(IStatusInstanceStore instances)
    {
        _instances = instances;
    }

    public IReadOnlyList<StatusApplicationRecord> Applications
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(
                    _applications.Values.OrderBy(value => value.Request.CreatedAtUtc));
            }
        }
    }

    public IReadOnlyList<StatusRemovalRecord> Removals
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(
                    _removals.Values.OrderBy(value => value.Request.CreatedAtUtc));
            }
        }
    }

    public IReadOnlyList<StatusTriggerPlanRecord> TriggerPlans
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(
                    _triggerPlans.Values.OrderBy(value => value.Plan.CreatedAtUtc));
            }
        }
    }

    public IReadOnlyList<StatusInstance> StatusInstances
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(
                    _applications.Values
                        .Select(value => value.Plan.BattleInstanceId)
                        .Concat(_removals.Values.Select(value => value.Plan.BattleInstanceId))
                        .Distinct()
                        .SelectMany(_instances.LoadBattle)
                        .DistinctBy(value => value.StatusInstanceId)
                        .OrderBy(value => value.AppliedAtUtc));
            }
        }
    }

    public IReadOnlyList<StatusTriggerPlanRecord> StatusTriggerPlans => TriggerPlans;

    public IReadOnlyList<StatusModifierInspectorItem> StatusModifierSnapshots
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(_modifiers);
            }
        }
    }

    public IReadOnlyList<StatusRestrictionInspectorItem> StatusRestrictionResults
    {
        get
        {
            lock (_sync)
            {
                return StatusRuntimeCollections.Freeze(_restrictions);
            }
        }
    }

    public StatusApplicationRecord? GetApplication(string idempotencySafeId)
    {
        lock (_sync)
        {
            return _applications.GetValueOrDefault(idempotencySafeId);
        }
    }

    public StatusRemovalRecord? GetRemoval(string idempotencySafeId)
    {
        lock (_sync)
        {
            return _removals.GetValueOrDefault(idempotencySafeId);
        }
    }

    public OperationResult SaveApplication(StatusApplicationRecord record, StatusMutationState expectedState)
    {
        lock (_sync)
        {
            var key = BattleRuntimeHash.SafeId(record.Request.IdempotencyKey);
            if (!_applications.TryGetValue(key, out var current))
            {
                if (expectedState != StatusMutationState.Planned)
                {
                    return OperationResult.Failure(
                        "status.application_version_conflict",
                        "Application record does not exist at the expected state.");
                }

                _applications[key] = record;
                return OperationResult.Success;
            }

            if (current.State != expectedState)
            {
                return OperationResult.Failure(
                    "status.application_version_conflict",
                    "Application state changed.");
            }

            _applications[key] = record;
            return OperationResult.Success;
        }
    }

    public OperationResult SaveRemoval(StatusRemovalRecord record, StatusMutationState expectedState)
    {
        lock (_sync)
        {
            var key = BattleRuntimeHash.SafeId(record.Request.IdempotencyKey);
            if (!_removals.TryGetValue(key, out var current))
            {
                if (expectedState != StatusMutationState.Planned)
                {
                    return OperationResult.Failure(
                        "status.removal_version_conflict",
                        "Removal record does not exist at the expected state.");
                }

                _removals[key] = record;
                return OperationResult.Success;
            }

            if (current.State != expectedState)
            {
                return OperationResult.Failure(
                    "status.removal_version_conflict",
                    "Removal state changed.");
            }

            _removals[key] = record;
            return OperationResult.Success;
        }
    }

    public StatusInstance? GetStatus(Guid statusInstanceId) => _instances.GetStatus(statusInstanceId);

    public IReadOnlyList<StatusInstance> LoadBattle(Guid battleInstanceId) =>
        _instances.LoadBattle(battleInstanceId);

    public StatusTriggerPlanRecord? GetTriggerPlan(Guid planId)
    {
        lock (_sync)
        {
            return _triggerPlans.GetValueOrDefault(planId);
        }
    }

    public StatusTriggerExecutionResult? GetTriggerResult(Guid triggerExecutionId)
    {
        lock (_sync)
        {
            return _triggerPlans.Values
                .SelectMany(value => value.Results)
                .FirstOrDefault(value => value.TriggerExecutionId == triggerExecutionId);
        }
    }

    public OperationResult SaveTriggerPlan(StatusTriggerPlanRecord record, int expectedCursor)
    {
        lock (_sync)
        {
            var id = record.Plan.StatusTriggerExecutionPlanId;
            if (!_triggerPlans.TryGetValue(id, out var current))
            {
                if (expectedCursor != -1)
                {
                    return OperationResult.Failure(
                        "status.trigger_cursor_conflict",
                        "Trigger plan does not exist at the expected cursor.");
                }

                _triggerPlans[id] = record;
                return OperationResult.Success;
            }

            if (current.ResolutionCursor != expectedCursor)
            {
                return OperationResult.Failure(
                    "status.trigger_cursor_conflict",
                    "Trigger cursor changed.");
            }

            _triggerPlans[id] = record;
            return OperationResult.Success;
        }
    }

    public void RecordModifier(StatusModifierInspectorItem item)
    {
        lock (_sync)
        {
            _modifiers.Add(item);
        }
    }

    public void RecordRestriction(StatusRestrictionInspectorItem item)
    {
        lock (_sync)
        {
            _restrictions.Add(item);
        }
    }
}

public sealed class DeterministicStatusStackPolicy : IStatusStackPolicy
{
    public StatusStackDecision Evaluate(StatusDefinition definition, StatusInstance? existing)
    {
        var maximum = definition.StackPolicy.MaximumStacks ?? 1;
        if (existing is null || !existing.IsActive)
        {
            return Success(StatusApplicationOperation.AddNew, 0, 1, definition.StackPolicy.PolicyStatus);
        }

        return definition.StackPolicy.PolicyType switch
        {
            StatusStackPolicyType.RejectDuplicate => new StatusStackDecision(
                StatusApplicationOperation.RejectDuplicate,
                existing.StackCount,
                existing.StackCount,
                StatusResultCode.DuplicateStatusRejected,
                "status.duplicate_rejected",
                definition.StackPolicy.PolicyStatus),
            StatusStackPolicyType.RefreshDuration => Success(
                StatusApplicationOperation.RefreshDuration,
                existing.StackCount,
                existing.StackCount,
                definition.StackPolicy.PolicyStatus),
            StatusStackPolicyType.ReplaceExisting => Success(
                StatusApplicationOperation.ReplaceExisting,
                existing.StackCount,
                1,
                definition.StackPolicy.PolicyStatus),
            StatusStackPolicyType.AddStacks when definition.PolicyStatus == CombatPolicyStatus.TestOnly &&
                                                  existing.StackCount < maximum => Success(
                StatusApplicationOperation.AddStack,
                existing.StackCount,
                checked(existing.StackCount + 1),
                definition.StackPolicy.PolicyStatus),
            StatusStackPolicyType.AddStacks when existing.StackCount >= maximum => new StatusStackDecision(
                StatusApplicationOperation.AddStack,
                existing.StackCount,
                existing.StackCount,
                StatusResultCode.StackLimitReached,
                "status.stack_limit_reached",
                definition.StackPolicy.PolicyStatus),
            _ => new StatusStackDecision(
                StatusApplicationOperation.EvidenceBlocked,
                existing.StackCount,
                existing.StackCount,
                StatusResultCode.StackPolicyEvidenceBlocked,
                "status.stack_policy_evidence_blocked",
                CombatPolicyStatus.EvidenceBlocked)
        };
    }

    private static StatusStackDecision Success(
        StatusApplicationOperation operation,
        int before,
        int after,
        CombatPolicyStatus policyStatus) =>
        new(
            operation,
            before,
            after,
            StatusResultCode.Success,
            "",
            policyStatus);
}

public sealed class RoundBasedStatusDurationPolicy : IStatusDurationPolicy
{
    public StatusDurationDecision Evaluate(
        StatusDefinition definition,
        StatusInstance? existing,
        int roundNumber)
    {
        var before = existing?.RemainingRounds;
        return definition.DurationPolicy.PolicyType switch
        {
            StatusDurationPolicyType.Rounds
                when definition.DurationPolicy.DurationRounds is > 0 &&
                     definition.PolicyStatus is (
                         CombatPolicyStatus.TestOnly or
                         CombatPolicyStatus.Verified or
                         CombatPolicyStatus.ContentBacked) &&
                     definition.DurationPolicy.PolicyStatus is (
                         CombatPolicyStatus.TestOnly or
                         CombatPolicyStatus.Verified or
                         CombatPolicyStatus.ContentBacked) =>
                Success(
                    before,
                    definition.DurationPolicy.DurationRounds,
                    checked(roundNumber + definition.DurationPolicy.DurationRounds.Value - 1),
                    definition.DurationPolicy.DurationRounds,
                    definition.DurationPolicy.PolicyStatus),
            StatusDurationPolicyType.UntilBattleEnd or StatusDurationPolicyType.UntilRemoved =>
                Success(
                    before,
                    null,
                    null,
                    null,
                    definition.DurationPolicy.PolicyStatus),
            _ => new StatusDurationDecision(
                before,
                before,
                existing?.ExpiresAfterRound,
                existing?.RemainingRounds,
                StatusResultCode.DurationEvidenceBlocked,
                "status.duration_evidence_blocked",
                CombatPolicyStatus.EvidenceBlocked)
        };
    }

    public StatusDurationDecision CloseRound(
        StatusDefinition definition,
        StatusInstance instance,
        int roundNumber)
    {
        if (definition.DurationPolicy.PolicyType != StatusDurationPolicyType.Rounds ||
            instance.ExpiresAfterRound is null)
        {
            return Success(
                instance.RemainingRounds,
                instance.RemainingRounds,
                instance.ExpiresAfterRound,
                instance.RemainingRounds,
                definition.DurationPolicy.PolicyStatus);
        }

        var remaining = Math.Max(0, instance.ExpiresAfterRound.Value - roundNumber);
        return Success(
            instance.RemainingRounds,
            remaining,
            instance.ExpiresAfterRound,
            remaining,
            definition.DurationPolicy.PolicyStatus);
    }

    private static StatusDurationDecision Success(
        int? before,
        int? after,
        int? expires,
        int? remaining,
        CombatPolicyStatus policyStatus) =>
        new(
            before,
            after,
            expires,
            remaining,
            StatusResultCode.Success,
            "",
            policyStatus);
}

public sealed class StatusApplicationPlanner : IStatusApplicationPlanner
{
    public OperationResult<(StatusApplicationPlan Plan, StatusInstance ProposedInstance)> Create(
        StatusApplicationRequest request,
        StatusDefinition definition,
        StatusInstance? existing,
        StatusStackDecision stack,
        StatusDurationDecision duration,
        DateTimeOffset now)
    {
        if (stack.ResultCode != StatusResultCode.Success ||
            duration.ResultCode != StatusResultCode.Success)
        {
            return OperationResult<(StatusApplicationPlan, StatusInstance)>.Failure(
                stack.ResultCode != StatusResultCode.Success ? stack.FailureCode : duration.FailureCode,
                "Status application policy did not produce an executable plan.");
        }

        var planId = BattleRuntimeHash.DeterministicGuid(
            $"status-application-plan:{request.StatusApplicationId:N}");
        var instanceId = stack.Operation is
            StatusApplicationOperation.RefreshDuration or
            StatusApplicationOperation.AddStack
            ? existing!.StatusInstanceId
            : BattleRuntimeHash.DeterministicGuid(
                $"status-instance:{request.BattleInstanceId:N}:{request.StatusApplicationId:N}");
        var maximumStacks = definition.StackPolicy.MaximumStacks ?? 1;
        var plan = new StatusApplicationPlan(
            planId,
            request.StatusApplicationId,
            instanceId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceParticipantId,
            request.TargetParticipantId,
            request.StatusDefinitionId,
            existing?.StatusInstanceId,
            stack.Operation,
            stack.StackBefore,
            stack.StackAfter,
            duration.DurationBefore,
            duration.DurationAfter,
            request.ExpectedTargetVersion,
            existing?.RuntimeVersion ?? -1,
            StatusRuntimeCollections.Freeze(new[]
            {
                definition.PolicyStatus,
                definition.StackPolicy.PolicyStatus,
                definition.DurationPolicy.PolicyStatus
            }),
            now,
            request.CorrelationId);
        var proposed = new StatusInstance(
            instanceId,
            definition.StatusDefinitionId,
            request.BattleInstanceId,
            request.TargetParticipantId,
            request.SourceParticipantId,
            request.SourceActionId,
            request.SourceSkillExecutionId,
            request.SourceEffectExecutionId,
            stack.StackAfter,
            maximumStacks,
            stack.Operation == StatusApplicationOperation.ReplaceExisting
                ? request.RoundNumber
                : existing?.AppliedRound ?? request.RoundNumber,
            request.RoundNumber,
            duration.ExpiresAfterRound,
            duration.RemainingRounds,
            stack.Operation is StatusApplicationOperation.RefreshDuration or StatusApplicationOperation.AddStack
                ? StatusLifecycleState.Active
                : StatusLifecycleState.Created,
            existing?.TriggerCursor ?? new StatusTriggerCursor(
                0,
                0,
                null,
                StatusRecoveryState.NotRequired,
                0),
            stack.Operation is StatusApplicationOperation.RefreshDuration or StatusApplicationOperation.AddStack
                ? existing!.RuntimeVersion
                : 0,
            definition.ContentVersion,
            definition.PolicyStatus,
            stack.Operation == StatusApplicationOperation.ReplaceExisting
                ? now
                : existing?.AppliedAtUtc ?? now,
            now,
            null,
            null,
            request.CorrelationId,
            definition.RawMetadata);
        return OperationResult<(StatusApplicationPlan, StatusInstance)>.Success((plan, proposed));
    }
}

public sealed class InMemoryStatusEventSink : IStatusEventSink
{
    private readonly ConcurrentQueue<StatusEvent> _events = [];

    public IReadOnlyList<StatusEvent> Snapshot => _events.ToArray();

    public void Publish(StatusEvent runtimeEvent) => _events.Enqueue(runtimeEvent);
}

public sealed class InMemoryStatusAuditLedger : IStatusAuditLedger
{
    private readonly ConcurrentQueue<StatusAuditRecord> _records = [];

    public IReadOnlyList<StatusAuditRecord> Snapshot => _records.ToArray();

    public void Append(StatusAuditRecord record) => _records.Enqueue(record);
}

public sealed class StatusEffectCoordinator :
    IStatusEffectCoordinator,
    IStatusRecoveryCoordinator
{
    private readonly IStatusDefinitionCatalog _catalog;
    private readonly IBattleStatusAuthority _authority;
    private readonly IStatusApplicationPlanner _planner;
    private readonly IStatusApplicationStore _store;
    private readonly IStatusStackPolicy _stack;
    private readonly IStatusDurationPolicy _duration;
    private readonly IStatusEventSink _events;
    private readonly IStatusAuditLedger _audit;
    private readonly IBattleClock _clock;
    private readonly AsyncKeyedLock<(Guid BattleId, Guid ParticipantId)> _locks = new();

    public StatusEffectCoordinator(
        IStatusDefinitionCatalog catalog,
        IBattleStatusAuthority authority,
        IStatusApplicationPlanner planner,
        IStatusApplicationStore store,
        IStatusStackPolicy stack,
        IStatusDurationPolicy duration,
        IStatusEventSink events,
        IStatusAuditLedger audit,
        IBattleClock clock)
    {
        _catalog = catalog;
        _authority = authority;
        _planner = planner;
        _store = store;
        _stack = stack;
        _duration = duration;
        _events = events;
        _audit = audit;
        _clock = clock;
    }

    public StatusFailureInjection FailureInjection { get; } = new();

    public async Task<StatusApplicationResult> ApplyAsync(
        BattleInstance battle,
        StatusApplicationRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _locks.AcquireAsync(
            (request.BattleInstanceId, request.TargetParticipantId),
            cancellationToken);
        Publish(
            StatusEventKind.StatusApplicationRequested,
            request.BattleInstanceId,
            request.RoundNumber,
            request.StatusApplicationId,
            null,
            null,
            request.StatusDefinitionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            "",
            request.CorrelationId);
        var safeId = BattleRuntimeHash.SafeId(request.IdempotencyKey);
        var payloadHash = ApplicationPayloadHash(request);
        var replay = _store.GetApplication(safeId);
        if (replay is not null)
        {
            if (!string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return ApplicationFailure(
                    request,
                    StatusResultCode.ReplayConflict,
                    "status.application_replay_conflict");
            }

            if (replay.Result is not null)
            {
                return replay.Result with
                {
                    ResultCode = StatusResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
            }

            return RecoverApplicationRecord(battle, replay);
        }

        var validation = ValidateApplication(battle, request);
        if (validation is not null)
        {
            return validation;
        }

        var catalogSource = ResolveCatalogSource(battle, request.Source);
        var definitionResult = _catalog.Get(request.StatusDefinitionId, catalogSource);
        if (!definitionResult.Succeeded || definitionResult.Value is null)
        {
            var code = definitionResult.Error.Code switch
            {
                "status.not_found" => StatusResultCode.StatusNotFound,
                "status.disabled" => StatusResultCode.StatusDisabled,
                _ => StatusResultCode.StatusEvidenceBlocked
            };
            return ApplicationFailure(request, code, definitionResult.Error.Code);
        }

        var definition = definitionResult.Value;
        Publish(
            StatusEventKind.StatusDefinitionResolved,
            request.BattleInstanceId,
            request.RoundNumber,
            request.StatusApplicationId,
            null,
            null,
            request.StatusDefinitionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            "",
            request.CorrelationId);
        var snapshot = _authority.GetParticipantSnapshot(
            request.BattleInstanceId,
            request.TargetParticipantId,
            _clock.UtcNow);
        if (snapshot.StatusVersion != request.ExpectedTargetVersion)
        {
            return ApplicationFailure(
                request,
                StatusResultCode.VersionConflict,
                "status.target_version_conflict",
                snapshot.StatusVersion);
        }

        var existing = snapshot.Statuses
            .Where(value => value.StatusDefinitionId == request.StatusDefinitionId && value.IsActive)
            .OrderBy(value => value.AppliedRound)
            .ThenBy(value => value.StatusInstanceId)
            .FirstOrDefault();
        var stack = _stack.Evaluate(definition, existing);
        if (stack.ResultCode != StatusResultCode.Success)
        {
            return ApplicationFailure(
                request,
                stack.ResultCode,
                stack.FailureCode,
                snapshot.StatusVersion,
                stack);
        }

        var duration = _duration.Evaluate(definition, existing, request.RoundNumber);
        if (duration.ResultCode != StatusResultCode.Success)
        {
            return ApplicationFailure(
                request,
                duration.ResultCode,
                duration.FailureCode,
                snapshot.StatusVersion,
                stack,
                duration);
        }

        var planned = _planner.Create(
            request,
            definition,
            existing,
            stack,
            duration,
            _clock.UtcNow);
        if (!planned.Succeeded)
        {
            return ApplicationFailure(
                request,
                StatusResultCode.InternalFailure,
                planned.Error.Code,
                snapshot.StatusVersion,
                stack,
                duration);
        }

        var plan = planned.Value.Plan;
        var record = new StatusApplicationRecord(
            request,
            plan,
            planned.Value.ProposedInstance,
            payloadHash,
            StatusMutationState.Planned,
            null,
            StatusRecoveryState.NotRequired,
            _clock.UtcNow);
        if (FailureInjection.Point == StatusFailurePoint.ApplicationPersistence ||
            !_store.SaveApplication(record, StatusMutationState.Planned).Succeeded)
        {
            return ApplicationFailure(
                request,
                StatusResultCode.PersistenceFailure,
                "status.application_persistence_failed",
                snapshot.StatusVersion,
                stack,
                duration,
                plan);
        }

        return CommitApplication(record, existing, definition);
    }

    public async Task<StatusRemovalResult> RemoveAsync(
        BattleInstance battle,
        StatusRemovalRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _locks.AcquireAsync(
            (request.BattleInstanceId, request.TargetParticipantId),
            cancellationToken);
        Publish(
            StatusEventKind.StatusRemovalRequested,
            request.BattleInstanceId,
            request.RoundNumber,
            null,
            request.StatusRemovalId,
            request.StatusInstanceId,
            request.StatusDefinitionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            "",
            request.CorrelationId);
        var safeId = BattleRuntimeHash.SafeId(request.IdempotencyKey);
        var payloadHash = RemovalPayloadHash(request);
        var replay = _store.GetRemoval(safeId);
        if (replay is not null)
        {
            if (!string.Equals(replay.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return RemovalFailure(
                    request,
                    StatusResultCode.ReplayConflict,
                    "status.removal_replay_conflict");
            }

            if (replay.Result is not null)
            {
                return replay.Result with
                {
                    ResultCode = StatusResultCode.DuplicateCompleted,
                    IsDuplicate = true
                };
            }

            return CommitRemoval(replay);
        }

        var validation = ValidateRemoval(battle, request);
        if (validation is not null)
        {
            return validation;
        }

        var snapshot = _authority.GetParticipantSnapshot(
            request.BattleInstanceId,
            request.TargetParticipantId,
            _clock.UtcNow);
        if (snapshot.StatusVersion != request.ExpectedTargetVersion)
        {
            return RemovalFailure(
                request,
                StatusResultCode.VersionConflict,
                "status.target_version_conflict",
                snapshot.StatusVersion);
        }

        var status = request.StatusInstanceId is not null
            ? snapshot.Statuses.FirstOrDefault(value => value.StatusInstanceId == request.StatusInstanceId)
            : snapshot.Statuses
                .Where(value => value.StatusDefinitionId == request.StatusDefinitionId && value.IsActive)
                .OrderBy(value => value.AppliedRound)
                .ThenBy(value => value.StatusInstanceId)
                .FirstOrDefault();
        if (status is null)
        {
            return RemovalFailure(
                request,
                StatusResultCode.StatusNotFound,
                "status.instance_not_found",
                snapshot.StatusVersion);
        }

        if (!status.IsActive)
        {
            return new StatusRemovalResult(
                request.StatusRemovalId,
                null,
                status.StatusInstanceId,
                request.BattleInstanceId,
                request.RoundNumber,
                request.TargetParticipantId,
                status.StatusDefinitionId,
                StatusResultCode.DuplicateCompleted,
                "",
                true,
                request.RemovalReason,
                snapshot.StatusVersion,
                snapshot.StatusVersion,
                status.RuntimeVersion,
                status.RuntimeVersion,
                StatusRecoveryState.NotRequired,
                [],
                _clock.UtcNow);
        }

        if (request.Source == StatusRequestSource.OfficialClient)
        {
            return RemovalFailure(
                request,
                StatusResultCode.InvalidSource,
                "status.packet_handler_direct_remove_denied",
                snapshot.StatusVersion);
        }

        var plan = new StatusRemovalPlan(
            BattleRuntimeHash.DeterministicGuid($"status-removal-plan:{request.StatusRemovalId:N}"),
            request.StatusRemovalId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceParticipantId,
            request.TargetParticipantId,
            status.StatusInstanceId,
            status.StatusDefinitionId,
            request.RemovalReason,
            request.ExpectedTargetVersion,
            status.RuntimeVersion,
            _clock.UtcNow,
            request.CorrelationId);
        var record = new StatusRemovalRecord(
            request,
            plan,
            payloadHash,
            StatusMutationState.Planned,
            null,
            StatusRecoveryState.NotRequired,
            _clock.UtcNow);
        if (FailureInjection.Point == StatusFailurePoint.RemovalPersistence ||
            !_store.SaveRemoval(record, StatusMutationState.Planned).Succeeded)
        {
            return RemovalFailure(
                request,
                StatusResultCode.PersistenceFailure,
                "status.removal_persistence_failed",
                snapshot.StatusVersion,
                status,
                plan);
        }

        return CommitRemoval(record);
    }

    public Task<StatusRemovalResult> CleanupAsync(
        BattleInstance battle,
        StatusInstance status,
        StatusRemovalReason reason,
        CancellationToken cancellationToken)
    {
        var snapshot = _authority.GetParticipantSnapshot(
            battle.BattleInstanceId,
            status.TargetParticipantId,
            _clock.UtcNow);
        var removalId = BattleRuntimeHash.DeterministicGuid(
            $"status-cleanup:{battle.BattleInstanceId:N}:{status.StatusInstanceId:N}:{reason}");
        return RemoveAsync(
            battle,
            new StatusRemovalRequest(
                removalId,
                $"status-cleanup:{battle.BattleInstanceId:N}:{status.StatusInstanceId:N}:{reason}",
                battle.BattleInstanceId,
                battle.CurrentRoundNumber,
                Guid.Empty,
                status.SourceParticipantId,
                status.TargetParticipantId,
                status.StatusInstanceId,
                status.StatusDefinitionId,
                reason,
                snapshot.StatusVersion,
                StatusRequestSource.BattleRuntime,
                _clock.UtcNow,
                status.CorrelationId),
            cancellationToken);
    }

    public Task<StatusApplicationResult> RecoverApplicationAsync(
        Guid statusApplicationId,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _store.Applications.FirstOrDefault(
            value => value.Request.StatusApplicationId == statusApplicationId);
        return Task.FromResult(record is null
            ? ApplicationFailure(
                new StatusApplicationRequest(
                    statusApplicationId,
                    statusApplicationId.ToString("N"),
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    Guid.Empty,
                    null,
                    null,
                    Guid.Empty,
                    Guid.Empty,
                    0,
                    null,
                    null,
                    battle.BattleVersion,
                    0,
                    StatusRequestSource.Recovery,
                    _clock.UtcNow,
                    ""),
                StatusResultCode.StatusNotFound,
                "status.application_not_found")
            : RecoverApplicationRecord(battle, record));
    }

    public Task<StatusRemovalResult> RecoverRemovalAsync(
        Guid statusRemovalId,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _store.Removals.FirstOrDefault(
            value => value.Request.StatusRemovalId == statusRemovalId);
        return Task.FromResult(record is null
            ? RemovalFailure(
                new StatusRemovalRequest(
                    statusRemovalId,
                    statusRemovalId.ToString("N"),
                    battle.BattleInstanceId,
                    battle.CurrentRoundNumber,
                    Guid.Empty,
                    Guid.Empty,
                    Guid.Empty,
                    null,
                    null,
                    StatusRemovalReason.RecoveryCleanup,
                    0,
                    StatusRequestSource.Recovery,
                    _clock.UtcNow,
                    ""),
                StatusResultCode.StatusNotFound,
                "status.removal_not_found")
            : CommitRemoval(record));
    }

    private StatusApplicationResult CommitApplication(
        StatusApplicationRecord record,
        StatusInstance? existing,
        StatusDefinition definition)
    {
        if (FailureInjection.Point is StatusFailurePoint.RuntimeAdd or
            StatusFailurePoint.StackPersistence or
            StatusFailurePoint.DurationRefresh)
        {
            var recovery = record with
            {
                State = StatusMutationState.RecoveryRequired,
                RecoveryState = StatusRecoveryState.RecoveryRequired,
                UpdatedAtUtc = _clock.UtcNow
            };
            _ = _store.SaveApplication(recovery, StatusMutationState.Planned);
            PublishRecoveryRequired(record.Request, "status.runtime_mutation_failed");
            return ApplicationFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                "status.runtime_mutation_failed",
                record.Plan.ExpectedTargetVersion,
                plan: record.Plan,
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        OperationResult<StatusInstance> mutation;
        var participantVersionBefore = record.Plan.ExpectedTargetVersion;
        if (record.Plan.Operation == StatusApplicationOperation.AddNew)
        {
            mutation = _authority.AddStatusInstance(
                record.ProposedInstance,
                participantVersionBefore,
                BattleRuntimeHash.SafeId(record.Request.IdempotencyKey));
        }
        else if (record.Plan.Operation == StatusApplicationOperation.ReplaceExisting && existing is not null)
        {
            var removed = _authority.RemoveStatusInstance(
                existing.StatusInstanceId,
                existing.RuntimeVersion,
                participantVersionBefore,
                StatusRemovalReason.Replaced,
                _clock.UtcNow,
                BattleRuntimeHash.SafeId($"{record.Request.IdempotencyKey}:replace-remove"));
            mutation = !removed.Succeeded
                ? OperationResult<StatusInstance>.Failure(removed.Error.Code, removed.Error.Message)
                : _authority.AddStatusInstance(
                    record.ProposedInstance,
                    checked(participantVersionBefore + 1),
                    BattleRuntimeHash.SafeId($"{record.Request.IdempotencyKey}:replace-add"));
            if (mutation.Succeeded)
            {
                participantVersionBefore = checked(participantVersionBefore + 1);
            }
        }
        else
        {
            mutation = _authority.UpdateStatusInstance(
                record.ProposedInstance,
                record.Plan.ExpectedStatusVersion,
                participantVersionBefore,
                BattleRuntimeHash.SafeId(record.Request.IdempotencyKey));
        }

        if (!mutation.Succeeded || mutation.Value is null)
        {
            var recovery = record with
            {
                State = StatusMutationState.RecoveryRequired,
                RecoveryState = StatusRecoveryState.RecoveryRequired,
                UpdatedAtUtc = _clock.UtcNow
            };
            _ = _store.SaveApplication(recovery, StatusMutationState.Planned);
            PublishRecoveryRequired(record.Request, mutation.Error.Code);
            return ApplicationFailure(
                record.Request,
                mutation.Error.Code.Contains("version", StringComparison.Ordinal)
                    ? StatusResultCode.VersionConflict
                    : StatusResultCode.RecoveryRequired,
                mutation.Error.Code,
                participantVersionBefore,
                plan: record.Plan,
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        var committed = record with
        {
            State = StatusMutationState.RuntimeCommitted,
            ProposedInstance = mutation.Value,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (!_store.SaveApplication(committed, StatusMutationState.Planned).Succeeded)
        {
            PublishRecoveryRequired(record.Request, "status.application_result_persistence_failed");
            return ApplicationFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                "status.application_result_persistence_failed",
                participantVersionBefore,
                plan: record.Plan,
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        var result = new StatusApplicationResult(
            record.Request.StatusApplicationId,
            record.Plan.StatusApplicationPlanId,
            mutation.Value.StatusInstanceId,
            record.Request.BattleInstanceId,
            record.Request.RoundNumber,
            record.Request.TargetParticipantId,
            record.Request.StatusDefinitionId,
            StatusResultCode.Success,
            "",
            false,
            record.Plan.Operation,
            record.Plan.StackBefore,
            record.Plan.StackAfter,
            record.Plan.DurationBefore,
            record.Plan.DurationAfter,
            record.Plan.ExpectedTargetVersion,
            checked(participantVersionBefore + 1),
            record.Plan.ExpectedStatusVersion,
            mutation.Value.RuntimeVersion,
            StatusRecoveryState.NotRequired,
            [],
            _clock.UtcNow);
        var completed = committed with
        {
            State = StatusMutationState.Completed,
            Result = result,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (!_store.SaveApplication(completed, StatusMutationState.RuntimeCommitted).Succeeded)
        {
            PublishRecoveryRequired(record.Request, "status.application_completion_persistence_failed");
            return result with
            {
                ResultCode = StatusResultCode.RecoveryRequired,
                FailureCode = "status.application_completion_persistence_failed",
                RecoveryState = StatusRecoveryState.RecoveryRequired
            };
        }

        var eventKind = record.Plan.Operation switch
        {
            StatusApplicationOperation.AddStack => StatusEventKind.StatusStackAdded,
            StatusApplicationOperation.RefreshDuration => StatusEventKind.StatusDurationRefreshed,
            StatusApplicationOperation.ReplaceExisting => StatusEventKind.StatusReplaced,
            _ => StatusEventKind.StatusApplied
        };
        Publish(
            eventKind,
            record.Request.BattleInstanceId,
            record.Request.RoundNumber,
            record.Request.StatusApplicationId,
            null,
            mutation.Value.StatusInstanceId,
            record.Request.StatusDefinitionId,
            record.Request.SourceParticipantId,
            record.Request.TargetParticipantId,
            mutation.Value.LifecycleState.ToString(),
            record.Request.CorrelationId);
        AppendApplicationAudit(record, result, definition);
        return result;
    }

    private StatusApplicationResult RecoverApplicationRecord(
        BattleInstance battle,
        StatusApplicationRecord record)
    {
        Publish(
            StatusEventKind.StatusRecoveryStarted,
            record.Request.BattleInstanceId,
            record.Request.RoundNumber,
            record.Request.StatusApplicationId,
            null,
            record.Plan.StatusInstanceId,
            record.Request.StatusDefinitionId,
            record.Request.SourceParticipantId,
            record.Request.TargetParticipantId,
            record.State.ToString(),
            record.Request.CorrelationId);
        if (FailureInjection.Point is StatusFailurePoint.Recovery or StatusFailurePoint.MissingPersistedDefinition)
        {
            PublishRecoveryRequired(record.Request, "status.recovery_failed");
            return ApplicationFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                "status.recovery_failed",
                record.Plan.ExpectedTargetVersion,
                plan: record.Plan,
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        var definition = _catalog.Get(
            record.Plan.StatusDefinitionId,
            ResolveCatalogSource(battle, StatusRequestSource.Recovery));
        if (!definition.Succeeded || definition.Value is null)
        {
            PublishRecoveryRequired(record.Request, "status.persisted_definition_unavailable");
            return ApplicationFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                "status.persisted_definition_unavailable",
                record.Plan.ExpectedTargetVersion,
                plan: record.Plan,
                recoveryState: StatusRecoveryState.RecoveryRequired);
        }

        var current = _authority.GetStatusInstance(record.Plan.StatusInstanceId);
        if (current is not null && current.RuntimeVersion > record.Plan.ExpectedStatusVersion)
        {
            var snapshot = _authority.GetParticipantSnapshot(
                record.Plan.BattleInstanceId,
                record.Plan.TargetParticipantId,
                _clock.UtcNow);
            var recoveredResult = new StatusApplicationResult(
                record.Request.StatusApplicationId,
                record.Plan.StatusApplicationPlanId,
                current.StatusInstanceId,
                record.Plan.BattleInstanceId,
                record.Plan.RoundNumber,
                record.Plan.TargetParticipantId,
                record.Plan.StatusDefinitionId,
                StatusResultCode.DuplicateCompleted,
                "",
                true,
                record.Plan.Operation,
                record.Plan.StackBefore,
                current.StackCount,
                record.Plan.DurationBefore,
                current.RemainingRounds,
                record.Plan.ExpectedTargetVersion,
                snapshot.StatusVersion,
                record.Plan.ExpectedStatusVersion,
                current.RuntimeVersion,
                StatusRecoveryState.Recovered,
                [],
                _clock.UtcNow);
            var expected = record.State;
            var recovered = record with
            {
                State = StatusMutationState.Completed,
                Result = recoveredResult,
                RecoveryState = StatusRecoveryState.Recovered,
                ProposedInstance = current,
                UpdatedAtUtc = _clock.UtcNow
            };
            _ = _store.SaveApplication(recovered, expected);
            Publish(
                StatusEventKind.StatusRecoveryCompleted,
                record.Plan.BattleInstanceId,
                record.Plan.RoundNumber,
                record.Request.StatusApplicationId,
                null,
                current.StatusInstanceId,
                current.StatusDefinitionId,
                current.SourceParticipantId,
                current.TargetParticipantId,
                StatusRecoveryState.Recovered.ToString(),
                record.Request.CorrelationId);
            return recoveredResult;
        }

        var existing = record.Plan.ExistingStatusInstanceId is null
            ? null
            : _authority.GetStatusInstance(record.Plan.ExistingStatusInstanceId.Value);
        var reset = record with
        {
            State = StatusMutationState.Planned,
            RecoveryState = StatusRecoveryState.Recovering,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (record.State != StatusMutationState.Planned)
        {
            _ = _store.SaveApplication(reset, record.State);
        }

        return CommitApplication(reset, existing, definition.Value);
    }

    private StatusRemovalResult CommitRemoval(StatusRemovalRecord record)
    {
        var current = _authority.GetStatusInstance(record.Plan.StatusInstanceId);
        if (current is null)
        {
            return RemovalFailure(
                record.Request,
                StatusResultCode.StatusNotFound,
                "status.instance_not_found",
                record.Plan.ExpectedTargetVersion,
                plan: record.Plan);
        }

        if (!current.IsActive)
        {
            var duplicate = new StatusRemovalResult(
                record.Request.StatusRemovalId,
                record.Plan.StatusRemovalPlanId,
                current.StatusInstanceId,
                record.Plan.BattleInstanceId,
                record.Plan.RoundNumber,
                record.Plan.TargetParticipantId,
                current.StatusDefinitionId,
                StatusResultCode.DuplicateCompleted,
                "",
                true,
                record.Plan.RemovalReason,
                record.Plan.ExpectedTargetVersion,
                record.Plan.ExpectedTargetVersion,
                record.Plan.ExpectedStatusVersion,
                current.RuntimeVersion,
                StatusRecoveryState.Recovered,
                [],
                _clock.UtcNow);
            var duplicateCompleted = record with
            {
                State = StatusMutationState.Completed,
                Result = duplicate,
                RecoveryState = StatusRecoveryState.Recovered,
                UpdatedAtUtc = _clock.UtcNow
            };
            _ = _store.SaveRemoval(duplicateCompleted, record.State);
            return duplicate;
        }

        var mutation = _authority.RemoveStatusInstance(
            record.Plan.StatusInstanceId,
            record.Plan.ExpectedStatusVersion,
            record.Plan.ExpectedTargetVersion,
            record.Plan.RemovalReason,
            _clock.UtcNow,
            BattleRuntimeHash.SafeId(record.Request.IdempotencyKey));
        if (!mutation.Succeeded || mutation.Value is null)
        {
            var recovery = record with
            {
                State = StatusMutationState.RecoveryRequired,
                RecoveryState = StatusRecoveryState.RecoveryRequired,
                UpdatedAtUtc = _clock.UtcNow
            };
            _ = _store.SaveRemoval(recovery, record.State);
            return RemovalFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                mutation.Error.Code,
                record.Plan.ExpectedTargetVersion,
                current,
                record.Plan,
                StatusRecoveryState.RecoveryRequired);
        }

        var runtimeCommitted = record with
        {
            State = StatusMutationState.RuntimeCommitted,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (!_store.SaveRemoval(runtimeCommitted, record.State).Succeeded)
        {
            return RemovalFailure(
                record.Request,
                StatusResultCode.RecoveryRequired,
                "status.removal_result_persistence_failed",
                record.Plan.ExpectedTargetVersion,
                current,
                record.Plan,
                StatusRecoveryState.RecoveryRequired);
        }

        var result = new StatusRemovalResult(
            record.Request.StatusRemovalId,
            record.Plan.StatusRemovalPlanId,
            mutation.Value.StatusInstanceId,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            record.Plan.TargetParticipantId,
            mutation.Value.StatusDefinitionId,
            StatusResultCode.Success,
            "",
            false,
            record.Plan.RemovalReason,
            record.Plan.ExpectedTargetVersion,
            checked(record.Plan.ExpectedTargetVersion + 1),
            record.Plan.ExpectedStatusVersion,
            mutation.Value.RuntimeVersion,
            StatusRecoveryState.NotRequired,
            [],
            _clock.UtcNow);
        var completed = runtimeCommitted with
        {
            State = StatusMutationState.Completed,
            Result = result,
            UpdatedAtUtc = _clock.UtcNow
        };
        if (!_store.SaveRemoval(completed, StatusMutationState.RuntimeCommitted).Succeeded)
        {
            return result with
            {
                ResultCode = StatusResultCode.RecoveryRequired,
                FailureCode = "status.removal_completion_persistence_failed",
                RecoveryState = StatusRecoveryState.RecoveryRequired
            };
        }

        Publish(
            record.Plan.RemovalReason == StatusRemovalReason.Expired
                ? StatusEventKind.StatusExpired
                : StatusEventKind.StatusRemoved,
            record.Plan.BattleInstanceId,
            record.Plan.RoundNumber,
            null,
            record.Request.StatusRemovalId,
            mutation.Value.StatusInstanceId,
            mutation.Value.StatusDefinitionId,
            mutation.Value.SourceParticipantId,
            mutation.Value.TargetParticipantId,
            mutation.Value.LifecycleState.ToString(),
            record.Request.CorrelationId);
        AppendRemovalAudit(record, result);
        return result;
    }

    private StatusApplicationResult? ValidateApplication(
        BattleInstance battle,
        StatusApplicationRequest request)
    {
        if (request.BattleInstanceId != battle.BattleInstanceId ||
            FailureInjection.Point == StatusFailurePoint.WrongBattle)
        {
            return ApplicationFailure(request, StatusResultCode.BattleNotFound, "status.battle_mismatch");
        }

        if (battle.State != BattleState.Active ||
            request.RoundNumber != battle.CurrentRoundNumber ||
            FailureInjection.Point == StatusFailurePoint.WrongRound)
        {
            return ApplicationFailure(request, StatusResultCode.InvalidRound, "status.invalid_round");
        }

        if (request.ExpectedBattleVersion != battle.BattleVersion)
        {
            return ApplicationFailure(request, StatusResultCode.VersionConflict, "status.battle_version_conflict");
        }

        if (request.Source == StatusRequestSource.OfficialClient)
        {
            return ApplicationFailure(
                request,
                StatusResultCode.InvalidSource,
                "status.packet_handler_direct_apply_denied");
        }

        var source = battle.Participants.FirstOrDefault(
            value => value.ParticipantId == request.SourceParticipantId);
        var target = battle.Participants.FirstOrDefault(
            value => value.ParticipantId == request.TargetParticipantId);
        if (source is null || FailureInjection.Point == StatusFailurePoint.InvalidSource)
        {
            return ApplicationFailure(request, StatusResultCode.ParticipantNotFound, "status.source_not_found");
        }

        if (target is null || FailureInjection.Point == StatusFailurePoint.InvalidTarget)
        {
            return ApplicationFailure(request, StatusResultCode.ParticipantNotFound, "status.target_not_found");
        }

        if (!target.IsAlive || FailureInjection.Point == StatusFailurePoint.TargetDead)
        {
            return ApplicationFailure(request, StatusResultCode.ParticipantDead, "status.target_dead");
        }

        return null;
    }

    private StatusRemovalResult? ValidateRemoval(BattleInstance battle, StatusRemovalRequest request)
    {
        if (request.BattleInstanceId != battle.BattleInstanceId ||
            FailureInjection.Point == StatusFailurePoint.WrongBattle)
        {
            return RemovalFailure(request, StatusResultCode.BattleNotFound, "status.battle_mismatch");
        }

        if (battle.State != BattleState.Active && request.RemovalReason != StatusRemovalReason.BattleCompleted)
        {
            return RemovalFailure(request, StatusResultCode.InvalidPhase, "status.battle_not_active");
        }

        if (request.RoundNumber != battle.CurrentRoundNumber ||
            FailureInjection.Point == StatusFailurePoint.WrongRound)
        {
            return RemovalFailure(request, StatusResultCode.InvalidRound, "status.invalid_round");
        }

        if (!battle.Participants.Any(value => value.ParticipantId == request.SourceParticipantId) &&
            request.Source is not (StatusRequestSource.BattleRuntime or StatusRequestSource.Recovery))
        {
            return RemovalFailure(request, StatusResultCode.ParticipantNotFound, "status.source_not_found");
        }

        if (!battle.Participants.Any(value => value.ParticipantId == request.TargetParticipantId))
        {
            return RemovalFailure(request, StatusResultCode.ParticipantNotFound, "status.target_not_found");
        }

        if (request.StatusInstanceId is null && request.StatusDefinitionId is null)
        {
            return RemovalFailure(request, StatusResultCode.StatusNotFound, "status.removal_target_missing");
        }

        return null;
    }

    private static StatusRequestSource ResolveCatalogSource(
        BattleInstance battle,
        StatusRequestSource requestSource) =>
        battle.BattleType == BattleType.InternalTest ||
        requestSource is StatusRequestSource.TrustedInternalTest or StatusRequestSource.AdministrativeTest
            ? StatusRequestSource.TrustedInternalTest
            : requestSource;

    private static string ApplicationPayloadHash(StatusApplicationRequest request) =>
        BattleRuntimeHash.PersistenceKey(string.Join(
            '\u001f',
            request.StatusApplicationId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceActionId,
            request.SourceSkillExecutionId,
            request.SourceEffectExecutionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            request.StatusDefinitionId,
            request.ExpectedBattleVersion,
            request.ExpectedTargetVersion,
            request.Source,
            request.CorrelationId));

    private static string RemovalPayloadHash(StatusRemovalRequest request) =>
        BattleRuntimeHash.PersistenceKey(string.Join(
            '\u001f',
            request.StatusRemovalId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.SourceActionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            request.StatusInstanceId,
            request.StatusDefinitionId,
            request.RemovalReason,
            request.ExpectedTargetVersion,
            request.Source,
            request.CorrelationId));

    private StatusApplicationResult ApplicationFailure(
        StatusApplicationRequest request,
        StatusResultCode code,
        string failureCode,
        long targetVersion = 0,
        StatusStackDecision? stack = null,
        StatusDurationDecision? duration = null,
        StatusApplicationPlan? plan = null,
        StatusRecoveryState recoveryState = StatusRecoveryState.NotRequired)
    {
        Publish(
            recoveryState == StatusRecoveryState.RecoveryRequired
                ? StatusEventKind.StatusRecoveryRequired
                : StatusEventKind.StatusApplicationRejected,
            request.BattleInstanceId,
            request.RoundNumber,
            request.StatusApplicationId,
            null,
            plan?.StatusInstanceId,
            request.StatusDefinitionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            failureCode,
            request.CorrelationId);
        return new StatusApplicationResult(
            request.StatusApplicationId,
            plan?.StatusApplicationPlanId,
            plan?.StatusInstanceId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.TargetParticipantId,
            request.StatusDefinitionId,
            code,
            failureCode,
            false,
            plan?.Operation ?? stack?.Operation ?? StatusApplicationOperation.EvidenceBlocked,
            plan?.StackBefore ?? stack?.StackBefore ?? 0,
            plan?.StackAfter ?? stack?.StackAfter ?? 0,
            plan?.DurationBefore ?? duration?.DurationBefore,
            plan?.DurationAfter ?? duration?.DurationAfter,
            targetVersion,
            targetVersion,
            plan?.ExpectedStatusVersion ?? 0,
            plan?.ExpectedStatusVersion ?? 0,
            recoveryState,
            [],
            _clock.UtcNow);
    }

    private StatusRemovalResult RemovalFailure(
        StatusRemovalRequest request,
        StatusResultCode code,
        string failureCode,
        long targetVersion = 0,
        StatusInstance? status = null,
        StatusRemovalPlan? plan = null,
        StatusRecoveryState recoveryState = StatusRecoveryState.NotRequired) =>
        new(
            request.StatusRemovalId,
            plan?.StatusRemovalPlanId,
            status?.StatusInstanceId ?? plan?.StatusInstanceId,
            request.BattleInstanceId,
            request.RoundNumber,
            request.TargetParticipantId,
            status?.StatusDefinitionId ?? request.StatusDefinitionId,
            code,
            failureCode,
            false,
            request.RemovalReason,
            targetVersion,
            targetVersion,
            status?.RuntimeVersion ?? plan?.ExpectedStatusVersion ?? 0,
            status?.RuntimeVersion ?? plan?.ExpectedStatusVersion ?? 0,
            recoveryState,
            [],
            _clock.UtcNow);

    private void PublishRecoveryRequired(StatusApplicationRequest request, string failureCode) =>
        Publish(
            StatusEventKind.StatusRecoveryRequired,
            request.BattleInstanceId,
            request.RoundNumber,
            request.StatusApplicationId,
            null,
            null,
            request.StatusDefinitionId,
            request.SourceParticipantId,
            request.TargetParticipantId,
            failureCode,
            request.CorrelationId);

    private void Publish(
        StatusEventKind kind,
        Guid battleId,
        int round,
        Guid? applicationId,
        Guid? removalId,
        Guid? instanceId,
        int? definitionId,
        Guid? sourceId,
        Guid? targetId,
        string stateOrFailure,
        string correlationId)
    {
        _events.Publish(new StatusEvent(
            Guid.NewGuid(),
            kind,
            battleId,
            round,
            applicationId,
            removalId,
            null,
            null,
            instanceId,
            definitionId,
            sourceId,
            targetId,
            stateOrFailure,
            kind is StatusEventKind.StatusApplicationRejected or StatusEventKind.StatusRecoveryRequired
                ? stateOrFailure
                : "",
            _clock.UtcNow,
            correlationId));
    }

    private void AppendApplicationAudit(
        StatusApplicationRecord record,
        StatusApplicationResult result,
        StatusDefinition definition)
    {
        if (FailureInjection.Point == StatusFailurePoint.Audit)
        {
            return;
        }

        _audit.Append(new StatusAuditRecord(
            Guid.NewGuid(),
            record.Request.StatusApplicationId,
            null,
            null,
            null,
            result.StatusInstanceId,
            record.Request.StatusDefinitionId,
            record.Request.BattleInstanceId,
            record.Request.RoundNumber,
            null,
            null,
            null,
            BattleRuntimeHash.SafeId(record.Request.IdempotencyKey),
            record.Request.CorrelationId,
            record.Request.SourceParticipantId,
            record.Request.TargetParticipantId,
            record.Request.SourceActionId,
            record.Request.SourceSkillExecutionId,
            record.Request.SourceEffectExecutionId,
            record.Plan.Operation.ToString(),
            record.Plan.StackBefore,
            record.Plan.StackAfter,
            record.Plan.DurationBefore,
            record.Plan.DurationAfter,
            result.StatusVersionBefore,
            result.StatusVersionAfter,
            null,
            null,
            null,
            null,
            null,
            result.ResultCode,
            result.FailureCode,
            null,
            result.RecoveryState,
            StatusRuntimeCollections.Freeze(new[]
            {
                definition.PolicyStatus,
                definition.StackPolicy.PolicyStatus,
                definition.DurationPolicy.PolicyStatus
            }),
            record.Request.CreatedAtUtc,
            result.CompletedAtUtc));
    }

    private void AppendRemovalAudit(StatusRemovalRecord record, StatusRemovalResult result)
    {
        if (FailureInjection.Point == StatusFailurePoint.Audit)
        {
            return;
        }

        _audit.Append(new StatusAuditRecord(
            Guid.NewGuid(),
            null,
            record.Request.StatusRemovalId,
            null,
            null,
            result.StatusInstanceId,
            result.StatusDefinitionId,
            record.Request.BattleInstanceId,
            record.Request.RoundNumber,
            null,
            null,
            null,
            BattleRuntimeHash.SafeId(record.Request.IdempotencyKey),
            record.Request.CorrelationId,
            record.Request.SourceParticipantId,
            record.Request.TargetParticipantId,
            record.Request.SourceActionId,
            null,
            null,
            record.Plan.RemovalReason.ToString(),
            null,
            null,
            null,
            null,
            result.StatusVersionBefore,
            result.StatusVersionAfter,
            null,
            null,
            null,
            null,
            null,
            result.ResultCode,
            result.FailureCode,
            record.Plan.RemovalReason,
            result.RecoveryState,
            [],
            record.Request.CreatedAtUtc,
            result.CompletedAtUtc));
    }
}
