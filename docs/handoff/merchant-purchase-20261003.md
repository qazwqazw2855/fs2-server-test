# Merchant purchase verification — 2026-10-03

- Six suites: 362 passed, 0 failed, 0 skipped; DB integration enabled.
- Suites: Core 7, Application 104, Session 20, Protocol 111, Network 80, Persistence Integration 40.
- Internal purchase loads enabled merchant listing price and pack count from DB.
- Item grant, wallet deduction, replay receipt and audits share one transaction.
- Eight identical concurrent requests yield one purchase and seven replays.
- Two distinct requests using the same wallet version yield one purchase and one version conflict.
- Insufficient funds are rejected; committed requests replay across new writers.
- Wallet UPDATE permission denial after grant writes triggers owned transaction rollback.
- Inventory snapshot, wallet balance/version and character-scoped replay/audit records were checked after rollback.
- The exact failed request succeeds when retried with the normal fixture account.
- Failure after wallet deduction during purchase receipt or wallet audit writes was not injected.
- Fixture price 40 and Gold approval are test inputs, not promotion of official merchant content.
- Temporary disabled characters, merchant/listing and DB users were cleaned.
- Repeat: Automation/test-v2-merchant-purchase.sh --all with GOD2_TEST_PASSWORD set.
- Production deployment source remains 3344eb3; no deployment in this round.
- Network integration and original-client purchase acceptance remain unverified.
- Current phase: M2 baseline/gap parity; no promotion gate was advanced.
- Next work: reviewed merchant listing/currency provenance and trusted NPC interaction integration.
