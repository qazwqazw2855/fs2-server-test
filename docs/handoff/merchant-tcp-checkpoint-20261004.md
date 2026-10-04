# Merchant TCP checkpoint — 2026-10-04

## Completed
- Added generic OfficialMerchantTransactionCodec for decoded 12-byte opcode 0x38 requests.
- Extracts NPC handle, client item ID, quantity, buy/sell operation and catalog index/client inventory slot.
- Validates length, opcode, checksum, nonzero identity/quantity and supported operation.
- Decoder does not bind a specific merchant, authorize a transaction or resolve client inventory slots.
- Protocol suite: 121 passed, 0 failed, 0 skipped; 10 cases added.
- Latest full six-suite verification remains 409 passed at source dd31e94.
- Full suites were not rerun after migration 478 or this decoder change.

## Database
- Migration 478 applied to god2-runtime-db-test and journaled.
- Checksum: 38072cfbb3425c4a2aecfe65f74bd5bb7527b218a11eb5b9a778950af1174cf1.
- Small leg meat canonical item_id 2117880098 now has client_item_id 6906.
- Global prices remain buy_price NULL / sell_price 60; maximum_stack remains unknown.
- Observed merchant purchase price 60 and buyback price 6 are catalog-specific evidence.
- No merchant listing or NPC spawn was added this session.

## Evidence boundaries
- User confirms shops share generic functionality; catalogs and prices are server configuration.
- CN official server: 夢迴朝歌; NPC 楓華雜貨鋪老闆.
- CN client SHA256: 6f2639a0a7ad25053d0364108147173eb68bd04f57e6942491f42633f40052bc.
- CN requests: handle 1504, item 6906, purchase index 13, sale client slot 12.
- Two purchases of quantity 1 cost 60 each; one sale of quantity 2 returned 12.
- CN shop-entry capture indicates Area 2 Map 15 and NPC position (17,8).
- Private historical mapping: map 557790525, Area 4 Map 19, indoor/groceryl.hmd.
- Uploaded private groceryL.hmdZ SHA256 matches historical resource evidence:
  42d769c0ff328db3e1e295e17208d0db7a0ff12608976f952dae800ff79c3c00.
- Historical private NPC: npc_id 1075128734, spawn_id 316049902, handle 1504, position (17,8).
- Current DB has the NPC template disabled, merchant_id NULL, and no spawn for handle 1504.
- CN and private map IDs, cipher state and spawn bytes must retain separate provenance.

## Next implementation
1. Inspect authoritative merchant catalog, item identity, inventory and wallet repository interfaces.
2. Resolve purchase index and sale client slot through server-owned projections.
3. Construct commands from authoritative character, inventory, slot and wallet state.
4. Dispatch through PurchaseInLeaseAsync / SellInLeaseAsync using the TCP connection's existing lane.
5. Implement verified open/result/close wire projections and corresponding tests.
6. Verify DB and isolated TCP flow before Client acceptance.

## Current limitations
- TCP has not been wired to the merchant transaction decoder or command service.
- No new merchant response serializer or arbitrary slot mapping was introduced.
- External registry mutations are not all coordinated through connection lanes.
- Latest isolated 6002 World Probe was at b17cdbb; not rerun for this checkpoint.
- No 6001 restart or deployment; production source remains 3344eb3.
- No private Client merchant acceptance performed.

## Working locations
- Repository: /home/ubuntu/games/fs2-v2-inventory-grant-20261003
- Branch: codex/inventory-grant-20261003
- Source review: /tmp/god2-merchant-tcp-review.txt (temporary; recreate if missing)
- See docs/parity/merchant-cn-live-20261004.md for capture and identity repair audit.
