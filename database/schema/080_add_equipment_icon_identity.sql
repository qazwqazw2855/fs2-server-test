ALTER TABLE `god2_game`.`equipment`
    ADD COLUMN IF NOT EXISTS `icon_id` int NULL COMMENT 'Official global item icon code' AFTER `source_item_type`,
    ADD COLUMN IF NOT EXISTS `model_key` varchar(256) NULL COMMENT 'Official client presentation/model key' AFTER `icon_id`,
    ADD KEY IF NOT EXISTS `ix_equipment_icon_id` (`icon_id`);
