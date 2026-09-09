CREATE OR REPLACE VIEW `god2_game`.`vw_container_item_relationships_readable` AS
SELECT
    relationship_row.`RelationshipId` AS `關係ID`,
    relationship_row.`ContainerItemId` AS `容器道具ID`,
    COALESCE(container_item_row.`NameZhTw`, container_item_row.`DisplayName`, container_item_row.`Name`) AS `容器道具名稱`,
    relationship_row.`ContainedItemId` AS `內容物道具ID`,
    COALESCE(contained_item_row.`NameZhTw`, contained_item_row.`DisplayName`, contained_item_row.`Name`) AS `內容物名稱`,
    relationship_row.`Quantity` AS `數量`,
    relationship_row.`DeclaredProbability` AS `宣告機率`,
    relationship_row.`EffectiveProbability` AS `有效機率`,
    relationship_row.`RelationshipStatus` AS `關係狀態Key`,
    CASE relationship_row.`RelationshipStatus`
        WHEN 'Candidate' THEN '候選內容證據'
        WHEN 'Verified' THEN '已驗證內容關係'
        WHEN 'EvidenceBlocked' THEN '證據不足保留'
        ELSE relationship_row.`RelationshipStatus`
    END AS `關係狀態`,
    CASE relationship_row.`Enabled`
        WHEN 1 THEN '一般啟用'
        WHEN 0 THEN '未啟用'
        ELSE '未標示'
    END AS `一般啟用狀態`,
    CASE relationship_row.`ProductionRelationshipEnabled`
        WHEN 1 THEN '正式內容關係啟用'
        WHEN 0 THEN '正式內容關係未啟用'
        ELSE '未標示'
    END AS `正式內容狀態`,
    CASE relationship_row.`ProductionRecipeEnabled`
        WHEN 1 THEN '正式配方啟用'
        WHEN 0 THEN '正式配方未啟用'
        ELSE '未標示'
    END AS `正式配方狀態`,
    relationship_row.`RunId` AS `證據批次`,
    relationship_row.`PromotionRunId` AS `正式提升批次`
FROM `god2`.`container_item_relationships` relationship_row
LEFT JOIN `god2`.`items` container_item_row
    ON container_item_row.`Id` = relationship_row.`ContainerItemId`
LEFT JOIN `god2`.`items` contained_item_row
    ON contained_item_row.`Id` = relationship_row.`ContainedItemId`;
