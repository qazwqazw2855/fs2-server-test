# Server V2 收工與網站進度頁 SOP

每次 Server V2 收工要同步兩份不同的進度資料，並檢查網站實際回應。寫入一個 repository **不會**自動更新另一個 repository 的檔案。

## 資料來源與顯示路徑

1. 專案來源：`qazwqazw2855/fs2-server-test` 的 `refactor/server-v2:progress.json`。記錄當日成果、測試、待辦與保守的整體完成率。
2. 網頁資料：`qazwqazw2855/fs2db` 的 `main:public/server-v2-progress/progress.json`。兩個網站頁面都從這份 JSON 取摘要、百分比、測試資訊與清單：`public/server-v2-progress/index.html` 使用 `./progress.json`；`public/server-progress/index.html` 使用 `../server-v2-progress/progress.json`。兩個頁面還讀 `public/server-v2-progress/parity.json`。
3. 實際網站位於 AWS `/var/www/fs2db/public`；主機的 Git 工作樹可能停在 `feature/item-v2` 且有未提交修改，甚至完全缺少上述靜態頁面目錄。**不得以整站 git pull、checkout、reset 或 clean 代替部署進度檔。**

## 收工順序

1. 在 Server V2 repo 提交交接紀錄與 `progress.json`。註明 commit、實際測試結果及環境；隔離分支測試不得計入正式分支測試或直接提高完成率，無畫面測試不得說成客戶端畫面驗收。
2. 在 fs2db repo `main` 更新 `public/server-v2-progress/progress.json`，保持 `overall_percent` 有證據，`source_head` 指向當次 Server V2 正式分支進度 commit；`updated_at` 用台灣時間。顯示文字需區分「正式 6001」、「隔離 6002」及待驗證事項。
3. 在 AWS 檢查 `/var/www/fs2db` 的分支與 `git status --short`。可 `git fetch origin main` 取回網站 repo 新版本。若工作樹不是乾淨 `main`，只從 `origin/main` 提取進度頁需要的白名單檔案；先確認目標檔是否有其他人的本機修改。不存在的 `public/server-v2-progress`、`public/server-progress` 目錄可建立。不要碰其他應用程式或道具開發檔案。
4. 部署的白名單：`public/server-v2-progress/progress.json`；首次建頁或靜態檔缺失才補 `public/server-v2-progress/index.html`、`public/server-v2-progress/parity.json`、`public/server-progress/index.html`。複製前對新 JSON 執行 `jq -e` 檢查 `updated_at`、`source_head`、`overall_percent`；不覆蓋未檢視的本機差異。
5. **網站回應是最後一道完成條件**。從 AWS 以 `curl -fsS --max-time 15 "https://fs2db.com/server-v2-progress/progress.json?ts=$(date +%s)"` 取回資料，`jq` 檢查線上 `updated_at` / `source_head` / `overall_percent` 與剛部署檔一致；檢查 `/server-v2-progress/`、`/server-progress/` 的 HTTP 狀態及 `parity.json` 能載入。若 CDN/網站不可達、回傳舊資料或 JSON 無效，記錄「網站未驗證」，不可回報網站已同步完成。
6. 收工回報列出：Server V2 source commit、fs2db 網頁資料 commit、AWS 本機部署結果、線上 URL 核對結果、保留的進度百分比及下一個沒有 client 可做的工作。

## 2026-09-24 此次狀態

- Server V2 `refactor/server-v2` 進度 commit：`fa843fbe8b1ede1a9dbd7e292a07f07e33c17541`，overall 78%。
- fs2db `main` 網頁資料 commit：`619d6eb4b7fe4d1874380dca2efd37d9ba0e3a72`。
- 使用者在 AWS 的 `feature/item-v2` 工作樹建立並複製四個進度頁檔案，`jq` 驗證本機 `updated_at=2026-09-24T00:44:36+08:00`、overall 78%、`source_head=fa843fbe...`。
- **線上 URL 尚未取得成功的回應核對**；必須完成第 5 步才能把網站標記為已驗證同步。
