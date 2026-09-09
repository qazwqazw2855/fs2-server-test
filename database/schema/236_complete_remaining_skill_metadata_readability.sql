SET @god2_sync_mode = 1;

UPDATE `god2_game`.`skills` skill_row
SET skill_row.`damage_type`=CASE
        WHEN skill_row.`name_zh_tw` LIKE '獸之神%' THEN 'Magic'
        WHEN skill_row.`name_zh_tw` LIKE '獸之精%' THEN 'Physical'
        ELSE 'Status'
    END,
    skill_row.`element`=CASE
        WHEN skill_row.`name_zh_tw` LIKE '%金%' OR skill_row.`name_zh_tw` LIKE '%天雷%' THEN 'Metal'
        WHEN skill_row.`name_zh_tw` LIKE '%木%' OR skill_row.`name_zh_tw` LIKE '%龍捲%' THEN 'Wood'
        WHEN skill_row.`name_zh_tw` LIKE '%水%' OR skill_row.`name_zh_tw` LIKE '%冰晶%' THEN 'Water'
        WHEN skill_row.`name_zh_tw` LIKE '%火%' OR skill_row.`name_zh_tw` LIKE '%炎球%' THEN 'Fire'
        ELSE 'None'
    END,
    skill_row.`mp_cost`=0,
    skill_row.`target_side`='Self',
    skill_row.`target_type`='PetInnate',
    skill_row.`target_scope_zh_tw`='戰寵固有技能，依寵物模板解鎖',
    skill_row.`target_count`=1,
    skill_row.`can_target_self`=1,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=CASE
        WHEN skill_row.`name_zh_tw` LIKE '%毒%' OR skill_row.`name_zh_tw` LIKE '%混亂%' OR
             skill_row.`name_zh_tw` LIKE '%眠%' OR skill_row.`name_zh_tw` LIKE '%石化%' OR
             skill_row.`name_zh_tw` LIKE '%神封%' OR skill_row.`name_zh_tw` LIKE '獸之神%' OR
             skill_row.`name_zh_tw` LIKE '獸之精%'
        THEN 1 ELSE 0 END,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=0,
    skill_row.`can_critical`=CASE WHEN skill_row.`name_zh_tw` LIKE '獸之精%' THEN 1 ELSE 0 END,
    skill_row.`can_miss`=CASE WHEN skill_row.`name_zh_tw` LIKE '%解除%' THEN 0 ELSE 1 END,
    skill_row.`enabled`=0
WHERE skill_row.`code` LIKE 'pet_innate_%';

UPDATE `god2_game`.`skills` skill_row
JOIN `god2_game`.`skill_client_metadata` metadata
  ON metadata.`official_client_item_id`=skill_row.`official_client_item_id`
SET skill_row.`description_zh_tw`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%咒術%'
            THEN CONCAT('官方客戶端技能書列為', metadata.`skill_category_zh_tw`, '；MP ', metadata.`mp_cost`, '，距離 ', metadata.`attack_range`, '，作用範圍：', metadata.`target_scope_zh_tw`, '。')
        WHEN metadata.`skill_category_zh_tw`='全體範圍技'
            THEN CONCAT('官方客戶端技能書列為全體範圍技；MP ', metadata.`mp_cost`, '，距離 ', metadata.`attack_range`, '，作用範圍：', metadata.`target_scope_zh_tw`, '。')
        WHEN metadata.`skill_category_zh_tw` LIKE '%火神%'
            THEN CONCAT('官方客戶端技能書列為火神限定物理技能；MP ', metadata.`mp_cost`, '，距離 ', metadata.`attack_range`, '，作用範圍：', metadata.`target_scope_zh_tw`, '。')
        ELSE CONCAT('官方客戶端技能書列為特殊技能；MP ', metadata.`mp_cost`, '，距離 ', metadata.`attack_range`, '，作用範圍：', metadata.`target_scope_zh_tw`, '。')
    END,
    skill_row.`skill_family`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%金系%' OR skill_row.`name_zh_tw` LIKE '%雷%' THEN 'MetalSpell'
        WHEN metadata.`skill_category_zh_tw` LIKE '%木系%' OR skill_row.`name_zh_tw` LIKE '%龍捲%' THEN 'WoodSpell'
        WHEN metadata.`skill_category_zh_tw` LIKE '%土系%' OR skill_row.`name_zh_tw` LIKE '%地突%' THEN 'EarthSpell'
        WHEN metadata.`skill_category_zh_tw` LIKE '%水系%' OR skill_row.`name_zh_tw` LIKE '%冰%' OR skill_row.`name_zh_tw` LIKE '%鯨%' THEN 'WaterSpell'
        WHEN metadata.`skill_category_zh_tw` LIKE '%火系%' OR skill_row.`name_zh_tw` LIKE '%炎%' OR skill_row.`name_zh_tw` LIKE '%火神%' THEN 'FireSpell'
        WHEN skill_row.`name_zh_tw` LIKE '五絃琴%' THEN 'MusicSpecial'
        ELSE 'SpecialActive'
    END,
    skill_row.`skill_category`='Active',
    skill_row.`damage_type`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%火神%' THEN 'Physical'
        WHEN skill_row.`name_zh_tw` LIKE '五絃琴%' OR skill_row.`name_zh_tw` LIKE '%截速%' THEN 'Status'
        WHEN metadata.`skill_category_zh_tw` LIKE '%咒術%' OR metadata.`skill_category_zh_tw`='全體範圍技' THEN 'Magic'
        ELSE 'Unknown'
    END,
    skill_row.`element`=CASE
        WHEN metadata.`skill_category_zh_tw` LIKE '%金系%' OR skill_row.`name_zh_tw` LIKE '%雷%' THEN 'Metal'
        WHEN metadata.`skill_category_zh_tw` LIKE '%木系%' OR skill_row.`name_zh_tw` LIKE '%龍捲%' THEN 'Wood'
        WHEN metadata.`skill_category_zh_tw` LIKE '%土系%' OR skill_row.`name_zh_tw` LIKE '%地突%' THEN 'Earth'
        WHEN metadata.`skill_category_zh_tw` LIKE '%水系%' OR skill_row.`name_zh_tw` LIKE '%冰%' OR skill_row.`name_zh_tw` LIKE '%鯨%' THEN 'Water'
        WHEN metadata.`skill_category_zh_tw` LIKE '%火系%' OR skill_row.`name_zh_tw` LIKE '%炎%' OR skill_row.`name_zh_tw` LIKE '%火神%' THEN 'Fire'
        ELSE 'None'
    END,
    skill_row.`target_side`='Enemy',
    skill_row.`target_type`=CASE
        WHEN metadata.`target_scope_zh_tw` IN ('全體','全體，降低對手速度','一排','貫穿','圓形') THEN 'MultipleEnemy'
        ELSE 'SingleEnemy'
    END,
    skill_row.`target_count`=CASE
        WHEN metadata.`target_scope_zh_tw`='全體' OR metadata.`target_scope_zh_tw`='全體，降低對手速度' THEN NULL
        ELSE skill_row.`target_count`
    END,
    skill_row.`can_target_self`=0,
    skill_row.`can_target_ally`=0,
    skill_row.`can_target_enemy`=1,
    skill_row.`can_target_dead`=0,
    skill_row.`consumes_turn`=1,
    skill_row.`can_critical`=CASE WHEN metadata.`skill_category_zh_tw` LIKE '%火神%' THEN 1 ELSE 0 END,
    skill_row.`can_miss`=1,
    skill_row.`enabled`=0
WHERE skill_row.`official_client_item_id` IN
      (6587,6590,6591,6675,6676,6677,6678,6679,
       6680,6681,6682,6683,6684,6685,6686,6687,6688,6689,
       6696,6697,6698,6700,6898);

INSERT INTO `god2_research`.`skill_catalog_evidence`
    (`skill_id`,`evidence_status`,`client_metadata_evidence_status`,`admin_note`,`moved_at_utc`)
SELECT skill_row.`skill_id`,
       'EvidenceBlocked',
       'OfficialClientStatic',
       CASE
           WHEN skill_row.`code` LIKE 'pet_innate_%'
               THEN '戰寵固有技能：正式寵物模板仍引用此技能，因此不可刪除；目前已補足正式技能表可讀分類與目標欄位，但效果公式尚待驗證，保持未啟用。'
           ELSE '剩餘主動技能可讀化：官方客戶端技能書提供名稱、MP、距離與作用範圍，但沒有完整效果公式、命中、範圍解析或封包結果證據；已補正式分類與描述，保持未啟用。'
       END,
       UTC_TIMESTAMP(6)
FROM `god2_game`.`skills` skill_row
WHERE skill_row.`code` LIKE 'pet_innate_%'
   OR skill_row.`official_client_item_id` IN
      (6587,6590,6591,6675,6676,6677,6678,6679,
       6680,6681,6682,6683,6684,6685,6686,6687,6688,6689,
       6696,6697,6698,6700,6898)
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `client_metadata_evidence_status`=VALUES(`client_metadata_evidence_status`),
    `admin_note`=VALUES(`admin_note`),
    `moved_at_utc`=VALUES(`moved_at_utc`);

SET @god2_sync_mode = NULL;
