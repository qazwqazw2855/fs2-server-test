-- 268_normalize_display_text_english_tokens.sql
-- Game functions: readable item/equipment/treasure effect text, boss names,
-- experience item names, and quest objective display text.

UPDATE `god2_game`.`equipment`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `description_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`equipment`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `name_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`items`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `description_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`items`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `name_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`item_registry`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `description_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`item_registry`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `name_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`item_usage_rules`
SET `condition_text_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`condition_text_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `condition_text_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`magic_treasures`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `description_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`magic_treasures`
SET `effect_description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(`effect_description_zh_tw`,
        'Max Hp Mp', '最大生命與最大法力'),
        'Max Hp', '最大生命'),
        'Max Mp', '最大法力'),
        'MPHP', '生命法力'),
        'BOSS', '首領'),
        'DIY', '自訂'),
        'exp', '經驗值')
WHERE `effect_description_zh_tw` REGEXP 'Max Hp|Max Mp|MPHP|BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`monsters`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `name_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`quest_objectives`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `description_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';

UPDATE `god2_game`.`quests`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'BOSS', '首領'),
        'DIY', '自訂'),
        'EXP', '經驗值'),
        'exp', '經驗值')
WHERE `name_zh_tw` REGEXP 'BOSS|DIY|exp|EXP';
