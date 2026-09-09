# Packet Documentation

Each packet knowledge row carries:

- `Confidence`
- `Recovered`
- `Verified`
- `EvidenceSources`
- `KnownFields`
- `UnknownFields`
- `Samples`
- `FixedPrefixes`

## Implemented Layer

- `ProtocolRegistry` holds the official protocol knowledge base.
- `OpcodeRegistry` resolves opcode candidates.
- `PacketRegistry` stores known packet metadata and verified handlers.
- `PacketFactory` creates envelopes from raw frames or known packet metadata.
- `PacketSerializer` writes length-prefixed frames.
- `PacketDeserializer` validates length prefix and classifies recovered frames.

## Remaining Recovery

Character delete/rename, portal/map transfer raw frames, merchant buy/sell, inventory move, equip/unequip, quest accept/complete, combat, skill, drop item, and pickup item remain `NeedsRecovery`.

## Phase 3.6 Packet Catalog

Current formal world records:

- `WorldVersionAcceptedS2C6`
- `PlayerSpawnS2C128`
- `WorldMapSceneBootstrapS2C320`
- `WorldStaticStateBootstrapS2C752`
- `WorldParserAcceptedStateS2C68`
- `WorldCompactStateB3S2C182`
- `WorldUiStateS2C36`
- `WorldCompactStateD3S2C63`
- `WorldCompactState61S2C42`
- `WorldCompactState66S2C67`
- `WorldCompactStateA7S2C88`
- `WorldBootstrapTerminator9ES2C26`
- `WorldHeartbeatC2S5`
- `WorldEntryReadyAckC2S41`
- `WorldBootstrapFollowUpRequestC2S71`
- `WorldUiStateAckC2S12`

NPC recovery starts from `world-npc-interaction-client-8-byte-candidate` as the first known C2S touchpoint; S2C NPC spawn is the next blocker.
