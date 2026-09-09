CREATE OR REPLACE VIEW god2.vw_items_sanitized_readable AS
SELECT
    `物品ID`,
    `物品名稱`,
    CASE
        WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立'
        ELSE '服務端代碼已建立'
    END AS `服務端對應狀態`,
    CASE WHEN `物品分類` = 'Unknown' THEN '分類待確認' ELSE COALESCE(NULLIF(`物品分類`, ''), '分類待確認') END AS `物品分類`,
    CASE WHEN `物品家族` = 'Accessory' THEN '飾品' WHEN `物品家族` = 'PetItem' THEN '戰寵道具' WHEN `物品家族` = 'Consumable' THEN '消耗品' WHEN `物品家族` = 'Unknown' THEN '家族待確認' ELSE COALESCE(NULLIF(`物品家族`, ''), '家族待確認') END AS `物品家族`,
    CASE WHEN `物品類型` = 'Unknown' THEN '類型待確認' ELSE COALESCE(NULLIF(`物品類型`, ''), '類型待確認') END AS `物品類型`,
    CASE WHEN `裝備分類` = 'Unknown' THEN '非裝備或待確認' ELSE COALESCE(NULLIF(`裝備分類`, ''), '非裝備或待確認') END AS `裝備分類`,
    CASE WHEN `消耗品分類` = 'Unknown' THEN '非消耗品或待確認' ELSE COALESCE(NULLIF(`消耗品分類`, ''), '非消耗品或待確認') END AS `消耗品分類`,
    CASE WHEN `堆疊規則` = 'ExplicitNonStackable' THEN '不可堆疊' WHEN `堆疊規則` = 'Stackable' THEN '可堆疊' ELSE COALESCE(NULLIF(`堆疊規則`, ''), '堆疊待確認') END AS `堆疊規則`,
    `最大堆疊`,
    CASE WHEN `綁定規則` = 'Unknown' THEN '綁定待確認' ELSE COALESCE(NULLIF(`綁定規則`, ''), '綁定待確認') END AS `綁定規則`,
    CASE WHEN `交易規則` = 'Allowed' THEN '可交易' WHEN `交易規則` = 'Blocked' THEN '不可交易' ELSE COALESCE(NULLIF(`交易規則`, ''), '交易待確認') END AS `交易規則`,
    CASE WHEN `販售規則` = 'Unknown' THEN '販售待確認' ELSE COALESCE(NULLIF(`販售規則`, ''), '販售待確認') END AS `販售規則`,
    `基礎買價`,
    `賣價`,
    CASE WHEN `貨幣類型` = 'Gold' THEN '金錢' ELSE COALESCE(NULLIF(`貨幣類型`, ''), '貨幣待確認') END AS `貨幣類型`,
    `任務道具狀態`,
    `正式狀態`,
    CASE WHEN `恢復狀態` = 'Recovered' THEN '已恢復' ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認') END AS `恢復狀態`,
    CASE WHEN `在地化狀態` = 'ConvertedToTraditional' THEN '已轉繁體' WHEN `在地化狀態` = 'TraditionalVerified' THEN '繁體已驗證' ELSE COALESCE(NULLIF(`在地化狀態`, ''), '待確認') END AS `在地化狀態`,
    `功能對照`,
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`繁中說明`, ''), '/c[0-9]+', ''), '[[:space:]]+', ' ')) AS `繁中說明`,
    `更新時間UTC`
FROM god2.vw_items_readable;

CREATE OR REPLACE VIEW god2.vw_skills_sanitized_readable AS
SELECT
    `技能ID`,
    `技能名稱`,
    CASE WHEN `服務端代碼` IS NULL OR TRIM(`服務端代碼`) = '' THEN '服務端代碼待建立' ELSE '服務端代碼已建立' END AS `服務端對應狀態`,
    COALESCE(NULLIF(`技能說明`, ''), '技能說明待確認') AS `技能說明`,
    COALESCE(NULLIF(`技能家族`, ''), '技能家族待確認') AS `技能家族`,
    COALESCE(NULLIF(`目標規則`, ''), '目標規則待確認') AS `目標規則`,
    `最高等級`,
    `需求等級`,
    `MP消耗`,
    CASE WHEN `MP消耗規則` = 'Unknown' THEN 'MP消耗待確認' ELSE COALESCE(NULLIF(`MP消耗規則`, ''), 'MP消耗待確認') END AS `MP消耗規則`,
    CASE WHEN `恢復狀態` = 'Recovered' THEN '已恢復' ELSE COALESCE(NULLIF(`恢復狀態`, ''), '待確認') END AS `恢復狀態`,
    CASE WHEN `在地化狀態` = 'ConvertedToTraditional' THEN '已轉繁體' WHEN `在地化狀態` = 'TraditionalVerified' THEN '繁體已驗證' ELSE COALESCE(NULLIF(`在地化狀態`, ''), '待確認') END AS `在地化狀態`,
    `功能對照`
FROM god2.vw_skills_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_item_use_test_targets_sanitized_readable AS
SELECT
    `物品ID`,
    `客戶端物品ID`,
    `名稱`,
    TRIM(REGEXP_REPLACE(REGEXP_REPLACE(COALESCE(`說明`, ''), '/c[0-9]+', ''), '[[:space:]]+', ' ')) AS `說明`,
    `分類`,
    `細分類`,
    IF(`平時可用` = 1, '平時可用', '平時不可用') AS `平時使用狀態`,
    IF(`戰鬥可用` = 1, '戰鬥可用', '戰鬥不可用') AS `戰鬥使用狀態`,
    IF(`可對他人使用` = 1, '可對他人使用', '僅能對自己或不可對他人') AS `目標使用狀態`,
    IF(`規則可進runtime` = 1, '使用規則可進Runtime', '使用規則待補Runtime') AS `規則Runtime狀態`,
    IF(`規則已啟用` = 1, '使用規則已啟用', '使用規則未啟用') AS `規則啟用狀態`,
    `效果序`,
    `效果類型`,
    `效果數值`,
    `效果使用範圍`,
    `效果目標`,
    `效果說明`,
    IF(`效果可進runtime` = 1, '效果可進Runtime', '效果待補Runtime') AS `效果Runtime狀態`,
    IF(`效果已啟用` = 1, '效果已啟用', '效果未啟用') AS `效果啟用狀態`,
    `黑箱優先級`,
    '道具使用：平時/戰鬥可用性、目標、HP/MP恢復或其他效果' AS `功能對照`
FROM god2_game.vw_blackbox_item_use_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_skill_status_test_targets_sanitized_readable AS
SELECT
    `效果記錄ID`,
    CASE WHEN `來源Bank` IS NULL THEN '來源Bank待確認' ELSE '來源Bank已對應' END AS `來源Bank狀態`,
    CASE WHEN `來源槽位` IS NULL THEN '來源槽位待確認' ELSE '來源槽位已對應' END AS `來源槽位狀態`,
    COALESCE(NULLIF(`技能名稱`, ''), '技能名稱待確認') AS `技能名稱`,
    COALESCE(NULLIF(`技能說明`, ''), '技能說明待確認') AS `技能說明`,
    `技能等級`,
    `功能族群`,
    `效果類型`,
    `狀態或屬性軸`,
    `MP消耗`,
    CASE WHEN `目標Context` IS NULL THEN '目標Context待確認' ELSE '目標Context已對應' END AS `目標Context狀態`,
    CASE WHEN `目標範圍` IS NULL THEN '目標範圍待確認' ELSE '目標範圍已對應' END AS `目標範圍狀態`,
    COALESCE(NULLIF(`數值欄位`, ''), '數值欄位待確認') AS `數值欄位`,
    `最低可用服務端規則`,
    COALESCE(NULLIF(`暫定成功規則`, ''), '成功規則待黑箱確認') AS `暫定成功規則`,
    COALESCE(NULLIF(`暫定持續規則`, ''), '持續規則待黑箱確認') AS `暫定持續規則`,
    IF(`候選啟用` = 1, '候選已啟用', '候選未啟用') AS `候選狀態`,
    CASE WHEN `正式技能ID` IS NULL THEN '正式技能待對應' ELSE '正式技能已對應' END AS `正式技能狀態`,
    IF(`正式技能已啟用` = 1, '正式技能已啟用', '正式技能未啟用或待對應') AS `正式技能啟用狀態`,
    COALESCE(NULLIF(`正式技能族群`, ''), '正式技能族群待確認') AS `正式技能族群`,
    COALESCE(NULLIF(`正式技能分類`, ''), '正式技能分類待確認') AS `正式技能分類`,
    COALESCE(NULLIF(`正式目標範圍`, ''), '正式目標範圍待確認') AS `正式目標範圍`,
    CASE WHEN `正式狀態ID` IS NULL THEN '正式狀態待對應' ELSE '正式狀態已對應' END AS `正式狀態對應`,
    `黑箱優先級`,
    '技能/狀態：治療、傷害、Buff/Debuff、MP消耗、目標範圍與持續回合' AS `功能對照`
FROM god2_game.vw_blackbox_skill_status_test_targets_readable;

CREATE OR REPLACE VIEW god2_game.vw_monster_combat_stat_design_rules_sanitized_readable AS
SELECT
    CONCAT('怪物戰鬥數值規則#', ROW_NUMBER() OVER (ORDER BY `等級範圍`, `怪物定位`)) AS `規則`,
    `等級範圍`,
    `怪物定位`,
    `HPMP倍率`,
    `攻防倍率`,
    `五行總點數`,
    `獎勵倍率`,
    `使用政策`,
    `規則狀態`,
    '怪物數值：HP/MP、攻防、五行與經驗金錢曲線' AS `功能對照`
FROM god2_game.vw_monster_combat_stat_design_rules_readable;

CREATE OR REPLACE VIEW god2_game.vw_monster_drop_design_rules_sanitized_readable AS
SELECT
    CONCAT('怪物掉落規則#', ROW_NUMBER() OVER (ORDER BY `等級範圍`, `怪物定位`)) AS `規則`,
    `等級範圍`,
    `怪物定位`,
    `建議掉落率`,
    `建議數量`,
    `使用政策`,
    `規則狀態`,
    '怪物掉落：掉落率、數量與定位規則' AS `功能對照`
FROM god2_game.vw_monster_drop_design_rules_readable;

CREATE OR REPLACE VIEW god2_game.vw_blackbox_catalog_target_sanitized_observations_readable AS
SELECT
    CONVERT('道具使用效果' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`物品ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`效果類型`, ' ', `效果數值`, '；', `戰鬥使用狀態`, '；', `效果Runtime狀態`, '；', `效果啟用狀態`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('平時與戰鬥各使用一次，記錄HP/MP、Buff、道具消耗、是否可對他人使用' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_item_use_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('技能狀態效果' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`效果記錄ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`技能名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`效果類型`, '；MP=', COALESCE(CAST(`MP消耗` AS CHAR), '待確認'), '；', `正式技能狀態`, '；', `正式狀態對應`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('施放技能並記錄MP消耗、目標範圍、傷害/治療/Buff數值與持續回合' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_blackbox_skill_status_test_targets_sanitized_readable
UNION ALL
SELECT
    CONVERT('怪物數值規則' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`規則` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物定位` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`等級範圍`, '；', `HPMP倍率`, '；', `攻防倍率`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('選同等級怪物打測HP/MP、物攻物防、魔攻魔防、五行加減傷與防禦1.5倍' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_monster_combat_stat_design_rules_sanitized_readable
UNION ALL
SELECT
    CONVERT('怪物掉落規則' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`規則` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標ID`,
    CONVERT(`怪物定位` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標名稱`,
    CONVERT(CONCAT(`等級範圍`, '；', `建議掉落率`, '；', `建議數量`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('連續擊殺同類怪物，記錄掉落物、數量與大致掉落率' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_game.vw_monster_drop_design_rules_sanitized_readable;
