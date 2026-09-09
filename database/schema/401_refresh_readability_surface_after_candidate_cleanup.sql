UPDATE `god2_research`.`database_readability_surface` surface
SET surface.`contains_tool_only_fields` = CASE
        WHEN EXISTS (
            SELECT 1
            FROM information_schema.`COLUMNS` column_row
            WHERE column_row.`TABLE_SCHEMA` = surface.`schema_name`
              AND column_row.`TABLE_NAME` = surface.`table_name`
              AND (
                    column_row.`COLUMN_NAME` LIKE '%Hash%'
                 OR column_row.`COLUMN_NAME` LIKE '%Payload%'
                 OR column_row.`COLUMN_NAME` LIKE '%Raw%'
                 OR column_row.`COLUMN_NAME` LIKE '%Json%'
                 OR column_row.`COLUMN_NAME` LIKE '%Binary%'
                 OR column_row.`COLUMN_NAME` LIKE '%Blob%'
                 OR column_row.`COLUMN_NAME` LIKE '%Hex%'
                 OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
                 OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
                 OR column_row.`COLUMN_NAME` = 'RunId'
              )
        ) THEN 1
        ELSE 0
    END,
    surface.`cleanup_policy_zh_tw` = CASE
        WHEN surface.`safe_to_browse` = 1
         AND NOT EXISTS (
            SELECT 1
            FROM information_schema.`COLUMNS` column_row
            WHERE column_row.`TABLE_SCHEMA` = surface.`schema_name`
              AND column_row.`TABLE_NAME` = surface.`table_name`
              AND (
                    column_row.`COLUMN_NAME` LIKE '%Hash%'
                 OR column_row.`COLUMN_NAME` LIKE '%Payload%'
                 OR column_row.`COLUMN_NAME` LIKE '%Raw%'
                 OR column_row.`COLUMN_NAME` LIKE '%Json%'
                 OR column_row.`COLUMN_NAME` LIKE '%Binary%'
                 OR column_row.`COLUMN_NAME` LIKE '%Blob%'
                 OR column_row.`COLUMN_NAME` LIKE '%Hex%'
                 OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
                 OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
                 OR column_row.`COLUMN_NAME` = 'RunId'
              )
        ) THEN '可人工瀏覽；目前未發現工具追蹤欄位。'
        ELSE surface.`cleanup_policy_zh_tw`
    END
WHERE surface.`schema_name` IN ('god2_game','god2_research');

TRUNCATE TABLE `god2_research`.`database_field_readability_audit`;

INSERT INTO `god2_research`.`database_field_readability_audit`
    (`schema_name`,`table_name`,`field_name`,`field_type`,`readability_status_zh_tw`,`cleanup_policy_zh_tw`,`game_function_zh_tw`)
SELECT
    column_row.`TABLE_SCHEMA`,
    column_row.`TABLE_NAME`,
    column_row.`COLUMN_NAME`,
    column_row.`DATA_TYPE`,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '可人工瀏覽'
        WHEN surface.`safe_to_browse` = 1 THEN '可人工瀏覽但需注意欄位'
        ELSE '工具欄位'
    END,
    CASE
        WHEN surface.`safe_to_browse` = 1 AND surface.`contains_tool_only_fields` = 0 THEN '保留為可讀欄位。'
        WHEN surface.`safe_to_browse` = 1 THEN '保留欄位但需在候選表中提供繁中語意，不直接暴露原始內容。'
        ELSE '保留給工具追證據；若要給人工看，必須另建可讀候選表。'
    END,
    surface.`game_function_zh_tw`
FROM information_schema.`COLUMNS` column_row
JOIN `god2_research`.`database_readability_surface` surface
  ON surface.`schema_name` = column_row.`TABLE_SCHEMA`
 AND surface.`table_name` = column_row.`TABLE_NAME`
WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_research')
  AND (
        column_row.`COLUMN_NAME` LIKE '%Hash%'
     OR column_row.`COLUMN_NAME` LIKE '%Payload%'
     OR column_row.`COLUMN_NAME` LIKE '%Raw%'
     OR column_row.`COLUMN_NAME` LIKE '%Json%'
     OR column_row.`COLUMN_NAME` LIKE '%Binary%'
     OR column_row.`COLUMN_NAME` LIKE '%Blob%'
     OR column_row.`COLUMN_NAME` LIKE '%Hex%'
     OR column_row.`COLUMN_NAME` LIKE '%ProfileId%'
     OR column_row.`COLUMN_NAME` LIKE '%RelationshipId%'
     OR column_row.`COLUMN_NAME` = 'RunId'
  );
