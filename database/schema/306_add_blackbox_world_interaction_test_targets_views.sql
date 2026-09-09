-- Owner-facing black-box world interaction target views.
-- These views focus formal server testing on portal, NPC, merchant, and shop
-- behavior that players will touch immediately during an opening test.

CREATE OR REPLACE VIEW `god2_game`.`vw_blackbox_portal_test_targets_readable` AS
SELECT
    portal_row.`portal_id` AS `傳送點ID`,
    portal_row.`name_zh_tw` AS `傳送點名稱`,
    portal_row.`source_map_id` AS `來源地圖ID`,
    source_map.`name_zh_tw` AS `來源地圖`,
    portal_row.`source_x` AS `來源X`,
    portal_row.`source_y` AS `來源Y`,
    portal_row.`source_radius` AS `觸發半徑`,
    portal_row.`destination_map_id` AS `目標地圖ID`,
    destination_map.`name_zh_tw` AS `目標地圖`,
    portal_row.`destination_x` AS `目標X`,
    portal_row.`destination_y` AS `目標Y`,
    portal_row.`destination_direction` AS `目標方向`,
    portal_row.`required_level` AS `需求等級`,
    portal_row.`required_quest_id` AS `需求任務ID`,
    portal_row.`cost_item_id` AS `消耗物品ID`,
    portal_row.`cost_quantity` AS `消耗數量`,
    portal_row.`enabled` AS `已啟用`,
    CASE
        WHEN portal_row.`enabled` = 1 THEN '第一批：已啟用，直接實機走點驗證'
        WHEN source_map.`enabled` = 1 AND destination_map.`enabled` = 1 THEN '第二批：兩端地圖已啟用，可黑箱開通前驗證座標'
        ELSE '暫緩：來源或目標地圖尚未啟用'
    END AS `黑箱優先級`
FROM `god2_game`.`portals` portal_row
LEFT JOIN `god2_game`.`maps` source_map
  ON source_map.`map_id` = portal_row.`source_map_id`
LEFT JOIN `god2_game`.`maps` destination_map
  ON destination_map.`map_id` = portal_row.`destination_map_id`
ORDER BY
    portal_row.`enabled` DESC,
    source_map.`enabled` DESC,
    destination_map.`enabled` DESC,
    portal_row.`source_map_id`,
    portal_row.`portal_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_blackbox_npc_merchant_test_targets_readable` AS
SELECT
    npc_row.`npc_id` AS `NPCID`,
    npc_row.`name_zh_tw` AS `NPC名稱`,
    npc_row.`npc_type` AS `NPC類型`,
    npc_row.`interaction_family` AS `互動類型`,
    npc_row.`default_dialog_id` AS `預設對話ID`,
    dialog_row.`title_zh_tw` AS `對話標題`,
    CASE WHEN dialog_row.`dialog_id` IS NULL THEN 0 ELSE 1 END AS `有對話`,
    npc_row.`merchant_id` AS `NPC商人ID`,
    merchant_row.`merchant_id` AS `商人ID`,
    merchant_row.`name_zh_tw` AS `商人名稱`,
    merchant_row.`merchant_type` AS `商人類型`,
    COUNT(DISTINCT inventory_row.`merchant_inventory_id`) AS `商品數`,
    COUNT(DISTINCT CASE WHEN inventory_row.`enabled` = 1 THEN inventory_row.`merchant_inventory_id` END) AS `已啟用商品數`,
    COUNT(DISTINCT spawn_row.`spawn_id`) AS `出生點數`,
    COUNT(DISTINCT CASE WHEN spawn_row.`enabled` = 1 THEN spawn_row.`spawn_id` END) AS `已啟用出生點數`,
    MIN(spawn_row.`map_id`) AS `第一個地圖ID`,
    MIN(spawn_row.`map_name_cache`) AS `第一個地圖名稱`,
    MIN(spawn_row.`position_x`) AS `第一個X`,
    MIN(spawn_row.`position_y`) AS `第一個Y`,
    npc_row.`enabled` AS `NPC已啟用`,
    merchant_row.`enabled` AS `商人已啟用`,
    CASE
        WHEN npc_row.`enabled` = 1 AND COUNT(DISTINCT CASE WHEN spawn_row.`enabled` = 1 THEN spawn_row.`spawn_id` END) > 0
             AND merchant_row.`enabled` = 1 AND COUNT(DISTINCT CASE WHEN inventory_row.`enabled` = 1 THEN inventory_row.`merchant_inventory_id` END) > 0
            THEN '第一批：NPC/商店/商品都可直接實機測'
        WHEN npc_row.`enabled` = 1 AND COUNT(DISTINCT CASE WHEN spawn_row.`enabled` = 1 THEN spawn_row.`spawn_id` END) > 0
            THEN '第二批：NPC 可實機點擊，商店或商品待補'
        WHEN npc_row.`enabled` = 1
            THEN '第三批：NPC 已啟用但出生點待補'
        ELSE '暫緩：NPC 尚未啟用'
    END AS `黑箱優先級`
FROM `god2_game`.`npcs` npc_row
LEFT JOIN `god2_game`.`npc_dialogs` dialog_row
  ON dialog_row.`dialog_id` = npc_row.`default_dialog_id`
LEFT JOIN `god2_game`.`merchants` merchant_row
  ON merchant_row.`merchant_id` = npc_row.`merchant_id`
LEFT JOIN `god2_game`.`merchant_inventory` inventory_row
  ON inventory_row.`merchant_id` = merchant_row.`merchant_id`
LEFT JOIN `god2_game`.`npc_spawns` spawn_row
  ON spawn_row.`npc_id` = npc_row.`npc_id`
GROUP BY
    npc_row.`npc_id`,
    npc_row.`name_zh_tw`,
    npc_row.`npc_type`,
    npc_row.`interaction_family`,
    npc_row.`default_dialog_id`,
    dialog_row.`title_zh_tw`,
    dialog_row.`dialog_id`,
    npc_row.`merchant_id`,
    merchant_row.`merchant_id`,
    merchant_row.`name_zh_tw`,
    merchant_row.`merchant_type`,
    npc_row.`enabled`,
    merchant_row.`enabled`
ORDER BY
    npc_row.`enabled` DESC,
    `已啟用出生點數` DESC,
    merchant_row.`enabled` DESC,
    `已啟用商品數` DESC,
    npc_row.`npc_id`;
