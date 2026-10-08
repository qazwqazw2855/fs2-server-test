# 公司收工節點 — 2026-10-08

## 分支與來源
- 工作目錄：/home/ubuntu/games/fs2-v2-inventory-grant-20261003
- 分支：codex/inventory-grant-20261003
- 收工前 HEAD：4e12e3e，已推送 origin。

## 最新驗證
- 六套基礎分組：554/554，0 failed、0 skipped。
- 15 支隔離 fixture 腳本：20 次測試執行全部通過。
- 上述執行包含前置案例重跑，不相加為唯一測試數。
- Fixture 清理完成；progress.json 未變動。
- 最新整合 log：/tmp/god2-v2-grouped-position-20261008-133014.log。
- Host lifecycle runner 涵蓋登出重登、Pending ownership、
  在線重複登入拒絕、30 秒閒置逾時、移動 ACK、
  重複移動序號拒絕與拒絕後重登；presence 進入／釋放各 9 次。
- Host log：/tmp/god2-host-lifecycle-8cxvcF。
- Committed position fixture 1/1 通過：
  提交後新連線讀回座標、版本與 token；舊請求被拒絕且不修改狀態。
- Position log：/tmp/god2-position-committed-20261008-132754.log。

## 維持狀態
- 工程 78%、可玩性 55%；promotion/evidence gates 未提升。
- Merchant Host 接線預設關閉，BUY/SELL evidence gates 保持 Blocked。
- 正式 production 6001 未部署或重啟。
- 自動化 Probe 驗證不代表原版客戶端驗收完成。
- 最新 Host 與 position 驗證尚未同步至 progress.json／網站。
- 既有未追蹤測試 log 保留，未清除或全部加入 Git。

## 回家接續
1. 先讀 docs/handoff/merchant-home-client-checklist-20261008.md，
   安排原版客戶端證據與驗收。
2. 移動持久化仍缺 TCP 重登讀回座標驗證；
   使用隔離 fixture，不直接改 test001 的位置。
3. 怪物／掉落身分盤點已完成，數值、出生與掉落語意證據仍不足；
   參考 docs/parity/monster-promotion-gap-20261008.md。
4. 收工驗證摘要可另行同步 progress.json 與網站分支，
   保留百分比及 gates，勿直接合併整個開發分支。

## 可重跑入口
- bash Automation/test-v2-host-session-lifecycle.sh
- bash Automation/test-v2-position-committed.sh
- 完整分組需透過 quest fixture 外層，設定 GOD2_V2_TEST_ONLY=1，
  並提供 GOD2_TEST_PASSWORD；不要裸跑完整 Persistence suite。
