namespace God2.ClassicServer.Persistence.Tests;

public sealed class CharacterCreationProfileMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "104_publish_verified_default_character_creation_profile.sql");

    private static readonly string IdentityMigrationPath = Path.Combine(
        RepositoryRoot(),
        "database",
        "schema",
        "105_fix_character_identity_generation.sql");

    [Fact]
    public void Migration_publishes_only_the_captured_current_build_profile()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("'god2-opt-6b127086e0c0', 'Swordsman', 'Female', 'LifeSkill1'", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`client_map_id` = 19", sql, StringComparison.Ordinal);
        Assert.Contains("map_row.`client_area_id` = 4", sql, StringComparison.Ordinal);
        Assert.Contains("28, 34, 'Recovered'", sql, StringComparison.Ordinal);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Character_identity_migration_matches_repository_generated_identity_contract()
    {
        var sql = File.ReadAllText(IdentityMigrationPath);
        var repository = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbRuntimeRepositories.cs"));

        Assert.Contains("ALTER TABLE `god2_player`.`characters`", sql, StringComparison.Ordinal);
        Assert.Contains("`character_id` bigint NOT NULL AUTO_INCREMENT", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("characterId = command.LastInsertedId;", repository, StringComparison.Ordinal);
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
