# God2 服主管理與 HeidiSQL 編輯指南

## 權限與基本規則

服主使用被授予 `god2_content_admin_role` 的個人 MariaDB 帳號，不使用 `root` 或 Runtime 帳號。可編輯 `god2_game` 與必要的 `god2_player`；禁止直接改 `god2` Evidence、`god2_game_meta.field_provenance`、release、sync run、衝突與 Audit。不要編輯 `*_cache` 取代權威 ID。

每次只用完整主鍵與明確欄位：

```sql
UPDATE god2_game.monsters SET max_hp=12500 WHERE monster_id=123;
UPDATE god2_game.monsters SET strength=80, constitution=90, intelligence=25, speed=35 WHERE monster_id=123;
UPDATE god2_game.monster_spawns SET position_x=420, position_y=315, respawn_seconds_min=60, respawn_seconds_max=90 WHERE spawn_id=456;
UPDATE god2_game.monster_drops SET drop_rate=0.02500000 WHERE drop_id=789;
UPDATE god2_game.merchant_inventory SET selling_price=5000 WHERE merchant_inventory_id=321;
```

新增商品時先確認 `merchant_id`、`item_id` 都存在且已啟用，再填價格；未知回收價用 `NULL`，不是 0。修改技能使用 `skill_levels.mp_cost`、`skills.cooldown_rounds`；Buff 使用 `status_effects.duration_rounds` 與 apply/tick/expire phase。怪物技能改 `monster_skills`，回合 AI 改 `monster_ai_rules` 的 order、condition、action、probability。陣型改 `formation_slots`／`formation_bonuses`，不要寫死槽數。

玩家數值用 `god2_player.characters` 的 `*_base` 與 `*_bonus`；戰寵用 `character_pets`（包括 current/max lifespan 與 fusion expiry）；神仙用 `character_immortals`。未知能力保留 `NULL`。

## Admin Lock 與 Audit

直接 SQL `UPDATE` 後，trigger 會為每個改動欄位建立或更新 `admin_field_locks`，並把 old/new 值寫入 `admin_change_audit`。Evidence Sync 看到鎖後不覆蓋該欄，同列其他未鎖的 `NULL` 欄仍可補齊。

先查再解鎖：

```sql
SELECT * FROM god2_game_meta.admin_change_audit
WHERE entity_schema='god2_game' AND entity_table='monsters' AND entity_id='123'
ORDER BY changed_at_utc DESC;

DELETE FROM god2_game_meta.admin_field_locks
WHERE entity_schema='god2_game' AND entity_table='monsters'
  AND entity_id='123' AND field_name='max_hp';
```

複合主鍵的 `entity_id` 格式是 `欄位=值|欄位=值`。解鎖不刪 Audit；解鎖後同步器仍只會依白名單、Evidence gate 與 fill-null-only 規則工作。

## 驗證、Reload、回復

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Validate-God2GameCatalog.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Reload-God2GameplayCatalog.ps1
```

接著在 Server console 輸入 `reload gameplay`。若 FK、required field、Evidence gate、Candidate 或未知掉率驗證失敗，Active release 與舊 Runtime snapshot 都保留。

誤改時從 Audit 找 `old_value`，用明確型別回寫，Validate 後 Reload。大量修改前先 dump `god2_game`、`god2_player`、`god2_game_meta`。schema、資料庫帳號、連線或程式碼修改必須重啟；一般 Gameplay 列可熱重載。
