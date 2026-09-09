UPDATE `content_runtime_releases`
SET `FormalCatalogManifestVersion`=NULL,
    `FormalCatalogFingerprint`=NULL,
    `FormalCatalogRecordCount`=NULL,
    `FormalCatalogPinnedAtUtc`=NULL
WHERE `FormalCatalogManifestVersion`='god2-formal-runtime-catalog-manifest-v1';
