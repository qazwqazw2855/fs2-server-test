using System;
using System.IO;
using Xunit;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class FormalReadableViewCleanupMigrationTests
{
    [Fact]
    public void Player_readable_view_cleanup_repoints_assets_and_capture_gaps_to_formal_tables()
    {
        var sql = File.ReadAllText(FindMigration("365_repoint_player_readable_views_to_formal_tables.sql"));

        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_player_assets_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_player`.`characters` character_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_player`.`character_inventory` slot_row", sql, StringComparison.Ordinal);
        Assert.Contains("FROM `god2_player`.`character_equipment` equipment_row", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_capture_gap_priority_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("防禦狀態物防與魔防以 1.5 倍處理", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2`.`inventory_slots`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `god2`.`equipment_slots`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*) FROM `god2`.`inventory_slots`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT COUNT(*) FROM `god2`.`equipment_slots`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_assets_readable_view_converts_ascii_state_columns_before_traditional_chinese_fallbacks()
    {
        var sql = File.ReadAllText(FindMigration("366_fix_player_assets_readable_collations.sql"));

        Assert.Contains("CONVERT(inventory_state_row.`InventoryId` USING utf8mb4)", sql, StringComparison.Ordinal);
        Assert.Contains("CONVERT(inventory_state_row.`DirtyState` USING utf8mb4)", sql, StringComparison.Ordinal);
        Assert.Contains("CONVERT(currency_row.`CurrencyType` USING utf8mb4)", sql, StringComparison.Ordinal);
        Assert.Contains("CONVERT(currency_row.`DirtyState` USING utf8mb4)", sql, StringComparison.Ordinal);
        Assert.Contains("CONVERT(equipment_row.`equipment_slot` USING utf8mb4)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_legacy_player_authority_tables_are_dropped_only_after_row_and_view_reference_guards()
    {
        var sql = File.ReadAllText(FindMigration("367_drop_empty_legacy_player_authority_tables.sql"));

        Assert.Contains("@legacy_player_authority_rows", sql, StringComparison.Ordinal);
        Assert.Contains("@legacy_player_authority_view_refs", sql, StringComparison.Ordinal);
        Assert.Contains("SIGNAL SQLSTATE", sql, StringComparison.Ordinal);
        Assert.Contains("45000", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`character_creation_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`equipment_slots`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`inventory_slots`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`inventory_audit_ledger`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`player_currency_balances`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`player_inventory_state`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`world_interaction_audit`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_legacy_spawns_table_is_dropped_only_after_formal_monster_spawns_repoint()
    {
        var sql = File.ReadAllText(FindMigration("368_drop_empty_legacy_spawns_after_formal_monster_spawns_repoint.sql"));

        Assert.Contains("@legacy_spawns_rows", sql, StringComparison.Ordinal);
        Assert.Contains("@formal_monster_spawns_rows", sql, StringComparison.Ordinal);
        Assert.Contains("@legacy_spawns_view_refs", sql, StringComparison.Ordinal);
        Assert.Contains("SIGNAL SQLSTATE", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`spawns`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Promoted_gameplay_runtime_loads_active_release_from_formal_runtime_catalog_meta()
    {
        var source = File.ReadAllText(FindSource("God2.ClassicServer.Persistence", "MariaDbPromotedGameplayContentRuntime.cs"));

        Assert.Contains("`god2_game_meta`.`runtime_catalog_releases`", source, StringComparison.Ordinal);
        Assert.Contains("FormalRuntimeCatalogManifestBuilder.Version", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM `content_runtime_releases`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE `content_runtime_releases`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_legacy_content_runtime_releases_table_is_dropped_only_after_meta_repoint()
    {
        var sql = File.ReadAllText(FindMigration("369_drop_empty_legacy_content_runtime_releases_after_meta_repoint.sql"));

        Assert.Contains("@legacy_content_release_rows", sql, StringComparison.Ordinal);
        Assert.Contains("@formal_active_runtime_catalog_releases", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_game_meta`.`runtime_catalog_releases`", sql, StringComparison.Ordinal);
        Assert.Contains("SIGNAL SQLSTATE", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE IF EXISTS `god2`.`content_runtime_releases`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Remaining_runtime_evidence_tables_are_retained_in_schema_inventory_audit()
    {
        var sql = File.ReadAllText(FindMigration("370_reclassify_remaining_runtime_evidence_tables_as_retained.sql"));

        Assert.Contains("`資料庫`", sql, StringComparison.Ordinal);
        Assert.Contains("`用途分類`", sql, StringComparison.Ordinal);
        Assert.Contains("保留：怪物戰鬥、死亡與重生Runtime結構", sql, StringComparison.Ordinal);
        Assert.Contains("保留：寵物蛋孵化關聯證據結構", sql, StringComparison.Ordinal);
        Assert.Contains("god2_game_meta", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("寤跺緦", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("璩囨枡", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_text_quality_audit_view_keeps_readable_traditional_chinese_labels_without_literal_mojibake()
    {
        var sql = File.ReadAllText(FindMigration("372_narrow_formal_text_quality_audit_hex_patterns.sql"));

        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_formal_text_quality_mojibake_audit_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `資料表`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `欄位`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `可疑筆數`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `狀態`", sql, StringComparison.Ordinal);
        Assert.Contains("THEN '通過' ELSE '需要清理'", sql, StringComparison.Ordinal);
        Assert.Contains("HEX(COALESCE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIKE '%", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\u9435\u20ac", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\u95B9\u7E6A", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\u7B60\u20ac", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("E996BB", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_gap_priority_view_marks_deferred_design_separately_from_current_cleanup()
    {
        var sql = File.ReadAllText(FindMigration("375_reclassify_capture_gap_priority_for_deferred_design.sql"));

        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_capture_gap_priority_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `整理狀態`", sql, StringComparison.Ordinal);
        Assert.Contains("'暫緩設計；只保留校準缺口'", sql, StringComparison.Ordinal);
        Assert.Contains("'暫緩設計'", sql, StringComparison.Ordinal);
        Assert.Contains("'現在整理'", sql, StringComparison.Ordinal);
        Assert.Contains("'AI 觸發條件可由服務端自行設計，但依照目前指示先不做。'", sql, StringComparison.Ordinal);
        Assert.Contains("'防禦狀態物防與魔防以 1.5 倍處理；後續用抓包或黑箱繼續校準公式。'", sql, StringComparison.Ordinal);
    }

    private static string FindMigration(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", "schema", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find migration {fileName} from {AppContext.BaseDirectory}.");
    }

    private static string FindSource(string projectName, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", projectName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find source {fileName} from {AppContext.BaseDirectory}.");
    }
}
