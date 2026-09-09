-- The verification fixture is infrastructure state, not player-authored data.
-- Move it out of god2_player so canonical player-table audit coverage remains
-- exact, then grant the runtime role DML on this single bounded fixture only.

CREATE TABLE IF NOT EXISTS `god2`.`runtime_verification_transactions` (
    `run_id` char(32) NOT NULL COMMENT '隔離每次驗證執行的隨機識別碼',
    `operation_key` varchar(160) NOT NULL COMMENT '案例內唯一且可重播的操作鍵',
    `category` varchar(64) NOT NULL COMMENT '驗證案例分類',
    `worker_id` int NOT NULL COMMENT '並行工作者編號',
    `state_version` bigint NOT NULL DEFAULT 0 COMMENT '樂觀並行控制版本',
    `payload_sha256` char(64) NOT NULL COMMENT '案例內容 SHA-256',
    `created_at_utc` datetime(6) NOT NULL COMMENT '建立時間（UTC）',
    PRIMARY KEY (`run_id`, `operation_key`),
    CONSTRAINT `ck_runtime_verification_state_version` CHECK (`state_version` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='正式執行帳號的有界交易、回滾、重播與恢復驗證夾具；每次執行後清除該 RunId';

INSERT IGNORE INTO `god2`.`runtime_verification_transactions`
    (`run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`payload_sha256`,`created_at_utc`)
SELECT `run_id`,`operation_key`,`category`,`worker_id`,`state_version`,`payload_sha256`,`created_at_utc`
FROM `god2_player`.`runtime_verification_transactions`;

DROP TABLE `god2_player`.`runtime_verification_transactions`;

GRANT SELECT, INSERT, UPDATE, DELETE
ON `god2`.`runtime_verification_transactions`
TO `god2_runtime_role`;
