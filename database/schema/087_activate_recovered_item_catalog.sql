-- Activate recovered official-client item identities now that inventory and fixed
-- recovery effects have production runtime authority. Unknown stack limits remain
-- NULL and therefore fail closed as single-item slots in the runtime.

UPDATE `god2_game`.`item_registry`
SET `enabled`=CASE
        WHEN `evidence_status` IN ('Verified','Recovered')
         AND `client_item_id` IS NOT NULL
         AND `name_zh_tw` IS NOT NULL
         AND `name_zh_tw`<>''
            THEN 1
        ELSE 0
    END,
    `admin_note`=CASE
        WHEN `evidence_status` IN ('Verified','Recovered')
         AND `client_item_id` IS NOT NULL
         AND `name_zh_tw` IS NOT NULL
         AND `name_zh_tw`<>''
            THEN '官方客戶端物品身分已恢復；服務端背包與已驗證固定回復效果已啟用。未知欄位維持NULL。'
        ELSE `admin_note`
    END;

UPDATE `god2_game`.`items` child_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=child_row.`item_id`
SET child_row.`enabled`=registry_row.`enabled`,child_row.`admin_note`=registry_row.`admin_note`;

UPDATE `god2_game`.`weapons` child_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=child_row.`item_id`
SET child_row.`enabled`=registry_row.`enabled`,child_row.`admin_note`=registry_row.`admin_note`;

UPDATE `god2_game`.`equipment` child_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=child_row.`item_id`
SET child_row.`enabled`=registry_row.`enabled`,child_row.`admin_note`=registry_row.`admin_note`;

UPDATE `god2_game`.`magic_treasures` child_row
JOIN `god2_game`.`item_registry` registry_row ON registry_row.`item_id`=child_row.`item_id`
SET child_row.`enabled`=registry_row.`enabled`,child_row.`admin_note`=registry_row.`admin_note`;

DROP TEMPORARY TABLE IF EXISTS `tmp_active_consumable_effects`;
CREATE TEMPORARY TABLE `tmp_active_consumable_effects` AS
SELECT registry_row.`item_id`,
       JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.statCandidates[0]')) AS `effect_text`,
       CASE
           WHEN rule_row.`normal_use`=1 AND rule_row.`battle_use`=1 THEN 'Both'
           WHEN rule_row.`battle_use`=1 THEN 'Battle'
           WHEN rule_row.`normal_use`=1 THEN 'World'
           ELSE NULL
       END AS `usage_scope`,
       CASE WHEN rule_row.`use_on_other`=1 THEN 'OtherAllowed' ELSE 'Self' END AS `target_policy`
FROM `god2_game`.`item_registry` registry_row
JOIN `god2`.`items` source_row ON source_row.`Id`=registry_row.`item_id`
JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=registry_row.`item_id`
WHERE registry_row.`enabled`=1
  AND rule_row.`enabled`=1
  AND registry_row.`item_category`='Consumable'
  AND JSON_UNQUOTE(JSON_EXTRACT(source_row.`PayloadJson`, '$.statCandidates[0]')) REGEXP
      '^(恢复生命[[:space:]]+[0-9]+|恢复法力[[:space:]]+[0-9]+|恢复生命[[:space:]]+[0-9]+，法力[[:space:]]+[0-9]+)$';

DELETE FROM `god2_game`.`item_effects`;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,
     `effect_text_zh_tw`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT effect_row.`item_id`,1,'RestoreHp',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '恢复生命[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED),
       effect_row.`usage_scope`,effect_row.`target_policy`,
       CONCAT('恢復生命 ',CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '恢复生命[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED)),
       'Recovered','官方客戶端 gamedata.csvZ 固定回復欄位',1
FROM `tmp_active_consumable_effects` effect_row
WHERE effect_row.`effect_text` REGEXP '^恢复生命[[:space:]]+[0-9]+'
  AND effect_row.`usage_scope` IS NOT NULL;

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,
     `effect_text_zh_tw`,`evidence_status`,`source_reference_zh_tw`,`enabled`)
SELECT effect_row.`item_id`,
       CASE WHEN effect_row.`effect_text` REGEXP '^恢复生命' THEN 2 ELSE 1 END,
       'RestoreMp',
       CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '法力[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED),
       effect_row.`usage_scope`,effect_row.`target_policy`,
       CONCAT('恢復法力 ',CAST(REGEXP_REPLACE(REGEXP_SUBSTR(effect_row.`effect_text`, '法力[[:space:]]+[0-9]+'),'[^0-9]','') AS UNSIGNED)),
       'Recovered','官方客戶端 gamedata.csvZ 固定回復欄位',1
FROM `tmp_active_consumable_effects` effect_row
WHERE effect_row.`effect_text` REGEXP '(^恢复法力|，法力)[[:space:]]+[0-9]+$'
  AND effect_row.`usage_scope` IS NOT NULL;

DROP TEMPORARY TABLE IF EXISTS `tmp_active_consumable_effects`;

CREATE OR REPLACE VIEW `god2_game`.`vw_item_catalog_health` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,item_row.`name_zh_tw` AS `繁體名稱`,
       item_row.`catalog_type_zh_tw` AS `資料表分類`,item_row.`item_category` AS `主要分類`,
       CASE
           WHEN item_row.`maximum_stack` IS NULL THEN '未知堆疊上限，服務端暫按單件處理'
           WHEN item_row.`item_category`='Consumable' AND item_row.`usable`=1 AND effect_row.`item_id` IS NULL THEN '可使用消耗品尚無已驗證效果'
           WHEN rule_row.`item_id` IS NULL OR rule_row.`enabled`=0 THEN '使用限制尚未啟用'
           ELSE '可用'
       END AS `健康狀態`,
       item_row.`evidence_status` AS `證據狀態`,item_row.`enabled` AS `服務端啟用`
FROM `god2_game`.`vw_all_item_definitions` item_row
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
LEFT JOIN (SELECT DISTINCT `item_id` FROM `god2_game`.`item_effects` WHERE `enabled`=1) effect_row ON effect_row.`item_id`=item_row.`item_id`;
