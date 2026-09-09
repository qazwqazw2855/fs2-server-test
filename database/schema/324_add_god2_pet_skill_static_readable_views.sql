DROP VIEW IF EXISTS `god2`.`vw_pet_content_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_pet_innate_definitions_readable`;
DROP VIEW IF EXISTS `god2`.`vw_pet_egg_relationships_readable`;
DROP VIEW IF EXISTS `god2`.`vw_skills_readable`;
DROP VIEW IF EXISTS `god2`.`vw_skill_content_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_skill_semantic_profiles_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_pet_skill_static_observations_readable`;

CREATE VIEW `god2`.`vw_pet_content_profiles_readable` AS
SELECT
    pet.`ClientPetId` AS `客戶端神獸ID`,
    pet.`ItemId` AS `物品ID`,
    item_view.`物品名稱` AS `對應物品名稱`,
    pet.`NameZhTw` AS `神獸名稱`,
    pet.`PetFamily` AS `神獸類型`,
    pet.`GrowthType` AS `成長類型`,
    CASE WHEN pet.`BaseStatsJson` IS NULL OR pet.`BaseStatsJson` = '{}' THEN '尚未整理基礎數值' ELSE '已有基礎數值證據' END AS `基礎數值狀態`,
    CASE WHEN pet.`SkillReferencesJson` IS NULL OR pet.`SkillReferencesJson` = '[]' THEN '尚未整理技能參照' ELSE '已有技能參照證據' END AS `技能參照狀態`,
    CASE WHEN pet.`EvolutionReferencesJson` IS NULL OR pet.`EvolutionReferencesJson` = '[]' THEN '尚未整理進化參照' ELSE '已有進化參照證據' END AS `進化參照狀態`,
    CASE
        WHEN pet.`PetFamily` LIKE '%Combat%' THEN '戰鬥神獸：出戰、能力值、技能、升級與戰鬥行動'
        ELSE '神獸資料：持有、顯示、孵化、成長或圖鑑對照'
    END AS `功能對照`
FROM `god2`.`pet_content_profiles` pet
LEFT JOIN `god2`.`vw_items_readable` item_view
    ON item_view.`物品ID` = pet.`ItemId`;

CREATE VIEW `god2`.`vw_pet_innate_definitions_readable` AS
SELECT
    innate.`InnateId` AS `天賦ID`,
    innate.`NameZhTw` AS `天賦名稱`,
    innate.`ArtifactNameZhTw` AS `關聯寶珠`,
    innate.`DescriptionZhTw` AS `天賦說明`,
    CASE
        WHEN innate.`EffectReference` IS NULL OR innate.`EffectReference` = '' THEN '無效果參照或不需參照'
        ELSE '有效果參照'
    END AS `效果參照狀態`,
    innate.`EffectValue` AS `效果數值`,
    CASE WHEN innate.`ProductionEnabled` = 1 THEN '正式啟用' ELSE '正式未啟用' END AS `正式狀態`,
    CASE
        WHEN innate.`DescriptionZhTw` LIKE '%回血%' OR innate.`DescriptionZhTw` LIKE '%回覆Hp%' THEN '神獸天賦：屬性吸收回血'
        WHEN innate.`DescriptionZhTw` LIKE '%減傷%' THEN '神獸天賦：傷害減免'
        WHEN innate.`DescriptionZhTw` LIKE '%增加%' THEN '神獸天賦：能力值增加'
        WHEN innate.`InnateId` = 0 THEN '神獸天賦：沒有天賦'
        ELSE '神獸天賦：戰鬥被動效果'
    END AS `功能對照`
FROM `god2`.`pet_innate_definitions` innate;

CREATE VIEW `god2`.`vw_pet_egg_relationships_readable` AS
SELECT
    egg.`EggItemId` AS `蛋物品ID`,
    egg_item.`物品名稱` AS `蛋名稱`,
    egg.`HatchResultItemId` AS `孵化結果物品ID`,
    COALESCE(result_item.`物品名稱`, egg.`HatchResultNameZhTw`) AS `孵化結果名稱`,
    egg.`DeclaredProbability` AS `宣告機率`,
    egg.`EffectiveProbability` AS `有效機率`,
    egg.`RelationshipStatus` AS `關係狀態`,
    CASE WHEN egg.`Enabled` = 1 THEN '資料啟用' ELSE '資料未啟用' END AS `資料狀態`,
    CASE WHEN egg.`ProductionHatchEnabled` = 1 THEN '正式孵化啟用' ELSE '正式孵化未啟用' END AS `正式孵化狀態`,
    '神獸蛋孵化、結果神獸、機率與背包發放' AS `功能對照`
FROM `god2`.`pet_egg_relationships` egg
LEFT JOIN `god2`.`vw_items_readable` egg_item
    ON egg_item.`物品ID` = egg.`EggItemId`
LEFT JOIN `god2`.`vw_items_readable` result_item
    ON result_item.`物品ID` = egg.`HatchResultItemId`;

CREATE VIEW `god2`.`vw_skills_readable` AS
SELECT
    skill.`Id` AS `技能ID`,
    skill.`Code` AS `服務端代碼`,
    COALESCE(NULLIF(skill.`NameZhTw`, ''), skill.`Name`) AS `技能名稱`,
    skill.`DescriptionZhTw` AS `技能說明`,
    skill.`SkillFamily` AS `技能家族`,
    skill.`TargetPolicy` AS `目標規則`,
    skill.`MaxLevel` AS `最高等級`,
    skill.`RequiredLevel` AS `需求等級`,
    skill.`MpCost` AS `MP消耗`,
    skill.`MpCostPolicy` AS `MP消耗規則`,
    skill.`RecoveryStatus` AS `恢復狀態`,
    skill.`LocalizationStatus` AS `在地化狀態`,
    CASE
        WHEN skill.`NameZhTw` LIKE '%冥思%' THEN '被動或狀態技能：冥思相關回復/加持，需要黑箱確認實際效果'
        WHEN skill.`NameZhTw` LIKE '%解除%' THEN '技能效果：解除異常狀態'
        WHEN skill.`NameZhTw` LIKE '%回血%' OR skill.`DescriptionZhTw` LIKE '%回覆%' THEN '技能效果：回復HP或吸收傷害'
        WHEN skill.`NameZhTw` LIKE '%減傷%' THEN '技能效果：傷害減免'
        WHEN skill.`NameZhTw` LIKE '%上升%' OR skill.`DescriptionZhTw` LIKE '%增加%' THEN '技能效果：能力值提升'
        ELSE '技能施放、目標選擇、MP消耗、效果與狀態套用'
    END AS `功能對照`
FROM `god2`.`skills` skill;

CREATE VIEW `god2`.`vw_skill_content_profiles_readable` AS
SELECT
    content.`SkillId` AS `技能ID`,
    COALESCE(skill_view.`技能名稱`, content.`NameZhTw`) AS `技能名稱`,
    content.`ClientSkillId` AS `客戶端技能ID`,
    content.`SkillFamily` AS `技能家族`,
    content.`TargetPolicy` AS `目標規則`,
    content.`MpCostPolicy` AS `MP消耗規則`,
    content.`MpCost` AS `MP消耗`,
    CASE WHEN content.`EffectReferencesJson` IS NULL OR content.`EffectReferencesJson` = '[]' THEN '尚未整理效果參照' ELSE '已有效果參照證據' END AS `效果參照狀態`,
    CASE WHEN content.`StatusReferencesJson` IS NULL OR content.`StatusReferencesJson` = '[]' THEN '尚未整理狀態參照' ELSE '已有狀態參照證據' END AS `狀態參照狀態`,
    CASE WHEN content.`AnimationKey` IS NULL OR content.`AnimationKey` = '' THEN '尚未整理施放表現' ELSE '已有施放表現參照' END AS `施放表現狀態`,
    CASE WHEN content.`Enabled` = 1 THEN '正式啟用' ELSE '正式未啟用' END AS `正式狀態`,
    CASE
        WHEN skill_view.`功能對照` IS NOT NULL THEN skill_view.`功能對照`
        ELSE '技能內容：客戶端技能、效果參照、狀態參照與施放表現'
    END AS `功能對照`,
    content.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`skill_content_profiles` content
LEFT JOIN `god2`.`vw_skills_readable` skill_view
    ON skill_view.`技能ID` = content.`SkillId`;

CREATE VIEW `god2`.`vw_skill_semantic_profiles_readable` AS
SELECT
    semantic_profile.`SkillId` AS `技能ID`,
    skill_view.`技能名稱`,
    semantic_profile.`ClientSkillId` AS `客戶端技能ID`,
    semantic_profile.`Profession` AS `職業`,
    semantic_profile.`SkillRank` AS `技能階級`,
    semantic_profile.`SkillFamily` AS `技能家族`,
    semantic_profile.`TargetPolicy` AS `目標規則`,
    semantic_profile.`MpCostPolicy` AS `MP消耗規則`,
    semantic_profile.`MpCost` AS `MP消耗`,
    CASE WHEN semantic_profile.`EffectReferencesJson` IS NULL OR semantic_profile.`EffectReferencesJson` = '[]' THEN '尚未整理效果語意' ELSE '已有效果語意證據' END AS `效果語意狀態`,
    CASE WHEN semantic_profile.`StatusReferencesJson` IS NULL OR semantic_profile.`StatusReferencesJson` = '[]' THEN '尚未整理狀態語意' ELSE '已有狀態語意證據' END AS `狀態語意狀態`,
    CASE WHEN semantic_profile.`ProductionEnabled` = 1 THEN '正式啟用' ELSE '正式未啟用' END AS `正式狀態`,
    CASE
        WHEN skill_view.`功能對照` IS NOT NULL THEN skill_view.`功能對照`
        ELSE '技能語意：職業、階級、目標、MP消耗、效果與狀態'
    END AS `功能對照`
FROM `god2`.`skill_semantic_profiles` semantic_profile
LEFT JOIN `god2`.`vw_skills_readable` skill_view
    ON skill_view.`技能ID` = semantic_profile.`SkillId`;

CREATE VIEW `god2`.`vw_blackbox_pet_skill_static_observations_readable` AS
SELECT
    '神獸資料' AS `觀察類型`,
    pet_view.`客戶端神獸ID` AS `主要ID`,
    pet_view.`神獸名稱` AS `名稱`,
    pet_view.`神獸類型` AS `分類`,
    pet_view.`功能對照`,
    CASE
        WHEN pet_view.`基礎數值狀態` = '尚未整理基礎數值' THEN '第一優先：測神獸面板HP/MP/攻防/速度'
        WHEN pet_view.`技能參照狀態` = '尚未整理技能參照' THEN '第二優先：測神獸技能欄與戰鬥技能'
        ELSE '第三優先：測神獸顯示、成長與進化'
    END AS `黑箱測試優先級`,
    CONCAT('物品ID=', COALESCE(pet_view.`物品ID`, 0), '；成長=', COALESCE(pet_view.`成長類型`, ''), '；', pet_view.`基礎數值狀態`, '；', pet_view.`技能參照狀態`) AS `測試重點`
FROM `god2`.`vw_pet_content_profiles_readable` pet_view

UNION ALL

SELECT
    '神獸天賦' AS `觀察類型`,
    innate_view.`天賦ID` AS `主要ID`,
    innate_view.`天賦名稱` AS `名稱`,
    COALESCE(innate_view.`關聯寶珠`, '') AS `分類`,
    innate_view.`功能對照`,
    CASE
        WHEN innate_view.`正式狀態` = '正式啟用' THEN '第一優先：測戰鬥中被動效果是否觸發'
        ELSE '擱置：天賦正式未啟用'
    END AS `黑箱測試優先級`,
    CONCAT('效果值=', COALESCE(innate_view.`效果數值`, 0), '；說明=', COALESCE(innate_view.`天賦說明`, '')) AS `測試重點`
FROM `god2`.`vw_pet_innate_definitions_readable` innate_view

UNION ALL

SELECT
    '技能語意' AS `觀察類型`,
    skill_view.`技能ID` AS `主要ID`,
    skill_view.`技能名稱` AS `名稱`,
    COALESCE(skill_view.`技能家族`, '') AS `分類`,
    skill_view.`功能對照`,
    CASE
        WHEN skill_view.`MP消耗規則` = 'EvidenceBlocked' OR skill_view.`目標規則` = 'Unknown' THEN '第一優先：測技能目標、MP消耗與是否可施放'
        WHEN skill_view.`功能對照` LIKE '%冥思%' THEN '第一優先：測冥思實際回復/加持效果'
        ELSE '第二優先：測技能效果、狀態、動畫與冷卻'
    END AS `黑箱測試優先級`,
    CONCAT('職業=', COALESCE(skill_view.`職業`, ''), '；階級=', COALESCE(skill_view.`技能階級`, 0), '；MP=', COALESCE(skill_view.`MP消耗`, 0), '；', skill_view.`效果語意狀態`, '；', skill_view.`狀態語意狀態`) AS `測試重點`
FROM `god2`.`vw_skill_semantic_profiles_readable` skill_view;
