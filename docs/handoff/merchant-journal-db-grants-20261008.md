# Merchant journal DB grants — 2026-10-08

Target: god2-runtime-db-test, 127.0.0.1:3308.
Host account: god2_v2@172.17.0.1.

Both journal tables exist:
- god2_player.v2_merchant_purchase_journal
- god2_player.v2_merchant_sale_journal

Before change: no journal table grants, schema grants or assigned roles;
global privilege was USAGE only.

Applied SELECT and INSERT on each journal table.
Verified all four table privilege entries after the change.
No UPDATE or DELETE privileges were added.

This records an applied test DB privilege change.
Host connection/read/write verification with this account remains pending.
Merchant evidence gates remain Blocked; no deployment or restart was
performed as part of this grant change.

Home follow-up: locate attempt-759 original Windows trace at:
C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Artifacts\ClientInstrumentation\ElevatedAutomationHost\host-run-20260812-000800\attempt-759-trace
