using God2.ClassicServer.Application.Configuration;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

public sealed class Phase3DeepSemanticAnalyzer
{
    private readonly DatabaseOptions _options;
    private string _sourceRunId = string.Empty;

    public Phase3DeepSemanticAnalyzer(DatabaseOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, object?>> AnalyzeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `RunId` FROM `content_recovery_runs`
            WHERE `Phase`=@phase AND `Status`='COMPLETED'
            ORDER BY `CompletedAtUtc` DESC, `StartedAtUtc` DESC, `RunId` DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("@phase", RecoveryVersions.Phase2);
        var sourceRunId = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(sourceRunId))
        {
            throw new InvalidOperationException("A completed Phase 2 RunId was not found.");
        }

        return await AnalyzeAsync(sourceRunId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, object?>> AnalyzeAsync(string sourceRunId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRunId);
        _sourceRunId = sourceRunId;
        await using var connection = new MySqlConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["phase2RunId"] = _sourceRunId,
            ["npcIdentityDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Total`,SUM(e.`ClientNpcId` IS NOT NULL) `ClientIdPresent`,SUM(e.`MapId` IS NOT NULL) `MapIdPresent`,
                    SUM(nid.`Id` IS NOT NULL) `ExactIdMatch`,SUM(nn.`Id` IS NOT NULL) `ExactNameMatch`,SUM(mm.`Id` IS NOT NULL) `ExactMapNameMatch`
                FROM `god2_research`.`npc_coordinate_evidence` e
                LEFT JOIN `npcs` nid ON nid.`Id`=e.`ClientNpcId`
                LEFT JOIN `npcs` nn ON BINARY COALESCE(nn.`NameZhTw`,nn.`Name`)=BINARY e.`NpcNameZhTw`
                LEFT JOIN `maps` mm ON BINARY COALESCE(mm.`NameZhTw`,mm.`Name`)=BINARY e.`MapNameZhTw`
                WHERE e.`RunId`=@run;
                """, cancellationToken),
            ["npcEvidenceSample"] = await Rows(connection, """
                SELECT `ClientNpcId`,`NpcNameZhTw`,`MapId`,`MapNameZhTw`,`PositionX`,`PositionY`,`CoordinateEvidenceStatus`,`SourceType`,`SourceIdentity`
                FROM `god2_research`.`npc_coordinate_evidence` WHERE `RunId`=@run ORDER BY `ClientNpcId`,`NpcNameZhTw` LIMIT 40;
                """, cancellationToken),
            ["npcEvidenceDistribution"] = await Rows(connection, """
                SELECT `SourceType`,`CoordinateEvidenceStatus`,SUBSTRING_INDEX(`SourceIdentity`,':line-',1) `SourceGroup`,COUNT(*) `Rows`,
                    SUM(`ClientNpcId` IS NOT NULL) `ClientIdPresent`,SUM(`MapId` IS NOT NULL) `MapIdPresent`
                FROM `god2_research`.`npc_coordinate_evidence` WHERE `RunId`=@run
                GROUP BY `SourceType`,`CoordinateEvidenceStatus`,SUBSTRING_INDEX(`SourceIdentity`,':line-',1)
                ORDER BY `Rows` DESC;
                """, cancellationToken),
            ["npcClientIdCompositeDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `EvidenceRows`,SUM(f.`FormalMatches`=1) `UniqueFormalClientId`,SUM(f.`FormalMatches`>1) `CompetingFormalClientId`,
                    SUM(n.`Id` IS NOT NULL) `ExactClientIdAndNameMatches`
                FROM `god2_research`.`npc_coordinate_evidence` e
                LEFT JOIN (
                    SELECT `NpcId` `ClientNpcId`,COUNT(*) `FormalMatches`
                    FROM `npc_client_identities`
                    GROUP BY `NpcId`
                ) f ON f.`ClientNpcId`=e.`ClientNpcId`
                LEFT JOIN `npcs` n ON n.`Id`=e.`ClientNpcId`
                    AND BINARY COALESCE(n.`NameZhTw`,n.`Name`)=BINARY e.`NpcNameZhTw`
                WHERE e.`RunId`=@run AND e.`ClientNpcId` IS NOT NULL;
                """, cancellationToken),
            ["npcDerivedSample"] = await Rows(connection, """
                SELECT e.`ClientNpcId`,e.`NpcNameZhTw`,e.`MapNameZhTw`,e.`PositionX`,e.`PositionY`,e.`SourceIdentity`,
                    n.`Id` `FormalNpcId`,n.`NameZhTw` `FormalName`,n.`Code` `FormalNpcCode`
                FROM `god2_research`.`npc_coordinate_evidence` e
                LEFT JOIN `npcs` n ON n.`Id`=e.`ClientNpcId`
                WHERE e.`RunId`=@run AND e.`ClientNpcId` IS NOT NULL ORDER BY e.`ClientNpcId`,n.`Id` LIMIT 80;
                """, cancellationToken),
            ["formalNpcSample"] = await Rows(connection, """
                SELECT `Id`,`Code`,`NameZhTw`,`MapId`,`InteractionFamily` FROM `npcs` ORDER BY `Id` LIMIT 40;
                """, cancellationToken),
            ["formalMaps"] = await Rows(connection, "SELECT `Id`,`Code`,`NameZhTw`,`Width`,`Height`,`SourceReference` FROM `maps` ORDER BY `NameZhTw`,`Id`;", cancellationToken),
            ["skillIdentityDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Total`,COUNT(DISTINCT p.`ClientSkillId`) `DistinctClientIds`,SUM(p.`ClientSkillId` IS NOT NULL) `ClientIdPresent`,
                    SUM(sid.`Id` IS NOT NULL) `ExactIdMatch`,SUM(sn.`Id` IS NOT NULL) `ExactNameMatch`,
                    SUM(sid.`Id` IS NOT NULL AND BINARY COALESCE(sid.`NameZhTw`,sid.`Name`)=BINARY p.`NameZhTw`) `ExactIdNameMatch`
                FROM `skill_content_profiles` p
                LEFT JOIN `skills` sid ON sid.`Id`=p.`ClientSkillId`
                LEFT JOIN `skills` sn ON BINARY COALESCE(sn.`NameZhTw`,sn.`Name`)=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run;
                """, cancellationToken),
            ["skillExactUniqueDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Profiles`,COUNT(DISTINCT fs.`Id`) `FormalSkills`,
                    SUM(p.`SkillFamily`<>'Unknown') `FamilySemantics`,
                    SUM(p.`TargetPolicy`<>'Unknown') `TargetSemantics`,
                    SUM(p.`MpCostPolicy` NOT IN ('Unknown','EvidenceBlocked') AND p.`MpCost` IS NOT NULL) `MpSemantics`
                FROM `skill_content_profiles` p
                JOIN (
                    SELECT MIN(`Id`) `Id`,COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `skills` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) fs ON BINARY fs.`ExactName`=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run;
                """, cancellationToken),
            ["skillSourceDistribution"] = await Rows(connection, """
                SELECT 'formal-runtime-profile' AS `SourceType`,p.`RunId` AS `SourceFile`,COUNT(*) `Profiles`,SUM(fs.`Id` IS NOT NULL) `ExactUniqueFormalName`,
                    SUM(p.`SkillFamily`<>'Unknown') `FamilySemantics`,
                    SUM(p.`TargetPolicy`<>'Unknown') `TargetSemantics`,
                    SUM(p.`MpCostPolicy` NOT IN ('Unknown','EvidenceBlocked') AND p.`MpCost` IS NOT NULL) `MpSemantics`
                FROM `skill_content_profiles` p
                LEFT JOIN (
                    SELECT MIN(`Id`) `Id`,COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `skills` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) fs ON BINARY fs.`ExactName`=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run GROUP BY p.`RunId` ORDER BY `ExactUniqueFormalName` DESC,`Profiles` DESC;
                """, cancellationToken),
            ["skillExactUniqueSample"] = await Rows(connection, """
                SELECT fs.`Id` `FormalSkillId`,fs.`NameZhTw` `FormalName`,fs.`SourceReference` `FormalSource`,p.`ClientSkillId`,p.`NameZhTw`,
                    p.`SkillFamily`,p.`TargetPolicy`,p.`MpCostPolicy`,p.`MpCost`,'formal-runtime-profile' AS `EvidenceStatus`,p.`RunId` AS `SourceType`,p.`ProfileId` AS `SourceFile`,p.`ProfileId` AS `SourceIdentity`
                FROM `skill_content_profiles` p
                JOIN (
                    SELECT MIN(`Id`) `Id`,MAX(`NameZhTw`) `NameZhTw`,MAX(`SourceReference`) `SourceReference`,COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `skills` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) fs ON BINARY fs.`ExactName`=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run ORDER BY fs.`Id`,p.`ProfileId` LIMIT 80;
                """, cancellationToken),
            ["skillFormalNameMultiplicity"] = await Rows(connection, """
                SELECT COUNT(*) `NamesMatched`,SUM(f.`FormalRows`=1) `UniqueFormalNames`,SUM(f.`FormalRows`=2) `TwoFormalRows`,
                    MAX(f.`FormalRows`) `MaximumFormalRowsPerName`
                FROM (SELECT BINARY COALESCE(`NameZhTw`,`Name`) `ExactName`,COUNT(*) `FormalRows` FROM `skills`
                      GROUP BY BINARY COALESCE(`NameZhTw`,`Name`)) f
                JOIN (SELECT DISTINCT BINARY `NameZhTw` `ExactName` FROM `skill_content_profiles` WHERE `RunId`=@run) p
                    ON p.`ExactName`=f.`ExactName`;
                """, cancellationToken),
            ["skillExactAllSample"] = await Rows(connection, """
                SELECT s.`Id` `FormalSkillId`,s.`NameZhTw` `FormalName`,s.`SourceReference` `FormalSource`,s.`Code` `FormalSkillCode`,
                    p.`ClientSkillId`,p.`NameZhTw`,'formal-runtime-profile' AS `SourceType`,p.`ProfileId` AS `SourceFile`,p.`ProfileId` AS `SourceIdentity`
                FROM `skill_content_profiles` p JOIN `skills` s ON BINARY COALESCE(s.`NameZhTw`,s.`Name`)=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run ORDER BY p.`NameZhTw`,s.`Id`,p.`SourceIdentity` LIMIT 60;
                """, cancellationToken),
            ["skillProfileSample"] = await Rows(connection, """
                SELECT `ClientSkillId`,`NameZhTw`,`SkillFamily`,`TargetPolicy`,`MpCostPolicy`,`MpCost`,'formal-runtime-profile' AS `EvidenceStatus`,`ProfileId` AS `SourceIdentity`
                FROM `skill_content_profiles` WHERE `RunId`=@run ORDER BY `ClientSkillId` LIMIT 50;
                """, cancellationToken),
            ["formalSkillSample"] = await Rows(connection, """
                SELECT `Id`,`Code`,`NameZhTw`,`SkillFamily`,`TargetPolicy`,`MpCostPolicy`,`MpCost` FROM `skills` ORDER BY `Id` LIMIT 50;
                """, cancellationToken),
            ["questIdentityDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Total`,COUNT(DISTINCT p.`ClientQuestId`) `DistinctClientIds`,SUM(qid.`Id` IS NOT NULL) `ExactIdMatch`,
                    SUM(qn.`Id` IS NOT NULL) `ExactNameMatch`,
                    SUM(qid.`Id` IS NOT NULL AND BINARY COALESCE(qid.`NameZhTw`,qid.`Name`)=BINARY p.`NameZhTw`) `ExactIdNameMatch`
                FROM `quest_content_profiles` p
                LEFT JOIN `quests` qid ON qid.`Id`=p.`ClientQuestId`
                LEFT JOIN `quests` qn ON BINARY COALESCE(qn.`NameZhTw`,qn.`Name`)=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run;
                """, cancellationToken),
            ["questExactUniqueDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Profiles`,COUNT(DISTINCT fq.`Id`) `FormalQuests`,COUNT(DISTINCT p.`ClientQuestId`) `ClientQuestIds`,
                    SUM(JSON_VALID(p.`StepsJson`)) `ObjectiveSemantics`,
                    SUM(p.`RewardTextZhTw` IS NOT NULL AND p.`RewardTextZhTw`<>'') `RewardSemantics`
                FROM `quest_content_profiles` p
                JOIN (
                    SELECT MIN(`Id`) `Id`,COALESCE(`NameZhTw`,`Name`) `ExactName`
                    FROM `quests` GROUP BY BINARY COALESCE(`NameZhTw`,`Name`) HAVING COUNT(*)=1
                ) fq ON BINARY fq.`ExactName`=BINARY p.`NameZhTw`
                WHERE p.`RunId`=@run;
                """, cancellationToken),
            ["questObjectiveDistribution"] = await Rows(connection, """
                SELECT `ObjectiveType`,`EvidenceStatus`,COUNT(*) `Rows`,SUM(`ItemId` IS NOT NULL) `ItemTarget`,
                    SUM(`MonsterId` IS NOT NULL) `MonsterTarget`,SUM(`RequiredQuantity` IS NOT NULL) `QuantityPresent`
                FROM `god2_research`.`quest_objective_candidates` WHERE `RunId`=@run GROUP BY `ObjectiveType`,`EvidenceStatus` ORDER BY `Rows` DESC;
                """, cancellationToken),
            ["questProfileSample"] = await Rows(connection, """
                SELECT `ClientQuestId`,`NameZhTw`,`StartNpcClientId`,`EndNpcClientId`,
                    CASE WHEN JSON_VALID(`StepsJson`) THEN 'Derived' ELSE 'EvidenceBlocked' END AS `ObjectiveEvidenceStatus`,
                    CASE WHEN `RewardTextZhTw` IS NOT NULL AND `RewardTextZhTw`<>'' THEN 'Derived' ELSE 'Candidate' END AS `RewardEvidenceStatus`,
                    CASE WHEN `QuestId` IS NULL THEN 'EvidenceBlocked' ELSE 'Derived' END AS `EvidenceStatus`
                FROM `quest_content_profiles` WHERE `RunId`=@run ORDER BY `ClientQuestId` LIMIT 50;
                """, cancellationToken),
            ["formalQuestSample"] = await Rows(connection, """
                SELECT `Id`,`Code`,`NameZhTw`,`StartNpcId`,`EndNpcId` FROM `quests` ORDER BY `Id` LIMIT 50;
                """, cancellationToken),
            ["merchantDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Total`,COUNT(DISTINCT `ClientInventoryGroupId`) `DistinctGroups`,SUM(`MerchantId` IS NOT NULL) `BoundMerchant`,
                    SUM(`BuyPrice` IS NOT NULL) `BuyPricePresent`,SUM(`SellPrice` IS NOT NULL) `SellPricePresent`,
                    MIN(`ClientInventoryGroupId`) `MinimumGroup`,MAX(`ClientInventoryGroupId`) `MaximumGroup`
                FROM `god2_research`.`merchant_inventory_candidates` WHERE `RunId`=@run;
                """, cancellationToken),
            ["formalMerchants"] = await Rows(connection, "SELECT * FROM `merchants` ORDER BY `Id`;", cancellationToken),
            ["combineDistribution"] = await Rows(connection, """
                SELECT `RelationshipStatus`,
                    CASE WHEN `DeclaredProbability` IS NULL THEN 'DefaultDisabledZero' WHEN `EffectiveProbability`>=0 THEN 'Derived' ELSE 'EvidenceBlocked' END AS `ProbabilityEvidenceStatus`,
                    COUNT(*) `Rows`,SUM(`Quantity` IS NOT NULL) `QuantityPresent`
                FROM `container_item_relationships` WHERE `RunId`=@run GROUP BY `RelationshipStatus`,`ProbabilityEvidenceStatus`;
                """, cancellationToken),
            ["petProfileDistribution"] = await Rows(connection, """
                SELECT COUNT(*) `Total`,COUNT(DISTINCT `ClientPetId`) `DistinctClientPetIds`,SUM(`ItemId` IS NOT NULL) `ItemIdentityMapped`,
                    SUM(JSON_LENGTH(`SkillReferencesJson`)>0) `SkillReferencesPresent`,SUM(JSON_LENGTH(`EvolutionReferencesJson`)>0) `EvolutionReferencesPresent`
                FROM `pet_content_profiles` WHERE `RunId`=@run;
                """, cancellationToken)
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Rows(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (sql.Contains("@run", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("@run", _sourceRunId);
        }

        var result = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                row[reader.GetName(ordinal)] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            result.Add(row);
        }

        return result;
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
}



