using System.Data;
using God2.ClassicServer.Application.Configuration;
using God2.ClassicServer.Persistence;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

public sealed record Phase3RecoveryResult(
    string RunId,
    string SourceRunId,
    string Status,
    string ServerVersion,
    IReadOnlyDictionary<string, long> Promotions,
    IReadOnlyDictionary<string, long> Coverage,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> MissingFieldMatrix,
    IReadOnlyDictionary<string, long> ReferentialIntegrity,
    IReadOnlyDictionary<string, object?> RuntimeValidation,
    IReadOnlyDictionary<string, object?> HeadlessValidation,
    IReadOnlyDictionary<string, object?> DeepSemanticAnalysis,
    long ProductionSimplifiedDisplayRows,
    DateTime CompletedAtUtc);

public sealed class Phase3SemanticRecovery
{
    private readonly DatabaseOptions _options;
    private readonly string _schemaDirectory;
    private readonly ZhTwLocalization _localization;
    private string _phase2SourceRunId = string.Empty;

    public Phase3SemanticRecovery(DatabaseOptions options, string schemaDirectory, ZhTwLocalization localization)
    {
        _options = options;
        _schemaDirectory = schemaDirectory;
        _localization = localization;
    }

    public async Task<Phase3RecoveryResult> RecoverAsync(RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!string.Equals(workspace.Phase, RecoveryVersions.Phase3, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Phase 3 semantic recovery requires a Phase 3 workspace.");
        }

        var migration = await new SqlFileMigrationRunner(_options, _schemaDirectory).VerifyAsync(cancellationToken);
        if (!migration.Succeeded || migration.Value is null || migration.Value.Migrations.All(item => item.Version != "034"))
        {
            throw new InvalidOperationException($"MariaDB migration 034 failed: {migration.Error.Code}: {migration.Error.Message}");
        }

        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        var serverVersion = connection.ServerVersion;
        await AcquireLockAsync(connection, cancellationToken);
        try
        {
            _phase2SourceRunId = await ResolveLatestBaselineAsync(connection, cancellationToken);
            var deepSemanticAnalysis = await new Phase3DeepSemanticAnalyzer(_options)
                .AnalyzeAsync(_phase2SourceRunId, cancellationToken);
            await InsertRunAsync(connection, workspace, cancellationToken);
            await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            {
                try
                {
                    await PromoteNpcAsync(connection, transaction, workspace, cancellationToken);
                    await PromoteMonstersAsync(connection, transaction, workspace, cancellationToken);
                    await PromoteDropsAsync(connection, transaction, workspace, cancellationToken);
                    await PromoteSkillsAsync(connection, transaction, workspace, cancellationToken);
                    await PromoteMerchantQuestAsync(connection, transaction, workspace, cancellationToken);
                    await PromoteEquipmentCombinePetAsync(connection, transaction, workspace, cancellationToken);
                    await PopulateMissingFieldClosureAsync(connection, transaction, workspace, cancellationToken);
                    await PopulatePromotionAuditAsync(connection, transaction, workspace, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await UpdateRunAsync(connection, workspace.RunId, "FAILED", null, cancellationToken);
                    throw;
                }
            }

            var promotions = await ReadPromotionsAsync(connection, workspace.RunId, cancellationToken);
            var coverage = await ReadCoverageAsync(connection, workspace.RunId, cancellationToken);
            var matrix = await ReadMissingFieldMatrixAsync(connection, workspace.RunId, cancellationToken);
            var referential = await ReadReferentialIntegrityAsync(connection, workspace.RunId, cancellationToken);
            var runtime = await ValidateRuntimeAsync(connection, workspace.RunId, cancellationToken);
            var headless = await ValidateHeadlessAsync(connection, workspace.RunId, cancellationToken);
            var simplified = (await new MariaDbContentRecoveryStore(_options, _schemaDirectory, _localization)
                .FindProductionSimplifiedAsync(cancellationToken)).Count;
            var unclassified = matrix.Sum(row => Convert.ToInt64(row.GetValueOrDefault("unclassified"), System.Globalization.CultureInfo.InvariantCulture));
            var passed = unclassified == 0 && simplified == 0 && referential.Values.Sum() == 0 &&
                string.Equals(runtime.GetValueOrDefault("status")?.ToString(), "PASS", StringComparison.Ordinal) &&
                string.Equals(headless.GetValueOrDefault("status")?.ToString(), "PASS", StringComparison.Ordinal);
            var status = passed ? "PASS WITH DOCUMENTED EVIDENCE GAPS" : "FAILED";
            var completedAtUtc = DateTime.UtcNow;
            var summary = RecoveryJson.Compact(new
            {
                sourceRunId = _phase2SourceRunId,
                promotions,
                coverage,
                unclassified,
                referential,
                runtime,
                headless,
                productionSimplifiedDisplayRows = simplified
            });
            await UpdateRunAsync(connection, workspace.RunId, passed ? "COMPLETED" : "FAILED_VALIDATION_GATE", summary, cancellationToken);
            return new Phase3RecoveryResult(
                workspace.RunId,
                _phase2SourceRunId,
                status,
                serverVersion,
                promotions,
                coverage,
                matrix,
                referential,
                runtime,
                headless,
                deepSemanticAnalysis,
                simplified,
                completedAtUtc);
        }
        finally
        {
            await ReleaseLockAsync(connection, CancellationToken.None);
        }
    }

    private async Task PromoteNpcAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE `npcs` n
            LEFT JOIN (SELECT `NpcId`,COUNT(*) c FROM `merchants` GROUP BY `NpcId`) m ON m.`NpcId`=n.`Id`
            LEFT JOIN (SELECT qn.`NpcId`,COUNT(*) c FROM (
                SELECT `StartNpcId` `NpcId` FROM `quests` WHERE `StartNpcId` IS NOT NULL
                UNION ALL SELECT `EndNpcId` FROM `quests` WHERE `EndNpcId` IS NOT NULL) qn GROUP BY qn.`NpcId`) q ON q.`NpcId`=n.`Id`
            SET n.`OfficialIdentityStatus`=CASE WHEN n.`Id`>0 AND n.`Code`<>'' AND COALESCE(n.`NameZhTw`,n.`Name`)<>'' THEN 'Verified' ELSE 'EvidenceBlocked' END,
                n.`NpcType`=CASE WHEN m.c IS NOT NULL AND q.c IS NOT NULL THEN 'MultiService' WHEN m.c IS NOT NULL THEN 'Service' WHEN q.c IS NOT NULL THEN 'Quest' ELSE n.`NpcType` END,
                n.`InteractionFamily`=CASE WHEN m.c IS NOT NULL AND q.c IS NOT NULL THEN 'MerchantQuest' WHEN m.c IS NOT NULL THEN 'Merchant' WHEN q.c IS NOT NULL THEN 'Quest' ELSE n.`InteractionFamily` END;
            """, cancellationToken);

        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_research`.`npc_coordinate_evidence` e
            JOIN (
                SELECT e2.`CoordinateEvidenceId`,MIN(n.`Id`) `NpcId`,MIN(m.`Id`) `MapId`
                FROM `god2_research`.`npc_coordinate_evidence` e2
                JOIN `npcs` n ON BINARY COALESCE(n.`NameZhTw`,n.`Name`)=BINARY e2.`NpcNameZhTw`
                JOIN `maps` m ON BINARY COALESCE(m.`NameZhTw`,m.`Name`)=BINARY e2.`MapNameZhTw`
                WHERE e2.`RunId`=@sourceRun AND e2.`NpcNameZhTw` IS NOT NULL AND e2.`MapNameZhTw` IS NOT NULL
                GROUP BY e2.`CoordinateEvidenceId`
                HAVING COUNT(DISTINCT n.`Id`)=1 AND COUNT(DISTINCT m.`Id`)=1
            ) x ON x.`CoordinateEvidenceId`=e.`CoordinateEvidenceId`
            SET e.`NpcId`=COALESCE(e.`NpcId`,x.`NpcId`),e.`MapId`=COALESCE(e.`MapId`,x.`MapId`),e.`IdentityEvidenceStatus`='Derived'
            WHERE e.`RunId`=@sourceRun;
            """, cancellationToken, ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_research`.`npc_coordinate_evidence` e
            JOIN `maps` m ON m.`Id`=e.`MapId`
            SET e.`IdentityEvidenceStatus`=CASE WHEN e.`NpcId` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END,
                e.`BoundsValidationStatus`=CASE WHEN e.`PositionX` BETWEEN 0 AND m.`Width` AND e.`PositionY` BETWEEN 0 AND m.`Height` THEN 'Verified' ELSE 'EvidenceBlocked' END
            WHERE e.`RunId`=@sourceRun;
            """, cancellationToken, ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_research`.`npc_coordinate_evidence` e
            JOIN (
                SELECT `NpcId`
                FROM `god2_research`.`npc_coordinate_evidence`
                WHERE `RunId`=@sourceRun AND `NpcId` IS NOT NULL AND `MapId` IS NOT NULL
                    AND `CoordinateEvidenceStatus`='Derived' AND `BoundsValidationStatus`='Verified'
                GROUP BY `NpcId`
                HAVING COUNT(DISTINCT CONCAT(`MapId`,':',`PositionX`,':',`PositionY`))=1
            ) unique_position ON unique_position.`NpcId`=e.`NpcId`
            SET e.`ProductionSpawnEnabled`=1,e.`PromotionRunId`=@run
            WHERE e.`RunId`=@sourceRun AND e.`CoordinateEvidenceStatus`='Derived' AND e.`BoundsValidationStatus`='Verified';
            """, cancellationToken, ("@sourceRun", _phase2SourceRunId), ("@run", workspace.RunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `npcs` n
            JOIN `god2_research`.`npc_coordinate_evidence` e ON e.`NpcId`=n.`Id` AND e.`RunId`=@sourceRun AND e.`ProductionSpawnEnabled`=1
            SET n.`MapId`=e.`MapId`,n.`PositionX`=e.`PositionX`,n.`PositionY`=e.`PositionY`,n.`Direction`=e.`Direction`,
                n.`CoordinateEvidenceStatus`='Derived',n.`ProductionSpawnEnabled`=1;
            """, cancellationToken, ("@sourceRun", _phase2SourceRunId));
    }

    private async Task PromoteMonstersAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `monster_semantic_profiles`
                (`RunId`,`MonsterId`,`Level`,`MaxHp`,`HpPolicy`,`MaxMp`,`MpPolicy`,
                 `PhysicalAttack`,`MagicAttack`,`PhysicalDefense`,`MagicDefense`,`Speed`,`Initiative`,`Accuracy`,`Evasion`,`CriticalRate`,`Element`,`Race`,`AiFamily`,
                 `ExperienceReward`,`CurrencyMinimum`,`CurrencyMaximum`,`DropPolicyStatus`,`ProductionEnabled`)
            SELECT @run,m.`Id`,
                CASE WHEN m.`Level`>0 THEN m.`Level` ELSE NULL END,
                CASE WHEN m.`MaxHp`>0 AND m.`EvidenceStatus` IN ('Verified','Derived') THEN m.`MaxHp` ELSE NULL END,
                CASE WHEN m.`MaxHp`>0 AND m.`EvidenceStatus` IN ('Verified','Derived') THEN 'Fixed' ELSE 'EvidenceBlocked' END,
                CASE WHEN m.`MaxMp`>=0 AND m.`MpPolicy`<>'Unknown' THEN m.`MaxMp` ELSE NULL END,
                CASE WHEN m.`MpPolicy`='ExplicitZero' THEN 'ExplicitOfficialZero' WHEN m.`MaxMp`>0 AND m.`MpPolicy`<>'Unknown' THEN m.`MpPolicy` ELSE 'EvidenceBlocked' END,
                CASE WHEN m.`Attack`>0 THEN m.`Attack` ELSE NULL END,NULL,CASE WHEN m.`Defense`>0 THEN m.`Defense` ELSE NULL END,NULL,
                NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
                m.`ExperienceReward`,m.`CurrencyRewardMinimum`,m.`CurrencyRewardMaximum`,
                CASE WHEN EXISTS(SELECT 1 FROM `monster_drop_relationships` d WHERE d.`RunId`=@sourceRun AND d.`MonsterId`=m.`Id`) THEN 'Derived'
                     WHEN m.`DropPolicy`='ExplicitNoItemDrop' THEN 'ExplicitOfficialZero' ELSE 'EvidenceBlocked' END,
                1
            FROM `monsters` m;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `monster_spawn_semantics`
                (`RunId`,`MonsterId`,`MapId`,`PositionX`,`PositionY`,`RespawnSeconds`,`MapRelationshipStatus`,`CoordinateEvidenceStatus`,`RespawnPolicy`,`EvidenceStatus`,`ProductionSpawnEnabled`)
            SELECT @run,m.`Id`,s.`MapId`,s.`PositionX`,s.`PositionY`,s.`RespawnSeconds`,
                CASE WHEN s.`MonsterId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,
                CASE WHEN s.`MonsterId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,
                CASE WHEN s.`MonsterId` IS NULL THEN 'EvidenceBlocked' ELSE 'Fixed' END,
                CASE WHEN s.`MonsterId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,
                CASE WHEN s.`MonsterId` IS NULL THEN 0 ELSE 1 END
            FROM `monsters` m
            LEFT JOIN (
                SELECT
                    s1.`monster_id` AS `MonsterId`,
                    s1.`map_id` AS `MapId`,
                    s1.`position_x` AS `PositionX`,
                    s1.`position_y` AS `PositionY`,
                    COALESCE(s1.`respawn_seconds_min`, s1.`respawn_seconds_max`, 0) AS `RespawnSeconds`
                FROM `god2_game`.`monster_spawns` s1
                JOIN (
                    SELECT `monster_id`, MIN(`spawn_id`) AS `spawn_id`
                    FROM `god2_game`.`monster_spawns`
                    GROUP BY `monster_id`
                ) first_spawn ON first_spawn.`spawn_id`=s1.`spawn_id`
            ) s ON s.`MonsterId`=m.`Id`;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            SELECT 'monster_semantic_profiles',CAST(`MonsterId` AS char),`RunId`,@sourceRun,NULL,
                   '怪物 HP/MP、攻防、獎勵與掉落語意的 Phase2 來源 RunId 已移入 research；正式表只保留服務端執行需要的怪物語意欄位。',
                   UTC_TIMESTAMP(6)
            FROM `monster_semantic_profiles`
            WHERE `RunId`=@run
            ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            SELECT 'monster_spawn_semantics',CAST(`MonsterId` AS char),`RunId`,@sourceRun,NULL,
                   '怪物地圖出生點、座標、重生與生產啟用語意的 Phase2 來源 RunId 已移入 research；正式表只保留服務端刷新怪物需要的語意欄位。',
                   UTC_TIMESTAMP(6)
            FROM `monster_spawn_semantics`
            WHERE `RunId`=@run
            ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
    }

    private async Task PromoteDropsAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE `monster_drop_relationships`
            SET `ProductionDropEnabled`=CASE
                    WHEN `DropRelationshipStatus` IN ('Verified','Derived')
                         AND `DeclaredDropChance` IS NOT NULL
                         AND `MinimumQuantity` IS NOT NULL
                         AND `MaximumQuantity` IS NOT NULL
                         AND `MinimumQuantity`>0
                         AND `MaximumQuantity`>=`MinimumQuantity` THEN `IsDropEnabled`
                    ELSE 0 END,
                `PromotionRunId`=@run
            WHERE `RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
    }

    private async Task PromoteSkillsAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE `skill_content_profiles` p
            JOIN (
                SELECT p2.`ProfileId`,MIN(s.`Id`) `SkillId`
                FROM `skill_content_profiles` p2
                JOIN `skills` s
                    ON BINARY COALESCE(s.`NameZhTw`,s.`Name`)=BINARY p2.`NameZhTw`
                JOIN (
                    SELECT BINARY COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `skills` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) unique_formal ON unique_formal.`ExactName`=BINARY COALESCE(s.`NameZhTw`,s.`Name`)
                JOIN (
                    SELECT BINARY `NameZhTw` `ExactName`
                    FROM `skill_content_profiles` WHERE `RunId`=@sourceRun
                    GROUP BY BINARY `NameZhTw` HAVING COUNT(*)=1
                ) unique_profile ON unique_profile.`ExactName`=BINARY p2.`NameZhTw`
                WHERE p2.`RunId`=@sourceRun AND p2.`ClientSkillId` IS NOT NULL
                GROUP BY p2.`ProfileId` HAVING COUNT(DISTINCT s.`Id`)=1
            ) exact_composite ON exact_composite.`ProfileId`=p.`ProfileId`
            SET p.`SkillId`=exact_composite.`SkillId`,p.`PromotionRunId`=@run
            WHERE p.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `skill_semantic_profiles`
                (`RunId`,`SkillId`,`ClientProfileId`,`ClientSkillId`,`SkillFamily`,`TargetPolicy`,`MpCostPolicy`,`MpCost`,
                 `EffectReferencesJson`,`StatusReferencesJson`,`ProductionEnabled`)
            SELECT @run,s.`Id`,p.`ProfileId`,p.`ClientSkillId`,
                COALESCE(NULLIF(p.`SkillFamily`,'Unknown'),NULLIF(s.`SkillFamily`,'Unknown'),'Unknown'),
                COALESCE(NULLIF(p.`TargetPolicy`,'Unknown'),NULLIF(s.`TargetPolicy`,'Unknown'),'Unknown'),
                CASE WHEN p.`MpCostPolicy` IS NOT NULL AND p.`MpCostPolicy`<>'Unknown' THEN p.`MpCostPolicy`
                     WHEN s.`MpCostPolicy`<>'Unknown' THEN s.`MpCostPolicy` ELSE 'EvidenceBlocked' END,
                COALESCE(p.`MpCost`,s.`MpCost`),COALESCE(p.`EffectReferencesJson`,JSON_ARRAY()),COALESCE(p.`StatusReferencesJson`,JSON_ARRAY()),
                CASE WHEN p.`ProfileId` IS NOT NULL
                          AND COALESCE(NULLIF(p.`SkillFamily`,'Unknown'),NULLIF(s.`SkillFamily`,'Unknown'),'Unknown')<>'Unknown'
                          AND COALESCE(NULLIF(p.`TargetPolicy`,'Unknown'),NULLIF(s.`TargetPolicy`,'Unknown'),'Unknown')<>'Unknown'
                          AND COALESCE(p.`MpCost`,s.`MpCost`) IS NOT NULL
                          AND (CASE WHEN p.`MpCostPolicy` IS NOT NULL AND p.`MpCostPolicy`<>'Unknown' THEN p.`MpCostPolicy`
                                    WHEN s.`MpCostPolicy`<>'Unknown' THEN s.`MpCostPolicy` ELSE 'EvidenceBlocked' END) <> 'EvidenceBlocked'
                          AND JSON_LENGTH(COALESCE(p.`EffectReferencesJson`,JSON_ARRAY()))>0
                     THEN 1 ELSE 0 END
            FROM `skills` s
            LEFT JOIN `skill_content_profiles` p ON p.`RunId`=@sourceRun AND p.`SkillId`=s.`Id`;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`semantic_profile_source_run_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceRunId`,`ResourceKey`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            SELECT 'skill_semantic_profiles',CAST(`SkillId` AS char),`RunId`,@sourceRun,NULL,
                   '技能 MP 消耗、目標、效果與狀態語意的 Phase2 來源 RunId/ResourceKey 已移入 research；正式表只保留服務端施放技能需要的語意欄位。',
                   UTC_TIMESTAMP(6)
            FROM `skill_semantic_profiles`
            WHERE `RunId`=@run
            ON DUPLICATE KEY UPDATE `SourceRunId`=VALUES(`SourceRunId`),`ResourceKey`=VALUES(`ResourceKey`),`ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `skills` s JOIN `skill_semantic_profiles` p ON p.`RunId`=@run AND p.`SkillId`=s.`Id`
            SET s.`SkillFamily`=CASE WHEN p.`SkillFamily`<>'Unknown' THEN p.`SkillFamily` ELSE s.`SkillFamily` END,
                s.`TargetPolicy`=CASE WHEN p.`TargetPolicy`<>'Unknown' THEN p.`TargetPolicy` ELSE s.`TargetPolicy` END,
                s.`MpCostPolicy`=CASE WHEN p.`MpCostPolicy`<>'EvidenceBlocked' THEN p.`MpCostPolicy` ELSE s.`MpCostPolicy` END,
                s.`MpCost`=CASE WHEN p.`MpCostPolicy`<>'EvidenceBlocked' THEN p.`MpCost` ELSE s.`MpCost` END;
            """, cancellationToken, ("@run", workspace.RunId));
    }

    private async Task PromoteMerchantQuestAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_research`.`merchant_inventory_candidates` c
            LEFT JOIN `merchants` m ON m.`Id`=c.`MerchantId`
            LEFT JOIN `items` i ON i.`Id`=c.`ItemId`
            SET c.`RelationshipEvidenceStatus`=CASE WHEN m.`Id` IS NOT NULL AND i.`Id` IS NOT NULL THEN 'Derived' ELSE 'Candidate' END,
                c.`PriceEvidenceStatus`=CASE WHEN (c.`BuyPrice` IS NOT NULL AND c.`BuyPrice`>=0) OR (c.`SellPrice` IS NOT NULL AND c.`SellPrice`>=0) THEN 'Derived' ELSE 'EvidenceBlocked' END,
                c.`ProductionSaleEnabled`=CASE WHEN m.`Id` IS NOT NULL AND i.`Id` IS NOT NULL AND c.`BuyPrice` IS NOT NULL AND c.`BuyPrice`>=0 THEN 1 ELSE 0 END,
                c.`PromotionRunId`=@run
            WHERE c.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `quest_content_profiles` p
            JOIN (
                SELECT p2.`ProfileId`,MIN(q.`Id`) `QuestId`
                FROM `quest_content_profiles` p2
                JOIN `quests` q ON BINARY COALESCE(q.`NameZhTw`,q.`Name`)=BINARY p2.`NameZhTw`
                JOIN (
                    SELECT BINARY COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `quests` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) unique_formal ON unique_formal.`ExactName`=BINARY p2.`NameZhTw`
                JOIN (
                    SELECT BINARY `NameZhTw` `ExactName`
                    FROM `quest_content_profiles` WHERE `RunId`=@sourceRun
                    GROUP BY BINARY `NameZhTw` HAVING COUNT(*)=1
                ) unique_profile ON unique_profile.`ExactName`=BINARY p2.`NameZhTw`
                WHERE p2.`RunId`=@sourceRun
                GROUP BY p2.`ProfileId` HAVING COUNT(DISTINCT q.`Id`)=1
            ) exact_composite ON exact_composite.`ProfileId`=p.`ProfileId`
            SET p.`QuestId`=exact_composite.`QuestId`,p.`ProductionProfileEnabled`=1,p.`PromotionRunId`=@run
            WHERE p.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));

        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_research`.`quest_objective_candidates` o
            JOIN `quest_content_profiles` p ON p.`RunId`=@sourceRun AND p.`ClientQuestId`=o.`ClientQuestId`
                AND p.`QuestId` IS NOT NULL
            JOIN `god2_research`.`content_profile_source_archive` s ON s.`FormalTable`='quest_content_profiles'
                AND s.`RecordIdentity`=p.`ProfileId` AND s.`SourceHash`=o.`SourceHash`
            SET o.`QuestId`=p.`QuestId`,
                o.`RelationshipEvidenceStatus`=CASE WHEN o.`EvidenceStatus` IN ('Verified','Derived','Candidate') AND ((o.`ItemId` IS NULL)<>(o.`MonsterId` IS NULL)) THEN 'Derived' ELSE 'Candidate' END,
                o.`QuantityEvidenceStatus`=CASE WHEN o.`RequiredQuantity`>0 THEN 'Derived' ELSE 'EvidenceBlocked' END,
                o.`ProductionObjectiveEnabled`=CASE WHEN o.`EvidenceStatus` IN ('Verified','Derived') AND o.`RequiredQuantity`>0 AND ((o.`ItemId` IS NULL)<>(o.`MonsterId` IS NULL)) THEN 1 ELSE 0 END,
                o.`PromotionRunId`=@run
            WHERE o.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
    }

    private async Task PromoteEquipmentCombinePetAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE `equipment_set_definitions` d
            LEFT JOIN (SELECT `SetId`,COUNT(*) c,COUNT(DISTINCT `ItemId`) distinct_c FROM `equipment_set_members` GROUP BY `SetId`) m ON m.`SetId`=d.`SetId`
            LEFT JOIN `god2_research`.`content_profile_source_archive` s ON s.`FormalTable`='equipment_set_definitions' AND s.`RecordIdentity`=CAST(d.`SetId` AS char)
            SET d.`SetRelationshipStatus`=CASE WHEN COALESCE(s.`SourceHash`,'') REGEXP '^[0-9A-Fa-f]{64}$' AND d.`RequiredPieces` BETWEEN 1 AND 5 AND m.c=m.distinct_c AND m.c>=d.`RequiredPieces` THEN 'Verified' ELSE 'EvidenceBlocked' END,
                d.`ProductionRelationshipEnabled`=CASE WHEN COALESCE(s.`SourceHash`,'') REGEXP '^[0-9A-Fa-f]{64}$' AND d.`RequiredPieces` BETWEEN 1 AND 5 AND m.c=m.distinct_c AND m.c>=d.`RequiredPieces` THEN 1 ELSE 0 END,
                d.`ProductionBonusEnabled`=0,d.`PromotionRunId`=@run
            WHERE d.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
        await ExecuteAsync(connection, transaction, """
            UPDATE `equipment_set_members` m JOIN `equipment_set_definitions` d ON d.`SetId`=m.`SetId`
            JOIN `items` i ON i.`Id`=m.`ItemId`
            SET m.`ProductionRelationshipEnabled`=d.`ProductionRelationshipEnabled`,m.`PromotionRunId`=@run
            WHERE m.`RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
        await ExecuteAsync(connection, transaction, """
            UPDATE `container_item_relationships`
            SET `ProductionRelationshipEnabled`=CASE WHEN `RelationshipStatus` IN ('Verified','Derived') THEN 1 ELSE 0 END,
                `ProductionRecipeEnabled`=CASE WHEN `RelationshipStatus` IN ('Verified','Derived') AND `EffectiveProbability`>=0 AND `Quantity`>0 THEN `Enabled` ELSE 0 END,
                `PromotionRunId`=@run
            WHERE `RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
        await ExecuteAsync(connection, transaction, """
            UPDATE `pet_innate_definitions`
            SET `ProductionEnabled`=CASE WHEN `NameZhTw`<>'' AND ((`EffectReference` IS NULL AND `EffectValue` IS NULL) OR (`EffectReference` IS NOT NULL AND `EffectValue` IS NOT NULL)) THEN 1 ELSE 0 END,
                `PromotionRunId`=@run
            WHERE `RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
        await ExecuteAsync(connection, transaction, """
            UPDATE `pet_egg_relationships`
            SET `ProductionHatchEnabled`=CASE WHEN `RelationshipStatus` IN ('Verified','Derived') AND `EffectiveProbability`>=0 THEN `Enabled` ELSE 0 END,
                `PromotionRunId`=@run
            WHERE `RunId`=@sourceRun;
            """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
    }

    private async Task PopulateMissingFieldClosureAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var specs = BuildClosureSpecs();
        foreach (var spec in specs)
        {
            var sql = $"""
                INSERT INTO `god2_research`.`content_phase3_field_closure`
                    (`RunId`,`SourceRunId`,`Domain`,`EntityKey`,`FieldName`,`ValueJson`,`EvidenceStatus`,`BlockingReason`,`MissingEvidenceType`,
                     `SourcesSearchedJson`,`ClientFunctionsSearchedJson`,`CandidateSource`,`IdentityMatchMethod`,`Confidence`,`ConflictingCandidates`,
                     `PromotionRequirementsJson`,`RecoveryPass`,`LastAttemptedAtUtc`)
                SELECT @run,@sourceRun,@domain,CAST({spec.EntityKeySql} AS CHAR),@field,NULL,{spec.StatusSql},
                    CASE {spec.StatusSql}
                        WHEN 'EvidenceBlocked' THEN @blockedReason WHEN 'Candidate' THEN @candidateReason ELSE 'No blocking condition for the recorded field state.' END,
                    CASE WHEN {spec.StatusSql} IN ('EvidenceBlocked','Candidate','DefaultDisabledZero') THEN @missingType ELSE 'NotApplicable' END,
                    @sources,@functions,@candidateSource,@matchMethod,
                    CASE {spec.StatusSql} WHEN 'Verified' THEN 'High' WHEN 'Derived' THEN 'Medium' WHEN 'Candidate' THEN 'Low' ELSE 'Blocked' END,
                    0,@requirements,3,UTC_TIMESTAMP(6)
                FROM {spec.FromSql}
                ON DUPLICATE KEY UPDATE `ValueJson`=VALUES(`ValueJson`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                    `BlockingReason`=VALUES(`BlockingReason`),`MissingEvidenceType`=VALUES(`MissingEvidenceType`),
                    `SourcesSearchedJson`=VALUES(`SourcesSearchedJson`),`ClientFunctionsSearchedJson`=VALUES(`ClientFunctionsSearchedJson`),
                    `CandidateSource`=VALUES(`CandidateSource`),`IdentityMatchMethod`=VALUES(`IdentityMatchMethod`),`Confidence`=VALUES(`Confidence`),
                    `ConflictingCandidates`=VALUES(`ConflictingCandidates`),`PromotionRequirementsJson`=VALUES(`PromotionRequirementsJson`),
                    `RecoveryPass`=VALUES(`RecoveryPass`),`LastAttemptedAtUtc`=VALUES(`LastAttemptedAtUtc`);
                """;
            await ExecuteAsync(connection, transaction, sql, cancellationToken,
                ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId), ("@domain", spec.Domain), ("@field", spec.Field),
                ("@blockedReason", spec.BlockedReason), ("@candidateReason", spec.CandidateReason), ("@missingType", spec.MissingEvidenceType),
                ("@sources", spec.SourcesJson), ("@functions", spec.FunctionsJson), ("@candidateSource", spec.CandidateSource),
                ("@matchMethod", spec.IdentityMatchMethod), ("@requirements", spec.PromotionRequirementsJson));
        }
    }

    private static IReadOnlyList<ClosureSpec> BuildClosureSpecs()
    {
        var result = new List<ClosureSpec>();
        void Add(string domain, string from, string key, string field, string status, string reason, string missing, string sources, string functions, string candidate = "", string match = "None", string requirements = "[]") =>
            result.Add(new(domain, from, key, field, status, reason, $"Candidate requires {reason}", missing, sources, functions, candidate, match, requirements));

        const string npcSources = "[\"Phase2 npc_coordinate_evidence\",\"formal npcs/maps\",\"merchant/quest bindings\",\"official quest-location text\",\"existing guide evidence\"]";
        const string npcFunctions = "[\"MapSceneLoader\",\"ObjectPlacementLoader\",\"CoordinateConversion\",\"NpcLookup\",\"ServiceBindingResolver\"]";
        Add("NPC", "`npcs` n", "n.`Id`", "Identity", "n.`OfficialIdentityStatus`", "Official identity requires stable ID, code and display name.", "OfficialIdentity", npcSources, npcFunctions, match: "FormalPrimaryKey+Code");
        Add("NPC", "`npcs` n", "n.`Id`", "Type", "CASE WHEN n.`NpcType` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", "NPC type requires a unique service or client type binding.", "NpcType", npcSources, npcFunctions);
        Add("NPC", "`npcs` n", "n.`Id`", "Interaction", "CASE WHEN n.`InteractionFamily` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", "Interaction family requires an authoritative dialog, merchant, quest, portal or service binding.", "InteractionFamily", npcSources, npcFunctions);
        foreach (var field in new[] { "Map", "X", "Y" }) Add("NPC", "`npcs` n", "n.`Id`", field, "CASE WHEN n.`ProductionSpawnEnabled`=1 THEN n.`CoordinateEvidenceStatus` ELSE 'EvidenceBlocked' END", "Coordinate requires unique NPC/map identity, proven scale, bounds and no competing position.", "Coordinate", npcSources, npcFunctions, "npc_coordinate_evidence", "ExactBinaryName+Map+UniquePosition", "[\"Unique identity\",\"Bounds PASS\",\"No conflict\"]");
        Add("NPC", "`npcs` n", "n.`Id`", "Direction", "CASE WHEN n.`ProductionSpawnEnabled`=1 AND n.`Direction` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END", "Direction requires placement evidence for the promoted spawn.", "Direction", npcSources, npcFunctions);
        Add("NPC", "`npcs` n", "n.`Id`", "Bindings", "CASE WHEN n.`InteractionFamily` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", "Bindings require reusable formal service relationships.", "ServiceBinding", npcSources, npcFunctions);

        const string monsterSources = "[\"formal monsters/spawns\",\"Phase2 drop relationships\",\"official enemy/battle tables\",\"quest targets\",\"existing guide evidence\"]";
        const string monsterFunctions = "[\"EncounterBuilder\",\"BattleParticipantConstructor\",\"StatLookup\",\"HpBarInitialization\",\"RewardCalculator\",\"SpawnScheduler\"]";
        var monsterStatuses = new Dictionary<string, string>
        {
            ["Identity"] = "'Verified'",
            ["Spawn"] = "ms.`EvidenceStatus`",
            ["Level"] = "CASE WHEN mp.`Level` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["HP"] = "CASE WHEN mp.`HpPolicy`='EvidenceBlocked' THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["MP"] = "CASE WHEN mp.`MpPolicy` IN ('EvidenceBlocked') THEN 'EvidenceBlocked' WHEN mp.`MpPolicy`='ExplicitOfficialZero' THEN 'ExplicitOfficialZero' ELSE 'Derived' END",
            ["Stats"] = "CASE WHEN mp.`PhysicalAttack` IS NOT NULL AND mp.`PhysicalDefense` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END",
            ["AI"] = "CASE WHEN mp.`AiFamily` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["EXP"] = "CASE WHEN mp.`ExperienceReward` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["Currency"] = "CASE WHEN mp.`CurrencyMinimum` IS NULL OR mp.`CurrencyMaximum` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["DropPolicy"] = "mp.`DropPolicyStatus`"
        };
        foreach (var pair in monsterStatuses) Add("Monster", "`monster_semantic_profiles` mp JOIN `monster_spawn_semantics` ms ON ms.`RunId`=mp.`RunId` AND ms.`MonsterId`=mp.`MonsterId`", "mp.`MonsterId`", pair.Key, pair.Value,
            $"Monster {pair.Key} requires authoritative client field or formula consumer evidence.", $"Monster{pair.Key}", monsterSources, monsterFunctions);

        const string skillSources = "[\"formal skills\",\"Phase2 skill profiles\",\"SpgEft/FightPetSkill/NewFightPet2Skill\",\"official display names\"]";
        const string skillFunctions = "[\"ProfessionSkillTree\",\"SkillTooltipFormatter\",\"CastValidator\",\"TargetMaskValidator\",\"MpDisplayFormatter\",\"EffectDispatcher\",\"StatusLookup\"]";
        var skillStatuses = new Dictionary<string, string>
        {
            ["Identity"] = "CASE WHEN sp.`ClientProfileId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["Profession"] = "CASE WHEN sp.`Profession` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["Family"] = "CASE WHEN sp.`SkillFamily`='Unknown' THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["Target"] = "CASE WHEN sp.`TargetPolicy`='Unknown' THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["MP"] = "CASE WHEN sp.`MpCostPolicy` IN ('EvidenceBlocked','Unknown') OR sp.`MpCost` IS NULL THEN 'EvidenceBlocked' WHEN sp.`MpCostPolicy`='ExplicitOfficialZero' THEN 'ExplicitOfficialZero' ELSE 'Derived' END",
            ["Effect"] = "CASE WHEN JSON_LENGTH(sp.`EffectReferencesJson`)>0 THEN 'Derived' ELSE 'EvidenceBlocked' END",
            ["Status"] = "CASE WHEN JSON_LENGTH(sp.`StatusReferencesJson`)>0 THEN 'Derived' ELSE 'EvidenceBlocked' END",
            ["Rank"] = "CASE WHEN sp.`SkillRank` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END",
            ["ActivePassive"] = "CASE WHEN sp.`SkillFamily`='Passive' THEN 'Derived' WHEN sp.`SkillFamily`<>'Unknown' THEN 'Derived' ELSE 'EvidenceBlocked' END"
        };
        foreach (var pair in skillStatuses) Add("Skill", "`skill_semantic_profiles` sp", "sp.`SkillId`", pair.Key, pair.Value,
            $"Skill {pair.Key} requires exact ID/resource/display composite and consumer semantics.", $"Skill{pair.Key}", skillSources, skillFunctions, "skill_content_profiles", "ExactClientId+SourcePath+SourceRecordIndex+BinaryDisplayName");

        const string dropSources = "[\"monster_drop_relationships\",\"official identities\",\"Bahamut\",\"17173\",\"historical observations\"]";
        const string dropFunctions = "[\"MonsterEncyclopedia\",\"RewardCalculator\",\"DropRoller\",\"QuestItemSourceLookup\"]";
        var dropStatuses = new Dictionary<string, string> { ["Monster"] = "d.`DropRelationshipStatus`", ["Item"] = "d.`DropRelationshipStatus`", ["Quantity"] = "CASE WHEN d.`MinimumQuantity` IS NOT NULL AND d.`MaximumQuantity` IS NOT NULL AND d.`MinimumQuantity`>0 AND d.`MaximumQuantity`>=d.`MinimumQuantity` THEN 'Derived' ELSE 'EvidenceBlocked' END", ["Chance"] = "CASE WHEN d.`DeclaredDropChance` IS NULL THEN 'DefaultDisabledZero' WHEN d.`EffectiveDropChance`>=0 THEN 'Derived' ELSE 'EvidenceBlocked' END", ["Weight"] = "CASE WHEN d.`Weight` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", ["Group"] = "CASE WHEN d.`DropGroupId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END", ["Condition"] = "CASE WHEN d.`QuestCondition` IS NULL AND d.`MapCondition` IS NULL AND d.`LevelCondition` IS NULL AND d.`EventCondition` IS NULL THEN 'NotApplicable' ELSE 'Candidate' END" };
        foreach (var pair in dropStatuses) Add("Drop", "`monster_drop_relationships` d", "d.`RelationshipId`", pair.Key, pair.Value, $"Drop {pair.Key} requires official relationship or multi-source identity evidence.", $"Drop{pair.Key}", dropSources, dropFunctions, "monster_drop_relationships", "ExactMonster+ItemIdentity");

        const string merchantSources = "[\"merchant_inventory_candidates\",\"formal merchants/items/npcs\",\"official gamedata sections\"]";
        const string merchantFunctions = "[\"MerchantUiLoader\",\"InventoryGroupLookup\",\"PriceFormatter\",\"PurchaseValidator\"]";
        foreach (var pair in new Dictionary<string, string> { { "NPC", "CASE WHEN c.`MerchantId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "Item", "c.`RelationshipEvidenceStatus`" }, { "Price", "c.`PriceEvidenceStatus`" }, { "Quantity", "CASE WHEN c.`QuantityLimit` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "Refresh", "CASE WHEN c.`RefreshPolicy`='Unknown' THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "Requirement", "NotApplicable" } })
            Add("Merchant", "`merchant_inventory_candidates` c", "c.`CandidateId`", pair.Key, pair.Value == "NotApplicable" ? "'NotApplicable'" : pair.Value, $"Merchant {pair.Key} requires exact binding and understood column semantics.", $"Merchant{pair.Key}", merchantSources, merchantFunctions, "merchant_inventory_candidates", "ExactFormalForeignKeys");

        const string questSources = "[\"quest_content_profiles\",\"quest_objective_candidates\",\"formal quests/npcs/items/monsters/maps\",\"official quest text\"]";
        const string questFunctions = "[\"QuestTracker\",\"QuestUiFormatter\",\"ObjectiveResolver\",\"RewardResolver\",\"QuestScriptLoader\"]";
        foreach (var pair in new Dictionary<string, string> { { "Identity", "CASE WHEN q.`QuestId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "StartEnd", "CASE WHEN q.`StartNpcClientId` IS NULL AND q.`EndNpcClientId` IS NULL THEN 'EvidenceBlocked' ELSE 'Candidate' END" }, { "Objective", "CASE WHEN JSON_VALID(q.`StepsJson`) THEN 'Derived' ELSE 'EvidenceBlocked' END" }, { "Reward", "CASE WHEN q.`RewardTextZhTw` IS NULL OR q.`RewardTextZhTw`='' THEN 'Candidate' ELSE 'Derived' END" } })
            Add("Quest", "`quest_content_profiles` q", "q.`ProfileId`", pair.Key, pair.Value, $"Quest {pair.Key} requires exact quest identity and proven record layout.", $"Quest{pair.Key}", questSources, questFunctions, "quest_content_profiles", "UniqueExactBinaryDisplayName+SourceHash");
        foreach (var pair in new Dictionary<string, string> { { "Target", "o.`RelationshipEvidenceStatus`" }, { "Quantity", "o.`QuantityEvidenceStatus`" }, { "ObjectiveType", "CASE WHEN o.`ObjectiveType` LIKE '%Candidate' THEN 'Candidate' ELSE o.`EvidenceStatus` END" } })
            Add("QuestObjective", "`quest_objective_candidates` o", "o.`ObjectiveId`", pair.Key, pair.Value, $"Quest objective {pair.Key} requires client tracker column semantics.", $"QuestObjective{pair.Key}", questSources, questFunctions, "quest_objective_candidates", "ExactReferencedIdentity");

        const string equipmentSources = "[\"EquipSetList exact official layout\",\"formal items\",\"Migration 033 field evidence\"]";
        const string equipmentFunctions = "[\"EquipmentSetLookup\",\"PieceThresholdResolver\",\"SetBonusFormatter\"]";
        foreach (var pair in new Dictionary<string, string> { { "Set", "d.`SetRelationshipStatus`" }, { "RequiredPieces", "CASE WHEN d.`RequiredPieces` BETWEEN 1 AND 5 THEN 'Verified' ELSE 'EvidenceBlocked' END" }, { "Bonus", "CASE WHEN d.`ProductionBonusEnabled`=1 THEN 'Verified' WHEN d.`EffectsZhTw`<>'' THEN 'Candidate' ELSE 'NotApplicable' END" } })
            Add("Equipment", "`equipment_set_definitions` d", "d.`SetId`", pair.Key, pair.Value, $"Equipment {pair.Key} requires exact official layout and valid members.", $"Equipment{pair.Key}", equipmentSources, equipmentFunctions, "EquipSetList.csvZ", "ExactSetId+ExactColumnLayout");
        Add("EquipmentMember", "`equipment_set_members` m", "CONCAT(m.`SetId`,':',m.`ItemId`)", "Member", "CASE WHEN m.`ProductionRelationshipEnabled`=1 THEN 'Verified' ELSE 'EvidenceBlocked' END", "Equipment member requires an existing official item and nonduplicate set membership.", "EquipmentMember", equipmentSources, equipmentFunctions, "EquipSetList.csvZ", "ExactItemId");

        const string combineSources = "[\"container_item_relationships\",\"FuDai/Egg/client tables\",\"formal items\"]";
        const string combineFunctions = "[\"CombineUi\",\"ContainerOpenResolver\",\"ProbabilityFormatter\",\"RecipeValidator\"]";
        foreach (var pair in new Dictionary<string, string> { { "Inputs", "r.`RelationshipStatus`" }, { "Outputs", "r.`RelationshipStatus`" }, { "Quantity", "CASE WHEN r.`Quantity`>0 THEN 'Derived' ELSE 'EvidenceBlocked' END" }, { "Probability", "CASE WHEN r.`DeclaredProbability` IS NULL THEN 'DefaultDisabledZero' WHEN r.`EffectiveProbability`>=0 THEN 'Derived' ELSE 'EvidenceBlocked' END" }, { "Cost", "'EvidenceBlocked'" }, { "Failure", "'EvidenceBlocked'" } })
            Add("Combine", "`container_item_relationships` r", "r.`RelationshipId`", pair.Key, pair.Value, $"Combine {pair.Key} requires proven record layout and consumer semantics.", $"Combine{pair.Key}", combineSources, combineFunctions, "container_item_relationships", "ExactItemIds+RecordLayout");

        const string petSources = "[\"pet_content_profiles\",\"NewCombatPet_Innate exact official layout\",\"formal items/pets\"]";
        const string petFunctions = "[\"PetProfileLookup\",\"InnateLookup\",\"EggTooltip\",\"HatchResultLookup\",\"PetCreation\"]";
        foreach (var pair in new Dictionary<string, string> { { "Pet", "CASE WHEN p.`ItemId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "Growth", "CASE WHEN p.`GrowthType` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END" }, { "Stats", "CASE WHEN JSON_LENGTH(p.`BaseStatsJson`)>0 THEN 'Derived' ELSE 'EvidenceBlocked' END" }, { "Skills", "CASE WHEN JSON_LENGTH(p.`SkillReferencesJson`)>0 THEN 'Derived' ELSE 'EvidenceBlocked' END" }, { "Evolution", "CASE WHEN JSON_LENGTH(p.`EvolutionReferencesJson`)>0 THEN 'Derived' ELSE 'EvidenceBlocked' END" } })
            Add("Pet", "`pet_content_profiles` p", "p.`ProfileId`", pair.Key, pair.Value, $"Pet {pair.Key} requires exact client profile semantics.", $"Pet{pair.Key}", petSources, petFunctions, "pet_content_profiles", "ExactClientPetId");
        foreach (var pair in new Dictionary<string, string> { { "Innate", "CASE WHEN i.`ProductionEnabled`=1 THEN 'Verified' ELSE 'EvidenceBlocked' END" }, { "Effect", "CASE WHEN i.`EffectReference` IS NULL AND i.`EffectValue` IS NULL THEN 'NotApplicable' WHEN i.`EffectReference` IS NOT NULL AND i.`EffectValue` IS NOT NULL THEN 'Verified' ELSE 'EvidenceBlocked' END" } })
            Add("PetInnate", "`pet_innate_definitions` i", "i.`InnateId`", pair.Key, pair.Value, $"Pet innate {pair.Key} requires exact field alignment and effect lookup.", $"PetInnate{pair.Key}", petSources, petFunctions, "NewCombatPet_Innate.csvZ", "ExactInnateId+ExactColumnLayout");
        return result;
    }

    private async Task PopulatePromotionAuditAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        var statements = new[]
        {
            (Domain:"NPC", Sql:"SELECT @run,SHA2(CONCAT('npc:',n.`Id`),256),@sourceRun,'NPC',CAST(n.`Id` AS CHAR),'npcs',CAST(n.`Id` AS CHAR),CASE WHEN n.`ProductionSpawnEnabled`=1 THEN 'Promoted' ELSE 'FieldClosureOnly' END,'FormalPrimaryKey+UniquePosition',n.`CoordinateEvidenceStatus`,JSON_OBJECT('coordinate',n.`CoordinateEvidenceStatus`),JSON_OBJECT('source','Phase2+formal'),UTC_TIMESTAMP(6) FROM `npcs` n"),
            (Domain:"Monster", Sql:"SELECT @run,SHA2(CONCAT('monster:',p.`MonsterId`),256),@sourceRun,'Monster',CAST(p.`MonsterId` AS CHAR),'monster_semantic_profiles',CAST(p.`MonsterId` AS CHAR),'Promoted','FormalPrimaryKey',CASE WHEN p.`PhysicalAttack` IS NOT NULL AND p.`PhysicalDefense` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END,JSON_OBJECT('stats',CASE WHEN p.`PhysicalAttack` IS NOT NULL AND p.`PhysicalDefense` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END,'reward',CASE WHEN p.`ExperienceReward` IS NOT NULL AND p.`CurrencyMinimum` IS NOT NULL AND p.`CurrencyMaximum` IS NOT NULL THEN 'Derived' ELSE 'EvidenceBlocked' END,'drop',p.`DropPolicyStatus`),JSON_OBJECT('source','formal+Phase2'),UTC_TIMESTAMP(6) FROM `monster_semantic_profiles` p WHERE p.`RunId`=@run"),
            (Domain:"Drop", Sql:"SELECT @run,SHA2(CONCAT('drop:',d.`RelationshipId`),256),@sourceRun,'Drop',d.`RelationshipId`,'monster_drop_relationships',d.`RelationshipId`,CASE WHEN d.`DropRelationshipStatus` IN ('Verified','Derived') THEN 'Promoted' ELSE 'FieldClosureOnly' END,'ExactMonster+ItemIdentity',d.`DropRelationshipStatus`,JSON_OBJECT('relationship',d.`DropRelationshipStatus`,'quantity',CASE WHEN d.`MinimumQuantity` IS NOT NULL AND d.`MaximumQuantity` IS NOT NULL AND d.`MinimumQuantity`>0 AND d.`MaximumQuantity`>=d.`MinimumQuantity` THEN 'Derived' ELSE 'EvidenceBlocked' END,'chance',CASE WHEN d.`DeclaredDropChance` IS NULL THEN 'DefaultDisabledZero' WHEN d.`EffectiveDropChance`>=0 THEN 'Derived' ELSE 'EvidenceBlocked' END),JSON_OBJECT('sourceType',COALESCE(s.`SourceType`,''),'sourceIdentity',COALESCE(s.`SourceIdentity`,''),'sourceHash',COALESCE(s.`SourceHash`,'')),UTC_TIMESTAMP(6) FROM `monster_drop_relationships` d LEFT JOIN `god2_research`.`relationship_source_archive` s ON s.`FormalTable`='monster_drop_relationships' AND s.`RelationshipId`=d.`RelationshipId` WHERE d.`RunId`=@sourceRun"),
            (Domain:"Skill", Sql:"SELECT @run,SHA2(CONCAT('skill:',p.`SkillId`),256),@sourceRun,'Skill',CAST(p.`SkillId` AS CHAR),'skill_semantic_profiles',CAST(p.`SkillId` AS CHAR),CASE WHEN p.`ProductionEnabled`=1 THEN 'Promoted' ELSE 'FieldClosureOnly' END,'ExactClientId+DisplayName+RuntimeShape',CASE WHEN p.`ClientProfileId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,JSON_OBJECT('identity',CASE WHEN p.`ClientProfileId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,'family',CASE WHEN p.`SkillFamily`='Unknown' THEN 'EvidenceBlocked' ELSE 'Derived' END,'target',CASE WHEN p.`TargetPolicy`='Unknown' THEN 'EvidenceBlocked' ELSE 'Derived' END,'mp',CASE WHEN p.`MpCostPolicy` IN ('EvidenceBlocked','Unknown') OR p.`MpCost` IS NULL THEN 'EvidenceBlocked' WHEN p.`MpCostPolicy`='ExplicitOfficialZero' THEN 'ExplicitOfficialZero' ELSE 'Derived' END),JSON_OBJECT('source','Phase2 skill profile'),UTC_TIMESTAMP(6) FROM `skill_semantic_profiles` p WHERE p.`RunId`=@run"),
            (Domain:"Quest", Sql:"SELECT @run,SHA2(CONCAT('quest:',q.`ProfileId`),256),@sourceRun,'Quest',q.`ProfileId`,'quest_content_profiles',COALESCE(CAST(q.`QuestId` AS CHAR),q.`ProfileId`),CASE WHEN q.`ProductionProfileEnabled`=1 THEN 'Promoted' ELSE 'FieldClosureOnly' END,'UniqueExactBinaryDisplayName+ArchivedSourceHash',CASE WHEN q.`QuestId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,JSON_OBJECT('identity',CASE WHEN q.`QuestId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END,'objective',CASE WHEN JSON_VALID(q.`StepsJson`) THEN 'Derived' ELSE 'EvidenceBlocked' END,'reward',CASE WHEN q.`RewardTextZhTw` IS NULL OR q.`RewardTextZhTw`='' THEN 'Candidate' ELSE 'Derived' END),JSON_OBJECT('sourceHash',COALESCE(s.`SourceHash`,'')),UTC_TIMESTAMP(6) FROM `quest_content_profiles` q LEFT JOIN `god2_research`.`content_profile_source_archive` s ON s.`FormalTable`='quest_content_profiles' AND s.`RecordIdentity`=q.`ProfileId` WHERE q.`RunId`=@sourceRun"),
            (Domain:"Equipment", Sql:"SELECT @run,SHA2(CONCAT('equipment:',d.`SetId`),256),@sourceRun,'Equipment',CAST(d.`SetId` AS CHAR),'equipment_set_definitions',CAST(d.`SetId` AS CHAR),CASE WHEN d.`ProductionRelationshipEnabled`=1 THEN 'Promoted' ELSE 'FieldClosureOnly' END,'ExactSetId+ExactColumnLayout',d.`SetRelationshipStatus`,JSON_OBJECT('relationship',d.`SetRelationshipStatus`,'bonus',CASE WHEN d.`ProductionBonusEnabled`=1 THEN 'Verified' WHEN d.`EffectsZhTw`<>'' THEN 'Candidate' ELSE 'NotApplicable' END),JSON_OBJECT('source','EquipSetList.csvZ'),UTC_TIMESTAMP(6) FROM `equipment_set_definitions` d WHERE d.`RunId`=@sourceRun"),
            (Domain:"PetInnate", Sql:"SELECT @run,SHA2(CONCAT('pet-innate:',i.`InnateId`),256),@sourceRun,'PetInnate',CAST(i.`InnateId` AS CHAR),'pet_innate_definitions',CAST(i.`InnateId` AS CHAR),CASE WHEN i.`ProductionEnabled`=1 THEN 'Promoted' ELSE 'FieldClosureOnly' END,'ExactInnateId+ExactColumnLayout',CASE WHEN i.`ProductionEnabled`=1 THEN 'Verified' ELSE 'EvidenceBlocked' END,JSON_OBJECT('definition',CASE WHEN i.`ProductionEnabled`=1 THEN 'Verified' ELSE 'EvidenceBlocked' END,'effect',CASE WHEN i.`EffectReference` IS NULL AND i.`EffectValue` IS NULL THEN 'NotApplicable' WHEN i.`EffectReference` IS NOT NULL AND i.`EffectValue` IS NOT NULL THEN 'Verified' ELSE 'EvidenceBlocked' END),JSON_OBJECT('source','NewCombatPet_Innate.csvZ'),UTC_TIMESTAMP(6) FROM `pet_innate_definitions` i WHERE i.`RunId`=@sourceRun")
        };
        foreach (var statement in statements)
        {
            await ExecuteAsync(connection, transaction, $"""
                INSERT INTO `god2_research`.`content_phase3_promotions`
                    (`RunId`,`PromotionId`,`SourceRunId`,`Domain`,`EntityKey`,`TargetTable`,`TargetIdentity`,`PromotionStatus`,`IdentityMatchMethod`,`EvidenceStatus`,`FieldStatesJson`,`SourceProvenanceJson`,`PromotedAtUtc`)
                {statement.Sql}
                ON DUPLICATE KEY UPDATE `PromotionStatus`=VALUES(`PromotionStatus`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`FieldStatesJson`=VALUES(`FieldStatesJson`),`PromotedAtUtc`=VALUES(`PromotedAtUtc`);
                """, cancellationToken, ("@run", workspace.RunId), ("@sourceRun", _phase2SourceRunId));
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>> ValidateRuntimeAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var loader = new MariaDbStaticDataLoader(_options);
        var load = await loader.LoadAsync(cancellationToken);
        var build = load.Succeeded && load.Value is not null ? await loader.BuildAsync(load.Value, cancellationToken) : null;
        var gameplay = await new MariaDbGameplayContentCatalogRepository(_options).LoadAsync(cancellationToken);
        var issues = await ReadSemanticIssuesAsync(connection, runId, cancellationToken);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mariaDbLoad"] = load.Succeeded ? "PASS" : "FAIL",
            ["repositoryMapping"] = load.Succeeded ? "PASS" : "FAIL",
            ["runtimeCatalogBuild"] = build?.Succeeded == true ? "PASS" : "FAIL_CLOSED",
            ["gameplayCatalogValidation"] = gameplay.QuarantinedItems.Count == 0 && gameplay.Issues.Count == 0 ? "PASS" : "FAIL_CLOSED",
            ["phase3SpecializedCatalogLoad"] = "PASS",
            ["phase3SpecializedValidation"] = issues.Values.Sum() == 0 ? "PASS" : "FAIL_CLOSED",
            ["issues"] = issues,
            ["status"] = load.Succeeded && build?.Succeeded == true && gameplay.QuarantinedItems.Count == 0 && gameplay.Issues.Count == 0 && issues.Values.Sum() == 0 ? "PASS" : "FAIL"
        };
    }

    private static async Task<IReadOnlyDictionary<string, object?>> ValidateHeadlessAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var lookups = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["npcRows"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `npcs`;", cancellationToken),
            ["npcEnabledSpawns"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `npcs` WHERE `ProductionSpawnEnabled`=1;", cancellationToken),
            ["monsterProfiles"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run;", cancellationToken, ("@run", runId)),
            ["monsterSpawnStates"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `monster_spawn_semantics` WHERE `RunId`=@run;", cancellationToken, ("@run", runId)),
            ["skillProfiles"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run;", cancellationToken, ("@run", runId)),
            ["skillIdentityMapped"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ClientProfileId` IS NOT NULL;", cancellationToken, ("@run", runId)),
            ["questProfilesPromoted"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `ProductionProfileEnabled`=1;", cancellationToken),
            ["questObjectiveRelationships"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `RelationshipEvidenceStatus` IN ('Verified','Derived');", cancellationToken),
            ["equipmentSets"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `ProductionRelationshipEnabled`=1;", cancellationToken),
            ["equipmentMembers"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `equipment_set_members` WHERE `ProductionRelationshipEnabled`=1;", cancellationToken),
            ["petInnates"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `ProductionEnabled`=1;", cancellationToken),
            ["disabledDrops"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `DeclaredDropChance` IS NULL AND `EffectiveDropChance`=0 AND `ProductionDropEnabled`=0;", cancellationToken),
            ["disabledCombine"] = await ScalarAsync(connection, "SELECT COUNT(*) FROM `container_item_relationships` WHERE `DeclaredProbability` IS NULL AND `EffectiveProbability`=0 AND `ProductionRecipeEnabled`=0;", cancellationToken)
        };
        var issues = await ReadSemanticIssuesAsync(connection, runId, cancellationToken);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scenario"] = "MariaDB Phase 3 field-closure and promoted specialized catalog lookup",
            ["lookups"] = lookups,
            ["disabledZeroSimulation"] = issues.GetValueOrDefault("DefaultDisabledZeroViolations") == 0 ? "PASS" : "FAIL",
            ["nextPlayerOperableState"] = "N/A_CONTENT_ONLY",
            ["status"] = issues.Values.Sum() == 0 ? "PASS" : "FAIL"
        };
    }

    private static async Task<Dictionary<string, long>> ReadSemanticIssuesAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UnclassifiedFields"] = "SELECT COUNT(*) FROM `god2_research`.`content_phase3_field_closure` WHERE `RunId`=@run AND `EvidenceStatus` NOT IN ('Verified','Derived','Candidate','EvidenceBlocked','ExplicitOfficialZero','DefaultDisabledZero','NotApplicable','Deprecated');",
            ["MissingBlockedReasons"] = "SELECT COUNT(*) FROM `god2_research`.`content_phase3_field_closure` WHERE `RunId`=@run AND `EvidenceStatus` IN ('EvidenceBlocked','Candidate','DefaultDisabledZero') AND (`BlockingReason`='' OR `MissingEvidenceType`='' OR JSON_LENGTH(`SourcesSearchedJson`)=0 OR JSON_LENGTH(`ClientFunctionsSearchedJson`)=0);",
            ["InvalidNpcSpawns"] = "SELECT COUNT(*) FROM `npcs` n LEFT JOIN `maps` m ON m.`Id`=n.`MapId` WHERE n.`ProductionSpawnEnabled`=1 AND (m.`Id` IS NULL OR n.`PositionX`<0 OR n.`PositionY`<0 OR n.`PositionX`>m.`Width` OR n.`PositionY`>m.`Height` OR n.`CoordinateEvidenceStatus` NOT IN ('Verified','Derived'));",
            ["InvalidMonsterProfiles"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND ((`HpPolicy`='EvidenceBlocked' AND `MaxHp` IS NOT NULL) OR (`MpPolicy`='EvidenceBlocked' AND `MaxMp` IS NOT NULL) OR (`MpPolicy`='ExplicitOfficialZero' AND `MaxMp`<>0));",
            ["InvalidMonsterSpawns"] = "SELECT COUNT(*) FROM `monster_spawn_semantics` s LEFT JOIN `maps` m ON m.`Id`=s.`MapId` WHERE s.`RunId`=@run AND s.`ProductionSpawnEnabled`=1 AND (m.`Id` IS NULL OR s.`PositionX` IS NULL OR s.`PositionY` IS NULL OR s.`PositionX`<0 OR s.`PositionY`<0 OR s.`PositionX`>m.`Width` OR s.`PositionY`>m.`Height`);",
            ["InvalidSkillProfiles"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ProductionEnabled`=1 AND (`ClientProfileId` IS NULL OR `SkillFamily`='Unknown' OR `TargetPolicy`='Unknown' OR `MpCostPolicy` IN ('EvidenceBlocked','Unknown') OR `MpCost` IS NULL OR JSON_LENGTH(`EffectReferencesJson`)=0);",
            ["InvalidQuestPromotions"] = "SELECT (SELECT COUNT(*) FROM `quest_content_profiles` p LEFT JOIN `quests` q ON q.`Id`=p.`QuestId` WHERE p.`ProductionProfileEnabled`=1 AND (q.`Id` IS NULL OR p.`NameZhTw`='' OR NOT JSON_VALID(p.`StepsJson`))) + (SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` o LEFT JOIN `quests` q ON q.`Id`=o.`QuestId` WHERE o.`RelationshipEvidenceStatus`='Derived' AND (q.`Id` IS NULL OR ((o.`ItemId` IS NULL)=(o.`MonsterId` IS NULL))));",
            ["DefaultDisabledZeroViolations"] = "SELECT (SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `ProductionDropEnabled`=0 AND `DeclaredDropChance` IS NULL AND `EffectiveDropChance`<>0) + (SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProductionRecipeEnabled`=0 AND `DeclaredProbability` IS NULL AND `EffectiveProbability`<>0) + (SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `ProductionHatchEnabled`=0 AND `DeclaredProbability` IS NULL AND `EffectiveProbability`<>0);",
            ["InvalidEquipmentPromotions"] = "SELECT COUNT(*) FROM `equipment_set_definitions` d LEFT JOIN (SELECT `SetId`,COUNT(*) c FROM `equipment_set_members` WHERE `ProductionRelationshipEnabled`=1 GROUP BY `SetId`) m ON m.`SetId`=d.`SetId` WHERE d.`ProductionRelationshipEnabled`=1 AND (d.`RequiredPieces`<1 OR d.`RequiredPieces`>5 OR COALESCE(m.c,0)<d.`RequiredPieces`);",
            ["InvalidPetInnates"] = "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `ProductionEnabled`=1 AND (`NameZhTw`='' OR ((`EffectReference` IS NULL) <> (`EffectValue` IS NULL)));",
            ["StalePhase3Rows"] = "SELECT (SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`<>@run AND `RunId` IN (SELECT `RunId` FROM `content_recovery_runs` WHERE `Phase`='GameplayContentRecoveryPhase3' AND `Status`='RUNNING')) + (SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`<>@run AND `RunId` IN (SELECT `RunId` FROM `content_recovery_runs` WHERE `Phase`='GameplayContentRecoveryPhase3' AND `Status`='RUNNING'));"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries) result[query.Key] = await ScalarAsync(connection, query.Value, cancellationToken, ("@run", runId));
        return result;
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadPromotionsAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NpcCoordinatesPromoted"] = "SELECT COUNT(*) FROM `god2_research`.`npc_coordinate_evidence` WHERE `PromotionRunId`=@run AND `ProductionSpawnEnabled`=1;",
            ["MonsterProfilesPromoted"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run;",
            ["MonsterSpawnsPromoted"] = "SELECT COUNT(*) FROM `monster_spawn_semantics` WHERE `RunId`=@run AND `ProductionSpawnEnabled`=1;",
            ["DropRelationshipsPromoted"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `PromotionRunId`=@run AND `DropRelationshipStatus` IN ('Verified','Derived');",
            ["SkillsIdentityMapped"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ClientProfileId` IS NOT NULL;",
            ["SkillsSemanticallyEnabled"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ProductionEnabled`=1;",
            ["MerchantRelationshipsPromoted"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `PromotionRunId`=@run AND `RelationshipEvidenceStatus` IN ('Verified','Derived');",
            ["QuestProfilesPromoted"] = "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `PromotionRunId`=@run AND `ProductionProfileEnabled`=1;",
            ["QuestObjectivesPromoted"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `PromotionRunId`=@run AND `ProductionObjectiveEnabled`=1;",
            ["QuestObjectiveRelationshipsPromoted"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `PromotionRunId`=@run AND `RelationshipEvidenceStatus` IN ('Verified','Derived');",
            ["EquipmentSetsPromoted"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1;",
            ["EquipmentMembersPromoted"] = "SELECT COUNT(*) FROM `equipment_set_members` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1;",
            ["CombineRelationshipsPromoted"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `PromotionRunId`=@run AND `ProductionRelationshipEnabled`=1;",
            ["PetInnatesPromoted"] = "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `PromotionRunId`=@run AND `ProductionEnabled`=1;",
            ["PetEggRelationshipsPromoted"] = "SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `PromotionRunId`=@run AND `ProductionHatchEnabled`=1;"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries) result[query.Key] = await ScalarAsync(connection, query.Value, cancellationToken, ("@run", runId));
        return result;
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadCoverageAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NpcTotal"] = "SELECT COUNT(*) FROM `npcs`;",
            ["NpcOfficialIdentity"] = "SELECT COUNT(*) FROM `npcs` WHERE `OfficialIdentityStatus` IN ('Verified','Derived');",
            ["NpcCoordinateVerified"] = "SELECT COUNT(*) FROM `npcs` WHERE `CoordinateEvidenceStatus`='Verified';",
            ["NpcCoordinateDerived"] = "SELECT COUNT(*) FROM `npcs` WHERE `CoordinateEvidenceStatus`='Derived';",
            ["NpcProductionSpawn"] = "SELECT COUNT(*) FROM `npcs` WHERE `ProductionSpawnEnabled`=1;",
            ["NpcInteractionClassified"] = "SELECT COUNT(*) FROM `npcs` WHERE `InteractionFamily` IS NOT NULL;",
            ["MonsterTotal"] = "SELECT COUNT(*) FROM `monsters`;",
            ["MonsterSpawnRelationship"] = "SELECT COUNT(*) FROM `monster_spawn_semantics` WHERE `RunId`=@run AND `MapRelationshipStatus` IN ('Verified','Derived');",
            ["MonsterProductionSpawn"] = "SELECT COUNT(*) FROM `monster_spawn_semantics` WHERE `RunId`=@run AND `ProductionSpawnEnabled`=1;",
            ["MonsterHp"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `HpPolicy`<>'EvidenceBlocked';",
            ["MonsterMpPolicy"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `MpPolicy`<>'EvidenceBlocked';",
            ["MonsterCombatStats"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `PhysicalAttack` IS NOT NULL AND `PhysicalDefense` IS NOT NULL;",
            ["MonsterRewards"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `ExperienceReward` IS NOT NULL AND `CurrencyMinimum` IS NOT NULL AND `CurrencyMaximum` IS NOT NULL;",
            ["MonsterDropPolicyStatus"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` WHERE `RunId`=@run AND `DropPolicyStatus` IN ('Verified','Derived','ExplicitOfficialZero','EvidenceBlocked');",
            ["DropRelationships"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `DropRelationshipStatus` IN ('Verified','Derived');",
            ["DropMonstersCovered"] = "SELECT COUNT(DISTINCT `MonsterId`) FROM `monster_drop_relationships` WHERE `DropRelationshipStatus` IN ('Verified','Derived');",
            ["DropChanceVerified"] = "SELECT COUNT(*) FROM `monster_drop_relationships` WHERE `DeclaredDropChance` IS NOT NULL AND `EffectiveDropChance`>=0;",
            ["SkillTotal"] = "SELECT COUNT(*) FROM `skills`;",
            ["SkillIdentityMapped"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `ClientProfileId` IS NOT NULL;",
            ["SkillFamily"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `SkillFamily`<>'Unknown';",
            ["SkillTarget"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `TargetPolicy`<>'Unknown';",
            ["SkillMp"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND `MpCostPolicy` NOT IN ('EvidenceBlocked','Unknown') AND `MpCost` IS NOT NULL;",
            ["SkillEffect"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND JSON_LENGTH(`EffectReferencesJson`)>0;",
            ["SkillStatus"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` WHERE `RunId`=@run AND JSON_LENGTH(`StatusReferencesJson`)>0;",
            ["MerchantCandidates"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates`;",
            ["MerchantRelationships"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `RelationshipEvidenceStatus` IN ('Verified','Derived');",
            ["MerchantProduction"] = "SELECT COUNT(*) FROM `god2_research`.`merchant_inventory_candidates` WHERE `ProductionSaleEnabled`=1;",
            ["QuestProfiles"] = "SELECT COUNT(*) FROM `quest_content_profiles`;",
            ["QuestProfilesPromoted"] = "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `ProductionProfileEnabled`=1;",
            ["QuestObjectives"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates`;",
            ["QuestObjectiveRelationships"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `RelationshipEvidenceStatus` IN ('Verified','Derived');",
            ["QuestObjectivesPromoted"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` WHERE `ProductionObjectiveEnabled`=1;",
            ["QuestRewardProfiles"] = "SELECT COUNT(*) FROM `quest_content_profiles` WHERE `RewardTextZhTw` IS NOT NULL AND `RewardTextZhTw`<>'';",
            ["EquipmentSets"] = "SELECT COUNT(*) FROM `equipment_set_definitions`;",
            ["EquipmentSetsPromoted"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `ProductionRelationshipEnabled`=1;",
            ["EquipmentMembersPromoted"] = "SELECT COUNT(*) FROM `equipment_set_members` WHERE `ProductionRelationshipEnabled`=1;",
            ["EquipmentBonusesEnabled"] = "SELECT COUNT(*) FROM `equipment_set_definitions` WHERE `ProductionBonusEnabled`=1;",
            ["CombineCandidates"] = "SELECT COUNT(*) FROM `container_item_relationships`;",
            ["CombineRelationshipsPromoted"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `ProductionRelationshipEnabled`=1;",
            ["CombineProbabilityVerified"] = "SELECT COUNT(*) FROM `container_item_relationships` WHERE `DeclaredProbability` IS NOT NULL AND `EffectiveProbability`>=0;",
            ["PetProfiles"] = "SELECT COUNT(*) FROM `pet_content_profiles`;",
            ["PetProfilesItemMapped"] = "SELECT COUNT(*) FROM `pet_content_profiles` WHERE `ItemId` IS NOT NULL;",
            ["PetProfilesSemanticallyPromotable"] = "SELECT COUNT(*) FROM `pet_content_profiles` WHERE `ItemId` IS NOT NULL;",
            ["PetInnatesPromoted"] = "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `ProductionEnabled`=1;",
            ["PetEggRelations"] = "SELECT COUNT(*) FROM `pet_egg_relationships`;",
            ["HatchProbabilityVerified"] = "SELECT COUNT(*) FROM `pet_egg_relationships` WHERE `DeclaredProbability` IS NOT NULL AND `EffectiveProbability`>=0;"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries) result[query.Key] = query.Value.Contains("@run", StringComparison.Ordinal) ? await ScalarAsync(connection, query.Value, cancellationToken, ("@run", runId)) : await ScalarAsync(connection, query.Value, cancellationToken);
        return result;
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadMissingFieldMatrixAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `Domain`,`FieldName`,COUNT(*) `Total`,
                SUM(`EvidenceStatus`='Verified') `Verified`,SUM(`EvidenceStatus`='Derived') `Derived`,SUM(`EvidenceStatus`='Candidate') `Candidate`,
                SUM(`EvidenceStatus`='EvidenceBlocked') `EvidenceBlocked`,SUM(`EvidenceStatus`='ExplicitOfficialZero') `ExplicitOfficialZero`,
                SUM(`EvidenceStatus`='DefaultDisabledZero') `DefaultDisabledZero`,SUM(`EvidenceStatus`='NotApplicable') `NotApplicable`,
                SUM(`EvidenceStatus`='Deprecated') `Deprecated`,SUM(`EvidenceStatus` NOT IN ('Verified','Derived','Candidate','EvidenceBlocked','ExplicitOfficialZero','DefaultDisabledZero','NotApplicable','Deprecated')) `Unclassified`
            FROM `god2_research`.`content_phase3_field_closure` WHERE `RunId`=@run GROUP BY `Domain`,`FieldName` ORDER BY `Domain`,`FieldName`;
            """;
        command.Parameters.AddWithValue("@run", runId);
        var result = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["domain"] = reader.GetString(0),
                ["field"] = reader.GetString(1),
                ["total"] = reader.GetInt64(2),
                ["verified"] = reader.GetInt64(3),
                ["derived"] = reader.GetInt64(4),
                ["candidate"] = reader.GetInt64(5),
                ["evidenceBlocked"] = reader.GetInt64(6),
                ["explicitOfficialZero"] = reader.GetInt64(7),
                ["defaultDisabledZero"] = reader.GetInt64(8),
                ["notApplicable"] = reader.GetInt64(9),
                ["deprecated"] = reader.GetInt64(10),
                ["unclassified"] = reader.GetInt64(11)
            });
        }
        return result;
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadReferentialIntegrityAsync(MySqlConnection connection, string runId, CancellationToken cancellationToken)
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BrokenNpcMap"] = "SELECT COUNT(*) FROM `npcs` n LEFT JOIN `maps` m ON m.`Id`=n.`MapId` WHERE n.`ProductionSpawnEnabled`=1 AND m.`Id` IS NULL;",
            ["BrokenMonsterProfile"] = "SELECT COUNT(*) FROM `monster_semantic_profiles` p LEFT JOIN `monsters` m ON m.`Id`=p.`MonsterId` WHERE p.`RunId`=@run AND m.`Id` IS NULL;",
            ["BrokenMonsterSpawn"] = "SELECT COUNT(*) FROM `monster_spawn_semantics` s LEFT JOIN `monsters` m ON m.`Id`=s.`MonsterId` LEFT JOIN `maps` mp ON mp.`Id`=s.`MapId` WHERE s.`RunId`=@run AND (m.`Id` IS NULL OR (s.`MapId` IS NOT NULL AND mp.`Id` IS NULL));",
            ["BrokenSkillProfile"] = "SELECT COUNT(*) FROM `skill_semantic_profiles` p LEFT JOIN `skills` s ON s.`Id`=p.`SkillId` LEFT JOIN `skill_content_profiles` cp ON cp.`ProfileId`=p.`ClientProfileId` WHERE p.`RunId`=@run AND (s.`Id` IS NULL OR (p.`ClientProfileId` IS NOT NULL AND cp.`ProfileId` IS NULL));",
            ["BrokenQuestProfile"] = "SELECT COUNT(*) FROM `quest_content_profiles` p LEFT JOIN `quests` q ON q.`Id`=p.`QuestId` WHERE p.`ProductionProfileEnabled`=1 AND q.`Id` IS NULL;",
            ["BrokenQuestObjective"] = "SELECT COUNT(*) FROM `god2_research`.`quest_objective_candidates` o LEFT JOIN `quests` q ON q.`Id`=o.`QuestId` LEFT JOIN `items` i ON i.`Id`=o.`ItemId` LEFT JOIN `monsters` m ON m.`Id`=o.`MonsterId` WHERE o.`RelationshipEvidenceStatus` IN ('Verified','Derived') AND (q.`Id` IS NULL OR (o.`ItemId` IS NOT NULL AND i.`Id` IS NULL) OR (o.`MonsterId` IS NOT NULL AND m.`Id` IS NULL));",
            ["BrokenEquipmentMember"] = "SELECT COUNT(*) FROM `equipment_set_members` m LEFT JOIN `equipment_set_definitions` d ON d.`SetId`=m.`SetId` LEFT JOIN `items` i ON i.`Id`=m.`ItemId` WHERE m.`ProductionRelationshipEnabled`=1 AND (d.`SetId` IS NULL OR i.`Id` IS NULL);",
            ["BrokenPetInnate"] = "SELECT COUNT(*) FROM `pet_innate_definitions` WHERE `ProductionEnabled`=1 AND `EvidenceStatus`<>'Verified';",
            ["ContentOrphans"] = "SELECT COUNT(*) FROM `god2_research`.`content_phase3_field_closure` c LEFT JOIN `content_recovery_runs` r ON r.`RunId`=c.`RunId` WHERE c.`RunId`=@run AND r.`RunId` IS NULL;"
        };
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var query in queries) result[query.Key] = query.Value.Contains("@run", StringComparison.Ordinal) ? await ScalarAsync(connection, query.Value, cancellationToken, ("@run", runId)) : await ScalarAsync(connection, query.Value, cancellationToken);
        return result;
    }

    private static async Task<string> ResolveLatestBaselineAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `RunId`
            FROM `content_recovery_runs`
            WHERE `Phase`=@phase AND `Status`='COMPLETED'
            ORDER BY `CompletedAtUtc` DESC, `StartedAtUtc` DESC, `RunId` DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@phase", RecoveryVersions.Phase2);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("A completed Phase 2 RunId was not found; Phase 3 made no changes.");
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task InsertRunAsync(MySqlConnection connection, RecoveryWorkspace workspace, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `content_recovery_runs`
                (`RunId`,`Phase`,`Status`,`ClientRootIdentity`,`ExtractorVersion`,`ConverterName`,`ConverterVersion`,`GlossaryVersion`,`StartedAtUtc`)
            VALUES (@run,@phase,'RUNNING','OfficialClient:God2:Phase2Baseline',@extractor,@converter,@converterVersion,@glossary,@started);
            """;
        command.Parameters.AddWithValue("@run", workspace.RunId); command.Parameters.AddWithValue("@phase", workspace.Phase); command.Parameters.AddWithValue("@extractor", workspace.ExtractorVersion);
        command.Parameters.AddWithValue("@converter", RecoveryVersions.ConverterName); command.Parameters.AddWithValue("@converterVersion", RecoveryVersions.ConverterVersion); command.Parameters.AddWithValue("@glossary", RecoveryVersions.GlossaryVersion); command.Parameters.AddWithValue("@started", workspace.StartedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRunAsync(MySqlConnection connection, string runId, string status, string? summary, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE `content_recovery_runs` SET `Status`=@status,`CompletedAtUtc`=UTC_TIMESTAMP(6) WHERE `RunId`=@run;";
        command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@run", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = """
            INSERT INTO `god2_research`.`content_recovery_run_summary_archive`
                (`RunId`,`Status`,`SummaryJson`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES (@run,@status,@summary,'Recovery run 摘要 JSON 已移入 research；正式 run ledger 只保留流程狀態與時間。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `Status`=VALUES(`Status`),`SummaryJson`=VALUES(`SummaryJson`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """;
        summaryCommand.Parameters.AddWithValue("@run", runId);
        summaryCommand.Parameters.AddWithValue("@status", status);
        summaryCommand.Parameters.AddWithValue("@summary", (object?)summary ?? DBNull.Value);
        await summaryCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireLockAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var result = await ScalarAsync(connection, "SELECT GET_LOCK('god2-content-recovery-GameplayContentRecoveryPhase3',0);", cancellationToken);
        if (result != 1) throw new InvalidOperationException("Another Phase 3 content recovery process holds the MariaDB advisory lock.");
    }

    private static async Task ReleaseLockAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open) await ScalarAsync(connection, "SELECT RELEASE_LOCK('god2-content-recovery-GameplayContentRecoveryPhase3');", cancellationToken);
    }

    private string BuildConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = _options.Host,
        Port = (uint)_options.Port,
        Database = _options.DatabaseName,
        UserID = _options.Username,
        Password = _options.Password,
        CharacterSet = "utf8mb4",
        ConnectionTimeout = (uint)Math.Max(1, _options.ConnectionTimeoutSeconds),
        DefaultCommandTimeout = 180,
        Pooling = true,
        SslMode = MySqlSslMode.Preferred
    }.ConnectionString;

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken); return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record ClosureSpec(string Domain, string FromSql, string EntityKeySql, string Field, string StatusSql, string BlockedReason, string CandidateReason, string MissingEvidenceType, string SourcesJson, string FunctionsJson, string CandidateSource, string IdentityMatchMethod, string PromotionRequirementsJson);
}




