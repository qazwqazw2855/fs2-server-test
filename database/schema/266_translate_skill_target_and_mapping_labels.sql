-- 266_translate_skill_target_and_mapping_labels.sql
-- Game functions: skill target side labels and official skill metadata name-mapping method.

UPDATE `god2_game`.`skills`
SET `target_side` = CASE `target_side`
        WHEN 'Self' THEN '自己'
        WHEN 'Enemy' THEN '敵方'
        WHEN 'Ally' THEN '友方'
        ELSE `target_side`
    END;

UPDATE `god2_game`.`skill_client_metadata_mappings`
SET `mapping_method` = CASE `mapping_method`
        WHEN 'ExactNameIgnoringSpacing' THEN '精確名稱比對（忽略空白）'
        ELSE `mapping_method`
    END;
