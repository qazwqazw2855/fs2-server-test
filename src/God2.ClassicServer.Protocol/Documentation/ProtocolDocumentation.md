# Protocol Documentation

God2 Classic Server protocol layer owns the migrated protocol knowledge. It does not depend on Unified Server projects, runtime binaries, old solution files, LoginServer, WorldServer, Control Center, or GUI code.

## Visibility Boundary

Official visual success is evidence, but it is not raw opcode proof. A packet can become `Verified` only when repeated raw frame evidence or source-backed frozen startup evidence exists.

Unknown encrypted or obfuscated fields remain `Unknown` / `NeedsRecovery`. Do not invent semantic fields.

## Common Frame

Current recovered raw frames use a two-byte little-endian length prefix at offset `0`.

Bytes at offset `2` can be an opcode candidate, but must not be treated as a confirmed opcode until repeated differential analysis proves the action semantics.

## Families In Scope

Heartbeat, Login, Character, WorldTransfer, Movement, NPC, Merchant, Inventory, Item, Quest, Portal, MapTransfer, Mount, Unmount, Combat, Skill, Drop, Pickup, Logout, ClientClose.

## Runtime Usage

`TcpNetworkHost` uses `PacketFactory` and `PacketDeserializer` as the ingress classification layer. Gameplay handlers are added later through `PacketRegistry` and `InMemoryPacketRouter`.

## Phase 3.6 World Recovery Priority

Walking Recovery, Mount State Recovery, Coordinate Scale Recovery, Mounted flag semantics, NMP0154 linkage, Walking parser work, and MovementSpeedModel are deferred until official walking evidence exists.

The current active recovery lane is:

- `OfficialWorldBootstrap1778`: 12 frames cataloged in `Evidence/OfficialProtocolCompletion/WorldBootstrapSequence.json`.
- `PlayerSpawnS2C128`: structurally split and built by `OfficialClientWorldProtocolFrames.BuildPlayerSpawnFrame128`.
- `LoginSuccessServerGroupBootstrap`: built by `OfficialClientLoginProtocolFrames.BuildLoginSuccessServerGroupBootstrap`.
- `ServerSelectionCharacterListBootstrap`: built by `OfficialClientLoginProtocolFrames.BuildServerSelectionCharacterListBootstrap`.
- `WorldHeartbeatC2S5`: classified as keepalive/idle tick; sequence/tick/ack semantics remain unproven.
- `WorldEntryReadyAckC2S41`, `WorldBootstrapFollowUpRequestC2S71`, and `WorldUiStateAckC2S12`: classified as entry/bootstrap acknowledgements for the golden path.

The next active lane is NPC Spawn/Despawn/Update packet recovery only; no NPC AI.
