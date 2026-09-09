-- 422_link_xjz_item_effect_candidates_to_formal_items.sql
-- Purpose: link XJZ client-recovered executable item effect candidates to formal server item roots.
-- Boundary: candidate item_id is the client item id; formal runtime must use god2_game.items.item_id.

ALTER TABLE god2_research.xjz_executable_item_effect_candidates
  ADD COLUMN IF NOT EXISTS formal_item_id INT NULL AFTER item_id,
  ADD COLUMN IF NOT EXISTS formal_item_code VARCHAR(128) NULL AFTER formal_item_id,
  ADD COLUMN IF NOT EXISTS formal_item_name_zh_tw VARCHAR(128) NULL AFTER formal_item_code,
  ADD KEY IF NOT EXISTS idx_xjz_item_effect_candidate_formal_item (formal_item_id);

UPDATE god2_research.xjz_executable_item_effect_candidates candidate
LEFT JOIN god2_game.items formal_item
  ON formal_item.client_item_id=candidate.item_id
SET candidate.formal_item_id=formal_item.item_id,
    candidate.formal_item_code=formal_item.code,
    candidate.formal_item_name_zh_tw=formal_item.name_zh_tw
WHERE candidate.source_pack_id='XJZ-csharp-evidence-capture-pack-20260818';

DELETE FROM god2_research.xjz_executable_item_effect_candidate_summary
WHERE source_pack_id='XJZ-csharp-evidence-capture-pack-20260818';
INSERT INTO god2_research.xjz_executable_item_effect_candidate_summary
(handler_name,candidate_count,linked_formal_item_count,packet_boundary_count,game_function_zh_tw,server_action_zh_tw,source_pack_id)
SELECT candidate.handler_name,
       COUNT(*) AS candidate_count,
       SUM(CASE WHEN candidate.formal_item_id IS NULL THEN 0 ELSE 1 END) AS linked_formal_item_count,
       SUM(candidate.packet_evidence_boundary) AS packet_boundary_count,
       CASE candidate.handler_name
           WHEN 'restore_hp' THEN '補血道具'
           WHEN 'restore_mp' THEN '補魔道具'
           WHEN 'restore_hp_mp' THEN '補血補魔道具'
           WHEN 'clear_status' THEN '解除異常狀態道具'
           WHEN 'apply_stat_buff' THEN '能力提升藥品'
           WHEN 'learn_skill' THEN '學習技能道具'
           WHEN 'teleport' THEN '傳送道具'
           WHEN 'feed_pet_or_mount' THEN '餵食寵物或坐騎道具'
           ELSE '其他道具效果'
       END AS game_function_zh_tw,
       CASE candidate.handler_name
           WHEN 'restore_hp' THEN '可比對 ItemUseEffectEngine 補 HP handler 後導入。'
           WHEN 'restore_mp' THEN '可比對 ItemUseEffectEngine 補 MP handler 後導入。'
           WHEN 'restore_hp_mp' THEN '可比對 ItemUseEffectEngine 補 HP/MP handler 後導入。'
           WHEN 'clear_status' THEN '可比對狀態效果系統後導入解除異常。'
           WHEN 'apply_stat_buff' THEN '可比對 Buff/回合持續時間規則後導入。'
           WHEN 'learn_skill' THEN '需比對技能書與角色技能狀態後導入。'
           WHEN 'teleport' THEN '需補正式地圖與傳送封包驗證後導入。'
           WHEN 'feed_pet_or_mount' THEN '需比對寵物/坐騎忠誠與餵食規則後導入。'
           ELSE '保留候選，待服務端 handler 對照。'
       END AS server_action_zh_tw,
       'XJZ-csharp-evidence-capture-pack-20260818'
FROM god2_research.xjz_executable_item_effect_candidates candidate
WHERE candidate.source_pack_id='XJZ-csharp-evidence-capture-pack-20260818'
GROUP BY candidate.handler_name;

UPDATE god2_research.xjz_csharp_apply_queue
SET safe_apply_level_zh_tw='已建立 1664 筆可執行候選，其中 1552 筆已對到正式道具主表',
    next_server_action_zh_tw='從已連結 formal_item_id 的補血、補魔、補血補魔、解除異常與能力提升藥品開始分批導入 ItemUseEffectEngine；傳送、學技能、餵食寵物或坐騎另需服務端狀態與封包驗證。'
WHERE queue_key='item_effect_rules'
  AND source_pack_id='XJZ-csharp-evidence-capture-pack-20260818';
