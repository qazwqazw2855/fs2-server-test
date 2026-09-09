using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence;

public sealed class MariaDbEquipmentEnhancementCatalogRepository : MariaDbRuntimeRepository
{
    public MariaDbEquipmentEnhancementCatalogRepository(DatabaseOptions options)
        : base(options)
    {
    }

    public async Task<EquipmentEnhancementCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var materials = new List<EquipmentEnhancementMaterialDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `material_item_id`,`client_item_id`,`name_zh_tw`,`target_type`,`equipment_tier`,`grade`,
                       `minimum_increment`,`maximum_increment`,`maximum_durability_loss`,`failure_policy`,
                       `enabled`
                FROM `god2_game`.`equipment_enhancement_materials`
                WHERE `enabled`=1
                ORDER BY `client_item_id`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                materials.Add(new EquipmentEnhancementMaterialDefinition(
                    reader.GetInt32("material_item_id"),
                    reader.GetInt32("client_item_id"),
                    reader.GetString("name_zh_tw"),
                    ParseTargetType(reader.GetString("target_type")),
                    reader.GetInt32("equipment_tier"),
                    ParseGrade(reader.GetString("grade")),
                    reader.GetInt32("minimum_increment"),
                    reader.GetInt32("maximum_increment"),
                    reader.GetInt32("maximum_durability_loss"),
                    ParseFailurePolicy(reader.GetString("failure_policy")),
                    "RuntimeEnabled",
                    "god2_game.equipment_enhancement_materials",
                    reader.GetBoolean("enabled")));
            }
        }

        var rates = new List<EquipmentEnhancementRateDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT `grade`,`target_enhancement_level`,`success_rate_basis_points`,
                       `enabled`
                FROM `god2_game`.`equipment_enhancement_rates`
                WHERE `enabled`=1
                ORDER BY `grade`,`target_enhancement_level`;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rates.Add(new EquipmentEnhancementRateDefinition(
                    ParseGrade(reader.GetString("grade")),
                    reader.GetInt32("target_enhancement_level"),
                    reader.GetInt32("success_rate_basis_points"),
                    "RuntimeEnabled",
                    "god2_game.equipment_enhancement_rates",
                    reader.GetBoolean("enabled")));
            }
        }

        return new EquipmentEnhancementCatalog(materials, rates);
    }

    private static T Parse<T>(string value)
        where T : struct =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported equipment enhancement value '{value}'.");

    private static EquipmentEnhancementTargetType ParseTargetType(string value) => value switch
    {
        "武器" => EquipmentEnhancementTargetType.Weapon,
        "裝備" => EquipmentEnhancementTargetType.Equipment,
        _ => Parse<EquipmentEnhancementTargetType>(value)
    };

    private static EquipmentEnhancementGrade ParseGrade(string value) => value switch
    {
        "一般" => EquipmentEnhancementGrade.General,
        "進階" => EquipmentEnhancementGrade.Advanced,
        "特殊" => EquipmentEnhancementGrade.Special,
        _ => Parse<EquipmentEnhancementGrade>(value)
    };

    private static EquipmentEnhancementFailurePolicy ParseFailurePolicy(string value) => value switch
    {
        "強化失敗破壞目標" => EquipmentEnhancementFailurePolicy.DestroyTarget,
        "強化失敗保留目標" => EquipmentEnhancementFailurePolicy.PreserveTarget,
        _ => Parse<EquipmentEnhancementFailurePolicy>(value)
    };
}