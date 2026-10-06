# Merchant BUY response and execution — 2026-10-06

## Implementation

- BUY response projection reproduces the existing Classic Stage-5 frame.
- Supported profile: exact build god2-opt-6b127086e0c0, handle 3954,
  client item 6901, quantity 1, catalog index 7.
- Response is encoded World opcode 0x3B, length 57.
- Preflight permits only an empty authoritative inventory.
- Postcommit validation requires one canonical item 253231541 at authority
  slot 0, quantity 1, matching transaction and incremented versions.
- Historical replay receipts do not emit possibly obsolete wallet balances.
- Execution shares the existing connection lease and revalidates interaction.
- TCP execution requires explicitly injected purchase and sale writers.
- Host supplies neither writer, so normal Host execution remains disabled.
- SELL TCP dispatch remains unwired.
- Failed, uncertain or unreconciled execution closes the TCP connection.
- No automatic purchase retry is performed.

## Verification

- Classic merchant/inventory baseline: 23/23 passed.
- V2 Protocol Release suite: 136/136 passed.
- V2 Network Release suite: 165/165 passed.
- Host Release build passed.
- Six TCP execution cases passed:
  unsupported 5042 profile with zero writer calls;
  successful 3954 response; committed layout mismatch;
  evidence rejection; writer exception; historical replay.
- Successful TCP fixture receives the encoded 57-byte purchase response
  before ordered logout and EOF.

## Limits

- TCP repositories, writers, authority and distance are synthetic fixtures.
- The 3954 test spawn application hash is computed for a synthetic transport
  fixture; it is not an official captured spawn or evidence promotion.
- Existing Classic capture bytes establish a regression baseline only.
- Original attempt-759 trace and Stage 4-6 exports remain unavailable.
- Formal merchant authority and protocol promotion gates were not changed.
- This does not establish generic inventory layouts, stacking, repeated BUY,
  production-ready replay reconciliation, or original-client acceptance.
- Writer exceptions do not simulate a lost database commit acknowledgement.
- Earlier filtered MariaDB purchase and command tests passed and cleaned
  fixtures; the new execution coordinator has not yet been DB-integrated.
- No production 6001 deployment/restart or test001 wallet change occurred.
