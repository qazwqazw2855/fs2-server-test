using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ContainerGapEvidenceAuditMigrationTests
{
    [Fact]
    public void Migration416PublishesContainerGapAuditWithoutInventingContainerItems()
    {
        var schema = ReadSchema("416_publish_container_gap_evidence_audit.sql");

        Assert.Contains("container_gap_evidence_audit", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_container_item_id", schema, StringComparison.Ordinal);
        Assert.Contains("formal_item_id", schema, StringComparison.Ordinal);
        Assert.Contains("formal_container_id", schema, StringComparison.Ordinal);
        Assert.Contains("legacy_reward_count", schema, StringComparison.Ordinal);
        Assert.Contains("formal_reward_count", schema, StringComparison.Ordinal);
        Assert.Contains("缺正式道具本體，不能建立容器", schema, StringComparison.Ordinal);
        Assert.Contains("不能只用數字 ID 建立正式容器", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_apply_automatically = 0", schema, StringComparison.Ordinal);
        Assert.Contains("部分完成，缺正式容器道具本體", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO god2_game.items", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`items`", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.containers", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`containers`", schema, StringComparison.OrdinalIgnoreCase);
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
