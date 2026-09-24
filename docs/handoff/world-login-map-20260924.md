# 2026-09-24：V2 World Login Map Identity 隔離驗證

## 已確認

- 角色 `test001`（ID 1）正式資料庫地圖為 `170015007` 九天冰屋，客戶端地圖身分為 Area 15 / Map 7。
- `WorldLoginMapIdentityRepository` 已可由 Formal DB 取得 `client_build_id`、`client_area_id`、`client_map_id` 與座標 bounds。
- Official map catalog 共 144 張；144 張 enabled map 全部具備 World Login repository 所需 identity 與 bounds，正式資料層覆蓋為 **144/144**。
- 先前 World Login 會呼叫 `OfficialPortalWireCodec.SerializeWorldProjectionDestination()`，因此錯誤受到 Portal 六張已驗證 destination whitelist 限制。此限制來自 World Login 對 Portal serializer 的實作耦合，不代表正服 World Login 只有六張地圖可登入。
- commit `ad05cbf` 已將 production World Login 與 Portal wire projection 解耦。World Login 仍保留 Formal Map identity 與座標 bounds 驗證，但不再於 Bootstrap 後追加 Portal `0x61/0xBB` projection。
- Portal serializer、六張已驗證 destination、PositionMode、PreludeState、MapTransitionFirst 等 Portal evidence gate 均未修改；Portal 與 World Login 維持不同證據鏈。
- 隔離埠 `127.0.0.1:6002` LoginProbe 已驗證：登入、選角、World Handshake 成功，World Bootstrap 完整接收 **1772 bytes**，沒有追加 Portal projection；Server 成功進入 `InWorld`，日誌顯示 `character=1; map=170015007`。
- Probe 已配合新 production 行為，不再等待舊的 12-byte `0x61` + 6-byte `0xBB` Portal projection。
- Network.Tests **80/80** 通過；完整 solution test 成功；`git diff --check` 通過。
- 分支 `codex/world-login-map-identity` 已推送至遠端，最新功能 commit 為 `ad05cbf fix: decouple world login from portal wire projection`。
- 正式 `refactor/server-v2`、正式 6001 systemd 服務與 Launcher 尚未套用此隔離分支。
- Headless Probe 成功只證明 Server 的 World Login identity/bounds gate、1772-byte Bootstrap 與 InWorld 流程成立；**尚未證明 Windows 原版 Client 在任意 144 張地圖都能正確載入畫面。**

## World Login / Portal 證據邊界

- World Login Formal identity coverage：**144/144**。
- World Login production Bootstrap：**1772 bytes**。
- World Login 不再依賴 Portal destination whitelist。
- Portal wire projection：目前仍只有 **6 個 evidence-backed destination identities**，不得擴張成 144/144。
- 不得用 World Login 144/144 宣稱 Portal 144/144；也不得用 Portal 6/144 反向限制 World Login Formal identity coverage。
- 無新的正服證據前，不推測 Portal 的 PositionMode、PreludeState 或 MapTransitionFirst。

## 尚未完成

- Windows 原版 Client 實際登入九天冰屋及其他地圖的畫面驗收。
- 多張不同 Area/Map 的 real-client World Entry acceptance。
- Portal 1 入口 `(252,397)`、來源半徑 `0` 與 Map 3 正式 bounds `(0..251,0..251)` 衝突。2026-09-24 唯讀查詢正式資料庫 `god2_game.portals` JOIN `god2_game.maps`，取得 `source_in_bounds=0`。Migration 083 的備註只記載切圖前最後移動座標 `(252,397)`，尚未核對原始封包／座標系；Portal 1 來源觸發驗收受阻。保留現有資料，不推定更正座標或擴張 bounds。
- 舊 `OfficialPortalRouteCatalog` 將 Portal 1 標為 `ExplicitActivation`，引用 `PortalCapture/portal-verified-20260811-151345`。`Automation/State/portal-elevation-run.txt` 指向舊 Windows `C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Artifacts\PortalCapture\portal-verified-20260811-151345`；2026-09-24 使用者在 AWS `/home/ubuntu/games` 搜尋該檔名無結果。這些索引尚未提供原始封包，不能據以解決 bounds 衝突。
- 正式分支合併與 6001 production deployment 尚未進行。

## 下一步

1. World Login 與 Portal 證據鏈已分別寫入 `docs/parity/official-parity-baseline.md`；維持此邊界。
2. 搜尋並核對 Portal 1 原始切圖／移動封包與 Map 3 座標系，確認 `(252,397)` 是否可作來源觸發位置；在此之前不得修改路線或 bounds。
3. 取得 exact-current 客戶端檔案後，核對 65 筆 disabled staging portal links 的來源檔案與觸發證據。
4. 回到可執行原版 Windows Client 的環境後，以隔離埠驗收 World Entry 畫面及 Portal 1–4 真實切圖；驗收前不宣稱 144 張地圖皆可正常顯示，亦不宣稱 Portal 1–4 已通過實機觸發。
