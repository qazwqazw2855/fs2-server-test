-- Conservative initial canonical catalog build.
-- Existing production tables are copied without modifying them. Unknown semantic
-- values remain NULL and Candidate/Derived/EvidenceBlocked rows are not enabled.

SET @god2_sync_mode = 1;

INSERT INTO `god2_game_meta`.`sync_runs`
    (`sync_run_id`,`started_at_utc`,`completed_at_utc`,`status`,`source_watermark`,`mapped_value_count`,`blocked_candidate_count`,`conflict_count`,`unmapped_count`)
VALUES
    ('00000000-0000-0000-0000-000000000053',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),'Completed','initial-migration-053',0,0,0,0)
ON DUPLICATE KEY UPDATE `status`=VALUES(`status`),`completed_at_utc`=VALUES(`completed_at_utc`);

INSERT INTO `god2_game`.`life_skills`
    (`life_skill_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`maximum_level`,`enabled`,`admin_note`)
VALUES
    (1,'ArmorForging','防具鍛造',NULL,'製作防具、護具、防禦型裝備，以及防具強化與修理。',NULL,1,'固定生活技能 Seed'),
    (2,'WeaponForging','武器鍛造',NULL,'製作、強化與修理武器。',NULL,1,'固定生活技能 Seed'),
    (3,'PillAlchemy','丹藥鍛造',NULL,'製作丹藥、藥品、回復品與 Buff 消耗品。',NULL,1,'固定生活技能 Seed'),
    (4,'MagicTreasureForging','法寶鍛造',NULL,'製作法寶、符器、Artifact 與魔法型裝備。',NULL,1,'固定生活技能 Seed')
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`),
    `enabled`=VALUES(`enabled`);

INSERT INTO `god2_game`.`battle_rule_parameters`
    (`parameter_key`,`display_name_zh_tw`,`numeric_value`,`text_value`,`description_zh_tw`,`evidence_status`,`enabled`,`admin_note`)
VALUES
    ('turn_order_rule','回合出手順序規則',NULL,NULL,'尚未確認速度與出手順序的正式公式。','EvidenceBlocked',0,'等待正式證據'),
    ('speed_tie_rule','同速度決勝規則',NULL,NULL,'尚未確認速度相同時的決勝規則。','EvidenceBlocked',0,'等待正式證據'),
    ('action_priority_rule','行動優先規則',NULL,NULL,'尚未確認技能與一般行動優先規則。','EvidenceBlocked',0,'等待正式證據'),
    ('round_start_rule','回合開始規則',NULL,NULL,'尚未確認回合開始處理順序。','EvidenceBlocked',0,'等待正式證據'),
    ('round_end_rule','回合結束規則',NULL,NULL,'尚未確認回合結束處理順序。','EvidenceBlocked',0,'等待正式證據'),
    ('dead_participant_rule','死亡參與者規則',NULL,NULL,'尚未確認死亡參與者後續規則。','EvidenceBlocked',0,'等待正式證據'),
    ('summon_rule','召喚規則',NULL,NULL,'尚未確認召喚規則。','EvidenceBlocked',0,'等待正式證據'),
    ('flee_rule','逃跑規則',NULL,NULL,'尚未確認逃跑公式。','EvidenceBlocked',0,'等待正式證據'),
    ('critical_rule','暴擊規則',NULL,NULL,'尚未確認暴擊公式。','EvidenceBlocked',0,'等待正式證據'),
    ('hit_rule','命中規則',NULL,NULL,'尚未確認命中公式。','EvidenceBlocked',0,'等待正式證據'),
    ('element_rule','五行規則',NULL,NULL,'尚未確認五行相剋公式。','EvidenceBlocked',0,'等待正式證據')
ON DUPLICATE KEY UPDATE
    `display_name_zh_tw`=VALUES(`display_name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`);

INSERT INTO `god2_game`.`server_rates`
    (`rate_key`,`display_name_zh_tw`,`rate_value`,`description_zh_tw`,`enabled`,`admin_note`)
VALUES
    ('experience_rate','全域經驗倍率',NULL,'全域經驗倍率；正式值未知時保持 NULL 並停用。',0,'等待服主設定或正式證據'),
    ('drop_rate','全域掉落倍率',NULL,'全域掉落倍率；正式值未知時保持 NULL 並停用。',0,'等待服主設定或正式證據'),
    ('currency_rate','全域貨幣倍率',NULL,'全域貨幣倍率；正式值未知時保持 NULL 並停用。',0,'等待服主設定或正式證據')
ON DUPLICATE KEY UPDATE
    `display_name_zh_tw`=VALUES(`display_name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`);

INSERT INTO `god2_game`.`items`
    (`item_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`item_category`,`item_family`,
     `maximum_stack`,`buy_price`,`sell_price`,`stackable`,`usable`,`equippable`,`evidence_status`,`enabled`,`admin_note`,
     `created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,NULLIF(source_row.`Code`,''),
    COALESCE(NULLIF(source_row.`NameZhTw`,''),NULLIF(source_row.`DisplayName`,''),source_row.`Name`),
    NULLIF(source_row.`OriginalName`,''),source_row.`DescriptionZhTw`,
    COALESCE(NULLIF(source_row.`ItemCategory`,''),'Unknown'),NULLIF(source_row.`ItemFamily`,''),
    source_row.`MaxStack`,NULLIF(source_row.`BaseBuyPrice`,0),source_row.`SellPrice`,
    CASE WHEN source_row.`MaxStack` IS NULL THEN NULL WHEN source_row.`MaxStack` > 1 THEN 1 ELSE 0 END,
    CASE WHEN NULLIF(source_row.`ConsumableCategory`,'') IS NULL THEN NULL ELSE 1 END,
    CASE WHEN NULLIF(source_row.`EquipmentCategory`,'') IS NULL THEN NULL ELSE 1 END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered') AND source_row.`Enabled`=1 THEN 1 ELSE 0 END,
    CASE WHEN source_row.`Enabled`=1 AND source_row.`EvidenceStatus` NOT IN ('Verified','Recovered') THEN '舊表曾啟用，但未達 Canonical Production Gate，已安全停用。' ELSE NULL END,
    COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`UpdatedAtUtc`,source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`items` source_row
ON DUPLICATE KEY UPDATE
    `code`=COALESCE(`god2_game`.`items`.`code`,VALUES(`code`)),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=COALESCE(`god2_game`.`items`.`name_original`,VALUES(`name_original`)),
    `description_zh_tw`=COALESCE(`god2_game`.`items`.`description_zh_tw`,VALUES(`description_zh_tw`)),
    `item_category`=VALUES(`item_category`),
    `item_family`=COALESCE(`god2_game`.`items`.`item_family`,VALUES(`item_family`));

INSERT INTO `god2_game`.`equipment`
    (`item_id`,`equipment_type`,`equipment_slot`,`enabled`,`admin_note`)
SELECT source_row.`Id`,NULLIF(source_row.`EquipmentCategory`,''),NULL,0,
       '來源只證明裝備分類，裝備能力與欄位位置尚未驗證。'
FROM `god2`.`items` source_row
WHERE NULLIF(source_row.`EquipmentCategory`,'') IS NOT NULL
ON DUPLICATE KEY UPDATE `equipment_type`=COALESCE(`god2_game`.`equipment`.`equipment_type`,VALUES(`equipment_type`));

INSERT INTO `god2_game`.`maps`
    (`map_id`,`code`,`name_zh_tw`,`name_original`,`resource_identity`,`width`,`height`,
     `client_build_id`,`client_map_id`,`client_area_id`,`coordinate_scale_x`,`coordinate_scale_y`,
     `coordinate_offset_x`,`coordinate_offset_y`,`identity_evidence_status`,`coordinate_evidence_status`,
     `enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,NULLIF(source_row.`Code`,''),
    COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),NULLIF(source_row.`OriginalName`,''),
    identity_row.`ResourceIdentity`,source_row.`Width`,source_row.`Height`,
    identity_row.`ClientBuildId`,identity_row.`ClientMapId`,identity_row.`ClientAreaId`,
    identity_row.`CoordinateScaleX`,identity_row.`CoordinateScaleY`,identity_row.`CoordinateOffsetX`,identity_row.`CoordinateOffsetY`,
    COALESCE(identity_row.`IdentityEvidenceStatus`,source_row.`EvidenceStatus`,'Unknown'),
    COALESCE(identity_row.`CoordinateEvidenceStatus`,'Unknown'),
    CASE WHEN identity_row.`ProductionEnabled`=1 AND identity_row.`IdentityEvidenceStatus`='Verified' AND identity_row.`CoordinateEvidenceStatus`='Verified' THEN 1 ELSE 0 END,
    CASE WHEN identity_row.`ProductionEnabled`=1 THEN NULL ELSE '未達地圖 Production Gate，已安全停用。' END,
    source_row.`CreatedAtUtc`,source_row.`UpdatedAtUtc`
FROM `god2`.`maps` source_row
LEFT JOIN `god2`.`client_map_identities` identity_row ON identity_row.`MapId`=source_row.`Id` AND identity_row.`ProductionEnabled`=1
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=COALESCE(`god2_game`.`maps`.`name_original`,VALUES(`name_original`)),
    `client_build_id`=COALESCE(`god2_game`.`maps`.`client_build_id`,VALUES(`client_build_id`)),
    `client_map_id`=COALESCE(`god2_game`.`maps`.`client_map_id`,VALUES(`client_map_id`)),
    `client_area_id`=COALESCE(`god2_game`.`maps`.`client_area_id`,VALUES(`client_area_id`));

INSERT INTO `god2_game`.`portals`
    (`portal_id`,`name_zh_tw`,`source_map_id`,`source_x`,`source_y`,`destination_map_id`,`destination_x`,`destination_y`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT source_row.`Id`,source_row.`Name`,source_row.`SourceMapId`,source_row.`SourceX`,source_row.`SourceY`,
       source_row.`TargetMapId`,source_row.`TargetX`,source_row.`TargetY`,
       CASE WHEN source_row.`RecoveryStatus` IN ('Verified','Recovered') AND source_row.`SourceMapId` IS NOT NULL AND source_row.`TargetMapId` IS NOT NULL THEN 1 ELSE 0 END,
       CASE WHEN source_row.`RecoveryStatus` NOT IN ('Verified','Recovered') THEN '未達傳送門 Production Gate，已安全停用。' ELSE NULL END,
       COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`portals` source_row
ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`);

INSERT INTO `god2_game`.`monsters`
    (`monster_id`,`code`,`name_zh_tw`,`name_original`,`level`,`max_hp`,`max_mp`,`physical_attack`,`physical_defense`,
     `experience_reward`,`currency_reward`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,NULLIF(source_row.`Code`,''),COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),
    NULLIF(source_row.`OriginalName`,''),source_row.`Level`,source_row.`MaxHp`,source_row.`MaxMp`,source_row.`Attack`,source_row.`Defense`,
    source_row.`ExperienceReward`,
    CASE WHEN source_row.`CurrencyRewardMinimum` <=> source_row.`CurrencyRewardMaximum` THEN source_row.`CurrencyRewardMinimum` ELSE NULL END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
    CASE WHEN source_row.`EvidenceStatus` NOT IN ('Verified','Recovered') THEN '未達怪物 Production Gate，保留可讀列但停用。' ELSE NULL END,
    COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`monsters` source_row
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=COALESCE(`god2_game`.`monsters`.`name_original`,VALUES(`name_original`));

INSERT INTO `god2_game`.`monster_spawns`
    (`spawn_id`,`monster_id`,`monster_name_cache`,`map_id`,`map_name_cache`,`position_x`,`position_y`,`spawn_count`,
     `spawn_radius`,`respawn_seconds_min`,`respawn_seconds_max`,`evidence_status`,`enabled`,`admin_note`)
SELECT source_row.`Id`,source_row.`MonsterId`,monster_row.`name_zh_tw`,source_row.`MapId`,map_row.`name_zh_tw`,
       source_row.`PositionX`,source_row.`PositionY`,source_row.`SpawnCount`,source_row.`SpawnRadius`,
       source_row.`RespawnSeconds`,source_row.`RespawnSeconds`,source_row.`EvidenceStatus`,source_row.`ProductionEnabled`,
       CASE WHEN source_row.`ProductionEnabled`=0 THEN '舊出生點未通過 Production Gate。' ELSE NULL END
FROM `god2`.`spawns` source_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=source_row.`MonsterId`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=source_row.`MapId`
ON DUPLICATE KEY UPDATE
    `monster_name_cache`=VALUES(`monster_name_cache`),
    `map_name_cache`=VALUES(`map_name_cache`);

INSERT INTO `god2_game`.`monster_drops`
    (`drop_id`,`monster_id`,`monster_name_cache`,`item_id`,`item_name_cache`,`minimum_quantity`,`maximum_quantity`,
     `drop_rate`,`drop_rate_unit`,`drop_group`,`is_guaranteed`,`evidence_status`,`enabled`,`admin_note`)
SELECT
    ROW_NUMBER() OVER (ORDER BY source_row.`RelationshipId`),source_row.`MonsterId`,monster_row.`name_zh_tw`,
    source_row.`ItemId`,item_row.`name_zh_tw`,source_row.`MinimumQuantity`,source_row.`MaximumQuantity`,
    CASE WHEN source_row.`ChanceEvidenceStatus` IN ('Verified','Recovered','ExplicitOfficialZero') THEN source_row.`OriginalDropChance` ELSE NULL END,
    CASE WHEN source_row.`ChanceEvidenceStatus` IN ('Verified','Recovered','ExplicitOfficialZero') THEN 'Probability' ELSE 'Unknown' END,
    source_row.`DropGroupId`,source_row.`Guaranteed`,
    CASE WHEN source_row.`DropRelationshipStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`DropRelationshipStatus` ELSE 'Unknown' END,
    CASE WHEN source_row.`ProductionDropEnabled`=1 AND source_row.`DropRelationshipStatus` IN ('Verified','Recovered') AND source_row.`ChanceEvidenceStatus` IN ('Verified','Recovered','ExplicitOfficialZero') THEN 1 ELSE 0 END,
    CASE WHEN source_row.`ProductionDropEnabled`=0 OR source_row.`DropRelationshipStatus` NOT IN ('Verified','Recovered') THEN '未達掉落 Production Gate，未知掉率保持 NULL。' ELSE NULL END
FROM `god2`.`monster_drop_relationships` source_row
JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=source_row.`MonsterId`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=source_row.`ItemId`
ON DUPLICATE KEY UPDATE
    `monster_name_cache`=VALUES(`monster_name_cache`),
    `item_name_cache`=VALUES(`item_name_cache`);

INSERT INTO `god2_game`.`npcs`
    (`npc_id`,`code`,`name_zh_tw`,`name_original`,`npc_type`,`resource_key`,`interaction_family`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,NULLIF(source_row.`Code`,''),COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),NULLIF(source_row.`OriginalName`,''),
    COALESCE(NULLIF(source_row.`NpcType`,''),'Unknown'),identity_row.`ResourceKey`,NULLIF(source_row.`InteractionFamily`,''),
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
    CASE WHEN EXISTS (SELECT 1 FROM `god2`.`npc_spawns` production_spawn WHERE production_spawn.`NpcId`=source_row.`Id` AND production_spawn.`ProductionEnabled`=1) THEN 1 ELSE 0 END,
    CASE WHEN NOT EXISTS (SELECT 1 FROM `god2`.`npc_spawns` production_spawn WHERE production_spawn.`NpcId`=source_row.`Id` AND production_spawn.`ProductionEnabled`=1) THEN 'NPC 模板尚無通過 Gate 的正式出生點，已安全停用。' ELSE NULL END,
    COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`npcs` source_row
LEFT JOIN `god2`.`npc_client_identities` identity_row ON identity_row.`NpcId`=source_row.`Id`
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `resource_key`=COALESCE(`god2_game`.`npcs`.`resource_key`,VALUES(`resource_key`)),
    `enabled`=GREATEST(`god2_game`.`npcs`.`enabled`,VALUES(`enabled`));

INSERT INTO `god2_game`.`npc_spawns`
    (`spawn_id`,`npc_id`,`npc_name_cache`,`map_id`,`map_name_cache`,`position_x`,`position_y`,`direction`,`client_build_id`,
     `observed_client_entity_handle`,`official_resource_type`,`official_resource_ordinal`,`official_selector_high_bits`,
     `official_direction_code`,`official_state_code`,`wire_evidence_status`,`identity_evidence_status`,
     `coordinate_evidence_status`,`service_evidence_status`,`application_message_sha256`,`opaque_template_sha256`,`enabled`,`admin_note`,
     `created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,source_row.`NpcId`,npc_row.`name_zh_tw`,source_row.`MapId`,map_row.`name_zh_tw`,
    source_row.`PositionX`,source_row.`PositionY`,source_row.`Direction`,source_row.`ClientBuildId`,source_row.`ObservedClientEntityHandle`,
    source_row.`OfficialResourceType`,source_row.`OfficialResourceOrdinal`,source_row.`OfficialSelectorHighBits`,
    source_row.`OfficialDirectionCode`,source_row.`OfficialStateCode`,source_row.`WireEvidenceStatus`,source_row.`IdentityEvidenceStatus`,
    source_row.`CoordinateEvidenceStatus`,source_row.`ServiceEvidenceStatus`,source_row.`ApplicationMessageSha256`,source_row.`OpaqueTemplateSha256`,
    source_row.`ProductionEnabled`,CASE WHEN source_row.`ProductionEnabled`=0 THEN '舊 NPC 出生點未通過 Production Gate。' ELSE NULL END,
    source_row.`CreatedAtUtc`,source_row.`UpdatedAtUtc`
FROM `god2`.`npc_spawns` source_row
JOIN `god2_game`.`npcs` npc_row ON npc_row.`npc_id`=source_row.`NpcId`
JOIN `god2_game`.`maps` map_row ON map_row.`map_id`=source_row.`MapId`
ON DUPLICATE KEY UPDATE
    `npc_name_cache`=VALUES(`npc_name_cache`),
    `map_name_cache`=VALUES(`map_name_cache`),
    `enabled`=VALUES(`enabled`);

INSERT INTO `god2_game`.`npc_dialogs`
    (`dialog_id`,`code`,`npc_id`,`body_zh_tw`,`dialog_type`,`enabled`,`admin_note`)
SELECT source_row.`Id`,NULLIF(source_row.`Code`,''),source_row.`NpcId`,NULL,NULL,
       CASE WHEN source_row.`RecoveryStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
       CONCAT('舊 TextKey：',COALESCE(source_row.`TextKey`,'NULL'),
              CASE WHEN source_row.`RecoveryStatus` NOT IN ('Verified','Recovered') THEN '；未達對話 Production Gate。' ELSE '' END)
FROM `god2`.`dialogs` source_row
WHERE source_row.`NpcId` IS NULL OR EXISTS (SELECT 1 FROM `god2_game`.`npcs` target_npc WHERE target_npc.`npc_id`=source_row.`NpcId`)
ON DUPLICATE KEY UPDATE `code`=COALESCE(`god2_game`.`npc_dialogs`.`code`,VALUES(`code`));

INSERT INTO `god2_game`.`merchants`
    (`merchant_id`,`npc_id`,`name_zh_tw`,`merchant_type`,`buyback_enabled`,`enabled`,`admin_note`)
SELECT source_row.`Id`,source_row.`NpcId`,COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),NULL,NULL,
       CASE WHEN EXISTS (SELECT 1 FROM `god2`.`merchant_inventory_candidates` inventory_row WHERE inventory_row.`MerchantId`=source_row.`Id` AND inventory_row.`ProductionSaleEnabled`=1) THEN 1 ELSE 0 END,
       CASE WHEN NOT EXISTS (SELECT 1 FROM `god2`.`merchant_inventory_candidates` inventory_row WHERE inventory_row.`MerchantId`=source_row.`Id` AND inventory_row.`ProductionSaleEnabled`=1) THEN '商店尚無通過 Gate 的正式庫存，已安全停用。' ELSE NULL END
FROM `god2`.`merchants` source_row
WHERE source_row.`NpcId` IS NULL OR EXISTS (SELECT 1 FROM `god2_game`.`npcs` target_npc WHERE target_npc.`npc_id`=source_row.`NpcId`)
ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`);

INSERT INTO `god2_game`.`merchant_inventory`
    (`merchant_inventory_id`,`merchant_id`,`merchant_name_cache`,`item_id`,`item_name_cache`,`display_order`,
     `selling_price`,`purchasing_price`,`quantity_limit`,`evidence_status`,`enabled`,`admin_note`)
SELECT ROW_NUMBER() OVER (ORDER BY source_row.`CandidateId`),source_row.`MerchantId`,merchant_row.`name_zh_tw`,
       source_row.`ItemId`,item_row.`name_zh_tw`,NULL,source_row.`BuyPrice`,source_row.`SellPrice`,source_row.`QuantityLimit`,
       CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
       CASE WHEN source_row.`ProductionSaleEnabled`=1 AND source_row.`RelationshipEvidenceStatus` IN ('Verified','Recovered') AND source_row.`PriceEvidenceStatus` IN ('Verified','Recovered','NotApplicable') THEN 1 ELSE 0 END,
       CASE WHEN source_row.`ProductionSaleEnabled`=0 THEN '未達商店庫存 Production Gate，已安全停用。' ELSE NULL END
FROM `god2`.`merchant_inventory_candidates` source_row
JOIN `god2_game`.`merchants` merchant_row ON merchant_row.`merchant_id`=source_row.`MerchantId`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=source_row.`ItemId`
ON DUPLICATE KEY UPDATE
    `merchant_name_cache`=VALUES(`merchant_name_cache`),
    `item_name_cache`=VALUES(`item_name_cache`);

UPDATE `god2_game`.`npcs` npc_row
JOIN `god2_game`.`merchants` merchant_row ON merchant_row.`npc_id`=npc_row.`npc_id`
SET npc_row.`merchant_id`=merchant_row.`merchant_id`
WHERE npc_row.`merchant_id` IS NULL;

INSERT INTO `god2_game`.`skills`
    (`skill_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`skill_family`,`required_level`,`maximum_level`,`mp_cost`,
     `target_type`,`evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT
    source_row.`Id`,NULLIF(source_row.`Code`,''),COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),
    NULLIF(source_row.`OriginalName`,''),source_row.`DescriptionZhTw`,NULLIF(source_row.`SkillFamily`,''),
    source_row.`RequiredLevel`,source_row.`MaxLevel`,
    CASE WHEN source_row.`MpCostEvidenceStatus` IN ('Verified','Recovered','ExplicitOfficialZero') THEN source_row.`MpCost` ELSE NULL END,
    CASE WHEN source_row.`TargetPolicyEvidenceStatus` IN ('Verified','Recovered') THEN source_row.`TargetPolicy` ELSE NULL END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
    CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered') AND source_row.`SkillFamilyEvidenceStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
    CASE WHEN source_row.`EvidenceStatus` NOT IN ('Verified','Recovered') THEN '未達技能 Production Gate，未知 MP／回合冷卻保持 NULL。' ELSE NULL END,
    COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`skills` source_row
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=COALESCE(`god2_game`.`skills`.`name_original`,VALUES(`name_original`)),
    `description_zh_tw`=COALESCE(`god2_game`.`skills`.`description_zh_tw`,VALUES(`description_zh_tw`));

INSERT INTO `god2_game`.`quests`
    (`quest_id`,`code`,`name_zh_tw`,`name_original`,`start_npc_id`,`end_npc_id`,`required_level`,`description_zh_tw`,
     `evidence_status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`)
SELECT source_row.`Id`,NULLIF(source_row.`Code`,''),COALESCE(NULLIF(source_row.`NameZhTw`,''),source_row.`Name`),
       NULLIF(source_row.`OriginalName`,''),
       CASE WHEN EXISTS (SELECT 1 FROM `god2_game`.`npcs` start_npc WHERE start_npc.`npc_id`=source_row.`StartNpcId`) THEN source_row.`StartNpcId` ELSE NULL END,
       CASE WHEN EXISTS (SELECT 1 FROM `god2_game`.`npcs` end_npc WHERE end_npc.`npc_id`=source_row.`EndNpcId`) THEN source_row.`EndNpcId` ELSE NULL END,
       source_row.`RequiredLevel`,source_row.`DescriptionZhTw`,
       CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
       CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
       CASE WHEN source_row.`EvidenceStatus` NOT IN ('Verified','Recovered') THEN '未達任務 Production Gate，已安全停用。' ELSE NULL END,
       COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6)),COALESCE(source_row.`ImportedAtUtc`,UTC_TIMESTAMP(6))
FROM `god2`.`quests` source_row
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=COALESCE(`god2_game`.`quests`.`description_zh_tw`,VALUES(`description_zh_tw`));

INSERT INTO `god2_game`.`quest_objectives`
    (`objective_id`,`quest_id`,`objective_order`,`objective_type`,`target_id`,`target_name_cache`,`required_quantity`,
     `description_zh_tw`,`evidence_status`,`enabled`,`admin_note`)
SELECT ROW_NUMBER() OVER (ORDER BY source_row.`ObjectiveId`),source_row.`QuestId`,
       ROW_NUMBER() OVER (PARTITION BY source_row.`QuestId` ORDER BY source_row.`ObjectiveId`),source_row.`ObjectiveType`,
       COALESCE(source_row.`ItemId`,source_row.`MonsterId`),
       COALESCE(item_row.`name_zh_tw`,monster_row.`name_zh_tw`),source_row.`RequiredQuantity`,source_row.`ObjectiveTextZhTw`,
       CASE WHEN source_row.`EvidenceStatus` IN ('Verified','Recovered','Derived','Candidate','EvidenceBlocked') THEN source_row.`EvidenceStatus` ELSE 'Unknown' END,
       CASE WHEN source_row.`ProductionObjectiveEnabled`=1 AND source_row.`RelationshipEvidenceStatus` IN ('Verified','Recovered') AND source_row.`QuantityEvidenceStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
       CASE WHEN source_row.`ProductionObjectiveEnabled`=0 THEN '未達任務目標 Production Gate，已安全停用。' ELSE NULL END
FROM `god2`.`quest_objective_candidates` source_row
JOIN `god2_game`.`quests` quest_row ON quest_row.`quest_id`=source_row.`QuestId`
LEFT JOIN `god2_game`.`items` item_row ON item_row.`item_id`=source_row.`ItemId`
LEFT JOIN `god2_game`.`monsters` monster_row ON monster_row.`monster_id`=source_row.`MonsterId`
ON DUPLICATE KEY UPDATE
    `target_name_cache`=VALUES(`target_name_cache`),
    `description_zh_tw`=COALESCE(`god2_game`.`quest_objectives`.`description_zh_tw`,VALUES(`description_zh_tw`));

INSERT INTO `god2_game`.`item_sets`
    (`set_id`,`name_zh_tw`,`description_zh_tw`,`enabled`,`admin_note`)
SELECT source_row.`SetId`,source_row.`NameZhTw`,source_row.`EffectsZhTw`,
       CASE WHEN source_row.`ProductionRelationshipEnabled`=1 AND source_row.`SetRelationshipStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
       CASE WHEN source_row.`ProductionBonusEnabled`=0 THEN '套裝關係可用；套裝加成數值尚未驗證。' ELSE NULL END
FROM `god2`.`equipment_set_definitions` source_row
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `description_zh_tw`=VALUES(`description_zh_tw`);

INSERT INTO `god2_game`.`item_set_members`
    (`set_id`,`item_id`,`slot_name`,`enabled`,`admin_note`)
SELECT source_row.`SetId`,source_row.`ItemId`,source_row.`SlotName`,
       CASE WHEN source_row.`ProductionRelationshipEnabled`=1 AND source_row.`EvidenceStatus` IN ('Verified','Recovered') THEN 1 ELSE 0 END,
       CASE WHEN source_row.`ProductionRelationshipEnabled`=0 THEN '套裝成員未達 Production Gate。' ELSE NULL END
FROM `god2`.`equipment_set_members` source_row
JOIN `god2_game`.`item_sets` set_row ON set_row.`set_id`=source_row.`SetId`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=source_row.`ItemId`
ON DUPLICATE KEY UPDATE `slot_name`=VALUES(`slot_name`);

INSERT INTO `god2_game`.`pet_templates`
    (`pet_template_id`,`code`,`name_zh_tw`,`base_level`,`evidence_status`,`enabled`,`admin_note`)
SELECT source_row.`Id`,NULLIF(source_row.`Code`,''),source_row.`Name`,source_row.`BaseLevel`,'EvidenceBlocked',0,
       '舊 BattlePet 身分可讀，但能力與正式戰寵語意尚未通過 Gate。'
FROM `god2`.`battle_pets` source_row
ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`);

INSERT INTO `god2_game`.`immortal_templates`
    (`immortal_template_id`,`code`,`name_zh_tw`,`initial_level`,`evidence_status`,`enabled`,`admin_note`)
SELECT source_row.`Id`,NULLIF(source_row.`Code`,''),source_row.`Name`,NULL,'EvidenceBlocked',0,
       CONCAT('舊資料階位候選值：',source_row.`Rank`,'；階位與等級語意尚未通過 Gate。')
FROM `god2`.`immortals` source_row
ON DUPLICATE KEY UPDATE `name_zh_tw`=VALUES(`name_zh_tw`);

INSERT INTO `god2_player`.`accounts`
    (`account_id`,`username`,`password_hash`,`status`,`failed_login_count`,`locked_until_utc`,`last_login_at_utc`,`created_at_utc`,`updated_at_utc`)
SELECT source_row.`Id`,source_row.`LoginName`,source_row.`PasswordHash`,source_row.`Status`,source_row.`FailedLoginCount`,
       source_row.`LockedUntilUtc`,source_row.`LastLoginAtUtc`,source_row.`CreatedAtUtc`,source_row.`UpdatedAtUtc`
FROM `god2`.`accounts` source_row
ON DUPLICATE KEY UPDATE
    `username`=VALUES(`username`),
    `password_hash`=VALUES(`password_hash`),
    `status`=VALUES(`status`),
    `failed_login_count`=VALUES(`failed_login_count`),
    `locked_until_utc`=VALUES(`locked_until_utc`),
    `last_login_at_utc`=VALUES(`last_login_at_utc`);

INSERT INTO `god2_player`.`characters`
    (`character_id`,`account_id`,`name`,`class_id`,`class_name_cache`,`level`,`map_id`,`position_x`,`position_y`,`direction`,
     `status`,`enabled`,`admin_note`,`created_at_utc`,`updated_at_utc`,`last_played_at_utc`)
SELECT source_row.`Id`,source_row.`AccountId`,source_row.`Name`,NULL,NULLIF(source_row.`Class`,''),source_row.`Level`,
       CASE WHEN EXISTS (SELECT 1 FROM `god2_game`.`maps` map_row WHERE map_row.`map_id`=source_row.`MapId`) THEN source_row.`MapId` ELSE NULL END,
       source_row.`PositionX`,source_row.`PositionY`,
       CASE WHEN source_row.`CurrentDirection` REGEXP '^-?[0-9]+$' THEN CAST(source_row.`CurrentDirection` AS SIGNED) ELSE NULL END,
       source_row.`Status`,CASE WHEN source_row.`DeletedAtUtc` IS NULL THEN 1 ELSE 0 END,
       CASE WHEN source_row.`LifeSkill`<>'' THEN CONCAT('舊單值 LifeSkill 僅保留於此註記，不直接升格：',source_row.`LifeSkill`) ELSE NULL END,
       source_row.`CreatedAtUtc`,source_row.`UpdatedAtUtc`,source_row.`LastPlayedAtUtc`
FROM `god2`.`characters` source_row
ON DUPLICATE KEY UPDATE
    `name`=VALUES(`name`),
    `class_name_cache`=VALUES(`class_name_cache`),
    `level`=VALUES(`level`),
    `map_id`=VALUES(`map_id`),
    `position_x`=VALUES(`position_x`),
    `position_y`=VALUES(`position_y`),
    `direction`=VALUES(`direction`),
    `status`=VALUES(`status`),
    `enabled`=VALUES(`enabled`),
    `last_played_at_utc`=VALUES(`last_played_at_utc`);

INSERT INTO `god2_player`.`character_inventory`
    (`inventory_id`,`character_id`,`slot_index`,`item_id`,`item_name_cache`,`quantity`,`bound`,`enabled`,`created_at_utc`,`updated_at_utc`)
SELECT source_row.`PersistentInventoryItemId`,source_row.`CharacterId`,source_row.`SlotIndex`,source_row.`ItemId`,item_row.`name_zh_tw`,
       source_row.`Quantity`,CASE WHEN source_row.`BindState`='Unbound' THEN 0 WHEN source_row.`BindState`='' THEN NULL ELSE 1 END,1,
       source_row.`CreatedAtUtc`,source_row.`UpdatedAtUtc`
FROM `god2`.`inventory_slots` source_row
JOIN `god2_player`.`characters` character_row ON character_row.`character_id`=source_row.`CharacterId`
JOIN `god2_game`.`items` item_row ON item_row.`item_id`=source_row.`ItemId`
WHERE source_row.`DeletedAtUtc` IS NULL
ON DUPLICATE KEY UPDATE
    `slot_index`=VALUES(`slot_index`),
    `item_id`=VALUES(`item_id`),
    `item_name_cache`=VALUES(`item_name_cache`),
    `quantity`=VALUES(`quantity`),
    `bound`=VALUES(`bound`),
    `enabled`=VALUES(`enabled`);

INSERT INTO `god2_player`.`character_life_skills`
    (`character_id`,`life_skill_id`,`level`,`experience`,`proficiency`,`progress_value`,`is_unlocked`,`is_active`,`admin_note`)
SELECT character_row.`character_id`,skill_row.`life_skill_id`,NULL,NULL,NULL,NULL,0,0,
       '固定建立四項生活技能槽；未知等級、經驗與熟練度保持 NULL。'
FROM `god2_player`.`characters` character_row
CROSS JOIN `god2_game`.`life_skills` skill_row
WHERE skill_row.`code` IN ('ArmorForging','WeaponForging','PillAlchemy','MagicTreasureForging')
ON DUPLICATE KEY UPDATE `character_id`=VALUES(`character_id`);

INSERT INTO `god2_game_meta`.`field_mappings`
    (`source_schema`,`source_table`,`source_key_column`,`source_value_column`,`source_status_column`,`source_gate_column`,
     `target_schema`,`target_table`,`target_key_column`,`target_field`,`value_type`,`fill_null_only`,`enabled`,`admin_note`)
VALUES
    ('god2','monsters','Id','MaxHp','EvidenceStatus',NULL,'god2_game','monsters','monster_id','max_hp','Int64',1,1,'只接受 Verified；Recovered 需另加 Gate 欄位後才可用'),
    ('god2','monsters','Id','MaxMp','EvidenceStatus',NULL,'god2_game','monsters','monster_id','max_mp','Int64',1,1,'只填空白正式欄位'),
    ('god2','monsters','Id','Level','EvidenceStatus',NULL,'god2_game','monsters','monster_id','level','Int32',1,1,'只填空白正式欄位'),
    ('god2','skills','Id','MpCost','MpCostEvidenceStatus',NULL,'god2_game','skills','skill_id','mp_cost','Int64',1,1,'只接受 Verified MP 成本'),
    ('god2','skills','Id','RequiredLevel','EvidenceStatus',NULL,'god2_game','skills','skill_id','required_level','Int32',1,1,'只填空白正式欄位'),
    ('god2','items','Id','SellPrice','EvidenceStatus',NULL,'god2_game','items','item_id','sell_price','Int64',1,1,'只填空白正式欄位')
ON DUPLICATE KEY UPDATE
    `source_status_column`=VALUES(`source_status_column`),
    `source_gate_column`=VALUES(`source_gate_column`),
    `value_type`=VALUES(`value_type`),
    `fill_null_only`=VALUES(`fill_null_only`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game_meta`.`synchronization_watermarks`
    (`watermark_key`,`watermark_value`)
VALUES ('initial_catalog_migration','053')
ON DUPLICATE KEY UPDATE `watermark_value`=VALUES(`watermark_value`);

UPDATE `god2_game_meta`.`sync_runs`
SET `mapped_value_count` =
      (SELECT COUNT(*) FROM `god2_game`.`items`) +
      (SELECT COUNT(*) FROM `god2_game`.`maps`) +
      (SELECT COUNT(*) FROM `god2_game`.`monsters`) +
      (SELECT COUNT(*) FROM `god2_game`.`npcs`) +
      (SELECT COUNT(*) FROM `god2_game`.`skills`) +
      (SELECT COUNT(*) FROM `god2_game`.`quests`) +
      (SELECT COUNT(*) FROM `god2_player`.`accounts`) +
      (SELECT COUNT(*) FROM `god2_player`.`characters`),
    `blocked_candidate_count` =
      (SELECT COUNT(*) FROM `god2_game`.`items` WHERE `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')) +
      (SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')) +
      (SELECT COUNT(*) FROM `god2_game`.`skills` WHERE `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown')) +
      (SELECT COUNT(*) FROM `god2_game`.`quests` WHERE `evidence_status` IN ('Derived','Candidate','EvidenceBlocked','Unknown'))
WHERE `sync_run_id`='00000000-0000-0000-0000-000000000053';

SET @god2_sync_mode = 0;
