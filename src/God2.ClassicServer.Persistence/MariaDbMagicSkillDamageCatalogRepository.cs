using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbMagicSkillDamageCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbMagicSkillDamageCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<MagicSkillDamageCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `attack_element`, `skill_tier`, `minimum_multiplier`, `midpoint_multiplier`,
                   `maximum_multiplier`, `attacker_same_element_divisor`,
                   `target_weak_element_divisor`, `target_counter_element_divisor`,
                   `target_same_element_divisor`, `pet_meditation_multiplier`,
                   `xiandao_meditation_level1_bonus`, `xiandao_meditation_level2_bonus`,
                   `enabled`
            FROM `god2_game`.`magic_skill_damage_coefficients`
            WHERE `enabled`=1
            ORDER BY `attack_element`, `skill_tier`;
            """;

        var rules = new List<MagicSkillDamageCoefficient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new MagicSkillDamageCoefficient(
                ParseBattleElement(reader.GetString("attack_element")),
                reader.GetInt32("skill_tier"),
                reader.GetDecimal("minimum_multiplier"),
                reader.GetDecimal("midpoint_multiplier"),
                reader.GetDecimal("maximum_multiplier"),
                reader.GetDecimal("attacker_same_element_divisor"),
                reader.GetDecimal("target_weak_element_divisor"),
                reader.GetDecimal("target_counter_element_divisor"),
                reader.GetDecimal("target_same_element_divisor"),
                reader.GetDecimal("pet_meditation_multiplier"),
                reader.IsDBNull(reader.GetOrdinal("xiandao_meditation_level1_bonus"))
                    ? null
                    : reader.GetDecimal("xiandao_meditation_level1_bonus"),
                reader.IsDBNull(reader.GetOrdinal("xiandao_meditation_level2_bonus"))
                    ? null
                    : reader.GetDecimal("xiandao_meditation_level2_bonus"),
                "RuntimeEnabled",
                "FormalCompatibilityAccepted",
                "god2_game.magic_skill_damage_coefficients",
                reader.GetBoolean("enabled")));
        }

        return new MagicSkillDamageCatalog(rules);
    }

    private static BattleElement ParseBattleElement(string value) => value switch
    {
        "金" => BattleElement.Metal,
        "木" => BattleElement.Wood,
        "水" => BattleElement.Water,
        "火" => BattleElement.Fire,
        "土" => BattleElement.Earth,
        _ when Enum.TryParse<BattleElement>(value, ignoreCase: false, out var parsed) => parsed,
        _ => throw new InvalidOperationException($"Unsupported magic skill attack element '{value}'.")
    };
}