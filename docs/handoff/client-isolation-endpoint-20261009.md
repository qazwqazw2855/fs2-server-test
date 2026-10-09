# God2 V2 客戶端隔離與端點分析交接 — 2026-10-09

## 工作環境

- Branch: codex/inventory-grant-20261003
- 分析工具基線 Commit: fca0d2c
- AWS 隔離服務目標：127.0.0.1:6002
- 隔離資料庫：127.0.0.1:3309
- 正式 6001 與既有資料庫不動
- Windows 原始客戶端 E:\God2 保持不變
- Windows 隔離副本 E:\God2-V2-Isolated-Test
- VMware Host-only 隔離 VM，尚未執行客戶端

## 已完成：Exact Client Endpoint

工具：
tools/God2.ExactClientEndpoint/

- 指定客戶端 SHA256：
  6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC
- 17 項有界控制流程測試 PASS
- 30 項安全停止案例 PASS
- PE Import 確認 CommandLineToArgvW、GetProcAddress、LoadLibraryA
- ws2_32.dll ordinal-57 存在，但未證明實際 socket 呼叫
- connect 字串命中 CoDisconnectObject，非 socket 證據
- 8 項原始位址參照搜尋皆為 0
- 靜態入口追蹤至 RVA 0x645001 的 pusha
- 後續控制流程 UNKNOWN
- EndpointPrecedence = UNKNOWN
- 未執行遊戲、未進行網路操作

## 已完成：LoginServer RoundTrip

工具：
tools/God2.LoginServerRoundTrip/

- 25 項 fail-closed 測試 PASS
- V2 LoginServer.csvZ RoundTrip PASS
- Legacy LoginServer.csvZ RoundTrip PASS
- 記憶體內 IP/Port 替換與 RoundTrip PASS
- 未產生新的 csvZ，原始檔未修改

## Launcher 已知限制

依目前檢視的 Launcher 原始碼：

- 直接啟動 God2_opt.exe
- 啟動參數包含 52.63.34.162
- V2EndpointService 驗證內建 LoginServer.v2.csvZ
- 隔離設定檔 SHA256 與內建資源不同
- 尚未確認遊戲如何使用啟動參數
- 尚未排除 God2_optmgr.exe 的間接依賴

## 安全限制

- Defender 曾將 God2_optmgr.exe 偵測為 Ramnit 相關威脅
- 不還原、不放行、不執行可疑程式
- 不直接執行隔離遊戲客戶端
- 不接觸正式 6001
- 不修改正式資料庫
- 不以靜態證據宣稱已確認實際連線行為

## 尚未解決

1. God2_opt.exe 實際端點選擇流程
2. 啟動參數與 LoginServer.csvZ 的優先順序
3. God2Con.csvZ 是否參與端點選擇
4. God2_optmgr.exe 是否存在間接依賴
5. 原始台服商店交易 capture 證據核對

## 下一階段

- 保留 EndpointPrecedence = UNKNOWN
- 優先研究不執行客戶端的靜態證據
- 不為了測試而繞過 Defender
- 未完成證據與安全審查前，不啟動客戶端
- BUY／SELL evidence gates 繼續維持 Blocked
- 不將 CN 官方資料直接視為台服正服證據

## Git 注意事項

- 既有未追蹤 merchant / journal 測試證據不得刪除
- 本交接文件建立後，需獨立檢查差異
- 未經確認不提交、不推送、不更新 progress.json
