INSERT INTO `god2_game`.`maps`
    (`map_id`,`code`,`name_zh_tw`,`name_original`,`resource_identity`,`width`,`height`,
     `minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,`allow_teleport`,`allow_escape`,
     `allow_resurrection`,`allow_mount`,`allow_pet`,`client_build_id`,`client_map_id`,
     `client_area_id`,`coordinate_scale_x`,`coordinate_scale_y`,`coordinate_offset_x`,
     `coordinate_offset_y`,`identity_evidence_status`,`coordinate_evidence_status`,
     `enabled`,`admin_note`)
VALUES
    (130139698,'hongmeng_realm','鴻濛境','鸿蒙境','official-map/hongmeng-realm',64,64,
     0,1343,0,1343,1,1,1,1,1,'god2-opt-6b127086e0c0',0,2,1,1,0,0,
     'Verified','Derived',1,
     '2026-08-11 官方返回封包：client map 0、area 2、落點 283/453；資源格邊界採保守運作值，待地圖檔量測後收斂。'),
    (192354557,'biyou_palace_1f','碧遊宮 1F','碧游宫 1F','official-map/biyou-palace-1f',64,64,
     0,1343,0,1343,1,1,1,1,1,'god2-opt-6b127086e0c0',43,2,1,1,0,0,
     'Verified','Derived',1,
     '2026-08-11 官方進入封包：client map 43、area 2、落點 52/184；畫面名稱交叉確認為碧遊宮 1F。')
ON DUPLICATE KEY UPDATE
    `code`=VALUES(`code`),
    `name_zh_tw`=VALUES(`name_zh_tw`),
    `name_original`=VALUES(`name_original`),
    `resource_identity`=VALUES(`resource_identity`),
    `width`=VALUES(`width`),
    `height`=VALUES(`height`),
    `minimum_x`=VALUES(`minimum_x`),
    `maximum_x`=VALUES(`maximum_x`),
    `minimum_y`=VALUES(`minimum_y`),
    `maximum_y`=VALUES(`maximum_y`),
    `client_build_id`=VALUES(`client_build_id`),
    `client_map_id`=VALUES(`client_map_id`),
    `client_area_id`=VALUES(`client_area_id`),
    `coordinate_scale_x`=VALUES(`coordinate_scale_x`),
    `coordinate_scale_y`=VALUES(`coordinate_scale_y`),
    `coordinate_offset_x`=VALUES(`coordinate_offset_x`),
    `coordinate_offset_y`=VALUES(`coordinate_offset_y`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`portals`
    (`portal_id`,`name_zh_tw`,`source_map_id`,`source_x`,`source_y`,`source_radius`,
     `destination_map_id`,`destination_x`,`destination_y`,`enabled`,`admin_note`)
SELECT
    3,'鴻濛境至碧遊宮 1F',source_map.`map_id`,284,452,8,
    destination_map.`map_id`,52,184,1,
    '移動區域觸發。實測有效點 284/452；入口範圍經玩家確認不只單一座標，半徑 8 為可調推導值。'
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_map_id`=43 AND destination_map.`client_area_id`=2
WHERE source_map.`client_map_id`=0 AND source_map.`client_area_id`=2
UNION ALL
SELECT
    4,'碧遊宮 1F 至鴻濛境',source_map.`map_id`,51,185,8,
    destination_map.`map_id`,283,453,1,
    '移動區域觸發。實測有效點 51/185；返回官方封包落點為鴻濛境 283/453。'
FROM `god2_game`.`maps` source_map
JOIN `god2_game`.`maps` destination_map
  ON destination_map.`client_map_id`=0 AND destination_map.`client_area_id`=2
WHERE source_map.`client_map_id`=43 AND source_map.`client_area_id`=2
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
