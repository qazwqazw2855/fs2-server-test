# Official Restoration Policy

God2 Classic Server 的唯一目標是還原官方遊戲。所有玩法規則、資料、流程與協議都必須以官方 Client Recovery 與已恢復官方資料為依據。

不得建立假 Monster、假 NPC、假 Item、假 Quest、假 Reward、假 Immortal 或假 Battle Pet。尚未完成完整交叉掃描的類別使用 `NeedsRecovery` 狀態保留 importer 介面與空資料，不自行編造內容。

Official Client 是目前唯一官方資料來源。Recovery 不得只分析單一檔案，也不得因找不到單一檔案就宣稱 `Missing`；必須持續執行 DAT、Lua、Script、Resource、Localization、Model、Packet、Runtime、NPC、Quest、Dialog、Item、Skill 的交叉比對。

官方固定存在的功能全部屬於 Core，不得做成 Enable、Disable、Feature Switch 或 Config 開關。

官方金錢流程固定為：

Monster -> Drop Item -> Player Pickup -> Merchant -> Sell Item -> Gold

怪物不得直接掉金幣，不得新增金幣掉落倍率或金幣掉落開關。
