#!/usr/bin/env bash
set -euo pipefail

container="${GOD2_DB_CONTAINER:-god2-runtime-db-test}"
command -v sudo >/dev/null
command -v docker >/dev/null

cat <<'SQL' | sudo docker exec -i "$container" sh -lc '
password="$(cat "$MARIADB_ROOT_PASSWORD_FILE")"
exec mariadb -uroot -p"$password" --batch --raw
'
-- Match Migration 381: aggregate profiles by legacy QuestId, then join by the
-- formal quest_id. This query only reads data and never changes the database.
SELECT
  COUNT(*) AS formal_quests,
  SUM(q.enabled = 1) AS formal_enabled,
  SUM(p.QuestId IS NOT NULL) AS with_legacy_profile,
  SUM(COALESCE(p.profile_enabled, 0) = 1) AS with_enabled_profile,
  SUM(q.enabled = 1 AND COALESCE(p.profile_enabled, 0) = 1)
      AS enabled_with_enabled_profile,
  SUM(q.enabled = 1 AND COALESCE(p.profile_enabled, 0) = 0)
      AS enabled_without_enabled_profile,
  SUM(q.enabled = 0 AND COALESCE(p.profile_enabled, 0) = 1)
      AS disabled_with_enabled_profile,
  SUM(q.completion_text_zh_tw IS NOT NULL) AS formal_completion_text,
  SUM(p.reward_text IS NOT NULL) AS profile_reward_text,
  SUM(q.completion_text_zh_tw IS NOT NULL AND p.reward_text IS NULL)
      AS completion_text_without_profile_reward,
  SUM(q.completion_text_zh_tw IS NULL AND p.reward_text IS NOT NULL)
      AS profile_reward_without_completion_text
FROM god2_game.quests AS q
LEFT JOIN (
  SELECT QuestId,
         MAX(ProductionProfileEnabled) AS profile_enabled,
         MAX(NULLIF(RewardTextZhTw, '')) AS reward_text
  FROM god2.quest_content_profiles
  WHERE QuestId IS NOT NULL
  GROUP BY QuestId
) AS p ON p.QuestId = q.quest_id;

-- Evidence labels are archived separately. Their counts do not certify
-- objective execution, NPC binding, or reward delivery.
SELECT e.evidence_status, COUNT(*) AS formal_quest_rows
FROM god2_game.quests AS q
LEFT JOIN god2_research.quest_catalog_evidence AS e
  ON e.quest_id = q.quest_id
GROUP BY e.evidence_status
ORDER BY formal_quest_rows DESC, e.evidence_status;
SQL
