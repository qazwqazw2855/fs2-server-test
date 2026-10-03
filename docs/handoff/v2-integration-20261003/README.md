# V2 integration verification — 2026-10-03

- Tested source: 253560f; integration merge: 1f0853a.
- Six test suites: 292 passed, 0 failed, 0 skipped.
- DB integration enabled against 127.0.0.1:3308.
- Isolated endpoint: 127.0.0.1:6002; movement persistence disabled.
- Probe mode: GOD2_PROBE_VERIFY_WORLD_LOGIN_MAP=1.
- Probe exit code: 0.
- Login, character selection, World handshake and 1772-byte bootstrap passed.
- Server verified map 170015007, client 15:7, position (17,15).
- No Portal projection appended; heartbeat classified as KeepAlive.
- Disconnect released World presence.
- NPC spawn, Portal and original Windows Client visuals were not verified by this Probe.
- Production 6001 was not deployed during this verification.
- Earlier default-mode Probe exited 134 after bootstrap because it expected NPC 5042 but received 3793; no code or NPC evidence gate was changed.

## Production acceptance

- Deployed source: 3344eb3.
- Restart: 2026-10-03 09:03:20 +08:00; PID 1438231.
- Listener: 0.0.0.0:6001; advertised 52.63.34.162:6001.
- Probe connected locally to 127.0.0.1:6001; exit code 0.
- Login → World, 1772-byte bootstrap, heartbeat and presence cleanup passed.
- Map 170015007; client 15:7; position (17,15).
- Movement persistence enabled; movement was not exercised.
- External connectivity, NPC, Portal and Windows Client visuals were not verified.
- Pre-deployment binaries backed up at /home/ubuntu/games/v2-deploy-backup-20261003.eIxWtl.
- Backup source-commit.txt records deployment target 3344eb3, not the unknown source of the previous binaries.
