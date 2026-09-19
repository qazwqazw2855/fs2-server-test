using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbItemStackRuleRepository(
    MariaDbAuthenticationOptions options) : IItemStackRuleRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<ItemStackRule?> GetByItemIdAsync(
        long itemId,
        CancellationToken cancellationToken)
    {
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_id, name_zh_tw, maximum_stack, enabled
            FROM god2_game.items
            WHERE item_id = @itemId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@itemId", itemId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var configuredMaximum = reader.IsDBNull(2)
            ? (int?)null
            : reader.GetInt32(2);

        if (configuredMaximum is <= 0)
            throw new InvalidDataException(
                $"Invalid maximum_stack: item={itemId}");

        return new ItemStackRule(
            reader.GetInt32(0),
            reader.GetString(1),
            configuredMaximum,
            reader.GetBoolean(3));
    }
}
