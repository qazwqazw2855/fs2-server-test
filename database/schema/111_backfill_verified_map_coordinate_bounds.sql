UPDATE `god2_game`.`maps`
SET `minimum_x` = 0,
    `maximum_x` = 251,
    `minimum_y` = 0,
    `maximum_y` = 251,
    `admin_note` = 'Coordinate bounds derived from verified 12x12 MBD grid with official 21-unit cell divisor.'
WHERE `map_id` = 1675308248
  AND `client_map_id` = 3
  AND `client_area_id` = 4
  AND `width` = 12
  AND `height` = 12
  AND `resource_identity` = 'cityi2/cityi2.mdt'
  AND `identity_evidence_status` = 'Verified'
  AND `coordinate_evidence_status` = 'Verified';

UPDATE `god2_game`.`maps`
SET `minimum_x` = 0,
    `maximum_x` = 209,
    `minimum_y` = 0,
    `maximum_y` = 629,
    `admin_note` = 'Coordinate bounds derived from verified HMD v1.6 Pass 10x30 grid with official 21-unit cell divisor.'
WHERE `map_id` = 557790525
  AND `client_map_id` = 19
  AND `client_area_id` = 4
  AND `width` = 10
  AND `height` = 30
  AND `resource_identity` = 'indoor/groceryl.hmd'
  AND `identity_evidence_status` = 'Verified'
  AND `coordinate_evidence_status` = 'Verified';
