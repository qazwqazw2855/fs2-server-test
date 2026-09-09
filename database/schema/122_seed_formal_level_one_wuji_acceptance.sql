-- Publish one isolated, evidence-backed Lv1 Wuji fixture for the formal external-client
-- acceptance character. Existing owned immortals on every other character are untouched.
INSERT INTO `god2_player`.`character_immortals`
    (`immortal_instance_id`,`owner_character_id`,`immortal_template_id`,`name`,
     `rank_id`,`rank_name_cache`,`level`,`experience`,`conversation_experience`,`conversation_level`,
     `current_hp`,`max_hp`,`current_mp`,`max_mp`,
     `strength_base`,`strength_bonus`,`constitution_base`,`constitution_bonus`,
     `intelligence_base`,`intelligence_bonus`,`speed_base`,`speed_bonus`,
     `metal_base`,`metal_bonus`,`wood_base`,`wood_bonus`,`water_base`,`water_bonus`,
     `fire_base`,`fire_bonus`,`earth_base`,`earth_bonus`,
     `is_active`,`enabled`,`admin_note`)
SELECT 1180000009,
       character_row.`character_id`,
       template_row.`immortal_template_id`,
       template_row.`name_zh_tw`,
       NULL,NULL,
       baseline_row.`level`,0,0,NULL,
       baseline_row.`maximum_hp`,baseline_row.`maximum_hp`,
       baseline_row.`maximum_mp`,baseline_row.`maximum_mp`,
       baseline_row.`strength`,0,baseline_row.`constitution`,0,
       baseline_row.`intelligence`,0,baseline_row.`speed`,0,
       baseline_row.`metal`,0,baseline_row.`wood`,0,baseline_row.`water`,0,
       baseline_row.`fire`,0,baseline_row.`earth`,0,
       1,1,
       'Formal external-client Lv1 immortal projection acceptance; verified migration 106 baseline and migration 118 wire identity.'
FROM `god2_player`.`characters` character_row
JOIN `god2_game`.`immortal_templates` template_row
  ON template_row.`code`='immortal_wuji'
 AND template_row.`immortal_template_id`=1060001
 AND template_row.`enabled`=1
JOIN `god2_game`.`immortal_base_stats` baseline_row
  ON baseline_row.`immortal_template_id`=template_row.`immortal_template_id`
 AND baseline_row.`level`=1
 AND baseline_row.`evidence_status`='Verified'
 AND baseline_row.`enabled`=1
WHERE character_row.`character_id`=9
  AND character_row.`account_id`=11
  AND character_row.`name`='fresh13a'
  AND character_row.`enabled`=1
  AND NOT EXISTS (
      SELECT 1
      FROM `god2_player`.`character_immortals` existing_row
      WHERE existing_row.`immortal_instance_id`=1180000009
         OR (existing_row.`owner_character_id`=character_row.`character_id`
             AND existing_row.`immortal_template_id`=template_row.`immortal_template_id`
             AND existing_row.`enabled`=1));
