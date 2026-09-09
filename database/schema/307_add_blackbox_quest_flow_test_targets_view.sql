DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_quest_flow_test_targets_readable`;

CREATE VIEW `god2_game`.`vw_blackbox_quest_flow_test_targets_readable` AS
SELECT
    q.`quest_id` AS `任務編號`,
    q.`code` AS `服務端代碼`,
    q.`name_zh_tw` AS `任務名稱`,
    COALESCE(q.`quest_type`, '') AS `任務類型`,
    q.`required_level` AS `最低等級`,
    q.`maximum_level` AS `最高等級`,
    q.`start_npc_id` AS `接任務NPC編號`,
    COALESCE(start_npc.`name_zh_tw`, '') AS `接任務NPC名稱`,
    q.`end_npc_id` AS `回報NPC編號`,
    COALESCE(end_npc.`name_zh_tw`, '') AS `回報NPC名稱`,
    COALESCE(objective_summary.`目標數量`, 0) AS `目標數量`,
    COALESCE(objective_summary.`目標摘要`, '') AS `目標摘要`,
    COALESCE(reward_summary.`獎勵數量`, 0) AS `獎勵數量`,
    COALESCE(reward_summary.`獎勵摘要`, '') AS `獎勵摘要`,
    CASE WHEN q.`repeatable` = 1 THEN '可重複' ELSE '不可重複' END AS `重複設定`,
    COALESCE(q.`repeat_interval_seconds`, 0) AS `重複間隔秒數`,
    CASE
        WHEN q.`enabled` = 1
             AND COALESCE(objective_summary.`目標數量`, 0) > 0
             AND COALESCE(reward_summary.`獎勵數量`, 0) > 0
            THEN '第一優先：可測完整任務流程'
        WHEN q.`enabled` = 1
             AND COALESCE(objective_summary.`目標數量`, 0) > 0
            THEN '第二優先：可測接取與目標，缺獎勵證據'
        WHEN COALESCE(objective_summary.`目標數量`, 0) > 0
            THEN '第三優先：任務未啟用，先黑箱確認官方流程'
        ELSE '擱置：缺任務目標'
    END AS `黑箱測試優先級`,
    CASE
        WHEN COALESCE(reward_summary.`獎勵數量`, 0) = 0 THEN '缺獎勵資料；測試時記錄經驗、金錢、物品、選擇獎勵。'
        WHEN q.`enabled` <> 1 THEN '正式庫尚未啟用；測試時先對照官方是否存在此任務。'
        ELSE '測接任務、完成目標、回報NPC、獎勵入帳。'
    END AS `測試重點`,
    CASE WHEN q.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`quests` q
LEFT JOIN `god2_game`.`npcs` start_npc ON start_npc.`npc_id` = q.`start_npc_id`
LEFT JOIN `god2_game`.`npcs` end_npc ON end_npc.`npc_id` = q.`end_npc_id`
LEFT JOIN (
    SELECT
        qo.`quest_id`,
        COUNT(*) AS `目標數量`,
        GROUP_CONCAT(
            CONCAT(
                '#', qo.`objective_order`,
                ':', COALESCE(qo.`objective_type`, ''),
                ' ', COALESCE(qo.`target_name_cache`, ''),
                ' x', COALESCE(qo.`required_quantity`, 0)
            )
            ORDER BY qo.`objective_order`
            SEPARATOR '；'
        ) AS `目標摘要`
    FROM `god2_game`.`quest_objectives` qo
    GROUP BY qo.`quest_id`
) objective_summary ON objective_summary.`quest_id` = q.`quest_id`
LEFT JOIN (
    SELECT
        qr.`quest_id`,
        COUNT(*) AS `獎勵數量`,
        GROUP_CONCAT(
            CONCAT(
                '#', qr.`reward_order`,
                ':', COALESCE(qr.`reward_type`, ''),
                CASE
                    WHEN qr.`item_id` IS NOT NULL THEN CONCAT(' ', COALESCE(qr.`item_name_cache`, ''), ' x', COALESCE(qr.`quantity`, 0))
                    WHEN COALESCE(qr.`experience`, 0) > 0 THEN CONCAT(' 經驗 ', qr.`experience`)
                    WHEN COALESCE(qr.`currency`, 0) > 0 THEN CONCAT(' 金錢 ', qr.`currency`)
                    ELSE ''
                END
            )
            ORDER BY qr.`reward_order`
            SEPARATOR '；'
        ) AS `獎勵摘要`
    FROM `god2_game`.`quest_rewards` qr
    GROUP BY qr.`quest_id`
) reward_summary ON reward_summary.`quest_id` = q.`quest_id`;
