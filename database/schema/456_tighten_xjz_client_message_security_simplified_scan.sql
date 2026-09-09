-- 456_tighten_xjz_client_message_security_simplified_scan.sql
-- Purpose: tighten simplified-Chinese scan for C# message/security research tables to avoid false positives on normal Traditional Chinese characters.

CREATE OR REPLACE VIEW god2_research.vw_xjz_client_message_security_simplified_scan_zh_tw AS
SELECT '客戶端訊息模板' AS 資料表, COUNT(*) AS 疑似簡體殘留
FROM god2_research.xjz_client_message_template_evidence
WHERE CONCAT_WS('', text_zh_tw, alt_text_zh_tw, service_label_zh_tw, service_phase_zh_tw) REGEXP '[国庆黄欢类卖贩们圆质给评铊撸变汉语简宝龙乐风声广电东华义乌张赵刘陈万与专业务达过进远开关点时个来实师术号种药选购钱冻态赏悬员输键额资献现竞场奖确对将马馆际娱宾顺总锁锤罗尔门问胜区发团历练级敌观护绳结见]'
UNION ALL
SELECT '聊天命名禁詞', COUNT(*)
FROM god2_research.xjz_forbidden_word_evidence
WHERE CONCAT_WS('', word_text_zh_tw, normalized_key_zh_tw) REGEXP '[国庆黄欢类卖贩们圆质给评铊撸变汉语简宝龙乐风声广电东华义乌张赵刘陈万与专业务达过进远开关点时个来实师术号种药选购钱冻态赏悬员输键额资献现竞场奖确对将马馆际娱宾顺总锁锤罗尔门问胜区发团历练级敌观护绳结见]'
UNION ALL
SELECT '安全阻擋規則', COUNT(*)
FROM god2_research.xjz_client_security_block_rule_evidence
WHERE CONCAT_WS('', pattern_text_zh_tw, note_zh_tw) REGEXP '[国庆黄欢类卖贩们圆质给评铊撸变汉语简宝龙乐风声广电东华义乌张赵刘陈万与专业务达过进远开关点时个来实师术号种药选购钱冻态赏悬员输键额资献现竞场奖确对将马馆际娱宾顺总锁锤罗尔门问胜区发团历练级敌观护绳结见]';

UPDATE god2_research.xjz_csharp_zip_evidence_digest
SET blocked_reason_zh_tw = '明確簡體殘留掃描已歸零；正式阻擋仍需避免誤殺。',
    related_project_object_zh_tw = 'migration 454-456, vw_xjz_client_message_security_simplified_scan_zh_tw'
WHERE evidence_key IN ('client_message_templates', 'forbidden_words');