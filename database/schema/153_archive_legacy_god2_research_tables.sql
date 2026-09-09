CREATE DATABASE IF NOT EXISTS `god2_research`
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

DROP PROCEDURE IF EXISTS `god2`.`archive_legacy_god2_research_table`;

CREATE PROCEDURE `god2`.`archive_legacy_god2_research_table`(
    IN source_table_name VARCHAR(128),
    IN target_table_name VARCHAR(128)
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA='god2'
          AND TABLE_NAME=source_table_name
          AND TABLE_TYPE='BASE TABLE'
    ) THEN
        SET @drop_target_sql = CONCAT('DROP TABLE IF EXISTS `god2_research`.`', REPLACE(target_table_name, '`', '``'), '`');
        PREPARE drop_target_statement FROM @drop_target_sql;
        EXECUTE drop_target_statement;
        DEALLOCATE PREPARE drop_target_statement;

        SET @create_target_sql = CONCAT(
            'CREATE TABLE `god2_research`.`',
            REPLACE(target_table_name, '`', '``'),
            '` LIKE `god2`.`',
            REPLACE(source_table_name, '`', '``'),
            '`'
        );
        PREPARE create_target_statement FROM @create_target_sql;
        EXECUTE create_target_statement;
        DEALLOCATE PREPARE create_target_statement;

        SET @insert_target_sql = CONCAT(
            'INSERT INTO `god2_research`.`',
            REPLACE(target_table_name, '`', '``'),
            '` SELECT * FROM `god2`.`',
            REPLACE(source_table_name, '`', '``'),
            '`'
        );
        PREPARE insert_target_statement FROM @insert_target_sql;
        EXECUTE insert_target_statement;
        DEALLOCATE PREPARE insert_target_statement;

        SET @drop_source_sql = CONCAT('DROP TABLE `god2`.`', REPLACE(source_table_name, '`', '``'), '`');
        PREPARE drop_source_statement FROM @drop_source_sql;
        EXECUTE drop_source_statement;
        DEALLOCATE PREPARE drop_source_statement;
    END IF;
END;

SET FOREIGN_KEY_CHECKS=0;

CALL `god2`.`archive_legacy_god2_research_table`('content_raw_records', 'content_raw_records');
CALL `god2`.`archive_legacy_god2_research_table`('content_staging_records', 'content_staging_records');
CALL `god2`.`archive_legacy_god2_research_table`('historical_gameplay_observations', 'historical_gameplay_observations');
CALL `god2`.`archive_legacy_god2_research_table`('merchant_inventory_candidates', 'merchant_inventory_candidates');
CALL `god2`.`archive_legacy_god2_research_table`('npc_coordinate_evidence', 'legacy_god2_npc_coordinate_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_action_grouping_evidence', 'packet_capture_action_grouping_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_action_pattern_evidence', 'packet_capture_action_pattern_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_content_application_gates', 'packet_capture_content_application_gates');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_decoded_stage_evidence', 'packet_capture_decoded_stage_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_evidence_sources', 'packet_capture_evidence_sources');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_frame_handler_correlation_evidence', 'packet_capture_frame_handler_correlation_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_frame_variant_catalog_evidence', 'packet_capture_frame_variant_catalog_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_handler_family_evidence', 'packet_capture_handler_family_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_lifecycle_field_candidate_evidence', 'packet_capture_lifecycle_field_candidate_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_protocol_applications', 'packet_capture_protocol_applications');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_semantic_families', 'packet_capture_semantic_families');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_sessions', 'packet_capture_sessions');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_stage_correlation_evidence', 'packet_capture_stage_correlation_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('packet_capture_supplemental_frame_catalog_evidence', 'packet_capture_supplemental_frame_catalog_evidence');
CALL `god2`.`archive_legacy_god2_research_table`('quest_objective_candidates', 'quest_objective_candidates');
CALL `god2`.`archive_legacy_god2_research_table`('verified_monster_observations', 'verified_monster_observations');
CALL `god2`.`archive_legacy_god2_research_table`('verified_skill_observations', 'verified_skill_observations');

SET FOREIGN_KEY_CHECKS=1;

DROP PROCEDURE IF EXISTS `god2`.`archive_legacy_god2_research_table`;
