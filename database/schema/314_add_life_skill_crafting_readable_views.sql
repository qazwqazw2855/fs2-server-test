DROP VIEW IF EXISTS `god2_game`.`vw_crafting_recipes_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_crafting_recipe_test_targets_readable`;
DROP VIEW IF EXISTS `god2_player`.`vw_blackbox_life_skill_observations_readable`;

CREATE VIEW `god2_game`.`vw_crafting_recipes_readable` AS
SELECT
    recipe.`recipe_id` AS `配方ID`,
    recipe.`name_zh_tw` AS `配方名稱`,
    recipe.`recipe_category` AS `配方分類`,
    recipe.`life_skill_id` AS `生活技能ID`,
    COALESCE(life_skill.`name_zh_tw`, '') AS `生活技能`,
    recipe.`required_life_skill_level` AS `需求生活技能等級`,
    recipe.`required_character_level` AS `需求角色等級`,
    recipe.`base_success_rate` AS `基礎成功率`,
    recipe.`base_quality_rate` AS `基礎品質率`,
    recipe.`currency_cost` AS `金錢成本`,
    recipe.`craft_duration_ms` AS `製作時間毫秒`,
    COALESCE(material_summary.`材料數量`, 0) AS `材料種類數`,
    COALESCE(material_summary.`材料摘要`, '') AS `材料摘要`,
    COALESCE(output_summary.`成品數量`, 0) AS `成品種類數`,
    COALESCE(output_summary.`成品摘要`, '') AS `成品摘要`,
    CASE WHEN recipe.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`crafting_recipes` recipe
LEFT JOIN `god2_game`.`life_skills` life_skill
    ON life_skill.`life_skill_id` = recipe.`life_skill_id`
LEFT JOIN (
    SELECT
        material.`recipe_id`,
        COUNT(*) AS `材料數量`,
        GROUP_CONCAT(
            CONCAT(
                '#', material.`material_order`,
                ':', COALESCE(material.`item_name_cache`, ''),
                ' x', material.`required_quantity`,
                CASE WHEN material.`consume_on_failure` = 1 THEN '(失敗消耗)' ELSE '(失敗不消耗)' END
            )
            ORDER BY material.`material_order`
            SEPARATOR '；'
        ) AS `材料摘要`
    FROM `god2_game`.`crafting_recipe_materials` material
    WHERE material.`enabled` = 1
    GROUP BY material.`recipe_id`
) material_summary ON material_summary.`recipe_id` = recipe.`recipe_id`
LEFT JOIN (
    SELECT
        output.`recipe_id`,
        COUNT(*) AS `成品數量`,
        GROUP_CONCAT(
            CONCAT(
                '#', output.`output_order`,
                ':', COALESCE(output.`item_name_cache`, ''),
                ' x', output.`minimum_quantity`, '-', output.`maximum_quantity`,
                ' 機率', output.`output_probability`,
                ' 品質', output.`quality_tier`
            )
            ORDER BY output.`output_order`
            SEPARATOR '；'
        ) AS `成品摘要`
    FROM `god2_game`.`crafting_recipe_outputs` output
    WHERE output.`enabled` = 1
    GROUP BY output.`recipe_id`
) output_summary ON output_summary.`recipe_id` = recipe.`recipe_id`;

CREATE VIEW `god2_game`.`vw_blackbox_crafting_recipe_test_targets_readable` AS
SELECT
    recipe_view.`配方ID`,
    recipe_view.`配方名稱`,
    recipe_view.`配方分類`,
    recipe_view.`生活技能`,
    recipe_view.`需求生活技能等級`,
    recipe_view.`需求角色等級`,
    recipe_view.`基礎成功率`,
    recipe_view.`基礎品質率`,
    recipe_view.`金錢成本`,
    recipe_view.`材料種類數`,
    recipe_view.`材料摘要`,
    recipe_view.`成品種類數`,
    recipe_view.`成品摘要`,
    CASE
        WHEN recipe_view.`服務端狀態` = '已啟用'
             AND recipe_view.`材料種類數` > 0
             AND recipe_view.`成品種類數` > 0
            THEN '第一優先：測製作成功、材料消耗、成品產出'
        WHEN recipe_view.`服務端狀態` = '已啟用'
            THEN '第二優先：配方啟用但材料或成品缺資料'
        ELSE '擱置：配方未啟用'
    END AS `黑箱測試優先級`,
    '測配方學習、材料檢查、製作成功率、失敗材料消耗、成品數量與品質。' AS `測試重點`,
    recipe_view.`服務端狀態`
FROM `god2_game`.`vw_crafting_recipes_readable` recipe_view;

CREATE VIEW `god2_player`.`vw_blackbox_life_skill_observations_readable` AS
SELECT
    character_skill.`character_id` AS `角色ID`,
    COALESCE(character_info.`name`, '') AS `角色名稱`,
    character_skill.`life_skill_id` AS `生活技能ID`,
    COALESCE(life_skill.`name_zh_tw`, '') AS `生活技能`,
    character_skill.`level` AS `等級`,
    character_skill.`experience` AS `經驗`,
    character_skill.`proficiency` AS `熟練度`,
    character_skill.`progress_value` AS `目前進度`,
    CASE WHEN character_skill.`is_unlocked` = 1 THEN '已解鎖' ELSE '未解鎖' END AS `解鎖狀態`,
    CASE WHEN character_skill.`is_active` = 1 THEN '啟用中' ELSE '未啟用' END AS `啟用狀態`,
    character_skill.`success_rate_bonus` AS `成功率加成`,
    character_skill.`quality_rate_bonus` AS `品質加成`,
    character_skill.`critical_craft_rate_bonus` AS `特殊製作率加成`,
    character_skill.`daily_use_count` AS `今日使用次數`,
    character_skill.`daily_use_limit` AS `每日限制`,
    character_skill.`last_used_at_utc` AS `最後使用時間UTC`,
    CASE
        WHEN character_skill.`is_unlocked` = 1 AND character_skill.`is_active` = 1 THEN '第一優先：測生活技能使用與熟練度成長'
        WHEN character_skill.`is_unlocked` = 1 THEN '第二優先：測啟用生活技能'
        ELSE '擱置：生活技能未解鎖'
    END AS `黑箱測試優先級`,
    '測生活技能解鎖、啟用、製作後經驗/熟練度/每日次數變化。' AS `測試重點`
FROM `god2_player`.`character_life_skills` character_skill
LEFT JOIN `god2_player`.`characters` character_info
    ON character_info.`character_id` = character_skill.`character_id`
LEFT JOIN `god2_game`.`life_skills` life_skill
    ON life_skill.`life_skill_id` = character_skill.`life_skill_id`;
