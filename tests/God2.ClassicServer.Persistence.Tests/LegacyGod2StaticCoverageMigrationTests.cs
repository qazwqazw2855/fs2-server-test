using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class LegacyGod2StaticCoverageMigrationTests
{
    [Fact]
    public void Migration413PublishesStaticCoverageAndMovesRuntimeTablesOutOfGameData()
    {
        var schema = ReadSchema("413_publish_legacy_god2_static_table_coverage.sql");

        Assert.Contains("legacy_god2_static_table_coverage", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_table_name", schema, StringComparison.Ordinal);
        Assert.Contains("formal_table_name", schema, StringComparison.Ordinal);
        Assert.Contains("coverage_status_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("next_verification_step_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("monster_combat_runtime_state", schema, StringComparison.Ordinal);
        Assert.Contains("quest_operation_idempotency", schema, StringComparison.Ordinal);
        Assert.Contains("skill_idempotency", schema, StringComparison.Ordinal);
        Assert.Contains("suggested_target_schema = 'god2_player'", schema, StringComparison.Ordinal);
        Assert.Contains("npc_dialogs", schema, StringComparison.Ordinal);
        Assert.Contains("monster_drops", schema, StringComparison.Ordinal);
        Assert.Contains("quest_rewards", schema, StringComparison.Ordinal);
        Assert.Contains("skill_client_metadata", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_drop_now", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP DATABASE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM `god2`", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE `god2`", schema, StringComparison.OrdinalIgnoreCase);
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
