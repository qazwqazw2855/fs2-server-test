-- 469_pin_server_v2_npc_spawn_evidence.sql
-- Purpose: pin independently reviewable application-record hashes for the
-- Server V2 Map 3 NPC spawn profiles. These rows are Derived from the
-- capture-backed resource-type-0 family and are not labelled Verified.

ALTER TABLE god2_game.npc_spawns
  ADD COLUMN IF NOT EXISTS spawn_message_sha256 char(64) NULL
    COMMENT 'Expected SHA-256 of the decoded 21-byte 0x72 application record',
  ADD COLUMN IF NOT EXISTS opaque_template_sha256 char(64) NULL
    COMMENT 'Expected SHA-256 of the preserved nine-byte ignored region',
  ADD COLUMN IF NOT EXISTS wire_evidence_status varchar(30) NOT NULL
    DEFAULT 'EvidenceBlocked'
    COMMENT 'Verified, Derived, or EvidenceBlocked',
  ADD COLUMN IF NOT EXISTS wire_evidence_reference varchar(500) NULL
    COMMENT 'Auditable source and derivation boundary';

UPDATE god2_game.npc_spawns
SET
  spawn_message_sha256 =
    '3F25673AE985BF8F4818F2EE1254AB019B0406B8BD1C38C44F701DD5BAE27834',
  opaque_template_sha256 =
    'C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838',
  wire_evidence_status = 'Derived',
  wire_evidence_reference =
    'ServerV2-M3-derived-type0; CurrentBuild-live-0x72/21; handle=5042; position=74,124'
WHERE spawn_id = 1310005042
  AND npc_id = 1300005042
  AND map_id = 1675308248
  AND client_build_id = 'god2-opt-6b127086e0c0'
  AND observed_client_entity_handle = 5042
  AND official_resource_type = 0
  AND official_resource_ordinal = 45
  AND official_selector_high_bits = 3
  AND official_direction_code = 4
  AND official_state_code = 1
  AND position_x = 74
  AND position_y = 124
  AND enabled = 1;

UPDATE god2_game.npc_spawns
SET
  spawn_message_sha256 =
    '78230A74DC17C388FE1A6FFDF6EBBD284A05C3E77E20719BE1921941903CC9B7',
  opaque_template_sha256 =
    'C1D92D6C8E358E6E894389F60CB522CBB67429566A58C2B66DEBC872FB9E7838',
  wire_evidence_status = 'Derived',
  wire_evidence_reference =
    'ServerV2-M3-derived-type0; CurrentBuild-live-0x72/21; handle=5096; position=73,121'
WHERE spawn_id = 1310005096
  AND npc_id = 1300005096
  AND map_id = 1675308248
  AND client_build_id = 'god2-opt-6b127086e0c0'
  AND observed_client_entity_handle = 5096
  AND official_resource_type = 0
  AND official_resource_ordinal = 45
  AND official_selector_high_bits = 3
  AND official_direction_code = 4
  AND official_state_code = 1
  AND position_x = 73
  AND position_y = 121
  AND enabled = 1;
