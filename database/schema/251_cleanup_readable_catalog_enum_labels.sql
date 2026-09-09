-- 251_cleanup_readable_catalog_enum_labels.sql
-- 清理正式遊戲目錄中會直接給管理者閱讀的英文候選值，同時保留任務與寵物捕捉等服務端枚舉欄位的相容性。

UPDATE `god2_game`.`maps`
SET `name_zh_tw` = TRIM(REPLACE(`name_zh_tw`, ' candidate', ''))
WHERE `name_zh_tw` LIKE '% candidate';

UPDATE `god2_game`.`npcs`
SET `npc_type` = CASE `npc_type`
    WHEN 'Unknown' THEN '未分類'
    WHEN 'WorldNpc' THEN '場景NPC'
    WHEN 'Service' THEN '服務NPC'
    WHEN 'Dialog' THEN '對話NPC'
    WHEN 'Merchant' THEN '商店NPC'
    ELSE `npc_type`
END
WHERE `npc_type` IN ('Unknown','WorldNpc','Service','Dialog','Merchant');

ALTER TABLE `god2_game`.`skill_client_metadata`
    DROP CONSTRAINT IF EXISTS `ck_skill_client_metadata_target_shape`;

UPDATE `god2_game`.`skill_client_metadata`
SET `target_shape` = CASE `target_shape`
    WHEN 'Unknown' THEN '未確認'
    WHEN 'SingleTarget' THEN '單一目標'
    ELSE `target_shape`
END
WHERE `target_shape` IN ('Unknown','SingleTarget');

ALTER TABLE `god2_game`.`npcs`
    MODIFY COLUMN `npc_type` varchar(50) NOT NULL COMMENT 'NPC 類型繁中分類，供管理與目錄閱讀使用',
    MODIFY COLUMN `interaction_family` varchar(64) NULL COMMENT '互動服務家族；服務端路由枚舉，依服務相容性保留';

ALTER TABLE `god2_game`.`skill_client_metadata`
    MODIFY COLUMN `target_shape` varchar(32) NOT NULL DEFAULT '未確認' COMMENT '技能前端目標形狀繁中標籤';

ALTER TABLE `god2_game`.`skill_client_metadata`
    ADD CONSTRAINT `ck_skill_client_metadata_target_shape` CHECK (`target_shape` IN ('未確認','單一目標'));

ALTER TABLE `god2_game`.`quest_objectives`
    MODIFY COLUMN `objective_type` varchar(50) NOT NULL COMMENT '任務目標服務端枚舉；依任務解析相容性保留';

ALTER TABLE `god2_game`.`monsters`
    MODIFY COLUMN `capture_eligibility` varchar(20) NOT NULL DEFAULT 'Unknown' COMMENT '寵物捕捉資格服務端枚舉；依捕捉規則相容性保留';
