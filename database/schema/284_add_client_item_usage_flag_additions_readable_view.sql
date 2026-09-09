CREATE OR REPLACE VIEW `god2_game`.`vw_client_item_usage_flag_additions_readable` AS
SELECT
    flag_row.`client_item_id` AS `客戶端道具ID`,
    flag_row.`display_name_zh_tw` AS `道具名稱`,
    CASE flag_row.`normal_use`
        WHEN 1 THEN '可在一般場景使用'
        WHEN 0 THEN '不可在一般場景使用'
        ELSE '未標示'
    END AS `一般使用`,
    CASE flag_row.`battle_use`
        WHEN 1 THEN '可在戰鬥中使用'
        WHEN 0 THEN '不可在戰鬥中使用'
        ELSE '未標示'
    END AS `戰鬥使用`,
    CASE flag_row.`equippable`
        WHEN 1 THEN '可裝備'
        WHEN 0 THEN '不可裝備'
        ELSE '未標示'
    END AS `裝備狀態`,
    CASE flag_row.`use_on_other`
        WHEN 1 THEN '可對其他目標使用'
        WHEN 0 THEN '不可對其他目標使用'
        ELSE '未標示'
    END AS `對他人使用`,
    CASE flag_row.`hotkey_allowed`
        WHEN 1 THEN '可放入快捷列'
        WHEN 0 THEN '不可放入快捷列'
        ELSE '未標示'
    END AS `快捷列`,
    CASE flag_row.`tradable`
        WHEN 1 THEN '可交易'
        WHEN 0 THEN '不可交易'
        ELSE '未標示'
    END AS `交易`,
    CASE flag_row.`droppable`
        WHEN 1 THEN '可丟棄'
        WHEN 0 THEN '不可丟棄'
        ELSE '未標示'
    END AS `丟棄`,
    CASE flag_row.`storable`
        WHEN 1 THEN '可存倉'
        WHEN 0 THEN '不可存倉'
        ELSE '未標示'
    END AS `倉庫`,
    CASE flag_row.`stackable`
        WHEN 1 THEN '可堆疊'
        WHEN 0 THEN '不可堆疊'
        ELSE '未標示'
    END AS `堆疊`,
    CASE flag_row.`combine_up`
        WHEN 1 THEN '可向上合成'
        WHEN 0 THEN '不可向上合成'
        ELSE '未標示'
    END AS `向上合成`,
    CASE flag_row.`combine_down`
        WHEN 1 THEN '可向下拆分或降階'
        WHEN 0 THEN '不可向下拆分或降階'
        ELSE '未標示'
    END AS `向下拆分`,
    flag_row.`binding_status` AS `綁定證據狀態`,
    CASE flag_row.`runtime_eligible`
        WHEN 1 THEN '可進入服務端執行候選'
        WHEN 0 THEN '不可執行或仍需證據'
        ELSE '未標示'
    END AS `服務端執行候選`,
    flag_row.`created_at_utc` AS `建立時間UTC`
FROM `god2_game`.`client_item_usage_flag_additions` flag_row;
