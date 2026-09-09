DROP VIEW IF EXISTS `god2_game_meta`.`vw_admin_change_audit_readable`;
DROP VIEW IF EXISTS `god2_game_meta`.`vw_admin_field_locks_readable`;
DROP VIEW IF EXISTS `god2_game_meta`.`vw_catalog_validation_readable`;
DROP VIEW IF EXISTS `god2_game_meta`.`vw_runtime_catalog_releases_readable`;
DROP VIEW IF EXISTS `god2_game_meta`.`vw_admin_change_audit_summary_readable`;
DROP VIEW IF EXISTS `god2_game_meta`.`vw_admin_field_locks_summary_readable`;

CREATE VIEW `god2_game_meta`.`vw_admin_change_audit_readable` AS
SELECT
    audit.`audit_id` AS `審計ID`,
    audit.`entity_schema` AS `資料庫`,
    audit.`entity_table` AS `資料表`,
    audit.`entity_id` AS `資料ID`,
    audit.`field_name` AS `欄位名稱`,
    CASE
        WHEN audit.`old_value` IS NULL THEN '空值'
        WHEN CHAR_LENGTH(audit.`old_value`) > 120 THEN CONCAT(SUBSTRING(audit.`old_value`, 1, 120), '...')
        ELSE audit.`old_value`
    END AS `舊值摘要`,
    CASE
        WHEN audit.`new_value` IS NULL THEN '空值'
        WHEN CHAR_LENGTH(audit.`new_value`) > 120 THEN CONCAT(SUBSTRING(audit.`new_value`, 1, 120), '...')
        ELSE audit.`new_value`
    END AS `新值摘要`,
    audit.`changed_by` AS `異動者`,
    audit.`change_source` AS `異動來源`,
    audit.`note` AS `備註`,
    CASE
        WHEN audit.`change_source` LIKE '%migration%' THEN '資料庫 migration 異動'
        WHEN audit.`change_source` LIKE '%admin%' THEN '管理工具手動異動'
        WHEN audit.`change_source` LIKE '%import%' THEN '資料匯入異動'
        ELSE '正式資料異動審計'
    END AS `功能對照`,
    audit.`changed_at_utc` AS `異動時間UTC`
FROM `god2_game_meta`.`admin_change_audit` audit;

CREATE VIEW `god2_game_meta`.`vw_admin_field_locks_readable` AS
SELECT
    lock_row.`entity_schema` AS `資料庫`,
    lock_row.`entity_table` AS `資料表`,
    lock_row.`entity_id` AS `資料ID`,
    lock_row.`field_name` AS `欄位名稱`,
    lock_row.`locked_value_hash` AS `鎖定值Hash`,
    lock_row.`locked_by` AS `鎖定者`,
    lock_row.`note` AS `備註`,
    '保護已確認欄位，防止後續匯入或重建覆蓋正式值' AS `功能對照`,
    lock_row.`locked_at_utc` AS `鎖定時間UTC`
FROM `god2_game_meta`.`admin_field_locks` lock_row;

CREATE VIEW `god2_game_meta`.`vw_catalog_validation_readable` AS
SELECT
    validation.`validation_id` AS `驗證ID`,
    validation.`validation_run_id` AS `驗證批次ID`,
    validation.`validated_at_utc` AS `驗證時間UTC`,
    validation.`severity` AS `嚴重程度`,
    validation.`entity_schema` AS `資料庫`,
    validation.`entity_table` AS `資料表`,
    validation.`entity_id` AS `資料ID`,
    validation.`field_name` AS `欄位名稱`,
    validation.`rule_code` AS `規則代碼`,
    validation.`message_zh_tw` AS `繁中訊息`,
    CASE
        WHEN validation.`severity` IN ('Error', 'Fatal') THEN '必須修復'
        WHEN validation.`severity` = 'Warning' THEN '需確認'
        ELSE '資訊'
    END AS `處理建議`
FROM `god2_game_meta`.`catalog_validation` validation;

CREATE VIEW `god2_game_meta`.`vw_runtime_catalog_releases_readable` AS
SELECT
    release_row.`catalog_release_id` AS `目錄版本ID`,
    release_row.`catalog_fingerprint` AS `目錄指紋`,
    release_row.`catalog_record_count` AS `目錄資料筆數`,
    release_row.`status` AS `版本狀態`,
    CASE WHEN release_row.`active_slot` = 1 THEN '目前啟用' ELSE '未啟用' END AS `啟用狀態`,
    release_row.`validation_run_id` AS `驗證批次ID`,
    release_row.`builder_version` AS `建置器版本`,
    release_row.`failure_message` AS `失敗訊息`,
    '正式 runtime catalog 發佈與啟用紀錄' AS `功能對照`,
    release_row.`built_at_utc` AS `建置時間UTC`,
    release_row.`activated_at_utc` AS `啟用時間UTC`
FROM `god2_game_meta`.`runtime_catalog_releases` release_row;

CREATE VIEW `god2_game_meta`.`vw_admin_change_audit_summary_readable` AS
SELECT
    audit.`entity_schema` AS `資料庫`,
    audit.`entity_table` AS `資料表`,
    audit.`field_name` AS `欄位名稱`,
    COUNT(*) AS `異動次數`,
    MIN(audit.`changed_at_utc`) AS `第一次異動UTC`,
    MAX(audit.`changed_at_utc`) AS `最後異動UTC`,
    GROUP_CONCAT(DISTINCT audit.`change_source` ORDER BY audit.`change_source` SEPARATOR '；') AS `異動來源摘要`,
    '查看正式資料哪些欄位曾被批次匯入、管理工具或 migration 異動' AS `功能對照`
FROM `god2_game_meta`.`admin_change_audit` audit
GROUP BY audit.`entity_schema`, audit.`entity_table`, audit.`field_name`;

CREATE VIEW `god2_game_meta`.`vw_admin_field_locks_summary_readable` AS
SELECT
    lock_row.`entity_schema` AS `資料庫`,
    lock_row.`entity_table` AS `資料表`,
    lock_row.`field_name` AS `欄位名稱`,
    COUNT(*) AS `鎖定筆數`,
    MIN(lock_row.`locked_at_utc`) AS `第一次鎖定UTC`,
    MAX(lock_row.`locked_at_utc`) AS `最後鎖定UTC`,
    GROUP_CONCAT(DISTINCT lock_row.`locked_by` ORDER BY lock_row.`locked_by` SEPARATOR '；') AS `鎖定者摘要`,
    '查看哪些正式欄位受保護，避免證據資料被後續匯入覆蓋' AS `功能對照`
FROM `god2_game_meta`.`admin_field_locks` lock_row
GROUP BY lock_row.`entity_schema`, lock_row.`entity_table`, lock_row.`field_name`;
