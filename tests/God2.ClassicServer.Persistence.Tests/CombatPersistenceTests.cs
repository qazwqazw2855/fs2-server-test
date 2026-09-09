using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class CombatPersistenceTests
{
    [Fact]
    public void Combat_migration_is_additive_version_025()
    {
        var root = RepositoryRoot();
        var migrations = SqlMigrationFile.Discover(Path.Combine(root, "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "025");
        var ordered = migrations.ToList();

        Assert.Equal("025_combat_runtime.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "024"));
    }

    [Theory]
    [InlineData("monster_combat_runtime_state")]
    [InlineData("combat_idempotency")]
    [InlineData("monster_death_records")]
    [InlineData("monster_respawn_schedules")]
    [InlineData("combat_audit")]
    public void Combat_migration_contains_required_durable_boundaries(string table)
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "025_combat_runtime.sql"));

        Assert.Contains($"`{table}`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Combat_migration_enforces_hp_and_stat_invariants()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "025_combat_runtime.sql"));

        Assert.Contains("`MaximumHp` > 0", sql, StringComparison.Ordinal);
        Assert.Contains("`CurrentHp` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`CurrentHp` <= `MaximumHp`", sql, StringComparison.Ordinal);
        Assert.Contains("`AttackPower` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`Defense` >= 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_combat_store_implements_runtime_mutation_contract()
    {
        Assert.Contains(typeof(ICombatMutationStore), typeof(MariaDbCombatMutationStore).GetInterfaces());
        Assert.Contains(typeof(ICombatIdempotencyStore), typeof(ICombatMutationStore).GetInterfaces());
    }

    [Fact]
    public void MariaDb_combat_queries_are_parameterized_and_do_not_reference_protocol()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCombatMutationStore.cs"));

        Assert.Contains("@runtimeEntityId", source, StringComparison.Ordinal);
        Assert.Contains("@idempotencyKeyHash", source, StringComparison.Ordinal);
        Assert.Contains("@characterId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Packet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Combat_migration_preserves_raw_metadata_until_later_formal_rename_without_guessing_stats()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "schema", "025_combat_runtime.sql"));

        Assert.Contains("`RawMetadata` longtext NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`StatPolicyStatus` varchar(32) NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Combat_store_uses_formal_idempotency_hash_column_name()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbCombatMutationStore.cs"));

        Assert.Contains("`CombatFingerprintSha256`", source, StringComparison.Ordinal);
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
