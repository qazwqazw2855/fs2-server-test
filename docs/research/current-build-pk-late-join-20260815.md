# Current-build PK assistance join evidence (2026-08-15)

## Evidence boundary

This recovery uses three independently instrumented current official clients. Wang and Xia were already in PK; Kero selected Xia and entered the active battle. All three defended, and Kero later used the escape control. The raw traces remain immutable under `Artifacts/ClientInstrumentation/OfficialEvidenceLauncher/PKLateJoinAnalysis-20260815`.

| Trace | SHA-256 |
|---|---|
| `trace-wang.snapshot.bin` | `B58C99CBF91EDCD99C3AB75D0F72B298C56CE1928DE0026112F5DE074DA4E901` |
| `trace-xia.snapshot.bin` | `A3E335D1E24B186500A99BD96D2C529AE637AC36CDF8E8ED8C866A0D01C50B20` |
| `trace-kero.snapshot.bin` | `79F10C01D6485A70DDAA597A2263D919A75B31735813DA18EFB8C4746EF510FD` |

## Exact causal chain

1. Kero sequence `4327`, C2S PreEncrypt: `0C006AFE000000F2000000FA` (SHA-256 `9CD2644836F8A09BFD273F8A3194EB84B5CF14F02DCCEF9334CD840D53281669`). The two LE32 fields bind to Kero entity `254` and existing participant Xia entity `242`.
2. Kero sequence `4336`, S2C PostDecrypt: opcode `0x82`, 364 bytes (SHA-256 `10230E3B1BC665A6EE034921E41A1B5B9EBBDA0A3D3AD5519B162C58E3F8BA74`). Its handler-decoded stream creates Wang, Kero, Xia and the battle pet.
3. Wang sequence `81091` and Xia sequence `81136`, S2C PostDecrypt: opcode `0x86`, 676 bytes. Both streams add Kero and synchronize the existing roster. Their frame hashes are `5115534B04DFE59940051968976466642011A970DFF6A80690E589A8BDFFF992` and `5DAF10A148C37583862FBF69A8387556C443E405A711E26462B24B8EB391B1D5`.
4. Kero sequence `15647`, C2S PreEncrypt: `0x35` action code `11`; sequence `15665`, handler-decoded `0x89`, 57 bytes, closes Kero's battle view. The controlled UI action was escape, so current-build action code `11` is promoted to `Flee`.

`0x36` is not promoted to “help”: the exact frame `0C00360100000000000000D7` repeats throughout ordinary battle rounds on all clients. It remains a battle-boundary acknowledgement candidate.

## Implemented boundary

- `OfficialBattleAssistanceJoinWireCodec`: exact current-build C2S `0x6A` layout and lossless encoder.
- `OfficialBattleBatchWireCodec`: current-build S2C `0x82` bootstrap and `0x86` incremental container parsing/round trip for recovered inner record sizes.
- `OfficialBattleCommandWireCodec`: action code `11` is `Flee`.

Runtime battle mutation and production serialization remain fail-closed. A working join response requires authoritative active-battle lookup, participant admission rules, position allocation and construction of every current-build inner roster/state record. The present evidence recovers the transport contract but does not justify guessing those server rules.

## Public-beta separation

`FS2TW-research-evidence-20260815.zip` (SHA-256 `75BA3B07058F83581B0E7DC40B8469ABF413ED5CF4271CBDC497D340D907598D`) is static public-beta evidence, not a current-server capture. Its PK requests are `0x69` (45-byte cursor target) and `0xB8` (5-byte pet target); its derived-stat record is `0x2C` (31 bytes). They are implemented only under `LegacyPublicBetaPkWireCodec` and are not registered in the current-build runtime.

The archive is also used as a hypothesis source for the current build. Promotion requires an independent current-build handler or live trace:

| Record | Public beta | Current build | Promotion |
|---|---:|---:|---|
| C2S `0x35` | 17 | 17 application bytes inside the 20-byte frame | opcode/actor/action prefix only; remaining layout changed |
| S2C `0x1C` | 29 | 45 | identity prefix through local ID promoted; 36-byte tail preserved |
| S2C `0x38` | 2 | 2 | boundary and unread payload promoted |
| S2C `0x83` | 15 | 15 | action/source prefix corroborated; target-mask offsets changed and use current static evidence |
| S2C `0x85` | 2 | 2 | boundary and unread payload promoted |
| S2C `0x86` | 13 | 17 | current boundary/subtype/position only; old value offsets rejected |
| S2C `0x87` | 3 | 3 | packed selector and bits 14/15 promoted |
| S2C `0x88` | 121 | 121 | round plus fourteen HP/MP pairs corroborated |
| S2C `0x89` | 53 | 57 | terminal role only; old settlement offsets rejected |
| S2C `0x2C` | 31 | current canonical minimum 61 | exact-current World/Battle handlers share the local player-panel consumer; it synchronizes self modifiers/elements/physical-magical stats and is not an opponent-stat packet |

The machine-readable promotion ledger is `OfficialBattleCrossVersionEvidence`; current `0x1C/0x38/0x85/0x86/0x87` layouts are implemented by `OfficialBattleRosterControlWireCodec`.
