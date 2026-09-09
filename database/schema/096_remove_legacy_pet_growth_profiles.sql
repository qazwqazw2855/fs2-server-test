-- Remove the unused generic pet-growth placeholder superseded by formal grade and allocation rules.

ALTER TABLE `god2_game`.`pet_templates`
    DROP FOREIGN KEY IF EXISTS `fk_pet_templates_growth_profile`,
    DROP INDEX IF EXISTS `ix_pet_templates_growth_profile`,
    DROP COLUMN IF EXISTS `growth_profile_id`;

DROP TABLE IF EXISTS `god2_game`.`pet_growth_profiles`;
