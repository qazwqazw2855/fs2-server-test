# Isolated Host login/logout verification — 2026-10-08

- Source: a0138e3 plus the LoginProbe NPC expectation override.
- Host and LoginProbe Release builds passed.
- Host listened on loopback port 6002.
- LoginProbe verified account login and character test001 / ID 1.
- World handshake and the complete 1772-byte bootstrap passed.
- Explicit expected NPC handle 3793 was received.
- Official logout passed; Host released character presence and closed the connection.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Movement persistence and merchant execution were disabled.
- BUY/SELL evidence gates remained Blocked.
- Production port 6001 was not restarted or deployed.
- This is automated protocol verification, not original-client acceptance.
- LoginProbe now accepts GOD2_PROBE_EXPECTED_NPC_HANDLES as a comma-separated list of positive integers.
- Existing mode-specific NPC expectations remain the default when the override is absent.
- Local logs: /tmp/god2-host-probe-20261008-ESEaxN
