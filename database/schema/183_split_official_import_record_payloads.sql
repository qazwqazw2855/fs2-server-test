CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_record_payload_evidence` (
    `category` varchar(64) NOT NULL,
    `record_key` varchar(255) NOT NULL,
    `payload_json` longtext NOT NULL,
    `payload_sha256` char(64) NOT NULL,
    `moved_at_utc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`category`,`record_key`),
    CONSTRAINT `ck_official_import_payload_sha256`
        CHECK (CHAR_LENGTH(`payload_sha256`) = 64)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `god2_research`.`official_import_record_payload_evidence`
    (`category`,`record_key`,`payload_json`,`payload_sha256`)
SELECT `Category`,`RecordKey`,`PayloadJson`,`PayloadSha256`
FROM `official_import_records`
ON DUPLICATE KEY UPDATE
    `payload_json`=VALUES(`payload_json`),
    `payload_sha256`=VALUES(`payload_sha256`);

ALTER TABLE `official_import_records`
    DROP COLUMN IF EXISTS `PayloadJson`,
    DROP COLUMN IF EXISTS `PayloadSha256`;
