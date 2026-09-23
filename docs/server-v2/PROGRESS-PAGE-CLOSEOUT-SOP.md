# Server V2 收工與網站進度頁 SOP

每次 Server V2 收工要同步兩份不同的進度資料，並檢查網站實際回應。寫入一個 repository **不會**自動更新另一個 repository 的檔案。

## 資料來源與顯示路徑

1. 專案來源：`qazwqazw2855/fs2-server-test` 的 `refactor/server-v2:progress.json`。記錄當日成果、測試、待辦與保守的整體完成率。
2. 網頁資料：`qazwqazw2855/fs2db` 的 `main:public/server-v2-progress/progress.json`。網站進度顯示讀取此 JSON。AWS 若已有 Laravel 的 `/server-progress` 路由及自訂 Blade 頁面（含右側分頁），保留原頁面及路由；GitHub 的 `public/server-progress/index.html` 只是另一套靜態備用頁面，不可部署到該 URL 覆蓋既有頁面。
3. 實際網站位於 AWS `/var/www/fs2db/public`；主機的 Git 工作樹可能停在 `feature/item-v2` 且有未提交修改，而且可能已有尚未提交的 Laravel 進度頁與右側分頁。**不得以整站 git pull、checkout、reset 或 clean 代替部署進度檔。**

## 收工順序

1. 在 Server V2 repo 提交交接紀錄與 `progress.json`。註明 commit、實際測試結果及環境；隔離分支測試不得計入正式分支測試或直接提高完成率，無畫面測試不得說成客戶端畫面驗收。
2. 在 fs2db repo `main` 更新 `public/server-v2-progress/progress.json`，保持 `overall_percent` 有證據，`source_head` 指向當次 Server V2 正式分支進度 commit；`updated_at` 用台灣時間。顯示文字需區分「正式 6001」、「隔離 6002」及待驗證事項。
3. 在 AWS 檢查 `/var/www/fs2db` 的分支與 `git status --short`。可 `git fetch origin main` 取回網站 repo 新版本。若工作樹不是乾淨 `main`，先以 `php artisan route:list --path=server-progress` 與 `rg -n 'server-progress|ServerProgressController' routes/web.php` 確認原網站進度路由；只提取所需資料檔，先確認目標檔是否有其他人的本機修改。部署 JSON 時只需建立 `public/server-v2-progress` 目錄；有 Laravel `/server-progress` 路由時不可建立 `public/server-progress` 靜態目錄。不要碰其他應用程式或道具開發檔案。
4. 例行部署白名單：`public/server-v2-progress/progress.json`；若原頁面確實使用 `parity.json`，才另外同步 `public/server-v2-progress/parity.json`。只有確認該 URL 沒有 Laravel 路由、也沒有原本的自訂頁面，才可另行評估部署靜態 HTML。複製前對新 JSON 執行 `jq -e` 檢查 `updated_at`、`source_head`、`overall_percent`；不覆蓋未檢視的本機差異。
5. **網站回應是最後一道完成條件**。從 AWS 以 `curl -fsS --max-time 15 "https://fs2db.com/server-v2-progress/progress.json?ts=$(date +%s)"` 取回資料，`jq` 檢查線上 `updated_at` / `source_head` / `overall_percent` 與剛部署檔一致；檢查 `/server-v2-progress/`、`/server-progress/` 的 HTTP 狀態；瀏覽 `/server-progress/` 確認右側分頁仍在、百分比與 JSON 一致、沒有 `Cannot read properties of undefined`，並確認頁面依賴的 `parity.json` 可載入。HTTP 200 和 JSON 成功並不能替代頁面驗收。若 CDN/網站不可達、回傳舊資料或 JSON 無效，記錄「網站未驗證」，不可回報網站已同步完成。
6. 收工回報列出：Server V2 source commit、fs2db 網頁資料 commit、AWS 本機部署結果、線上 URL 核對結果、保留的進度百分比及下一個沒有 client 可做的工作。

## 2026-09-24 此次狀態

- Server V2 `refactor/server-v2` 進度 commit：`fa843fbe8b1ede1a9dbd7e292a07f07e33c17541`，overall 78%。
- fs2db `main` 網頁資料 commit：`619d6eb4b7fe4d1874380dca2efd37d9ba0e3a72`。
- 使用者在 AWS 的 `feature/item-v2` 工作樹複製四個進度頁檔案，`jq` 驗證本機 `updated_at=2026-09-24T00:44:36+08:00`、overall 78%、`source_head=fa843fbe...`。其中新增的 `public/server-progress/index.html` 遮住原本 Laravel 進度頁及右側分頁，靜態頁還因 `milestones.items` 不存在而報 `.join()` 錯誤並顯示舊 75%；恢復時須核對原路由及檔案內容，僅移開這次加入的靜態頁與其空目錄，切勿清理其他未提交檔案。
- 線上 JSON 已由 AWS `curl` 核對為 78% 及 `fa843fbe...`。AWS 已核對 `php artisan route:list --path=server-progress` 存在 Laravel `ServerProgressController@index` 路由；比對 `619d6eb4:public/server-progress/index.html` 完全相同後，僅將遮住路由的靜態檔移往 `/tmp/fs2db-progress-static.QAHby5.html` 並移除空目錄。使用者重新開頁後回報正常，畫面顯示 78%、右側自動化測試與近期開發紀錄已恢復。這是網站畫面驗收，不代表新的 Server V2 實機遊戲驗收。
