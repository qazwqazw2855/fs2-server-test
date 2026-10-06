# Merchant TCP preparation checkpoint — 2026-10-06

Branch: codex/inventory-grant-20261003
Previous commit: 2c21f97 — prepare merchant purchases from server snapshots

## Implementation

- Host supplies merchant authority, catalog and wallet repositories.
- TCP BUY preparation uses the existing connection command lease.
- Preparation projects server-owned catalog, inventory and wallet snapshots.
- Transaction execution remains BlockedNotWired; no purchase response is emitted.
- Merchant codec exposes EncodeRequest for transport fixtures.
- LoginProbe adds a mutually exclusive merchant rejection mode.
- This probe expects NPC 3793 on the current test001 map.

## Verified

- Protocol: 125/125 passed, including three EncodeRequest round trips.
- Network: 139/139 passed after the Protocol and TCP changes.
- Host and LoginProbe Release builds passed.
- Isolated 127.0.0.1:6002 probe passed:
  login → World handshake → 1772-byte bootstrap → NPC 3793.
- Synthetic BUY fixture: handle 5042, client item 6901, quantity 1,
  operation Buy, catalog index 7; no NPC interaction was opened.
- Probe received EOF without a transaction response.
- Server explicitly logged NoCurrentNpcInteraction.
- No Merchant BUY preparation log appeared.
- Evidence: merchant-rejection-20261006-113034/server-6002.log
  and merchant-rejection-20261006-113034/probe-6002.log.

## Limits and next work

- This validates the TCP ingress rejection gate, not the successful
  TCP preparation branch or a completed purchase.
- Synthetic fixture bytes are not new official capture evidence.
- Classic source and RoadMap describe exact-build NPC-open axis distance 3
  at RVA 0x00089511–0x0008953F. Referenced recovery artifacts are absent
  from this checkout; provenance and applicability to transaction
  revalidation remain to be checked.
- Merchant authority enablement and distance settings were not changed.
- Purchase writer and purchase response wire remain unwired.
- SELL slot-to-instance mapping remains unresolved.
- No production 6001 deployment/restart was performed.
- No manual wallet seeding was performed.
