namespace God2.ClassicServer.Persistence.Tests;

public sealed class Map19GroceryMerchantMigrationTests
{
    [Fact]
    public void Migration_enables_only_the_verified_Map19_grocery_merchant_binding()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "113_enable_verified_map19_grocery_merchant.sql"));

        Assert.Contains("merchant_row.`merchant_id` = 286397401", sql, StringComparison.Ordinal);
        Assert.Contains("merchant_row.`npc_id` = 1075128734", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`observed_client_entity_handle` IN (1504, 4638)", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`client_map_id` = 19", sql, StringComparison.Ordinal);
        Assert.Contains("merchant_row.`enabled` = 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`merchant_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Follow_up_migration_promotes_only_the_two_profile_Map19_grocery_NPC()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "119_promote_verified_map19_grocery_npc_evidence.sql"));

        Assert.Contains("npc_row.`npc_id` = 1075128734", sql, StringComparison.Ordinal);
        Assert.Contains("npc_row.`merchant_id` = 286397401", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT spawn_row.`observed_client_entity_handle`)", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`observed_client_entity_handle` IN (1504, 4638)", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`wire_evidence_status` = 'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`identity_evidence_status` = 'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`coordinate_evidence_status` = 'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("spawn_row.`service_evidence_status` = 'Derived'", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`client_map_id` = 19", sql, StringComparison.Ordinal);
        Assert.Contains("npc_row.`evidence_status` = 'Derived'", sql, StringComparison.Ordinal);
        Assert.Contains("stock remains evidence-gated", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `god2_game`.`merchant_inventory`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
