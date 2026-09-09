using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialMapCatalogMigrationTests
{
    [Fact]
    public void Migration_publishes_all_official_map_identities_and_keeps_missing_navigation_disabled()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "114_publish_official_map_catalog.sql"));
        var associationValues = sql[
            sql.IndexOf("INSERT INTO `god2_game`.`client_map_resource_identities`", StringComparison.Ordinal)..];
        associationValues = associationValues[..associationValues.IndexOf("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal)];
        var identities = Regex.Matches(
            associationValues,
            @"\((?<id>\d+),'god2-opt-6b127086e0c0',(?<area>\d+),(?<map>\d+),",
            RegexOptions.CultureInvariant);

        Assert.Equal(144, identities.Count);
        Assert.Equal(144, identities.Select(match => $"{match.Groups["area"].Value}:{match.Groups["map"].Value}").Distinct().Count());
        Assert.Contains("(130139698,'god2-opt-6b127086e0c0',2,0,", associationValues, StringComparison.Ordinal);
        Assert.Contains("(192354557,'god2-opt-6b127086e0c0',2,43,", associationValues, StringComparison.Ordinal);
        Assert.Contains("(1675308248,'god2-opt-6b127086e0c0',4,3,", associationValues, StringComparison.Ordinal);
        Assert.Contains("(170015007,'god2-opt-6b127086e0c0',15,7,", associationValues, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(associationValues, "'client:map/gem/gem01/gem01'", RegexOptions.CultureInvariant).Count);
        Assert.Single(Regex.Matches(associationValues, "'Verified','EvidenceBlocked',0", RegexOptions.CultureInvariant));
        Assert.Contains("ADD UNIQUE KEY IF NOT EXISTS `ux_maps_client_identity`", sql, StringComparison.Ordinal);
        Assert.Contains("`world_map_x` int NULL COMMENT 'Official world-map UI X; not a gameplay spawn'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("default_spawn", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS `god2_game`.`client_map_resource_identities`", sql, StringComparison.Ordinal);
        Assert.Contains("Source SHA-256: c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", sql, StringComparison.Ordinal);
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
