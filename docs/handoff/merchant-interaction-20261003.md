# Merchant interaction checks — 2026-10-03

- Network suite: 92 passed, 0 failed, 0 skipped.
- Each NPC interaction has a unique identity; closing and reopening invalidates the old identity.
- Merchant policy checks character, map, NPC ID, spawn ID, handle, client build and distance.
- Merchant binding and distance limit must come from reviewed server content.
- Resolver reads current World presence, NPC and interaction registries.
- Tests cover movement out of range, map change, World departure, close/reopen, NPC removal or replacement, another connection and character replacement.
- Resolver rechecks observed registry snapshots before returning.
- These checks do not reserve state across an asynchronous DB transaction.
- No purchase/sale writer or packet dispatch is wired to this resolver.
- NPC wire evidence and transaction content approval remain separate gates.
- No formal merchant mapping or distance limit was promoted.
- No DB data or privileges changed; production 6001 was not deployed.
- Full six-suite verification has not been rerun for this change; progress.json retains the previous full-run result.
