ALTER TABLE `god2_game`.`client_map_resources`
    ADD COLUMN IF NOT EXISTS `resource_format` varchar(16) NULL COMMENT 'MDT or HMD',
    ADD COLUMN IF NOT EXISTS `resource_file_relative_path` varchar(512) NULL COMMENT 'Official client resource file path',
    ADD COLUMN IF NOT EXISTS `resource_file_sha256` char(64) NULL COMMENT 'Official client packed resource SHA-256',
    ADD COLUMN IF NOT EXISTS `navigation_format` varchar(30) NULL COMMENT 'MBD v1.2 or HMD v1.6',
    ADD COLUMN IF NOT EXISTS `navigation_relative_path` varchar(512) NULL COMMENT 'Navigation evidence file path',
    ADD COLUMN IF NOT EXISTS `navigation_sha256` char(64) NULL COMMENT 'Navigation evidence file SHA-256',
    ADD COLUMN IF NOT EXISTS `navigation_evidence_status` varchar(30) NOT NULL DEFAULT 'Unknown' COMMENT 'Navigation dimension evidence status';

UPDATE `god2_game`.`client_map_resources` resource
JOIN `god2_game`.`maps` map_row ON map_row.`map_id` = 1675308248
SET resource.`canonical_map_id` = map_row.`map_id`,
    resource.`client_map_id` = 3,
    resource.`client_area_id` = 4,
    resource.`resource_format` = 'MDT',
    resource.`resource_file_relative_path` = 'Data2/map/island03/cityi2/cityi2.mdtZ',
    resource.`resource_file_sha256` = '292d0011561d97214fd77adcd0e0121a31b491b13bb26309c96fe4617630695e',
    resource.`navigation_format` = 'MBD v1.2',
    resource.`navigation_relative_path` = 'Data2/map/island03/cityi2/cityi2.mbd',
    resource.`navigation_sha256` = 'eba7aa7ae1ddc0381c7fe30d452fa251345aa9310386948b5eaf080c37d78e59',
    resource.`navigation_evidence_status` = 'Derived',
    resource.`grid_width` = 12,
    resource.`grid_height` = 12,
    resource.`minimum_x` = 0,
    resource.`maximum_x` = 251,
    resource.`minimum_y` = 0,
    resource.`maximum_y` = 251,
    resource.`map_identity_evidence_status` = 'Verified',
    resource.`portal_placement_evidence_status` = 'Verified',
    resource.`enabled` = 1,
    resource.`admin_note` = 'Verified official Client FileIO and static consumer binding; Migration 036.'
WHERE resource.`resource_key` = 'client:map/island03/cityi2/cityi2'
  AND resource.`client_build_id` = 'god2-opt-6b127086e0c0'
  AND resource.`grid_width` = 12
  AND resource.`grid_height` = 12
  AND resource.`can_sha256` = '5a176c2bde2c1c98e4e853213cb7017d114eb4ac0f7dae9e0810f91fc806e727'
  AND map_row.`client_map_id` = 3
  AND map_row.`client_area_id` = 4
  AND map_row.`identity_evidence_status` = 'Verified'
  AND map_row.`coordinate_evidence_status` = 'Verified';

UPDATE `god2_game`.`client_map_resources` resource
JOIN `god2_game`.`maps` map_row ON map_row.`map_id` = 557790525
SET resource.`canonical_map_id` = map_row.`map_id`,
    resource.`client_map_id` = 19,
    resource.`client_area_id` = 4,
    resource.`resource_format` = 'HMD',
    resource.`resource_file_relative_path` = 'Data2/map/island01/indoor/groceryl.hmdZ',
    resource.`resource_file_sha256` = '42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00',
    resource.`navigation_format` = 'HMD v1.6',
    resource.`navigation_relative_path` = 'Data2/map/island01/indoor/groceryl.hmdZ',
    resource.`navigation_sha256` = '42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00',
    resource.`navigation_evidence_status` = 'Derived',
    resource.`grid_width` = 10,
    resource.`grid_height` = 30,
    resource.`minimum_x` = 0,
    resource.`maximum_x` = 209,
    resource.`minimum_y` = 0,
    resource.`maximum_y` = 629,
    resource.`map_identity_evidence_status` = 'Verified',
    resource.`portal_placement_evidence_status` = 'Verified',
    resource.`enabled` = 1,
    resource.`admin_note` = 'Verified official Client FileIO and static consumer binding; Migration 038.'
WHERE resource.`resource_key` = 'client:map/island01/indoor/groceryl'
  AND resource.`client_build_id` = 'god2-opt-6b127086e0c0'
  AND resource.`can_sha256` = 'f2b9ca9dc4dcebcd1c1c40de14ea63f1b2061993f644c49b9c7baca9ce2d2f97'
  AND map_row.`client_map_id` = 19
  AND map_row.`client_area_id` = 4
  AND map_row.`identity_evidence_status` = 'Verified'
  AND map_row.`coordinate_evidence_status` = 'Verified';

ALTER TABLE `god2_game`.`client_map_resources`
    ADD KEY IF NOT EXISTS `ix_client_map_resources_navigation` (`resource_format`,`navigation_evidence_status`),
    DROP CONSTRAINT IF EXISTS `ck_client_map_resources_navigation_gate`,
    ADD CONSTRAINT `ck_client_map_resources_navigation_gate` CHECK (
        `enabled` = 0 OR (
            `grid_width` > 0 AND `grid_height` > 0 AND
            `minimum_x` IS NOT NULL AND `maximum_x` IS NOT NULL AND
            `minimum_y` IS NOT NULL AND `maximum_y` IS NOT NULL AND
            `navigation_evidence_status` IN ('Verified','Derived')));
