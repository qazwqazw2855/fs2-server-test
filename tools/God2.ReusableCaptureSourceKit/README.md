# 可重用遊戲取證原始碼套件

這份套件是目前 God2 解密前／解密後封包擷取、記憶體觀測與事件關聯程式的原始碼副本，供日後在**獲得授權的測試環境**移植到另一款遊戲。

## 套件內容

- `reference/God2.ClientInstrumentation/src/God2ClientTraceProbe.cpp`：目前完整 x86 DLL 參考實作，含 `PostDecrypt`、`PreEncrypt`、傳輸層、戰鬥事件與記憶體快照。
- `reference/God2.ClientInstrumentation/src/GenericProbePlan.*`：資料驅動的通用函式入口／返回／呼叫者／消費者觀測核心。
- `reference/God2.ClientInstrumentation/src/God2PacketCaptureInjector.cpp`：注入器原始碼。
- `reference/God2.ClientInstrumentation/src/God2ClientTraceLauncher.cpp`：控制器原始碼。
- `reference/God2.ClientInstrumentation/scripts`：建置、啟動與自我測試腳本副本。
- `generic`：通用探針計畫範例、JSON Schema 與控制腳本。
- `docs/PORTING-GUIDE.zh-TW.md`：換遊戲時的拆分與移植步驟。
- `docs/CODEX-OPERATOR-WORKFLOW.zh-TW.md`：由 Codex 負責技術操作、使用者只負責遊戲操作的固定工作流程。
- `SOURCE-MANIFEST.json`：本套件檔案 SHA-256 清單。

## 重要限制

這不是跨遊戲即插即用 DLL。封包擷取與記憶體觀測框架可重用，但每款遊戲的執行檔身分、架構、RVA、函式簽章、加解密邊界、封包格式與物件布局都必須重新驗證。

本套件不包含正式登入器、安裝目錄檔案、帳號資料、封包內容、記憶體擷取結果或任何敏感執行產物，也不提供反作弊規避功能。
