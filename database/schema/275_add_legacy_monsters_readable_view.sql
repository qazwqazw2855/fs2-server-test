CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_monsters_readable` AS
SELECT
    monster_row.`Id` AS `怪物ID`,
    monster_row.`Code` AS `怪物代碼`,
    COALESCE(monster_row.`NameZhTw`, monster_row.`Name`) AS `怪物名稱`,
    monster_row.`Level` AS `等級`,
    monster_row.`MaxHp` AS `最大生命`,
    CASE
        WHEN monster_row.`MaxHp` IS NULL THEN '生命值未確認'
        ELSE '生命值已記錄'
    END AS `生命資料狀態`,
    monster_row.`MaxMp` AS `最大法力`,
    monster_row.`MpPolicy` AS `法力規則Key`,
    CASE monster_row.`MpPolicy`
        WHEN 'Unknown' THEN '法力資料未確認'
        ELSE monster_row.`MpPolicy`
    END AS `法力資料狀態`,
    monster_row.`Attack` AS `攻擊`,
    monster_row.`Defense` AS `防禦`,
    monster_row.`ExperienceReward` AS `經驗獎勵`,
    monster_row.`CurrencyRewardMinimum` AS `最少金錢獎勵`,
    monster_row.`CurrencyRewardMaximum` AS `最多金錢獎勵`,
    monster_row.`DropPolicy` AS `掉落規則Key`,
    CASE monster_row.`DropPolicy`
        WHEN 'Unknown' THEN '掉落資料未確認'
        WHEN 'KnownItemsProbabilityUnknown' THEN '掉落道具已知但機率未確認'
        ELSE monster_row.`DropPolicy`
    END AS `掉落資料狀態`,
    monster_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE monster_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE monster_row.`RecoveryStatus`
    END AS `回收狀態`,
    monster_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE monster_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE monster_row.`LocalizationStatus`
    END AS `在地化狀態`,
    monster_row.`ContentRecoveryRunId` AS `內容回收批次`
FROM `god2`.`monsters` monster_row;
