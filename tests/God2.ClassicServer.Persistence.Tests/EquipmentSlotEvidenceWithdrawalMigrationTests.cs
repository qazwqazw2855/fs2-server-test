using System.Text.Json;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class EquipmentSlotEvidenceWithdrawalMigrationTests
{
    private static readonly IReadOnlyDictionary<int, int> ExpectedSubtypeCounts =
        new Dictionary<int, int>
        {
            [0] = 78,
            [1] = 79,
            [2] = 70,
            [3] = 79,
            [4] = 70,
            [5] = 78,
            [6] = 79,
            [7] = 101,
            [8] = 70,
            [9] = 6,
            [10] = 21
        };

    [Fact]
    public void Exact_client_equipment_rows_disprove_one_generic_slot_for_every_item()
    {
        var path = Path.Combine(
            RepositoryRoot(), "Artifacts", "OfficialGameDataSections", "EQU.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var rows = root.GetProperty("rows").EnumerateArray().ToArray();

        Assert.Equal(731, rows.Length);
        var actual = rows
            .GroupBy(row => int.Parse(row.GetProperty("fields")[1].GetString()!))
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(ExpectedSubtypeCounts, actual);

        var readableTypes = rows.Select(row => row.GetProperty("fields")[8].GetString()!).ToArray();
        Assert.Contains(readableTypes, value => value.Contains("防具-铠", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-袍", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-头巾", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-头冠", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-头盔", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-手套", StringComparison.Ordinal));
        Assert.Contains(readableTypes, value => value.Contains("防具-鞋子", StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_withdraws_only_the_generic_slot_and_preserves_gameplay_gates()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "130_withdraw_generic_equipment_slot_labels.sql"));

        Assert.Contains("COUNT(*)=731", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(equipment_row.`equipment_slot`='Armor')=731", sql, StringComparison.Ordinal);
        Assert.Contains("`equipment_slot_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("WithdrawnGenericFamilyMisclassifiedAsSlot", sql, StringComparison.Ordinal);
        Assert.Contains("SET equipment_row.`equipment_slot`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("evidence_row.`runtime_eligible`=0", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_equipment_slot_evidence_readable`", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("SET equipment_row.`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_equipment`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`equipment_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialInventoryActivation", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`equipment`", sql, StringComparison.OrdinalIgnoreCase);
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
