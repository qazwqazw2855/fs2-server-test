using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterInventorySnapshotRepository(
    MariaDbAuthenticationOptions options)
    : ICharacterInventorySnapshotRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<CharacterInventorySnapshot?> GetByCharacterAsync(
        long characterId,
        CancellationToken cancellationToken)
    {
        if (characterId <= 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.InventoryId, s.Capacity, s.InventoryVersion,
                   s.MutationSequence, s.DirtyState,
                   i.slot_index, i.item_id, i.quantity,
                   i.bind_state, i.item_instance_metadata
            FROM god2_player.player_inventory_state AS s
            LEFT JOIN god2_player.character_inventory AS i
                ON i.character_id = s.CharacterId
               AND i.enabled = 1
               AND i.deleted_at_utc IS NULL
            WHERE s.CharacterId = @characterId
            ORDER BY i.slot_index;
            """;
        command.Parameters.AddWithValue("@characterId", characterId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var inventoryId = reader.GetGuid(0);
        var capacity = reader.GetInt32(1);
        var version = reader.GetInt64(2);
        var mutationSequence = reader.GetInt64(3);
        var dirtyState = reader.GetString(4);
        var slots = new List<CharacterInventorySlot>();

        do
        {
            if (!reader.IsDBNull(5))
            {
                slots.Add(new CharacterInventorySlot(
                    reader.GetInt32(5),
                    reader.GetInt64(6),
                    reader.GetInt32(7),
                    reader.GetString(8),
                    reader.GetString(9)));
            }
        }
        while (await reader.ReadAsync(cancellationToken));

        if (capacity <= 0)
            throw new InvalidDataException(
                $"Invalid inventory capacity: character={characterId}");

        var seenSlots = new HashSet<int>();
        foreach (var slot in slots)
        {
            if (slot.SlotIndex < 0 ||
                slot.SlotIndex >= capacity ||
                slot.ItemId <= 0 ||
                slot.Quantity <= 0 ||
                !seenSlots.Add(slot.SlotIndex))
            {
                throw new InvalidDataException(
                    $"Invalid inventory slot: character={characterId}; " +
                    $"slot={slot.SlotIndex}");
            }
        }

        return new CharacterInventorySnapshot(
            inventoryId,
            characterId,
            capacity,
            version,
            mutationSequence,
            dirtyState,
            slots);
    }
}
