-- 464_tighten_xjz_skill_effect_text_quality_scan.sql
-- Purpose: tighten the C# skill-effect text quality scan so valid Traditional Chinese words are not reported as simplified residue.

DROP VIEW IF EXISTS god2_research.vw_xjz_skill_effect_text_quality_zh_tw;
CREATE VIEW god2_research.vw_xjz_skill_effect_text_quality_zh_tw AS
SELECT '技能效果規則' AS table_zh_tw, SUM(COALESCE(description_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') AS suspicious_text_count FROM god2_research.xjz_skill_effect_rules_csharp
UNION ALL SELECT 'BUFF效果規則', SUM(COALESCE(description_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') FROM god2_research.xjz_buff_effect_rules_csharp
UNION ALL SELECT '角色/神仙技能模板', SUM(name_zh_tw REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]' OR COALESCE(class_text_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]' OR COALESCE(skill_family_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]' OR COALESCE(target_scope_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') FROM god2_research.xjz_character_god_skill_templates_csharp
UNION ALL SELECT '戰鬥寵技能模板', SUM(option_group_zh_tw REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]' OR content_zh_tw REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') FROM god2_research.xjz_combat_pet_skill_templates_csharp
UNION ALL SELECT '怪物技能探針', SUM(monster_name_zh_tw REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') FROM god2_research.xjz_monster_skill_probe_templates_csharp
UNION ALL SELECT '技能施放抓包目標', SUM(skill_name_zh_tw REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]' OR COALESCE(request_hint_zh_tw, '') REGEXP '[绁诲嫏鎺湜鍦锛击术两围圆]') FROM god2_research.xjz_skill_runtime_capture_targets_csharp;
