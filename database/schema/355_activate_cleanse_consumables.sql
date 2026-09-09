-- Schema 355
-- Game function: consumable items remove active battle debuffs.
-- Evidence source: Traditional Chinese item descriptions explicitly say remove poison, sleep, seal, petrify, or confusion.
-- Runtime bridge: MariaDbItemUseRepository removes battle_status_instances and writes battle_status_removals before consuming the item.

INSERT INTO `god2`.`status_effects`
    (`Id`,`Code`,`Name`,`DurationSeconds`)
VALUES
    (91001,'poison','中毒',0),
    (91002,'sleep','睡眠',0),
    (91003,'seal','神仙封',0),
    (91004,'petrify','石化',0),
    (91005,'confusion','混亂',0)
ON DUPLICATE KEY UPDATE
    `Code`=VALUES(`Code`),
    `Name`=VALUES(`Name`),
    `DurationSeconds`=VALUES(`DurationSeconds`);

UPDATE `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
SET effect_row.`runtime_eligible`=1,
    effect_row.`enabled`=1
WHERE effect_row.`effect_type` IN ('解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂')
  AND effect_row.`numeric_value`=1
  AND effect_row.`usage_scope`='戰鬥'
  AND item_row.`usable`=1
  AND item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND rule_row.`battle_use`=1
  AND item_row.`description_zh_tw` REGEXP '解除(石化|混亂|中毒|神仙封|封印|睡眠|狀態|異常|全部)';

CREATE OR REPLACE VIEW `god2_game`.`vw_cleanse_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`usage_scope` AS `使用場景`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：解除戰鬥異常狀態' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type` IN ('解除中毒','解除睡眠','解除神仙封','解除石化','解除混亂')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;
