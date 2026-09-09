God2 Semantic Recovery Engine v1.3.0 Ultimate Live Validation
============================================================

目前狀態：Native live entrypoint contract 已實作；官方 exact target 實機證據尚未執行，
因此發佈時 live evidence gate 仍是 EVIDENCE_BLOCKED_EXTERNAL_LIVE_GATE_PENDING。

CMD 會直接執行封裝 EXE 的嚴格 `--internal-ultimate-live-validation --package-root` 入口，
不會呼叫 PowerShell。若目前不是系統管理員，封裝 EXE 會先驗證 canonical package root、
拒絕 reparse point、確認 EXE 是該 root 的直接子檔案且沒有路徑逸出，再透過 Windows
ShellExecuteEx `runas` 以同一個封裝 EXE、同一個 package root 自我提升。父程序會等待，
並回傳提升後子程序的原始 exit code。請核對並允許 Windows UAC 提示；若取消 UAC，
或提升後仍不是系統管理員，結果必須是 EVIDENCE_BLOCKED_ADMINISTRATOR_REQUIRED，
不會產生 capture PASS。`--no-uac-automation` 只供診斷，非系統管理員使用時同樣受阻。

它只在 exact client identity、PID/creation time/session/build、verified hook map、ring
零遺失且完全 drain、必要 probe event、25-domain health、recovery bundle identity/hash、
完整 manifest/ZIP/attestation 全部一致時，才會輸出窄範圍元件證據狀態：
`ULTIMATE_LIVE_DEEP_RECOVERY_EVIDENCE_PASS`。

此狀態只代表 `ExactBuildDeepProbeSharedTransportRecoveryBundleOnly`。Live runner 不執行
operation-specific GPU matrix，也沒有綁定遊戲穩定度或 overhead 測量，因此結果會明示：
- OperationSpecificGpuStatus = EvidenceBlockedNotEvaluatedByLiveRunner
- GameStabilityStatus = EvidenceBlockedNoBoundStabilityTelemetry
- OverheadStatus = EvidenceBlockedNoBoundOverheadMeasurement
- UltimateCompletionEligible = false

即使取得上述元件 PASS，也絕對不是全域 Ultimate PASS，不能單獨提升發佈 gate。

操作：
1. 將整個 ZIP 解壓縮到本機資料夾，不要直接在 ZIP 內執行。
2. 雙擊 RUN-ULTIMATE-LIVE-VALIDATION.cmd；若 Windows 顯示 UAC，核對封裝 EXE 後允許。
3. 工具顯示 Exact Build、Probe、Capture Health 後，按「開始完整恢復取證」。
4. 正常操作官方 God2 Client；請勿輸入或記錄不必要的帳號、密碼或憑證。
5. 回到工具按「停止並產生 Codex 恢復包」。
6. 將畫面顯示的 Result ZIP 與 God2UltimateRecovery ZIP 帶回獨立驗證。

任何窄範圍 live 元件 PASS 都必須把同一條密碼學證據鏈綁定到：release EXE SHA-256、
process ID/creation time/path hash、exact God2_opt.exe、verified hook map、25-domain
semantic health、native recovery bundle ZIP/packageId，以及 importer result manifest。
只有狀態文字或單一 JSON 檔案不能提升 gate；即使完整鏈通過，仍只在上述窄範圍有效。

安全限制：
- 不安裝服務、排程工作、驅動程式或持久化項目。
- 不修改 Defender、防火牆、SMB、Windows 安全性或 Production DB。
- 不需要 Visual Studio、CUDA Toolkit、PowerShell 或完整專案。
- 不接受已被替換、移出 package root、位於 reparse point 或路徑逸出的 EXE。
- 取消 UAC 或無法取得系統管理員權限時只會回報 EvidenceBlocked，不會降級為不安全執行。
- 未通過 exact client SHA、probe signature、manifest 或 evidence gate 時，正確結果是 EvidenceBlocked，不是 PASS。

目標 Client：God2_opt.exe / x86 / 1.0.0.1
SHA-256：6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B
