UPDATE `god2_game`.`monsters` formal_monster
JOIN `god2_game`.`vw_monster_hpmp_reconciliation` reconciliation
  ON reconciliation.`正式怪物ID` = formal_monster.`monster_id`
SET
    formal_monster.`combat_stat_source_zh_tw` = 'HPMP reconciliation 證據',
    formal_monster.`combat_stat_policy_zh_tw` = '同繁中名稱唯一命中或代碼名稱同時命中；只提升 HP/MP 來源標記，不宣稱官方實測。',
    formal_monster.`updated_at_utc` = UTC_TIMESTAMP(6)
WHERE reconciliation.`同步判定` IN ('可人工確認：名稱唯一命中','可人工確認：代碼與名稱同時命中')
  AND reconciliation.`最大生命` IS NOT NULL
  AND reconciliation.`最大法力` IS NOT NULL
  AND formal_monster.`max_hp` = reconciliation.`最大生命`
  AND formal_monster.`max_mp` = reconciliation.`最大法力`;
