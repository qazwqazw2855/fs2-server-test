using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Runtime;
using MySqlConnector;

namespace God2.ClassicServer.Persistence.Tests;

public sealed class EquipmentEnhancementPersistenceTests
{
    [Fact]
    public void MigrationPublishesAllOfficialMaterialFamiliesAndEvidenceBoundaries()
    {
        var sql = Read("Database", "schema", "088_publish_equipment_enhancement_rules.sql");

        Assert.Contains("BETWEEN 9101 AND 9160", sql, StringComparison.Ordinal);
        Assert.Contains("'DestroyTarget'", sql, StringComparison.Ordinal);
        Assert.Contains("'PreserveTarget'", sql, StringComparison.Ordinal);
        Assert.Contains("'OfficialClient'", sql, StringComparison.Ordinal);
        Assert.Contains("'BahamutVerified'", sql, StringComparison.Ordinal);
        Assert.Contains("'CompatibilityEstimate'", sql, StringComparison.Ordinal);
        Assert.Contains("('General',3,10000,'BahamutVerified'", sql, StringComparison.Ordinal);
        Assert.Contains("('General',4,8000,'CompatibilityEstimate'", sql, StringComparison.Ordinal);
        Assert.Contains("('Advanced',4,9000,'CompatibilityEstimate'", sql, StringComparison.Ordinal);
        Assert.Contains("('Special',10,10000,'OfficialClient'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationPublishesReadableTraditionalChineseAdministrationView()
    {
        var sql = Read("Database", "schema", "088_publish_equipment_enhancement_rules.sql");

        Assert.Contains("`vw_equipment_enhancement_rules_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `強化道具名稱`", sql, StringComparison.Ordinal);
        Assert.Contains("AS `成功率百分比`", sql, StringComparison.Ordinal);
        Assert.Contains("'服務端相容估算'", sql, StringComparison.Ordinal);
        Assert.Contains("AS `強化耐久損失`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceGateMigrationDisablesOnlyCompatibilityEstimateRatesAndPreservesCatalogRows()
    {
        var sql = Read("database", "schema", "125_disable_compatibility_estimate_enhancement_rates.sql");

        Assert.Contains("UPDATE `god2_game`.`equipment_enhancement_rates`", sql, StringComparison.Ordinal);
        Assert.Contains("SET `enabled` = 0", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE `evidence_status` = 'CompatibilityEstimate'", sql, StringComparison.Ordinal);
        Assert.Contains("AND `enabled` = 1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OfficialClient'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BahamutVerified'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("item_registry", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("equipment_enhancement_materials", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryUsesOneLockedIdempotentTransactionForMaterialAndTarget()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbEquipmentEnhancementRepository.cs");

        Assert.Contains("BeginTransactionAsync(IsolationLevel.RepeatableRead", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("ConsumeMaterialAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyTargetResultAsync", source, StringComparison.Ordinal);
        Assert.Contains("AdvanceInventoryVersionAsync", source, StringComparison.Ordinal);
        Assert.Contains("InsertReplayAsync", source, StringComparison.Ordinal);
        Assert.Contains("InsertAuditAsync", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
        Assert.Contains("RandomNumberGenerator.GetInt32(10_000)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeLoadsAndExposesEnhancementCatalogAndCoordinator()
    {
        var source = Read("src", "God2.ClassicServer.Persistence", "MariaDbGameplayInventoryRuntime.cs");
        var repository = Read("src", "God2.ClassicServer.Persistence", "MariaDbEquipmentEnhancementCatalogRepository.cs");

        Assert.Contains("IEquipmentEnhancementCoordinator", source, StringComparison.Ordinal);
        Assert.Contains("MariaDbEquipmentEnhancementCatalogRepository", source, StringComparison.Ordinal);
        Assert.Contains("EquipmentEnhancementMaterialCount", source, StringComparison.Ordinal);
        Assert.Contains("EquipmentEnhancementRateCount", source, StringComparison.Ordinal);
        Assert.Contains("EnhanceEquipmentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("`evidence_status`", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("`source_reference_zh_tw`", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceSplitMigrationKeepsEquipmentEnhancementRulesInFormalTablesOnly()
    {
        var sql = Read("database", "schema", "168_split_equipment_enhancement_evidence.sql");

        Assert.Contains("`god2_research`.`equipment_enhancement_material_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("`god2_research`.`equipment_enhancement_rate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP VIEW IF EXISTS `god2_game`.`vw_equipment_enhancement_rules_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_material_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS `ck_equipment_enhancement_rate_evidence`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `evidence_status`", sql, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN IF EXISTS `source_reference_zh_tw`", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_enhancement_rules_readable`", sql, StringComparison.Ordinal);
        Assert.Contains("`成功率百分比`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("material.`evidence_status` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("rate.`source_reference_zh_tw` AS", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "MariaDb")]
    public async Task FormalRepositoryCommitsAndReplaysGuaranteedSpecialEnhancement()
    {
        var password = Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var username = $"enhance_{Guid.NewGuid():N}";
        var characterId = 7_000_000_000_000_000_000L + Random.Shared.Next(1, 1_000_000);
        long accountId = 0;
        long targetInventoryId = 0;
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = 3306,
            Database = "god2",
            UserID = "god2_server",
            Password = password,
            CharacterSet = "utf8mb4"
        }.ConnectionString;

        await using var setup = new MySqlConnection(connectionString);
        await setup.OpenAsync();
        try
        {
            int materialItemId;
            int targetItemId;
            await using (var lookup = setup.CreateCommand())
            {
                lookup.CommandText = """
                    SELECT material_row.`item_id`,target_row.`item_id`
                    FROM (
                        SELECT `item_id`,LEAST(10,FLOOR(COALESCE(`required_level`,0)/10)+1) AS `equipment_tier`
                        FROM `god2_game`.`weapons`
                        WHERE `enabled`=1
                        ORDER BY `required_level`,`item_id`
                        LIMIT 1
                    ) target_row
                    JOIN `god2_game`.`item_registry` material_row
                      ON material_row.`client_item_id`=9120+target_row.`equipment_tier`
                     AND material_row.`enabled`=1;
                    """;
                await using var reader = await lookup.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.False(reader.IsDBNull(0));
                Assert.False(reader.IsDBNull(1));
                materialItemId = reader.GetInt32(0);
                targetItemId = reader.GetInt32(1);
            }

            await using (var account = setup.CreateCommand())
            {
                account.CommandText = """
                    INSERT INTO `god2_player`.`accounts`
                        (`username`,`password_hash`,`status`,`current_session_id`,`concurrency_token`)
                    VALUES (@username,'integration-test-hash','Active',@sessionId,@token);
                    """;
                account.Parameters.AddWithValue("@username", username);
                account.Parameters.AddWithValue("@sessionId", sessionId);
                account.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
                await account.ExecuteNonQueryAsync();
                accountId = account.LastInsertedId;
            }

            await using (var seed = setup.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO `god2_player`.`characters`
                        (`character_id`,`account_id`,`name`,`status`,`enabled`,`concurrency_token`)
                    VALUES (@characterId,@accountId,@name,'Active',1,@token);
                    INSERT INTO `god2_player`.`player_inventory_state`
                        (`CharacterId`,`InventoryId`,`Capacity`,`InventoryVersion`,`MutationSequence`,`DirtyState`,`UpdatedAtUtc`)
                    VALUES (@characterId,@inventoryGuid,20,0,0,'Clean',UTC_TIMESTAMP(6));
                    INSERT INTO `god2_player`.`character_inventory`
                        (`character_id`,`slot_index`,`item_id`,`quantity`,`inventory_version`,`slot_version`,`enabled`,`item_instance_metadata`)
                    VALUES
                        (@characterId,0,@materialItemId,3,0,1,1,'{}'),
                        (@characterId,1,@targetItemId,1,0,1,1,'{}');
                    """;
                seed.Parameters.AddWithValue("@characterId", characterId);
                seed.Parameters.AddWithValue("@accountId", accountId);
                seed.Parameters.AddWithValue("@name", $"強化測試{Random.Shared.Next(100000, 999999)}");
                seed.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
                seed.Parameters.AddWithValue("@inventoryGuid", Guid.NewGuid().ToString());
                seed.Parameters.AddWithValue("@materialItemId", materialItemId);
                seed.Parameters.AddWithValue("@targetItemId", targetItemId);
                await seed.ExecuteNonQueryAsync();
            }

            await using (var targetId = setup.CreateCommand())
            {
                targetId.CommandText = "SELECT `inventory_id` FROM `god2_player`.`character_inventory` WHERE `character_id`=@characterId AND `slot_index`=1;";
                targetId.Parameters.AddWithValue("@characterId", characterId);
                targetInventoryId = Convert.ToInt64(await targetId.ExecuteScalarAsync());
            }

            var options = new DatabaseOptions("127.0.0.1", 3306, "god2", "god2_server", password, 2, "ConfigValue");
            var catalog = await new MariaDbEquipmentEnhancementCatalogRepository(options).LoadAsync(CancellationToken.None);
            var repository = new MariaDbEquipmentEnhancementRepository(options);
            var request = new EquipmentEnhancementTransactionRequest(
                Guid.NewGuid(), Guid.NewGuid().ToString("N"), characterId, accountId, sessionId,
                materialItemId, targetInventoryId, DateTimeOffset.UtcNow);

            var first = await repository.EnhanceAsync(request, new EquipmentEnhancementEngine(catalog), CancellationToken.None);
            var replay = await repository.EnhanceAsync(request, new EquipmentEnhancementEngine(catalog), CancellationToken.None);
            var secondRequest = request with
            {
                TransactionId = Guid.NewGuid(),
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var second = await repository.EnhanceAsync(secondRequest, new EquipmentEnhancementEngine(catalog), CancellationToken.None);

            Assert.True(first.Succeeded, first.FailureCode);
            Assert.True(first.EnhancementSucceeded);
            Assert.InRange(first.EnhancementLevelAfter, 1, 2);
            Assert.Equal(10_000, first.SuccessRateBasisPoints);
            Assert.True(replay.Succeeded, replay.FailureCode);
            Assert.True(replay.Replayed);
            Assert.True(second.Succeeded, second.FailureCode);
            Assert.True(second.EnhancementSucceeded);
            Assert.Equal(1, second.InventoryVersionBefore);
            Assert.Equal(2, second.InventoryVersionAfter);
            Assert.True(second.EnhancementLevelAfter > first.EnhancementLevelAfter);

            await using var verify = setup.CreateCommand();
            verify.CommandText = """
                SELECT
                    (SELECT `quantity` FROM `god2_player`.`character_inventory`
                     WHERE `character_id`=@characterId AND `slot_index`=0),
                    (SELECT `enhancement_level` FROM `god2_player`.`equipment_instances`
                     WHERE `inventory_id`=@targetInventoryId AND `enabled`=1),
                    (SELECT `InventoryVersion` FROM `god2_player`.`player_inventory_state`
                     WHERE `CharacterId`=@characterId);
                """;
            verify.Parameters.AddWithValue("@characterId", characterId);
            verify.Parameters.AddWithValue("@targetInventoryId", targetInventoryId);
            await using var verification = await verify.ExecuteReaderAsync();
            Assert.True(await verification.ReadAsync());
            Assert.Equal(1, verification.GetInt64(0));
            Assert.Equal(second.EnhancementLevelAfter, verification.GetInt32(1));
            Assert.Equal(2, verification.GetInt64(2));
        }
        finally
        {
            if (accountId > 0)
            {
                await using var cleanup = setup.CreateCommand();
                cleanup.CommandText = """
                    DELETE FROM `god2_player`.`character_equipment` WHERE `character_id`=@characterId;
                    DELETE FROM `god2_player`.`character_inventory` WHERE `character_id`=@characterId;
                    DELETE FROM `god2_player`.`characters` WHERE `character_id`=@characterId;
                    DELETE FROM `god2_player`.`accounts` WHERE `account_id`=@accountId;
                    """;
                cleanup.Parameters.AddWithValue("@characterId", characterId);
                cleanup.Parameters.AddWithValue("@accountId", accountId);
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. segments]));

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
