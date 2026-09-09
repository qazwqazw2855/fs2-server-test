namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialAttributeGrowthPersistenceTests
{
    private static string MigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "085_publish_official_character_attribute_growth.sql"));

    [Fact]
    public void Migration_replaces_four_class_growth_rules_with_verified_values()
    {
        var sql = MigrationSql();

        Assert.Contains("DELETE FROM `god2_game`.`class_stat_growth`", sql, StringComparison.Ordinal);
        Assert.Equal(4, Count(sql, "'ManualPerLevel',4,1,'Verified',1"));
        Assert.Equal(16, Count(sql, "'AutomaticPerLevel'"));
        Assert.Contains("(1,'Swordsman','劍士'", sql, StringComparison.Ordinal);
        Assert.Contains("(2,'Taoist','仙道'", sql, StringComparison.Ordinal);
        Assert.Contains("(3,'Pharmacist','藥師'", sql, StringComparison.Ordinal);
        Assert.Contains("(4,'Warlock','謀士'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_records_pet_budget_without_inventing_attribute_distribution()
    {
        var sql = MigrationSql();

        Assert.Contains("`automatic_points_per_level`", sql, StringComparison.Ordinal);
        Assert.Contains("`manual_points_per_level`", sql, StringComparison.Ordinal);
        Assert.Contains("(1,'通用寵物升級配點預算',4,1,'Verified',1", sql, StringComparison.Ordinal);
        Assert.Contains("自動配點的四維比例尚未提供", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_publishes_traditional_chinese_readable_view()
    {
        var sql = MigrationSql();

        Assert.Contains("`vw_official_character_attribute_growth`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `每級自動體力`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `每級自動腕力`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `每級自由配點`", sql, StringComparison.Ordinal);
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

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
