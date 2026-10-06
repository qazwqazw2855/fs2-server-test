# Merchant TCP preparation fixture — 2026-10-06

## Verified

- Network Release suite: 141/141 passed, zero failed or skipped.
- Host Release build passed.
- Two loopback TCP cases use an OS-assigned port and in-memory repositories.
- Both complete Login, server selection, World handshake, 1772-byte
  bootstrap, NPC 5042 spawn, NPC open, BUY and logout.
- With a wallet: interaction=Allowed, prepared=True.
- Without a wallet: interaction=Allowed, prepared=False,
  reason=WalletMissing.
- Catalog lookup uses merchant 99, the pinned spawn client build,
  catalog index 7 and client item 6901.
- Inventory is read at World entry and BUY preparation;
  wallet is read once during preparation.
- Both cases receive EOF after ordered logout without a transaction response.
- Execution remains BlockedNotWired; wallet and inventory snapshots are unchanged.

## Implementation

- TcpGameServer exposes its bound LocalEndpoint after listener startup.
- Login advertises the actual bound port, including when configured port is 0.
- Fixture character uses the supported Female Swordsman Appearance1 profile.
- Failed transport tests include captured server logs.

## Limits

- Merchant authority, catalog and wallet are synthetic test fixtures.
- Fixture distance 2 is not a formal merchant distance approval.
- This validates TCP preparation with injected repositories, not a
  successful purchase using MariaDB or the original client.
- No production 6001 deployment, restart or database change was performed.
- Purchase execution, SELL and transaction response remain unwired.

## Distance evidence audit

- Existing Classic source references exact-build NPC-open axis distance 3
  at RVA 0x00089511–0x0008953F.
- Referenced recovery report and artifact are absent from this checkout.
- Expected runtime SHA256:
  E997DB114BD71E0BD5046D64A53D7D0C1EE78DD96A8C2D5BF224B638A2D2077A.
- Available God2_opt_D1CDD9.runtime.bin SHA256:
  DB51FA1C753DCC6E84DC7B4B3639A9FCBF3E849F207A400F5A239A148300D0F7.
- The hashes differ; the available binary cannot validate that exact-build claim.
- NPC-open evidence alone does not establish BUY transaction revalidation policy.
- Formal merchant enablement and distance settings remain unchanged.
