using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class XjzItemEffectRuntimeReconciliationMigrationTests
{
    [Fact]
    public void Migration423ReconcilesXjzItemEffectCandidatesWithoutGuessingMissingValues()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "423_reconcile_xjz_item_effect_candidates_with_runtime.sql"));

        Assert.Contains("xjz_item_effect_runtime_reconciliation", sql, StringComparison.Ordinal);
        Assert.Contains("vw_xjz_item_effect_runtime_reconciliation_readable", sql, StringComparison.Ordinal);
        Assert.Contains("runtime_effect.item_id=expected.formal_item_id", sql, StringComparison.Ordinal);
        Assert.Contains("候選缺少可執行數值", sql, StringComparison.Ordinal);
        Assert.Contains("不能猜數值，留待官方封包或黑箱測試。", sql, StringComparison.Ordinal);
        Assert.Contains("已實裝且數值一致", sql, StringComparison.Ordinal);
        Assert.Contains("正式服務端缺效果", sql, StringComparison.Ordinal);
        Assert.Contains("正式服務端道具ID", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_description_json", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\ufffd', sql);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
