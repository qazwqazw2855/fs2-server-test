# Portal Parity Baseline

- Evidence-backed expected routes: **5**
- Formal routes: **3**
- Missing evidence-backed routes: **2**
- Supplemental CAN-link candidates: **68**

## Formal routes

| Portal | Route | Authority | Formal status |
|---:|---|---|---|
| 1 | Area 4 Map 3 -> Map 19 | CURRENT_BUILD_RUNTIME_CAPTURE | MISSING |
| 2 | Area 4 Map 19 -> Map 3 | CURRENT_BUILD_RUNTIME_CAPTURE | MISSING |
| 3 | Area 2 Map 0 -> Map 43 | CURRENT_BUILD_RUNTIME_CAPTURE | PRESENT |
| 4 | Area 2 Map 43 -> Map 0 | CURRENT_BUILD_RUNTIME_CAPTURE | PRESENT |
| 170015007 | Area 15 Map 0 -> Map 7 | TW_LIVE_CLIENT_V2_ACCEPTANCE | PRESENT |

## Regression

- Status: **BLOCKED**
- Missing IDs: `1, 2`
- Cause: `FORGE_SEED_LEDGER_DRIFT`

The 68 legacy CAN-link candidates remain TransferTriggerUnverified and cannot be imported into gameplay.

No production database writes were performed.
