CREATE OR REPLACE VIEW `god2_game`.`vw_player_assets_readable` AS
SELECT
    character_row.`Id` AS `角色ID`,
    character_row.`Name` AS `角色名稱`,
    character_row.`AccountId` AS `帳號ID`,
    COALESCE(inventory_state_row.`InventoryId`, '尚未建立') AS `背包ID`,
    COALESCE(inventory_state_row.`Capacity`, 0) AS `背包容量`,
    COALESCE(inventory_state_row.`InventoryVersion`, 0) AS `背包版本`,
    COALESCE(inventory_state_row.`MutationSequence`, 0) AS `背包異動序號`,
    CASE
        WHEN inventory_state_row.`CharacterId` IS NULL THEN '背包尚未建立'
        WHEN inventory_state_row.`DirtyState` = '乾淨' THEN '背包狀態乾淨'
        WHEN inventory_state_row.`DirtyState` = 'Dirty' THEN '背包有未同步異動'
        ELSE COALESCE(inventory_state_row.`DirtyState`, '背包狀態未標示')
    END AS `背包狀態`,
    COALESCE(inventory_count_row.`ItemStackCount`, 0) AS `背包物品格數`,
    COALESCE(inventory_count_row.`TotalQuantity`, 0) AS `背包物品總數量`,
    COALESCE(currency_row.`CurrencyType`, 'Gold') AS `貨幣Key`,
    CASE COALESCE(currency_row.`CurrencyType`, 'Gold')
        WHEN 'Gold' THEN '金錢'
        ELSE COALESCE(currency_row.`CurrencyType`, '未知貨幣')
    END AS `貨幣`,
    COALESCE(currency_row.`Balance`, 0) AS `餘額`,
    COALESCE(currency_row.`Version`, 0) AS `錢包版本`,
    CASE
        WHEN currency_row.`CharacterId` IS NULL THEN '錢包尚未建立'
        WHEN currency_row.`DirtyState` = '乾淨' THEN '錢包狀態乾淨'
        WHEN currency_row.`DirtyState` = 'Dirty' THEN '錢包有未同步異動'
        ELSE COALESCE(currency_row.`DirtyState`, '錢包狀態未標示')
    END AS `錢包狀態`,
    COALESCE(equipment_count_row.`EquippedCount`, 0) AS `已裝備格數`,
    COALESCE(equipment_count_row.`EquippedItemNames`, '無已裝備道具') AS `已裝備道具`,
    character_row.`UpdatedAtUtc` AS `角色更新時間UTC`,
    inventory_state_row.`UpdatedAtUtc` AS `背包更新時間UTC`,
    currency_row.`UpdatedAtUtc` AS `錢包更新時間UTC`
FROM `god2`.`characters` character_row
LEFT JOIN `god2`.`player_inventory_state` inventory_state_row
    ON inventory_state_row.`CharacterId` = character_row.`Id`
LEFT JOIN (
    SELECT
        slot_row.`CharacterId`,
        COUNT(*) AS `ItemStackCount`,
        COALESCE(SUM(slot_row.`Quantity`), 0) AS `TotalQuantity`
    FROM `god2`.`inventory_slots` slot_row
    WHERE slot_row.`DeletedAtUtc` IS NULL
    GROUP BY slot_row.`CharacterId`
) inventory_count_row
    ON inventory_count_row.`CharacterId` = character_row.`Id`
LEFT JOIN `god2`.`player_currency_balances` currency_row
    ON currency_row.`CharacterId` = character_row.`Id`
LEFT JOIN (
    SELECT
        equipment_row.`CharacterId`,
        COUNT(*) AS `EquippedCount`,
        GROUP_CONCAT(
            CONCAT(
                CASE equipment_row.`SlotName`
                    WHEN 'Weapon' THEN '武器'
                    WHEN 'Armor' THEN '防具'
                    WHEN 'Helmet' THEN '頭盔'
                    WHEN 'Gloves' THEN '手套'
                    WHEN 'Shoes' THEN '鞋子'
                    ELSE equipment_row.`SlotName`
                END,
                ':',
                COALESCE(item_row.`NameZhTw`, item_row.`DisplayName`, item_row.`Name`, CAST(equipment_row.`ItemId` AS CHAR))
            )
            ORDER BY equipment_row.`SlotName`
            SEPARATOR '、'
        ) AS `EquippedItemNames`
    FROM `god2`.`equipment_slots` equipment_row
    LEFT JOIN `god2`.`items` item_row
        ON item_row.`Id` = equipment_row.`ItemId`
    GROUP BY equipment_row.`CharacterId`
) equipment_count_row
    ON equipment_count_row.`CharacterId` = character_row.`Id`;
