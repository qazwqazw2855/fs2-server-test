# Server V2 收工與網站進度頁 SOP

Server V2 的 `progress.json` 是進度唯一資料來源。FS2DB `/server-progress` 由 Laravel `ServerProgressController` 讀取 GitHub `refactor/server-v2:progress.json`，不再人工同步第二份網站 JSON。

## 核心原則

1. 沒有正服證據，不猜協定；沒有 baseline，不直接改 V2。
2. Tests PASS 不等於 Evidence Gate 完成。
3. `overall_percent` 由實際工程成果與證據人工調整，不由腳本自行推算。
4. Roadmap / Full Playable Gate status 只有取得足夠 evidence 才能修改。
5. Production 6001、isolated 6002、Headless Probe、原版 Windows Client 實機驗收必須分開描述。
6. World Login identity coverage 與 Portal wire projection evidence 不互相推論。
7. Progress Page HTTP + 實際畫面驗收是收工完成條件。

## 收工流程

### 1. 確認今日成果

執行 `git status --short` 與 `git log -1 --oneline`。

確認今日完成項目、Evidence、Production / isolated 狀態、EvidenceBlocked 與未解問題。

禁止 `git add .`，必須精準 Stage。

### 2. Evidence Gate Review

只有取得足夠 evidence 才能調整 Roadmap / Playability Gate。

特別注意：

- World Login 144/144 不代表 Portal 144/144。
- Portal destination 不得由 World Login coverage 推論。
- Headless Probe 成功不代表原版 Client 畫面驗收。
- isolated 6002 成功不代表 Production 6001 acceptance。
- Tests PASS 不代表正服 protocol semantics 已確認。

### 3. 一鍵 Progress Verification

執行：

`./Automation/update-progress-all.sh`

此腳本自動：

- 執行六個 Server V2 Test Projects。
- 從 TRX 統計 Passed / Failed。
- 更新 `progress.json` verification。
- 更新 Roadmap current phase。
- 依 Gate 權重計算 Full Playable percentage。
- 更新 promotion/playability ready 狀態。
- 更新台灣時間 `updated_at`。

腳本不得自行修改 `overall_percent`、Roadmap Gate status、Playability Gate status。

### 4. Progress 指標

Engineering Progress 使用 `overall_percent`，由實際工程成果與 Evidence 人工調整。

Full Playable 使用 `playability.percent`：

- completed = 100% Gate 權重
- in_progress = 50% Gate 權重
- pending = 0%

應描述為「依目前定義的 Full Playable Gate 權重計算」，不得描述成客觀遊戲完成率。

### 5. 更新 Handoff

重要成果需記錄：

- 今日完成項目
- Evidence / baseline
- Tests
- Production / isolated 狀態
- Client acceptance 狀態
- EvidenceBlocked
- Current Phase
- 下一個 Evidence Gate
- 明天第一個工作起點
- Server V2 HEAD
- Production service 狀態（若有部署）

不得把推論寫成已驗證事實。

### 6. Git 檢查與提交

提交前執行：

`git diff --check`

`git status --short`

`git diff`

只 Stage 本次確認的檔案；未確認的 generated / untracked files 不得擅自加入或刪除。

Stage 後再次執行：

`git diff --cached --stat`

`git status --short`

確認後 Commit，並 Push：

`git push origin refactor/server-v2`

最後確認：

`git log -1 --oneline`

`git status --short`

### 7. Progress Page 自動同步

正式頁面：

`https://fs2db.com/server-progress`

資料流：

Server V2 `progress.json`
→ GitHub `refactor/server-v2`
→ FS2DB `ServerProgressController`
→ `/server-progress`

不再人工複製 `progress.json` 到 FS2DB。

網站 Blade/UI 只負責顯示，不作為第二份進度來源。

Controller 有 Cache，因此 Push 後可能需要等待快取更新。

### 8. Live 驗收

至少確認 `/server-progress` 回傳 HTTP 200。

並實際開啟頁面確認：

- Roadmap 正常
- Current Phase 正確
- COMPLETE / IN PROGRESS / PENDING 正確
- Test Passed / Total 正確
- Engineering Progress 正確
- Full Playable Progress 正確
- Promotion Ready 正確
- 最新摘要 / 活動正確
- 無 HTTP 500 / Blade / JavaScript 錯誤

HTTP 200 不能取代實際畫面驗收。

### 9. 最終收工摘要

固定記錄：

Server V2 HEAD：<commit>
Tests：<passed>/<total> PASS
Production：<status>
Current Phase：<phase>
Engineering Progress：<overall_percent>%
Full Playable：<playability.percent>%
Promotion Ready：<true/false>
Progress Page：HTTP 200 / Live Verified
Git：已 Push
未提交檔案：<list or none>
下一步：<next evidence gate>

完成上述驗證後才正式回報「今日收工完成」。

## 流程總覽

開發完成
→ Evidence Review
→ `update-progress-all.sh`
→ Tests / Roadmap / Full Playable 驗證
→ 更新 Handoff
→ `git diff --check`
→ 精準 Stage
→ Commit / Push
→ FS2DB 自動讀取 `progress.json`
→ Progress Page HTTP + 畫面驗收
→ 記錄 HEAD / Tests / Progress / Next Gate
→ 收工
