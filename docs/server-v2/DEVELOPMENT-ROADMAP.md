# God2 Server V2 - Development Roadmap & Dev Log

Updated: 2026-09-18
Branch: `refactor/server-v2`

## Goal
Rebuild a clean, evidence-driven server compatible with the official God2 client, then add custom content without corrupting the classic baseline.

## Product Modes
- **Classic Compatibility Mode**: preserve official-client behavior and recovered official rules/data as closely as evidence allows.
- **Custom Expansion Mode**: load custom progression, balance, content and events on top of the same protocol/runtime core.

## Non-Negotiable Rules
1. Official client behavior, captures and static reverse engineering are the protocol truth.
2. Unknown packet fields/opcodes stay `NeedsRecovery` or `Blocked`; do not fabricate wire behavior.
3. Protocol transport and gameplay rules are separate layers.
4. Runtime entity IDs and client wire entity handles are separate identities.
5. Replication events must retain immutable snapshots so despawn can serialize after authoritative removal.
6. Classic data and custom data remain separable and auditable.
7. No feature is considered complete merely because it is a NoOp or avoids disconnecting.

## Target Architecture
- `God2.ServerV2.Host`: process startup, DI/configuration, graceful shutdown.
- `God2.ServerV2.Core`: shared lifecycle/evidence/content-mode contracts.
- Planned: `Network`, `Protocol`, `Session`, `World`, `Gameplay`, `Data`, `Persistence`, `Diagnostics`.

## M1 - Core Closed Loop
Target: official client can repeatedly login, select character, enter world, move, logout, and relogin without stale connections/sessions/entities.

Work:
- TCP connection lifecycle and send queue.
- LoginHandshake/Login/CharacterSelect/WorldHandshake stages.
- Explicit `CloseAsync(reason)` lifecycle; logout is not a transport error.
- Session ownership and duplicate login handling.
- Evidence-status-aware protocol registry.
- Player spawn/movement/despawn closed loop.
- Immutable replication event snapshots.
- Official-client acceptance and repeated reconnect tests.

Exit criteria:
- 50 repeated login/world/logout/relogin cycles without ghost sessions or entities.
- Two-client test proves spawn, movement, AOI leave and logout despawn.

## M2 - World Systems
Target: stable multiplayer world.

Work:
- Maps and map instances.
- Entity registry: Player/NPC/Monster/Portal/Drop/Pet/InteractiveObject.
- AOI visibility ownership.
- Spawn/Update/Despawn replication.
- NPC runtime and interaction entry point.
- Portal/map transition lifecycle.
- Character position persistence.

Exit criteria:
- Multiple clients see each other consistently across movement, map changes, disconnects and relogs.

## M3 - Playable RPG Systems
Target: core game loop is playable.

Order:
1. Character runtime and stat aggregation.
2. Inventory and item instances.
3. Equipment and restrictions.
4. Monster spawn/AI.
5. Combat resolution.
6. Skills, costs and cooldowns.
7. Death/revival.
8. EXP/level progression.
9. Drops and pickup.
10. Shops.
11. Quests and rewards.

Exit criteria:
- Player can obtain/equip/use items, fight monsters, gain rewards, complete quests and persist progress through restart.

## M4 - Complete Systems
Target: recover and implement remaining official systems, then enable custom expansion.

Systems:
- Pets/Battle Pets.
- Immortal/companion systems where protocol evidence exists.
- Crafting.
- Warehouse.
- Party and chat.
- Player trading.
- Guild and mail where supported.
- PvP.
- Instances/dungeons.
- Events and endgame systems.

## Data Model Strategy
Separate persistent state from runtime state.

Persistent examples:
- level/EXP/currency
- inventory/equipment
- skills
- quest progress
- pets
- saved map/position

Runtime examples:
- current target
- movement/cast state
- combat state
- AOI visibility
- temporary buffs/cooldowns

Important economic operations use DB transactions.

## Official vs Custom Content
Recommended logical split:

```text
data/
  official/
    items/
    monsters/
    skills/
    quests/
  custom/
    items/
    monsters/
    skills/
    quests/
```

Classic mode loads only official content. Custom mode layers custom definitions/rules on top.

## Future Custom Progression - Bounded Infinite Growth
This is intentionally postponed until the classic baseline is stable.

Design goal: permit effectively unlimited long-term growth without allowing raw damage/defense numbers to trivialize all content immediately.

Planned concepts to evaluate later:
- tier/realm-based progression gates instead of only linear level inflation;
- diminishing returns on raw stats;
- content scaling bands;
- soft caps with conversion to secondary progression resources;
- separate offensive, defensive and utility progression budgets;
- boss/instance minimum mechanics that cannot be bypassed by raw damage alone;
- seasonal/prestige/reincarnation layers that preserve permanent progression without uncontrolled numeric overflow;
- integer/packet field limits audited before final formulas are selected.

The protocol core must not contain these balance rules; they belong in configurable gameplay progression modules.

## Testing Standard
Every recovered/implemented feature progresses through:
1. codec/unit tests;
2. runtime/domain tests;
3. TCP integration tests;
4. official-client acceptance tests;
5. regression tests after future systems are added.

## Development Log
### 2026-09-14 - V2 Start
- Decision made to stop treating the old ClassicServer runtime as the future production architecture.
- Existing server retained as protocol research/reference and compatibility oracle.
- Created branch `refactor/server-v2`.
- Created `src/God2.ServerV2.Core`.
- Added `ServerContentMode`: `ClassicCompatibility` and `CustomExpansion`.
- Added connection stage and protocol evidence status foundations.
- Created `src/God2.ServerV2.Host` bootstrap project.
- M1 target fixed as Login -> Character -> World -> Movement -> Logout -> Relogin.

### 2026-09-14 19:12 - Progress Sync
- Checked `refactor/server-v2`; head remains `c95ed096996362fbb749f3eedb98a5e239aabdb5` (`Add Server V2 development roadmap`).
- No new substantive commits were present since the previous sync.
- No GitHub commit status / CI test results were available for the current head, so no test pass was claimed.
- M1 remains at 10%; no milestone percentage was increased.
- Current blockers remain Network/Protocol/Session implementation for the first V2 closed loop and evidence for the official Player Despawn wire/entity-handle mapping.
- Next target remains Network/Protocol/Session foundations, migration of verified LoginHandshake/Login behavior, then a repeatable Login -> World -> Logout closed loop.

### 2026-09-15 19:19 - Progress Sync
- `refactor/server-v2` is now at `a87b7266ae9d663ce725bc89a1cd4218283fc0cb` (`Test Server V2 advertised address options`), 131 commits ahead of the 2026-09-14 sync head `c95ed096`.
- Added V2 Application, Network, Protocol, Session and Persistence layers plus their test projects.
- Implemented TCP framing/lifecycle, Session registry, LoginHandshake/Login request-response codecs, MariaDB PBKDF2 account authentication, character-list persistence, Server Selection, WorldHandshake/Bootstrap, Heartbeat, Movement and Logout protocol foundations.
- Added pending world-entry, world-activity and movement-sequence tracking; latest work separates/validates bind versus advertised IPv4 address configuration.
- Historical development records reported 67/67 tests passing at an earlier checkpoint, but current head had no GitHub commit status/CI result at this sync point.
- M1 was conservatively tracked at 60% pending current-head validation and official-client acceptance.
- Blockers at this sync point: official-client end-to-end acceptance incomplete and official Player Despawn wire/entity-handle evidence unresolved.

### 2026-09-15 21:25 - AWS Runtime Validation and M1 Headless Closure
- Deployed Server V2 as the enabled `god2-server-v2.service` systemd unit on AWS and verified graceful restart.
- Configured the public game endpoint on TCP `2592` with MariaDB runtime access through `127.0.0.1:3308`.
- Validated least-privilege `god2_v2@172.17.0.1` access and confirmed canonical migration `055` through current schema version `468`.
- Verified real MariaDB authentication for `god2test`, character list recovery for `test001`, World Handshake and the complete 1772-byte World Bootstrap.
- Ran all Server V2 automated projects: 114 tests passed, 0 failed and 0 skipped.
- Verified headless World Logout closes the TCP connection, releases session ownership and permits immediate same-account relogin.
- Verified ordered movement acknowledgement and persistence; test character coordinates changed from `(202,128)` to `(16,14)` and runtime version advanced from `29` to `32`.
- Verified duplicate movement sequence rejection without a second acknowledgement or duplicate persistence.
- Verified World idle timeout closes the connection after 30.0 seconds.
- Completed 50 consecutive Login -> World -> Logout cycles with no failures or stale sessions.
- Completed 50 abrupt World disconnect -> immediate relogin cycles with no failures or stale sessions.
- Fixed World session ownership so an account remains exclusively owned while its character is in World.
- Added explicit LoginProbe validation for the official DuplicateLogin failure frame `05000A79C2`.
- Official-client acceptance and multiplayer spawn/AOI/despawn evidence remain pending.

### 2026-09-16 - Official Client Baseline and V2 Launcher Decision
- Completed the current Server V2 session-ownership and Login-to-World ownership-transfer work, including duplicate-account ownership protection and pending-world claim coverage.
- Current focused Server V2 test projects pass 59/59: Session 20/20, Application 21/21 and Network 18/18.
- Verified the official Login 19-byte server handshake and 19-byte client handshake against the legacy protocol evidence.
- Re-tested the 6-byte Login version follow-up and the recovered 900 ms handshake timing. The current client still rejected the tested version follow-up before sending LoginRequest, so official-client login acceptance remains unresolved.
- Identified conflicting historical Login version evidence (`0x0022` versus current legacy source `0x0025`); stale tests must not be treated as protocol authority without matching the exact client build.
- Established a single immutable Server V2 reference client: original `God2.exe`, SHA256 `fb72296cab5950b74d8f7c98155145f7c5f58749c3011adb0540325b60749dd1`.
- Verified that the newly recovered FS2DB client copy and the AWS reference client contain the exact same `God2.exe`.
- Removed `God2_opt.exe` and its separately supplied launcher configuration from the Server V2 protocol baseline; it represents a different client/launcher target and must not be mixed with the reference client.
- Architecture decision: do not rebuild or replace the original God2 game client. Preserve the verified original client as the compatibility target.
- Added the God2 V2 Launcher as a planned component. It will own Server V2 endpoint selection/injection, reference-client integrity/version identification, launch orchestration and later update/server-selection functionality.
- Confirmed that the existing reverse-engineering toolchain already contains a verified `.csvZ` reader using the God2 `05 16` LZSS wrapper and CP936 text decoding (`God2PackedFile.ReadCsvZAsync`).
- Next protocol/client task: decode and characterize the reference client's `LoginServer.csvZ`, determine the exact login-endpoint selection path used by `God2.exe`, and make the future V2 Launcher direct the unmodified reference client to Server V2.
- After endpoint control is established, rebuild the official-client Login baseline from this exact reference client before continuing Character Select and World acceptance testing.

### 2026-09-16 19:48 - Progress Sync
- Compared against the prior sync head `a87b7266`; `refactor/server-v2` advanced 7 commits to `dbd3429453d7d054a184d1ed37aed866602f3cac` (`Align Server V2 progress with current development`).
- Server V2 headless M1 infrastructure now includes AWS Runtime validation, Login-to-World ownership transfer, duplicate-account protection, World Bootstrap, movement sequencing/persistence, Logout, abrupt-disconnect cleanup and immediate relogin.
- Recorded acceptance evidence includes 50 consecutive Login -> World -> Logout cycles and 50 abrupt World disconnect -> immediate relogin cycles without stale sessions.
- Development log records 114/114 Server V2 automated tests passing at the AWS runtime checkpoint; the most recent focused suites report Session 20/20, Application 21/21 and Network 18/18 (59/59), with Host Release build passing.
- Current head has no GitHub commit status/CI result, so these executed test records are retained without claiming CI verification for `dbd3429`.
- Official compatibility baseline is now the immutable original `God2.exe`; `God2_opt.exe` is excluded from the V2 compatibility target.
- V2 Launcher direction is established: preserve the original game client and build a separate launcher for Server V2 endpoint routing, integrity/version identification and later server-selection/update functions.
- Current protocol blocker is `LoginServer.csvZ` endpoint selection plus the reference-client Login version follow-up; official Client Login -> Character -> World acceptance is still incomplete.
- Multiplayer Player Despawn remains evidence-blocked pending official wire/entity-handle mapping and two-client AOI/despawn acceptance.
- M1 is conservatively tracked at 70%: headless lifecycle/reconnect criteria are substantially proven, but official-client and multiplayer exit criteria remain open.
- Next: decode `LoginServer.csvZ`, establish V2 Launcher endpoint control, rebuild Login acceptance against the single reference client, then complete official Client Character -> World -> Movement -> Logout -> Relogin and two-client despawn acceptance.

### 2026-09-17 19:19 - Progress Sync
- Since the 2026-09-16 19:48 sync, `refactor/server-v2` advanced to `e52d74dcd5283098f8d51b174e9da4634ede839b` (`Sync God2 Rework progress for 2026-09-17`); relative to the previous roadmap sync base, 12 commits are present.
- Added evidence-gated NPC spawn support, MariaDB NPC snapshot/evidence migrations, NPC interaction session ownership, World presence/replication outbox foundations, and pinned live-dialog NPC spawn evidence.
- God2 Rework Launcher now has a WPF project, immutable `God2.exe` SHA256 verification, embedded Rework `LoginServer.csvZ`, Server V2 endpoint display/deployment, launch orchestration, Release build and win-x64 self-contained publish.
- Windows acceptance established that Launcher can create the original `God2.exe` process, but the client exits normally after about 0.131 seconds with Exit Code 0 and no Application crash event. Original `God2Patch.exe` reaches Now Loading, then reports maintenance/network failure.
- Investigation has therefore moved to `God2Con.csvZ` / `God2Con2.csvZ`, Patch Server/version checks and any prerequisite state/arguments/files the original Patch supplies before launching `God2.exe`.
- Latest recorded focused Server V2 verification remains Session 20/20, Application 21/21 and Network 18/18 (59/59), Host Release build passing; Launcher Release build and win-x64 self-contained publish also completed. Current head has no GitHub CI/status, so no CI pass is claimed.
- Project `progress.json` currently tracks overall/M1 work at 65%; this sync preserves that project-authored percentage rather than inventing a higher completion value.
- Remaining blockers: original `God2.exe` launch prerequisite, legacy Patch/update flow, official reference-client Login/Character/World acceptance, and multiplayer Player Spawn/AOI/Despawn evidence/acceptance.
- Next: decode `God2Con.csvZ` and `God2Con2.csvZ`, reproduce only the necessary Patch prerequisite in Rework Launcher, retest Launcher -> God2.exe -> Server V2 Login, then complete Character -> World -> Movement -> Logout -> Relogin and two-client despawn acceptance.

### 2026-09-17 22:30 - NPC 3793 Dialog Closed Loop
- Added evidence-gated Server V2 dialog response support for the verified live NPC handle `3793`.
- Replayed the exact 32-byte S2C `0x7A` dialog response pinned to the current reference-client build.
- Added verified standalone 10-byte `0x85` and compound 15-byte `0x86 + 0x85` dialog-selection decoding.
- Restricted selection acceptance to handle `3793`, selector `7`, state `0`, and the observed opaque client values `0x11` / `0x12`.
- Integrated NPC interaction ownership so a valid selection releases the active NPC session and emits no unsupported response.
- Extended LoginProbe to verify Spawn -> Open -> Dialog Response -> Selection -> Session Release -> Reopen -> Logout.
- Completed the isolated AWS TCP probe with Exit Code `0`; the test character was restored to map `1675308248`, position `(16,15)`.
- Preserved existing NPC `5042` open/close behavior and verified `5042 -> 5096` single-session ownership rejection.
- Current focused validation totals 177/177 passing tests: Core 7, Session 20, Protocol 73, Application 24, Network 45 and Persistence Integration 8.
- Deployed commits `1d4b11f` and `f1ed78b` to the enabled AWS `god2-server-v2.service`; TCP `6001` is healthy.
- Official-client acceptance remains pending until testing resumes on a Windows machine where the original client is not blocked by endpoint protection.

### 2026-09-18 19:41 - Progress Sync
- Since the 2026-09-17 sync commit `2dadf010`, `refactor/server-v2` advanced 6 commits to `ffcf6b4d412f8c387660978c5bcd2e2580d32a66` (`Refresh public God2 V2 development progress`).
- Completed the evidence-gated NPC 3793 dialog closed loop: exact 32-byte S2C `0x7A`, verified `0x85` and compound `0x86 + 0x85` selection decoding, interaction ownership release/reopen and LoginProbe coverage.
- Latest recorded verification is 177/177 passing: Core 7, Session 20, Protocol 73, Application 24, Network 45 and Persistence Integration 8; Host Release build and isolated NPC 3793 AWS TCP Probe also passed.
- Current head has no GitHub commit status/CI result, so the recorded local/AWS verification is retained without claiming CI validation for `ffcf6b4d`.
- God2Con.csvZ / God2Con2.csvZ are decoded and the self-hosted Patch FTP can serve `god_crc_p.txtZ` and `god_del.txt`; a 0-file manifest still triggers Large-Over 1054.
- Large-Over has been narrowed: FTP STOR failure is not the cause, and the Patch diagnostic return path includes `.txtZ`, `.cgodZ` and `.cdirZ` data that must be preserved and decoded next.
- Project-authored `progress.json` now tracks overall progress at 69%; this sync uses that value rather than inventing a higher percentage.
- Blockers: Large-Over 1054 decision rule, the original God2.exe Patch-created prerequisite state, official reference-client Login/Character/World/NPC acceptance, and multiplayer Player Spawn/AOI/Despawn evidence/acceptance.
- Next: decode the Large-Over diagnostic artifacts and local file-scan comparison rules, reproduce only the required Patch prerequisite in Rework Launcher, then complete Launcher -> God2.exe -> Server V2 Login/World/NPC acceptance and two-client despawn acceptance.

## Current Known Assets to Reuse
- Official client and original client assets/UI/maps/animations.
- Existing MariaDB game data and schema/migrations where valid.
- Verified login/character/world handshake behavior.
- Verified movement and portal closed loops from the existing server.
- Existing NPC and replication evidence.
- Static/runtime reverse-engineering tooling.
- Current runtime-memory snapshot for the active official client build.
- Existing tests and packet fixtures as compatibility references.

## Definition of Done
Server V2 is not complete until the official client can reliably perform the major original gameplay systems, persistence survives restart, multiplayer state remains consistent, disconnects leave no ghosts, and classic regression tests remain green when custom content is enabled.
