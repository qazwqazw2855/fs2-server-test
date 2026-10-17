# 隔離回歸來源與結果追溯 — 2026-10-17

入口：`bash Automation/test-v2-all-isolated.sh`；無參數為完整六套及 16 支 fixture。
`--persistence` 只驗證過濾後的 baseline persistence，不能稱為完整回歸。
只使用現有 3310／3311 容器與認證；不使用 legacy shared runner。

## 證據格式

每次入口在 `docs/handoff/v2-regression-<UTC>-<random>/` 新建獨立目錄。
`source-before.json`／`source-after.json` 記錄 UTC、branch、完整 HEAD、tracked staged/unstaged name-status，以及每個輸入檔 SHA256 和 tracked 狀態。
範圍為六個測試專案及遞迴 ProjectReference 的專案目錄、祖先建置設定、database/schema SQL 和 Automation 的 sh/py/sql 原始檔保守超集。
即使檔案 untracked 或被 ignore，仍納入上述原始碼範圍。bin/obj、TestResults、Python cache、證據輸出及秘密設定排除；不能以秘密命名排除 cs/csproj/props/targets 原始碼。
自動 manifest 不複製原始碼全文，也不保存環境 dump、密碼或秘密設定內容；本輪審查另以 tested-source 保存必要受測新增來源及 tracked patch，見後節。

`run.json` 保存入口 argv、開始／結束時間、worker 與最終 exit code、來源穩定性及變動路徑。
新增／刪除／修改範圍內檔案或 HEAD/branch 改變均標示不穩定；原成功改為 exit 2，原失敗保持失敗。
每個 dotnet test 與 fixture wrapper 的 `invocations/invocation-*/invocation.json` 保存實際 argv、UTC、exit code、該次 TRX counters；output.log 和該 invocation 的 TRX 副本一併保留。
沒有產生 TRX 時保留 expected path 與空結果，不能宣稱通過。敏感命令參數拒絕紀錄；GOD2 密碼／secret／token 環境值遮罩。
fixture 原有結果目錄與 EXIT 清理保留；wrapper 紀錄包含清理完成標記。run 的 summary.json 列出每一次 invocation，包括失敗，沒有合併為唯一測試數。
不自動重跑；後續重跑另開新目錄，必須分別引用。六套基礎執行與 fixture 前置案例重複分開計數。

## 具名 DB 檢查

`Automation/regression_db_state.py` 固定使用本機 Docker socket、具名 isolated container 和 host binding 127.0.0.1:3310／3311。
比對 container hostname 與 DB @@hostname、內部 port 3306、root principal 及 god2_game/god2_player schemas。
認證仍由既有 container /run/secrets 讀取；不寫入證據。
`Automation/regression-db-3310.sql` 依現有 merchant／position／quest preflight 與 cleanup 規則，檢查空表、canonical item、合成 map/quest、臨時 users/triggers。
`Automation/regression-db-3311.sql` 依 EnabledPortalBuildIntegrationTests 和 CharacterOne inventory snapshot 規則檢查 routes/build/bounds、背包基線及臨時 fault users。
每列明列 check_name、actual_value、expected_value；判斷為 actual == expected。
`db-before.json`／`db-after.json` 保存每項名稱、SQL 檔、隔離目標、值與條件；失敗停止或讓最終執行非成功。
baseline fault user 在 finally 移除，baseline-cleanup.json 紀錄結果；失敗的測試不會因成功清理改為成功。

## 歷史界線

10/13 的 `docs/handoff/v2-isolated-regression-20261013-gn2hvcny/fixture-db-state.tsv` 與 `baseline-db-state.tsv` 原樣保留。
其產生 SQL／欄義未找回，不能從數字推測。10/17 的新具名 checks 不補證或重新定義舊 TSV。
10/13 的 source-commit + working-tree diff 不含當時新增檔案；後來提交保存的檔案不能自動當作測試當時同一份內容。
本次 manifest 與測試只適用本次執行，不補成 10/13 原始證據，也不核准 Merchant gates。

## 驗證紀錄

實作階段：Python AST、Bash -n、git diff --check 通過；不連 DB 的 evidence failure-path tests 6/6 通過。
唯讀 preflight：兩個具名容器、host port、DB identity 及 31 項具名 checks 全部通過。
完整入口的實際結果另於本節補充，不能以前次結果替代。

### 本次完整入口結果

- 實際命令：`bash Automation/test-v2-all-isolated.sh`（無必要附加參數）。完整入口只執行一次，沒有失敗／重跑。
- UTC 開始：2026-10-17T04:30:30.491646+00:00；結束：2026-10-17T04:34:22.861677+00:00。
- HEAD：`827998516cc2cc8ced8f4f9c4dbfac0ea78148a9`，branch `codex/inventory-grant-20261003`，含本輪尚未提交修改。
- 證據：`docs/handoff/v2-regression-20261017T043030Z-ycvw4c1j`。
- manifest：12 個專案（六個 test + 六個 transitive source projects），798 個檔案；11 個 untracked 輸入（包含既有五個 Automation SQL 及本輪六個新收集／驗證原始檔）。
- 前後 source hashes 一致，`source_stable=true`，changed_paths 為空；worker 與最終 exit code 均為 0。
- 43 筆 invocation 紀錄：26 個 dotnet test、16 個 fixture wrappers、1 個 grouped runner。26 個 dotnet invocation 均有各自 TRX 副本。
- 基礎六套：Core 7/7、Application 114/114、Session 20/20、Protocol 147/147、Network 214/214、Persistence.Integration 80/80；合計 582/582，failed/skipped 均為 0。
- fixture 共 25/25 次測試執行，分別如下；不與基礎分組相加宣稱唯一測試總數。

| Fixture runner（省略 test-v2- 與 -isolated.sh） | dotnet invocation 數 | 通過／執行 | exit code | 清理 |
|---|---:|---:|---:|---|
| position-committed | 1 | 1/1 | 0 | PASS |
| merchant-purchase | 1 | 1/1 | 0 | PASS |
| economy-snapshot | 1 | 1/1 | 0 | PASS |
| merchant-tcp-reconnect | 1 | 1/1 | 0 | PASS |
| merchant-tcp-lost-result | 1 | 1/1 | 0 | PASS |
| merchant-command | 1 | 1/1 | 0 | PASS |
| inventory-restore-blocked | 1 | 1/1 | 0 | PASS |
| quest-process-recovery | 2 | 4/4 | 0 | PASS |
| merchant-buy-sell-tcp | 1 | 1/1 | 0 | PASS |
| merchant-sale | 1 | 1/1 | 0 | PASS |
| merchant-tcp | 1 | 1/1 | 0 | PASS |
| inventory-stack-committed | 1 | 1/1 | 0 | PASS |
| inventory-grant-concurrency | 1 | 1/1 | 0 | PASS |
| inventory-grant-rollback | 1 | 4/4 | 0 | PASS |
| merchant-tcp-new-server | 1 | 1/1 | 0 | PASS |
| merchant-recovery | 4 | 4/4 | 0 | PASS |

- merchant 15 次 EXIT cleanup（recovery wrapper 內含兩次）、position 清理、quest remaining=0 均有標記；兩組 atomic users/triggers 清理通過。
- `db-before.json` 與 `db-after.json` 各 31 項具名檢查全通過；`baseline-cleanup.json` 確認臨時 baseline fault account 已移除。
- 已知 baseline DB／登入密碼在本次證據檔的檢查命中數為 0，檢查未輸出秘密值。
- 兩個歷史 TSV 與 HEAD blob 比較相同。本次證據不追溯替代 10/13 證據。
- 不連線或操作 3308/3309，不啟停 6001/6002；未修改 gameplay／協定／Merchant gate，未部署、commit 或 push，也未清理原有 untracked。

## 修改檔案

既有 runner：Automation/test-v2-all-isolated.sh、test-v2-all-no-progress.sh、test-v2-merchant-isolated-3310.sh、test-v2-position-committed-isolated.sh、test-v2-quest-process-recovery-isolated.sh。
新增：Automation/regression_evidence.py、run_v2_isolated.py、regression_db_state.py、regression-db-3310.sql、regression-db-3311.sql、test_regression_evidence.py，以及本交接文件。
`RegressionBaselineDatabaseGuard.cs` 僅閱讀／核對，沒有變更。

## 本機提交前審查 — 2026-10-17

完整閱讀五支 runner diff、所有新增程式／SQL／測試／本文件。原入口的參數限制、branch／container／port／label／認證 selector 檢查、GOD2 環境隔離、臨時 fault user 建立／finally 移除、完整與 persistence 分支及 80-case 檢查均由 worker／supervisor 承接；fixture 原有 EXIT traps 和 pipefail 保留。

發現並修正：
- Python assert 在 -O 可略過，甚至略過 assert collect(...) 的 DB preflight 呼叫；改為明確 require，最佳化模式也不能繞過。
- 原 invocation 收集器在缺失／無效 TRX 時仍可記為 passed；改驗證 counters，子程序 exit 0 但證據無效時回傳 2，另保存 process_exit_code；原非零不改成成功。
- DB SQL 某一目標回傳空／缺少／重複具名列可能漏檢；改與 SQL 列名／列數逐項核對。
- supervisor 摘要收集失敗明確記 collection_error 並使原成功變非零；baseline 清理／最終收集抽成 finish，驗證失敗不會變成功。

局部測試包含：測試失敗保留 exit code、缺失／壞 TRX、清理失敗、最終收集失敗、來源異動、摘要儲存失敗、DB identity／binding 不符、缺列與條件失敗。所有外部程序／DB 以 mock 取代；一般與 -O 各 11/11。未重跑完整入口、未連 DB。

先前完整回歸結果不變，但它對應修正前 tested-source，而非其後審查修正版。原始 run.json／manifest／DB JSON／invocation JSON 不重寫。
`source-preservation.json` 核對所有 787 個 tracked 輸入：由原 HEAD 或 tested-source/tracked-changes.diff.gz 重建；六個必要新增來源以 tested-source/Automation/ 下副本保存，hash 與原 manifest 相符。
提交包含目前審查版與原受測副本，可分辨兩者；本次沒有把局部驗證冒充全套。

### 11 個 untracked 輸入與七個新增檔案交叉核對

| 精確路徑 | 保存／歸屬 |
|---|---|
| Automation/merchant-isolated-3310-cleanup-preview.sql | 歷史檔；未見 runner 引用；僅保守超集 hash，留本機不提交 |
| Automation/merchant-isolated-3310-cleanup.sql | 同上；內容為歷史 review/design template |
| Automation/merchant-isolated-3310-identity-preview.sql | 歷史檔；未見 runner 引用；留本機不提交 |
| Automation/merchant-isolated-3310-lock-preview.sql | 歷史檔；未見 runner 引用；留本機不提交 |
| Automation/merchant-isolated-3310-restrict-preview.sql | 歷史檔；未見 runner 引用；留本機不提交 |
| Automation/regression-db-3310.sql | 本輪新增；提交目前來源及原受測副本 |
| Automation/regression-db-3311.sql | 本輪新增；提交目前來源及原受測副本 |
| Automation/regression_db_state.py | 本輪新增；提交審查版及原受測副本 |
| Automation/regression_evidence.py | 本輪新增；提交審查版及原受測副本 |
| Automation/run_v2_isolated.py | 本輪新增；提交審查版及原受測副本 |
| Automation/test_regression_evidence.py | 本輪新增；提交審查版及原受測副本 |

第七個新增檔案是本交接文件，不屬程式執行輸入，因此不在 manifest 中。五個歷史 SQL 的作者／歷史歸屬未重新認定，不刪除、不順手提交；它們不是重建實際受測 runner 所必需的來源。

### 證據保存範圍

完整回歸證據原位保留：
`/home/ubuntu/games/fs2-v2-inventory-grant-20261003/docs/handoff/v2-regression-20261017T043030Z-ycvw4c1j/`。
提交明確選取的 root／invocation JSON、26 個 invocation TRX 副本、受測來源 patch／副本、來源重建稽核、審查局部測試及 artifact-index.json。
不提交原始 entry/grouped/invocation logs，也不提交重複的 grouped TRX；artifact-index.json 逐檔保存 SHA256、大小、原始絕對路徑及是否納入 Git。
原 fixture runner 的外部 evidence 目錄不整批加入；原 TRX 路徑由 invocation.json 記錄，TRX 副本位於本次主證據目錄。
檢查本次主證據目錄無 Git ignored 檔案；仍逐檔選取，不使用整個目錄的強制加入。
progress.json 新增本輪紀錄並保存既有 10/13 latest_grouped_regression／current_validation 為歷史；582 與 fixture 25 次分開，Merchant gates／原版 Client／正式部署限制維持。

## 已提交審查修正版完整驗證 — 2026-10-17

受測 commit：`51d1e158e093f14604cf768f021805f0ca385e70`（51d1e158）；branch `codex/inventory-grant-20261003`。執行前 tracked 工作樹與 index 乾淨；所有 290 個既有 untracked 路徑均保留，沒有清理。
實際命令：`bash Automation/test-v2-all-isolated.sh`，完整入口只執行 1 次，重跑 0 次，最終 exit code 0。
UTC 開始：2026-10-17T05:06:50.010060+00:00；結束：2026-10-17T05:10:43.224364+00:00。
新證據：`docs/handoff/v2-regression-20261017T050649Z-kh78l93n`。結果均由本次 TRX／invocation 讀取，沒有沿用先前數字。

| 六套基礎 | 通過／執行 | failed | skipped |
|---|---:|---:|---:|
| Core | 7/7 | 0 | 0 |
| Application | 114/114 | 0 | 0 |
| Session | 20/20 | 0 | 0 |
| Protocol | 147/147 | 0 | 0 |
| Network | 214/214 | 0 | 0 |
| Persistence.Integration | 80/80 | 0 | 0 |

基礎共 582/582。以下 16 支 fixture 共 25/25 次測試執行；包含重複前置案例，不與基礎合併為唯一測試總數。

| Fixture runner（省略 test-v2- 與 -isolated.sh） | dotnet invocation | 通過／執行 | exit | 清理 |
|---|---:|---:|---:|---|
| position-committed | 1 | 1/1 | 0 | PASS |
| merchant-purchase | 1 | 1/1 | 0 | PASS |
| economy-snapshot | 1 | 1/1 | 0 | PASS |
| merchant-tcp-reconnect | 1 | 1/1 | 0 | PASS |
| merchant-tcp-lost-result | 1 | 1/1 | 0 | PASS |
| merchant-command | 1 | 1/1 | 0 | PASS |
| inventory-restore-blocked | 1 | 1/1 | 0 | PASS |
| quest-process-recovery | 2 | 4/4 | 0 | PASS |
| merchant-buy-sell-tcp | 1 | 1/1 | 0 | PASS |
| merchant-sale | 1 | 1/1 | 0 | PASS |
| merchant-tcp | 1 | 1/1 | 0 | PASS |
| inventory-stack-committed | 1 | 1/1 | 0 | PASS |
| inventory-grant-concurrency | 1 | 1/1 | 0 | PASS |
| inventory-grant-rollback | 1 | 4/4 | 0 | PASS |
| merchant-tcp-new-server | 1 | 1/1 | 0 | PASS |
| merchant-recovery | 4 | 4/4 | 0 | PASS |

- 43 筆 invocation：26 次 dotnet test、16 個 fixture wrappers、1 個 grouped runner；每筆 process_exit_code／exit_code 均為 0。
- 26 份 invocation TRX 的 counters 與原檔一致，trx_valid 均為 true，failed／skipped 均為 0。
- manifest 前後一致：12 專案／798 檔案，source_stable=true；開始與結束時 staged／unstaged 均為空。
- 793 個 tracked 輸入逐檔 SHA256 對應受測 commit；五個歷史 untracked SQL 仍僅為保守超集，hash 未變，未被 runner 引用，未加入本次提交。
- DB 前後各 31 項具名檢查全通過；container／hostname／port／principal／schemas 前後一致，只使用 3310／3311。
- 每支 fixture 有清理標記；merchant EXIT cleanup、position 清理、quest remaining=0、atomic fault users/triggers 清理通過；baseline fault account 已移除。
- result-audit.json 保存逐項核對結果；artifact-index.json 保存完整證據檔名、大小、SHA256、原始絕對路徑與 Git 選取狀態。
- 逐檔提交本次 JSON 與 invocation TRX 副本；原始 logs／重複 grouped TRX 留本機，不整個目錄加入。
- 此次直接驗證已提交的審查修正版，取代前節「審查修正版僅局部驗證」作為最新狀態；前節保留為歷史紀錄。
- 本輪沒有功能或重構變更，沒有修改 gameplay／Merchant gate，沒有操作 3308／3309 或啟停 6001／6002；Merchant gates 仍 Blocked、原版 Client 驗收 pending、未部署正式環境，未 push。
