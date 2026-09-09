-- 269_finish_display_text_token_case_residue.sql
-- Game functions: remaining display text cleanup for experience boosts, boss references,
-- and custom pet labels missed by case-sensitive replacements.

UPDATE `god2_game`.`items`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'EXP', '經驗值'),
        'boss', '首領'),
        'diy', '自訂')
WHERE `description_zh_tw` REGEXP 'EXP|boss|diy';

UPDATE `god2_game`.`item_registry`
SET `description_zh_tw` = REPLACE(REPLACE(REPLACE(`description_zh_tw`,
        'EXP', '經驗值'),
        'boss', '首領'),
        'diy', '自訂')
WHERE `description_zh_tw` REGEXP 'EXP|boss|diy';

UPDATE `god2_game`.`item_usage_rules`
SET `condition_text_zh_tw` = REPLACE(REPLACE(REPLACE(`condition_text_zh_tw`,
        'EXP', '經驗值'),
        'boss', '首領'),
        'diy', '自訂')
WHERE `condition_text_zh_tw` REGEXP 'EXP|boss|diy';

UPDATE `god2_game`.`monsters`
SET `name_zh_tw` = REPLACE(REPLACE(REPLACE(`name_zh_tw`,
        'EXP', '經驗值'),
        'boss', '首領'),
        'diy', '自訂')
WHERE `name_zh_tw` REGEXP 'EXP|boss|diy';
