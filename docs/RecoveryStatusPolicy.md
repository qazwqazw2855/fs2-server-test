# RecoveryStatus Policy

正式資料表與 official import staging 必須支援 `RecoveryStatus`。

允許值：

- `Recovered`：資料已從 Official Client Recovery 匯入，但尚未完成全部語意驗證。
- `Verified`：資料已完成必要 reference 與 gameplay 語意驗證。
- `NeedsRecovery`：已知類別或欄位仍需 recovery 補齊；交叉掃描尚未完成時必須使用此狀態。
- `EvidenceOnly`：只有 evidence / candidate，尚不足以直接推進到 gameplay table。
- `Missing`：完整 Cross Reference、Container Scan、Runtime Scan、Resource Scan、String Scan、Identifier Scan 後，仍確認沒有官方資料來源。

狀態可從 `Recovered` 更新為 `Verified`，不得為了狀態改變而重建整個資料表。

不得因找不到單一檔案、單一 DAT 或單一 table 就標示 `Missing`。
