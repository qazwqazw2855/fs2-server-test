CREATE OR REPLACE VIEW `god2_game`.`vw_player_assets_readable` AS
SELECT
    character_row.`character_id` AS `角色ID`,
    character_row.`name` AS `角色名稱`,
    character_row.`account_id` AS `帳號ID`,
    character_row.`class_name_cache` AS `職業`,
    character_row.`level` AS `等級`,
    COALESCE(inventory_state_row.`InventoryId`, '尚未建立') AS `背包ID`,
    COALESCE(inventory_state_row.`Capacity`, 0) AS `背包容量`,
    COALESCE(inventory_state_row.`InventoryVersion`, 0) AS `背包版本`,
    COALESCE(inventory_state_row.`MutationSequence`, 0) AS `背包異動序號`,
    CASE
        WHEN inventory_state_row.`CharacterId` IS NULL THEN '背包尚未建立'
        WHEN inventory_state_row.`DirtyState` = 'Clean' THEN '已同步'
        WHEN inventory_state_row.`DirtyState` = 'Dirty' THEN '有未同步異動'
        ELSE COALESCE(inventory_state_row.`DirtyState`, '未標示')
    END AS `背包狀態`,
    COALESCE(inventory_count_row.`ItemStackCount`, 0) AS `背包物品格數`,
    COALESCE(inventory_count_row.`TotalQuantity`, 0) AS `背包物品總數量`,
    COALESCE(currency_row.`CurrencyType`, 'Gold') AS `貨幣Key`,
    CASE COALESCE(currency_row.`CurrencyType`, 'Gold')
        WHEN 'Gold' THEN '金錢'
        ELSE COALESCE(currency_row.`CurrencyType`, '未知貨幣')
    END AS `貨幣`,
    COALESCE(currency_row.`Balance`, 0) AS `餘額`,
    COALESCE(currency_row.`Version`, 0) AS `錢包版本`,
    CASE
        WHEN currency_row.`CharacterId` IS NULL THEN '錢包尚未建立'
        WHEN currency_row.`DirtyState` = 'Clean' THEN '已同步'
        WHEN currency_row.`DirtyState` = 'Dirty' THEN '有未同步異動'
        ELSE COALESCE(currency_row.`DirtyState`, '未標示')
    END AS `錢包狀態`,
    COALESCE(equipment_count_row.`EquippedCount`, 0) AS `已裝備格數`,
    COALESCE(equipment_count_row.`EquippedItemNames`, '沒有已裝備道具') AS `已裝備道具`,
    character_row.`updated_at_utc` AS `角色更新時間UTC`,
    inventory_state_row.`UpdatedAtUtc` AS `背包更新時間UTC`,
    currency_row.`UpdatedAtUtc` AS `錢包更新時間UTC`
FROM `god2_player`.`characters` character_row
LEFT JOIN `god2_player`.`player_inventory_state` inventory_state_row
    ON inventory_state_row.`CharacterId` = character_row.`character_id`
LEFT JOIN (
    SELECT
        slot_row.`character_id`,
        COUNT(*) AS `ItemStackCount`,
        COALESCE(SUM(slot_row.`quantity`), 0) AS `TotalQuantity`
    FROM `god2_player`.`character_inventory` slot_row
    WHERE slot_row.`enabled` = 1
      AND slot_row.`deleted_at_utc` IS NULL
    GROUP BY slot_row.`character_id`
) inventory_count_row
    ON inventory_count_row.`character_id` = character_row.`character_id`
LEFT JOIN `god2_player`.`player_currency_balances` currency_row
    ON currency_row.`CharacterId` = character_row.`character_id`
LEFT JOIN (
    SELECT
        equipment_row.`character_id`,
        COUNT(*) AS `EquippedCount`,
        GROUP_CONCAT(
            CONCAT(
                CASE equipment_row.`equipment_slot`
                    WHEN 'Weapon' THEN '武器'
                    WHEN 'Armor' THEN '防具'
                    WHEN 'Helmet' THEN '頭盔'
                    WHEN 'Gloves' THEN '手套'
                    WHEN 'Shoes' THEN '鞋子'
                    ELSE equipment_row.`equipment_slot`
                END,
                ':',
                COALESCE(equipment_row.`item_name_cache`, item_row.`name_zh_tw`, CAST(equipment_row.`item_id` AS CHAR))
            )
            ORDER BY equipment_row.`equipment_slot`
            SEPARATOR '、'
        ) AS `EquippedItemNames`
    FROM `god2_player`.`character_equipment` equipment_row
    LEFT JOIN `god2_game`.`items` item_row
        ON item_row.`item_id` = equipment_row.`item_id`
    WHERE equipment_row.`enabled` = 1
    GROUP BY equipment_row.`character_id`
) equipment_count_row
    ON equipment_count_row.`character_id` = character_row.`character_id`;

CREATE OR REPLACE VIEW `god2_game`.`vw_capture_gap_priority_readable` AS
SELECT
    gap_row.`priority_order` AS `優先順序`,
    gap_row.`capture_domain_zh_tw` AS `需要抓包的功能`,
    gap_row.`game_function_zh_tw` AS `遊戲功能`,
    gap_row.`current_gap_zh_tw` AS `目前缺口`,
    gap_row.`recommended_scope_zh_tw` AS `建議抓包範圍`,
    gap_row.`done_when_zh_tw` AS `完成標準`,
    gap_row.`operator_note_zh_tw` AS `備註`
FROM (
    SELECT
        10 AS `priority_order`,
        '怪物數據與 HP/MP 校準' AS `capture_domain_zh_tw`,
        '打怪與戰鬥基礎數值；HP、MP、攻擊、防禦、五行、經驗與平衡強度' AS `game_function_zh_tw`,
        CONCAT('正式怪物 ', (SELECT COUNT(*) FROM `god2_game`.`monsters`), ' 筆；同時有 HP/MP 的怪物 ', (SELECT COUNT(*) FROM `god2_game`.`monsters` WHERE `max_hp` IS NOT NULL AND `max_mp` IS NOT NULL), ' 筆。') AS `current_gap_zh_tw`,
        '先用服務端設計值補齊一般怪，再用官方抓包校準常見怪、任務怪、首領怪。' AS `recommended_scope_zh_tw`,
        '正式表能區分服務端設計值與官方證據值；抓包用來校準代表怪與關鍵怪。' AS `done_when_zh_tw`,
        '怪物數據可以自己設計，但不能把自訂數值偽裝成官方證據。' AS `operator_note_zh_tw`
    UNION ALL SELECT
        20,
        '怪物出生點',
        '地圖上的怪物出現與刷新',
        CONCAT('正式 monster_spawns ', (SELECT COUNT(*) FROM `god2_game`.`monster_spawns`), ' 筆。'),
        '每張練功地圖先抓代表區域：進圖、怪物出現、刷新後再出現。',
        '地圖、怪物、座標、刷新條件可以寫入正式 monster_spawns。',
        '沒有出生點就算怪物主表存在，也不會自然出現在地圖。'
    UNION ALL SELECT
        30,
        '完整戰鬥流程',
        '普攻、技能、命中、傷害、狀態、死亡、結算',
        CONCAT('戰鬥 runtime 核心紀錄 ', ((SELECT COUNT(*) FROM `god2`.`battle_instances`) + (SELECT COUNT(*) FROM `god2`.`battle_actions`) + (SELECT COUNT(*) FROM `god2`.`battle_action_results`) + (SELECT COUNT(*) FROM `god2`.`battle_skill_usage`) + (SELECT COUNT(*) FROM `god2`.`battle_status_instances`)), ' 筆。'),
        '先抓玩家打怪、玩家 PK、怪物攻擊玩家、技能命中/未命中、死亡與結算。',
        '能重建一回合完整輸入、服務端結果、客戶端顯示傷害與狀態。',
        '防禦狀態物防與魔防以 1.5 倍處理；後續用抓包繼續校準公式。'
    UNION ALL SELECT
        40,
        '怪物掉落與掉落平衡',
        '怪物死亡後物品、金錢或其他獎勵掉落',
        CONCAT('正式 monster_drops ', (SELECT COUNT(*) FROM `god2_game`.`monster_drops`), ' 筆；候選掉落關係 ', (SELECT COUNT(*) FROM `god2`.`monster_drop_relationships`), ' 筆。'),
        '掉落表可由服務端先按怪物等級、地圖、稀有度設計；官方抓包用來校準代表怪、任務怪與首領怪。',
        '正式表能區分服務端設計掉落與官方實測掉落，並標清機率來源。',
        '掉落也可以自己設計，但任務必要物、首領稀有物最好用官方或實測機率校準。'
    UNION ALL SELECT
        50,
        '怪物技能與 AI 設計',
        '怪物會用哪些技能，以及服務端普攻、放技能、切目標等 AI 規則',
        CONCAT('正式 monster_skills ', (SELECT COUNT(*) FROM `god2_game`.`monster_skills`), ' 筆；monster_ai_profiles ', (SELECT COUNT(*) FROM `god2_game`.`monster_ai_profiles`), ' 筆。'),
        '官方抓包只需要確認怪物技能、狀態效果、MP 消耗與少量代表行為；AI 觸發條件可由服務端自行設計。',
        '怪物技能與效果能對上證據；AI profile/rules 可由服務端設計後落表並通過戰鬥測試。',
        'AI 不再算必須抓包阻塞項，真正需要證據的是怪物技能本身與技能效果。'
    UNION ALL SELECT
        60,
        '背包與裝備 runtime',
        '登入背包、道具移動、堆疊、穿裝、脫裝、丟棄、交易前置',
        CONCAT('character_inventory ', (SELECT COUNT(*) FROM `god2_player`.`character_inventory`), ' 筆；character_equipment ', (SELECT COUNT(*) FROM `god2_player`.`character_equipment`), ' 筆。'),
        '抓登入背包快照、移動一格、堆疊、拆堆、穿裝、脫裝、丟棄。',
        '背包格、裝備欄、數量、綁定、版本與重放保護能正確對應服務端。',
        '這是開服測試基本盤，優先抓少量代表流程即可。'
    UNION ALL SELECT
        70,
        '任務獎勵',
        '完成任務後經驗、金錢、物品或狀態獎勵',
        CONCAT('正式 quest_rewards ', (SELECT COUNT(*) FROM `god2_game`.`quest_rewards`), ' 筆。'),
        '抓新手任務、打怪任務、收集任務、對話任務的完成與領獎。',
        '能確認任務 ID、獎勵物、數量、金錢、經驗與是否可重領。',
        '任務目標已有不少，獎勵會影響任務完成後體驗。'
    UNION ALL SELECT
        80,
        '商店買賣清單',
        'NPC 商店開啟、商品、價格、貨幣、購買與出售',
        CONCAT('正式 merchant_inventory ', (SELECT COUNT(*) FROM `god2_game`.`merchant_inventory`), ' 筆。'),
        '每種商店先抓一個：藥水店、裝備店、雜貨店、特殊貨幣店。',
        '商人、商品、價格、貨幣、購買限制能安全落表。',
        '不用每個 NPC 商店都先抓，先抓類型代表。'
    UNION ALL SELECT
        90,
        'NPC 出生與互動',
        'NPC 在地圖上的位置、對話、任務入口、商店入口',
        CONCAT('正式 npc_spawns ', (SELECT COUNT(*) FROM `god2_game`.`npc_spawns`), ' 筆。'),
        '先抓主城、新手村、任務關鍵 NPC。',
        'NPC ID、地圖、座標、對話入口、任務或商店入口能對上。',
        'NPC 主資料多不代表都已正式出生；要用 spawn/link 判斷。'
    UNION ALL SELECT
        100,
        '道具使用效果',
        '補 HP/MP、復活、解狀態、技能書、寶箱、傳送、特殊道具',
        CONCAT('item_usage_rules ', (SELECT COUNT(*) FROM `god2_game`.`item_usage_rules`), ' 筆；enabled ', (SELECT COUNT(*) FROM `god2_game`.`item_usage_rules` WHERE `enabled` = 1), ' 筆；item_effects enabled ', (SELECT COUNT(*) FROM `god2_game`.`item_effects` WHERE `enabled` = 1), ' 筆。'),
        '第一輪只測 30 到 50 個代表道具：補 HP/MP、復活、解狀態、技能書、寶箱、傳送、任務道具。',
        '同類道具封包格式與效果規則一致後，剩餘同類可批次套規則。',
        '不是要測全部道具；材料、收藏品、純任務收集物不需要硬補使用效果。'
    UNION ALL SELECT
        110,
        '其他細節修正',
        '交易、組隊、公會、郵件、拍賣、活動或商城等非第一輪開服核心功能',
        '目前列為較低優先級，不阻塞第一輪開服測試。',
        '等核心戰鬥、怪物、背包、任務、商店流程穩定後再抓。',
        '有正式需求或實機證據時再提升優先級。',
        '先不要讓旁支功能拖住開服測試基本盤。'
) gap_row
ORDER BY gap_row.`priority_order`;
