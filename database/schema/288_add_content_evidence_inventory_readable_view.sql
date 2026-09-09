CREATE OR REPLACE VIEW `god2_game`.`vw_content_evidence_inventory_readable` AS
SELECT
    inventory_row.`domain_order` AS `排序`,
    inventory_row.`content_domain_zh_tw` AS `內容範圍`,
    inventory_row.`game_function_zh_tw` AS `遊戲功能`,
    inventory_row.`evidence_source_zh_tw` AS `證據來源`,
    inventory_row.`evidence_count` AS `證據筆數`,
    inventory_row.`runtime_count` AS `正式服務端筆數`,
    inventory_row.`ready_count` AS `已可安全使用筆數`,
    inventory_row.`risk_note_zh_tw` AS `注意事項`
FROM (
    SELECT
        10 AS `domain_order`,
        '技能' AS `content_domain_zh_tw`,
        '玩家、怪物、寵物施放技能、被動技能、增益、負面狀態與淨化' AS `game_function_zh_tw`,
        '舊技能表、技能內容 profile、技能語意 profile、公測技能效果表' AS `evidence_source_zh_tw`,
        (SELECT COUNT(*) FROM `god2`.`skills`)
            + (SELECT COUNT(*) FROM `god2`.`skill_content_profiles`)
            + (SELECT COUNT(*) FROM `god2`.`skill_semantic_profiles`)
            + (SELECT COUNT(*) FROM `god2_game`.`public_beta_skill_effect_v0`) AS `evidence_count`,
        (SELECT COUNT(*) FROM `god2_game`.`skills`) AS `runtime_count`,
        (SELECT COUNT(*) FROM `god2_game`.`public_beta_skill_effect_v0` WHERE `enabled` = 1) AS `ready_count`,
        '技能效果候選已分出啟用狀態；目標情境代碼仍保留官方原碼，未硬猜目標類型。' AS `risk_note_zh_tw`
    UNION ALL
    SELECT
        20,
        '道具',
        '背包道具、裝備、消耗品、使用限制、交易/丟棄/倉庫/堆疊規則',
        '舊道具表、正式道具表、道具使用規則、客戶端旗標補充、道具效果與視覺證據',
        (SELECT COUNT(*) FROM `god2`.`items`)
            + (SELECT COUNT(*) FROM `god2_game`.`item_usage_rules`)
            + (SELECT COUNT(*) FROM `god2_game`.`client_item_usage_flag_additions`)
            + (SELECT COUNT(*) FROM `god2_game`.`client_item_effect_visuals`)
            + (SELECT COUNT(*) FROM `god2_game`.`item_effects`),
        (SELECT COUNT(*) FROM `god2_game`.`items`),
        (SELECT COUNT(*) FROM `god2_game`.`item_effects` WHERE `enabled` = 1),
        '道具主資料已大量進正式表；使用規則多數仍需 runtime 啟用證據，不直接硬開。'
    UNION ALL
    SELECT
        30,
        '怪物',
        '打怪、怪物 HP/MP、怪物技能、掉落、出生點、死亡與重生',
        '正式怪物表、舊怪物表、怪物語意 profile、出生語意、掉落關係候選',
        (SELECT COUNT(*) FROM `god2_game`.`monsters`)
            + (SELECT COUNT(*) FROM `god2`.`monsters`)
            + (SELECT COUNT(*) FROM `god2`.`monster_semantic_profiles`)
            + (SELECT COUNT(*) FROM `god2`.`monster_spawn_semantics`)
            + (SELECT COUNT(*) FROM `god2`.`monster_drop_relationships`),
        (SELECT COUNT(*) FROM `god2_game`.`monsters`),
        (SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `max_hp` IS NOT NULL AND `max_mp` IS NOT NULL),
        '目前只有 HP/MP 同時有證據的怪物可安全用於完整戰鬥；舊怪物 code/name 曾出現衝突，不自動同步。'
    UNION ALL
    SELECT
        40,
        '任務',
        '接任務、任務步驟、目標、獎勵、進度與完成',
        '舊任務表、任務內容 profile、正式任務/目標/獎勵表',
        (SELECT COUNT(*) FROM `god2`.`quests`)
            + (SELECT COUNT(*) FROM `god2`.`quest_content_profiles`),
        (SELECT COUNT(*) FROM `god2_game`.`quests`)
            + (SELECT COUNT(*) FROM `god2_game`.`quest_objectives`)
            + (SELECT COUNT(*) FROM `god2_game`.`quest_rewards`),
        (SELECT COUNT(*) FROM `god2`.`quest_content_profiles` WHERE `ProductionProfileEnabled` = 1),
        '任務內容 profile 很多仍是候選；只有 ProductionProfileEnabled 的內容可視為正式候選。'
    UNION ALL
    SELECT
        50,
        '世界互動',
        '地圖、NPC、商人、傳送門、NPC 對話與世界互動',
        '舊地圖/NPC/商人/傳送門/對話表與正式世界資料表',
        (SELECT COUNT(*) FROM `god2`.`maps`)
            + (SELECT COUNT(*) FROM `god2`.`npcs`)
            + (SELECT COUNT(*) FROM `god2`.`merchants`)
            + (SELECT COUNT(*) FROM `god2`.`portals`)
            + (SELECT COUNT(*) FROM `god2`.`dialogs`),
        (SELECT COUNT(*) FROM `god2_game`.`maps`)
            + (SELECT COUNT(*) FROM `god2_game`.`npcs`)
            + (SELECT COUNT(*) FROM `god2_game`.`merchants`)
            + (SELECT COUNT(*) FROM `god2_game`.`portals`)
            + (SELECT COUNT(*) FROM `god2_game`.`npc_dialogs`),
        (SELECT COUNT(*) FROM `god2_game`.`npc_spawns`)
            + (SELECT COUNT(*) FROM `god2_game`.`merchant_inventory`)
            + (SELECT COUNT(*) FROM `god2_game`.`portal_resource_links`),
        '世界資料已分正式表與舊證據表；NPC/商人/傳送是否真的出現在地圖上要看 spawn/link 是否啟用。'
    UNION ALL
    SELECT
        60,
        '寵物',
        '寵物模板、成長、固有技能、寵物技能學習與戰鬥用途',
        '寵物內容 profile、固有技能定義、正式寵物模板與模板技能',
        (SELECT COUNT(*) FROM `god2`.`pet_content_profiles`)
            + (SELECT COUNT(*) FROM `god2`.`pet_innate_definitions`),
        (SELECT COUNT(*) FROM `god2_game`.`pet_templates`)
            + (SELECT COUNT(*) FROM `god2_game`.`pet_template_skills`),
        (SELECT COUNT(*) FROM `god2_game`.`pet_templates`),
        '正式寵物模板已存在；部分 profile 仍是內容證據，不代表已完整進戰鬥 runtime。'
    UNION ALL
    SELECT
        70,
        '本地化與繁中整理',
        '所有顯示名稱、說明文字、資料表可讀化與繁中一致性',
        '本地化條目、繁中 readable views、正式繁中檢查腳本',
        (SELECT COUNT(*) FROM `god2`.`localization_entries`),
        (SELECT COUNT(*) FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA` IN ('god2', 'god2_game') AND `TABLE_TYPE` = 'VIEW' AND `TABLE_NAME` LIKE '%readable%'),
        (SELECT COUNT(*) FROM `information_schema`.`TABLES` WHERE `TABLE_SCHEMA` IN ('god2', 'god2_game') AND `TABLE_TYPE` = 'VIEW' AND `TABLE_NAME` LIKE '%readable%'),
        '繁中檢查目前由正式驗收流程把關；官方 key、hash、idempotency key 會保留，不翻成猜測文字。'
) inventory_row
ORDER BY inventory_row.`domain_order`;
