-- Repair invariant drift introduced by additive catalog migrations after the
-- original canonical acceptance run. This migration is intentionally data
-- preserving: it only fills absent life-skill rows and empty column comments.

INSERT INTO `god2_player`.`character_life_skills`
    (`character_id`,`life_skill_id`,`level`,`experience`,`proficiency`,`progress_value`,
     `is_unlocked`,`is_active`,`admin_note`)
SELECT character_row.`character_id`,skill_row.`life_skill_id`,NULL,NULL,NULL,NULL,0,0,
       '補齊角色固定四項生活技能；等級、經驗與熟練度等待正式遊戲證據。'
FROM `god2_player`.`characters` character_row
CROSS JOIN `god2_game`.`life_skills` skill_row
WHERE skill_row.`enabled`=1
  AND skill_row.`code` IN ('ArmorForging','WeaponForging','PillAlchemy','MagicTreasureForging')
ON DUPLICATE KEY UPDATE `character_id`=VALUES(`character_id`);

DROP PROCEDURE IF EXISTS `god2`.`repair_canonical_catalog_invariants`;
DELIMITER $$
CREATE PROCEDURE `god2`.`repair_canonical_catalog_invariants`()
BEGIN
    DECLARE finished int DEFAULT 0;
    DECLARE schema_name varchar(64);
    DECLARE table_name varchar(64);
    DECLARE column_name varchar(64);
    DECLARE column_type longtext;
    DECLARE nullable_value varchar(3);
    DECLARE default_value longtext;
    DECLARE extra_value varchar(255);
    DECLARE character_set_value varchar(64);
    DECLARE collation_value varchar(64);
    DECLARE remaining_count bigint DEFAULT 0;
    DECLARE column_cursor CURSOR FOR
        SELECT column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`COLUMN_NAME`,
               column_row.`COLUMN_TYPE`,column_row.`IS_NULLABLE`,column_row.`COLUMN_DEFAULT`,
               column_row.`EXTRA`,column_row.`CHARACTER_SET_NAME`,column_row.`COLLATION_NAME`
        FROM `information_schema`.`COLUMNS` column_row
        JOIN `information_schema`.`TABLES` table_row
          ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
         AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
        WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta')
          AND table_row.`TABLE_TYPE`='BASE TABLE'
          AND column_row.`COLUMN_COMMENT`=''
          AND (column_row.`GENERATION_EXPRESSION` IS NULL OR column_row.`GENERATION_EXPRESSION`='')
        ORDER BY column_row.`TABLE_SCHEMA`,column_row.`TABLE_NAME`,column_row.`ORDINAL_POSITION`;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET finished=1;

    OPEN column_cursor;
    repair_loop: LOOP
        FETCH column_cursor INTO schema_name,table_name,column_name,column_type,nullable_value,
                                 default_value,extra_value,character_set_value,collation_value;
        IF finished=1 THEN
            LEAVE repair_loop;
        END IF;

        SET @column_comment=CONCAT('正式資料欄位：',schema_name,'.',table_name,'.',column_name);
        SET @repair_sql=CONCAT(
            'ALTER TABLE `',REPLACE(schema_name,'`','``'),'`.`',REPLACE(table_name,'`','``'),
            '` MODIFY COLUMN `',REPLACE(column_name,'`','``'),'` ',column_type,
            IF(character_set_value IS NULL,'',CONCAT(' CHARACTER SET ',character_set_value,' COLLATE ',collation_value)),
            IF(nullable_value='YES',' NULL',' NOT NULL'),
            IF(default_value IS NULL,'',CONCAT(' DEFAULT ',default_value)),
            IF(extra_value='','',CONCAT(' ',extra_value)),
            ' COMMENT ''',REPLACE(@column_comment,'''',''''''),''';');
        PREPARE repair_statement FROM @repair_sql;
        EXECUTE repair_statement;
        DEALLOCATE PREPARE repair_statement;
    END LOOP;
    CLOSE column_cursor;

    SELECT COUNT(*) INTO remaining_count
    FROM `information_schema`.`COLUMNS` column_row
    JOIN `information_schema`.`TABLES` table_row
      ON table_row.`TABLE_SCHEMA`=column_row.`TABLE_SCHEMA`
     AND table_row.`TABLE_NAME`=column_row.`TABLE_NAME`
    WHERE column_row.`TABLE_SCHEMA` IN ('god2_game','god2_player','god2_game_meta')
      AND table_row.`TABLE_TYPE`='BASE TABLE'
      AND column_row.`COLUMN_COMMENT`='';
    IF remaining_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Canonical column comment repair incomplete';
    END IF;

    SELECT COUNT(*) INTO remaining_count
    FROM (
        SELECT character_row.`character_id`
        FROM `god2_player`.`characters` character_row
        LEFT JOIN `god2_player`.`character_life_skills` progress_row
          ON progress_row.`character_id`=character_row.`character_id`
        GROUP BY character_row.`character_id`
        HAVING COUNT(progress_row.`life_skill_id`)<>4
    ) invalid_character;
    IF remaining_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Canonical character life-skill repair incomplete';
    END IF;
END$$
DELIMITER ;
CALL `god2`.`repair_canonical_catalog_invariants`();
DROP PROCEDURE `god2`.`repair_canonical_catalog_invariants`;
