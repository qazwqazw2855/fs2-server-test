using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbPetGrowthGradeCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbPetGrowthGradeCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<PetGrowthGradeCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT `growth_grade`,`minimum_level`,`maximum_level`,`automatic_points_per_level`,
                   `manual_points_per_level`,`initial_quality`,`breakthrough_level`,
                   `enabled`
            FROM `god2_game`.`pet_growth_grade_rules`
            WHERE `enabled`=1
            ORDER BY FIELD(`growth_grade`,'Normal','Top','LateBreakthrough','Breakthrough'),`minimum_level`;
            """;

        var rules = new List<PetGrowthGradeRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new PetGrowthGradeRule(
                Parse<PetGrowthGrade>(reader.GetString("growth_grade")),
                reader.GetInt32("minimum_level"),
                reader.GetInt32("maximum_level"),
                reader.GetInt32("automatic_points_per_level"),
                reader.GetInt32("manual_points_per_level"),
                Parse<PetInitialGrowthQuality>(reader.GetString("initial_quality")),
                reader.IsDBNull(reader.GetOrdinal("breakthrough_level"))
                    ? null
                    : reader.GetInt32("breakthrough_level"),
                "RuntimeEnabled",
                "god2_game.pet_growth_grade_rules",
                reader.GetBoolean("enabled")));
        }
        await reader.DisposeAsync();

        var allocations = new List<PetAutomaticGrowthAllocation>();
        await using var allocationCommand = connection.CreateCommand();
        allocationCommand.CommandTimeout = CommandTimeoutSeconds;
        allocationCommand.CommandText = """
            SELECT category_row.`category_id`,allocation.`growth_grade`,allocation.`minimum_level`,allocation.`maximum_level`,
                   allocation.`constitution_delta`,allocation.`strength_delta`,allocation.`intelligence_delta`,allocation.`speed_delta`,
                   allocation.`enabled`
            FROM `god2_game`.`pet_categories` category_row
            JOIN `god2_game`.`pet_automatic_growth_allocations` allocation
              ON allocation.`growth_archetype_id`=category_row.`growth_archetype_id`
            WHERE allocation.`enabled`=1
            ORDER BY category_row.`category_id`,FIELD(allocation.`growth_grade`,'Normal','Top','Breakthrough','LateBreakthrough'),allocation.`minimum_level`;
            """;
        await using var allocationReader = await allocationCommand.ExecuteReaderAsync(cancellationToken);
        while (await allocationReader.ReadAsync(cancellationToken))
        {
            allocations.Add(new PetAutomaticGrowthAllocation(
                allocationReader.GetInt32("category_id"),
                Parse<PetGrowthGrade>(allocationReader.GetString("growth_grade")),
                allocationReader.GetInt32("minimum_level"),
                allocationReader.GetInt32("maximum_level"),
                allocationReader.GetInt32("constitution_delta"),
                allocationReader.GetInt32("strength_delta"),
                allocationReader.GetInt32("intelligence_delta"),
                allocationReader.GetInt32("speed_delta"),
                "RuntimeEnabled",
                "god2_game.pet_automatic_growth_allocations",
                allocationReader.GetBoolean("enabled")));
        }

        return new PetGrowthGradeCatalog(rules, allocations);
    }

    private static T Parse<T>(string value)
        where T : struct =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported pet growth value '{value}'.");
}
