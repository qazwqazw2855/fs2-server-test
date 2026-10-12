# Exact-current God2_opt.exe 靜態控制流程證據 — 2026-10-12

## 基線

- Branch: codex/inventory-grant-20261003
- Baseline HEAD: 712dc22
- Client: /srv/god2-reference/client-current-6f2639/God2_opt.exe
- SHA256: 6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC
- PE: pei-i386
- ImageBase: 0x00400000
- EntryPoint VA: 0x00401000
- Static trace stop RVA: 0x00645001
- Static trace stop VA: 0x00A45001
- File offset: 0x001B2401
- Section: .data, executable

## 分析方式

- GNU objdump 靜態反組譯
- xxd 原始位元組檢查
- 每次分析前驗證 exact-current SHA256
- 不執行客戶端
- 不進行網路連線
- 不修改客戶端或伺服器

## STEP 90Q-17B-R2

成功確認 PE 格式與 .data 區段。

停止位置：

0xA45001: pusha
0xA45002: call 0xA4500A

線性反組譯會將 0xA45007 起始的位元組解碼為 JMP，
但 CALL 目標落在該指令位元組範圍內。

不能以線性反組譯直接推論實際控制流程。

## STEP 90Q-17C

從 0xA4500A 獨立解碼：

0xA4500A: pop ebp
0xA4500B: inc ebp
0xA4500C: push ebp
0xA4500D: ret

確認第一組重疊指令解碼。

## STEP 90Q-17D

從 0xA45014 獨立解碼：

0xA45014: pop ebp
0xA45015: mov ebx,0xFFFFFFED
0xA4501A: add ebx,ebp
0xA4501C: sub ebx,0x645000

附近存在條件分支與間接呼叫候選。

尚未證明完整堆疊與暫存器狀態。

## STEP 90Q-17E

確認以下靜態指令候選：

- 0xA4508A: call eax
- 0xA450A0: call eax
- 0xA450B3: call DWORD PTR [ebp+0x3d]
- 0xA450CC: mov eax,0x57A71225
- 0xA450D1: call 0xA450E5
- 0xA4510A: jmp 0xA4511A

間接呼叫目標未知。

## STEP 90Q-17F

從 0xA450E5 獨立解碼：

0xA450E5: pop edi
0xA450E6: adc eax,0xE26E602
0xA450EC: add edi,0xAC1

從 0xA4511A 獨立解碼：

0xA4511A: pop esi
0xA4512A: xor esi,0x35941897
0xA45135: jmp 0xA4514A
0xA45155: mov DWORD PTR [edi+edx],esi

第三組跳躍目標亦落在線性反組譯的指令範圍內。

## 已確認的重疊解碼位置

1. 0xA4500A — CALL 目標
2. 0xA450E5 — CALL 目標
3. 0xA4514A — JMP 目標

以上為靜態指令解碼證據，不代表完整執行路徑已確認。

## 分析限制

- 不將線性反組譯視為實際執行證明
- 不假設未知間接呼叫目標
- 不假設暫存器或堆疊狀態
- 不推論已確認解殼或惡意行為
- 不推論 LoginServer.csvZ、God2Con.csvZ 或啟動參數優先順序
- 不修改現有 fail-closed 分析器

## 尚未解決

- Exact-current 客戶端實際端點選擇流程
- 啟動參數與 LoginServer.csvZ 優先順序
- God2Con.csvZ 是否參與端點選擇
- 間接呼叫與記憶體操作的實際作用
- 是否需要安全、有限範圍的靜態分析器擴充

EndpointPrecedence = UNKNOWN

## 安全與交接原則

- 沒有正服證據，不猜協定
- 沒有 baseline，不直接改 V2
- 正式 6001 不動
- 正式資料庫不動
- BUY / SELL evidence gates = Blocked
- 不執行客戶端
- 不繞過 Defender
- 不還原 God2_optmgr.exe
- 不修改 progress.json
- 不清理既有未追蹤 merchant / journal 證據
- 未經授權不 Commit、不 Push
