-- 470_pin_server_v2_live_dialog_spawn_evidence.sql
--
-- Promote only the exact-build handle-3793 spawn already accepted by the
-- Classic live-recovery evidence. This does not generalize type-zero NPCs,
-- dialog selectors, merchant behavior, or any other entity handle.

UPDATE `god2_game`.`npc_spawns`
SET
    `spawn_message_sha256` =
        'FAEB2E6FEC5D9B23F9A06143085535443473A8D63A8C0165BA43CF9586F8FFFF',
    `opaque_template_sha256` =
        'C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838',
    `wire_evidence_status` = 'Verified',
    `wire_evidence_reference` =
        'LiveRecovery/attempt-759-decode-2579; exact handle=3793; map=170015007; position=65,61; decoded=180072D10E000058600000A1000000CF01000004017A0075',
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `spawn_id` = 170153793
  AND `npc_id` = 170015087
  AND `map_id` = 170015007
  AND `client_build_id` = 'god2-opt-6b127086e0c0'
  AND `observed_client_entity_handle` = 3793
  AND `official_resource_type` = 0
  AND `official_resource_ordinal` = 87
  AND `official_selector_high_bits` = 3
  AND `official_direction_code` = 5
  AND `official_state_code` = 1
  AND `position_x` = 65
  AND `position_y` = 61
  AND `enabled` = 1;
