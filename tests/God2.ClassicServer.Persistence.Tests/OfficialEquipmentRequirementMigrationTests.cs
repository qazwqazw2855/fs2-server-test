using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentRequirementMigrationTests
{
    private const string ExactClientSourceSha256 = "c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f";

    [Fact]
    public void Exact_client_exports_have_complete_encoded_requirement_domain()
    {
        AssertSection("WPN", 645, (1, 1), (66, 1030), (67, 1060), (68, 2030));
        AssertSection("EQU", 731, (1001, 1), (1066, 1030), (1068, 1060), (1074, 2030));
    }

    [Fact]
    public void Migration_publishes_field_authority_without_enabling_equip_mutation()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "128_publish_verified_equipment_requirements.sql"));

        Assert.Contains("COUNT(*)=1376", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(source_row.`ItemType`='WPN')=645", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(source_row.`ItemType`='EQU')=731", sql, StringComparison.Ordinal);
        Assert.Contains(ExactClientSourceSha256, sql, StringComparison.Ordinal);
        Assert.Contains("DIV 1000", sql, StringComparison.Ordinal);
        Assert.Contains("MOD 1000", sql, StringComparison.Ordinal);
        Assert.Contains("`item_requirement_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`required_level`=evidence_row.`required_level`", sql, StringComparison.Ordinal);
        Assert.Contains("`minimum_rebirth`=evidence_row.`minimum_rebirth`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_item_requirements_readable`", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("`god2_player`.`character_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_equipment`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`equipment_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialInventoryActivation", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`weapons`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`equipment`", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSection(
        string section,
        int expectedCount,
        params (int ClientItemId, int EncodedRequirement)[] samples)
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", $"{section}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        Assert.Equal(section, root.GetProperty("section").GetString());
        Assert.Equal(ExactClientSourceSha256, root.GetProperty("sourceSha256").GetString());
        Assert.Equal(expectedCount, root.GetProperty("declaredCount").GetInt32());
        Assert.Equal(expectedCount, root.GetProperty("exportedCount").GetInt32());

        var requirements = root.GetProperty("rows").EnumerateArray().ToDictionary(
            row => int.Parse(row.GetProperty("fields")[0].GetString()!),
            row => int.Parse(row.GetProperty("fields")[28].GetString()!));
        Assert.Equal(expectedCount, requirements.Count);
        Assert.All(requirements.Values, encoded =>
        {
            Assert.InRange(encoded / 1000, 0, 5);
            Assert.InRange(encoded % 1000, 1, 90);
        });

        foreach (var sample in samples)
        {
            Assert.Equal(sample.EncodedRequirement, requirements[sample.ClientItemId]);
        }
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
