-- 451_intake_xjz_csharp_zip_evidence_digest.sql
-- Purpose: digest the C# restored XJZ evidence capture pack into a server-readable evidence map.
-- Source pack: XJZ-csharp-evidence-capture-pack-20260818.zip
-- Important: this migration records evidence/application state only; it does not promote unverified formulas or fake gameplay data.

CREATE TABLE IF NOT EXISTS god2_research.xjz_csharp_zip_evidence_digest (
  evidence_key VARCHAR(96) NOT NULL,
  source_pack_id VARCHAR(128) NOT NULL,
  game_function_zh_tw VARCHAR(128) NOT NULL,
  evidence_area_zh_tw VARCHAR(128) NOT NULL,
  evidence_count INT NULL,
  evidence_unit_zh_tw VARCHAR(64) NOT NULL,
  current_project_status_zh_tw VARCHAR(128) NOT NULL,
  safe_apply_level_zh_tw VARCHAR(128) NOT NULL,
  next_server_action_zh_tw VARCHAR(512) NOT NULL,
  blocked_reason_zh_tw VARCHAR(512) NULL,
  related_project_object_zh_tw VARCHAR(256) NULL,
  updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (evidence_key),
  KEY ix_xjz_csharp_zip_digest_status (current_project_status_zh_tw),
  KEY ix_xjz_csharp_zip_digest_level (safe_apply_level_zh_tw)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='C#還原證據包消化索引：標示已套用、候選、待抓包、待黑箱測試的服務端功能證據。';

INSERT INTO god2_research.xjz_csharp_zip_evidence_digest
(evidence_key, source_pack_id, game_function_zh_tw, evidence_area_zh_tw, evidence_count, evidence_unit_zh_tw, current_project_status_zh_tw, safe_apply_level_zh_tw, next_server_action_zh_tw, blocked_reason_zh_tw, related_project_object_zh_tw)
VALUES
('service_ports', 'XJZ-csharp-evidence-capture-pack-20260818', '服務端服務入口對照', 'C# ServicePortManifest', 316, '服務入口', '已套用到研究表與服務對照', '可安全保存為正式證據索引', '保留作為服務端缺口盤點依據；新增服務時先對照此清單避免漏接入口。', NULL, 'migration 420, xjz_csharp_service_port_bindings'),
('runtime_contracts', 'XJZ-csharp-evidence-capture-pack-20260818', '背包、道具、任務、戰寵、坐騎、角色運行服務', 'C# RuntimeServiceContracts', 18, '合約群組', '已建立戰鬥與運行功能審核', '只能作為服務端功能邊界，不能直接當公式', '逐項比對現有 runtime service；缺服務先補介面與資料流，再用封包或黑箱補數值。', '合約只證明客戶端呼叫邊界，不證明完整伺服器規則。', 'migration 428'),
('combat_runtime_contracts', 'XJZ-csharp-evidence-capture-pack-20260818', '戰鬥行動、狀態、五行、獎勵與戰鬥事件', 'C# CombatRuntimeContracts/Command/Award', 19, '功能審核項', '已審核並發布研究儀表', '可用於補服務流程；數值仍需實測', '已確認防禦物防與魔防倍率為1.5；其餘公式只在有實測證據時更新。', 'C# 合約不得硬編成官方公式。', 'migration 428, CompatibilityBattleFormula'),
('item_effect_candidates', 'XJZ-csharp-evidence-capture-pack-20260818', '道具使用效果', 'C# ItemEffectContracts + recovered item effect candidates', 1664, '候選效果', '已匯入、已連結、部分提升到正式 runtime', '有數值且已連結者可提升；缺數值者待抓包或黑箱', '剩餘233項需補證據：226項缺可執行數值、7項正式道具未連結。', '客戶端候選不一定含恢復量、傳送點、狀態強度等可執行參數。', 'migration 421-424, 443'),
('npc_shop_slots', 'XJZ-csharp-evidence-capture-pack-20260818', 'NPC 商店販售格', 'Recovered NPC shop slot candidates', 2265, '販售格候選', '已匯入研究表，尚未全部提升正式商店', '已連結正式道具者可分批提升；武器防具缺連結先保留', '先依正式道具連結與商店清單補正式販售資料，未連結項留待道具對照。', '部分武器、防具候選未能連到正式 item。', 'migration 425'),
('monster_visuals', 'XJZ-csharp-evidence-capture-pack-20260818', '怪物外觀與戰鬥模型', 'Recovered monster visual candidates', 215, '怪物外觀候選', '已匯入研究表並暫連正式怪物', '可補外觀模型；不可補HP/MP/能力值', '可同步正式怪物外觀欄位；怪物HP/MP與AI留到設計或實測，不從外觀證據推導。', '外觀證據不等於數值證據。', 'migration 426'),
('quest_reward_targets', 'XJZ-csharp-evidence-capture-pack-20260818', '任務完成獎勵', 'Quest reward capture targets', 418, '任務目標', '已匯入研究表與缺口儀表，正式獎勵仍為空', '只可建立抓包目標；不可偽造獎勵', '照任務ID建立實測清單，抓到獎勵封包後再提升正式 quest_rewards。', '目前只有目標，不含確定獎勵內容。', 'migration 427, 439, 443'),
('npc_dialog_candidates', 'XJZ-csharp-evidence-capture-pack-20260818', 'NPC 對話與任務文字', 'Recovered NPC dialog candidates', 27856, '對話候選', '已匯入研究表並完成繁體清理', '仍需NPC與流程綁定後才能提升正式表', '下一步比對 NPC 身分、地圖、任務流程，建立可安全提升的正式 npc_dialogs 子集。', '候選文字存在來源碎片與分支不明，不能整批直接正式化。', 'migration 441, 444-449'),
('monster_skill_probe_targets', 'XJZ-csharp-evidence-capture-pack-20260818', '怪物戰鬥技能', 'Monster skill probe targets', 215, '抓包目標', '已匯入研究表，正式 monster_skills 仍為空', '只可作抓包/黑箱測試目標', '依怪物逐一測試技能ID、施放率、條件後再寫正式 monster_skills。', '缺技能ID、機率與觸發條件。', 'migration 434, 435'),
('pet_skill_learning_items', 'XJZ-csharp-evidence-capture-pack-20260818', '戰寵餵食學技能', 'Pet skill learning item candidates', 239, '候選道具', '已匯入研究表，正式表仍為空', '需技能去重與道具連結後分批提升', '先做技能名稱去重與正式 skill link；唯一連結後再提升 pet_skill_learning_items。', '目前213項缺精準技能連結、26項模糊。', 'migration 436, 437'),
('craft_material_targets', 'XJZ-csharp-evidence-capture-pack-20260818', '製作材料與生活技能', 'Craft material capture targets', 954, '材料目標', '已匯入研究表，正式製作表仍為空', '只可作配方抓包目標', '需要輸出物、材料數量、成功率、技能需求後才能建立正式 recipe。', '材料目標不等於完整配方。', 'migration 438'),
('evidence_source_index', 'XJZ-csharp-evidence-capture-pack-20260818', '客戶端證據來源索引', 'evidence_source + client_evidence_file', 256, '來源檔', '本次建立消化索引，尚未逐檔匯入來源表', '可安全匯入研究來源索引', '下一批可把256個來源檔建成 research source 表，連到所有候選資料。', NULL, 'evidence-source-index-v0.md'),
('skill_effect_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '角色技能、神仙技能、戰寵技能效果', 'Skill effect docs/contracts', 767, '技能抓包目標', '尚未完整套用', '需分成已知被動、主動效果、狀態效果三類', '先補被動技能如仙道冥思的正式說明與資料欄位，再排主動技能黑箱測試。', '客戶端還原可能缺伺服端倍率與持續回合。', 'skill runtime capture targets'),
('god_system_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '神仙取得、試煉、相關任務', 'God acquisition/trial/quest evidence', 111, '取得規則候選', '尚未完整套用', '可先匯入研究候選；正式取得規則需任務/道具/戰鬥關聯確認', '建立神仙取得規則候選表，連到任務與NPC後分批正式化。', '部分取得條件需要任務流程或戰鬥驗證。', 'god acquisition capture targets'),
('warehouse_storage_rules', 'XJZ-csharp-evidence-capture-pack-20260818', '倉庫與物品存放規則', 'Warehouse kinds + item storage rules', 17287, '存放規則模板', '尚未套用', '可先匯入研究規則，正式限制需比對背包/倉庫服務', '優先補服務端倉庫種類與可存放規則，避免正式測試時道具分類錯誤。', '需要確認現有背包欄位與倉庫封包。', 'warehouse/item storage templates'),
('client_message_templates', 'XJZ-csharp-evidence-capture-pack-20260818', '客戶端訊息文字與系統提示', 'Client message templates', 3136, '訊息模板', '尚未套用', '可安全匯入為繁體訊息候選', '匯入後用於服務端錯誤碼、系統提示、互動提示對照。', '需避免覆蓋已由正式封包驗證的文字。', 'message templates'),
('forbidden_words', 'XJZ-csharp-evidence-capture-pack-20260818', '聊天與命名禁詞', 'Forbidden words', 12921, '禁詞', '尚未套用', '可匯入但需繁體化與誤殺檢查', '先放研究表，後續建立正式聊天/命名過濾服務開關。', '禁詞可能含簡體、拼音、誤判詞，需要人工/測試分層。', 'forbidden word table'),
('social_market_contracts', 'XJZ-csharp-evidence-capture-pack-20260818', '交易、拍賣、擺攤、結婚、結拜、師徒', 'C# SocialMarketContracts', 18, '社交市場合約', '尚未完整套用', '可補服務端功能骨架；資料需實測', '先盤點正式服務端已有 trade/auction/stall/marriage/mentor 表與封包，再補缺口。', '合約只證明功能存在，不證明經濟規則。', 'SocialMarketContracts.cs'),
('reward_pool_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '獎勵池、福袋、轉盤獎勵', 'Reward pool/fudai/roulette evidence', 378, '獎勵池候選', '尚未套用', '只能研究匯入；正式獎勵需來源與機率驗證', '建立獎勵池候選表，先不套正式掉落或商城。', '缺機率、觸發來源與正式掉落關聯。', 'reward pool docs'),
('combat_pet_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '戰寵設計、資質、技能池、品質星級', 'Combat pet design/options/pools', 4235, '戰寵運行抓包目標', '尚未完整套用', '研究匯入優先；正式數值不可直接推導', '可建立戰寵候選資料表與測試目標；正式成長、技能池需證據或設計決策。', '大量為客戶端顯示/選項，不等於伺服器隨機規則。', 'combat pet docs'),
('mount_transport_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '坐騎與傳送探索', 'Mount exploration capture targets', 332, '探索目標', '尚未完整套用', '可作抓包目標與傳送候選', '連到地圖座標與傳送道具後，補正式 mount/teleport runtime。', '缺正式座標與條件驗證。', 'mount exploration capture targets'),
('achievement_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '成就模板與收藏規則', 'Achievement templates/collection rules', 551, '成就與收藏規則', '尚未套用', '可研究匯入；正式觸發需事件對照', '建立成就候選資料，再把擊殺、任務、收集、等級等觸發事件逐一接 runtime。', '缺服務端事件觸發與完成條件驗證。', 'achievement docs'),
('activity_mission_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '活動任務與條件', 'Activity missions/requirements', 913, '活動任務條件', '尚未套用', '只可研究匯入；正式活動需排程與獎勵驗證', '先建立活動任務候選，等排程、NPC、獎勵證據齊後再正式化。', '活動時間與獎勵來源不完整。', 'activity mission docs'),
('rebirth_level_sources', 'XJZ-csharp-evidence-capture-pack-20260818', '轉生與等級經驗需求', 'Rebirth probes + level exp requirements', 24, '規則/探針', '尚未套用', '需黑箱或正式資料補齊', '先記錄逆推探針，不改等級曲線；等實測或完整表出現再套用。', 'level exp requirements 目前為0，不能偽造。', 'rebirth/level docs')
ON DUPLICATE KEY UPDATE
  source_pack_id = VALUES(source_pack_id),
  game_function_zh_tw = VALUES(game_function_zh_tw),
  evidence_area_zh_tw = VALUES(evidence_area_zh_tw),
  evidence_count = VALUES(evidence_count),
  evidence_unit_zh_tw = VALUES(evidence_unit_zh_tw),
  current_project_status_zh_tw = VALUES(current_project_status_zh_tw),
  safe_apply_level_zh_tw = VALUES(safe_apply_level_zh_tw),
  next_server_action_zh_tw = VALUES(next_server_action_zh_tw),
  blocked_reason_zh_tw = VALUES(blocked_reason_zh_tw),
  related_project_object_zh_tw = VALUES(related_project_object_zh_tw);

CREATE OR REPLACE VIEW god2_research.vw_xjz_csharp_zip_evidence_digest_zh_tw AS
SELECT
  evidence_key AS `證據鍵`,
  game_function_zh_tw AS `遊戲功能`,
  evidence_area_zh_tw AS `證據範圍`,
  evidence_count AS `證據數量`,
  evidence_unit_zh_tw AS `數量單位`,
  current_project_status_zh_tw AS `目前狀態`,
  safe_apply_level_zh_tw AS `可套用層級`,
  next_server_action_zh_tw AS `下一步服務端動作`,
  blocked_reason_zh_tw AS `暫時不能直接實裝原因`,
  related_project_object_zh_tw AS `已關聯專案物件`
FROM god2_research.xjz_csharp_zip_evidence_digest;

CREATE OR REPLACE VIEW god2_research.vw_xjz_csharp_zip_evidence_digest_summary_zh_tw AS
SELECT
  current_project_status_zh_tw AS `目前狀態`,
  COUNT(*) AS `功能群組數`,
  COALESCE(SUM(evidence_count), 0) AS `證據數量合計`
FROM god2_research.xjz_csharp_zip_evidence_digest
GROUP BY current_project_status_zh_tw;

INSERT INTO god2_research.xjz_csharp_apply_queue
(queue_key, game_function_zh_tw, client_recovered_evidence_zh_tw, safe_apply_level_zh_tw, next_server_action_zh_tw, blocked_reason_zh_tw, source_pack_id)
SELECT evidence_key, game_function_zh_tw, evidence_area_zh_tw, safe_apply_level_zh_tw, next_server_action_zh_tw, blocked_reason_zh_tw, source_pack_id
FROM god2_research.xjz_csharp_zip_evidence_digest d
WHERE d.current_project_status_zh_tw NOT LIKE '已%'
ON DUPLICATE KEY UPDATE
  game_function_zh_tw = VALUES(game_function_zh_tw),
  client_recovered_evidence_zh_tw = VALUES(client_recovered_evidence_zh_tw),
  safe_apply_level_zh_tw = VALUES(safe_apply_level_zh_tw),
  next_server_action_zh_tw = VALUES(next_server_action_zh_tw),
  blocked_reason_zh_tw = VALUES(blocked_reason_zh_tw),
  source_pack_id = VALUES(source_pack_id);
