using System.Data;
using System.Security.Cryptography;
using System.Text;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbEquipmentEnhancementRepository : MariaDbRuntimeRepository
{
    public MariaDbEquipmentEnhancementRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<EquipmentEnhancementTransactionResult> EnhanceAsync(
        EquipmentEnhancementTransactionRequest request,
        EquipmentEnhancementEngine engine,
        CancellationToken cancellationToken)
    {
        if (request.TransactionId == Guid.Empty || string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.CharacterId <= 0 || request.AccountId <= 0 || string.IsNullOrWhiteSpace(request.SessionId) ||
            request.MaterialItemTemplateId <= 0 || request.TargetInventoryId <= 0)
        {
            return Failed(request, "equipment.enhancement.request_invalid");
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

            if (!await LockActorAsync(connection, transaction, request, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "equipment.enhancement.session_or_character_invalid");
            }

            var inventoryVersion = await LockInventoryStateAsync(connection, transaction, request.CharacterId, cancellationToken);
            var material = await LockMaterialAsync(connection, transaction, request, cancellationToken);
            var target = await LockTargetAsync(connection, transaction, request, cancellationToken);
            if (inventoryVersion is null || material is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "equipment.enhancement.material_missing");
            }

            if (target is null || target.InventoryId == material.InventoryId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "equipment.enhancement.target_missing");
            }

            await EnsureEquipmentInstanceAsync(connection, transaction, target, cancellationToken);
            var state = await LockEquipmentStateAsync(connection, transaction, target, cancellationToken);
            if (state is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, "equipment.enhancement.target_state_missing");
            }

            var evaluation = engine.Evaluate(
                request.MaterialItemTemplateId,
                state,
                RandomNumberGenerator.GetInt32(10_000),
                RandomNumberGenerator.GetInt32(int.MaxValue));
            if (!evaluation.Succeeded || evaluation.Value is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(request, evaluation.Error.Code);
            }

            var nextInventoryVersion = checked(inventoryVersion.Value + 1);
            await ConsumeMaterialAsync(connection, transaction, material, nextInventoryVersion, cancellationToken);
            await ApplyTargetResultAsync(connection, transaction, target, state, evaluation.Value, nextInventoryVersion, cancellationToken);
            await AdvanceInventoryVersionAsync(connection, transaction, request.CharacterId, inventoryVersion.Value, cancellationToken);

            var result = new EquipmentEnhancementTransactionResult(
                request.TransactionId,
                Replayed: false,
                inventoryVersion.Value,
                nextInventoryVersion,
                evaluation.Value.EnhancementSucceeded,
                evaluation.Value.TargetDestroyed,
                state.EnhancementLevel,
                evaluation.Value.UpdatedTarget.EnhancementLevel,
                evaluation.Value.AppliedIncrement,
                evaluation.Value.SuccessRateBasisPoints,
                evaluation.Value.UpdatedTarget.CurrentDurability,
                evaluation.Value.UpdatedTarget.MaximumDurability,
                string.Empty);
            await InsertReplayAsync(connection, transaction, request, payloadHash, result, cancellationToken);
            await InsertAuditAsync(connection, transaction, request, material, target, result, cancellationToken);
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
            return Failed(request, "equipment.enhancement.persistence_failed");
        }
    }

    private static async Task<EquipmentEnhancementTransactionResult?> ReadReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `enhancement_fingerprint_sha256`,`transaction_id`,`inventory_version_before`,`inventory_version_after`,
                   `enhancement_succeeded`,`target_destroyed`,`enhancement_level_before`,`enhancement_level_after`,
                   `applied_increment`,`success_rate_basis_points`,`current_durability_after`,`maximum_durability_after`
            FROM `god2_player`.`equipment_enhancement_idempotency`
            WHERE `idempotency_key_hash`=@keyHash
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@keyHash", Hash(request.IdempotencyKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!string.Equals(reader.GetString("enhancement_fingerprint_sha256"), payloadHash, StringComparison.Ordinal))
        {
            return Failed(request, "equipment.enhancement.idempotency_payload_conflict");
        }

        return new EquipmentEnhancementTransactionResult(
            reader.GetGuid(reader.GetOrdinal("transaction_id")),
            Replayed: true,
            reader.GetInt64("inventory_version_before"),
            reader.GetInt64("inventory_version_after"),
            reader.GetBoolean("enhancement_succeeded"),
            reader.GetBoolean("target_destroyed"),
            reader.GetInt32("enhancement_level_before"),
            reader.GetInt32("enhancement_level_after"),
            reader.GetInt32("applied_increment"),
            reader.GetInt32("success_rate_basis_points"),
            NullableInt32(reader, "current_durability_after"),
            NullableInt32(reader, "maximum_durability_after"),
            string.Empty);
    }

    private static async Task<bool> LockActorAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT character_row.`character_id`
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
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<long?> LockInventoryStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT `InventoryVersion` FROM `god2_player`.`player_inventory_state` WHERE `CharacterId`=@characterId FOR UPDATE;";
        command.Parameters.AddWithValue("@characterId", characterId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<LockedMaterial?> LockMaterialAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `inventory_id`,`quantity`,`slot_version`
            FROM `god2_player`.`character_inventory`
            WHERE `character_id`=@characterId AND `item_id`=@itemId AND `enabled`=1
              AND `deleted_at_utc` IS NULL AND `quantity`>0
            ORDER BY `slot_index`
            LIMIT 1 FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@itemId", request.MaterialItemTemplateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LockedMaterial(reader.GetInt64("inventory_id"), reader.GetInt64("quantity"), reader.GetInt64("slot_version"))
            : null;
    }

    private static async Task<LockedTarget?> LockTargetAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT inventory_row.`inventory_id`,inventory_row.`item_id`,inventory_row.`quantity`,inventory_row.`slot_version`,
                   CASE WHEN weapon_row.`item_id` IS NOT NULL THEN 'Weapon'
                        WHEN equipment_row.`item_id` IS NOT NULL THEN 'Equipment' END AS `target_type`,
                   COALESCE(weapon_row.`required_level`,equipment_row.`required_level`,0) AS `required_level`,
                   COALESCE(weapon_row.`maximum_enhancement`,equipment_row.`maximum_enhancement`,10) AS `maximum_enhancement`,
                   COALESCE(weapon_row.`base_durability`,equipment_row.`durability`) AS `base_durability`
            FROM `god2_player`.`character_inventory` inventory_row
            LEFT JOIN `god2_game`.`weapons` weapon_row ON weapon_row.`item_id`=inventory_row.`item_id` AND weapon_row.`enabled`=1
            LEFT JOIN `god2_game`.`equipment` equipment_row ON equipment_row.`item_id`=inventory_row.`item_id` AND equipment_row.`enabled`=1
            WHERE inventory_row.`inventory_id`=@inventoryId AND inventory_row.`character_id`=@characterId
              AND inventory_row.`enabled`=1 AND inventory_row.`deleted_at_utc` IS NULL AND inventory_row.`quantity`=1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@inventoryId", request.TargetInventoryId);
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull("target_type"))
        {
            return null;
        }

        return new LockedTarget(
            reader.GetInt64("inventory_id"),
            reader.GetInt32("item_id"),
            ParseTargetType(reader.GetString("target_type")),
            EquipmentEnhancementEngine.ResolveEquipmentTier(reader.GetInt32("required_level")),
            Math.Clamp(reader.GetInt32("maximum_enhancement"), 1, 10),
            NullableInt32(reader, "base_durability"),
            reader.GetInt64("slot_version"));
    }

    private static async Task EnsureEquipmentInstanceAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedTarget target,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`equipment_instances`
                (`inventory_id`,`character_id`,`item_id`,`catalog_type`,`enhancement_level`,`refinement_level`,
                 `current_durability`,`maximum_durability`,`maximum_durability_penalty`,`socket_count`,`enabled`,`admin_note`)
            SELECT inventory_row.`inventory_id`,inventory_row.`character_id`,inventory_row.`item_id`,@catalogType,0,0,
                   @baseDurability,@baseDurability,0,0,1,'首次強化時建立正式裝備實例'
            FROM `god2_player`.`character_inventory` inventory_row
            WHERE inventory_row.`inventory_id`=@inventoryId
            ON DUPLICATE KEY UPDATE
                `current_durability`=COALESCE(`current_durability`,VALUES(`current_durability`)),
                `maximum_durability`=COALESCE(`maximum_durability`,VALUES(`maximum_durability`)),
                `enabled`=1;
            """;
        command.Parameters.AddWithValue("@catalogType", target.TargetType.ToString());
        command.Parameters.AddWithValue("@baseDurability", DbValue(target.BaseDurability));
        command.Parameters.AddWithValue("@inventoryId", target.InventoryId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<EquipmentEnhancementState?> LockEquipmentStateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedTarget target,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `inventory_id`,`item_id`,`catalog_type`,`enhancement_level`,`current_durability`,
                   `maximum_durability`,`maximum_durability_penalty`
            FROM `god2_player`.`equipment_instances`
            WHERE `inventory_id`=@inventoryId AND `enabled`=1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@inventoryId", target.InventoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EquipmentEnhancementState(
                reader.GetInt64("inventory_id"),
                reader.GetInt32("item_id"),
                ParseTargetType(reader.GetString("catalog_type")),
                target.EquipmentTier,
                reader.GetInt32("enhancement_level"),
                target.MaximumEnhancement,
                NullableInt32(reader, "current_durability"),
                NullableInt32(reader, "maximum_durability"),
                reader.GetInt32("maximum_durability_penalty"))
            : null;
    }

    private static async Task ConsumeMaterialAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedMaterial material,
        long nextInventoryVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            UPDATE `god2_player`.`character_inventory`
            SET `quantity`=`quantity`-1,`inventory_version`=@nextVersion,`slot_version`=`slot_version`+1,
                `enabled`=CASE WHEN `quantity`=1 THEN 0 ELSE 1 END,
                `deleted_at_utc`=CASE WHEN `quantity`=1 THEN UTC_TIMESTAMP(6) ELSE NULL END,
                `updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `inventory_id`=@inventoryId AND `quantity`=@quantity AND `slot_version`=@slotVersion;
            """;
        command.Parameters.AddWithValue("@nextVersion", nextInventoryVersion);
        command.Parameters.AddWithValue("@inventoryId", material.InventoryId);
        command.Parameters.AddWithValue("@quantity", material.Quantity);
        command.Parameters.AddWithValue("@slotVersion", material.SlotVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Enhancement material changed during transaction.");
        }
    }

    private static async Task ApplyTargetResultAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LockedTarget target,
        EquipmentEnhancementState before,
        EquipmentEnhancementEvaluation evaluation,
        long nextInventoryVersion,
        CancellationToken cancellationToken)
    {
        if (evaluation.TargetDestroyed)
        {
            await using (var equipped = connection.CreateCommand())
            {
                equipped.Transaction = transaction;
                equipped.CommandTimeout = CommandTimeoutSeconds;
                equipped.CommandText = """
                    UPDATE `god2_player`.`character_equipment`
                    SET `enabled`=0,`admin_note`='一般強化失敗，裝備已毀損'
                    WHERE `inventory_id`=@inventoryId AND `enabled`=1;
                    """;
                equipped.Parameters.AddWithValue("@inventoryId", target.InventoryId);
                await equipped.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var instance = connection.CreateCommand())
            {
                instance.Transaction = transaction;
                instance.CommandTimeout = CommandTimeoutSeconds;
                instance.CommandText = "UPDATE `god2_player`.`equipment_instances` SET `enabled`=0,`admin_note`='一般強化失敗，裝備已毀損',`updated_at_utc`=UTC_TIMESTAMP(6) WHERE `inventory_id`=@inventoryId;";
                instance.Parameters.AddWithValue("@inventoryId", target.InventoryId);
                if (await instance.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("Destroyed equipment instance was not updated.");
                }
            }

            await using var destroyedInventory = connection.CreateCommand();
            destroyedInventory.Transaction = transaction;
            destroyedInventory.CommandTimeout = CommandTimeoutSeconds;
            destroyedInventory.CommandText = """
                UPDATE `god2_player`.`character_inventory`
                SET `quantity`=0,`enabled`=0,`inventory_version`=@nextVersion,`slot_version`=`slot_version`+1,
                    `admin_note`='一般強化失敗，裝備已毀損',`deleted_at_utc`=UTC_TIMESTAMP(6),`updated_at_utc`=UTC_TIMESTAMP(6)
                WHERE `inventory_id`=@inventoryId AND `slot_version`=@slotVersion AND `enabled`=1;
                """;
            destroyedInventory.Parameters.AddWithValue("@nextVersion", nextInventoryVersion);
            destroyedInventory.Parameters.AddWithValue("@inventoryId", target.InventoryId);
            destroyedInventory.Parameters.AddWithValue("@slotVersion", target.SlotVersion);
            if (await destroyedInventory.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Destroyed inventory equipment was not updated.");
            }
            return;
        }

        if (!evaluation.EnhancementSucceeded)
        {
            return;
        }

        await using (var instance = connection.CreateCommand())
        {
            instance.Transaction = transaction;
            instance.CommandTimeout = CommandTimeoutSeconds;
            instance.CommandText = """
                UPDATE `god2_player`.`equipment_instances`
                SET `enhancement_level`=@enhancementLevel,`current_durability`=@currentDurability,
                    `maximum_durability`=@maximumDurability,`maximum_durability_penalty`=@durabilityPenalty,
                    `updated_at_utc`=UTC_TIMESTAMP(6)
                WHERE `inventory_id`=@inventoryId AND `enhancement_level`=@beforeLevel AND `enabled`=1;
                """;
            instance.Parameters.AddWithValue("@enhancementLevel", evaluation.UpdatedTarget.EnhancementLevel);
            instance.Parameters.AddWithValue("@currentDurability", DbValue(evaluation.UpdatedTarget.CurrentDurability));
            instance.Parameters.AddWithValue("@maximumDurability", DbValue(evaluation.UpdatedTarget.MaximumDurability));
            instance.Parameters.AddWithValue("@durabilityPenalty", evaluation.UpdatedTarget.MaximumDurabilityPenalty);
            instance.Parameters.AddWithValue("@inventoryId", target.InventoryId);
            instance.Parameters.AddWithValue("@beforeLevel", before.EnhancementLevel);
            if (await instance.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Equipment enhancement state changed during transaction.");
            }
        }

        await using (var equipped = connection.CreateCommand())
        {
            equipped.Transaction = transaction;
            equipped.CommandTimeout = CommandTimeoutSeconds;
            equipped.CommandText = """
                UPDATE `god2_player`.`character_equipment`
                SET `enhancement_level`=@enhancementLevel,`durability`=@currentDurability
                WHERE `inventory_id`=@inventoryId AND `enabled`=1;
                """;
            equipped.Parameters.AddWithValue("@enhancementLevel", evaluation.UpdatedTarget.EnhancementLevel);
            equipped.Parameters.AddWithValue("@currentDurability", DbValue(evaluation.UpdatedTarget.CurrentDurability));
            equipped.Parameters.AddWithValue("@inventoryId", target.InventoryId);
            await equipped.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var updatedInventory = connection.CreateCommand();
        updatedInventory.Transaction = transaction;
        updatedInventory.CommandTimeout = CommandTimeoutSeconds;
        updatedInventory.CommandText = """
            UPDATE `god2_player`.`character_inventory`
            SET `inventory_version`=@nextVersion,`slot_version`=`slot_version`+1,`updated_at_utc`=UTC_TIMESTAMP(6)
            WHERE `inventory_id`=@inventoryId AND `slot_version`=@slotVersion AND `enabled`=1;
            """;
        updatedInventory.Parameters.AddWithValue("@nextVersion", nextInventoryVersion);
        updatedInventory.Parameters.AddWithValue("@inventoryId", target.InventoryId);
        updatedInventory.Parameters.AddWithValue("@slotVersion", target.SlotVersion);
        if (await updatedInventory.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Enhanced inventory equipment was not updated.");
        }
    }

    private static async Task AdvanceInventoryVersionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long characterId,
        long beforeVersion,
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
        command.Parameters.AddWithValue("@beforeVersion", beforeVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Inventory version changed during equipment enhancement.");
        }
    }

    private static async Task InsertReplayAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        string payloadHash,
        EquipmentEnhancementTransactionResult result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO `god2_player`.`equipment_enhancement_idempotency`
                (`idempotency_key_hash`,`enhancement_fingerprint_sha256`,`transaction_id`,`character_id`,`material_item_id`,`target_inventory_id`,
                 `inventory_version_before`,`inventory_version_after`,`enhancement_succeeded`,`target_destroyed`,
                 `enhancement_level_before`,`enhancement_level_after`,`applied_increment`,`success_rate_basis_points`,
                 `current_durability_after`,`maximum_durability_after`,`created_at_utc`,`completed_at_utc`)
            VALUES
                (@keyHash,@payloadHash,@transactionId,@characterId,@materialItemId,@targetInventoryId,
                 @versionBefore,@versionAfter,@succeeded,@destroyed,@levelBefore,@levelAfter,@increment,@rate,
                 @currentDurability,@maximumDurability,@createdAtUtc,UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue("@keyHash", Hash(request.IdempotencyKey));
        command.Parameters.AddWithValue("@payloadHash", payloadHash);
        command.Parameters.AddWithValue("@transactionId", request.TransactionId.ToString());
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@materialItemId", request.MaterialItemTemplateId);
        command.Parameters.AddWithValue("@targetInventoryId", request.TargetInventoryId);
        command.Parameters.AddWithValue("@versionBefore", result.InventoryVersionBefore);
        command.Parameters.AddWithValue("@versionAfter", result.InventoryVersionAfter);
        command.Parameters.AddWithValue("@succeeded", result.EnhancementSucceeded);
        command.Parameters.AddWithValue("@destroyed", result.TargetDestroyed);
        command.Parameters.AddWithValue("@levelBefore", result.EnhancementLevelBefore);
        command.Parameters.AddWithValue("@levelAfter", result.EnhancementLevelAfter);
        command.Parameters.AddWithValue("@increment", result.AppliedIncrement);
        command.Parameters.AddWithValue("@rate", result.SuccessRateBasisPoints);
        command.Parameters.AddWithValue("@currentDurability", DbValue(result.CurrentDurabilityAfter));
        command.Parameters.AddWithValue("@maximumDurability", DbValue(result.MaximumDurabilityAfter));
        command.Parameters.AddWithValue("@createdAtUtc", request.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        LockedMaterial material,
        LockedTarget target,
        EquipmentEnhancementTransactionResult result,
        CancellationToken cancellationToken)
    {
        await InsertAuditRowAsync(
            connection, transaction, request, result, request.MaterialItemTemplateId, material.InventoryId,
            checked((int)material.Quantity), checked((int)material.Quantity - 1), cancellationToken);

        if (result.EnhancementSucceeded || result.TargetDestroyed)
        {
            await InsertAuditRowAsync(
                connection, transaction, request, result, target.ItemTemplateId, target.InventoryId,
                1, result.TargetDestroyed ? 0 : 1, cancellationToken);
        }
    }

    private static async Task InsertAuditRowAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        EquipmentEnhancementTransactionRequest request,
        EquipmentEnhancementTransactionResult result,
        int itemTemplateId,
        long inventoryId,
        int quantityBefore,
        int quantityAfter,
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
                (@auditId,@transactionId,@safeId,@characterId,@sessionId,'裝備強化','裝備強化',
                 NULL,@itemId,@inventoryId,@quantityBefore,@quantityAfter,'無',0,0,@versionBefore,@versionAfter,
                 '成功','',@createdAtUtc,UTC_TIMESTAMP(6),@transactionId);
            """;
        command.Parameters.AddWithValue("@auditId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@transactionId", request.TransactionId.ToString());
        command.Parameters.AddWithValue("@safeId", Hash(request.IdempotencyKey)[..16]);
        command.Parameters.AddWithValue("@characterId", request.CharacterId);
        command.Parameters.AddWithValue("@sessionId", request.SessionId);
        command.Parameters.AddWithValue("@itemId", itemTemplateId);
        command.Parameters.AddWithValue("@inventoryId", inventoryId);
        command.Parameters.AddWithValue("@quantityBefore", quantityBefore);
        command.Parameters.AddWithValue("@quantityAfter", quantityAfter);
        command.Parameters.AddWithValue("@versionBefore", result.InventoryVersionBefore);
        command.Parameters.AddWithValue("@versionAfter", result.InventoryVersionAfter);
        command.Parameters.AddWithValue("@createdAtUtc", request.CreatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static EquipmentEnhancementTargetType ParseTargetType(string value) => value switch
    {
        "武器" => EquipmentEnhancementTargetType.Weapon,
        "裝備" => EquipmentEnhancementTargetType.Equipment,
        _ when Enum.TryParse<EquipmentEnhancementTargetType>(value, ignoreCase: false, out var parsed) => parsed,
        _ => throw new InvalidOperationException("Unsupported equipment enhancement target type.")
    };

    private static int? NullableInt32(MySqlDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetInt32(name);

    private static object DbValue(int? value) => value is int number ? number : DBNull.Value;

    private static string PayloadHash(EquipmentEnhancementTransactionRequest request) => Hash(string.Join('|',
        request.TransactionId,
        request.CharacterId,
        request.AccountId,
        request.SessionId,
        request.MaterialItemTemplateId,
        request.TargetInventoryId));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static EquipmentEnhancementTransactionResult Failed(EquipmentEnhancementTransactionRequest request, string code) =>
        new(request.TransactionId, false, 0, 0, false, false, 0, 0, 0, 0, null, null, code);

    private sealed record LockedMaterial(long InventoryId, long Quantity, long SlotVersion);

    private sealed record LockedTarget(
        long InventoryId,
        int ItemTemplateId,
        EquipmentEnhancementTargetType TargetType,
        int EquipmentTier,
        int MaximumEnhancement,
        int? BaseDurability,
        long SlotVersion);
}
