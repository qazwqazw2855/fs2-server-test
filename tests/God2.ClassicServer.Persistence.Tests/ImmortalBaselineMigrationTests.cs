namespace God2.ClassicServer.Persistence.Tests;

public sealed class ImmortalBaselineMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "106_publish_level_one_immortal_baseline.sql");

    [Fact]
    public void Migration_separates_level_one_stats_and_profession_access_from_owned_instances()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`immortal_base_stats`", sql, StringComparison.Ordinal);
        Assert.Contains("`profession_code` varchar(32)", sql, StringComparison.Ordinal);
        Assert.Contains("`is_special` tinyint(1)", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`level` = 1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("immortal_growth_profiles", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_publishes_one_level_one_baseline_for_each_profession_and_one_special_immortal()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("'immortal_wuji','武吉','武吉','Swordsman',0,1", sql, StringComparison.Ordinal);
        Assert.Contains("'immortal_tuxingsun','土行孫','土行孫','Taoist',0,1", sql, StringComparison.Ordinal);
        Assert.Contains("'immortal_cihang_daoren','慈航道人','慈航道人','Pharmacist',0,1", sql, StringComparison.Ordinal);
        Assert.Contains("'immortal_jiang_ziya','姜子牙','姜子牙','Warlock',0,1", sql, StringComparison.Ordinal);
        Assert.Contains("'immortal_hu_ximei','胡喜媚','胡喜媚',NULL,1,1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Wuji_has_complete_user_verified_level_one_stats_while_unknown_hp_mp_remain_null()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("(1060001,1,400,156,30,20,10,5,0,10,0,0,0,'Verified'", sql, StringComparison.Ordinal);
        Assert.Contains("(1060002,1,NULL,NULL,1,9,25,10,0,0,0,0,50,'Recovered'", sql, StringComparison.Ordinal);
        Assert.Contains("(1060003,1,NULL,NULL,10,10,30,10,0,0,0,10,10,'Recovered'", sql, StringComparison.Ordinal);
        Assert.Contains("(1060004,1,NULL,NULL,2,8,25,5,0,30,30,0,0,'Recovered'", sql, StringComparison.Ordinal);
        Assert.Contains("(1060005,1,NULL,NULL,5,10,30,20,10,10,10,10,10,'Recovered'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_catalog_reads_the_separate_baseline_table()
    {
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCanonicalCatalogRepositories.cs"));

        Assert.Contains("JOIN `god2_game`.`immortal_base_stats`", repository, StringComparison.Ordinal);
        Assert.Contains("template_row.`profession_code`", repository, StringComparison.Ordinal);
        Assert.Contains("template_row.`is_special`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("stats_row.`evidence_status`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("stats_row.`evidence_reference`", repository, StringComparison.Ordinal);
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
