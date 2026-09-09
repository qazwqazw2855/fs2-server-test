# Protocol Coverage

Current phase status:

- Login / World Unified Runtime Architecture: PASS
- Single Listener: PASS
- Single Connection Lifecycle: PASS
- Session Stage Refresh: PASS
- Disconnect Cleanup: PASS
- Per-Connection Packet Sequence: PASS
- Single Runtime Composition: PASS
- Protocol Runtime: PASS for TCP ingress boundary, frame accumulation, decode, route result, heartbeat, stage rejection, and unknown capture.
- Login Runtime: PARTIAL for official-client packet handling; PASS for service authentication/session authority behavior.
- Character Runtime: PARTIAL for create/delete/rename packet formats; PASS for service list/create/delete/rename/select ownership behavior.

Migrated evidence:

- Heartbeat: verified/recovered raw frames from migrated evidence.
- Movement: recovered family classification with unknown semantic fields.
- NPC, Merchant, Logout: raw candidate or visual-evidence-only entries remain recovery-gated.

Remaining unknown packets:

- Login request / response raw format
- Character list request / response raw format
- Character create request / response raw format
- Character delete request / response raw format
- Character rename request / response raw format
- Character select request / response raw format
- World transfer raw context packet format

Official client test boundary:

- TCP connect can be accepted by `TcpNetworkHost`.
- Heartbeat can be decoded/accepted.
- Login and character packet responses are not claimed as official-client PASS until raw packet serializer definitions are recovered.
- World entry is out of scope for this phase.
- Official Login Packet: PARTIAL - PROTOCOL RECOVERY REQUIRED
- Official World Entry Packet: PARTIAL - PROTOCOL RECOVERY REQUIRED
- Official Login / Character Protocol Recovery Sprint:
  - Login Request: gated by missing official raw packet evidence.
  - Login Response: gated by missing official success/failure raw response evidence.
  - CharacterList Request: gated by missing official raw request evidence.
  - CharacterList Response: gated by missing official character entry raw structure evidence.
  - CharacterSelect Request: gated by missing official CharacterId/Slot raw request evidence.
  - CharacterSelect Response: gated by missing official raw response evidence.
  - World Entry Context: gated by missing official post-select world entry raw packet sequence.
  - Runtime handlers return `protocol.recovery_required` and do not mutate session/account/character state without decoded official packets.

Result name:

`God2 Classic Server Phase 5-7 PARTIAL - PROTOCOL RECOVERY REQUIRED`
