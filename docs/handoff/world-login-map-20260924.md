# 2026-09-24 收工：V2 登入地圖定位隔離驗證

## 已確認

- 角色 `test001`（ID 1）正式資料庫地圖為 `170015007` 九天冰屋，客戶端地圖身分是 Area 15 / Map 7。原本固定的 1772-byte World Bootstrap 會顯示類似雜貨店的室內畫面；資料庫地圖與畫面錯位。
- 隔離分支 `codex/world-login-map-identity` 讀取正式地圖的 `client_build_id`、`client_area_id`、`client_map_id` 與座標界線。只有舊版協定中已有目的地證據的地圖可組出登入定位封包；未知目的地拒絕，沒有推測封包。
- AWS 隔離工作樹 `~/games/fs2-world-login-test`：Protocol.Tests **110/110**、Network.Tests **80/80** 通過；Host Release build 成功。
- 2026-09-23 16:44:36 UTC，以新分支啟動 **127.0.0.1:6002**，執行唯讀 LoginProbe：登入、選角、World Handshake 成功；固定 Bootstrap **1772 bytes**，新增地圖定位後總計 **1790 bytes**；解碼結果為 **7:15 / (17,15)**，伺服器日誌顯示 `character=1; map=170015007`。Probe 未重設角色、未發移動封包，結束時關閉隔離伺服器。
- 正式 `refactor/server-v2` 分支、正式 6001 systemd 服務及現有 Launcher 均未套用此隔離分支。**目前僅證明無畫面封包與地圖對應正確，尚未證明 Windows 客戶端實際載入九天冰屋或主世界。**
- Portal 1（崑崙仙界 → 雜貨店）入口 `(252,397)` 超過崑崙仙界正式資料庫界線 `(0..251,0..251)`；另外四條已啟用 Portal 入口和出口均在各自界線內。此衝突尚未修正，不能把該入口用作登入測試點。

## 下一步（公司無 client 可執行）

1. 對隔離分支加入 Network 層測試：成功地圖定位、未知 client build / 未驗證 map / 超出 bounds 時拒絕，並檢查連線結束與 Session 清理。檢查持久化查詢對缺值及停用地圖的行為。
2. 檢查 6002 Probe 的實測座標 `(17,15)` 與正式 DB 最新位置一致；以唯讀查詢核對，避免將舊的 `(14,14)` 當作當前值。
3. 檢查現有 `EncodeWithVerifiedLocation` 是否在 bootstrap 後、NPC Spawn 前送出與舊版實測一致的順序，並評估登入失敗發生於 WorldPresence 註冊後的清理行為。
4. 處理 Portal 1 界線與實測入口來源衝突，但無新正服證據前保持現有數據不動。

## 回到有 client 的環境

- 在隔離埠進行真正的 Windows 畫面驗收，確認畫面地圖、伺服器地圖與 DB 位置一致；再測登出重登和傳送前後持久化。
- 驗收合格後才考慮合併正式分支與更新 6001。不得僅因 headless Probe 通過就宣稱主世界可玩。
