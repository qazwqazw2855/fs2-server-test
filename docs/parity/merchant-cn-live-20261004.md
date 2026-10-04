# CN official merchant observation — 2026-10-04

## Source
- Authority: CN_OFFICIAL, supplemental evidence.
- Server: 夢迴朝歌.
- Location reported by operator: 楓華雜貨店／楓華雜貨舖老闆.
- Client SHA256: 6f2639a0a7ad25053d0364108147173eb68bd04f57e6942491f42633f40052bc.
- Capture SHA256: 0c3bd367fec249e8cc8abb83fe781b434700e664f7b3daa7be104a80dfe5f647.
- Original actions.txt SHA256: 7e8a75072bc52ae958b9ccec3f26261858cb3b4c5f0290ecf8cd1b01ac33d753.
- Later operator clarification: initially zero items; two purchases of one each, then one sale of two.
- Original action labels are not exact click timestamps.

## Decoded transactions
- Three requests and three responses passed checksum using captured runtime cipher state.
- Request opcode 0x38, 12 bytes; purchase response 0x3B, 57 bytes; sale response 0x41, 25 bytes.
- NPC handle 1504; client item ID 6906; purchase index 13; sale slot value 12.
- Purchases at 14:15:01 and 14:15:40, quantity 1 each.
- Sale at 14:16:40, quantity 2.
- Observed balance values: 593627533 → 593627473 → 593627485.
- Operator reports purchase cost 60 each and total sale income 12.
- Cipher state is capture-specific; no universal key or cross-build compatibility claimed.

## Item identity and database discrepancy
- Official JSON entry client:item/pfd/6906: 小腿肉.
- Source clientItemId 6906, clientDisplayId 58006; these are distinct fields.
- Source price candidates: systemSell 60, systemRecycle 6; stackLimit unknown.
- Formal row: item_id 2117880098, code item_6906, client_item_id 2117880098.
- Formal buy_price NULL, sell_price 60; maximum_stack NULL.
- Legacy row has BaseBuyPrice 0, BaseSellPrice 0, SellPrice 60.
- Migration 419 inserts legacy Id into both identity columns and copies legacy pricing.
- Its audit table is absent in the inspected database; actual row creation provenance is unresolved.

## Boundaries
- Shop listings and prices may be configured independently by server operators.
- No formal NPC spawn for handle 1504 or matching merchant listing was found.
- No canonical NPC identity or authority inventory slot mapping has been approved.
- Initial audit made no DB changes; the subsequent identity-only repair is recorded below.
- Existing full-suite verification remains 409 passed; this audit adds no test coverage.

## Identity repair verification
- Migration 478 applied to god2-runtime-db-test.
- Checksum: 38072cfbb3425c4a2aecfe65f74bd5bb7527b218a11eb5b9a778950af1174cf1.
- Canonical item_id remains 2117880098; client_item_id corrected to 6906.
- Rollback trial restored the previous identity successfully before application.
- Application registered migration 478 in the same transaction as the identity update.
- Prices remain buy_price NULL / sell_price 60; pricing semantics remain unresolved.
- Stack policy, merchant listings and NPC evidence gates were not changed.
- No 6001 restart/deployment or Client acceptance.
- Full 409-test suite has not been rerun after this DB repair.
