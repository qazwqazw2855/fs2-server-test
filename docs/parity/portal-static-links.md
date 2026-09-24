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

## 2026-09-24 local CAN bytes cross-check

The operator supplied four unmodified CAN files copied from `E:\\God2\\Data2\\map` while the official client continued running separately from `D:\\god\\珈賜換II`. The executables and these four files have identical hashes across the two directories. The archive entry names omit the installation-specific `Original/` prefix used in the supplemental inventory.

| CAN file | Bytes | SHA-256 | Header records | Staging links |
|---|---:|---|---:|---:|
| `island03/Island03.can` | 1080 | `5A176C2BDE2C1C98E4E853213CB7017D114EB4AC0F7DAE9E0810F91FC806E727` | 16 | 15 |
| `array/array.Can` | 1336 | `FAB5D383BEA882D17952741F49C80BA9DFD683AEB76AA4F844AB862316994B0B` | 20 | 19 |
| `tong/TONG.Can` | 942 | `7D97EFBA6DC2728FF8FF190B5BEDC0B29339E8E64ADEEAA17591350B2A868144` | 14 | 13 |
| `south003/South003.can` | 1302 | `ADD0EA1A01BD3EA57ED7CFC075E2E5D3EA14E8BD829776CE6508A49E695804EE` | 19 | 18 |

All four file hashes match `db/imports/official/maps/God2_exact_current_map_sha256.csv`. For every one of the **65** staging links, the byte offset `12 + 64 * can_record_index`, one-byte record type, NUL-terminated resource path and the inventory's normalized destination resource candidate match the supplied CAN bytes; **0 mismatches**. The matching migration rows also agree with the supplemental inventory on source path, record index and type. The four additional header records identify source resources and are not staging links. This proves provenance of these four CAN file bytes and the 65 static link entries; it does not establish any transfer trigger, click/walk action, source/destination coordinates or server opcode.

The observed `God2_opt.exe` SHA-256 in both local directories is `6F2639A0A7AD25053D0364108147173EB68BD04F57E6942491F42633F40052BC`. The repository's protocol build `god2-opt-6b127086e0c0` is tied to executable SHA-256 `6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B`. The executable mismatch prevents treating these local files as executable-specific proof of the latter build's trigger behavior. Destination asset bytes have not been checked for all links; `exactClientFileProvenanceComplete` and full Portal promotion remain **false**, and all staging links stay disabled.

### Installed destination asset presence, 2026-09-24

A read-only PowerShell enumeration used the 65 CAN record paths (record indices 1 through count minus 1) to check each adjacent map asset both as the literal `.mdt/.hmd` and with a `Z` suffix. The two local directories `E:\\God2` and `D:\\god\\珈賜換II` each returned **65 checked, 60 existing, 5 missing**. The same five exact relative paths are absent in both:

| CAN source | Record index | Missing adjacent asset |
|---|---:|---|
| `Island03.can` | 10 | `Data2/map/island03/indoor/god03.hmd[Z]` |
| `TONG.Can` | 8 | `Data2/map/tong/reborn/reborn.mdt[Z]` |
| `South003.can` | 9 | `Data2/map/south003/indoor/god01.hmd[Z]` |
| `South003.can` | 11 | `Data2/map/south003/indoor/god02.hmd[Z]` |
| `South003.can` | 14 | `Data2/map/south003/indoor/god03.hmd[Z]` |

The other 60 files were checked for presence only, not compared byte-for-byte with the pinned manifest. The five missing exact paths do not prove the content cannot be supplied by another install, patch, archive or client-side resolver. No link is promoted, deleted or rewritten based on presence alone; the client executable build difference and trigger evidence gap remain unresolved.
