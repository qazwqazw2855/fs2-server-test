CREATE OR REPLACE VIEW `god2_game`.`vw_capture_gap_priority_readable` AS
SELECT
    gap_row.`priority_order` AS `優先順序`,
    gap_row.`work_status_zh_tw` AS `整理狀態`,
    gap_row.`capture_domain_zh_tw` AS `需要處理的功能`,
    gap_row.`game_function_zh_tw` AS `遊戲功能`,
    gap_row.`current_gap_zh_tw` AS `目前缺口`,
    gap_row.`recommended_scope_zh_tw` AS `建議處理範圍`,
    gap_row.`done_when_zh_tw` AS `完成標準`,
    gap_row.`operator_note_zh_tw` AS `備註`
FROM (
    SELECT
        10 AS `priority_order`,
        '暫緩設計；只保留校準缺口' AS `work_status_zh_tw`,
        '怪物數據與 HP/MP 校準' AS `capture_domain_zh_tw`,
        '打怪與戰鬥基礎數值；HP、MP、攻擊、防禦、五行、經驗與平衡強度' AS `game_function_zh_tw`,
        CONCAT('正式怪物 ', (SELECT COUNT(*) FROM `god2_game`.`monsters`), ' 筆；同時有 HP/MP 的怪物 ', (SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `max_hp` IS NOT NULL AND `max_mp` IS NOT NULL), ' 筆。') AS `current_gap_zh_tw`,
        '本輪不繼續設計怪物數值；只保留後續官方或黑箱校準入口。' AS `recommended_scope_zh_tw`,
        '正式表能區分服務端設計值與官方證據值；目前不把數值設計列為本輪整理阻塞。' AS `done_when_zh_tw`,
        '怪物數據可以自己設計，但本輪先暫停；不能把自訂數值偽裝成官方證據。' AS `operator_note_zh_tw`
    UNION ALL SELECT
        20,
        '現在整理',
        '怪物出生點',
        '地圖上的怪物出現與刷新',
        CONCAT('正式 monster_spawns ', (SELECT COUNT(*) FROM `god2_game`.`monster_spawns`), ' 筆。'),
        '先整理現有正式出生點與地圖關聯；缺正式證據的地圖列入後續黑箱測試清單。',
        '地圖、怪物、座標、刷新條件可以寫入正式 monster_spawns，且不混入未標記來源。',
        '沒有出生點就算怪物主表存在，也不會自然出現在地圖。'
    UNION ALL SELECT
        30,
        '現在整理',
        '完整戰鬥流程',
        '普攻、技能、命中、傷害、狀態、死亡、結算',
        CONCAT('戰鬥 runtime 核心紀錄 ', ((SELECT COUNT(*) FROM `god2`.`battle_instances`) + (SELECT COUNT(*) FROM `god2`.`battle_actions`) + (SELECT COUNT(*) FROM `god2`.`battle_action_results`) + (SELECT COUNT(*) FROM `god2`.`battle_skill_usage`) + (SELECT COUNT(*) FROM `god2`.`battle_status_instances`)), ' 筆。'),
        '先整理玩家打怪、玩家 PK、怪物攻擊玩家、技能命中/未命中、死亡與結算的證據缺口。',
        '能重建一回合完整輸入、服務端結果、客戶端顯示傷害與狀態。',
        '防禦狀態物防與魔防以 1.5 倍處理；後續用抓包或黑箱繼續校準公式。'
    UNION ALL SELECT
        40,
        '暫緩設計；只保留掉落校準缺口',
        '怪物掉落與掉落平衡',
        '怪物死亡後物品、金錢或其他獎勵掉落',
        CONCAT('正式 monster_drops ', (SELECT COUNT(*) FROM `god2_game`.`monster_drops`), ' 筆；候選掉落關係 ', (SELECT COUNT(*) FROM `god2`.`monster_drop_relationships`), ' 筆。'),
        '本輪不繼續設計掉落平衡；只整理任務必要物、首領稀有物與代表怪的後續校準缺口。',
        '正式表能區分服務端設計掉落與官方實測掉落，並標清機率來源。',
        '掉落可以自己設計，但本輪先暫停；任務必要物與首領稀有物後續再校準。'
    UNION ALL SELECT
        50,
        '暫緩設計',
        '怪物技能與 AI 設計',
        '怪物會用哪些技能，以及服務端普攻、放技能、切目標等 AI 規則',
        CONCAT('正式 monster_skills ', (SELECT COUNT(*) FROM `god2_game`.`monster_skills`), ' 筆；monster_ai_profiles ', (SELECT COUNT(*) FROM `god2_game`.`monster_ai_profiles`), ' 筆。'),
        '本輪不繼續設計怪物 AI；只保留怪物技能、狀態效果與 MP 消耗的後續證據缺口。',
        '怪物技能與效果能對上證據；AI profile/rules 等下次設計時再處理。',
        'AI 觸發條件可由服務端自行設計，但依照目前指示先不做。'
    UNION ALL SELECT
        60,
        '現在整理',
        '背包與裝備 runtime',
        '登入背包、道具移動、堆疊、穿裝、脫裝、丟棄、交易前置',
        CONCAT('character_inventory ', (SELECT COUNT(*) FROM `god2_player`.`character_inventory`), ' 筆；character_equipment ', (SELECT COUNT(*) FROM `god2_player`.`character_equipment`), ' 筆。'),
        '整理登入背包快照、移動一格、堆疊、拆堆、穿裝、脫裝、丟棄的服務端對應狀態。',
        '背包格、裝備欄、數量、綁定、版本與重放保護能正確對應服務端。',
        '這是開服測試基本盤，優先整理少量代表流程即可。'
    UNION ALL SELECT
        70,
        '現在整理',
        '任務獎勵',
        '完成任務後經驗、金錢、物品或狀態獎勵',
        CONCAT('正式 quest_rewards ', (SELECT COUNT(*) FROM `god2_game`.`quest_rewards`), ' 筆。'),
        '整理新手任務、打怪任務、收集任務、對話任務的完成與領獎缺口。',
        '能確認任務 ID、獎勵物、數量、金錢、經驗與是否可重領。',
        '任務目標已有不少，獎勵會影響任務完成後體驗。'
    UNION ALL SELECT
        80,
        '現在整理',
        '商店買賣清單',
        'NPC 商店開啟、商品、價格、貨幣、購買與出售',
        CONCAT('正式 merchant_inventory ', (SELECT COUNT(*) FROM `god2_game`.`merchant_inventory`), ' 筆。'),
        '整理每種商店代表流程：藥水店、裝備店、雜貨店、特殊貨幣店。',
        '商人、商品、價格、貨幣、購買限制能安全落表。',
        '不用每個 NPC 商店都先抓，先整理類型代表。'
    UNION ALL SELECT
        90,
        '現在整理',
        'NPC 出生與互動',
        'NPC 在地圖上的位置、對話、任務入口、商店入口',
        CONCAT('正式 npc_spawns ', (SELECT COUNT(*) FROM `god2_game`.`npc_spawns`), ' 筆。'),
        '整理主城、新手村、任務關鍵 NPC 的出生與互動入口。',
        'NPC ID、地圖、座標、對話入口、任務或商店入口能對上。',
        'NPC 主資料多不代表都已正式出生；要用 spawn/link 判斷。'
    UNION ALL SELECT
        100,
        '現在整理',
        '道具使用效果',
        '補 HP/MP、復活、解狀態、技能書、寶箱、傳送、特殊道具',
        CONCAT('item_usage_rules ', (SELECT COUNT(*) FROM `god2_game`.`item_usage_rules`), ' 筆；enabled ', (SELECT COUNT(*) FROM `god2_game`.`item_usage_rules` WHERE `enabled` = 1), ' 筆；item_effects enabled ', (SELECT COUNT(*) FROM `god2_game`.`item_effects` WHERE `enabled` = 1), ' 筆。'),
        '整理 30 到 50 個代表道具類型：補 HP/MP、復活、解狀態、技能書、寶箱、傳送、任務道具。',
        '同類道具封包格式與效果規則一致後，剩餘同類可批次套規則。',
        '不是要測全部道具；材料、收藏品、純任務收集物不需要硬補使用效果。'
    UNION ALL SELECT
        110,
        '低優先；不阻塞第一輪',
        '其他細節修正',
        '交易、組隊、公會、郵件、拍賣、活動或商城等非第一輪開服核心功能',
        '目前列為較低優先級，不阻塞第一輪開服測試。',
        '等核心戰鬥、怪物、背包、任務、商店流程穩定後再抓。',
        '有正式需求或實機證據時再提升優先級。',
        '先不要讓旁支功能拖住開服測試基本盤。'
) gap_row
ORDER BY gap_row.`priority_order`;
