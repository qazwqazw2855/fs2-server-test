START TRANSACTION;

UPDATE god2_game.quests q
LEFT JOIN god2_game.npcs start_npc ON start_npc.npc_id = q.start_npc_id
LEFT JOIN god2_game.npcs end_npc ON end_npc.npc_id = q.end_npc_id
LEFT JOIN (
    SELECT quest_id, COUNT(*) AS objective_count
    FROM god2_game.quest_objectives
    GROUP BY quest_id
) objective_summary ON objective_summary.quest_id = q.quest_id
SET
    q.description_zh_tw = CONCAT(
        '功能對照：',
        q.name_zh_tw,
        '。類型：',
        CASE
            WHEN q.quest_type IS NULL OR q.quest_type = '' THEN '未分類任務'
            WHEN q.quest_type = 'Town' THEN '城鎮任務'
            WHEN q.quest_type = 'Story' THEN '劇情任務'
            WHEN q.quest_type = 'Daily' THEN '每日任務'
            WHEN q.quest_type = 'Repeatable' THEN '可重複任務'
            ELSE CONCAT(q.quest_type, ' 任務')
        END,
        '。等級限制：',
        CASE
            WHEN q.required_level IS NULL AND q.maximum_level IS NULL THEN '未設定'
            WHEN q.required_level IS NOT NULL AND q.maximum_level IS NOT NULL THEN CONCAT(q.required_level, '-', q.maximum_level, ' 級')
            WHEN q.required_level IS NOT NULL THEN CONCAT(q.required_level, ' 級以上')
            ELSE CONCAT(q.maximum_level, ' 級以下')
        END,
        '。起始 NPC：',
        COALESCE(NULLIF(start_npc.name_zh_tw, ''), '未設定'),
        '；完成 NPC：',
        COALESCE(NULLIF(end_npc.name_zh_tw, ''), '未設定'),
        '。目標步驟：',
        COALESCE(objective_summary.objective_count, 0),
        ' 項。',
        CASE
            WHEN q.repeatable = 1 THEN CONCAT(
                '重複設定：可重複',
                CASE
                    WHEN q.repeat_interval_seconds IS NULL OR q.repeat_interval_seconds = 0 THEN '，未設定間隔。'
                    ELSE CONCAT('，間隔 ', q.repeat_interval_seconds, ' 秒。')
                END
            )
            ELSE '重複設定：不可重複。'
        END,
        '正式狀態：',
        CASE WHEN q.enabled = 1 THEN '已啟用。' ELSE '未啟用；此列僅提供任務功能對照，不改正式流程。' END
    ),
    q.completion_text_zh_tw = CASE
        WHEN q.completion_text_zh_tw IS NULL OR q.completion_text_zh_tw = '' THEN CONCAT(
            '完成對照：完成「',
            q.name_zh_tw,
            '」後回報至 ',
            COALESCE(NULLIF(end_npc.name_zh_tw, ''), '指定 NPC'),
            '。實際獎勵與後續流程需以任務獎勵表及服務端流程為準。'
        )
        ELSE q.completion_text_zh_tw
    END
WHERE q.description_zh_tw IS NULL
   OR q.description_zh_tw = '';

COMMIT;
