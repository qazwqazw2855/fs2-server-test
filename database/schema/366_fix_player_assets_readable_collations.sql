CREATE OR REPLACE VIEW `god2_game`.`vw_player_assets_readable` AS
SELECT
    character_row.`character_id` AS `角色ID`,
    character_row.`name` AS `角色名稱`,
    character_row.`account_id` AS `帳號ID`,
    character_row.`class_name_cache` AS `職業`,
    character_row.`level` AS `等級`,
    COALESCE(CONVERT(inventory_state_row.`InventoryId` USING utf8mb4), '尚未建立') AS `背包ID`,
    COALESCE(inventory_state_row.`Capacity`, 0) AS `背包容量`,
    COALESCE(inventory_state_row.`InventoryVersion`, 0) AS `背包版本`,
    COALESCE(inventory_state_row.`MutationSequence`, 0) AS `背包異動序號`,
    CASE
        WHEN inventory_state_row.`CharacterId` IS NULL THEN '背包尚未建立'
        WHEN inventory_state_row.`DirtyState` = 'Clean' THEN '已同步'
        WHEN inventory_state_row.`DirtyState` = 'Dirty' THEN '有未同步異動'
        ELSE COALESCE(CONVERT(inventory_state_row.`DirtyState` USING utf8mb4), '未標示')
    END AS `背包狀態`,
    COALESCE(inventory_count_row.`ItemStackCount`, 0) AS `背包物品格數`,
    COALESCE(inventory_count_row.`TotalQuantity`, 0) AS `背包物品總數量`,
    COALESCE(CONVERT(currency_row.`CurrencyType` USING utf8mb4), 'Gold') AS `貨幣Key`,
    CASE COALESCE(currency_row.`CurrencyType`, 'Gold')
        WHEN 'Gold' THEN '金錢'
        ELSE COALESCE(CONVERT(currency_row.`CurrencyType` USING utf8mb4), '未知貨幣')
    END AS `貨幣`,
    COALESCE(currency_row.`Balance`, 0) AS `餘額`,
    COALESCE(currency_row.`Version`, 0) AS `錢包版本`,
    CASE
        WHEN currency_row.`CharacterId` IS NULL THEN '錢包尚未建立'
        WHEN currency_row.`DirtyState` = 'Clean' THEN '已同步'
        WHEN currency_row.`DirtyState` = 'Dirty' THEN '有未同步異動'
        ELSE COALESCE(CONVERT(currency_row.`DirtyState` USING utf8mb4), '未標示')
    END AS `錢包狀態`,
    COALESCE(equipment_count_row.`EquippedCount`, 0) AS `已裝備格數`,
    COALESCE(equipment_count_row.`EquippedItemNames`, '沒有已裝備道具') AS `已裝備道具`,
    character_row.`updated_at_utc` AS `角色更新時間UTC`,
    inventory_state_row.`UpdatedAtUtc` AS `背包更新時間UTC`,
    currency_row.`UpdatedAtUtc` AS `錢包更新時間UTC`
FROM `god2_player`.`characters` character_row
LEFT JOIN `god2_player`.`player_inventory_state` inventory_state_row
    ON inventory_state_row.`CharacterId` = character_row.`character_id`
LEFT JOIN (
    SELECT
        slot_row.`character_id`,
        COUNT(*) AS `ItemStackCount`,
        COALESCE(SUM(slot_row.`quantity`), 0) AS `TotalQuantity`
    FROM `god2_player`.`character_inventory` slot_row
    WHERE slot_row.`enabled` = 1
      AND slot_row.`deleted_at_utc` IS NULL
    GROUP BY slot_row.`character_id`
) inventory_count_row
    ON inventory_count_row.`character_id` = character_row.`character_id`
LEFT JOIN `god2_player`.`player_currency_balances` currency_row
    ON currency_row.`CharacterId` = character_row.`character_id`
LEFT JOIN (
    SELECT
        equipment_row.`character_id`,
        COUNT(*) AS `EquippedCount`,
        GROUP_CONCAT(
            CONCAT(
                CASE equipment_row.`equipment_slot`
                    WHEN 'Weapon' THEN '武器'
                    WHEN 'Armor' THEN '防具'
                    WHEN 'Helmet' THEN '頭盔'
                    WHEN 'Gloves' THEN '手套'
                    WHEN 'Shoes' THEN '鞋子'
                    ELSE CONVERT(equipment_row.`equipment_slot` USING utf8mb4)
                END,
                ':',
                COALESCE(equipment_row.`item_name_cache`, item_row.`name_zh_tw`, CAST(equipment_row.`item_id` AS CHAR))
            )
            ORDER BY equipment_row.`equipment_slot`
            SEPARATOR '、'
        ) AS `EquippedItemNames`
    FROM `god2_player`.`character_equipment` equipment_row
    LEFT JOIN `god2_game`.`items` item_row
        ON item_row.`item_id` = equipment_row.`item_id`
    WHERE equipment_row.`enabled` = 1
    GROUP BY equipment_row.`character_id`
) equipment_count_row
    ON equipment_count_row.`character_id` = character_row.`character_id`;
