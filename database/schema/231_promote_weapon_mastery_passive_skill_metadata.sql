SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='WeaponMastery',
    skill_row.`skill_category`='Passive',
    skill_row.`damage_type`='Physical',
    skill_row.`element`='None',
    skill_row.`maximum_level`=1,
    skill_row.`mp_cost`=0,
    skill_row.`attack_range`=0,
    skill_row.`hp_cost`=0,
    skill_row.`cooldown_rounds`=0,
    skill_row.`cast_rounds`=0,
    skill_row.`duration_rounds`=NULL,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='Passive',
    skill_row.`target_scope_zh_tw`=CASE
        WHEN skill_row.`name_zh_tw` LIKE '刀之極意%' THEN '自身被動，裝備刀類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '劍之極意%' THEN '自身被動，裝備劍類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '斧之極意%' THEN '自身被動，裝備斧類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '杖之極意%' THEN '自身被動，裝備杖類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '槍之極意%' THEN '自身被動，裝備槍類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '鞭之極意%' THEN '自身被動，裝備鞭類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '弓之極意%' THEN '自身被動，裝備弓類武器提高殺傷力'
        WHEN skill_row.`name_zh_tw` LIKE '飛刀之極意%' THEN '自身被動，裝備飛刀類武器提高殺傷力'
        ELSE '自身被動，裝備對應武器提高殺傷力'
    END,
    skill_row.`target_count`=1,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` BETWEEN 6541 AND 6564
  AND skill_row.`name_zh_tw` REGEXP '^(刀|劍|斧|杖|槍|鞭|弓|飛刀)之極意[123]$';

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       'EvidenceBlocked',
       'OfficialClientStatic',
       '武器極意被動：官方客戶端技能書文字證明不需主動使用，且裝備指定武器時提高殺傷力；目前服務端只有物理技能倍率表，尚無已接受的武器被動加成係數與套用點，因此正式技能只補功能對照，暫不啟用。',
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
WHERE skill_row.`official_client_item_id` BETWEEN 6541 AND 6564
  AND skill_row.`name_zh_tw` REGEXP '^(刀|劍|斧|杖|槍|鞭|弓|飛刀)之極意[123]$'
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
