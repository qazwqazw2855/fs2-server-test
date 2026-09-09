using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace God2.GameplayContentRecovery;

internal static class Phase2MariaDbPromoter
{
    public static async Task<IReadOnlyDictionary<string, object?>> PromoteAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        RecoveryWorkspace workspace,
        ZhTwLocalization localization,
        CancellationToken cancellationToken)
    {
        var authorities = await ReadAuthorityMapAsync(connection, transaction, cancellationToken);
        var dropTables = await ReadDropTablesAsync(connection, transaction, cancellationToken);
        var uniqueNames = await ReadUniqueNamesAsync(connection, transaction, cancellationToken);
        var metrics = new Dictionary<string, long>(StringComparer.Ordinal);

        await InsertLayoutsAsync(connection, transaction, workspace, metrics, cancellationToken);
        await InsertFieldEvidenceAsync(connection, transaction, workspace, metrics, cancellationToken);

        foreach (var entity in workspace.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (entity.Domain)
            {
                case "ItemProfile":
                    await PromoteItemProfileAsync(connection, transaction, workspace, entity, authorities, metrics, cancellationToken);
                    break;
                case "MonsterDropRelationship":
                    await PromoteMonsterDropAsync(connection, transaction, workspace, entity, authorities, dropTables, metrics, cancellationToken);
                    break;
                case "NpcCoordinateEvidence":
                    await PromoteNpcCoordinateAsync(connection, transaction, workspace, entity, authorities, uniqueNames, localization, metrics, cancellationToken);
                    break;
                case "SkillProfile":
                    await PromoteSkillProfileAsync(connection, transaction, workspace, entity, authorities, uniqueNames, localization, metrics, cancellationToken);
                    break;
                case "MerchantInventoryCandidate":
                    await PromoteMerchantCandidateAsync(connection, transaction, workspace, entity, authorities, metrics, cancellationToken);
                    break;
                case "QuestProfile":
                    await PromoteQuestProfileAsync(connection, transaction, workspace, entity, authorities, uniqueNames, localization, metrics, cancellationToken);
                    break;
                case "QuestObjectiveCandidate":
                    await PromoteQuestObjectiveAsync(connection, transaction, workspace, entity, authorities, localization, metrics, cancellationToken);
                    break;
                case "EquipmentSet":
                    await PromoteEquipmentSetAsync(connection, transaction, workspace, entity, localization, metrics, cancellationToken);
                    break;
                case "EquipmentSetMember":
                    await PromoteEquipmentSetMemberAsync(connection, transaction, workspace, entity, authorities, metrics, cancellationToken);
                    break;
                case "ContainerRelationship":
                    await PromoteContainerAsync(connection, transaction, workspace, entity, authorities, metrics, cancellationToken);
                    break;
                case "PetProfile":
                    await PromotePetProfileAsync(connection, transaction, workspace, entity, authorities, localization, metrics, cancellationToken);
                    break;
                case "PetInnate":
                    await PromotePetInnateAsync(connection, transaction, workspace, entity, localization, metrics, cancellationToken);
                    break;
                case "HistoricalObservation":
                    await PromoteHistoricalObservationAsync(connection, transaction, workspace, entity, metrics, cancellationToken);
                    break;
            }
        }

        await ReconcileCurrentSnapshotAsync(connection, transaction, workspace.RunId, cancellationToken);

        metrics["verified"] = workspace.Entities.LongCount(entity => entity.EvidenceStatus == "Verified");
        metrics["derived"] = workspace.Entities.LongCount(entity => entity.EvidenceStatus == "Derived");
        metrics["candidate"] = workspace.Entities.LongCount(entity => entity.EvidenceStatus == "Candidate");
        metrics["evidenceBlocked"] = workspace.Entities.LongCount(entity => entity.EvidenceStatus is "EvidenceBlocked" or "PartiallyMapped" || entity.MissingRequiredFields.Count > 0);
        metrics["defaultDisabledZero"] = workspace.Entities.LongCount(entity => entity.Fields.Values.Any(value => string.Equals(value?.ToString(), "DefaultDisabledZero", StringComparison.Ordinal)));
        return metrics.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
    }

    private static async Task InsertLayoutsAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `god2_research`.`content_client_table_layouts`
                (`LayoutId`,`RunId`,`SourceFile`,`SourceHash`,`Decoder`,`RecordCount`,`MaximumFieldCount`,`HeaderJson`,`RecordBoundaryStatus`,`LoaderEvidenceStatus`,`ScannedAtUtc`)
            VALUES (@id,@run,@file,@hash,@decoder,@records,@fields,@header,@boundary,@loader,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RecordCount`=VALUES(`RecordCount`),`MaximumFieldCount`=VALUES(`MaximumFieldCount`),
                `HeaderJson`=VALUES(`HeaderJson`),`RecordBoundaryStatus`=VALUES(`RecordBoundaryStatus`),`LoaderEvidenceStatus`=VALUES(`LoaderEvidenceStatus`),`ScannedAtUtc`=VALUES(`ScannedAtUtc`);
            """;
        foreach (var entity in workspace.Entities.Where(value => value.Domain == "ClientTableLayout"))
        {
            await ExecuteAsync(connection, transaction, sql, cancellationToken,
                ("@id", ContentHash.StableId("layout", entity.AuthorityKey, entity.SourceHash)), ("@run", workspace.RunId), ("@file", entity.SourceFile), ("@hash", entity.SourceHash),
                ("@decoder", Get<string>(entity, "decoder") ?? "Unknown"), ("@records", Get<int>(entity, "recordCount")), ("@fields", Get<int>(entity, "maximumFieldCount")),
                ("@header", Get<string>(entity, "headerJson") ?? "[]"), ("@boundary", Get<string>(entity, "recordBoundaryStatus") ?? "EvidenceBlocked"),
                ("@loader", Get<string>(entity, "loaderEvidenceStatus") ?? "EvidenceBlocked"));
            Increment(metrics, "clientTableLayoutsUpserted");
        }
    }

    private static async Task InsertFieldEvidenceAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `god2_research`.`content_field_evidence`
                (`EvidenceId`,`RunId`,`Domain`,`AuthorityKey`,`FieldName`,`ValueJson`,`EvidenceState`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,`Reason`,`RecordedAtUtc`)
            VALUES (@id,@run,@domain,@authority,@field,@value,@state,@sourceType,@file,@identity,@hash,@confidence,@reason,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `ValueJson`=VALUES(`ValueJson`),`EvidenceState`=VALUES(`EvidenceState`),`Reason`=VALUES(`Reason`),`RecordedAtUtc`=VALUES(`RecordedAtUtc`);
            """;
        foreach (var entity in workspace.Entities.Where(value => value.Domain != "ClientTableLayout"))
        {
            foreach (var field in entity.Fields)
            {
                var state = FieldState(entity, field.Key, field.Value);
                await ExecuteAsync(connection, transaction, sql, cancellationToken,
                    ("@id", ContentHash.StableId("phase2-field", entity.Domain, entity.AuthorityKey, field.Key, entity.SourceHash)), ("@run", workspace.RunId),
                    ("@domain", entity.Domain), ("@authority", Truncate(entity.AuthorityKey, 512)), ("@field", Truncate(field.Key, 128)),
                    ("@value", field.Value is null ? null : RecoveryJson.Compact(field.Value)), ("@state", state), ("@sourceType", SourceType(entity.SourceFile)),
                    ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)), ("@hash", entity.SourceHash),
                    ("@confidence", Truncate(entity.Confidence, 32)), ("@reason", Truncate(field.Value is null ? "No evidence-backed value was available; no default was applied." : entity.TransformationRule, 1024)));
                Increment(metrics, "fieldEvidenceUpserted");
            }

            foreach (var missing in entity.MissingRequiredFields.Where(missing => !entity.Fields.ContainsKey(missing)))
            {
                await ExecuteAsync(connection, transaction, sql, cancellationToken,
                    ("@id", ContentHash.StableId("phase2-field", entity.Domain, entity.AuthorityKey, missing, entity.SourceHash)), ("@run", workspace.RunId),
                    ("@domain", entity.Domain), ("@authority", Truncate(entity.AuthorityKey, 512)), ("@field", Truncate(missing, 128)), ("@value", null),
                    ("@state", "EvidenceBlocked"), ("@sourceType", SourceType(entity.SourceFile)), ("@file", Truncate(entity.SourceFile, 768)),
                    ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)), ("@hash", entity.SourceHash), ("@confidence", Truncate(entity.Confidence, 32)),
                    ("@reason", "Required field was not supported by an accessible source; no default was applied."));
                Increment(metrics, "fieldEvidenceUpserted");
            }
        }
    }

    private static async Task PromoteItemProfileAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        var authority = Get<string>(entity, "formalAuthorityKey");
        if (!Resolve(authorities, "Item", authority, out var itemId))
        {
            Increment(metrics, "itemProfilesIdentityBlocked");
            return;
        }

        const string sql = """
            INSERT INTO `item_content_profiles`
                (`ItemId`,`RunId`,`ClientItemId`,`ItemFamily`,`OfficialCategoryZhTw`,`Stackable`,`MaximumStack`,
                 `TradePolicy`,`WarehousePolicy`,`EquipmentSlot`,`RequiredLevel`,`PhysicalAttackBonus`,`MagicAttackBonus`,`PhysicalDefenseBonus`,`MagicDefenseBonus`,
                 `HpBonus`,`MpBonus`,`SpeedBonus`,`IconKey`,`ModelKey`,`UpdatedAtUtc`)
            VALUES (@item,@run,@client,@family,@category,@stack,@maxStack,@trade,@warehouse,@slot,@level,@patk,@matk,@pdef,@mdef,@hp,@mp,@speed,@icon,@model,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`ItemFamily`=VALUES(`ItemFamily`),`OfficialCategoryZhTw`=VALUES(`OfficialCategoryZhTw`),
                 `Stackable`=VALUES(`Stackable`),`MaximumStack`=VALUES(`MaximumStack`),
                 `TradePolicy`=VALUES(`TradePolicy`),`WarehousePolicy`=VALUES(`WarehousePolicy`),`EquipmentSlot`=VALUES(`EquipmentSlot`),
                 `PhysicalAttackBonus`=VALUES(`PhysicalAttackBonus`),`MagicAttackBonus`=VALUES(`MagicAttackBonus`),`PhysicalDefenseBonus`=VALUES(`PhysicalDefenseBonus`),
                 `MagicDefenseBonus`=VALUES(`MagicDefenseBonus`),`HpBonus`=VALUES(`HpBonus`),`MpBonus`=VALUES(`MpBonus`),`SpeedBonus`=VALUES(`SpeedBonus`),
                 `IconKey`=VALUES(`IconKey`),`ModelKey`=VALUES(`ModelKey`),`UpdatedAtUtc`=VALUES(`UpdatedAtUtc`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            ("@item", itemId), ("@run", workspace.RunId), ("@client", Get<int>(entity, "clientItemId")),
            ("@family", Get<string>(entity, "itemFamily") ?? "Unknown"), ("@category", Get<string>(entity, "officialCategoryZhTw") ?? "未分類"),
            ("@stack", Get<bool?>(entity, "stackable")), ("@maxStack", Get<int?>(entity, "maximumStack")),
            ("@trade", Get<string>(entity, "tradePolicy") ?? "Unknown"), ("@warehouse", Get<string>(entity, "warehousePolicy") ?? "Unknown"),
            ("@slot", Get<string>(entity, "equipmentSlot")), ("@level", Get<int?>(entity, "requiredLevel")), ("@patk", Get<int?>(entity, "physicalAttackBonus")),
            ("@matk", Get<int?>(entity, "magicAttackBonus")), ("@pdef", Get<int?>(entity, "physicalDefenseBonus")), ("@mdef", Get<int?>(entity, "magicDefenseBonus")),
            ("@hp", Get<int?>(entity, "hpBonus")), ("@mp", Get<int?>(entity, "mpBonus")), ("@speed", Get<int?>(entity, "speedBonus")),
            ("@icon", Truncate(Get<string>(entity, "iconKey"), 256)), ("@model", Truncate(Get<string>(entity, "modelKey"), 256)));
        await ExecuteAsync(connection, transaction, """
            UPDATE `god2_game`.`item_registry`
            SET `model_key`=@model
            WHERE `item_id`=@item
              AND @model IS NOT NULL
              AND @model<>'';

            UPDATE `god2_game`.`items`
            SET `model_key`=@model
            WHERE `item_id`=@item
              AND @model IS NOT NULL
              AND @model<>'';

            UPDATE `god2_game`.`weapons`
            SET `model_key`=@model
            WHERE `item_id`=@item
              AND @model IS NOT NULL
              AND @model<>'';

            UPDATE `god2_game`.`equipment`
            SET `model_key`=@model
            WHERE `item_id`=@item
              AND @model IS NOT NULL
              AND @model<>'';

            UPDATE `god2_game`.`magic_treasures`
            SET `model_key`=@model
            WHERE `item_id`=@item
              AND @model IS NOT NULL
              AND @model<>'';
            """, cancellationToken, ("@item", itemId), ("@model", Truncate(Get<string>(entity, "modelKey"), 256)));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`content_profile_source_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES ('item_content_profiles',CAST(@item AS char),@run,@sourceType,@file,@identity,@hash,@evidence,'來源區段與來源 hash 已移入 research 封存；正式物品 profile 只保留物品屬性與服務端規則。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@item", itemId), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(Get<string>(entity, "sourceSection") ?? entity.SourceFile, 768)),
            ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)), ("@hash", entity.SourceHash),
            ("@evidence", NormalizeEvidence(entity.EvidenceStatus)));
        await ExecuteAsync(connection, transaction, "UPDATE `items` SET `ItemFamily`=@family,`StackPolicy`=@stack,`TradePolicy`=@trade WHERE `Id`=@id;", cancellationToken,
            ("@family", Get<string>(entity, "itemFamily")), ("@stack", Get<string>(entity, "stackPolicy") ?? "Unknown"), ("@trade", Get<string>(entity, "tradePolicy") ?? "Unknown"), ("@id", itemId));
        Increment(metrics, "itemProfilesUpserted");
    }

    private static async Task PromoteMonsterDropAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, IReadOnlyDictionary<int, int> dropTables, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        if (!Resolve(authorities, "MonsterTemplate", Get<string>(entity, "formalMonsterAuthorityKey"), out var monsterId) ||
            !Resolve(authorities, "Item", Get<string>(entity, "formalItemAuthorityKey"), out var itemId))
        {
            Increment(metrics, "dropRelationshipsIdentityBlocked");
            return;
        }

        var id = ContentHash.StableId("phase2-drop", monsterId.ToString(CultureInfo.InvariantCulture), itemId.ToString(CultureInfo.InvariantCulture), entity.SourceIdentity);
        const string sql = """
            INSERT INTO `monster_drop_relationships`
                (`RelationshipId`,`RunId`,`MonsterId`,`DropTableId`,`DropGroupId`,`DropEntryId`,`ItemId`,`MinimumQuantity`,`MaximumQuantity`,`DeclaredDropChance`,
                 `EffectiveDropChance`,`Weight`,`RollType`,`ExclusiveGroup`,`Guaranteed`,`QuestCondition`,`MapCondition`,`LevelCondition`,`EventCondition`,
                 `DropRelationshipStatus`,`IsDropEnabled`,`CreatedAtUtc`,`UpdatedAtUtc`)
            VALUES (@id,@run,@monster,@table,@group,@entry,@item,@min,@max,@original,@effective,@weight,@roll,@exclusive,@guaranteed,@quest,@map,@level,@event,
                    @relationshipStatus,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`MinimumQuantity`=VALUES(`MinimumQuantity`),`MaximumQuantity`=VALUES(`MaximumQuantity`),
                 `DeclaredDropChance`=VALUES(`DeclaredDropChance`),`EffectiveDropChance`=VALUES(`EffectiveDropChance`),`Weight`=VALUES(`Weight`),
                 `DropRelationshipStatus`=VALUES(`DropRelationshipStatus`),`IsDropEnabled`=0,`UpdatedAtUtc`=VALUES(`UpdatedAtUtc`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            ("@id", id), ("@run", workspace.RunId), ("@monster", monsterId), ("@table", dropTables.GetValueOrDefault(monsterId) is var tableId && tableId > 0 ? tableId : null),
            ("@group", Get<string>(entity, "dropGroupId")), ("@entry", Truncate(entity.AuthorityKey, 128)), ("@item", itemId),
            ("@min", Get<int?>(entity, "minimumQuantity")), ("@max", Get<int?>(entity, "maximumQuantity")), ("@original", Get<decimal?>(entity, "originalDropChance")),
            ("@effective", Get<decimal?>(entity, "effectiveDropChance") ?? 0m), ("@weight", Get<decimal?>(entity, "weight")), ("@roll", Get<string>(entity, "rollType")),
            ("@exclusive", Get<string>(entity, "exclusiveGroup")), ("@guaranteed", Get<bool?>(entity, "guaranteed")), ("@quest", Get<string>(entity, "questCondition")),
            ("@map", Get<string>(entity, "mapCondition")), ("@level", Get<string>(entity, "levelCondition")), ("@event", Get<string>(entity, "eventCondition")),
            ("@relationshipStatus", Get<string>(entity, "dropRelationshipStatus") ?? "Candidate"));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`monsters`
                (`monster_id`,`code`,`name_zh_tw`,`level`,`max_hp`,`max_mp`,`physical_attack`,`physical_defense`,
                 `experience_reward`,`currency_reward`,`combat_stat_source_zh_tw`,`combat_stat_policy_zh_tw`,`enabled`)
            SELECT
                monster_row.`Id`,
                monster_row.`Code`,
                LEFT(COALESCE(NULLIF(TRIM(monster_row.`NameZhTw`),''), NULLIF(TRIM(monster_row.`Name`),''), CONCAT('Monster ', monster_row.`Id`)), 150),
                monster_row.`Level`,
                monster_row.`MaxHp`,
                monster_row.`MaxMp`,
                monster_row.`Attack`,
                monster_row.`Defense`,
                monster_row.`ExperienceReward`,
                COALESCE(monster_row.`CurrencyRewardMaximum`, monster_row.`CurrencyRewardMinimum`),
                CASE
                    WHEN monster_row.`Level` IS NOT NULL OR monster_row.`MaxHp` IS NOT NULL OR monster_row.`MaxMp` IS NOT NULL
                        OR monster_row.`Attack` IS NOT NULL OR monster_row.`Defense` IS NOT NULL THEN '既有怪物資料同步'
                    ELSE '待服務端設計'
                END,
                CASE
                    WHEN monster_row.`Level` IS NOT NULL OR monster_row.`MaxHp` IS NOT NULL OR monster_row.`MaxMp` IS NOT NULL
                        OR monster_row.`Attack` IS NOT NULL OR monster_row.`Defense` IS NOT NULL THEN '只同步既有欄位，未證實戰鬥公式不覆蓋'
                    ELSE '尚未建立平衡規則'
                END,
                0
            FROM `god2`.`monsters` monster_row
            WHERE monster_row.`Id`=@monster
            ON DUPLICATE KEY UPDATE
                `code`=COALESCE(VALUES(`code`), `god2_game`.`monsters`.`code`),
                `name_zh_tw`=VALUES(`name_zh_tw`),
                `level`=COALESCE(VALUES(`level`), `god2_game`.`monsters`.`level`),
                `max_hp`=COALESCE(VALUES(`max_hp`), `god2_game`.`monsters`.`max_hp`),
                `max_mp`=COALESCE(VALUES(`max_mp`), `god2_game`.`monsters`.`max_mp`),
                `physical_attack`=COALESCE(VALUES(`physical_attack`), `god2_game`.`monsters`.`physical_attack`),
                `physical_defense`=COALESCE(VALUES(`physical_defense`), `god2_game`.`monsters`.`physical_defense`),
                `experience_reward`=COALESCE(VALUES(`experience_reward`), `god2_game`.`monsters`.`experience_reward`),
                `currency_reward`=COALESCE(VALUES(`currency_reward`), `god2_game`.`monsters`.`currency_reward`),
                `combat_stat_source_zh_tw`=CASE
                    WHEN VALUES(`combat_stat_source_zh_tw`) <> '待服務端設計' THEN VALUES(`combat_stat_source_zh_tw`)
                    ELSE `god2_game`.`monsters`.`combat_stat_source_zh_tw`
                END,
                `combat_stat_policy_zh_tw`=CASE
                    WHEN VALUES(`combat_stat_policy_zh_tw`) <> '尚未建立平衡規則' THEN VALUES(`combat_stat_policy_zh_tw`)
                    ELSE `god2_game`.`monsters`.`combat_stat_policy_zh_tw`
                END,
                `updated_at_utc`=UTC_TIMESTAMP(6);
            """, cancellationToken, ("@monster", monsterId));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`monster_drops`
                (`drop_id`,`monster_id`,`monster_name_cache`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,
                 `drop_rate`,`drop_rate_unit`,`drop_group`,`drop_source_zh_tw`,`drop_policy_zh_tw`,`is_guaranteed`,`enabled`)
            SELECT CAST(CONV(SUBSTRING(@id,1,15),16,10) AS unsigned),
                @monster,
                LEFT(COALESCE(monster_row.`name_zh_tw`,CONCAT('Monster ',@monster)),150),
                @item,
                LEFT(COALESCE(item_row.`name_zh_tw`,CONCAT('Item ',@item)),200),
                @min,
                @max,
                CASE
                    WHEN @original IS NULL THEN NULL
                    WHEN @original > 9999.99999999 THEN 9999.99999999
                    ELSE @original
                END,
                CASE WHEN @original IS NULL THEN 'Unknown' ELSE 'OfficialEvidence' END,
                LEFT(COALESCE(@group,@entry),50),
                'Phase2 monster_drop_relationships',
                CASE WHEN @relationshipStatus IN ('Verified','Derived') THEN '既有證據同步' ELSE '候選掉落，尚未正式啟用' END,
                @guaranteed,
                0
            FROM `god2`.`monsters` legacy_monster
            JOIN `god2_game`.`monsters` monster_row ON monster_row.`code`=legacy_monster.`Code`
            JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=@item
            WHERE legacy_monster.`Id`=@monster
            ON DUPLICATE KEY UPDATE
                `monster_name_cache`=VALUES(`monster_name_cache`),
                `item_name_cache`=VALUES(`item_name_cache`),
                `minimum_quantity`=VALUES(`minimum_quantity`),
                `maximum_quantity`=VALUES(`maximum_quantity`),
                `drop_rate`=VALUES(`drop_rate`),
                `drop_rate_unit`=VALUES(`drop_rate_unit`),
                `drop_group`=VALUES(`drop_group`),
                `drop_source_zh_tw`=VALUES(`drop_source_zh_tw`),
                `drop_policy_zh_tw`=VALUES(`drop_policy_zh_tw`),
                `is_guaranteed`=VALUES(`is_guaranteed`),
                `updated_at_utc`=UTC_TIMESTAMP(6);
            """, cancellationToken, ("@id", id), ("@monster", monsterId), ("@item", itemId),
            ("@min", Get<int?>(entity, "minimumQuantity")), ("@max", Get<int?>(entity, "maximumQuantity")),
            ("@original", Get<decimal?>(entity, "originalDropChance")), ("@group", Get<string>(entity, "dropGroupId")),
            ("@entry", Truncate(entity.AuthorityKey, 128)), ("@relationshipStatus", Get<string>(entity, "dropRelationshipStatus") ?? "Candidate"),
            ("@guaranteed", Get<bool?>(entity, "guaranteed")));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`relationship_source_archive`
                (`FormalTable`,`RelationshipId`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,`ArchivedAtUtc`)
            VALUES ('monster_drop_relationships',@id,@run,@sourceType,@file,@identity,@hash,@confidence,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`Confidence`=VALUES(`Confidence`),
                `ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken,
            ("@id", id), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)), ("@file", Truncate(entity.SourceFile, 768)),
            ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)), ("@hash", entity.SourceHash), ("@confidence", Truncate(entity.Confidence, 32)));
        await ExecuteAsync(connection, transaction, "UPDATE `monsters` SET `DropPolicy`='KnownItemsProbabilityUnknown' WHERE `Id`=@id AND (`DropPolicy` IS NULL OR `DropPolicy`='Unknown');", cancellationToken, ("@id", monsterId));
        Increment(metrics, "dropRelationshipsUpserted");
    }

    private static async Task PromoteNpcCoordinateAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, IReadOnlyDictionary<(string Table, string Name), int> uniqueNames,
        ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        int? npcId = Resolve(authorities, "NpcTemplate", Get<string>(entity, "formalNpcAuthorityKey"), out var resolvedNpc) ? resolvedNpc : null;
        int? mapId = Get<int?>(entity, "mapId");
        var name = Convert(localization, entity.Name, entity.SourceHash);
        var map = Convert(localization, Get<string>(entity, "mapName"), entity.SourceHash);
        if (npcId is null && name is not null && uniqueNames.TryGetValue(("npcs", NormalizeName(name)), out resolvedNpc)) npcId = resolvedNpc;
        if (mapId is null && map is not null && uniqueNames.TryGetValue(("maps", NormalizeName(map)), out var resolvedMap)) mapId = resolvedMap;
        const string sql = """
            INSERT INTO `god2_research`.`npc_coordinate_evidence`
                (`CoordinateEvidenceId`,`RunId`,`NpcId`,`ClientNpcId`,`NpcNameZhTw`,`MapId`,`MapNameZhTw`,`PositionX`,`PositionY`,`Direction`,
                 `CoordinateEvidenceStatus`,`ProductionSpawnEnabled`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,`CreatedAtUtc`)
            VALUES (@id,@run,@npc,@client,@name,@mapId,@mapName,@x,@y,@direction,@status,0,@sourceType,@file,@identity,@hash,@confidence,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`NpcId`=VALUES(`NpcId`),`MapId`=VALUES(`MapId`),`NpcNameZhTw`=VALUES(`NpcNameZhTw`),
                 `MapNameZhTw`=VALUES(`MapNameZhTw`),`CoordinateEvidenceStatus`=VALUES(`CoordinateEvidenceStatus`),`ProductionSpawnEnabled`=0;
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            ("@id", ContentHash.StableId("npc-coordinate", entity.AuthorityKey, entity.SourceHash)), ("@run", workspace.RunId), ("@npc", npcId),
            ("@client", Get<int?>(entity, "clientNpcId")), ("@name", Truncate(name, 256)), ("@mapId", mapId), ("@mapName", Truncate(map, 256)),
            ("@x", Get<int>(entity, "x")), ("@y", Get<int>(entity, "y")), ("@direction", Get<int?>(entity, "direction")),
            ("@status", Get<string>(entity, "coordinateEvidenceStatus") ?? "Candidate"), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)), ("@hash", entity.SourceHash), ("@confidence", Truncate(entity.Confidence, 32)));
        if (npcId is not null)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO `god2_game`.`npcs`
                    (`npc_id`,`code`,`name_zh_tw`,`npc_type`,`interaction_family`,`enabled`)
                SELECT source_npc.`Id`,LEFT(NULLIF(source_npc.`Code`,''),100),
                    LEFT(COALESCE(NULLIF(source_npc.`NameZhTw`,''),@name,NULLIF(source_npc.`Name`,''),CONCAT('Npc ',source_npc.`Id`)),150),
                    LEFT(COALESCE(NULLIF(source_npc.`NpcType`,''),'一般'),50),
                    source_npc.`InteractionFamily`,
                    source_npc.`ProductionSpawnEnabled`
                FROM `npcs` source_npc
                WHERE source_npc.`Id`=@id
                ON DUPLICATE KEY UPDATE
                    `name_zh_tw`=VALUES(`name_zh_tw`),
                    `npc_type`=VALUES(`npc_type`),
                    `interaction_family`=COALESCE(VALUES(`interaction_family`),`interaction_family`),
                    `enabled`=CASE WHEN `god2_game`.`npcs`.`enabled`=1 OR VALUES(`enabled`)=1 THEN 1 ELSE 0 END,
                    `updated_at_utc`=UTC_TIMESTAMP(6);
                """, cancellationToken, ("@id", npcId), ("@name", Truncate(name, 150)));
        }
        Increment(metrics, npcId is null || mapId is null ? "npcCoordinatesIdentityBlocked" : "npcCoordinatesResolved");
        Increment(metrics, "npcCoordinateEvidenceUpserted");
    }

    private static async Task PromoteSkillProfileAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, IReadOnlyDictionary<(string Table, string Name), int> uniqueNames,
        ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        int? skillId = Resolve(authorities, "Skill", Get<string>(entity, "formalSkillAuthorityKey"), out var resolved) ? resolved : null;
        var nameZhTw = Convert(localization, entity.Name, entity.SourceHash);
        if (skillId is null && nameZhTw is not null && uniqueNames.TryGetValue(("skills", NormalizeName(nameZhTw)), out resolved)) skillId = resolved;
        var profileId = ContentHash.StableId("skill-profile", entity.AuthorityKey, entity.SourceHash);
        const string sql = """
            INSERT INTO `skill_content_profiles`
                (`ProfileId`,`RunId`,`SkillId`,`ClientSkillId`,`NameZhTw`,`SkillFamily`,`TargetPolicy`,
                 `MpCostPolicy`,`MpCost`,`EffectReferencesJson`,`StatusReferencesJson`,`AnimationKey`,`PresentationKey`,`Enabled`,
                 `UpdatedAtUtc`)
            VALUES (@id,@run,@skill,@client,@name,@family,@target,@mpPolicy,@mp,@effects,@statuses,@animation,@presentation,0,
                    UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SkillId`=VALUES(`SkillId`),`NameZhTw`=VALUES(`NameZhTw`),`SkillFamily`=VALUES(`SkillFamily`),
                 `TargetPolicy`=VALUES(`TargetPolicy`),
                 `MpCostPolicy`=VALUES(`MpCostPolicy`),`MpCost`=VALUES(`MpCost`),
                 `EffectReferencesJson`=VALUES(`EffectReferencesJson`),`StatusReferencesJson`=VALUES(`StatusReferencesJson`),`UpdatedAtUtc`=VALUES(`UpdatedAtUtc`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            ("@id", profileId), ("@run", workspace.RunId), ("@skill", skillId), ("@client", Get<int?>(entity, "clientSkillId")),
            ("@name", Truncate(nameZhTw, 256)), ("@family", Get<string>(entity, "skillFamily") ?? "Unknown"),
            ("@target", Get<string>(entity, "targetPolicy") ?? "Unknown"),
            ("@mpPolicy", Get<string>(entity, "mpCostPolicy") ?? "Unknown"),
            ("@mp", Get<int?>(entity, "mpCost")),
            ("@effects", RecoveryJson.Compact(Get<object>(entity, "effectReferences") ?? Array.Empty<string>())),
            ("@statuses", RecoveryJson.Compact(Get<object>(entity, "statusReferences") ?? Array.Empty<string>())),
            ("@animation", Truncate(Get<string>(entity, "animationKey"), 256)), ("@presentation", Truncate(Get<string>(entity, "presentationKey"), 256)));
        if (skillId is not null)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE `skills` SET
                    `SkillFamily`=CASE WHEN (`SkillFamily` IS NULL OR `SkillFamily`='Unknown') AND @family<>'Unknown' THEN @family ELSE `SkillFamily` END,
                    `TargetPolicy`=CASE WHEN (`TargetPolicy` IS NULL OR `TargetPolicy`='Unknown') AND @target<>'Unknown' THEN @target ELSE `TargetPolicy` END,
                    `MpCostPolicy`=CASE WHEN (`MpCostPolicy` IS NULL OR `MpCostPolicy`='Unknown') AND @mpPolicy<>'Unknown' THEN @mpPolicy ELSE `MpCostPolicy` END,
                    `MpCost`=COALESCE(@mp, `MpCost`)
                WHERE `Id`=@id;
                """, cancellationToken, ("@family", Get<string>(entity, "skillFamily") ?? "Unknown"),
                ("@target", Get<string>(entity, "targetPolicy") ?? "Unknown"),
                ("@mpPolicy", Get<string>(entity, "mpCostPolicy") ?? "Unknown"), ("@mp", Get<int?>(entity, "mpCost")), ("@id", skillId));
            await ExecuteAsync(connection, transaction, """
                INSERT INTO `god2_game`.`skills`
                    (`skill_id`,`code`,`name_zh_tw`,`official_client_item_id`,`description_zh_tw`,`skill_family`,`mp_cost`,`target_type`,`enabled`)
                SELECT source_skill.`Id`,LEFT(NULLIF(source_skill.`Code`,''),100),
                    LEFT(COALESCE(NULLIF(source_skill.`NameZhTw`,''),@name,NULLIF(source_skill.`Name`,''),CONCAT('Skill ',source_skill.`Id`)),150),
                    @client,
                    source_skill.`DescriptionZhTw`,
                    CASE WHEN @family<>'Unknown' THEN @family ELSE NULL END,
                    @mp,
                    CASE WHEN @target<>'Unknown' THEN @target ELSE NULL END,
                    0
                FROM `skills` source_skill
                WHERE source_skill.`Id`=@id
                ON DUPLICATE KEY UPDATE
                    `name_zh_tw`=VALUES(`name_zh_tw`),
                    `official_client_item_id`=COALESCE(VALUES(`official_client_item_id`),`official_client_item_id`),
                    `description_zh_tw`=COALESCE(VALUES(`description_zh_tw`),`description_zh_tw`),
                    `skill_family`=COALESCE(VALUES(`skill_family`),`skill_family`),
                    `mp_cost`=COALESCE(VALUES(`mp_cost`),`mp_cost`),
                    `target_type`=COALESCE(VALUES(`target_type`),`target_type`),
                    `updated_at_utc`=UTC_TIMESTAMP(6);
                """, cancellationToken, ("@family", Get<string>(entity, "skillFamily") ?? "Unknown"),
                ("@target", Get<string>(entity, "targetPolicy") ?? "Unknown"), ("@mp", Get<int?>(entity, "mpCost")),
                ("@name", Truncate(nameZhTw, 150)), ("@client", Get<int?>(entity, "clientSkillId")), ("@id", skillId));
        }
        Increment(metrics, "skillProfilesUpserted");
        Increment(metrics, skillId is null ? "skillProfilesIdentityBlocked" : "skillProfilesResolved");
    }

    private static async Task PromoteMerchantCandidateAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        if (!Resolve(authorities, "Item", Get<string>(entity, "formalItemAuthorityKey"), out var itemId)) { Increment(metrics, "merchantCandidatesIdentityBlocked"); return; }
        const string sql = """
            INSERT INTO `god2_research`.`merchant_inventory_candidates` (`CandidateId`,`RunId`,`MerchantId`,`ClientInventoryGroupId`,`ItemId`,`BuyPrice`,`SellPrice`,`QuantityLimit`,`RefreshPolicy`,`EvidenceStatus`,`Enabled`,`SourceHash`,`SourceIdentity`,`UpdatedAtUtc`)
            VALUES (@id,@run,NULL,@group,@item,@buy,@sell,@quantity,@refresh,@evidence,0,@hash,@identity,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`ItemId`=VALUES(`ItemId`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`Enabled`=0,`UpdatedAtUtc`=VALUES(`UpdatedAtUtc`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", ContentHash.StableId("merchant-candidate", entity.AuthorityKey)), ("@run", workspace.RunId),
            ("@group", Get<int>(entity, "clientInventoryGroupId")), ("@item", itemId), ("@buy", Get<long?>(entity, "buyPrice")), ("@sell", Get<long?>(entity, "sellPrice")),
            ("@quantity", Get<int?>(entity, "quantityLimit")), ("@refresh", Get<string>(entity, "refreshPolicy") ?? "Unknown"),
            ("@evidence", NormalizeEvidence(entity.EvidenceStatus)), ("@hash", entity.SourceHash), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)));
        Increment(metrics, "merchantInventoryCandidatesUpserted");
    }

    private static async Task PromoteQuestProfileAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, IReadOnlyDictionary<(string Table, string Name), int> uniqueNames,
        ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        int? questId = Resolve(authorities, "Quest", Get<string>(entity, "formalQuestAuthorityKey"), out var resolved) ? resolved : null;
        var nameZhTw = Convert(localization, entity.Name, entity.SourceHash);
        if (questId is null && nameZhTw is not null && uniqueNames.TryGetValue(("quests", NormalizeName(nameZhTw)), out resolved)) questId = resolved;
        var profileId = ContentHash.StableId("quest-profile", entity.AuthorityKey, entity.SourceHash);
        const string sql = """
            INSERT INTO `quest_content_profiles` (`ProfileId`,`RunId`,`QuestId`,`ClientQuestId`,`NameZhTw`,`DescriptionZhTw`,`StepsJson`,`StartNpcClientId`,`EndNpcClientId`,`RewardTextZhTw`,`UpdatedAtUtc`)
            VALUES (@id,@run,@quest,@client,@name,@description,@steps,@start,@end,@reward,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`QuestId`=VALUES(`QuestId`),`NameZhTw`=VALUES(`NameZhTw`),`DescriptionZhTw`=VALUES(`DescriptionZhTw`),`StepsJson`=VALUES(`StepsJson`),`RewardTextZhTw`=VALUES(`RewardTextZhTw`),`UpdatedAtUtc`=VALUES(`UpdatedAtUtc`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", profileId), ("@run", workspace.RunId),
            ("@quest", questId), ("@client", Get<int>(entity, "clientQuestId")), ("@name", Truncate(nameZhTw ?? $"Quest {Get<int>(entity, "clientQuestId")}", 256)),
            ("@description", Convert(localization, entity.Description, entity.SourceHash)), ("@steps", RecoveryJson.Compact(Get<object>(entity, "steps") ?? Array.Empty<string>())),
            ("@start", Get<int?>(entity, "startNpcClientId")), ("@end", Get<int?>(entity, "endNpcClientId")),
            ("@reward", Convert(localization, Get<string>(entity, "rewardText"), entity.SourceHash)));
        if (questId is not null)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO `god2_game`.`quests`
                    (`quest_id`,`code`,`name_zh_tw`,`start_npc_id`,`end_npc_id`,`required_level`,`description_zh_tw`,`completion_text_zh_tw`,`enabled`)
                SELECT source_quest.`Id`,LEFT(NULLIF(source_quest.`Code`,''),100),
                    LEFT(COALESCE(NULLIF(source_quest.`NameZhTw`,''),@name,NULLIF(source_quest.`Name`,''),CONCAT('Quest ',source_quest.`Id`)),200),
                    source_quest.`StartNpcId`,source_quest.`EndNpcId`,source_quest.`RequiredLevel`,
                    COALESCE(NULLIF(source_quest.`DescriptionZhTw`,''),@description),
                    @reward,
                    0
                FROM `quests` source_quest
                WHERE source_quest.`Id`=@id
                ON DUPLICATE KEY UPDATE
                    `name_zh_tw`=VALUES(`name_zh_tw`),
                    `start_npc_id`=COALESCE(VALUES(`start_npc_id`),`start_npc_id`),
                    `end_npc_id`=COALESCE(VALUES(`end_npc_id`),`end_npc_id`),
                    `required_level`=COALESCE(VALUES(`required_level`),`required_level`),
                    `description_zh_tw`=COALESCE(VALUES(`description_zh_tw`),`description_zh_tw`),
                    `completion_text_zh_tw`=COALESCE(VALUES(`completion_text_zh_tw`),`completion_text_zh_tw`),
                    `updated_at_utc`=UTC_TIMESTAMP(6);
                """, cancellationToken, ("@id", questId), ("@name", Truncate(nameZhTw, 200)),
                ("@description", Convert(localization, entity.Description, entity.SourceHash)),
                ("@reward", Convert(localization, Get<string>(entity, "rewardText"), entity.SourceHash)));
        }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`content_profile_source_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES ('quest_content_profiles',@id,@run,@sourceType,@file,@identity,@hash,@evidence,'來源 hash 已移入 research 封存；正式任務 profile 只保留任務文字、步驟、NPC 與獎勵。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@id", profileId), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)),
            ("@hash", entity.SourceHash), ("@evidence", NormalizeEvidence(entity.EvidenceStatus)));
        Increment(metrics, "questProfilesUpserted");
    }

    private static async Task PromoteQuestObjectiveAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        int? itemId = Resolve(authorities, "Item", Get<string>(entity, "formalItemAuthorityKey"), out var item) ? item : null;
        int? monsterId = Resolve(authorities, "MonsterTemplate", Get<string>(entity, "formalMonsterAuthorityKey"), out var monster) ? monster : null;
        if (itemId is null && monsterId is null) { Increment(metrics, "questObjectivesIdentityBlocked"); return; }
        const string sql = """
            INSERT INTO `god2_research`.`quest_objective_candidates` (`ObjectiveId`,`RunId`,`ClientQuestId`,`ObjectiveType`,`ItemId`,`MonsterId`,`RequiredQuantity`,`ObjectiveTextZhTw`,`EvidenceStatus`,`Enabled`,`SourceHash`,`SourceIdentity`)
            VALUES (@id,@run,@quest,@type,@item,@monster,@quantity,@text,@evidence,0,@hash,@identity)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`ItemId`=VALUES(`ItemId`),`MonsterId`=VALUES(`MonsterId`),`RequiredQuantity`=VALUES(`RequiredQuantity`),`ObjectiveTextZhTw`=VALUES(`ObjectiveTextZhTw`),`Enabled`=0;
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", ContentHash.StableId("quest-objective", entity.AuthorityKey)), ("@run", workspace.RunId),
            ("@quest", Get<int>(entity, "clientQuestId")), ("@type", Get<string>(entity, "objectiveType") ?? "Unknown"), ("@item", itemId), ("@monster", monsterId),
            ("@quantity", Get<int?>(entity, "requiredQuantity")), ("@text", Convert(localization, Get<string>(entity, "objectiveText"), entity.SourceHash)),
            ("@evidence", NormalizeEvidence(entity.EvidenceStatus)), ("@hash", entity.SourceHash), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)));
        Increment(metrics, "questObjectiveCandidatesUpserted");
    }

    private static async Task PromoteEquipmentSetAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO `equipment_set_definitions` (`SetId`,`RunId`,`NameZhTw`,`RequiredPieces`,`EffectsZhTw`)
            VALUES (@id,@run,@name,@pieces,@effects)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`NameZhTw`=VALUES(`NameZhTw`),`RequiredPieces`=VALUES(`RequiredPieces`),`EffectsZhTw`=VALUES(`EffectsZhTw`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", Get<int>(entity, "setId")), ("@run", workspace.RunId),
            ("@name", Truncate(Convert(localization, entity.Name, entity.SourceHash) ?? $"Set {Get<int>(entity, "setId")}", 256)), ("@pieces", Get<int>(entity, "requiredPieces")),
            ("@effects", Convert(localization, Get<string>(entity, "effects"), entity.SourceHash) ?? string.Empty));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`item_sets`
                (`set_id`,`name_zh_tw`,`description_zh_tw`,`enabled`,`admin_note`)
            VALUES (@id,@name,@effects,0,'由 Phase2 equipment_set_definitions 同步；等待 Phase3 promotion 決定是否啟用')
            ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`),`description_zh_tw`=VALUES(`description_zh_tw`),`admin_note`=VALUES(`admin_note`),`updated_at_utc`=UTC_TIMESTAMP(6);
            """, cancellationToken, ("@id", Get<int>(entity, "setId")),
            ("@name", Truncate(Convert(localization, entity.Name, entity.SourceHash) ?? $"Set {Get<int>(entity, "setId")}", 256)),
            ("@effects", Convert(localization, Get<string>(entity, "effects"), entity.SourceHash) ?? string.Empty));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`content_profile_source_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES ('equipment_set_definitions',CAST(@id AS char),@run,@sourceType,@file,@identity,@hash,@evidence,'來源 hash 已移入 research 封存；正式裝備套裝表只保留套裝件數與效果。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@id", Get<int>(entity, "setId")), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)),
            ("@hash", entity.SourceHash), ("@evidence", NormalizeEvidence(entity.EvidenceStatus)));
        Increment(metrics, "equipmentSetsUpserted");
    }

    private static async Task PromoteEquipmentSetMemberAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        if (!Resolve(authorities, "Item", Get<string>(entity, "formalItemAuthorityKey"), out var itemId)) { Increment(metrics, "equipmentSetMembersIdentityBlocked"); return; }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `equipment_set_members` (`SetId`,`ItemId`,`RunId`,`SlotName`) VALUES (@set,@item,@run,@slot)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SlotName`=VALUES(`SlotName`);
            """, cancellationToken, ("@set", Get<int>(entity, "setId")), ("@item", itemId), ("@run", workspace.RunId),
            ("@slot", Get<string>(entity, "slotName") ?? "UnresolvedSlot"));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`item_set_members`
                (`set_id`,`item_id`,`slot_name`,`enabled`,`admin_note`)
            VALUES (@set,@item,@slot,0,'由 Phase2 equipment_set_members 同步；等待 Phase3 promotion 決定是否啟用')
            ON DUPLICATE KEY UPDATE `slot_name`=VALUES(`slot_name`),`admin_note`=VALUES(`admin_note`);
            """, cancellationToken, ("@set", Get<int>(entity, "setId")), ("@item", itemId),
            ("@slot", Get<string>(entity, "slotName") ?? "UnresolvedSlot"));        Increment(metrics, "equipmentSetMembersUpserted");
    }

    private static async Task PromoteContainerAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        if (!Resolve(authorities, "Item", Get<string>(entity, "containerAuthorityKey"), out var container) || !Resolve(authorities, "Item", Get<string>(entity, "containedItemAuthorityKey"), out var item))
        { Increment(metrics, "containerRelationshipsIdentityBlocked"); return; }
        const string sql = """
            INSERT INTO `container_item_relationships` (`RelationshipId`,`RunId`,`ContainerItemId`,`ContainedItemId`,`Quantity`,`DeclaredProbability`,`EffectiveProbability`,`RelationshipStatus`,`Enabled`)
            VALUES (@id,@run,@container,@item,@quantity,@original,0,@status,0)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`Quantity`=VALUES(`Quantity`),`DeclaredProbability`=VALUES(`DeclaredProbability`),`EffectiveProbability`=0,`RelationshipStatus`=VALUES(`RelationshipStatus`),`Enabled`=0;
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", ContentHash.StableId("container", entity.AuthorityKey)), ("@run", workspace.RunId),
            ("@container", container), ("@item", item), ("@quantity", Get<int?>(entity, "quantity")), ("@original", Get<decimal?>(entity, "originalProbability")),
            ("@status", Get<string>(entity, "relationshipStatus") ?? "Candidate"));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`containers`
                (`container_id`,`item_id`,`name_zh_tw`,`roll_count`,`enabled`,`admin_note`)
            SELECT @container,@container,LEFT(COALESCE(registry_row.`name_zh_tw`,CONCAT('Container ',@container)),200),NULL,0,
                '由 Phase2 container_item_relationships 同步；等待 Phase3 promotion 決定是否啟用'
            FROM `god2_game`.`item_registry` registry_row
            WHERE registry_row.`item_id`=@container
            ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`),`admin_note`=VALUES(`admin_note`);

            INSERT INTO `god2_game`.`container_rewards`
                (`container_id`,`reward_order`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,`reward_probability`,`reward_group`,`enabled`)
            SELECT @container,
                COALESCE(
                    (SELECT existing_reward.`reward_order`
                     FROM `god2_game`.`container_rewards` existing_reward
                     WHERE existing_reward.`container_id`=@container AND existing_reward.`item_id`=@item
                     ORDER BY existing_reward.`reward_order`
                     LIMIT 1),
                    (SELECT COALESCE(MAX(next_reward.`reward_order`),0)+1
                     FROM `god2_game`.`container_rewards` next_reward
                     WHERE next_reward.`container_id`=@container)),
                @item,
                LEFT(COALESCE(registry_row.`name_zh_tw`,CONCAT('Item ',@item)),200),
                @quantity,
                @quantity,
                CASE
                    WHEN @original IS NULL THEN NULL
                    WHEN @original > 9999.99999999 THEN 9999.99999999
                    ELSE @original
                END,
                @status,
                0
            FROM `god2_game`.`item_registry` registry_row
            WHERE registry_row.`item_id`=@item
            ON DUPLICATE KEY UPDATE
                `item_id`=VALUES(`item_id`),
                `item_name_cache`=VALUES(`item_name_cache`),
                `minimum_quantity`=VALUES(`minimum_quantity`),
                `maximum_quantity`=VALUES(`maximum_quantity`),
                `reward_probability`=VALUES(`reward_probability`),
                `reward_group`=VALUES(`reward_group`);
            """, cancellationToken, ("@container", container), ("@item", item),
            ("@quantity", Get<int?>(entity, "quantity")), ("@original", Get<decimal?>(entity, "originalProbability")),
            ("@status", Get<string>(entity, "relationshipStatus") ?? "Candidate"));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`relationship_source_archive`
                (`FormalTable`,`RelationshipId`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`Confidence`,`ArchivedAtUtc`)
            VALUES ('container_item_relationships',@id,@run,@sourceType,@file,@identity,@hash,@confidence,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`Confidence`=VALUES(`Confidence`),
                `ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken,
            ("@id", ContentHash.StableId("container", entity.AuthorityKey)), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)),
            ("@hash", entity.SourceHash), ("@confidence", Truncate(entity.Confidence, 32)));
        Increment(metrics, "containerRelationshipsUpserted");
    }

    private static async Task PromotePetProfileAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        IReadOnlyDictionary<(string Domain, string Authority), int> authorities, ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        int? itemId = Resolve(authorities, "Item", Get<string>(entity, "formalItemAuthorityKey"), out var item) ? item : null;
        var profileId = ContentHash.StableId("pet-profile", entity.AuthorityKey, entity.SourceHash);
        const string sql = """
            INSERT INTO `pet_content_profiles` (`ProfileId`,`RunId`,`ClientPetId`,`ItemId`,`NameZhTw`,`PetFamily`,`GrowthType`,`BaseStatsJson`,`SkillReferencesJson`,`EvolutionReferencesJson`)
            VALUES (@id,@run,@client,@item,@name,@family,@growth,@stats,@skills,@evolution)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`ItemId`=VALUES(`ItemId`),`NameZhTw`=VALUES(`NameZhTw`),`PetFamily`=VALUES(`PetFamily`),`GrowthType`=VALUES(`GrowthType`),`BaseStatsJson`=VALUES(`BaseStatsJson`),`SkillReferencesJson`=VALUES(`SkillReferencesJson`),`EvolutionReferencesJson`=VALUES(`EvolutionReferencesJson`);
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken, ("@id", profileId), ("@run", workspace.RunId),
            ("@client", Get<int>(entity, "clientPetId")), ("@item", itemId), ("@name", Truncate(Convert(localization, entity.Name, entity.SourceHash) ?? $"Pet {Get<int>(entity, "clientPetId")}", 256)),
            ("@family", Get<string>(entity, "petFamily") ?? "Unknown"), ("@growth", Get<string>(entity, "growthType")),
            ("@stats", RecoveryJson.Compact(Get<object>(entity, "baseStats") ?? new { })), ("@skills", RecoveryJson.Compact(Get<object>(entity, "skillReferences") ?? Array.Empty<string>())),
            ("@evolution", RecoveryJson.Compact(Get<object>(entity, "evolutionReferences") ?? Array.Empty<string>())));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`content_profile_source_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES ('pet_content_profiles',@id,@run,@sourceType,@file,@identity,@hash,@evidence,'來源 hash 已移入 research 封存；正式寵物 profile 只保留寵物資料與服務端規則。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@id", profileId), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)),
            ("@hash", entity.SourceHash), ("@evidence", NormalizeEvidence(entity.EvidenceStatus)));
        Increment(metrics, "petProfilesUpserted");
    }

    private static async Task PromotePetInnateAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        ZhTwLocalization localization, Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `pet_innate_definitions` (`InnateId`,`RunId`,`NameZhTw`,`ArtifactNameZhTw`,`DescriptionZhTw`,`EffectReference`,`EffectValue`)
            VALUES (@id,@run,@name,@artifact,@description,@effect,@value)
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`NameZhTw`=VALUES(`NameZhTw`),`ArtifactNameZhTw`=VALUES(`ArtifactNameZhTw`),`DescriptionZhTw`=VALUES(`DescriptionZhTw`),`EffectReference`=VALUES(`EffectReference`),`EffectValue`=VALUES(`EffectValue`);
            """, cancellationToken, ("@id", Get<int>(entity, "innateId")), ("@run", workspace.RunId), ("@name", Truncate(Convert(localization, entity.Name, entity.SourceHash), 256)),
            ("@artifact", Truncate(Convert(localization, Get<string>(entity, "artifactName"), entity.SourceHash), 256)),
            ("@description", Convert(localization, entity.Description, entity.SourceHash)), ("@effect", Truncate(Get<string>(entity, "effectReference"), 256)),
            ("@value", Get<int?>(entity, "effectValue")));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_game`.`pet_innate_definitions`
                (`innate_id`,`name_zh_tw`,`artifact_name_zh_tw`,`description_zh_tw`,`effect_reference`,`effect_value`,`effect_evidence_status`,`enabled`)
            VALUES (@id,@name,@artifact,@description,@effect,@value,
                CASE
                    WHEN @effect IS NULL AND @value IS NULL THEN 'NotApplicable'
                    WHEN @effect IS NOT NULL AND @value IS NOT NULL THEN 'Verified'
                    ELSE 'EvidenceBlocked'
                END,
                0)
            ON DUPLICATE KEY UPDATE
                `name_zh_tw`=VALUES(`name_zh_tw`),
                `artifact_name_zh_tw`=VALUES(`artifact_name_zh_tw`),
                `description_zh_tw`=VALUES(`description_zh_tw`),
                `effect_reference`=VALUES(`effect_reference`),
                `effect_value`=VALUES(`effect_value`),
                `effect_evidence_status`=VALUES(`effect_evidence_status`),
                `updated_at_utc`=UTC_TIMESTAMP(6);
            """, cancellationToken, ("@id", Get<int>(entity, "innateId")),
            ("@name", Truncate(Convert(localization, entity.Name, entity.SourceHash), 150)),
            ("@artifact", Truncate(Convert(localization, Get<string>(entity, "artifactName"), entity.SourceHash), 150)),
            ("@description", Convert(localization, entity.Description, entity.SourceHash)), ("@effect", Truncate(Get<string>(entity, "effectReference"), 150)),
            ("@value", Get<int?>(entity, "effectValue")));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`content_profile_source_archive`
                (`FormalTable`,`RecordIdentity`,`RunId`,`SourceType`,`SourceFile`,`SourceIdentity`,`SourceHash`,`EvidenceStatus`,`ArchiveReasonZhTw`,`ArchivedAtUtc`)
            VALUES ('pet_innate_definitions',CAST(@id AS char),@run,@sourceType,@file,@identity,@hash,@evidence,'來源 hash 已移入 research 封存；正式寵物天賦表只保留天賦名稱、描述與效果。',UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`SourceType`=VALUES(`SourceType`),`SourceFile`=VALUES(`SourceFile`),
                `SourceIdentity`=VALUES(`SourceIdentity`),`SourceHash`=VALUES(`SourceHash`),`EvidenceStatus`=VALUES(`EvidenceStatus`),
                `ArchiveReasonZhTw`=VALUES(`ArchiveReasonZhTw`),`ArchivedAtUtc`=VALUES(`ArchivedAtUtc`);
            """, cancellationToken, ("@id", Get<int>(entity, "innateId")), ("@run", workspace.RunId), ("@sourceType", SourceType(entity.SourceFile)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@identity", RecoveryDatabaseIdentity.ForStorage(entity.SourceIdentity)),
            ("@hash", entity.SourceHash), ("@evidence", NormalizeEvidence(entity.EvidenceStatus)));
        Increment(metrics, "petInnatesUpserted");
    }

    private static async Task PromoteHistoricalObservationAsync(MySqlConnection connection, MySqlTransaction transaction, RecoveryWorkspace workspace, ContentEntity entity,
        Dictionary<string, long> metrics, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO `god2_research`.`historical_gameplay_observations` (`ObservationId`,`RunId`,`EventName`,`ObservedAtUtc`,`GameplayDataJson`,`EvidenceStatus`,`SourceFile`,`SourceHash`,`RecordedAtUtc`)
            VALUES (@id,@run,@event,@observed,@data,@evidence,@file,@hash,UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `RunId`=VALUES(`RunId`),`GameplayDataJson`=VALUES(`GameplayDataJson`),`EvidenceStatus`=VALUES(`EvidenceStatus`),`RecordedAtUtc`=VALUES(`RecordedAtUtc`);
            """, cancellationToken, ("@id", ContentHash.StableId("historical", entity.AuthorityKey)), ("@run", workspace.RunId),
            ("@event", Truncate(Get<string>(entity, "eventName") ?? "Unknown", 128)), ("@observed", Get<DateTime?>(entity, "observedAtUtc")),
            ("@data", RecoveryJson.Compact(Get<object>(entity, "gameplayData") ?? new { })), ("@evidence", NormalizeEvidence(entity.EvidenceStatus)),
            ("@file", Truncate(entity.SourceFile, 768)), ("@hash", entity.SourceHash));
        Increment(metrics, "historicalObservationsUpserted");
    }

    private static async Task<Dictionary<(string Domain, string Authority), int>> ReadAuthorityMapAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `Domain`,`AuthorityKey`,`TargetRowIdentity` FROM `god2_research`.`content_production_manifest`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (int.TryParse(reader.GetString(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                result[(reader.GetString(0), reader.GetString(1))] = id;
            }
        }
        return result;
    }

    private static async Task ReconcileCurrentSnapshotAsync(MySqlConnection connection, MySqlTransaction transaction, string runId, CancellationToken cancellationToken)
    {
        foreach (var table in new[]
        {
            "`pet_egg_relationships`",
            "`equipment_set_members`",
            "`god2_research`.`historical_gameplay_observations`",
            "`pet_innate_definitions`",
            "`pet_content_profiles`",
            "`container_item_relationships`",
            "`equipment_set_definitions`",
            "`god2_research`.`quest_objective_candidates`",
            "`quest_content_profiles`",
            "`god2_research`.`merchant_inventory_candidates`",
            "`skill_content_profiles`",
            "`god2_research`.`npc_coordinate_evidence`",
            "`monster_drop_relationships`",
            "`item_content_profiles`"
        })
        {
            await ExecuteAsync(connection, transaction, $"DELETE FROM {table} WHERE `RunId`<>@run;", cancellationToken, ("@run", runId));
        }
    }

    private static async Task<Dictionary<int, int>> ReadDropTablesAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `Id`,`MonsterId` FROM `drop_tables` WHERE `MonsterId` IS NOT NULL;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetInt32(1)] = reader.GetInt32(0);
        return result;
    }

    private static async Task<Dictionary<(string Table, string Name), int>> ReadUniqueNamesAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        var candidates = new List<(string Table, string Name, int Id)>();
        foreach (var table in new[] { "maps", "npcs", "skills", "quests" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT `Id`,`NameZhTw` FROM `{table}` WHERE `NameZhTw` IS NOT NULL AND `NameZhTw`<>'';";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) candidates.Add((table, NormalizeName(reader.GetString(1)), reader.GetInt32(0)));
        }
        return candidates.GroupBy(row => (row.Table, row.Name)).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Id);
    }

    private static bool Resolve(IReadOnlyDictionary<(string Domain, string Authority), int> map, string domain, string? authority, out int id)
    {
        if (authority is not null && map.TryGetValue((domain, authority), out id)) return true;
        id = 0;
        return false;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static T? Get<T>(ContentEntity entity, string key)
    {
        if (!entity.Fields.TryGetValue(key, out var value) || value is null) return default;
        if (value is T typed) return typed;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try { return (T?)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture); }
        catch { return default; }
    }

    private static string FieldState(ContentEntity entity, string field, object? value)
    {
        if (field.EndsWith("EvidenceStatus", StringComparison.OrdinalIgnoreCase) && value is string explicitState) return NormalizeState(explicitState);
        if (value is null) return "EvidenceBlocked";
        if (value is string text && text is "Unknown" or "EvidenceBlocked") return "EvidenceBlocked";
        return NormalizeState(entity.EvidenceStatus);
    }

    private static string NormalizeState(string value) => value switch
    {
        "Verified" => "Verified",
        "Derived" => "Derived",
        "Candidate" => "Candidate",
        "EvidenceBlocked" or "PartiallyMapped" => "EvidenceBlocked",
        "DefaultDisabledZero" => "DefaultDisabledZero",
        "ExplicitOfficialZero" => "ExplicitOfficialZero",
        "NotApplicable" => "NotApplicable",
        "Deprecated" => "Deprecated",
        _ => "Candidate"
    };
    private static string NormalizeEvidence(string value) => value == "PartiallyMapped" ? "EvidenceBlocked" : value;
    private static string SourceType(string file) => RecoverySourceClassification.FromFile(file);
    private static string? Convert(ZhTwLocalization localization, string? value, string hash) => string.IsNullOrWhiteSpace(value) ? null : localization.Convert(value, hash).ConvertedText;
    private static void Increment(Dictionary<string, long> metrics, string key) => metrics[key] = metrics.GetValueOrDefault(key) + 1;
    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
    private static string NormalizeName(string value) => new(value.Normalize().Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character)).ToArray());
}






