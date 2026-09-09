DROP VIEW IF EXISTS `god2_player`.`vw_character_inventory_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_character_equipment_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_player_currency_balances_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_inventory_audit_ledger_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_blackbox_inventory_operation_observations_readable`;

CREATE VIEW `god2_player`.`vw_character_inventory_readable` AS
SELECT
    inventory.`character_id` AS `角色ID`,
    inventory.`slot_index` AS `背包格位`,
    inventory.`inventory_id` AS `背包項目ID`,
    inventory.`protocol_visible_item_id` AS `封包顯示項目ID`,
    inventory.`item_id` AS `物品ID`,
    inventory.`item_name_cache` AS `物品名稱`,
    inventory.`quantity` AS `數量`,
    inventory.`inventory_version` AS `背包版本`,
    inventory.`slot_version` AS `格位版本`,
    CASE WHEN inventory.`bound` = 1 THEN '綁定' ELSE '未綁定' END AS `綁定狀態`,
    COALESCE(inventory.`bind_state`, '') AS `綁定細節`,
    CASE WHEN inventory.`enabled` = 1 AND inventory.`deleted_at_utc` IS NULL THEN '有效' ELSE '停用或已刪除' END AS `服務端狀態`,
    inventory.`updated_at_utc` AS `更新時間UTC`
FROM `god2_player`.`character_inventory` inventory;

CREATE VIEW `god2_player`.`vw_character_equipment_readable` AS
SELECT
    equipment.`character_id` AS `角色ID`,
    equipment.`equipment_slot` AS `裝備欄位`,
    equipment.`inventory_id` AS `背包項目ID`,
    equipment.`item_id` AS `物品ID`,
    equipment.`item_name_cache` AS `裝備名稱`,
    equipment.`enhancement_level` AS `強化等級`,
    equipment.`durability` AS `耐久`,
    instance.`maximum_durability` AS `最大耐久`,
    instance.`socket_count` AS `孔數`,
    instance.`metal_bonus` AS `金加成`,
    instance.`wood_bonus` AS `木加成`,
    instance.`water_bonus` AS `水加成`,
    instance.`fire_bonus` AS `火加成`,
    instance.`earth_bonus` AS `土加成`,
    CASE WHEN equipment.`enabled` = 1 THEN '已穿戴' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_player`.`character_equipment` equipment
LEFT JOIN `god2_player`.`equipment_instances` instance
    ON instance.`inventory_id` = equipment.`inventory_id`;

CREATE VIEW `god2_player`.`vw_player_currency_balances_readable` AS
SELECT
    balance.`CharacterId` AS `角色ID`,
    balance.`CurrencyType` AS `貨幣類型`,
    balance.`Balance` AS `餘額`,
    balance.`Version` AS `版本`,
    balance.`DirtyState` AS `同步狀態`,
    balance.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2_player`.`player_currency_balances` balance;

CREATE VIEW `god2_player`.`vw_inventory_audit_ledger_readable` AS
SELECT
    audit.`AuditId` AS `審計ID`,
    audit.`TransactionId` AS `交易ID`,
    audit.`CharacterId` AS `角色ID`,
    audit.`SessionId` AS `連線ID`,
    audit.`OperationType` AS `操作類型`,
    audit.`Source` AS `來源`,
    audit.`MerchantTemplateId` AS `商人模板ID`,
    audit.`ItemTemplateId` AS `物品模板ID`,
    audit.`InventoryItemId` AS `背包項目ID`,
    audit.`QuantityBefore` AS `操作前數量`,
    audit.`QuantityAfter` AS `操作後數量`,
    audit.`CurrencyType` AS `貨幣類型`,
    audit.`CurrencyBefore` AS `操作前貨幣`,
    audit.`CurrencyAfter` AS `操作後貨幣`,
    audit.`InventoryVersionBefore` AS `操作前背包版本`,
    audit.`InventoryVersionAfter` AS `操作後背包版本`,
    audit.`Result` AS `結果`,
    audit.`FailureCode` AS `失敗代碼`,
    audit.`CreatedAtUtc` AS `建立時間UTC`,
    audit.`CompletedAtUtc` AS `完成時間UTC`,
    audit.`CorrelationId` AS `關聯ID`
FROM `god2_player`.`inventory_audit_ledger` audit;

CREATE VIEW `god2_player`.`vw_blackbox_inventory_operation_observations_readable` AS
SELECT
    audit.`CharacterId` AS `角色ID`,
    audit.`OperationType` AS `操作類型`,
    audit.`Source` AS `來源`,
    audit.`ItemTemplateId` AS `物品模板ID`,
    COALESCE(item_info.`name_zh_tw`, audit.`ItemTemplateId`) AS `物品名稱`,
    audit.`QuantityBefore` AS `操作前數量`,
    audit.`QuantityAfter` AS `操作後數量`,
    audit.`CurrencyType` AS `貨幣類型`,
    audit.`CurrencyBefore` AS `操作前貨幣`,
    audit.`CurrencyAfter` AS `操作後貨幣`,
    audit.`InventoryVersionBefore` AS `操作前背包版本`,
    audit.`InventoryVersionAfter` AS `操作後背包版本`,
    audit.`Result` AS `結果`,
    audit.`FailureCode` AS `失敗代碼`,
    CASE
        WHEN audit.`OperationType` LIKE '%Merchant%' OR audit.`Source` LIKE '%Merchant%' THEN '測商店買賣與金錢扣加'
        WHEN audit.`OperationType` LIKE '%Use%' THEN '測道具使用、消耗、HP/MP或狀態變化'
        WHEN audit.`OperationType` LIKE '%Equip%' THEN '測穿裝卸裝與面板能力變化'
        WHEN audit.`OperationType` LIKE '%Enhance%' THEN '測強化成功失敗、材料消耗、耐久變化'
        ELSE '測背包格位、數量、版本與重放保護'
    END AS `功能對照`,
    audit.`CompletedAtUtc` AS `完成時間UTC`
FROM `god2_player`.`inventory_audit_ledger` audit
LEFT JOIN `god2_game`.`items` item_info
    ON item_info.`item_id` = audit.`ItemTemplateId`;
