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

## Same-Host relogin verification

- Source: f6412a2.
- One isolated Host stayed running throughout both login/logout rounds.
- The same account re-entered as test001 / ID 1 successfully.
- Both rounds received NPC handle 3793 and passed official logout.
- Host logs recorded exactly two world presence entries and two releases.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Local logs: /tmp/god2-host-relogin-20261008-r1QyGB
- This verifies sequential ownership reuse, not simultaneous duplicate login.

## Active-World duplicate login verification

- Source: 595d33a.
- One isolated Host on loopback port 6002 served both probes.
- The first probe entered World from 127.0.0.1 and completed a 15-second heartbeat hold.
- A second probe from 127.0.0.2 attempted the same account login.
- The second probe verified the exact DuplicateLogin failure response.
- The original probe completed successfully after the rejection.
- Host logs recorded exactly one world presence entry and one release.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Local logs: /tmp/god2-host-duplicate-20261008-nE0Gab
- Production port 6001 and original-client acceptance remain outside this verification.

## Pending-World ownership verification

- Source: 6c8281b.
- One isolated Host listened on loopback port 6002.
- After character selection and Login connection closure, the probe attempted the same account from a second connection.
- The probe verified the exact DuplicateLogin failure during pending World ownership.
- The original connection subsequently entered World as test001 / ID 1.
- NPC handle 3793 and the heartbeat path passed.
- Host logs recorded exactly one world presence entry and one release.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Local logs: /tmp/god2-host-pending-20261008-5zvEYw
- This run ended by closing the probe connection; it did not test official logout.
