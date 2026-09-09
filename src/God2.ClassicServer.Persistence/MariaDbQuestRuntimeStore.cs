using System.Collections.Concurrent;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbQuestDefinitionRepository :
    MariaDbRuntimeRepository,
    IQuestDefinitionRepository
{
    public MariaDbQuestDefinitionRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<IReadOnlyList<QuestDefinitionRecord>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `Id`, `Code`, COALESCE(`NameZhTw`, `Name`) AS `Name`, `RequiredLevel`, `StartNpcId`, `EndNpcId`
            FROM `quests`
            ORDER BY `Id`;
            """;
        var records = new List<QuestDefinitionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32("Id");
            var requiredLevel = reader.GetInt32("RequiredLevel");
            var startNpcId = reader.IsDBNull(reader.GetOrdinal("StartNpcId"))
                ? (int?)null
                : reader.GetInt32("StartNpcId");
            var endNpcId = reader.IsDBNull(reader.GetOrdinal("EndNpcId"))
                ? (int?)null
                : reader.GetInt32("EndNpcId");
            var raw = JsonSerializer.Serialize(new
            {
                Source = "database.quests",
                RequiredLevelCandidate = requiredLevel,
                StartNpcIdCandidate = startNpcId,
                EndNpcIdCandidate = endNpcId,
                Classification = "LegacyCandidate",
                GameplayPromotion = "EvidenceBlocked"
            });
            records.Add(new QuestDefinitionRecord(
                id,
                reader.GetString("Code"),
                reader.GetString("Name"),
                "",
                QuestCategory.Unknown,
                QuestType.Unknown,
                "EvidenceBlocked",
                QuestCompletionPolicyType.Unknown,
                new QuestRepeatPolicy(
                    QuestRepeatPolicyType.Unknown,
                    null,
                    QuestContentStatus.EvidenceBlocked,
                    raw),
                requiredLevel > 0
                    ?
                    [
                        new QuestPrerequisiteDefinition(
                            $"quest:{id}:required-level-candidate",
                            QuestPrerequisiteType.CharacterLevel,
                            null,
                            null,
                            requiredLevel,
                            QuestContentStatus.EvidenceBlocked,
                            raw)
                    ]
                    : [],
                [
                    new QuestObjectiveDefinition(
                        $"quest:{id}:objective-unknown",
                        id,
                        0,
                        QuestObjectiveType.Unknown,
                        null,
                        "Unknown",
                        0,
                        QuestProgressMode.Unknown,
                        "EvidenceBlocked",
                        true,
                        QuestContentStatus.EvidenceBlocked,
                        $"database-quest-{id}-raw-v1",
                        raw)
                ],
                new QuestRewardDefinition(
                    $"quest:{id}:reward-unknown",
                    [],
                    [],
                    null,
                    null,
                    [],
                    QuestContentStatus.EvidenceBlocked,
                    raw),
                BuildCandidateBindings(id, startNpcId, endNpcId, raw),
                true,
                $"database-quest-{id}-raw-v1",
                QuestContentStatus.EvidenceBlocked,
                QuestContentStatus.EvidenceBlocked,
                raw));
        }

        return records;
    }

    private static IReadOnlyList<QuestNpcBinding> BuildCandidateBindings(
        int questDefinitionId,
        int? startNpcId,
        int? endNpcId,
        string raw)
    {
        var bindings = new List<QuestNpcBinding>();
        if (startNpcId is not null)
        {
            bindings.Add(new QuestNpcBinding(
                questDefinitionId,
                startNpcId.Value,
                QuestNpcBindingType.Accept,
                null,
                "Candidate",
                false,
                QuestContentStatus.EvidenceBlocked,
                raw));
        }

        if (endNpcId is not null)
        {
            bindings.Add(new QuestNpcBinding(
                questDefinitionId,
                endNpcId.Value,
                QuestNpcBindingType.TurnIn,
                null,
                "Candidate",
                false,
                QuestContentStatus.EvidenceBlocked,
                raw));
        }

        return bindings;
    }
}

public sealed class MariaDbQuestRuntimeStore :
    MariaDbRuntimeRepository,
    IQuestRuntimeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<QuestProgressMutationPlan> _progressPlans = [];
    private readonly ConcurrentQueue<(QuestRewardPlan Plan, QuestRewardResult Result)> _rewardRecords = [];

    public MariaDbQuestRuntimeStore(DatabaseOptions options)
        : base(options)
    {
    }

    public IReadOnlyList<QuestProgressMutationPlan> ProgressPlans => _progressPlans.ToArray();

    public IReadOnlyList<(QuestRewardPlan Plan, QuestRewardResult Result)> RewardRecords =>
        _rewardRecords.ToArray();

    public Task<QuestReplayLookup> FindOperationAsync(
        string operation,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken) =>
        FindOperationCoreAsync(operation, idempotencyHash, payloadHash, cancellationToken);

    public async Task<OperationResult> CommitAcceptanceAsync(
        QuestAcceptancePlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertInstanceAsync(connection, transaction, instance, cancellationToken);
            await InsertObjectivesAsync(connection, transaction, instance, cancellationToken);
            await InsertOperationAsync(
                connection,
                transaction,
                "accept",
                idempotencyHash,
                payloadHash,
                result,
                instance.CharacterId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception)
        {
            return Failure("quest.persistence.acceptance_failed", exception);
        }
    }

    public async Task<IReadOnlyList<QuestInstance>> LoadCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InstanceJson`
            FROM `quest_instances`
            WHERE `CharacterId` = @characterId
            ORDER BY `QuestDefinitionId`, `RepeatIteration`, `QuestInstanceId`;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        var instances = new List<QuestInstance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var instance = JsonSerializer.Deserialize<QuestInstance>(
                reader.GetString("InstanceJson"),
                JsonOptions);
            if (instance is not null)
            {
                instances.Add(instance);
            }
        }

        return instances;
    }

    public async Task<QuestInstance?> LoadInstanceAsync(
        Guid questInstanceId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InstanceJson`
            FROM `quest_instances`
            WHERE `QuestInstanceId` = @questInstanceId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@questInstanceId", questInstanceId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : JsonSerializer.Deserialize<QuestInstance>((string)value, JsonOptions);
    }

    public async Task<QuestProgressReplayLookup> FindProgressAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ProgressFingerprintSha256`, `ResultJson`
            FROM `quest_progress_mutations`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new QuestProgressReplayLookup(false, true, null);
        }

        return new QuestProgressReplayLookup(
            true,
            string.Equals(reader.GetString("ProgressFingerprintSha256"), payloadHash, StringComparison.Ordinal),
            JsonSerializer.Deserialize<QuestProgressResult>(reader.GetString("ResultJson"), JsonOptions));
    }

    public async Task<OperationResult> CommitProgressAsync(
        QuestProgressMutationPlan plan,
        QuestInstance instance,
        QuestProgressResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var versionUpdated = await UpdateInstanceAsync(
                connection,
                transaction,
                instance,
                plan.ExpectedQuestVersion,
                cancellationToken);
            if (!versionUpdated)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "quest.persistence.quest_version_conflict",
                    "Quest instance optimistic version check failed.",
                    plan.QuestInstanceId.ToString());
            }

            var objective = instance.ObjectiveStates.First(
                value => value.ObjectiveDefinitionId == plan.ObjectiveDefinitionId);
            var objectiveUpdated = await UpdateObjectiveAsync(
                connection,
                transaction,
                instance.QuestInstanceId,
                objective,
                plan.ExpectedObjectiveVersion,
                cancellationToken);
            if (!objectiveUpdated)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "quest.persistence.objective_version_conflict",
                    "Quest objective optimistic version check failed.",
                    plan.ObjectiveDefinitionId);
            }

            await using var mutation = connection.CreateCommand();
            mutation.Transaction = transaction;
            mutation.CommandTimeout = CommandTimeoutSeconds;
            mutation.CommandText = """
                INSERT INTO `quest_progress_mutations`
                    (`ProgressMutationId`, `SemanticEventId`, `QuestInstanceId`,
                     `ObjectiveDefinitionId`, `CharacterId`, `ProgressBefore`, `ProgressDelta`,
                     `ProgressAfter`, `ExpectedQuestVersion`, `ExpectedObjectiveVersion`,
                     `IdempotencyKeyHash`, `ProgressFingerprintSha256`, `ResultCode`, `FailureCode`,
                     `PlanJson`, `ResultJson`, `CreatedAtUtc`)
                VALUES
                    (@progressMutationId, @semanticEventId, @questInstanceId,
                     @objectiveDefinitionId, @characterId, @progressBefore, @progressDelta,
                     @progressAfter, @expectedQuestVersion, @expectedObjectiveVersion,
                     @idempotencyKeyHash, @payloadHash, @resultCode, @failureCode,
                     @planJson, @resultJson, @createdAtUtc);
                """;
            mutation.Parameters.AddWithValue("@progressMutationId", plan.ProgressMutationId.ToString());
            mutation.Parameters.AddWithValue("@semanticEventId", plan.SemanticEventId.ToString());
            mutation.Parameters.AddWithValue("@questInstanceId", plan.QuestInstanceId.ToString());
            mutation.Parameters.AddWithValue("@objectiveDefinitionId", plan.ObjectiveDefinitionId);
            mutation.Parameters.AddWithValue("@characterId", plan.CharacterId);
            mutation.Parameters.AddWithValue("@progressBefore", plan.ProgressBefore);
            mutation.Parameters.AddWithValue("@progressDelta", plan.ProgressDelta);
            mutation.Parameters.AddWithValue("@progressAfter", plan.ProgressAfter);
            mutation.Parameters.AddWithValue("@expectedQuestVersion", plan.ExpectedQuestVersion);
            mutation.Parameters.AddWithValue("@expectedObjectiveVersion", plan.ExpectedObjectiveVersion);
            mutation.Parameters.AddWithValue("@idempotencyKeyHash", idempotencyHash);
            mutation.Parameters.AddWithValue("@payloadHash", payloadHash);
            mutation.Parameters.AddWithValue("@resultCode", result.Code.ToString());
            mutation.Parameters.AddWithValue("@failureCode", result.FailureCode);
            mutation.Parameters.AddWithValue("@planJson", JsonSerializer.Serialize(plan, JsonOptions));
            mutation.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
            mutation.Parameters.AddWithValue("@createdAtUtc", plan.CreatedAtUtc.UtcDateTime);
            await mutation.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _progressPlans.Enqueue(plan);
            return OperationResult.Success;
        }
        catch (MySqlException exception)
        {
            return Failure("quest.persistence.progress_failed", exception);
        }
    }

    public Task<OperationResult> CommitAbandonmentAsync(
        QuestAbandonPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken) =>
        CommitTerminalOperationAsync(
            "abandon",
            instance,
            result,
            plan.ExpectedQuestVersion,
            idempotencyHash,
            payloadHash,
            cancellationToken);

    public async Task<QuestRewardReplayLookup> FindRewardAsync(
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `RewardFingerprintSha256`, `ResultJson`
            FROM `quest_reward_finalization`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new QuestRewardReplayLookup(false, true, null);
        }

        return new QuestRewardReplayLookup(
            true,
            string.Equals(reader.GetString("RewardFingerprintSha256"), payloadHash, StringComparison.Ordinal),
            JsonSerializer.Deserialize<QuestRewardResult>(reader.GetString("ResultJson"), JsonOptions));
    }

    public async Task<OperationResult> CommitRewardAsync(
        QuestRewardPlan plan,
        QuestRewardResult result,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `quest_reward_finalization`
                    (`QuestRewardPlanId`, `CompletionPlanId`, `QuestInstanceId`, `CharacterId`,
                     `IdempotencyKeyHash`, `RewardFingerprintSha256`, `State`, `ResultCode`, `FailureCode`,
                     `RewardJson`, `ResultJson`, `CreatedAtUtc`, `CompletedAtUtc`)
                VALUES
                    (@questRewardPlanId, @completionPlanId, @questInstanceId, @characterId,
                     @idempotencyKeyHash, @payloadHash, @state, @resultCode, @failureCode,
                     @rewardJson, @resultJson, @createdAtUtc, @completedAtUtc);
                """;
            command.Parameters.AddWithValue("@questRewardPlanId", plan.QuestRewardPlanId.ToString());
            command.Parameters.AddWithValue("@completionPlanId", plan.CompletionPlanId.ToString());
            command.Parameters.AddWithValue("@questInstanceId", plan.QuestInstanceId.ToString());
            command.Parameters.AddWithValue("@characterId", plan.CharacterId);
            command.Parameters.AddWithValue("@idempotencyKeyHash", plan.IdempotencyKeyHash);
            command.Parameters.AddWithValue("@payloadHash", payloadHash);
            command.Parameters.AddWithValue("@state", result.Code is QuestResultCode.Success or QuestResultCode.DuplicateCompleted ? "Committed" : "Failed");
            command.Parameters.AddWithValue("@resultCode", result.Code.ToString());
            command.Parameters.AddWithValue("@failureCode", result.FailureCode);
            command.Parameters.AddWithValue("@rewardJson", JsonSerializer.Serialize(plan, JsonOptions));
            command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
            command.Parameters.AddWithValue("@createdAtUtc", plan.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _rewardRecords.Enqueue((plan, result));
            return OperationResult.Success;
        }
        catch (MySqlException exception)
        {
            return Failure("quest.persistence.reward_failed", exception);
        }
    }

    public Task<OperationResult> CommitCompletionAsync(
        QuestCompletionPlan plan,
        QuestInstance instance,
        QuestOperationResult result,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken) =>
        CommitTerminalOperationAsync(
            "turnin",
            instance,
            result,
            plan.ExpectedQuestVersion,
            idempotencyHash,
            payloadHash,
            cancellationToken);

    public async Task<OperationResult> SaveRecoveryAsync(
        QuestInstance instance,
        string failureCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await UpsertInstanceWithoutVersionAsync(connection, transaction, instance, cancellationToken);
            await using var recovery = connection.CreateCommand();
            recovery.Transaction = transaction;
            recovery.CommandTimeout = CommandTimeoutSeconds;
            recovery.CommandText = """
                INSERT INTO `quest_recovery_state`
                    (`RecoveryId`, `QuestInstanceId`, `CharacterId`, `RecoveryState`,
                     `FailureCode`, `RecoveryJson`, `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`)
                VALUES
                    (@recoveryId, @questInstanceId, @characterId, @recoveryState,
                     @failureCode, @recoveryJson, @createdAtUtc, @updatedAtUtc, NULL)
                ON DUPLICATE KEY UPDATE
                    `RecoveryState` = VALUES(`RecoveryState`),
                    `FailureCode` = VALUES(`FailureCode`),
                    `RecoveryJson` = VALUES(`RecoveryJson`),
                    `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
                """;
            recovery.Parameters.AddWithValue("@recoveryId", Guid.NewGuid().ToString());
            recovery.Parameters.AddWithValue("@questInstanceId", instance.QuestInstanceId.ToString());
            recovery.Parameters.AddWithValue("@characterId", instance.CharacterId);
            recovery.Parameters.AddWithValue("@recoveryState", instance.RecoveryState.ToString());
            recovery.Parameters.AddWithValue("@failureCode", failureCode);
            recovery.Parameters.AddWithValue("@recoveryJson", JsonSerializer.Serialize(instance, JsonOptions));
            recovery.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow);
            recovery.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow);
            await recovery.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception)
        {
            return Failure("quest.persistence.recovery_failed", exception);
        }
    }

    private async Task<QuestReplayLookup> FindOperationCoreAsync(
        string operation,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `OperationFingerprintSha256`, `ResultJson`
            FROM `quest_operation_idempotency`
            WHERE `Operation` = @operation
              AND `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@operation", operation);
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencyHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new QuestReplayLookup(false, true, null);
        }

        return new QuestReplayLookup(
            true,
            string.Equals(reader.GetString("OperationFingerprintSha256"), payloadHash, StringComparison.Ordinal),
            JsonSerializer.Deserialize<QuestOperationResult>(reader.GetString("ResultJson"), JsonOptions));
    }

    private async Task<OperationResult> CommitTerminalOperationAsync(
        string operation,
        QuestInstance instance,
        QuestOperationResult result,
        long expectedVersion,
        string idempotencyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var updated = await UpdateInstanceAsync(
                connection,
                transaction,
                instance,
                expectedVersion,
                cancellationToken);
            if (!updated)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "quest.persistence.quest_version_conflict",
                    "Quest instance optimistic version check failed.",
                    instance.QuestInstanceId.ToString());
            }

            await InsertOperationAsync(
                connection,
                transaction,
                operation,
                idempotencyHash,
                payloadHash,
                result,
                instance.CharacterId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception)
        {
            return Failure($"quest.persistence.{operation}_failed", exception);
        }
    }

    private static async Task InsertInstanceAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestInstance instance,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `quest_instances`
                (`QuestInstanceId`, `CharacterId`, `QuestDefinitionId`, `State`, `QuestVersion`,
                 `DefinitionContentVersion`, `RepeatIteration`, `RewardState`, `RecoveryState`,
                 `InstanceJson`, `AcceptedAtUtc`, `ReadyAtUtc`, `CompletedAtUtc`, `AbandonedAtUtc`,
                 `LastProgressAtUtc`, `UpdatedAtUtc`)
            VALUES
                (@questInstanceId, @characterId, @questDefinitionId, @state, @questVersion,
                 @definitionContentVersion, @repeatIteration, @rewardState, @recoveryState,
                 @instanceJson, @acceptedAtUtc, @readyAtUtc, @completedAtUtc, @abandonedAtUtc,
                 @lastProgressAtUtc, @updatedAtUtc);
            """;
        AddInstanceParameters(command, instance);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> UpdateInstanceAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestInstance instance,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `quest_instances`
            SET `State` = @state,
                `QuestVersion` = @questVersion,
                `RewardState` = @rewardState,
                `RecoveryState` = @recoveryState,
                `InstanceJson` = @instanceJson,
                `ReadyAtUtc` = @readyAtUtc,
                `CompletedAtUtc` = @completedAtUtc,
                `AbandonedAtUtc` = @abandonedAtUtc,
                `LastProgressAtUtc` = @lastProgressAtUtc,
                `UpdatedAtUtc` = @updatedAtUtc
            WHERE `QuestInstanceId` = @questInstanceId
              AND `QuestVersion` = @expectedVersion;
            """;
        AddInstanceParameters(command, instance);
        command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task UpsertInstanceWithoutVersionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestInstance instance,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `quest_instances`
            SET `State` = @state,
                `QuestVersion` = @questVersion,
                `RewardState` = @rewardState,
                `RecoveryState` = @recoveryState,
                `InstanceJson` = @instanceJson,
                `ReadyAtUtc` = @readyAtUtc,
                `CompletedAtUtc` = @completedAtUtc,
                `AbandonedAtUtc` = @abandonedAtUtc,
                `LastProgressAtUtc` = @lastProgressAtUtc,
                `UpdatedAtUtc` = @updatedAtUtc
            WHERE `QuestInstanceId` = @questInstanceId;
            """;
        AddInstanceParameters(command, instance);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertObjectivesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        QuestInstance instance,
        CancellationToken cancellationToken)
    {
        foreach (var objective in instance.ObjectiveStates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO `quest_objective_states`
                    (`QuestInstanceId`, `ObjectiveDefinitionId`, `ObjectiveIndex`, `ObjectiveType`,
                     `TargetTemplateId`, `RequiredCount`, `CurrentProgress`, `State`,
                     `ObjectiveVersion`, `LastSemanticEventSafeId`, `PolicyStatus`, `UpdatedAtUtc`)
                VALUES
                    (@questInstanceId, @objectiveDefinitionId, @objectiveIndex, @objectiveType,
                     @targetTemplateId, @requiredCount, @currentProgress, @state,
                     @objectiveVersion, @lastSemanticEventSafeId, @policyStatus, @updatedAtUtc);
                """;
            AddObjectiveParameters(command, instance.QuestInstanceId, objective);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> UpdateObjectiveAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid questInstanceId,
        QuestObjectiveState objective,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `quest_objective_states`
            SET `CurrentProgress` = @currentProgress,
                `State` = @state,
                `ObjectiveVersion` = @objectiveVersion,
                `LastSemanticEventSafeId` = @lastSemanticEventSafeId,
                `UpdatedAtUtc` = @updatedAtUtc
            WHERE `QuestInstanceId` = @questInstanceId
              AND `ObjectiveDefinitionId` = @objectiveDefinitionId
              AND `ObjectiveVersion` = @expectedVersion;
            """;
        AddObjectiveParameters(command, questInstanceId, objective);
        command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task InsertOperationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string operation,
        string idempotencyHash,
        string payloadHash,
        QuestOperationResult result,
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `quest_operation_idempotency`
                (`Operation`, `IdempotencyKeyHash`, `OperationFingerprintSha256`, `QuestIntentId`,
                 `QuestInstanceId`, `CharacterId`, `ResultCode`, `ResultJson`,
                 `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@operation, @idempotencyKeyHash, @payloadHash, @questIntentId,
                 @questInstanceId, @characterId, @resultCode, @resultJson,
                 @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@operation", operation);
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotencyHash);
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@questIntentId", result.QuestIntentId.ToString());
        command.Parameters.AddWithValue("@questInstanceId", (object?)result.QuestInstanceId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@resultCode", result.Code.ToString());
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddInstanceParameters(
        MySqlCommand command,
        QuestInstance instance)
    {
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.AddWithValue("@questInstanceId", instance.QuestInstanceId.ToString());
        command.Parameters.AddWithValue("@characterId", instance.CharacterId);
        command.Parameters.AddWithValue("@questDefinitionId", instance.QuestDefinitionId);
        command.Parameters.AddWithValue("@state", instance.State.ToString());
        command.Parameters.AddWithValue("@questVersion", instance.QuestVersion);
        command.Parameters.AddWithValue("@definitionContentVersion", instance.DefinitionContentVersion);
        command.Parameters.AddWithValue("@repeatIteration", instance.RepeatIteration);
        command.Parameters.AddWithValue("@rewardState", instance.RewardState.ToString());
        command.Parameters.AddWithValue("@recoveryState", instance.RecoveryState.ToString());
        command.Parameters.AddWithValue("@instanceJson", JsonSerializer.Serialize(instance, JsonOptions));
        command.Parameters.AddWithValue("@acceptedAtUtc", instance.AcceptedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@readyAtUtc", DbValue(instance.ReadyAtUtc));
        command.Parameters.AddWithValue("@completedAtUtc", DbValue(instance.CompletedAtUtc));
        command.Parameters.AddWithValue("@abandonedAtUtc", DbValue(instance.AbandonedAtUtc));
        command.Parameters.AddWithValue("@lastProgressAtUtc", DbValue(instance.LastProgressAtUtc));
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow);
    }

    private static void AddObjectiveParameters(
        MySqlCommand command,
        Guid questInstanceId,
        QuestObjectiveState objective)
    {
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.AddWithValue("@questInstanceId", questInstanceId.ToString());
        command.Parameters.AddWithValue("@objectiveDefinitionId", objective.ObjectiveDefinitionId);
        command.Parameters.AddWithValue("@objectiveIndex", objective.ObjectiveIndex);
        command.Parameters.AddWithValue("@objectiveType", objective.ObjectiveType.ToString());
        command.Parameters.AddWithValue("@targetTemplateId", (object?)objective.TargetTemplateId ?? DBNull.Value);
        command.Parameters.AddWithValue("@requiredCount", objective.RequiredCount);
        command.Parameters.AddWithValue("@currentProgress", objective.CurrentProgress);
        command.Parameters.AddWithValue("@state", objective.State.ToString());
        command.Parameters.AddWithValue("@objectiveVersion", objective.ObjectiveVersion);
        command.Parameters.AddWithValue("@lastSemanticEventSafeId", (object?)objective.LastSemanticEventSafeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@policyStatus", objective.PolicyStatus.ToString());
        command.Parameters.AddWithValue("@updatedAtUtc", objective.UpdatedAtUtc.UtcDateTime);
    }

    private static object DbValue(DateTimeOffset? value) =>
        value is null ? DBNull.Value : value.Value.UtcDateTime;

    private static OperationResult Failure(string code, MySqlException exception) =>
        OperationResult.Failure(code, exception.Message, "MariaDbQuestRuntimeStore");
}
