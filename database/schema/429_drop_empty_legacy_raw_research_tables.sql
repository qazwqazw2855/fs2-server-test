USE god2_game;

CREATE TABLE IF NOT EXISTS god2_research.database_cleanup_audit (
    cleanup_key VARCHAR(128) NOT NULL PRIMARY KEY,
    cleanup_action_zh_tw VARCHAR(256) NOT NULL,
    affected_schema VARCHAR(64) NOT NULL,
    affected_table VARCHAR(128) NOT NULL,
    reason_zh_tw VARCHAR(512) NOT NULL,
    evidence_zh_tw VARCHAR(512) NOT NULL,
    applied_at_utc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELIMITER //

CREATE OR REPLACE PROCEDURE god2_research.drop_empty_legacy_raw_research_table(
    IN p_table_name VARCHAR(128),
    IN p_reason_zh_tw VARCHAR(512)
)
BEGIN
    DECLARE v_exists INT DEFAULT 0;
    DECLARE v_rows BIGINT DEFAULT 0;
    DECLARE v_sql TEXT;

    SELECT COUNT(*)
      INTO v_exists
      FROM information_schema.tables
     WHERE table_schema = 'god2_research'
       AND table_name = p_table_name
       AND table_type = 'BASE TABLE';

    IF v_exists = 1 THEN
        SET @count_sql = CONCAT('SELECT COUNT(*) INTO @legacy_raw_research_row_count FROM god2_research.`', REPLACE(p_table_name, '`', '``'), '`');
        PREPARE count_stmt FROM @count_sql;
        EXECUTE count_stmt;
        DEALLOCATE PREPARE count_stmt;
        SET v_rows = @legacy_raw_research_row_count;

        IF v_rows <> 0 THEN
            SIGNAL SQLSTATE '45000'
                SET MESSAGE_TEXT = 'Refusing to drop non-empty legacy research table.';
        END IF;

        SET v_sql = CONCAT('DROP TABLE god2_research.`', REPLACE(p_table_name, '`', '``'), '`');
        PREPARE drop_stmt FROM v_sql;
        EXECUTE drop_stmt;
        DEALLOCATE PREPARE drop_stmt;

        INSERT INTO god2_research.database_cleanup_audit (
            cleanup_key,
            cleanup_action_zh_tw,
            affected_schema,
            affected_table,
            reason_zh_tw,
            evidence_zh_tw
        ) VALUES (
            CONCAT('drop_empty_legacy_raw_research_table:', p_table_name),
            '刪除空的舊研究 raw/json 暫存表',
            'god2_research',
            p_table_name,
            p_reason_zh_tw,
            '套用前以 information_schema 與 COUNT(*) 確認存在且為 0 筆資料，且未作為正式服務端 runtime 表。'
        )
        ON DUPLICATE KEY UPDATE
            cleanup_action_zh_tw = VALUES(cleanup_action_zh_tw),
            reason_zh_tw = VALUES(reason_zh_tw),
            evidence_zh_tw = VALUES(evidence_zh_tw),
            applied_at_utc = UTC_TIMESTAMP(6);
    END IF;
END//

DELIMITER ;

CALL god2_research.drop_empty_legacy_raw_research_table(
    'content_client_table_layouts',
    '舊內容還原表面暫存表，含 HeaderJson 欄位但目前 0 筆資料；正式內容已由整理後表與正式 god2_game 表承接。'
);

CALL god2_research.drop_empty_legacy_raw_research_table(
    'content_field_evidence',
    '舊內容欄位證據暫存表，含 ValueJson 欄位但目前 0 筆資料；保留空表只會增加資料庫雜訊。'
);

CALL god2_research.drop_empty_legacy_raw_research_table(
    'content_phase3_field_closure',
    '舊第三階段欄位閉合暫存表，含多個 Json 欄位但目前 0 筆資料；已無正式 runtime 依賴。'
);

CALL god2_research.drop_empty_legacy_raw_research_table(
    'content_phase3_promotions',
    '舊第三階段提升暫存表，含 FieldStatesJson/SourceProvenanceJson 但目前 0 筆資料；已無正式 runtime 依賴。'
);

CALL god2_research.drop_empty_legacy_raw_research_table(
    'content_recovery_run_summary_archive',
    '舊內容恢復 run summary archive，含 SummaryJson 但目前 0 筆資料；保留會讓資料庫可讀面變亂。'
);

CALL god2_research.drop_empty_legacy_raw_research_table(
    'historical_gameplay_observations',
    '舊歷史 gameplay observation 暫存表，含 GameplayDataJson 但目前 0 筆資料；正式黑箱觀測已有專用 sanitized/readable 表。'
);

DROP PROCEDURE IF EXISTS god2_research.drop_empty_legacy_raw_research_table;
