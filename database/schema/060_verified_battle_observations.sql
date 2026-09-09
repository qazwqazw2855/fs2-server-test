DROP TABLE IF EXISTS `verified_battle_action_semantics`;
DROP TABLE IF EXISTS `verified_skill_observations`;

CREATE TABLE `verified_skill_observations` (
    `SkillNameZhTw` varchar(256) NOT NULL COMMENT '實機畫面確認的技能名稱',
    `SkillCategoryZhTw` varchar(64) NOT NULL COMMENT '技能分類',
    `SkillLevel` int NOT NULL COMMENT '技能階級',
    `MpCost` int NOT NULL COMMENT '施放消耗 MP',
    `AttackRange` int NOT NULL COMMENT '攻擊距離',
    `TargetScopeZhTw` varchar(64) NOT NULL COMMENT '作用範圍',
    `PowerDescriptionZhTw` varchar(128) NOT NULL COMMENT '畫面顯示的威力描述',
    `OfficialClientItemId` int NULL COMMENT '唯一匹配的官方客戶端技能書物品編號',
    `OfficialDisplayId` int NULL COMMENT '官方客戶端顯示編號',
    `ObservedAtUtc` datetime(6) NOT NULL COMMENT '實機觀察時間',
    `EvidenceStatusZhTw` varchar(64) NOT NULL COMMENT '證據狀態',
    PRIMARY KEY (`SkillNameZhTw`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `verified_battle_action_semantics` (
    `ActionNameZhTw` varchar(64) NOT NULL COMMENT '受控實機操作確認的戰鬥動作',
    `ObservedAtUtc` datetime(6) NOT NULL COMMENT '實機觀察時間',
    `EvidenceStatusZhTw` varchar(64) NOT NULL COMMENT '證據狀態',
    `RuntimeReady` tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否已可安全驅動正式 Runtime',
    `RemainingGapZhTw` varchar(500) NULL COMMENT '仍待確認的欄位',
    PRIMARY KEY (`ActionNameZhTw`),
    CONSTRAINT `CK_VerifiedBattleAction_RuntimeReady` CHECK (`RuntimeReady` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `verified_skill_observations`
    (`SkillNameZhTw`,`SkillCategoryZhTw`,`SkillLevel`,`MpCost`,`AttackRange`,`TargetScopeZhTw`,
     `PowerDescriptionZhTw`,`OfficialClientItemId`,`OfficialDisplayId`,`ObservedAtUtc`,`EvidenceStatusZhTw`)
VALUES
    ('劍一  蒼穹訣。劍氣','劍技',1,5,3,'單體','中威力技',6501,20001,'2026-08-10 23:51:00.000000','實機畫面與官方資料唯一匹配')
ON DUPLICATE KEY UPDATE
    `SkillCategoryZhTw`=VALUES(`SkillCategoryZhTw`),
    `SkillLevel`=VALUES(`SkillLevel`),
    `MpCost`=VALUES(`MpCost`),
    `AttackRange`=VALUES(`AttackRange`),
    `TargetScopeZhTw`=VALUES(`TargetScopeZhTw`),
    `PowerDescriptionZhTw`=VALUES(`PowerDescriptionZhTw`),
    `OfficialClientItemId`=VALUES(`OfficialClientItemId`),
    `OfficialDisplayId`=VALUES(`OfficialDisplayId`),
    `ObservedAtUtc`=VALUES(`ObservedAtUtc`),
    `EvidenceStatusZhTw`=VALUES(`EvidenceStatusZhTw`);

INSERT INTO `verified_battle_action_semantics`
    (`ActionNameZhTw`,`ObservedAtUtc`,`EvidenceStatusZhTw`,`RuntimeReady`,`RemainingGapZhTw`)
VALUES
    ('普通攻擊','2026-08-10 23:51:00.000000','受控實機操作確認',0,'目標遮罩與動作參數仍待確認'),
    ('防禦','2026-08-10 23:51:00.000000','受控實機操作確認',0,'動作參數與伺服器結果欄位仍待確認'),
    ('技能','2026-08-10 23:51:00.000000','受控實機操作確認',0,'技能編號、目標遮罩與動作參數仍待確認')
ON DUPLICATE KEY UPDATE
    `ObservedAtUtc`=VALUES(`ObservedAtUtc`),
    `EvidenceStatusZhTw`=VALUES(`EvidenceStatusZhTw`),
    `RuntimeReady`=VALUES(`RuntimeReady`),
    `RemainingGapZhTw`=VALUES(`RemainingGapZhTw`);
