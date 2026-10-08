using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public enum MerchantSaleRecoveryStatus
{
    Unknown,
    Committed,
    IdentityConflict
}

public sealed record MerchantSaleRecoveryReceipt(
    MerchantSaleRecoveryStatus Status,
    Guid TransactionId,
    long? BalanceBefore = null,
    long? BalanceAfter = null,
    long? InventoryVersionBefore = null,
    long? InventoryVersionAfter = null);

// Read-only reconciliation. No receipt means unknown, not permission to sell.
// A committed receipt does not prove that the client received a response.
public sealed class MariaDbMerchantSaleRecoveryReader(
    MariaDbAuthenticationOptions options)
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask<MerchantSaleRecoveryReceipt> ReadAsync(
        MerchantSaleRequest request,
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
            "@key", MerchantSaleRequestIdentity.Key(request));

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(MerchantSaleRecoveryStatus.Unknown,
                request.TransactionId);

        if (!string.Equals(reader.GetString(0),
                MerchantSaleRequestIdentity.Fingerprint(request),
                StringComparison.Ordinal) ||
            reader.GetGuid(1) != request.TransactionId ||
            reader.GetInt64(2) != request.CharacterId ||
            reader.GetString(3) != "V2MerchantSell")
            return new(MerchantSaleRecoveryStatus.IdentityConflict,
                request.TransactionId);

        if (reader.GetString(4) != "成功")
            return new(MerchantSaleRecoveryStatus.Unknown,
                request.TransactionId);

        for (var index = 5; index <= 8; index++)
            if (reader.IsDBNull(index))
                throw new InvalidDataException("Sale receipt is incomplete.");

        var receipt = new MerchantSaleRecoveryReceipt(
            MerchantSaleRecoveryStatus.Committed,
            request.TransactionId,
            reader.GetInt64(5), reader.GetInt64(6),
            reader.GetInt64(7), reader.GetInt64(8));

        if (receipt.BalanceBefore < 0 ||
            receipt.BalanceAfter < 0 ||
            receipt.BalanceAfter <= receipt.BalanceBefore ||
            receipt.InventoryVersionBefore != request.ExpectedInventoryVersion ||
            request.ExpectedInventoryVersion == long.MaxValue ||
            receipt.InventoryVersionAfter != request.ExpectedInventoryVersion + 1 ||
            await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("Sale receipt integrity mismatch.");

        return receipt;
    }
}
