-- 261_translate_growth_and_merchant_type_labels.sql
-- Purpose: make class attribute growth type and merchant type labels readable in Traditional Chinese.

UPDATE `god2_game`.`class_stat_growth`
SET `growth_type` = CASE `growth_type`
    WHEN 'AutomaticPerLevel' THEN '每級自動成長'
    WHEN 'ManualPerLevel' THEN '每級手動配點'
    ELSE `growth_type`
END
WHERE `growth_type` IN ('AutomaticPerLevel','ManualPerLevel');

UPDATE `god2_game`.`merchants`
SET `merchant_type` = CASE `merchant_type`
    WHEN 'General' THEN '一般商店'
    ELSE `merchant_type`
END
WHERE `merchant_type` IN ('General');
