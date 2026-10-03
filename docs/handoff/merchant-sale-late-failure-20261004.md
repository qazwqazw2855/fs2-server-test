# Merchant sale late-write rollback — 2026-10-04

- Targeted MerchantSaleIntegrationTests: 1 passed, 0 failed, 0 skipped.
- Extended the existing test; test count is unchanged.
- Dedicated receipt-failure account can update the wallet but cannot insert transaction receipts.
- Dedicated audit-failure account can update the wallet and insert receipts but cannot insert audits.
- Both failures report MariaDB error 1142 for the expected table.
- After each failure, inventory snapshot and slot version match the pre-sale state.
- Wallet balance/version remain 60/1; no sale receipt or sale audit remains.
- The identical request subsequently succeeds once among eight concurrent requests; seven replay.
- Fixture accounts, temporary DB users and merchant cleanup reported remaining_shop_fixture=0.
- Production writers were unchanged.
- Purchase late-write failures remain unverified.
- Latest full six-suite result remains 405 passed at source 1f1c98c; full suites were not rerun for this change.
- No TCP merchant dispatch, production deployment or Client acceptance performed.
