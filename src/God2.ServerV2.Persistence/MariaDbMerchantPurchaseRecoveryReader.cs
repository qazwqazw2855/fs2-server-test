using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public enum MerchantPurchaseRecoveryStatus
{
    Unknown,
    Committed,
    IdentityConflict
}

public sealed record MerchantPurchaseRecoveryReceipt(
    MerchantPurchaseRecoveryStatus Status,
    Guid TransactionId,
    long? BalanceBefore = null,
    long? BalanceAfter = null,
    long? InventoryVersionBefore = null,
    long? InventoryVersionAfter = null);

// Read-only reconciliation. No receipt means unknown, not permission to buy.
// A committed receipt does not prove that the client received a response.
public sealed class MariaDbMerchantPurchaseRecoveryReader(
    MariaDbAuthenticationOptions options)
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask<MerchantPurchaseRecoveryReceipt> ReadAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.CharacterId <= 0 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("Invalid recovery request.");

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT TransactionFingerprintSha256,TransactionId,
                   CharacterId,OperationType,Result,
                   CurrencyBefore,CurrencyAfter,
                   InventoryVersionBefore,InventoryVersionAfter
            FROM god2_player.inventory_transaction_idempotency
            WHERE IdempotencyKeyHash=@key
            LIMIT 2;
            """;
        command.Parameters.AddWithValue(
            "@key", MerchantPurchaseRequestIdentity.Key(request));

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(MerchantPurchaseRecoveryStatus.Unknown,
                request.TransactionId);

        if (!string.Equals(reader.GetString(0),
                MerchantPurchaseRequestIdentity.Fingerprint(request),
                StringComparison.Ordinal) ||
            reader.GetGuid(1) != request.TransactionId ||
            reader.GetInt64(2) != request.CharacterId ||
            reader.GetString(3) != "V2MerchantBuy")
            return new(MerchantPurchaseRecoveryStatus.IdentityConflict,
                request.TransactionId);

        if (reader.GetString(4) != "成功")
            return new(MerchantPurchaseRecoveryStatus.Unknown,
                request.TransactionId);

        for (var index = 5; index <= 8; index++)
            if (reader.IsDBNull(index))
                throw new InvalidDataException("Purchase receipt is incomplete.");

        var receipt = new MerchantPurchaseRecoveryReceipt(
            MerchantPurchaseRecoveryStatus.Committed,
            request.TransactionId,
            reader.GetInt64(5), reader.GetInt64(6),
            reader.GetInt64(7), reader.GetInt64(8));

        if (receipt.BalanceBefore < 0 ||
            receipt.BalanceAfter < 0 ||
            receipt.BalanceAfter > receipt.BalanceBefore ||
            receipt.InventoryVersionBefore != request.ExpectedInventoryVersion ||
            request.ExpectedInventoryVersion == long.MaxValue ||
            receipt.InventoryVersionAfter != request.ExpectedInventoryVersion + 1 ||
            await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("Purchase receipt integrity mismatch.");

        return receipt;
    }
}
