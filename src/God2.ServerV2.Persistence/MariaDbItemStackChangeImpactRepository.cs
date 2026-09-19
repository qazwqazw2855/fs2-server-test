using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbItemStackChangeImpactRepository(
    MariaDbAuthenticationOptions options)
    : IItemStackChangeImpactRepository
{
    private readonly string _connectionString =
        (options ?? throw new ArgumentNullException(nameof(options)))
            .BuildConnectionString();

    public async ValueTask<IReadOnlyList<ItemStackChangeImpact>> ListByItemAsync(
        long itemId,
        int newMaximumStack,
        CancellationToken cancellationToken)
    {
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));
        if (newMaximumStack <= 0)
            throw new ArgumentOutOfRangeException(nameof(newMaximumStack));

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.character_id, i.quantity, s.Capacity,
                   (SELECT COUNT(*)
                    FROM god2_player.character_inventory AS occupied
                    WHERE occupied.character_id = i.character_id
                      AND occupied.enabled = 1
                      AND occupied.deleted_at_utc IS NULL) AS UsedSlots
            FROM god2_player.character_inventory AS i
            LEFT JOIN god2_player.player_inventory_state AS s
                ON s.CharacterId = i.character_id
            WHERE i.item_id = @itemId
              AND i.enabled = 1
              AND i.deleted_at_utc IS NULL
            ORDER BY i.character_id, i.slot_index;
            """;
        command.Parameters.AddWithValue("@itemId", itemId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var impacts = new List<ItemStackChangeImpact>();
        long? currentCharacterId = null;
        var freeSlots = 0;
        var additionalSlots = 0;

        void FinishCharacter()
        {
            if (currentCharacterId is long id)
                impacts.Add(new ItemStackChangeImpact(
                    id,
                    new ItemStackChangePreview(
                        additionalSlots,
                        freeSlots,
                        additionalSlots <= freeSlots)));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var characterId = reader.GetInt64(0);
            var quantity = reader.GetInt32(1);

            if (reader.IsDBNull(2) || quantity <= 0)
                throw new InvalidDataException(
                    $"Invalid inventory state: character={characterId}");

            var capacity = reader.GetInt32(2);
            var occupied = checked((int)reader.GetInt64(3));
            if (capacity <= 0 || occupied > capacity)
                throw new InvalidDataException(
                    $"Invalid inventory capacity: character={characterId}");

            if (currentCharacterId != characterId)
            {
                FinishCharacter();
                currentCharacterId = characterId;
                freeSlots = capacity - occupied;
                additionalSlots = 0;
            }

            additionalSlots = checked(
                additionalSlots + (quantity - 1) / newMaximumStack);
        }

        FinishCharacter();
        return impacts;
    }
}
