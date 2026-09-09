SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%劍技%' THEN 'Sword'
        WHEN metadata.`skill_category_zh_tw` LIKE '%刀技%' THEN 'Blade'
        WHEN metadata.`skill_category_zh_tw` LIKE '%杖技%' THEN 'Staff'
        WHEN metadata.`skill_category_zh_tw` LIKE '%斧技%' THEN 'Axe'
        WHEN metadata.`skill_category_zh_tw` LIKE '%槍技%' OR metadata.`skill_category_zh_tw` LIKE '%火神%' THEN 'Spear'
        WHEN metadata.`skill_category_zh_tw` LIKE '%鞭技%' THEN 'Whip'
        WHEN metadata.`skill_category_zh_tw` LIKE '%飛刀技%' THEN 'ThrowingKnife'
        WHEN metadata.`skill_category_zh_tw` LIKE '%弓技%' THEN 'Bow'
        WHEN metadata.`skill_category_zh_tw` LIKE '%爪技%' THEN 'Claw'
        WHEN metadata.`skill_category_zh_tw` LIKE '%無形%' THEN 'Formless'
        ELSE 'PhysicalAttack'
    END,
    skill_row.`skill_category`='Active',
    skill_row.`damage_type`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%仙術%' THEN 'Magic'
        ELSE 'Physical'
    END,
    skill_row.`element`='None',
    skill_row.`target_side`='Enemy',
    skill_row.`target_type`=CASE
        WHEN metadata.`target_scope_zh_tw` IN ('兩體','三體','一排','圓形','任意兩體，可選同一體') THEN 'MultipleEnemy'
        ELSE 'SingleEnemy'
    END,
    skill_row.`target_scope_zh_tw`=COALESCE(NULLIF(metadata.`target_scope_zh_tw`,''),metadata.`official_effect_text_zh_tw`),
    skill_row.`target_count`=CASE
        WHEN metadata.`target_scope_zh_tw`='三體' THEN 3
        WHEN metadata.`target_scope_zh_tw` IN ('兩體','任意兩體，可選同一體') THEN 2
        ELSE skill_row.`target_count`
    END,
    skill_row.`can_target_self`=0,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=1,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=1,
    skill_row.`can_critical`=1,
    skill_row.`can_miss`=1,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` IN
      (6593,6594,6595,6596,6597,6598,6599,6600,6641,6642,
       6644,6645,6646,6766,6767,6768,6769,6771,6897);

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%仙術無形%' THEN 'FormlessMagic'
        WHEN metadata.`skill_category_zh_tw` LIKE '%仙術爪技%' THEN 'ClawMagic'
        ELSE 'NuwaElementalSpell'
    END,
    skill_row.`skill_category`='Active',
    skill_row.`damage_type`='Magic',
    skill_row.`element`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%金系%' OR skill_row.`name_zh_tw` LIKE '%白虎%' THEN 'Metal'
        WHEN metadata.`skill_category_zh_tw` LIKE '%木系%' OR skill_row.`name_zh_tw` LIKE '%青龍%' THEN 'Wood'
        WHEN metadata.`skill_category_zh_tw` LIKE '%土系%' OR skill_row.`name_zh_tw` LIKE '%麒麟%' THEN 'Earth'
        WHEN metadata.`skill_category_zh_tw` LIKE '%水系%' OR skill_row.`name_zh_tw` LIKE '%玄武%' THEN 'Water'
        WHEN metadata.`skill_category_zh_tw` LIKE '%火系%' OR skill_row.`name_zh_tw` LIKE '%朱雀%' THEN 'Fire'
        ELSE 'None'
    END,
    skill_row.`target_side`='Enemy',
    skill_row.`target_type`=CASE
        WHEN metadata.`target_scope_zh_tw` IN ('兩體','三體','一排','圓形','任意兩體，可選同一體') THEN 'MultipleEnemy'
        ELSE 'SingleEnemy'
    END,
    skill_row.`target_scope_zh_tw`=COALESCE(NULLIF(metadata.`target_scope_zh_tw`,''),metadata.`official_effect_text_zh_tw`),
    skill_row.`target_count`=CASE
        WHEN metadata.`target_scope_zh_tw`='三體' THEN 3
        WHEN metadata.`target_scope_zh_tw` IN ('兩體','任意兩體，可選同一體') THEN 2
        ELSE skill_row.`target_count`
    END,
    skill_row.`can_target_self`=0,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=1,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=1,
    skill_row.`can_critical`=1,
    skill_row.`can_miss`=1,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` IN
      (6770,6772,6784,6785,6786,6787,6807);

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       'EvidenceBlocked',
       'OfficialClientStatic',
       CASE
           WHEN skill_row.`official_client_item_id` IN (6784,6785,6786,6787,6807)
               THEN '女媧補天五行咒術：官方客戶端技能書文字已證明圓形攻擊並附帶封印、麻痺、石化、中毒或混亂；目前服務端尚無正式五行咒術傷害階級、範圍解析與狀態附加率證據，因此只補功能對照，暫不啟用。'
           ELSE '高階主動攻擊技能：官方客戶端技能書文字已證明目標範圍、威力、武器損耗、速度/MP/石化等效果語意；目前服務端尚無正式倍率、範圍命中、武器耐久損耗或附加狀態機率證據，因此只補功能對照，暫不啟用。'
       END,
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
WHERE skill_row.`official_client_item_id` IN
      (6593,6594,6595,6596,6597,6598,6599,6600,6641,6642,
       6644,6645,6646,6766,6767,6768,6769,6770,6771,6772,
       6784,6785,6786,6787,6807,6897)
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
