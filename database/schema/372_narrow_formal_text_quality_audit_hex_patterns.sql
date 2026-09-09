-- Schema 372
-- Game function: formal database text quality audit.
-- Purpose: narrow mojibake byte detection so legitimate Traditional Chinese item names are not reported.

CREATE OR REPLACE VIEW `god2_game`.`vw_formal_text_quality_mojibake_audit_readable` AS
SELECT 'god2_game.item_registry' AS `資料表`,
       'name_zh_tw' AS `欄位`,
       COUNT(*) AS `可疑筆數`,
       CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END AS `狀態`
FROM `god2_game`.`item_registry`
WHERE HEX(COALESCE(`name_zh_tw`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
UNION ALL
SELECT 'god2_game.item_registry','description_zh_tw',COUNT(*),CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END
FROM `god2_game`.`item_registry`
WHERE HEX(COALESCE(`description_zh_tw`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
UNION ALL
SELECT 'god2_game.item_effects','effect_type',COUNT(*),CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END
FROM `god2_game`.`item_effects`
WHERE HEX(COALESCE(`effect_type`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
UNION ALL
SELECT 'god2_game.item_effects','effect_text_zh_tw',COUNT(*),CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END
FROM `god2_game`.`item_effects`
WHERE HEX(COALESCE(`effect_text_zh_tw`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
UNION ALL
SELECT 'god2.status_effects','Name',COUNT(*),CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END
FROM `god2`.`status_effects`
WHERE HEX(COALESCE(`Name`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
UNION ALL
SELECT 'god2_player.inventory_audit_ledger','OperationType/Source/CurrencyType/Result',COUNT(*),CASE WHEN COUNT(*)=0 THEN '通過' ELSE '需要清理' END
FROM `god2_player`.`inventory_audit_ledger`
WHERE HEX(COALESCE(`OperationType`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
   OR HEX(COALESCE(`Source`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
   OR HEX(COALESCE(`CurrencyType`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0'
   OR HEX(COALESCE(`Result`,'')) REGEXP 'E996B9|E9909F|E9908E|E996BA|E6BF9E|E5A891|E7BC81|E99781|E996BF|E7BBA0';
