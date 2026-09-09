# Official Client First Contact Final

## Freeze

Official Client First Contact is frozen at the verified golden path:

Unknown -> LoginHandshake -> LoginAuthenticated -> CharacterSelect -> PendingWorld -> WorldHandshake -> InWorld

Verified stages:

- Launcher-owned client startup
- Login 19/19/6 handshake
- Login version follow-up `0600ED7CEE11`
- 208-byte LoginRequest
- Server Group bootstrap
- Server Selection / Character List request `0600A60001D9`
- Character Select
- World role split on the second connection
- World first S2C `0600A96DB7E8`
- World bootstrap stream, 1778 bytes
- World screen visible with character `kero`
- 5-byte heartbeat/world traffic sustained for at least 60 seconds

## Golden Artifacts

- Golden bundle: `Artifacts/OfficialClientFirstContact/Golden/20260730-041612`
- Golden run: `Artifacts/ClientInstrumentation/ElevatedAutomationHost/host-run-20260730-041612`
- World screenshot: `Artifacts/ClientInstrumentation/ElevatedAutomationHost/host-run-20260730-041612/enter-world-enter-world-confirm-world-ready-2.png`
- Trace metadata: `Artifacts/ClientInstrumentation/ElevatedAutomationHost/host-run-20260730-041612/attempt-1-trace/metadata.jsonl`
- Server log: `Artifacts/Runtime/ServerRuns/freeze-regression-20260730-041527/stdout.log`
- Protocol records: `src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/login-to-character-select-records.json`
- Unknown world packet batch: `src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/world-unknown-first-batch.json`

Sensitive payloads and credentials are not copied into this report.

## Regression Gates

- Runtime protocol tests must pass for login version, 208-byte login bootstrap, server selection, world handshake/bootstrap, and pending-world routing.
- PowerShell host syntax must parse successfully.
- WorldReady must require world bootstrap activity, visible world screen, and sustained heartbeat/world traffic.
- Latest full run: PASS, `WorldReady=true`, `heartbeatCount=73`, `heartbeatDurationSeconds=60.395`, `worldTrafficCount=115`.
- Stop All: PASS, `God2_opt` remaining count is 0; Launcher intentionally remains alive.

## Next Phase

World Protocol Recovery starts from the captured unknown world packets:

- Client-to-server 41-byte world-entry acknowledgement candidate
- Client-to-server 71-byte bootstrap follow-up candidate
- Client-to-server 12-byte state acknowledgement candidate
- Repeating 5-byte heartbeat/world traffic
- Server-decoded unknown payload lengths: 206, 39, 69, 10
