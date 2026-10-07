using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterInventoryItemIdentityRepository(
    MariaDbAuthenticationOptions options)
    : ICharacterInventoryItemIdentityRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
        .BuildConnectionString();

    public async ValueTask<CharacterInventoryItemIdentity?> GetBySlotAsync(
        long characterId,
        int authoritySlotIndex,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0 || authoritySlotIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = """
            SELECT character_id,inventory_id,slot_index,item_id,quantity,
                   slot_version,bind_state,bound,item_instance_metadata
            FROM god2_player.character_inventory
            WHERE character_id=@character AND slot_index=@slot
              AND enabled=1 AND deleted_at_utc IS NULL
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("@character", characterId);
        command.Parameters.AddWithValue("@slot", authoritySlotIndex);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var quantity = reader.GetInt64(4);
        if (quantity <= 0 || quantity > int.MaxValue)
            throw new InvalidDataException("Invalid inventory identity quantity.");

        var identity = new CharacterInventoryItemIdentity(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            (int)quantity,
            reader.GetInt64(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetBoolean(7),
            reader.IsDBNull(8) ? "null" : reader.GetString(8));

        if (identity.CharacterId != characterId ||
            identity.SlotIndex != authoritySlotIndex ||
            identity.ItemInstanceId <= 0 ||
            identity.ItemId <= 0 ||
            identity.SlotVersion < 0 ||
            await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException(
                "Invalid or ambiguous inventory item identity.");

        return identity;
    }
}
