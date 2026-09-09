UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=95,
    `admin_note`=CONCAT_WS(' | ',NULLIF(`admin_note`,''),
        'Official Client identity: FightEny.csvZ identifies immortal_wuji as ID 95; S2C 0x30 resolves IDs 1..99 through the static name table.'),
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060001
  AND `code`='immortal_wuji';
