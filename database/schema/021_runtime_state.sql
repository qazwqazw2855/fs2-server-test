ALTER TABLE `accounts`
    ADD COLUMN IF NOT EXISTS `Status` varchar(32) NOT NULL DEFAULT 'Active' AFTER `PasswordHash`,
    ADD COLUMN IF NOT EXISTS `LastLoginAtUtc` datetime(6) NULL AFTER `UpdatedAtUtc`,
    ADD COLUMN IF NOT EXISTS `FailedLoginCount` int NOT NULL DEFAULT 0 AFTER `LastLoginAtUtc`,
    ADD COLUMN IF NOT EXISTS `LockedUntilUtc` datetime(6) NULL AFTER `FailedLoginCount`,
    ADD COLUMN IF NOT EXISTS `CurrentSessionId` varchar(64) NULL AFTER `LockedUntilUtc`;

ALTER TABLE `characters`
    ADD COLUMN IF NOT EXISTS `Class` varchar(32) NOT NULL DEFAULT 'Unknown' AFTER `Name`,
    ADD COLUMN IF NOT EXISTS `Gender` varchar(16) NOT NULL DEFAULT 'Unknown' AFTER `Class`,
    ADD COLUMN IF NOT EXISTS `Appearance` varchar(255) NOT NULL DEFAULT '' AFTER `Gender`,
    ADD COLUMN IF NOT EXISTS `Status` varchar(32) NOT NULL DEFAULT 'Active' AFTER `Level`,
    ADD COLUMN IF NOT EXISTS `LastPlayedAtUtc` datetime(6) NULL AFTER `UpdatedAtUtc`;
