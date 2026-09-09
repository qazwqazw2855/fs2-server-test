SET @legacy_player_authority_rows := (
    SELECT
        (SELECT COUNT(*) FROM `god2`.`character_creation_profiles`) +
        (SELECT COUNT(*) FROM `god2`.`equipment_slots`) +
        (SELECT COUNT(*) FROM `god2`.`inventory_audit_ledger`) +
        (SELECT COUNT(*) FROM `god2`.`inventory_slots`) +
        (SELECT COUNT(*) FROM `god2`.`player_currency_balances`) +
        (SELECT COUNT(*) FROM `god2`.`player_inventory_state`) +
        (SELECT COUNT(*) FROM `god2`.`world_interaction_audit`)
);

SET @legacy_player_authority_view_refs := (
    SELECT COUNT(*)
    FROM information_schema.VIEWS
    WHERE VIEW_DEFINITION LIKE '%`god2`.`character_creation_profiles`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`equipment_slots`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`inventory_audit_ledger`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`inventory_slots`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`player_currency_balances`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`player_inventory_state`%'
       OR VIEW_DEFINITION LIKE '%`god2`.`world_interaction_audit`%'
       OR VIEW_DEFINITION LIKE '%god2.character_creation_profiles%'
       OR VIEW_DEFINITION LIKE '%god2.equipment_slots%'
       OR VIEW_DEFINITION LIKE '%god2.inventory_audit_ledger%'
       OR VIEW_DEFINITION LIKE '%god2.inventory_slots%'
       OR VIEW_DEFINITION LIKE '%god2.player_currency_balances%'
       OR VIEW_DEFINITION LIKE '%god2.player_inventory_state%'
       OR VIEW_DEFINITION LIKE '%god2.world_interaction_audit%'
);

SET @legacy_player_authority_guard_failure := (
    SELECT @legacy_player_authority_rows + @legacy_player_authority_view_refs
);

SET @guard_message := CONCAT(
    'legacy player authority tables still unsafe to drop: rows=',
    @legacy_player_authority_rows,
    ', view_refs=',
    @legacy_player_authority_view_refs);

SET @guard_sql := IF(
    @legacy_player_authority_guard_failure = 0,
    'SELECT 1',
    CONCAT('SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''', @guard_message, ''''));
PREPARE guard_statement FROM @guard_sql;
EXECUTE guard_statement;
DEALLOCATE PREPARE guard_statement;

DROP TABLE IF EXISTS `god2`.`equipment_slots`;
DROP TABLE IF EXISTS `god2`.`inventory_slots`;
DROP TABLE IF EXISTS `god2`.`inventory_audit_ledger`;
DROP TABLE IF EXISTS `god2`.`player_currency_balances`;
DROP TABLE IF EXISTS `god2`.`player_inventory_state`;
DROP TABLE IF EXISTS `god2`.`world_interaction_audit`;
DROP TABLE IF EXISTS `god2`.`character_creation_profiles`;
