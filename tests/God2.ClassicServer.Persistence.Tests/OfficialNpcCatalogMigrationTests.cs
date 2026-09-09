using System.Text.RegularExpressions;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class OfficialNpcCatalogMigrationTests
{
    [Fact]
    public void Source_row_migration_preserves_all_4449_rows_and_both_duplicate_handle_variants()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "117_catalog_all_official_npc_appearance_source_rows.sql"));
        var rows = Values(sql, "INSERT INTO `god2_game`.`npc_appearance_source_rows`");

        Assert.Equal(4449, Regex.Matches(rows, @"\(1340\d+,'god2-opt-6b127086e0c0',", RegexOptions.CultureInvariant).Count);
        Assert.Equal(2, Regex.Matches(rows, @",6195,", RegexOptions.CultureInvariant).Count);
        Assert.Equal(2, Regex.Matches(rows, @",6276,", RegexOptions.CultureInvariant).Count);
        Assert.Contains("五行魔自選包", rows, StringComparison.Ordinal);
        Assert.Contains("五行封自選包", rows, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (`client_build_id`,`client_entity_handle`)", sql, StringComparison.Ordinal);
        Assert.Contains("Official gamedata SHA-256: c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_catalogs_all_official_npc_identities_and_only_enables_derived_coordinates()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "database", "schema",
            "116_publish_official_npc_appearance_and_coordinates.sql"));
        var identities = Values(sql, "INSERT INTO `god2_game`.`npc_appearance_identities`");
        var coordinates = Values(sql, "INSERT INTO `god2_game`.`npc_coordinate_evidence`");
        var spawns = Values(sql, "INSERT INTO `god2_game`.`npc_spawns`");

        Assert.Equal(4449, Regex.Matches(identities, @"\(1320\d+,'god2-opt-6b127086e0c0',", RegexOptions.CultureInvariant).Count);
        Assert.Equal(4449, Regex.Matches(identities, "'Verified',", RegexOptions.CultureInvariant).Count);
        Assert.Equal(133, Regex.Matches(coordinates, @"\(\d+,'god2-opt-6b127086e0c0',", RegexOptions.CultureInvariant).Count);
        Assert.Equal(18, Regex.Matches(coordinates, @"'Derived',1\)", RegexOptions.CultureInvariant).Count);
        Assert.Equal(18, Regex.Matches(spawns, @"\(1310\d+,1300\d+,", RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("'Candidate','EvidenceBlocked'", spawns, StringComparison.Ordinal);
        Assert.Contains("CHECK (`runtime_enabled`=0 OR `evidence_status` IN ('Verified','Derived'))", sql, StringComparison.Ordinal);
        Assert.Contains("Official gamedata SHA-256: c3bc7ebe8b2e7ea346c376dee25b3b07f90e82208d14f8e26f5feded18bc1e1f", sql, StringComparison.Ordinal);
        Assert.Contains("Official DayMissionDesc SHA-256: f87b26694dfede0de52bb941735630f67f6db7b478c0776b2ce8b1437226e144", sql, StringComparison.Ordinal);
    }

    private static string Values(string sql, string marker)
    {
        var values = sql[sql.IndexOf(marker, StringComparison.Ordinal)..];
        return values[..values.IndexOf("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal)];
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
