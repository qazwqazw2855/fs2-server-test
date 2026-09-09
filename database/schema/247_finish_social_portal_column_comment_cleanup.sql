ALTER TABLE `god2_game`.`character_creation_profiles`
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許服務端建立角色時使用';

ALTER TABLE `god2_game`.`client_map_resources`
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允許正式服務端載入';

ALTER TABLE `god2_game`.`equipment_enhancement_materials`
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`equipment_enhancement_rates`
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`immortal_base_stats`
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`magic_skill_damage_coefficients`
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`pet_growth_grade_rules`
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`physical_skill_damage_coefficients`
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`portal_resource_links`
  MODIFY COLUMN `portal_link_id` varchar(191) NOT NULL COMMENT '傳送門資源連結 ID',
  MODIFY COLUMN `portal_id` bigint(20) NULL COMMENT '服務端傳送門 ID；未知時為 NULL',
  MODIFY COLUMN `client_build_id` varchar(64) NOT NULL COMMENT '客戶端版本 ID',
  MODIFY COLUMN `source_resource_key` varchar(512) NOT NULL COMMENT '來源地圖資源鍵',
  MODIFY COLUMN `destination_resource_key` varchar(512) NOT NULL COMMENT '目的地地圖資源鍵',
  MODIFY COLUMN `source_map_id` bigint(20) NOT NULL COMMENT '來源服務端地圖 ID',
  MODIFY COLUMN `destination_map_id` bigint(20) NULL COMMENT '目的地服務端地圖 ID；未知時為 NULL',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否啟用此傳送門資源連結';

ALTER TABLE `god2_game`.`skill_client_metadata`
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）',
  MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) ON UPDATE current_timestamp(6) COMMENT '更新時間（UTC）';

ALTER TABLE `god2_game`.`skill_client_metadata_mappings`
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL DEFAULT utc_timestamp(6) COMMENT '建立時間（UTC）';

ALTER TABLE `god2_game`.`weapons`
  MODIFY COLUMN `client_item_id` int(11) NULL COMMENT '官方客戶端物品顯示 ID',
  MODIFY COLUMN `enabled` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否載入正式服務端；0 為停用';

ALTER TABLE `god2_player`.`character_inventory`
  MODIFY COLUMN `item_instance_metadata` longtext NULL COMMENT '服務端執行期需要的物品實例資料；不存放封包原文';

ALTER TABLE `god2_player`.`equipment_instances`
  MODIFY COLUMN `item_id` bigint(20) NOT NULL COMMENT '共用物品索引 ID';

ALTER TABLE `god2_player`.`player_relationships`
  MODIFY COLUMN `character_id_low` bigint(20) NOT NULL COMMENT '關係中較小的角色 ID',
  MODIFY COLUMN `character_id_high` bigint(20) NOT NULL COMMENT '關係中較大的角色 ID',
  MODIFY COLUMN `relationship_kind` varchar(32) NOT NULL COMMENT '玩家關係類型',
  MODIFY COLUMN `status` varchar(32) NOT NULL COMMENT '玩家關係狀態',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL COMMENT '建立時間（UTC）',
  MODIFY COLUMN `ended_at_utc` datetime(6) NULL COMMENT '結束時間（UTC）；尚未結束時為 NULL',
  MODIFY COLUMN `version` bigint(20) NOT NULL DEFAULT 1 COMMENT '資料版本號';

ALTER TABLE `god2_player`.`player_social_blocks`
  MODIFY COLUMN `blocker_character_id` bigint(20) NOT NULL COMMENT '執行封鎖的角色 ID',
  MODIFY COLUMN `blocked_character_id` bigint(20) NOT NULL COMMENT '被封鎖的角色 ID',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL COMMENT '建立時間（UTC）';

ALTER TABLE `god2_player`.`player_social_invitations`
  MODIFY COLUMN `kind` varchar(32) NOT NULL COMMENT '社交邀請類型',
  MODIFY COLUMN `requester_character_id` bigint(20) NOT NULL COMMENT '發出邀請的角色 ID',
  MODIFY COLUMN `target_character_id` bigint(20) NOT NULL COMMENT '收到邀請的角色 ID',
  MODIFY COLUMN `status` varchar(32) NOT NULL COMMENT '邀請狀態',
  MODIFY COLUMN `requested_at_utc` datetime(6) NOT NULL COMMENT '邀請建立時間（UTC）',
  MODIFY COLUMN `responded_at_utc` datetime(6) NULL COMMENT '回覆時間（UTC）；尚未回覆時為 NULL',
  MODIFY COLUMN `response_actor_id` bigint(20) NULL COMMENT '回覆邀請的角色 ID；尚未回覆時為 NULL',
  MODIFY COLUMN `version` bigint(20) NOT NULL DEFAULT 1 COMMENT '資料版本號';

ALTER TABLE `god2_player`.`player_social_operations`
  MODIFY COLUMN `actor_character_id` bigint(20) NOT NULL COMMENT '執行社交操作的角色 ID',
  MODIFY COLUMN `request_id` varchar(96) NOT NULL COMMENT '社交操作請求 ID',
  MODIFY COLUMN `operation_kind` varchar(32) NOT NULL COMMENT '社交操作類型',
  MODIFY COLUMN `result_code` varchar(48) NOT NULL COMMENT '社交操作結果代碼',
  MODIFY COLUMN `related_character_id` bigint(20) NULL COMMENT '相關角色 ID；無相關角色時為 NULL',
  MODIFY COLUMN `mutated` tinyint(1) NOT NULL COMMENT '是否造成資料異動',
  MODIFY COLUMN `blocked` tinyint(1) NULL COMMENT '是否被封鎖規則擋下；不適用時為 NULL',
  MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL COMMENT '建立時間（UTC）';

ALTER TABLE `god2_player`.`world_interaction_audit`
  MODIFY COLUMN `PlayerRuntimeEntityId` bigint(20) NOT NULL COMMENT '玩家執行期實體 ID',
  MODIFY COLUMN `TargetRuntimeEntityId` bigint(20) NOT NULL COMMENT '目標執行期實體 ID',
  MODIFY COLUMN `RuntimeVersionBefore` bigint(20) NOT NULL COMMENT '操作前執行期版本',
  MODIFY COLUMN `RuntimeVersionAfter` bigint(20) NOT NULL COMMENT '操作後執行期版本';
