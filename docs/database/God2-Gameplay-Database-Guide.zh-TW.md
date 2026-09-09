# God2 正式 Gameplay Database 指南

## 權威邊界

正式環境分為三個 schema：

- `god2_game`：可直接閱讀與編輯的靜態 Gameplay Catalog。Runtime 只載入 `enabled=1`。
- `god2_player`：帳號、角色、背包、戰寵、神仙、生活技能與交易狀態。
- `god2_game_meta`：來源對照、欄位 provenance、Admin Lock、Audit、同步衝突、驗證與 Active release。

舊 `god2` 保留為 Evidence／migration 歷史與 `__SchemaVersion`。正常 Server 啟動只會從它讀取很小的 migration 版本表，不會掃描 `content_field_evidence`、`content_raw_records`、staging 或 packet capture 表。Catalog Builder 才能讀 Evidence。

## 資料語意

`NULL` 是「未知／尚無證據」，`0` 是已確認數值為零。例如 `monster_drops.drop_rate=NULL` 代表掉率未知，絕不是 0%。未知公式、機率、重生秒數與能力值不得猜填。

`enabled=1` 表示可進入 Runtime；`enabled=0` 表示仍可在 HeidiSQL 檢視，但不會載入。`Candidate`、`Derived`、`EvidenceBlocked` 不得自動啟用；只有通過 gate 的 `Verified`／`Recovered` 值可由同步器補入空欄位。

## 標準流程

1. 先在 HeidiSQL 編輯 `god2_game` 或 `god2_player` 的明確欄位。
2. 直接 `UPDATE` 會逐欄寫入 `god2_game_meta.admin_change_audit`，並建立 `admin_field_locks`。
3. 執行 `scripts/Validate-God2GameCatalog.ps1`。
4. 執行 `scripts/Reload-God2GameplayCatalog.ps1` 建立並啟用已驗證 release。
5. 對已執行的 Server console 輸入 `reload gameplay`，重新建立 immutable snapshot；失敗時保留舊 snapshot。

Rebuild 使用 `scripts/Rebuild-God2GameCatalog.ps1`；顯示名稱快取可用 `scripts/Refresh-God2DisplayCaches.ps1` 重建。Migration 只用 `scripts/Apply-God2DatabaseMigrations.ps1` 的管理憑證。`Start_Server.bat` 會解開本機 DPAPI Runtime 憑證並以最小權限帳號啟動。換機或輪替密碼時，先準備 admin DPAPI secret，再執行 `scripts/Provision-God2DatabaseRuntimeAccounts.ps1`；腳本不輸出明文。

## 可熱重載與需重啟

物品、怪物、出生、掉落、NPC、商店、技能、狀態、任務、戰寵模板、神仙模板、陣型、配方、Server rate 與 battle rule 可走 Catalog reload。帳號憑證、資料庫連線、監聽 IP/Port、schema migration、角色結構欄位與 Runtime 程式碼修改需要 Server restart。

## 天堂私服表對照

| 天堂常見表 | God2 正式表 |
|---|---|
| `npc` | `npcs` + `monsters` |
| `mobskill` | `monster_skills` + `monster_ai_rules` |
| `spawnlist` | `monster_spawns` |
| `spawnlist_npc` | `npc_spawns` |
| `droplist` | `monster_drops` |
| `shop` | `merchant_inventory` |
| `skills` | `skills` + `skill_levels` + `skill_effects` + `status_effects` |
| `weapon`／`armor`／`etcitem` | `items` + `equipment` + `consumables` |
| `characters` | `god2_player.characters` |
| `mapids` | `maps` + `map_rules` + `portals` |

God2 是回合制，不直接套用天堂即時戰鬥模型；因此另外正規化回合冷卻、Buff tick phase、五行、陣型、行動規則、怪物回合 AI、戰寵、神仙與四項生活技能。

## 備份與回復

大改前先完整 dump 三個 canonical schema。誤改單欄時，從 `admin_change_audit.old_value` 確認原值，再以明確主鍵 `UPDATE` 回復；回復本身也會留下 Audit。確認後可刪除該欄的 `admin_field_locks`，再 Validate／Reload。不要刪除 Evidence 或已套用 migration，也不要修改已記錄 checksum 的 migration。
