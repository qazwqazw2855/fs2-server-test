# Connection command lane — 2026-10-03

- Network suite: 106 passed, 0 failed, 0 skipped.
- Real merchant command DB closed-loop test: 1 passed; fixture cleaned.
- MerchantCommandService requires an explicitly supplied command lane.
- The lane covers interaction checks through writer completion.
- Coordinated state updates use the same lane; close rejects new/queued commands and waits for the active operation before cleanup.
- Tests verify serialization, operation failure recovery, close waiting for a dispatched writer, cleanup once and post-close rejection.
- Cleanup failure remains observable and leaves the lane closed.
- Callbacks must not recursively enter or close the same lane.
- TCP state updates and cleanup are not wired yet.
- Registry mutations and NPC snapshot publication outside the lane remain uncoordinated.
- No production deployment, official content promotion or Client acceptance.
- Full six-suite verification has not been rerun for this change.

## TCP frame and cleanup verification

- Tested source: b17cdbbebdcd62fcfb05ecd6ce21bf0962f61eb3.
- Six suites: 401 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 107, Persistence Integration 42.
- All fixture runners reported successful cleanup.
- TCP World frame handling acquires a connection-local lane lease after reading.
- Continue, break, return and exceptions release the frame lease.
- Disconnect cleanup runs through CloseAsync.
- Isolated 127.0.0.1:6002 Probe exited 0: login, 1772-byte bootstrap, heartbeat and presence cleanup passed.
- Account god2test; character test001, ID 1; map 170015007, position (17,15).
- Movement persistence was disabled; movement, NPC interaction and merchant TCP dispatch were not exercised.
- Merchant commands are not wired into TCP and do not yet share its lane instance.
- Future dispatch must avoid recursively acquiring a lane already held by the frame.
- External registry publishers are not fully coordinated by this lane.
- Production 6001 remains deployed from 3344eb3; no Client acceptance performed.
