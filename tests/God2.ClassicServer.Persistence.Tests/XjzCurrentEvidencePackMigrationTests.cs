namespace God2.ClassicServer.Persistence.Tests;

public sealed class XjzCurrentEvidencePackMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "149_publish_xjz_current_client_evidence_pack.sql");

    [Fact]
    public void Migration_publishes_current_client_evidence_tables_without_runtime_authority_leak()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_evidence_pack_sources", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_god_menu_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_combat_pet_quality_star_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_item_effect_visual_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_monster_visual_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_mission_text_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS god2_game.xjz_daily_mission_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW god2_game.vw_xjz_current_evidence_pack_summary", sql, StringComparison.Ordinal);
        Assert.Contains("'VisualOnlyServerAuthorityBlocked'", sql, StringComparison.Ordinal);
        Assert.Contains("packet_evidence_boundary tinyint(1) NOT NULL DEFAULT 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("god2_player.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.skills", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.monsters", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO god2_game.items", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_preserves_representative_function_mappings_from_the_xjz_evidence_pack()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("(1,2,'姜子牙','金',1,3,4,10,8,5)", sql, StringComparison.Ordinal);
        Assert.Contains("(15,390,780,5)", sql, StringComparison.Ordinal);
        Assert.Contains("'ItemEft3','8400'", sql, StringComparison.Ordinal);
        Assert.Contains("'ItemEft3','8417'", sql, StringComparison.Ordinal);
        Assert.Contains("'GodItemEft','8500'", sql, StringComparison.Ordinal);
        Assert.Contains("'eny',3,'土虫'", sql, StringComparison.Ordinal);
        Assert.Contains("'FightEny',4,'土虫'", sql, StringComparison.Ordinal);
        Assert.Contains("(1,1,'[城镇任务]城镇向导'", sql, StringComparison.Ordinal);
        Assert.Contains("(0,0,'炼丹修行一'", sql, StringComparison.Ordinal);
        Assert.Contains("'FE2B222C0D4DA25C0EE41948F225BAE2B6FFBE3173E416DDB70B8BF93A58EB53'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_migration_removes_header_rows_already_inserted_by_the_first_publish()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "150_cleanup_xjz_current_client_evidence_header_rows.sql"));

        Assert.Contains("DELETE FROM god2_game.xjz_item_effect_visual_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("source_table = 'ItemEft3' AND effect_key = '档案标识符'", sql, StringComparison.Ordinal);
        Assert.Contains("source_table = 'GodItemEft' AND effect_key = '档案标识符'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("god2_player.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("god2_game.monsters", sql, StringComparison.OrdinalIgnoreCase);
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
