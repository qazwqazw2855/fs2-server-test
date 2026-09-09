using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class XjzItemEffectFormalLinkMigrationTests
{
    [Fact]
    public void Migration422LinksClientItemEffectCandidatesThroughFormalClientItemId()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "422_link_xjz_item_effect_candidates_to_formal_items.sql"));

        Assert.Contains("ADD COLUMN IF NOT EXISTS formal_item_id", sql, StringComparison.Ordinal);
        Assert.Contains("ON formal_item.client_item_id=candidate.item_id", sql, StringComparison.Ordinal);
        Assert.Contains("已建立 1664 筆可執行候選，其中 1552 筆已對到正式道具主表", sql, StringComparison.Ordinal);
        Assert.Contains("補血道具", sql, StringComparison.Ordinal);
        Assert.Contains("能力提升藥品", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("formal_item.item_id=candidate.item_id", sql, StringComparison.Ordinal);
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
