# Official Client Recovery Permanent Policy

Official Client 是目前唯一官方資料來源。God2 Classic Server 的資料工作目標是 Extract Everything Recoverable，直到沒有任何可恢復官方資料為止。

## Recovery 原則

- 不得因找不到單一檔案就宣稱 `Missing`。
- 必須持續執行 Cross Reference、Container Scan、Runtime Scan、Resource Scan、String Scan、Identifier Scan。
- 所有新增資料必須有官方來源證據，不得自行編造 NPC、Monster、Item、Immortal、Battle Pet 或任何官方資料。
- `Missing` 只能在完整交叉掃描後確認沒有官方資料時使用；掃描未完成時使用 `NeedsRecovery`。

## Recovery Scope

正式 scope 至少包含：

Map、Portal、NPC、Monster、Spawn、Item、Equipment、Skill、Quest、Merchant、Dialog、Drop Table、Localization、Immortal、Battle Pet、Reward、Animation、Model、Texture、Icon、Sound、Music、Effect、Script、Lua、Container、Resource Table、String Table。

任何 Official Client 可恢復資料都不得忽略。

## Cross Reference

Recovery 不得只分析單一檔案。必須交叉比對：

DAT、Lua、Script、Resource、Localization、Model、Packet、Runtime、NPC、Quest、Dialog、Item、Skill。

Reward 若沒有獨立資料表，必須從 Quest、NPC、Dialog、Script 交叉拆分，不得因沒有獨立 reward 檔案就停止。

Immortal 與 Battle Pet 必須持續搜尋所有官方資源；不得因沒有 `immortal.dat` 或單一資料檔就停止。

## Inventory

每一類 Recovery inventory 至少包含：

- source
- format
- recordCount
- recoveredFields
- missingFields
- verificationStatus
- recoveryProgress

`db/imports/official/` 中每個類別都必須有對應 importer。空資料類別仍需保留 inventory 與 importer 介面，但狀態必須是 `NeedsRecovery`，不得放假 record。
