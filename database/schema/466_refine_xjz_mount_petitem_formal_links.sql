-- 466_refine_xjz_mount_petitem_formal_links.sql
-- Purpose: refine unresolved C# mount item links when exactly one formal PetItem exists for the mount name.

ALTER TABLE god2_research.xjz_mount_formal_item_links_csharp
  MODIFY link_status_zh_tw VARCHAR(64) NOT NULL,
  MODIFY server_apply_note_zh_tw VARCHAR(512) NOT NULL;

UPDATE god2_research.xjz_mount_formal_item_links_csharp l
JOIN (
  SELECT
    m.mount_template_id,
    MIN(i.item_id) AS formal_item_id,
    MIN(i.client_item_id) AS formal_client_item_id,
    GROUP_CONCAT(CONCAT(i.item_id, '/', i.client_item_id, '/', i.item_category) ORDER BY i.item_id SEPARATOR ' | ') AS candidate_items_zh_tw
  FROM god2_research.xjz_mount_templates_csharp m
  JOIN god2_game.items i ON i.name_zh_tw = m.name_zh_tw AND i.item_category = 'PetItem'
  WHERE m.formal_item_id IS NULL
  GROUP BY m.mount_template_id
  HAVING COUNT(i.item_id) = 1
) p ON p.mount_template_id = l.mount_template_id
SET
  l.formal_item_id = p.formal_item_id,
  l.formal_client_item_id = p.formal_client_item_id,
  l.candidate_count = 1,
  l.candidate_items_zh_tw = p.candidate_items_zh_tw,
  l.link_status_zh_tw = '唯一寵物道具對照',
  l.server_apply_note_zh_tw = '原始同名資料含非坐騎項目，但正式 PetItem 只有一筆，已安全連到坐騎道具。';

UPDATE god2_research.xjz_mount_templates_csharp m
JOIN god2_research.xjz_mount_formal_item_links_csharp l ON l.mount_template_id = m.mount_template_id
SET
  m.formal_item_id = l.formal_item_id,
  m.formal_item_link_status_zh_tw = l.link_status_zh_tw,
  m.formal_item_link_note_zh_tw = l.server_apply_note_zh_tw
WHERE l.link_status_zh_tw = '唯一寵物道具對照';

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
WHERE formal_item_id IS NULL;

DROP VIEW IF EXISTS god2_research.vw_xjz_mount_transport_csharp_summary_zh_tw;
CREATE VIEW god2_research.vw_xjz_mount_transport_csharp_summary_zh_tw AS
SELECT '坐騎模板' AS evidence_area_zh_tw, COUNT(*) AS row_count, SUM(can_ride = 1) AS ride_enabled_count, SUM(can_teleport = 1) AS teleport_enabled_count, SUM(formal_item_id IS NOT NULL) AS linked_formal_item_count, SUM(formal_item_id IS NULL) AS needs_packet_count, '坐騎/傳送獸道具功能辨識' AS game_function_zh_tw FROM god2_research.xjz_mount_templates_csharp
UNION ALL SELECT '坐騎正式道具安全對照', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), 0, '坐騎模板連回正式道具' FROM god2_research.xjz_mount_formal_item_links_csharp WHERE formal_item_id IS NOT NULL
UNION ALL SELECT '坐騎正式道具待確認', COUNT(*), NULL, NULL, 0, COUNT(*), '同名多筆或缺正式道具，需補證' FROM god2_research.xjz_mount_formal_item_links_csharp WHERE formal_item_id IS NULL
UNION ALL SELECT '坐騎外觀', COUNT(*), NULL, NULL, NULL, 0, '坐騎模型與騎乘顯示' FROM god2_research.xjz_mount_visuals_csharp
UNION ALL SELECT '坐騎技能綁定', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), 0, '坐騎附帶技能槽位' FROM god2_research.xjz_mount_skill_bindings_csharp
UNION ALL SELECT '坐騎飾品外觀', COUNT(*), NULL, NULL, NULL, 0, '坐騎飾品/獸型外觀與速度' FROM god2_research.xjz_mount_doll_visuals_csharp
UNION ALL SELECT '交通綁定', COUNT(*), NULL, NULL, SUM(formal_item_id IS NOT NULL), SUM(source_item_id IS NOT NULL AND formal_item_id IS NULL), '坐騎交通來源對照' FROM god2_research.xjz_transport_bindings_csharp
UNION ALL SELECT '交通抓包目標', COUNT(*), NULL, NULL, NULL, SUM(packet_evidence_boundary = 1), '騎乘/移動/傳送封包欄位確認' FROM god2_research.xjz_transport_flow_capture_targets_csharp
UNION ALL SELECT '交通Playbook路線', COUNT(*), NULL, NULL, NULL, SUM(packet_evidence_boundary = 1), '正式抓包路線' FROM god2_research.xjz_transport_flow_playbook_routes_csharp
UNION ALL SELECT '交通服務模板', COUNT(*), NULL, NULL, NULL, 0, '交通服務入口候選' FROM god2_research.xjz_transport_service_templates_csharp;

UPDATE god2_research.xjz_csharp_apply_queue
SET
  safe_apply_level_zh_tw = '已匯入研究表，293 筆坐騎已安全連正式道具，其餘列入待確認',
  next_server_action_zh_tw = '坐騎模板已用名稱與唯一 PetItem 規則安全連回正式道具；同名多個 PetItem 與缺正式道具清單已建立。',
  blocked_reason_zh_tw = '19 筆同名多個 PetItem、20 筆正式道具缺口，不能未經證據硬選正式 item_id。'
WHERE queue_key = 'mount_transport_sources';
