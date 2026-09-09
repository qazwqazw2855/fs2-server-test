ALTER TABLE `god2_game`.`character_creation_profiles`
  MODIFY COLUMN `client_build_id` varchar(64) NOT NULL COMMENT '適用的正式客戶端版本 ID',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`client_map_resources`
  MODIFY COLUMN `area_code` varchar(64) NOT NULL COMMENT '客戶端地圖區域代碼',
  MODIFY COLUMN `resource_name` varchar(256) NOT NULL COMMENT '客戶端地圖資源名稱',
  MODIFY COLUMN `grid_width` int(11) NULL COMMENT '客戶端地圖網格寬度；未知時為 NULL',
  MODIFY COLUMN `grid_height` int(11) NULL COMMENT '客戶端地圖網格高度；未知時為 NULL',
  MODIFY COLUMN `client_map_id` int(11) NULL COMMENT '已驗證的客戶端地圖 ID',
  MODIFY COLUMN `client_area_id` int(11) NULL COMMENT '已驗證的客戶端區域 ID',
  MODIFY COLUMN `resource_format` varchar(16) NULL COMMENT '地圖資源格式',
  MODIFY COLUMN `navigation_format` varchar(30) NULL COMMENT '導航網格格式';

ALTER TABLE `god2_game`.`npc_spawns`
  MODIFY COLUMN `observed_client_entity_handle` bigint(20) unsigned NULL COMMENT '已驗證客戶端實體控制代碼';

ALTER TABLE `god2_player`.`player_social_operations`
  MODIFY COLUMN `operation_fingerprint` varchar(200) NOT NULL COMMENT '社交操作指紋；用於冪等與重複操作辨識';
