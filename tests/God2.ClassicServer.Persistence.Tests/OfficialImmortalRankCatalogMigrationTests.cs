using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialImmortalRankCatalogMigrationTests
{
    private const string ExactSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";

    [Fact]
    public void Exact_client_GodLevel_section_contains_the_complete_four_rank_identity_catalog()
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "MapIdentityRecovery", "takeover-godlevel.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal("GodLevel", root.GetProperty("section").GetString());
        Assert.Equal(ExactSourceSha256, root.GetProperty("sourceSha256").GetString());
        Assert.Equal(4, root.GetProperty("declaredCount").GetInt32());
        Assert.Equal(4, root.GetProperty("exportedCount").GetInt32());

        var rows = root.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Collection(rows,
            row => AssertRow(row, 22872, 0, "小仙位", "1"),
            row => AssertRow(row, 22873, 1, "正仙位", "2"),
            row => AssertRow(row, 22874, 2, "强仙位", "3"),
            row => AssertRow(row, 22875, 3, "齐仙位", "4"));
    }

    [Fact]
    public void Migration_publishes_only_rank_identity_and_keeps_progression_unassigned()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "138_publish_verified_immortal_rank_catalog.sql"));

        Assert.Contains("(1,'小仙位','小仙位',1,0,22872)", sql, StringComparison.Ordinal);
        Assert.Contains("(2,'正仙位','正仙位',2,1,22873)", sql, StringComparison.Ordinal);
        Assert.Contains("(3,'強仙位','强仙位',3,2,22874)", sql, StringComparison.Ordinal);
        Assert.Contains("(4,'齊仙位','齐仙位',4,3,22875)", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'VerifiedOfficialClientStatic',0,1", sql, StringComparison.Ordinal);
        Assert.Contains("NULL,NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_immortal_ranks_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `god2_game`.`immortal_templates`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("character_immortals", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRow(
        JsonElement row,
        int sourceLine,
        int rowIndex,
        string name,
        string rank)
    {
        Assert.Equal(sourceLine, row.GetProperty("sourceLine").GetInt32());
        Assert.Equal(rowIndex, row.GetProperty("sectionRowIndex").GetInt32());
        var fields = row.GetProperty("fields");
        Assert.Equal(name, fields[0].GetString());
        Assert.Equal(rank, fields[1].GetString());
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
