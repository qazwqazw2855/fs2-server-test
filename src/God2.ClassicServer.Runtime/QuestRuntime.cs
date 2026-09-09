using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Common;

namespace God2.ClassicServer.Runtime;

public sealed class InMemoryQuestDefinitionRepository : IQuestDefinitionRepository
{
    private readonly IReadOnlyList<QuestDefinitionRecord> _records;

    public InMemoryQuestDefinitionRepository(IEnumerable<QuestDefinitionRecord> records)
    {
        _records = QuestRuntimeCollections.Freeze(records);
    }

    public Task<IReadOnlyList<QuestDefinitionRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records);
    }
}

public sealed class QuestDefinitionMapper : IQuestDefinitionMapper
{
    public OperationResult<QuestDefinition> Map(QuestDefinitionRecord record)
    {
        var objectives = QuestRuntimeCollections.Freeze(
            record.ObjectiveDefinitions
                .OrderBy(objective => objective.ObjectiveIndex)
                .ThenBy(objective => objective.ObjectiveDefinitionId, StringComparer.Ordinal));
        var prerequisites = QuestRuntimeCollections.Freeze(
            record.Prerequisites
                .OrderBy(prerequisite => prerequisite.PrerequisiteDefinitionId, StringComparer.Ordinal));
        var bindings = QuestRuntimeCollections.Freeze(
            record.NpcBindings
                .OrderBy(binding => binding.BindingType)
                .ThenBy(binding => binding.NpcTemplateId));
        var reward = record.RewardDefinition ?? new QuestRewardDefinition(
            $"quest:{record.QuestDefinitionId}:no-reward",
            [],
            [],
            null,
            null,
            [],
            record.PolicyStatus is QuestContentStatus.TestOnly
                ? QuestContentStatus.TestOnly
                : QuestContentStatus.EvidenceBlocked,
            """{"classification":"ExplicitNoRewardOrEvidenceBlocked"}""");

        return OperationResult<QuestDefinition>.Success(new QuestDefinition(
            record.QuestDefinitionId,
            record.ExternalQuestId.Trim(),
            record.RawName.Trim(),
            record.RawDescription.Trim(),
            record.QuestCategory,
            record.QuestType,
            record.AcceptPolicy.Trim(),
            record.CompletionPolicy,
            record.RepeatPolicy,
            prerequisites,
            objectives,
            reward with
            {
                ItemRewards = QuestRuntimeCollections.Freeze(
                    reward.ItemRewards.OrderBy(item => item.ItemTemplateId)),
                CurrencyRewards = QuestRuntimeCollections.Freeze(
                    reward.CurrencyRewards.OrderBy(currency => currency.CurrencyType, StringComparer.Ordinal)),
                OtherRewardCandidates = QuestRuntimeCollections.Freeze(
                    reward.OtherRewardCandidates.OrderBy(value => value, StringComparer.Ordinal))
            },
            bindings,
            record.Enabled,
            record.ContentVersion.Trim(),
            record.PolicyStatus,
            record.ProtocolStatus,
            record.RawMetadata));
    }
}

public sealed class QuestDefinitionValidator : IQuestDefinitionValidator
{
    private static readonly HashSet<QuestObjectiveType> SupportedObjectiveTypes =
    [
        QuestObjectiveType.KillMonster,
        QuestObjectiveType.OwnItem,
        QuestObjectiveType.InteractNpc,
        QuestObjectiveType.VisitMap,
        QuestObjectiveType.UsePortal,
        QuestObjectiveType.CompleteBattle,
        QuestObjectiveType.CraftItem
    ];

    public OperationResult Validate(QuestDefinition definition)
    {
        if (definition.QuestDefinitionId <= 0 || string.IsNullOrWhiteSpace(definition.ExternalQuestId))
        {
            return Failure("quest.definition.invalid_id", "QuestDefinitionId and ExternalQuestId must be valid.");
        }

        if (definition.ObjectiveDefinitions.Count == 0)
        {
            return Failure("quest.definition.missing_objective", "Quest definitions require at least one objective.");
        }

        if (definition.ObjectiveDefinitions.Select(value => value.ObjectiveDefinitionId).Distinct(StringComparer.Ordinal).Count() !=
            definition.ObjectiveDefinitions.Count)
        {
            return Failure("quest.definition.duplicate_objective_id", "ObjectiveDefinitionId must be unique.");
        }

        if (definition.ObjectiveDefinitions.Select(value => value.ObjectiveIndex).Distinct().Count() !=
            definition.ObjectiveDefinitions.Count)
        {
            return Failure("quest.definition.duplicate_objective_index", "ObjectiveIndex must be unique.");
        }

        if (definition.ObjectiveDefinitions.Any(value => value.RequiredCount < 0))
        {
            return Failure("quest.definition.negative_required_count", "Objective RequiredCount cannot be negative.");
        }

        if (definition.ObjectiveDefinitions.Any(value => value.QuestDefinitionId != definition.QuestDefinitionId))
        {
            return Failure("quest.definition.objective_owner_mismatch", "Objective quest identity does not match its definition.");
        }

        if (definition.Prerequisites.Any(value => value.RequiredQuestDefinitionId == definition.QuestDefinitionId))
        {
            return Failure("quest.definition.self_prerequisite", "Quest prerequisites cannot reference the same quest.");
        }

        if (definition.NpcBindings.Any(binding =>
                binding.QuestDefinitionId != definition.QuestDefinitionId ||
                binding.NpcTemplateId <= 0 ||
                binding.BindingType == QuestNpcBindingType.Unknown))
        {
            return Failure("quest.definition.invalid_npc_binding", "Quest NPC bindings must be explicit and valid.");
        }

        if (definition.RewardDefinition.ItemRewards.Any(value => value.ItemTemplateId <= 0 || value.Quantity <= 0) ||
            definition.RewardDefinition.CurrencyRewards.Any(value => string.IsNullOrWhiteSpace(value.CurrencyType) || value.Amount <= 0))
        {
            return Failure("quest.definition.invalid_reward", "Quest item and currency rewards must be positive and explicit.");
        }

        if (definition.PolicyStatus is QuestContentStatus.Verified or QuestContentStatus.ContentBacked)
        {
            if (definition.QuestType == QuestType.Unknown)
            {
                return Failure("quest.definition.unknown_type_verified", "Unknown quest types cannot be production ready.");
            }

            if (definition.ObjectiveDefinitions.Any(value =>
                    !SupportedObjectiveTypes.Contains(value.ObjectiveType) ||
                    value.PolicyStatus is QuestContentStatus.TestOnly or QuestContentStatus.EvidenceBlocked))
            {
                return Failure("quest.definition.unsupported_production_objective", "Production quests require supported content-backed objectives.");
            }

            if (definition.RewardDefinition.PolicyStatus is QuestContentStatus.TestOnly or QuestContentStatus.EvidenceBlocked)
            {
                return Failure("quest.definition.unsupported_production_reward", "Production quests cannot use TestOnly or evidence-blocked rewards.");
            }
        }

        if (definition.PolicyStatus == QuestContentStatus.TestOnly &&
            definition.ObjectiveDefinitions.Any(value => value.PolicyStatus != QuestContentStatus.TestOnly))
        {
            return Failure("quest.definition.test_policy_mismatch", "TestOnly quest objectives must remain TestOnly.");
        }

        if (!RawMetadataIsSafe(definition.RawMetadata))
        {
            return Failure("quest.definition.raw_metadata_secret", "RawMetadata contains a secret-like key.");
        }

        return OperationResult.Success;
    }

    public OperationResult ValidateCatalog(IReadOnlyList<QuestDefinition> definitions)
    {
        if (definitions.Select(value => value.QuestDefinitionId).Distinct().Count() != definitions.Count)
        {
            return Failure("quest.catalog.duplicate_definition_id", "QuestDefinitionId must be unique.");
        }

        foreach (var definition in definitions)
        {
            var validation = Validate(definition);
            if (!validation.Succeeded)
            {
                return validation;
            }
        }

        var definitionsById = definitions.ToDictionary(value => value.QuestDefinitionId);
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (var definition in definitions)
        {
            if (HasCycle(definition.QuestDefinitionId, definitionsById, visiting, visited))
            {
                return Failure("quest.catalog.circular_prerequisite", "Quest prerequisite graph contains a cycle.");
            }
        }

        return OperationResult.Success;
    }

    private static bool HasCycle(
        int questDefinitionId,
        IReadOnlyDictionary<int, QuestDefinition> definitions,
        ISet<int> visiting,
        ISet<int> visited)
    {
        if (visited.Contains(questDefinitionId))
        {
            return false;
        }

        if (!visiting.Add(questDefinitionId))
        {
            return true;
        }

        if (definitions.TryGetValue(questDefinitionId, out var definition))
        {
            foreach (var prerequisite in definition.Prerequisites.Where(value => value.RequiredQuestDefinitionId is not null))
            {
                if (HasCycle(prerequisite.RequiredQuestDefinitionId!.Value, definitions, visiting, visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(questDefinitionId);
        visited.Add(questDefinitionId);
        return false;
    }

    private static bool RawMetadataIsSafe(string rawMetadata)
    {
        var normalized = rawMetadata.ToLowerInvariant();
        return !normalized.Contains("\"password\"", StringComparison.Ordinal) &&
               !normalized.Contains("\"token\"", StringComparison.Ordinal) &&
               !normalized.Contains("\"connectionstring\"", StringComparison.Ordinal) &&
               !normalized.Contains("\"secret\"", StringComparison.Ordinal);
    }

    private static OperationResult Failure(string code, string message) =>
        OperationResult.Failure(code, message, "QuestDefinition");
}

public sealed class ImmutableQuestDefinitionCatalog : IQuestDefinitionCatalog
{
    private readonly FrozenDictionary<int, QuestDefinition> _definitions;

    public ImmutableQuestDefinitionCatalog(
        IEnumerable<QuestDefinition> definitions,
        IQuestDefinitionValidator validator)
    {
        var materialized = definitions
            .OrderBy(value => value.QuestDefinitionId)
            .ToArray();
        var validation = validator.ValidateCatalog(materialized);
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException($"{validation.Error.Code}: {validation.Error.Message}");
        }

        _definitions = materialized.ToFrozenDictionary(value => value.QuestDefinitionId);
        AllSnapshot = QuestRuntimeCollections.Freeze(materialized);
        Snapshot = QuestRuntimeCollections.Freeze(materialized.Where(IsProductionReady));
    }

    public IReadOnlyList<QuestDefinition> Snapshot { get; }

    public IReadOnlyList<QuestDefinition> AllSnapshot { get; }

    public OperationResult<QuestDefinition> Get(int questDefinitionId, QuestRequestSource source)
    {
        if (!_definitions.TryGetValue(questDefinitionId, out var definition))
        {
            return OperationResult<QuestDefinition>.Failure(
                "quest.not_found",
                "Quest definition was not found.",
                questDefinitionId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!definition.Enabled)
        {
            return OperationResult<QuestDefinition>.Failure(
                "quest.disabled",
                "Quest definition is disabled.",
                definition.ExternalQuestId);
        }

        if (definition.PolicyStatus == QuestContentStatus.TestOnly &&
            source != QuestRequestSource.TestOnly)
        {
            return OperationResult<QuestDefinition>.Failure(
                "quest.test_only_isolated",
                "TestOnly quest definitions cannot enter a production or general runtime path.",
                definition.ExternalQuestId);
        }

        if (source == QuestRequestSource.ProductionClient && !IsProductionReady(definition))
        {
            return OperationResult<QuestDefinition>.Failure(
                "quest.evidence_blocked",
                "Production quest content is not verified.",
                definition.ExternalQuestId);
        }

        return OperationResult<QuestDefinition>.Success(definition);
    }

    private static bool IsProductionReady(QuestDefinition definition) =>
        definition.Enabled &&
        definition.PolicyStatus is QuestContentStatus.Verified or QuestContentStatus.ContentBacked;
}

public sealed class InMemoryQuestSessionValidator : IQuestSessionValidator
{
    private readonly ConcurrentDictionary<string, QuestSessionSnapshot> _sessions = new(StringComparer.Ordinal);

    public void Register(QuestSessionSnapshot session) => _sessions[session.SessionId] = session;

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public OperationResult<QuestSessionSnapshot> Validate(QuestIntent intent)
    {
        if (!_sessions.TryGetValue(intent.SessionId, out var session) || !session.Authenticated || !session.CharacterActive)
        {
            return OperationResult<QuestSessionSnapshot>.Failure(
                "quest.invalid_session",
                "Quest intent session is not active.",
                QuestRuntimeSecurity.SafeId(intent.SessionId));
        }

        if (session.CharacterId != intent.CharacterId ||
            session.PlayerRuntimeEntityId != intent.PlayerRuntimeEntityId)
        {
            return OperationResult<QuestSessionSnapshot>.Failure(
                "quest.ownership_mismatch",
                "Quest intent character ownership does not match the active session.",
                QuestRuntimeSecurity.SafeId(intent.SessionId));
        }

        if (session.CharacterRuntimeVersion != intent.ExpectedCharacterRuntimeVersion)
        {
            return OperationResult<QuestSessionSnapshot>.Failure(
                "quest.character_version_conflict",
                "Quest intent character runtime version is stale.",
                QuestRuntimeSecurity.SafeId(intent.SessionId));
        }

        return OperationResult<QuestSessionSnapshot>.Success(session);
    }
}

public sealed class QuestBindingResolver : IQuestBindingResolver
{
    public OperationResult<QuestNpcBinding> Resolve(QuestBindingContext context)
    {
        if (!context.InteractionValidated ||
            context.NpcRuntimeEntityId is null ||
            context.NpcRuntimeEntityId <= 0 ||
            context.NpcTemplateId is null)
        {
            return Failure(context, "quest.binding.invalid_interaction", "NPC interaction authority was not validated.");
        }

        var binding = context.Definition.NpcBindings
            .Where(value => value.Enabled)
            .Where(value => value.BindingType == context.RequiredBindingType ||
                            value.BindingType == QuestNpcBindingType.AcceptAndTurnIn)
            .FirstOrDefault(value => value.NpcTemplateId == context.NpcTemplateId);
        if (binding is null)
        {
            return Failure(context, "quest.binding.invalid_npc", "NPC template is not bound to this quest operation.");
        }

        if (binding.MapIdCandidate is not null && binding.MapIdCandidate != context.CurrentMapId)
        {
            return Failure(context, "quest.binding.invalid_map", "NPC quest binding map does not match the current map.");
        }

        if (context.ProductionPath && binding.PolicyStatus == QuestContentStatus.TestOnly)
        {
            return Failure(context, "quest.binding.test_only_isolated", "TestOnly NPC binding cannot enter production.");
        }

        if (context.ProductionPath &&
            binding.PolicyStatus is not (QuestContentStatus.Verified or QuestContentStatus.ContentBacked))
        {
            return Failure(context, "quest.binding.evidence_blocked", "NPC quest binding evidence is insufficient.");
        }

        return OperationResult<QuestNpcBinding>.Success(binding);
    }

    private static OperationResult<QuestNpcBinding> Failure(
        QuestBindingContext context,
        string code,
        string message) =>
        OperationResult<QuestNpcBinding>.Failure(code, message, context.Definition.ExternalQuestId);
}

public sealed class QuestEligibilityPolicy : IQuestEligibilityPolicy
{
    public QuestEligibilityResult Evaluate(QuestEligibilityContext context)
    {
        if (!context.Session.Authenticated || !context.Session.CharacterActive)
        {
            return Reject(QuestResultCode.InvalidSession, "quest.invalid_session");
        }

        if (context.Session.CharacterId != context.Intent.CharacterId ||
            context.Session.PlayerRuntimeEntityId != context.Intent.PlayerRuntimeEntityId)
        {
            return Reject(QuestResultCode.OwnershipMismatch, "quest.ownership_mismatch");
        }

        if (!context.Definition.Enabled)
        {
            return Reject(QuestResultCode.QuestDisabled, "quest.disabled");
        }

        if (context.ProductionPath &&
            context.Definition.PolicyStatus is not (QuestContentStatus.Verified or QuestContentStatus.ContentBacked))
        {
            return Reject(QuestResultCode.QuestEvidenceBlocked, "quest.evidence_blocked");
        }

        var instances = context.ExistingInstances
            .Where(value => value.QuestDefinitionId == context.Definition.QuestDefinitionId)
            .ToArray();
        if (instances.Any(value => value.State is
                QuestInstanceState.Created or
                QuestInstanceState.Accepting or
                QuestInstanceState.Active or
                QuestInstanceState.ReadyToComplete or
                QuestInstanceState.Completing or
                QuestInstanceState.RewardPending or
                QuestInstanceState.RecoveryRequired))
        {
            return Reject(QuestResultCode.QuestAlreadyActive, "quest.already_active");
        }

        if (instances.Any(value => value.State == QuestInstanceState.Completed) &&
            context.Definition.RepeatPolicy.Type == QuestRepeatPolicyType.Once)
        {
            return Reject(QuestResultCode.QuestAlreadyCompleted, "quest.already_completed");
        }

        if (context.Definition.RepeatPolicy.Type is
                QuestRepeatPolicyType.Daily or
                QuestRepeatPolicyType.Weekly or
                QuestRepeatPolicyType.EventWindow or
                QuestRepeatPolicyType.Unknown)
        {
            return Reject(QuestResultCode.EligibilityEvidenceBlocked, "quest.repeat_policy_evidence_blocked");
        }

        foreach (var prerequisite in context.Definition.Prerequisites)
        {
            var met = prerequisite.Type switch
            {
                QuestPrerequisiteType.QuestCompleted when prerequisite.RequiredQuestDefinitionId is not null =>
                    context.CompletedQuestDefinitionIds.Contains(prerequisite.RequiredQuestDefinitionId.Value),
                QuestPrerequisiteType.QuestNotCompleted when prerequisite.RequiredQuestDefinitionId is not null =>
                    !context.CompletedQuestDefinitionIds.Contains(prerequisite.RequiredQuestDefinitionId.Value),
                QuestPrerequisiteType.ItemOwned when prerequisite.ItemTemplateId is not null =>
                    context.OwnedItemQuantities.GetValueOrDefault(prerequisite.ItemTemplateId.Value) >= prerequisite.RequiredValue,
                QuestPrerequisiteType.QuestActive when prerequisite.RequiredQuestDefinitionId is not null =>
                    context.ExistingInstances.Any(value =>
                        value.QuestDefinitionId == prerequisite.RequiredQuestDefinitionId &&
                        value.State is QuestInstanceState.Active or QuestInstanceState.ReadyToComplete),
                _ => false
            };
            if (!met)
            {
                return prerequisite.Type is
                    QuestPrerequisiteType.CharacterLevel or
                    QuestPrerequisiteType.CharacterClass or
                    QuestPrerequisiteType.CurrencyOwned or
                    QuestPrerequisiteType.MapReached or
                    QuestPrerequisiteType.Scripted or
                    QuestPrerequisiteType.Unknown
                    ? Reject(QuestResultCode.EligibilityEvidenceBlocked, "quest.prerequisite_evidence_blocked")
                    : Reject(QuestResultCode.PrerequisiteNotMet, "quest.prerequisite_not_met");
            }
        }

        return new QuestEligibilityResult(QuestResultCode.Success, "");
    }

    private static QuestEligibilityResult Reject(QuestResultCode code, string failureCode) =>
        new(code, failureCode);
}

public sealed class NullQuestInventorySnapshotProvider : IQuestInventorySnapshotProvider
{
    public Task<long> GetOwnedQuantityAsync(
        long characterId,
        int itemTemplateId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}

public sealed class InventoryCoordinatorQuestSnapshotProvider : IQuestInventorySnapshotProvider
{
    private readonly IInventoryTransactionCoordinator _inventory;

    public InventoryCoordinatorQuestSnapshotProvider(IInventoryTransactionCoordinator inventory)
    {
        _inventory = inventory;
    }

    public Task<long> GetOwnedQuantityAsync(
        long characterId,
        int itemTemplateId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _inventory.CaptureInspector(new InventoryInspectorQuery(CharacterId: characterId));
        var quantity = snapshot.Items
            .Where(value => value.CharacterId == characterId && value.ItemTemplateId == itemTemplateId)
            .Sum(value => (long)value.Quantity);
        return Task.FromResult(quantity);
    }
}

public sealed class InMemoryQuestInstanceAuthority : IQuestInstanceAuthority
{
    private readonly ConcurrentDictionary<Guid, QuestInstance> _instances = [];
    private readonly object _gate = new();

    public IReadOnlyList<QuestInstance> Snapshot =>
        QuestRuntimeCollections.Freeze(
            _instances.Values
                .OrderBy(value => value.CharacterId)
                .ThenBy(value => value.QuestDefinitionId)
                .ThenBy(value => value.QuestInstanceId));

    public QuestInstance? Get(Guid questInstanceId) =>
        _instances.GetValueOrDefault(questInstanceId);

    public IReadOnlyList<QuestInstance> GetCharacter(long characterId) =>
        QuestRuntimeCollections.Freeze(
            _instances.Values
                .Where(value => value.CharacterId == characterId)
                .OrderBy(value => value.QuestDefinitionId)
                .ThenBy(value => value.QuestInstanceId));

    public OperationResult<QuestInstance> Add(QuestInstance instance)
    {
        lock (_gate)
        {
            if (_instances.Values.Any(value =>
                    value.CharacterId == instance.CharacterId &&
                    value.QuestDefinitionId == instance.QuestDefinitionId &&
                    IsOpen(value.State)))
            {
                return Failure(instance, "quest.authority.duplicate_active", "A character cannot have duplicate active quest instances.");
            }

            if (!_instances.TryAdd(instance.QuestInstanceId, instance))
            {
                return Failure(instance, "quest.authority.instance_exists", "Quest instance identity already exists.");
            }

            return OperationResult<QuestInstance>.Success(instance);
        }
    }

    public OperationResult<QuestInstance> Replace(QuestInstance instance, long expectedVersion)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(instance.QuestInstanceId, out var current))
            {
                return Failure(instance, "quest.authority.instance_missing", "Quest instance was not found.");
            }

            if (current.QuestVersion != expectedVersion || instance.QuestVersion <= current.QuestVersion)
            {
                return Failure(instance, "quest.authority.version_conflict", "Quest version is stale or did not advance.");
            }

            if (current.State == QuestInstanceState.Completed && instance.State != QuestInstanceState.Completed)
            {
                return Failure(instance, "quest.authority.completed_regression", "Completed quest cannot return to an earlier state.");
            }

            if (current.State == QuestInstanceState.Abandoned && instance.State == QuestInstanceState.Active)
            {
                return Failure(instance, "quest.authority.abandoned_regression", "Abandoned quest requires a new instance.");
            }

            _instances[instance.QuestInstanceId] = instance;
            return OperationResult<QuestInstance>.Success(instance);
        }
    }

    public OperationResult Reload(long characterId, IEnumerable<QuestInstance> instances)
    {
        lock (_gate)
        {
            foreach (var key in _instances.Values
                         .Where(value => value.CharacterId == characterId)
                         .Select(value => value.QuestInstanceId)
                         .ToArray())
            {
                _instances.TryRemove(key, out _);
            }

            foreach (var instance in instances.OrderBy(value => value.QuestInstanceId))
            {
                if (instance.CharacterId != characterId)
                {
                    return OperationResult.Failure(
                        "quest.authority.reload_ownership_mismatch",
                        "Reloaded quest instance belongs to another character.",
                        instance.QuestInstanceId.ToString());
                }

                _instances[instance.QuestInstanceId] = instance;
            }

            return OperationResult.Success;
        }
    }

    private static bool IsOpen(QuestInstanceState state) =>
        state is
            QuestInstanceState.Created or
            QuestInstanceState.Accepting or
            QuestInstanceState.Active or
            QuestInstanceState.ReadyToComplete or
            QuestInstanceState.Completing or
            QuestInstanceState.RewardPending or
            QuestInstanceState.RecoveryRequired;

    private static OperationResult<QuestInstance> Failure(
        QuestInstance instance,
        string code,
        string message) =>
        OperationResult<QuestInstance>.Failure(code, message, instance.QuestInstanceId.ToString());
}

public sealed class InMemoryQuestRuntimeStore : IQuestRuntimeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, QuestInstance> _instances = [];
    private readonly Dictionary<string, (string PayloadHash, QuestOperationResult Result)> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string PayloadHash, QuestProgressResult Result)> _progress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string PayloadHash, QuestRewardResult Result)> _rewards = new(StringComparer.Ordinal);
    private readonly List<QuestProgressMutationPlan> _progressPlans = [];
    private readonly List<(QuestRewardPlan Plan, QuestRewardResult Result)> _rewardRecords = [];

    public InMemoryQuestRuntimeStore(QuestFailureInjection? failureInjection = null)
    {
        FailureInjection = failureInjection ?? new QuestFailureInjection();
    }

    public QuestFailureInjection FailureInjection { get; }

    public IReadOnlyList<QuestProgressMutationPlan> ProgressPlans
    {
        get
        {
            lock (_gate)
            {
                return QuestRuntimeCollections.Freeze(_progressPlans);
            }
        }
    }

    public IReadOnlyList<(QuestRewardPlan Plan, QuestRewardResult Result)> RewardRecords
    {
        get
        {
            lock (_gate)
            {
                return QuestRuntimeCollections.Freeze(_rewardRecords);
            }
        }
    }

    public Task<QuestReplayLookup> FindOperationAsync(
        string operation,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_operations.TryGetValue(OperationKey(operation, idempotencyHash), out var value)
                ? new QuestReplayLookup(true, value.PayloadHash == payloadHash, value.Result)
                : new QuestReplayLookup(false, true, null));
        }
    }

    public Task<OperationResult> CommitAcceptanceAsync(
        QuestAcceptancePlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.AcceptancePersistence)
            {
                return Task.FromResult(Failure("quest.persistence.acceptance_failed"));
            }

            if (_instances.Values.Any(value =>
                    value.CharacterId == instance.CharacterId &&
                    value.QuestDefinitionId == instance.QuestDefinitionId &&
                    value.State is QuestInstanceState.Active or QuestInstanceState.ReadyToComplete or QuestInstanceState.RecoveryRequired))
            {
                return Task.FromResult(Failure("quest.persistence.duplicate_active"));
            }

            _instances[instance.QuestInstanceId] = instance;
            _operations[OperationKey("accept", idempotencyHash)] = (payloadHash, result);
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<IReadOnlyList<QuestInstance>> LoadCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<QuestInstance>>(QuestRuntimeCollections.Freeze(
                _instances.Values
                    .Where(value => value.CharacterId == characterId)
                    .OrderBy(value => value.QuestDefinitionId)
                    .ThenBy(value => value.QuestInstanceId)));
        }
    }

    public Task<QuestInstance?> LoadInstanceAsync(
        Guid questInstanceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_instances.GetValueOrDefault(questInstanceId));
        }
    }

    public Task<QuestProgressReplayLookup> FindProgressAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_progress.TryGetValue(idempotencyHash, out var value)
                ? new QuestProgressReplayLookup(true, value.PayloadHash == payloadHash, value.Result)
                : new QuestProgressReplayLookup(false, true, null));
        }
    }

    public Task<OperationResult> CommitProgressAsync(
        QuestProgressMutationPlan plan,
        QuestInstance instance,
        QuestProgressResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.ProgressPersistence)
            {
                return Task.FromResult(Failure("quest.persistence.progress_failed"));
            }

            if (!_instances.TryGetValue(plan.QuestInstanceId, out var current) ||
                current.QuestVersion != plan.ExpectedQuestVersion)
            {
                return Task.FromResult(Failure("quest.persistence.quest_version_conflict"));
            }

            var currentObjective = current.ObjectiveStates
                .FirstOrDefault(value => value.ObjectiveDefinitionId == plan.ObjectiveDefinitionId);
            if (currentObjective is null || currentObjective.ObjectiveVersion != plan.ExpectedObjectiveVersion)
            {
                return Task.FromResult(Failure("quest.persistence.objective_version_conflict"));
            }

            _instances[instance.QuestInstanceId] = instance;
            _progress[idempotencyHash] = (payloadHash, result);
            _progressPlans.Add(plan);
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<OperationResult> CommitAbandonmentAsync(
        QuestAbandonPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.AbandonPersistence)
            {
                return Task.FromResult(Failure("quest.persistence.abandon_failed"));
            }

            if (!_instances.TryGetValue(plan.QuestInstanceId, out var current) ||
                current.QuestVersion != plan.ExpectedQuestVersion)
            {
                return Task.FromResult(Failure("quest.persistence.quest_version_conflict"));
            }

            _instances[instance.QuestInstanceId] = instance;
            _operations[OperationKey("abandon", idempotencyHash)] = (payloadHash, result);
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<QuestRewardReplayLookup> FindRewardAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_rewards.TryGetValue(idempotencyHash, out var value)
                ? new QuestRewardReplayLookup(true, value.PayloadHash == payloadHash, value.Result)
                : new QuestRewardReplayLookup(false, true, null));
        }
    }

    public Task<OperationResult> CommitRewardAsync(
        QuestRewardPlan plan,
        QuestRewardResult result,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.RewardTransaction)
            {
                return Task.FromResult(Failure("quest.persistence.reward_failed"));
            }

            _rewards[plan.IdempotencyKeyHash] = (payloadHash, result);
            _rewardRecords.Add((plan, result));
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<OperationResult> CommitCompletionAsync(
        QuestCompletionPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.CompletionPersistence)
            {
                return Task.FromResult(Failure("quest.persistence.completion_failed"));
            }

            if (!_instances.TryGetValue(plan.QuestInstanceId, out var current) ||
                current.QuestVersion != plan.ExpectedQuestVersion)
            {
                return Task.FromResult(Failure("quest.persistence.quest_version_conflict"));
            }

            _instances[instance.QuestInstanceId] = instance;
            _operations[OperationKey("turnin", idempotencyHash)] = (payloadHash, result);
            return Task.FromResult(OperationResult.Success);
        }
    }

    public Task<OperationResult> SaveRecoveryAsync(
        QuestInstance instance,
        string failureCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailureInjection.Point == QuestFailurePoint.Recovery)
            {
                return Task.FromResult(Failure("quest.persistence.recovery_failed"));
            }

            _instances[instance.QuestInstanceId] = instance;
            return Task.FromResult(OperationResult.Success);
        }
    }

    public void Seed(QuestInstance instance)
    {
        lock (_gate)
        {
            _instances[instance.QuestInstanceId] = instance;
        }
    }

    private static string OperationKey(string operation, string idempotencyHash) =>
        $"{operation}:{idempotencyHash}";

    private static OperationResult Failure(string code) =>
        OperationResult.Failure(code, "Injected or optimistic quest persistence failure.", "QuestRuntimeStore");
}

public abstract class QuestObjectiveHandlerBase : IQuestObjectiveHandler
{
    public abstract QuestObjectiveType ObjectiveType { get; }

    public OperationResult<QuestProgressMutationPlan?> CreatePlan(
        QuestDefinition definition,
        QuestInstance instance,
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (objective.ObjectiveType != ObjectiveType)
        {
            return OperationResult<QuestProgressMutationPlan?>.Failure(
                "quest.objective.handler_mismatch",
                "Objective handler type does not match the objective definition.",
                objective.ObjectiveDefinitionId);
        }

        if (!semanticEvent.Committed ||
            instance.State is not (QuestInstanceState.Active or QuestInstanceState.ReadyToComplete))
        {
            return OperationResult<QuestProgressMutationPlan?>.Success(null);
        }

        if (state.State == QuestObjectiveStateCode.Completed &&
            !string.Equals(
                state.LastSemanticEventSafeId,
                QuestRuntimeSecurity.SafeId(semanticEvent.SemanticEventId.ToString("N")),
                StringComparison.Ordinal))
        {
            return OperationResult<QuestProgressMutationPlan?>.Success(null);
        }

        if (objective.PolicyStatus == QuestContentStatus.EvidenceBlocked)
        {
            return OperationResult<QuestProgressMutationPlan?>.Failure(
                "quest.objective.evidence_blocked",
                "Objective semantics are evidence blocked.",
                objective.ObjectiveDefinitionId);
        }

        var progress = CalculateProgress(objective, state, semanticEvent);
        if (!progress.Succeeded || progress.Value is null)
        {
            return progress.Succeeded
                ? OperationResult<QuestProgressMutationPlan?>.Success(null)
                : OperationResult<QuestProgressMutationPlan?>.Failure(
                    progress.Error.Code,
                    progress.Error.Message,
                    progress.Error.Source);
        }

        var after = progress.Value.Value;
        if (after < 0 || after > objective.RequiredCount)
        {
            return OperationResult<QuestProgressMutationPlan?>.Failure(
                "quest.progress.out_of_range",
                "Server-computed objective progress is outside its legal range.",
                objective.ObjectiveDefinitionId);
        }

        var objectiveAfter = after >= objective.RequiredCount
            ? QuestObjectiveStateCode.Completed
            : after > 0
                ? QuestObjectiveStateCode.InProgress
                : QuestObjectiveStateCode.Active;
        var plan = new QuestProgressMutationPlan(
            Guid.NewGuid(),
            semanticEvent.SemanticEventId,
            QuestRuntimeSecurity.Hash(JsonSerializer.Serialize(semanticEvent)),
            instance.QuestInstanceId,
            instance.QuestDefinitionId,
            objective.ObjectiveDefinitionId,
            objective.ObjectiveIndex,
            instance.CharacterId,
            state.CurrentProgress,
            checked(after - state.CurrentProgress),
            after,
            state.State,
            objectiveAfter,
            instance.State,
            instance.State,
            instance.QuestVersion,
            state.ObjectiveVersion,
            objective.PolicyStatus,
            semanticEvent.OccurredAtUtc,
            semanticEvent.CorrelationId);
        return OperationResult<QuestProgressMutationPlan?>.Success(plan);
    }

    protected abstract OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent);

    protected static OperationResult<long?> NoMatch() =>
        OperationResult<long?>.Success(null);

    protected static OperationResult<long?> Progress(long value) =>
        OperationResult<long?>.Success(value);
}

public sealed class KillMonsterQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.KillMonster;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType != QuestSemanticEventType.MonsterDefeated ||
            semanticEvent.DeathId is null ||
            semanticEvent.MonsterTemplateId != objective.TargetTemplateId)
        {
            return NoMatch();
        }

        return Progress(Math.Min(checked(state.CurrentProgress + 1), objective.RequiredCount));
    }
}

public sealed class OwnItemQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.OwnItem;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType is not (QuestSemanticEventType.InventoryCommitted or QuestSemanticEventType.ItemOwnershipChanged) ||
            semanticEvent.ItemTemplateId != objective.TargetTemplateId ||
            semanticEvent.QuantityAfter is null)
        {
            return NoMatch();
        }

        if (semanticEvent.QuantityAfter < 0)
        {
            return OperationResult<long?>.Failure(
                "quest.inventory_snapshot.invalid_quantity",
                "Inventory authority returned a negative quantity.",
                objective.ObjectiveDefinitionId);
        }

        return Progress(Math.Min(semanticEvent.QuantityAfter.Value, objective.RequiredCount));
    }
}

public sealed class InteractNpcQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.InteractNpc;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType != QuestSemanticEventType.NpcInteractionCompleted ||
            semanticEvent.WorldInteractionId is null ||
            semanticEvent.TargetTemplateId != objective.TargetTemplateId)
        {
            return NoMatch();
        }

        return Progress(Math.Min(checked(state.CurrentProgress + 1), objective.RequiredCount));
    }
}

public sealed class VisitMapQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.VisitMap;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType is not (QuestSemanticEventType.PortalTransitionCommitted or QuestSemanticEventType.MapEntered) ||
            semanticEvent.PortalTransitionId is null ||
            semanticEvent.TargetMapId != objective.TargetTemplateId)
        {
            return NoMatch();
        }

        return Progress(objective.RequiredCount);
    }
}

public sealed class UsePortalQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.UsePortal;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType != QuestSemanticEventType.PortalTransitionCommitted ||
            semanticEvent.PortalTransitionId is null ||
            semanticEvent.PortalTemplateId != objective.TargetTemplateId)
        {
            return NoMatch();
        }

        return Progress(Math.Min(checked(state.CurrentProgress + 1), objective.RequiredCount));
    }
}

public sealed class CompleteBattleQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.CompleteBattle;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType != QuestSemanticEventType.BattleCompleted ||
            semanticEvent.BattleInstanceId is null)
        {
            return NoMatch();
        }

        if (objective.TargetRuntimeType.Equals("WinBattle", StringComparison.Ordinal) &&
            semanticEvent.BattleWon != true)
        {
            return NoMatch();
        }

        if (objective.TargetRuntimeType.Equals("Encounter", StringComparison.Ordinal) &&
            !string.Equals(
                semanticEvent.EncounterDefinitionId,
                objective.TargetTemplateId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return NoMatch();
        }

        return Progress(Math.Min(checked(state.CurrentProgress + 1), objective.RequiredCount));
    }
}

public sealed class CraftItemQuestObjectiveHandler : QuestObjectiveHandlerBase
{
    public override QuestObjectiveType ObjectiveType => QuestObjectiveType.CraftItem;

    protected override OperationResult<long?> CalculateProgress(
        QuestObjectiveDefinition objective,
        QuestObjectiveState state,
        QuestSemanticEvent semanticEvent)
    {
        if (semanticEvent.EventType != QuestSemanticEventType.CraftingCommitted ||
            semanticEvent.InventoryTransactionId is null ||
            semanticEvent.ItemTemplateId != objective.TargetTemplateId ||
            semanticEvent.QuantityAfter is null ||
            semanticEvent.QuantityAfter <= 0)
        {
            return NoMatch();
        }

        return Progress(Math.Min(checked(state.CurrentProgress + semanticEvent.QuantityAfter.Value), objective.RequiredCount));
    }
}

public sealed class QuestObjectiveHandlerRegistry : IQuestObjectiveHandlerRegistry
{
    private readonly FrozenDictionary<QuestObjectiveType, IQuestObjectiveHandler> _handlers;

    public QuestObjectiveHandlerRegistry(IEnumerable<IQuestObjectiveHandler> handlers)
    {
        var values = handlers.ToArray();
        if (values.Select(value => value.ObjectiveType).Distinct().Count() != values.Length)
        {
            throw new InvalidOperationException("Quest objective handlers must be explicitly unique.");
        }

        _handlers = values.ToFrozenDictionary(value => value.ObjectiveType);
    }

    public OperationResult<IQuestObjectiveHandler> Resolve(QuestObjectiveType objectiveType) =>
        _handlers.TryGetValue(objectiveType, out var handler)
            ? OperationResult<IQuestObjectiveHandler>.Success(handler)
            : OperationResult<IQuestObjectiveHandler>.Failure(
                "quest.objective.unsupported",
                "No explicitly registered handler supports this objective type.",
                objectiveType.ToString());

    public static QuestObjectiveHandlerRegistry CreateDefault() =>
        new(
        [
            new KillMonsterQuestObjectiveHandler(),
            new OwnItemQuestObjectiveHandler(),
            new InteractNpcQuestObjectiveHandler(),
            new VisitMapQuestObjectiveHandler(),
            new UsePortalQuestObjectiveHandler(),
            new CompleteBattleQuestObjectiveHandler(),
            new CraftItemQuestObjectiveHandler()
        ]);
}

public sealed class QuestSemanticEventAdapter : IQuestSemanticEventAdapter
{
    public OperationResult<QuestSemanticEvent> FromMonsterDeath(
        MonsterDeathRecord death,
        long characterId,
        string accountSafeReference)
    {
        if (death.KillerCharacterId != characterId || death.DeathId == Guid.Empty)
        {
            return Failure("quest.semantic_event.invalid_killer", "Monster death does not belong to the eligible character.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: death.DeathId,
            EventType: QuestSemanticEventType.MonsterDefeated,
            SourceRuntime: "CombatRuntime",
            CharacterId: characterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: null,
            RoundNumber: null,
            MonsterRuntimeEntityId: death.MonsterRuntimeEntityId,
            MonsterTemplateId: death.MonsterTemplateId,
            DeathId: death.DeathId,
            InventoryTransactionId: null,
            ItemTemplateId: null,
            QuantityBefore: null,
            QuantityAfter: null,
            WorldInteractionId: null,
            TargetRuntimeEntityId: null,
            TargetTemplateId: null,
            PortalTransitionId: null,
            PortalTemplateId: null,
            SourceMapId: death.MapId,
            TargetMapId: null,
            EncounterDefinitionId: null,
            BattleWon: null,
            Committed: true,
            EventVersion: death.RuntimeVersionAfter,
            OccurredAtUtc: death.DiedAtUtc,
            CorrelationId: death.CorrelationId,
            RawMetadata: """{"credit":"DirectCharacterOwnership"}"""));
    }

    public OperationResult<QuestSemanticEvent> FromInventoryCommit(
        InventoryTransactionResult result,
        InventoryTransactionRequest request,
        int itemTemplateId,
        long quantityBefore,
        long quantityAfter,
        string accountSafeReference,
        string correlationId)
    {
        if (!result.Succeeded || quantityAfter < 0)
        {
            return Failure("quest.semantic_event.inventory_not_committed", "Inventory result is not a committed authority snapshot.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: result.TransactionId,
            EventType: QuestSemanticEventType.InventoryCommitted,
            SourceRuntime: "InventoryRuntime",
            CharacterId: request.CharacterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: null,
            RoundNumber: null,
            MonsterRuntimeEntityId: null,
            MonsterTemplateId: null,
            DeathId: null,
            InventoryTransactionId: result.TransactionId,
            ItemTemplateId: itemTemplateId,
            QuantityBefore: quantityBefore,
            QuantityAfter: quantityAfter,
            WorldInteractionId: null,
            TargetRuntimeEntityId: null,
            TargetTemplateId: null,
            PortalTransitionId: null,
            PortalTemplateId: null,
            SourceMapId: null,
            TargetMapId: null,
            EncounterDefinitionId: null,
            BattleWon: null,
            Committed: true,
            EventVersion: result.InventoryVersionAfter,
            OccurredAtUtc: request.CreatedAtUtc,
            CorrelationId: correlationId,
            RawMetadata: """{"authority":"InventorySnapshot"}"""));
    }

    public OperationResult<QuestSemanticEvent> FromNpcInteraction(
        WorldInteractionEvent runtimeEvent,
        int npcTemplateId,
        bool interactionSucceeded,
        string accountSafeReference)
    {
        if (!interactionSucceeded ||
            runtimeEvent.Kind is not (WorldInteractionEventKind.InteractionCompleted or WorldInteractionEventKind.NpcInteractionResolved))
        {
            return Failure("quest.semantic_event.interaction_not_committed", "World interaction did not complete.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: runtimeEvent.InteractionId,
            EventType: QuestSemanticEventType.NpcInteractionCompleted,
            SourceRuntime: "WorldInteractionRuntime",
            CharacterId: runtimeEvent.CharacterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: null,
            RoundNumber: null,
            MonsterRuntimeEntityId: null,
            MonsterTemplateId: null,
            DeathId: null,
            InventoryTransactionId: null,
            ItemTemplateId: null,
            QuantityBefore: null,
            QuantityAfter: null,
            WorldInteractionId: runtimeEvent.InteractionId,
            TargetRuntimeEntityId: runtimeEvent.TargetRuntimeEntityId,
            TargetTemplateId: npcTemplateId,
            PortalTransitionId: null,
            PortalTemplateId: null,
            SourceMapId: runtimeEvent.SourceMapId,
            TargetMapId: runtimeEvent.TargetMapId,
            EncounterDefinitionId: null,
            BattleWon: null,
            Committed: true,
            EventVersion: 1,
            OccurredAtUtc: runtimeEvent.OccurredAtUtc,
            CorrelationId: runtimeEvent.CorrelationId,
            RawMetadata: """{"authority":"WorldInteractionCoordinator"}"""));
    }

    public OperationResult<QuestSemanticEvent> FromPortalTransition(
        PortalTransitionResult result,
        string accountSafeReference)
    {
        if (!result.PersistenceCommitted ||
            result.State != PortalTransitionState.Committed ||
            !result.SessionRebound)
        {
            return Failure("quest.semantic_event.portal_not_committed", "Portal transition is not a committed session rebind.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: result.Plan.TransitionId,
            EventType: QuestSemanticEventType.PortalTransitionCommitted,
            SourceRuntime: "WorldInteractionRuntime",
            CharacterId: result.Plan.CharacterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: null,
            RoundNumber: null,
            MonsterRuntimeEntityId: null,
            MonsterTemplateId: null,
            DeathId: null,
            InventoryTransactionId: null,
            ItemTemplateId: null,
            QuantityBefore: null,
            QuantityAfter: null,
            WorldInteractionId: null,
            TargetRuntimeEntityId: result.Plan.PlayerRuntimeEntityId,
            TargetTemplateId: result.Plan.PortalTemplateId,
            PortalTransitionId: result.Plan.TransitionId,
            PortalTemplateId: result.Plan.PortalTemplateId,
            SourceMapId: result.Plan.SourceMapId,
            TargetMapId: result.Plan.TargetMapId,
            EncounterDefinitionId: null,
            BattleWon: null,
            Committed: true,
            EventVersion: result.RuntimeVersionAfter,
            OccurredAtUtc: result.Plan.CreatedAtUtc,
            CorrelationId: result.Plan.CorrelationId,
            RawMetadata: """{"authority":"PortalTransitionCommit"}"""));
    }

    public OperationResult<QuestSemanticEvent> FromBattleCompletion(
        Guid semanticEventId,
        Guid battleInstanceId,
        long characterId,
        string encounterDefinitionId,
        bool won,
        bool committed,
        string accountSafeReference,
        DateTimeOffset occurredAtUtc,
        string correlationId)
    {
        if (!committed || semanticEventId == Guid.Empty || battleInstanceId == Guid.Empty)
        {
            return Failure("quest.semantic_event.battle_not_committed", "Battle completion is not an exactly-once committed result.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: semanticEventId,
            EventType: QuestSemanticEventType.BattleCompleted,
            SourceRuntime: "TurnBasedBattleRuntime",
            CharacterId: characterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: battleInstanceId,
            RoundNumber: null,
            MonsterRuntimeEntityId: null,
            MonsterTemplateId: null,
            DeathId: null,
            InventoryTransactionId: null,
            ItemTemplateId: null,
            QuantityBefore: null,
            QuantityAfter: null,
            WorldInteractionId: null,
            TargetRuntimeEntityId: null,
            TargetTemplateId: null,
            PortalTransitionId: null,
            PortalTemplateId: null,
            SourceMapId: null,
            TargetMapId: null,
            EncounterDefinitionId: encounterDefinitionId,
            BattleWon: won,
            Committed: true,
            EventVersion: 1,
            OccurredAtUtc: occurredAtUtc,
            CorrelationId: correlationId,
            RawMetadata: """{"authority":"BattleCompletionResult"}"""));
    }

    public OperationResult<QuestSemanticEvent> FromCraftingCommit(
        Guid semanticEventId,
        CraftingResult result,
        long characterId,
        int resultItemTemplateId,
        string accountSafeReference,
        DateTimeOffset occurredAtUtc,
        string correlationId)
    {
        if (semanticEventId == Guid.Empty ||
            result.ResultCode is not (CraftingResultCode.Success or CraftingResultCode.DuplicateCompleted) ||
            result.CreatedQuantity <= 0)
        {
            return Failure("quest.semantic_event.crafting_not_committed", "Crafting result is not an exactly-once committed result.");
        }

        return Success(new QuestSemanticEvent(
            SemanticEventId: semanticEventId,
            EventType: QuestSemanticEventType.CraftingCommitted,
            SourceRuntime: "CraftingRuntime",
            CharacterId: characterId,
            AccountSafeReference: accountSafeReference,
            BattleInstanceId: null,
            RoundNumber: null,
            MonsterRuntimeEntityId: null,
            MonsterTemplateId: null,
            DeathId: null,
            InventoryTransactionId: semanticEventId,
            ItemTemplateId: resultItemTemplateId,
            QuantityBefore: null,
            QuantityAfter: result.CreatedQuantity,
            WorldInteractionId: null,
            TargetRuntimeEntityId: null,
            TargetTemplateId: result.QuestProgressEventId,
            PortalTransitionId: null,
            PortalTemplateId: null,
            SourceMapId: null,
            TargetMapId: null,
            EncounterDefinitionId: null,
            BattleWon: null,
            Committed: true,
            EventVersion: result.Inventory.Version,
            OccurredAtUtc: occurredAtUtc,
            CorrelationId: correlationId,
            RawMetadata: """{"authority":"CraftingCommit"}"""));
    }

    private static OperationResult<QuestSemanticEvent> Success(QuestSemanticEvent value) =>
        OperationResult<QuestSemanticEvent>.Success(value);

    private static OperationResult<QuestSemanticEvent> Failure(string code, string message) =>
        OperationResult<QuestSemanticEvent>.Failure(code, message, "QuestSemanticEventAdapter");
}

public sealed class AllRequiredQuestCompletionPolicy : IQuestCompletionPolicy
{
    public OperationResult<bool> IsReady(QuestDefinition definition, QuestInstance instance)
    {
        if (definition.CompletionPolicy != QuestCompletionPolicyType.AllRequired)
        {
            return OperationResult<bool>.Failure(
                "quest.completion_policy.unsupported",
                "Only AllRequired completion is implemented in this sprint.",
                definition.CompletionPolicy.ToString());
        }

        var requiredIds = definition.ObjectiveDefinitions
            .Where(value => value.Required)
            .Select(value => value.ObjectiveDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        if (requiredIds.Count == 0)
        {
            return OperationResult<bool>.Failure(
                "quest.completion_policy.missing_required",
                "Quest must have at least one required objective.",
                definition.ExternalQuestId);
        }

        var completed = instance.ObjectiveStates
            .Where(value => value.State == QuestObjectiveStateCode.Completed)
            .Select(value => value.ObjectiveDefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        return OperationResult<bool>.Success(requiredIds.IsSubsetOf(completed));
    }
}

public sealed class InMemoryQuestEventSink : IQuestEventSink
{
    private readonly ConcurrentQueue<QuestEvent> _events = [];

    public IReadOnlyList<QuestEvent> Snapshot => _events.ToArray();

    public void Publish(QuestEvent runtimeEvent) => _events.Enqueue(runtimeEvent);
}

public sealed class InMemoryQuestAuditLedger : IQuestAuditLedger
{
    private readonly ConcurrentQueue<QuestAuditRecord> _records = [];

    public IReadOnlyList<QuestAuditRecord> Snapshot => _records.ToArray();

    public void Append(QuestAuditRecord record) => _records.Enqueue(record);
}

public sealed class QuestProgressCoordinator : IQuestProgressCoordinator
{
    private readonly IQuestRuntimeStore _store;
    private readonly IQuestInstanceAuthority _authority;
    private readonly IQuestDefinitionCatalog _catalog;
    private readonly IQuestCompletionPolicy _completionPolicy;
    private readonly IQuestEventSink _events;
    private readonly IQuestAuditLedger _audit;
    private readonly QuestFailureInjection _failureInjection;
    private readonly AsyncKeyedLock<Guid> _locks = new();

    public QuestProgressCoordinator(
        IQuestRuntimeStore store,
        IQuestInstanceAuthority authority,
        IQuestDefinitionCatalog catalog,
        IQuestCompletionPolicy completionPolicy,
        IQuestEventSink events,
        IQuestAuditLedger audit,
        QuestFailureInjection? failureInjection = null)
    {
        _store = store;
        _authority = authority;
        _catalog = catalog;
        _completionPolicy = completionPolicy;
        _events = events;
        _audit = audit;
        _failureInjection = failureInjection ?? new QuestFailureInjection();
    }

    public async Task<QuestProgressResult> CommitAsync(
        QuestProgressMutationPlan plan,
        CancellationToken cancellationToken)
    {
        var idempotencyHash = QuestRuntimeSecurity.Hash(
            $"{plan.SemanticEventId:N}:{plan.QuestInstanceId:N}:{plan.ObjectiveDefinitionId}");
        var payloadHash = plan.SemanticPayloadHash;
        var replay = await _store.FindProgressAsync(idempotencyHash, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return Rejected(plan, QuestResultCode.ProgressReplayConflict, "quest.progress.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with { Code = QuestResultCode.DuplicateCompleted, IsDuplicate = true };
        }

        using var gate = await _locks.AcquireAsync(plan.QuestInstanceId, cancellationToken);
        try
        {
            replay = await _store.FindProgressAsync(idempotencyHash, payloadHash, cancellationToken);
            if (replay is { Found: true, PayloadMatches: false })
            {
                return Rejected(plan, QuestResultCode.ProgressReplayConflict, "quest.progress.replay_conflict");
            }

            if (replay is { Found: true, Result: not null })
            {
                return replay.Result with { Code = QuestResultCode.DuplicateCompleted, IsDuplicate = true };
            }

            var current = _authority.Get(plan.QuestInstanceId);
            if (current is null ||
                current.QuestVersion != plan.ExpectedQuestVersion ||
                current.State is QuestInstanceState.Completed or QuestInstanceState.Abandoned)
            {
                return Rejected(plan, QuestResultCode.ProgressVersionConflict, "quest.progress.quest_version_conflict");
            }

            var objective = current.ObjectiveStates.FirstOrDefault(
                value => value.ObjectiveDefinitionId == plan.ObjectiveDefinitionId);
            if (objective is null || objective.ObjectiveVersion != plan.ExpectedObjectiveVersion)
            {
                return Rejected(plan, QuestResultCode.ProgressVersionConflict, "quest.progress.objective_version_conflict");
            }

            if (plan.ProgressAfter < 0 || plan.ProgressAfter > objective.RequiredCount)
            {
                return Rejected(plan, QuestResultCode.Rejected, "quest.progress.out_of_range");
            }

            var objectiveAfter = objective with
            {
                CurrentProgress = plan.ProgressAfter,
                State = plan.ObjectiveStateAfter,
                ObjectiveVersion = checked(objective.ObjectiveVersion + 1),
                LastSemanticEventSafeId = QuestRuntimeSecurity.SafeId(plan.SemanticEventId.ToString("N")),
                UpdatedAtUtc = plan.CreatedAtUtc
            };
            var objectives = current.ObjectiveStates
                .Select(value => value.ObjectiveDefinitionId == objective.ObjectiveDefinitionId ? objectiveAfter : value)
                .OrderBy(value => value.ObjectiveIndex)
                .ToArray();
            var candidate = current with
            {
                ObjectiveStates = QuestRuntimeCollections.Freeze(objectives),
                QuestVersion = checked(current.QuestVersion + 1),
                LastProgressAtUtc = plan.CreatedAtUtc,
                CorrelationId = plan.CorrelationId
            };

            var definition = ResolveDefinition(candidate);
            if (!definition.Succeeded || definition.Value is null)
            {
                return Rejected(plan, QuestResultCode.RecoveryRequired, "quest.progress.definition_missing");
            }

            var readiness = _completionPolicy.IsReady(definition.Value, candidate);
            if (!readiness.Succeeded)
            {
                return Rejected(plan, QuestResultCode.ObjectiveEvidenceBlocked, readiness.Error.Code);
            }

            if (readiness.Value && candidate.State == QuestInstanceState.Active)
            {
                candidate = candidate with
                {
                    State = QuestInstanceState.ReadyToComplete,
                    ReadyAtUtc = plan.CreatedAtUtc
                };
            }

            var result = new QuestProgressResult(
                QuestResultCode.Success,
                plan.ProgressMutationId,
                plan.SemanticEventId,
                plan.QuestInstanceId,
                plan.ObjectiveDefinitionId,
                plan.ProgressBefore,
                plan.ProgressAfter,
                candidate.QuestVersion,
                objectiveAfter.ObjectiveVersion,
                objectiveAfter.State == QuestObjectiveStateCode.Completed,
                candidate.State == QuestInstanceState.ReadyToComplete,
                false,
                "");
            var persisted = await _store.CommitProgressAsync(
                plan,
                candidate,
                result,
                idempotencyHash,
                payloadHash,
                cancellationToken);
            if (!persisted.Succeeded)
            {
                return Rejected(plan, QuestResultCode.PersistenceFailure, persisted.Error.Code);
            }

            if (_failureInjection.Point == QuestFailurePoint.ProgressRuntime)
            {
                var loaded = await _store.LoadCharacterAsync(plan.CharacterId, cancellationToken);
                _authority.Reload(plan.CharacterId, loaded);
                return result with
                {
                    Code = QuestResultCode.RecoveryRequired,
                    FailureCode = "quest.progress.runtime_failed_reloaded"
                };
            }

            var replaced = _authority.Replace(candidate, current.QuestVersion);
            if (!replaced.Succeeded)
            {
                var loaded = await _store.LoadCharacterAsync(plan.CharacterId, cancellationToken);
                _authority.Reload(plan.CharacterId, loaded);
                return result with
                {
                    Code = QuestResultCode.RecoveryRequired,
                    FailureCode = "quest.progress.runtime_reconciled"
                };
            }

            Publish(QuestEventKind.QuestProgressCommitted, plan, candidate.State.ToString(), "");
            if (result.ObjectiveCompleted)
            {
                Publish(QuestEventKind.QuestObjectiveCompleted, plan, objectiveAfter.State.ToString(), "");
            }

            if (result.QuestReady)
            {
                Publish(QuestEventKind.QuestReadyToComplete, plan, candidate.State.ToString(), "");
            }

            AppendAudit(plan, current, candidate, objective, objectiveAfter, result);
            return result;
        }
        catch (OverflowException)
        {
            return Rejected(plan, QuestResultCode.Rejected, "quest.progress.overflow");
        }
    }

    private OperationResult<QuestDefinition> ResolveDefinition(QuestInstance instance)
    {
        var source = instance.RawMetadata.Contains("\"testOnly\":true", StringComparison.OrdinalIgnoreCase)
            ? QuestRequestSource.TestOnly
            : QuestRequestSource.ServerRuntime;
        return _catalog.Get(instance.QuestDefinitionId, source);
    }

    private void Publish(
        QuestEventKind kind,
        QuestProgressMutationPlan plan,
        string state,
        string failureCode) =>
        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            kind,
            null,
            plan.QuestInstanceId,
            plan.QuestDefinitionId,
            plan.ObjectiveDefinitionId,
            plan.SemanticEventId,
            null,
            null,
            plan.CharacterId,
            state,
            failureCode,
            DateTimeOffset.UtcNow,
            plan.CorrelationId,
            0));

    private void AppendAudit(
        QuestProgressMutationPlan plan,
        QuestInstance before,
        QuestInstance after,
        QuestObjectiveState objectiveBefore,
        QuestObjectiveState objectiveAfter,
        QuestProgressResult result) =>
        _audit.Append(new QuestAuditRecord(
            Guid.NewGuid(),
            null,
            plan.QuestInstanceId,
            plan.QuestDefinitionId,
            plan.ObjectiveDefinitionId,
            plan.ProgressMutationId,
            plan.SemanticEventId,
            null,
            null,
            QuestRuntimeSecurity.SafeId($"{plan.SemanticEventId:N}:{plan.ObjectiveDefinitionId}"),
            plan.CorrelationId,
            "",
            plan.CharacterId,
            null,
            "SemanticProgress",
            "Progress",
            before.State,
            after.State,
            objectiveBefore.State,
            objectiveAfter.State,
            plan.ProgressBefore,
            plan.ProgressDelta,
            plan.ProgressAfter,
            before.QuestVersion,
            after.QuestVersion,
            objectiveBefore.ObjectiveVersion,
            objectiveAfter.ObjectiveVersion,
            after.RewardState,
            false,
            result.Code,
            result.FailureCode,
            after.RecoveryState,
            [plan.PolicyStatus],
            plan.CreatedAtUtc,
            DateTimeOffset.UtcNow));

    private static QuestProgressResult Rejected(
        QuestProgressMutationPlan plan,
        QuestResultCode code,
        string failureCode) =>
        new(
            code,
            plan.ProgressMutationId,
            plan.SemanticEventId,
            plan.QuestInstanceId,
            plan.ObjectiveDefinitionId,
            plan.ProgressBefore,
            plan.ProgressBefore,
            plan.ExpectedQuestVersion,
            plan.ExpectedObjectiveVersion,
            false,
            false,
            false,
            failureCode);
}

public sealed class QuestEventRouter : IQuestEventRouter
{
    private readonly IQuestInstanceAuthority _authority;
    private readonly IQuestDefinitionCatalog _catalog;
    private readonly IQuestObjectiveHandlerRegistry _handlers;
    private readonly IQuestProgressCoordinator _progress;
    private readonly IQuestEventSink _events;
    private readonly bool _testOnlyRuntime;

    public QuestEventRouter(
        IQuestInstanceAuthority authority,
        IQuestDefinitionCatalog catalog,
        IQuestObjectiveHandlerRegistry handlers,
        IQuestProgressCoordinator progress,
        IQuestEventSink events,
        bool testOnlyRuntime = false)
    {
        _authority = authority;
        _catalog = catalog;
        _handlers = handlers;
        _progress = progress;
        _events = events;
        _testOnlyRuntime = testOnlyRuntime;
    }

    public async Task<IReadOnlyList<QuestProgressResult>> RouteAsync(
        QuestSemanticEvent semanticEvent,
        CancellationToken cancellationToken)
    {
        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            QuestEventKind.QuestSemanticEventReceived,
            null,
            null,
            null,
            null,
            semanticEvent.SemanticEventId,
            null,
            null,
            semanticEvent.CharacterId,
            "Received",
            "",
            DateTimeOffset.UtcNow,
            semanticEvent.CorrelationId,
            0));

        if (!semanticEvent.Committed)
        {
            return [];
        }

        var results = new List<QuestProgressResult>();
        var instances = _authority.GetCharacter(semanticEvent.CharacterId)
            .Where(value => value.State is QuestInstanceState.Active or QuestInstanceState.ReadyToComplete)
            .OrderBy(value => value.QuestDefinitionId)
            .ThenBy(value => value.QuestInstanceId)
            .ToArray();
        foreach (var instanceSnapshot in instances)
        {
            var instance = _authority.Get(instanceSnapshot.QuestInstanceId) ?? instanceSnapshot;
            var definitionResult = _catalog.Get(
                instance.QuestDefinitionId,
                _testOnlyRuntime ? QuestRequestSource.TestOnly : QuestRequestSource.ServerRuntime);
            if (!definitionResult.Succeeded || definitionResult.Value is null)
            {
                continue;
            }

            var definition = definitionResult.Value;
            foreach (var objective in definition.ObjectiveDefinitions.OrderBy(value => value.ObjectiveIndex))
            {
                instance = _authority.Get(instance.QuestInstanceId) ?? instance;
                var state = instance.ObjectiveStates.First(value =>
                    value.ObjectiveDefinitionId == objective.ObjectiveDefinitionId);
                var handler = _handlers.Resolve(objective.ObjectiveType);
                if (!handler.Succeeded || handler.Value is null)
                {
                    results.Add(new QuestProgressResult(
                        QuestResultCode.UnsupportedObjective,
                        Guid.Empty,
                        semanticEvent.SemanticEventId,
                        instance.QuestInstanceId,
                        objective.ObjectiveDefinitionId,
                        state.CurrentProgress,
                        state.CurrentProgress,
                        instance.QuestVersion,
                        state.ObjectiveVersion,
                        false,
                        false,
                        false,
                        "quest.objective.unsupported"));
                    continue;
                }

                var planned = handler.Value.CreatePlan(definition, instance, objective, state, semanticEvent);
                if (!planned.Succeeded)
                {
                    results.Add(new QuestProgressResult(
                        planned.Error.Code == "quest.objective.evidence_blocked"
                            ? QuestResultCode.ObjectiveEvidenceBlocked
                            : QuestResultCode.Rejected,
                        Guid.Empty,
                        semanticEvent.SemanticEventId,
                        instance.QuestInstanceId,
                        objective.ObjectiveDefinitionId,
                        state.CurrentProgress,
                        state.CurrentProgress,
                        instance.QuestVersion,
                        state.ObjectiveVersion,
                        false,
                        false,
                        false,
                        planned.Error.Code));
                    continue;
                }

                if (planned.Value is null)
                {
                    continue;
                }

                _events.Publish(new QuestEvent(
                    Guid.NewGuid(),
                    QuestEventKind.QuestProgressPlanned,
                    null,
                    instance.QuestInstanceId,
                    instance.QuestDefinitionId,
                    objective.ObjectiveDefinitionId,
                    semanticEvent.SemanticEventId,
                    null,
                    null,
                    semanticEvent.CharacterId,
                    "Planned",
                    "",
                    DateTimeOffset.UtcNow,
                    semanticEvent.CorrelationId,
                    0));
                results.Add(await _progress.CommitAsync(planned.Value, cancellationToken));
            }
        }

        return QuestRuntimeCollections.Freeze(results);
    }
}

public sealed class QuestRewardCoordinator : IQuestRewardCoordinator
{
    private readonly IQuestRuntimeStore _store;
    private readonly IInventoryTransactionCoordinator _inventory;
    private readonly IQuestEventSink _events;

    public QuestRewardCoordinator(
        IQuestRuntimeStore store,
        IInventoryTransactionCoordinator inventory,
        IQuestEventSink events)
    {
        _store = store;
        _inventory = inventory;
        _events = events;
    }

    public async Task<QuestRewardResult> CommitAsync(
        QuestRewardPlan plan,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var payloadHash = QuestRuntimeSecurity.Hash(JsonSerializer.Serialize(plan));
        var replay = await _store.FindRewardAsync(plan.IdempotencyKeyHash, payloadHash, cancellationToken);
        if (replay is { Found: true, PayloadMatches: false })
        {
            return Rejected(plan, QuestResultCode.CompletionConflict, "quest.reward.replay_conflict");
        }

        if (replay is { Found: true, Result: not null })
        {
            return replay.Result with { Code = QuestResultCode.DuplicateCompleted, IsDuplicate = true };
        }

        if (plan.PolicyStatus is QuestContentStatus.EvidenceBlocked or QuestContentStatus.Unknown or QuestContentStatus.VisualOnly)
        {
            return Rejected(plan, QuestResultCode.RewardEvidenceBlocked, "quest.reward.evidence_blocked");
        }

        if (plan.ExperienceRewardCandidate is not null ||
            plan.SkillRewardCandidate is not null ||
            plan.OtherRewardCandidates.Count > 0)
        {
            return Rejected(plan, QuestResultCode.UnsupportedReward, "quest.reward.unsupported");
        }

        if (plan.CurrencyRewards.Any(value => !value.CurrencyType.Equals("Gold", StringComparison.OrdinalIgnoreCase)) ||
            plan.CurrencyRewards.Select(value => value.CurrencyType).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return Rejected(plan, QuestResultCode.UnsupportedReward, "quest.reward.currency_type_unsupported");
        }

        InventoryTransactionResult? inventoryResult = null;
        if (plan.ItemRewards.Count > 0 || plan.CurrencyRewards.Count > 0)
        {
            var inventoryVersion = await _inventory.GetCurrentVersionAsync(plan.CharacterId, cancellationToken);
            if (!inventoryVersion.Succeeded)
            {
                return Rejected(
                    plan,
                    QuestResultCode.InventoryTransactionRejected,
                    inventoryVersion.Error.Code);
            }

            var request = new InventoryTransactionRequest(
                plan.QuestRewardPlanId,
                $"quest-reward:{plan.IdempotencyKeyHash}",
                plan.CharacterId,
                sessionId,
                null,
                InventoryOperationType.SystemGrant,
                inventoryVersion.Value,
                "QuestReward",
                QuestRuntimeCollections.Freeze(plan.ItemRewards.Select(value =>
                    new InventoryMutationRequest(ItemTemplateId: value.ItemTemplateId, Quantity: value.Quantity))),
                plan.CurrencyRewards.Sum(value => value.Amount),
                null,
                plan.CreatedAtUtc,
                InventoryMutationAuthorityKind.TrustedServer);
            inventoryResult = await _inventory.ExecuteAsync(request, cancellationToken);
            if (!inventoryResult.Succeeded)
            {
                return Rejected(
                    plan,
                    QuestResultCode.InventoryTransactionRejected,
                    inventoryResult.FailureCode,
                    inventoryResult);
            }
        }

        var result = new QuestRewardResult(
            QuestResultCode.Success,
            plan.QuestRewardPlanId,
            plan.ItemRewards.Count > 0,
            plan.CurrencyRewards.Count > 0,
            false,
            "",
            inventoryResult);
        var persisted = await _store.CommitRewardAsync(plan, result, payloadHash, cancellationToken);
        if (!persisted.Succeeded)
        {
            return Rejected(plan, QuestResultCode.RecoveryRequired, persisted.Error.Code, inventoryResult);
        }

        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            QuestEventKind.QuestRewardCommitted,
            null,
            plan.QuestInstanceId,
            plan.QuestDefinitionId,
            null,
            null,
            plan.CompletionPlanId,
            plan.QuestRewardPlanId,
            plan.CharacterId,
            "Committed",
            "",
            DateTimeOffset.UtcNow,
            plan.CorrelationId,
            0));
        return result;
    }

    private static QuestRewardResult Rejected(
        QuestRewardPlan plan,
        QuestResultCode code,
        string failureCode,
        InventoryTransactionResult? inventoryResult = null) =>
        new(
            code,
            plan.QuestRewardPlanId,
            false,
            false,
            false,
            failureCode,
            inventoryResult);
}

public sealed class QuestCoordinator : IQuestCoordinator
{
    private readonly IQuestDefinitionCatalog _catalog;
    private readonly IQuestSessionValidator _sessions;
    private readonly IQuestBindingResolver _bindings;
    private readonly IQuestEligibilityPolicy _eligibility;
    private readonly IQuestInventorySnapshotProvider _inventorySnapshots;
    private readonly IQuestInstanceAuthority _authority;
    private readonly IQuestRuntimeStore _store;
    private readonly IQuestCompletionPolicy _completionPolicy;
    private readonly IQuestRewardCoordinator _rewards;
    private readonly IQuestEventSink _events;
    private readonly IQuestAuditLedger _audit;
    private readonly QuestFailureInjection _failureInjection;
    private readonly AsyncKeyedLock<long> _locks = new();

    public QuestCoordinator(
        IQuestDefinitionCatalog catalog,
        IQuestSessionValidator sessions,
        IQuestBindingResolver bindings,
        IQuestEligibilityPolicy eligibility,
        IQuestInventorySnapshotProvider inventorySnapshots,
        IQuestInstanceAuthority authority,
        IQuestRuntimeStore store,
        IQuestCompletionPolicy completionPolicy,
        IQuestRewardCoordinator rewards,
        IQuestEventSink events,
        IQuestAuditLedger audit,
        QuestFailureInjection? failureInjection = null)
    {
        _catalog = catalog;
        _sessions = sessions;
        _bindings = bindings;
        _eligibility = eligibility;
        _inventorySnapshots = inventorySnapshots;
        _authority = authority;
        _store = store;
        _completionPolicy = completionPolicy;
        _rewards = rewards;
        _events = events;
        _audit = audit;
        _failureInjection = failureInjection ?? new QuestFailureInjection();
    }

    public Task<QuestOperationResult> AcceptAsync(QuestIntent intent, CancellationToken cancellationToken) =>
        ExecuteLockedAsync(intent.CharacterId, () => AcceptLockedAsync(intent, cancellationToken), cancellationToken);

    public Task<QuestOperationResult> AbandonAsync(QuestIntent intent, CancellationToken cancellationToken) =>
        ExecuteLockedAsync(intent.CharacterId, () => AbandonLockedAsync(intent, cancellationToken), cancellationToken);

    public Task<QuestOperationResult> TurnInAsync(QuestIntent intent, CancellationToken cancellationToken) =>
        ExecuteLockedAsync(intent.CharacterId, () => TurnInLockedAsync(intent, cancellationToken), cancellationToken);

    private async Task<QuestOperationResult> AcceptLockedAsync(
        QuestIntent intent,
        CancellationToken cancellationToken)
    {
        Publish(QuestEventKind.QuestAcceptRequested, intent, null, "Requested", "");
        if (intent.IntentType != QuestIntentType.Accept)
        {
            return Reject(intent, QuestResultCode.Rejected, "quest.intent.invalid_type");
        }

        var replay = await FindReplayAsync("accept", intent, cancellationToken);
        if (replay.Result is not null)
        {
            return replay.Result;
        }

        if (!replay.PayloadMatches)
        {
            return Reject(intent, QuestResultCode.ProgressReplayConflict, "quest.accept.replay_conflict");
        }

        var session = _sessions.Validate(intent);
        if (!session.Succeeded || session.Value is null)
        {
            return Reject(
                intent,
                session.Error.Code == "quest.ownership_mismatch"
                    ? QuestResultCode.OwnershipMismatch
                    : QuestResultCode.InvalidSession,
                session.Error.Code);
        }

        var definitionResult = _catalog.Get(intent.QuestDefinitionId, intent.Source);
        if (!definitionResult.Succeeded || definitionResult.Value is null)
        {
            return Reject(
                intent,
                definitionResult.Error.Code == "quest.disabled"
                    ? QuestResultCode.QuestDisabled
                    : definitionResult.Error.Code == "quest.not_found"
                        ? QuestResultCode.QuestNotFound
                        : QuestResultCode.QuestEvidenceBlocked,
                definitionResult.Error.Code);
        }

        var definition = definitionResult.Value;
        var production = intent.Source == QuestRequestSource.ProductionClient;
        if (definition.NpcBindings.Any(value =>
                value.BindingType is QuestNpcBindingType.Accept or QuestNpcBindingType.AcceptAndTurnIn))
        {
            var binding = _bindings.Resolve(new QuestBindingContext(
                definition,
                QuestNpcBindingType.Accept,
                intent.SourceNpcRuntimeEntityId,
                intent.SourceNpcTemplateId,
                session.Value.MapId,
                intent.SourceNpcRuntimeEntityId is > 0,
                production));
            if (!binding.Succeeded)
            {
                return Reject(intent, QuestResultCode.InvalidNpcBinding, binding.Error.Code);
            }
        }

        var existing = await _store.LoadCharacterAsync(intent.CharacterId, cancellationToken);
        var completed = existing
            .Where(value => value.State == QuestInstanceState.Completed)
            .Select(value => value.QuestDefinitionId)
            .ToHashSet();
        var owned = new Dictionary<int, long>();
        foreach (var itemTemplateId in definition.Prerequisites
                     .Where(value => value.ItemTemplateId is not null)
                     .Select(value => value.ItemTemplateId!.Value)
                     .Concat(definition.ObjectiveDefinitions
                         .Where(value => value.ObjectiveType == QuestObjectiveType.OwnItem && value.TargetTemplateId is not null)
                         .Select(value => value.TargetTemplateId!.Value))
                     .Distinct())
        {
            owned[itemTemplateId] = await _inventorySnapshots.GetOwnedQuantityAsync(
                intent.CharacterId,
                itemTemplateId,
                cancellationToken);
        }

        var eligibility = _eligibility.Evaluate(new QuestEligibilityContext(
            intent,
            definition,
            session.Value,
            existing,
            completed,
            QuestRuntimeCollections.Freeze(owned),
            production));
        if (!eligibility.Succeeded)
        {
            return Reject(intent, eligibility.Code, eligibility.FailureCode);
        }

        var now = intent.CreatedAtUtc;
        var initialStates = definition.ObjectiveDefinitions
            .OrderBy(value => value.ObjectiveIndex)
            .Select(objective =>
            {
                var progress = objective.ObjectiveType == QuestObjectiveType.OwnItem &&
                               objective.TargetTemplateId is not null
                    ? Math.Min(owned.GetValueOrDefault(objective.TargetTemplateId.Value), objective.RequiredCount)
                    : 0;
                return new QuestObjectiveState(
                    objective.ObjectiveDefinitionId,
                    objective.ObjectiveIndex,
                    objective.ObjectiveType,
                    objective.TargetTemplateId,
                    objective.RequiredCount,
                    progress,
                    progress >= objective.RequiredCount
                        ? QuestObjectiveStateCode.Completed
                        : progress > 0
                            ? QuestObjectiveStateCode.InProgress
                            : QuestObjectiveStateCode.Active,
                    0,
                    null,
                    objective.PolicyStatus,
                    now);
            })
            .ToArray();
        var iteration = existing
            .Where(value => value.QuestDefinitionId == definition.QuestDefinitionId)
            .Select(value => value.RepeatIteration)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var instance = new QuestInstance(
            Guid.NewGuid(),
            intent.CharacterId,
            definition.QuestDefinitionId,
            QuestInstanceState.Active,
            now,
            intent.SourceNpcTemplateId,
            session.Value.MapId,
            QuestRuntimeCollections.Freeze(initialStates),
            1,
            definition.ContentVersion,
            iteration,
            null,
            null,
            null,
            QuestRewardState.None,
            QuestRecoveryState.None,
            null,
            intent.CorrelationId,
            definition.PolicyStatus == QuestContentStatus.TestOnly
                ? """{"testOnly":true}"""
                : """{"testOnly":false}""");
        var readiness = _completionPolicy.IsReady(definition, instance);
        if (readiness.Succeeded && readiness.Value)
        {
            instance = instance with { State = QuestInstanceState.ReadyToComplete, ReadyAtUtc = now };
        }

        var plan = new QuestAcceptancePlan(
            Guid.NewGuid(),
            intent.QuestIntentId,
            instance.QuestInstanceId,
            intent.CharacterId,
            definition.QuestDefinitionId,
            intent.SourceNpcTemplateId,
            instance.ObjectiveStates,
            0,
            definition.ContentVersion,
            now,
            intent.CorrelationId);
        var result = Success(intent, instance);
        var persisted = await _store.CommitAcceptanceAsync(
            plan,
            instance,
            result,
            QuestRuntimeSecurity.Hash(intent.IdempotencyKey),
            QuestRuntimeSecurity.PayloadHash(intent),
            cancellationToken);
        if (!persisted.Succeeded)
        {
            return Reject(intent, QuestResultCode.PersistenceFailure, persisted.Error.Code);
        }

        if (_failureInjection.Point == QuestFailurePoint.AcceptanceRuntime)
        {
            var loaded = await _store.LoadCharacterAsync(intent.CharacterId, cancellationToken);
            _authority.Reload(intent.CharacterId, loaded);
            return result with
            {
                Code = QuestResultCode.RecoveryRequired,
                FailureCode = "quest.accept.runtime_failed_reloaded"
            };
        }

        var added = _authority.Add(instance);
        if (!added.Succeeded)
        {
            var loaded = await _store.LoadCharacterAsync(intent.CharacterId, cancellationToken);
            _authority.Reload(intent.CharacterId, loaded);
            return result with
            {
                Code = QuestResultCode.RecoveryRequired,
                FailureCode = "quest.accept.runtime_reconciled"
            };
        }

        Publish(QuestEventKind.QuestAccepted, intent, instance, instance.State.ToString(), "");
        AppendIntentAudit("Accept", intent, null, instance, result, null);
        return result;
    }

    private async Task<QuestOperationResult> AbandonLockedAsync(
        QuestIntent intent,
        CancellationToken cancellationToken)
    {
        Publish(QuestEventKind.QuestAbandonRequested, intent, null, "Requested", "");
        if (intent.IntentType != QuestIntentType.Abandon)
        {
            return Reject(intent, QuestResultCode.Rejected, "quest.intent.invalid_type");
        }

        var replay = await FindReplayAsync("abandon", intent, cancellationToken);
        if (replay.Result is not null)
        {
            return replay.Result;
        }

        if (!replay.PayloadMatches)
        {
            return Reject(intent, QuestResultCode.ProgressReplayConflict, "quest.abandon.replay_conflict");
        }

        var session = _sessions.Validate(intent);
        if (!session.Succeeded)
        {
            return Reject(intent, QuestResultCode.InvalidSession, session.Error.Code);
        }

        var current = FindIntentInstance(intent);
        if (current is null)
        {
            return Reject(intent, QuestResultCode.QuestNotActive, "quest.not_active");
        }

        if (current.State == QuestInstanceState.Completed)
        {
            return Reject(intent, QuestResultCode.QuestAlreadyCompleted, "quest.completed_cannot_abandon");
        }

        if (current.State is not (QuestInstanceState.Active or QuestInstanceState.ReadyToComplete))
        {
            return Reject(intent, QuestResultCode.QuestNotActive, "quest.not_active");
        }

        if (intent.ExpectedQuestVersion is not null && intent.ExpectedQuestVersion != current.QuestVersion)
        {
            return Reject(intent, QuestResultCode.ProgressVersionConflict, "quest.version_conflict");
        }

        var after = current with
        {
            State = QuestInstanceState.Abandoned,
            QuestVersion = checked(current.QuestVersion + 1),
            AbandonedAtUtc = intent.CreatedAtUtc,
            CorrelationId = intent.CorrelationId
        };
        var plan = new QuestAbandonPlan(
            Guid.NewGuid(),
            intent.QuestIntentId,
            current.QuestInstanceId,
            current.CharacterId,
            current.QuestDefinitionId,
            current.QuestVersion,
            current.ObjectiveStates,
            intent.CreatedAtUtc,
            intent.CorrelationId);
        var result = Success(intent, after);
        var persisted = await _store.CommitAbandonmentAsync(
            plan,
            after,
            result,
            QuestRuntimeSecurity.Hash(intent.IdempotencyKey),
            QuestRuntimeSecurity.PayloadHash(intent),
            cancellationToken);
        if (!persisted.Succeeded)
        {
            return Reject(intent, QuestResultCode.PersistenceFailure, persisted.Error.Code);
        }

        var replaced = _authority.Replace(after, current.QuestVersion);
        if (!replaced.Succeeded)
        {
            var loaded = await _store.LoadCharacterAsync(intent.CharacterId, cancellationToken);
            _authority.Reload(intent.CharacterId, loaded);
            return result with
            {
                Code = QuestResultCode.RecoveryRequired,
                FailureCode = "quest.abandon.runtime_reconciled"
            };
        }

        Publish(QuestEventKind.QuestAbandoned, intent, after, after.State.ToString(), "");
        AppendIntentAudit("Abandon", intent, current, after, result, null);
        return result;
    }

    private async Task<QuestOperationResult> TurnInLockedAsync(
        QuestIntent intent,
        CancellationToken cancellationToken)
    {
        Publish(QuestEventKind.QuestTurnInRequested, intent, null, "Requested", "");
        if (intent.IntentType != QuestIntentType.TurnIn)
        {
            return Reject(intent, QuestResultCode.Rejected, "quest.intent.invalid_type");
        }

        var replay = await FindReplayAsync("turnin", intent, cancellationToken);
        if (replay.Result is not null)
        {
            return replay.Result;
        }

        if (!replay.PayloadMatches)
        {
            return Reject(intent, QuestResultCode.CompletionConflict, "quest.turnin.replay_conflict");
        }

        var session = _sessions.Validate(intent);
        if (!session.Succeeded || session.Value is null)
        {
            return Reject(intent, QuestResultCode.InvalidSession, session.Error.Code);
        }

        var current = FindIntentInstance(intent);
        if (current is null)
        {
            return Reject(intent, QuestResultCode.QuestNotActive, "quest.not_active");
        }

        if (current.State == QuestInstanceState.Completed)
        {
            return Reject(intent, QuestResultCode.QuestAlreadyCompleted, "quest.already_completed");
        }

        if (current.State != QuestInstanceState.ReadyToComplete)
        {
            return Reject(intent, QuestResultCode.QuestNotReady, "quest.not_ready");
        }

        if (intent.ExpectedQuestVersion is not null && intent.ExpectedQuestVersion != current.QuestVersion)
        {
            return Reject(intent, QuestResultCode.ProgressVersionConflict, "quest.version_conflict");
        }

        var source = current.RawMetadata.Contains("\"testOnly\":true", StringComparison.OrdinalIgnoreCase)
            ? QuestRequestSource.TestOnly
            : intent.Source;
        var definitionResult = _catalog.Get(current.QuestDefinitionId, source);
        if (!definitionResult.Succeeded || definitionResult.Value is null)
        {
            return Reject(intent, QuestResultCode.RecoveryRequired, "quest.completion.definition_missing");
        }

        var definition = definitionResult.Value;
        if (definition.NpcBindings.Any(value =>
                value.BindingType is QuestNpcBindingType.TurnIn or QuestNpcBindingType.AcceptAndTurnIn))
        {
            var binding = _bindings.Resolve(new QuestBindingContext(
                definition,
                QuestNpcBindingType.TurnIn,
                intent.SourceNpcRuntimeEntityId,
                intent.SourceNpcTemplateId,
                session.Value.MapId,
                intent.SourceNpcRuntimeEntityId is > 0,
                intent.Source == QuestRequestSource.ProductionClient));
            if (!binding.Succeeded)
            {
                return Reject(intent, QuestResultCode.InvalidNpcBinding, binding.Error.Code);
            }
        }

        var readiness = _completionPolicy.IsReady(definition, current);
        if (!readiness.Succeeded || !readiness.Value)
        {
            return Reject(intent, QuestResultCode.QuestNotReady, readiness.Succeeded ? "quest.not_ready" : readiness.Error.Code);
        }

        var completionPlanId = Guid.NewGuid();
        var rewardPlan = new QuestRewardPlan(
            Guid.NewGuid(),
            completionPlanId,
            current.QuestInstanceId,
            current.QuestDefinitionId,
            current.CharacterId,
            definition.RewardDefinition.ItemRewards,
            definition.RewardDefinition.CurrencyRewards,
            definition.RewardDefinition.ExperienceRewardCandidate,
            definition.RewardDefinition.SkillRewardCandidate,
            definition.RewardDefinition.OtherRewardCandidates,
            QuestRuntimeSecurity.Hash($"quest:{current.QuestInstanceId:N}:completion:{current.QuestVersion}"),
            definition.RewardDefinition.PolicyStatus,
            intent.CreatedAtUtc,
            intent.CorrelationId);
        var completionPlan = new QuestCompletionPlan(
            completionPlanId,
            intent.QuestIntentId,
            current.QuestInstanceId,
            current.QuestDefinitionId,
            current.CharacterId,
            intent.SourceNpcTemplateId,
            current.ObjectiveStates,
            rewardPlan,
            current.QuestVersion,
            current.DefinitionContentVersion,
            intent.CreatedAtUtc,
            intent.CorrelationId);
        Publish(QuestEventKind.QuestCompletionStarted, intent, current, "Completing", "");
        Publish(QuestEventKind.QuestRewardPlanned, intent, current, "Planned", "");
        var reward = await _rewards.CommitAsync(rewardPlan, intent.SessionId, cancellationToken);
        if (!reward.Code.Equals(QuestResultCode.Success) &&
            !reward.Code.Equals(QuestResultCode.DuplicateCompleted))
        {
            var recovery = current with
            {
                State = QuestInstanceState.RecoveryRequired,
                RewardState = QuestRewardState.RecoveryRequired,
                RecoveryState = QuestRecoveryState.RecoveryRequired,
                QuestVersion = checked(current.QuestVersion + 1)
            };
            await _store.SaveRecoveryAsync(recovery, reward.FailureCode, cancellationToken);
            _authority.Replace(recovery, current.QuestVersion);
            return Reject(intent, reward.Code, reward.FailureCode, recovery, reward);
        }

        var completed = current with
        {
            State = QuestInstanceState.Completed,
            RewardState = QuestRewardState.Committed,
            RecoveryState = QuestRecoveryState.None,
            QuestVersion = checked(current.QuestVersion + 1),
            CompletedAtUtc = intent.CreatedAtUtc,
            CorrelationId = intent.CorrelationId
        };
        var result = Success(intent, completed, reward);
        var persisted = await _store.CommitCompletionAsync(
            completionPlan,
            completed,
            result,
            QuestRuntimeSecurity.Hash(intent.IdempotencyKey),
            QuestRuntimeSecurity.PayloadHash(intent),
            cancellationToken);
        if (!persisted.Succeeded)
        {
            var recovery = current with
            {
                State = QuestInstanceState.RecoveryRequired,
                RewardState = QuestRewardState.Committed,
                RecoveryState = QuestRecoveryState.RecoveryRequired,
                QuestVersion = checked(current.QuestVersion + 1)
            };
            await _store.SaveRecoveryAsync(recovery, persisted.Error.Code, cancellationToken);
            _authority.Replace(recovery, current.QuestVersion);
            return result with
            {
                Code = QuestResultCode.RecoveryRequired,
                Instance = recovery,
                QuestVersion = recovery.QuestVersion,
                FailureCode = "quest.completion.persistence_after_reward"
            };
        }

        var replaced = _authority.Replace(completed, current.QuestVersion);
        if (!replaced.Succeeded)
        {
            var loaded = await _store.LoadCharacterAsync(intent.CharacterId, cancellationToken);
            _authority.Reload(intent.CharacterId, loaded);
        }

        Publish(QuestEventKind.QuestCompleted, intent, completed, completed.State.ToString(), "");
        AppendIntentAudit("TurnIn", intent, current, completed, result, rewardPlan);
        return result;
    }

    private QuestInstance? FindIntentInstance(QuestIntent intent)
    {
        var values = _authority.GetCharacter(intent.CharacterId)
            .Where(value => value.QuestDefinitionId == intent.QuestDefinitionId)
            .OrderByDescending(value => value.RepeatIteration)
            .ThenByDescending(value => value.AcceptedAtUtc)
            .ToArray();
        return intent.ExpectedQuestVersion is null
            ? values.FirstOrDefault()
            : values.FirstOrDefault(value => value.QuestVersion == intent.ExpectedQuestVersion);
    }

    private async Task<(bool PayloadMatches, QuestOperationResult? Result)> FindReplayAsync(
        string operation,
        QuestIntent intent,
        CancellationToken cancellationToken)
    {
        var replay = await _store.FindOperationAsync(
            operation,
            QuestRuntimeSecurity.Hash(intent.IdempotencyKey),
            QuestRuntimeSecurity.PayloadHash(intent),
            cancellationToken);
        if (!replay.Found)
        {
            return (true, null);
        }

        if (!replay.PayloadMatches)
        {
            return (false, null);
        }

        return (
            true,
            replay.Result is null
                ? null
                : replay.Result with
                {
                    Code = QuestResultCode.DuplicateCompleted,
                    IsDuplicate = true
                });
    }

    private async Task<QuestOperationResult> ExecuteLockedAsync(
        long characterId,
        Func<Task<QuestOperationResult>> action,
        CancellationToken cancellationToken)
    {
        using var gate = await _locks.AcquireAsync(characterId, cancellationToken);
        try
        {
            return await action();
        }
        catch (OverflowException)
        {
            return new QuestOperationResult(
                QuestResultCode.Rejected,
                Guid.Empty,
                null,
                0,
                false,
                "quest.numeric_overflow",
                null,
                null,
                []);
        }
    }

    private void Publish(
        QuestEventKind kind,
        QuestIntent intent,
        QuestInstance? instance,
        string state,
        string failureCode) =>
        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            kind,
            intent.QuestIntentId,
            instance?.QuestInstanceId,
            intent.QuestDefinitionId,
            null,
            null,
            null,
            null,
            intent.CharacterId,
            state,
            failureCode,
            DateTimeOffset.UtcNow,
            intent.CorrelationId,
            0));

    private void AppendIntentAudit(
        string operation,
        QuestIntent intent,
        QuestInstance? before,
        QuestInstance after,
        QuestOperationResult result,
        QuestRewardPlan? rewardPlan) =>
        _audit.Append(new QuestAuditRecord(
            Guid.NewGuid(),
            intent.QuestIntentId,
            after.QuestInstanceId,
            after.QuestDefinitionId,
            null,
            null,
            null,
            rewardPlan?.CompletionPlanId,
            rewardPlan?.QuestRewardPlanId,
            QuestRuntimeSecurity.SafeId(intent.IdempotencyKey),
            intent.CorrelationId,
            QuestRuntimeSecurity.SafeId(intent.SessionId),
            intent.CharacterId,
            intent.SourceNpcTemplateId,
            intent.IntentType.ToString(),
            operation,
            before?.State,
            after.State,
            null,
            null,
            null,
            null,
            null,
            before?.QuestVersion,
            after.QuestVersion,
            null,
            null,
            after.RewardState,
            after.RewardState == QuestRewardState.Committed,
            result.Code,
            result.FailureCode,
            after.RecoveryState,
            [],
            intent.CreatedAtUtc,
            DateTimeOffset.UtcNow));

    private static QuestOperationResult Success(
        QuestIntent intent,
        QuestInstance instance,
        QuestRewardResult? reward = null) =>
        new(
            QuestResultCode.Success,
            intent.QuestIntentId,
            instance.QuestInstanceId,
            instance.QuestVersion,
            false,
            "",
            instance,
            reward,
            []);

    private QuestOperationResult Reject(
        QuestIntent intent,
        QuestResultCode code,
        string failureCode,
        QuestInstance? instance = null,
        QuestRewardResult? reward = null)
    {
        if (intent.IntentType == QuestIntentType.Accept)
        {
            Publish(QuestEventKind.QuestAcceptRejected, intent, instance, "Rejected", failureCode);
        }

        return new QuestOperationResult(
            code,
            intent.QuestIntentId,
            instance?.QuestInstanceId,
            instance?.QuestVersion ?? 0,
            false,
            failureCode,
            instance,
            reward,
            []);
    }
}

public sealed class QuestRecoveryCoordinator : IQuestRecoveryCoordinator
{
    private readonly IQuestRuntimeStore _store;
    private readonly IQuestInstanceAuthority _authority;
    private readonly IQuestDefinitionCatalog _catalog;
    private readonly IQuestEventSink _events;

    public QuestRecoveryCoordinator(
        IQuestRuntimeStore store,
        IQuestInstanceAuthority authority,
        IQuestDefinitionCatalog catalog,
        IQuestEventSink events)
    {
        _store = store;
        _authority = authority;
        _catalog = catalog;
        _events = events;
    }

    public async Task<QuestOperationResult> RecoverAsync(
        long characterId,
        Guid? questInstanceId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            QuestEventKind.QuestRecoveryStarted,
            null,
            questInstanceId,
            null,
            null,
            null,
            null,
            null,
            characterId,
            "Started",
            "",
            DateTimeOffset.UtcNow,
            correlationId,
            0));
        var loaded = await _store.LoadCharacterAsync(characterId, cancellationToken);
        if (questInstanceId is not null)
        {
            loaded = QuestRuntimeCollections.Freeze(loaded.Where(value => value.QuestInstanceId == questInstanceId));
        }

        foreach (var instance in loaded)
        {
            var source = instance.RawMetadata.Contains("\"testOnly\":true", StringComparison.OrdinalIgnoreCase)
                ? QuestRequestSource.TestOnly
                : QuestRequestSource.Recovery;
            var definition = _catalog.Get(instance.QuestDefinitionId, source);
            if (!definition.Succeeded)
            {
                var missing = instance with
                {
                    State = QuestInstanceState.RecoveryRequired,
                    RecoveryState = QuestRecoveryState.RecoveryRequired,
                    QuestVersion = checked(instance.QuestVersion + 1)
                };
                await _store.SaveRecoveryAsync(missing, "quest.recovery.definition_missing", cancellationToken);
                _authority.Reload(characterId, loaded.Select(value =>
                    value.QuestInstanceId == missing.QuestInstanceId ? missing : value));
                return new QuestOperationResult(
                    QuestResultCode.RecoveryRequired,
                    Guid.Empty,
                    missing.QuestInstanceId,
                    missing.QuestVersion,
                    false,
                    "quest.recovery.definition_missing",
                    missing,
                    null,
                    []);
            }

            if (instance.ObjectiveStates.Any(state =>
                    definition.Value!.ObjectiveDefinitions.All(objective =>
                        objective.ObjectiveDefinitionId != state.ObjectiveDefinitionId)))
            {
                var missing = instance with
                {
                    State = QuestInstanceState.RecoveryRequired,
                    RecoveryState = QuestRecoveryState.RecoveryRequired,
                    QuestVersion = checked(instance.QuestVersion + 1)
                };
                await _store.SaveRecoveryAsync(missing, "quest.recovery.objective_missing", cancellationToken);
                _authority.Reload(characterId, loaded.Select(value =>
                    value.QuestInstanceId == missing.QuestInstanceId ? missing : value));
                return new QuestOperationResult(
                    QuestResultCode.RecoveryRequired,
                    Guid.Empty,
                    missing.QuestInstanceId,
                    missing.QuestVersion,
                    false,
                    "quest.recovery.objective_missing",
                    missing,
                    null,
                    []);
            }
        }

        _authority.Reload(characterId, loaded);
        var recovered = questInstanceId is null
            ? loaded.FirstOrDefault()
            : loaded.FirstOrDefault(value => value.QuestInstanceId == questInstanceId);
        _events.Publish(new QuestEvent(
            Guid.NewGuid(),
            QuestEventKind.QuestRecoveryCompleted,
            null,
            recovered?.QuestInstanceId,
            recovered?.QuestDefinitionId,
            null,
            null,
            null,
            null,
            characterId,
            "Reconciled",
            "",
            DateTimeOffset.UtcNow,
            correlationId,
            0));
        return new QuestOperationResult(
            QuestResultCode.Success,
            Guid.Empty,
            recovered?.QuestInstanceId,
            recovered?.QuestVersion ?? 0,
            false,
            "",
            recovered,
            null,
            []);
    }
}

public sealed class QuestRuntimeInspector : IQuestInspectorSource
{
    private readonly IQuestDefinitionCatalog _catalog;
    private readonly IQuestInstanceAuthority _authority;
    private readonly IQuestRuntimeStore _store;
    private readonly IQuestAuditLedger _audit;
    private readonly QuestFailureInjection _failureInjection;

    public QuestRuntimeInspector(
        IQuestDefinitionCatalog catalog,
        IQuestInstanceAuthority authority,
        IQuestRuntimeStore store,
        IQuestAuditLedger audit,
        QuestFailureInjection? failureInjection = null)
    {
        _catalog = catalog;
        _authority = authority;
        _store = store;
        _audit = audit;
        _failureInjection = failureInjection ?? new QuestFailureInjection();
    }

    public QuestInspectorSnapshot Capture(QuestInspectorQuery query)
    {
        try
        {
            if (_failureInjection.Point == QuestFailurePoint.Inspector)
            {
                throw new InvalidOperationException("Injected quest inspector failure.");
            }

            var allDefinitions = _catalog is ImmutableQuestDefinitionCatalog immutable
                ? immutable.AllSnapshot
                : _catalog.Snapshot;
            IEnumerable<QuestDefinition> definitions = allDefinitions;
            IEnumerable<QuestInstance> instances = _authority.Snapshot;
            IEnumerable<QuestProgressMutationPlan> progress = _store.ProgressPlans;
            IEnumerable<(QuestRewardPlan Plan, QuestRewardResult Result)> rewards = _store.RewardRecords;
            IEnumerable<QuestAuditRecord> audit = _audit.Snapshot;

            if (query.CharacterId is not null)
            {
                instances = instances.Where(value => value.CharacterId == query.CharacterId);
                progress = progress.Where(value => value.CharacterId == query.CharacterId);
                rewards = rewards.Where(value => value.Plan.CharacterId == query.CharacterId);
                audit = audit.Where(value => value.CharacterId == query.CharacterId);
            }

            if (query.QuestDefinitionId is not null)
            {
                definitions = definitions.Where(value => value.QuestDefinitionId == query.QuestDefinitionId);
                instances = instances.Where(value => value.QuestDefinitionId == query.QuestDefinitionId);
                progress = progress.Where(value => value.QuestDefinitionId == query.QuestDefinitionId);
                rewards = rewards.Where(value => value.Plan.QuestDefinitionId == query.QuestDefinitionId);
            }

            if (query.QuestInstanceId is not null)
            {
                instances = instances.Where(value => value.QuestInstanceId == query.QuestInstanceId);
                progress = progress.Where(value => value.QuestInstanceId == query.QuestInstanceId);
                rewards = rewards.Where(value => value.Plan.QuestInstanceId == query.QuestInstanceId);
            }

            if (query.QuestInstanceState is not null)
            {
                instances = instances.Where(value => value.State == query.QuestInstanceState);
            }

            if (query.ActiveOnly)
            {
                instances = instances.Where(value => value.State == QuestInstanceState.Active);
            }

            if (query.ReadyOnly)
            {
                instances = instances.Where(value => value.State == QuestInstanceState.ReadyToComplete);
            }

            if (query.CompletedOnly)
            {
                instances = instances.Where(value => value.State == QuestInstanceState.Completed);
            }

            if (query.AbandonedOnly)
            {
                instances = instances.Where(value => value.State == QuestInstanceState.Abandoned);
            }

            if (query.RecoveryRequiredOnly)
            {
                instances = instances.Where(value =>
                    value.State == QuestInstanceState.RecoveryRequired ||
                    value.RecoveryState == QuestRecoveryState.RecoveryRequired);
            }

            if (query.EvidenceBlockedOnly)
            {
                definitions = definitions.Where(value => value.PolicyStatus == QuestContentStatus.EvidenceBlocked);
            }

            if (query.TestOnlyOnly)
            {
                definitions = definitions.Where(value => value.PolicyStatus == QuestContentStatus.TestOnly);
            }

            var instanceArray = instances
                .OrderBy(value => value.CharacterId)
                .ThenBy(value => value.QuestDefinitionId)
                .ThenBy(value => value.QuestInstanceId)
                .ToArray();
            var instanceIds = instanceArray.Select(value => value.QuestInstanceId).ToHashSet();
            var objectiveItems = instanceArray
                .SelectMany(instance => instance.ObjectiveStates.Select(state => (Instance: instance, State: state)))
                .Where(value => query.ObjectiveType is null || value.State.ObjectiveType == query.ObjectiveType)
                .Where(value => query.ObjectiveState is null || value.State.State == query.ObjectiveState)
                .OrderBy(value => value.Instance.QuestInstanceId)
                .ThenBy(value => value.State.ObjectiveIndex)
                .Select(value => new QuestObjectiveInspectorItem(
                    value.Instance.QuestInstanceId,
                    value.State.ObjectiveDefinitionId,
                    value.State.ObjectiveIndex,
                    value.State.ObjectiveType,
                    value.State.TargetTemplateId,
                    value.State.RequiredCount,
                    value.State.CurrentProgress,
                    value.State.State,
                    value.State.ObjectiveVersion,
                    value.State.LastSemanticEventSafeId ?? "",
                    value.State.PolicyStatus,
                    value.State.UpdatedAtUtc))
                .ToArray();
            var offset = Math.Max(0, query.Offset);
            var limit = Math.Clamp(query.Limit, 1, 500);
            return new QuestInspectorSnapshot(
                QuestRuntimeCollections.Freeze(definitions
                    .OrderBy(value => value.QuestDefinitionId)
                    .Skip(offset)
                    .Take(limit)
                    .Select(ToDefinitionItem)),
                QuestRuntimeCollections.Freeze(instanceArray
                    .Skip(offset)
                    .Take(limit)
                    .Select(ToInstanceItem)),
                QuestRuntimeCollections.Freeze(objectiveItems.Skip(offset).Take(limit)),
                QuestRuntimeCollections.Freeze(progress
                    .Where(value => instanceIds.Count == 0 || instanceIds.Contains(value.QuestInstanceId))
                    .OrderBy(value => value.CreatedAtUtc)
                    .Skip(offset)
                    .Take(limit)
                    .Select(value => new QuestProgressInspectorItem(
                        value.ProgressMutationId,
                        QuestRuntimeSecurity.SafeId(value.SemanticEventId.ToString("N")),
                        value.QuestInstanceId,
                        value.ObjectiveDefinitionId,
                        value.ProgressBefore,
                        value.ProgressDelta,
                        value.ProgressAfter,
                        QuestResultCode.Success,
                        "",
                        value.CreatedAtUtc))),
                QuestRuntimeCollections.Freeze(rewards
                    .OrderBy(value => value.Plan.CreatedAtUtc)
                    .Skip(offset)
                    .Take(limit)
                    .Select(value => new QuestRewardInspectorItem(
                        value.Plan.QuestRewardPlanId,
                        value.Plan.QuestInstanceId,
                        value.Plan.ItemRewards.Count,
                        value.Plan.CurrencyRewards.Count,
                        value.Result.SucceededState(),
                        value.Result.Code,
                        value.Result.FailureCode,
                        QuestRuntimeSecurity.SafeId(value.Plan.IdempotencyKeyHash),
                        value.Plan.CreatedAtUtc,
                        value.Result.Code is QuestResultCode.Success or QuestResultCode.DuplicateCompleted
                            ? value.Plan.CreatedAtUtc
                            : null))),
                QuestRuntimeCollections.Freeze(audit
                    .OrderBy(value => value.CreatedAtUtc)
                    .Skip(offset)
                    .Take(limit)),
                offset,
                limit,
                "",
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new QuestInspectorSnapshot([], [], [], [], [], [], 0, 0, "quest.inspector.failure", DateTimeOffset.UtcNow);
        }
    }

    private static QuestDefinitionInspectorItem ToDefinitionItem(QuestDefinition definition)
    {
        var rewardTypes = new List<QuestRewardType>();
        if (definition.RewardDefinition.IsNoReward)
        {
            rewardTypes.Add(QuestRewardType.NoReward);
        }

        if (definition.RewardDefinition.ItemRewards.Count > 0)
        {
            rewardTypes.Add(QuestRewardType.Item);
        }

        if (definition.RewardDefinition.CurrencyRewards.Count > 0)
        {
            rewardTypes.Add(QuestRewardType.Currency);
        }

        if (definition.RewardDefinition.ExperienceRewardCandidate is not null)
        {
            rewardTypes.Add(QuestRewardType.Experience);
        }

        if (definition.RewardDefinition.SkillRewardCandidate is not null)
        {
            rewardTypes.Add(QuestRewardType.Skill);
        }

        return new QuestDefinitionInspectorItem(
            definition.QuestDefinitionId,
            definition.Name,
            definition.QuestCategory,
            definition.QuestType,
            definition.ObjectiveDefinitions.Count,
            QuestRuntimeCollections.Freeze(rewardTypes),
            definition.NpcBindings.Count(value =>
                value.BindingType is QuestNpcBindingType.Accept or QuestNpcBindingType.AcceptAndTurnIn),
            definition.NpcBindings.Count(value =>
                value.BindingType is QuestNpcBindingType.TurnIn or QuestNpcBindingType.AcceptAndTurnIn),
            definition.RepeatPolicy.Type,
            definition.Enabled,
            definition.ContentVersion,
            definition.PolicyStatus,
            definition.ProtocolStatus);
    }

    private static QuestInstanceInspectorItem ToInstanceItem(QuestInstance instance) =>
        new(
            instance.QuestInstanceId,
            instance.CharacterId,
            instance.QuestDefinitionId,
            instance.State,
            instance.QuestVersion,
            instance.ObjectiveStates.Count,
            instance.ObjectiveStates.Count(value => value.State == QuestObjectiveStateCode.Completed),
            instance.RewardState,
            instance.RepeatIteration,
            instance.RecoveryState,
            instance.AcceptedAtUtc,
            instance.ReadyAtUtc,
            instance.CompletedAtUtc,
            instance.AbandonedAtUtc);
}

public static class QuestRuntimeSecurity
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string SafeId(string value)
    {
        var hash = Hash(value);
        return hash[..16];
    }

    public static string PayloadHash(QuestIntent intent) =>
        Hash(string.Join(
            "|",
            intent.QuestIntentId,
            intent.SessionId,
            intent.CharacterId,
            intent.PlayerRuntimeEntityId,
            intent.QuestDefinitionId,
            intent.SourceNpcRuntimeEntityId,
            intent.SourceNpcTemplateId,
            intent.IntentType,
            intent.ExpectedQuestVersion,
            intent.ExpectedCharacterRuntimeVersion,
            intent.Source,
            intent.CorrelationId));
}

file static class QuestRewardResultExtensions
{
    public static QuestRewardState SucceededState(this QuestRewardResult result) =>
        result.Code is QuestResultCode.Success or QuestResultCode.DuplicateCompleted
            ? QuestRewardState.Committed
            : result.Code == QuestResultCode.RewardEvidenceBlocked
                ? QuestRewardState.EvidenceBlocked
                : QuestRewardState.Failed;
}
