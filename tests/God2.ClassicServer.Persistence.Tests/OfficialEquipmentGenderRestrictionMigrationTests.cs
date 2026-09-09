using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentGenderRestrictionMigrationTests
{
    private const string ExactSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";

    [Fact]
    public void Exact_client_contains_the_complete_direct_WPN_EQU_gender_restriction_domain()
    {
        var wpn = ReadSection("WPN", 645);
        var equ = ReadSection("EQU", 731);
        var rows = wpn.Concat(equ).Where(IsDirectGenderRestriction).ToArray();

        Assert.Equal(44, rows.Length);
        Assert.Equal(10, wpn.Count(IsDirectGenderRestriction));
        Assert.Equal(34, equ.Count(IsDirectGenderRestriction));
        Assert.Equal(22, rows.Count(row => Field(row).Contains("男性", StringComparison.Ordinal)));
        Assert.Equal(22, rows.Count(row => Field(row).Contains("女性", StringComparison.Ordinal)));
    }

    [Fact]
    public void Migration_publishes_field_level_static_evidence_without_enabling_runtime_activation()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "137_publish_verified_equipment_gender_restrictions.sql"));

        Assert.Contains("COUNT(*)=44", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`source_item_type`='WPN')=10", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`source_item_type`='EQU')=34", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`gender_restriction_zh_tw`='男性')=22", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(seed.`gender_restriction_zh_tw`='女性')=22", sql, StringComparison.Ordinal);
        Assert.Contains(ExactSourceSha256, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`item_gender_restriction_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("'VerifiedOfficialClientText','EvidenceBlocked',0", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_item_gender_restrictions_readable`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_usage_rules`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`runtime_eligible`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_player`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("character_equipment", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("equipment_instances", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement[] ReadSection(string section, int expectedCount)
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", $"{section}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal(ExactSourceSha256, root.GetProperty("sourceSha256").GetString());
        Assert.Equal(expectedCount, root.GetProperty("exportedCount").GetInt32());
        return root.GetProperty("rows").EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static bool IsDirectGenderRestriction(JsonElement row)
    {
        var field = Field(row);
        return (field.Contains("男性", StringComparison.Ordinal) ||
                field.Contains("女性", StringComparison.Ordinal)) &&
               field.Contains("装备", StringComparison.Ordinal);
    }

    private static string Field(JsonElement row) =>
        row.GetProperty("fields")[7].GetString() ?? string.Empty;

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
