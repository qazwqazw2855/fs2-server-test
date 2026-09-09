using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class LegacyGod2ConsolidationMigrationTests
{
    [Fact]
    public void Migration411PublishesLegacyGod2TableInventoryWithoutDroppingData()
    {
        var schema = ReadSchema("411_publish_legacy_god2_table_consolidation_inventory.sql");

        Assert.Contains("legacy_god2_table_consolidation_inventory", schema, StringComparison.Ordinal);
        Assert.Contains("suggested_target_schema", schema, StringComparison.Ordinal);
        Assert.Contains("game_function_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_drop_now", schema, StringComparison.Ordinal);
        Assert.Contains("'god2_player'", schema, StringComparison.Ordinal);
        Assert.Contains("'god2_game'", schema, StringComparison.Ordinal);
        Assert.Contains("'god2_research'", schema, StringComparison.Ordinal);
        Assert.Contains("'god2_game_meta'", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_drop_now = 0", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP DATABASE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM god2.", schema, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSchema(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(baseDirectory))
        {
            var candidate = Path.Combine(baseDirectory, "database", "schema", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            baseDirectory = Directory.GetParent(baseDirectory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate database/schema/{fileName}");
    }
}
