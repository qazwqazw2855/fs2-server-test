-- Owner-facing black-box skill/status target view.
-- Public beta skill-effect records are formal runtime candidates, but many are
-- not yet enabled through the canonical skills/status_effects tables. This view
-- turns them into a focused Traditional Chinese test queue for black-box
-- verification of heal, buff, debuff, cleanse, revive, MP cost, targeting, and
-- duration behavior.

CREATE OR REPLACE VIEW `god2_game`.`vw_blackbox_skill_status_test_targets_readable` AS
SELECT
    effect_row.`global_record_id` AS `效果記錄ID`,
    effect_row.`bank` AS `來源Bank`,
    effect_row.`slot_one_based` AS `來源槽位`,
    effect_row.`name_zh_tw` AS `技能名稱`,
    effect_row.`description_zh_tw` AS `技能說明`,
    effect_row.`skill_level` AS `技能等級`,
    effect_row.`implementation_family` AS `功能族群`,
    effect_row.`effect_kind` AS `效果類型`,
    effect_row.`status_or_axis` AS `狀態或屬性軸`,
    effect_row.`mp_cost` AS `MP消耗`,
    effect_row.`target_context` AS `目標Context`,
    effect_row.`target_scope` AS `目標範圍`,
    effect_row.`numeric_effect_fields` AS `數值欄位`,
    effect_row.`minimum_working_server_rule` AS `最低可用服務端規則`,
    effect_row.`provisional_success_rule` AS `暫定成功規則`,
    effect_row.`provisional_duration_rule` AS `暫定持續規則`,
    effect_row.`enabled` AS `候選啟用`,
    skill_row.`skill_id` AS `正式技能ID`,
    skill_row.`enabled` AS `正式技能已啟用`,
    skill_row.`skill_family` AS `正式技能族群`,
    skill_row.`skill_category` AS `正式技能分類`,
    skill_row.`target_scope_zh_tw` AS `正式目標範圍`,
    skill_row.`status_effect_id` AS `正式狀態ID`,
    CASE
        WHEN effect_row.`effect_kind` = '治療' THEN '第一批：補血/治療，最容易黑箱驗證'
        WHEN effect_row.`effect_kind` = '負面' THEN '第一批：負面狀態，測命中、持續回合與限制效果'
        WHEN effect_row.`effect_kind` = '淨化' THEN '第二批：解除狀態，需先製造負面狀態'
        WHEN effect_row.`effect_kind` = '增益' AND effect_row.`implementation_family` = '屬性增益' THEN '第二批：屬性增益，測攻防速與持續回合'
        WHEN effect_row.`effect_kind` = '增益' THEN '第三批：五行/特殊增益，需搭配傷害測試'
        WHEN effect_row.`effect_kind` = '復活' THEN '第三批：復活，需死亡角色情境'
        ELSE '人工確認：未知技能效果'
    END AS `黑箱優先級`
FROM `god2_game`.`public_beta_skill_effect_v0` effect_row
LEFT JOIN `god2_game`.`skills` skill_row
  ON skill_row.`name_zh_tw` = effect_row.`name_zh_tw`
WHERE effect_row.`enabled` = 1
   OR effect_row.`effect_kind` IN ('治療','負面','淨化','復活')
ORDER BY
    CASE
        WHEN effect_row.`effect_kind` = '治療' THEN 0
        WHEN effect_row.`effect_kind` = '負面' THEN 1
        WHEN effect_row.`effect_kind` = '淨化' THEN 2
        WHEN effect_row.`effect_kind` = '增益' AND effect_row.`implementation_family` = '屬性增益' THEN 3
        WHEN effect_row.`effect_kind` = '增益' THEN 4
        WHEN effect_row.`effect_kind` = '復活' THEN 5
        ELSE 6
    END,
    effect_row.`implementation_family`,
    effect_row.`skill_level`,
    effect_row.`global_record_id`;
