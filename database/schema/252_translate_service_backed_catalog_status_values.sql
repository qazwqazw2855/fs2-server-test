-- 252_translate_service_backed_catalog_status_values.sql
-- 將正式資料表中仍會被管理者直接看見的服務端狀態值改為繁中；服務端讀取端負責正規化回 runtime 語意。

UPDATE `god2_game`.`quest_objectives`
SET `objective_type` = CASE `objective_type`
    WHEN 'ItemCandidate' THEN '候選物品目標'
    WHEN 'Unknown' THEN '未確認'
    WHEN 'Candidate' THEN '候選目標'
    ELSE `objective_type`
END
WHERE `objective_type` IN ('ItemCandidate','Unknown','Candidate');

ALTER TABLE `god2_game`.`monsters`
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_eligibility`,
    DROP CONSTRAINT IF EXISTS `ck_monsters_capture_blocked_types`;

UPDATE `god2_game`.`monsters`
SET `capture_eligibility` = CASE `capture_eligibility`
    WHEN 'Unknown' THEN '未確認'
    WHEN 'Capturable' THEN '可捕捉'
    WHEN 'Blocked' THEN '不可捕捉'
    ELSE `capture_eligibility`
END
WHERE `capture_eligibility` IN ('Unknown','Capturable','Blocked');

ALTER TABLE `god2_game`.`monsters`
    MODIFY COLUMN `capture_eligibility` varchar(20) NOT NULL DEFAULT '未確認' COMMENT '寵物捕捉資格繁中狀態',
    ADD CONSTRAINT `ck_monsters_capture_eligibility` CHECK (`capture_eligibility` IN ('未確認','可捕捉','不可捕捉') AND (
        (`capture_eligibility`='可捕捉' AND `name_color_zh_tw`='白色' AND `is_quest_monster`=0 AND `is_formation_boss`=0)
        OR (`capture_eligibility`='不可捕捉' AND `capture_block_reason_zh_tw` IS NOT NULL)
        OR (`capture_eligibility`='未確認')
    ));
