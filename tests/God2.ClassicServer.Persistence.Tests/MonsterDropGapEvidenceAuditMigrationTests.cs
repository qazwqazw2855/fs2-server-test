using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class MonsterDropGapEvidenceAuditMigrationTests
{
    [Fact]
    public void Migration418PublishesMonsterDropGapAuditWithoutGuessingMonsterDrops()
    {
        var schema = ReadSchema("418_publish_monster_drop_gap_evidence_audit.sql");

        Assert.Contains("monster_drop_gap_evidence_audit", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_relationship_id", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_monster_id", schema, StringComparison.Ordinal);
        Assert.Contains("formal_monster_id", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_item_id", schema, StringComparison.Ordinal);
        Assert.Contains("effective_drop_chance", schema, StringComparison.Ordinal);
        Assert.Contains("缺正式怪物對應", schema, StringComparison.Ordinal);
        Assert.Contains("不能把未知怪物 ID 當正式怪物", schema, StringComparison.Ordinal);
        Assert.Contains("不得把舊怪物 ID 當正式怪物 ID", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_apply_automatically = 0", schema, StringComparison.Ordinal);
        Assert.Contains("缺正式怪物對應，暫停自動補", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO god2_game.monster_drops", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`monster_drops`", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`monster_drops`", schema, StringComparison.OrdinalIgnoreCase);
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
