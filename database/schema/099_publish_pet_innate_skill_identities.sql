-- Publish stable identities and unlock slots for the 42 expanded general-pet skill names.
-- Effect formulas remain evidence-blocked, so skill definitions stay disabled until their semantics are verified.

INSERT INTO `god2_game`.`skills`
    (`skill_id`,`code`,`name_zh_tw`,`name_original`,`description_zh_tw`,`skill_family`,`skill_category`,
     `evidence_status`,`enabled`,`admin_note`)
SELECT 3000000000 + source_row.`sequence_number`,
       CONCAT('pet_innate_',LPAD(source_row.`sequence_number`,3,'0')),
       source_row.`skill_name_zh_tw`,source_row.`skill_name_zh_tw`,
       '一般戰寵固有技能；名稱與20、40、60級解鎖位置已核對，效果公式尚待驗證。',
       'PetInnate','PetSkill','WikiVerified',0,
       '技能身分與解鎖槽位可使用；傷害、命中、狀態與消耗尚未驗證，因此正式技能執行保持停用。'
FROM (
    SELECT ROW_NUMBER() OVER (ORDER BY names_row.`skill_name_zh_tw`) AS `sequence_number`,names_row.`skill_name_zh_tw`
    FROM (
        SELECT DISTINCT source_names.`skill_name_zh_tw`
        FROM (
            SELECT `level_20_skill_name_zh_tw` AS `skill_name_zh_tw`
            FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_20_skill_name_zh_tw` IS NOT NULL
            UNION ALL
            SELECT `level_40_skill_name_zh_tw`
            FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_40_skill_name_zh_tw` IS NOT NULL
            UNION ALL
            SELECT `level_60_skill_name_zh_tw`
            FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_60_skill_name_zh_tw` IS NOT NULL
        ) source_names
    ) names_row
) source_row
WHERE NOT EXISTS (
    SELECT 1 FROM `god2_game`.`skills` existing_row
    WHERE existing_row.`name_zh_tw`=source_row.`skill_name_zh_tw`
);

INSERT IGNORE INTO `god2_game`.`pet_template_skills`
    (`pet_template_id`,`slot_index`,`skill_id`,`skill_name_cache`,`skill_level`,`enabled`,`admin_note`)
SELECT source_row.`pet_template_id`,source_row.`slot_index`,skill_row.`skill_id`,source_row.`skill_name_zh_tw`,
       source_row.`required_level`,1,
       '一般戰寵Wiki技能階級與官方客戶端完整繁中技能名的精確對應；效果公式仍由skills.enabled阻擋。'
FROM (
    SELECT `pet_template_id`,1 AS `slot_index`,`level_20_skill_name_zh_tw` AS `skill_name_zh_tw`,20 AS `required_level`
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_20_skill_name_zh_tw` IS NOT NULL
    UNION ALL
    SELECT `pet_template_id`,2,`level_40_skill_name_zh_tw`,40
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_40_skill_name_zh_tw` IS NOT NULL
    UNION ALL
    SELECT `pet_template_id`,3,`level_60_skill_name_zh_tw`,60
    FROM `god2_game`.`pet_templates` WHERE `enabled`=1 AND `level_60_skill_name_zh_tw` IS NOT NULL
) source_row
JOIN `god2_game`.`skills` skill_row ON skill_row.`name_zh_tw`=source_row.`skill_name_zh_tw`;

CREATE OR REPLACE VIEW `god2_game`.`vw_pet_innate_skills_readable` AS
SELECT template_row.`pet_template_id` AS `戰寵模板ID`,template_row.`name_zh_tw` AS `戰寵名稱`,
       mapping_row.`slot_index` AS `技能格`,mapping_row.`skill_level` AS `解鎖等級`,
       mapping_row.`skill_name_cache` AS `完整技能名稱`,
       CASE skill_row.`enabled` WHEN 1 THEN '效果已驗證可執行' ELSE '名稱與解鎖已實裝，效果公式待驗證' END AS `服務端狀態`
FROM `god2_game`.`pet_template_skills` mapping_row
JOIN `god2_game`.`pet_templates` template_row ON template_row.`pet_template_id`=mapping_row.`pet_template_id`
JOIN `god2_game`.`skills` skill_row ON skill_row.`skill_id`=mapping_row.`skill_id`
WHERE mapping_row.`enabled`=1
ORDER BY template_row.`pet_template_id`,mapping_row.`slot_index`;
