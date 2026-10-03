# Merchant purchase late-write rollback — 2026-10-04

- Targeted MerchantPurchaseIntegrationTests: 1 passed, 0 failed, 0 skipped.
- Extended the existing test; test count is unchanged.
- Randomly named fault triggers match only dedicated fixture users, the fixture character and V2MerchantBuy.
- Receipt fault confirms wallet balance/version are 60/1 before raising error 1644.
- Audit fault confirms wallet balance/version are 60/1 and the purchase receipt exists before raising error 1644.
- Tests require the exact fault message, distinguishing the intended phase from failed preconditions.
- After each failure, the inventory snapshot matches its initial state, wallet balance/version are 100/0, and no character inventory row, receipt or audit remains.
- The identical request subsequently succeeds once among eight concurrent requests; seven replay.
- Cleanup reported remaining_shop_fixture=0, including temporary users, merchant and both triggers.
- Production writers were unchanged.
- Latest full six-suite result remains 405 passed at source 1f1c98c; full suites were not rerun for this change.
- No TCP merchant dispatch, production deployment or Client acceptance performed.
