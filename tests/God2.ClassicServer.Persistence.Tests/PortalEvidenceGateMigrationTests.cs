namespace God2.ClassicServer.Persistence.Tests;

public sealed class PortalEvidenceGateMigrationTests
{
    [Fact]
    public void Migration_requires_complete_portal_evidence_before_runtime_enablement()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "database",
            "schema",
            "109_add_portal_evidence_gate.sql");
        var sql = File.ReadAllText(path);

        Assert.Contains("`identity_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("`source_coordinate_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("`destination_coordinate_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("`trigger_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(TRIM(`source_reference`),'') IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ADD KEY IF NOT EXISTS `ix_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `ck_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE `portal_id` IN (1,2,3,4,170015007)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_gate_pins_every_enabled_route_and_requires_a_sha256()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "database",
            "schema",
            "127_pin_enabled_portal_evidence_hashes.sql");
        var sql = File.ReadAllText(path);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["database/schema/083_publish_latest_verified_map3_map19_portals.sql"] =
                "DD787EED030AD5576D5D3571DC5E0D7BB745BC7FB5EEAF83DECF010EE93D56E5",
            ["database/schema/084_publish_hongmeng_biyou_portal_pair.sql"] =
                "5FD4ADB8C3AE0D15761C07DB9F09AC6FA9EDC3AA720A5C27F746AD90808A27D6",
            ["database/schema/103_publish_live_stage3_stage7_runtime_slice.sql"] =
                "4B65E9DC8273A3F2F2B6DE05D5D2E9E93BAA37F8EADCED6CB9CE00455C8F6CFA",
            ["database/schema/107_correct_map19_visual_exit_trigger.sql"] =
                "4BD5FCE2C10C3096081CD61928C4606A7E3CC2BE507C775BFB93AC4E2EA5D41B"
        };

        foreach (var pair in expected)
        {
            var artifactPath = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(artifactPath)));
            Assert.Equal(pair.Value, actualHash);
            Assert.Contains($"`source_reference` = '{pair.Key}'", sql, StringComparison.Ordinal);
            Assert.Contains($"`source_sha256` = '{pair.Value}'", sql, StringComparison.Ordinal);
        }

        foreach (var portalId in new[] { "1", "2", "3", "4", "170015007" })
        {
            Assert.Contains($"`portal_id` = {portalId}", sql, StringComparison.Ordinal);
        }

        Assert.Contains("`source_sha256` NOT REGEXP '^[0-9A-Fa-f]{64}$'", sql, StringComparison.Ordinal);
        Assert.Contains("`source_sha256` REGEXP '^[0-9A-Fa-f]{64}$'", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `ck_portals_evidence_gate`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forge_drift_repair_restores_runtime_routes_and_split_evidence()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "database",
            "schema",
            "473_repair_forge_portal_catalog_and_evidence_drift.sql");
        var sql = File.ReadAllText(path);

        Assert.Contains("CREATE DATABASE IF NOT EXISTS `god2_research`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_research`.`portal_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_game`.`portals`", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `god2_research`.`portal_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("FORGE_SEED_LEDGER_DRIFT", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TEMPORARY TABLE `god2_portal_repair_guard`", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`ready` = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("`portal_id` IN (3,4,170015007)", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TEMPORARY TABLE `god2_portal_repair_guard`", sql, StringComparison.Ordinal);

        Assert.Contains("1, '崑崙仙界三至雜貨店'", sql, StringComparison.Ordinal);
        Assert.Contains("2, '雜貨店至崑崙仙界三'", sql, StringComparison.Ordinal);
        Assert.Contains("source_map.`client_area_id` = 4", sql, StringComparison.Ordinal);
        Assert.Contains("source_map.`client_map_id` = 3", sql, StringComparison.Ordinal);
        Assert.Contains("source_map.`client_map_id` = 19", sql, StringComparison.Ordinal);
        Assert.Contains("source_map.`enabled` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("16, 20, 1", sql, StringComparison.Ordinal);

        foreach (var portalId in new[] { "1", "2", "3", "4", "170015007" })
        {
            Assert.Contains($"({portalId}, 'god2-opt-6b127086e0c0'", sql, StringComparison.Ordinal);
        }

        Assert.Contains("DD787EED030AD5576D5D3571DC5E0D7BB745BC7FB5EEAF83DECF010EE93D56E5", sql, StringComparison.Ordinal);
        Assert.Contains("4BD5FCE2C10C3096081CD61928C4606A7E3CC2BE507C775BFB93AC4E2EA5D41B", sql, StringComparison.Ordinal);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE `god2_game`.`portals`", sql, StringComparison.Ordinal);
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
