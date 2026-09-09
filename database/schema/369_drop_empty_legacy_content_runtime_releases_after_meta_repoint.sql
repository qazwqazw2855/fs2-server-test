SET @legacy_content_release_rows := (SELECT COUNT(*) FROM `god2`.`content_runtime_releases`);
SET @formal_active_runtime_catalog_releases := (
    SELECT COUNT(*)
    FROM `god2_game_meta`.`runtime_catalog_releases`
    WHERE `status`='Active'
      AND `active_slot`=1
      AND `catalog_fingerprint` IS NOT NULL
      AND `catalog_record_count` IS NOT NULL
);
SET @legacy_content_release_view_refs := (
    SELECT COUNT(*)
    FROM information_schema.VIEWS
    WHERE VIEW_DEFINITION LIKE '%`god2`.`content_runtime_releases`%'
       OR VIEW_DEFINITION LIKE '%god2.content_runtime_releases%'
);

SET @legacy_content_release_guard_failure := (
    SELECT CASE
        WHEN @legacy_content_release_rows <> 0 THEN 1
        WHEN @formal_active_runtime_catalog_releases <> 1 THEN 1
        WHEN @legacy_content_release_view_refs <> 0 THEN 1
        ELSE 0
    END
);

SET @guard_message := CONCAT(
    'legacy content_runtime_releases table still unsafe to drop: rows=',
    @legacy_content_release_rows,
    ', formal_active_runtime_catalog_releases=',
    @formal_active_runtime_catalog_releases,
    ', view_refs=',
    @legacy_content_release_view_refs);

SET @guard_sql := IF(
    @legacy_content_release_guard_failure = 0,
    'SELECT 1',
    CONCAT('SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''', @guard_message, ''''));
PREPARE guard_statement FROM @guard_sql;
EXECUTE guard_statement;
DEALLOCATE PREPARE guard_statement;

DROP TABLE IF EXISTS `god2`.`content_runtime_releases`;
