-- Verified source: client:item/pfd/6906, corroborated by CN official
-- merchant capture recorded in docs/parity/merchant-cn-live-20261004.md.
-- Preserve canonical item_id, prices, stack policy and player references.

CREATE TEMPORARY TABLE `god2_game`.`god2_small_leg_identity_guard` (
    `ready` tinyint NOT NULL,
    CHECK (`ready` = 1)
);

START TRANSACTION;

SELECT `item_id`, `client_item_id`
FROM `god2_game`.`items`
WHERE `item_id`=2117880098 OR `client_item_id`=6906
FOR UPDATE;

INSERT INTO `god2_game`.`god2_small_leg_identity_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*) FROM `god2_game`.`items`
     WHERE `item_id`=2117880098
       AND `code`='item_6906'
       AND `name_zh_tw`='小腿肉'
       AND `client_item_id` IN (2117880098,6906))=1
    AND
    (SELECT COUNT(*) FROM `god2_game`.`items`
     WHERE `client_item_id`=6906 AND `item_id`<>2117880098)=0
    AND
    (SELECT COUNT(*) FROM `information_schema`.`KEY_COLUMN_USAGE`
     WHERE `REFERENCED_TABLE_SCHEMA`='god2_game'
       AND `REFERENCED_TABLE_NAME`='items'
       AND `REFERENCED_COLUMN_NAME`='client_item_id')=0,
    1,0);

UPDATE `god2_game`.`items`
SET `client_item_id`=6906
WHERE `item_id`=2117880098 AND `client_item_id`=2117880098;

INSERT INTO `god2_game`.`god2_small_leg_identity_guard` (`ready`)
SELECT IF(
    (SELECT COUNT(*) FROM `god2_game`.`items`
     WHERE `item_id`=2117880098 AND `client_item_id`=6906
       AND `code`='item_6906' AND `name_zh_tw`='小腿肉')=1,
    1,0);

COMMIT;

DROP TEMPORARY TABLE `god2_game`.`god2_small_leg_identity_guard`;
