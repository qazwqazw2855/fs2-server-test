# God2 Classic Server Permanent Architecture Policy v1

God2 Classic Server 是唯一正式服務端，目標是 Official Restoration：還原官方遊戲，不是自創 MMORPG，也不是魔改版。

## 技術定位

- C# / .NET 10 LTS
- MariaDB 是唯一正式資料權威
- 單一 ConsoleHost
- 單一 `Start_Server.bat`
- Login / World 合一
- UTF-8 / Unicode
- 專案內容只使用相對路徑，不寫死本機使用者目錄絕對路徑

不得建立 `LoginServer.exe`、`WorldServer.exe`、GUI Control Center、多個 Console、多個 Server Process。

## Core Policy

以下官方固定存在的功能與流程全部屬於 Core，不得做成 Config、Enable / Disable 或 Feature Switch：

Account、Character、Inventory、Equipment、Item、NPC、Monster、Map、Portal、Spawn、Movement、Battle、Skill、Quest、Merchant、Drop、Login、World、Runtime、Packet、Authentication、Visibility、Persistence、Immortal、Battle Pet、Localization Runtime。

## Config Policy

`config/` 只保留：

- `server.json`
- `database.json`
- `network.json`
- `logging.json`
- `security.json`
- `localization.json`
- `persistence.json`
- `rates.json`

`rates.json` 目前只保留 `experienceRate` 與 `dropRate`。

不得存在：`moneyRate`、`goldRate`、`currencyRate`、`moneyDropRate`、`goldDropRate`、`enableMoneyDrop`、`enableGoldDrop`、`enableQuest`、`enableMonster`、`enableNPC`、`enableMerchant`、`enableItem`、`enableBattlePet`、`enableImmortal`，以及任何官方固定功能開關。

## Data Policy

正式遊戲資料全部必須來自 Official Client Recovery，不得建立假 Monster、假 NPC、假 Item、假 Quest。

Official Client 是目前唯一官方資料來源，Recovery 目標是 Extract Everything Recoverable。不得因找不到單一檔案就宣稱 `Missing`；必須完成 Cross Reference、Container Scan、Runtime Scan、Resource Scan、String Scan、Identifier Scan 後才可確認缺失。掃描尚未完成時使用 `NeedsRecovery`。

官方金錢規則依官方流程：

Monster -> Drop Item -> Player Pickup -> Merchant -> Sell Item -> Gold

怪物不得直接掉金幣。

## Directory Policy

`db/` 只存放：

- `imports/`
- `official/`
- `converted/`
- `legacy/`
- `exports/`
- `backups/`
- `snapshots/`

`database/` 只存放：

- `schema/`
- `migrations/`
- `seeds/`

## Encoding Policy

整個 Server 使用 UTF-8 / Unicode。不得依賴 Windows Code Page、Windows 系統語系或 Windows 地區設定。正式支援 Windows 10 x64 與 Windows 11 x64，包括繁體中文與簡體中文 Windows，不得產生亂碼、Encoding Error 或 Question Mark。

## Configuration JSON

全部 JSON 必須：

- UTF-8
- 4 spaces
- 繁體中文
- 不得 BOM
- 包含 `_comment`、`_version`、`_example`
- 每一個正式欄位都有 `_欄位名稱`
- 中文說明至少包含用途、預設值、可接受範圍、是否需要重新啟動

## Migration Policy

Server 啟動自動執行 `database/schema/` 內所有 migration。第二次啟動只比對 `__SchemaVersion`，不得重建 table。任何 migration 失敗不得 Ready，也不得啟動 Network Host。
