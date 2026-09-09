# Public-beta full-system evidence promotion — 2026-08-15

## Scope and authority boundary

Source archive: `C:/Users/SeiHo/Desktop/FS2TW-research-evidence-20260815.zip`  
SHA-256: `75BA3B07058F83581B0E7DC40B8469ABF413ED5CF4271CBDC497D340D907598D`

The archive is a static reconstruction of the public-beta client, not a capture
from the exact current official build and not original server source. Its 97-entry
protocol catalog covers login, routing, character lifecycle, player state,
social/text, mail, missions, party/team, immortals, pets, PK, vendor cart, Tong
and battle. It is used as a semantic and consumer-path oracle only.

Promotion rule:

1. Exact-current capture or exact-current static analysis remains authoritative.
2. A matching public-beta opcode and length is structural corroboration, not proof
   that every field or server policy is unchanged.
3. Changed lengths reject the old layout outright.
4. No client request grants mutation authority. Authorization, inventory, currency,
   rewards, progression and persistence stay server-owned.

## Complete catalog inventory

| Direction | Entries | Exact public-beta semantics | Partial public-beta semantics |
|---|---:|---:|---:|
| C2S | 36 | included in total | included in total |
| S2C | 61 | included in total | included in total |
| Total | 97 | 28 | 69 |

### C2S current-build comparison

Application-record lengths exclude the exact-current outer two-byte frame length
and checksum. `var` means an embedded-length application record.

| Domain | Opcode | Public beta | Current | Result | 遊戲功能 |
|---|---:|---:|---:|---|---|
| account login | `04` | 169 | 205 | changed/rejected | 帳號登入請求 |
| route | `A9` | 3 | 5 | changed/rejected | 路由/登錄入口查詢 |
| game login | `00` | 169 | 205 | changed/rejected | 遊戲登入請求 |
| character create | `17` | 45 | 45 | structural match | 角色建立 |
| character catalog | `18` | 2 | 2 | structural match | 角色列表與欄位查詢 |
| character delete | `19` | 54 | 54 | structural match | 刪除角色 |
| character select | `1A` | 38 | 38 | structural match | 選擇角色 |
| character create | `1B` | 45 | 45 | structural match | 角色建立（備援） |
| combination step | `A4` | 3 | 3 | structural match | 合成流程步驟 |
| combination commit | `A5` | 5 | 11 | changed/rejected | 完成合成/提交 |
| disconnect | `0A` | 17 | 17 | structural match | 角色登出與斷線 |
| attribute increment | `21` | 2 | 2 | current boundary observed; code meanings candidate-only | 屬性加點 |
| text envelope | `33` | var | var | structural match | 聊天封包外層封裝 |
| text message | `2F` | var | var | current static + capture verified; beta corroborates offsets | 聊天訊息內容 |
| mail action | `AE` | 9 | 13 | changed/rejected | 信件操作 |
| battle action | `35` | 17 | 17 | length matches, offsets changed/rejected | 戰鬥行為指令 |
| interaction | `24` | 3 | 3 | structural match | 互動請求 family #1 |
| item request | `25` | 5 | 5 | current static + capture verified; beta corroborates offsets | 物品/道具互動請求 |
| world interaction | `26` | 7 | 7 | structural match | 世界互動請求 |
| interaction | `27` | 5 | 5 | structural match | 互動請求 family #2 |
| inventory activation | `28` | 5 | 5 | current static + capture verified; beta corroborates offsets | 物品啟用/裝備發動 |
| party | `8F` | 3 | 25 | changed/rejected | 組隊操作 |
| party | `90` | 2 | 5 | changed/rejected | 組隊操作 |
| party | `91` | 25 | 5 | changed/rejected | 組隊操作 |
| party | `92` | 25 | 2 | changed/rejected | 組隊操作 |
| party | `93` | 5 | 5 | structural match | 組隊操作 |
| party | `94` | 5 | 9 | changed/rejected | 組隊操作 |
| party | `95` | 2 | 2 | structural match | 組隊操作 |
| fight pet | `BB` | 5 | 85 | changed/rejected | 寵物戰鬥行為 |
| pet egg | `B7` | 2 | 5 | changed/rejected | 寵物蛋相關操作 |
| PK cursor | `69` | 45 | 45 | hypothesis only; current assistance is `6A`/12 | PK 目標定位 |
| pet PK | `B8` | 5 | 2 | changed/rejected | 寵物 PK |
| team | `5A` | 19 | 19 | structural match | 組隊操作 |
| vendor publish | `B0` | var | 3 | changed/rejected | 商店欄位發布 |
| vendor action | `B2` | 13 | 5 | changed/rejected | 商店操作 |
| vendor add record | `BD` | 5 | 25 | changed/rejected | 商店目錄新增 |

Result: 20 opcode/length matches (including two variable records) and 16 proven
length changes. Same-length `0x35` and `0x69` are explicitly not promoted.

### C2S completion progress snapshot (2026-08-16)

| Promotion status | Entries | What remains |
|---|---:|---|
| `CurrentBuildVerified` | 5 | Kept in runtime compatibility and considered completed |
| `StructuralCorroborationOnly` | 13 | Boundaries/RVA known; semantic confirmation still pending |
| `HypothesisOnly` | 1 | Observed only by static or public-beta context |
| `RejectedAsChanged` | 17 | Lengths diverge or fixed-point is incompatible |
| `Runtime-completion pending` | 5 | Subset of `StructuralCorroborationOnly` (`24`, `26`, `27`, `93`, `95`) where capture-only evidence still gates mutation |

Total C2S rows: `36` entries. Runtime-completion pending is a subset of structured rows above.  
Total S2C/inbound entries: `61`, all in progress by domain inventory.

### 97 項逐條對照（全量）

| # | 方向 | Domain | Opcode | 遊戲功能 |
|---:|---|---|---:|---|
| 1 | C2S | account login | `04` | 帳號登入請求 |
| 2 | C2S | route | `A9` | 路由/登錄入口查詢 |
| 3 | C2S | game login | `00` | 遊戲登入請求 |
| 4 | C2S | character create | `17` | 角色建立 |
| 5 | C2S | character catalog | `18` | 角色列表與欄位查詢 |
| 6 | C2S | character delete | `19` | 刪除角色 |
| 7 | C2S | character select | `1A` | 選擇角色 |
| 8 | C2S | character create | `1B` | 角色建立（備援） |
| 9 | C2S | combination step | `A4` | 合成流程步驟 |
| 10 | C2S | combination commit | `A5` | 完成合成/提交 |
| 11 | C2S | disconnect | `0A` | 角色登出與斷線 |
| 12 | C2S | attribute increment | `21` | 屬性加點 |
| 13 | C2S | text envelope | `33` | 聊天封包外層封裝 |
| 14 | C2S | text message | `2F` | 聊天訊息內容 |
| 15 | C2S | mail action | `AE` | 信件操作 |
| 16 | C2S | battle action | `35` | 戰鬥行為指令 |
| 17 | C2S | interaction | `24` | 互動請求 family #1 |
| 18 | C2S | item request | `25` | 物品/道具互動請求 |
| 19 | C2S | world interaction | `26` | 世界互動請求 |
| 20 | C2S | interaction | `27` | 互動請求 family #2 |
| 21 | C2S | inventory activation | `28` | 物品啟用/裝備發動 |
| 22 | C2S | party | `8F` | 組隊操作 |
| 23 | C2S | party | `90` | 組隊操作 |
| 24 | C2S | party | `91` | 組隊操作 |
| 25 | C2S | party | `92` | 組隊操作 |
| 26 | C2S | party | `93` | 組隊操作 |
| 27 | C2S | party | `94` | 組隊操作 |
| 28 | C2S | party | `95` | 組隊操作 |
| 29 | C2S | fight pet | `BB` | 寵物戰鬥行為 |
| 30 | C2S | pet egg | `B7` | 寵物蛋相關操作 |
| 31 | C2S | PK cursor | `69` | PK 目標定位 |
| 32 | C2S | pet PK | `B8` | 寵物 PK |
| 33 | C2S | team | `5A` | 組隊操作 |
| 34 | C2S | vendor publish | `B0` | 商店欄位發布 |
| 35 | C2S | vendor action | `B2` | 商店操作 |
| 36 | C2S | vendor add record | `BD` | 商店目錄新增 |
| 37 | S2C | account_login | `01` | 帳號握手回應、進入帳號通道 |
| 38 | S2C | account_login | `1D` | 帳號資料與角色目錄回傳 |
| 39 | S2C | account_login | `1E` | 帳號流程錯誤回報 |
| 40 | S2C | route | `47` | 路由節點/伺服器清單回傳 |
| 41 | S2C | route | `02` | 路由查詢錯誤 |
| 42 | S2C | game_login | `01` | 遊戲伺服器握手回應 |
| 43 | S2C | game_login | `1F` | 登入/進場結果（角色可進入場景） |
| 44 | S2C | game_login | `1E` | 登入驗證錯誤 |
| 45 | S2C | character | `03` | 角色目錄結果 |
| 46 | S2C | character | `17` | 角色操作結果（建立/更新/回傳欄位） |
| 47 | S2C | character | `1E` | 角色類錯誤回報 |
| 48 | S2C | combat | `1C` | 戰鬥名單快照（參戰者） |
| 49 | S2C | combat | `38` | 戰鬥控制分隔邊界 |
| 50 | S2C | combat | `83` | 單體戰鬥效果 |
| 51 | S2C | combat | `84` | 多目標戰鬥效果 |
| 52 | S2C | combat | `85` | 戰鬥回合/流程節點 |
| 53 | S2C | combat | `86` | 戰鬥控制紀錄 |
| 54 | S2C | combat | `87` | 戰鬥特殊控制片段 |
| 55 | S2C | combat | `88` | 戰鬥回合快照 |
| 56 | S2C | combat | `89` | 戰鬥結算摘要 |
| 57 | S2C | mission | `DF` | 任務世界同步（世界/進度） |
| 58 | S2C | gameplay | `22` | 玩家主狀態快照（生命、等級、主要屬性） |
| 59 | S2C | gameplay | `24` | 玩家體力／HP/MP 類快照 |
| 60 | S2C | gameplay | `2B` | 玩家神將/神獸進度（神魂、天階） |
| 61 | S2C | gameplay | `2C` | 玩家派生面板數值 |
| 62 | S2C | gameplay | `6F` | 玩家重生/角色繪製事件 |
| 63 | S2C | gameplay | `71` | 怪物重生/刷新 |
| 64 | S2C | gameplay | `72` | NPC 重生/刷新 |
| 65 | S2C | gameplay | `30` | 神將/神獸新增 |
| 66 | S2C | gameplay | `31` | 神將/神獸狀態快照 |
| 67 | S2C | gameplay | `32` | 神將技能目錄切換 |
| 68 | S2C | gameplay | `33` | 神將介面旗標 |
| 69 | S2C | gameplay | `34` | 神將技能新增 |
| 70 | S2C | gameplay | `35` | 神將技能等級更新 |
| 71 | S2C | gameplay | `39` | 神將生命／戰鬥數值快照 |
| 72 | S2C | gameplay | `D5` | 神將面板參考欄位 |
| 73 | S2C | gameplay | `D6` | 神將面板加成欄位 |
| 74 | S2C | gameplay | `DD` | 神將移除 |
| 75 | S2C | social | `DB` | 聊天頻道設定快取（server_text 類） |
| 76 | S2C | social | `19` | 本地化文字模板事件 |
| 77 | S2C | social | `20` | 狀態式文字模板事件 |
| 78 | S2C | pet | `E7` | 寵物紀錄新增 |
| 79 | S2C | pet | `E8` | 寵物紀錄更新 |
| 80 | S2C | pet | `E9` | 寵物事件 |
| 81 | S2C | pet | `EA` | 寵物蛋狀態 |
| 82 | S2C | pet | `EB` | 寵物蛋事件 |
| 83 | S2C | pet | `4A` | 坐騎狀態同步 |
| 84 | S2C | tong | `98` | 宗門／幫派快照（variable） |
| 85 | S2C | tong | `8E` | 宗門／幫派事件 |
| 86 | S2C | tong | `8F` | 宗門／幫派事件 |
| 87 | S2C | tong | `90` | 宗門／幫派事件 |
| 88 | S2C | tong | `91` | 宗門／幫派事件 |
| 89 | S2C | tong | `92` | 宗門／幫派事件 |
| 90 | S2C | tong | `93` | 宗門／幫派事件 |
| 91 | S2C | tong | `94` | 宗門／幫派事件 |
| 92 | S2C | tong | `95` | 宗門／幫派事件 |
| 93 | S2C | tong | `96` | 宗門／幫派事件 |
| 94 | S2C | tong | `97` | 宗門／幫派事件 |
| 95 | S2C | tong | `99` | 宗門／幫派事件 |
| 96 | S2C | tong | `9A` | 宗門／幫派事件 |
| 97 | S2C | tong | `9B` | 宗門／幫派事件 |

### S2C domain inventory

| Domain | Entries | Current disposition | 遊戲功能 |
|---|---:|---|---|
| account login | 3 | current framing changed; hypothesis only | 登入帳號流程與錯誤回報 |
| route | 2 | request changed; response fields not promoted by symmetry | 路由切換與服務回應 |
| game login | 3 | current login request/result lengths changed | 遊戲進入結果（成功/失敗） |
| character | 3 | C2S boundaries match; response fields remain evidence-gated | 角色欄位變更與結果回傳 |
| gameplay/player/immortal/spawn | 17 | promote per opcode; current `22/24` lengths, frozen `31/39`, and exact-current static `2C` consumer have independent evidence | 玩家即時屬性、神獸與怪物/生成點資料 |
| social | 3 | current general output is `5C`; beta `19/20/DB` remain hypotheses | 聊天/訊息類事件 |
| battle | 9 | tracked by the current battle cross-version ledger | 戰鬥回報（排程、效果、戰果） |
| mission | 1 | exact-current `DF` minimal payload is three bytes; beta 13-byte subtype/u32/u32 layout rejected | 任務世界狀態回填 |
| pet | 6 | request families changed; no response promotion | 寵物狀態同步 |
| Tong | 14 | current handlers exist, layouts/business meanings not verified | 幫派/宗門訊息 |

### S2C opcode-level function map（完整 61 筆）

| Domain | Opcode | Name | 遊戲功能 |
|---|---:|---|---|
| account_login | `01` | account_server_hello | 帳號握手回應、進入帳號通道 |
| account_login | `1D` | account_record | 帳號資料與角色目錄回傳 |
| account_login | `1E` | account_error | 帳號流程錯誤回報 |
| route | `47` | route_catalog | 路由節點/伺服器清單回傳 |
| route | `02` | route_error | 路由查詢錯誤 |
| game_login | `01` | game_server_hello | 遊戲伺服器握手回應 |
| game_login | `1F` | game_login_result | 登入/進場結果（角色可進入場景） |
| game_login | `1E` | game_login_error | 登入驗證錯誤 |
| character | `03` | character_catalog_selector_result | 角色目錄結果 |
| character | `17` | character_result | 角色操作結果（建立/更新/回傳欄位） |
| character | `1E` | character_error | 角色類錯誤回報 |
| combat | `1C` | combat_roster_record | 戰鬥名單快照（參戰者） |
| combat | `38` | combat_control_boundary_38 | 戰鬥控制分隔邊界 |
| combat | `83` | combat_single_action | 單體戰鬥效果 |
| combat | `84` | combat_multi_target_action | 多目標戰鬥效果 |
| combat | `85` | combat_sequence_boundary_85 | 戰鬥回合/流程節點 |
| combat | `86` | combat_control_record | 戰鬥控制紀錄 |
| combat | `87` | fightgod_spc_record_control_87 | 戰鬥特殊控制片段 |
| combat | `88` | combat_round_snapshot | 戰鬥回合快照 |
| combat | `89` | combat_settlement | 戰鬥結算摘要 |
| mission | `DF` | mission_sync | 任務世界同步（世界/進度） |
| gameplay | `22` | player_status_snapshot | 玩家主狀態快照（生命、等級、主要屬性） |
| gameplay | `24` | player_vitals_snapshot | 玩家體力／HP/MP 類快照 |
| gameplay | `2B` | player_god_progress_snapshot_2b | 玩家神將/神獸進度（神魂、天階） |
| gameplay | `2C` | player_derived_stats_snapshot | 玩家派生面板數值 |
| gameplay | `6F` | player_spawn_6f | 玩家重生/角色繪製事件 |
| gameplay | `71` | monster_spawn_71 | 怪物重生/刷新 |
| gameplay | `72` | npc_spawn_72 | NPC 重生/刷新 |
| gameplay | `30` | god_add_30 | 神將/神獸新增 |
| gameplay | `31` | god_status_snapshot_31 | 神將/神獸狀態快照 |
| gameplay | `32` | god_catalog_selector_32 | 神將技能目錄切換 |
| gameplay | `33` | god_ui_flags_33 | 神將介面旗標 |
| gameplay | `34` | god_skill_add_34 | 神將技能新增 |
| gameplay | `35` | god_skill_level_35 | 神將技能等級更新 |
| gameplay | `39` | god_vitals_snapshot_39 | 神將生命／戰鬥數值快照 |
| gameplay | `D5` | god_panel_reference_bank_d5 | 神將面板參考欄位 |
| gameplay | `D6` | god_panel_modifier_bank_d6 | 神將面板加成欄位 |
| gameplay | `DD` | god_remove_dd | 神將移除 |
| social | `DB` | text_channel_selection_db | 聊天頻道設定快取（server_text 類） |
| social | `19` | localized_template_text_19 | 本地化文字模板事件 |
| social | `20` | stateful_template_text_20 | 狀態式文字模板事件 |
| pet | `E7` | fpet_add_record_e7 | 寵物紀錄新增 |
| pet | `E8` | fpet_update_record_e8 | 寵物紀錄更新 |
| pet | `E9` | fpet_event_e9 | 寵物事件 |
| pet | `EA` | pet_egg_state_ea | 寵物蛋狀態 |
| pet | `EB` | pet_egg_event_eb | 寵物蛋事件 |
| pet | `4A` | mount_state_4a | 坐騎狀態同步 |
| tong | `98` | tong_snapshot_98 | 宗門／幫派快照（variable） |
| tong | `8E` | tong_event_8e | 宗門／幫派事件 |
| tong | `8F` | tong_event_8f | 宗門／幫派事件 |
| tong | `90` | tong_event_90 | 宗門／幫派事件 |
| tong | `91` | tong_event_91 | 宗門／幫派事件 |
| tong | `92` | tong_event_92 | 宗門／幫派事件 |
| tong | `93` | tong_event_93 | 宗門／幫派事件 |
| tong | `94` | tong_event_94 | 宗門／幫派事件 |
| tong | `95` | tong_event_95 | 宗門／幫派事件 |
| tong | `96` | tong_event_96 | 宗門／幫派事件 |
| tong | `97` | tong_event_97 | 宗門／幫派事件 |
| tong | `99` | tong_event_99 | 宗門／幫派事件 |
| tong | `9A` | tong_event_9a | 宗門／幫派事件 |
| tong | `9B` | tong_event_9b | 宗門／幫派事件 |

### 97 項功能對照完成狀態

- C2S：36/36（全部補齊 `遊戲功能`）
- S2C：61/61（全部補齊 `遊戲功能`）
- 合計：97/97

### 97 項後續擱置（先不合併到可用 runtime）

- `C2S 24`（interaction family #1）：目前僅有 capture 證據，未進入可回放的 runtime mutation。
- `C2S 26`（world interaction）：目前僅有 capture 證據，未進入可回放的 runtime mutation。
- `C2S 27`（interaction family #2）：目前僅有 capture 證據，未進入可回放的 runtime mutation。
- `C2S 93`（party 家族 #1）：目前僅有 capture 證據，未進入可回放的 runtime mutation。
- `C2S 95`（party 家族 #2）：目前僅有 capture 證據，未進入可回放的 runtime mutation。

### 97 項 runtime 補齊待辦（含遊戲功能）

| # | 方向 | Opcode | 遊戲功能 | 狀態 | 下一步 |
|---|---|---:|---|---|---|
| 1 | C2S | `24` | 互動請求 family #1 | capture-only，已可路由 | 先針對 `RawValue0/RawValue1` 收正式世界回放，補齊「互動命令→DB/狀態」證據，再切 `ProductionMutationAllowed=true` |
| 2 | C2S | `26` | 世界互動請求 | capture-only，已可路由 | 先跑 `world interaction` 控制案例（最小化/中性化）截包，驗證每組 `RawValue0/RawValue2/RawValue4/尾碼` 的副作用後才開啟 mutation |
| 3 | C2S | `27` | 互動請求 family #2 | capture-only，已可路由 | 先補齊代表性交互 payload 的世界截包對照，確認回應/快照差異為 deterministic 後再啟用正式作用 |
| 4 | C2S | `93` | 組隊操作 | capture-only，已可路由 | 先用正式客戶端穩態腳本分別回放 `93` 邀請/回應/拒絕等分支，對齊隊伍成員清單變更證據 |
| 5 | C2S | `95` | 組隊操作 | capture-only，已可路由 | 先抓 `95` 全量變體並比對隊伍狀態（建立/離隊/踢出）前後差分，避免舊版 binding 進入 production |

> 狀態說明：這 5 筆目前都已完成「協定對照與 transport 運作」，但都還未上線有副作用的正式 mutation；每完成一筆可直接對照此表把狀態更新為已完成，並補上新實測證據哈希。

## Formal-server additions in this batch

- `OfficialPublicBetaCrossVersionEvidence` is the machine-readable 36-row C2S
  comparison plus the complete 61-entry S2C domain inventory.
- `OfficialPlayerSnapshotCandidateWireCodec` decodes current application-record
  boundaries for `21`, `22` and `24`, preserves unknown fields and keeps runtime
  mutation disabled. Public-beta names are explicitly candidates.
- `OfficialPlayerDerivedStatsWireCodec` promotes the exact-current `2C`
  local-panel consumer, while `OfficialMissionSlotSyncWireCodec` promotes the
  changed current `DF` three-byte payload. Both remain runtime-registration
  gated.
- Existing exact-current `25`, `28` and `2F` codecs now cite the public-beta
  archive as an independent offset/shape corroboration source.
- Party, pet, vendor cart, login/route and changed battle layouts are explicitly
  rejected, preventing old meanings from silently entering current runtime code.

## Non-network data and resource evidence

The ZIP also contains reconstructed parsers/tests and corpus contracts for 50
public-beta `.csvZ` files (4,229,682 decoded bytes, 41,510 rows and 384,162
fields). It does **not** contain those original decoded data rows. Therefore the
formats and cross-table rules are formal evidence, while direct MariaDB gameplay
import remains disabled until the exact-current client supplies the values.

`OfficialPublicBetaDataEvidence` records 16 feature/data domains:

| Domain | Public-beta contract useful to the formal server | Import boundary |
|---|---|---|
| packed CSVZ | `05 16`, CRLF/quote/NUL and row-consumption rules | parser regression only |
| sound index | 1,024 sequential slots; empty slots are intentional | presentation only |
| NPC text | mission IDs 1..9711 and normal IDs 1..1706 are separate | current text rows required |
| NPC/map presence | sparse appearances and unresolved references are valid | no invented foreign keys |
| dialogs/missions | conversations, choices, missions and steps use independent key spaces | equal integers are not joins |
| activity requirements | 200 rows, four item/quantity pairs, exact `0,0` empties | no completion/consumption/reward policy |
| sectioned GameData | 57 sections and 12,958 rows in that beta corpus | counts remain version-specific |
| consumables | 31 MED02 authoring rows and request-route clues | no effect arithmetic |
| monsters | 2,146 identity/presentation source rows | no stats or growth derivation |
| skill books/effects | 259/259 identity-to-effect joins | no MP/damage/targeting authority |
| god menu | ten level-one four-ability baselines | no growth, HP/MP or derived formulas |
| message/flat tables | lossless prologues, annotations and five message sections | current localized rows required |
| historical server groups | seven groups/34 endpoints | never contact beta endpoints |
| fight-common resources | two versioned ten-row ROM manifests | presentation only |
| combat-special | 343 active client metadata records | server formulas remain blocked |
| map/resource formats | CAN/MBD/MDT/MMB/RTB/model parser boundaries | current map IDs/triggers required |

This converts the rest of the package into actionable extractor/schema evidence
without pretending that documentation counts are production data.

## Next exact-current evidence targets

1. Controlled one-at-a-time clicks for all four `21` attribute buttons, with
   before/after `22` snapshots, to bind codes 1..4 in the current build.
2. Exercise the newly recovered current `2C` 61-byte canonical record through
   the official client while changing exactly one equipment/immortal source;
   static evidence proves the local panel layout, while live acceptance is the
   remaining runtime-registration gate.
3. Exercise current `DF` through the official client to confirm its three-byte
   canonical application boundary; capture pet and Tong records before enabling
   those families. Existing `31/39` work is retained and is not a new-analysis
   target.
4. Runtime-completion pending items from this batch (capture-only so far):  
   4.1. `mission` `24`: perform live in-world interaction captures for both
   bytes in `RawValue0`/`RawValue1` and map each to a server-observable side effect.
   4.2. `mission` `26`: trigger controlled world-interaction cases to confirm
   per-two-byte action codes and tail behavior before enabling runtime mutation.
   4.3. `mission` `27`: replay representative interaction payloads and verify
   deterministic response/snapshot deltas for each major value.
   4.4. `party` `93`: execute each party-family opcode path and prove which raw
   payload shape belongs to invite/response/leave before enabling DB effects.
   4.5. `party` `95`: execute and capture `95` variants to prove command intent and
   guard against accidental old-layout binding before allowing mutation.
5. Done (`2026-08-16`): Recover current producers for the structurally stable `24`, `26`, `27`, `5A`,
   `93`, `95` and `A4` families; RVA-backed evidence and boundary assertions are now
   recorded and runtime-compatibility routing remains covered in protocol/runtime tests.
