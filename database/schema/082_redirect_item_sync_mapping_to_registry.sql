UPDATE `god2_game_meta`.`field_mappings`
SET `target_table`='item_registry',
    `admin_note`='同步到共用物品索引；武器、裝備、法寶由分表載入'
WHERE `source_schema`='god2'
  AND `source_table`='items'
  AND `target_schema`='god2_game'
  AND `target_table`='items';
