-- Pin the exact-current client file disposition for the sole formal map identity
-- whose declared resource file is absent from the exact-current map manifest.
-- Evidence source:
--   db/imports/official/maps/God2_exact_current_map_sha256.csv
--   SHA-256: b2362491b9045a7c4b2f489706ad66eb36c71bcb999c22c103aab47035e56a51
--
-- Formal reconciliation:
--   144 formal map identities
--   143 declared resource files present
--   1 declared resource file absent:
--     map_id 1200070008
--     client:map/tong/reborn/reborn
--     Data2/map/tong/reborn/reborn.mdtZ
--
-- This migration updates research evidence only.
-- It does not enable maps, identities or portals and does not modify player state.

CREATE TEMPORARY TABLE `god2_exact_map_file_provenance_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_exact_map_file_provenance_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_exact_map_file_provenance_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*) FROM `god2_game`.`client_map_resource_identities`
     WHERE `client_build_id`='god2-opt-6b127086e0c0') = 144
    AND
    (SELECT COUNT(*)
     FROM `god2_game`.`client_map_resource_identities`
     WHERE `client_build_id`='god2-opt-6b127086e0c0'
       AND `map_id`=1200070008
       AND `resource_key`='client:map/tong/reborn/reborn') = 1
    AND
    (SELECT COUNT(*)
     FROM `god2_research`.`client_map_resource_evidence`
     WHERE `resource_key`='client:map/tong/reborn/reborn'
       AND `map_identity_evidence_status`='Verified'
       AND `navigation_evidence_status`='EvidenceBlocked'
       AND `resource_file_relative_path` IS NULL
       AND `resource_file_sha256` IS NULL) = 1,
    1,
    0
);

UPDATE `god2_research`.`client_map_resource_evidence`
SET
    `resource_evidence_status`='Recovered',
    `map_identity_evidence_status`='Verified',
    `navigation_evidence_status`='EvidenceBlocked',
    `resource_file_relative_path`=NULL,
    `resource_file_sha256`=NULL,
    `admin_note`='Exact-current manifest b2362491b9045a7c4b2f489706ad66eb36c71bcb999c22c103aab47035e56a51 confirms declared Data2/map/tong/reborn/reborn.mdtZ is absent; identity remains verified from pinned Migration 114 and navigation remains EvidenceBlocked.',
    `moved_at_utc`=UTC_TIMESTAMP(6)
WHERE `resource_key`='client:map/tong/reborn/reborn';

CREATE TEMPORARY TABLE `god2_exact_map_file_provenance_result_guard` (
    `ready` tinyint NOT NULL,
    CONSTRAINT `ck_god2_exact_map_file_provenance_result_guard`
        CHECK (`ready` = 1)
);

INSERT INTO `god2_exact_map_file_provenance_result_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*)
     FROM `god2_research`.`client_map_resource_evidence`
     WHERE `resource_key`='client:map/tong/reborn/reborn'
       AND `map_identity_evidence_status`='Verified'
       AND `navigation_evidence_status`='EvidenceBlocked'
       AND `resource_file_relative_path` IS NULL
       AND `resource_file_sha256` IS NULL
       AND `admin_note` LIKE 'Exact-current manifest b2362491%') = 1,
    1,
    0
);

DROP TEMPORARY TABLE `god2_exact_map_file_provenance_result_guard`;
DROP TEMPORARY TABLE `god2_exact_map_file_provenance_guard`;
