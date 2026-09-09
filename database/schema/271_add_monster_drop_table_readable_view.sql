CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_monster_drop_tables_readable` AS
SELECT
    drop_table_row.`Id` AS `掉落表ID`,
    drop_table_row.`Name` AS `官方來源Key`,
    CASE
        WHEN drop_table_row.`Name` LIKE 'client:drop-table-candidate/client-monster/fight-eny/%'
            THEN CONCAT('客戶端怪物掉落候選表 ', SUBSTRING_INDEX(drop_table_row.`Name`, '/', -1))
        ELSE drop_table_row.`Name`
    END AS `掉落表功能`,
    drop_table_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE drop_table_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE drop_table_row.`RecoveryStatus`
    END AS `回收狀態`,
    drop_table_row.`MonsterId` AS `怪物ID`,
    COALESCE(monster_row.`NameZhTw`, monster_row.`Name`) AS `怪物名稱`,
    relationship_row.`ItemId` AS `道具ID`,
    COALESCE(item_row.`NameZhTw`, item_row.`DisplayName`, item_row.`Name`) AS `掉落道具`,
    relationship_row.`MinimumQuantity` AS `最小數量`,
    relationship_row.`MaximumQuantity` AS `最大數量`,
    relationship_row.`DeclaredDropChance` AS `宣告掉落率`,
    relationship_row.`EffectiveDropChance` AS `有效掉落率`,
    relationship_row.`Weight` AS `權重`,
    relationship_row.`RollType` AS `擲骰Key`,
    CASE relationship_row.`RollType`
        WHEN 'Independent' THEN '各自獨立判定'
        WHEN 'Weighted' THEN '權重抽選'
        ELSE relationship_row.`RollType`
    END AS `擲骰方式`,
    CASE relationship_row.`Guaranteed`
        WHEN 1 THEN '保證掉落'
        WHEN 0 THEN '非保證掉落'
        ELSE '未標示'
    END AS `保證掉落`,
    relationship_row.`DropRelationshipStatus` AS `掉落關係狀態Key`,
    CASE relationship_row.`ProductionDropEnabled`
        WHEN 1 THEN '正式啟用'
        WHEN 0 THEN '證據保留'
        ELSE '未標示'
    END AS `正式掉落狀態`,
    relationship_row.`RunId` AS `證據批次`,
    relationship_row.`PromotionRunId` AS `正式提升批次`
FROM `god2`.`drop_tables` drop_table_row
LEFT JOIN `god2`.`monsters` monster_row
    ON monster_row.`Id` = drop_table_row.`MonsterId`
LEFT JOIN `god2`.`monster_drop_relationships` relationship_row
    ON relationship_row.`DropTableId` = drop_table_row.`Id`
LEFT JOIN `god2`.`items` item_row
    ON item_row.`Id` = relationship_row.`ItemId`;
