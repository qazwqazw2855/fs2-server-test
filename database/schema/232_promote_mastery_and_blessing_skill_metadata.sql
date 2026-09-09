SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='Blessing',
    skill_row.`skill_category`='Active',
    skill_row.`damage_type`='Status',
    skill_row.`element`='None',
    skill_row.`target_side`=CASE
        WHEN metadata.`official_effect_text_zh_tw` LIKE '%全員%' THEN 'Ally'
        ELSE 'Self'
    END,
    skill_row.`target_type`='Buff',
    skill_row.`target_scope_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=CASE WHEN metadata.`official_effect_text_zh_tw` LIKE '%全員%' THEN 1 ELSE 0 END,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=CASE WHEN metadata.`official_effect_text_zh_tw` LIKE '%死亡%' OR metadata.`official_effect_text_zh_tw` LIKE '%復活%' THEN 1 ELSE 0 END,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`enabled`=0
WHERE skill_row.`name_zh_tw` LIKE '%加持'
  AND skill_row.`official_client_item_id` IN (6565,6566,6567,6662,6663,6664,6760,6762,6871,6872);

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='RebirthMastery',
    skill_row.`skill_category`='Passive',
    skill_row.`damage_type`=CASE
        WHEN skill_row.`name_zh_tw` IN ('回覆大師','能力大師','狀態大師','回覆陣大師') THEN 'Status'
        WHEN skill_row.`name_zh_tw` IN ('金系大師','木系大師','土系大師','水系大師','火系大師','五行陣大師') THEN 'Magic'
        ELSE 'Physical'
    END,
    skill_row.`element`=CASE
        WHEN skill_row.`name_zh_tw`='金系大師' THEN 'Metal'
        WHEN skill_row.`name_zh_tw`='木系大師' THEN 'Wood'
        WHEN skill_row.`name_zh_tw`='土系大師' THEN 'Earth'
        WHEN skill_row.`name_zh_tw`='水系大師' THEN 'Water'
        WHEN skill_row.`name_zh_tw`='火系大師' THEN 'Fire'
        ELSE 'None'
    END,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='Passive',
    skill_row.`target_scope_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`critical_rate`=NULL,
    skill_row.`enabled`=0
WHERE skill_row.`name_zh_tw` LIKE '%大師'
  AND skill_row.`official_client_item_id` IN
      (6568,6569,6570,6571,6572,6573,6574,6575,
       6665,6666,6667,6668,6669,
       6763,6764,6765,
       6884,6885,6886);

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       'EvidenceBlocked',
       'OfficialClientStatic',
       CASE
           WHEN skill_row.`name_zh_tw` LIKE '%加持'
               THEN '加持類主動技能：官方客戶端技能書文字已證明限時、一次性、全隊、反射、復活或 HP/MP 對調等功能語意；目前服務端尚無完整限時 buff、觸發消耗與正式封包結果套用點，因此只補功能對照，暫不啟用。'
           ELSE '轉生大師類技能：官方客戶端技能書文字已證明轉生保留、暴擊、反擊、命中、回覆、能力或陣法加成等功能語意；目前服務端尚無已接受的暴擊率、觸發條件與各系效果公式，因此只補功能對照，暫不啟用。'
       END,
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
WHERE skill_row.`official_client_item_id` IN
      (6565,6566,6567,6568,6569,6570,6571,6572,6573,6574,6575,
       6662,6663,6664,6665,6666,6667,6668,6669,
       6760,6762,6763,6764,6765,
       6871,6872,6884,6885,6886)
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
