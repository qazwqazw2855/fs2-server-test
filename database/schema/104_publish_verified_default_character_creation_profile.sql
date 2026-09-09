INSERT INTO `god2_game`.`character_creation_profiles`
    (`client_build_id`, `class_code`, `gender_code`, `life_skill_code`,
     `map_id`, `position_x`, `position_y`, `evidence_status`,
     `evidence_reference`, `enabled`, `admin_note`)
SELECT
    'god2-opt-6b127086e0c0', 'Swordsman', 'Female', 'LifeSkill1',
    map_row.`map_id`, 28, 34, 'Recovered',
    'Artifacts/ClientInstrumentation/ElevatedAutomationHost/host-run-20260812-124729/attempt-317-trace',
    1,
    'Current-build 48-byte CharacterCreate request and verified map 19 spawn baseline.'
FROM `god2_game`.`maps` map_row
WHERE map_row.`client_build_id` = 'god2-opt-6b127086e0c0'
  AND map_row.`client_map_id` = 19
  AND map_row.`client_area_id` = 4
  AND map_row.`enabled` = 1
ON DUPLICATE KEY UPDATE
    `map_id` = VALUES(`map_id`),
    `position_x` = VALUES(`position_x`),
    `position_y` = VALUES(`position_y`),
    `evidence_status` = VALUES(`evidence_status`),
    `evidence_reference` = VALUES(`evidence_reference`),
    `enabled` = VALUES(`enabled`),
    `admin_note` = VALUES(`admin_note`);
