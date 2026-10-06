using System.Text.Json;
using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMerchantPurchaseJournal(
    MariaDbAuthenticationOptions options) : IMerchantPurchaseJournal
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask SaveAsync(
        MerchantPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            INSERT INTO god2_player.v2_merchant_purchase_journal
                (IdempotencyKeyHash,TransactionFingerprintSha256,
                 TransactionId,CharacterId,RequestJson,CreatedAtUtc)
            VALUES (@key,@fingerprint,@transaction,@character,
                    @request,UTC_TIMESTAMP(6));
            """;
        command.Parameters.AddWithValue(
            "@key", MerchantPurchaseRequestIdentity.Key(request));
        command.Parameters.AddWithValue(
            "@fingerprint", MerchantPurchaseRequestIdentity.Fingerprint(request));
        command.Parameters.AddWithValue(
            "@transaction", request.TransactionId.ToString("D"));
        command.Parameters.AddWithValue("@character", request.CharacterId);
        command.Parameters.AddWithValue(
            "@request", MerchantPurchaseRequestIdentity.Serialize(request));

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            // No updates: an existing request remains immutable.
            var existing = await FindAsync(
                request.CharacterId, request.TransactionId, cancellationToken);
            if (existing != request)
                throw new InvalidDataException(
                    "Merchant purchase journal identity conflict.", exception);
        }
    }

    public async ValueTask<MerchantPurchaseRequest?> FindAsync(
        long characterId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0 || transactionId == Guid.Empty)
            throw new ArgumentException("Invalid journal lookup identity.");

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT IdempotencyKeyHash,TransactionFingerprintSha256,
                   TransactionId,CharacterId,RequestJson
            FROM god2_player.v2_merchant_purchase_journal
            WHERE CharacterId=@character AND TransactionId=@transaction
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("@character", characterId);
        command.Parameters.AddWithValue(
            "@transaction", transactionId.ToString("D"));

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var key = reader.GetString(0);
        var fingerprint = reader.GetString(1);
        var storedTransaction = reader.GetGuid(2);
        var storedCharacter = reader.GetInt64(3);
        var request = JsonSerializer.Deserialize<MerchantPurchaseRequest>(
            reader.GetString(4))
            ?? throw new InvalidDataException("Journal request is missing.");

        Validate(request);
        if (request.CharacterId != characterId ||
            request.CharacterId != storedCharacter ||
            request.TransactionId != transactionId ||
            request.TransactionId != storedTransaction ||
            !string.Equals(key, MerchantPurchaseRequestIdentity.Key(request),
                StringComparison.Ordinal) ||
            !string.Equals(fingerprint,
                MerchantPurchaseRequestIdentity.Fingerprint(request),
                StringComparison.Ordinal) ||
            await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("Journal request integrity mismatch.");

        return request;
    }

    private static void Validate(MerchantPurchaseRequest request)
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
            throw new ArgumentException("Invalid merchant journal request.");
    }
}
