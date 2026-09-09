using System.Data;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbItemUseRepository : MariaDbRuntimeRepository
{
    public MariaDbItemUseRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<ItemUseTransactionResult> UseItemAsync(
        ItemUseTransactionRequest request,
        ItemUseEffectEngine engine,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.CharacterId <= 0 || request.AccountId <= 0 || string.IsNullOrWhiteSpace(request.SessionId) ||
            request.ItemTemplateId <= 0 || !request.TargetsSelf)
        {
            return Failed(request, "item.use.request_invalid");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            var payloadHash = PayloadHash(request);
            var replay = await ReadReplayAsync(connection, transaction, request, payloadHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var actor = await LockActorAsync(connection, transaction, request, cancellationToken);
            if (actor is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "item.use.session_or_character_invalid");
            }

            actor = actor with
            {
                ActiveStatusCodes = await LoadActiveBattleStatusCodesAsync(
                    connection,
                    transaction,
                    request.CharacterId,
                    cancellationToken)
            };

            var inventory = await LockInventoryAsync(connection, transaction, request, cancellationToken);
            if (inventory is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "item.use.inventory_item_missing");
            }

            var evaluation = engine.Evaluate(
                new ItemUseRequest(request.ItemTemplateId, request.Context, request.TargetsSelf),
                actor);
            if (!evaluation.Succeeded || evaluation.Value is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, evaluation.Error.Code);
            }

            if (evaluation.Value.RemovedStatusCodes is { Count: > 0 })
            {
                await RemoveBattleStatusEffectsAsync(connection, transaction, request, evaluation.Value, cancellationToken);
            }

            await InsertTimedBuffEffectsAsync(connection, transaction, request, inventory, evaluation.Value, cancellationToken);
            await InsertExperienceBuffEffectsAsync(connection, transaction, request, inventory, evaluation.Value, cancellationToken);
            await InsertElementBuffEffectsAsync(connection, transaction, request, inventory, evaluation.Value, cancellationToken);

            await UpdateActorAsync(connection, transaction, actor, evaluation.Value, cancellationToken);
            await ConsumeInventoryItemAsync(connection, transaction, inventory, cancellationToken);
            await AdvanceInventoryVersionAsync(connection, transaction, request.CharacterId, inventory, cancellationToken);

            var result = new ItemUseTransactionResult(
                request.TransactionId,
                Replayed: false,
                inventory.InventoryVersion,
                checked(inventory.InventoryVersion + 1),
                evaluation.Value.RestoredHp,
                evaluation.Value.RestoredMp,
                evaluation.Value.UpdatedActor.CurrentHp,
                evaluation.Value.UpdatedActor.CurrentMp,
                string.Empty);
            await InsertReplayAsync(connection, transaction, request, payloadHash, result, cancellationToken);
            await InsertAuditAsync(connection, transaction, request, inventory, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException or OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failed(request, "item.use.persistence_failed");
        }
    }

    private static async Task<ItemUseTransactionResult?> ReadReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `item_use_fingerprint_sha256`,`transaction_id`,`inventory_version_before`,`inventory_version_after`,
                   `restored_hp`,`restored_mp`,`current_hp`,`current_mp`
            FROM `god2_player`.`item_use_idempotency`
            WHERE `idempotency_key_hash`=@keyHash
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@keyHash", Hash(request.IdempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!string.Equals(reader.GetString("item_use_fingerprint_sha256"), payloadHash, StringComparison.Ordinal))
        {
            return Failed(request, "item.use.idempotency_payload_conflict");
        }

        return new ItemUseTransactionResult(
            Guid.Parse(reader.GetString("transaction_id")),
            Replayed: true,
            reader.GetInt64("inventory_version_before"),
            reader.GetInt64("inventory_version_after"),
            reader.GetInt64("restored_hp"),
            reader.GetInt64("restored_mp"),
            reader.GetInt64("current_hp"),
            reader.GetInt64("current_mp"),
            string.Empty);
    }

    private static async Task<ItemUseActorState?> LockActorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT character_row.`character_id`,COALESCE(character_row.`level`,0) AS `level`,
                   COALESCE(character_row.`rebirth_count`,0) AS `rebirth_count`,
                   COALESCE(character_row.`class_name_cache`,character_row.`class_code`,'') AS `class_name`,
                   COALESCE(character_row.`gender_code`,'') AS `gender`,
                   character_row.`current_hp`,character_row.`max_hp`,character_row.`current_mp`,character_row.`max_mp`
            FROM `god2_player`.`characters` character_row
            JOIN `god2_player`.`accounts` account_row ON account_row.`account_id`=character_row.`account_id`
            WHERE character_row.`character_id`=@characterId AND character_row.`account_id`=@accountId
              AND character_row.`enabled`=1 AND character_row.`deleted_at_utc` IS NULL
              AND account_row.`current_session_id`=@sessionId
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@accountId", request.AccountId);
        command.Parameters.AddWithValue("@sessionId", request.SessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull("current_hp") || reader.IsDBNull("max_hp") ||
            reader.IsDBNull("current_mp") || reader.IsDBNull("max_mp"))
        {
            return null;
        }

        return new ItemUseActorState(
            reader.GetInt64("character_id"),
            reader.GetInt32("level"),
            reader.GetInt32("rebirth_count"),
            reader.GetString("class_name"),
            reader.GetString("gender"),
            reader.GetInt64("current_hp"),
            reader.GetInt64("max_hp"),
            reader.GetInt64("current_mp"),
            reader.GetInt64("max_mp"));
    }

    private static async Task<IReadOnlyList<string>> LoadActiveBattleStatusCodesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT status_row.`Code`
            FROM `god2`.`battle_status_instances` instance_row
            JOIN `god2`.`status_effects` status_row ON status_row.`Id`=instance_row.`StatusDefinitionId`
            JOIN `god2`.`battle_participants` participant_row
              ON participant_row.`BattleInstanceId`=instance_row.`BattleInstanceId`
             AND participant_row.`ParticipantId`=instance_row.`TargetParticipantId`
            JOIN `god2`.`battle_instances` battle_row
              ON battle_row.`BattleInstanceId`=participant_row.`BattleInstanceId`
            WHERE participant_row.`CharacterId`=@characterId
              AND participant_row.`IsAlive`=1
              AND battle_row.`CompletedAtUtc` IS NULL
              AND instance_row.`LifecycleState`='Active'
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        var codes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            codes.Add(reader.GetString("Code"));
        }

        return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task RemoveBattleStatusEffectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        ItemUseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (evaluation.RemovedStatusCodes is not { Count: > 0 } removedCodes)
        {
            return;
        }

        foreach (var statusCode in removedCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandTimeout = CommandTimeoutSeconds;
            select.CommandText = """
                SELECT instance_row.`StatusInstanceId`,instance_row.`BattleInstanceId`,
                       battle_row.`CurrentRoundNumber`,instance_row.`TargetParticipantId`,
                       instance_row.`StatusDefinitionId`
                FROM `god2`.`battle_status_instances` instance_row
                JOIN `god2`.`status_effects` status_row ON status_row.`Id`=instance_row.`StatusDefinitionId`
                JOIN `god2`.`battle_participants` participant_row
                  ON participant_row.`BattleInstanceId`=instance_row.`BattleInstanceId`
                 AND participant_row.`ParticipantId`=instance_row.`TargetParticipantId`
                JOIN `god2`.`battle_instances` battle_row
                  ON battle_row.`BattleInstanceId`=participant_row.`BattleInstanceId`
                WHERE participant_row.`CharacterId`=@characterId
                  AND participant_row.`IsAlive`=1
                  AND battle_row.`CompletedAtUtc` IS NULL
                  AND instance_row.`LifecycleState`='Active'
                  AND status_row.`Code`=@statusCode
                ORDER BY instance_row.`AppliedAtUtc`,instance_row.`StatusInstanceId`
                FOR UPDATE;
                """;
            select.Parameters.AddWithValue("@characterId", request.CharacterId);
            select.Parameters.AddWithValue("@statusCode", statusCode);
            var records = new List<BattleStatusRemovalRecord>();
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    records.Add(new BattleStatusRemovalRecord(
                        reader.GetString("StatusInstanceId"),
                        reader.GetString("BattleInstanceId"),
                        reader.GetInt32("CurrentRoundNumber"),
                        reader.GetString("TargetParticipantId"),
                        reader.GetInt32("StatusDefinitionId")));
                }
            }

            if (records.Count == 0)
            {
                throw new InvalidOperationException("Expected active status instance was not found for item cleanse.");
            }

            foreach (var record in records)
            {
                await InsertBattleStatusRemovalAsync(connection, transaction, request, record, statusCode, cancellationToken);
                await MarkBattleStatusRemovedAsync(connection, transaction, record.StatusInstanceId, cancellationToken);
            }
        }
    }

    private static async Task InsertBattleStatusRemovalAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        BattleStatusRemovalRecord record,
        string statusCode,
        CancellationToken cancellationToken)
    {
        var idempotency = Hash($"{request.IdempotencyKey}|cleanse|{record.StatusInstanceId}");
        var removalFingerprint = Hash($"{request.TransactionId}|{record.StatusInstanceId}|{statusCode}");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2`.`battle_status_removals`
                (`StatusRemovalId`,`StatusRemovalPlanId`,`StatusInstanceId`,`BattleInstanceId`,`RoundNumber`,
                 `TargetParticipantId`,`StatusDefinitionId`,`RemovalReason`,`IdempotencyKeyHash`,
                 `RemovalFingerprintSha256`,`State`,`RecoveryState`,`RemovalJson`,`ResultJson`,
                 `CreatedAtUtc`,`UpdatedAtUtc`,`CompletedAtUtc`)
            VALUES
                (@statusRemovalId,@statusRemovalPlanId,@statusInstanceId,@battleInstanceId,@roundNumber,
                 @targetParticipantId,@statusDefinitionId,'ItemCleanse',@idempotencyKeyHash,
                 @removalFingerprint,'Completed','Clean','{}','{}',
                 UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@statusRemovalId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@statusRemovalPlanId", request.TransactionId.ToString());
        command.Parameters.AddWithValue("@statusInstanceId", record.StatusInstanceId);
        command.Parameters.AddWithValue("@battleInstanceId", record.BattleInstanceId);
        command.Parameters.AddWithValue("@roundNumber", record.RoundNumber);
        command.Parameters.AddWithValue("@targetParticipantId", record.TargetParticipantId);
        command.Parameters.AddWithValue("@statusDefinitionId", record.StatusDefinitionId);
        command.Parameters.AddWithValue("@idempotencyKeyHash", idempotency);
        command.Parameters.AddWithValue("@removalFingerprint", removalFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkBattleStatusRemovedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string statusInstanceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2`.`battle_status_instances`
            SET `LifecycleState`='Removed',
                `RemovedAtUtc`=UTC_TIMESTAMP(6),
                `RemovalReason`='ItemCleanse',
                `RuntimeVersion`=`RuntimeVersion`+1,
                `UpdatedAtUtc`=UTC_TIMESTAMP(6)
            WHERE `StatusInstanceId`=@statusInstanceId AND `LifecycleState`='Active';
            """;
        command.Parameters.AddWithValue("@statusInstanceId", statusInstanceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Status instance changed during item cleanse.");
        }
    }

    private static async Task InsertTimedBuffEffectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        LockedInventoryItem inventory,
        ItemUseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (evaluation.AppliedBuffs is not { Count: > 0 })
        {
            return;
        }

        foreach (var buff in evaluation.AppliedBuffs)
        {
            if (buff.Duration is not { TotalSeconds: > 0 } duration)
            {
                throw new InvalidOperationException("Timed item buff is missing a verified duration.");
            }

            var (effectType, statType) = buff.StatCode switch
            {
                "strength" => ("提升腕力", "腕力"),
                "constitution" => ("提升體力", "體力"),
                "intelligence" => ("提升智力", "智力"),
                "speed" => ("提升速度", "速度"),
                _ => throw new InvalidOperationException("Timed item buff stat is not supported.")
            };

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`character_item_timed_effects`
                    (`character_id`,`source_item_id`,`effect_type`,`stat_type`,`bonus_value`,`duration_seconds`,
                     `started_at_utc`,`expires_at_utc`,`source_inventory_id`,`source_transaction_id`,
                     `runtime_state`,`enabled`,`created_at_utc`,`updated_at_utc`)
                VALUES
                    (@characterId,@sourceItemId,@effectType,@statType,@bonusValue,@durationSeconds,
                     @startedAtUtc,DATE_ADD(@startedAtUtc, INTERVAL @durationSeconds SECOND),@sourceInventoryId,@sourceTransactionId,
                     'Active',1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                """;
            command.Parameters.AddWithValue("@characterId", request.CharacterId);
            command.Parameters.AddWithValue("@sourceItemId", request.ItemTemplateId);
            command.Parameters.AddWithValue("@effectType", effectType);
            command.Parameters.AddWithValue("@statType", statType);
            command.Parameters.AddWithValue("@bonusValue", checked((int)buff.Amount));
            command.Parameters.AddWithValue("@durationSeconds", checked((int)duration.TotalSeconds));
            command.Parameters.AddWithValue("@startedAtUtc", request.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@sourceInventoryId", inventory.InventoryId);
            command.Parameters.AddWithValue("@sourceTransactionId", request.TransactionId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertExperienceBuffEffectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        LockedInventoryItem inventory,
        ItemUseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (evaluation.AppliedExperienceBuffs is not { Count: > 0 })
        {
            return;
        }

        foreach (var buff in evaluation.AppliedExperienceBuffs)
        {
            if (buff.Duration is not { TotalSeconds: > 0 } duration)
            {
                throw new InvalidOperationException("Timed experience item buff is missing a verified duration.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`character_item_experience_bonuses`
                    (`character_id`,`source_item_id`,`bonus_percent`,`duration_seconds`,
                     `started_at_utc`,`expires_at_utc`,`source_inventory_id`,`source_transaction_id`,
                     `runtime_state`,`enabled`,`created_at_utc`,`updated_at_utc`)
                VALUES
                    (@characterId,@sourceItemId,@bonusPercent,@durationSeconds,
                     @startedAtUtc,DATE_ADD(@startedAtUtc, INTERVAL @durationSeconds SECOND),@sourceInventoryId,@sourceTransactionId,
                     'Active',1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                """;
            command.Parameters.AddWithValue("@characterId", request.CharacterId);
            command.Parameters.AddWithValue("@sourceItemId", request.ItemTemplateId);
            command.Parameters.AddWithValue("@bonusPercent", checked((int)buff.Percent));
            command.Parameters.AddWithValue("@durationSeconds", checked((int)duration.TotalSeconds));
            command.Parameters.AddWithValue("@startedAtUtc", request.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@sourceInventoryId", inventory.InventoryId);
            command.Parameters.AddWithValue("@sourceTransactionId", request.TransactionId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertElementBuffEffectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        LockedInventoryItem inventory,
        ItemUseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (evaluation.AppliedElementBuffs is not { Count: > 0 })
        {
            return;
        }

        await using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction;
            replace.CommandTimeout = CommandTimeoutSeconds;
            replace.CommandText = """
                UPDATE `god2_player`.`character_item_element_bonuses`
                SET `runtime_state`='Replaced',
                    `enabled`=0,
                    `replaced_by_transaction_id`=@sourceTransactionId,
                    `updated_at_utc`=UTC_TIMESTAMP(6)
                WHERE `character_id`=@characterId
                  AND `enabled`=1
                  AND `runtime_state`='Active'
                  AND `expires_at_utc` > UTC_TIMESTAMP(6);
                """;
            replace.Parameters.AddWithValue("@characterId", request.CharacterId);
            replace.Parameters.AddWithValue("@sourceTransactionId", request.TransactionId.ToString());
            await replace.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var buff in evaluation.AppliedElementBuffs)
        {
            if (buff.Duration is not { TotalSeconds: > 0 } duration)
            {
                throw new InvalidOperationException("Timed element item buff is missing a verified duration.");
            }

            var elementType = buff.ElementCode switch
            {
                "metal" => "金",
                "wood" => "木",
                "water" => "水",
                "fire" => "火",
                "earth" => "土",
                _ => throw new InvalidOperationException("Element item buff is not supported.")
            };

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                INSERT INTO `god2_player`.`character_item_element_bonuses`
                    (`character_id`,`source_item_id`,`element_type`,`bonus_value`,`duration_seconds`,
                     `started_at_utc`,`expires_at_utc`,`source_inventory_id`,`source_transaction_id`,
                     `runtime_state`,`enabled`,`created_at_utc`,`updated_at_utc`)
                VALUES
                    (@characterId,@sourceItemId,@elementType,@bonusValue,@durationSeconds,
                     @startedAtUtc,DATE_ADD(@startedAtUtc, INTERVAL @durationSeconds SECOND),@sourceInventoryId,@sourceTransactionId,
                     'Active',1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                """;
            command.Parameters.AddWithValue("@characterId", request.CharacterId);
            command.Parameters.AddWithValue("@sourceItemId", request.ItemTemplateId);
            command.Parameters.AddWithValue("@elementType", elementType);
            command.Parameters.AddWithValue("@bonusValue", checked((int)buff.Amount));
            command.Parameters.AddWithValue("@durationSeconds", checked((int)duration.TotalSeconds));
            command.Parameters.AddWithValue("@startedAtUtc", request.CreatedAtUtc.UtcDateTime);
            command.Parameters.AddWithValue("@sourceInventoryId", inventory.InventoryId);
            command.Parameters.AddWithValue("@sourceTransactionId", request.TransactionId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<LockedInventoryItem?> LockInventoryAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT inventory_row.`inventory_id`,inventory_row.`slot_index`,inventory_row.`quantity`,
                   inventory_row.`slot_version`,state_row.`InventoryVersion`
            FROM `god2_player`.`character_inventory` inventory_row
            JOIN `god2_player`.`player_inventory_state` state_row ON state_row.`CharacterId`=inventory_row.`character_id`
            WHERE inventory_row.`character_id`=@characterId AND inventory_row.`item_id`=@itemId
              AND inventory_row.`enabled`=1 AND inventory_row.`deleted_at_utc` IS NULL AND inventory_row.`quantity`>0
            ORDER BY inventory_row.`slot_index`
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@itemId", request.ItemTemplateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LockedInventoryItem(
                reader.GetInt64("inventory_id"),
                reader.GetInt32("slot_index"),
                reader.GetInt64("quantity"),
                reader.GetInt64("slot_version"),
                reader.GetInt64("InventoryVersion"))
            : null;
    }

    private static async Task UpdateActorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseActorState before,
        ItemUseEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`characters`
            SET `current_hp`=@currentHp,`current_mp`=@currentMp,
                `concurrency_token`=@token,`updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `character_id`=@characterId AND `current_hp`=@beforeHp AND `current_mp`=@beforeMp;
            """;
        command.Parameters.AddWithValue("@currentHp", evaluation.UpdatedActor.CurrentHp);
        command.Parameters.AddWithValue("@currentMp", evaluation.UpdatedActor.CurrentMp);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@characterId", before.CharacterId);
        command.Parameters.AddWithValue("@beforeHp", before.CurrentHp);
        command.Parameters.AddWithValue("@beforeMp", before.CurrentMp);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Character vitals changed during item use.");
        }
    }

    private static async Task ConsumeInventoryItemAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedInventoryItem inventory,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`character_inventory`
            SET `quantity`=`quantity`-1,`inventory_version`=@nextInventoryVersion,`slot_version`=`slot_version`+1,
                `enabled`=CASE WHEN `quantity`=1 THEN 0 ELSE 1 END,
                `deleted_at_utc`=CASE WHEN `quantity`=1 THEN UTC_TIMESTAMP(6) ELSE NULL END,
                `updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `inventory_id`=@inventoryId AND `quantity`=@quantity AND `slot_version`=@slotVersion;
            """;
        command.Parameters.AddWithValue("@nextInventoryVersion", checked(inventory.InventoryVersion + 1));
        command.Parameters.AddWithValue("@inventoryId", inventory.InventoryId);
        command.Parameters.AddWithValue("@quantity", inventory.Quantity);
        command.Parameters.AddWithValue("@slotVersion", inventory.SlotVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Inventory item changed during item use.");
        }
    }

    private static async Task AdvanceInventoryVersionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        LockedInventoryItem inventory,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`player_inventory_state`
            SET `InventoryVersion`=`InventoryVersion`+1,`MutationSequence`=`MutationSequence`+1,
                `DirtyState`='乾淨',`UpdatedAtUtc`=UTC_TIMESTAMP(6)
            WHERE `CharacterId`=@characterId AND `InventoryVersion`=@beforeVersion;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);
        command.Parameters.AddWithValue("@beforeVersion", inventory.InventoryVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Inventory version changed during item use.");
        }
    }

    private static async Task InsertReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        string payloadHash,
        ItemUseTransactionResult result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`item_use_idempotency`
                (`idempotency_key_hash`,`item_use_fingerprint_sha256`,`transaction_id`,`character_id`,`item_id`,
                 `inventory_version_before`,`inventory_version_after`,`restored_hp`,`restored_mp`,`current_hp`,`current_mp`,
                 `created_at_utc`,`completed_at_utc`)
            VALUES
                (@keyHash,@payloadHash,@transactionId,@characterId,@itemId,
                 @versionBefore,@versionAfter,@restoredHp,@restoredMp,@currentHp,@currentMp,@createdAtUtc,UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@keyHash", Hash(request.IdempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@transactionId", request.TransactionId.ToString());
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@itemId", request.ItemTemplateId);
        command.Parameters.AddWithValue("@versionBefore", result.InventoryVersionBefore);
        command.Parameters.AddWithValue("@versionAfter", result.InventoryVersionAfter);
        command.Parameters.AddWithValue("@restoredHp", result.RestoredHp);
        command.Parameters.AddWithValue("@restoredMp", result.RestoredMp);
        command.Parameters.AddWithValue("@currentHp", result.CurrentHp);
        command.Parameters.AddWithValue("@currentMp", result.CurrentMp);
        command.Parameters.AddWithValue("@createdAtUtc", request.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ItemUseTransactionRequest request,
        LockedInventoryItem inventory,
        ItemUseTransactionResult result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`inventory_audit_ledger`
                (`AuditId`,`TransactionId`,`IdempotencySafeId`,`CharacterId`,`SessionId`,`OperationType`,`Source`,
                 `MerchantTemplateId`,`ItemTemplateId`,`InventoryItemId`,`QuantityBefore`,`QuantityAfter`,
                 `CurrencyType`,`CurrencyBefore`,`CurrencyAfter`,`InventoryVersionBefore`,`InventoryVersionAfter`,
                 `Result`,`FailureCode`,`CreatedAtUtc`,`CompletedAtUtc`,`CorrelationId`)
            VALUES
                (@auditId,@transactionId,@safeId,@characterId,@sessionId,'使用物品','官方物品使用',
                 NULL,@itemId,@inventoryId,@quantityBefore,@quantityAfter,
                 '無',0,0,@versionBefore,@versionAfter,'成功','',@createdAtUtc,UTC_TIMESTAMP(6),@transactionId);
            """;
        command.Parameters.AddWithValue("@auditId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@transactionId", request.TransactionId.ToString());
        command.Parameters.AddWithValue("@safeId", Hash(request.IdempotencyKey)[..16]);
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@sessionId", request.SessionId);
        command.Parameters.AddWithValue("@itemId", request.ItemTemplateId);
        command.Parameters.AddWithValue("@inventoryId", inventory.InventoryId);
        command.Parameters.AddWithValue("@quantityBefore", inventory.Quantity);
        command.Parameters.AddWithValue("@quantityAfter", inventory.Quantity - 1);
        command.Parameters.AddWithValue("@versionBefore", result.InventoryVersionBefore);
        command.Parameters.AddWithValue("@versionAfter", result.InventoryVersionAfter);
        command.Parameters.AddWithValue("@createdAtUtc", request.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string PayloadHash(ItemUseTransactionRequest request) => Hash(string.Join('|',
        request.TransactionId,
        request.CharacterId,
        request.AccountId,
        request.SessionId,
        request.ItemTemplateId,
        request.Context,
        request.TargetsSelf));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ItemUseTransactionResult Failed(ItemUseTransactionRequest request, string code) =>
        new(request.TransactionId, false, 0, 0, 0, 0, 0, 0, code);

    private sealed record LockedInventoryItem(
        long InventoryId,
        int SlotIndex,
        long Quantity,
        long SlotVersion,
        long InventoryVersion);

    private sealed record BattleStatusRemovalRecord(
        string StatusInstanceId,
        string BattleInstanceId,
        int RoundNumber,
        string TargetParticipantId,
        int StatusDefinitionId);
}
