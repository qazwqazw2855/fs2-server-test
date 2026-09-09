CREATE OR REPLACE VIEW god2.vw_characters_sanitized_readable AS
SELECT
    `角色ID`,
    `帳號ID`,
    `角色名稱`,
    `目前顯示名稱`,
    CASE
        WHEN `職業代碼` = 'Swordsman' THEN '劍士'
        WHEN `職業代碼` = 'Taoist' THEN '仙道'
        WHEN `職業代碼` = 'Pharmacist' THEN '藥師'
        WHEN `職業代碼` = 'Warlock' THEN '謀士'
        WHEN `職業代碼` = 'Unknown' THEN '職業待確認'
        ELSE COALESCE(NULLIF(`職業代碼`, ''), '職業待確認')
    END AS `職業`,
    CASE
        WHEN `性別` = 'Male' THEN '男'
        WHEN `性別` = 'Female' THEN '女'
        WHEN `性別` = 'Unknown' THEN '性別待確認'
        ELSE COALESCE(NULLIF(`性別`, ''), '性別待確認')
    END AS `性別`,
    CASE
        WHEN `先天技能` LIKE 'LifeSkill%' THEN CONCAT('先天技能', REPLACE(`先天技能`, 'LifeSkill', ''), '（待確認）')
        WHEN `先天技能` = 'Unknown' THEN '先天技能待確認'
        ELSE COALESCE(NULLIF(`先天技能`, ''), '先天技能待確認')
    END AS `先天技能`,
    CASE
        WHEN `外觀` = 'Unknown' THEN '外觀待確認'
        ELSE COALESCE(NULLIF(`外觀`, ''), '外觀待確認')
    END AS `外觀`,
    `等級`,
    `地圖ID`,
    CASE
        WHEN `地圖名稱` = 'Island01/indoor/groceryL' THEN '島嶼01室內雜貨左側'
        WHEN LOCATE('/', `地圖名稱`) > 0 OR LOCATE(CHAR(92), `地圖名稱`) > 0 THEN '地圖名稱待確認'
        ELSE COALESCE(NULLIF(`地圖名稱`, ''), '地圖名稱待確認')
    END AS `地圖名稱`,
    `座標X`,
    `座標Y`,
    CASE
        WHEN `方向` = 'Unknown' THEN '方向待確認'
        ELSE COALESCE(NULLIF(`方向`, ''), '方向待確認')
    END AS `方向`,
    CASE
        WHEN `最後傳送門ID` IS NULL THEN '尚未記錄傳送門'
        ELSE '已記錄傳送門'
    END AS `最後傳送門狀態`,
    CASE
        WHEN `角色狀態` = 'Active' THEN '可登入'
        WHEN `角色狀態` = 'Deleted' THEN '已刪除'
        WHEN `角色狀態` = 'Locked' THEN '已鎖定'
        ELSE COALESCE(NULLIF(`角色狀態`, ''), '狀態待確認')
    END AS `角色狀態`,
    `功能對照`,
    `建立時間UTC`,
    `更新時間UTC`,
    `最後遊玩時間UTC`,
    `刪除時間UTC`
FROM god2.vw_characters_readable;

CREATE OR REPLACE VIEW god2_player.vw_character_status_sanitized_readable AS
SELECT
    `帳號ID`,
    `角色ID`,
    `角色名稱`,
    CASE
        WHEN `職業名稱` = 'Swordsman' THEN '劍士'
        WHEN `職業名稱` = 'Taoist' THEN '仙道'
        WHEN `職業名稱` = 'Pharmacist' THEN '藥師'
        WHEN `職業名稱` = 'Warlock' THEN '謀士'
        WHEN `職業名稱` = 'Unknown' THEN '職業待確認'
        ELSE COALESCE(NULLIF(`職業名稱`, ''), '職業待確認')
    END AS `職業`,
    CASE
        WHEN `性別代碼` = 'Male' THEN '男'
        WHEN `性別代碼` = 'Female' THEN '女'
        WHEN `性別代碼` = 'Unknown' THEN '性別待確認'
        ELSE COALESCE(NULLIF(`性別代碼`, ''), '性別待確認')
    END AS `性別`,
    CASE
        WHEN `先天技能代碼` LIKE 'LifeSkill%' THEN CONCAT('先天技能', REPLACE(`先天技能代碼`, 'LifeSkill', ''), '（待確認）')
        WHEN `先天技能代碼` = 'Unknown' THEN '先天技能待確認'
        ELSE COALESCE(NULLIF(`先天技能代碼`, ''), '先天技能待確認')
    END AS `先天技能`,
    `等級`,
    `經驗`,
    `聲望`,
    `轉生次數`,
    `未分配屬性點`,
    `目前HP`,
    `最大HP`,
    `目前MP`,
    `最大MP`,
    `腕力總值`,
    `體力總值`,
    `智力總值`,
    `速度總值`,
    `金總值`,
    `木總值`,
    `水總值`,
    `火總值`,
    `土總值`,
    `物攻總值`,
    `物防總值`,
    `魔攻總值`,
    `魔防總值`,
    `地圖ID`,
    `地圖名稱`,
    `座標X`,
    `座標Y`,
    COALESCE(NULLIF(`方向`, ''), '方向待確認') AS `方向`,
    `角色版本`,
    `角色狀態`,
    `服務端狀態`,
    `最後遊玩時間UTC`,
    `更新時間UTC`
FROM god2_player.vw_character_status_readable;

CREATE OR REPLACE VIEW god2_player.vw_inventory_audit_ledger_sanitized_readable AS
SELECT
    CONCAT('背包審計#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `操作類型`)) AS `審計紀錄`,
    `角色ID`,
    `操作類型`,
    CASE
        WHEN `來源` LIKE '%官方實機%' THEN '官方實機證據'
        WHEN `來源` IS NULL OR TRIM(`來源`) = '' THEN '來源待確認'
        ELSE `來源`
    END AS `來源`,
    `商人模板ID`,
    `物品模板ID`,
    `背包項目ID`,
    `操作前數量`,
    `操作後數量`,
    `貨幣類型`,
    `操作前貨幣`,
    `操作後貨幣`,
    `操作前背包版本`,
    `操作後背包版本`,
    `結果`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    CASE
        WHEN `交易ID` IS NULL OR TRIM(`交易ID`) = '' THEN '交易識別待建立'
        ELSE '交易識別已建立'
    END AS `交易識別狀態`,
    CASE
        WHEN `連線ID` IS NULL OR TRIM(`連線ID`) = '' THEN '連線識別待建立'
        ELSE '連線識別已建立'
    END AS `連線識別狀態`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2_player.vw_inventory_audit_ledger_readable;

CREATE OR REPLACE VIEW god2_player.vw_player_social_operations_sanitized_readable AS
SELECT
    CONCAT('社交操作#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `操作者角色ID`, `操作類型`)) AS `社交紀錄`,
    `操作者角色ID`,
    `操作者名稱`,
    `操作類型`,
    CASE
        WHEN `結果代碼` IS NULL OR TRIM(`結果代碼`) = '' THEN '結果待確認'
        ELSE `結果代碼`
    END AS `結果`,
    CASE
        WHEN `請求ID` IS NULL OR TRIM(`請求ID`) = '' THEN '無請求識別'
        ELSE '請求識別已建立'
    END AS `請求狀態`,
    CASE
        WHEN `邀請ID` IS NULL OR TRIM(`邀請ID`) = '' THEN '無邀請識別'
        ELSE '邀請識別已建立'
    END AS `邀請狀態`,
    `相關角色ID`,
    `相關角色名稱`,
    `是否異動`,
    `封鎖判定`,
    `功能對照`,
    `建立時間UTC`
FROM god2_player.vw_player_social_operations_readable;

CREATE OR REPLACE VIEW god2_player.vw_world_interaction_audit_sanitized_readable AS
SELECT
    CONCAT('世界互動審計#', ROW_NUMBER() OVER (ORDER BY `建立時間UTC`, `角色ID`, `互動類型`)) AS `審計紀錄`,
    `角色ID`,
    `角色名稱`,
    CASE
        WHEN `玩家RuntimeID` IS NULL THEN '玩家Runtime待建立'
        ELSE '玩家Runtime已建立'
    END AS `玩家Runtime狀態`,
    CASE
        WHEN `目標RuntimeID` IS NULL THEN '目標Runtime待建立'
        ELSE '目標Runtime已建立'
    END AS `目標Runtime狀態`,
    `目標模板ID`,
    `互動類型`,
    COALESCE(NULLIF(`服務端處理器`, ''), '服務端處理器待確認') AS `服務端功能`,
    `來源地圖ID`,
    `來源地圖名稱`,
    `目標地圖ID`,
    `目標地圖名稱`,
    `來源座標`,
    `目標座標`,
    `操作前角色版本`,
    `操作後角色版本`,
    `結果`,
    CASE
        WHEN `失敗代碼` IS NULL OR TRIM(`失敗代碼`) = '' THEN '無失敗'
        ELSE `失敗代碼`
    END AS `失敗狀態`,
    `回滾狀態`,
    `連線重綁`,
    `同步清除`,
    `同步重建`,
    `功能對照`,
    `建立時間UTC`,
    `完成時間UTC`
FROM god2_player.vw_world_interaction_audit_readable;

CREATE OR REPLACE VIEW god2_player.vw_blackbox_player_operation_sanitized_observations_readable AS
SELECT
    CONVERT('角色狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(CAST(`角色ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前名稱`,
    CONVERT(CONCAT(`職業`, '；', `性別`, '；', `先天技能`, '；HP=', COALESCE(CAST(`目前HP` AS CHAR), '待確認'), '/', COALESCE(CAST(`最大HP` AS CHAR), '待確認'), '；MP=', COALESCE(CAST(`目前MP` AS CHAR), '待確認'), '/', COALESCE(CAST(`最大MP` AS CHAR), '待確認')) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('角色登入、狀態面板、HP/MP、能力值與所在地圖' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('登入角色後打開角色狀態與地圖畫面，核對職業、性別、先天技能、HP/MP、座標' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_player.vw_character_status_sanitized_readable
UNION ALL
SELECT
    CONVERT('背包審計' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`審計紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(CAST(`角色ID` AS CHAR) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前名稱`,
    CONVERT(CONCAT(`操作類型`, '；', `結果`, '；', `失敗狀態`, '；貨幣=', `操作前貨幣`, '->', `操作後貨幣`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT('商店買賣、背包數量、金錢異動與審計紀錄' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('做一次買賣或物品異動，核對背包版本、數量與金錢前後值是否吻合' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_player.vw_inventory_audit_ledger_sanitized_readable
UNION ALL
SELECT
    CONVERT('世界互動' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`審計紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`角色名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前名稱`,
    CONVERT(CONCAT(`互動類型`, '；', `結果`, '；', `來源地圖名稱`, `來源座標`, ' -> ', `目標地圖名稱`, `目標座標`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('踩傳送門或觸發世界互動，核對來源/目標地圖、座標、同步重建與回滾狀態' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_player.vw_world_interaction_audit_sanitized_readable
UNION ALL
SELECT
    CONVERT('社交操作' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `觀察類型`,
    CONVERT(`社交紀錄` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目標`,
    CONVERT(`操作者名稱` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前名稱`,
    CONVERT(CONCAT(`操作類型`, '；', `結果`, '；', `是否異動`, '；', `封鎖判定`) USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `目前狀態`,
    CONVERT(`功能對照` USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `功能對照`,
    CONVERT('測好友、邀請、封鎖或社交互動，確認結果與角色關係是否正確' USING utf8mb4) COLLATE utf8mb4_unicode_ci AS `黑箱測試方式`
FROM god2_player.vw_player_social_operations_sanitized_readable;
