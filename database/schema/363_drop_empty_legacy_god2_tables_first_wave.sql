-- Schema 363
-- Game function: remove first wave of empty legacy god2 tables after consolidation.
-- Safety: these tables must remain empty before being dropped; formal runtime uses god2_game/god2_player replacements.

SET @legacy_drop_table_items_rows := (SELECT COUNT(*) FROM `god2`.`drop_table_items`);
SET @legacy_merchant_items_rows := (SELECT COUNT(*) FROM `god2`.`merchant_items`);
SET @legacy_inventory_idempotency_rows := (SELECT COUNT(*) FROM `god2`.`inventory_transaction_idempotency`);
SET @legacy_empty_cleanup_rows :=
    @legacy_drop_table_items_rows +
    @legacy_merchant_items_rows +
    @legacy_inventory_idempotency_rows;

SET @legacy_empty_cleanup_guard_sql := IF(
    @legacy_empty_cleanup_rows = 0,
    'SELECT 1',
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT=''Legacy god2 table cleanup aborted because candidate tables are not empty.'''
);
PREPARE legacy_empty_cleanup_guard_statement FROM @legacy_empty_cleanup_guard_sql;
EXECUTE legacy_empty_cleanup_guard_statement;
DEALLOCATE PREPARE legacy_empty_cleanup_guard_statement;

DROP TABLE IF EXISTS `god2`.`drop_table_items`;
DROP TABLE IF EXISTS `god2`.`merchant_items`;
DROP TABLE IF EXISTS `god2`.`inventory_transaction_idempotency`;
