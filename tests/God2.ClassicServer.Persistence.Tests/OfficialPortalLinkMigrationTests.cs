using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialPortalLinkMigrationTests
{
    [Fact]
    public void Migration_resolves_the_official_can_graph_without_enabling_unverified_triggers()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "115_publish_official_portal_resource_links.sql"));
        var values = sql[sql.IndexOf("INSERT INTO `god2_game`.`portal_resource_links`", StringComparison.Ordinal)..];
        values = values[..values.IndexOf("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal)];
        var links = Regex.Matches(values, @"\('client:can-link/", RegexOptions.CultureInvariant);

        Assert.Equal(65, links.Count);
        Assert.Equal(44, Regex.Matches(values, "'Derived',0\\)", RegexOptions.CultureInvariant).Count);
        Assert.Equal(21, Regex.Matches(values, "'Candidate',0\\)", RegexOptions.CultureInvariant).Count);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`portal_resource_links`", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`source_x`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`destination_x`=NULL", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`trigger_evidence_status`='Unknown'", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`enabled`=0", sql, StringComparison.Ordinal);
        Assert.Contains("Source SHA-256: 27ce439286bc921699d0067b5362714f083ce0b754939fa6891b2401a0e101d2", sql, StringComparison.Ordinal);
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
