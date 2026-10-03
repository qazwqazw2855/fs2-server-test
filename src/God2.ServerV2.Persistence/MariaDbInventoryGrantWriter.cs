using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbInventoryGrantWriter(
    MariaDbAuthenticationOptions options) : IInventoryGrantWriter
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask<InventoryGrantResult> GrantAsync(
        InventoryGrantRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            var result = await GrantInTransactionAsync(
                connection, transaction, request, cancellationToken);
            if (result.Status == InventoryGrantStatus.Granted)
                await transaction.CommitAsync(cancellationToken);
            else
                await transaction.RollbackAsync(CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // Caller owns commit/rollback. Any exception requires rolling back the
    // transaction. This overload also supports rollback-only integration tests.
    public async ValueTask<InventoryGrantResult> GrantInTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        InventoryGrantRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection != connection)
            throw new ArgumentException("Transaction connection mismatch.");

        MySqlCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", request.CharacterId);
            return command;
        }

        InventoryGrantResult Reject(InventoryGrantStatus status) =>
            new(status, request.TransactionId, -1, -1);

        // Match the existing writer's character -> inventory lock order.
        using (var command = Command("""
            SELECT character_id FROM god2_player.characters
            WHERE character_id=@character AND deleted_at_utc IS NULL
            FOR UPDATE;
            """))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is null)
                return Reject(InventoryGrantStatus.CharacterMissing);
        }

        // Namespace separates V2 grants from Classic transaction keys.
        var keyHash = Hash("God2.ServerV2.InventoryGrant/1:" + request.IdempotencyKey);
        var fingerprint = Hash(JsonSerializer.Serialize(request));

        using (var command = Command("""
            SELECT TransactionFingerprintSha256, TransactionId,
                   InventoryVersionBefore, InventoryVersionAfter,
                   CharacterId, OperationType, Result
            FROM god2_player.inventory_transaction_idempotency
            WHERE IdempotencyKeyHash=@key
            FOR UPDATE;
            """))
        {
            command.Parameters.AddWithValue("@key", keyHash);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(0) != fingerprint ||
                    reader.GetInt64(4) != request.CharacterId ||
                    reader.GetString(5) != "V2ItemGrant" ||
                    reader.GetString(6) != "成功")
                    return Reject(InventoryGrantStatus.IdempotencyConflict);

                return new InventoryGrantResult(
                    InventoryGrantStatus.Replayed,
                    reader.GetGuid(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3));
            }
        }

        Guid inventoryId;
        int capacity;
        long version;
        long sequence;
        string dirtyState;

        using (var command = Command("""
            SELECT InventoryId, Capacity, InventoryVersion,
                   MutationSequence, DirtyState
            FROM god2_player.player_inventory_state
            WHERE CharacterId=@character FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Reject(InventoryGrantStatus.InventoryMissing);
            inventoryId = reader.GetGuid(0);
            capacity = reader.GetInt32(1);
            version = reader.GetInt64(2);
            sequence = reader.GetInt64(3);
            dirtyState = reader.GetString(4);
        }

        if (inventoryId != request.InventoryId ||
            version != request.ExpectedVersion ||
            sequence != request.ExpectedMutationSequence)
            return Reject(InventoryGrantStatus.VersionConflict);

        ItemStackRule rule;
        using (var command = Command("""
            SELECT item_id, name_zh_tw, maximum_stack, enabled
            FROM god2_game.items WHERE item_id=@item LOCK IN SHARE MODE;
            """))
        {
            command.Parameters.AddWithValue("@item", request.ItemId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Reject(InventoryGrantStatus.ItemMissing);
            rule = new ItemStackRule(
                reader.GetInt64(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetBoolean(3));
        }

        var active = new List<CharacterInventorySlot>();
        var rows = new Dictionary<int, (long Id, long Version, bool Active)>();
        using (var command = Command("""
            SELECT inventory_id, slot_index, item_id, quantity,
                   bind_state, item_instance_metadata, slot_version,
                   enabled, deleted_at_utc
            FROM god2_player.character_inventory
            WHERE character_id=@character
            ORDER BY slot_index FOR UPDATE;
            """))
        {
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var index = reader.GetInt32(1);
                var isActive = reader.GetBoolean(7) && reader.IsDBNull(8);
                rows.Add(index, (
                    reader.GetInt64(0), reader.GetInt64(6), isActive));
                if (isActive)
                {
                    active.Add(new CharacterInventorySlot(
                        index, reader.GetInt64(2),
                        checked((int)reader.GetInt64(3)),
                        reader.GetString(4), reader.GetString(5)));
                }
            }
        }

        var snapshot = new CharacterInventorySnapshot(
            inventoryId, request.CharacterId, capacity, version,
            sequence, dirtyState, active);
        var plan = InventoryGrantPlanner.Plan(
            snapshot, rule, request.Quantity, "Unbound", "{}");

        if (!plan.Succeeded)
            return Reject(plan.Failure switch
            {
                InventoryGrantFailure.ItemDisabled =>
                    InventoryGrantStatus.ItemDisabled,
                InventoryGrantFailure.InsufficientCapacity =>
                    InventoryGrantStatus.InsufficientCapacity,
                _ => throw new InvalidDataException("Unexpected grant failure.")
            });

        var nextVersion = checked(version + 1);
        var nextSequence = checked(sequence + 1);

        // Preflight slot-version overflow before any writes.
        foreach (var slot in plan.ChangedSlots)
            if (rows.TryGetValue(slot.SlotIndex, out var row) && row.Active)
                _ = checked(row.Version + 1);

        foreach (var slot in plan.ChangedSlots)
        {
            rows.TryGetValue(slot.SlotIndex, out var row);
            long instanceId;

            if (row.Active)
            {
                instanceId = row.Id;
                using var command = Command("""
                    UPDATE god2_player.character_inventory
                    SET quantity=@quantity, inventory_version=@version,
                        slot_version=slot_version+1, updated_at_utc=UTC_TIMESTAMP(6)
                    WHERE inventory_id=@id AND character_id=@character
                      AND enabled=1 AND deleted_at_utc IS NULL;
                    """);
                command.Parameters.AddWithValue("@quantity", slot.Quantity);
                command.Parameters.AddWithValue("@version", nextVersion);
                command.Parameters.AddWithValue("@id", instanceId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Inventory slot update failed.");
            }
            else
            {
                // Reuse the slot index with a NEW item identity. Its previous
                // identity remains in historical audit records.
                if (rows.ContainsKey(slot.SlotIndex))
                {
                    using var remove = Command("""
                        DELETE FROM god2_player.character_inventory
                        WHERE inventory_id=@id AND character_id=@character
                          AND (enabled=0 OR deleted_at_utc IS NOT NULL);
                        """);
                    remove.Parameters.AddWithValue("@id", row.Id);
                    if (await remove.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidDataException("Inactive slot changed.");
                }

                using (var reserve = Command("""
                    INSERT INTO god2_player.inventory_item_identity_sequence
                        (ReservedAtUtc) VALUES (UTC_TIMESTAMP(6));
                    """))
                {
                    await reserve.ExecuteNonQueryAsync(cancellationToken);
                    instanceId = reserve.LastInsertedId;
                    if (instanceId <= 0)
                        throw new InvalidDataException("Invalid reserved item ID.");
                }

                using var insert = Command("""
                    INSERT INTO god2_player.character_inventory
                        (inventory_id, character_id, slot_index, item_id,
                         quantity, inventory_version, slot_version, bound,
                         bind_state, item_instance_metadata, enabled,
                         created_at_utc, updated_at_utc)
                    VALUES (@id,@character,@slot,@item,@quantity,@version,
                            1,0,'Unbound','{}',1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                    """);
                insert.Parameters.AddWithValue("@id", instanceId);
                insert.Parameters.AddWithValue("@slot", slot.SlotIndex);
                insert.Parameters.AddWithValue("@item", request.ItemId);
                insert.Parameters.AddWithValue("@quantity", slot.Quantity);
                insert.Parameters.AddWithValue("@version", nextVersion);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            var beforeQuantity = active
                .FirstOrDefault(value => value.SlotIndex == slot.SlotIndex)
                ?.Quantity ?? 0;

            using var audit = Command("""
                INSERT INTO god2_player.inventory_audit_ledger
                    (AuditId,TransactionId,IdempotencySafeId,CharacterId,
                     SessionId,OperationType,Source,ItemTemplateId,
                     InventoryItemId,QuantityBefore,QuantityAfter,
                     CurrencyType,CurrencyBefore,CurrencyAfter,
                     InventoryVersionBefore,InventoryVersionAfter,
                     Result,FailureCode,CreatedAtUtc,CompletedAtUtc,CorrelationId)
                VALUES (@audit,@transaction,@safe,@character,
                        'ServerV2','V2ItemGrant',@source,@item,
                        @instance,@beforeQuantity,@afterQuantity,
                        '無',0,0,@beforeVersion,@afterVersion,
                        '成功','',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),@correlation);
                """);
            audit.Parameters.AddWithValue("@audit", Guid.NewGuid().ToString());
            audit.Parameters.AddWithValue("@transaction", request.TransactionId.ToString());
            audit.Parameters.AddWithValue("@safe", keyHash[..32]);
            audit.Parameters.AddWithValue("@source", request.SourceReference);
            audit.Parameters.AddWithValue("@item", request.ItemId);
            audit.Parameters.AddWithValue("@instance", instanceId);
            audit.Parameters.AddWithValue("@beforeQuantity", beforeQuantity);
            audit.Parameters.AddWithValue("@afterQuantity", slot.Quantity);
            audit.Parameters.AddWithValue("@beforeVersion", version);
            audit.Parameters.AddWithValue("@afterVersion", nextVersion);
            audit.Parameters.AddWithValue("@correlation", request.TransactionId.ToString());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var state = Command("""
            UPDATE god2_player.player_inventory_state
            SET InventoryVersion=@after, MutationSequence=@sequence,
                DirtyState='乾淨', UpdatedAtUtc=UTC_TIMESTAMP(6)
            WHERE CharacterId=@character AND InventoryId=@inventory
              AND InventoryVersion=@before AND MutationSequence=@beforeSequence;
            """))
        {
            state.Parameters.AddWithValue("@after", nextVersion);
            state.Parameters.AddWithValue("@sequence", nextSequence);
            state.Parameters.AddWithValue("@inventory", inventoryId.ToString());
            state.Parameters.AddWithValue("@before", version);
            state.Parameters.AddWithValue("@beforeSequence", sequence);
            if (await state.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("Inventory version update failed.");
        }

        using (var replay = Command("""
            INSERT INTO god2_player.inventory_transaction_idempotency
                (IdempotencyKeyHash,TransactionFingerprintSha256,TransactionId,
                 CharacterId,OperationType,Result,FailureCode,
                 InventoryVersionBefore,InventoryVersionAfter,
                 CurrencyBefore,CurrencyAfter,CreatedAtUtc,CompletedAtUtc)
            VALUES (@key,@fingerprint,@transaction,@character,
                    'V2ItemGrant','成功','',@before,@after,
                    0,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
            """))
        {
            replay.Parameters.AddWithValue("@key", keyHash);
            replay.Parameters.AddWithValue("@fingerprint", fingerprint);
            replay.Parameters.AddWithValue("@transaction", request.TransactionId.ToString());
            replay.Parameters.AddWithValue("@before", version);
            replay.Parameters.AddWithValue("@after", nextVersion);
            await replay.ExecuteNonQueryAsync(cancellationToken);
        }

        return new InventoryGrantResult(
            InventoryGrantStatus.Granted,
            request.TransactionId, version, nextVersion);
    }

    private static void Validate(InventoryGrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.InventoryId == Guid.Empty ||
            request.CharacterId <= 0 ||
            request.ItemId <= 0 || request.ItemId > int.MaxValue ||
            request.Quantity <= 0 ||
            request.ExpectedVersion < 0 ||
            request.ExpectedMutationSequence < 0 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 256 ||
            string.IsNullOrWhiteSpace(request.SourceReference) ||
            request.SourceReference.Length > 128)
            throw new ArgumentException("Invalid server inventory grant request.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
