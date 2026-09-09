UPDATE `god2_player`.`characters`
SET `status` = 'Deleted',
    `enabled` = 0,
    `deleted_at_utc` = COALESCE(`deleted_at_utc`, UTC_TIMESTAMP(6)),
    `updated_at_utc` = UTC_TIMESTAMP(6),
    `admin_note` = 'Retired legacy stitched validation character; only characters created on 2026-08-12 or later are valid for formal client validation.',
    `concurrency_token` = REPLACE(UUID(), '-', '')
WHERE (`character_id`,`account_id`,`name`,`created_at_utc`) IN (
    (1,11,'G2A','2026-07-31 09:40:49.796066'),
    (2,12,'G2B','2026-07-31 09:40:49.936450'),
    (3,13,'G2C','2026-07-31 09:40:50.006871'),
    (4,14,'G2D','2026-07-31 09:40:50.098543'));
