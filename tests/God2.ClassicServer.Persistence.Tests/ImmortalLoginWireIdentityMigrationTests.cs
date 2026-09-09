using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ImmortalLoginWireIdentityMigrationTests
{
    private const string SourceSha256 = "42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9";
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "126_publish_verified_immortal_login_wire_identities.sql");

    [Fact]
    public void Migration_publishes_exact_GodEft_login_wire_identities_for_four_recovered_templates()
    {
        var sql = File.ReadAllText(MigrationPath);
        var updates = ParseTemplateUpdates(sql);

        Assert.Equal(4, updates.Count);
        AssertUpdate(updates, 1060004, "immortal_jiang_ziya", 1, 27, "5601", "姜子牙(金)");
        AssertUpdate(updates, 1060005, "immortal_hu_ximei", 10, 36, "5610", "胡喜媚(土)");
        AssertUpdate(updates, 1060003, "immortal_cihang_daoren", 20, 46, "5620", "慈航道人");
        AssertUpdate(updates, 1060002, "immortal_tuxingsun", 28, 54, "5628", "土行孙");

        Assert.Contains("Row 26 names column 1 `神仙ID`", sql, StringComparison.Ordinal);
        Assert.Contains("Wuji row 43 maps `武吉` to 17", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("immortal_wuji'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`resource_id`=17", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_catalog_only_and_fails_closed_on_conflicting_or_unverified_templates()
    {
        var sql = File.ReadAllText(MigrationPath);
        var updates = ParseTemplateUpdates(sql);

        foreach (var update in updates)
        {
            var assignedColumns = Regex.Matches(
                    update.SetClause,
                    @"(?m)^\s*`(?<column>[a-z_]+)`\s*=",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["column"].Value)
                .ToArray();

            Assert.Equal(["resource_id", "admin_note", "updated_at_utc"], assignedColumns);
            Assert.Contains("`enabled`=1", update.WhereClause, StringComparison.Ordinal);
            Assert.Contains("`evidence_status`='Recovered'", update.WhereClause, StringComparison.Ordinal);
            Assert.Matches(
                @"AND \(`resource_id` IS NULL OR `resource_id`=(?<identity>\d+)\)\s*$",
                update.WhereClause);
        }

        Assert.DoesNotContain("UPDATE `god2_game`.`immortal_base_stats`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_immortals`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_game`.`immortal_template_skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_immortal_skills`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertUpdate(
        IReadOnlyList<TemplateUpdate> updates,
        long templateId,
        string code,
        int identity,
        int sourceRow,
        string archiveId,
        string originalName)
    {
        var update = Assert.Single(updates, candidate =>
            candidate.WhereClause.Contains($"`immortal_template_id`={templateId}", StringComparison.Ordinal));

        Assert.Contains($"`code`='{code}'", update.WhereClause, StringComparison.Ordinal);
        Assert.Contains($"`resource_id`={identity}", update.SetClause, StringComparison.Ordinal);
        Assert.Contains($"`resource_id`={identity})", update.WhereClause, StringComparison.Ordinal);
        Assert.Contains($"GodEft.csvZ SHA-256 {SourceSha256} row {sourceRow}", update.SetClause, StringComparison.Ordinal);
        Assert.Contains($"rawFields={identity}|{archiveId}|", update.SetClause, StringComparison.Ordinal);
        Assert.Contains($"|{originalName}; column 1 header=神仙ID.", update.SetClause, StringComparison.Ordinal);
    }

    private static IReadOnlyList<TemplateUpdate> ParseTemplateUpdates(string sql) =>
        Regex.Matches(
                sql,
                @"UPDATE `god2_game`.`immortal_templates`\s+SET\s+(?<set>.*?)\s+WHERE\s+(?<where>.*?);",
                RegexOptions.CultureInvariant | RegexOptions.Singleline)
            .Select(match => new TemplateUpdate(
                match.Groups["set"].Value,
                match.Groups["where"].Value))
            .ToArray();

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "God2ClassicServer.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record TemplateUpdate(string SetClause, string WhereClause);
}
