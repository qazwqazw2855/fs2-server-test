# Map Parity Baseline

- Client build: `god2-opt-6b127086e0c0`
- Official catalog: **144**
- Formal current-build rows: **145**
- Missing official rows: **0**
- Classified extra rows: **1**
- Enabled maps: **144**
- Maps with bounds: **144**

## Authority boundary

Migration 114 contains the 144-map exact-current static catalog.
Map 557790525 is a pre-existing verified Map 19 runtime slice.
The legacy 69-record resource inventory is supplemental and uses a different ID namespace.

## Current blocking gap

- client_map_resources: 158
- client_map_resource_identities: 144
- portal_resource_links: 65
- Provenance status: **PARTIAL**

## File path evidence scope

The exact-client file provenance check compares the 144 formal resource identities against the pinned client file manifest. It does not compare those identities with the official gamedata resource path field.
Nine gamedata paths differ from Migration 114 while the corresponding Migration 114 paths appear in the pinned file manifest. See `docs/parity/map-resource-path-differences.md` for individual rows and hashes. Do not change resource keys or promote runtime maps based on either source alone.

No production database writes were performed.
