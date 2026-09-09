namespace God2.ClassicServer.Persistence.Tests;

public sealed class LegacyCharacterRetirementMigrationTests
{
    [Fact]
    public void Migration_retires_only_the_four_exact_legacy_validation_characters()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "112_retire_legacy_stitched_validation_characters.sql"));

        Assert.Contains("(1,11,'G2A','2026-07-31 09:40:49.796066')", sql, StringComparison.Ordinal);
        Assert.Contains("(2,12,'G2B','2026-07-31 09:40:49.936450')", sql, StringComparison.Ordinal);
        Assert.Contains("(3,13,'G2C','2026-07-31 09:40:50.006871')", sql, StringComparison.Ordinal);
        Assert.Contains("(4,14,'G2D','2026-07-31 09:40:50.098543')", sql, StringComparison.Ordinal);
        Assert.Contains("`status` = 'Deleted'", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled` = 0", sql, StringComparison.Ordinal);
        Assert.Contains("`deleted_at_utc`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("created_at_utc` <", sql, StringComparison.OrdinalIgnoreCase);
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
