DROP VIEW IF EXISTS `god2`.`vw_items_readable`;
DROP VIEW IF EXISTS `god2`.`vw_item_content_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_container_item_relationships_readable`;
DROP VIEW IF EXISTS `god2`.`vw_equipment_sets_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_item_container_observations_readable`;

CREATE VIEW `god2`.`vw_items_readable` AS
SELECT
    item.`Id` AS `物品ID`,
    item.`Code` AS `服務端代碼`,
    COALESCE(NULLIF(item.`NameZhTw`, ''), NULLIF(item.`DisplayName`, ''), item.`Name`) AS `物品名稱`,
    item.`ItemCategory` AS `物品分類`,
    item.`ItemFamily` AS `物品家族`,
    item.`ItemType` AS `物品類型`,
    item.`EquipmentCategory` AS `裝備分類`,
    item.`ConsumableCategory` AS `消耗品分類`,
    item.`StackPolicy` AS `堆疊規則`,
    item.`MaxStack` AS `最大堆疊`,
    item.`BindPolicy` AS `綁定規則`,
    item.`TradePolicy` AS `交易規則`,
    item.`SellPolicy` AS `販售規則`,
    item.`BaseBuyPrice` AS `基礎買價`,
    COALESCE(item.`SellPrice`, item.`BaseSellPrice`) AS `賣價`,
    item.`CurrencyType` AS `貨幣類型`,
    CASE WHEN item.`QuestItemFlag` = 1 THEN '任務道具' ELSE '一般道具' END AS `任務道具狀態`,
    CASE WHEN item.`Enabled` = 1 THEN '正式可用' ELSE '正式未啟用' END AS `正式狀態`,
    item.`RecoveryStatus` AS `恢復狀態`,
    item.`LocalizationStatus` AS `在地化狀態`,
    CASE
        WHEN item.`EquipmentCategory` IS NOT NULL AND item.`EquipmentCategory` <> '' THEN '裝備穿脫、角色能力值與外觀'
        WHEN item.`ConsumableCategory` IS NOT NULL AND item.`ConsumableCategory` <> '' THEN '道具使用、恢復、增益或戰鬥消耗'
        WHEN item.`QuestItemFlag` = 1 THEN '任務取得、任務條件與任務交付'
        WHEN item.`ItemFamily` LIKE '%container%' OR item.`ItemCategory` LIKE '%container%' THEN '容器開啟與內容物發放'
        ELSE '背包持有、交易、倉庫與販售'
    END AS `功能對照`,
    item.`DescriptionZhTw` AS `繁中說明`,
    item.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`items` item;

CREATE VIEW `god2`.`vw_item_content_profiles_readable` AS
SELECT
    profile.`ItemId` AS `物品ID`,
    item_view.`物品名稱`,
    profile.`ClientItemId` AS `客戶端物品ID`,
    profile.`ItemFamily` AS `物品家族`,
    profile.`OfficialCategoryZhTw` AS `官方分類`,
    CASE WHEN profile.`Stackable` = 1 THEN '可堆疊' ELSE '不可堆疊或未證實' END AS `堆疊狀態`,
    profile.`MaximumStack` AS `最大堆疊`,
    profile.`TradePolicy` AS `交易規則`,
    profile.`WarehousePolicy` AS `倉庫規則`,
    profile.`EquipmentSlot` AS `裝備部位`,
    profile.`RequiredLevel` AS `需求等級`,
    profile.`PhysicalAttackBonus` AS `物攻加成`,
    profile.`MagicAttackBonus` AS `魔攻加成`,
    profile.`PhysicalDefenseBonus` AS `物防加成`,
    profile.`MagicDefenseBonus` AS `魔防加成`,
    profile.`HpBonus` AS `HP加成`,
    profile.`MpBonus` AS `MP加成`,
    profile.`SpeedBonus` AS `速度加成`,
    profile.`IconKey` AS `圖示鍵`,
    profile.`ModelKey` AS `模型鍵`,
    CASE
        WHEN profile.`EquipmentSlot` IS NOT NULL AND profile.`EquipmentSlot` <> '' THEN '裝備能力、裝備欄位與角色能力面板'
        WHEN profile.`OfficialCategoryZhTw` LIKE '%藥%' OR profile.`OfficialCategoryZhTw` LIKE '%丹%' THEN '道具使用、HP/MP恢復或狀態效果'
        WHEN profile.`ItemFamily` LIKE '%container%' THEN '容器開啟與抽取內容物'
        ELSE '物品分類、堆疊、交易、倉庫與客戶端顯示'
    END AS `功能對照`,
    profile.`RunId` AS `證據批次`,
    profile.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`item_content_profiles` profile
LEFT JOIN `god2`.`vw_items_readable` item_view
    ON item_view.`物品ID` = profile.`ItemId`;

CREATE VIEW `god2`.`vw_container_item_relationships_readable` AS
SELECT
    relationship.`RelationshipId` AS `關係ID`,
    relationship.`ContainerItemId` AS `容器物品ID`,
    COALESCE(container_item.`物品名稱`, CONCAT('物品#', relationship.`ContainerItemId`)) AS `容器名稱`,
    relationship.`ContainedItemId` AS `內容物品ID`,
    COALESCE(contained_item.`物品名稱`, CONCAT('物品#', relationship.`ContainedItemId`)) AS `內容物名稱`,
    relationship.`Quantity` AS `數量`,
    relationship.`DeclaredProbability` AS `宣告機率`,
    relationship.`EffectiveProbability` AS `有效機率`,
    relationship.`RelationshipStatus` AS `關係狀態`,
    CASE WHEN relationship.`Enabled` = 1 THEN '資料啟用' ELSE '資料未啟用' END AS `資料狀態`,
    CASE WHEN relationship.`ProductionRelationshipEnabled` = 1 THEN '正式內容關係啟用' ELSE '正式內容關係未啟用' END AS `正式內容狀態`,
    CASE WHEN relationship.`ProductionRecipeEnabled` = 1 THEN '正式合成配方啟用' ELSE '正式合成配方未啟用' END AS `正式配方狀態`,
    '容器開啟、禮包內容、合成材料或內容物發放' AS `功能對照`,
    relationship.`RunId` AS `證據批次`,
    relationship.`PromotionRunId` AS `正式提升批次`
FROM `god2`.`container_item_relationships` relationship
LEFT JOIN `god2`.`vw_items_readable` container_item
    ON container_item.`物品ID` = relationship.`ContainerItemId`
LEFT JOIN `god2`.`vw_items_readable` contained_item
    ON contained_item.`物品ID` = relationship.`ContainedItemId`;

CREATE VIEW `god2`.`vw_equipment_sets_readable` AS
SELECT
    set_definition.`SetId` AS `套裝ID`,
    set_definition.`NameZhTw` AS `套裝名稱`,
    set_definition.`RequiredPieces` AS `需求件數`,
    set_definition.`EffectsZhTw` AS `套裝效果`,
    COUNT(set_member.`ItemId`) AS `已知部件數`,
    GROUP_CONCAT(
        CONCAT(set_member.`SlotName`, ':', COALESCE(item_view.`物品名稱`, CONCAT('物品#', set_member.`ItemId`)))
        ORDER BY set_member.`SlotName`, set_member.`ItemId`
        SEPARATOR '、'
    ) AS `套裝部件`,
    set_definition.`SetRelationshipStatus` AS `套裝關係狀態`,
    CASE WHEN set_definition.`ProductionRelationshipEnabled` = 1 THEN '正式套裝關係啟用' ELSE '正式套裝關係未啟用' END AS `正式關係狀態`,
    CASE WHEN set_definition.`ProductionBonusEnabled` = 1 THEN '正式套裝加成啟用' ELSE '正式套裝加成未啟用' END AS `正式加成狀態`,
    '裝備套裝穿戴件數、套裝能力加成與角色能力面板' AS `功能對照`,
    set_definition.`RunId` AS `證據批次`,
    set_definition.`PromotionRunId` AS `正式提升批次`
FROM `god2`.`equipment_set_definitions` set_definition
LEFT JOIN `god2`.`equipment_set_members` set_member
    ON set_member.`SetId` = set_definition.`SetId`
LEFT JOIN `god2`.`vw_items_readable` item_view
    ON item_view.`物品ID` = set_member.`ItemId`
GROUP BY
    set_definition.`SetId`,
    set_definition.`NameZhTw`,
    set_definition.`RequiredPieces`,
    set_definition.`EffectsZhTw`,
    set_definition.`SetRelationshipStatus`,
    set_definition.`ProductionRelationshipEnabled`,
    set_definition.`ProductionBonusEnabled`,
    set_definition.`RunId`,
    set_definition.`PromotionRunId`;

CREATE VIEW `god2`.`vw_blackbox_item_container_observations_readable` AS
SELECT
    '道具使用' AS `觀察類型`,
    content.`物品ID` AS `主要ID`,
    content.`物品名稱` AS `名稱`,
    content.`官方分類` AS `分類`,
    content.`功能對照`,
    CASE
        WHEN content.`功能對照` LIKE '道具使用%' THEN '第一優先：測使用後HP/MP/狀態/戰鬥限制'
        WHEN content.`功能對照` LIKE '裝備能力%' THEN '第二優先：測穿脫後角色能力面板'
        ELSE '第三優先：測背包、交易、倉庫與顯示'
    END AS `黑箱測試優先級`,
    CONCAT('客戶端物品ID=', content.`客戶端物品ID`, '；需求等級=', COALESCE(content.`需求等級`, 0), '；裝備部位=', COALESCE(content.`裝備部位`, '')) AS `測試重點`
FROM `god2`.`vw_item_content_profiles_readable` content
WHERE content.`功能對照` NOT LIKE '容器開啟%'

UNION ALL

SELECT
    '容器內容' AS `觀察類型`,
    relationship.`容器物品ID` AS `主要ID`,
    relationship.`容器名稱` AS `名稱`,
    relationship.`內容物名稱` AS `分類`,
    relationship.`功能對照`,
    CASE
        WHEN relationship.`正式內容狀態` = '正式內容關係啟用' THEN '第一優先：測開啟後內容物與數量'
        ELSE '擱置：已有證據但正式關係未啟用'
    END AS `黑箱測試優先級`,
    CONCAT('內容物=', relationship.`內容物品ID`, '；數量=', COALESCE(relationship.`數量`, 0), '；有效機率=', relationship.`有效機率`) AS `測試重點`
FROM `god2`.`vw_container_item_relationships_readable` relationship

UNION ALL

SELECT
    '裝備套裝' AS `觀察類型`,
    equipment_set.`套裝ID` AS `主要ID`,
    equipment_set.`套裝名稱` AS `名稱`,
    CONCAT('需求', equipment_set.`需求件數`, '件') AS `分類`,
    equipment_set.`功能對照`,
    CASE
        WHEN equipment_set.`正式加成狀態` = '正式套裝加成啟用' THEN '第一優先：測穿戴件數與能力加成'
        ELSE '擱置：套裝證據已整理但正式加成未啟用'
    END AS `黑箱測試優先級`,
    CONCAT('部件=', COALESCE(equipment_set.`套裝部件`, ''), '；效果=', equipment_set.`套裝效果`) AS `測試重點`
FROM `god2`.`vw_equipment_sets_readable` equipment_set;
