using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class SkillRuntimePersistenceTests
{
    [Fact]
    public void Skill_runtime_migration_is_additive_version_027()
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(RepositoryRoot(), "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "027");
        var ordered = migrations.ToList();

        Assert.Equal("027_skill_runtime.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "026"));
    }

    [Theory]
    [InlineData("skill_executions")]
    [InlineData("skill_effect_executions")]
    [InlineData("skill_cost_reservations")]
    [InlineData("battle_skill_usage")]
    [InlineData("skill_idempotency")]
    [InlineData("skill_audit")]
    public void Skill_runtime_migration_contains_required_durable_boundaries(string table)
    {
        Assert.Contains($"`{table}`", MigrationSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void Skill_runtime_migration_enforces_order_idempotency_and_non_negative_values()
    {
        var sql = MigrationSql();

        Assert.Contains("UX_SkillExecution_BattleAction", sql, StringComparison.Ordinal);
        Assert.Contains("UX_SkillEffect_Order", sql, StringComparison.Ordinal);
        Assert.Contains("UX_SkillCost_Execution", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`Scope`, `IdempotencyKeyHash`)", sql, StringComparison.Ordinal);
        Assert.Contains("`ResolutionCursor` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`Damage` >= 0 AND `Heal` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`UsageCount` >= 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Skill_runtime_migration_cascades_from_battle_and_execution_boundaries()
    {
        var sql = MigrationSql();

        Assert.Contains("FK_SkillExecution_Battle", sql, StringComparison.Ordinal);
        Assert.Contains("FK_SkillEffect_Execution", sql, StringComparison.Ordinal);
        Assert.Contains("FK_SkillCost_Execution", sql, StringComparison.Ordinal);
        Assert.Contains("FK_SkillUsage_Battle", sql, StringComparison.Ordinal);
        Assert.Contains("FK_SkillIdempotency_Execution", sql, StringComparison.Ordinal);
        Assert.Contains("FK_SkillAudit_Execution", sql, StringComparison.Ordinal);
        Assert.Equal(6, CountOccurrences(sql, "ON DELETE CASCADE"));
    }

    [Fact]
    public void MariaDb_skill_store_implements_runtime_persistence_and_inspector_contracts()
    {
        var interfaces = typeof(MariaDbSkillRuntimeStore).GetInterfaces();

        Assert.Contains(typeof(ISkillExecutionStore), interfaces);
        Assert.Contains(typeof(ISkillCostReservationStore), interfaces);
        Assert.Contains(typeof(ISkillUsageStore), interfaces);
        Assert.Contains(typeof(ISkillAuditLedger), interfaces);
        Assert.Contains(typeof(ISkillInspectorSource), interfaces);
        Assert.Contains(typeof(ISkillDefinitionRepository), typeof(MariaDbSkillDefinitionRepository).GetInterfaces());
    }

    [Fact]
    public void MariaDb_skill_queries_are_parameterized_transactional_and_transport_free()
    {
        var source = StoreSource();

        Assert.Contains("@skillExecutionId", source, StringComparison.Ordinal);
        Assert.Contains("@battleInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("@participantId", source, StringComparison.Ordinal);
        Assert.Contains("@skillDefinitionId", source, StringComparison.Ordinal);
        Assert.Contains("@expectedCursor", source, StringComparison.Ordinal);
        Assert.Contains("SELECT `UsageJson`", source, StringComparison.Ordinal);
        Assert.Contains("WHERE `BattleInstanceId` = @battleInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransaction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Packet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkBytes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_skill_store_persists_safe_ids_without_credentials()
    {
        var source = StoreSource();

        Assert.Contains("BattleRuntimeHash.PersistenceKey", source, StringComparison.Ordinal);
        Assert.Contains("BattleRuntimeHash.SafeId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GOD2_DB_PASSWORD", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_skill_store_uses_formal_execution_hash_column_names()
    {
        var source = StoreSource();

        Assert.Contains("`ExecutionFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_skill_definitions_promote_public_beta_v0_effects_without_mutating_legacy_skill_rows()
    {
        var source = StoreSource();
        var definitionSource = source[..source.IndexOf(
            "public sealed class MariaDbSkillRuntimeStore",
            StringComparison.Ordinal)];

        Assert.Contains("SkillCategory.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillActionCategory.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillTargetPolicyType.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("public_beta_skill_effect_v0", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillEffectType.ApplyStatus", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillEffectType.Heal", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillEffectType.RemoveStatus", definitionSource, StringComparison.Ordinal);
        Assert.Contains("SkillEffectType.Revive", definitionSource, StringComparison.Ordinal);
        Assert.Contains("CombatPolicyStatus.ContentBacked", definitionSource, StringComparison.Ordinal);
        Assert.True(CountOccurrences(definitionSource, "CombatPolicyStatus.EvidenceBlocked") >= 5);
        Assert.Contains("SkillResourceType.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO `skills`", PublicBetaMigrationSql(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE `skills`", PublicBetaMigrationSql(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Database_skill_definitions_normalize_traditional_public_beta_labels()
    {
        var source = StoreSource();
        var definitionSource = source[..source.IndexOf(
            "public sealed class MariaDbSkillRuntimeStore",
            StringComparison.Ordinal)];

        Assert.Contains("\"增益\" => \"buff\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"負面\" => \"debuff\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"生命治療\" => \"heal_hp\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"全部負面狀態\" => \"all_negative\"", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("澧炵泭", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("灞", definitionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_beta_skill_effect_v0_migration_imports_all_candidate_functions()
    {
        var sql = PublicBetaMigrationSql();

        Assert.Contains("148_publish_public_beta_skill_effect_v0.sql", PublicBetaMigration().Name, StringComparison.Ordinal);
        Assert.Contains("`public_beta_skill_effect_v0`", sql, StringComparison.Ordinal);
        Assert.Contains("(1,1,0,1,'劍術Lv1'", sql, StringComparison.Ordinal);
        Assert.Contains("(851,83,3,83,'石化解除Lv2'", sql, StringComparison.Ordinal);
        Assert.Contains("'stat_buff'", sql, StringComparison.Ordinal);
        Assert.Contains("'heal_hp'", sql, StringComparison.Ordinal);
        Assert.Contains("'debuff_poison'", sql, StringComparison.Ordinal);
        Assert.Contains("'cleanse_general'", sql, StringComparison.Ordinal);
        Assert.Contains("'revive'", sql, StringComparison.Ordinal);
        Assert.Contains("'revive','revive','dead_ally'", sql, StringComparison.Ordinal);
        Assert.Contains("floor(current_hp/10)", sql, StringComparison.Ordinal);
    }

    private static string MigrationSql() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "027_skill_runtime.sql"));

    private static string StoreSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbSkillRuntimeStore.cs"));

    private static SqlMigrationFile PublicBetaMigration() =>
        Assert.Single(SqlMigrationFile.Discover(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema")), value => value.Version == "148");

    private static string PublicBetaMigrationSql() =>
        File.ReadAllText(PublicBetaMigration().Path);

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
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
