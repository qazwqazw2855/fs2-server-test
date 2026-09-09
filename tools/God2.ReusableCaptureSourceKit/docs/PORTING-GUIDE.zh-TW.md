# 移植指南

## 1. 先決定可重用與必須重寫的邊界

可以保留：事件佇列、二進位追蹤格式、metadata、時間戳、執行緒關聯、隱私過濾、GenericProbePlan、控制命令與 fail-closed 檢查。

必須重寫：目標執行檔識別、x86/x64 ABI、加密前／解密後掛點、封包框架、Opcode、遊戲物件布局、戰鬥函式 RVA、欄位偏移與版本簽章。

## 2. 建立新遊戲專用分支

1. 複製 `reference/God2.ClientInstrumentation` 到新遊戲自己的專案目錄。
2. 重新命名 DLL、控制器、命名空間與輸出目錄。
3. 不要直接修改這份參考副本；參考副本應保持可比較。
4. 不要共用 God2 的正式登入器、設定檔或安裝路徑。

## 3. 驗證目標身分

至少固定：

- 執行檔名稱與完整路徑
- PE 架構與 Image Size
- SHA-256 或等價精確版本身分
- 模組基址與映像範圍
- 程序 PID 與建立時間

身分不符時應拒絕掛鉤，不要猜測 RVA。

## 4. 找到明文邊界

傳輸層 `send/recv/WSASend/WSARecv` 只能保證取得網路位元組；若遊戲使用自訂加密，必須在獲授權的目標中重新驗證：

- 收到密文後、解析封包前的 `PostDecrypt`
- 組好明文後、送入加密前的 `PreEncrypt`

每個掛點都要記錄呼叫慣例、參數位置、長度來源、返回值、執行緒與至少 8 個預期機器碼位元組。版本不符時 fail closed。

## 5. 封包框架適配

重寫並驗證：

- Header 長度
- 封包總長欄位
- Opcode 位置與寬度
- 大小端序
- 壓縮順序
- 黏包／拆包規則
- 登入敏感欄位遮罩

先保存階段與長度等 metadata；原始明文只應寫入明確標示的敏感資料區。

## 6. 記憶體與戰鬥適配

優先使用 `GenericProbePlan` 輪替候選函式，觀測 ECX、EDX、有限 stack words、返回點與 consumer。確認物件生命週期後才增加固定快照。

每個快照必須：

- 限制最大長度
- 先驗證記憶體為 committed、readable、非 guard
- 不保存裸指標；輸出 session-scoped token
- 使用物件身分、戰鬥位置與封包 sequence 做關聯
- 在版本或簽章不符時停止，不掃描任意大範圍記憶體

## 7. x64 遊戲

目前參考核心是 x86。x64 不能只重新編譯，必須重寫 calling convention、暫存器擷取、執行緒 context/debug-register 邏輯、指令解碼假設與注入器架構檢查。

## 8. 最低驗收門檻

- 精確目標身分通過
- `PreEncrypt` 與 `PostDecrypt` 各有受控樣本
- 封包邊界可重現
- P0/P1 loss 為 0
- sequence gap 為 0
- pending 與 active invocation 最終為 0
- 敏感登入內容不進一般日誌
- 關閉遊戲後能正常 drain 與卸載

只在你擁有或明確獲准測試的遊戲上使用；不要加入隱匿、反偵測或反作弊繞過功能。
