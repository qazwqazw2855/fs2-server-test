# Merchant TCP checkpoint — 2026-10-04

## Checkpoint
- Branch: `codex/inventory-grant-20261003`
- Implementation checkpoint: `70cb113`
- Repository: `/home/ubuntu/games/fs2-v2-inventory-grant-20261003`
- Production 6001 was not deployed or restarted.

## Completed
- Generic `OfficialMerchantTransactionCodec` handles the 12-byte opcode `0x38` request.
- Codec now owns the encoded World-frame boundary through `IsCandidate` / `TryDecode`.
- It extracts NPC handle, client item ID, quantity, BUY/SELL operation and catalog index/client inventory slot.
- It validates length, opcode, checksum, nonzero identities/quantity and supported operation.
- Decoder does not authorize merchants, trust client prices or resolve authoritative inventory identity.
- Protocol suite is now 125/125.
- `InternalsVisibleTo` is limited to Protocol.Tests so tests can use the existing World encoder without duplicating cipher logic.

## Server-owned merchant projections
- Migration 479 publishes the verified current-build merchant client catalog identity.
- Migration 480 grants the V2 runtime role the required merchant read access.
- Migration 479 checksum:
  `3dd4c3b7aa94c7e61ef79543cb1adae7dbbd319887b0fc78dcc02acdb2ad1f94`
- Migration 480 checksum:
  `870d0ce2e1bfe6b82d24a058333c83bfa984e484016bf111bbba6b65f1790039`
- Added `MerchantCatalogIdentity` repository for exact:
  MerchantId + ClientBuildId + wire catalog index + client item ID → canonical server listing/item identity.
- Added `CharacterWalletSnapshotRepository`; wallet state remains server-owned.
- No client-supplied price is authoritative.

## Merchant interaction authority
- Added `MerchantInteractionAuthority` and MariaDB repository.
- Authority lookup key is:
  `SpawnId + MapId + ClientBuildId + ClientEntityHandle`.
- The resolved authority contains MerchantId; TCP does not hardcode MerchantId.
- Merchant/NPC identity is checked bidirectionally.
- Disabled/ambiguous/mismatched authority fails closed.
- Added `MerchantInteractionBindingProvider` as a straight authority → binding projection.
- Provider does not infer evidence, distance or enablement.

## TCP authorization chain
Current request path:

`0x38 encoded World frame`
→ `OfficialMerchantTransactionCodec`
→ current `NpcInteractionSession`
→ handle ownership check
→ authoritative `SpawnId / MapId / Handle`
→ DB Merchant Authority
→ `MerchantId / MerchantInteractionBinding`
→ `MerchantInteractionResolver.Check`
→ transaction execution boundary

- The NPC interaction session's SpawnId comes from the server-visible NPC registry, not the client request.
- Client build comes from the server's exact-current build identity, not the `0x38` payload.
- Resolver rechecks interaction, world presence, NPC identity and policy.
- Existing per-connection `ConnectionCommandLane` remains authoritative; no second lane was introduced.

## Current fail-closed state
- Merchant handle 3954 can resolve formal identity, but its merchant interaction Binding is not approved.
- Current Binding remains `Enabled=false`.
- Current `MaximumDistance=null`.
- Current merchant interaction evidence is not sufficient for promotion.
- Therefore `MerchantInteractionResolver` returns a blocked status.
- Do not promote a distance such as `2` from test fixtures.
- NPC Open spatial logging is `ObservationOnly` and is not authorization evidence.

## Transaction boundary
- BUY execution is intentionally not wired yet.
- `PurchaseInLeaseAsync` has not been called from TCP.
- No Gold is deducted.
- No item is granted.
- No merchant result wire response is emitted.
- SELL execution remains unwired.
- Do not infer SELL item-instance identity from the client slot value.
- `test001` currently has no authoritative Gold wallet row; do not insert one manually merely to make BUY pass.

## Verification at close
- Core: 7/7
- Application: 114/114
- Session: 20/20
- Protocol: 125/125
- Network: 113/113
- Non-DB suites total: 379/379
- Merchant Catalog Identity + Merchant Interaction Authority DB integration: 4/4
- Network project build succeeded after TCP authority wiring.
- `git diff --check` clean before checkpoint commit.

## Evidence boundaries
- Shops share generic functionality; catalogs and prices are server configuration.
- CN official evidence remains supplemental and must not be merged with private historical provenance.
- CN observed handle 1504 / item 6906 / BUY index 13 / SELL slot 12 remain capture evidence only.
- CN observed purchase price 60 and buyback 6 are catalog-specific evidence, not global item-price rules.
- Private historical grocery mapping remains separate provenance.
- 3954 must not be promoted solely from historical rows or test fixtures.

## Next implementation
1. Establish independently supportable merchant interaction evidence / distance policy before enabling a Binding.
2. Keep the lookup chain server-owned:
   current interaction → Spawn/Map/Build/Handle → Authority → MerchantId/Binding.
3. After interaction authorization is legitimately `Allowed`, resolve BUY catalog identity through the server repository.
4. Read authoritative inventory and wallet snapshots.
5. Construct `MerchantPurchaseCommand` from server-owned state.
6. Dispatch through `PurchaseInLeaseAsync` using the TCP connection's existing `ConnectionCommandLane`.
7. Keep SELL blocked until authoritative client-slot → item-instance projection is established.
8. Verify on isolated 6002 before any production 6001 deployment.

## Permanent rules
- 沒有正服證據，不猜協定；沒有建立 baseline，不直接改 V2。
- Taiwan official evidence first; CN official/public beta remains supplemental with explicit provenance.
- Merchant products and prices are DB configuration, not NPC-specific hardcoded TCP logic.
- Do not hardcode MerchantId in TCP.
- Do not trust client price, character identity, wallet state or canonical item identity.
- Do not create a second connection command lane.
- Do not deploy or restart 6001 before an explicitly validated checkpoint.
