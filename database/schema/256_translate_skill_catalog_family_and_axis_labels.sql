-- 256_translate_skill_catalog_family_and_axis_labels.sql
-- 技能目錄分類、技能系列與公測技能效果作用軸繁中化；runtime 讀取端保留繁中/英文雙相容。

UPDATE `god2_game`.`skills`
SET `skill_category` = CASE `skill_category`
    WHEN 'Passive' THEN '被動'
    WHEN 'Active' THEN '主動'
    WHEN 'PetSkill' THEN '寵物技能'
    ELSE `skill_category`
END
WHERE `skill_category` IN ('Passive','Active','PetSkill');

UPDATE `god2_game`.`skills`
SET `skill_family` = CASE `skill_family`
    WHEN 'PetInnate' THEN '寵物天賦'
    WHEN 'WeaponMastery' THEN '武器精通'
    WHEN 'RebirthMastery' THEN '轉生精通'
    WHEN 'XianDaoMeditation' THEN '仙道冥思'
    WHEN 'Blessing' THEN '祝福'
    WHEN 'FormationTactics' THEN '陣法戰術'
    WHEN 'SpellCostMastery' THEN '法術消耗精通'
    WHEN 'NuwaElementalSpell' THEN '女媧五行法術'
    WHEN 'WaterSpell' THEN '水系法術'
    WHEN 'Claw' THEN '爪技'
    WHEN 'FireSpell' THEN '火系法術'
    WHEN 'Formless' THEN '無形技'
    WHEN 'MetalSpell' THEN '金系法術'
    WHEN 'Blade' THEN '刀技'
    WHEN 'EarthSpell' THEN '土系法術'
    WHEN 'WoodSpell' THEN '木系法術'
    WHEN 'Bow' THEN '弓技'
    WHEN 'MusicSpecial' THEN '音律特殊技'
    WHEN 'Spear' THEN '槍技'
    WHEN 'SpecialActive' THEN '特殊主動技'
    WHEN 'Axe' THEN '斧技'
    WHEN 'ClawMagic' THEN '爪系仙術'
    WHEN 'FormlessMagic' THEN '無形仙術'
    WHEN 'Staff' THEN '杖技'
    WHEN 'Sword' THEN '劍技'
    WHEN 'Whip' THEN '鞭技'
    ELSE `skill_family`
END
WHERE `skill_family` IN (
    'PetInnate','WeaponMastery','RebirthMastery','XianDaoMeditation','Blessing',
    'FormationTactics','SpellCostMastery','NuwaElementalSpell','WaterSpell','Claw',
    'FireSpell','Formless','MetalSpell','Blade','EarthSpell','WoodSpell','Bow',
    'MusicSpecial','Spear','SpecialActive','Axe','ClawMagic','FormlessMagic',
    'Staff','Sword','Whip'
);

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `status_or_axis` = CASE `status_or_axis`
    WHEN 'stats' THEN '屬性'
    WHEN 'element' THEN '五行'
    WHEN 'element_attack' THEN '五行攻擊'
    WHEN 'hp' THEN '生命值'
    WHEN 'confusion' THEN '混亂'
    WHEN 'petrify' THEN '石化'
    WHEN 'poison' THEN '中毒'
    WHEN 'seal' THEN '封印'
    WHEN 'sleep' THEN '睡眠'
    WHEN 'all_negative' THEN '全部負面狀態'
    WHEN 'dead_ally' THEN '死亡友方'
    ELSE `status_or_axis`
END
WHERE `status_or_axis` IN (
    'stats','element','element_attack','hp','confusion','petrify','poison',
    'seal','sleep','all_negative','dead_ally'
);

ALTER TABLE `god2_game`.`skills`
    MODIFY COLUMN `skill_family` varchar(50) NULL COMMENT '技能系列繁中分類',
    MODIFY COLUMN `skill_category` varchar(50) NULL COMMENT '技能分類繁中標籤';

ALTER TABLE `god2_game`.`public_beta_skill_effect_v0`
    MODIFY COLUMN `status_or_axis` varchar(64) NULL COMMENT '狀態效果或能力軸繁中標籤；不適用時為 NULL';
