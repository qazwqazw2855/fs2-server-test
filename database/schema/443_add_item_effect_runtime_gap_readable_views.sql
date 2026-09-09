CREATE OR REPLACE VIEW god2_research.vw_item_effect_runtime_gap_zh_tw AS
SELECT
    handler_name,
    expected_effect_type_zh_tw,
    promotion_status_zh_tw,
    COUNT(*) AS item_count,
    SUM(CASE WHEN formal_item_id IS NOT NULL THEN 1 ELSE 0 END) AS linked_formal_item_count,
    SUM(CASE WHEN runtime_match = 1 THEN 1 ELSE 0 END) AS runtime_matched_count,
    SUM(CASE WHEN runtime_match <> 1 OR runtime_match IS NULL THEN 1 ELSE 0 END) AS still_needs_work_count,
    MIN(action_zh_tw) AS action_zh_tw,
    '道具使用效果' AS game_function_zh_tw,
    UTC_TIMESTAMP() AS generated_at_utc
FROM god2_research.xjz_item_effect_runtime_reconciliation
GROUP BY handler_name, expected_effect_type_zh_tw, promotion_status_zh_tw;

CREATE OR REPLACE VIEW god2_research.vw_item_effect_runtime_gap_rows_zh_tw AS
SELECT
    client_item_id,
    formal_item_id,
    item_name_zh_tw,
    handler_name,
    expected_effect_type_zh_tw,
    expected_numeric_value,
    expected_duration_seconds,
    runtime_effect_index,
    runtime_numeric_value,
    runtime_duration_seconds,
    promotion_status_zh_tw,
    action_zh_tw,
    CASE
        WHEN promotion_status_zh_tw = '已實裝且數值一致' THEN '正式服務端已可使用。'
        WHEN promotion_status_zh_tw = '正式服務端缺效果' THEN '候選有可執行數值，可進一步評估是否安全寫入正式 item_effects。'
        WHEN promotion_status_zh_tw = '候選缺少可執行數值' THEN '缺數值或持續時間，不可直接寫正式服，需抓包或黑箱測試。'
        WHEN promotion_status_zh_tw = '正式道具未連結' THEN '找不到正式道具對應，需先補 item 對照。'
        ELSE '待人工確認。'
    END AS readable_next_step_zh_tw,
    source_pack_id,
    updated_at_utc
FROM god2_research.xjz_item_effect_runtime_reconciliation;

CREATE OR REPLACE VIEW god2_research.vw_research_packet_evidence_needed_zh_tw AS
SELECT '道具使用效果' AS game_function_zh_tw,
       'xjz_executable_item_effect_candidates / xjz_item_effect_runtime_reconciliation' AS research_table_name,
       (SELECT COUNT(*) FROM god2_research.xjz_executable_item_effect_candidates) AS evidence_rows,
       (SELECT COUNT(*) FROM god2_research.xjz_executable_item_effect_candidates WHERE formal_item_id IS NOT NULL) AS linked_formal_rows,
       (SELECT COUNT(*) FROM god2_research.xjz_item_effect_runtime_reconciliation WHERE runtime_match <> 1 OR runtime_match IS NULL) AS packet_or_blackbox_needed_rows,
       '121 筆已對照正式 runtime；其餘缺可執行數值或正式道具連結，不能硬塞正式服。' AS current_meaning_zh_tw
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
