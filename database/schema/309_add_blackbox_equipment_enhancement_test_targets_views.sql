DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_equipment_test_targets_readable`;
DROP VIEW IF EXISTS `god2_game`.`vw_blackbox_equipment_enhancement_test_targets_readable`;

CREATE VIEW `god2_game`.`vw_blackbox_equipment_test_targets_readable` AS
SELECT
    e.`item_id` AS `物品ID`,
    e.`client_item_id` AS `客戶端物品ID`,
    e.`code` AS `服務端代碼`,
    e.`name_zh_tw` AS `裝備名稱`,
    COALESCE(e.`item_category`, '') AS `物品分類`,
    COALESCE(e.`item_family`, '') AS `物品家族`,
    COALESCE(e.`equipment_type`, '') AS `裝備類型`,
    COALESCE(e.`equipment_slot`, '') AS `裝備欄位`,
    e.`required_level` AS `需求等級`,
    e.`required_strength` AS `需求腕力`,
    e.`required_intelligence` AS `需求智力`,
    e.`hp_bonus` AS `HP加成`,
    e.`mp_bonus` AS `MP加成`,
    e.`strength_bonus` AS `腕力加成`,
    e.`constitution_bonus` AS `體力加成`,
    e.`intelligence_bonus` AS `智力加成`,
    e.`speed_bonus` AS `速度加成`,
    e.`physical_attack_bonus` AS `物攻加成`,
    e.`physical_defense_bonus` AS `物防加成`,
    e.`magic_attack_bonus` AS `魔攻加成`,
    e.`magic_defense_bonus` AS `魔防加成`,
    e.`metal_bonus` AS `金加成`,
    e.`wood_bonus` AS `木加成`,
    e.`water_bonus` AS `水加成`,
    e.`fire_bonus` AS `火加成`,
    e.`earth_bonus` AS `土加成`,
    e.`durability` AS `耐久`,
    e.`maximum_enhancement` AS `最高強化`,
    e.`socket_count` AS `孔數`,
    CASE
        WHEN e.`enabled` = 1 AND e.`equippable` = 1 AND e.`equipment_slot` IS NOT NULL
            THEN '第一優先：測穿戴、卸下、面板能力變化'
        WHEN e.`enabled` = 1 AND e.`equippable` = 1
            THEN '第二優先：可裝備但欄位需黑箱確認'
        WHEN e.`equippable` = 1
            THEN '第三優先：資料存在但未啟用'
        ELSE '擱置：不是可穿戴裝備'
    END AS `黑箱測試優先級`,
    CASE
        WHEN e.`equipment_slot` IS NULL OR e.`equipment_slot` = '' THEN '確認官方裝備欄位、職業限制、性別限制。'
        WHEN e.`maximum_enhancement` > 0 THEN '測穿戴能力、裝備欄位、耐久、可否強化。'
        ELSE '測穿戴能力、裝備欄位、交易與掉落限制。'
    END AS `測試重點`,
    CASE WHEN e.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`equipment` e;

CREATE VIEW `god2_game`.`vw_blackbox_equipment_enhancement_test_targets_readable` AS
SELECT
    material.`material_item_id` AS `材料物品ID`,
    material.`client_item_id` AS `材料客戶端ID`,
    material.`name_zh_tw` AS `強化材料名稱`,
    COALESCE(material.`target_type`, '') AS `適用類別`,
    material.`equipment_tier` AS `裝備階級`,
    COALESCE(material.`grade`, '') AS `材料品質`,
    material.`minimum_increment` AS `最小強化增幅`,
    material.`maximum_increment` AS `最大強化增幅`,
    material.`maximum_durability_loss` AS `最大耐久損失`,
    COALESCE(material.`failure_policy`, '') AS `失敗處置`,
    rate_summary.`機率階段數` AS `機率階段數`,
    COALESCE(rate_summary.`成功率摘要`, '') AS `成功率摘要`,
    CASE
        WHEN material.`enabled` = 1 AND COALESCE(rate_summary.`機率階段數`, 0) > 0
            THEN '第一優先：測強化成功率、失敗處置、耐久變化'
        WHEN material.`enabled` = 1
            THEN '第二優先：材料啟用但缺成功率階段'
        ELSE '擱置：材料未啟用'
    END AS `黑箱測試優先級`,
    CASE
        WHEN COALESCE(rate_summary.`機率階段數`, 0) = 0 THEN '缺成功率資料；先確認官方材料用途。'
        ELSE '測每級強化成功、失敗是否破壞或保留、耐久扣減、強化值增幅。'
    END AS `測試重點`,
    CASE WHEN material.`enabled` = 1 THEN '已啟用' ELSE '未啟用' END AS `服務端狀態`
FROM `god2_game`.`equipment_enhancement_materials` material
LEFT JOIN (
    SELECT
        rate.`grade`,
        COUNT(*) AS `機率階段數`,
        GROUP_CONCAT(
            CONCAT(
                '+', rate.`target_enhancement_level`,
                '=',
                TRIM(TRAILING '.' FROM TRIM(TRAILING '0' FROM CAST((rate.`success_rate_basis_points` / 100.0) AS CHAR))),
                '%'
            )
            ORDER BY rate.`target_enhancement_level`
            SEPARATOR '；'
        ) AS `成功率摘要`
    FROM `god2_game`.`equipment_enhancement_rates` rate
    WHERE rate.`enabled` = 1
    GROUP BY rate.`grade`
) rate_summary ON rate_summary.`grade` = material.`grade`;
