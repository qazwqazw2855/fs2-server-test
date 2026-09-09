INSERT INTO `god2_game`.`portals`
    (`portal_id`, `name_zh_tw`, `source_map_id`, `source_x`, `source_y`, `source_radius`,
     `destination_map_id`, `destination_x`, `destination_y`, `destination_direction`,
     `required_level`, `required_quest_id`, `cost_item_id`, `cost_quantity`, `enabled`, `admin_note`)
SELECT
    1,
    '崑崙仙界三至雜貨店',
    source_map.`map_id`, 252, 397, 0,
    destination_map.`map_id`, 28, 34, NULL,
    NULL, NULL, NULL, NULL, 1,
    '2026-08-11 官方實測：切圖前最後移動座標 252,397；0xBB 狀態 0x88；目的 map 19 落點 28,34。'
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_map_id`=19 AND destination_map.`client_area_id`=4
WHERE source_map.`client_map_id`=3 AND source_map.`client_area_id`=4
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `source_map_id`=VALUES(`source_map_id`),
    `source_x`=VALUES(`source_x`),
    `source_y`=VALUES(`source_y`),
    `source_radius`=VALUES(`source_radius`),
    `destination_map_id`=VALUES(`destination_map_id`),
    `destination_x`=VALUES(`destination_x`),
    `destination_y`=VALUES(`destination_y`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`portals`
    (`portal_id`, `name_zh_tw`, `source_map_id`, `source_x`, `source_y`, `source_radius`,
     `destination_map_id`, `destination_x`, `destination_y`, `destination_direction`,
     `required_level`, `required_quest_id`, `cost_item_id`, `cost_quantity`, `enabled`, `admin_note`)
SELECT
    2,
    '雜貨店至崑崙仙界三',
    source_map.`map_id`, 28, 34, 0,
    destination_map.`map_id`, 196, 139, NULL,
    NULL, NULL, NULL, NULL, 1,
    '既有六次官方往返證據：map 19 入口 28,34；目的 map 3 落點 196,139。'
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_map_id`=3 AND destination_map.`client_area_id`=4
WHERE source_map.`client_map_id`=19 AND source_map.`client_area_id`=4
ON DUPLICATE KEY UPDATE
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `source_map_id`=VALUES(`source_map_id`),
    `source_x`=VALUES(`source_x`),
    `source_y`=VALUES(`source_y`),
    `source_radius`=VALUES(`source_radius`),
    `destination_map_id`=VALUES(`destination_map_id`),
    `destination_x`=VALUES(`destination_x`),
    `destination_y`=VALUES(`destination_y`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);
