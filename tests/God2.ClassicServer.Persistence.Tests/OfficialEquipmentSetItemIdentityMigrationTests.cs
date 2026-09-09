using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentSetItemIdentityMigrationTests
{
    private const string ExactClientSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";
    private static readonly int[] ExpectedSetIds = [1, 2, 3, 4, 5, 25, 42, 43, 44, 45, 46];

    [Fact]
    public void Exact_client_field_30_has_the_complete_equipment_set_identity_domain()
    {
        var weaponRows = ReadRows("WPN");
        var equipmentRows = ReadRows("EQU");

        var weapons = NonzeroSetIdentities("WPN", weaponRows);
        var equipment = NonzeroSetIdentities("EQU", equipmentRows);
        var all = weapons.Concat(equipment).ToArray();

        Assert.Equal(3, weapons.Count);
        Assert.Equal(28, equipment.Count);
        Assert.Equal(31, all.Length);
        Assert.Equal(ExpectedSetIds, all.Select(identity => identity.SetId).Distinct().Order().ToArray());
        Assert.Equal(31, all.Select(identity => (identity.SourceType, identity.ClientItemId)).Distinct().Count());

        Assert.Contains(("WPN", 177, 45), all);
        Assert.Contains(("WPN", 178, 46), all);
        Assert.Contains(("WPN", 777, 25), all);
        Assert.Contains(("EQU", 1597, 1), all);
        Assert.Contains(("EQU", 1198, 25), all);
        Assert.Contains(("EQU", 1685, 46), all);
    }

    [Fact]
    public void Migration_publishes_only_the_verified_relationship_and_keeps_bonus_and_mutation_blocked()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "131_publish_verified_equipment_set_item_identities.sql"));

        Assert.Contains("COUNT(*)=31", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(source_row.`ItemType`='WPN')=3", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(source_row.`ItemType`='EQU')=28", sql, StringComparison.Ordinal);
        Assert.Contains("$.rawFields[30]", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT CAST", sql, StringComparison.Ordinal);
        Assert.Contains("=11", sql, StringComparison.Ordinal);
        Assert.Contains("`equipment_set_item_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("EquipSetCount", sql, StringComparison.Ordinal);
        Assert.Contains("lookup RVA 0x000287E0", sql, StringComparison.Ordinal);
        Assert.Contains(ExactClientSourceSha256, sql, StringComparison.Ordinal);
        Assert.Contains("`relationship_evidence_status`='Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("`bonus_evidence_status`='EvidenceBlocked'", sql, StringComparison.Ordinal);
        Assert.Contains("SET equipment_row.`set_id`=evidence_row.`set_id`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_equipment_set_item_evidence_readable`", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("UPDATE `god2_game`.`item_set_bonuses`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`item_set_bonuses`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET set_row.`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET equipment_row.`equipment_slot`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_equipment`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`equipment_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialInventoryActivation", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement[] ReadRows(string section)
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", $"{section}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        Assert.Equal(section, root.GetProperty("section").GetString());
        Assert.Equal(ExactClientSourceSha256, root.GetProperty("sourceSha256").GetString());
        return root.GetProperty("rows").EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static IReadOnlyList<(string SourceType, int ClientItemId, int SetId)> NonzeroSetIdentities(
        string sourceType,
        IEnumerable<JsonElement> rows)
    {
        var result = new List<(string SourceType, int ClientItemId, int SetId)>();
        foreach (var row in rows)
        {
            var fields = row.GetProperty("fields");
            var setId = int.Parse(fields[30].GetString()!);
            if (setId == 0)
            {
                continue;
            }

            result.Add((
                sourceType,
                int.Parse(fields[0].GetString()!),
                setId));
        }

        return result;
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
