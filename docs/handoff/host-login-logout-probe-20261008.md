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

## Idle timeout and same-Host relogin verification

- Source: 0c3f84e.
- One isolated Host remained running on loopback port 6002.
- The first probe observed World connection closure after 30.0 seconds of inactivity.
- Host logs confirmed the 30-second idle timeout and presence release.
- The same account subsequently re-entered World as test001 / ID 1.
- The second probe passed official logout.
- Host logs recorded exactly two world presence entries and two releases.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Local logs: /tmp/god2-host-idle-20261008-1AqRad
- Production port 6001 was not restarted or deployed; original-client acceptance remains pending.

## Repeatable Host lifecycle runner

- Added Automation/test-v2-host-session-lifecycle.sh.
- Tested source: e61cbb4 plus the new runner.
- Host and LoginProbe Release builds passed.
- One isolated Host on loopback port 6002 completed all scenarios.
- Coverage: logout/relogin, pending ownership rejection, active-World duplicate login rejection, idle timeout and subsequent relogin/logout.
- The active probe completed its 15-second heartbeat hold; idle closure was observed at 30.0 seconds.
- Host logs recorded exactly six world presence entries and six releases.
- Runner exit code: 0; its Host process was terminated during cleanup.
- progress.json remained unchanged.
- Merchant execution and movement persistence remained disabled.
- This runner expects test001 / ID 1 and NPC 3793 in the current test environment.
- Local logs: /tmp/god2-host-lifecycle-1Bl9s5
- Production deployment and original-client acceptance remain pending.

## Movement sequencing and rejection recovery

- Source: 99a79ea.
- Read-only DB inspection confirmed map 170015007 bounds: X/Y 0 through 293.
- Recovered movement samples target (16,14), sequence 1, and (16,15), sequence 2.
- One isolated Host on loopback port 6002 served all three probes.
- Consecutive movement requests received correctly sequenced acknowledgements and completed logout.
- Repeating sequence 1 closed the connection without a second acknowledgement.
- The same account subsequently re-entered World and passed official logout.
- Host logs recorded exactly three world presence entries and three releases.
- Movement bounds enforcement was enabled; movement persistence was disabled.
- Runner exit code: 0; its Host process was terminated during cleanup.
- Local logs: /tmp/god2-host-movement-20261008-gohJDA
- This verifies protocol acknowledgements and connection recovery, not durable position updates or original-client movement acceptance.

## Lifecycle runner with movement coverage

- Tested source: c0fae98 plus the lifecycle runner extension.
- Added consecutive movement, duplicate movement sequence rejection and subsequent relogin/logout.
- All existing login, pending ownership, duplicate login and idle timeout scenarios passed together with the movement scenarios.
- One isolated Host recorded exactly nine world presence entries and nine releases.
- Host and LoginProbe Release builds passed.
- Runner exit code: 0; its Host process was terminated during cleanup.
- progress.json remained unchanged.
- Movement bounds enforcement remained enabled; movement persistence and merchant execution remained disabled.
- Local logs: /tmp/god2-host-lifecycle-8cxvcF
- Durable position updates, production deployment and original-client acceptance were not verified by this run.
