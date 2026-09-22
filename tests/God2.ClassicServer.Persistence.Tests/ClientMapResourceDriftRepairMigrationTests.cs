using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class ClientMapResourceDriftRepairMigrationTests
{
    private const string MigrationName =
        "476_repair_forge_client_map_resource_provenance_drift.sql";

    [Fact]
    public void Migration_restores_exact_pinned_staging_inventory()
    {
        var sql = ReadMigration();

        var identityStage = Slice(
            sql,
            "INSERT INTO `god2_map_identity_stage`",
            "CREATE TEMPORARY TABLE `god2_portal_link_stage`");
        var portalStage = Slice(
            sql,
            "INSERT INTO `god2_portal_link_stage`",
            "INSERT INTO `god2_game`.`client_map_resources`");

        Assert.Equal(
            144,
            Regex.Matches(
                identityStage,
                @"^\s*\(\d+,'god2-opt-6b127086e0c0',",
                RegexOptions.Multiline | RegexOptions.CultureInvariant).Count);

        Assert.Equal(
            65,
            Regex.Matches(
                portalStage,
                @"^\s*\('client:can-link/",
                RegexOptions.Multiline | RegexOptions.CultureInvariant).Count);

        Assert.Equal(
            44,
            Regex.Matches(
                portalStage,
                "'Derived',0\\)",
                RegexOptions.CultureInvariant).Count);

        Assert.Equal(
            21,
            Regex.Matches(
                portalStage,
                "'Candidate',0\\)",
                RegexOptions.CultureInvariant).Count);

        Assert.Contains(
            "(SELECT COUNT(*) FROM `god2_game`.`client_map_resources`) = 158",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "(SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`) = 144",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "(SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`) = 65",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_is_fail_closed_and_never_promotes_runtime_portals()
    {
        var sql = ReadMigration();

        Assert.Contains("god2_client_resource_repair_guard", sql, StringComparison.Ordinal);
        Assert.Contains("god2_client_resource_result_guard", sql, StringComparison.Ordinal);
        Assert.Contains("114_publish_official_map_catalog.sql", sql, StringComparison.Ordinal);
        Assert.Contains("115_publish_official_portal_resource_links.sql", sql, StringComparison.Ordinal);
        Assert.Contains("179_split_client_resource_link_evidence.sql", sql, StringComparison.Ordinal);
        Assert.Contains("226_archive_client_map_resource_file_evidence.sql", sql, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "UPDATE `god2_game`.`maps`",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "UPDATE `god2_game`.`portals`",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`source_x`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`destination_x`", sql, StringComparison.Ordinal);

        Assert.Contains("`portal_id`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`enabled`=0", sql, StringComparison.Ordinal);
        Assert.Contains("'Unknown','Unknown','Unknown'", sql, StringComparison.Ordinal);
    }

    private static string Slice(string sql, string start, string end)
    {
        var startIndex = sql.IndexOf(start, StringComparison.Ordinal);
        var endIndex = sql.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);
        Assert.True(endIndex > startIndex);
        return sql[startIndex..endIndex];
    }

    private static string ReadMigration() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema", MigrationName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
