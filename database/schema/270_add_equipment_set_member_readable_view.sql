CREATE OR REPLACE VIEW `god2_game`.`vw_equipment_set_members_readable` AS
SELECT
    member_row.`SetId` AS `套裝ID`,
    set_row.`NameZhTw` AS `套裝名稱`,
    member_row.`ItemId` AS `道具ID`,
    COALESCE(item_row.`NameZhTw`, item_row.`DisplayName`, item_row.`Name`) AS `道具名稱`,
    member_row.`SlotName` AS `官方部位Key`,
    CASE member_row.`SlotName`
        WHEN 'Weapon' THEN '武器'
        WHEN 'Armor' THEN '防具'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Gloves' THEN '手套'
        WHEN 'Shoes' THEN '鞋子'
        ELSE member_row.`SlotName`
    END AS `部位功能`,
    CASE member_row.`ProductionRelationshipEnabled`
        WHEN 1 THEN '正式啟用'
        ELSE '證據保留'
    END AS `套裝關係狀態`,
    CASE set_row.`ProductionBonusEnabled`
        WHEN 1 THEN '套裝效果啟用'
        ELSE '套裝效果未啟用'
    END AS `套裝效果狀態`,
    member_row.`RunId` AS `證據批次`,
    member_row.`PromotionRunId` AS `正式提升批次`
FROM `god2`.`equipment_set_members` member_row
LEFT JOIN `god2`.`equipment_set_definitions` set_row
    ON set_row.`SetId` = member_row.`SetId`
LEFT JOIN `god2`.`items` item_row
    ON item_row.`Id` = member_row.`ItemId`;
