# Game Data Import Policy

目前採用 Recovered First, Verified Later。

只要資料確實來自 Official Client Recovery，就可以先匯入 MariaDB。未經官方 recovery 驗證的 gameplay 欄位必須保留為 `NULL`、`Unknown` 或原始 payload，不得自行猜測官方數值。

Official Client 是唯一官方資料來源。Recovery 必須 Extract Everything Recoverable，且不得因找不到單一檔案就宣告 `Missing`。

正式流程：

Official Client Recovery -> Inventory Scan -> Source Preservation -> Converter -> Validator -> Importer -> MariaDB Cache/Log -> Formal Runtime Tables -> Import Verification -> Runtime Cache

Importer 必須支援可重複執行、transaction、upsert、import batch、source path、source hash、original id、`RecoveryStatus`、匯入結果輸出。

每一項 inventory 必須保留 `source`、`format`、`recordCount`、`recoveredFields`、`missingFields`、`verificationStatus`、`recoveryProgress`。尚未完成交叉掃描的類別使用 `NeedsRecovery`，不得使用 `Missing` 當作搜尋終點。

## Runtime Data Source

正式 Runtime Data Source 是正式資料表：

- `maps`
- `portals`
- `items`
- `skills`
- `npcs`
- `monsters`
- `quests`
- `merchants`
- `dialogs`
- `drop_tables`
- `localization_entries`

`official_import_categories`、`official_import_records`、`official_import_reference_issues` 僅保留作為 Import Cache 與 Import Log，不得成為正式 runtime data source。

Recovery data 匯入正式資料表時，`RecoveryStatus` 預設為 `Recovered`。未驗證欄位保留 `NULL` 或完整 `PayloadJson`，不得建立假 NPC、假 Monster、假 Item、假 Quest、假 Immortal 或假 Battle Pet。

目前已完成正式表 live verification：

- `items`: 17,407
- `monsters`: 208
- `npcs`: 319
- `quests`: 418
- `maps`: 69
- `portals`: 68
- `merchants`: 16
- `skills`: 1,108
- `dialogs`: 18
- `drop_tables`: 208
- `localization_entries` with `Language = 'official-recovery'`: 34
