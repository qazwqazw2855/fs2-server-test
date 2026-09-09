CREATE OR REPLACE VIEW `god2_game`.`vw_legacy_items_readable` AS
SELECT
    item_row.`Id` AS `道具ID`,
    item_row.`Code` AS `道具代碼`,
    COALESCE(item_row.`NameZhTw`, item_row.`DisplayName`, item_row.`Name`) AS `道具名稱`,
    item_row.`DescriptionZhTw` AS `道具說明`,
    item_row.`ItemType` AS `官方類型Key`,
    CASE item_row.`ItemType`
        WHEN 'WPN' THEN '武器'
        WHEN 'TWP' THEN '天界武器'
        WHEN 'GWP' THEN '神仙武器'
        WHEN 'EQU' THEN '防具'
        WHEN 'TEQ' THEN '天界防具'
        WHEN 'GEQ' THEN '神仙裝備'
        WHEN 'GEQ02' THEN '神仙裝備二'
        WHEN 'GEH' THEN '神仙頭部裝備'
        WHEN 'GEB' THEN '神仙身體裝備'
        WHEN 'PEQ' THEN '寵物裝備'
        WHEN 'MEQ' THEN '法寶裝備'
        WHEN 'SES' THEN '特殊防具'
        WHEN 'SSW' THEN '特殊武器'
        WHEN 'COM' THEN '消耗品'
        WHEN 'MED01' THEN '藥品一'
        WHEN 'MED02' THEN '藥品二'
        WHEN 'MED03' THEN '藥品三'
        WHEN 'MAT01' THEN '材料'
        WHEN 'MIS' THEN '任務道具'
        WHEN 'MIS02' THEN '任務道具二'
        WHEN 'MIS03' THEN '任務道具三'
        WHEN 'PET' THEN '寵物'
        WHEN 'EGG' THEN '寵物蛋'
        WHEN 'GOD' THEN '神仙'
        WHEN 'SKB' THEN '技能書'
        WHEN 'SPI' THEN '靈物'
        WHEN 'AMU' THEN '護符'
        WHEN 'MAP' THEN '地圖道具'
        WHEN 'NCP' THEN 'NPC 相關道具'
        WHEN 'NMP' THEN '一般地圖道具'
        WHEN 'KIT01' THEN '禮包一'
        WHEN 'KIT02' THEN '禮包二'
        WHEN 'CBF01' THEN '卡片包一'
        WHEN 'CBF02' THEN '卡片包二'
        WHEN 'CBK' THEN '卡冊'
        WHEN 'CAD' THEN '卡片'
        WHEN 'BAR' THEN '條狀道具'
        WHEN 'BEB' THEN '寶箱/包裹'
        WHEN 'PFD' THEN '寵物食品'
        WHEN 'SCD' THEN '特殊卡片'
        WHEN 'SPP' THEN '特殊包裹'
        WHEN 'STR' THEN '強化相關'
        WHEN 'TLI' THEN '時限道具'
        WHEN 'UPS' THEN '升級道具'
        WHEN 'VPT' THEN '加值票券'
        WHEN 'NST' THEN '巢穴/特殊票券'
        WHEN 'ELE' THEN '五行屬性道具'
        WHEN 'EQC' THEN '裝備箱/裝備券'
        ELSE item_row.`ItemType`
    END AS `道具類型`,
    item_row.`ItemFamily` AS `道具家族Key`,
    CASE item_row.`ItemFamily`
        WHEN 'PetItem' THEN '寵物道具'
        WHEN 'QuestItem' THEN '任務道具'
        WHEN 'Special' THEN '特殊道具'
        WHEN 'Armor' THEN '防具'
        WHEN 'Equipment' THEN '裝備'
        WHEN 'Weapon' THEN '武器'
        WHEN 'Material' THEN '材料'
        WHEN 'Accessory' THEN '飾品'
        WHEN 'Recipe' THEN '配方'
        WHEN 'Consumable' THEN '消耗品'
        WHEN 'Helmet' THEN '頭盔'
        WHEN 'Currency' THEN '貨幣'
        WHEN 'Egg' THEN '蛋'
        ELSE item_row.`ItemFamily`
    END AS `道具家族`,
    item_row.`StackPolicy` AS `堆疊規則Key`,
    CASE item_row.`StackPolicy`
        WHEN 'ExplicitNonStackable' THEN '不可堆疊'
        WHEN 'ExplicitStackable' THEN '可堆疊'
        WHEN 'Unknown' THEN '未確認'
        ELSE item_row.`StackPolicy`
    END AS `堆疊規則`,
    item_row.`MaxStack` AS `最大堆疊`,
    item_row.`TradePolicy` AS `交易規則Key`,
    CASE item_row.`TradePolicy`
        WHEN 'Allowed' THEN '可交易'
        WHEN 'Disallowed' THEN '不可交易'
        WHEN 'Unknown' THEN '未確認'
        ELSE item_row.`TradePolicy`
    END AS `交易規則`,
    item_row.`BindPolicy` AS `綁定規則Key`,
    CASE item_row.`BindPolicy`
        WHEN 'Unknown' THEN '未確認'
        ELSE item_row.`BindPolicy`
    END AS `綁定規則`,
    item_row.`SellPolicy` AS `販售規則Key`,
    CASE item_row.`SellPolicy`
        WHEN 'Unknown' THEN '未確認'
        ELSE item_row.`SellPolicy`
    END AS `販售規則`,
    item_row.`BaseBuyPrice` AS `基礎買價`,
    item_row.`BaseSellPrice` AS `基礎賣價`,
    item_row.`SellPrice` AS `販售價格`,
    item_row.`CurrencyType` AS `貨幣Key`,
    CASE item_row.`CurrencyType`
        WHEN 'Gold' THEN '金錢'
        ELSE item_row.`CurrencyType`
    END AS `貨幣`,
    CASE item_row.`QuestItemFlag`
        WHEN 1 THEN '任務道具'
        WHEN 0 THEN '非任務道具'
        ELSE '未標示'
    END AS `任務道具狀態`,
    CASE item_row.`Enabled`
        WHEN 1 THEN '啟用'
        WHEN 0 THEN '未啟用'
        ELSE '未標示'
    END AS `啟用狀態`,
    item_row.`RecoveryStatus` AS `回收狀態Key`,
    CASE item_row.`RecoveryStatus`
        WHEN 'Recovered' THEN '已由證據回收'
        ELSE item_row.`RecoveryStatus`
    END AS `回收狀態`,
    item_row.`LocalizationStatus` AS `在地化狀態Key`,
    CASE item_row.`LocalizationStatus`
        WHEN 'ConvertedToTraditional' THEN '已轉為繁體中文'
        WHEN 'TraditionalVerified' THEN '繁體中文已驗證'
        WHEN 'MixedLanguageNormalized' THEN '混合語系已正規化'
        WHEN 'NotApplicable' THEN '不適用'
        ELSE item_row.`LocalizationStatus`
    END AS `在地化狀態`,
    item_row.`ContentRecoveryRunId` AS `內容回收批次`,
    item_row.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`items` item_row;
