namespace God2.ClassicServer.Persistence.Tests;

public sealed class MapResourceNavigationMigrationTests
{
    [Fact]
    public void Migration_adds_navigation_gate_and_exact_verified_map_bindings()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "110_expand_map_resource_navigation_and_bind_verified_maps.sql"));

        Assert.Contains("`navigation_evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("`resource_key` = 'client:map/island03/cityi2/cityi2'", sql, StringComparison.Ordinal);
        Assert.Contains("`resource_key` = 'client:map/island01/indoor/groceryl'", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`map_id` = 1675308248", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`map_id` = 557790525", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`can_sha256` = '5a176c2bde2c1c98e4e853213cb7017d114eb4ac0f7dae9e0810f91fc806e727'", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`resource_format` = 'MDT'", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`navigation_format` = 'MBD v1.2'", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`resource_format` = 'HMD'", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`navigation_format` = 'HMD v1.6'", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`grid_width` = 10", sql, StringComparison.Ordinal);
        Assert.Contains("resource.`grid_height` = 30", sql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT `ck_client_map_resources_navigation_gate`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinate_bounds_migration_backfills_both_verified_maps()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "111_backfill_verified_map_coordinate_bounds.sql"));

        Assert.Contains("`map_id` = 1675308248", sql, StringComparison.Ordinal);
        Assert.Contains("`maximum_x` = 251", sql, StringComparison.Ordinal);
        Assert.Contains("`maximum_y` = 251", sql, StringComparison.Ordinal);
        Assert.Contains("`map_id` = 557790525", sql, StringComparison.Ordinal);
        Assert.Contains("`maximum_x` = 209", sql, StringComparison.Ordinal);
        Assert.Contains("`maximum_y` = 629", sql, StringComparison.Ordinal);
        Assert.Contains("`coordinate_evidence_status` = 'Verified'", sql, StringComparison.Ordinal);
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
