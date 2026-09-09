-- 第 300 波：第一批怪物掉落服務端設計值。
-- 遊戲功能：怪物死亡後給玩家可觀察的低風險材料掉落，供開服測試擊殺、掉落、背包入帳與掉率校準。
-- 來源政策：沒有官方逐怪掉落證據時，先用服務端設計值；後續黑箱測試或官方證據可覆寫。

INSERT INTO `god2_game`.`monster_drops` (
    `drop_id`,
    `monster_id`,
    `monster_name_cache`,
    `item_id`,
    `item_name_cache`,
    `minimum_quantity`,
    `maximum_quantity`,
    `drop_rate`,
    `drop_rate_unit`,
    `drop_group`,
    `drop_source_zh_tw`,
    `drop_policy_zh_tw`,
    `is_guaranteed`,
    `enabled`,
    `created_at_utc`,
    `updated_at_utc`)
SELECT
    300000 + monster_row.`monster_id` AS `drop_id`,
    monster_row.`monster_id`,
    monster_row.`name_zh_tw`,
    CASE MOD(monster_row.`monster_id`, 8)
        WHEN 0 THEN 67632252
        WHEN 1 THEN 131025371
        WHEN 2 THEN 732479429
        WHEN 3 THEN 848042268
        WHEN 4 THEN 1796148254
        WHEN 5 THEN 1856301026
        WHEN 6 THEN 1984980765
        ELSE 2092591819
    END AS `item_id`,
    CASE MOD(monster_row.`monster_id`, 8)
        WHEN 0 THEN '玄鐵'
        WHEN 1 THEN '鳳羽'
        WHEN 2 THEN '水銀'
        WHEN 3 THEN '碧水珠'
        WHEN 4 THEN '破損鎧甲'
        WHEN 5 THEN '龍麟'
        WHEN 6 THEN '血玉髓'
        ELSE '玄蠶寒絲'
    END AS `item_name_cache`,
    1 AS `minimum_quantity`,
    CASE
        WHEN COALESCE(monster_row.`level`, 1) <= 30 THEN 1
        WHEN COALESCE(monster_row.`level`, 1) <= 60 THEN 2
        ELSE 3
    END AS `maximum_quantity`,
    CASE
        WHEN COALESCE(monster_row.`level`, 1) <= 30 THEN 0.220000
        WHEN COALESCE(monster_row.`level`, 1) <= 60 THEN 0.200000
        ELSE 0.180000
    END AS `drop_rate`,
    'probability' AS `drop_rate_unit`,
    'first_wave_material_test' AS `drop_group`,
    '服務端設計值' AS `drop_source_zh_tw`,
    '第一批怪物材料掉落；用於開服黑箱測試擊殺、掉落與背包入帳，待官方證據或實測校準後可覆寫。' AS `drop_policy_zh_tw`,
    0 AS `is_guaranteed`,
    1 AS `enabled`,
    UTC_TIMESTAMP(6) AS `created_at_utc`,
    UTC_TIMESTAMP(6) AS `updated_at_utc`
FROM `god2_game`.`monsters` monster_row
WHERE monster_row.`enabled` = 1
  AND EXISTS (
      SELECT 1
      FROM `god2_game`.`monster_spawns` spawn_row
      WHERE spawn_row.`monster_id` = monster_row.`monster_id`
        AND spawn_row.`enabled` = 1
        AND spawn_row.`spawn_source_zh_tw` = '服務端設計值')
  AND EXISTS (
      SELECT 1
      FROM `god2_game`.`items` item_row
      WHERE item_row.`item_id` = CASE MOD(monster_row.`monster_id`, 8)
          WHEN 0 THEN 67632252
          WHEN 1 THEN 131025371
          WHEN 2 THEN 732479429
          WHEN 3 THEN 848042268
          WHEN 4 THEN 1796148254
          WHEN 5 THEN 1856301026
          WHEN 6 THEN 1984980765
          ELSE 2092591819
      END
        AND item_row.`enabled` = 1
        AND item_row.`item_category` = '材料'
        AND item_row.`equippable` = 0
        AND item_row.`usable` = 0)
ON DUPLICATE KEY UPDATE
    `monster_name_cache` = VALUES(`monster_name_cache`),
    `item_id` = VALUES(`item_id`),
    `item_name_cache` = VALUES(`item_name_cache`),
    `minimum_quantity` = VALUES(`minimum_quantity`),
    `maximum_quantity` = VALUES(`maximum_quantity`),
    `drop_rate` = VALUES(`drop_rate`),
    `drop_rate_unit` = VALUES(`drop_rate_unit`),
    `drop_group` = VALUES(`drop_group`),
    `drop_source_zh_tw` = VALUES(`drop_source_zh_tw`),
    `drop_policy_zh_tw` = VALUES(`drop_policy_zh_tw`),
    `is_guaranteed` = VALUES(`is_guaranteed`),
    `enabled` = VALUES(`enabled`),
    `updated_at_utc` = UTC_TIMESTAMP(6);

CREATE OR REPLACE VIEW `god2_game`.`vw_monster_first_wave_drop_runtime_readiness_readable` AS
SELECT
    COUNT(*) AS `第一批已啟用掉落數`,
    COUNT(DISTINCT drop_row.`monster_id`) AS `覆蓋怪物數`,
    COUNT(DISTINCT drop_row.`item_id`) AS `使用材料種數`,
    SUM(CASE WHEN drop_row.`drop_source_zh_tw` = '服務端設計值' THEN 1 ELSE 0 END) AS `服務端設計值筆數`,
    SUM(CASE WHEN item_row.`item_category` = '材料' AND item_row.`equippable` = 0 AND item_row.`usable` = 0 THEN 1 ELSE 0 END) AS `低風險材料筆數`,
    MIN(drop_row.`drop_rate`) AS `最低掉率`,
    MAX(drop_row.`drop_rate`) AS `最高掉率`
FROM `god2_game`.`monster_drops` drop_row
JOIN `god2_game`.`items` item_row
  ON item_row.`item_id` = drop_row.`item_id`
WHERE drop_row.`enabled` = 1
  AND drop_row.`drop_group` = 'first_wave_material_test';
