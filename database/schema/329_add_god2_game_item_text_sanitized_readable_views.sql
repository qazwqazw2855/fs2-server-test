DROP VIEW IF EXISTS `god2_game`.`vw_item_registry_text_sanitized_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_item_usage_rules_sanitized_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_item_text_cleanup_observations_readable`;

CREATE VIEW `god2_game`.`vw_item_registry_text_sanitized_readable` AS
SELECT
    item.`item_id` AS `物品ID`,
    item.`client_item_id` AS `客戶端物品ID`,
    item.`name_zh_tw` AS `物品名稱`,
    item.`item_category` AS `物品分類`,
    item.`item_family` AS `物品家族`,
    TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(COALESCE(item.`description_zh_tw`, ''), '/c[0-9]+', ''),
            '[[:space:]]+',
            ' '
        )
    ) AS `清潔說明`,
    CASE
        WHEN item.`description_zh_tw` REGEXP '/c[0-9]+' THEN '已移除客戶端顏色格式碼'
        WHEN item.`description_zh_tw` IS NULL OR item.`description_zh_tw` = '' THEN '沒有說明文字'
        ELSE '原始說明已是可讀文字'
    END AS `說明清潔狀態`,
    item.`required_level` AS `需求等級`,
    item.`maximum_stack` AS `最大堆疊`,
    item.`buy_price` AS `買價`,
    item.`sell_price` AS `賣價`,
    CASE WHEN item.`usable` = 1 THEN '可使用' ELSE '不可直接使用' END AS `使用狀態`,
    CASE WHEN item.`equippable` = 1 THEN '可裝備' ELSE '不可裝備' END AS `裝備狀態`,
    CASE WHEN item.`tradable` = 1 THEN '可交易' ELSE '不可交易' END AS `交易狀態`,
    CASE WHEN item.`storable` = 1 THEN '可放倉庫' ELSE '不可放倉庫' END AS `倉庫狀態`,
    CASE WHEN item.`enabled` = 1 THEN '正式啟用' ELSE '正式未啟用' END AS `正式狀態`,
    CASE
        WHEN item.`equippable` = 1 THEN '裝備穿脫、能力值、外觀與背包說明'
        WHEN item.`usable` = 1 THEN '道具使用、效果觸發、限制判定與背包說明'
        ELSE '背包顯示、交易、倉庫與物品說明'
    END AS `功能對照`,
    item.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`item_registry` item;

CREATE VIEW `god2_game`.`vw_item_usage_rules_sanitized_readable` AS
SELECT
    usage_rule.`item_id` AS `物品ID`,
    item_text.`物品名稱`,
    item_text.`物品分類`,
    CASE WHEN usage_rule.`normal_use` = 1 THEN '可平時使用' ELSE '不可平時使用或未證實' END AS `平時使用`,
    CASE WHEN usage_rule.`battle_use` = 1 THEN '可戰鬥中使用' ELSE '不可戰鬥中使用或未證實' END AS `戰鬥使用`,
    CASE WHEN usage_rule.`equippable` = 1 THEN '可裝備' ELSE '不可裝備或未證實' END AS `裝備使用`,
    CASE WHEN usage_rule.`use_on_other` = 1 THEN '可對他人使用' ELSE '不可對他人使用或未證實' END AS `對他人使用`,
    CASE WHEN usage_rule.`hotkey_allowed` = 1 THEN '可放快捷鍵' ELSE '不可放快捷鍵或未證實' END AS `快捷鍵`,
    CASE
        WHEN usage_rule.`class_restriction_zh_tw` IS NULL OR usage_rule.`class_restriction_zh_tw` = '' THEN '無職業限制'
        WHEN usage_rule.`class_restriction_zh_tw` LIKE '%未確認%' THEN '職業限制待黑箱確認'
        ELSE usage_rule.`class_restriction_zh_tw`
    END AS `職業限制`,
    usage_rule.`minimum_rebirth` AS `最低轉生`,
    CASE
        WHEN usage_rule.`gender_restriction_zh_tw` IS NULL OR usage_rule.`gender_restriction_zh_tw` = '' THEN '無性別限制'
        WHEN usage_rule.`gender_restriction_zh_tw` LIKE '%未確認%' THEN '性別限制待黑箱確認'
        ELSE usage_rule.`gender_restriction_zh_tw`
    END AS `性別限制`,
    CASE
        WHEN usage_rule.`equipment_target_restriction_zh_tw` IS NULL OR usage_rule.`equipment_target_restriction_zh_tw` = '' THEN '無裝備目標限制'
        WHEN usage_rule.`equipment_target_restriction_zh_tw` LIKE '%未確認%' THEN '裝備目標限制待黑箱確認'
        ELSE usage_rule.`equipment_target_restriction_zh_tw`
    END AS `裝備目標限制`,
    TRIM(
        REGEXP_REPLACE(
            REGEXP_REPLACE(COALESCE(usage_rule.`condition_text_zh_tw`, ''), '/c[0-9]+', ''),
            '[[:space:]]+',
            ' '
        )
    ) AS `清潔使用條件`,
    CASE WHEN usage_rule.`runtime_eligible` = 1 THEN '服務端可執行' ELSE '服務端暫不執行' END AS `服務端執行狀態`,
    CASE WHEN usage_rule.`enabled` = 1 THEN '規則啟用' ELSE '規則未啟用' END AS `規則狀態`,
    '道具使用限制、戰鬥內外使用、裝備限制、快捷鍵與服務端執行判定' AS `功能對照`,
    usage_rule.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`item_usage_rules` usage_rule
LEFT JOIN `god2_game`.`vw_item_registry_text_sanitized_readable` item_text
    ON item_text.`物品ID` = usage_rule.`item_id`;

CREATE VIEW `god2_game`.`vw_blackbox_item_text_cleanup_observations_readable` AS
SELECT
    '物品說明' AS `觀察類型`,
    item_text.`物品ID` AS `主要ID`,
    item_text.`物品名稱` AS `名稱`,
    item_text.`物品分類` AS `分類`,
    item_text.`功能對照`,
    CASE
        WHEN item_text.`說明清潔狀態` = '已移除客戶端顏色格式碼' THEN '第二優先：確認背包說明顯示不含格式碼'
        WHEN item_text.`正式狀態` = '正式啟用' THEN '第三優先：抽樣確認背包說明'
        ELSE '擱置：物品正式未啟用'
    END AS `黑箱測試優先級`,
    CONCAT(item_text.`說明清潔狀態`, '；', LEFT(COALESCE(item_text.`清潔說明`, ''), 180)) AS `測試重點`
FROM `god2_game`.`vw_item_registry_text_sanitized_readable` item_text
WHERE item_text.`說明清潔狀態` = '已移除客戶端顏色格式碼'
   OR item_text.`正式狀態` = '正式啟用'

UNION ALL

SELECT
    '道具使用限制' AS `觀察類型`,
    usage_view.`物品ID` AS `主要ID`,
    usage_view.`物品名稱` AS `名稱`,
    usage_view.`物品分類` AS `分類`,
    usage_view.`功能對照`,
    CASE
        WHEN usage_view.`職業限制` LIKE '%待黑箱確認%' OR usage_view.`性別限制` LIKE '%待黑箱確認%' OR usage_view.`裝備目標限制` LIKE '%待黑箱確認%' THEN '第一優先：測使用限制與錯誤提示'
        WHEN usage_view.`服務端執行狀態` = '服務端可執行' THEN '第二優先：測道具可用情境'
        ELSE '擱置：服務端暫不執行'
    END AS `黑箱測試優先級`,
    CONCAT(usage_view.`平時使用`, '；', usage_view.`戰鬥使用`, '；', usage_view.`職業限制`, '；', usage_view.`性別限制`, '；', usage_view.`裝備目標限制`) AS `測試重點`
FROM `god2_game`.`vw_item_usage_rules_sanitized_readable` usage_view;
