-- Publish only the exact static login-wire identities recovered from the
-- official client table Data2/Patch/GodEft.csvZ.
-- Source SHA-256: 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9
-- Decoder: God2-0516-LZSS+CP936-strict; record boundaries: Verified.
-- Row 26 names column 1 `神仙ID`. Wuji row 43 maps `武吉` to 17 and
-- independently converges with the exact 0x31 consumer and formal-client
-- acceptance, establishing that this column is the login-wire identity domain.
-- This catalog-only promotion deliberately leaves HP/MP, attributes, owned
-- instances, runtime codec allowlists, and immortal skills unchanged.

UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=1,
    `admin_note`=CASE
        WHEN COALESCE(`admin_note`,'') LIKE '%GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 27%'
            THEN `admin_note`
        ELSE LEFT(CONCAT_WS(' | ',NULLIF(`admin_note`,''),
            'Verified static login-wire identity only: Data2/Patch/GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 27 rawFields=1|5601|data2\\fry\\fry5601.rom|姜子牙(金); column 1 header=神仙ID.'),500)
    END,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060004
  AND `code`='immortal_jiang_ziya'
  AND `enabled`=1
  AND `evidence_status`='Recovered'
  AND (`resource_id` IS NULL OR `resource_id`=1);

UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=10,
    `admin_note`=CASE
        WHEN COALESCE(`admin_note`,'') LIKE '%GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 36%'
            THEN `admin_note`
        ELSE LEFT(CONCAT_WS(' | ',NULLIF(`admin_note`,''),
            'Verified static login-wire identity only: Data2/Patch/GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 36 rawFields=10|5610|data2\\fry\\fry5610.rom|胡喜媚(土); column 1 header=神仙ID.'),500)
    END,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060005
  AND `code`='immortal_hu_ximei'
  AND `enabled`=1
  AND `evidence_status`='Recovered'
  AND (`resource_id` IS NULL OR `resource_id`=10);

UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=20,
    `admin_note`=CASE
        WHEN COALESCE(`admin_note`,'') LIKE '%GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 46%'
            THEN `admin_note`
        ELSE LEFT(CONCAT_WS(' | ',NULLIF(`admin_note`,''),
            'Verified static login-wire identity only: Data2/Patch/GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 46 rawFields=20|5620|data2\\fry\\fry5620.rom|慈航道人; column 1 header=神仙ID.'),500)
    END,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060003
  AND `code`='immortal_cihang_daoren'
  AND `enabled`=1
  AND `evidence_status`='Recovered'
  AND (`resource_id` IS NULL OR `resource_id`=20);

UPDATE `god2_game`.`immortal_templates`
SET `resource_id`=28,
    `admin_note`=CASE
        WHEN COALESCE(`admin_note`,'') LIKE '%GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 54%'
            THEN `admin_note`
        ELSE LEFT(CONCAT_WS(' | ',NULLIF(`admin_note`,''),
            'Verified static login-wire identity only: Data2/Patch/GodEft.csvZ SHA-256 42c3dad914184fea990a449fac62b668390b0f28d0efcf66cab07fe2156027c9 row 54 rawFields=28|5628|data2\\fry\\fry5628.rom|土行孙; column 1 header=神仙ID.'),500)
    END,
    `updated_at_utc`=UTC_TIMESTAMP(6)
WHERE `immortal_template_id`=1060002
  AND `code`='immortal_tuxingsun'
  AND `enabled`=1
  AND `evidence_status`='Recovered'
  AND (`resource_id` IS NULL OR `resource_id`=28);
