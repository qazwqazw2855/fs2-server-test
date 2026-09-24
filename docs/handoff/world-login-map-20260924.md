# 2026-09-24：V2 World Login Map Identity 隔離驗證

## 已確認

- 角色 `test001`（ID 1）正式資料庫地圖為 `170015007` 九天冰屋，客戶端地圖身分為 Area 15 / Map 7。
- `WorldLoginMapIdentityRepository` 已可由 Formal DB 取得 `client_build_id`、`client_area_id`、`client_map_id` 與座標 bounds。
- Official map catalog 共 **144 張**；其中 **143 張啟用、1 張停用**（Area 7 / Map 8）。另有一張已啟用的既有 Map 19 室內圖（`557790525`）。因此目前已啟用且屬於當前 build 的正式 DB map 共 **144 張 = 143 張官方地圖 + 1 張室內圖**，與官方 catalog 的 144 張屬不同集合。
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

- 官方 catalog 的 Formal identity 資料覆蓋：**144/144**（含一張停用地圖）。production World Login repository 只回傳 enabled map；目前可讀取的 **144 張 enabled map = 143 張官方地圖 + 1 張 Map 19 室內圖**。
- World Login production Bootstrap：**1772 bytes**。
- World Login 不再依賴 Portal destination whitelist。
- Portal wire projection：目前仍只有 **6 個 evidence-backed destination identities**，不得擴張成 144/144。
- 不得將兩個不同的 144 張集合混為一談，也不得用 World Login identity 資料覆蓋宣稱 Portal 144/144 或實機地圖載入完成。
- 無新的正服證據前，不推測 Portal 的 PositionMode、PreludeState 或 MapTransitionFirst。

## 2026-09-24 Portal Runtime 觸發路徑稽核

- `TcpGameServer` 目前只在成功解碼 `PortalActivate` 後呼叫 `portalRouteService.ResolveAsync`，再做 destination codec 與 `WorldMapTransitionService.TryTransitionAsync`。
- `WorldMovement` 路徑只檢查序號與 bounds、更新位置／presence、回傳 ACK；該路徑沒有呼叫 Portal route resolver 或 transition。Portal 2–4 的 DB route／codec integration tests 僅證明資料可讀、指定座標可解析與目的地可編碼，**不證明移動進入口會切圖**。
- 舊 `OfficialPortalRouteCatalog` 將 Portal 1 分類 `ExplicitActivation`，Portal 2–4 分類 `MovementRegion`；這是舊 runtime 的標籤，尚不足以證明 V2 收到的原版 Client movement／activate 事件序列。維持 production 程式不變，先取得對應原始 Client 觸發封包或可重現的實機紀錄。
- GitHub 搜尋 Portal 2–4 capture ID，只找到 codec／舊 route catalog 的引用，沒有包含觸發順序的原始 trace。Migration 109/127 將 Portal 2 的 `trigger_evidence_status` 設為 `Verified`，Portal 3／4 設為 `Derived`；舊 `MariaDbWorldContentRepository` 以 `SourceRadius > 0` 產生 `MovementRegion` 標籤。這些欄位與標籤不能替代 Client 封包時序驗證。

## 尚未完成

- Windows 原版 Client 實際登入九天冰屋及其他地圖的畫面驗收。
- 多張不同 Area/Map 的 real-client World Entry acceptance。
- Portal 1 入口 `(252,397)`、來源半徑 `0` 與 Map 3 正式 bounds `(0..251,0..251)` 衝突。2026-09-24 唯讀查詢正式資料庫 `god2_game.portals` JOIN `god2_game.maps`，取得 `source_in_bounds=0`。Migration 083 的備註只記載切圖前最後移動座標 `(252,397)`，尚未核對原始封包／座標系；Portal 1 來源觸發驗收受阻。保留現有資料，不推定更正座標或擴張 bounds。
- 舊 `OfficialPortalRouteCatalog` 將 Portal 1 標為 `ExplicitActivation`，引用 `PortalCapture/portal-verified-20260811-151345`。`Automation/State/portal-elevation-run.txt` 指向舊 Windows `C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Artifacts\PortalCapture\portal-verified-20260811-151345`；2026-09-24 使用者在 AWS `/home/ubuntu/games` 搜尋該檔名無結果。這些索引尚未提供原始封包，不能據以解決 bounds 衝突。
- 正式分支合併與 6001 production deployment 尚未進行。

## 下一步

1. World Login 與 Portal 證據鏈已分別寫入 `docs/parity/official-parity-baseline.md`；維持此邊界。
2. 搜尋並核對 Portal 1 原始切圖／移動封包與 Map 3 座標系，確認 `(252,397)` 是否可作來源觸發位置；在此之前不得修改路線或 bounds。另對 Portal 2–4 蒐集實際來源觸發事件，確認 V2 是否需要新增 movement-trigger dispatch，避免把 route／codec 單元驗證誤認為完整切圖。
3. 取得 exact-current 客戶端檔案後，核對 65 筆 disabled staging portal links 的來源檔案與觸發證據。
4. 回到可執行原版 Windows Client 的環境後，以隔離埠驗收 World Entry 畫面及 Portal 1–4 真實切圖；驗收前不宣稱 144 張地圖皆可正常顯示，亦不宣稱 Portal 1–4 已通過實機觸發。

## 2026-09-24 無 Client 切圖一致性驗證

- 隔離 checkout 執行 `WorldMapTransitionServiceTests`：**11/11 通過**。涵蓋目的地 NPC 載入、持久化衝突、NPC 互動清理與既有 presence／replication 行為。
- 設定 `GOD2_RUN_DB_INTEGRATION=1`，執行 `StaleCharacterMapTransitionWrite_IsRejectedWithoutMutation` 與 `CurrentCharacterMapTransitionWrite_SucceedsAndRollsBack`：**2/2 通過**；後者在交易內驗證寫入並回滾，不應表述為正式角色已切圖。
- `Runtime_version_change_after_database_commit_reports_inconsistent_state` 以測試 writer 主動改動 presence，證實「DB 已提交但 registry 版本不符」時服務會拋出明確例外。這是防護／故障注入測試，**不是正式連線曾發生該競態的證據**。
- 現行 `TcpGameServer` 對單一連線在同一 world-frame 讀取迴圈依序 await Portal 切圖與移動；離線清理位於該處理流程的 `finally`。因此不能僅憑故障注入測試要求新增角色鎖或宣稱同連線會在 DB 寫入期間處理下一筆移動。若後續引入跨任務 presence 更新，須先建立真正的併發呼叫路徑與重現測試，再設計跨 DB／registry 的一致性處理。
- 未持有 `GOD2_TEST_PASSWORD`，本輪沒有啟動 6002 LoginProbe；6001 service 與正式分支未改動。Portal 的原版 Client 觸發序列與視覺驗收仍待證據。

## 2026-09-25 Portal 1 可達性檢查

- `0d91e43` 的唯讀 baseline 從正式 DB 回報：五條 enabled route 中，只有 Portal 1 的來源中心超出來源地圖 bounds；五條目的地都在 bounds 內。Portal 1 來源中心 `(252,397)`、半徑 `0`，Map 3 正式 bounds 為 `x=0..251,y=0..251`。
- 在目前 V2 實作，`TcpGameServer` 的 World Login identity 檢查拒絕 bounds 外的登入位置，WorldMovement 也在更新 presence 之前拒絕 bounds 外座標；`PortalActivate` 只用現有 world presence 位置呼叫 `PortalRouteService.ResolveAsync`，該服務以 `max(abs(dx),abs(dy)) <= SourceRadius` 判定命中。因此在這些 gate 啟用且 presence 只經正常 World Login／Movement 更新的條件下，Portal 1 不可能以來源中心 `(252,397)` 命中。這是目前資料與 V2 控制流程的推論，不是正服 client 的觸發時序證據。
- 未核對原始移動／切圖封包與座標系前，不修改 Portal 1 資料、Map 3 bounds 或 WorldMovement gate，也不將 Portal 1 列為已通過真實切圖驗收。Portal 2–4 的移動觸發語意仍待實機證據。

## 2026-09-24 Portal build 與逐張 World Login DB 驗證

- `d9ba47f` 在 Portal route 兩端新增 current client build 比對，於世界切圖寫入前拒絕不符路線；隔離 checkout 的 Network.Tests **80/80 通過**，但尚未以原版 Client 驗收該拒絕分支。
- 唯讀 DB 查詢與 `a84c5fb` 的整合測試確認目前五條已啟用 Portal（1、2、3、4、170015007）的來源及目的地 build 均為 `god2-opt-6b127086e0c0`；整合測試 **1/1 通過**。
- `e300d24` 的整合測試逐張呼叫 production `MariaDbWorldLoginMapIdentityRepository`：**144 張 enabled map 均可讀取 current build identity 與有效 bounds**，停用的 Area 7 / Map 8 不會回傳；AWS 測試 **1/1 通過**。這是資料層驗證，不等於原版 Client 已載入每張地圖。
