# God2 Official Parity Baseline

Progress source: `2026-09-23T00:55:38+08:00`

Overall: **78%**

## Evidence-set boundary

- Runtime mappings: **91**; blocked: **91**; integrated: **0**.
- Evidence routes: **97**; candidate gate count: **97**.
- Unknown-new: **17**; actionable: **13**.
- Classification build id: `unknown`.

> The 91 runtime mappings, 97 evidence routes and 17 unknown-new classifications are separate sets and must not be merged.

## System gap registry

| Priority | System | Data | Logic | Wire | Real client | Next gate |
|---:|---|---|---|---|---|---|
| 0 | Character List / Select | PARTIAL | VERIFIED | VERIFIED | VERIFIED | Capture lifecycle variants only when selected as the active gap. |
| 0 | Login | NOT_APPLICABLE | VERIFIED | VERIFIED | VERIFIED | Preserve regression coverage. |
| 0 | Movement | PARTIAL | VERIFIED | VERIFIED | VERIFIED | Bind validation to promoted collision evidence. |
| 0 | World Entry | PARTIAL | VERIFIED | VERIFIED | VERIFIED | Run isolated original-client World Entry acceptance across multiple Area/Map identities; keep World Login identity coverage separate from Portal wire projection evidence. |
| 1 | Map Content / Hierarchy / Collision | PARTIAL | PARTIAL | PARTIAL | PARTIAL | Validate the restored staging rows against exact-current client files, then diff hierarchy, collision and portal references. |
| 1 | Monster / Spawn / AI / Drop | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Complete template, spawn and drop provenance diff. |
| 1 | NPC Spawn / Dialog | PARTIAL | PARTIAL | PARTIAL | PARTIAL | Diff NPC templates, spawns, map bindings and dialogs. |
| 1 | Portal / Map Transition | PARTIAL | VERIFIED | VERIFIED | PARTIAL | Validate Portal destinations and staging links only from exact-current or live-client evidence; do not infer PositionMode, PreludeState, MapTransitionFirst, or additional destination support from World Login coverage. |
| 2 | Item / Inventory / Equipment | CANDIDATE | PARTIAL | BLOCKED | BLOCKED | Complete identity and bootstrap baseline before mutation. |
| 2 | Merchant / Shop | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Promote catalog, then verify isolated purchase and sale. |
| 2 | Player Replication / AOI / Despawn | PARTIAL | PARTIAL | BLOCKED | BLOCKED | Run two-client AOI after World Content is stable. |
| 3 | Quest | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Promote catalog and NPC bindings first. |
| 3 | Skill / Combat / Status | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Capture a controlled battle closed loop. |
| 4 | Pet / Mount / Immortal | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Separate official identity from supplemental growth evidence. |
| 4 | Party / Guild / Chat / Trade / Mail / Social | CANDIDATE | BLOCKED | BLOCKED | BLOCKED | Defer until core world and character mutation are stable. |

## Proven scope and gaps

### Character List / Select

- Proven scope: List, server selection, ownership reservation and World transfer.
- Gap: Create, delete and rename live-client acceptance are not promoted.
- Authorities: `TW_LIVE_CLIENT`, `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`

### Login

- Proven scope: 19/19/6 handshake, 208-byte LoginRequest and authenticated session path.
- Gap: Failure variants and broader account-policy parity remain outside scope.
- Authorities: `TW_LIVE_CLIENT`, `AUTOMATED_ACCEPTANCE`

### Movement

- Proven scope: Sequence rejection, optional bounds and persistence.
- Gap: Collision rules are incomplete for the full map catalog.
- Authorities: `TW_LIVE_CLIENT`, `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`

### World Entry

- Proven scope: 10/10 World handshake and official 1772-byte bootstrap are verified. Formal World Login map identity/bounds coverage is 144/144 enabled official maps. Isolated 6002 LoginProbe verified map 170015007 identity/bounds validation and successful InWorld entry using the 1772-byte bootstrap without Portal wire projection.
- Gap: 144/144 is Formal World Login identity/bounds coverage, not proof that the original Windows Client visually loads all 144 maps. The official 1772-byte bootstrap does not provide verified per-map World Entry projection semantics.
- Authorities: `TW_LIVE_CLIENT`, `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`

### Map Content / Hierarchy / Collision

- Proven scope: Migration 114 exact-current static catalog is complete at 144/144 with zero missing formal rows. Migration 476 restored 158 client map resources, 144 resource identities and 65 portal resource links as disabled pinned staging provenance. Map 557790525 remains a separately classified verified runtime slice.
- Gap: Exact-current client file hashes and navigation provenance remain incomplete. The restored staging rows are disabled and are not Taiwan live gameplay proof; hierarchy semantics and collision provenance remain unresolved.
- Authorities: `EXACT_CURRENT_STATIC`, `FORMAL_DB`, `LEGACY_SERVER`

### Monster / Spawn / AI / Drop

- Proven scope: Template and evidence catalogs exist.
- Gap: Spawn, AI, combat, drop and replication are unverified.
- Authorities: `EXACT_CURRENT_STATIC`, `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`

### NPC Spawn / Dialog

- Proven scope: Pinned NPC 3793 spawn, dialog, selector, release and reopen.
- Gap: Map 170015000 returned zero NPC snapshot rows; full NPC coverage is absent.
- Authorities: `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`, `RUNTIME_CAPTURE`

### Portal / Map Transition

- Proven scope: Migration 475 restored all 5 evidence-backed Formal routes and 5 portal evidence rows. Migration 476 restored 65 disabled staging portal resource links. OfficialPortalWireCodec currently has 6 evidence-backed destination identities; this Portal wire evidence is independent of the 144/144 World Login Formal identity/bounds coverage.
- Gap: Portal wire projection remains evidence-gated to 6 verified destination identities. This must not be generalized to 144/144 from World Login coverage. The 65 staging links still lack complete exact-client provenance and verified triggers; 68 legacy CAN-link candidates remain supplemental.
- Authorities: `TW_LIVE_CLIENT`, `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`, `RUNTIME_CAPTURE`, `LEGACY_SERVER`

### Item / Inventory / Equipment

- Proven scope: Read-only inventory, stack rules and selected static permissions exist.
- Gap: Mutation, equipment effects, economy and client update wire are blocked.
- Authorities: `EXACT_CURRENT_STATIC`, `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`

### Merchant / Shop

- Proven scope: Capture-specific open, purchase, sale and close candidates exist.
- Gap: Catalog, identity, price, wallet and inventory mutation gates are incomplete.
- Authorities: `RUNTIME_CAPTURE`, `FORMAL_DB`, `CN_SUPPLEMENTAL`

### Player Replication / AOI / Despawn

- Proven scope: Cleanup, presence foundations and ownership behavior exist.
- Gap: Official entity mapping and two-client acceptance are missing.
- Authorities: `AUTOMATED_ACCEPTANCE`, `FORMAL_DB`

### Quest

- Proven scope: Catalog and interaction candidates exist.
- Gap: Bindings, objectives, transitions, rewards and mutation are not promoted.
- Authorities: `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`, `RUNTIME_CAPTURE`

### Skill / Combat / Status

- Proven scope: Static identities and architecture foundations exist.
- Gap: No promoted battle sequence, typed HP/MP mutation, formula or production serializer.
- Authorities: `EXACT_CURRENT_STATIC`, `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`, `RUNTIME_CAPTURE`

### Pet / Mount / Immortal

- Proven scope: Identity, presentation and selected growth evidence exist.
- Gap: Runtime state, progression, battle, equipment and wire are unverified.
- Authorities: `EXACT_CURRENT_STATIC`, `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`, `THIRD_PARTY`

### Party / Guild / Chat / Trade / Mail / Social

- Proven scope: Historical and candidate evidence exists.
- Gap: Current wire and persistent multiplayer behavior are not promoted.
- Authorities: `FORMAL_DB`, `LEGACY_SERVER`, `CN_SUPPLEMENTAL`, `RUNTIME_CAPTURE`
