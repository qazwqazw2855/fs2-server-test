# Test Results

Build command:

`dotnet build God2ClassicServer.sln -c Release`

Result:

- 0 errors
- 0 warnings

Test command:

`dotnet test God2ClassicServer.sln -c Release --no-build`

Result:

- 36 passed
- 0 failed
- 0 skipped

BAT smoke test:

- `Start_Server.bat` starts `God2 Classic Server.exe`.
- Missing `config/database.json` password stops startup before MariaDB connection.
- `Server Ready` is not displayed.
- Exit code is 20.

Second round Database Bootstrap verification:

- `database/schema/001_accounts.sql` through `021_runtime_state.sql` are present and copied to ConsoleHost output.
- Database bootstrap creates `god2` when absent.
- Bootstrap ensures `__SchemaVersion`.
- Migration runner skips versions already present in `__SchemaVersion`.
- Migration failure returns non-zero startup result and does not start Network Host.

Phase 2 Configuration verification:

- All `config/*.json` files include `_comment`, `_version`, `_example`, and per-field Traditional Chinese descriptions.
- All `config/*.json` files are valid UTF-8 JSON without BOM.
- Config indentation is validated as 4 spaces.
- Validation errors include file name, field name, current value, and legal value.

Live MariaDB verification:

- Ran `Start_Server.bat` with the MariaDB connection settings read directly from `config/database.json`.
- Server output showed `Database Connected`, `Migration 001 Current` through `Migration 020 Current`, `Database Ready`, and `Server Ready`.
- HeidiSQL-equivalent query as `god2_server` confirmed core tables: `__schemaversion`, `accounts`, `characters`, `maps`.
- `god2.__SchemaVersion` contains 20 migration rows, versions `001` through `020`.
- `god2.__SchemaVersion.Checksum` is populated for all 20 migrations.
- `INFORMATION_SCHEMA.TABLES` reports 27 tables in database `god2`.

Phase 4 Official Client Data Migration verification:

- Official import staging is managed by the importer, not by core startup schema migration.
- Ran `God2 Classic Server.exe --import-official-data <import-root>` with the MariaDB password read directly from `config/database.json`.
- MariaDB confirmed staging tables: `official_import_categories`, `official_import_records`, `official_import_reference_issues`.
- `official_import_categories` contains 28 categories with current `ImportBatch` and `RecoveryStatus`.
- `official_import_records` contains 19,943 recovered records across 13 non-empty categories.
- `official_import_reference_issues` contains 2 unresolved portal reference issues.
- Import run showed `Database Connected`, `Migration 020 Current`, `Official Import Count: 19943`, `Official Runtime Import Count: 19873`, `Official Reference Issues: 2`, `Official Import Batch: 20260728122724`, and `Official Import Ready`.
- Live category counts: maps 69, portals 68, npcs 319, monsters 208, spawns 69, items 17,407, equipment 1, skills 1,108, quests 418, merchants 16, drop_tables 208, dialogs 18, localization 34.
- Live MariaDB formal runtime table counts: `items` 17,407, `monsters` 208, `npcs` 319, `quests` 418, `maps` 69, `portals` 68, `merchants` 16, `skills` 1,108, `dialogs` 18, `drop_tables` 208, `localization_entries` with `Language = 'official-recovery'` 34.
- Live MariaDB formal runtime tables report all imported rows with `RecoveryStatus = 'Recovered'`.
- `official_import_*` tables remain import cache/log tables; they are not the formal runtime data source.
- Cross-reference pending categories remain represented without fake data: immortals 0, battle_pets 0, rewards 0, animations 0, models 0, textures 0, icons 0, sounds 0, music 0, effects 0, scripts 0, lua 0, containers 0, resource_tables 0, string_tables 0.
- Live MariaDB `official_import_categories` has 0 categories with `RecoveryStatus = 'Missing'` and 16 categories with `RecoveryStatus = 'NeedsRecovery'`.

Configuration & Core Policy v1 verification:

- Source `config/` contains only `server.json`, `database.json`, `network.json`, `logging.json`, `security.json`, `localization.json`, `persistence.json`, and `rates.json`.
- Removed gameplay and feature switch config files: `features.json`, `character.json`, `combat.json`, `quest.json`, `merchant.json`, `immortal.json`, `battle-pet.json`.
- `rates.json` contains only `experienceRate` and `dropRate`.
- Source `database/schema/` contains core migrations `001_accounts.sql` through `021_runtime_state.sql`.
- Live MariaDB `god2.__SchemaVersion` contains 20 core migration rows.
- Live MariaDB official staging still contains 19,943 recovered records.

Permanent Architecture Policy v1 verification:

- Added `docs/PermanentArchitecturePolicy.md`.
- All projects target `net10.0`.
- Only `God2.ClassicServer.ConsoleHost` is an executable project.
- Repository root contains only one BAT entry point: `Start_Server.bat`.
- Integration tests enforce config file whitelist, banned feature switch fields, directory policy, and no Windows code page dependency in source.
- Integration tests enforce Official Client Recovery permanent scope, required inventory fields, importer coverage, and no premature `Missing` status.

Phase 3.5 Protocol & Knowledge Migration verification:

- Protocol evidence copied into `src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion`.
- Protocol knowledge base created at `src/God2.ClassicServer.Protocol/Knowledge/protocol-knowledge-base.json`.
- Knowledge base contains 3 verified/recovered opcode entries, 3 unknown opcode entries, 25 packet families, 6 migrated packet knowledge rows, and 20 remaining recovery items.
- Protocol layer includes `ProtocolRegistry`, `OpcodeRegistry`, `PacketRegistry`, `PacketFactory`, `PacketSerializer`, and `PacketDeserializer`.
- Runtime `TcpNetworkHost` now decodes ingress frames through the Classic Server protocol layer.
- Source/evidence scan confirmed no local absolute paths in migrated protocol evidence.

Phase 5-7 Protocol/Login/Character Runtime verification:

- Added protocol frame accumulation tests for partial packet, multiple packets, invalid length, oversized packet, and sequence replay candidate.
- Added runtime tests for unknown packet capture, stage rejection, valid login, invalid password, missing account, disabled account, locked account, duplicate login, replay login, character list/create/delete/rename/select, ownership isolation, character limit, and duplicate request id rejection.
- Real workspace build command attempted: `dotnet build God2ClassicServer.sln`.
- Real workspace build result: blocked before compilation by installed SDK 8.0.422 because all projects target `net10.0` (`NETSDK1045`).
- Temporary compile check: copied workspace to `%TEMP%`, changed only the temporary copy from `net10.0` to `net8.0`, and ran `dotnet build`.
- Temporary compile result: 0 warnings, 0 errors.
- Temporary focused tests: `God2.ClassicServer.Protocol.Tests` passed 9/9; `God2.ClassicServer.Runtime.Tests` passed 8/8.
- Full temporary solution test intentionally not used as final status because architecture tests correctly require source projects to remain `net10.0`.

Phase 5-7 completion report:

- Current phase: PARTIAL - PROTOCOL RECOVERY REQUIRED
- First blocker: raw official Login and Character request/response packet formats are not present in migrated evidence.
- Protocol Runtime: PASS
- TCP Ingress: PASS
- Frame Decode: PASS
- Packet Route: PASS for known/heartbeat and explicit handler-missing result
- Unknown Packet Capture: PASS
- Heartbeat: PASS
- Login Runtime: PARTIAL
- Account Authentication: PASS
- Session Authority: PASS
- Duplicate Login: PASS
- Login Response: NEEDS PROTOCOL RECOVERY
- Character List: service PASS; official packet response NEEDS PROTOCOL RECOVERY
- Character Create: service PASS; official packet format NEEDS PROTOCOL RECOVERY
- Character Delete: service PASS; official packet format NEEDS PROTOCOL RECOVERY
- Character Rename: service PASS; official packet format NEEDS PROTOCOL RECOVERY
- Character Select: service PASS; official packet format NEEDS PROTOCOL RECOVERY
- Character Ownership: PASS
- Migrated Evidence: heartbeat, movement, NPC, merchant, logout retained
- Migrated Opcode: no new guessed opcodes
- Remaining Unknown Packets: login and character-management raw request/response packets
- Official Client Test: not run to Character List because protocol evidence is insufficient for login response serialization
- Whether entered Character List: No official-client PASS claimed
- Whether completed Character Select: No official-client PASS claimed
- Whether entered World: No, out of scope
- Build: blocked in real workspace by missing .NET 10 SDK; temporary net8 compile PASS
- Warnings: temporary compile 0
- Tests: temporary focused protocol/runtime tests PASS
- Unified Server modification count: 0
- Launcher modification count: 0
- Next single step: recover raw official Login and CharacterList/Select packet definitions, then bind packet handlers to `AuthenticationService` and `CharacterRuntimeService`.
- Result: God2 Classic Server Phase 5-7 PARTIAL - PROTOCOL RECOVERY REQUIRED

Unified Login / World Runtime verification:

- Runtime now binds only `Network.LoginPort` as the unified Game Endpoint.
- `Network.WorldPort` remains a legacy/deprecated config field and is ignored by runtime binding.
- `TcpNetworkHost` exposes listener diagnostics for tests: listener count, accept loop count, and bound ports.
- Receive loop refreshes the latest `RuntimeSession` from `SessionAuthority` before every complete frame.
- `SessionStore` now supports Create, Get, Authenticate, BindCharacter, Transition, and Close.
- Disconnect cleanup path closes the latest session and supports `ISessionCloseObserver`; login cleanup releases `DuplicateLoginGuard` and clears `Account.CurrentSessionId`.
- Protocol stages now include `WorldEntering` and `InWorld`.
- Stage gate now allows world-entry families after `CharacterSelected`/`WorldEntering`, and gameplay families only at `InWorld`.
- Console ready output now shows one `Game Endpoint`, `Login / World Mode: Unified`, and `Network Listener Count: 1`.
- Startup stage 9 is now `Unified Network Host`.

Required real workspace commands:

- `dotnet restore God2ClassicServer.sln`: failed before restore with `NETSDK1045`; installed SDK is 8.0.422 and projects target `net10.0`.
- `dotnet build God2ClassicServer.sln`: failed before compilation with the same `NETSDK1045` environment blocker.
- `dotnet test God2ClassicServer.sln`: failed before test execution with the same `NETSDK1045` environment blocker.

Temporary verification because local SDK 10 is unavailable:

- Copied workspace to `%TEMP%` and changed only the temporary copy from `net10.0` to `net8.0`.
- Temporary `dotnet build`: 0 warnings, 0 errors.
- Temporary runtime tests: 12 passed, 0 failed.
- Temporary integration tests excluding the two retarget-only `net10.0` assertions: 19 passed, 0 failed.
- Temporary full solution tests excluding the same two retarget-only assertions: 50 passed, 0 failed.

Unified runtime status:

- Login / World Unified Runtime Architecture: PASS
- Single Listener: PASS
- Single Connection Lifecycle: PASS
- Session Stage Refresh: PASS
- Disconnect Cleanup: PASS
- Official Login Packet: PARTIAL - PROTOCOL RECOVERY REQUIRED
- Official World Entry Packet: PARTIAL - PROTOCOL RECOVERY REQUIRED

Unified Runtime Finalization Sprint verification:

- Production composition root now uses `UnifiedRuntimeComposition`.
- `ConsoleEntry` uses one shared `SessionStore`, `DuplicateLoginGuard`, `AccountRepository`, `AuthenticationService`, `CharacterRuntimeService`, and `SessionAuthority` through that composition.
- `SessionStore` rejects stage regressions and supports the forward lifecycle: Connected, Login, Authenticated, CharacterList, CharacterSelected, WorldEntering, InWorld, Closing, Closed.
- `AuthenticationService` now transitions successful login through `Authenticated` before `CharacterList`.
- `ProtocolConnectionRuntime` sequence validation is per `ConnectionId`, not one global monotonic validator.
- `CharacterRuntimeRegistry` releases selected-character runtime registration on session close.
- Added tests for shared runtime composition, per-connection sequence isolation, stage-regression rejection, and selected-character cleanup.

Required net10 command status for this sprint:

- `dotnet restore God2ClassicServer.sln`: not passable in this environment because only .NET SDK 8.0.422 is installed; `NETSDK1045` is the true external blocker.
- Per sprint rule, `dotnet build` and `dotnet test` are not claimed PASS while restore cannot evaluate the `net10.0` projects.

Current status remains:

- PARTIAL - PROTOCOL RECOVERY REQUIRED
- True build blocker: missing .NET 10 SDK.
- True protocol blockers: missing official raw Login, CharacterList, CharacterSelect, and World Entry packets.

Official Login / Character Protocol Recovery Sprint verification:

- Date: 2026-07-28 Asia/Taipei.
- .NET SDK observed by command output: 10.0.302.
- Added evidence-gated official Login / Character protocol codec.
- Added runtime bridge from protocol codec to AuthenticationService, CharacterListQuery, and CharacterRuntimeService.
- Runtime bridge refuses Login, CharacterList, CharacterSelect, and World Entry handling with `protocol.recovery_required` until verified official raw packet evidence exists.
- No Launcher files modified.
- No Runtime Import, MariaDB Import, Runtime Cache, Unified Runtime, or Startup files modified.
- `dotnet restore God2ClassicServer.sln`: PASS after NuGet network access was allowed.
- `dotnet build God2ClassicServer.sln -c Release`: PASS, 0 warnings, 0 errors.
- `dotnet test God2ClassicServer.sln -c Release --no-build`: PASS, 58 passed, 0 failed, 0 skipped.
- Current status: PARTIAL - PROTOCOL RECOVERY REQUIRED.
- True protocol blocker: verified official raw Login Request, Login Response, CharacterList Request, CharacterList Response, CharacterSelect Request, CharacterSelect Response, and World Entry Context packets are still absent from ProtocolRegistry evidence.
