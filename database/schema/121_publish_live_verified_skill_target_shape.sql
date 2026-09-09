-- 將正式實機戰鬥已逐欄確認的「單體」作用形狀，從原文顯示欄位分離成
-- 可供 Runtime 使用的正規化中繼資料。這不推定敵我陣營、技能命令參數或傷害效果。

ALTER TABLE `god2_game`.`skill_client_metadata`
    ADD COLUMN IF NOT EXISTS `target_shape` varchar(32) NOT NULL DEFAULT 'Unknown'
        COMMENT '正規化作用形狀；Unknown/SingleTarget' AFTER `target_scope_zh_tw`,
    ADD COLUMN IF NOT EXISTS `target_shape_evidence_status` varchar(40) NOT NULL DEFAULT 'EvidenceBlocked'
        COMMENT '作用形狀的逐欄證據狀態' AFTER `target_shape`;

ALTER TABLE `god2_game`.`skill_client_metadata`
    DROP CONSTRAINT IF EXISTS `ck_skill_client_metadata_target_shape`,
    ADD CONSTRAINT `ck_skill_client_metadata_target_shape`
        CHECK (`target_shape` IN ('Unknown','SingleTarget'));

UPDATE `god2_game`.`skill_client_metadata` metadata
JOIN `god2`.`verified_skill_observations` observation
  ON observation.`OfficialClientItemId`=metadata.`official_client_item_id`
 AND observation.`OfficialDisplayId`=metadata.`official_display_id`
 AND observation.`SkillNameZhTw`=metadata.`name_zh_tw`
SET metadata.`target_shape`='SingleTarget',
    metadata.`target_shape_evidence_status`='OfficialClientLiveVerified',
    metadata.`live_verified`=1,
    metadata.`observed_at_utc`=observation.`ObservedAtUtc`
WHERE observation.`TargetScopeZhTw`='單體'
  AND observation.`EvidenceStatusZhTw`='實機畫面與官方資料唯一匹配';

CREATE OR REPLACE VIEW `god2_game`.`vw_verified_skill_target_shapes` AS
SELECT metadata.`official_client_item_id` AS `技能書物品編號`,
       metadata.`official_display_id` AS `官方顯示編號`,
       metadata.`name_zh_tw` AS `技能名稱`,
       metadata.`target_scope_zh_tw` AS `官方原文作用範圍`,
       observation.`TargetScopeZhTw` AS `實機觀察作用範圍`,
       metadata.`target_shape` AS `正規化作用形狀`,
       metadata.`target_shape_evidence_status` AS `作用形狀證據`,
       metadata.`observed_at_utc` AS `實機觀察時間`
FROM `god2_game`.`skill_client_metadata` metadata
JOIN `god2`.`verified_skill_observations` observation
  ON observation.`OfficialClientItemId`=metadata.`official_client_item_id`
 AND observation.`OfficialDisplayId`=metadata.`official_display_id`
 AND observation.`SkillNameZhTw`=metadata.`name_zh_tw`
WHERE metadata.`target_shape_evidence_status`='OfficialClientLiveVerified';

GRANT SELECT ON `god2_game`.`vw_verified_skill_target_shapes` TO `god2_runtime_role`;
