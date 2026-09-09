# God2 四項生活技能與製作指南

## 固定技能

`god2_game.life_skills` 固定 seed 四個唯一 code：

| code | 繁中名稱 |
|---|---|
| `ArmorForging` | 防具鍛造 |
| `WeaponForging` | 武器鍛造 |
| `PillAlchemy` | 丹藥鍛造 |
| `MagicTreasureForging` | 法寶鍛造 |

它們不是戰鬥技能、戰寵技能或神仙技能，也不是 `characters` 的四個固定 Level 欄。每個角色在 `god2_player.character_life_skills` 各有四列，未知 level、experience、proficiency、成功率加成都保留 `NULL`。

## HeidiSQL 編輯

```sql
UPDATE god2_player.character_life_skills
SET level=12, experience=3450, proficiency=87
WHERE character_id=1
  AND life_skill_id=(SELECT life_skill_id FROM god2_game.life_skills WHERE code='WeaponForging');
```

這會產生 Admin Lock／Audit；Evidence Sync 不會覆蓋該欄。`is_unlocked=0` 代表不可使用；`is_active` 是玩家狀態，不應拿來表示技能定義是否存在。

## 配方

`crafting_recipes.life_skill_id` 指向一項生活技能，`required_life_skill_level` 控制最低等級；材料在 `crafting_recipe_materials`，一項或多項產出在 `crafting_recipe_outputs`。未知 `success_rate`、`critical_success_rate` 或產出率一律 `NULL`，且未驗證公式的配方保持 `enabled=0`。

Server 製作流程必須自行驗證：配方已啟用、角色已解鎖 `character_learned_recipes`、生活技能等級足夠、材料與貨幣足夠；Server 扣除材料並擲正式機率，絕不信任 Client 回報的製作結果。

新增／修改配方後依序 Validate、`Reload-God2GameplayCatalog.ps1`，再於 Server console 執行 `reload gameplay`。若要回復，依 `admin_change_audit` 回寫原值，再視需要解除逐欄鎖。
