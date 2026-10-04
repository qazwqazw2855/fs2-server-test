# Transaction verification — 2026-10-04

- Tested source: 1c2588101cfbd2ff526b88ce6c2c0b8aa8d1bfa9.
- Six suites: 408 passed, 0 failed, 0 skipped; DB integration enabled.
- Core 7, Application 114, Session 20, Protocol 111, Network 111, Persistence Integration 45.
- All fixture runners completed successfully and reported cleanup.
- Purchase and sale late-write failures verify rollback after wallet updates, followed by successful retry and replay protection.
- Inventory stack tests verify merge, spill, capacity rejection and replay inside caller-owned transactions.
- Stack tests use connection-local temporary item and audit tables; transactions are rolled back.
- Committed cross-connection stack replay and concurrent stack merging remain unverified.
- Fixture stack rules do not approve formal item content.
- Original merchant trace and Stage 4–6 exports remain unavailable in the audited locations.
- Merchant TCP dispatch, formal content approval and Client acceptance remain incomplete.
- The earlier b17cdbb isolated World Probe passed; this source did not rerun it.
- Production 6001 was not deployed; recorded production source remains 3344eb3.
