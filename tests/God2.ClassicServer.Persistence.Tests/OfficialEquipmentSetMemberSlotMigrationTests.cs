namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialEquipmentSetMemberSlotMigrationTests
{
    private const string ExactEquipSetSourceSha256 = "5696e3f6492d7f9b4c9205ad775fb30b14d612941a64706ca0bced324411c858";

    [Fact]
    public void Migration_publishes_only_the_32_exact_unambiguous_equ_member_slots()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "database", "schema", "132_publish_verified_equipment_set_member_slots.sql"));

        Assert.Contains("COUNT(*)=32", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT source_item.`Id`)=32", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(member_row.`SlotName`='Armor')=10", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(member_row.`SlotName`='Helmet')=10", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(member_row.`SlotName`='Gloves')=6", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(member_row.`SlotName`='Shoes')=6", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(field30_evidence.`item_id`)=28", sql, StringComparison.Ordinal);
        Assert.Contains("member_row.`SetId`=26", sql, StringComparison.Ordinal);
        Assert.Contains(ExactEquipSetSourceSha256, sql, StringComparison.Ordinal);
        Assert.Contains("`equipment_slot_verified_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`slot_evidence_status`='Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("SET equipment_row.`equipment_slot`=evidence_row.`equipment_slot`", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_equipment_slot_verified_evidence_readable`", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("source_item.`ItemType`<>'EQU'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `god2_game`.`item_set_bonuses`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET equipment_row.`enabled`=1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`character_equipment`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`god2_player`.`equipment_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OfficialInventoryActivation", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Existing_phase2_parser_contract_keeps_fixed_member_columns_explicit()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tools", "God2.GameplayContentRecovery", "Phase2ClientExhaustionExtractor.cs"));
        var tests = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "tests", "God2.GameplayContentRecovery.Tests", "RecoveryPipelineTests.cs"));

        Assert.Contains("(Index: 3, Slot: \"Armor\")", source, StringComparison.Ordinal);
        Assert.Contains("(Index: 5, Slot: \"Helmet\")", source, StringComparison.Ordinal);
        Assert.Contains("(Index: 7, Slot: \"Gloves\")", source, StringComparison.Ordinal);
        Assert.Contains("(Index: 9, Slot: \"Shoes\")", source, StringComparison.Ordinal);
        Assert.Contains("(Index: 11, Slot: \"Weapon\")", source, StringComparison.Ordinal);
        Assert.Contains("ExplicitOfficialColumnLayoutAndExactItemId", source, StringComparison.Ordinal);
        Assert.Contains("Phase2EquipmentSetUsesExplicitOfficialColumnsOnly", tests, StringComparison.Ordinal);
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
