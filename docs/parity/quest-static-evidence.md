# Supplemental quest static evidence

This offline check covers recovered client presentation data, not Formal DB or verified quest state transitions.

| Source | Rows | SHA-256 |
|---|---:|---|
| `quests/quests.official.json` | 418 | `7064806a04e9b9202b038020ff663b395ec012170f7c7b8dc2c8320e057770d3` |
| `rewards/rewards.official.json` | 0 | `73a51e706f2bc3afbc29b269167ea391491c5fb560de56ec02e0c90ab379b02d` |

All 418 quest IDs are distinct client-presentation entries with objective text. None has a bound start/end NPC, server state-machine ID or reward candidate. Exactly 169 quests have client item target references; item identity and count semantics are a separate deferred item task. No NPC, monster or map target references are populated in these records.

The separate reward inventory contains zero records. Do not treat objective text or client item references as verified server quest triggers or rewards. Quest runtime activation remains blocked pending NPC/dialog, item, state-machine and reward evidence.
