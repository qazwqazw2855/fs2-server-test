-- Replace importer-generated hash codes with readable source identities.
-- PayloadSha256 remains in the recovery schema as provenance evidence.

SET @god2_sync_mode = 1;

UPDATE `god2`.`items`
SET `Code` = CONCAT('item_', CAST(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.clientItemId')) AS UNSIGNED))
WHERE JSON_EXTRACT(`PayloadJson`, '$.clientItemId') IS NOT NULL;

UPDATE `god2`.`maps`
SET `Code` = LEFT(CONCAT(
    'map_',
    REGEXP_REPLACE(
        REGEXP_REPLACE(LOWER(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.id'))), '^client:map/', ''),
        '[^a-z0-9]+', '_')),
    64)
WHERE JSON_EXTRACT(`PayloadJson`, '$.id') IS NOT NULL;

UPDATE `god2`.`skills`
SET `Code` = LEFT(CONCAT(
    'skill_',
    REGEXP_REPLACE(
        REGEXP_REPLACE(LOWER(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.id'))), '^client:skill/data2/patch/', ''),
        '[^a-z0-9]+', '_')),
    64)
WHERE JSON_EXTRACT(`PayloadJson`, '$.id') IS NOT NULL;

UPDATE `god2`.`npcs`
SET `Code` = LEFT(CONCAT('npc_', REPLACE(
    SUBSTRING_INDEX(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.id')), '/', -1),
    ' ', '_')), 64)
WHERE JSON_EXTRACT(`PayloadJson`, '$.id') IS NOT NULL;

UPDATE `god2`.`monsters`
SET `Code` = CONCAT('monster_', CAST(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.clientMonsterId')) AS UNSIGNED))
WHERE JSON_EXTRACT(`PayloadJson`, '$.clientMonsterId') IS NOT NULL;

UPDATE `god2`.`quests`
SET `Code` = CONCAT('quest_', `Id`);

UPDATE `god2`.`dialogs`
SET `Code` = LEFT(CONCAT(
        'dialog_',
        REGEXP_REPLACE(
            REGEXP_REPLACE(LOWER(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.id'))), '^client:dialog-catalog/', ''),
            '[^a-z0-9]+', '_')),
        64),
    `TextKey` = LEFT(CONCAT(
        'official.dialogs.',
        REGEXP_REPLACE(
            REGEXP_REPLACE(LOWER(JSON_UNQUOTE(JSON_EXTRACT(`PayloadJson`, '$.id'))), '^client:dialog-catalog/', ''),
            '[^a-z0-9]+', '_')),
        128)
WHERE JSON_EXTRACT(`PayloadJson`, '$.id') IS NOT NULL;

UPDATE `god2`.`localization_entries`
SET `TextKey` = LEFT(CONCAT(
    'official.localization.',
    TRIM(BOTH '_' FROM REGEXP_REPLACE(
        REGEXP_REPLACE(LOWER(`TextValue`), '^original/', ''),
        '[^a-z0-9]+', '_'))),
    128)
WHERE `TextKey` REGEXP '^official[.]localization[.][0-9a-f]{32}$';

UPDATE `god2_game`.`items` formal_row
JOIN `god2`.`items` source_row ON source_row.`Id` = formal_row.`item_id`
SET formal_row.`code` = source_row.`Code`;

UPDATE `god2_game`.`maps` formal_row
JOIN `god2`.`maps` source_row ON source_row.`Id` = formal_row.`map_id`
SET formal_row.`code` = source_row.`Code`;

UPDATE `god2_game`.`skills` formal_row
JOIN `god2`.`skills` source_row ON source_row.`Id` = formal_row.`skill_id`
SET formal_row.`code` = source_row.`Code`;

UPDATE `god2_game`.`npcs` formal_row
JOIN `god2`.`npcs` source_row ON source_row.`Id` = formal_row.`npc_id`
SET formal_row.`code` = source_row.`Code`;

UPDATE `god2_game`.`npc_dialogs` formal_row
JOIN `god2`.`dialogs` source_row ON source_row.`Id` = formal_row.`dialog_id`
SET formal_row.`code` = source_row.`Code`;

UPDATE `god2_game`.`quests`
SET `code` = CONCAT('quest_', `quest_id`);

UPDATE `god2_game`.`monsters`
SET `code` = CONCAT('monster_', `monster_id`)
WHERE `code` IS NULL OR `code` = '' OR `code` REGEXP '^r_[0-9a-f]{32}$';

UPDATE `god2_game`.`character_classes`
SET `code` = CONCAT('class_', `class_id`)
WHERE `code` REGEXP '^r_[0-9a-f]{32}$';

UPDATE `god2_game`.`immortal_templates`
SET `code` = CONCAT('immortal_', `immortal_template_id`)
WHERE `code` REGEXP '^r_[0-9a-f]{32}$';

UPDATE `god2_game`.`mount_templates`
SET `code` = CONCAT('mount_', `mount_template_id`)
WHERE `code` REGEXP '^r_[0-9a-f]{32}$';

UPDATE `god2_game`.`pet_templates`
SET `code` = CONCAT('pet_', `pet_template_id`)
WHERE `code` REGEXP '^r_[0-9a-f]{32}$';

SET @god2_sync_mode = NULL;
