-- Align the canonical NPC service catalog with the exact-build formal-client
-- acceptances completed on 2026-08-13.  This migration does not generalize
-- selector semantics: only handle 3793 selector 7 is a complete observed
-- dialog slice.  The legacy recovered dialog rows contain no NPC binding,
-- body, type, or options and therefore remain catalog evidence only.

UPDATE `god2_game`.`npc_spawns`
SET `service_evidence_status` = 'CompleteObservedDialogSlice',
    `admin_note` = LEFT(CONCAT(
        COALESCE(NULLIF(`admin_note`,''), ''),
        CASE
            WHEN COALESCE(`admin_note`,'') LIKE '%formal selector 7 acceptance%' THEN ''
            ELSE ' 2026-08-13 formal selector 7 acceptance: exact 0x85 and compound 0x86+0x85 were consumed with no response or gameplay mutation; every other selector remains evidence-blocked.'
        END), 500)
WHERE `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `observed_client_entity_handle` = 3793
  AND `wire_evidence_status` = 'Verified'
  AND `identity_evidence_status` = 'Verified'
  AND `coordinate_evidence_status` = 'Verified'
  AND `service_evidence_status` IN ('OpenOnly','CompleteObservedDialogSlice')
  AND `enabled` = 1;

UPDATE `god2_game`.`npc_dialogs` dialog_row
SET dialog_row.`enabled` = 0,
    dialog_row.`admin_note` = LEFT(CONCAT(
        COALESCE(NULLIF(dialog_row.`admin_note`,''), ''),
        CASE
            WHEN COALESCE(dialog_row.`admin_note`,'') LIKE '%CatalogOnlyEvidenceBlocked%' THEN ''
            ELSE ' CatalogOnlyEvidenceBlocked: no production NPC binding, body, dialog type, and enabled option set are available.'
        END), 500)
WHERE dialog_row.`enabled` = 1
  AND (
      dialog_row.`npc_id` IS NULL
      OR NULLIF(TRIM(dialog_row.`body_zh_tw`),'') IS NULL
      OR NULLIF(TRIM(dialog_row.`dialog_type`),'') IS NULL
      OR NOT EXISTS (
          SELECT 1
          FROM `god2_game`.`npc_dialog_options` option_row
          WHERE option_row.`dialog_id` = dialog_row.`dialog_id`
            AND option_row.`enabled` = 1));
