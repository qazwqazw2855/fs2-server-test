DROP VIEW IF EXISTS `god2`.`vw_item_content_profiles_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_container_item_relationships_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_equipment_sets_sanitized_readable`;
DROP VIEW IF EXISTS `god2`.`vw_blackbox_item_container_set_sanitized_observations_readable`;

CREATE VIEW `god2`.`vw_item_content_profiles_sanitized_readable` AS
SELECT
    item_profile.`物品ID`,
    item_profile.`物品名稱`,
    item_profile.`客戶端物品ID`,
    CASE
        WHEN item_profile.`物品家族` = 'Accessory' THEN '飾品'
        WHEN item_profile.`物品家族` = 'PetItem' THEN '神獸相關物品'
        WHEN item_profile.`物品家族` = 'Equipment' THEN '裝備'
        WHEN item_profile.`物品家族` = 'Consumable' THEN '消耗品'
        WHEN item_profile.`物品家族` = 'QuestItem' THEN '任務道具'
        WHEN item_profile.`物品家族` IS NULL OR item_profile.`物品家族` = '' OR item_profile.`物品家族` = 'Unknown' THEN '物品家族待確認'
        ELSE item_profile.`物品家族`
    END AS `物品家族`,
    item_profile.`官方分類`,
    item_profile.`堆疊狀態`,
    item_profile.`最大堆疊`,
    CASE WHEN item_profile.`交易規則` = 'Allowed' THEN '可交易' WHEN item_profile.`交易規則` = 'Denied' THEN '不可交易' ELSE '交易規則待確認' END AS `交易規則`,
    CASE WHEN item_profile.`倉庫規則` = 'Allowed' THEN '可放倉庫' WHEN item_profile.`倉庫規則` = 'Denied' THEN '不可放倉庫' ELSE '倉庫規則待確認' END AS `倉庫規則`,
    CASE
        WHEN item_profile.`裝備部位` = 'Weapon' THEN '武器'
        WHEN item_profile.`裝備部位` = 'Armor' THEN '身體'
        WHEN item_profile.`裝備部位` = 'Helmet' THEN '頭部'
        WHEN item_profile.`裝備部位` = 'Gloves' THEN '護手'
        WHEN item_profile.`裝備部位` = 'Shoes' THEN '鞋子'
        WHEN item_profile.`裝備部位` = 'Accessory' THEN '飾品'
        WHEN item_profile.`裝備部位` IS NULL OR item_profile.`裝備部位` = '' THEN '非裝備'
        ELSE '裝備部位待確認'
    END AS `裝備部位`,
    item_profile.`需求等級`,
    item_profile.`物攻加成`,
    item_profile.`魔攻加成`,
    item_profile.`物防加成`,
    item_profile.`魔防加成`,
    item_profile.`HP加成`,
    item_profile.`MP加成`,
    item_profile.`速度加成`,
    CASE WHEN item_profile.`圖示鍵` IS NULL OR item_profile.`圖示鍵` = '' THEN '圖示未對應' ELSE '圖示已對應' END AS `圖示狀態`,
    CASE WHEN item_profile.`模型鍵` IS NULL OR item_profile.`模型鍵` = '' THEN '模型未對應' ELSE '模型已對應' END AS `模型狀態`,
    item_profile.`功能對照`,
    item_profile.`更新時間UTC`
FROM `god2`.`vw_item_content_profiles_readable` item_profile;

CREATE VIEW `god2`.`vw_container_item_relationships_sanitized_readable` AS
SELECT
    relationship.`容器物品ID`,
    relationship.`容器名稱`,
    relationship.`內容物品ID`,
    relationship.`內容物名稱`,
    COALESCE(relationship.`數量`, 0) AS `數量`,
    relationship.`有效機率`,
    CASE
        WHEN relationship.`關係狀態` = 'Verified' THEN '關係已驗證'
        WHEN relationship.`關係狀態` = 'Candidate' THEN '候選關係待確認'
        ELSE '關係狀態待確認'
    END AS `關係狀態`,
    relationship.`資料狀態`,
    relationship.`正式內容狀態`,
    relationship.`正式配方狀態`,
    relationship.`功能對照`
FROM `god2`.`vw_container_item_relationships_readable` relationship;

CREATE VIEW `god2`.`vw_equipment_sets_sanitized_readable` AS
SELECT
    equipment_set.`套裝ID`,
    equipment_set.`套裝名稱`,
    equipment_set.`需求件數`,
    equipment_set.`套裝效果`,
    equipment_set.`已知部件數`,
    REPLACE(
        REPLACE(
            REPLACE(
                REPLACE(
                    REPLACE(
                        REPLACE(equipment_set.`套裝部件`, 'Weapon:', '武器:'),
                        'Armor:', '身體:'
                    ),
                    'Helmet:', '頭部:'
                ),
                'Gloves:', '護手:'
            ),
            'Shoes:', '鞋子:'
        ),
        'Accessory:', '飾品:'
    ) AS `套裝部件`,
    CASE
        WHEN equipment_set.`套裝關係狀態` = 'Verified' THEN '套裝關係已驗證'
        WHEN equipment_set.`套裝關係狀態` = 'Candidate' THEN '套裝關係待確認'
        ELSE '套裝關係狀態待確認'
    END AS `套裝關係狀態`,
    equipment_set.`正式關係狀態`,
    equipment_set.`正式加成狀態`,
    equipment_set.`功能對照`
FROM `god2`.`vw_equipment_sets_readable` equipment_set;

CREATE VIEW `god2`.`vw_blackbox_item_container_set_sanitized_observations_readable` AS
SELECT
    '物品內容' AS `觀察類型`,
    item_profile.`物品ID` AS `主要ID`,
    item_profile.`物品名稱` AS `名稱`,
    item_profile.`官方分類` AS `分類`,
    item_profile.`功能對照`,
    CASE
        WHEN item_profile.`裝備部位` <> '非裝備' THEN '第二優先：測裝備穿脫與能力面板'
        WHEN item_profile.`官方分類` LIKE '%藥%' OR item_profile.`官方分類` LIKE '%丹%' THEN '第一優先：測道具使用效果'
        ELSE '第三優先：測背包顯示、交易與倉庫'
    END AS `黑箱測試優先級`,
    CONCAT(item_profile.`交易規則`, '；', item_profile.`倉庫規則`, '；', item_profile.`裝備部位`, '；', item_profile.`圖示狀態`, '；', item_profile.`模型狀態`) AS `測試重點`
FROM `god2`.`vw_item_content_profiles_sanitized_readable` item_profile

UNION ALL

SELECT
    '容器內容' AS `觀察類型`,
    relationship.`容器物品ID` AS `主要ID`,
    relationship.`容器名稱` AS `名稱`,
    relationship.`內容物名稱` AS `分類`,
    relationship.`功能對照`,
    CASE
        WHEN relationship.`正式內容狀態` = '正式內容關係啟用' THEN '第一優先：測開啟後內容物與數量'
        ELSE '擱置：內容關係待正式啟用'
    END AS `黑箱測試優先級`,
    CONCAT('內容物=', relationship.`內容物品ID`, '；數量=', relationship.`數量`, '；機率=', relationship.`有效機率`, '；', relationship.`關係狀態`) AS `測試重點`
FROM `god2`.`vw_container_item_relationships_sanitized_readable` relationship

UNION ALL

SELECT
    '裝備套裝' AS `觀察類型`,
    equipment_set.`套裝ID` AS `主要ID`,
    equipment_set.`套裝名稱` AS `名稱`,
    CONCAT('需求', equipment_set.`需求件數`, '件') AS `分類`,
    equipment_set.`功能對照`,
    CASE
        WHEN equipment_set.`正式加成狀態` = '正式套裝加成啟用' THEN '第一優先：測穿戴件數與套裝加成'
        ELSE '擱置：套裝加成待正式啟用'
    END AS `黑箱測試優先級`,
    CONCAT(equipment_set.`套裝關係狀態`, '；部件=', COALESCE(equipment_set.`套裝部件`, ''), '；效果=', equipment_set.`套裝效果`) AS `測試重點`
FROM `god2`.`vw_equipment_sets_sanitized_readable` equipment_set;
