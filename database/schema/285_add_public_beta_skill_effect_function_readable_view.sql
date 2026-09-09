CREATE OR REPLACE VIEW `god2_game`.`vw_public_beta_skill_effect_function_readable` AS
SELECT
    effect_row.`global_record_id` AS `全域記錄ID`,
    effect_row.`record_id_in_bank` AS `技能庫內記錄ID`,
    effect_row.`bank` AS `技能庫`,
    effect_row.`slot_one_based` AS `技能庫欄位`,
    COALESCE(NULLIF(effect_row.`name_zh_tw`, ''), '未命名技能效果') AS `技能名稱`,
    COALESCE(NULLIF(effect_row.`description_zh_tw`, ''), '未提供技能說明') AS `技能說明`,
    effect_row.`skill_level` AS `技能等級`,
    effect_row.`implementation_family` AS `實作分類`,
    effect_row.`effect_kind` AS `效果類型`,
    COALESCE(NULLIF(effect_row.`status_or_axis`, ''), '未標示狀態或屬性軸') AS `狀態或屬性軸`,
    CASE effect_row.`effect_kind`
        WHEN '增益' THEN CONCAT('替目標套用正面效果；分類：', effect_row.`implementation_family`)
        WHEN '負面' THEN CONCAT('對目標套用負面狀態；狀態：', COALESCE(NULLIF(effect_row.`status_or_axis`, ''), '未標示'))
        WHEN '淨化' THEN CONCAT('解除目標身上的狀態；範圍：', COALESCE(NULLIF(effect_row.`status_or_axis`, ''), '未標示'))
        WHEN '治療' THEN '恢復目標生命值'
        WHEN '復活' THEN '使死亡友方回到可戰鬥狀態'
        ELSE CONCAT('未分類技能效果；原始類型：', effect_row.`effect_kind`)
    END AS `遊戲功能對照`,
    CASE
        WHEN effect_row.`mp_cost` IS NULL THEN '未提供MP消耗'
        ELSE CONCAT('消耗MP ', effect_row.`mp_cost`)
    END AS `MP消耗`,
    CASE
        WHEN effect_row.`target_context` IS NULL THEN '未提供客戶端目標情境'
        ELSE CONCAT('客戶端目標情境代碼 ', effect_row.`target_context`, '；保留原碼等待官方對照')
    END AS `目標情境`,
    CASE
        WHEN effect_row.`target_scope` IS NULL THEN '未提供客戶端目標範圍'
        ELSE CONCAT('客戶端目標範圍代碼 ', effect_row.`target_scope`, '；保留原碼等待官方對照')
    END AS `目標範圍`,
    COALESCE(NULLIF(effect_row.`numeric_effect_fields`, ''), '未提供數值效果欄位') AS `數值效果欄位`,
    COALESCE(NULLIF(effect_row.`v0_to_v23_nonzero`, ''), '未提供非零效果欄位') AS `非零效果欄位`,
    effect_row.`minimum_working_server_rule` AS `最低可運作服務端規則`,
    COALESCE(NULLIF(effect_row.`provisional_success_rule`, ''), '未提供暫定成功規則') AS `暫定成功規則`,
    COALESCE(NULLIF(effect_row.`provisional_duration_rule`, ''), '未提供暫定持續規則') AS `暫定持續規則`,
    CASE effect_row.`enabled`
        WHEN 1 THEN '服務端技能效果候選已啟用'
        WHEN 0 THEN '服務端技能效果候選未啟用'
        ELSE '未標示'
    END AS `服務端候選狀態`,
    effect_row.`created_at_utc` AS `建立時間UTC`,
    effect_row.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`public_beta_skill_effect_v0` effect_row;
