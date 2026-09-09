using System.Text.Json;
using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialWeaponAttackRangeMigrationTests
{
    private const string ExactClientSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";

    [Fact]
    public void Exact_client_weapon_rows_have_complete_numeric_range_and_independent_text_correlation()
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", "WPN.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal("WPN", root.GetProperty("section").GetString());
        Assert.Equal(ExactClientSourceSha256, root.GetProperty("sourceSha256").GetString());
        Assert.Equal(645, root.GetProperty("declaredCount").GetInt32());
        Assert.Equal(645, root.GetProperty("exportedCount").GetInt32());

        var ranges = new Dictionary<int, int>();
        var textCorrelated = 0;
        var displayTextAbsent = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var fields = row.GetProperty("fields");
            var clientItemId = int.Parse(fields[0].GetString()!);
            var subtype = int.Parse(fields[1].GetString()!);
            var range = int.Parse(fields[29].GetString()!);
            Assert.InRange(subtype, 0, 10);
            Assert.InRange(range, 2, 4);
            ranges.Add(clientItemId, range);

            var match = Regex.Match(fields[12].GetString()!, @"攻击距离\s*(\d+)");
            if (match.Success)
            {
                Assert.Equal(range, int.Parse(match.Groups[1].Value));
                textCorrelated++;
            }
            else
            {
                displayTextAbsent++;
            }
        }

        Assert.Equal(645, ranges.Count);
        Assert.Equal(642, textCorrelated);
        Assert.Equal(3, displayTextAbsent);
        Assert.Equal(2, ranges[177]);
        Assert.Equal(2, ranges[178]);
        Assert.Equal(4, ranges[777]);
    }

    [Fact]
    public void Migration_publishes_only_static_range_evidence_and_keeps_mutations_blocked()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "129_publish_verified_weapon_attack_ranges.sql"));

        Assert.Contains("COUNT(*)=645", sql, StringComparison.Ordinal);
        Assert.Contains("$.rawFields[29]", sql, StringComparison.Ordinal);
        Assert.Contains("BETWEEN 2 AND 4", sql, StringComparison.Ordinal);
        Assert.Contains("`weapon_attack_range_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains(ExactClientSourceSha256, sql, StringComparison.Ordinal);
        Assert.Contains("`attack_range`=evidence_row.`attack_range`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_weapon_attack_ranges_readable`", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("`god2_player`.`character_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_equipment`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`equipment_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialInventoryActivation", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`weapons`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`equipment`", sql, StringComparison.OrdinalIgnoreCase);
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
