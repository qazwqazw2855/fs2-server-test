using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMerchantPurchaseWriter
    : IMerchantPurchaseWriter
{
    private readonly string _connectionString;
    private readonly MariaDbInventoryGrantWriter _grants;
    private readonly IMerchantPurchaseEvidenceGate _evidence;

    public MariaDbMerchantPurchaseWriter(
        MariaDbAuthenticationOptions options,
        IMerchantPurchaseEvidenceGate evidence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(evidence);
        _connectionString = options.BuildConnectionString();
        _grants = new MariaDbInventoryGrantWriter(options);
        _evidence = evidence;
    }

    public async ValueTask<MerchantPurchaseResult> PurchaseAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.InventoryId == Guid.Empty ||
            request.CharacterId <= 0 || request.MerchantId <= 0 ||
            request.ItemId <= 0 || request.ItemId > int.MaxValue ||
            request.Quantity != 1 ||
            request.ExpectedInventoryVersion < 0 ||
            request.ExpectedMutationSequence < 0 ||
            request.ExpectedWalletVersion < 0 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 256)
            throw new ArgumentException("Invalid merchant purchase request.");

        var key = MerchantPurchaseRequestIdentity.Key(request);
        var fingerprint = MerchantPurchaseRequestIdentity.Fingerprint(request);

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
            command.Parameters.AddWithValue("@key", key);
            return command;
        }

        MerchantPurchaseResult Reject(MerchantPurchaseStatus status,
            InventoryGrantStatus? inventoryFailure = null) =>
            new(status, request.TransactionId, -1, -1, -1, -1, -1,
                inventoryFailure);

        try
        {
            var result = await Execute();
            if (result.Status == MerchantPurchaseStatus.Purchased)
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

        async ValueTask<MerchantPurchaseResult> Execute()
        {
            using (var command = Command("""
                SELECT character_id FROM god2_player.characters
                WHERE character_id=@character AND deleted_at_utc IS NULL
                FOR UPDATE;
                """))
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is null)
                    return Reject(MerchantPurchaseStatus.CharacterMissing);
            }

            using (var command = Command("""
                SELECT TransactionFingerprintSha256,TransactionId,
                       CharacterId,OperationType,Result,
                       CurrencyBefore,CurrencyAfter,
                       InventoryVersionBefore,InventoryVersionAfter
                FROM god2_player.inventory_transaction_idempotency
                WHERE IdempotencyKeyHash=@key FOR UPDATE;
                """))
            {
                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetString(0) != fingerprint ||
                        reader.GetInt64(2) != request.CharacterId ||
                        reader.GetString(3) != "V2MerchantBuy" ||
                        reader.GetString(4) != "成功")
                        return Reject(MerchantPurchaseStatus.IdempotencyConflict);

                    return new MerchantPurchaseResult(
                        MerchantPurchaseStatus.Replayed, reader.GetGuid(1),
                        reader.GetInt64(5), reader.GetInt64(6),
                        checked(request.ExpectedWalletVersion + 1),
                        reader.GetInt64(7), reader.GetInt64(8));
                }
            }

            long price;
            int pack;
            using (var command = Command("""
                SELECT m.enabled,i.enabled,i.selling_price,
                       i.pack_count,i.quantity_limit
                FROM god2_game.merchants m
                JOIN god2_game.merchant_inventory i
                  ON i.merchant_id=m.merchant_id
                WHERE m.merchant_id=@merchant AND i.item_id=@item
                LOCK IN SHARE MODE;
                """))
            {
                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Reject(MerchantPurchaseStatus.ListingMissing);
                if (!reader.GetBoolean(0) || !reader.GetBoolean(1))
                    return Reject(MerchantPurchaseStatus.ListingDisabled);
                if (reader.IsDBNull(2) || reader.IsDBNull(3))
                    return Reject(MerchantPurchaseStatus.PricingBlocked);
                price = reader.GetInt64(2);
                pack = reader.GetInt32(3);
                if (price <= 0 || pack != 1 ||
                    !reader.IsDBNull(4) && reader.GetInt32(4) < 1)
                    return Reject(MerchantPurchaseStatus.PricingBlocked);
            }

            // Current internal slice uses Gold only after explicit gate approval.
            var quote = new MerchantPurchaseQuote(
                request.MerchantId, request.ItemId, "Gold", price, pack, true);
            if (!await _evidence.IsApprovedAsync(
                    request, quote, cancellationToken))
                return Reject(MerchantPurchaseStatus.EvidenceBlocked);

            // Match character -> inventory -> wallet ordering.
            using (var command = Command("""
                SELECT InventoryVersion FROM god2_player.player_inventory_state
                WHERE CharacterId=@character FOR UPDATE;
                """))
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is null)
                    return Reject(MerchantPurchaseStatus.InventoryRejected,
                        InventoryGrantStatus.InventoryMissing);
            }

            long balance;
            long walletVersion;
            using (var command = Command("""
                SELECT Balance,Version
                FROM god2_player.player_currency_balances
                WHERE CharacterId=@character AND CurrencyType='Gold'
                FOR UPDATE;
                """))
            {
                await using var reader =
                    await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Reject(MerchantPurchaseStatus.WalletMissing);
                balance = reader.GetInt64(0);
                walletVersion = reader.GetInt64(1);
            }

            if (balance < 0 || walletVersion < 0)
                throw new InvalidDataException("Invalid wallet state.");
            if (walletVersion != request.ExpectedWalletVersion)
                return Reject(MerchantPurchaseStatus.WalletVersionConflict);
            if (balance < price)
                return Reject(MerchantPurchaseStatus.InsufficientFunds);

            var nextWalletVersion = checked(walletVersion + 1);
            var after = balance - price;

            // The grant's separate namespace is internal to this purchase.
            // Its writes, wallet and purchase receipt share one transaction.
            var grant = await _grants.GrantInTransactionAsync(
                connection, transaction,
                new InventoryGrantRequest(
                    request.TransactionId,
                    "MerchantPurchaseGrant:" + key,
                    "MerchantPurchase:" + request.MerchantId,
                    request.CharacterId, request.InventoryId,
                    request.ExpectedInventoryVersion,
                    request.ExpectedMutationSequence,
                    request.ItemId, 1),
                cancellationToken);
            if (grant.Status != InventoryGrantStatus.Granted)
                return Reject(MerchantPurchaseStatus.InventoryRejected, grant.Status);

            using (var command = Command("""
                UPDATE god2_player.player_currency_balances
                SET Balance=@after,Version=@next,DirtyState='乾淨',
                    UpdatedAtUtc=UTC_TIMESTAMP(6)
                WHERE CharacterId=@character AND CurrencyType='Gold'
                  AND Version=@expected AND Balance=@before;
                """))
            {
                command.Parameters.AddWithValue("@after", after);
                command.Parameters.AddWithValue("@next", nextWalletVersion);
                command.Parameters.AddWithValue("@expected", walletVersion);
                command.Parameters.AddWithValue("@before", balance);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Wallet CAS failed.");
            }

            using (var command = Command("""
                INSERT INTO god2_player.inventory_transaction_idempotency
                  (IdempotencyKeyHash,TransactionFingerprintSha256,TransactionId,
                   CharacterId,OperationType,Result,FailureCode,
                   InventoryVersionBefore,InventoryVersionAfter,
                   CurrencyBefore,CurrencyAfter,CreatedAtUtc,CompletedAtUtc)
                VALUES
                  (@key,@fingerprint,@transaction,@character,
                   'V2MerchantBuy','成功','',@invBefore,@invAfter,
                   @before,@after,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                """))
            {
                command.Parameters.AddWithValue("@fingerprint", fingerprint);
                command.Parameters.AddWithValue(
                    "@transaction", request.TransactionId.ToString("D"));
                command.Parameters.AddWithValue("@invBefore", grant.VersionBefore);
                command.Parameters.AddWithValue("@invAfter", grant.VersionAfter);
                command.Parameters.AddWithValue("@before", balance);
                command.Parameters.AddWithValue("@after", after);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // Append a wallet audit entry alongside the grant's slot audit.
            using (var command = Command("""
                INSERT INTO god2_player.inventory_audit_ledger
                  (AuditId,TransactionId,IdempotencySafeId,CharacterId,
                   SessionId,OperationType,Source,ItemTemplateId,InventoryItemId,
                   QuantityBefore,QuantityAfter,CurrencyType,
                   CurrencyBefore,CurrencyAfter,
                   InventoryVersionBefore,InventoryVersionAfter,
                   Result,FailureCode,CreatedAtUtc,CompletedAtUtc,CorrelationId)
                VALUES
                  (@audit,@transaction,@safe,@character,'ServerV2',
                   'V2MerchantBuy',@source,@item,NULL,0,1,'Gold',
                   @before,@after,@invBefore,@invAfter,'成功','',
                   UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),@transaction);
                """))
            {
                command.Parameters.AddWithValue("@audit", Guid.NewGuid().ToString("D"));
                command.Parameters.AddWithValue(
                    "@transaction", request.TransactionId.ToString("D"));
                command.Parameters.AddWithValue("@safe", key[..32]);
                command.Parameters.AddWithValue(
                    "@source", "MerchantPurchase:" + request.MerchantId);
                command.Parameters.AddWithValue("@before", balance);
                command.Parameters.AddWithValue("@after", after);
                command.Parameters.AddWithValue("@invBefore", grant.VersionBefore);
                command.Parameters.AddWithValue("@invAfter", grant.VersionAfter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new MerchantPurchaseResult(
                MerchantPurchaseStatus.Purchased, request.TransactionId,
                balance, after, nextWalletVersion,
                grant.VersionBefore, grant.VersionAfter);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
