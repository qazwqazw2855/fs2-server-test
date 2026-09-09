CREATE OR REPLACE VIEW god2_research.vw_formal_gap_readiness_zh_tw AS
SELECT
    q.formal_table_name,
    q.game_function_zh_tw,
    COALESCE(c.server_correspondence_status_zh_tw, q.current_status_zh_tw) AS server_correspondence_status_zh_tw,
    COALESCE(c.runtime_usage_policy_zh_tw, q.evidence_policy_zh_tw) AS runtime_usage_policy_zh_tw,
    q.current_status_zh_tw,
    q.safe_to_apply_automatically,
    q.safe_to_drop_legacy_now,
    CASE q.formal_table_name
        WHEN 'crafting_recipes' THEN (SELECT COUNT(*) FROM god2_game.crafting_recipes)
        WHEN 'crafting_recipe_materials' THEN (SELECT COUNT(*) FROM god2_game.crafting_recipe_materials)
        WHEN 'crafting_recipe_outputs' THEN (SELECT COUNT(*) FROM god2_game.crafting_recipe_outputs)
        WHEN 'formations' THEN (SELECT COUNT(*) FROM god2_game.formations)
        WHEN 'monster_skills' THEN (SELECT COUNT(*) FROM god2_game.monster_skills)
        WHEN 'npc_dialogs' THEN (SELECT COUNT(*) FROM god2_game.npc_dialogs)
        WHEN 'pet_skill_learning_items' THEN (SELECT COUNT(*) FROM god2_game.pet_skill_learning_items)
        WHEN 'quest_rewards' THEN (SELECT COUNT(*) FROM god2_game.quest_rewards)
        ELSE NULL
    END AS formal_row_count,
    CASE q.formal_table_name
        WHEN 'crafting_recipes' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets)
        WHEN 'crafting_recipe_materials' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets)
        WHEN 'crafting_recipe_outputs' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets)
        WHEN 'formations' THEN 0
        WHEN 'monster_skills' THEN (SELECT COUNT(*) FROM god2_research.xjz_monster_skill_probe_targets)
        WHEN 'npc_dialogs' THEN (SELECT COUNT(*) FROM god2_research.xjz_npc_dialog_candidates)
        WHEN 'pet_skill_learning_items' THEN (SELECT COUNT(*) FROM god2_research.xjz_pet_skill_learning_item_candidates)
        WHEN 'quest_rewards' THEN (SELECT COUNT(*) FROM god2_research.xjz_quest_reward_capture_targets)
        ELSE NULL
    END AS evidence_candidate_count,
    CASE q.formal_table_name
        WHEN 'crafting_recipes' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets WHERE formal_item_id IS NOT NULL)
        WHEN 'crafting_recipe_materials' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets WHERE formal_item_id IS NOT NULL)
        WHEN 'crafting_recipe_outputs' THEN (SELECT COUNT(*) FROM god2_research.xjz_craft_material_capture_targets WHERE formal_item_id IS NOT NULL)
        WHEN 'formations' THEN 0
        WHEN 'monster_skills' THEN (SELECT COUNT(*) FROM god2_research.xjz_monster_skill_probe_targets WHERE formal_monster_id IS NOT NULL)
        WHEN 'npc_dialogs' THEN 0
        WHEN 'pet_skill_learning_items' THEN 0
        WHEN 'quest_rewards' THEN (SELECT COUNT(*) FROM god2_research.xjz_quest_reward_capture_targets WHERE formal_quest_id IS NOT NULL)
        ELSE NULL
    END AS linked_formal_evidence_count,
    CASE q.formal_table_name
        WHEN 'crafting_recipes' THEN '材料候選已整理；缺產物、材料數量、成功率與製作流程封包，不自動寫正式配方。'
        WHEN 'crafting_recipe_materials' THEN '材料候選已整理；缺配方歸屬與需求數量，不自動寫正式材料表。'
        WHEN 'crafting_recipe_outputs' THEN '材料候選已整理；缺正式產物與成功/失敗規則，不自動寫正式產物表。'
        WHEN 'formations' THEN '目前缺官方陣型證據；此功能下次可由我們設計，但本輪不造假資料。'
        WHEN 'monster_skills' THEN '怪物戰鬥技能已有 215 個補證目標；缺技能 ID、施放機率、條件與效果。'
        WHEN 'npc_dialogs' THEN 'NPC 對話文字候選已匯入；缺 NPC、任務流程、選項與下一段對話綁定。'
        WHEN 'pet_skill_learning_items' THEN '戰寵餵食學技能候選已整理；正式技能名稱仍有缺失或重複，需技能表去重後才能套用。'
        WHEN 'quest_rewards' THEN '任務獎勵抓包目標已整理；缺實際領獎樣本，不自動寫正式獎勵。'
        ELSE q.current_status_zh_tw
    END AS next_required_work_zh_tw,
    UTC_TIMESTAMP() AS generated_at_utc
FROM god2_research.static_gap_remediation_queue q
LEFT JOIN god2_research.formal_table_server_correspondence c
    ON c.schema_name = 'god2_game'
   AND c.table_name = q.formal_table_name
WHERE q.formal_table_name IN ('crafting_recipes','crafting_recipe_materials','crafting_recipe_outputs','formations','monster_skills','npc_dialogs','pet_skill_learning_items','quest_rewards');

CREATE OR REPLACE VIEW god2_research.vw_research_packet_evidence_needed_zh_tw AS
SELECT '道具使用效果' AS game_function_zh_tw,
       'xjz_executable_item_effect_candidates' AS research_table_name,
       COUNT(*) AS evidence_rows,
       SUM(CASE WHEN formal_item_id IS NOT NULL THEN 1 ELSE 0 END) AS linked_formal_rows,
       SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END) AS packet_or_blackbox_needed_rows,
       '已可套用 HP 類效果；其餘能力值、傳送、學技能等仍需封包或黑箱數值。' AS current_meaning_zh_tw
FROM god2_research.xjz_executable_item_effect_candidates
UNION ALL
SELECT 'NPC 商店販售格', 'xjz_npc_shop_slot_candidates', COUNT(*), SUM(CASE WHEN formal_item_id IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END), '商店格位候選已匯入；缺正式 NPC/商店流程綁定與價格確認。'
FROM god2_research.xjz_npc_shop_slot_candidates
UNION ALL
SELECT '怪物外觀與戰鬥模型', 'xjz_monster_visual_candidates', COUNT(*), COUNT(*), SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END), '外觀候選已整理；不改 HP/MP/能力值，避免把未證實數值寫進正式怪物。'
FROM god2_research.xjz_monster_visual_candidates
UNION ALL
SELECT '怪物戰鬥技能', 'xjz_monster_skill_probe_targets', COUNT(*), SUM(CASE WHEN formal_monster_id IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END), '215 個怪物技能補證目標已建立；正式技能表仍等待技能、機率與條件證據。'
FROM god2_research.xjz_monster_skill_probe_targets
UNION ALL
SELECT '製作材料與生活技能', 'xjz_craft_material_capture_targets', COUNT(*), SUM(CASE WHEN formal_item_id IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END), '954 個材料補證目標已建立；正式配方仍需產物、數量與成功率證據。'
FROM god2_research.xjz_craft_material_capture_targets
UNION ALL
SELECT '任務完成獎勵', 'xjz_quest_reward_capture_targets', COUNT(*), SUM(CASE WHEN formal_quest_id IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN needs_packet_evidence <> 0 THEN 1 ELSE 0 END), '418 個任務獎勵抓包目標已建立；正式獎勵等待實際領獎樣本。'
FROM god2_research.xjz_quest_reward_capture_targets
UNION ALL
SELECT '戰寵餵食學技能', 'xjz_pet_skill_learning_item_candidates', COUNT(*), 0, COUNT(*), '239 個候選已整理；技能名稱缺失或重複，需技能表去重後才能正式套用。'
FROM god2_research.xjz_pet_skill_learning_item_candidates
UNION ALL
SELECT 'NPC 對話與任務文字', 'xjz_npc_dialog_candidates', COUNT(*), 0, COUNT(*), '27856 筆對話候選已匯入；缺 NPC、任務流程、選項與下一段對話綁定。'
FROM god2_research.xjz_npc_dialog_candidates;

