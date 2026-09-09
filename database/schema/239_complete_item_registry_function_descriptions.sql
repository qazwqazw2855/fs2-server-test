START TRANSACTION;

UPDATE `god2_game`.`items` item_row
SET item_row.`description_zh_tw` = CONCAT(
    '功能對照：',
    item_row.`name_zh_tw`,
    '。分類：',
    CASE
        WHEN item_row.`item_category` = 'Quest' AND item_row.`item_family` = 'QuestItem' THEN '任務物品'
        WHEN item_row.`item_category` = 'Generic' AND item_row.`item_family` = 'Special' THEN '特殊系統、地圖、NPC、模式或資料用項目'
        WHEN item_row.`item_family` IS NULL OR item_row.`item_family` = '' THEN item_row.`item_category`
        ELSE CONCAT(item_row.`item_category`, '/', item_row.`item_family`)
    END,
    '。使用規則：',
    CASE WHEN item_row.`usable` = 1 THEN '可使用' ELSE '不可直接使用' END,
    '；',
    CASE WHEN item_row.`equippable` = 1 THEN '可裝備' ELSE '不可裝備' END,
    '；',
    CASE WHEN item_row.`stackable` = 1 THEN CONCAT('可堆疊，上限 ', COALESCE(item_row.`maximum_stack`, 0)) ELSE '不可堆疊' END,
    '。交易規則：',
    CASE WHEN item_row.`tradable` = 1 THEN '可交易' ELSE '不可交易' END,
    '；',
    CASE WHEN item_row.`droppable` = 1 THEN '可掉落' ELSE '不可掉落' END,
    '；',
    CASE WHEN item_row.`storable` = 1 THEN '可存倉' ELSE '不可存倉' END,
    '。正式狀態：',
    CASE WHEN item_row.`enabled` = 1 THEN '已啟用。' ELSE '未啟用。' END
)
WHERE item_row.`description_zh_tw` IS NULL
   OR item_row.`description_zh_tw` = '';

UPDATE `god2_game`.`item_registry` registry_row
SET registry_row.`description_zh_tw` = CONCAT(
    '功能對照：',
    registry_row.`name_zh_tw`,
    '。分類：',
    CASE
        WHEN registry_row.`item_category` = 'Quest' AND registry_row.`item_family` = 'QuestItem' THEN '任務物品'
        WHEN registry_row.`item_category` = 'Generic' AND registry_row.`item_family` = 'Special' THEN '特殊系統、地圖、NPC、模式或資料用項目'
        WHEN registry_row.`item_family` IS NULL OR registry_row.`item_family` = '' THEN registry_row.`item_category`
        ELSE CONCAT(registry_row.`item_category`, '/', registry_row.`item_family`)
    END,
    '。使用規則：',
    CASE WHEN registry_row.`usable` = 1 THEN '可使用' ELSE '不可直接使用' END,
    '；',
    CASE WHEN registry_row.`equippable` = 1 THEN '可裝備' ELSE '不可裝備' END,
    '；',
    CASE WHEN registry_row.`stackable` = 1 THEN CONCAT('可堆疊，上限 ', COALESCE(registry_row.`maximum_stack`, 0)) ELSE '不可堆疊' END,
    '。交易規則：',
    CASE WHEN registry_row.`tradable` = 1 THEN '可交易' ELSE '不可交易' END,
    '；',
    CASE WHEN registry_row.`droppable` = 1 THEN '可掉落' ELSE '不可掉落' END,
    '；',
    CASE WHEN registry_row.`storable` = 1 THEN '可存倉' ELSE '不可存倉' END,
    '。正式狀態：',
    CASE WHEN registry_row.`enabled` = 1 THEN '已啟用。' ELSE '未啟用。' END
)
WHERE registry_row.`description_zh_tw` IS NULL
   OR registry_row.`description_zh_tw` = '';

COMMIT;
