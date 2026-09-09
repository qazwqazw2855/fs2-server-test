using System.Text.Json;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbBattleArchitectureV2Store :
    MariaDbRuntimeRepository,
    IBattleActorDurabilityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MariaDbBattleArchitectureV2Store(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<(BattleActorResultCode Code, BattleJournalEntry? Entry)> AppendAsync(
        BattleJournalEntry entry,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await LoadJournalEntryAsync(
                connection,
                transaction,
                entry.BattleId,
                entry.JournalSequence,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (
                    string.Equals(existing.PayloadHash, entry.PayloadHash, StringComparison.Ordinal)
                        ? BattleActorResultCode.DuplicateCompleted
                        : BattleActorResultCode.ReplayConflict,
                    existing);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `battle_command_journal`
                    (`BattleInstanceId`, `JournalSequence`, `EntryType`, `BattleVersion`,
                     `RoundNumber`, `CommandWindowVersion`, `PlanId`, `ActionExecutionId`,
                     `ResultReference`, `SafeIdempotencyHash`, `CommandSchemaVersion`,
                     `CanonicalCommandJson`, `CommandFingerprintSha256`, `CorrelationSafeId`, `CreatedAtUtc`)
                VALUES
                    (@battleId, @sequence, @entryType, @battleVersion,
                     @roundNumber, @commandWindowVersion, @planId, @actionExecutionId,
                     @resultReference, @safeIdempotencyHash, @payloadVersion,
                     @canonicalPayload, @payloadHash, @correlationSafeId, @createdAtUtc);
                """;
            command.Parameters.AddWithValue("@battleId", entry.BattleId.ToString());
            command.Parameters.AddWithValue("@sequence", entry.JournalSequence);
            command.Parameters.AddWithValue("@entryType", entry.EntryType.ToString());
            command.Parameters.AddWithValue("@battleVersion", entry.BattleVersion);
            command.Parameters.AddWithValue("@roundNumber", entry.RoundNumber);
            command.Parameters.AddWithValue("@commandWindowVersion", entry.CommandWindowVersion);
            command.Parameters.AddWithValue("@planId", (object?)entry.PlanId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("@actionExecutionId", (object?)entry.ActionExecutionId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("@resultReference", entry.ResultReference);
            command.Parameters.AddWithValue("@safeIdempotencyHash", entry.SafeIdempotencyIdentifier);
            command.Parameters.AddWithValue("@payloadVersion", entry.PayloadVersion);
            command.Parameters.AddWithValue("@canonicalPayload", entry.CanonicalPayload);
            command.Parameters.AddWithValue("@payloadHash", entry.PayloadHash);
            command.Parameters.AddWithValue("@correlationSafeId", BattleArchitectureV2Hash.SafeId(entry.CorrelationId));
            command.Parameters.AddWithValue("@createdAtUtc", entry.CreatedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (BattleActorResultCode.Success, entry);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            var existing = await ReadJournalEntryAsync(
                entry.BattleId,
                entry.JournalSequence,
                cancellationToken);
            return (
                existing is not null &&
                string.Equals(existing.PayloadHash, entry.PayloadHash, StringComparison.Ordinal)
                    ? BattleActorResultCode.DuplicateCompleted
                    : BattleActorResultCode.ReplayConflict,
                existing);
        }
    }

    public async Task<IReadOnlyList<BattleJournalEntry>> ReadAfterAsync(
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `JournalSequence`, `EntryType`, `BattleVersion`, `RoundNumber`,
                   `CommandWindowVersion`, `PlanId`, `ActionExecutionId`, `ResultReference`,
                   `SafeIdempotencyHash`, `CommandSchemaVersion`, `CanonicalCommandJson`, `CommandFingerprintSha256`,
                   `CreatedAtUtc`
            FROM `battle_command_journal`
            WHERE `BattleInstanceId` = @battleId
              AND `JournalSequence` > @sequence
            ORDER BY `JournalSequence`;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@sequence", sequence);
        List<BattleJournalEntry> entries = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadJournalEntry(reader, battleId));
        }

        return entries.AsReadOnly();
    }

    public async Task<BattleActorResultCode> SaveCheckpointAsync(
        BattleRoundCheckpoint checkpoint,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandTimeout = CommandTimeoutSeconds;
                versionCommand.CommandText = """
                    SELECT COALESCE(MAX(`CheckpointVersion`), 0)
                    FROM `battle_round_checkpoints`
                    WHERE `BattleInstanceId` = @battleId
                    FOR UPDATE;
                    """;
                versionCommand.Parameters.AddWithValue("@battleId", checkpoint.BattleId.ToString());
                var current = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken));
                if (current != expectedVersion)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return BattleActorResultCode.VersionConflict;
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                    INSERT INTO `battle_round_checkpoints`
                        (`CheckpointId`, `BattleInstanceId`, `CheckpointVersion`, `SnapshotSchemaVersion`,
                         `SnapshotHash`, `SnapshotJson`, `IsValid`, `CreatedAtUtc`)
                    VALUES
                        (@checkpointId, @battleId, @checkpointVersion, @payloadVersion,
                         @snapshotHash, @snapshotJson, @isValid, @createdAtUtc);
                    """;
                command.Parameters.AddWithValue("@checkpointId", checkpoint.CheckpointId.ToString());
                command.Parameters.AddWithValue("@battleId", checkpoint.BattleId.ToString());
                command.Parameters.AddWithValue("@checkpointVersion", checkpoint.CheckpointVersion);
                command.Parameters.AddWithValue("@payloadVersion", checkpoint.PayloadVersion);
                command.Parameters.AddWithValue("@snapshotHash", checkpoint.PayloadHash);
                command.Parameters.AddWithValue("@snapshotJson", JsonSerializer.Serialize(checkpoint.Snapshot, JsonOptions));
                command.Parameters.AddWithValue("@isValid", checkpoint.IsValid);
                command.Parameters.AddWithValue("@createdAtUtc", checkpoint.CreatedAtUtc.UtcDateTime);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpsertActorSnapshotAsync(connection, transaction, checkpoint.Snapshot, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BattleActorResultCode.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BattleActorResultCode.DuplicateCompleted;
        }
    }

    public async Task<BattleRoundCheckpoint?> LoadLatestValidAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `CheckpointId`, `CheckpointVersion`, `SnapshotSchemaVersion`,
                   `SnapshotHash`, `SnapshotJson`, `IsValid`, `CreatedAtUtc`
            FROM `battle_round_checkpoints`
            WHERE `BattleInstanceId` = @battleId
              AND `IsValid` = 1
            ORDER BY `CheckpointVersion` DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<BattleActorSnapshot>(
            reader.GetString("SnapshotJson"),
            JsonOptions);
        return snapshot is null
            ? null
            : new BattleRoundCheckpoint(
                Guid.Parse(reader.GetString("CheckpointId")),
                battleId,
                reader.GetInt64("CheckpointVersion"),
                snapshot,
                reader.GetInt32("SnapshotSchemaVersion"),
                reader.GetString("SnapshotHash"),
                reader.GetBoolean("IsValid"),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("CreatedAtUtc"), DateTimeKind.Utc)));
    }

    public async Task<(BattleActorResultCode Code, BattleEventEnvelope? Event)> AppendEventAsync(
        BattleEventEnvelope battleEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await LoadEventAsync(
                connection,
                transaction,
                battleEvent.BattleId,
                battleEvent.EventSequence,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (
                    BattleArchitectureV2Hash.Canonical(existing) ==
                    BattleArchitectureV2Hash.Canonical(battleEvent)
                        ? BattleActorResultCode.DuplicateCompleted
                        : BattleActorResultCode.ReplayConflict,
                    existing);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `battle_event_outbox`
                    (`BattleInstanceId`, `EventSequence`, `EventId`, `EventType`, `RoundNumber`,
                     `ActionExecutionId`, `EventSchemaVersion`, `EventFingerprintSha256`, `EventJson`,
                     `DeliveryStateJson`, `DispatchState`, `RetryCount`, `LastFailureCode`,
                     `CreatedAtUtc`, `UpdatedAtUtc`)
                VALUES
                    (@battleId, @eventSequence, @eventId, @eventType, @roundNumber,
                     @actionExecutionId, @payloadVersion, @payloadHash, @eventJson,
                     @deliveryStateJson, @dispatchState, 0, '', @createdAtUtc, @updatedAtUtc);
                """;
            command.Parameters.AddWithValue("@battleId", battleEvent.BattleId.ToString());
            command.Parameters.AddWithValue("@eventSequence", battleEvent.EventSequence);
            command.Parameters.AddWithValue("@eventId", battleEvent.EventId.ToString());
            command.Parameters.AddWithValue("@eventType", battleEvent.EventType.ToString());
            command.Parameters.AddWithValue("@roundNumber", battleEvent.RoundNumber);
            command.Parameters.AddWithValue("@actionExecutionId", (object?)battleEvent.ActionExecutionId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("@payloadVersion", battleEvent.PayloadVersion);
            command.Parameters.AddWithValue("@payloadHash", BattleArchitectureV2Hash.Canonical(battleEvent));
            command.Parameters.AddWithValue("@eventJson", JsonSerializer.Serialize(battleEvent, JsonOptions));
            command.Parameters.AddWithValue("@deliveryStateJson", "{}");
            command.Parameters.AddWithValue("@dispatchState", battleEvent.DispatchState.ToString());
            command.Parameters.AddWithValue("@createdAtUtc", battleEvent.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@updatedAtUtc", battleEvent.CreatedAtUtc.UtcDateTime);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (BattleActorResultCode.Success, battleEvent);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (BattleActorResultCode.ReplayConflict, null);
        }
    }

    public async Task<BattleActorResultCode> AcknowledgeAsync(
        Guid battleId,
        long eventSequence,
        BattleEventDeliveryCategory consumer,
        string payloadHash,
        DateTimeOffset acknowledgedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var loaded = await LoadEventWithDeliveryAsync(
            connection,
            transaction,
            battleId,
            eventSequence,
            cancellationToken);
        if (loaded.Event is null ||
            !string.Equals(BattleArchitectureV2Hash.Canonical(loaded.Event), payloadHash, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BattleActorResultCode.ReplayConflict;
        }

        var deliveries = loaded.Deliveries;
        if (deliveries.GetValueOrDefault(consumer.ToString()))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BattleActorResultCode.DuplicateCompleted;
        }

        deliveries[consumer.ToString()] = true;
        var allDispatched = loaded.Event.DeliveryCategories.All(value =>
            deliveries.GetValueOrDefault(value.ToString()));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `battle_event_outbox`
            SET `DeliveryStateJson` = @deliveryStateJson,
                `DispatchState` = @dispatchState,
                `UpdatedAtUtc` = @updatedAtUtc
            WHERE `BattleInstanceId` = @battleId
              AND `EventSequence` = @eventSequence;
            """;
        command.Parameters.AddWithValue("@deliveryStateJson", JsonSerializer.Serialize(deliveries, JsonOptions));
        command.Parameters.AddWithValue("@dispatchState", allDispatched
            ? BattleOutboxDispatchState.Dispatched.ToString()
            : BattleOutboxDispatchState.Pending.ToString());
        command.Parameters.AddWithValue("@updatedAtUtc", acknowledgedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@eventSequence", eventSequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BattleActorResultCode.Success;
    }

    public async Task<IReadOnlyList<BattleEventEnvelope>> ReadPendingAsync(
        Guid battleId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `EventJson`
            FROM `battle_event_outbox`
            WHERE `BattleInstanceId` = @battleId
              AND `DispatchState` <> 'Dispatched'
            ORDER BY `EventSequence`;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        List<BattleEventEnvelope> events = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = JsonSerializer.Deserialize<BattleEventEnvelope>(
                reader.GetString("EventJson"),
                JsonOptions);
            if (value is not null)
            {
                events.Add(value);
            }
        }

        return events.AsReadOnly();
    }

    public async Task<BattleCommandResult?> FindCommandAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `OperationFingerprintSha256`, `ResultJson`
            FROM `battle_idempotency`
            WHERE `Scope` = 'ActorCommand'
              AND `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", BattleArchitectureV2Hash.Canonical(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<BattleCommandResult>(
            reader.GetString("ResultJson"),
            JsonOptions);
        return result is null
            ? null
            : string.Equals(reader.GetString("OperationFingerprintSha256"), payloadHash, StringComparison.Ordinal)
                ? result
                : result with
                {
                    Code = BattleActorResultCode.ReplayConflict,
                    FailureCode = "battle.actor.command_replay_conflict"
                };
    }

    public async Task<BattleActorResultCode> SaveCommandAsync(
        BattleActorCommandEnvelope commandEnvelope,
        BattleCommandResult result,
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
                ('ActorCommand', @idempotencyKeyHash, @payloadHash, @battleId,
                 @resultJson, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", BattleArchitectureV2Hash.Canonical(commandEnvelope.IdempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", commandEnvelope.PayloadHash);
        command.Parameters.AddWithValue("@battleId", commandEnvelope.BattleId.ToString());
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", commandEnvelope.SubmittedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return BattleActorResultCode.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            var replay = await FindCommandAsync(
                commandEnvelope.IdempotencyKey,
                commandEnvelope.PayloadHash,
                cancellationToken);
            return replay?.Code == BattleActorResultCode.ReplayConflict
                ? BattleActorResultCode.ReplayConflict
                : BattleActorResultCode.DuplicateCompleted;
        }
    }

    public async Task<LockedRoundCommandSet?> LoadCommandLockAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        var json = await ReadScalarAsync(
            """
            SELECT `CommandLockJson`
            FROM `battle_round_plans`
            WHERE `BattleInstanceId` = @battleId AND `RoundNumber` = @roundNumber
            LIMIT 1;
            """,
            battleId,
            roundNumber,
            cancellationToken);
        return json is null
            ? null
            : JsonSerializer.Deserialize<LockedRoundCommandSet>(json, JsonOptions);
    }

    public async Task<BattleActorResultCode> SaveCommandLockAsync(
        LockedRoundCommandSet commandLock,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_round_plans`
                (`BattleInstanceId`, `RoundNumber`, `CommandWindowVersion`, `CommandLockId`,
                 `CommandLockHash`, `CommandLockJson`, `RoundResolutionPlanId`, `PlanHash`,
                 `PlanJson`, `RngAlgorithmVersion`, `RngStateJson`, `SchemaVersion`,
                 `CreatedAtUtc`, `UpdatedAtUtc`)
            VALUES
                (@battleId, @roundNumber, @commandWindowVersion, @commandLockId,
                 @commandLockHash, @commandLockJson, NULL, NULL, NULL,
                 @rngAlgorithmVersion, @rngStateJson, 1, @createdAtUtc, @updatedAtUtc);
            """;
        command.Parameters.AddWithValue("@battleId", commandLock.BattleId.ToString());
        command.Parameters.AddWithValue("@roundNumber", commandLock.RoundNumber);
        command.Parameters.AddWithValue("@commandWindowVersion", commandLock.CommandWindowVersion);
        command.Parameters.AddWithValue("@commandLockId", commandLock.CommandLockId.ToString());
        command.Parameters.AddWithValue("@commandLockHash", commandLock.CanonicalHash);
        command.Parameters.AddWithValue("@commandLockJson", JsonSerializer.Serialize(commandLock, JsonOptions));
        command.Parameters.AddWithValue("@rngAlgorithmVersion", XorShift64StarBattleRandom.Version);
        command.Parameters.AddWithValue("@rngStateJson", "{}");
        command.Parameters.AddWithValue("@createdAtUtc", commandLock.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", commandLock.CreatedAtUtc.UtcDateTime);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return BattleActorResultCode.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            var existing = await LoadCommandLockAsync(
                commandLock.BattleId,
                commandLock.RoundNumber,
                cancellationToken);
            return existing is not null &&
                   string.Equals(existing.CanonicalHash, commandLock.CanonicalHash, StringComparison.Ordinal)
                ? BattleActorResultCode.DuplicateCompleted
                : BattleActorResultCode.ReplayConflict;
        }
    }

    public async Task<RoundResolutionPlan?> LoadRoundPlanAsync(
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        var json = await ReadScalarAsync(
            """
            SELECT `PlanJson`
            FROM `battle_round_plans`
            WHERE `BattleInstanceId` = @battleId AND `RoundNumber` = @roundNumber
            LIMIT 1;
            """,
            battleId,
            roundNumber,
            cancellationToken);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<RoundResolutionPlan>(json, JsonOptions);
    }

    public async Task<BattleActorResultCode> SaveRoundPlanAsync(
        RoundResolutionPlan plan,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `battle_round_plans`
            SET `RoundResolutionPlanId` = @planId,
                `PlanHash` = @planHash,
                `PlanJson` = @planJson,
                `RngAlgorithmVersion` = @rngAlgorithmVersion,
                `RngStateJson` = @rngStateJson,
                `UpdatedAtUtc` = @updatedAtUtc
            WHERE `BattleInstanceId` = @battleId
              AND `RoundNumber` = @roundNumber
              AND (`RoundResolutionPlanId` IS NULL OR `PlanHash` = @planHash);
            """;
        command.Parameters.AddWithValue("@planId", plan.RoundResolutionPlanId.ToString());
        command.Parameters.AddWithValue("@planHash", plan.PlanHash);
        command.Parameters.AddWithValue("@planJson", JsonSerializer.Serialize(plan, JsonOptions));
        command.Parameters.AddWithValue("@rngAlgorithmVersion", plan.RngAlgorithmVersion);
        command.Parameters.AddWithValue("@rngStateJson", JsonSerializer.Serialize(plan.RngStateBefore, JsonOptions));
        command.Parameters.AddWithValue("@updatedAtUtc", plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@battleId", plan.BattleId.ToString());
        command.Parameters.AddWithValue("@roundNumber", plan.RoundNumber);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 1)
        {
            return BattleActorResultCode.Success;
        }

        var existing = await LoadRoundPlanAsync(plan.BattleId, plan.RoundNumber, cancellationToken);
        return existing is not null &&
               string.Equals(existing.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            ? BattleActorResultCode.DuplicateCompleted
            : BattleActorResultCode.ReplayConflict;
    }

    public async Task<BattleActorResultCode> SaveActionResultAsync(
        Guid battleId,
        int roundNumber,
        BattleActionResult result,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_action_results`
                (`BattleInstanceId`, `ActionExecutionId`, `RoundNumber`, `ExecutionOrder`,
                 `ParticipantId`, `ResultCode`, `ResultHash`, `ResultJson`, `CreatedAtUtc`)
            VALUES
                (@battleId, @actionExecutionId, @roundNumber, @executionOrder,
                 @participantId, @resultCode, @resultHash, @resultJson, @createdAtUtc);
            """;
        var hash = BattleArchitectureV2Hash.Canonical(result);
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@actionExecutionId", result.ActionId.ToString());
        command.Parameters.AddWithValue("@roundNumber", roundNumber);
        command.Parameters.AddWithValue("@executionOrder", result.ResolutionIndex);
        command.Parameters.AddWithValue("@participantId", result.ParticipantId.ToString());
        command.Parameters.AddWithValue("@resultCode", result.Result.ToString());
        command.Parameters.AddWithValue("@resultHash", hash);
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", result.ResolvedAtUtc.UtcDateTime);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return BattleActorResultCode.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            var existing = await LoadActionResultAsync(battleId, result.ActionId, cancellationToken);
            return existing is not null &&
                   BattleArchitectureV2Hash.Canonical(existing) == hash
                ? BattleActorResultCode.DuplicateCompleted
                : BattleActorResultCode.ReplayConflict;
        }
    }

    public async Task<BattleActionResult?> LoadActionResultAsync(
        Guid battleId,
        Guid actionExecutionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `ResultJson`
            FROM `battle_action_results`
            WHERE `BattleInstanceId` = @battleId
              AND `ActionExecutionId` = @actionExecutionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@actionExecutionId", actionExecutionId.ToString());
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull
            ? null
            : JsonSerializer.Deserialize<BattleActionResult>((string)scalar, JsonOptions);
    }

    public async Task<BattleFinalizationResult?> FindFinalizationAsync(
        Guid battleId,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `FinalizationFingerprintSha256`, `ResultJson`
            FROM `battle_finalizations`
            WHERE `BattleInstanceId` = @battleId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<BattleFinalizationResult>(
            reader.GetString("ResultJson"),
            JsonOptions);
        return result is null
            ? null
            : string.Equals(reader.GetString("FinalizationFingerprintSha256"), payloadHash, StringComparison.Ordinal)
                ? result
                : result with
                {
                    Code = BattleActorResultCode.ReplayConflict,
                    State = BattleFinalizationStateCode.RecoveryRequired,
                    FailureCode = "battle.finalization.replay_conflict"
                };
    }

    public async Task<BattleActorResultCode> SaveFinalizationAsync(
        BattleFinalizationPlan plan,
        BattleFinalizationResult result,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_finalizations`
                (`BattleInstanceId`, `FinalizationPlanId`, `SafeIdempotencyHash`, `FinalizationFingerprintSha256`,
                 `State`, `RewardCommitted`, `QuestEventsCommitted`, `WorldResumeCommitted`,
                 `PlanJson`, `ResultJson`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@battleId, @planId, @safeIdempotencyHash, @payloadHash,
                 @state, @rewardCommitted, @questCommitted, @worldResumeCommitted,
                 @planJson, @resultJson, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@battleId", plan.BattleId.ToString());
        command.Parameters.AddWithValue("@planId", plan.FinalizationPlanId.ToString());
        command.Parameters.AddWithValue("@safeIdempotencyHash", BattleArchitectureV2Hash.SafeId(plan.IdempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", plan.PayloadHash);
        command.Parameters.AddWithValue("@state", result.State.ToString());
        command.Parameters.AddWithValue("@rewardCommitted", result.RewardCommitted);
        command.Parameters.AddWithValue("@questCommitted", result.QuestEventsCommitted);
        command.Parameters.AddWithValue("@worldResumeCommitted", result.WorldResumeCommitted);
        command.Parameters.AddWithValue("@planJson", JsonSerializer.Serialize(plan, JsonOptions));
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", result.CompletedAtUtc.UtcDateTime);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return BattleActorResultCode.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            var replay = await FindFinalizationAsync(
                plan.BattleId,
                plan.PayloadHash,
                cancellationToken);
            return replay?.Code == BattleActorResultCode.ReplayConflict
                ? BattleActorResultCode.ReplayConflict
                : BattleActorResultCode.DuplicateCompleted;
        }
    }

    private async Task<string?> ReadScalarAsync(
        string sql,
        Guid battleId,
        int roundNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@roundNumber", roundNumber);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? null : (string)scalar;
    }

    private async Task<BattleJournalEntry?> ReadJournalEntryAsync(
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await LoadJournalEntryAsync(connection, null, battleId, sequence, cancellationToken);
    }

    private async Task<BattleJournalEntry?> LoadJournalEntryAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `JournalSequence`, `EntryType`, `BattleVersion`, `RoundNumber`,
                   `CommandWindowVersion`, `PlanId`, `ActionExecutionId`, `ResultReference`,
                   `SafeIdempotencyHash`, `CommandSchemaVersion`, `CanonicalCommandJson`, `CommandFingerprintSha256`,
                   `CreatedAtUtc`
            FROM `battle_command_journal`
            WHERE `BattleInstanceId` = @battleId
              AND `JournalSequence` = @sequence
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@sequence", sequence);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadJournalEntry(reader, battleId)
            : null;
    }

    private static BattleJournalEntry ReadJournalEntry(MySqlDataReader reader, Guid battleId) =>
        new(
            battleId,
            reader.GetInt64("JournalSequence"),
            Enum.Parse<BattleJournalEntryKind>(reader.GetString("EntryType")),
            reader.GetInt64("BattleVersion"),
            reader.GetInt32("RoundNumber"),
            reader.GetInt64("CommandWindowVersion"),
            reader.IsDBNull(reader.GetOrdinal("PlanId")) ? null : Guid.Parse(reader.GetString("PlanId")),
            reader.IsDBNull(reader.GetOrdinal("ActionExecutionId")) ? null : Guid.Parse(reader.GetString("ActionExecutionId")),
            reader.GetString("ResultReference"),
            reader.GetString("SafeIdempotencyHash"),
            reader.GetInt32("CommandSchemaVersion"),
            reader.GetString("CanonicalCommandJson"),
            reader.GetString("CommandFingerprintSha256"),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("CreatedAtUtc"), DateTimeKind.Utc)),
            "");

    private async Task<BattleEventEnvelope?> LoadEventAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadEventWithDeliveryAsync(
            connection,
            transaction,
            battleId,
            sequence,
            cancellationToken);
        return loaded.Event;
    }

    private async Task<(BattleEventEnvelope? Event, Dictionary<string, bool> Deliveries)> LoadEventWithDeliveryAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        Guid battleId,
        long sequence,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `EventJson`, `DeliveryStateJson`
            FROM `battle_event_outbox`
            WHERE `BattleInstanceId` = @battleId
              AND `EventSequence` = @eventSequence
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@battleId", battleId.ToString());
        command.Parameters.AddWithValue("@eventSequence", sequence);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (null, []);
        }

        var battleEvent = JsonSerializer.Deserialize<BattleEventEnvelope>(
            reader.GetString("EventJson"),
            JsonOptions);
        var deliveries = JsonSerializer.Deserialize<Dictionary<string, bool>>(
            reader.GetString("DeliveryStateJson"),
            JsonOptions) ?? [];
        return (battleEvent, deliveries);
    }

    private async Task UpsertActorSnapshotAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        BattleActorSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `battle_actor_instances`
                (`BattleInstanceId`, `EngineMode`, `ActorState`, `LifecycleState`,
                 `BattleVersion`, `RoundNumber`, `RoundVersion`, `CommandWindowVersion`,
                 `JournalSequence`, `OutboxSequence`, `FinalizationState`, `RecoveryState`,
                 `SnapshotJson`, `SnapshotHash`, `SchemaVersion`, `CreatedAtUtc`, `UpdatedAtUtc`)
            VALUES
                (@battleId, @engineMode, @actorState, @lifecycleState,
                 @battleVersion, @roundNumber, @roundVersion, @commandWindowVersion,
                 @journalSequence, @outboxSequence, @finalizationState, @recoveryState,
                 @snapshotJson, @snapshotHash, 1, @createdAtUtc, @updatedAtUtc)
            ON DUPLICATE KEY UPDATE
                `EngineMode` = VALUES(`EngineMode`),
                `ActorState` = VALUES(`ActorState`),
                `LifecycleState` = VALUES(`LifecycleState`),
                `BattleVersion` = VALUES(`BattleVersion`),
                `RoundNumber` = VALUES(`RoundNumber`),
                `RoundVersion` = VALUES(`RoundVersion`),
                `CommandWindowVersion` = VALUES(`CommandWindowVersion`),
                `JournalSequence` = VALUES(`JournalSequence`),
                `OutboxSequence` = VALUES(`OutboxSequence`),
                `FinalizationState` = VALUES(`FinalizationState`),
                `RecoveryState` = VALUES(`RecoveryState`),
                `SnapshotJson` = VALUES(`SnapshotJson`),
                `SnapshotHash` = VALUES(`SnapshotHash`),
                `UpdatedAtUtc` = VALUES(`UpdatedAtUtc`);
            """;
        command.Parameters.AddWithValue("@battleId", snapshot.BattleId.ToString());
        command.Parameters.AddWithValue("@engineMode", snapshot.EngineMode.ToString());
        command.Parameters.AddWithValue("@actorState", snapshot.BattleState.ToString());
        command.Parameters.AddWithValue("@lifecycleState", snapshot.LifecycleState.ToString());
        command.Parameters.AddWithValue("@battleVersion", snapshot.BattleVersion);
        command.Parameters.AddWithValue("@roundNumber", snapshot.RoundNumber);
        command.Parameters.AddWithValue("@roundVersion", snapshot.RoundVersion);
        command.Parameters.AddWithValue("@commandWindowVersion", snapshot.CommandWindowVersion);
        command.Parameters.AddWithValue("@journalSequence", snapshot.JournalSequence);
        command.Parameters.AddWithValue("@outboxSequence", snapshot.OutboxSequence);
        command.Parameters.AddWithValue("@finalizationState", snapshot.FinalizationState.ToString());
        command.Parameters.AddWithValue("@recoveryState", snapshot.RecoveryState.ToString());
        command.Parameters.AddWithValue("@snapshotJson", JsonSerializer.Serialize(snapshot, JsonOptions));
        command.Parameters.AddWithValue("@snapshotHash", snapshot.CanonicalHash);
        command.Parameters.AddWithValue("@createdAtUtc", snapshot.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAtUtc", snapshot.UpdatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
