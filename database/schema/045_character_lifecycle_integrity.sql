DROP INDEX IF EXISTS `UX_Characters_Name` ON `characters`;

ALTER TABLE `characters`
    ADD COLUMN IF NOT EXISTS `ActiveName` varchar(32)
        GENERATED ALWAYS AS (
            CASE WHEN `Status` <> 'Deleted' THEN `Name` ELSE NULL END
        ) STORED AFTER `Name`;

CREATE UNIQUE INDEX IF NOT EXISTS `UX_Characters_ActiveName`
    ON `characters` (`ActiveName`);

CREATE TABLE IF NOT EXISTS `character_lifecycle_idempotency` (
    `AccountId` bigint NOT NULL,
    `IdempotencyKeyHash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Operation` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `PayloadHash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ResultCode` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ResultJson` json NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`AccountId`, `IdempotencyKeyHash`),
    KEY `IX_CharacterLifecycleIdempotency_CreatedAtUtc` (`CreatedAtUtc`),
    CONSTRAINT `FK_CharacterLifecycleIdempotency_Account`
        FOREIGN KEY (`AccountId`) REFERENCES `accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `CK_CharacterLifecycleIdempotency_Operation`
        CHECK (`Operation` IN ('Create', 'Delete', 'Rename')),
    CONSTRAINT `CK_CharacterLifecycleIdempotency_ResultCode`
        CHECK (`ResultCode` = 'Success'),
    CONSTRAINT `CK_CharacterLifecycleIdempotency_Hashes`
        CHECK (CHAR_LENGTH(`IdempotencyKeyHash`) = 64 AND CHAR_LENGTH(`PayloadHash`) = 64)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
