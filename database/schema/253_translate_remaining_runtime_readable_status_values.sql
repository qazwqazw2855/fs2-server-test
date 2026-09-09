-- 253_translate_remaining_runtime_readable_status_values.sql
-- 清理正式庫下一批仍可見的英文狀態/規則文字；服務端讀取端保留舊英文與新繁中雙相容。

UPDATE `god2_game`.`npc_appearance_identities`
SET `runtime_family_status` = CASE `runtime_family_status`
    WHEN 'Derived' THEN '已推導'
    WHEN 'EvidenceBlocked' THEN '證據不足'
    ELSE `runtime_family_status`
END
WHERE `runtime_family_status` IN ('Derived','EvidenceBlocked');

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `minimum_working_server_rule` = CASE `minimum_working_server_rule`
    WHEN 'apply nonzero numeric stat fields from CSV as additive Buff candidates' THEN '套用 CSV 非零數值屬性欄位作為加成 Buff 候選'
    WHEN 'apply numeric element fields from CSV as additive Buff candidates' THEN '套用 CSV 五行數值欄位作為加成 Buff 候選'
    WHEN 'apply element attack Buff identity; magnitude unresolved in cbspec numeric fields' THEN '套用五行符咒攻擊 Buff 身分；實際幅度仍待 cbspec 數值欄位確認'
    WHEN 'remove matching negative status; general cleanse removes seal/sleep/petrify/poison/confusion' THEN '移除對應負面狀態；通用淨化可移除封印、睡眠、石化、中毒、混亂'
    WHEN 'apply_positive_hp_delta; provisional_amount=healer_magic_attack*0.5 + level_scaled_flat; replace_with_observed_S2C_delta_when_available' THEN '套用正向 HP 變化；暫定值為治療者魔攻*0.5 加等級平補，取得 S2C 實測差值後替換'
    WHEN 'apply_status(confusion): choose random legal target/action-family; no direct HP formula' THEN '套用混亂狀態：隨機選擇合法目標或行動類型，不直接套 HP 公式'
    WHEN 'apply_status(petrify): skip action; provisional_cannot_evade=true' THEN '套用石化狀態：跳過行動，暫定不可閃避'
    WHEN 'apply_status(poison): each poison tick subtracts floor(current_hp/10) from the target current HP; user-provided formula; rounding/minimum clamp pending packet validation' THEN '套用中毒狀態：每次毒傷扣目標目前 HP 的十分之一取整；此為使用者提供公式，取整與最低值仍待封包驗證'
    WHEN 'apply_status(seal): block active skill/cast commands; allow basic/defend/item policy configurable' THEN '套用封印狀態：阻擋主動技能或施法指令，普通攻擊、防禦、物品策略可設定'
    WHEN 'apply_status(sleep): skip action; provisional_remove_on_damage=true' THEN '套用睡眠狀態：跳過行動，暫定受傷後解除'
    WHEN 'revive_if_dead; provisional_hp_percent=25; clamp_to_max_hp' THEN '若目標死亡則復活；暫定回復 25% HP，且不超過最大 HP'
    WHEN 'revive_if_dead; provisional_hp_percent=50; clamp_to_max_hp' THEN '若目標死亡則復活；暫定回復 50% HP，且不超過最大 HP'
    WHEN 'revive_if_dead; provisional_hp_percent=75; clamp_to_max_hp' THEN '若目標死亡則復活；暫定回復 75% HP，且不超過最大 HP'
    ELSE `minimum_working_server_rule`
END
WHERE `minimum_working_server_rule` REGEXP 'apply|remove|revive';

UPDATE `god2_game`.`npcs`
SET `interaction_family` = CASE `interaction_family`
    WHEN 'Unknown' THEN '未知'
    WHEN 'Merchant' THEN '商店'
    WHEN 'Dialog' THEN '對話'
    WHEN 'Quest' THEN '任務'
    WHEN 'MerchantQuest' THEN '商店任務'
    ELSE `interaction_family`
END
WHERE `interaction_family` IN ('Unknown','Merchant','Dialog','Quest','MerchantQuest');

ALTER TABLE `god2_game`.`client_item_usage_flag_additions`
    DROP CONSTRAINT IF EXISTS `ck_client_item_usage_addition_binding`;

UPDATE `god2_game`.`client_item_usage_flag_additions`
SET `binding_status` = CASE `binding_status`
    WHEN 'EvidenceBlockedMissingCanonicalItemBinding' THEN '證據不足：缺少正式物品綁定'
    ELSE `binding_status`
END
WHERE `binding_status`='EvidenceBlockedMissingCanonicalItemBinding';

ALTER TABLE `god2_game`.`client_item_usage_flag_additions`
    MODIFY COLUMN `binding_status` varchar(80) NOT NULL COMMENT '物品使用旗標綁定狀態',
    ADD CONSTRAINT `ck_client_item_usage_addition_binding` CHECK (`binding_status`='證據不足：缺少正式物品綁定');

UPDATE `god2_player`.`characters`
SET `current_direction` = CASE `current_direction`
    WHEN 'Unknown' THEN '未知'
    WHEN 'North' THEN '北'
    WHEN 'South' THEN '南'
    WHEN 'East' THEN '東'
    WHEN 'West' THEN '西'
    WHEN 'NorthEast' THEN '東北'
    WHEN 'NorthWest' THEN '西北'
    WHEN 'SouthEast' THEN '東南'
    WHEN 'SouthWest' THEN '西南'
    ELSE `current_direction`
END
WHERE `current_direction` IN ('Unknown','North','South','East','West','NorthEast','NorthWest','SouthEast','SouthWest');

ALTER TABLE `god2_game`.`skills`
    DROP CONSTRAINT IF EXISTS `ck_skills_damage_type`,
    DROP CONSTRAINT IF EXISTS `ck_skills_element`;

UPDATE `god2_game`.`skills`
SET `element` = CASE `element`
    WHEN 'None' THEN '無'
    WHEN 'Unknown' THEN '未知'
    WHEN 'Metal' THEN '金'
    WHEN 'Wood' THEN '木'
    WHEN 'Water' THEN '水'
    WHEN 'Fire' THEN '火'
    WHEN 'Earth' THEN '土'
    ELSE `element`
END
WHERE `element` IN ('None','Unknown','Metal','Wood','Water','Fire','Earth');

UPDATE `god2_game`.`skills`
SET `damage_type` = CASE `damage_type`
    WHEN 'Physical' THEN '物理'
    WHEN 'Magic' THEN '法術'
    WHEN 'Pure' THEN '純傷'
    WHEN 'Healing' THEN '治療'
    WHEN 'Status' THEN '狀態'
    WHEN 'Unknown' THEN '未知'
    ELSE `damage_type`
END
WHERE `damage_type` IN ('Physical','Magic','Pure','Healing','Status','Unknown');

ALTER TABLE `god2_game`.`skills`
    MODIFY COLUMN `element` varchar(20) NULL COMMENT '技能五行繁中標籤',
    MODIFY COLUMN `damage_type` varchar(20) NULL COMMENT '技能傷害型態繁中標籤',
    ADD CONSTRAINT `ck_skills_damage_type` CHECK (`damage_type` IS NULL OR `damage_type` IN ('物理','法術','純傷','治療','狀態','未知')),
    ADD CONSTRAINT `ck_skills_element` CHECK (`element` IS NULL OR `element` IN ('金','木','水','火','土','無','未知'));

ALTER TABLE `god2_game`.`pet_categories`
    DROP CONSTRAINT IF EXISTS `ck_pet_categories_pattern`;

UPDATE `god2_game`.`pet_categories`
SET `growth_pattern` = CASE `growth_pattern`
    WHEN 'Pure' THEN '單一成長'
    WHEN 'Mixed' THEN '混合成長'
    WHEN 'Average' THEN '平均成長'
    WHEN 'Unknown' THEN '未知'
    ELSE `growth_pattern`
END
WHERE `growth_pattern` IN ('Pure','Mixed','Average','Unknown');

ALTER TABLE `god2_game`.`pet_categories`
    MODIFY COLUMN `growth_pattern` varchar(20) NOT NULL COMMENT '寵物成長型態繁中分類',
    ADD CONSTRAINT `ck_pet_categories_pattern` CHECK (`growth_pattern` IN ('單一成長','混合成長','平均成長','未知'));
