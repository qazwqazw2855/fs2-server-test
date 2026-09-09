CREATE OR REPLACE VIEW `god2_game`.`vw_item_usage_rules_readable` AS
SELECT
    usage_row.`item_id` AS `道具ID`,
    COALESCE(item_row.`name_zh_tw`, registry_row.`name_zh_tw`) AS `道具名稱`,
    CASE usage_row.`normal_use`
        WHEN 1 THEN '可在一般場景使用'
        WHEN 0 THEN '不可在一般場景使用'
        ELSE '未標示'
    END AS `一般使用`,
    CASE usage_row.`battle_use`
        WHEN 1 THEN '可在戰鬥中使用'
        WHEN 0 THEN '不可在戰鬥中使用'
        ELSE '未標示'
    END AS `戰鬥使用`,
    CASE usage_row.`equippable`
        WHEN 1 THEN '可裝備'
        WHEN 0 THEN '不可裝備'
        ELSE '未標示'
    END AS `裝備狀態`,
    CASE usage_row.`use_on_other`
        WHEN 1 THEN '可對其他目標使用'
        WHEN 0 THEN '不可對其他目標使用'
        ELSE '未標示'
    END AS `對他人使用`,
    CASE usage_row.`hotkey_allowed`
        WHEN 1 THEN '允許快捷鍵'
        WHEN 0 THEN '不允許快捷鍵'
        ELSE '未標示'
    END AS `快捷鍵`,
    COALESCE(NULLIF(usage_row.`class_restriction_zh_tw`, ''), '無職業限制') AS `職業限制`,
    usage_row.`minimum_rebirth` AS `最低轉生`,
    COALESCE(NULLIF(usage_row.`gender_restriction_zh_tw`, ''), '無性別限制') AS `性別限制`,
    COALESCE(NULLIF(usage_row.`equipment_target_restriction_zh_tw`, ''), '無裝備目標限制') AS `裝備目標限制`,
    COALESCE(NULLIF(usage_row.`condition_text_zh_tw`, ''), '無額外條件文字') AS `使用條件`,
    CASE usage_row.`runtime_eligible`
        WHEN 1 THEN '可進入服務端執行候選'
        WHEN 0 THEN '不可執行或仍需證據'
        ELSE '未標示'
    END AS `執行候選狀態`,
    CASE usage_row.`enabled`
        WHEN 1 THEN '服務端正式啟用'
        WHEN 0 THEN '服務端未啟用'
        ELSE '未標示'
    END AS `服務端狀態`,
    usage_row.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`item_usage_rules` usage_row
LEFT JOIN `god2_game`.`items` item_row
    ON item_row.`item_id` = usage_row.`item_id`
LEFT JOIN `god2_game`.`item_registry` registry_row
    ON registry_row.`item_id` = usage_row.`item_id`;
