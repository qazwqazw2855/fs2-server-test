using God2.ClassicServer.Persistence;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class StatusEffectRuntimePersistenceTests
{
    [Fact]
    public void Status_runtime_migration_is_additive_version_028()
    {
        var migrations = SqlMigrationFile.Discover(Path.Combine(RepositoryRoot(), "database", "schema"));
        var migration = Assert.Single(migrations, value => value.Version == "028");
        var ordered = migrations.ToList();

        Assert.Equal("028_status_effect_runtime.sql", migration.Name);
        Assert.True(ordered.IndexOf(migration) > ordered.FindIndex(value => value.Version == "027"));
    }

    [Theory]
    [InlineData("battle_status_participant_versions")]
    [InlineData("battle_status_instances")]
    [InlineData("battle_status_applications")]
    [InlineData("battle_status_removals")]
    [InlineData("battle_status_trigger_plans")]
    [InlineData("battle_status_trigger_results")]
    [InlineData("battle_status_idempotency")]
    [InlineData("battle_status_recovery")]
    [InlineData("battle_status_audit")]
    public void Status_runtime_migration_contains_required_durable_boundaries(string table)
    {
        Assert.Contains($"`{table}`", MigrationSql(), StringComparison.Ordinal);
    }

    [Fact]
    public void Status_runtime_migration_enforces_versions_order_and_non_negative_values()
    {
        var sql = MigrationSql();

        Assert.Contains("PRIMARY KEY (`BattleInstanceId`, `ParticipantId`)", sql, StringComparison.Ordinal);
        Assert.Contains("UX_StatusApplication_Idempotency", sql, StringComparison.Ordinal);
        Assert.Contains("UX_StatusRemoval_Idempotency", sql, StringComparison.Ordinal);
        Assert.Contains("UX_StatusTriggerPlan_Phase", sql, StringComparison.Ordinal);
        Assert.Contains("UX_StatusTriggerResult_Order", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`Scope`, `IdempotencyKeyHash`)", sql, StringComparison.Ordinal);
        Assert.Contains("`ResolutionCursor` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`RemainingRounds` IS NULL OR `RemainingRounds` >= 0", sql, StringComparison.Ordinal);
        Assert.Contains("`Damage` >= 0 AND `Heal` >= 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_status_store_implements_runtime_persistence_and_inspector_contracts()
    {
        var interfaces = typeof(MariaDbStatusRuntimeStore).GetInterfaces();

        Assert.Contains(typeof(IStatusApplicationStore), interfaces);
        Assert.Contains(typeof(IStatusTriggerExecutionStore), interfaces);
        Assert.Contains(typeof(IStatusInstanceStore), interfaces);
        Assert.Contains(typeof(IStatusAuditLedger), interfaces);
        Assert.Contains(typeof(IStatusInspectorSource), interfaces);
        Assert.Contains(
            typeof(IStatusDefinitionRepository),
            typeof(MariaDbStatusDefinitionRepository).GetInterfaces());
    }

    [Fact]
    public void MariaDb_status_queries_are_parameterized_transactional_and_transport_free()
    {
        var source = StoreSource();

        Assert.Contains("@statusInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("@battleInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("@participantId", source, StringComparison.Ordinal);
        Assert.Contains("@statusDefinitionId", source, StringComparison.Ordinal);
        Assert.Contains("@expectedCursor", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransaction", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opcode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Packet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_status_store_persists_runtime_and_recovery_snapshots_atomically()
    {
        var source = StoreSource();

        Assert.Contains("InstanceJson", source, StringComparison.Ordinal);
        Assert.Contains("battle_status_participant_versions", source, StringComparison.Ordinal);
        Assert.Contains("battle_status_recovery", source, StringComparison.Ordinal);
        Assert.Contains("GREATEST(`StatusVersion`", source, StringComparison.Ordinal);
        Assert.Contains("UpdateInstanceLifecycle", source, StringComparison.Ordinal);
        Assert.Contains("UpsertRecovery", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_status_store_uses_formal_idempotency_hash_column_names()
    {
        var source = StoreSource();

        Assert.Contains("`ApplicationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`RemovalFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.Contains("`OperationFingerprintSha256`", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`PayloadHash`", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_status_definitions_promote_public_beta_v0_debuffs_and_buffs()
    {
        var source = StoreSource();
        var definitionSource = source[..source.IndexOf(
            "public sealed class MariaDbStatusRuntimeStore",
            StringComparison.Ordinal)];

        Assert.Contains("StatusCategory.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusPolarity.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusDurationPolicyType.Unknown", definitionSource, StringComparison.Ordinal);
        Assert.Contains("CombatPolicyStatus.EvidenceBlocked", definitionSource, StringComparison.Ordinal);
        Assert.Contains("DurationSeconds", definitionSource, StringComparison.Ordinal);
        Assert.Contains("public_beta_skill_effect_v0", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusDurationPolicyType.Rounds", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusTriggerEffectType.PeriodicDamage", definitionSource, StringComparison.Ordinal);
        Assert.Contains("current_hp_div_10_user_formula", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusModifierType.AttackModifier", definitionSource, StringComparison.Ordinal);
        Assert.Contains("StatusActionRestrictionType.PreventSkillAction", definitionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_status_definitions_read_traditional_public_beta_buff_rows()
    {
        var source = StoreSource();
        var definitionSource = source[..source.IndexOf(
            "public sealed class MariaDbStatusRuntimeStore",
            StringComparison.Ordinal)];

        Assert.Contains("`effect_kind` IN ('buff','增益')", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"屬性增益\" => \"stat_buff\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"五行\" => \"element\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"物理攻擊\"", definitionSource, StringComparison.Ordinal);
        Assert.Contains("\"法術防禦\"", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("澧炵泭", definitionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("灞", definitionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MariaDb_status_store_contains_no_embedded_credentials()
    {
        var source = StoreSource();

        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=localhost", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GOD2_DB_PASSWORD", source, StringComparison.Ordinal);
    }

    private static string MigrationSql() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "database",
            "schema",
            "028_status_effect_runtime.sql"));

    private static string StoreSource() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "God2.ClassicServer.Persistence",
            "MariaDbStatusEffectRuntimeStore.cs"));

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
