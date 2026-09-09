namespace God2.ClassicServer.Persistence.Tests;

public sealed class ClientMapResourceCatalogMigrationTests
{
    [Fact]
    public void Migration_108_creates_disabled_evidence_gated_map_resource_catalog()
    {
        var root = FindRepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(
            root,
            "database",
            "schema",
            "108_create_client_map_resource_catalog.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`client_map_resources`", sql, StringComparison.Ordinal);
        Assert.Contains("`resource_key` varchar(512) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`canonical_map_id` bigint NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`portal_placement_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown'", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled` tinyint(1) NOT NULL DEFAULT 0", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT `ck_client_map_resources_gate`", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=0 OR", sql, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
