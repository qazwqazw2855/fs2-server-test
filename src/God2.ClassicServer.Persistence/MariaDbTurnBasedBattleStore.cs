using System.Collections.Concurrent;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbTurnBasedBattleStore :
    MariaDbRuntimeRepository,
    IBattleInstanceRepository,
    IBattleActionSubmissionStore,
    IBattleIdempotencyStore,
    IBattleRuntimeReadModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, BattleInstance> _cache = [];

    public MariaDbTurnBasedBattleStore(DatabaseOptions options)
        : base(options)
    {
    }

    public IReadOnlyList<BattleInstance> Snapshot => _cache.Values
        .OrderByDescending(value => value.UpdatedAtUtc)
        .ToArray();

    public async Task<BattleInstance?> GetAsync(
        Guid battleInstanceId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `AggregateJson`
            FROM `battle_instances`
            WHERE `BattleInstanceId` = @battleInstanceId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var battle = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<BattleInstance>((string)scalar, JsonOptions);
        if (battle is not null)
        {
            _cache[battle.BattleInstanceId] = battle;
        }

        return battle;
    }

    public async Task<BattleInstance?> FindActiveByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT b.`AggregateJson`
            FROM `battle_instances` b
            INNER JOIN `battle_participants` p
                ON p.`BattleInstanceId` = b.`BattleInstanceId`
            WHERE p.`CharacterId` = @characterId
              AND b.`State` NOT IN ('Completed', 'Aborted', 'Closed')
            ORDER BY b.`UpdatedAtUtc` DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var battle = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<BattleInstance>((string)scalar, JsonOptions);
        if (battle is not null)
        {
            _cache[battle.BattleInstanceId] = battle;
        }

        return battle;
    }

    public async Task<BattleInstance?> FindByRequestAsync(
        Guid battleRequestId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `AggregateJson`
            FROM `battle_instances`
            WHERE `BattleRequestId` = @battleRequestId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleRequestId", battleRequestId.ToString());
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var battle = scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<BattleInstance>((string)scalar, JsonOptions);
        if (battle is not null)
        {
            _cache[battle.BattleInstanceId] = battle;
        }

        return battle;
    }

    public async Task<OperationResult> CreateAsync(
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                    INSERT INTO `battle_instances`
                        (`BattleInstanceId`, `BattleRequestId`, `WorldInstanceId`, `SourceMapId`,
                         `EncounterDefinitionId`, `State`, `Phase`, `CurrentRoundNumber`,
                         `CurrentResolutionIndex`, `BattleVersion`, `WinnerSide`, `CompletionReason`,
                         `RewardState`, `RecoveryState`, `AggregateJson`, `CreatedAtUtc`,
                         `UpdatedAtUtc`, `CompletedAtUtc`)
                    VALUES
                        (@battleInstanceId, @battleRequestId, @worldInstanceId, @sourceMapId,
                         @encounterDefinitionId, @state, @phase, @currentRoundNumber,
                         @currentResolutionIndex, @battleVersion, @winnerSide, @completionReason,
                         @rewardState, @recoveryState, @aggregateJson, @createdAtUtc,
                         @updatedAtUtc, @completedAtUtc);
                    """;
                AddBattleParameters(command, battle);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await SyncDetailsAsync(connection, transaction, battle, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _cache[battle.BattleInstanceId] = battle;
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure("battle.duplicate", "Battle instance already exists.");
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure("battle.persistence_failure", exception.Message);
        }
    }

    public async Task<OperationResult> SaveAsync(
        BattleInstance battle,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                UPDATE `battle_instances`
                SET `State` = @state,
                    `Phase` = @phase,
                    `CurrentRoundNumber` = @currentRoundNumber,
                    `CurrentResolutionIndex` = @currentResolutionIndex,
                    `BattleVersion` = @battleVersion,
                    `WinnerSide` = @winnerSide,
                    `CompletionReason` = @completionReason,
                    `RewardState` = @rewardState,
                    `RecoveryState` = @recoveryState,
                    `AggregateJson` = @aggregateJson,
                    `UpdatedAtUtc` = @updatedAtUtc,
                    `CompletedAtUtc` = @completedAtUtc
                WHERE `BattleInstanceId` = @battleInstanceId
                  AND `BattleVersion` = @expectedVersion;
                """;
            AddBattleParameters(command, battle);
            command.Parameters.AddWithValue("@expectedVersion", expectedVersion);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "battle.version_conflict",
                    "Persisted battle version changed before commit.");
            }

            await SyncDetailsAsync(connection, transaction, battle, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _cache[battle.BattleInstanceId] = battle;
            return OperationResult.Success;
        }
        catch (Exception exception) when (
            exception is MySqlException or InvalidOperationException or TimeoutException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure("battle.persistence_failure", exception.Message);
        }
    }

    public async Task<BattleActionReplay> FindAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var record = await FindIdempotencyAsync(
            "Action",
            idempotencyKey,
            cancellationToken);
        if (record is null)
        {
            return new BattleActionReplay(false, true, null);
        }

        return new BattleActionReplay(
            true,
            string.Equals(record.Value.PayloadHash, payloadHash, StringComparison.Ordinal),
            JsonSerializer.Deserialize<BattleActionSubmissionResult>(record.Value.ResultJson, JsonOptions));
    }

    public Task SaveAsync(
        string idempotencyKey,
        string payloadHash,
        BattleActionSubmissionResult result,
        CancellationToken cancellationToken) =>
        SaveIdempotencyAsync(
            "Action",
            idempotencyKey,
            payloadHash,
            JsonSerializer.Serialize(result, JsonOptions),
            result.BattleInstanceId,
            cancellationToken);

    public async Task<BattleCreationReplay> FindCreationAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var record = await FindIdempotencyAsync(
            "Creation",
            idempotencyKey,
            cancellationToken);
        if (record is null)
        {
            return new BattleCreationReplay(false, true, null);
        }

        return new BattleCreationReplay(
            true,
            string.Equals(record.Value.PayloadHash, payloadHash, StringComparison.Ordinal),
            JsonSerializer.Deserialize<BattleCreationResult>(record.Value.ResultJson, JsonOptions));
    }

    public Task SaveCreationAsync(
        string idempotencyKey,
        string payloadHash,
        BattleCreationResult result,
        CancellationToken cancellationToken) =>
        SaveIdempotencyAsync(
            "Creation",
            idempotencyKey,
            payloadHash,
            JsonSerializer.Serialize(result, JsonOptions),
            result.BattleInstanceId,
            cancellationToken);

    private async Task<(string PayloadHash, string ResultJson)?> FindIdempotencyAsync(
        string scope,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `OperationFingerprintSha256`, `ResultJson`
            FROM `battle_idempotency`
            WHERE `Scope` = @scope
              AND `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@scope", scope);
        command.Parameters.AddWithValue("@idempotencyKeyHash", BattleRuntimeHash.PersistenceKey(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString("OperationFingerprintSha256"), reader.GetString("ResultJson"))
            : null;
    }

    private async Task SaveIdempotencyAsync(
        string scope,
        string idempotencyKey,
        string payloadHash,
        string resultJson,
        Guid? battleInstanceId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_idempotency`
                (`Scope`, `IdempotencyKeyHash`, `OperationFingerprintSha256`, `BattleInstanceId`,
                 `ResultJson`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@scope, @idempotencyKeyHash, @payloadHash, @battleInstanceId,
                 @resultJson, @createdAtUtc, @completedAtUtc)
            ON DUPLICATE KEY UPDATE
                `ResultJson` = IF(`OperationFingerprintSha256` = VALUES(`OperationFingerprintSha256`), VALUES(`ResultJson`), `ResultJson`),
                `CompletedAtUtc` = IF(`OperationFingerprintSha256` = VALUES(`OperationFingerprintSha256`), VALUES(`CompletedAtUtc`), `CompletedAtUtc`);
            """;
        command.Parameters.AddWithValue("@scope", scope);
        command.Parameters.AddWithValue("@idempotencyKeyHash", BattleRuntimeHash.PersistenceKey(idempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId?.ToString());
        command.Parameters.AddWithValue("@resultJson", resultJson);
        command.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SyncDetailsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        await DeleteDetailsAsync(connection, transaction, battle.BattleInstanceId, cancellationToken);
        foreach (var participant in battle.Participants)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `battle_participants`
                    (`BattleInstanceId`, `ParticipantId`, `ParticipantType`, `Side`, `FormationSlot`,
                     `CharacterId`, `MonsterTemplateId`, `SourceRuntimeEntityId`, `BattleRuntimeEntityId`,
                     `SessionSafeId`, `CurrentHp`, `IsAlive`, `IsConnected`, `RuntimeVersion`,
                     `ParticipantJson`, `UpdatedAtUtc`)
                VALUES
                    (@battleInstanceId, @participantId, @participantType, @side, @formationSlot,
                     @characterId, @monsterTemplateId, @sourceRuntimeEntityId, @battleRuntimeEntityId,
                     @sessionSafeId, @currentHp, @isAlive, @isConnected, @runtimeVersion,
                     @participantJson, @updatedAtUtc);
                """;
            command.Parameters.AddWithValue("@battleInstanceId", battle.BattleInstanceId.ToString());
            command.Parameters.AddWithValue("@participantId", participant.ParticipantId.ToString());
            command.Parameters.AddWithValue("@participantType", participant.ParticipantType.ToString());
            command.Parameters.AddWithValue("@side", participant.Side.ToString());
            command.Parameters.AddWithValue("@formationSlot", participant.FormationSlot);
            command.Parameters.AddWithValue("@characterId", participant.CharacterId);
            command.Parameters.AddWithValue("@monsterTemplateId", participant.MonsterTemplateId);
            command.Parameters.AddWithValue("@sourceRuntimeEntityId", participant.SourceRuntimeEntityId);
            command.Parameters.AddWithValue("@battleRuntimeEntityId", participant.BattleRuntimeEntityId);
            command.Parameters.AddWithValue("@sessionSafeId", BattleRuntimeHash.SessionSafeId(participant.SessionId));
            command.Parameters.AddWithValue("@currentHp", participant.CurrentHp);
            command.Parameters.AddWithValue("@isAlive", participant.IsAlive);
            command.Parameters.AddWithValue("@isConnected", participant.IsConnected);
            command.Parameters.AddWithValue("@runtimeVersion", participant.RuntimeVersion);
            command.Parameters.AddWithValue("@participantJson", JsonSerializer.Serialize(participant, JsonOptions));
            command.Parameters.AddWithValue("@updatedAtUtc", battle.UpdatedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var rounds = battle.CompletedRounds
            .Append(battle.CurrentRound)
            .DistinctBy(value => value.RoundNumber);
        foreach (var round in rounds)
        {
            await InsertRoundAsync(connection, transaction, battle, round, cancellationToken);
        }

        await UpsertCompletionAsync(connection, transaction, battle, cancellationToken);
    }

    private async Task InsertRoundAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        BattleInstance battle,
        BattleRound round,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `battle_rounds`
                    (`BattleInstanceId`, `RoundNumber`, `State`, `CurrentResolutionIndex`,
                     `RoundVersion`, `RoundJson`, `StartedAtUtc`, `CompletedAtUtc`)
                VALUES
                    (@battleInstanceId, @roundNumber, @state, @currentResolutionIndex,
                     @roundVersion, @roundJson, @startedAtUtc, @completedAtUtc);
                """;
            command.Parameters.AddWithValue("@battleInstanceId", battle.BattleInstanceId.ToString());
            command.Parameters.AddWithValue("@roundNumber", round.RoundNumber);
            command.Parameters.AddWithValue("@state", round.State.ToString());
            command.Parameters.AddWithValue("@currentResolutionIndex", round.CurrentResolutionIndex);
            command.Parameters.AddWithValue("@roundVersion", round.RoundVersion);
            command.Parameters.AddWithValue("@roundJson", JsonSerializer.Serialize(round, JsonOptions));
            command.Parameters.AddWithValue("@startedAtUtc", round.StartedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@completedAtUtc", round.CompletedAtUtc?.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var action in round.SubmittedActions)
        {
            var result = round.ResolutionResults.FirstOrDefault(value => value.ActionId == action.ActionId);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `battle_actions`
                    (`BattleInstanceId`, `RoundNumber`, `ActionId`, `ParticipantId`, `ActionType`,
                     `State`, `ResolutionIndex`, `ActionFingerprintSha256`, `ActionJson`, `ResultJson`,
                     `SubmittedAtUtc`, `ResolvedAtUtc`)
                VALUES
                    (@battleInstanceId, @roundNumber, @actionId, @participantId, @actionType,
                     @state, @resolutionIndex, @payloadHash, @actionJson, @resultJson,
                     @submittedAtUtc, @resolvedAtUtc);
                """;
            command.Parameters.AddWithValue("@battleInstanceId", battle.BattleInstanceId.ToString());
            command.Parameters.AddWithValue("@roundNumber", round.RoundNumber);
            command.Parameters.AddWithValue("@actionId", action.ActionId.ToString());
            command.Parameters.AddWithValue("@participantId", action.ParticipantId.ToString());
            command.Parameters.AddWithValue("@actionType", action.ActionType.ToString());
            command.Parameters.AddWithValue("@state", result is null ? action.State.ToString() : BattleActionState.Resolved.ToString());
            command.Parameters.AddWithValue("@resolutionIndex", result?.ResolutionIndex);
            command.Parameters.AddWithValue("@payloadHash", action.PayloadHash);
            command.Parameters.AddWithValue("@actionJson", JsonSerializer.Serialize(action, JsonOptions));
            command.Parameters.AddWithValue(
                "@resultJson",
                result is null ? null : JsonSerializer.Serialize(result, JsonOptions));
            command.Parameters.AddWithValue("@submittedAtUtc", action.SubmittedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@resolvedAtUtc", result?.ResolvedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task UpsertCompletionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        BattleInstance battle,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_completion`
                (`BattleInstanceId`, `WinnerSide`, `CompletionReason`, `RewardState`,
                 `RecoveryState`, `BattleVersion`, `CompletedAtUtc`, `UpdatedAtUtc`)
            VALUES
                (@battleInstanceId, @winnerSide, @completionReason, @rewardState,
                 @recoveryState, @battleVersion, @completedAtUtc, @updatedAtUtc)
            ON DUPLICATE KEY UPDATE
                `WinnerSide` = VALUES(`WinnerSide`),
                `CompletionReason` = VALUES(`CompletionReason`),
                `RewardState` = VALUES(`RewardState`),
                `RecoveryState` = VALUES(`RecoveryState`),
                `BattleVersion` = VALUES(`BattleVersion`),
                `CompletedAtUtc` = VALUES(`CompletedAtUtc`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
            """;
        command.Parameters.AddWithValue("@battleInstanceId", battle.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@winnerSide", battle.WinnerSide.ToString());
        command.Parameters.AddWithValue("@completionReason", battle.CompletionReason.ToString());
        command.Parameters.AddWithValue("@rewardState", battle.RewardState.ToString());
        command.Parameters.AddWithValue("@recoveryState", battle.RecoveryState.ToString());
        command.Parameters.AddWithValue("@battleVersion", battle.BattleVersion);
        command.Parameters.AddWithValue("@completedAtUtc", battle.CompletedAtUtc?.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", battle.UpdatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteDetailsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid battleInstanceId,
        CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "battle_actions", "battle_rounds", "battle_participants" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"DELETE FROM `{table}` WHERE `BattleInstanceId` = @battleInstanceId;";
            command.Parameters.AddWithValue("@battleInstanceId", battleInstanceId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddBattleParameters(MySqlCommand command, BattleInstance battle)
    {
        command.Parameters.AddWithValue("@battleInstanceId", battle.BattleInstanceId.ToString());
        command.Parameters.AddWithValue("@battleRequestId", battle.BattleRequestId.ToString());
        command.Parameters.AddWithValue("@worldInstanceId", battle.WorldInstanceId);
        command.Parameters.AddWithValue("@sourceMapId", battle.SourceMapId);
        command.Parameters.AddWithValue("@encounterDefinitionId", battle.EncounterDefinitionId);
        command.Parameters.AddWithValue("@state", battle.State.ToString());
        command.Parameters.AddWithValue("@phase", battle.CurrentPhase.ToString());
        command.Parameters.AddWithValue("@currentRoundNumber", battle.CurrentRoundNumber);
        command.Parameters.AddWithValue("@currentResolutionIndex", battle.CurrentResolutionIndex);
        command.Parameters.AddWithValue("@battleVersion", battle.BattleVersion);
        command.Parameters.AddWithValue("@winnerSide", battle.WinnerSide.ToString());
        command.Parameters.AddWithValue("@completionReason", battle.CompletionReason.ToString());
        command.Parameters.AddWithValue("@rewardState", battle.RewardState.ToString());
        command.Parameters.AddWithValue("@recoveryState", battle.RecoveryState.ToString());
        command.Parameters.AddWithValue("@aggregateJson", JsonSerializer.Serialize(battle, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", battle.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", battle.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", battle.CompletedAtUtc?.UtcDateTime);
    }
}
