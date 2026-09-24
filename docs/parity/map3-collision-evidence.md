# Map 3 MBD collision evidence (2026-09-25)

## Source and authority

An operator copied `E:\God2\Data2\map\island03\cityi2\cityi2.mbd` read-only and supplied a ZIP containing the single `cityi2.mbd` file. Its uncompressed size is **434,536 bytes** and SHA-256 is `EBA7AA7AE1DDC0381C7FE30D452FA251345AA9310386948B5EAF080C37D78E59`. Both values match `db/imports/official/maps/God2_exact_current_map_sha256.csv` and the decoded MBD hash pinned in Migration 036. The executable observed in the operator's installation has a different hash from the repository's pinned protocol build, so these bytes alone do not establish that build's live movement or Portal behavior.

## Structural decode

Using the rules in `src/God2.ClassicServer.Runtime/MbdCollisionMap.cs` (little-endian `MBD v1.2`, 21×21 cells per nonzero block pointer, 20-cell overlap stride, four 16-bit cell fields, and blocked if any of the first three fields is `0xFFFF`):

| Measure | Result |
|---|---:|
| Macro dimensions | 12 × 12 |
| Block pointers | 144 |
| Zero pointers / absent blocks | 21 |
| Nonzero unique block pointers | 123 |
| Expanded grid dimensions | 241 × 241 |
| Expanded positions | 58,081 |
| Positions with no source block | 8,400 |
| Assigned positions classified blocked | 15,233 |
| Assigned positions classified walkable | 34,448 |
| Shared-border cell-value conflict comparisons | 3,758 |

The reader resolves conflicting declarations on shared block borders by keeping a blocked cell if either declaration is blocked. Of the 3,758 conflict comparisons, 2,854 were walkable/walkable, 332 blocked/blocked, 87 blocked/walkable and 485 walkable/blocked at comparison time. SHA-256 of the final 58,081-byte walkability mask (one byte per position, `1` for reader-classified walkable, `0` otherwise, iterating `y` then `x`) is `52DD9DE5BFC15AF2BC8BC129811A050C1956E917559A3236F959CD76BA91E86D`.

## Coordinate boundary

Migration 111 assigns Map 3 server bounds `x=0..251,y=0..251` from the 12×12 resource dimensions and the observed 21-unit divisor. The decoded MBD grid uses overlapping 21×21 blocks with a 20-cell stride, yielding **241×241 grid indices**. The repository has not established a complete mapping from server world coordinates or Portal trigger positions to these expanded MBD cell indices. In particular, directly indexing this grid with Portal 1 `(252,397)` or Portal 2's Map 3 destination `(196,139)` is not a verified client collision test. No map bounds, Portal route, or runtime collision rule was changed by this offline analysis.

The next collision gate is to establish and verify the official client's coordinate-to-cell conversion and blocked-cell semantics for the same executable build before using MBD walkability to accept or reject movement.
