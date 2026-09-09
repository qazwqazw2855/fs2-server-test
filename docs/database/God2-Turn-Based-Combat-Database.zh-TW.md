# God2 回合制戰鬥資料庫

## 正式模型

角色、戰寵、神仙與怪物在 MariaDB 仍分表保存；Runtime 透過 `BattleParticipantStatsFactory` 投影成 `BattleParticipantStats`。投影包含 entity type/id、level、current/max HP/MP、力體智速、金木水火土、物攻物防法攻法防。角色／戰寵／神仙的 base 與 bonus 分離保存；兩者都未知時投影仍為 `NULL`，不轉成 0。

怪物模板在 `monsters`，技能關係在 `monster_skills`，逐回合決策在 `monster_ai_profiles`／`monster_ai_rules`。技能本體、等級、效果與狀態分別在 `skills`、`skill_levels`、`skill_effects`、`status_effects`。

## 回合欄位

- `skills.cooldown_rounds`：冷卻回合；`consumes_turn`：是否消耗行動。
- `status_effects.apply_phase`、`tick_phase`、`expire_phase`：套用、生效、到期時點；另有 duration、stack mode、maximum stacks。
- `monster_ai_rules`：可依回合、HP、狀態、概率與優先序選動作或技能。
- `formations`／`formation_slots`／`formation_bonuses`：槽位數與效果完全資料驅動。
- `battle_rule_parameters`：turn order、speed tie、action priority、round start/end、死亡、召喚、逃跑、暴擊、命中、五行規則。

沒有可信證據的戰鬥公式必須 `numeric_value=NULL`、`text_value=NULL`、`evidence_status='EvidenceBlocked'`、`enabled=0`。特別是 Speed 不得硬編為「速度最高必定先手」，conversation EXP 不得自動當神仙升級經驗。

## 管理與觀測隔離

畫面或封包觀察值寫入 `god2_game_meta.combatant_stat_observations`，只作研究與比對；不得直接污染正式 base stat。管理員可明確修改正式能力、冷卻、持續回合、AI 機率與陣型，但每次修改都會逐欄 Lock/Audit，完成後必須 Validate 與 Reload。
