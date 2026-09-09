using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class LegacyGod2ManualReviewClassificationMigrationTests
{
    [Fact]
    public void Migration412ClassifiesRemainingLegacyManualReviewTablesWithoutDroppingData()
    {
        var schema = ReadSchema("412_classify_remaining_legacy_god2_manual_review_tables.sql");

        Assert.Contains("combat_idempotency", schema, StringComparison.Ordinal);
        Assert.Contains("combat_audit", schema, StringComparison.Ordinal);
        Assert.Contains("dialogs", schema, StringComparison.Ordinal);
        Assert.Contains("localization_entries", schema, StringComparison.Ordinal);
        Assert.Contains("MariaDbCombatMutationStore", schema, StringComparison.Ordinal);
        Assert.Contains("god2_game.npc_dialogs", schema, StringComparison.Ordinal);
        Assert.Contains("manual_review base table", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_drop_now = 0", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP DATABASE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM god2.", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", schema, StringComparison.OrdinalIgnoreCase);
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
