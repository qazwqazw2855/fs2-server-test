using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class TurnBasedBattlePersistenceTests
{
    [Fact]
    public void Turn_based_battle_migration_is_additive_version_026()
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(RepositoryRoot(), "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "026");
        var ordered = migrations.ToList();

        Assert.Equal("026_turn_based_battles.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "025"));
    }

    [Theory]
    [InlineData("battle_instances")]
    [InlineData("battle_participants")]
    [InlineData("battle_rounds")]
    [InlineData("battle_actions")]
    [InlineData("battle_idempotency")]
    [InlineData("battle_completion")]
    [InlineData("battle_audit")]
    public void Turn_based_battle_migration_contains_required_durable_boundaries(string table)
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "026_turn_based_battles.sql"));

        Assert.Contains($"`{table}`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Turn_based_battle_migration_enforces_version_round_and_unique_action_invariants()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "026_turn_based_battles.sql"));

        Assert.Contains("`BattleVersion` >= 1", sql, StringComparison.Ordinal);
        Assert.Contains("`RoundNumber` >= 1", sql, StringComparison.Ordinal);
        Assert.Contains("`CurrentResolutionIndex` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("UX_BattleAction_ParticipantRound", sql, StringComparison.Ordinal);
        Assert.Contains("UX_BattleParticipant_Formation", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_battle_store_implements_runtime_persistence_and_read_model_contracts()
    {
        var interfaces = typeof(MariaDbTurnBasedBattleStore).GetInterfaces();

        Assert.Contains(typeof(IBattleInstanceRepository), interfaces);
        Assert.Contains(typeof(IBattleActionSubmissionStore), interfaces);
        Assert.Contains(typeof(IBattleIdempotencyStore), interfaces);
        Assert.Contains(typeof(IBattleRuntimeReadModel), interfaces);
    }

    [Fact]
    public void MariaDb_battle_queries_are_parameterized_and_do_not_reference_protocol_transport()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbTurnBasedBattleStore.cs"));

        Assert.Contains("@battleInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("@expectedVersion", source, StringComparison.Ordinal);
        Assert.Contains("@idempotencyKeyHash", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkBytes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_persistence_uses_safe_session_and_idempotency_identifiers()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbTurnBasedBattleStore.cs"));

        Assert.Contains("BattleRuntimeHash.PersistenceKey(idempotencyKey)", source, StringComparison.Ordinal);
        Assert.Contains("BattleRuntimeHash.SessionSafeId(participant.SessionId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Battle_persistence_uses_formal_action_and_idempotency_hash_column_names()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbTurnBasedBattleStore.cs"));

        Assert.Contains("`ActionFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "God2ClassicServer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? "";
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
