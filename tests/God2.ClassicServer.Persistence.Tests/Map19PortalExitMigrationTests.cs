namespace God2.ClassicServer.Persistence.Tests;

public sealed class Map19PortalExitMigrationTests
{
    [Fact]
    public void Migration_corrects_the_arrival_exit_conflation_without_changing_the_destination()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "107_correct_map19_visual_exit_trigger.sql"));

        Assert.Contains("portal_row.`source_x` = 16", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`source_y` = 20", sql, StringComparison.Ordinal);
        Assert.Contains("portal_row.`source_radius` = 1", sql, StringComparison.Ordinal);
        Assert.Contains("source_map.`client_map_id` = 19", sql, StringComparison.Ordinal);
        Assert.Contains("destination_map.`client_map_id` = 3", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("destination_x` =", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
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
