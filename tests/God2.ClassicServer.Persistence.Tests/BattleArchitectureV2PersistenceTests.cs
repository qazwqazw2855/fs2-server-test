using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class BattleArchitectureV2PersistenceTests
{
    [Fact]
    public void Battle_architecture_v2_migration_is_additive_version_030()
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(RepositoryRoot(), "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "030");
        var ordered = migrations.ToList();

        Assert.Equal("030_battle_architecture_v2.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "029"));
    }

    [Theory]
    [InlineData("battle_actor_instances")]
    [InlineData("battle_command_journal")]
    [InlineData("battle_round_plans")]
    [InlineData("battle_action_results")]
    [InlineData("battle_round_checkpoints")]
    [InlineData("battle_event_outbox")]
    [InlineData("battle_finalizations")]
    [InlineData("battle_actor_recovery")]
    public void Migration_contains_every_actor_durability_boundary(string table)
    {
        Assert.Contains($"`{table}`", MigrationSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_enforces_monotonic_sequences_and_exactly_once_keys()
    {
        var sql = MigrationSql();

        Assert.Contains("PRIMARY KEY (`BattleInstanceId`, `JournalSequence`)", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_BattleRoundPlan_Window`", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_BattleActionResult_Order`", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_BattleCheckpoint_Version`", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`BattleInstanceId`, `EventSequence`)", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_BattleFinalization_Idempotency`", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE KEY `UX_BattleRecovery_InstancePayload`", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`JournalSequence` >= 1", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`EventSequence` >= 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_reuses_existing_battle_root_and_is_strictly_additive()
    {
        var sql = MigrationSql();

        Assert.Contains("REFERENCES `battle_instances` (`BattleInstanceId`)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE `battle_instances`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS `battle_idempotency`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_store_implements_the_complete_actor_durability_contract()
    {
        var interfaces = typeof(MariaDbBattleArchitectureV2Store).GetInterfaces();

        Assert.Contains(typeof(IBattleActorDurabilityStore), interfaces);
        Assert.Contains(typeof(IBattleJournal), interfaces);
        Assert.Contains(typeof(IBattleCheckpointStore), interfaces);
        Assert.Contains(typeof(IBattleEventOutbox), interfaces);
        Assert.Contains(typeof(IBattleFinalizationStore), interfaces);
    }

    [Fact]
    public void MariaDb_store_uses_parameterized_transactional_queries()
    {
        var source = StoreSource();

        Assert.Contains("@battleId", source, StringComparison.Ordinal);
        Assert.Contains("@sequence", source, StringComparison.Ordinal);
        Assert.Contains("@payloadHash", source, StringComparison.Ordinal);
        Assert.Contains("@checkpointVersion", source, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"@roundNumber\", roundNumber)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("command.Parameters.AddWithValue(\"@roundNumber\", 1)", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_store_uses_formal_battle_v2_durability_column_names()
    {
        var source = StoreSource();

        Assert.Contains("`CommandSchemaVersion`", source, StringComparison.Ordinal);
        Assert.Contains("`CanonicalCommandJson`", source, StringComparison.Ordinal);
        Assert.Contains("`CommandFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`SnapshotSchemaVersion`", source, StringComparison.Ordinal);
        Assert.Contains("`EventSchemaVersion`", source, StringComparison.Ordinal);
        Assert.Contains("`EventFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`FinalizationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`CanonicalPayload`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_store_contains_no_embedded_credentials_or_machine_paths()
    {
        var source = StoreSource();

        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=localhost", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GOD2_DB_PASSWORD", source, StringComparison.Ordinal);
        var usersSegment = ":" + Path.DirectorySeparatorChar + "Users" + Path.DirectorySeparatorChar;
        Assert.DoesNotContain(usersSegment, source, StringComparison.OrdinalIgnoreCase);
    }

    private static string MigrationSql() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "030_battle_architecture_v2.sql"));

    private static string StoreSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbBattleArchitectureV2Store.cs"));

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
