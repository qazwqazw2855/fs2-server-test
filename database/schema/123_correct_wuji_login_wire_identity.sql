-- Correct the domain mismatch introduced by migration 118. FightEny.csvZ ID 95 is
-- not the owned-immortal login wire identity. In the exact frozen official 0x2B
-- bootstrap, application record 5 begins `31 11 02 ...`; the recovered 0x31
-- consumer reads payload byte 0 as identity and payload byte 1 as the displayed
-- level. That same record renders the known Wuji level-2 baseline (wood 22 and
-- primary attributes 27/41/13/6), proving Wuji login identity 0x11 (17).
UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=17,
    `admin_note`=CONCAT_WS(' | ',NULLIF(`admin_note`,''),
        'Corrected official login wire identity: exact frozen 0x2B application record 5 is 0x31/identity 0x11; FightEny ID 95 is a different resource domain.'),
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060001
  AND `code`='immortal_wuji'
  AND `enabled`=1;
