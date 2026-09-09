using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class PlayerSocialPersistenceTests
{
    [Fact]
    public void Migration_creates_relationship_invitation_block_and_idempotency_tables()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "142_add_server_owned_player_social_relationships.sql"));

        Assert.Contains("`player_social_invitations`", sql, StringComparison.Ordinal);
        Assert.Contains("`player_relationships`", sql, StringComparison.Ordinal);
        Assert.Contains("`player_social_blocks`", sql, StringComparison.Ordinal);
        Assert.Contains("`player_social_operations`", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`character_id_low` < `character_id_high`)", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`actor_character_id`,`request_id`)", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`kind` IN ('NameCard'))", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_repository_composes_against_the_server_owned_social_contract()
    {
        var repository = new MariaDbPlayerSocialRepository(
            new DatabaseOptions("127.0.0.1", 3306, "god2", "god2", string.Empty, 1));

        Assert.IsAssignableFrom<IPlayerSocialRepository>(repository);
        Assert.Contains("`status` = 'Active'", MariaDbPlayerSocialRepository.ActiveRelationshipQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void Invitation_identity_materialization_accepts_mysqlconnector_guid_and_text_forms()
    {
        var expected = Guid.Parse("bce2ec45-d6ed-4c8b-a9be-95b44a040d5c");

        Assert.Equal(expected, MariaDbPlayerSocialRepository.MaterializeGuid(expected));
        Assert.Equal(expected, MariaDbPlayerSocialRepository.MaterializeGuid(expected.ToString("D")));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "God2ClassicServer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
