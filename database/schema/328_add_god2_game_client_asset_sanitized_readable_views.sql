DROP VIEW IF EXISTS `god2_game`.`vw_item_asset_status_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_skill_client_metadata_status_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_world_client_resource_status_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_npc_appearance_status_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_client_asset_observations_readable`;

CREATE VIEW `god2_game`.`vw_item_asset_status_readable` AS
SELECT
    asset.`item_id` AS `物品ID`,
    COALESCE(item_catalog.`name_zh_tw`, CONCAT('物品#', asset.`item_id`)) AS `物品名稱`,
    asset.`client_item_id` AS `客戶端物品ID`,
    asset.`catalog_type` AS `目錄類型`,
    CASE WHEN asset.`icon_validation_status` = '已驗證' THEN '圖示已驗證' ELSE '圖示待確認' END AS `圖示狀態`,
    CASE WHEN asset.`model_validation_status` = '參照已驗證' OR asset.`model_validation_status` = '已驗證' THEN '外觀模型已驗證' ELSE '外觀模型待確認' END AS `模型狀態`,
    CASE WHEN asset.`overall_validation_status` = '已驗證' THEN '素材整體已驗證' ELSE '素材整體待確認' END AS `素材狀態`,
    asset.`validation_note_zh_tw` AS `驗證說明`,
    CASE
        WHEN asset.`catalog_type` LIKE '%裝備%' THEN '裝備圖示、穿戴外觀、背包顯示與角色紙娃娃'
        ELSE '物品圖示、背包顯示、拖曳、交易與道具使用提示'
    END AS `功能對照`,
    asset.`validated_at_utc` AS `驗證時間UTC`
FROM `god2_game`.`item_asset_mappings` asset
LEFT JOIN `god2_game`.`item_registry` item_catalog
    ON item_catalog.`item_id` = asset.`item_id`;

CREATE VIEW `god2_game`.`vw_skill_client_metadata_status_readable` AS
SELECT
    metadata.`official_client_item_id` AS `客戶端技能書物品ID`,
    metadata.`name_zh_tw` AS `技能名稱`,
    metadata.`description_zh_tw` AS `技能說明`,
    COALESCE(metadata.`skill_mode_zh_tw`, '未分類') AS `技能模式`,
    COALESCE(metadata.`skill_category_zh_tw`, '未分類') AS `技能分類`,
    metadata.`skill_tier` AS `技能階級`,
    metadata.`mp_cost` AS `MP消耗`,
    metadata.`attack_range` AS `攻擊距離`,
    COALESCE(metadata.`target_scope_zh_tw`, '目標未證實') AS `目標範圍`,
    CASE WHEN metadata.`target_shape` = '未確認' OR metadata.`target_shape` = 'Unknown' THEN '目標形狀待黑箱確認' ELSE metadata.`target_shape` END AS `目標形狀狀態`,
    COALESCE(metadata.`official_effect_text_zh_tw`, '效果待黑箱確認') AS `官方效果文字`,
    CASE WHEN metadata.`live_verified` = 1 THEN '實機已驗證' ELSE '實機待驗證' END AS `實機驗證狀態`,
    '技能書學習、技能施放、目標選擇、MP消耗、攻擊距離與效果文字' AS `功能對照`,
    metadata.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`skill_client_metadata` metadata;

CREATE VIEW `god2_game`.`vw_world_client_resource_status_readable` AS
SELECT
    resource.`canonical_map_id` AS `地圖ID`,
    COALESCE(map_catalog.`name_zh_tw`, resource.`area_code`) AS `地圖名稱`,
    resource.`area_code` AS `區域代碼`,
    resource.`grid_width` AS `格寬`,
    resource.`grid_height` AS `格高`,
    resource.`minimum_x` AS `最小X`,
    resource.`maximum_x` AS `最大X`,
    resource.`minimum_y` AS `最小Y`,
    resource.`maximum_y` AS `最大Y`,
    CASE WHEN resource.`enabled` = 1 THEN '地圖資源啟用' ELSE '地圖資源未啟用' END AS `資源狀態`,
    CASE WHEN identity_row.`map_identity_id` IS NULL THEN '客戶端地圖身分待對應' ELSE '客戶端地圖身分已對應' END AS `客戶端身分狀態`,
    '地圖載入、角色座標、碰撞範圍、傳送門目的地與世界顯示' AS `功能對照`,
    resource.`updated_at_utc` AS `更新時間UTC`
FROM `god2_game`.`client_map_resources` resource
LEFT JOIN `god2_game`.`maps` map_catalog
    ON map_catalog.`map_id` = resource.`canonical_map_id`
LEFT JOIN `god2_game`.`client_map_resource_identities` identity_row
    ON identity_row.`resource_key` = resource.`resource_key`;

CREATE VIEW `god2_game`.`vw_npc_appearance_status_readable` AS
SELECT
    appearance.`appearance_identity_id` AS `外觀對照ID`,
    appearance.`name_zh_tw` AS `NPC顯示名稱`,
    CASE WHEN appearance.`enabled` = 1 THEN '外觀對照啟用' ELSE '外觀對照未啟用' END AS `外觀狀態`,
    appearance.`runtime_family_status` AS `服務端家族狀態`,
    CASE
        WHEN appearance.`runtime_family_status` = '證據不足' THEN '外觀家族待實機確認'
        ELSE '外觀家族已有服務端判定'
    END AS `證據狀態`,
    'NPC外觀顯示、點擊命中、任務NPC辨識、商人NPC辨識與地圖出生顯示' AS `功能對照`
FROM `god2_game`.`npc_appearance_identities` appearance;

CREATE VIEW `god2_game`.`vw_blackbox_client_asset_observations_readable` AS
SELECT
    '物品素材' AS `觀察類型`,
    item_asset.`物品ID` AS `主要ID`,
    item_asset.`物品名稱` AS `名稱`,
    item_asset.`目錄類型` AS `分類`,
    item_asset.`功能對照`,
    CASE
        WHEN item_asset.`素材狀態` = '素材整體已驗證' THEN '第三優先：抽樣確認背包圖示與外觀'
        ELSE '第一優先：確認背包圖示、穿戴外觀或道具顯示'
    END AS `黑箱測試優先級`,
    CONCAT('客戶端物品ID=', item_asset.`客戶端物品ID`, '；', item_asset.`圖示狀態`, '；', item_asset.`模型狀態`, '；', item_asset.`素材狀態`) AS `測試重點`
FROM `god2_game`.`vw_item_asset_status_readable` item_asset

UNION ALL

SELECT
    '技能客戶端資料' AS `觀察類型`,
    skill_metadata.`客戶端技能書物品ID` AS `主要ID`,
    skill_metadata.`技能名稱` AS `名稱`,
    skill_metadata.`技能分類` AS `分類`,
    skill_metadata.`功能對照`,
    CASE
        WHEN skill_metadata.`實機驗證狀態` = '實機已驗證' THEN '第二優先：抽樣確認技能學習與施放'
        ELSE '第一優先：測技能目標、MP消耗、距離與效果'
    END AS `黑箱測試優先級`,
    CONCAT('模式=', skill_metadata.`技能模式`, '；階級=', COALESCE(skill_metadata.`技能階級`, 0), '；MP=', COALESCE(skill_metadata.`MP消耗`, 0), '；', skill_metadata.`目標形狀狀態`, '；', skill_metadata.`實機驗證狀態`) AS `測試重點`
FROM `god2_game`.`vw_skill_client_metadata_status_readable` skill_metadata

UNION ALL

SELECT
    '地圖資源' AS `觀察類型`,
    world_resource.`地圖ID` AS `主要ID`,
    world_resource.`地圖名稱` AS `名稱`,
    world_resource.`區域代碼` AS `分類`,
    world_resource.`功能對照`,
    CASE
        WHEN world_resource.`客戶端身分狀態` = '客戶端地圖身分已對應' THEN '第二優先：測地圖載入與傳送門'
        ELSE '第一優先：測地圖資源對應與座標範圍'
    END AS `黑箱測試優先級`,
    CONCAT('範圍X=', COALESCE(world_resource.`最小X`, 0), '-', COALESCE(world_resource.`最大X`, 0), '；範圍Y=', COALESCE(world_resource.`最小Y`, 0), '-', COALESCE(world_resource.`最大Y`, 0), '；', world_resource.`資源狀態`, '；', world_resource.`客戶端身分狀態`) AS `測試重點`
FROM `god2_game`.`vw_world_client_resource_status_readable` world_resource

UNION ALL

SELECT
    'NPC外觀' AS `觀察類型`,
    npc_appearance.`外觀對照ID` AS `主要ID`,
    npc_appearance.`NPC顯示名稱` AS `名稱`,
    npc_appearance.`服務端家族狀態` AS `分類`,
    npc_appearance.`功能對照`,
    CASE
        WHEN npc_appearance.`證據狀態` = '外觀家族待實機確認' THEN '第一優先：測NPC外觀、點擊與互動辨識'
        ELSE '第二優先：抽樣確認NPC顯示'
    END AS `黑箱測試優先級`,
    CONCAT(npc_appearance.`外觀狀態`, '；', npc_appearance.`證據狀態`) AS `測試重點`
FROM `god2_game`.`vw_npc_appearance_status_readable` npc_appearance;
