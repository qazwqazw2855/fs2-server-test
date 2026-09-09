DROP VIEW IF EXISTS `god2`.`vw_dialogs_readable`;
DROP VIEW IF EXISTS `god2`.`vw_merchants_readable`;
DROP VIEW IF EXISTS `god2`.`vw_localization_entries_readable`;
DROP VIEW IF EXISTS `god2`.`vw_content_recovery_runs_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_dialog_merchant_observations_readable`;

CREATE VIEW `god2`.`vw_dialogs_readable` AS
SELECT
    dialog.`Id` AS `對話ID`,
    dialog.`Code` AS `服務端代碼`,
    dialog.`NpcId` AS `NPC_ID`,
    COALESCE(npc.`NameZhTw`, npc.`Name`, '') AS `NPC名稱`,
    dialog.`TextKey` AS `文字鍵`,
    localization.`TextValue` AS `對話文字`,
    dialog.`RecoveryStatus` AS `恢復狀態`,
    'NPC點擊對話、任務提示、商人入口前置文字' AS `功能對照`
FROM `god2`.`dialogs` dialog
LEFT JOIN `god2`.`npcs` npc
    ON npc.`Id` = dialog.`NpcId`
LEFT JOIN `god2`.`localization_entries` localization
    ON localization.`TextKey` = dialog.`TextKey`;

CREATE VIEW `god2`.`vw_merchants_readable` AS
SELECT
    merchant.`Id` AS `商人ID`,
    merchant.`NpcId` AS `NPC_ID`,
    COALESCE(merchant.`NameZhTw`, merchant.`Name`) AS `商人名稱`,
    COALESCE(npc.`NameZhTw`, npc.`Name`, '') AS `NPC名稱`,
    npc.`MapId` AS `地圖ID`,
    COALESCE(map_info.`NameZhTw`, map_info.`Name`, '') AS `地圖名稱`,
    npc.`PositionX` AS `座標X`,
    npc.`PositionY` AS `座標Y`,
    merchant.`RecoveryStatus` AS `恢復狀態`,
    merchant.`LocalizationStatus` AS `在地化狀態`,
    merchant.`ContentRecoveryRunId` AS `恢復批次`,
    'NPC商店開啟、購買、出售、修理或商店分類入口' AS `功能對照`
FROM `god2`.`merchants` merchant
LEFT JOIN `god2`.`npcs` npc
    ON npc.`Id` = merchant.`NpcId`
LEFT JOIN `god2`.`maps` map_info
    ON map_info.`Id` = npc.`MapId`;

CREATE VIEW `god2`.`vw_localization_entries_readable` AS
SELECT
    localization.`Language` AS `語言來源`,
    localization.`TextKey` AS `文字鍵`,
    localization.`TextValue` AS `文字內容`,
    CASE
        WHEN localization.`Language` IN ('zh-TW', 'zh_tw', 'TraditionalChinese', 'official-recovery') THEN '繁中或官方恢復文字'
        ELSE '其他語言來源'
    END AS `語言狀態`,
    CASE
        WHEN localization.`TextKey` LIKE 'official.dialogs.%' THEN 'NPC對話顯示'
        WHEN localization.`TextKey` LIKE 'official.localization.source_client_%' THEN '客戶端資料來源對照'
        ELSE '遊戲文字顯示與資料對照'
    END AS `功能對照`,
    localization.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`localization_entries` localization;

CREATE VIEW `god2`.`vw_content_recovery_runs_readable` AS
SELECT
    recovery_run.`RunId` AS `恢復批次`,
    recovery_run.`Phase` AS `階段`,
    recovery_run.`Status` AS `狀態`,
    recovery_run.`ClientRootIdentity` AS `客戶端來源識別`,
    recovery_run.`ExtractorVersion` AS `擷取器版本`,
    recovery_run.`ConverterName` AS `轉換器名稱`,
    recovery_run.`ConverterVersion` AS `轉換器版本`,
    recovery_run.`GlossaryVersion` AS `詞彙表版本`,
    recovery_run.`StartedAtUtc` AS `開始時間UTC`,
    recovery_run.`CompletedAtUtc` AS `完成時間UTC`,
    '客戶端證據恢復批次、正式資料來源追蹤與回溯' AS `功能對照`
FROM `god2`.`content_recovery_runs` recovery_run;

CREATE VIEW `god2`.`vw_blackbox_dialog_merchant_observations_readable` AS
SELECT
    'NPC對話' AS `觀察類型`,
    dialog_view.`對話ID` AS `主要ID`,
    COALESCE(NULLIF(dialog_view.`NPC名稱`, ''), dialog_view.`服務端代碼`) AS `名稱`,
    dialog_view.`NPC_ID`,
    NULL AS `地圖ID`,
    '' AS `地圖名稱`,
    dialog_view.`功能對照`,
    CASE
        WHEN dialog_view.`NPC_ID` IS NULL THEN '擱置：對話尚未綁定NPC'
        ELSE '第一優先：測點擊NPC後文字與選項'
    END AS `黑箱測試優先級`,
    CONCAT('文字鍵=', COALESCE(dialog_view.`文字鍵`, ''), '；文字=', COALESCE(LEFT(dialog_view.`對話文字`, 120), '')) AS `測試重點`
FROM `god2`.`vw_dialogs_readable` dialog_view

UNION ALL

SELECT
    'NPC商店' AS `觀察類型`,
    merchant_view.`商人ID` AS `主要ID`,
    merchant_view.`商人名稱` AS `名稱`,
    merchant_view.`NPC_ID`,
    merchant_view.`地圖ID`,
    merchant_view.`地圖名稱`,
    merchant_view.`功能對照`,
    CASE
        WHEN merchant_view.`NPC_ID` IS NULL THEN '擱置：商店尚未綁定NPC'
        ELSE '第一優先：測NPC開店、買賣、關閉與錯誤提示'
    END AS `黑箱測試優先級`,
    CONCAT('座標=', COALESCE(merchant_view.`座標X`, 0), ',', COALESCE(merchant_view.`座標Y`, 0), '；恢復狀態=', merchant_view.`恢復狀態`) AS `測試重點`
FROM `god2`.`vw_merchants_readable` merchant_view;
