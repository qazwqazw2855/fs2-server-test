# God2 Classic Server 真實可玩化 RoadMap

更新日期：2026-08-05  
RoadMap 狀態：ACTIVE／EVIDENCE GATED  
目前里程碑：M1 COMPLETE；M2 BLOCKED；M3–M7 IN PROGRESS。M7 已開始角色生命週期的 MariaDB authority／零角色清單切片；Create/Delete/Rename 官方 Client family 仍 evidence-blocked。

## 完成定義

功能只有完整走通下列鏈路才算完成：

`MariaDB Authority → Runtime Entity → Gameplay Rule → Replication/Command → Official Serializer → Official Client 功能狀態 → MariaDB Persistence`

Backend、Headless、資料筆數、一般流量、Heartbeat 或 Client 未崩潰，都不能單獨標記完成。

狀態只允許：

- `NOT STARTED`：尚未開始實作或調查。
- `IN PROGRESS`：正在處理，尚未通過完整完成條件。
- `BLOCKED`：缺少必要官方證據、資料或安全環境，不以猜測或 fallback 繞過。
- `COMPLETE`：該里程碑的 Production、MariaDB、Runtime、官方 Client 功能驗收及持久化條件均已通過。

## 固定原則

- 官方 Client 只是本服前端，不同步或依賴官方營運伺服器。
- 帳號、角色、位置、內容與進度由本服 MariaDB 管理。
- Runtime cache 只能保存可由 MariaDB 重建的 snapshot。
- `LegacyCompatibilityBootstrap` 只能保留已驗證 framing 與 opaque 區域，不能充當角色、NPC、怪物或玩法 Authority。
- Production 禁止 InMemory、TestOnly、登入繞過、假資料與 Fake Network Bytes。
- 缺乏必要證據時 fail-closed。
- `LegacyPrimary` 維持正式戰鬥引擎；`ActorPrimary` 不啟用。
- 不要求玩家手動登入、操作或抓新封包。
- 不覆寫歷史 Phase Reports、Artifacts、Golden Evidence 或已執行 Migration 031–034。

## Production Truth Baseline

### Authority matrix

| 資料／行為 | Authority source | Production 狀態 |
|---|---|---|
| 帳號驗證 | MariaDB `accounts` | Production reachable |
| 角色身分、名稱、職業、Map/X/Y | MariaDB `characters` | M1 official Client verified |
| Login／World session ownership | MariaDB session/CAS + Runtime session | Production reachable |
| Static maps、NPCs、monsters、spawns | MariaDB static repository／immutable runtime snapshot | Production reachable；內容仍受 evidence gate |
| `MapRuntime`、AOI、player entity、session binding | Runtime，由 MariaDB snapshot 建立 | Production reachable |
| Client map identity | MariaDB `client_map_identities` | Map 19／3 verified and production enabled |
| 玩家 world bootstrap | Runtime player projection + build-pinned official framing | M1 official Client verified |
| NPC replication | MariaDB `npc_spawns` → `MapRuntime` → session-targeted queue → build-pinned serializer → TCP | IN PROGRESS；2 個 Verified spawn 與 0x60／0x75 production delivery 已接通，官方 Client lifecycle 尚未驗收 |
| Monster replication | 尚缺可唯一交叉的 Monster slice 與已驗證 S2C family | BLOCKED |
| Battle／Skill／Inventory official wire | Runtime backend 多數存在，Client family 未完成 | BLOCKED／partial |
| 角色建立出生點 | MariaDB `character_creation_profiles`，同時受 map／Client build identity gate | Production wired；目前沒有可安全 promotion 的 profile，因此 fail-closed |
| 角色建立／刪除／改名 | MariaDB `characters` transaction + account ownership | Backend authority IN PROGRESS；官方 Client command/result family 尚未完成 |

### Production 玩家進世界呼叫鏈

`ConsoleEntry`
→ `MariaDbAccountRepository`／`MariaDbCharacterRepository`
→ `StartupPipeline` 載入 MariaDB static snapshot
→ `MariaDbWorldSessionCoordinator`
→ Login／Server Selection／PendingWorld
→ world handshake 重新讀取 MariaDB `characters`
→ Login→World CAS
→ `MariaDbWorldContentRepository`
→ `WorldSessionCoordinator.BindAsync`
→ shared `MapRuntime` + player runtime entity + session binding
→ `MariaDbClientMapIdentitySource`
→ `LegacyCompatibilityWorldBootstrapProjector`
→ authoritative name／CharacterId／Client Map／Area／X／Y projection
→ official build-pinned serializer
→ TCP send
→ 成功後進入 `InWorld`

任一步失敗時，Production 會拒絕進世界並清除 binding；不切換 InMemory 或 TestOnly fallback。

## 里程碑狀態

| 里程碑 | 狀態 | 現況 |
|---|---|---|
| M0 — RoadMap 與 Truth Baseline | `COMPLETE` | RoadMap、程式、MariaDB 與 artifacts 已對齊 |
| M1 — 玩家與世界 Authority Cutover | `COMPLETE` | MariaDB 角色身分與位置已經 production 投影至官方 Client；Map 19／3 identity 與合法任意 X/Y 已驗證 |
| M2 — Map 19↔3 真實內容切片 | `BLOCKED` | 兩張地圖 identity 與 Map 19 正式 NPC slice 已完成；完整 Monster spawn/stat/AI/formation/reward slice 仍無安全候選 |
| M3 — 真實 NPC／怪物 Replication | `IN PROGRESS` | 2 個 MariaDB NPC 的 0x72 production spawn 與 session-targeted 0x60／0x75 delivery 已接通；Monster、durable outbox 與官方 Client lifecycle 驗收仍 blocked |
| M4 — NPC 互動與服務 | `IN PROGRESS` | handle 1504 的 MariaDB Merchant binding、C2S 0x37→S2C 0x7A open、C2S 0x39 close 與 ordinary movement authority 已接通；standalone Dialog 與 official Client acceptance 仍 blocked |
| M5 — 怪物戰鬥與技能 | `IN PROGRESS` | C2S 0x35 layout 與 Battle receive dispatch 已固定在正式 Client Build；MariaDB encounter／skill execution 合格內容皆為 0，S2C record semantics 仍 blocked，Production mutation 維持關閉 |
| M6 — 掉落、背包與持久化 | `IN PROGRESS` | Production 已接通 MariaDB item catalog 與 inventory authority；CAS、exactly-once、rollback、並發與 restart fixture PASS。M5、reward/drop content、equipment transaction 與 official serializer/client acceptance 仍 blocked |
| M7 — 角色生命週期與 Gameplay Families | `IN PROGRESS` | 零角色官方清單、MariaDB creation authority、原子 create/delete/rename、active-name reuse 與 durable idempotency 已接通；正式 command/result serializer 與各 Gameplay family 驗收仍 blocked |
| M8 — 全世界內容擴張 | `IN PROGRESS` | Current code/runtime gate PASS；仍有逐 map／family evidence blockers |
| M9 — Production Closure | `IN PROGRESS — BLOCKED` | Physical DB、backup/restore、current regression 與 smoke-soak PASS；完整 Client family matrix、7,200 秒 soak 及 M2–M8 closure 仍 blocked |

## M0 — RoadMap 與 Truth Baseline

狀態：`COMPLETE`

- 已區分 MariaDB Authority、Runtime snapshot、Client compatibility framing 與 EvidenceBlocked family。
- Phase 3 content recovery PASS 不再被描述為可玩 PASS。
- hard-coded legacy bootstrap、NPC／Monster serializer blocker 均保留為明確技術債。

## M1 — 玩家與世界 Authority Cutover

狀態：`COMPLETE`

### 已完成

1. Production composition 使用 `MariaDbWorldSessionCoordinator` 與 `MariaDbClientMapIdentitySource`；不存在 Production InMemory fallback。
2. World handshake 重新讀取 MariaDB `characters`，CAS 成功後才建立 `MapRuntime`、player entity 與 session binding。
3. Map 3：MariaDB `maps.Id=1675308248` ↔ Client `MapId=3/AreaId=4` ↔ `cityi2/cityi2.mdt`，Migration 036。
4. Map 19：MariaDB `maps.Id=557790525` ↔ Client `MapId=19/AreaId=4` ↔ `indoor/groceryl.hmd`，Migration 038。
5. Client X/Y consumer 已由 build-pinned 靜態程式碼證明為 15-bit packed world coordinates；resource grid 使用 `/21` 驗證。
6. `SerializeWorldProjectionDestination` 僅允許已 promotion 的 build／map／area，並拒絕超出 packed range 的位置；MariaDB projector 另驗證 resource bounds。
7. 動態改寫 player spawn 的 Name／CharacterId 後會重新計算官方 checksum。這是先前 production Client 顯示「連接錯誤」的實際根因。
8. 以安全 CAS 將 MariaDB character 1 從 Map 19 `(28,34)` 改為 `(29,35)`；下一次登入的 Server 投影記錄為 `runtimeMap=557790525, clientMap=19, clientArea=4, clientX=29, clientY=35`。
9. 自動化官方 Client 由本機 endpoint 登入，顯示 MariaDB 名稱 `G2A`，並進入 Map 19 世界畫面；manual operation：NO，new capture：NO，Fake Network Bytes：0。

### 驗收邊界

- 舊 `Invoke-FrozenProtocolRegression` 的 aggregate status 仍是 `BLOCKED`，唯一原因是本輪禁止新增 packet capture，故 `protocolMetadataAcceptance=NOT RUN — NO PACKET CAPTURE`。此歷史腳本結果未被改寫成 PASS。
- M1 的功能驗收依據是 MariaDB CAS、Production exact projection log、build-pinned Client coordinate consumer、官方 Client world screen state 與動態角色名稱；不以 heartbeat 或一般流量代替。
- M1 closure 當時畫面中的 NPC 仍來自 legacy bootstrap，不能算當時的 M3 證據；此歷史判定未被改寫。後續 M3 已移除該 84-byte entity 尾段並改由 MariaDB Runtime replication 送出。

證據：

- [RoadMap.M1.PlayerWorldAuthority.md](../Reports/RoadMap.M1.PlayerWorldAuthority.md)
- [RoadMap.M1.MapIdentityEvidence.md](../Reports/RoadMap.M1.MapIdentityEvidence.md)
- [M1 final-summary.json](../Artifacts/RoadMap/M1/final-summary.json)
- [Production arbitrary-position run](../Artifacts/CharacterLifecycleRegression/m1-production-arbitrary-29-35-20260803/frozen-regression-host-result.json)
- [Official Client world screenshot](../Artifacts/ClientInstrumentation/ElevatedAutomationHost/host-run-20260803-211847/enter-world-post-projection-stable.png)

## M2 — Map 19↔3 真實內容切片

狀態：`BLOCKED`

已完成地圖 identity、resource provenance、coordinate scale、bounds 與 Portal 19↔3 的既有正式 wire 證據。

Map 19 NPC slice 已實際完成：

- `npcs.Id=1075128734`／`雜貨老闆`／`npc2643.ROM`／`Merchant` 已由官方 Client static consumer、NPC.csvZ row 249 與 formal production identity 唯一交叉。
- S2C `0x72` 與 Client position consumer 證明兩個生成點 `(17,8)`、`(14,14)`。
- Migration 039／040 新增 `npc_client_identities` 與 `npc_spawns`，實際落庫 1 個 client identity、2 個 production spawns。
- 實際通過 `MariaDbStaticDataLoader → MariaDbWorldContentRepository → MapRuntimeFactory → NpcObject`，Runtime validation errors 0。

目前 first broken node 已縮小為完整 Monster slice：MariaDB strict complete candidate count 為 0。既有 battle Capture 能證明 encounter／attack／settlement／EXP／item events，但不能唯一交叉 formal MonsterId，也不能同時證明 world spawn、MP/stat、AI／formation 與 reward/drop policy。這些欄位不使用任意座標、預設 HP/MP、自創公式或未知掉率補齊。

證據：

- [M2 real content slice](../Reports/RoadMap.M2.RealContentSlice.md)
- [M2 runtime validation](../Reports/RoadMap.M2.RuntimeValidation.md)
- [M2 final summary](../Artifacts/RoadMap/M2/final-summary.json)

## M3 — 真實 NPC／怪物 Replication

狀態：`IN PROGRESS`

### 已完成的 NPC production slice

- Migration 041 只為兩筆唯一交叉的 `npc_spawns` 新增 build-pinned wire identity、selector、direction/state、application hash 與 opaque-region hash；沒有把其餘 NPC 猜成可序列化。
- `MariaDbWorldContentRepository → MapRuntimeFactory → NpcObject → ReplicationRuntime` 已保留上述 wire identity。
- `OfficialNpcReplicationWireCodec` 以官方 Client build `god2-opt-6b127086e0c0` 固定 0x72 spawn；兩筆 captured-position application SHA-256 必須逐筆相符，否則不產生任何 network bytes。
- 0x60 update 由既有 captured record 與 Client packed-position consumer 固定；0x75 fixed length=5、low-16-bit handle lookup 與 cleanup call 已由唯讀 runtime-code snapshot 可重現驗證。
- MariaDB authoritative bootstrap 會精確移除 84-byte legacy entity tail，其餘 bootstrap frame 與 checksum 重新驗證；NPC 不再有 legacy／MariaDB 雙重 Authority。
- Production `TcpNetworkHost` 在進入 `InWorld` 前 preflight queue，接著以 ordered runtime queue 將兩筆 0x72 frame 送到同一 TCP stream。未知、停用、非本 build 或 hash 不符的 NPC 保持 `SerializerBlockedByEvidence`。
- Runtime update／despawn event 只會投遞給實際可見且已生成該 entity 的 session；50ms connection-local drain 讓非同步 lifecycle 不必等待下一個 gameplay command。重複且無消費者的 replication broadcast queue 已移除。
- Replication emitter 先完成同一 session 全批次 serialization；任一 frame blocked 時整批 0 bytes，全部 Ready 才依 spawn→update→despawn 順序送出，避免 preflight 後的部分輸出。
- MariaDB `OpaqueTemplateSha256` 現在會傳入 Runtime wire identity，並與 build-pinned opaque bytes 逐值核對；64 字元但非 hex 或與正式 template 不同都會 fail-closed。
- Migration 042 為 legacy `spawns` 新增 `SpawnRadius`、`SpawnCount`、`EvidenceStatus` 與 `ProductionEnabled`；既有未知 rows 預設停用。Runtime 不再用 Level／HP／Attack／Defense 零值或固定 Count／Radius 冒充正式 Monster spawn。
- 實體 MariaDB probe：repository=`MariaDbWorldContentRepository`、runtime NPC=2、serializer Ready=2、hash match=2、headless sent=2、blocked=0、remaining queue=0、Fake Network Bytes=0。

### 尚未完成

- 其餘 317 個 formal NPC 仍沒有足以產生官方 Client entity selector／handle 的唯一 wire identity；它們仍在內容資料庫，不會被刪除，但不會被猜測送出。
- 0x75 尚無 retained raw application record；目前為靜態 consumer 推導，官方 Client 的 AOI leave／despawn／reconnect lifecycle 尚未驗收。
- NPC replication queue 目前為 connection-scoped ordered runtime queue，尚未接成 MariaDB durable transactional outbox。
- Monster family 仍缺唯一完整 content slice 與 spawn／update／despawn wire 證據。

因此 M3 不能標記 `COMPLETE`。目前 first broken nodes 為 `durable NPC lifecycle acceptance` 與 `Monster replication family`，沒有以 fallback 或 Fake Network Bytes 取代。

證據：

- [M3 NPC replication](../Reports/RoadMap.M3.NpcReplication.md)
- [M3 runtime validation](../Reports/RoadMap.M3.RuntimeValidation.md)
- [M3 NPC wire evidence](../Artifacts/RoadMap/M3/npc-wire-evidence.json)

## M4 — NPC 互動與服務

狀態：`IN PROGRESS`

> 下方「舊 evidence checkpoint」保留用來記錄被新證據推翻的判定；current source 狀態以本節後半的 Current Checkpoint 為準。

### Current Checkpoint

- 既有 15 組 action-bearing capture 已可用 exact-session cipher 狀態離線還原；C2S sequence 5／46 為 checksum-valid `0x37(handle=1504)`，並各自在 136ms 後對應相同的 checksum-valid S2C `0x7A`。Sequence 45／70 則為相同 handle、state 0 的 checksum-valid `0x39` Merchant close。
- Client `RVA 0x00089511–0x0008953F` 證明互動距離為 X、Y 各自絕對差不超過 3。
- `ConsoleEntry → TcpNetworkHost → world-session cipher decode → OfficialNpcInteractionClosedLoop → AuthoritativeWorldSessionInteractionTargetResolver → WorldInteractionCoordinator → MerchantServiceOpenInteractionHandler → OfficialNpcInteractionWireCodec → same TCP stream` 已接通。Close 會重新驗證 active session、handle、family、runtime entity、template 與 map，再原子移除 state，且不猜測回應 bytes。
- Target 必須來自目前 session 的 MariaDB-backed `MapRuntime`，且 official handle 唯一、build/status verified、ownership/map/instance/state/range 全部通過。
- 第一個 S2C 0x7A profile 只允許 handle 1504；完整 application SHA-256 為 `18BD3B82419B71C43BD7B84AC32FBE0B84320BC8AF473759C1D838EDE807560E`。其他 handle 零輸出 fail-closed。
- `0x86` 只在既有 QuestScriptedTransfer capture 出現，欄位為 83／4609 且 caller 不同；Production 明確拒絕把它當 NPC Dialog close。
- Decoder、checksum、truncated/malformed、wrong-state、unsupported profile、range、ownership、open replay、close replay 及 position-unsynchronized 已有 targeted regression。
- 既有世界封包 `0A0080B8D9C14BA69488` 經同一 session 的正式 world transport key 解碼、長度與 checksum 驗證後為 `0A002E12000C0001FF72`：opcode `0x2E`、X=18、Y=12、sequence=1、mounted state=`0xFF`。舊 artifact 的啟發式 XOR 大座標不是 transport-verified plaintext，已停止使用。
- `TcpNetworkHost → IWorldMovementStore → MariaDbPortalTransitionStore` 現以鎖定讀取和 expected-position/runtime-version CAS 原子提交 X/Y/direction，再更新 player `RuntimeEntity`／`PlayerObject`、AOI 與 NPC distance authority。bounds、非相鄰步幅、錯序、重播、持久化失敗全部 fail-closed。
- 成功 Portal commit 會先從同一 `IPortalTransitionStore` 重載目標 Map/X/Y，再以 `IWorldSessionTransitionCoordinator` 將主 `WorldSessionBinding` 從來源 `MapRuntime` 移除並在目標 runtime 建立唯一玩家；失敗時在 outbox dispatch／Client frame send 前關閉連線，重連由 MariaDB 重建，禁止半同步世界。
- Focused movement／portal authority regression：9 / 9 PASS；Full tests 2,843 / 2,843 PASS；Debug / Release / Format / NuGet / credential / sensitive-output 全部通過。
- Cancellation、runtime exception 或 serializer preflight divergence 會以 compare-and-remove 清除本次新增的 active interaction；取消後重試已由回歸測試證明不會誤判為既有互動。
- Current-source MariaDB physical runtime probe：PASS；實際使用 `MariaDbWorldContentRepository`，handle 1504 解析到 placement `316049902`、template `1075128734`、MerchantId `286397401`，open／close／close replay 語意全部通過；這不等同 official Client M4 acceptance。
- Current-source MariaDB movement fixture：PASS；實際使用 `MariaDbPortalTransitionStore`／`IWorldMovementStore`，17,12→17,11、direction North、RuntimeVersion 0→1，stale expected-position replay 被 `movement.position_conflict` 拒絕；實體內容 Map 557790525（Client 19）→1675308248（Client 3）於 196,139 的主 Runtime rebind PASS；隔離 fixture cleanup PASS。

Current blockers：

- Standalone non-Merchant Dialog 的 target-specific `0x7A` profile 與唯一 close branch 尚未完成；Quest transfer `0x86` 不可混用。
- Movement promotion 目前鎖定既有 LegacyMap19 world transport 與 capture-verified state `0xFF`；其他 Client profile／state 沒有證據時繼續 fail-closed。
- Automated official Client M4 feature-specific acceptance 尚未執行，所以 M4 不得標記 `COMPLETE`。

Current first broken node：`Standalone Official Dialog Profile / Close Contract`。

Current first broken edge：`non-Merchant C2S 0x37 → target-specific S2C 0x7A → uniquely paired close command`。

### Superseded evidence checkpoint

早期因未解析 exact-session cipher state，而將 C2S opcode、range policy 與 Dialog handler 判為 EvidenceBlocked；目前已由可重現的 existing-capture recovery 推翻。舊結論只保留於先前產物的歷史脈絡，不再作為 current-source 狀態，也不再與本節 Current Checkpoint 並列成互相矛盾的 gate。

Current M4 evidence gates：movement authority 與 Merchant slice PASS；Standalone Dialog BLOCKED。Official Client M4 acceptance：`NOT RUN — STANDALONE DIALOG / CLIENT FEATURE ACCEPTANCE BLOCKED`。Manual operation：NO；new capture：NO；Client write：NO；evidence-recovery network emission：0；Fake Network Bytes：0。

證據：

- [M4 NPC interaction evidence](../Reports/RoadMap.M4.NpcInteractionEvidence.md)
- [M4 runtime validation](../Reports/RoadMap.M4.RuntimeValidation.md)
- [M4 evidence artifact](../Artifacts/RoadMap/M4/npc-interaction-evidence.json)
- [M4 static recovery artifact](../Artifacts/RoadMap/M4/npc-interaction-static-recovery.json)
- [M4 existing-capture exhaustion](../Artifacts/RoadMap/M4/existing-capture-exhaustion.json)
- [M4 MariaDB runtime validation](../Artifacts/RoadMap/M4/mariadb-runtime-validation.json)
- [M4 movement authority evidence](../Artifacts/RoadMap/M4/world-movement-authority.json)
- [M4 MariaDB movement validation](../Artifacts/RoadMap/M4/mariadb-world-movement-validation.json)

## M5 — 怪物戰鬥與技能

狀態：`IN PROGRESS`

### Current Checkpoint

- 現有六組中文標記 Battle capture 已使用各 session 的正式 cipher 狀態離線重播：140 個 action windows、C2S 8 個 opcode、S2C 20 個 opcode、checksum failure 0。Artifact 只保存 opcode、length、hash、timing 與 action metadata；不保存完整封包或 cipher material。這些歷史 capture 沒有 capture-time executable SHA-256 attestation，因此 build association 明確維持 `Derived`，不冒充 verified capture-time identity。
- 目前官方 Client executable SHA-256 `6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B` 與 read-only runtime snapshot SHA-256 `E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A` 已由固定常數與實際檔案重新驗證；legacy manifest 的可變 `CaptureAttested` 欄位不能自行把證據升級，設為 true 或改寫 association status 都會 fail-closed。C2S `0x35` builder 固定於 RVA `0x0014E7D0–0x0014EB3C`：application payload 固定 16 bytes，byte 0 為 position，byte 1 含 action／continuation，bytes 4–9 為三組 UInt16 target masks，每組 14 個位置，bytes 10–15 暫只作 evidence-preserved context／parameter。
- World receive path 的 `0x82`／`0xE5` 與獨立 Battle receive path 的 `0x83/0x85/0x86/0x88/0x89` 已分開追蹤；Battle path 在 RVA `0x00146E40` 解碼，透過 RVA `0x001475A8/0x001474AC` 的 compressed-index tables dispatch。這些 target 目前只證明 handler identity，尚未證明 record layout／Client state mutation。
- `OfficialBattleCommandWireCodec` 只在 pinned Client build ID、`Battle` state、length／opcode／checksum／position／target-mask 全部通過時輸出 layout candidate；`DecodeForRuntime` 仍固定回傳 `SemanticEvidenceBlocked`，不允許 gameplay mutation，也沒有新增 serializer 或 network sender。靜態工具另行驗證 executable／runtime hashes，兩者不可互相替代。
- DPAPI secret 經既有安全流程解析後執行實體 MariaDB read-only gate，且 `config/database.json` 不再保存密碼；Production loader 與共用 Automation secret resolver 均只接受安全來源，遇到 `ConfigValue` 會清空或拒絕並 fail-closed。Gate 實際使用 `MariaDbWorldContentRepository` 與 `MariaDbSkillDefinitionRepository`；Monster 必須以同一 TemplateId／MapId／X／Y spawn identity 通過 semantic、formal repository 與 Runtime，Skill 必須以同一 SkillId 通過各 production edge，禁止用互不相關的非零筆數湊成 PASS。最新 Phase 3 RunId 為 `598a069d-415c-439a-9a44-a23ca4369531`：Monster 208、semantic profiles 208、enabled runtime spawns 0、encounter-eligible monsters 0；Skill 1,108、semantic profiles 1,108、repository definitions 1,108、structurally validated 1,108、execution-eligible skills 0。
- 現有 `MonsterSpawnDefinition`／combat runtime 尚未承載完整 MP、magic stats、AI、formation 與 reward 語意；`FullMonsterEncounterSemanticsEnabled=false` 使此缺口在 Production Gate 中明確 fail-closed。
- `LegacyPrimary` 仍為正式 engine；`ActorPrimary` 沒有啟用。既有 backend combat／turn／persistence runtime 未被 capture 或 TestOnly content 接入 Production。

### Current blockers

- 第一個 broken node：`MariaDB evidence-complete Monster encounter slice`。目前沒有同時具備正式 spawn、HP／MP policy、combat stats、AI／formation、reward／drop policy 的 Production monster。
- 即使之後出現 spawn row，也必須由同一 Monster ID 通過 semantic profile、formal repository、`MapRuntime` entity 與完整 encounter semantic mapping；任何一段只具備獨立非零 count 都不能 Promotion。
- Skill execution slice 同樣為 0；沒有可同時證明 identity、family、target、MP 與 effects/status 的 Production skill。
- C2S `0x35` 的 action code／action parameter 尚無唯一 consumer semantic proof；existing capture correlation 不能單獨授權 runtime mutation。
- S2C `0x82/0x83/0x85/0x86/0x88/0x89` 的 bootstrap、effect、HP／MP、death、turn advance、settlement 與 world-resume record layouts 尚未恢復到可寫 production serializer 的程度。
- 因此 DB-backed encounter、普通攻擊、正式 Skill 與 automated official Client M5 acceptance 均未執行；不得標記 `COMPLETE`。

Current M5 evidence gates：existing capture transport PASS（capture-time build identity：DERIVED）；C2S layout／pinned static dispatch PASS；MariaDB battle slice BLOCKED；full Monster runtime semantics BLOCKED；runtime mutation BLOCKED；official Client acceptance NOT RUN。Manual operation：NO；new capture：NO；network emission：0；Fake Network Bytes：0。

證據：

- [M5 Battle／Skill checkpoint](../Reports/RoadMap.M5.BattleAndSkills.md)
- [M5 existing battle evidence](../Artifacts/RoadMap/M5/existing-battle-evidence.json)
- [M5 official Client static recovery](../Artifacts/RoadMap/M5/official-client-static.json)
- [M5 MariaDB battle slice](../Artifacts/RoadMap/M5/mariadb-battle-slice.json)
- [M5 final summary](../Artifacts/RoadMap/M5/final-summary.json)

## M6 — 掉落、背包與持久化

狀態：`IN PROGRESS — EVIDENCE BLOCKED`

Production composition 現已在 Startup Stage 7 建立 `MariaDbGameplayInventoryRuntime`，從 `MariaDbGameplayContentCatalogRepository` 載入正式 item catalog，並以 `MariaDbGameplayInventoryRepository` 作為 inventory/currency/idempotency/audit authority。`TcpNetworkHost` 只接收 coordinator 依賴，不會自行建立 inventory authority；Production 沒有 InMemory/TestOnly fallback。

已完成的後端界線：

- MariaDB item catalog：17,407 筆可用、quarantine 0、fatal issue 0。
- Character、inventory state 與 currency 在同一 transaction 內以 locking read 驗證 expected authority；跨 coordinator lost update 只有一個 winner。
- Idempotency ledger 支援順序重播及同時重播；相同 payload 回傳 `DuplicateCompleted`，不同 payload fail-closed。
- InventoryId 在尚未建立 state row 時可由 CharacterId 穩定重建；Server restart 後從 MariaDB 恢復相同 identity/version/items/currency。
- Slot、stack、capacity、item FK、currency、idempotency 與 audit 在同一 MariaDB transaction 內 commit；錯誤 item FK fixture 證明 rollback 無 partial state 或 idempotency residue。
- MariaDB provider 的 CHAR／numeric reader 型別已採受控轉換，避免正式 replay/load 路徑發生 `InvalidCastException`。

實體 fixture 使用隔離 `fixture_m6_*` account/character，驗證 initial grant、sequential replay、concurrent replay、concurrent different-request CAS、rollback、restart reload 與 scoped cleanup；既有玩家 row 未被修改，fixture cleanup PASS。

目前不能安全完成的部分：

- M5 官方 Client Battle/Skill 核心閉環尚未完成，是 M6 的第一個 broken node。
- Evidence-complete Monster reward profile：0；Production enabled drop entry：0；`DefaultDisabledZero` violation：0。
- Production battle reward/drop policy 維持 disabled，不能用 TestOnly policy 或未知掉率發物品。
- Official Client inventory serializer 與功能專屬 acceptance 尚無充分證據，不能由 backend transaction 或 Client 未崩潰代替。
- `equipment_slots` 與 headless equipment runtime 尚未形成同一個 production inventory transaction；不得把 headless 能力標成 M6 equipment persistence 完成。
- 目前 4 個正式角色尚無 inventory state/slot/currency transaction row，因為沒有任何已驗證 official gameplay command 被允許發獎；這是 fail-closed 現況，不是遺失資料。

Current M6 gates：MariaDB inventory physical fixture PASS；Production composition PASS；CAS/exactly-once/restart PASS；M5 prerequisite BLOCKED；reward/drop content BLOCKED；official serializer BLOCKED；official Client acceptance NOT RUN。Manual operation：NO；new capture：NO；network emission：0；Fake Network Bytes：0。

證據：

- [M6 backend／fixture report](../Reports/RoadMap.M6.DropsInventoryPersistence.md)
- [M6 MariaDB persistence inventory](../Artifacts/RoadMap/M6/mariadb-inventory-persistence.json)
- [M6 MariaDB physical fixture](../Artifacts/RoadMap/M6/mariadb-inventory-physical-fixture.json)
- [M6 test results](../Artifacts/RoadMap/M6/test-results.json)
- [M6 final summary](../Artifacts/RoadMap/M6/final-summary.json)

## M4–M6 — 第一條非空殼核心循環

- M4：NPC target／ownership／map／distance／state 驗證，完成 Dialog 與 Merchant open/close。
- M5：DB-backed Monster encounter，完成普通攻擊、一個正式 Skill、HP/MP、death、turn advance 與 world resume。
- M6：Monster death → verified drop → inventory transaction → MariaDB persistence → restart／relogin recovery。
- M6 完成前，不宣稱 Server 已具備第一個非空殼可玩核心循環。

## M7 — 角色生命週期與 Gameplay Families

狀態：`IN PROGRESS`

### Current Checkpoint

- 登入後現在允許 MariaDB 帳號具有零或一個 active character；零角色不再被 character uniqueness gate 當成錯誤，也不會建立 `PendingWorld` claim。
- `OfficialServerSelectionWireCodec` 已固定目前 Client build 的 78-byte 零角色 `0x07` response：active count 為 0、兩個空 slot 的既有 opaque 區域保持不變，並重新計算正式 checksum。此能力已有 production TCP regression，不以假封包補欄位。
- Migration 044 新增 `character_creation_profiles`，以 Client build／Class／Gender／LifeSkill 唯一解析 Map/X/Y；只有 `Verified`／`Derived`、production-enabled、合法 map identity 與 bounds 全部通過才可建立角色。Migration 不 seed 任意出生點。
- Production composition 使用 marker-constrained `CreateProduction`、`MariaDbCharacterCreationAuthority` 與 MariaDB repositories；InMemory repositories 與 `CreateForTesting` 均為 internal，不會出現在 ConsoleHost。
- `MariaDbCharacterRepository` 的 create 會在 transaction 內鎖定 account、重新計算 active count、寫入並重讀後 commit；delete／rename 會先鎖定 account，再以 `SELECT ... FOR UPDATE` 驗證 active row 與 ownership，最後 conditional update。舊的「先讀 ownership、再另一次寫入」競態已移除。
- Migration 045 以 generated `ActiveName` 唯一索引允許 soft-deleted name 安全重用，並新增 `character_lifecycle_idempotency`。Create／Delete／Rename 的 exact retry 可跨 repository instance 回傳同一 persisted result；相同 key 不同 operation／payload 會 `ReplayConflict`。
- 角色名稱現在限制為目前 world projection 可完整表示的 3–11 ASCII letters／digits／underscore；未驗證 class profile、Unicode、過長名稱、無效或過長 request ID 及超出 UInt32 的新角色 identity 全部在 commit／wire 前 fail-closed。
- Generic Login／Create／Delete／Select protocol runtime 在 account/session/character mutation 前先驗證 capability 並 preflight outbound serializer；部分 request recovery 不會造成「已寫資料但無法回應 Client」。
- Current-source MariaDB physical fixture：Migration 044／045 `Current`；creation profiles 0／production profiles 0；concurrent create exactly-one winner、limit rejection、ownership、rename、delete、durable replay／conflict、duplicate-name rollback、soft-delete visibility／name reuse 與 scoped cleanup 全部 PASS，既有玩家 row 未被修改。
- 目前正式 wire 只證實零或一個 active character，backend admission 同步限制為 1；在 multi-slot record layout 被證實以前，不允許 backend 建出下一次登入無法投影的第二個角色。
- 既有 57-byte 候選經 current-build static opcode branch 重新核對後是 `0x19` Login Authentication，不是 Delete；舊的 capture transaction 標籤不得再被用作刪除協定證據。
- Character Create 已定位 Client send builder `RVA 0x0007D0D0`（opcode `0x17`、44-byte application payload）與 UI caller `RVA 0x0003D319`，以及 response／refresh observed hashes；但 Class／Gender／LifeSkill／Appearance 的完整 field semantics、create result mapping 與正式出生點尚不足以安全接入 Production。

### Current blockers

- Create：必要 request 欄位與結果碼／錯誤提示 serializer 尚未全部證實；`character_creation_profiles` 目前保持空白，所以 production create fail-closed。
- Delete：尚未找到可唯一證實的 C2S builder、payload identity 與 result／list-refresh pairing；不把 authentication 或 socket-connect frame 冒充 Delete。
- Rename：尚未找到正式 Client command／result family；目前只有 MariaDB／Runtime backend authority。
- Dynamic list：零／一角色可安全投影；多角色 slot layout、selection identity 與 refresh semantics 未完成，因此 current limit 為 1。
- Merchant、Quest、Equipment、Combine、Pet/Egg 的既有 backend／content 能力尚未逐 family 通過 M7 要求的 official Client 成功、拒絕、重播、rollback、重登驗收。

First broken node：`Official Character Create request/result semantic closure`。  
First broken edge：`Client 0x17 create request → canonical fields → MariaDB create transaction → official result/list refresh`。

證據：

- [M7 character lifecycle checkpoint](../Reports/RoadMap.M7.CharacterLifecycle.md)
- [M7 character lifecycle status](../Artifacts/RoadMap/M7/character-lifecycle-status.json)
- [M7 MariaDB lifecycle fixture](../Artifacts/RoadMap/M7/mariadb-character-lifecycle-fixture.json)
- [M7 test results](../Artifacts/RoadMap/M7/test-results.json)

### Current M7 Gate

- Debug／Release build：PASS，0 warnings／0 errors。
- Full tests：2,917／2,917 PASS。
- Format：PASS。
- MariaDB physical fixture：PASS；Migration 001–045 checksum current；fixture cleanup PASS。
- NuGet：25 projects，0 vulnerable package entries。
- Credential／sensitive build-output matches：0／0。
- Project-owned residual dotnet／testhost／vstest／server：0／0／0／0；另有 1 個 VS Code C# Dev Kit 的外部 `dotnet` process，未終止使用者 IDE。
- Manual operation：NO；New capture：NO；Fake Network Bytes：0。
- Official Client M7 acceptance：NOT RUN — Create／Delete／Rename wire evidence blocked。

## M8 — 全世界內容擴張

狀態：`IN PROGRESS — EVIDENCE BLOCKED`

### Current Checkpoint

- Migration 046 建立單一 Active Content Release authority；Migration 047–048 再將全部 19 張正式 Gameplay 表的內容指紋、版本與筆數釘選至該 release，資料變更不再能在同一 release 下靜默混用。
- Production startup 已接入 `MariaDbPromotedGameplayContentRuntime`。它在 Runtime Cache stage 從 Active release 載入 specialized catalogs，偵測 formal catalog 漂移、stale promotion、broken identity、FK、equipment threshold、簡體／Unicode／placeholder／markup 問題或 DefaultDisabledZero 誤啟用時 fail-closed 並清除既有 snapshot。
- 正式 `MariaDbWorldSessionCoordinator` 的 Bind／Rebind 及 `OfficialNpcInteractionClosedLoop` 的 open 都依賴同一 Content Authority；catalog 不再只是 startup cache，未就緒時玩家不能進入世界或開啟 NPC 互動。
- 目前 MariaDB Active release：`c6406d81-907a-11f1-8d1f-309c230ef6a7`，Source Run `598a069d-415c-439a-9a44-a23ca4369531`，v2 正式 catalog manifest 共 56,913 records／19 tables。實際 Runtime 載入 Quest Profile 167、Equipment Set 48、Equipment Set Member 108、Pet Innate 144；對應 headless identity/member lookup 全部 PASS。
- Formal production loaders 現在明確優先讀取 `NameZhTw`；Map、NPC、Monster、Item、Skill、Quest、Merchant 不再以原始 `Name` 優先覆蓋繁體欄位。
- 當前 Production coverage：Map 70／Client mapped 2；NPC template 319／具 enabled spawn 的 distinct template 1（2 spawn rows）；Monster template 208／spawn 0；Item 17,407；Skill 1,108／execution profile 0；Quest 418／promoted profile 167；Merchant 16／resolved inventory 0。
- Unknown probability families 仍維持停用：Drop 0、Combine 0、Pet/Egg Hatch 0；沒有將 `DefaultDisabledZero` 誤標成官方零值。
- Production simplified display scan：0；MariaDB formal/specialized Runtime load、referential integrity、Server Ready 與 headless lookup：PASS。

### Current blockers

- Map、NPC spawn、Monster spawn／execution、Skill execution、Quest objective、Merchant inventory、Equipment bonus、Drop、Combine、Pet/Egg 尚未達全量 Production promotion。
- 第一個跨里程碑阻塞仍是 M2–M7 的 official Client family evidence；M8 不會用 Runtime row、headless lookup 或 Client 未崩潰冒充 official Client gameplay acceptance。
- M8 尚未完成逐 map／family 的 official Client 可用性，因此不得標記 `COMPLETE`；M9 只能執行不會掩蓋上游缺口的 physical／operational gates，不能宣告 closure。

### Current M8 Gate

- Debug／Release build：PASS／PASS，0 warnings／0 errors。
- Full tests：2,929／2,929 PASS；movement concurrency regression 連續 6／6 PASS。
- Format：PASS；Migration 001–048 Current。
- MariaDB content expansion：PASS WITH DOCUMENTED EVIDENCE GAPS；MariaDB world/NPC interaction：PASS；Production Server Ready／Shutdown：PASS。
- NuGet：25 projects／0 vulnerable package entries；credential／sensitive build-output matches：0／0。
- Content Recovery network-emission API matches：0；Fake Network Bytes：0。
- Project-owned residual dotnet／testhost／vstest：0／0／0；Manual operation：NO；New capture：NO。

證據：

- [M8 content expansion validation](../Artifacts/RoadMap/M8/content-expansion-validation.json)
- [M8 test results](../Artifacts/RoadMap/M8/test-results.json)
- [M8 final summary](../Artifacts/RoadMap/M8/final-summary.json)
- [M8 report](../Reports/RoadMap.M8.GameplayContentExpansion.md)

## M9 — Production Closure

狀態：`IN PROGRESS — BLOCKED`

- Current-source MariaDB physical fixture：34／34 PASS；MariaDB 10,000、M6 inventory、M7 lifecycle、M8 content authority 均已重跑。
- 全資料 backup／isolated restore：114 tables、7,695,040 exact rows、6,570,951,559 bytes，來源穩定、restore 全等、cleanup PASS。
- Current-source official Client：login、character select、world screen、endpoint 與 clean shutdown PASS；WorldReady metadata 及 M2–M8 gameplay family matrix 仍 blocked。
- Current build-bound gate：Debug／Release PASS，0 warnings／errors；Full 2,931／2,931、Protocol 127／127、Runtime 1,985／1,985、Headless 438／438；Format PASS。
- Smoke-soak：120.048 秒、7,601 loops PASS；正式 7,200 秒 long soak 尚未執行。
- Credential／sensitive build output matches：0／0；NuGet vulnerable entries：0；project-owned residual processes：0；Fake Network Bytes：0。
- 第一個阻塞：`Client world screen → evidence-qualified WorldReady → portal and gameplay-family matrix`。
- [M9 report](../Reports/RoadMap.M9.ProductionClosure.md)
- [M9 final summary](../Artifacts/RoadMap/M9/final-summary.json)

## M9A — Production Runtime World Activation

Status: `PARTIAL`

### Activated authority path

`MariaDB active promoted release → immutable content snapshot → bounded ProductionMapRuntimeRegistry → MapRuntime → player/NPC runtime entities → semantic spawn/replication queue → evidence-gated serializer → ordered sender → TcpNetworkHost`

- Active release: `c6406d81-907a-11f1-8d1f-309c230ef6a7`.
- Formal catalog: 56,913 records; catalog maps 70; client-mapped MapRuntime instances created 2.
- Player runtime entity creation/cleanup: `PASS`.
- NPC runtime entities: 2; runtime target resolution: `PASS`.
- NPC evidence-ready probe frames: 2; queue drained; failed-serializer network bytes 0.
- Initial raw lifecycle queue is consumed before registry publication; pending entries 0.
- Production sessions sharing one CharacterId are conditionally owned; concurrent duplicate binding has exactly one winner.
- Runtime snapshot collections are frozen before registry publication; NPC/Monster/Portal identities are globally unique or startup fails closed.

### Evidence boundaries

- Production-enabled Monster spawns: 0; Monster runtime entities: 0.
- Enabled Portal definitions: 0; Portal runtime entities: 0.
- Official Monster spawn/update/despawn opcode and required record layout remain unproven. No bytes are guessed and no blocked serializer emits partial output.
- Current-source Official Client matrix: `BLOCKED`. Two valid retries reached Server Ready, but the Client did not establish the attested local TCP endpoint; the Server received no login request. Classification: automation/environment endpoint routing, not reproduced Server source failure.
- Formal 7,200-second soak: `NOT RUN`; the required Official Client matrix prerequisite did not pass.

### Current-source gate

- Build identity: `affr-c21a81e6caee212bfc03a863`.
- Source manifest: `97D9DB1C39789AFDB1032F225574A7D43DA8DE492A66CB320A79200A483DC3EA`.
- Debug/Release: `PASS/PASS`, warnings 0, errors 0.
- Full Release tests: `2,939/2,939 PASS`.
- Current-source MariaDB physical fixture: `35/35 PASS`.
- MariaDB 10,000: `PASS` — exactly-once 9,000; rollback 1,000; recovery 1,000; drift/leaks/duplicate side effects 0.
- Encrypted backup/isolated restore: `PASS` — 114 tables, 7,695,046 rows; definitions/counts/checksums match; cleanup PASS.
- Format PASS; NuGet vulnerabilities 0; credential matches 0; sensitive output matches 0; Fake Network Bytes 0; residual project processes 0.
- Manual operation: `NO`; new capture: `NO`.

M9A remains `PARTIAL`, not `PASS WITH DOCUMENTED GAPS`, because the requested production Monster and Portal runtime slices and the current-source Official Client matrix are not complete.

Evidence:

- [M9A final report](../Reports/RoadMap.M9A.ProductionRuntimeWorld.Final.md)
- [M9A final summary](../Artifacts/RoadMap/M9A/final-summary.json)
- [M9A test results](../Artifacts/RoadMap/M9A/test-results.json)
- [M9A Official Client matrix](../Artifacts/RoadMap/M9A/official-client-matrix.json)
- [M9A 7,200-second soak status](../Artifacts/RoadMap/M9A/soak-results.json)
- [M9A backup/restore](../Artifacts/RoadMap/M9A/backup-restore.json)

## M1 Gate（M1 closure historical）

- Debug build：PASS，0 warnings／0 errors。
- Release build：PASS，0 warnings／0 errors。
- Full tests：2,776／2,776 PASS。
- Format：PASS。
- NuGet：25 projects，0 vulnerable package entries。
- Automation environment／MariaDB authentication／server-ready：PASS。
- Credential scan：4,197 files，3 resolved secret values，0 matches。
- Sensitive build output：3,496 files，9 encoded patterns，0 matches。
- Official Client：login／character select／world screen PASS；local endpoint attested；manual operation NO；new capture NO；Fake Network Bytes 0。

## 本輪 M2 Gate

- Debug build：PASS，0 warnings／0 errors。
- Release build：PASS，0 warnings／0 errors。
- Full tests：2,788／2,788 PASS。
- M2 evidence analyzer：9／9 PASS。
- M2 migration tests：2／2 PASS。
- Persistence catalog/repository targeted tests：4／4 PASS。
- NPC Runtime targeted tests：8／8 PASS。
- MariaDB Migration 001–040：current；Server Ready PASS。
- M2 MariaDB → Repository → MapRuntime NPC semantic validation：PASS。
- Format：PASS。
- NuGet：25 projects，0 vulnerable package entries。
- Credential scan：4,213 files，3 resolved secret values，0 matches。
- Sensitive build outputs：3,796 files，9 encoded patterns，0 matches。
- M2 recovery network emission：0；Fake Network Bytes：0。
- Residual dotnet／testhost／vstest：0／0／0。
- Manual operation：NO；new capture：NO。

## 本輪 M3 Checkpoint Gate

- M3 狀態：`IN PROGRESS`；NPC initial-spawn production slice PASS，整體 M3 不宣告 COMPLETE。
- Debug build：PASS，0 warnings／0 errors。
- Release build：PASS，0 warnings／0 errors。
- Full tests：2,801／2,801 PASS。
- M3 targeted tests：12／12 PASS（NPC runtime 8、production TCP integration 1、static analyzer 3）。
- MariaDB Migration 001–041：Current；Server Ready PASS。
- MariaDB → Repository → MapRuntime → queue → serializer validation：PASS（2 sent／0 blocked）。
- 0x72 application hash：2／2 match；official checksum：2／2 valid。
- Legacy bootstrap entity tail：84 bytes removed；remaining frame checksum valid。
- Format：PASS。
- NuGet：25 projects，0 vulnerable package entries。
- Credential scan：4,221 files，3 resolved secret values，0 matches。
- Sensitive build outputs：3,798 files，9 encoded patterns，0 matches。
- Fake Network Bytes：0。
- Residual dotnet／testhost／vstest：0／0／0。
- Manual operation：NO；new capture：NO；official Client M3 acceptance：NOT RUN。
- Git Repository Root：NONE；Commit：NOT CREATED；Push：NOT ATTEMPTED。

Machine-readable checkpoint：

- [M3 test results](../Artifacts/RoadMap/M3/test-results.json)
- [M3 final summary](../Artifacts/RoadMap/M3/final-summary.json)

## M2／M3 Code Remediation Gate（pre-M4 checkpoint）

- 修復日期：2026-08-04。以下數字保留為 M2／M3 checkpoint；current source 應使用上方 M4 Current Checkpoint 的 current-source gate，不得把本段冒充最新結果。
- Debug build：PASS，0 warnings／0 errors。
- Release build：PASS，0 warnings／0 errors。
- Full tests：2,826／2,826 PASS。
- Regression coverage：session-targeted update／despawn、mixed ready/blocked zero-partial-output、non-NPC preflight、opaque hash mismatch、Monster missing-stat quarantine、Migration 041 partial-recovery、Migration 042 default-disabled gate。
- MariaDB Migration 001–042：Current；Database Ready。
- M2 MariaDB probe：PASS；NPC spawns=2；complete Monster candidates=0。
- M3 MariaDB probe：PASS；authority=`MariaDb`；runtime NPC=2；sent=2；blocked=0；Fake Network Bytes=0。
- Format：PASS。
- NuGet：25 projects，0 vulnerable package entries。
- Credential scan：4,221 files，0 matches。
- Sensitive build output：3,800 files，0 matches。
- Content-recovery network emission：0。
- Residual dotnet／testhost／vstest：0／0／0。
- Official Client M3 lifecycle acceptance：NOT RUN；Monster replication：BLOCKED BY EVIDENCE；因此 M2／M3 狀態不升級。

## RoadMap 更新規則

- 只有實際程式、MariaDB、Runtime、官方 Client 功能證據與 artifact 都存在時才標 `COMPLETE`。
- `BLOCKED` 必須記錄 first broken node／edge 與缺失證據，不能由 backend row、測試替身或報告解除。
- 歷史結果必須標為 historical，不能冒充 current。
- 每次更新同步核對 Production composition、migration 狀態及 machine-readable artifacts。
