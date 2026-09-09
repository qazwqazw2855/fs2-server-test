SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='FormationTactics',
    skill_row.`skill_category`='Passive',
    skill_row.`damage_type`='Status',
    skill_row.`element`=CASE WHEN skill_row.`name_zh_tw` LIKE '五行之要略%' THEN 'Unknown' ELSE 'None' END,
    skill_row.`mp_cost`=0,
    skill_row.`attack_range`=0,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='Passive',
    skill_row.`target_scope_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`target_count`=1,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` BETWEEN 6751 AND 6759
  AND skill_row.`name_zh_tw` REGEXP '^(侵攻|固守|五行)之要略[123]$';

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='SpellCostMastery',
    skill_row.`skill_category`='Passive',
    skill_row.`damage_type`='Status',
    skill_row.`element`='None',
    skill_row.`mp_cost`=0,
    skill_row.`attack_range`=0,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='Passive',
    skill_row.`target_scope_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`target_count`=1,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` BETWEEN 6862 AND 6870
  AND skill_row.`name_zh_tw` REGEXP '^(回覆|強化|狀態)之心得[123]$';

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       'EvidenceBlocked',
       'OfficialClientStatic',
       CASE
           WHEN skill_row.`official_client_item_id` BETWEEN 6751 AND 6759
               THEN '陣法要略被動：官方客戶端技能書文字已證明不需主動使用，並提高攻擊、防禦或五行陣法效果；目前服務端尚無已接受的陣法被動倍率與套用點，因此只補功能對照，暫不啟用。'
           ELSE '法術心得被動：官方客戶端技能書文字已證明不需主動使用，並降低回覆、強化或狀態法術 MP 消耗；目前服務端尚無已接受的 MP 降耗倍率與套用點，因此只補功能對照，暫不啟用。'
       END,
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
WHERE (skill_row.`official_client_item_id` BETWEEN 6751 AND 6759)
   OR (skill_row.`official_client_item_id` BETWEEN 6862 AND 6870)
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
