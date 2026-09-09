using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbPhysicalSkillDamageCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbPhysicalSkillDamageCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<PhysicalSkillDamageCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `skill_family`, `skill_tier`, `minimum_multiplier`, `midpoint_multiplier`,
                   `maximum_multiplier`, `enabled`
            FROM `god2_game`.`physical_skill_damage_coefficients`
            WHERE `enabled`=1
            ORDER BY `skill_family`, `skill_tier`;
            """;

        var rules = new List<PhysicalSkillDamageCoefficient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new PhysicalSkillDamageCoefficient(
                ParseSkillFamily(reader.GetString("skill_family")),
                reader.GetInt32("skill_tier"),
                reader.GetDecimal("minimum_multiplier"),
                reader.GetDecimal("midpoint_multiplier"),
                reader.GetDecimal("maximum_multiplier"),
                "RuntimeEnabled",
                1,
                "god2_game.physical_skill_damage_coefficients",
                null,
                reader.GetBoolean("enabled")));
        }

        return new PhysicalSkillDamageCatalog(rules);
    }

    private static PhysicalSkillFamily ParseSkillFamily(string value) => value switch
    {
        "刀技" => PhysicalSkillFamily.Blade,
        "劍技" => PhysicalSkillFamily.Sword,
        "杖技" => PhysicalSkillFamily.Staff,
        "鞭技" => PhysicalSkillFamily.Whip,
        "槍技" => PhysicalSkillFamily.Spear,
        "飛刀技" => PhysicalSkillFamily.ThrowingKnife,
        _ when Enum.TryParse<PhysicalSkillFamily>(value, ignoreCase: false, out var parsed) => parsed,
        _ => throw new InvalidOperationException($"Unsupported physical skill family '{value}'.")
    };
}