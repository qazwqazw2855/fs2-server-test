SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
JOIN `god2_game`.`public_beta_skill_effect_v0` effect_row
  ON effect_row.`description_zh_tw`=REPLACE(REPLACE(skill_row.`name_zh_tw`,'1','一'),'2','二')
     OR effect_row.`description_zh_tw`=REPLACE(REPLACE(skill_row.`name_zh_tw`,'3','三'),'2','二')
SET skill_row.`description_zh_tw`=metadata.`official_effect_text_zh_tw`,
    skill_row.`skill_family`='XianDaoMeditation',
    skill_row.`skill_category`='Passive',
    skill_row.`damage_type`='Magic',
    skill_row.`element`=CASE
        WHEN skill_row.`name_zh_tw` LIKE '金之冥思%' THEN 'Metal'
        WHEN skill_row.`name_zh_tw` LIKE '木之冥思%' THEN 'Wood'
        WHEN skill_row.`name_zh_tw` LIKE '土之冥思%' THEN 'Earth'
        WHEN skill_row.`name_zh_tw` LIKE '水之冥思%' THEN 'Water'
        WHEN skill_row.`name_zh_tw` LIKE '火之冥思%' THEN 'Fire'
        ELSE skill_row.`element`
    END,
    skill_row.`maximum_level`=1,
    skill_row.`mp_cost`=0,
    skill_row.`attack_range`=0,
    skill_row.`hp_cost`=0,
    skill_row.`cooldown_rounds`=0,
    skill_row.`cast_rounds`=0,
    skill_row.`duration_rounds`=NULL,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='Passive',
    skill_row.`target_scope_zh_tw`='自身被動，同系符咒攻擊殺傷力上升',
    skill_row.`target_count`=1,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=0,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=0,
    skill_row.`can_miss`=0,
    skill_row.`effect_id`=effect_row.`global_record_id`,
    skill_row.`enabled`=CASE WHEN metadata.`skill_tier` IN (1,2) THEN 1 ELSE 0 END
WHERE skill_row.`official_client_item_id` BETWEEN 6647 AND 6661
  AND skill_row.`name_zh_tw` REGEXP '^[金木土水火]之冥思[123]$'
  AND effect_row.`implementation_family`='element_talisman_attack_buff'
  AND effect_row.`skill_level`=metadata.`skill_tier`
  AND (
      (skill_row.`name_zh_tw` LIKE '金之冥思%' AND effect_row.`name_zh_tw` LIKE '金屬性%') OR
      (skill_row.`name_zh_tw` LIKE '木之冥思%' AND effect_row.`name_zh_tw` LIKE '木屬性%') OR
      (skill_row.`name_zh_tw` LIKE '土之冥思%' AND effect_row.`name_zh_tw` LIKE '土屬性%') OR
      (skill_row.`name_zh_tw` LIKE '水之冥思%' AND effect_row.`name_zh_tw` LIKE '水屬性%') OR
      (skill_row.`name_zh_tw` LIKE '火之冥思%' AND effect_row.`name_zh_tw` LIKE '火屬性%')
  );

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       CASE WHEN metadata.`skill_tier` IN (1,2) THEN 'Derived' ELSE 'EvidenceBlocked' END,
       'OfficialClientStatic',
       CASE WHEN metadata.`skill_tier` IN (1,2)
            THEN '仙道冥思被動：官方客戶端技能書文字證明不需主動使用並提高同五行殺傷力；公測 cbspec2 證明 element_talisman_attack_buff 身分；服務端魔法傷害公式已接受同系冥思一、二階係數。'
            ELSE '仙道冥思被動：官方客戶端與公測 cbspec2 已證明技能身分與同五行殺傷力語意，但服務端目前沒有已接受的三階冥思係數，因此正式技能保留未啟用。'
       END,
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
WHERE skill_row.`official_client_item_id` BETWEEN 6647 AND 6661
  AND skill_row.`name_zh_tw` REGEXP '^[金木土水火]之冥思[123]$'
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
