ALTER TABLE `spawns`
    ADD COLUMN IF NOT EXISTS `SpawnRadius` int NULL AFTER `RespawnSeconds`,
    ADD COLUMN IF NOT EXISTS `SpawnCount` int NULL AFTER `SpawnRadius`,
    ADD COLUMN IF NOT EXISTS `EvidenceStatus` varchar(32) NOT NULL DEFAULT 'EvidenceBlocked' AFTER `SpawnCount`,
    ADD COLUMN IF NOT EXISTS `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0 AFTER `EvidenceStatus`;
