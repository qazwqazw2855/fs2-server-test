-- Schema 373
-- Game function: monster combat runtime black-box support.
-- Purpose: persist monster MP, magic attack/defense, and five-element stats for combat formula calibration.

ALTER TABLE `god2`.`monster_combat_runtime_state`
    ADD COLUMN IF NOT EXISTS `MaximumMp` bigint NOT NULL DEFAULT 0 COMMENT '怪物最大 MP',
    ADD COLUMN IF NOT EXISTS `CurrentMp` bigint NOT NULL DEFAULT 0 COMMENT '怪物目前 MP',
    ADD COLUMN IF NOT EXISTS `MagicAttackPower` bigint NOT NULL DEFAULT 0 COMMENT '怪物魔法攻擊',
    ADD COLUMN IF NOT EXISTS `MagicDefense` bigint NOT NULL DEFAULT 0 COMMENT '怪物魔法防禦',
    ADD COLUMN IF NOT EXISTS `Metal` int NOT NULL DEFAULT 0 COMMENT '怪物金屬性',
    ADD COLUMN IF NOT EXISTS `Wood` int NOT NULL DEFAULT 0 COMMENT '怪物木屬性',
    ADD COLUMN IF NOT EXISTS `Water` int NOT NULL DEFAULT 0 COMMENT '怪物水屬性',
    ADD COLUMN IF NOT EXISTS `Fire` int NOT NULL DEFAULT 0 COMMENT '怪物火屬性',
    ADD COLUMN IF NOT EXISTS `Earth` int NOT NULL DEFAULT 0 COMMENT '怪物土屬性';

DROP VIEW IF EXISTS `god2`.`vw_monster_combat_runtime_state_readable`;

CREATE OR REPLACE VIEW `god2`.`vw_monster_combat_runtime_state_readable` AS
SELECT runtime_state.`RuntimeEntityId` AS `怪物Runtime實體ID`,
       runtime_state.`MonsterTemplateId` AS `怪物模板ID`,
       runtime_state.`SpawnDefinitionId` AS `出生點ID`,
       runtime_state.`MapId` AS `地圖ID`,
       runtime_state.`LifecycleState` AS `生命週期`,
       runtime_state.`CombatState` AS `戰鬥狀態`,
       runtime_state.`Level` AS `等級`,
       runtime_state.`MaximumHp` AS `最大HP`,
       runtime_state.`CurrentHp` AS `目前HP`,
       runtime_state.`MaximumMp` AS `最大MP`,
       runtime_state.`CurrentMp` AS `目前MP`,
       runtime_state.`AttackPower` AS `物理攻擊`,
       runtime_state.`Defense` AS `物理防禦`,
       runtime_state.`MagicAttackPower` AS `魔法攻擊`,
       runtime_state.`MagicDefense` AS `魔法防禦`,
       runtime_state.`Metal` AS `金`,
       runtime_state.`Wood` AS `木`,
       runtime_state.`Water` AS `水`,
       runtime_state.`Fire` AS `火`,
       runtime_state.`Earth` AS `土`,
       runtime_state.`CurrentTargetRuntimeEntityId` AS `目前目標Runtime實體ID`,
       runtime_state.`RuntimeVersion` AS `版本`,
       runtime_state.`DirtyFlags` AS `異動旗標`,
       runtime_state.`StatPolicyStatus` AS `數值政策狀態`,
       runtime_state.`ContentVersion` AS `內容版本`,
       runtime_state.`UpdatedAtUtc` AS `更新時間UTC`
FROM `god2`.`monster_combat_runtime_state` runtime_state;
