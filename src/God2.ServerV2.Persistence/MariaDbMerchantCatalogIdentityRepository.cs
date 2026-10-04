using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbMerchantCatalogIdentityRepository(
    MariaDbAuthenticationOptions options) : IMerchantCatalogIdentityRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<MerchantCatalogIdentity?> ResolvePurchaseAsync(
        long merchantId,
        string clientBuildId,
        int clientCatalogIndex,
        long clientItemId,
        CancellationToken cancellationToken)
    {
        if (merchantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(merchantId));
        if (string.IsNullOrWhiteSpace(clientBuildId))
            throw new ArgumentException(
                "Client build id is required.", nameof(clientBuildId));
        if (clientCatalogIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(clientCatalogIndex));
        if (clientItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientItemId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT
                identity_row.merchant_inventory_id,
                inventory_row.merchant_id,
                inventory_row.item_id,
                identity_row.client_catalog_index,
                identity_row.client_item_id
            FROM god2_game.merchant_client_catalog_identities identity_row
            JOIN god2_game.merchant_inventory inventory_row
              ON inventory_row.merchant_inventory_id =
                 identity_row.merchant_inventory_id
            JOIN god2_game.merchants merchant_row
              ON merchant_row.merchant_id =
                 inventory_row.merchant_id
            JOIN god2_game.items item_row
              ON item_row.item_id =
                 inventory_row.item_id
            WHERE identity_row.client_build_id = @build
              AND inventory_row.merchant_id = @merchant
              AND identity_row.client_catalog_index = @catalogIndex
              AND identity_row.client_item_id = @clientItem
              AND identity_row.enabled = 1
              AND inventory_row.enabled = 1
              AND merchant_row.enabled = 1
              AND item_row.enabled = 1
            LIMIT 2;
            """;

        command.Parameters.AddWithValue("@build", clientBuildId);
        command.Parameters.AddWithValue("@merchant", merchantId);
        command.Parameters.AddWithValue("@catalogIndex", clientCatalogIndex);
        command.Parameters.AddWithValue("@clientItem", clientItemId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var result = new MerchantCatalogIdentity(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetInt64(4));

        // Fail closed if formal authority is ambiguous.
        if (await reader.ReadAsync(cancellationToken))
            return null;

        return result;
    }
}
