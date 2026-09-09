-- Allow administrators to complete missing pet growth cells without manually maintaining totals.

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    DROP CONSTRAINT IF EXISTS `ck_pet_auto_growth_values`,
    ADD COLUMN IF NOT EXISTS `source_reference_zh_tw` varchar(500) NULL COMMENT '手動補值時填寫可讀資料來源' AFTER `source_article_sn`,
    ADD COLUMN IF NOT EXISTS `admin_note` varchar(500) NULL COMMENT '服主補值或校對備註' AFTER `source_reference_zh_tw`;

UPDATE `god2_game`.`pet_automatic_growth_allocations`
SET `source_reference_zh_tw`=CONCAT('巴哈姆特封神2精華區 SN ',`source_article_sn`)
WHERE `source_reference_zh_tw` IS NULL AND `source_article_sn` IS NOT NULL;

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    MODIFY COLUMN `source_article_sn` int NULL COMMENT '巴哈文章SN；非巴哈來源可留空',
    MODIFY COLUMN `published_total` int GENERATED ALWAYS AS (
        CASE
            WHEN `constitution_delta` IS NULL OR `strength_delta` IS NULL
              OR `intelligence_delta` IS NULL OR `speed_delta` IS NULL THEN NULL
            ELSE `constitution_delta`+`strength_delta`+`intelligence_delta`+`speed_delta`
        END
    ) STORED COMMENT '四維自動計算合計，不需手動填寫';

-- Correct the previously imported board-bull late-breakthrough source cell.
UPDATE `god2_game`.`pet_automatic_growth_allocations`
SET `constitution_delta`=5,`strength_delta`=5,`intelligence_delta`=0,`speed_delta`=0,
    `source_value_zh_tw`='5力5體',`evidence_status`='BahamutVerified',`enabled`=1
WHERE `growth_archetype_id`=18 AND `growth_grade`='LateBreakthrough' AND `minimum_level`=50;

ALTER TABLE `god2_game`.`pet_automatic_growth_allocations`
    ADD CONSTRAINT `ck_pet_auto_growth_values` CHECK (
        (`evidence_status`='SourceMissing' AND `constitution_delta` IS NULL AND `strength_delta` IS NULL
            AND `intelligence_delta` IS NULL AND `speed_delta` IS NULL AND `published_total` IS NULL AND `enabled`=0)
        OR (`evidence_status`='SourceConflict' AND `published_total` IS NOT NULL AND `enabled`=0)
        OR (`evidence_status`='BahamutVerified' AND `published_total`=`expected_total` AND `enabled`=1)
        OR (`evidence_status`='UserConfirmed' AND `published_total`=`expected_total` AND `enabled`=1
            AND `source_reference_zh_tw` IS NOT NULL)
    );

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_category_growth_values_readable` AS
SELECT category_row.`category_id` AS `類別ID`,category_row.`name_zh_tw` AS `戰寵族群`,
       archetype_row.`name_zh_tw` AS `成長原型`,
       CASE allocation.`growth_grade` WHEN 'Normal' THEN '普通' WHEN 'Top' THEN '頂級'
            WHEN 'Breakthrough' THEN '破頂' ELSE '晚破' END AS `戰寵品級`,
       allocation.`minimum_level` AS `起始等級`,allocation.`maximum_level` AS `結束等級`,
       allocation.`constitution_delta` AS `每級自動體力`,allocation.`strength_delta` AS `每級自動力量`,
       allocation.`intelligence_delta` AS `每級自動智力`,allocation.`speed_delta` AS `每級自動速度`,
       allocation.`published_total` AS `四維自動合計`,allocation.`expected_total` AS `應有合計`,
       allocation.`source_value_zh_tw` AS `來源原文配點`,
       CASE allocation.`evidence_status` WHEN 'BahamutVerified' THEN '巴哈資料已核對可實裝'
            WHEN 'UserConfirmed' THEN '服主手動確認可實裝'
            WHEN 'SourceConflict' THEN '原文總點數衝突，禁止使用' ELSE '原文缺值，等待手動補齊' END AS `資料狀態`,
       allocation.`source_reference_zh_tw` AS `資料來源`,allocation.`admin_note` AS `補值備註`
FROM `god2_game`.`pet_categories` category_row
JOIN `god2_game`.`pet_growth_archetypes` archetype_row
  ON archetype_row.`growth_archetype_id`=category_row.`growth_archetype_id`
JOIN `god2_game`.`pet_automatic_growth_allocations` allocation
  ON allocation.`growth_archetype_id`=archetype_row.`growth_archetype_id`
ORDER BY category_row.`category_id`,FIELD(allocation.`growth_grade`,'Normal','Top','Breakthrough','LateBreakthrough'),allocation.`minimum_level`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_growth_manual_fill_queue` AS
SELECT allocation.`growth_archetype_id` AS `成長原型ID`,archetype_row.`name_zh_tw` AS `成長原型`,
       CASE allocation.`growth_grade` WHEN 'Normal' THEN '普通' WHEN 'Top' THEN '頂級'
            WHEN 'Breakthrough' THEN '破頂' ELSE '晚破' END AS `戰寵品級`,
       allocation.`minimum_level` AS `起始等級`,allocation.`maximum_level` AS `結束等級`,
       allocation.`constitution_delta` AS `待填體力`,allocation.`strength_delta` AS `待填力量`,
       allocation.`intelligence_delta` AS `待填智力`,allocation.`speed_delta` AS `待填速度`,
       allocation.`expected_total` AS `必須符合的合計`,allocation.`source_value_zh_tw` AS `現有來源原文`,
       CASE allocation.`evidence_status` WHEN 'SourceConflict' THEN '原文衝突' ELSE '原文缺值' END AS `待處理原因`,
       allocation.`source_reference_zh_tw` AS `資料來源`,allocation.`admin_note` AS `補值備註`
FROM `god2_game`.`pet_automatic_growth_allocations` allocation
JOIN `god2_game`.`pet_growth_archetypes` archetype_row
  ON archetype_row.`growth_archetype_id`=allocation.`growth_archetype_id`
WHERE allocation.`evidence_status` IN ('SourceMissing','SourceConflict')
ORDER BY allocation.`growth_archetype_id`,FIELD(allocation.`growth_grade`,'Normal','Top','Breakthrough','LateBreakthrough'),allocation.`minimum_level`;
