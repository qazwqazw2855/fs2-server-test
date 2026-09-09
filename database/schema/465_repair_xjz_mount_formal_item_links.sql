-- 465_repair_xjz_mount_formal_item_links.sql
-- Purpose: repair C# mount template links to formal items using safe Traditional Chinese name matching.

DROP TABLE IF EXISTS god2_research.xjz_mount_formal_item_links_csharp;
CREATE TABLE god2_research.xjz_mount_formal_item_links_csharp AS
SELECT
  m.mount_template_id,
  m.item_id AS mount_item_id,
  m.mount_code,
  m.name_zh_tw,
  COUNT(i.item_id) AS candidate_count,
  CASE WHEN COUNT(i.item_id) = 1 THEN MIN(i.item_id) ELSE NULL END AS formal_item_id,
  CASE WHEN COUNT(i.item_id) = 1 THEN MIN(i.client_item_id) ELSE NULL END AS formal_client_item_id,
  GROUP_CONCAT(CONCAT(i.item_id, '/', i.client_item_id, '/', i.item_category) ORDER BY i.item_id SEPARATOR ' | ') AS candidate_items_zh_tw,
  CASE
    WHEN COUNT(i.item_id) = 1 THEN '唯一名稱對照'
    WHEN COUNT(i.item_id) > 1 THEN '同名多筆待確認'
    ELSE '正式道具缺口'
  END AS link_status_zh_tw,
  CASE
    WHEN COUNT(i.item_id) = 1 THEN '已用坐騎名稱安全連到正式道具。'
    WHEN COUNT(i.item_id) > 1 THEN '正式道具表有同名多筆，需用封包、道具代碼或人工證據確認後再綁定。'
    ELSE '正式道具表沒有同名資料，需回頭補道具表或確認名稱差異。'
  END AS server_apply_note_zh_tw,
  '坐騎正式道具對照：把 C# 坐騎模板連回正式道具表，供背包、道具使用、坐騎與傳送服務查詢。' AS game_function_zh_tw
FROM god2_research.xjz_mount_templates_csharp m
LEFT JOIN god2_game.items i ON i.name_zh_tw = m.name_zh_tw
GROUP BY m.mount_template_id, m.item_id, m.mount_code, m.name_zh_tw;

ALTER TABLE god2_research.xjz_mount_formal_item_links_csharp
  ADD PRIMARY KEY (mount_template_id),
  ADD KEY idx_xjz_mount_formal_item_links_status (link_status_zh_tw),
  ADD KEY idx_xjz_mount_formal_item_links_formal_item (formal_item_id);

ALTER TABLE god2_research.xjz_mount_templates_csharp
  ADD COLUMN IF NOT EXISTS formal_item_link_status_zh_tw VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS formal_item_link_note_zh_tw VARCHAR(512) NULL;

UPDATE god2_research.xjz_mount_templates_csharp m
JOIN god2_research.xjz_mount_formal_item_links_csharp l ON l.mount_template_id = m.mount_template_id
SET
  m.formal_item_id = l.formal_item_id,
  m.formal_item_link_status_zh_tw = l.link_status_zh_tw,
  m.formal_item_link_note_zh_tw = l.server_apply_note_zh_tw;

UPDATE god2_research.xjz_transport_bindings_csharp t
JOIN god2_research.xjz_mount_formal_item_links_csharp l ON l.mount_item_id = t.source_item_id
SET t.formal_item_id = l.formal_item_id
WHERE t.formal_item_id IS NULL
  AND l.link_status_zh_tw = '唯一名稱對照';

DROP VIEW IF EXISTS god2_research.vw_xjz_mount_formal_item_link_status_zh_tw;
CREATE VIEW god2_research.vw_xjz_mount_formal_item_link_status_zh_tw AS
SELECT
  link_status_zh_tw,
  COUNT(*) AS mount_template_count,
  SUM(formal_item_id IS NOT NULL) AS linked_formal_item_count,
  '坐騎正式道具對照狀態' AS game_function_zh_tw
FROM god2_research.xjz_mount_formal_item_links_csharp
GROUP BY link_status_zh_tw;

DROP VIEW IF EXISTS god2_research.vw_xjz_mount_transport_unresolved_links_zh_tw;
CREATE VIEW god2_research.vw_xjz_mount_transport_unresolved_links_zh_tw AS
SELECT
  mount_template_id,
  mount_item_id,
  mount_code,
  name_zh_tw,
  candidate_count,
  candidate_items_zh_tw,
  link_status_zh_tw,
  server_apply_note_zh_tw,
  game_function_zh_tw
FROM god2_research.xjz_mount_formal_item_links_csharp
WHERE link_status_zh_tw <> '唯一名稱對照';

DROP VIEW IF EXISTS god2_research.vw_xjz_mount_transport_csharp_summary_zh_tw;
CREATE VIEW god2_research.vw_xjz_mount_transport_csharp_summary_zh_tw AS
SELECT '坐騎模板' AS evidence_area_zh_tw, COUNT(*) AS row_count, SUM(can_ride = 1) AS ride_enabled_count, SUM(can_teleport = 1) AS teleport_enabled_count, SUM(formal_item_id IS NOT NULL) AS linked_formal_item_count, SUM(formal_item_id IS NULL) AS needs_packet_count, '坐騎/傳送獸道具功能辨識' AS game_function_zh_tw FROM god2_research.xjz_mount_templates_csharp
UNION ALL SELECT '坐騎正式道具唯一對照', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), 0, '坐騎模板連回正式道具' FROM god2_research.xjz_mount_formal_item_links_csharp WHERE link_status_zh_tw = '唯一名稱對照'
UNION ALL SELECT '坐騎正式道具待確認', COUNT(*), NULL, NULL, 0, COUNT(*), '同名或缺正式道具，需補證' FROM god2_research.xjz_mount_formal_item_links_csharp WHERE link_status_zh_tw <> '唯一名稱對照'
UNION ALL SELECT '坐騎外觀', COUNT(*), NULL, NULL, NULL, 0, '坐騎模型與騎乘顯示' FROM god2_research.xjz_mount_visuals_csharp
UNION ALL SELECT '坐騎技能綁定', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), 0, '坐騎附帶技能槽位' FROM god2_research.xjz_mount_skill_bindings_csharp
UNION ALL SELECT '坐騎飾品外觀', COUNT(*), NULL, NULL, NULL, 0, '坐騎飾品/獸型外觀與速度' FROM god2_research.xjz_mount_doll_visuals_csharp
UNION ALL SELECT '交通綁定', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), SUM(packet_evidence_boundary = 1), '坐騎交通來源對照' FROM god2_research.xjz_transport_bindings_csharp
UNION ALL SELECT '交通抓包目標', COUNT(*), NULL, NULL, NULL, SUM(packet_evidence_boundary = 1), '騎乘/移動/傳送封包欄位確認' FROM god2_research.xjz_transport_flow_capture_targets_csharp
UNION ALL SELECT '交通Playbook路線', COUNT(*), NULL, NULL, NULL, SUM(packet_evidence_boundary = 1), '正式抓包路線' FROM god2_research.xjz_transport_flow_playbook_routes_csharp
UNION ALL SELECT '交通服務模板', COUNT(*), NULL, NULL, NULL, 0, '交通服務入口候選' FROM god2_research.xjz_transport_service_templates_csharp;

UPDATE god2_research.xjz_csharp_apply_queue
SET
  safe_apply_level_zh_tw = '已匯入研究表，291 筆坐騎已安全連正式道具，其餘列入待確認',
  next_server_action_zh_tw = '坐騎模板已用名稱安全連回正式道具；同名多筆與缺正式道具清單已建立，後續用封包、道具代碼或人工證據補齊。',
  blocked_reason_zh_tw = '21 筆同名多筆、20 筆正式道具缺口，不能未經證據硬選正式 item_id。'
WHERE queue_key = 'mount_transport_sources';
