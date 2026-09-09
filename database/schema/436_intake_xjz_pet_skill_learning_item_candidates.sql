-- Migration 436: publish battle-pet feed skill learning candidates.
-- Function: battle-pet fourth skill learning items. This creates a readable worklist
-- and intentionally does not promote ambiguous mappings into god2_game.pet_skill_learning_items.

CREATE TABLE IF NOT EXISTS god2_research.xjz_pet_skill_learning_item_candidates (
  item_id BIGINT NOT NULL COMMENT '正式道具 ID',
  client_item_id INT NULL COMMENT '官方客戶端道具 ID',
  item_name_zh_tw VARCHAR(200) NOT NULL COMMENT '餵食道具名稱',
  skill_name_zh_tw VARCHAR(200) NOT NULL COMMENT '道具描述宣告可習得技能名稱',
  pet_generation_scope_zh_tw VARCHAR(80) NOT NULL COMMENT '可學習戰寵世代',
  required_level INT NULL COMMENT '道具描述宣告需求等級',
  formal_skill_match_count INT NOT NULL COMMENT '正式技能名稱符合筆數',
  candidate_skill_ids VARCHAR(500) NULL COMMENT '候選正式技能 ID 清單',
  formal_mapping_status_zh_tw VARCHAR(80) NOT NULL COMMENT '正式技能對應狀態',
  promote_to_runtime_allowed TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否允許自動寫入正式 pet_skill_learning_items',
  evidence_policy_zh_tw VARCHAR(255) NOT NULL COMMENT '證據使用政策',
  created_at_utc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6) COMMENT '建立 UTC 時間',
  PRIMARY KEY (item_id),
  KEY ix_xjz_pet_skill_learning_client_item (client_item_id),
  KEY ix_xjz_pet_skill_learning_status (formal_mapping_status_zh_tw),
  CONSTRAINT ck_xjz_pet_skill_learning_promote CHECK (promote_to_runtime_allowed IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='XJZ/正式道具描述戰寵餵食學技能候選';

TRUNCATE TABLE god2_research.xjz_pet_skill_learning_item_candidates;

INSERT INTO god2_research.xjz_pet_skill_learning_item_candidates
  (item_id, client_item_id, item_name_zh_tw, skill_name_zh_tw, pet_generation_scope_zh_tw,
   required_level, formal_skill_match_count, candidate_skill_ids, formal_mapping_status_zh_tw,
   promote_to_runtime_allowed, evidence_policy_zh_tw)
WITH pet_feed AS (
  SELECT item.item_id,
         item.client_item_id,
         item.name_zh_tw AS item_name_zh_tw,
         item.description_zh_tw,
         CASE WHEN TRIM(SUBSTRING_INDEX(item.description_zh_tw, CHAR(10), -1)) LIKE '%無法學習%'
              THEN TRIM(SUBSTRING_INDEX(SUBSTRING_INDEX(item.description_zh_tw, CHAR(10), -2), CHAR(10), 1))
              ELSE TRIM(SUBSTRING_INDEX(item.description_zh_tw, CHAR(10), -1)) END AS skill_name_zh_tw,
         CASE WHEN item.description_zh_tw LIKE '%一代戰寵無法學習%' THEN '僅二代戰寵' ELSE '戰寵' END AS pet_generation_scope_zh_tw,
         CASE
           WHEN item.description_zh_tw LIKE '%120級%' THEN 120
           WHEN item.description_zh_tw LIKE '%100級%' THEN 100
           WHEN item.description_zh_tw LIKE '%60級%' THEN 60
           ELSE NULL
         END AS required_level
  FROM god2_game.items item
  WHERE item.description_zh_tw LIKE '%戰寵%餵食%可習得%'
    AND item.description_zh_tw NOT LIKE '%降低戰寵等級%'
), skill_matches AS (
  SELECT name_zh_tw,
         COUNT(*) AS match_count,
         GROUP_CONCAT(skill_id ORDER BY skill_id SEPARATOR ',') AS candidate_skill_ids
  FROM god2_game.skills
  GROUP BY name_zh_tw
)
SELECT pet_feed.item_id,
       pet_feed.client_item_id,
       pet_feed.item_name_zh_tw,
       pet_feed.skill_name_zh_tw,
       pet_feed.pet_generation_scope_zh_tw,
       pet_feed.required_level,
       COALESCE(skill_matches.match_count, 0) AS formal_skill_match_count,
       skill_matches.candidate_skill_ids,
       CASE
         WHEN skill_matches.match_count = 1 THEN '可唯一對應但需人工確認啟用'
         WHEN skill_matches.match_count > 1 THEN '技能名稱重複，需拆分正式技能'
         ELSE '正式技能名稱未對齊'
       END AS formal_mapping_status_zh_tw,
       0 AS promote_to_runtime_allowed,
       '正式表需要唯一 item_id 到 skill_id；目前只建立候選清單，避免重名或缺名造成錯學技能。' AS evidence_policy_zh_tw
FROM pet_feed
LEFT JOIN skill_matches ON skill_matches.name_zh_tw = pet_feed.skill_name_zh_tw;

CREATE OR REPLACE VIEW god2_research.vw_xjz_pet_skill_learning_item_candidates_readable AS
SELECT
  item_id,
  client_item_id,
  item_name_zh_tw,
  '戰寵餵食學技能' AS game_function_zh_tw,
  skill_name_zh_tw,
  pet_generation_scope_zh_tw,
  required_level,
  formal_skill_match_count,
  candidate_skill_ids,
  formal_mapping_status_zh_tw,
  CASE promote_to_runtime_allowed WHEN 1 THEN '可套用正式 runtime' ELSE '暫不套正式 runtime' END AS runtime_apply_status_zh_tw,
  evidence_policy_zh_tw
FROM god2_research.xjz_pet_skill_learning_item_candidates;

UPDATE god2_research.static_gap_remediation_queue
SET priority_rank = 45,
    remediation_category_zh_tw = '候選對照',
    game_function_zh_tw = '戰寵餵食學技能',
    evidence_policy_zh_tw = '正式道具描述可證明餵食學技能語意，但正式 runtime 需要唯一 skill_id；重名或缺名不可自動啟用。',
    remediation_action_zh_tw = '已建立 xjz_pet_skill_learning_item_candidates 共 239 筆候選；先修正式技能名稱/分類重複後，再將唯一對應寫入 pet_skill_learning_items。',
    dependency_check_zh_tw = 'pet_skill_learning_items 需要 item_registry.item_id 與 skills.skill_id 外鍵；目前未自動插入正式表，避免技能重名造成錯誤學習。',
    current_status_zh_tw = '已建立候選清單，正式對應待技能去重',
    safe_to_apply_automatically = 0,
    safe_to_drop_legacy_now = 0,
    reviewed_at_utc = UTC_TIMESTAMP()
WHERE formal_table_name = 'pet_skill_learning_items';
