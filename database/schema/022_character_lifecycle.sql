ALTER TABLE `characters`
    ADD COLUMN IF NOT EXISTS `Class` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `Name`,
    ADD COLUMN IF NOT EXISTS `Gender` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `Class`,
    ADD COLUMN IF NOT EXISTS `LifeSkill` varchar(32) NOT NULL DEFAULT 'LifeSkill1' AFTER `Gender`,
    ADD COLUMN IF NOT EXISTS `Appearance` varchar(64) NOT NULL DEFAULT 'Unknown' AFTER `LifeSkill`,
    ADD COLUMN IF NOT EXISTS `Status` varchar(16) NOT NULL DEFAULT 'Active' AFTER `PositionY`,
    ADD COLUMN IF NOT EXISTS `LastPlayedAtUtc` datetime(6) NULL AFTER `UpdatedAtUtc`,
    ADD COLUMN IF NOT EXISTS `DeletedAtUtc` datetime(6) NULL AFTER `LastPlayedAtUtc`;
