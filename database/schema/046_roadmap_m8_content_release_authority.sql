CREATE TABLE IF NOT EXISTS `content_runtime_releases` (
    `ReleaseId` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `SourceRunId` char(36) NOT NULL,
    `ReleaseStatus` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ActiveSlot` tinyint
        GENERATED ALWAYS AS (
            CASE WHEN `ReleaseStatus` = 'Active' THEN 1 ELSE NULL END
        ) STORED,
    `EvidenceReference` varchar(512) NOT NULL,
    `ActivatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`ReleaseId`),
    UNIQUE KEY `UX_ContentRuntimeReleases_ActiveSlot` (`ActiveSlot`),
    KEY `IX_ContentRuntimeReleases_SourceRun` (`SourceRunId`),
    CONSTRAINT `FK_ContentRuntimeReleases_SourceRun`
        FOREIGN KEY (`SourceRunId`) REFERENCES `content_recovery_runs` (`RunId`),
    CONSTRAINT `CK_ContentRuntimeReleases_Status`
        CHECK (`ReleaseStatus` IN ('Active', 'Superseded', 'Rejected'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `content_runtime_releases`
    (`ReleaseId`, `SourceRunId`, `ReleaseStatus`, `EvidenceReference`, `ActivatedAtUtc`)
SELECT UUID(), completed.`RunId`, 'Active',
       'Migration 046: latest completed GameplayContentRecoveryPhase3 release at migration time',
       UTC_TIMESTAMP(6)
FROM `content_recovery_runs` completed
WHERE completed.`Phase` = 'GameplayContentRecoveryPhase3'
  AND completed.`Status` = 'COMPLETED'
  AND NOT EXISTS (
      SELECT 1
      FROM `content_runtime_releases` current_release
      WHERE current_release.`ReleaseStatus` = 'Active')
ORDER BY completed.`CompletedAtUtc` DESC, completed.`RunId` DESC
LIMIT 1;
