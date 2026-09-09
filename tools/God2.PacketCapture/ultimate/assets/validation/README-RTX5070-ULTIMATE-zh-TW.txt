God2 Semantic Recovery Engine v1.3.0 RTX 5070 驗證
====================================================

目標環境：Windows 11 x64 + NVIDIA GeForce RTX 5070。

操作方式：
1. 將完整 ZIP 解壓縮，不要直接在 ZIP 內執行。
2. 雙擊 RUN-RTX5070-ULTIMATE-VALIDATION.cmd。
3. 等待實體 CUDA、完整 CPU oracle、17 項 Ultimate workload 與封裝完整性驗證完成。
4. 帶回 RTX5070-VALIDATION-RESULT-*.zip 與外部稽核 attestation。

不需要登入遊戲、安裝 CUDA Toolkit、Visual Studio 或 PowerShell。驗證程式不會注入
God2、不會修改 Server/DB，也不會建立服務或排程工作。

正確的 v1.3 實作契約
---------------------

- 16 項非 AI workload 都有獨立的 operation-specific GPU implementation：
  TraceFeatureExtraction、CrossSessionCorrelation、ParserFieldPatternMatching、
  FunctionClustering、ObjectClustering、ClassLayoutScoring、RegistryScoring、
  HeapGraphSimilarity、ValueFlowAggregation、TaintGraphBatch、
  SemanticEdgeScoring、ApproximateNearestNeighbor、SequenceMining、FsmScoring、
  FormulaBatchEvaluation、ReplayStateComparison。
- 每項都必須實際執行 GPU candidate，具有正數 record/kernel 計數、完整 CPU 驗證、
  零 mismatch、零 backend error、零 dropped evidence，並使 GPU candidate digest、
  authoritative digest 與 CPU-only digest 完全一致。
- 並行執行必須有界，ConfiguredStreams 與 PeakInflightBatches 必須反映實際量測；
  OOM、timeout/TDR、worker failure 或 mismatch 必須安全回退 CPU 並保留診斷。
- 第 17 項 AiInference 在沒有已驗證 provider/model 時，必須明確回報
  EvidenceBlockedModelUnavailable，不得執行 provider inference、candidate 或 kernel。
  這是允許的證據限制，不是 GPU/ML pipeline 的實作缺口。
- 正確 aggregate 為 GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED：
  17 total / 16 GPU verified / 0 GPU implementation blocked /
  0 structured blocked / 1 model evidence blocked。

CPU authority 永遠保留。Auto 模式只有在相同輸入、最新完整路徑配對校準證明超過安全
門檻時才可選 GPU；否則必須明確選擇 CPU。負加速或 primitive-only 樣本不得被標示為
Auto 效益。

外部 RTX 5070 gate
------------------

本 ZIP 只提供可攜式 runner。正式 RTX 5070 PASS 必須由新版本 EXE 在指定實機執行，
並由 result manifest 與 v2 external attestation 綁定：

- 精確 release EXE SHA-256 與 PE 1.3.0.0；
- Windows 11 與 NVIDIA GeForce RTX 5070；
- 完整、安全、無重複且 SHA-256 全數相符的 ZIP inventory；
- 17 項 workload matrix 的精確 SHA-256；
- 16 項非 AI 實體 GPU/CPU 等價與 1 項誠實的 AI model EvidenceBlocked。

在 fresh RTX 5070 結果尚未帶回前，正式狀態仍是
PENDING_FRESH_ULTIMATE_RTX5070_RUN。舊 v1.2 ZIP 只能保留為 provenance，不得升格。

當所有本地程式碼、測試、靜態分析、Recovery Bundle 與 GPU 實作 gate 通過時，工程
狀態可正確標示為：

ULTIMATE_DEEP_RECOVERY_IMPLEMENTATION_COMPLETE_WITH_EXTERNAL_LIVE_GATE_PENDING

這不代表 fresh RTX 5070、官方客戶端 live session、精確官方 client identity 或真實
importer integration 已通過；上述項目仍是獨立的外部 pending gate。

主工具會永久顯示三語「DLL 增強擷取」狀態列，用於呈現閒置、附加、Ready、阻擋、
排空與停止狀態。增強擷取由既有雙動作流程自動啟用；這一列是狀態與診斷資訊，
不是第三個按鈕，也不要求使用者額外操作。正式 acceptance 必須綁定相同 release EXE、
RCDATA 201/202、25 個 domain（其中 21 個 candidate-only 保持 fail-closed）及五項 GUI
可見性／自動啟用 smoke key，且 failed 與 pending 都必須為 0。
