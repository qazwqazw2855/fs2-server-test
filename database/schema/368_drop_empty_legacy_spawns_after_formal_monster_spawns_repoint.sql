SET @legacy_spawns_rows := (SELECT COUNT(*) FROM `god2`.`spawns`);
SET @formal_monster_spawns_rows := (SELECT COUNT(*) FROM `god2_game`.`monster_spawns`);
SET @legacy_spawns_view_refs := (
    SELECT COUNT(*)
    FROM information_schema.VIEWS
    WHERE VIEW_DEFINITION LIKE '%`god2`.`spawns`%'
       OR VIEW_DEFINITION LIKE '%god2.spawns%'
);

SET @legacy_spawns_guard_failure := (
    SELECT CASE
        WHEN @legacy_spawns_rows <> 0 THEN 1
        WHEN @formal_monster_spawns_rows = 0 THEN 1
        WHEN @legacy_spawns_view_refs <> 0 THEN 1
        ELSE 0
    END
);

SET @guard_message := CONCAT(
    'legacy spawns table still unsafe to drop: rows=',
    @legacy_spawns_rows,
    ', formal_monster_spawns=',
    @formal_monster_spawns_rows,
    ', view_refs=',
    @legacy_spawns_view_refs);

SET @guard_sql := IF(
    @legacy_spawns_guard_failure = 0,
    'SELECT 1',
    CONCAT('SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''', @guard_message, ''''));
PREPARE guard_statement FROM @guard_sql;
EXECUTE guard_statement;
DEALLOCATE PREPARE guard_statement;

DROP TABLE IF EXISTS `god2`.`spawns`;
