USE god2_game;

CREATE TABLE IF NOT EXISTS god2_research.xjz_combat_runtime_contract_feature_audit (
    feature_key VARCHAR(96) NOT NULL,
    game_function_zh_tw VARCHAR(160) NOT NULL,
    client_recovered_contract_zh_tw VARCHAR(512) NOT NULL,
    current_safe_apply_level_zh_tw VARCHAR(160) NOT NULL,
    next_server_action_zh_tw VARCHAR(512) NOT NULL,
    blocked_reason_zh_tw VARCHAR(512) NULL,
    source_contract_file VARCHAR(128) NOT NULL,
    source_pack_id VARCHAR(96) NOT NULL,
    packet_evidence_boundary TINYINT(1) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
    PRIMARY KEY (source_pack_id, feature_key),
    KEY idx_xjz_combat_contract_apply_level (current_safe_apply_level_zh_tw)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELETE FROM god2_research.xjz_combat_runtime_contract_feature_audit
WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818';

INSERT INTO god2_research.xjz_combat_runtime_contract_feature_audit (
    feature_key,
    game_function_zh_tw,
    client_recovered_contract_zh_tw,
    current_safe_apply_level_zh_tw,
    next_server_action_zh_tw,
    blocked_reason_zh_tw,
    source_contract_file,
    source_pack_id,
    packet_evidence_boundary
) VALUES
('combat_action_kinds','戰鬥指令類型','CombatActionKind 包含 BasicAttack、Skill、Item、Wait。','可比對 runtime','確認正式 runtime 每回合可處理普攻、技能、道具、等待四類行動，缺的補服務端處理。',NULL,'CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('status_action_blocking','狀態阻擋行動','StatusActionRule/ActionBlockReason 涵蓋 Dead、Seal、Sleep、Petrify、NotEnoughMp。','可比對 runtime','檢查封神、睡眠、石化、死亡、MP 不足是否會阻擋對應行動並產生事件。','持續回合與命中率仍需官方封包或黑箱測試。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('round_start_tick','回合開始效果','FlowEventKind 包含 RoundStart、PoisonTick，ICombatEffectSystem 有 ApplyRoundStartTick。','可比對 runtime','補/驗證回合開始 tick：毒傷、持續狀態回合扣減、Buff/Debuff 到期。','毒傷公式目前只見 BoundaryConfig 預設 divisor，正式常數需測試。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('hp_damage_heal','生命傷害與治療','FlowEventKind/CombatEffectKind 涵蓋 HpDamage、HpHeal、DamageHp、HealHp。','可比對 runtime','確認傷害與治療事件都有寫入 combat event log，並同步角色/戰鬥狀態。','物攻物防、魔攻魔防、技能倍率仍需真實樣本校準。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('mp_heal','法力回復','FlowEventKind 包含 MpHeal，CombatVitals 含 Mp/MaxMp。','可比對 runtime','確認戰鬥中使用補魔道具或技能時會回復 MP 並記錄事件。','MP 顯示與怪物 MP 仍需抓包補證。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('revive','復活','CombatEffectKind 包含 Revive，BoundaryConfig 含 DefaultReviveHp。','可比對 runtime','確認復活效果可把死亡目標回到可行動狀態並套用預設或證據 HP。','復活 HP 數值仍需技能/道具實測。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('clear_status','解除異常狀態','CombatEffectKind 包含 ClearStatus、ClearNegativeStatuses，FlowEventKind 包含 StatusClear。','已部分實裝','道具解除狀態已在 item_effect runtime 對帳；戰鬥技能解除狀態仍需比對技能規則。','技能解除狀態的 effect id 與範圍仍需補證。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('element_buffs','五行狀態與 Buff','StatusEffectKind/ElementKind 涵蓋金木水火土，ElementBuff 含 Magnitude/RemainingRounds。','可比對 runtime','建立五行 Buff 與傷害加成/減免對照；有真實 PK/黑箱樣本後鎖定公式。','五行倍率、相生相剋、加成/減免公式仍需真實數據。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('skill_rule_loader','技能規則載入','ICombatRuleLoader 從 gamedata/spg effect 建 CombatSkillRule，含 mp cost、range hint、raw effect id。','可比對 runtime','檢查正式技能 catalog 是否能輸出 CombatSkillRule；缺 raw effect mapping 的技能先留待補證。','效果 ID 對狀態/元素/治療/復活的映射需證據。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_formula_samples','戰鬥公式樣本與推論','CombatFormulaSample/StageSample/Inference/InferenceEngine 定義公式黑箱樣本與推論。','應建立黑箱測試資料流','後續黑箱測試應存攻防、五行、Buff、觀測傷害/命中/暴擊，再鎖定公式。','目前沒有足夠正式樣本能鎖定所有公式。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('social_combat_effects','社交戰鬥效果','SocialCombatEffectKind 包含 MarriageProtect、SwornAssist、MentorGuidance。','可比對 runtime','比對夫妻保護、結拜助攻、師徒加成是否有正式 runtime 和資料表。','觸發率與傷害增減百分比需官方封包或黑箱測試。','CombatRuntimeContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_session_events','戰鬥事件與戰鬥存檔','ICombatSessionStore 涵蓋 session、event log、resolution、processor state。','可比對 runtime','確認正式服務端每場戰鬥能保存事件、結果、處理器狀態，方便除錯與回放。',NULL,'CombatAwardContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_rewards','戰鬥經驗金錢獎勵','ICombatAwardService/CombatRewardAwardRecord 定義戰鬥 EXP、Money 發放。','可比對 runtime','比對戰鬥結算是否能發 EXP/仙幣並寫入獎勵狀態。','實際獎勵公式與怪物掉落仍需證據。','CombatAwardContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_loot_awards','戰鬥掉落獎勵','CombatLootAwardRecord/LootGrant 定義戰鬥掉落道具發放。','可比對 runtime','比對正式掉落表與戰鬥結算是否能產生道具獎勵。','怪物掉落率與掉落表仍需官方封包或黑箱測試。','CombatAwardContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_quest_progress','戰鬥後任務進度','ICombatQuestProgressService 依 defeated target 與 requirement type 推進任務。','可比對 runtime','確認擊殺怪物後任務需求計數能推進，並保留 packet evidence boundary。','任務目標與獎勵的完整樣本仍需抓包。','CombatAwardContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_command_service','戰鬥命令入口','ICombatCommandRuntimeService 定義 PerformAction/StartRound。','可比對 runtime','確認封包進來後能路由到戰鬥命令服務，並輸出 outcome/logged event。','封包 opcode 與正式 UI 行為仍需持續實測。','CombatCommandContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('combat_actor_builder','戰鬥角色建立','ICombatActorBuilder 從 CharacterRuntimeState/current HP MP 建 CombatActor。','可比對 runtime','確認角色進戰鬥會套用裝備、技能、目前 HP/MP、位置。','怪物 actor 的 HP/MP 還需要怪物封包或黑箱設計。','CombatFactoryContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('runtime_inventory_equipment','背包與裝備 runtime','RuntimeServiceContracts 定義背包、裝備、交易/擺攤/拍賣/丟棄。','可比對 runtime','比對正式背包裝備服務與 item permission，缺少交易類行為先列服務端缺口。','市場/社交交易細節仍需封包證據。','RuntimeServiceContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1),
('runtime_pet_mount_change_doll','寵物坐騎與變身卡 runtime','RuntimeServiceContracts 定義 CreatePet、ActivateMount、CanTeleport、ApplyChangeDollCard。','可比對 runtime','確認寵物生成、坐騎啟用/傳送、變身卡能力套用有正式服務端對應。','寵物/坐騎部分數值與限制仍需官方證據。','RuntimeServiceContracts.cs','XJZ-csharp-evidence-capture-pack-20260818',1);

UPDATE god2_research.xjz_csharp_apply_queue
SET
    safe_apply_level_zh_tw = CONCAT(
        '已建立戰鬥/runtime 契約功能對照 ',
        (SELECT COUNT(*) FROM god2_research.xjz_combat_runtime_contract_feature_audit WHERE source_pack_id = 'XJZ-csharp-evidence-capture-pack-20260818'),
        ' 項'
    ),
    next_server_action_zh_tw = '依契約對照逐項檢查正式 runtime；公式常數、五行倍率、怪物 HP/MP、掉落/獎勵不得無證據硬填。'
WHERE queue_key = 'combat_contracts';
