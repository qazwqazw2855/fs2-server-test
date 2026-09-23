# Pinned portal staging link cross-check

Source: `database/schema/476_repair_forge_client_map_resource_provenance_drift.sql`.
This offline check compares only pinned staging rows, without querying or changing the production DB.

| Source map resource key | Derived with destination identity | Candidate without destination identity |
|---|---:|---:|
| `client:map/array/array` | 19 | 0 |
| `client:map/island03/island03` | 7 | 8 |
| `client:map/south003/south003` | 5 | 13 |
| `client:map/tong/tong` | 13 | 0 |
| **Total** | **44** | **21** |

All 65 links have matching source map IDs and are disabled. All 44 Derived links have matching destination map IDs. The 21 Candidate links have no destination map identity or destination map ID.
This establishes static identity consistency only. Trigger coordinates and transfer semantics remain unverified; no staging link is promoted to gameplay.

## Supplemental inventory difference

The 68-record supplemental inventory contains these 65 CAN links plus three `officialVisualTransfer` observations. Those three observations are separate evidence records, not missing CAN-link staging rows:

- `official-observed/kunlun-portal-to-secret-room`
- `official-observed/training-room-entry`
- `official-observed/yuanshi-transfer-to-fenghua`

Their exact destination/map identity, coordinates or packet fields are incomplete. Keep them outside CAN-link staging and gameplay promotion.
