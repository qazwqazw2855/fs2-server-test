# 2026-09-24 Item / Inventory 隔離基線

## 資料庫唯讀盤點

- `god2_game.items` 有 **17,407** 筆，`item_id` 欄位型別為 `BIGINT`；目前最小 ID 為 `21,323`，最大 ID 為 `2,147,483,157`，距離有號 Int32 上限尚有 **490**。目前沒有 ID 超過 Int32 範圍。
- 所有 **17,407** 筆的 `maximum_stack` 均為 `NULL`，沒有 disabled item。SQL `SUM(maximum_stack <= 0)` 回傳 `NULL`，是因為比較式對每列皆為 `NULL`，**不代表存在負數或零值**。
- `god2_player.player_inventory_state` 目前有 1 列，`character_inventory` 目前有 1 列；有效格位的數量不為空／非正數、缺背包狀態、超出容量、缺 item 與 disabled item 稽核計數均為 **0**。
- 現行 `ItemStackRule` 在 `maximum_stack IS NULL` 時保守使用 effective maximum 1。**不得把全欄空值解釋成官方證實 17,407 種物品均不可堆疊**；在獲得 exact-current 或其他可追溯官方使用／堆疊證據前，不填入猜測值、不啟用物品堆疊／背包 mutation。

## 程式驗證

- 隔離分支 `codex/item-inventory-baseline` 由 `refactor/server-v2` 建立，`97ffbc3` 將 `MariaDbItemStackRuleRepository` 讀取 `BIGINT item_id` 的 `GetInt32` 改成 `GetInt64`，符合 `ItemStackRule.ItemId` 的 `long` 型別。這是型別一致性與未來 ID 範圍修正，**尚無證據顯示目前 catalog 已因 ID 溢位失敗**。
- AWS 隔離 checkout 編譯成功，既有 `MeatItem_NullMaximumStack_IsSingle` 測試 **1/1 通過**。該測試使用既有小 ID，不能證明超過 Int32 的真實 ID 已在資料庫測過。
- 未觸及 Portal／World 測試分支、正式 `refactor/server-v2` 或 6001 systemd 服務。

## 後續證據門檻

1. 取得可追溯的官方 stack capacity／item usage 設定，釐清每一類物品是否可堆疊及最大量；分類、名稱或舊伺服器預設不可直接當作 current build 的真值。
2. 再以交易回滾的整合測試驗證 item quantity、slot capacity 與版本併發規則，設計 mutation；在 client 更新封包與背包實機驗收前，不宣稱 Inventory 閉環完成。
