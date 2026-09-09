CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_pet_content_profiles_readable` AS
SELECT
    profile_row.`ProfileId` AS `寵物內容ProfileID`,
    profile_row.`ClientPetId` AS `客戶端寵物ID`,
    profile_row.`ItemId` AS `關聯道具ID`,
    COALESCE(item_row.`NameZhTw`, item_row.`DisplayName`, item_row.`Name`, profile_row.`NameZhTw`) AS `關聯道具名稱`,
    profile_row.`NameZhTw` AS `寵物名稱`,
    profile_row.`PetFamily` AS `寵物家族Key`,
    CASE profile_row.`PetFamily`
        WHEN 'CombatPet' THEN '戰鬥寵物'
        WHEN 'Immortal' THEN '神仙'
        WHEN 'Mount' THEN '坐騎'
        ELSE COALESCE(profile_row.`PetFamily`, '未分類')
    END AS `寵物家族`,
    profile_row.`GrowthType` AS `成長類型Key`,
    CASE
        WHEN profile_row.`GrowthType` IS NULL OR profile_row.`GrowthType` = '' THEN '成長類型未確認'
        ELSE profile_row.`GrowthType`
    END AS `成長類型`,
    CASE
        WHEN profile_row.`BaseStatsJson` IS NULL OR profile_row.`BaseStatsJson` = '' THEN '基礎能力未回收'
        ELSE '基礎能力已回收為結構資料'
    END AS `基礎能力狀態`,
    CASE
        WHEN profile_row.`SkillReferencesJson` IS NULL OR profile_row.`SkillReferencesJson` = '' THEN '技能引用未回收'
        ELSE '技能引用已回收為結構資料'
    END AS `技能引用狀態`,
    CASE
        WHEN profile_row.`EvolutionReferencesJson` IS NULL OR profile_row.`EvolutionReferencesJson` = '' THEN '進化引用未回收'
        ELSE '進化引用已回收為結構資料'
    END AS `進化引用狀態`,
    CASE
        WHEN game_pet_row.`pet_template_id` IS NULL THEN '尚未對應正式寵物模板'
        ELSE '已對應正式寵物模板'
    END AS `正式模板對應狀態`,
    game_pet_row.`pet_template_id` AS `正式寵物模板ID`,
    game_pet_row.`base_level` AS `正式基礎等級`,
    game_pet_row.`base_max_hp` AS `正式基礎最大生命`,
    game_pet_row.`base_max_mp` AS `正式基礎最大法力`,
    game_pet_row.`enabled` AS `正式模板啟用`,
    profile_row.`RunId` AS `內容回收批次`
FROM `god2`.`pet_content_profiles` profile_row
LEFT JOIN `god2`.`items` item_row
    ON item_row.`Id` = profile_row.`ItemId`
LEFT JOIN `god2_game`.`pet_templates` game_pet_row
    ON game_pet_row.`name_zh_tw` = profile_row.`NameZhTw`;
