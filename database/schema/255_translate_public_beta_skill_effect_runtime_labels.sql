-- 255_translate_public_beta_skill_effect_runtime_labels.sql
-- 技能效果候選表與技能目標欄位繁中化；runtime 讀取端保留繁中/英文雙相容。

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `effect_kind` = CASE `effect_kind`
    WHEN 'buff' THEN '增益'
    WHEN 'debuff' THEN '負面'
    WHEN 'cleanse' THEN '淨化'
    WHEN 'heal' THEN '治療'
    WHEN 'revive' THEN '復活'
    ELSE `effect_kind`
END
WHERE `effect_kind` IN ('buff','debuff','cleanse','heal','revive');

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `implementation_family` = CASE `implementation_family`
    WHEN 'stat_buff' THEN '屬性增益'
    WHEN 'element_formation_buff' THEN '五行陣法增益'
    WHEN 'element_talisman_attack_buff' THEN '五行符咒攻擊增益'
    WHEN 'element_transform_buff' THEN '五行轉換增益'
    WHEN 'heal_hp' THEN '生命治療'
    WHEN 'cleanse_general' THEN '通用淨化'
    WHEN 'debuff_confusion' THEN '混亂負面'
    WHEN 'debuff_petrify' THEN '石化負面'
    WHEN 'debuff_poison' THEN '中毒負面'
    WHEN 'debuff_seal' THEN '封印負面'
    WHEN 'debuff_sleep' THEN '睡眠負面'
    WHEN 'revive' THEN '復活'
    WHEN 'cleanse_confusion' THEN '淨化混亂'
    WHEN 'cleanse_petrify' THEN '淨化石化'
    WHEN 'cleanse_poison' THEN '淨化中毒'
    WHEN 'cleanse_seal' THEN '淨化封印'
    WHEN 'cleanse_sleep' THEN '淨化睡眠'
    ELSE `implementation_family`
END
WHERE `implementation_family` IN (
    'stat_buff','element_formation_buff','element_talisman_attack_buff','element_transform_buff',
    'heal_hp','cleanse_general','debuff_confusion','debuff_petrify','debuff_poison',
    'debuff_seal','debuff_sleep','revive','cleanse_confusion','cleanse_petrify',
    'cleanse_poison','cleanse_seal','cleanse_sleep'
);

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `provisional_duration_rule` = CASE `provisional_duration_rule`
    WHEN 'provisional_turns=3; official_duration_requires_S2C_or_server_DB' THEN '暫定持續 3 回合；正式持續時間需要服務端回傳封包或服務端資料庫證據'
    WHEN 'provisional_turns=max(1,level+1); official_duration_requires_S2C_or_server_DB' THEN '暫定持續 max(1,等級+1) 回合；正式持續時間需要服務端回傳封包或服務端資料庫證據'
    WHEN 'provisional_turns=max(1,level+1); official_duration_requires_S2C_or_server_DB; poison_tick_formula=user_provided_current_hp_div_10' THEN '暫定持續 max(1,等級+1) 回合；正式持續時間需要服務端回傳封包或服務端資料庫證據；中毒每跳公式採使用者提供的目前生命值十分之一'
    ELSE `provisional_duration_rule`
END
WHERE `provisional_duration_rule` REGEXP 'provisional|S2C|server_DB|poison_tick';

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `provisional_success_rule` = CASE `provisional_success_rule`
    WHEN 'provisional_success=100_percent' THEN '暫定成功率 100%'
    WHEN 'provisional_hit_rate_percent=85; apply_resistance_later' THEN '暫定命中率 85%；抗性修正待後續套用'
    WHEN 'provisional_hit_rate_percent=65; apply_resistance_later' THEN '暫定命中率 65%；抗性修正待後續套用'
    WHEN 'provisional_hit_rate_percent=75; apply_resistance_later' THEN '暫定命中率 75%；抗性修正待後續套用'
    WHEN 'provisional_hit_rate_percent=95; apply_resistance_later' THEN '暫定命中率 95%；抗性修正待後續套用'
    ELSE `provisional_success_rule`
END
WHERE `provisional_success_rule` REGEXP 'provisional|apply';

UPDATE `god2_game`.`skills`
SET `target_type` = CASE `target_type`
    WHEN 'Passive' THEN '被動'
    WHEN 'PetInnate' THEN '寵物天賦'
    WHEN 'MultipleEnemy' THEN '多名敵人'
    WHEN 'SingleEnemy' THEN '單一敵人'
    WHEN 'Buff' THEN '增益'
    ELSE `target_type`
END
WHERE `target_type` IN ('Passive','PetInnate','MultipleEnemy','SingleEnemy','Buff');

UPDATE `god2_game`.`life_skills`
SET `description_zh_tw` = REPLACE(`description_zh_tw`, 'Buff', '增益')
WHERE `description_zh_tw` LIKE '%Buff%';
