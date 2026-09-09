-- 254_finish_public_beta_skill_rule_traditional_labels.sql
-- 移除公測技能效果最低服務端規則中的殘留英文技術詞，保留原本語意但改為繁中可讀。

UPDATE `god2_game`.`public_beta_skill_effect_v0`
SET `minimum_working_server_rule` = CASE `minimum_working_server_rule`
    WHEN '套用 CSV 非零數值屬性欄位作為加成 Buff 候選' THEN '套用資料表非零數值屬性欄位作為加成增益候選'
    WHEN '套用 CSV 五行數值欄位作為加成 Buff 候選' THEN '套用資料表五行數值欄位作為加成增益候選'
    WHEN '套用五行符咒攻擊 Buff 身分；實際幅度仍待 cbspec 數值欄位確認' THEN '套用五行符咒攻擊增益身分；實際幅度仍待公測技能數值欄位確認'
    WHEN '套用正向 HP 變化；暫定值為治療者魔攻*0.5 加等級平補，取得 S2C 實測差值後替換' THEN '套用正向生命值變化；暫定值為治療者魔攻*0.5 加等級平補，取得服務端回傳封包實測差值後替換'
    WHEN '套用中毒狀態：每次毒傷扣目標目前 HP 的十分之一取整；此為使用者提供公式，取整與最低值仍待封包驗證' THEN '套用中毒狀態：每次毒傷扣目標目前生命值的十分之一取整；此為使用者提供公式，取整與最低值仍待封包驗證'
    WHEN '套用混亂狀態：隨機選擇合法目標或行動類型，不直接套 HP 公式' THEN '套用混亂狀態：隨機選擇合法目標或行動類型，不直接套生命值公式'
    WHEN '若目標死亡則復活；暫定回復 25% HP，且不超過最大 HP' THEN '若目標死亡則復活；暫定回復 25% 生命值，且不超過最大生命值'
    WHEN '若目標死亡則復活；暫定回復 50% HP，且不超過最大 HP' THEN '若目標死亡則復活；暫定回復 50% 生命值，且不超過最大生命值'
    WHEN '若目標死亡則復活；暫定回復 75% HP，且不超過最大 HP' THEN '若目標死亡則復活；暫定回復 75% 生命值，且不超過最大生命值'
    ELSE `minimum_working_server_rule`
END
WHERE `minimum_working_server_rule` REGEXP 'CSV|Buff|HP|S2C|cbspec';
