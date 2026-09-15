# God2 Server V2 - Development Roadmap & Dev Log

Updated: 2026-09-15
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
- Ran all Server V2 automated projects: 113 tests passed, 0 failed and 0 skipped.
- Verified headless World Logout closes the TCP connection, releases session ownership and permits immediate same-account relogin.
- Verified ordered movement acknowledgement and persistence; test character coordinates changed from `(202,128)` to `(16,14)` and runtime version advanced from `29` to `32`.
- Verified duplicate movement sequence rejection without a second acknowledgement or duplicate persistence.
- Verified World idle timeout closes the connection after 30.0 seconds.
- Official-client acceptance, repeated 50-cycle validation and multiplayer spawn/AOI/despawn evidence remain pending.


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
