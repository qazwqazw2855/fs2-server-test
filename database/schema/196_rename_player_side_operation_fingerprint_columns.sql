-- Rename player-side operation fingerprints to formal purpose names. These
-- fields are server idempotency controls, not evidence payloads.

ALTER TABLE `god2_player`.`character_lifecycle_idempotency`
    ADD COLUMN IF NOT EXISTS `lifecycle_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '角色生命週期操作 SHA-256' AFTER `operation`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'character_lifecycle_idempotency'
          AND COLUMN_NAME = 'payload_hash'
    ),
    'UPDATE `god2_player`.`character_lifecycle_idempotency` SET `lifecycle_fingerprint_sha256`=`payload_hash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`character_lifecycle_idempotency`
    MODIFY COLUMN `lifecycle_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '角色生命週期操作 SHA-256',
    DROP COLUMN IF EXISTS `payload_hash`;

ALTER TABLE `god2_player`.`equipment_enhancement_idempotency`
    ADD COLUMN IF NOT EXISTS `enhancement_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '裝備強化操作 SHA-256' AFTER `idempotency_key_hash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'equipment_enhancement_idempotency'
          AND COLUMN_NAME = 'payload_hash'
    ),
    'UPDATE `god2_player`.`equipment_enhancement_idempotency` SET `enhancement_fingerprint_sha256`=`payload_hash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`equipment_enhancement_idempotency`
    MODIFY COLUMN `enhancement_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '裝備強化操作 SHA-256',
    DROP COLUMN IF EXISTS `payload_hash`;

ALTER TABLE `god2_player`.`item_use_idempotency`
    ADD COLUMN IF NOT EXISTS `item_use_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '物品使用操作 SHA-256' AFTER `idempotency_key_hash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'item_use_idempotency'
          AND COLUMN_NAME = 'payload_hash'
    ),
    'UPDATE `god2_player`.`item_use_idempotency` SET `item_use_fingerprint_sha256`=`payload_hash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`item_use_idempotency`
    MODIFY COLUMN `item_use_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '物品使用操作 SHA-256',
    DROP COLUMN IF EXISTS `payload_hash`;

ALTER TABLE `god2_player`.`pet_operation_idempotency`
    ADD COLUMN IF NOT EXISTS `operation_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '寵物操作 SHA-256' AFTER `idempotency_key_hash`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'pet_operation_idempotency'
          AND COLUMN_NAME = 'payload_hash'
    ),
    'UPDATE `god2_player`.`pet_operation_idempotency` SET `operation_fingerprint_sha256`=`payload_hash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`pet_operation_idempotency`
    MODIFY COLUMN `operation_fingerprint_sha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '寵物操作 SHA-256',
    DROP COLUMN IF EXISTS `payload_hash`;

ALTER TABLE `god2_player`.`player_social_operations`
    ADD COLUMN IF NOT EXISTS `operation_fingerprint` varchar(200) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '' AFTER `operation_kind`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'player_social_operations'
          AND COLUMN_NAME = 'payload_fingerprint'
    ),
    'UPDATE `god2_player`.`player_social_operations` SET `operation_fingerprint`=`payload_fingerprint`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`player_social_operations`
    MODIFY COLUMN `operation_fingerprint` varchar(200) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DROP COLUMN IF EXISTS `payload_fingerprint`;

ALTER TABLE `god2_player`.`world_interaction_idempotency`
    ADD COLUMN IF NOT EXISTS `InteractionFingerprintSha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000' COMMENT '世界互動操作 SHA-256' AFTER `InteractionType`;

SET @god2_sql = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = 'god2_player'
          AND TABLE_NAME = 'world_interaction_idempotency'
          AND COLUMN_NAME = 'PayloadHash'
    ),
    'UPDATE `god2_player`.`world_interaction_idempotency` SET `InteractionFingerprintSha256`=`PayloadHash`',
    'SELECT 1'
);
PREPARE god2_stmt FROM @god2_sql;
EXECUTE god2_stmt;
DEALLOCATE PREPARE god2_stmt;

ALTER TABLE `god2_player`.`world_interaction_idempotency`
    MODIFY COLUMN `InteractionFingerprintSha256` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '世界互動操作 SHA-256',
    DROP COLUMN IF EXISTS `PayloadHash`;
