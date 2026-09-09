using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class StaticGapRemediationQueueMigrationTests
{
    [Fact]
    public void Migration414PublishesStaticGapRemediationQueueWithEvidencePolicy()
    {
        var schema = ReadSchema("414_publish_static_gap_remediation_queue.sql");

        Assert.Contains("static_gap_remediation_queue", schema, StringComparison.Ordinal);
        Assert.Contains("remediation_category_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("evidence_policy_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("dependency_check_zh_tw", schema, StringComparison.Ordinal);
        Assert.Contains("safe_to_apply_automatically", schema, StringComparison.Ordinal);
        Assert.Contains("npc_dialogs", schema, StringComparison.Ordinal);
        Assert.Contains("monster_skills", schema, StringComparison.Ordinal);
        Assert.Contains("quest_rewards", schema, StringComparison.Ordinal);
        Assert.Contains("status_effects", schema, StringComparison.Ordinal);
        Assert.Contains("container_rewards", schema, StringComparison.Ordinal);
        Assert.Contains("merchant_inventory", schema, StringComparison.Ordinal);
        Assert.Contains("monster_drops", schema, StringComparison.Ordinal);
        Assert.Contains("不得猜值", schema, StringComparison.Ordinal);
        Assert.Contains("未知機器碼、payload、hash 不可進正式表", schema, StringComparison.Ordinal);
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
