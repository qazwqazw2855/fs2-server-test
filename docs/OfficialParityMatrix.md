# God2 Server V2 — Official Parity Matrix

> 目標：God2 Server V2 的第一個完成基線為「台服正服可提供的資料與遊戲系統完整度」，完成基線後才進入 V2 Custom Expansion。
>
> 證據原則：台服正服證據優先；台服缺失時可使用陸服正服作 supplemental evidence，但必須保留 provenance，不得靜默升格為台服事實。
>
> Client static、runtime capture、legacy server、third-party、derived evidence 必須分開記錄。Formal DB 中存在資料不等於已證明為台服正服資料。
>
> 沒有正服證據，不猜協定；沒有建立 baseline，不直接修改 V2。

## 1. 完成定義

每個系統分四層驗收：

1. **Data** — 舊服資料已盤點、匯入、可做 parity diff。
2. **Logic** — V2 Server 已具備對應的 authoritative gameplay/runtime 邏輯。
3. **Wire** — 原版 Client 所需 Server→Client / Client→Server protocol 已有可靠證據與 codec。
4. **Real Client** — 原版 Client 實機流程已驗證。

只有資料存在不代表系統完成；只有 Wire 可通也不代表正服功能完整。

## 2. 舊服 → V2 系統對應

| 正服系統 | 舊服/來源資料 | V2 目標 | 現況 |
| --- | --- | --- | --- |
| Map | exact-current gamedata + navigation resources；legacy runtime supplemental | maps + world runtime | 🟡 Data PARTIAL：官方 catalog 144/144、Formal 無缺漏；resource provenance 待重建 |
| Map hierarchy | region/area/town/city/dungeon/map | Map metadata / hierarchy | 🟡 |
| Collision | map movement/collision data | movement bounds + collision | 🟡 |
| Portal | evidence-backed migrations + runtime acceptance；legacy CAN-link supplemental | portals + WorldMapTransition | 🟡 Data PARTIAL：5 條 evidence-backed、Formal 現存 3 條；Portal 1/2 reconciliation regression |
| NPC | NPC templates | npcs + NPC runtime | 🟡 |
| NPC Spawn | NPC spawn/runtime data | npc_spawns + WorldNpcRegistry | 🟡 |
| NPC Dialog | dialog/script data | dialog engine | 🟡 |
| NPC Shop | merchant/shop data | merchant/shop service | 🔴 |
| NPC Quest | quest/NPC bindings | quest service | 🔴 |
| Monster | monster templates | monsters + combat runtime | 🔴 |
| Monster Spawn | spawn/runtime data | monster_spawns + world runtime | 🔴 |
| Monster AI | server AI/rules | Monster AI | 🔴 |
| Drop | drop tables | drop service | 🔴 |
| Item | item data | items + item service | 🟡 |
| Inventory | character inventory | character_inventory | 🟡 |
| Equipment | equipment rules | equipment runtime | 🔴 |
| Crafting | craft/recipe data | crafting service | 🟡 |
| Skill | skill data | skills + Skill Engine | 🔴 |
| Quest | mission/quest data | quests + Quest Engine | 🔴 |
| Character | character/account data | characters + Character Runtime | 🟡 |
| Currency | player currency data | authoritative currency state | 🔴 |
| Battle | battle/combat server logic | Battle Engine | 🔴 |
| Pet | battle-pet templates/data | Pet Runtime | 🟡 |
| Immortal | immortal/god data | Immortal Runtime | 🔴 |
| Party | party/team rules | Party Runtime | 🔴 |
| Guild | guild rules/data | Guild Runtime | 🔴 |
| Chat | chat protocol/runtime | Chat Runtime | 🔴 |
| Trade | trade rules | Trade Runtime | 🔴 |
| Mail | mail data/runtime | Mail Runtime | 🔴 |
| Friend/Social | social data/runtime | Social Runtime | 🔴 |
| Event | event rules/data | Event Runtime | 🔴 |
| Status/Buff | status/effect rules | Status Engine | 🔴 |
| Life Skill | life-skill data/rules | LifeSkill Runtime | 🔴 |
| Player replication | world actor state | AOI/Spawn/Despawn/Replication | 🔴 |
| NPC replication | NPC actor state | official NPC Spawn/Update | 🟡 |
| Monster replication | monster actor state | official Monster Spawn/Update | 🔴 |
| Logout | logout protocol | session/world cleanup | 🟢 已驗證 |

## 3. 目前已恢復資料基線

以下數字來自目前 repository 的 Official Client Recovery / runtime inventory；它們是現階段可核對的資料基線，不等同於「舊台服完整資料已全部 parity」。

| 類別 | 目前可核對筆數 | 狀態 |
| --- | ---: | --- |
| official map catalog | 144 | Exact-current static；Migration 114 與 Formal DB 零缺漏 |
| formal current-build maps | 145 | 144 official catalog + Map 557790525 pre-existing verified runtime slice；144 enabled 且具 dimensions/bounds |
| client map resource staging | 158 | Migration 476 restored pinned staging provenance；全部 disabled，exact-client file hash/navigation provenance 尚未完成 |
| client map resource identities | 144 | Migration 114 identity rows restored by Migration 476；143 Derived coordinates、1 EvidenceBlocked |
| legacy map resource inventory | 69 | Supplemental only；ID namespace 不得直接與 Formal map_id join |
| evidence-backed portal routes | 5 | Portal 1–4 來自 bounded runtime evidence；170015007 已通過台服原版 Client → V2 acceptance |
| formal portal routes | 5 | Portal 1、2、3、4、170015007 全數 enabled；Migration 475 已修復 FORGE_SEED_LEDGER_DRIFT |
| portal resource link staging | 65 | Migration 476 restored 44 Derived + 21 Candidate；全部 disabled、未綁定 runtime portal、trigger Unknown |
| legacy portal CAN-link candidates | 68 | Supplemental only；TransferTriggerUnverified，不得直接匯入 gameplay |
| npcs | 319 | Recovered; spawn/dialog/merchant binding仍需驗證 |
| monsters | 208 | Recovered; combat semantics仍需驗證 |
| spawns | 69 | Supplemental map-entry/respawn candidates; no monster template or coordinates; all runtime blocked |
| items | 17,407 | Recovered; gameplay fields仍需驗證 |
| equipment rules | 1 | Equipment rules only |
| skills | 1,108 | Recovered; runtime semantics仍需驗證 |
| quests | 418 | Recovered; reward/NPC state仍需驗證 |
| merchants | 16 | Recovered; inventory仍需驗證 |
| dialogs | 18 | Recovered; rows仍需展開 |
| drop tables | 208 | One candidate identity per monster; no verified items/rates/quantities; all runtime blocked |
| localization | 34 | Table-manifest level |
| immortals | 34 | Exact-client static identity；runtime semantics evidence-gated |
| battle pets | 178 | Exact-client static identity；runtime semantics evidence-gated |

> 舊台服 Server 的完整資料數量與上述 Official Client Recovery inventory 必須分開管理。後續以舊台服正式資料庫/Server source 做一次完整 inventory，再建立 machine-checkable parity diff；不得把不同來源的筆數直接視為同一集合。

Supplemental monster/spawn/drop identity checks: `docs/parity/monster-spawn-drop-static.md` (regenerate with `python3 Automation/check-monster-static-evidence.py`). These counts do not establish gameplay semantics.

## 4. Parity 驗收方式

每個資料域都要產生：
- source inventory
- V2 inventory
- missing records
- extra records
- changed records
- unresolved references
- evidence status
- import timestamp / source hash

建議最終提供 `docs/parity/<domain>.json` 與 `docs/parity/<domain>.md`，供 CI / maintenance script 使用。

### Parity Gate

```text
Source inventory
      ↓
Normalize identifiers
      ↓
Primary-key diff
      ↓
Foreign-key/reference diff
      ↓
Gameplay-field diff
      ↓
Evidence classification
      ↓
V2 import
      ↓
Integration test
      ↓
Official Client acceptance
```

## 5. 開發順序

### M2-A — World Content Foundation
- Map 全量盤點
- Map hierarchy
- Map collision / movement
- Portal 全量
- NPC 全量
- NPC Spawn 全量
- Monster 全量
- Monster Spawn 全量

### M2-B — Gameplay Data Foundation
- Item
- Inventory
- Equipment
- Skill
- Quest
- Craft
- Merchant
- Drop

### M2-C — Gameplay Runtime
- Character
- Stats
- Battle
- Monster AI
- NPC services
- Quest Engine
- Skill Engine
- Item/Equipment mutation
- Pet / Immortal

### M2-D — Online World
- Player Spawn
- AOI
- Player Despawn
- NPC Spawn/Update
- Monster Spawn/Update
- Party
- Guild
- Chat
- Trade
- Social

### M2-E — Official Client Acceptance
每一個完成系統都要至少有：
- automated tests
- protocol/evidence record
- real-client probe
- failure/reconnect case
- persistence verification

## 6. 正服基線完成後：V2 Custom Expansion

Custom Expansion 不取代正服內容。

```text
Official Parity Baseline
        ↓
100% functional baseline
        ↓
V2+ extension layer
        ├─ 新地圖
        ├─ 新 NPC
        ├─ 新怪物
        ├─ 新道具
        ├─ 新任務
        ├─ 新活動
        ├─ 新玩法
        └─ 管理/GM/API/監控
```

所有 Custom 功能應與 Official Compatibility Layer 分離，避免新功能破壞原版 Client 的正服相容性。

## 7. 現階段結論

目前 Server V2 已經證明 Login → World → Movement → Portal → NPC interaction → Logout 的核心閉環可運作；下一階段不再以單一 NPC 或單一封包作為主要開發單位，而是以 **Map / NPC / Monster / Item / Skill / Quest / Battle / Player / Online World** 等完整系統為單位建立正服 parity。

Map Data baseline 已完成 catalog 與 staging provenance gate：Migration 114 exact-current static catalog 為 144 筆，Formal DB 零缺漏；唯一額外列 557790525 已獨立分類為 Map 19 verified runtime slice。Migration 476 已恢復 `client_map_resources` 158 筆、`client_map_resource_identities` 144 筆與 `portal_resource_links` 65 筆，但它們屬於 pinned staging provenance，全部維持 disabled。AWS 尚無完整 exact-current client navigation resources，因此 Full World Runtime promotion 仍維持 blocked。

Portal Data baseline 已通過 machine-checkable catalog gate：5 條 evidence-backed route 已全部存在於 Formal DB，missing=0、unexpected=0；Migration 475 已修復 Portal 1、2 與 `god2_research.portal_evidence` 的 `FORGE_SEED_LEDGER_DRIFT`。Migration 476 已恢復 65 條 staging `portal_resource_links`，其中 44 條 Derived、21 條 Candidate；全部 disabled、未綁定 runtime portal 且 trigger Unknown。68 條 legacy CAN-link candidates 維持 supplemental，未驗證 trigger 前不得匯入。

`EvidenceBlocked` 代表「尚未取得足夠官方 wire 證據」，不是「資料不存在」。資料可先進 staging/runtime model，但未驗證的官方 Client wire 不得用猜測實作。
