namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialLevel1ClassProfilePersistenceTests
{
    private static string MigrationSql() => File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "120_publish_verified_level1_class_profiles.sql"));

    [Theory]
    [InlineData("(1,1,161,45,32,28,20,20")]
    [InlineData("(2,1,142,54,20,24,32,24")]
    [InlineData("(3,1,172,47,24,32,24,20")]
    [InlineData("(4,1,150,50,25,25,25,25")]
    public void Migration_publishes_each_verified_level1_profile(string expectedProfile)
    {
        Assert.Contains(expectedProfile, MigrationSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_keeps_unverified_elements_and_experience_null()
    {
        var sql = MigrationSql();

        Assert.Equal(4, Count(sql, "NULL,NULL,NULL,NULL,NULL,0,NULL,1"));
        Assert.Contains("五行與升級需求經驗尚無證據", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_idempotent_and_publishes_readable_view()
    {
        var sql = MigrationSql();

        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("`vw_official_level1_class_profiles`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `最大HP`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `最大MP`", sql, StringComparison.Ordinal);
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
