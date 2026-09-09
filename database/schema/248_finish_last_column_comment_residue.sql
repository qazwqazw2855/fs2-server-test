ALTER TABLE `god2_game`.`item_asset_mappings`
  MODIFY COLUMN `icon_atlas_id` int(11) NULL COMMENT '對應的官方地圖資源圖集 ID';

ALTER TABLE `god2_game`.`item_icon_atlases`
  MODIFY COLUMN `atlas_id` int(11) NOT NULL COMMENT '官方地圖資源圖集列 ID';

ALTER TABLE `god2_player`.`player_social_operations`
  DROP FOREIGN KEY `fk_player_social_operation_invitation`;

ALTER TABLE `god2_player`.`player_social_invitations`
  MODIFY COLUMN `invitation_id` char(36) NOT NULL COMMENT '社交邀請 ID';

ALTER TABLE `god2_player`.`player_social_operations`
  MODIFY COLUMN `invitation_id` char(36) NULL COMMENT '相關社交邀請 ID；無邀請時為 NULL',
  ADD CONSTRAINT `fk_player_social_operation_invitation`
    FOREIGN KEY (`invitation_id`)
    REFERENCES `god2_player`.`player_social_invitations` (`invitation_id`)
    ON DELETE RESTRICT
    ON UPDATE RESTRICT;
