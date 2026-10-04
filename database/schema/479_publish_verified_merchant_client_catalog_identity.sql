-- Publish the exact-current Server V2 merchant BUY catalog identity already
-- pinned by the historical Stage 4-7 runtime slice.
--
-- This migration also repairs the verified formal merchant_inventory row that
-- Migration 103 intended to publish. The original INSERT ... SELECT could
-- silently affect zero rows; no later migration intentionally removed it.
--
-- Runtime authority and evidence provenance remain separated:
--   god2_game     = executable merchant/catalog authority
--   god2_research = evidence status and provenance
--
-- This migration intentionally does not publish a SELL client-slot mapping.

CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- Fail closed unless the exact verified merchant and canonical client item
-- identities still exist and are enabled.
CREATE TEMPORARY TABLE `god2_game`.`god2_merchant_catalog_479_guard` (
    `ready` tinyint NOT NULL,
    CHECK (`ready` = 1)
);

INSERT INTO `god2_game`.`god2_merchant_catalog_479_guard` (`ready`)
SELECT CASE
    WHEN
        (SELECT COUNT(*)
         FROM `god2_game`.`merchants`
         WHERE `merchant_id` = 170015954
           AND `npc_id` = 170015081
           AND `enabled` = 1) = 1
    AND
        (SELECT COUNT(*)
         FROM `god2_game`.`items`
         WHERE `item_id` = 253231541
           AND `client_item_id` = 6901
           AND `enabled` = 1) = 1
    AND NOT EXISTS (
        SELECT 1
        FROM `god2_game`.`merchant_inventory`
        WHERE (`merchant_inventory_id` = 170156901
               OR (`merchant_id` = 170015954 AND `item_id` = 253231541))
          AND NOT (
              `merchant_inventory_id` = 170156901
              AND `merchant_id` = 170015954
              AND `item_id` = 253231541
          )
    )
    THEN 1
    ELSE 0
END;

-- Restore the exact verified formal listing from the historical Stage 5/6
-- runtime slice. Evidence-only columns were split out after Migration 103.
INSERT INTO `god2_game`.`merchant_inventory`
    (`merchant_inventory_id`,
     `merchant_id`,
     `merchant_name_cache`,
     `item_id`,
     `item_name_cache`,
     `display_order`,
     `selling_price`,
     `purchasing_price`,
     `pack_count`,
     `quantity_limit`,
     `enabled`)
SELECT
    170156901,
    170015954,
    'Observed Merchant 3954',
    item_row.`item_id`,
    item_row.`name_zh_tw`,
    1,
    40,
    4,
    1,
    NULL,
    1
FROM `god2_game`.`items` item_row
WHERE item_row.`item_id` = 253231541
  AND item_row.`client_item_id` = 6901
  AND item_row.`enabled` = 1
ON DUPLICATE KEY UPDATE
    `merchant_name_cache` = VALUES(`merchant_name_cache`),
    `item_name_cache` = VALUES(`item_name_cache`),
    `display_order` = VALUES(`display_order`),
    `selling_price` = VALUES(`selling_price`),
    `purchasing_price` = VALUES(`purchasing_price`),
    `pack_count` = VALUES(`pack_count`),
    `quantity_limit` = VALUES(`quantity_limit`),
    `enabled` = VALUES(`enabled`);

-- Do not allow the repair to succeed silently with zero rows or unexpected
-- authority values.
INSERT INTO `god2_game`.`god2_merchant_catalog_479_guard` (`ready`)
SELECT CASE
    WHEN (
        SELECT COUNT(*)
        FROM `god2_game`.`merchant_inventory`
        WHERE `merchant_inventory_id` = 170156901
          AND `merchant_id` = 170015954
          AND `item_id` = 253231541
          AND `display_order` = 1
          AND `selling_price` = 40
          AND `purchasing_price` = 4
          AND `pack_count` = 1
          AND `quantity_limit` IS NULL
          AND `enabled` = 1
    ) = 1
    THEN 1
    ELSE 0
END;

CREATE TABLE IF NOT EXISTS `god2_game`.`merchant_client_catalog_identities` (
    `client_build_id` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
        COMMENT '適用的正式客戶端版本 ID',
    `merchant_inventory_id` bigint NOT NULL
        COMMENT '對應的正式商店庫存列 ID',
    `client_catalog_index` int unsigned NOT NULL
        COMMENT '客戶端購買封包中的商店目錄索引',
    `client_item_id` bigint NOT NULL
        COMMENT '客戶端物品類型 ID；僅供 wire identity 驗證',
    `enabled` tinyint(1) NOT NULL DEFAULT 0
        COMMENT '是否允許 Server V2 使用此 mapping',
    PRIMARY KEY (`client_build_id`,`merchant_inventory_id`),
    KEY `ix_merchant_client_catalog_lookup`
        (`client_build_id`,`client_catalog_index`,`client_item_id`),
    CONSTRAINT `fk_merchant_client_catalog_inventory`
        FOREIGN KEY (`merchant_inventory_id`)
        REFERENCES `god2_game`.`merchant_inventory` (`merchant_inventory_id`),
    CONSTRAINT `ck_merchant_client_catalog_enabled`
        CHECK (`enabled` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='Server V2 商店購買封包的客戶端目錄索引至正式商店庫存 mapping';

CREATE TABLE IF NOT EXISTS `god2_research`.`merchant_client_catalog_identity_evidence` (
    `client_build_id` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `merchant_inventory_id` bigint NOT NULL,
    `evidence_status` varchar(32) NOT NULL,
    `evidence_authority` varchar(64) NOT NULL,
    `evidence_reference` varchar(512) NOT NULL,
    `admin_note` varchar(500) NULL,
    `archived_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6),
    PRIMARY KEY (`client_build_id`,`merchant_inventory_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='商店客戶端目錄 identity 的證據狀態與來源';

INSERT INTO `god2_game`.`merchant_client_catalog_identities`
    (`client_build_id`,
     `merchant_inventory_id`,
     `client_catalog_index`,
     `client_item_id`,
     `enabled`)
VALUES
    ('god2-opt-6b127086e0c0',170156901,7,6901,1)
ON DUPLICATE KEY UPDATE
    `client_catalog_index` = VALUES(`client_catalog_index`),
    `client_item_id` = VALUES(`client_item_id`),
    `enabled` = VALUES(`enabled`);

INSERT INTO `god2_research`.`merchant_client_catalog_identity_evidence`
    (`client_build_id`,
     `merchant_inventory_id`,
     `evidence_status`,
     `evidence_authority`,
     `evidence_reference`,
     `admin_note`)
VALUES
    ('god2-opt-6b127086e0c0',
     170156901,
     'Verified',
     'TW_OFFICIAL',
     'LiveRecovery/Stages4-7-attempt-759-trace',
     'Exact-current historical Stage 4-7 repository-pinned merchant evidence: handle 3954, client item 6901, canonical item 253231541, BUY catalog index 7. Original attempt-759 trace is not currently present on AWS; do not treat this row as a newly reverified raw capture.')
ON DUPLICATE KEY UPDATE
    `evidence_status` = VALUES(`evidence_status`),
    `evidence_authority` = VALUES(`evidence_authority`),
    `evidence_reference` = VALUES(`evidence_reference`),
    `admin_note` = VALUES(`admin_note`);

GRANT SELECT
ON `god2_game`.`merchant_client_catalog_identities`
TO `god2_runtime_role`;

DROP TEMPORARY TABLE `god2_game`.`god2_merchant_catalog_479_guard`;
