using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class QuestRuntimePersistenceTests
{
    [Fact]
    public void Quest_runtime_migration_is_additive_version_029()
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(RepositoryRoot(), "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "029");
        var ordered = migrations.ToList();

        Assert.Equal("029_quest_runtime.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "028"));
    }

    [Theory]
    [InlineData("quest_instances")]
    [InlineData("quest_objective_states")]
    [InlineData("quest_operation_idempotency")]
    [InlineData("quest_progress_mutations")]
    [InlineData("quest_reward_finalization")]
    [InlineData("quest_recovery_state")]
    [InlineData("quest_audit")]
    public void Quest_runtime_migration_contains_required_durable_boundaries(string table)
    {
        Assert.Contains($"`{table}`", MigrationSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void Quest_runtime_migration_enforces_idempotency_versions_and_non_negative_progress()
    {
        var sql = MigrationSql();

        Assert.Contains("UX_QuestObjective_Index", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`Operation`, `IdempotencyKeyHash`)", sql, StringComparison.Ordinal);
        Assert.Contains("UX_QuestProgress_EventObjective", sql, StringComparison.Ordinal);
        Assert.Contains("UX_QuestProgress_Idempotency", sql, StringComparison.Ordinal);
        Assert.Contains("UX_QuestReward_Idempotency", sql, StringComparison.Ordinal);
        Assert.Contains("`QuestVersion` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`CurrentProgress` <= `RequiredCount`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_quest_store_implements_runtime_persistence_contracts()
    {
        var interfaces = typeof(MariaDbQuestRuntimeStore).GetInterfaces();

        Assert.Contains(typeof(IQuestRuntimeStore), interfaces);
        Assert.Contains(typeof(IQuestAcceptanceStore), interfaces);
        Assert.Contains(typeof(IQuestRewardStore), interfaces);
        Assert.Contains(
            typeof(IQuestDefinitionRepository),
            typeof(MariaDbQuestDefinitionRepository).GetInterfaces());
    }

    [Fact]
    public void MariaDb_quest_queries_are_parameterized_transactional_and_transport_free()
    {
        var source = StoreSource();

        Assert.Contains("@characterId", source, StringComparison.Ordinal);
        Assert.Contains("@questInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("@objectiveDefinitionId", source, StringComparison.Ordinal);
        Assert.Contains("@idempotencyKeyHash", source, StringComparison.Ordinal);
        Assert.Contains("@expectedVersion", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Packet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_quest_store_persists_snapshots_and_uses_optimistic_updates()
    {
        var source = StoreSource();

        Assert.Contains("InstanceJson", source, StringComparison.Ordinal);
        Assert.Contains("ResultJson", source, StringComparison.Ordinal);
        Assert.Contains("QuestVersion` = @expectedVersion", source, StringComparison.Ordinal);
        Assert.Contains("ObjectiveVersion` = @expectedVersion", source, StringComparison.Ordinal);
        Assert.Contains("SaveRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("CommitRewardAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_quest_store_uses_formal_replay_hash_column_names()
    {
        var source = StoreSource();

        Assert.Contains("`OperationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`ProgressFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`RewardFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_database_quest_definitions_remain_evidence_blocked()
    {
        var source = StoreSource();
        var definitionSource = source[..source.IndexOf(
            "public sealed class MariaDbQuestRuntimeStore",
            StringComparison.Ordinal)];

        Assert.Contains("QuestType.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("QuestObjectiveType.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("QuestContentStatus.EvidenceBlocked", definitionSource, StringComparison.Ordinal);
        Assert.Contains("RequiredLevelCandidate", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("QuestContentStatus.Verified", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("QuestObjectiveType.KillMonster", definitionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_quest_store_contains_no_embedded_credentials_or_hardcoded_paths()
    {
        var source = StoreSource();

        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=localhost", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GOD2_DB_PASSWORD", source, StringComparison.Ordinal);
        Assert.DoesNotContain(@":\Users\", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string MigrationSql() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "029_quest_runtime.sql"));

    private static string StoreSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbQuestRuntimeStore.cs"));

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
