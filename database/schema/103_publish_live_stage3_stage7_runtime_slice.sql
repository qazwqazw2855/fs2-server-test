SET @god2_sync_mode = 1;

INSERT INTO `god2_game`.`maps`
    (`map_id`,`code`,`name_zh_tw`,`name_original`,`resource_identity`,`width`,`height`,
     `minimum_x`,`maximum_x`,`minimum_y`,`maximum_y`,`allow_teleport`,`allow_escape`,
     `allow_resurrection`,`allow_mount`,`allow_pet`,`client_build_id`,`client_map_id`,
     `client_area_id`,`coordinate_scale_x`,`coordinate_scale_y`,`coordinate_offset_x`,
     `coordinate_offset_y`,`identity_evidence_status`,`coordinate_evidence_status`,
     `enabled`,`admin_note`)
VALUES
    (170015000,'observed_map_0_area_15','Observed Map 0 Area 15',NULL,
     'official-client/map/0/area/15',64,64,0,1343,0,1343,1,1,1,1,1,
     'god2-opt-6b127086e0c0',0,15,1,1,0,0,'Verified','Verified',1,
     'Stage 3 live route source; client coordinate 249,246; official name and resource archive remain unknown.'),
    (170015007,'observed_map_7_area_15','Observed Map 7 Area 15',NULL,
     'official-client/map/7/area/15',64,64,0,1343,0,1343,1,1,1,1,1,
     'god2-opt-6b127086e0c0',7,15,1,1,0,0,'Verified','Verified',1,
     'Stage 3 live transition target 48,81 and Stage 4 merchant scene; official name and resource archive remain unknown.')
ON DUPLICATE KEY UPDATE
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
     `destination_map_id`,`destination_x`,`destination_y`,`destination_direction`,
     `required_level`,`required_quest_id`,`cost_item_id`,`cost_quantity`,`enabled`,`admin_note`)
VALUES
    (170015007,'Observed Stage 3 Route',170015000,249,246,0,
     170015007,48,81,NULL,NULL,NULL,NULL,NULL,1,
     'Exact transition application message: 0C0061CF010F0FC000A20051.')
ON DUPLICATE KEY UPDATE
    `source_map_id`=VALUES(`source_map_id`),
    `source_x`=VALUES(`source_x`),
    `source_y`=VALUES(`source_y`),
    `source_radius`=VALUES(`source_radius`),
    `destination_map_id`=VALUES(`destination_map_id`),
    `destination_x`=VALUES(`destination_x`),
    `destination_y`=VALUES(`destination_y`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`npcs`
    (`npc_id`,`code`,`name_zh_tw`,`name_original`,`npc_type`,`resource_id`,`resource_key`,
     `interaction_family`,`default_dialog_id`,`merchant_id`,`quest_provider`,
     `evidence_status`,`enabled`,`admin_note`)
VALUES
    (170015081,'observed_merchant_handle_3954','Observed Merchant 3954',NULL,'Merchant',NULL,
     'official-resource/type-0/ordinal-81','Merchant',NULL,NULL,0,'Verified',1,
     'Stage 4 live merchant identity; official display name remains unknown.'),
    (170015087,'observed_dialog_handle_3793','Observed Dialog NPC 3793',NULL,'Dialog',NULL,
     'official-resource/type-0/ordinal-87','Dialog',NULL,NULL,0,'Verified',1,
     'Live dialog-open identity from attempt 759; official display name and option semantics remain unknown.')
ON DUPLICATE KEY UPDATE
    `npc_type`=VALUES(`npc_type`),
    `resource_key`=VALUES(`resource_key`),
    `interaction_family`=VALUES(`interaction_family`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

INSERT INTO `god2_game`.`merchants`
    (`merchant_id`,`npc_id`,`name_zh_tw`,`merchant_type`,`buyback_enabled`,`enabled`,`admin_note`)
VALUES
    (170015954,170015081,'Observed Merchant 3954','General',1,1,
     'Stage 4-6 live open, buy and sell transaction path.')
ON DUPLICATE KEY UPDATE
    `npc_id`=VALUES(`npc_id`),
    `merchant_type`=VALUES(`merchant_type`),
    `buyback_enabled`=VALUES(`buyback_enabled`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

UPDATE `god2_game`.`npcs`
SET `merchant_id`=170015954
WHERE `npc_id`=170015081;

INSERT INTO `god2_game`.`npc_spawns`
    (`spawn_id`,`npc_id`,`npc_name_cache`,`map_id`,`map_name_cache`,`position_x`,`position_y`,
     `direction`,`instance_key`,`client_build_id`,`observed_client_entity_handle`,
     `official_resource_type`,`official_resource_ordinal`,`official_selector_high_bits`,
     `official_direction_code`,`official_state_code`,`wire_evidence_status`,
     `identity_evidence_status`,`coordinate_evidence_status`,`service_evidence_status`,
     `application_message_sha256`,`opaque_template_sha256`,`enabled`,`admin_note`)
VALUES
    (170153954,170015081,'Observed Merchant 3954',170015007,'Observed Map 7 Area 15',240,54,
     4,NULL,'god2-opt-6b127086e0c0',3954,0,81,3,4,1,'Verified',
     'Verified','Verified','Verified',
     'F53E8D79A02FB96A528E9ABF334E5D1383018780BA79D62340549FC5AD36885B',
     'C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838',1,
     'Exact Stage 4 spawn: 180072720F00005260000081000000CF010000C0036C00A1.'),
    (170153793,170015087,'Observed Dialog NPC 3793',170015007,'Observed Map 7 Area 15',65,61,
     5,NULL,'god2-opt-6b127086e0c0',3793,0,87,3,5,1,'Verified',
     'Verified','Verified','OpenOnly',
     'FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF',
     'C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838',1,
     'Nested spawn body from attempt 759 reconstructs 180072D10E000058600000A1000000CF01000004017A0075; only dialog open is verified.')
ON DUPLICATE KEY UPDATE
    `npc_id`=VALUES(`npc_id`),
    `map_id`=VALUES(`map_id`),
    `position_x`=VALUES(`position_x`),
    `position_y`=VALUES(`position_y`),
    `direction`=VALUES(`direction`),
    `official_resource_type`=VALUES(`official_resource_type`),
    `official_resource_ordinal`=VALUES(`official_resource_ordinal`),
    `official_selector_high_bits`=VALUES(`official_selector_high_bits`),
    `official_direction_code`=VALUES(`official_direction_code`),
    `official_state_code`=VALUES(`official_state_code`),
    `wire_evidence_status`=VALUES(`wire_evidence_status`),
    `identity_evidence_status`=VALUES(`identity_evidence_status`),
    `coordinate_evidence_status`=VALUES(`coordinate_evidence_status`),
    `service_evidence_status`=VALUES(`service_evidence_status`),
    `application_message_sha256`=VALUES(`application_message_sha256`),
    `opaque_template_sha256`=VALUES(`opaque_template_sha256`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

UPDATE `god2_game`.`items`
SET `buy_price`=40,
    `sell_price`=4,
    `admin_note`=CONCAT_WS(' ',NULLIF(`admin_note`,''),
        'Stage 5/6 merchant prices: client static purchase 40; live-observed recycle 4.')
WHERE `item_id`=253231541 AND `client_item_id`=6901;

INSERT INTO `god2_game`.`merchant_inventory`
    (`merchant_inventory_id`,`merchant_id`,`merchant_name_cache`,`item_id`,`item_name_cache`,
     `display_order`,`selling_price`,`purchasing_price`,`pack_count`,`quantity_limit`,
     `evidence_status`,`enabled`,`admin_note`)
SELECT
    170156901,170015954,'Observed Merchant 3954',item_row.`item_id`,item_row.`name_zh_tw`,
    1,40,4,1,NULL,'Verified',1,
    'Client item 6901 maps to canonical 253231541; Stage 5 buy and Stage 6 recycle paths observed.'
FROM `god2_game`.`items` item_row
WHERE item_row.`item_id`=253231541 AND item_row.`client_item_id`=6901
ON DUPLICATE KEY UPDATE
    `merchant_name_cache`=VALUES(`merchant_name_cache`),
    `item_name_cache`=VALUES(`item_name_cache`),
    `display_order`=VALUES(`display_order`),
    `selling_price`=VALUES(`selling_price`),
    `purchasing_price`=VALUES(`purchasing_price`),
    `pack_count`=VALUES(`pack_count`),
    `evidence_status`=VALUES(`evidence_status`),
    `enabled`=VALUES(`enabled`),
    `admin_note`=VALUES(`admin_note`);

SET @god2_sync_mode = NULL;
