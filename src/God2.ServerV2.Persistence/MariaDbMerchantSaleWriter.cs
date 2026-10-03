using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMerchantSaleWriter : IMerchantSaleWriter
{
    private readonly string _connectionString;
    private readonly IMerchantSaleEvidenceGate _evidence;

    public MariaDbMerchantSaleWriter(
        MariaDbAuthenticationOptions options,
        IMerchantSaleEvidenceGate evidence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(evidence);
        _connectionString = options.BuildConnectionString();
        _evidence = evidence;
    }

    public async ValueTask<MerchantSaleResult> SellAsync(
        MerchantSaleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.InventoryId == Guid.Empty ||
            request.CharacterId <= 0 || request.MerchantId <= 0 ||
            request.ItemId <= 0 || request.ItemId > int.MaxValue ||
            request.ItemInstanceId <= 0 || request.SlotIndex < 0 ||
            request.Quantity != 1 ||
            request.ExpectedInventoryVersion < 0 ||
            request.ExpectedMutationSequence < 0 ||
            request.ExpectedSlotVersion < 0 ||
            request.ExpectedWalletVersion < 0 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 256)
            throw new ArgumentException("Invalid merchant sale request.");

        var key = Hash("God2.ServerV2.MerchantSale/1:" +
            request.CharacterId + ":" + request.IdempotencyKey);
        var fingerprint = Hash(JsonSerializer.Serialize(request));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);

        MySqlCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 15;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@character", request.CharacterId);
            command.Parameters.AddWithValue("@merchant", request.MerchantId);
            command.Parameters.AddWithValue("@item", request.ItemId);
            command.Parameters.AddWithValue("@instance", request.ItemInstanceId);
            command.Parameters.AddWithValue("@slot", request.SlotIndex);
            command.Parameters.AddWithValue("@key", key);
            return command;
        }

        MerchantSaleResult Reject(MerchantSaleStatus status) =>
            new(status, request.TransactionId, -1, -1, -1, -1, -1);

        try
        {
            var result = await Execute();
            if (result.Status == MerchantSaleStatus.Sold)
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

        async ValueTask<MerchantSaleResult> Execute()
        {
            using (var command = Command("""
                SELECT character_id FROM god2_player.characters
                WHERE character_id=@character AND deleted_at_utc IS NULL
                FOR UPDATE;
                """))
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is null)
                    return Reject(MerchantSaleStatus.CharacterMissing);
            }

            using (var command = Command("""
                SELECT TransactionFingerprintSha256,TransactionId,CharacterId,
                       OperationType,Result,CurrencyBefore,CurrencyAfter,
                       InventoryVersionBefore,InventoryVersionAfter
                FROM god2_player.inventory_transaction_idempotency
                WHERE IdempotencyKeyHash=@key FOR UPDATE;
                """))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetString(0) != fingerprint ||
                        reader.GetInt64(2) != request.CharacterId ||
                        reader.GetString(3) != "V2MerchantSell" ||
                        reader.GetString(4) != "成功")
                        return Reject(MerchantSaleStatus.IdempotencyConflict);
                    return new MerchantSaleResult(
                        MerchantSaleStatus.Replayed, reader.GetGuid(1),
                        reader.GetInt64(5), reader.GetInt64(6),
                        checked(request.ExpectedWalletVersion + 1),
                        reader.GetInt64(7), reader.GetInt64(8));
                }
            }

            long price;
            using (var command = Command("""
                SELECT m.enabled,m.buyback_enabled,i.enabled,i.purchasing_price,
                       g.enabled,g.item_category
                FROM god2_game.merchants m
                JOIN god2_game.merchant_inventory i ON i.merchant_id=m.merchant_id
                JOIN god2_game.items g ON g.item_id=i.item_id
                WHERE m.merchant_id=@merchant AND i.item_id=@item
                LOCK IN SHARE MODE;
                """))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Reject(MerchantSaleStatus.ListingMissing);
                if (!reader.GetBoolean(0) || !reader.GetBoolean(2) ||
                    !reader.GetBoolean(4))
                    return Reject(MerchantSaleStatus.ListingDisabled);
                // Conservative internal slice; gate must approve buyback semantics.
                if (reader.IsDBNull(1) || !reader.GetBoolean(1) ||
                    reader.IsDBNull(3) || reader.IsDBNull(5))
                    return Reject(MerchantSaleStatus.PricingBlocked);
                price = reader.GetInt64(3);
                if (price <= 0 || reader.GetString(5) is "Quest" or "任務")
                    return Reject(MerchantSaleStatus.PricingBlocked);
            }

            var quote = new MerchantSaleQuote(
                request.MerchantId, request.ItemId, "Gold", price, true, true);
            if (!await _evidence.IsApprovedAsync(request, quote, cancellationToken))
                return Reject(MerchantSaleStatus.EvidenceBlocked);

            Guid inventoryId;
            int capacity;
            long version, sequence;
            string dirty;
            using (var command = Command("""
                SELECT InventoryId,Capacity,InventoryVersion,MutationSequence,DirtyState
                FROM god2_player.player_inventory_state
                WHERE CharacterId=@character FOR UPDATE;
                """))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Reject(MerchantSaleStatus.InventoryMissing);
                inventoryId = reader.GetGuid(0);
                capacity = reader.GetInt32(1);
                version = reader.GetInt64(2);
                sequence = reader.GetInt64(3);
                dirty = reader.GetString(4);
            }
            if (inventoryId != request.InventoryId ||
                version != request.ExpectedInventoryVersion ||
                sequence != request.ExpectedMutationSequence)
                return Reject(MerchantSaleStatus.InventoryVersionConflict);

            long balance, walletVersion;
            using (var command = Command("""
                SELECT Balance,Version FROM god2_player.player_currency_balances
                WHERE CharacterId=@character AND CurrencyType='Gold' FOR UPDATE;
                """))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Reject(MerchantSaleStatus.WalletMissing);
                balance = reader.GetInt64(0);
                walletVersion = reader.GetInt64(1);
            }
            if (walletVersion != request.ExpectedWalletVersion)
                return Reject(MerchantSaleStatus.WalletVersionConflict);

            var slots = new List<CharacterInventorySlot>();
            long selectedId = 0, selectedVersion = -1;
            var explicitlyUnbound = false;
            using (var command = Command("""
                SELECT inventory_id,slot_index,item_id,quantity,bind_state,
                       item_instance_metadata,slot_version,bound
                FROM god2_player.character_inventory
                WHERE character_id=@character AND enabled=1 AND deleted_at_utc IS NULL
                ORDER BY slot_index FOR UPDATE;
                """))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    slots.Add(new CharacterInventorySlot(
                        reader.GetInt32(1), reader.GetInt64(2),
                        checked((int)reader.GetInt64(3)),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? "null" : reader.GetString(5)));
                    if (reader.GetInt32(1) == request.SlotIndex)
                    {
                        selectedId = reader.GetInt64(0);
                        selectedVersion = reader.GetInt64(6);
                        explicitlyUnbound = !reader.IsDBNull(7) && !reader.GetBoolean(7);
                    }
                }
            }
            if (selectedId == 0)
                return Reject(MerchantSaleStatus.SlotMissing);
            if (selectedId != request.ItemInstanceId ||
                selectedVersion != request.ExpectedSlotVersion)
                return Reject(MerchantSaleStatus.SlotConflict);
            if (!explicitlyUnbound)
                return Reject(MerchantSaleStatus.ItemStateBlocked);

            var plan = MerchantSalePlanner.Plan(
                new CharacterInventorySnapshot(
                    inventoryId, request.CharacterId, capacity, version, sequence, dirty, slots),
                new CharacterWalletSnapshot(request.CharacterId, "Gold", balance, walletVersion),
                quote, request.SlotIndex, request.Quantity);
            if (!plan.Succeeded)
                return Reject(MerchantSaleStatus.ItemStateBlocked);

            var nextVersion = checked(version + 1);
            var nextSequence = checked(sequence + 1);
            var nextSlotVersion = checked(selectedVersion + 1);
            var nextWalletVersion = checked(walletVersion + 1);

            using (var command = Command("""
                UPDATE god2_player.character_inventory
                SET quantity=@quantity,inventory_version=@nextInventory,
                    slot_version=@nextSlot,
                    enabled=IF(@quantity=0,0,1),
                    deleted_at_utc=IF(@quantity=0,UTC_TIMESTAMP(6),NULL),
                    updated_at_utc=UTC_TIMESTAMP(6)
                WHERE inventory_id=@instance AND character_id=@character
                  AND slot_index=@slot AND item_id=@item
                  AND slot_version=@expectedSlot AND quantity=@beforeQuantity
                  AND enabled=1 AND deleted_at_utc IS NULL;
                """))
            {
                command.Parameters.AddWithValue("@quantity", plan.QuantityAfter);
                command.Parameters.AddWithValue("@beforeQuantity", plan.QuantityBefore);
                command.Parameters.AddWithValue("@nextInventory", nextVersion);
                command.Parameters.AddWithValue("@nextSlot", nextSlotVersion);
                command.Parameters.AddWithValue("@expectedSlot", selectedVersion);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Sale slot CAS failed.");
            }

            using (var command = Command("""
                UPDATE god2_player.player_inventory_state
                SET InventoryVersion=@next,MutationSequence=@nextSequence,
                    DirtyState='乾淨',UpdatedAtUtc=UTC_TIMESTAMP(6)
                WHERE CharacterId=@character AND InventoryId=@inventory
                  AND InventoryVersion=@before AND MutationSequence=@sequence;
                """))
            {
                command.Parameters.AddWithValue("@next", nextVersion);
                command.Parameters.AddWithValue("@nextSequence", nextSequence);
                command.Parameters.AddWithValue("@inventory", inventoryId.ToString("D"));
                command.Parameters.AddWithValue("@before", version);
                command.Parameters.AddWithValue("@sequence", sequence);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Sale inventory CAS failed.");
            }

            using (var command = Command("""
                UPDATE god2_player.player_currency_balances
                SET Balance=@after,Version=@next,DirtyState='乾淨',
                    UpdatedAtUtc=UTC_TIMESTAMP(6)
                WHERE CharacterId=@character AND CurrencyType='Gold'
                  AND Version=@expected AND Balance=@before;
                """))
            {
                command.Parameters.AddWithValue("@after", plan.BalanceAfter);
                command.Parameters.AddWithValue("@next", nextWalletVersion);
                command.Parameters.AddWithValue("@expected", walletVersion);
                command.Parameters.AddWithValue("@before", balance);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Sale wallet CAS failed.");
            }

            using (var command = Command("""
                INSERT INTO god2_player.inventory_transaction_idempotency
                  (IdempotencyKeyHash,TransactionFingerprintSha256,TransactionId,
                   CharacterId,OperationType,Result,FailureCode,
                   InventoryVersionBefore,InventoryVersionAfter,
                   CurrencyBefore,CurrencyAfter,CreatedAtUtc,CompletedAtUtc)
                VALUES (@key,@fingerprint,@transaction,@character,
                  'V2MerchantSell','成功','',@beforeVersion,@afterVersion,
                  @beforeBalance,@afterBalance,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                """))
            {
                AddReceipt(command);
                command.Parameters.AddWithValue("@fingerprint", fingerprint);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var command = Command("""
                INSERT INTO god2_player.inventory_audit_ledger
                  (AuditId,TransactionId,IdempotencySafeId,CharacterId,
                   SessionId,OperationType,Source,ItemTemplateId,InventoryItemId,
                   QuantityBefore,QuantityAfter,CurrencyType,CurrencyBefore,CurrencyAfter,
                   InventoryVersionBefore,InventoryVersionAfter,Result,FailureCode,
                   CreatedAtUtc,CompletedAtUtc,CorrelationId)
                VALUES (@audit,@transaction,@safe,@character,'ServerV2',
                  'V2MerchantSell',@source,@item,@instance,@beforeQuantity,@afterQuantity,
                  'Gold',@beforeBalance,@afterBalance,@beforeVersion,@afterVersion,
                  '成功','',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),@transaction);
                """))
            {
                AddReceipt(command);
                command.Parameters.AddWithValue("@audit", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue("@safe", key[..32]);
                command.Parameters.AddWithValue("@source", "MerchantSale:" + request.MerchantId);
                command.Parameters.AddWithValue("@beforeQuantity", plan.QuantityBefore);
                command.Parameters.AddWithValue("@afterQuantity", plan.QuantityAfter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new MerchantSaleResult(
                MerchantSaleStatus.Sold, request.TransactionId,
                balance, plan.BalanceAfter, nextWalletVersion, version, nextVersion);

            void AddReceipt(MySqlCommand command)
            {
                command.Parameters.AddWithValue("@transaction", request.TransactionId.ToString("D"));
                command.Parameters.AddWithValue("@beforeVersion", version);
                command.Parameters.AddWithValue("@afterVersion", nextVersion);
                command.Parameters.AddWithValue("@beforeBalance", balance);
                command.Parameters.AddWithValue("@afterBalance", plan.BalanceAfter);
            }
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
