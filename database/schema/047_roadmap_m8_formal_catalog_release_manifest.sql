ALTER TABLE `content_runtime_releases`
    ADD COLUMN IF NOT EXISTS `FormalCatalogManifestVersion` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `EvidenceReference`,
    ADD COLUMN IF NOT EXISTS `FormalCatalogFingerprint` char(64) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `FormalCatalogManifestVersion`,
    ADD COLUMN IF NOT EXISTS `FormalCatalogRecordCount` bigint unsigned NULL AFTER `FormalCatalogFingerprint`,
    ADD COLUMN IF NOT EXISTS `FormalCatalogPinnedAtUtc` datetime(6) NULL AFTER `FormalCatalogRecordCount`;

ALTER TABLE `content_runtime_releases`
    ADD INDEX IF NOT EXISTS `IX_ContentRuntimeReleases_FormalCatalogFingerprint` (`FormalCatalogFingerprint`);
