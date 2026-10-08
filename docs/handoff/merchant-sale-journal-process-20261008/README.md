# SELL journal and separate-process recovery — 2026-10-08

- Migration 482 adds immutable durable SELL requests.
- Journaled writer saves the exact request before sale dispatch.
- Shared SELL identity preserves the existing receipt key and fingerprint.
- TCP test verifies committed SELL result loss and unchanged explicit replay.
- A separate test process retrieves the request and receipt from DB only.
- Reconciliation does not dispatch a sale or authorize automatic retry.
- Balance remains 64; inventory/mutation/wallet versions remain 2.
- Both test processes passed: 2/2, 0 failed, 0 skipped.
- Fixture cleanup: remaining_shop_fixture=0; exit code 0.
- Host Release build previously passed with 0 warnings and 0 errors.
- This verification uses a disabled synthetic fixture, not official client evidence.
- Production Host wiring and official client acceptance remain pending.
