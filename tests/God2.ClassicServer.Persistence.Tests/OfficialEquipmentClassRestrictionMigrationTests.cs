using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentClassRestrictionMigrationTests
{
    private const string ExactSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";
    private static readonly Regex Restriction = new(
        "^限([一二三四五]转后)?(剑客|仙道|药师|谋士)(，(剑客|仙道|药师|谋士)){0,3}使用",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Exact_client_contains_the_complete_direct_WPN_EQU_class_restriction_domain()
    {
        AssertSection("WPN", 645, 147);
        AssertSection("EQU", 731, 112);
    }

    [Fact]
    public void Migration_promotes_only_direct_text_and_keeps_activation_blocked()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "136_publish_verified_equipment_class_restrictions.sql"));

        Assert.Contains("COUNT(*)=259", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`source_item_type`='WPN')=147", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`source_item_type`='EQU')=112", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`item_class_restriction_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'VerifiedOfficialClientText','EvidenceBlocked',0", sql, StringComparison.Ordinal);
        Assert.Contains("rule_row.`text_rule_evidence_status`='Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_item_class_restrictions_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("character_equipment", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("equipment_instances", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSection(string section, int total, int restricted)
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", $"{section}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal(ExactSourceSha256, root.GetProperty("sourceSha256").GetString());
        Assert.Equal(total, root.GetProperty("exportedCount").GetInt32());
        var rows = root.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(total, rows.Length);
        Assert.Equal(restricted, rows.Count(row =>
            Restriction.IsMatch(row.GetProperty("fields")[7].GetString() ?? string.Empty)));
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
