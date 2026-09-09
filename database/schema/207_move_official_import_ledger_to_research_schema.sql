-- Official import ledgers are import evidence and operational bookkeeping, not
-- formal gameplay runtime data. Move them out of god2 so the formal schema only
-- keeps server-facing runtime tables.

CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_categories` (
    `Category` varchar(64) NOT NULL,
    `SourcePath` varchar(512) NOT NULL,
    `Format` varchar(64) NOT NULL,
    `SourceSizeBytes` bigint NOT NULL,
    `RecordCount` int NOT NULL,
    `VerificationStatus` varchar(128) NOT NULL,
    `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered',
    `CanDirectImportToGameplay` tinyint(1) NOT NULL,
    `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy',
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='官方資料匯入分類帳本；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_records` (
    `Category` varchar(64) NOT NULL,
    `RecordKey` varchar(255) NOT NULL,
    `SourcePath` varchar(512) NOT NULL,
    `Format` varchar(64) NOT NULL,
    `VerificationStatus` varchar(128) NOT NULL,
    `RecoveryStatus` varchar(32) NOT NULL DEFAULT 'Recovered',
    `CanDirectImportToGameplay` tinyint(1) NOT NULL,
    `ImportBatch` varchar(32) NOT NULL DEFAULT 'legacy',
    `OriginalId` varchar(512) NOT NULL DEFAULT '',
    `SourceHash` char(64) NOT NULL DEFAULT '',
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Category`, `RecordKey`),
    CONSTRAINT `FK_ResearchOfficialImportRecords_Categories`
        FOREIGN KEY (`Category`) REFERENCES `god2_research`.`official_import_categories` (`Category`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='官方資料匯入逐筆帳本；非正式遊戲 runtime 資料';

CREATE TABLE IF NOT EXISTS `god2_research`.`official_import_reference_issues` (
    `Category` varchar(64) NOT NULL,
    `RecordKey` varchar(255) NOT NULL,
    `IssueCode` varchar(96) NOT NULL,
    `IssueMessage` varchar(512) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Category`, `RecordKey`, `IssueCode`),
    CONSTRAINT `FK_ResearchOfficialImportReferenceIssues_Records`
        FOREIGN KEY (`Category`, `RecordKey`) REFERENCES `god2_research`.`official_import_records` (`Category`, `RecordKey`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='官方資料匯入參照問題帳本；非正式遊戲 runtime 資料';

SET @copy_official_import_categories_sql = IF(
    EXISTS (
        SELECT 1 FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2' AND `TABLE_NAME`='official_import_categories'
    ),
    'INSERT INTO `god2_research`.`official_import_categories`
        (`Category`,`SourcePath`,`Format`,`SourceSizeBytes`,`RecordCount`,`VerificationStatus`,
         `RecoveryStatus`,`CanDirectImportToGameplay`,`ImportBatch`,`ImportedAtUtc`)
     SELECT `Category`,`SourcePath`,`Format`,`SourceSizeBytes`,`RecordCount`,`VerificationStatus`,
            `RecoveryStatus`,`CanDirectImportToGameplay`,`ImportBatch`,`ImportedAtUtc`
     FROM `god2`.`official_import_categories`
     ON DUPLICATE KEY UPDATE
        `SourcePath`=VALUES(`SourcePath`),
        `Format`=VALUES(`Format`),
        `SourceSizeBytes`=VALUES(`SourceSizeBytes`),
        `RecordCount`=VALUES(`RecordCount`),
        `VerificationStatus`=VALUES(`VerificationStatus`),
        `RecoveryStatus`=VALUES(`RecoveryStatus`),
        `CanDirectImportToGameplay`=VALUES(`CanDirectImportToGameplay`),
        `ImportBatch`=VALUES(`ImportBatch`),
        `ImportedAtUtc`=VALUES(`ImportedAtUtc`)',
    'DO 0'
);
PREPARE copy_official_import_categories_stmt FROM @copy_official_import_categories_sql;
EXECUTE copy_official_import_categories_stmt;
DEALLOCATE PREPARE copy_official_import_categories_stmt;

SET @copy_official_import_records_sql = IF(
    EXISTS (
        SELECT 1 FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2' AND `TABLE_NAME`='official_import_records'
    ),
    'INSERT INTO `god2_research`.`official_import_records`
        (`Category`,`RecordKey`,`SourcePath`,`Format`,`VerificationStatus`,`RecoveryStatus`,
         `CanDirectImportToGameplay`,`ImportBatch`,`OriginalId`,`SourceHash`,`ImportedAtUtc`)
     SELECT `Category`,`RecordKey`,`SourcePath`,`Format`,`VerificationStatus`,`RecoveryStatus`,
            `CanDirectImportToGameplay`,`ImportBatch`,`OriginalId`,`SourceHash`,`ImportedAtUtc`
     FROM `god2`.`official_import_records`
     ON DUPLICATE KEY UPDATE
        `SourcePath`=VALUES(`SourcePath`),
        `Format`=VALUES(`Format`),
        `VerificationStatus`=VALUES(`VerificationStatus`),
        `RecoveryStatus`=VALUES(`RecoveryStatus`),
        `CanDirectImportToGameplay`=VALUES(`CanDirectImportToGameplay`),
        `ImportBatch`=VALUES(`ImportBatch`),
        `OriginalId`=VALUES(`OriginalId`),
        `SourceHash`=VALUES(`SourceHash`),
        `ImportedAtUtc`=VALUES(`ImportedAtUtc`)',
    'DO 0'
);
PREPARE copy_official_import_records_stmt FROM @copy_official_import_records_sql;
EXECUTE copy_official_import_records_stmt;
DEALLOCATE PREPARE copy_official_import_records_stmt;

SET @copy_official_import_reference_issues_sql = IF(
    EXISTS (
        SELECT 1 FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2' AND `TABLE_NAME`='official_import_reference_issues'
    ),
    'INSERT INTO `god2_research`.`official_import_reference_issues`
        (`Category`,`RecordKey`,`IssueCode`,`IssueMessage`,`CreatedAtUtc`)
     SELECT `Category`,`RecordKey`,`IssueCode`,`IssueMessage`,`CreatedAtUtc`
     FROM `god2`.`official_import_reference_issues`
     ON DUPLICATE KEY UPDATE
        `IssueMessage`=VALUES(`IssueMessage`),
        `CreatedAtUtc`=VALUES(`CreatedAtUtc`)',
    'DO 0'
);
PREPARE copy_official_import_reference_issues_stmt FROM @copy_official_import_reference_issues_sql;
EXECUTE copy_official_import_reference_issues_stmt;
DEALLOCATE PREPARE copy_official_import_reference_issues_stmt;

DROP TABLE IF EXISTS `god2`.`official_import_reference_issues`;
DROP TABLE IF EXISTS `god2`.`official_import_records`;
DROP TABLE IF EXISTS `god2`.`official_import_categories`;
