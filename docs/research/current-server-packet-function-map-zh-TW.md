# 目前服務端封包功能對照

更新時間：2026-08-17

這份文件只整理目前服務端已登錄的 public-beta compatibility 封包功能語意，方便人工查閱。  
有真實 current-build 證據的列為「已驗證」；只有結構或跨版本證據的列為「相容轉接」或「仍需證據」。  
不把 public-beta offset 當成 current-build 真實欄位，也不把自訂服務端行為偽裝成官方證據。

## C2S：客戶端送到服務端

| Domain | Opcode | 遊戲功能 | 服務端狀態 |
|---|---:|---|---|
| account_login | `0x04` | 帳號登入請求 | 相容轉接，current-build 長度不同 |
| route | `0xA9` | 路由與連線節點查詢 | 相容轉接，current-build 長度不同 |
| game_login | `0x00` | 遊戲登入請求 | 相容轉接，current-build 長度不同 |
| character | `0x17` | 建立角色 | 已驗證，正式 runtime/MariaDB lifecycle 已接 |
| character | `0x18` | 角色欄位與角色目錄查詢 | 已驗證，角色欄位操作可解 |
| character | `0x19` | 刪除角色 | 相容轉接，正式 mutation 仍需證據閘門 |
| character | `0x1A` | 選擇角色 | 相容轉接，正式 mutation 仍需證據閘門 |
| character | `0x1B` | 建立角色備援請求 | 相容轉接，正式 mutation 仍需證據閘門 |
| combination | `0xA4` | 合成流程步驟 | 相容轉接，合成副作用仍需證據 |
| combination | `0xA5` | 提交合成結果 | 相容轉接，current-build 長度不同 |
| gameplay | `0x0A` | 斷線與登出通知 | 相容轉接，production mutation 仍需證據 |
| gameplay | `0x21` | 屬性加點 | 相容轉接，屬性代碼仍候選 |
| social | `0x33` | 聊天封包外層封裝 | 相容轉接，文字業務語意仍需 current 證據 |
| social | `0x2F` | 聊天訊息 | 已驗證，current C2S capture 支持 |
| mail | `0xAE` | 信件操作 | 相容轉接，current-build 長度不同 |
| combat | `0x35` | 戰鬥行為指令 | 相容轉接，欄位 offset 已變；runtime mutation fail-closed |
| mission | `0x24` | 任務與互動請求一 | 相容轉接，互動副作用仍需證據 |
| mission | `0x25` | 任務物品互動請求 | 已驗證，current C2S capture 支持 |
| mission | `0x26` | 世界互動請求 | 相容轉接，互動副作用仍需證據 |
| mission | `0x27` | 任務與互動請求二 | 相容轉接，互動副作用仍需證據 |
| mission | `0x28` | 物品使用與功能啟動 | 已驗證，裝備/物品 outbound callsite 與 capture 支持 |
| party | `0x8F` | 組隊系統操作 | 相容轉接，current-build 長度不同 |
| party | `0x90` | 組隊系統操作 | 相容轉接，current-build 長度不同 |
| party | `0x91` | 組隊系統操作 | 相容轉接，current-build 長度不同 |
| party | `0x92` | 組隊系統操作 | 相容轉接，current-build 長度不同 |
| party | `0x93` | 組隊系統操作 | 相容轉接，隊伍副作用仍需證據 |
| party | `0x94` | 組隊系統操作 | 相容轉接，current-build 長度不同 |
| party | `0x95` | 組隊系統操作 | 相容轉接，隊伍副作用仍需證據 |
| pet | `0xBB` | 寵物出戰與戰鬥行為 | 相容轉接，current-build 長度不同 |
| pet | `0xB7` | 寵物蛋操作 | 相容轉接，current-build 長度不同 |
| pk | `0x69` | PK 目標定位 | 相容轉接，current 目標語意仍需證據 |
| pk | `0xB8` | 寵物 PK 目標 | 相容轉接，current-build 長度不同 |
| team | `0x5A` | 隊伍請求 | 相容轉接，隊伍副作用仍需證據 |
| vendor_cart | `0xB0` | 擺攤發布與展示設定 | 相容轉接，current 固定長度轉接 |
| vendor_cart | `0xB2` | 擺攤操作 | 相容轉接，current-build 長度不同 |
| vendor_cart | `0xBD` | 擺攤項目新增 | 相容轉接，current-build 長度不同 |

## S2C：服務端送到客戶端

| Domain | Opcode | 遊戲功能 | 服務端狀態 |
|---|---:|---|---|
| account_login | `0x01` | 帳號服務握手與帳號通道 | 仍需 current-build serializer 證據 |
| account_login | `0x1D` | 帳號資料與角色目錄回傳 | 仍需 current-build serializer 證據 |
| account_login | `0x1E` | 帳號登入錯誤回應 | 仍需 current-build serializer 證據 |
| route | `0x47` | 路由公告與分流伺服器定位回應 | 仍需 current-build serializer 證據 |
| route | `0x02` | 路由查詢錯誤 | 仍需 current-build serializer 證據 |
| game_login | `0x01` | 遊戲伺服器握手 | 仍需 current-build serializer 證據 |
| game_login | `0x1F` | 登入結果與角色進場資料 | 仍需 current-build serializer 證據 |
| game_login | `0x1E` | 登入流程錯誤 | 仍需 current-build serializer 證據 |
| character | `0x03` | 角色目錄選擇結果 | 仍需 current-build serializer 證據 |
| character | `0x17` | 角色操作結果與欄位回傳 | 仍需 current-build serializer 證據 |
| character | `0x1E` | 角色錯誤回應 | 仍需 current-build serializer 證據 |
| combat | `0x1C` | 戰鬥名單快照 | current combat writer 已接 |
| combat | `0x38` | 戰鬥控制分隔記錄 | current combat writer 已接 |
| combat | `0x83` | 單體戰鬥效果 | current combat writer 已接 |
| combat | `0x84` | 多目標戰鬥效果 | 候選，未在 current build 證實 |
| combat | `0x85` | 戰鬥回合與流程公告 | current combat writer 已接 |
| combat | `0x86` | 戰鬥控制紀錄 | current combat writer 已接 |
| combat | `0x87` | 戰鬥特殊控制片段 | current combat writer 已接 |
| combat | `0x88` | 戰鬥回合快照 | current combat writer 已接 |
| combat | `0x89` | 戰鬥結算摘要 | current combat writer 已接 |
| mission | `0xDF` | 任務資料同步 | public-beta layout 已判定與 current 不同 |
| gameplay | `0x22` | 玩家狀態快照 | current gameplay writer 已接 |
| gameplay | `0x24` | 玩家生命與法力即時值 | current gameplay writer 已接 |
| gameplay | `0x2B` | 玩家與神仙進度 | current gameplay writer 已接 |
| gameplay | `0x2C` | 玩家派生面板數值 | current gameplay writer 已接 |
| gameplay | `0x6F` | 玩家出生與角色進入事件 | 仍需 current-build serializer 證據 |
| gameplay | `0x71` | 怪物出生與刷新 | current gameplay writer 已接 |
| gameplay | `0x72` | NPC 出生與刷新 | current gameplay writer 已接 |
| gameplay | `0x30` | 神仙資料新增 | 仍需 current-build serializer 證據 |
| gameplay | `0x31` | 神仙狀態快照 | current gameplay writer 已接 |
| gameplay | `0x32` | 神仙清單目標選擇 | current gameplay writer 已接 |
| gameplay | `0x33` | 神仙介面旗標 | current gameplay writer 已接 |
| gameplay | `0x34` | 神仙技能新增 | 仍需 current-build serializer 證據 |
| gameplay | `0x35` | 神仙技能等級更新 | 仍需 current-build serializer 證據 |
| gameplay | `0x39` | 神仙生命與法力數值快照 | current gameplay writer 已接 |
| gameplay | `0xD5` | 神仙面板參照欄位 | 仍需 current-build serializer 證據 |
| gameplay | `0xD6` | 神仙面板加成欄位 | 仍需 current-build serializer 證據 |
| gameplay | `0xDD` | 神仙移除 | 仍需 current-build serializer 證據 |
| social | `0xDB` | 聊天頻道設定與系統文字事件 | 仍需 current-build serializer 證據 |
| social | `0x19` | 在地化文字模板解碼 | 仍需 current-build serializer 證據 |
| social | `0x20` | 狀態式文字模板記錄 | 仍需 current-build serializer 證據 |
| pet | `0xE7` | 寵物紀錄新增 | 仍需 current-build serializer 證據 |
| pet | `0xE8` | 寵物紀錄更新 | 仍需 current-build serializer 證據 |
| pet | `0xE9` | 寵物事件 | 仍需 current-build serializer 證據 |
| pet | `0xEA` | 寵物蛋狀態 | 仍需 current-build serializer 證據 |
| pet | `0xEB` | 寵物蛋事件 | 仍需 current-build serializer 證據 |
| pet | `0x4A` | 坐騎狀態同步 | 仍需 current-build serializer 證據 |
| tong | `0x98` | 幫派資料快照 | 仍需 current-build serializer 證據 |
| tong | `0x8E` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x8F` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x90` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x91` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x92` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x93` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x94` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x95` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x96` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x97` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x99` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x9A` | 幫派事件 | 仍需 current-build serializer 證據 |
| tong | `0x9B` | 幫派事件 | 仍需 current-build serializer 證據 |

## 目前白話結論

所有已登錄的 public-beta compatibility 封包都有遊戲功能對照。

目前不是「看不懂這封包是什麼功能」的問題，而是分成三種狀態：

1. 已驗證：可以用 current-build 證據直接支撐。
2. 相容轉接：知道功能與封包邊界，但正式副作用或欄位語意仍要證據閘門。
3. 仍需證據：知道遊戲功能，但 current-build serializer/layout 還不能亂猜。
