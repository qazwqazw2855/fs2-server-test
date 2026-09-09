using System.Security.Cryptography;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using God2.ClassicServer.Application.Common;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbPortalTransitionStore : MariaDbRuntimeRepository, IPortalTransitionStore, IWorldMovementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public MariaDbPortalTransitionStore(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<PortalLocationState> LoadAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `character_id` AS `Id`, `map_id` AS `MapId`, `position_x` AS `PositionX`, `position_y` AS `PositionY`,
                   `current_direction` AS `CurrentDirection`, `runtime_version` AS `RuntimeVersion`,
                   `last_portal_template_id` AS `LastPortalTemplateId`, `last_portal_transition_id` AS `LastPortalTransitionId`,
                   `updated_at_utc` AS `UpdatedAtUtc`
            FROM `god2_player`.`characters`
            WHERE `character_id` = @characterId AND `status` <> 'Deleted'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Character runtime location was not found.");
        }

        return MaterializeLocation(reader);
    }

    internal static PortalLocationState MaterializeLocation(DbDataReader reader)
    {
        var directionText = reader.GetString(reader.GetOrdinal("CurrentDirection"));
        var direction = ParseWorldDirection(directionText);
        var transitionOrdinal = reader.GetOrdinal("LastPortalTransitionId");
        Guid? transitionId = reader.IsDBNull(transitionOrdinal)
            ? null
            : reader.GetGuid(transitionOrdinal);
        return new PortalLocationState(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetInt32(reader.GetOrdinal("MapId")),
            new WorldPosition3(
                reader.GetInt32(reader.GetOrdinal("PositionX")),
                reader.GetInt32(reader.GetOrdinal("PositionY"))),
            direction,
            reader.GetInt64(reader.GetOrdinal("RuntimeVersion")),
            reader.IsDBNull(reader.GetOrdinal("LastPortalTemplateId"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("LastPortalTemplateId")),
            transitionId,
            new DateTimeOffset(DateTime.SpecifyKind(
                reader.GetDateTime(reader.GetOrdinal("UpdatedAtUtc")),
                DateTimeKind.Utc)));
    }

    public async Task<PortalTransitionReplayLookup> FindCompletedAsync(
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InteractionFingerprintSha256`, `ResultJson`
            FROM `god2_player`.`world_interaction_idempotency`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PortalTransitionReplayLookup(false, true, null);
        }

        var storedHash = reader.GetString("InteractionFingerprintSha256");
        var result = JsonSerializer.Deserialize<PortalTransitionResult>(
            reader.GetString("ResultJson"),
            JsonOptions);
        return new PortalTransitionReplayLookup(
            true,
            string.Equals(storedHash, payloadHash, StringComparison.Ordinal),
            result);
    }

    public async Task<OperationResult> CommitAsync(
        PortalTransitionPersistenceCommit commit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await FindWithinTransactionAsync(
                connection,
                transaction,
                commit.Plan.IdempotencyKey,
                cancellationToken);
            if (replay is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return string.Equals(replay.Value.PayloadHash, commit.PayloadHash, StringComparison.Ordinal)
                    ? OperationResult.Success
                    : OperationResult.Failure(
                        "portal.replay_conflict",
                        "Portal idempotency key was reused with a different payload.");
            }

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandTimeout = CommandTimeoutSeconds;
                update.CommandText = """
                    UPDATE `god2_player`.`characters`
                    SET `map_id` = @mapId,
                        `position_x` = @positionX,
                        `position_y` = @positionY,
                        `current_direction` = @direction,
                        `runtime_version` = @runtimeVersionAfter,
                        `last_portal_template_id` = @portalTemplateId,
                        `last_portal_transition_id` = @transitionId,
                        `updated_at_utc` = @updatedAtUtc,
                        `concurrency_token` = @concurrencyToken
                    WHERE `character_id` = @characterId
                      AND `status` <> 'Deleted'
                      AND `runtime_version` = @runtimeVersionBefore;
                    """;
                update.Parameters.AddWithValue("@mapId", commit.After.CurrentMapId);
                update.Parameters.AddWithValue("@positionX", commit.After.RawPosition.X);
                update.Parameters.AddWithValue("@positionY", commit.After.RawPosition.Y);
                update.Parameters.AddWithValue("@direction", FormatWorldDirection(commit.After.Direction));
                update.Parameters.AddWithValue("@runtimeVersionAfter", commit.After.RuntimeVersion);
                update.Parameters.AddWithValue("@portalTemplateId", commit.After.LastPortalTemplateId);
                update.Parameters.AddWithValue("@transitionId", commit.After.LastTransitionId?.ToString());
                update.Parameters.AddWithValue("@updatedAtUtc", commit.After.UpdatedAtUtc.UtcDateTime);
                update.Parameters.AddWithValue("@concurrencyToken", Guid.NewGuid().ToString("N"));
                update.Parameters.AddWithValue("@characterId", commit.After.CharacterId);
                update.Parameters.AddWithValue("@runtimeVersionBefore", commit.Before.RuntimeVersion);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return OperationResult.Failure(
                        "portal.version_conflict",
                        "Character runtime location changed before portal transition commit.");
                }
            }

            var result = new PortalTransitionResult(
                WorldInteractionResultCode.Success,
                commit.Plan,
                PortalTransitionState.Committed,
                "",
                commit.Before.RuntimeVersion,
                commit.After.RuntimeVersion,
                true,
                true,
                true,
                true,
                "NotRequired");
            await InsertIdempotencyAsync(connection, transaction, commit, result, cancellationToken);
            await InsertAuditAsync(connection, transaction, commit.Audit, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Failure(
                "portal.replay_conflict",
                "Concurrent portal transition reused an idempotency key.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OperationResult> CommitMovementAsync(
        WorldMovementPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            long runtimeVersion;
            int persistedMapId;
            int persistedX;
            int persistedY;
            var found = false;
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandTimeout = CommandTimeoutSeconds;
                read.CommandText = """
                    SELECT `map_id` AS `MapId`, `position_x` AS `PositionX`, `position_y` AS `PositionY`, `runtime_version` AS `RuntimeVersion`
                    FROM `god2_player`.`characters`
                    WHERE `character_id` = @characterId AND `status` <> 'Deleted'
                    FOR UPDATE;
                    """;
                read.Parameters.AddWithValue("@characterId", request.CharacterId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    found = true;
                    persistedMapId = reader.GetInt32("MapId");
                    persistedX = reader.GetInt32("PositionX");
                    persistedY = reader.GetInt32("PositionY");
                    runtimeVersion = reader.GetInt64("RuntimeVersion");
                }
                else
                {
                    persistedMapId = 0;
                    persistedX = 0;
                    persistedY = 0;
                    runtimeVersion = 0;
                }
            }

            if (!found)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "movement.character_not_found",
                    "Character runtime location was not found.");
            }

            if (persistedMapId != request.MapId ||
                persistedX != request.ExpectedPosition.X ||
                persistedY != request.ExpectedPosition.Y)
            {
                await transaction.RollbackAsync(cancellationToken);
                return OperationResult.Failure(
                    "movement.position_conflict",
                    "Character runtime position changed before movement commit.");
            }

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandTimeout = CommandTimeoutSeconds;
                update.CommandText = """
                    UPDATE `god2_player`.`characters`
                    SET `position_x` = @positionX,
                        `position_y` = @positionY,
                        `current_direction` = @direction,
                        `runtime_version` = @runtimeVersionAfter,
                        `updated_at_utc` = @updatedAtUtc,
                        `concurrency_token` = @concurrencyToken
                    WHERE `character_id` = @characterId
                      AND `status` <> 'Deleted'
                      AND `map_id` = @mapId
                      AND `position_x` = @expectedX
                      AND `position_y` = @expectedY
                      AND `runtime_version` = @runtimeVersionBefore;
                    """;
                update.Parameters.AddWithValue("@positionX", request.Position.X);
                update.Parameters.AddWithValue("@positionY", request.Position.Y);
                update.Parameters.AddWithValue("@direction", FormatWorldDirection(request.Direction));
                update.Parameters.AddWithValue("@runtimeVersionAfter", checked(runtimeVersion + 1));
                update.Parameters.AddWithValue("@updatedAtUtc", request.UpdatedAtUtc.UtcDateTime);
                update.Parameters.AddWithValue("@concurrencyToken", Guid.NewGuid().ToString("N"));
                update.Parameters.AddWithValue("@characterId", request.CharacterId);
                update.Parameters.AddWithValue("@mapId", request.MapId);
                update.Parameters.AddWithValue("@expectedX", request.ExpectedPosition.X);
                update.Parameters.AddWithValue("@expectedY", request.ExpectedPosition.Y);
                update.Parameters.AddWithValue("@runtimeVersionBefore", runtimeVersion);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return OperationResult.Failure(
                        "movement.position_conflict",
                        "Character runtime position changed before movement commit.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<(string PayloadHash, string ResultJson)?> FindWithinTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `InteractionFingerprintSha256`, `ResultJson`
            FROM `god2_player`.`world_interaction_idempotency`
            WHERE `IdempotencyKeyHash` = @idempotencyKeyHash
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(idempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString("InteractionFingerprintSha256"), reader.GetString("ResultJson"))
            : null;
    }

    private static WorldDirection ParseWorldDirection(string value) => value switch
    {
        "未知" => WorldDirection.Unknown,
        "北" => WorldDirection.North,
        "南" => WorldDirection.South,
        "東" => WorldDirection.East,
        "西" => WorldDirection.West,
        "東北" => WorldDirection.NorthEast,
        "西北" => WorldDirection.NorthWest,
        "東南" => WorldDirection.SouthEast,
        "西南" => WorldDirection.SouthWest,
        _ when Enum.TryParse<WorldDirection>(value, true, out var parsed) => parsed,
        _ => WorldDirection.Unknown
    };

    private static string FormatWorldDirection(WorldDirection value) => value switch
    {
        WorldDirection.North => "北",
        WorldDirection.South => "南",
        WorldDirection.East => "東",
        WorldDirection.West => "西",
        WorldDirection.NorthEast => "東北",
        WorldDirection.NorthWest => "西北",
        WorldDirection.SouthEast => "東南",
        WorldDirection.SouthWest => "西南",
        _ => "未知"
    };

    private static async Task InsertIdempotencyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PortalTransitionPersistenceCommit commit,
        PortalTransitionResult result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`world_interaction_idempotency`
                (`IdempotencyKeyHash`, `CharacterId`, `InteractionType`, `InteractionFingerprintSha256`, `ResultJson`,
                 `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@idempotencyKeyHash, @characterId, '傳送門', @payloadHash, @resultJson,
                 @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@idempotencyKeyHash", Hash(commit.Plan.IdempotencyKey));
        command.Parameters.AddWithValue("@characterId", commit.Plan.CharacterId);
        command.Parameters.AddWithValue("@payloadHash", commit.PayloadHash);
        command.Parameters.AddWithValue("@resultJson", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("@createdAtUtc", commit.Plan.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        WorldInteractionAuditRecord audit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`world_interaction_audit`
                (`AuditId`, `InteractionId`, `TransitionId`, `IdempotencySafeId`, `CorrelationId`,
                 `SessionId`, `CharacterId`, `PlayerRuntimeEntityId`, `TargetRuntimeEntityId`,
                 `TargetTemplateId`, `InteractionType`, `Handler`, `SourceMapId`, `TargetMapId`,
                 `SourcePosition`, `TargetPosition`, `RuntimeVersionBefore`, `RuntimeVersionAfter`,
                 `Result`, `FailureCode`, `RollbackStatus`, `SessionRebound`, `ReplicationCleared`,
                 `ReplicationRebuilt`, `CreatedAtUtc`, `CompletedAtUtc`)
            VALUES
                (@auditId, @interactionId, @transitionId, @idempotencySafeId, @correlationId,
                 @sessionId, @characterId, @playerRuntimeEntityId, @targetRuntimeEntityId,
                 @targetTemplateId, @interactionType, @handler, @sourceMapId, @targetMapId,
                 @sourcePosition, @targetPosition, @runtimeVersionBefore, @runtimeVersionAfter,
                 @result, @failureCode, @rollbackStatus, @sessionRebound, @replicationCleared,
                 @replicationRebuilt, @createdAtUtc, @completedAtUtc);
            """;
        command.Parameters.AddWithValue("@auditId", audit.AuditId.ToString());
        command.Parameters.AddWithValue("@interactionId", audit.InteractionId.ToString());
        command.Parameters.AddWithValue("@transitionId", audit.TransitionId?.ToString());
        command.Parameters.AddWithValue("@idempotencySafeId", audit.IdempotencySafeId);
        command.Parameters.AddWithValue("@correlationId", audit.CorrelationId);
        command.Parameters.AddWithValue("@sessionId", audit.SessionId);
        command.Parameters.AddWithValue("@characterId", audit.CharacterId);
        command.Parameters.AddWithValue("@playerRuntimeEntityId", audit.PlayerRuntimeEntityId);
        command.Parameters.AddWithValue("@targetRuntimeEntityId", audit.TargetRuntimeEntityId);
        command.Parameters.AddWithValue("@targetTemplateId", audit.TargetTemplateId);
        command.Parameters.AddWithValue("@interactionType", FormatInteractionType(audit.InteractionType.ToString()));
        command.Parameters.AddWithValue("@handler", FormatWorldInteractionHandler(audit.Handler));
        command.Parameters.AddWithValue("@sourceMapId", audit.SourceMapId);
        command.Parameters.AddWithValue("@targetMapId", audit.TargetMapId);
        command.Parameters.AddWithValue("@sourcePosition", Position(audit.SourcePosition));
        command.Parameters.AddWithValue("@targetPosition", audit.TargetPosition is null ? null : Position(audit.TargetPosition));
        command.Parameters.AddWithValue("@runtimeVersionBefore", audit.RuntimeVersionBefore);
        command.Parameters.AddWithValue("@runtimeVersionAfter", audit.RuntimeVersionAfter);
        command.Parameters.AddWithValue("@result", FormatWorldInteractionResult(audit.Result.ToString()));
        command.Parameters.AddWithValue("@failureCode", audit.FailureCode);
        command.Parameters.AddWithValue("@rollbackStatus", FormatRollbackStatus(audit.RollbackStatus));
        command.Parameters.AddWithValue("@sessionRebound", audit.SessionRebound);
        command.Parameters.AddWithValue("@replicationCleared", audit.ReplicationCleared);
        command.Parameters.AddWithValue("@replicationRebuilt", audit.ReplicationRebuilt);
        command.Parameters.AddWithValue("@createdAtUtc", audit.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("@completedAtUtc", audit.CompletedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Position(WorldPosition3 position) =>
        $"{position.X},{position.Y},{position.Z}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string FormatInteractionType(string value) => value switch
    {
        "Portal" => "傳送門",
        _ => value
    };

    private static string FormatWorldInteractionResult(string value) => value switch
    {
        "Success" => "成功",
        _ => value
    };

    private static string FormatRollbackStatus(string value) => value switch
    {
        "NotRequired" => "不需回復",
        _ => value
    };

    private static string FormatWorldInteractionHandler(string value) => value switch
    {
        "Portal" => "傳送門處理器",
        "Merchant" => "商店處理器",
        "Dialog" => "對話處理器",
        "None" => "無",
        _ => value
    };
}
