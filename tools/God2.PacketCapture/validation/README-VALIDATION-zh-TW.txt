RTX 5070 最終硬體驗證
=====================

1. 將此 ZIP 複製到 Windows 11 + NVIDIA GeForce RTX 5070 電腦。
2. 解壓縮 ZIP。
3. 雙擊 RUN-RTX5070-VALIDATION.cmd。
4. 等待測試完成；過程可能需要數分鐘。
5. 將產生的 RTX5070-VALIDATION-RESULT-*.zip 帶回。

不需要登入遊戲，不需要完整原始碼、Visual Studio、PowerShell、.NET SDK、
CUDA Toolkit 或命令列參數。只需要正常 NVIDIA 顯示驅動程式。

這是離線硬體驗證，不會注入或啟動 God2、不會變更伺服器、資料庫、
Windows 防火牆、使用者、服務、排程工作、登錄持久化或安全性設定。

固定 equivalence 與 benchmark corpus 由原生執行器依 DeterministicOfflineCorpus-v1
演算法在記憶體內產生，避免攜帶玩家資料或歷史 Evidence。
