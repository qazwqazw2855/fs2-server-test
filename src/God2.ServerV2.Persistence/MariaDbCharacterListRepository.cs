using God2.ServerV2.Application;
using MySqlConnector;

namespace God2.ServerV2.Persistence;

public sealed class MariaDbCharacterListRepository :
    ICharacterListRepository
{
    private readonly string _connectionString;

    public MariaDbCharacterListRepository(
        MariaDbAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = options.BuildConnectionString();
    }

    public async ValueTask<IReadOnlyList<CharacterListEntry>> ListByAccountAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        await using var connection =
            new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                character_id,
                account_id,
                name,
                class_code,
                gender_code,
                life_skill_code,
                level,
                appearance_code,
                map_id,
                position_x,
                position_y,
                created_at_utc,
                last_played_at_utc
            FROM god2_player.characters
            WHERE account_id = @accountId
              AND enabled = 1
              AND status NOT IN ('Deleted', '已刪除')
              AND deleted_at_utc IS NULL
            ORDER BY created_at_utc, character_id;
            """;
        command.Parameters.AddWithValue("@accountId", accountId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var characterIdOrdinal = reader.GetOrdinal("character_id");
        var accountIdOrdinal = reader.GetOrdinal("account_id");
        var nameOrdinal = reader.GetOrdinal("name");
        var classCodeOrdinal = reader.GetOrdinal("class_code");
        var genderCodeOrdinal = reader.GetOrdinal("gender_code");
        var lifeSkillCodeOrdinal = reader.GetOrdinal("life_skill_code");
        var levelOrdinal = reader.GetOrdinal("level");
        var appearanceCodeOrdinal = reader.GetOrdinal("appearance_code");
        var mapIdOrdinal = reader.GetOrdinal("map_id");
        var positionXOrdinal = reader.GetOrdinal("position_x");
        var positionYOrdinal = reader.GetOrdinal("position_y");
        var createdAtUtcOrdinal = reader.GetOrdinal("created_at_utc");
        var lastPlayedAtUtcOrdinal = reader.GetOrdinal("last_played_at_utc");

        var characters = new List<CharacterListEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(new CharacterListEntry(
                reader.GetInt64(characterIdOrdinal),
                reader.GetInt64(accountIdOrdinal),
                reader.GetString(nameOrdinal),
                GetNullableString(reader, classCodeOrdinal),
                GetNullableString(reader, genderCodeOrdinal),
                GetNullableString(reader, lifeSkillCodeOrdinal),
                GetNullableInt32(reader, levelOrdinal),
                GetNullableString(reader, appearanceCodeOrdinal),
                GetNullableInt64(reader, mapIdOrdinal),
                GetNullableInt32(reader, positionXOrdinal),
                GetNullableInt32(reader, positionYOrdinal),
                GetUtcDateTimeOffset(reader, createdAtUtcOrdinal),
                GetNullableUtcDateTimeOffset(reader, lastPlayedAtUtcOrdinal)));
        }

        return characters;
    }

    private static string? GetNullableString(
        MySqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);

    private static int? GetNullableInt32(
        MySqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);

    private static long? GetNullableInt64(
        MySqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt64(ordinal);

    private static DateTimeOffset GetUtcDateTimeOffset(
        MySqlDataReader reader,
        int ordinal) =>
        new(DateTime.SpecifyKind(
            reader.GetDateTime(ordinal),
            DateTimeKind.Utc));

    private static DateTimeOffset? GetNullableUtcDateTimeOffset(
        MySqlDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : GetUtcDateTimeOffset(reader, ordinal);
}
