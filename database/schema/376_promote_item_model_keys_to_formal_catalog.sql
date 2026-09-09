ALTER TABLE `god2_game`.`item_registry`
    ADD COLUMN IF NOT EXISTS `model_key` varchar(256) NULL COMMENT '官方客戶端外觀或模型鍵' AFTER `model_id`;

ALTER TABLE `god2_game`.`items`
    ADD COLUMN IF NOT EXISTS `model_key` varchar(256) NULL COMMENT '官方客戶端外觀或模型鍵' AFTER `model_id`;

ALTER TABLE `god2_game`.`weapons`
    ADD COLUMN IF NOT EXISTS `model_key` varchar(256) NULL COMMENT '官方客戶端外觀或模型鍵' AFTER `model_id`;

ALTER TABLE `god2_game`.`magic_treasures`
    ADD COLUMN IF NOT EXISTS `model_key` varchar(256) NULL COMMENT '官方客戶端外觀或模型鍵' AFTER `model_id`;

UPDATE `god2_game`.`item_registry` registry_row
JOIN `god2`.`item_content_profiles` profile_row
  ON profile_row.`ItemId` = registry_row.`item_id`
SET registry_row.`model_key` = NULLIF(TRIM(profile_row.`ModelKey`), '')
WHERE NULLIF(TRIM(profile_row.`ModelKey`), '') IS NOT NULL;

UPDATE `god2_game`.`items` content_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`item_id` = content_row.`item_id`
SET content_row.`model_key` = registry_row.`model_key`
WHERE registry_row.`model_key` IS NOT NULL;

UPDATE `god2_game`.`weapons` content_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`item_id` = content_row.`item_id`
SET content_row.`model_key` = registry_row.`model_key`
WHERE registry_row.`model_key` IS NOT NULL;

UPDATE `god2_game`.`equipment` content_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`item_id` = content_row.`item_id`
SET content_row.`model_key` = registry_row.`model_key`
WHERE registry_row.`model_key` IS NOT NULL;

UPDATE `god2_game`.`magic_treasures` content_row
JOIN `god2_game`.`item_registry` registry_row
  ON registry_row.`item_id` = content_row.`item_id`
SET content_row.`model_key` = registry_row.`model_key`
WHERE registry_row.`model_key` IS NOT NULL;
