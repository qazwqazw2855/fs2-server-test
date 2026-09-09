CREATE OR REPLACE VIEW `god2_game`.`vw_monster_hpmp_reconciliation` AS
SELECT
    '名稱命中' AS `比對方式`,
    CASE
        WHEN legacy_name_count.`NameCount` = 1 AND game_name_count.`NameCount` = 1 THEN '可人工確認：名稱唯一命中'
        WHEN legacy_name_count.`NameCount` > 1 THEN '需人工確認：舊正式怪物名稱重複'
        WHEN game_name_count.`NameCount` > 1 THEN '禁止自動同步：正式怪物名稱重複'
        ELSE '需人工確認'
    END AS `同步判定`,
    legacy_row.`Id` AS `舊怪物ID`,
    COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`) AS `舊怪物名稱`,
    legacy_row.`Code` AS `舊怪物代碼`,
    game_row.`monster_id` AS `正式怪物ID`,
    game_row.`name_zh_tw` AS `正式怪物名稱`,
    game_row.`code` AS `正式怪物代碼`,
    game_row.`level` AS `等級`,
    game_row.`max_hp` AS `最大生命`,
    game_row.`max_mp` AS `最大法力`,
    game_row.`physical_attack` AS `物理攻擊`,
    game_row.`physical_defense` AS `物理防禦`,
    game_row.`magic_attack` AS `法術攻擊`,
    game_row.`magic_defense` AS `法術防禦`,
    legacy_name_count.`NameCount` AS `舊名稱筆數`,
    game_name_count.`NameCount` AS `正式名稱筆數`,
    '同繁中名稱且正式表已有 HP/MP；不直接寫回舊表，需人工確認後提升。' AS `證據說明`
FROM `god2`.`monsters` legacy_row
JOIN `god2_game`.`monsters` game_row
    ON game_row.`name_zh_tw` = COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`)
JOIN (
    SELECT COALESCE(`NameZhTw`, `Name`) AS `MonsterName`, COUNT(*) AS `NameCount`
    FROM `god2`.`monsters`
    GROUP BY COALESCE(`NameZhTw`, `Name`)
) legacy_name_count
    ON legacy_name_count.`MonsterName` = COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`)
JOIN (
    SELECT `name_zh_tw` AS `MonsterName`, COUNT(*) AS `NameCount`
    FROM `god2_game`.`monsters`
    WHERE `max_hp` IS NOT NULL OR `max_mp` IS NOT NULL
    GROUP BY `name_zh_tw`
) game_name_count
    ON game_name_count.`MonsterName` = game_row.`name_zh_tw`
WHERE game_row.`max_hp` IS NOT NULL OR game_row.`max_mp` IS NOT NULL
UNION ALL
SELECT
    '代碼命中' AS `比對方式`,
    CASE
        WHEN COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`) = game_row.`name_zh_tw`
            THEN '可人工確認：代碼與名稱同時命中'
        ELSE '禁止自動同步：代碼命中但名稱衝突'
    END AS `同步判定`,
    legacy_row.`Id` AS `舊怪物ID`,
    COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`) AS `舊怪物名稱`,
    legacy_row.`Code` AS `舊怪物代碼`,
    game_row.`monster_id` AS `正式怪物ID`,
    game_row.`name_zh_tw` AS `正式怪物名稱`,
    game_row.`code` AS `正式怪物代碼`,
    game_row.`level` AS `等級`,
    game_row.`max_hp` AS `最大生命`,
    game_row.`max_mp` AS `最大法力`,
    game_row.`physical_attack` AS `物理攻擊`,
    game_row.`physical_defense` AS `物理防禦`,
    game_row.`magic_attack` AS `法術攻擊`,
    game_row.`magic_defense` AS `法術防禦`,
    legacy_code_count.`CodeCount` AS `舊名稱筆數`,
    game_code_count.`CodeCount` AS `正式名稱筆數`,
    CASE
        WHEN COALESCE(legacy_row.`NameZhTw`, legacy_row.`Name`) = game_row.`name_zh_tw`
            THEN '同代碼且同名稱；可列入提升候選。'
        ELSE '同代碼但怪物名稱不同，代表舊表代碼或名稱錯位；此列只能做衝突證據，不可自動寫回 HP/MP。'
    END AS `證據說明`
FROM `god2`.`monsters` legacy_row
JOIN `god2_game`.`monsters` game_row
    ON game_row.`code` = legacy_row.`Code`
JOIN (
    SELECT `Code`, COUNT(*) AS `CodeCount`
    FROM `god2`.`monsters`
    GROUP BY `Code`
) legacy_code_count
    ON legacy_code_count.`Code` = legacy_row.`Code`
JOIN (
    SELECT `code`, COUNT(*) AS `CodeCount`
    FROM `god2_game`.`monsters`
    WHERE `max_hp` IS NOT NULL OR `max_mp` IS NOT NULL
    GROUP BY `code`
) game_code_count
    ON game_code_count.`code` = game_row.`code`
WHERE game_row.`max_hp` IS NOT NULL OR game_row.`max_mp` IS NOT NULL;
