# Protocol Knowledge Migration

Phase 3.5 migrates protocol knowledge and evidence only. It does not migrate Unified Server architecture, LoginServer, WorldServer, Control Center, GUI, old runtime, old solution, or old dependencies.

## Migrated Evidence

Evidence is preserved under `src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/`.

Included evidence:

- `VerifiedPacketCatalog.json`
- `UnknownPacketCatalog.json`
- `PacketFieldMap.json`
- `PacketSequenceMap.json`
- `PacketCoverageMatrix.csv`
- `OfficialClientE2ESummary.json`
- `OfficialClientMilestones.jsonl`
- `ProtocolCompletionSummary.md`
- `SecurityVerification.md`
- `TestResults.md`
- `Artifacts.sha256`

## Migrated Knowledge

`src/God2.ClassicServer.Protocol/Knowledge/protocol-knowledge-base.json` contains:

- verified-opcodes
- unknown-opcodes
- packet-families
- protocol-field-map
- protocol-visibility-boundary
- heartbeat
- login
- character
- world transfer
- movement
- runtime protocol knowledge
- official packet evidence
- official login evidence
- official world evidence

## Runtime Entry

Classic Server runtime now creates `TcpNetworkHost` with the Classic Server protocol `PacketFactory` / `PacketDeserializer`. This is the official ingress protocol layer for future gameplay handlers.
