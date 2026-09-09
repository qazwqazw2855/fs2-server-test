CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_quests_readable` AS
SELECT
    quest_row.`Id` AS `任務ID`,
    quest_row.`Code` AS `任務代碼`,
    COALESCE(profile_row.`NameZhTw`, quest_row.`NameZhTw`, quest_row.`Name`) AS `任務名稱`,
    COALESCE(profile_row.`DescriptionZhTw`, quest_row.`DescriptionZhTw`) AS `任務說明`,
    quest_row.`RequiredLevel` AS `需求等級`,
    quest_row.`StartNpcId` AS `開始NPC_ID`,
    COALESCE(start_npc_row.`NameZhTw`, start_npc_row.`Name`) AS `開始NPC名稱`,
    quest_row.`EndNpcId` AS `完成NPC_ID`,
    COALESCE(end_npc_row.`NameZhTw`, end_npc_row.`Name`) AS `完成NPC名稱`,
    profile_row.`ClientQuestId` AS `客戶端任務ID`,
    profile_row.`StartNpcClientId` AS `客戶端開始NPC_ID`,
    profile_row.`EndNpcClientId` AS `客戶端完成NPC_ID`,
    profile_row.`RewardTextZhTw` AS `獎勵文字`,
    CASE
        WHEN profile_row.`StepsJson` IS NULL THEN '任務步驟未回收'
        ELSE '任務步驟已回收為結構資料'
    END AS `任務步驟狀態`,
    quest_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE quest_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE quest_row.`RecoveryStatus`
    END AS `回收狀態`,
    quest_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE quest_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE quest_row.`LocalizationStatus`
    END AS `在地化狀態`,
    CASE profile_row.`ProductionProfileEnabled`
        WHEN 1 THEN '正式任務內容啟用'
        WHEN 0 THEN '證據保留'
        ELSE '尚未對應任務內容'
    END AS `正式任務狀態`,
    quest_row.`ContentRecoveryRunId` AS `任務回收批次`,
    profile_row.`RunId` AS `內容回收批次`,
    profile_row.`PromotionRunId` AS `正式提升批次`,
    profile_row.`UpdatedAtUtc` AS `內容更新時間UTC`
FROM `god2`.`quests` quest_row
LEFT JOIN `god2`.`quest_content_profiles` profile_row
    ON profile_row.`QuestId` = quest_row.`Id`
    AND profile_row.`ProductionProfileEnabled` = 1
LEFT JOIN `god2`.`npcs` start_npc_row
    ON start_npc_row.`Id` = quest_row.`StartNpcId`
LEFT JOIN `god2`.`npcs` end_npc_row
    ON end_npc_row.`Id` = quest_row.`EndNpcId`;
