-- Runtime verification transactions are test infrastructure, not formal gameplay data.
-- Keep the bounded fixture available to verification tooling, but move it out of
-- the formal god2 runtime schema so owner-facing server data stays clean.

CREATE TABLE IF NOT EXISTS `god2_research`.`runtime_verification_transactions` (
    `run_id` char(32) NOT NULL COMMENT '隔離每次驗證執行的隨機識別碼',
    `operation_key` varchar(160) NOT NULL COMMENT '案例內唯一且可重播的操作鍵',
    `category` varchar(64) NOT NULL COMMENT '驗證案例分類',
    `worker_id` int NOT NULL COMMENT '並行工作者編號',
    `state_version` bigint NOT NULL DEFAULT 0 COMMENT '樂觀並行控制版本',
    `verification_case_sha256` char(64) NOT NULL COMMENT '驗證案例 SHA-256',
    `created_at_utc` datetime(6) NOT NULL COMMENT '建立時間（UTC）',
    PRIMARY KEY (`run_id`, `operation_key`),
    CONSTRAINT `ck_runtime_verification_state_version` CHECK (`state_version` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='有界交易、回滾、重播與恢復驗證夾具；非正式遊戲內容，每次驗證後清除該 RunId';

SET @copy_runtime_verification_sql = IF(
    EXISTS (
        SELECT 1
        FROM `information_schema`.`TABLES`
        WHERE `TABLE_SCHEMA`='god2'
          AND `TABLE_NAME`='runtime_verification_transactions'
    ),
    'INSERT IGNORE INTO `god2_research`.`runtime_verification_transactions`
        (`run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`verification_case_sha256`,`created_at_utc`)
     SELECT `run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`verification_case_sha256`,`created_at_utc`
     FROM `god2`.`runtime_verification_transactions`',
    'DO 0'
);
PREPARE copy_runtime_verification_stmt FROM @copy_runtime_verification_sql;
EXECUTE copy_runtime_verification_stmt;
DEALLOCATE PREPARE copy_runtime_verification_stmt;

DROP TABLE IF EXISTS `god2`.`runtime_verification_transactions`;

GRANT SELECT, INSERT, UPDATE, DELETE
ON `god2_research`.`runtime_verification_transactions`
TO `god2_runtime_role`;
