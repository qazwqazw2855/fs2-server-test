# 回家客戶端待辦 — 2026-10-08

## 已完成
- SELL journal／跨程序復原：2/2 通過。
- Journal 保存失敗、保存後取消、不自動重試：3/3 通過。
- Host 設定拒絕與隔離啟動：5/5 通過。
- Host 帳號 god2_v2@172.17.0.1 可讀兩張 journal。
- BUY／SELL journal INSERT 權限測試：2/2 通過。
  使用 INSERT SELECT WHERE 1=0，插入均為 0 筆。
- 完整 request 寫入／讀回已由 synthetic fixture 驗證；
  尚未以 Host 帳號驗證完整資料寫入。

## 回家第一步：找原始證據
歷史 monitor 指向：
C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Artifacts\ClientInstrumentation\ElevatedAutomationHost\host-run-20260812-000800\attempt-759-trace

1. 確認目錄或備份是否存在。
2. 盤點原始 trace、Stage 4–6 匯出及 capture metadata。
3. 計算檔案 SHA256，核對 client build 與來源。
4. 核對 3954／6901、purchase index 7、sale slot 4→0 的證據。

## 原始證據找不到時
規劃獨立的台服正服實測批次：
- 保存 client build/hash、伺服器、NPC、商品及錄製工具資訊。
- 記錄購買／出售前後背包、數量、格位、餘額與操作時間。
- 保存原始 capture、操作紀錄及 SHA256。
- 與既有 CN_OFFICIAL 批次分開保存、分析。

## 後續 V2 客戶端驗收
須先完成 protocol/content 證據核對與 gate 審查，再安排：
- 隔離測試環境登入、開店、受支援範圍內 BUY／SELL。
- 核對畫面、背包、餘額、journal、receipt 與 audit。
- 關店、重連、過期互動與交易後斷線的行為。
- 不將已售出的交易視為新出售而自動重送。

目前 BUY／SELL evidence gates 均為 Blocked。
Host 接線預設關閉；這批變更未部署至正式 6001。
CN 1504／6906／出售數量 2 不得直接套用 Classic 3954／6901 profile。
