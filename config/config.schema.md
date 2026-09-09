# God2 Classic Server Config Schema

本文件列出 `config/*.json` 的正式營運設定。官方固定玩法與官方固定流程屬於 Core，不得放入 Config，也不得做成 Enable / Disable 或 Feature Switch。

| JSON | 欄位 | 型別 | 預設值 | 中文用途 |
| --- | --- | --- | --- | --- |
| server.json | name | string | God2 Classic Server | Console 顯示與服務辨識名稱。 |
| server.json | environment | string | Production | 標示唯一正式服務端執行環境。 |
| server.json | maximumPlayers | integer | 1000 | 同時在線玩家上限。 |
| database.json | host | string | 127.0.0.1 | MariaDB 主機 IP 或 DNS 名稱。 |
| database.json | port | integer | 3306 | MariaDB 連線 Port。 |
| database.json | databaseName | string | god2 | 正式遊戲資料庫名稱。 |
| database.json | username | string | god2_server | Server 使用的 MariaDB 帳號。 |
| database.json | password | string | 空字串 | Server 直接從 `config/database.json` 讀取的 MariaDB 密碼。 |
| database.json | passwordSource | string | ConfigValue | 舊版自動化工具相容欄位；正式 Server 不讀取。 |
| database.json | passwordEnvironmentVariable | string | GOD2_DB_PASSWORD | 舊版自動化工具相容欄位；正式 Server 不讀取。 |
| database.json | connectionTimeoutSeconds | integer | 2 | 資料庫連線逾時秒數。 |
| network.json | bindIp | string | 127.0.0.1 | Server 綁定 IP。 |
| network.json | loginPort | integer | 2592 | Login / World 合一 Runtime 的唯一 Game Endpoint Port；修改後必須重新啟動。 |
| network.json | worldPort | integer | 2596 | Legacy 相容欄位，正式 Runtime 不會 Bind；保留供舊設定檔讀取。 |
| network.json | maxFrameSize | integer | 4096 | 單一 application frame 最大位元組數；合法範圍 64 到 65535，修改後必須重新啟動。 |
| logging.json | logLevel | string | Information | 最低日誌等級。 |
| logging.json | console | boolean | true | 是否輸出日誌到 Console。 |
| logging.json | file | boolean | false | 保留欄位，目前僅接受 false；設為 true 會在啟動驗證階段拒絕。 |
| security.json | replayProtectionRequired | boolean | true | 強制封包重放防護，必須為 true。 |
| security.json | packetValidationRequired | boolean | true | 強制封包格式與安全驗證，必須為 true。 |
| security.json | authenticationValidationRequired | boolean | true | 強制帳號登入驗證，必須為 true。 |
| security.json | connectionLimit | integer | 1000 | 同時連線數上限。 |
| localization.json | defaultLanguage | string | zh-TW | 預設顯示語系。 |
| localization.json | fallbackLanguage | string | en-US | 缺少翻譯時使用的 fallback 語系。 |
| persistence.json | autosaveIntervalSeconds | integer | 300 | 玩家資料定期自動保存間隔秒數。 |
| persistence.json | backupIntervalMinutes | integer | 60 | 資料庫備份建議間隔分鐘數；0 表示不由 Server 排程備份。 |
| rates.json | experienceRate | decimal | 1.0 | 經驗倍率。 |
| rates.json | dropRate | decimal | 1.0 | 掉落倍率。 |

## 移除規則

以下設定不得存在於 `config/`：`features.json`、`character.json`、`combat.json`、`quest.json`、`merchant.json`、`immortal.json`、`battle-pet.json`。

以下欄位不得存在：`moneyRate`、`goldRate`、`currencyRate`、`goldDropRate`、`moneyDropRate`、`enableMoneyDrop`、`enableGoldDrop`、`enableCurrency`、`questReward`。
