CREATE SCHEMA IF NOT EXISTS `god2_research`;

SET @has_formal_glossary := (
    SELECT COUNT(*) FROM information_schema.TABLES
    WHERE `TABLE_SCHEMA`=DATABASE()
      AND `TABLE_NAME`='content_localization_glossary'
      AND `TABLE_TYPE`='BASE TABLE'
);

SET @create_research_glossary_sql := IF(
    @has_formal_glossary > 0,
    'CREATE TABLE IF NOT EXISTS `god2_research`.`content_localization_glossary` LIKE `content_localization_glossary`',
    'SELECT 1');
PREPARE create_research_glossary_stmt FROM @create_research_glossary_sql;
EXECUTE create_research_glossary_stmt;
DEALLOCATE PREPARE create_research_glossary_stmt;

SET @copy_research_glossary_sql := IF(
    @has_formal_glossary > 0,
    'INSERT INTO `god2_research`.`content_localization_glossary`
        (`GlossaryVersion`,`SimplifiedText`,`TraditionalText`,`Category`,`Source`,`SourceIdentity`,`Confidence`,`VerifiedBy`,`Notes`)
     SELECT `GlossaryVersion`,`SimplifiedText`,`TraditionalText`,`Category`,`Source`,`SourceIdentity`,`Confidence`,`VerifiedBy`,`Notes`
     FROM `content_localization_glossary`
     ON DUPLICATE KEY UPDATE
        `TraditionalText`=VALUES(`TraditionalText`),
        `Source`=VALUES(`Source`),
        `SourceIdentity`=VALUES(`SourceIdentity`),
        `Confidence`=VALUES(`Confidence`),
        `VerifiedBy`=VALUES(`VerifiedBy`),
        `Notes`=VALUES(`Notes`)',
    'SELECT 1');
PREPARE copy_research_glossary_stmt FROM @copy_research_glossary_sql;
EXECUTE copy_research_glossary_stmt;
DEALLOCATE PREPARE copy_research_glossary_stmt;

DROP TABLE IF EXISTS `content_localization_glossary`;
