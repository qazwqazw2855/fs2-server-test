using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class XjzExecutableItemEffectCandidateMigrationTests
{
    [Fact]
    public void Migration421IntakesExecutableItemEffectCandidatesWithoutPersistingRawDescriptions()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "421_intake_xjz_executable_item_effect_candidates.sql"));

        Assert.Contains("xjz_executable_item_effect_candidates", sql, StringComparison.Ordinal);
        Assert.Contains("xjz_executable_item_effect_candidate_summary", sql, StringComparison.Ordinal);
        Assert.Contains("補血道具", sql, StringComparison.Ordinal);
        Assert.Contains("補魔道具", sql, StringComparison.Ordinal);
        Assert.Contains("解除異常狀態道具", sql, StringComparison.Ordinal);
        Assert.Contains("能力提升藥品", sql, StringComparison.Ordinal);
        Assert.Contains("餵食寵物或坐騎道具", sql, StringComparison.Ordinal);
        var persistedTableDefinition = sql[..sql.IndexOf("CREATE TABLE IF NOT EXISTS god2_research.xjz_executable_item_effect_candidate_summary", StringComparison.Ordinal)];
        Assert.DoesNotContain("raw_description_json", persistedTableDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TEMPORARY TABLE IF EXISTS xjz_executable_item_rule_stage", sql, StringComparison.Ordinal);
        Assert.DoesNotContain('\ufffd', sql);
        Assert.Equal(1664, Regex.Matches(sql, @"INSERT INTO xjz_executable_item_rule_stage VALUES").Count);
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
