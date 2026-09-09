-- Schema 374
-- Game function: monster AI baseline.
-- Purpose: let enabled monsters participate in server-designed combat behavior without pretending skill evidence exists.

INSERT INTO `god2_game`.`monster_ai_profiles`
    (`ai_profile_id`, `name_zh_tw`, `description_zh_tw`, `enabled`, `admin_note`)
VALUES
    (1, '普通攻擊型', '服務端設計的基礎怪物 AI：沒有官方技能證據時使用普通攻擊，不寫入怪物技能證據。', 1,
     'ServiceDesigned; black-box baseline; do not mark as official evidence.')
ON DUPLICATE KEY UPDATE
    `name_zh_tw` = VALUES(`name_zh_tw`),
    `description_zh_tw` = VALUES(`description_zh_tw`),
    `enabled` = VALUES(`enabled`),
    `admin_note` = VALUES(`admin_note`);

INSERT INTO `god2_game`.`monster_ai_rules`
    (`ai_rule_id`, `ai_profile_id`, `priority`, `minimum_round`, `maximum_round`,
     `self_hp_percent_min`, `self_hp_percent_max`, `ally_hp_percent_max`, `enemy_hp_percent_max`,
     `required_status_effect_id`, `forbidden_status_effect_id`, `skill_id`, `target_selector`,
     `use_probability`, `cooldown_rounds`, `maximum_uses`, `fallback_action`, `enabled`, `admin_note`)
VALUES
    (1, 1, 100, 1, NULL,
     NULL, NULL, NULL, NULL,
     NULL, NULL, NULL, 'CurrentTargetOrNearestEnemy',
     1.0000, 0, NULL, 'BasicAttack', 1,
     'ServiceDesigned fallback rule; monster_skills stays evidence-driven.')
ON DUPLICATE KEY UPDATE
    `priority` = VALUES(`priority`),
    `minimum_round` = VALUES(`minimum_round`),
    `maximum_round` = VALUES(`maximum_round`),
    `target_selector` = VALUES(`target_selector`),
    `use_probability` = VALUES(`use_probability`),
    `cooldown_rounds` = VALUES(`cooldown_rounds`),
    `fallback_action` = VALUES(`fallback_action`),
    `enabled` = VALUES(`enabled`),
    `admin_note` = VALUES(`admin_note`);

UPDATE `god2_game`.`monsters`
SET `ai_profile_id` = 1,
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `enabled` = 1
  AND `ai_profile_id` IS NULL;
