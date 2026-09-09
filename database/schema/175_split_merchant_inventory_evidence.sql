CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_inventory_evidence` (
    `merchant_inventory_id` bigint NOT NULL,
    `evidence_status` varchar(30) NOT NULL,
    `admin_note` varchar(500) NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`merchant_inventory_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`merchant_inventory_evidence`
    (`merchant_inventory_id`,`evidence_status`,`admin_note`)
SELECT `merchant_inventory_id`,`evidence_status`,`admin_note`
FROM `god2_game`.`merchant_inventory`
ON DUPLICATE KEY UPDATE
    `evidence_status`=VALUES(`evidence_status`),
    `admin_note`=VALUES(`admin_note`);

DROP VIEW IF EXISTS `god2_game`.`vw_merchant_inventory_full`;

ALTER TABLE `god2_game`.`merchant_inventory`
    DROP COLUMN IF EXISTS `evidence_status`,
    DROP COLUMN IF EXISTS `admin_note`;

CREATE OR REPLACE VIEW `god2_game`.`vw_merchant_inventory_full` AS
SELECT
    inventory_row.`merchant_inventory_id` AS `庫存編號`,
    inventory_row.`merchant_id` AS `商店編號`,
    merchant_row.`name_zh_tw` AS `商店名稱`,
    inventory_row.`item_id` AS `物品編號`,
    item_row.`name_zh_tw` AS `物品名稱`,
    inventory_row.`display_order` AS `顯示順序`,
    inventory_row.`selling_price` AS `售價`,
    inventory_row.`purchasing_price` AS `回收價`,
    inventory_row.`pack_count` AS `包裝數量`,
    inventory_row.`quantity_limit` AS `數量限制`,
    CASE inventory_row.`enabled` WHEN 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`merchant_inventory` inventory_row
JOIN `god2_game`.`merchants` merchant_row ON merchant_row.`merchant_id`=inventory_row.`merchant_id`
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=inventory_row.`item_id`;
