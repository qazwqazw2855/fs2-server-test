DROP VIEW IF EXISTS `god2_game`.`vw_account_characters_sanitized_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_account_character_observations_readable`;

CREATE VIEW `god2_game`.`vw_account_characters_sanitized_readable` AS
SELECT
    account_character.`帳號ID`,
    account_character.`登入帳號`,
    account_character.`帳號狀態`,
    account_character.`登入失敗次數`,
    account_character.`鎖定狀態`,
    account_character.`連線狀態`,
    account_character.`最後登入時間UTC`,
    account_character.`角色ID`,
    account_character.`角色名稱`,
    account_character.`目前有效角色名稱`,
    COALESCE(class_catalog.`name_zh_tw`,
        CASE
            WHEN account_character.`職業` IN ('Swordsman', '劍士') THEN '劍士'
            WHEN account_character.`職業` IN ('Taoist', '仙道') THEN '仙道'
            WHEN account_character.`職業` IN ('Pharmacist', '藥師') THEN '藥師'
            WHEN account_character.`職業` IN ('Warlock', '謀士') THEN '謀士'
            WHEN account_character.`職業` IS NULL OR account_character.`職業` = '未標示' THEN '未建立角色'
            ELSE '職業待確認'
        END
    ) AS `職業`,
    CASE
        WHEN account_character.`性別` IN ('Male', '男性') THEN '男性'
        WHEN account_character.`性別` IN ('Female', '女性') THEN '女性'
        WHEN account_character.`性別` IS NULL OR account_character.`性別` = '未標示' THEN '未建立角色'
        ELSE '性別待確認'
    END AS `性別`,
    CASE
        WHEN account_character.`生活技能` IS NULL OR account_character.`生活技能` = '未標示' THEN '未建立角色'
        WHEN account_character.`生活技能` LIKE 'LifeSkill%' THEN '生活技能待確認'
        ELSE account_character.`生活技能`
    END AS `生活技能`,
    account_character.`等級`,
    account_character.`地圖ID`,
    COALESCE(map_catalog.`name_zh_tw`,
        CASE
            WHEN account_character.`地圖名稱` IS NULL THEN NULL
            WHEN LOCATE('/', account_character.`地圖名稱`) > 0 THEN '地圖名稱待確認'
            ELSE account_character.`地圖名稱`
        END
    ) AS `地圖名稱`,
    account_character.`座標X`,
    account_character.`座標Y`,
    CASE
        WHEN account_character.`方向Key` IS NULL THEN '方向未記錄'
        WHEN account_character.`方向Key` = 'Unknown' THEN '方向待確認'
        ELSE account_character.`方向Key`
    END AS `方向`,
    account_character.`角色狀態`,
    account_character.`刪除狀態`,
    account_character.`角色資料版本`,
    account_character.`最後遊玩時間UTC`,
    account_character.`角色建立時間UTC`,
    account_character.`角色更新時間UTC`,
    CASE
        WHEN account_character.`角色ID` IS NULL THEN '帳號登入後尚無角色'
        WHEN account_character.`帳號狀態` = '啟用' AND account_character.`角色狀態` = '啟用' AND account_character.`刪除狀態` = '未刪除' THEN '可登入選角並進入世界'
        WHEN account_character.`刪除狀態` <> '未刪除' THEN '角色已刪除不可進入'
        ELSE '登入或角色狀態待確認'
    END AS `功能對照`
FROM `god2_game`.`vw_account_characters_readable` account_character
LEFT JOIN `god2_game`.`character_classes` class_catalog
    ON class_catalog.`code` = account_character.`職業Key`
LEFT JOIN `god2_game`.`maps` map_catalog
    ON map_catalog.`map_id` = account_character.`地圖ID`;

CREATE VIEW `god2_game`.`vw_blackbox_account_character_observations_readable` AS
SELECT
    '帳號選角' AS `觀察類型`,
    account_view.`帳號ID` AS `主要ID`,
    account_view.`登入帳號` AS `名稱`,
    COALESCE(account_view.`角色名稱`, '尚無角色') AS `分類`,
    account_view.`功能對照`,
    CASE
        WHEN account_view.`功能對照` = '可登入選角並進入世界' THEN '第一優先：測登入、選角、進入世界與座標'
        WHEN account_view.`功能對照` = '帳號登入後尚無角色' THEN '第二優先：測建立角色流程'
        ELSE '擱置：帳號或角色狀態不可正常進入'
    END AS `黑箱測試優先級`,
    CONCAT(
        '帳號狀態=', account_view.`帳號狀態`,
        '；角色=', COALESCE(account_view.`角色名稱`, '無'),
        '；職業=', account_view.`職業`,
        '；性別=', account_view.`性別`,
        '；生活技能=', account_view.`生活技能`,
        '；地圖=', COALESCE(account_view.`地圖名稱`, '未進入世界'),
        '；座標=', COALESCE(account_view.`座標X`, 0), ',', COALESCE(account_view.`座標Y`, 0)
    ) AS `測試重點`
FROM `god2_game`.`vw_account_characters_sanitized_readable` account_view;
