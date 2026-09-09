# God2 Classic Server Configuration

God2 Classic Server 是 Official Restoration。Config 只保留管理員真正需要調整的營運設定；官方固定存在的系統、流程與規則屬於 Core，不得做成 Enable / Disable 或 Feature Switch。

## 保留的 Config

- `server.json`：Server 名稱、執行環境、同時在線玩家上限。
- `database.json`：MariaDB 主機、Port、資料庫、帳號與密碼；Server 直接讀取此檔案，不使用系統環境變數覆蓋。
- `network.json`：統一 Game Endpoint 的 Bind IP、Login Port、Legacy WorldPort 相容欄位，以及 application frame 大小上限。
- `logging.json`：日誌等級與 Console 輸出；`file` 為保留欄位，目前僅接受 `false`。
- `security.json`：封包重放防護、封包驗證、登入驗證、連線上限。
- `localization.json`：預設語系與 fallback 語系。
- `persistence.json`：自動保存間隔與備份建議間隔。
- `rates.json`：只保留 `experienceRate` 與 `dropRate`。

## 已移除的 Config

以下檔案不再存在，因為它們代表官方固定玩法或官方固定流程，不是營運開關：

- `features.json`
- `character.json`
- `combat.json`
- `quest.json`
- `merchant.json`
- `immortal.json`
- `battle-pet.json`

## Core Policy

Account、Character、Inventory、Equipment、Item、NPC、Monster、Map、Portal、Spawn、Movement、Battle、Skill、Quest、Merchant、Drop、Login、World、Runtime、Packet、Authentication、Visibility、Persistence、Localization Runtime、Immortal、Battle Pet 全部屬於 Core 或 Importer/MariaDB 管理範圍。

不得新增 `moneyRate`、`goldRate`、`currencyRate`、`goldDropRate`、`moneyDropRate`、`enableMoneyDrop`、`enableGoldDrop`、`enableCurrency`、`questReward`。

## JSON 規格

全部 JSON 必須使用 UTF-8、4 spaces、繁體中文說明、不得 BOM。每個 JSON 必須包含 `_comment`、`_version`、`_example`，且每一個正式欄位都必須有對應的 `_欄位名稱` 中文用途說明。
