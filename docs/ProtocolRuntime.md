# Protocol Runtime

Phase 5 adds a real protocol runtime boundary for TCP ingress.

Login and World now share one TCP listener, one connection, one session, and one runtime pipeline.

Implemented:

- `ProtocolStage`: Connected, Login, Authenticated, CharacterList, CharacterSelected, WorldEntering, InWorld, Closing, Closed.
- `FrameAccumulator`: supports partial packets, multiple packets in one receive, invalid length rejection, oversized packet rejection, and empty receive buffering.
- `Packet sequence boundary`: monotonic sequence validator, with sequence `0` preserved as unknown/not recovered.
- `Checksum boundary`: explicit no-recovered-checksum implementation, so checksum is not guessed.
- `Encryption boundary`: explicit no-recovered-encryption implementation, so encryption is not guessed.
- `ProtocolConnectionRuntime`: frame decode, stage validation, packet routing, heartbeat acceptance, unknown packet capture, and audit logging.
- `TcpNetworkHost`: starts one TCP listener on `Network.LoginPort`, accepts TCP clients, assigns connection ids, reads receive buffers, accumulates frames, refreshes the latest session before every frame, decodes packets, captures unknown packets, and writes safe console status.
- `Network.WorldPort`: retained only as a deprecated legacy configuration field and ignored by runtime binding.
- `UnifiedRuntimeComposition`: one production composition root creates the single `SessionStore`, `DuplicateLoginGuard`, account repository, authentication service, character runtime service, session authority, and network host runtime wiring.
- `SessionStore`: owns all session updates and rejects stage regressions such as `CharacterSelected -> Login`.
- Packet sequence validation is tracked per `ConnectionId`; players no longer share one global `lastSequence`.
- Disconnect cleanup closes the session and releases duplicate-login, account session ownership, and character runtime registration through close observers.

Sensitive data policy:

- Logs contain opcode candidate, payload length, stage, connection id, session id, and payload hash.
- Logs do not contain passwords, session secrets, database passwords, or full sensitive payloads.
- Unknown raw payload bytes are written to runtime evidence files for protocol recovery.

Current recovery boundary:

- Heartbeat packets are accepted from migrated evidence.
- Movement is classified but stage-gated away from Login.
- Movement is accepted by the stage gate only after the session reaches `InWorld`.
- Login, CharacterList, CharacterCreate, CharacterDelete, CharacterRename, and CharacterSelect packet formats are not promoted to PASS because raw official packet definitions are not present in the current knowledge base.
