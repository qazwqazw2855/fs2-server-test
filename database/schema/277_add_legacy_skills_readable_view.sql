CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_skills_readable` AS
SELECT
    skill_row.`Id` AS `技能ID`,
    skill_row.`Code` AS `技能代碼`,
    COALESCE(skill_row.`NameZhTw`, skill_row.`Name`) AS `技能名稱`,
    skill_row.`DescriptionZhTw` AS `技能說明`,
    skill_row.`MaxLevel` AS `最高等級`,
    skill_row.`RequiredLevel` AS `需求等級`,
    skill_row.`SkillFamily` AS `技能家族Key`,
    CASE skill_row.`SkillFamily`
        WHEN 'PhysicalAttack' THEN '物理攻擊'
        WHEN 'Status' THEN '狀態效果'
        WHEN 'PetInnate' THEN '寵物天生技能'
        WHEN 'WeaponMastery' THEN '武器精通'
        WHEN 'RebirthMastery' THEN '轉生精通'
        WHEN 'XianDaoMeditation' THEN '仙道冥思'
        WHEN 'Blessing' THEN '祝福'
        WHEN NULL THEN '未分類'
        ELSE COALESCE(skill_row.`SkillFamily`, '未分類')
    END AS `技能家族`,
    skill_row.`TargetPolicy` AS `目標規則Key`,
    CASE
        WHEN skill_row.`TargetPolicy` IS NULL OR skill_row.`TargetPolicy` = '' THEN '目標規則未確認'
        WHEN skill_row.`TargetPolicy` = 'Self' THEN '自己'
        WHEN skill_row.`TargetPolicy` = 'Enemy' THEN '敵方'
        WHEN skill_row.`TargetPolicy` = 'Ally' THEN '友方'
        WHEN skill_row.`TargetPolicy` = 'Party' THEN '隊伍'
        WHEN skill_row.`TargetPolicy` = 'Area' THEN '範圍'
        ELSE skill_row.`TargetPolicy`
    END AS `目標規則`,
    skill_row.`MpCost` AS `法力消耗`,
    skill_row.`MpCostPolicy` AS `法力消耗規則Key`,
    CASE skill_row.`MpCostPolicy`
        WHEN 'Unknown' THEN '法力消耗未確認'
        WHEN 'Fixed' THEN '固定法力消耗'
        WHEN 'None' THEN '不消耗法力'
        ELSE skill_row.`MpCostPolicy`
    END AS `法力消耗狀態`,
    skill_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE skill_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE skill_row.`RecoveryStatus`
    END AS `回收狀態`,
    skill_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE skill_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE skill_row.`LocalizationStatus`
    END AS `在地化狀態`,
    skill_row.`ContentRecoveryRunId` AS `內容回收批次`
FROM `god2`.`skills` skill_row;
