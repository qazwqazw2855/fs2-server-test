# Opcode Documentation

## Verified / Recovered Candidates

| Packet | Family | Length | Opcode Candidate | Confidence | Status |
| --- | --- | ---: | --- | --- | --- |
| world-heartbeat-05007ACCEC | Heartbeat | 5 | 7ACC | Verified | Verified raw heartbeat |
| world-heartbeat-05003D09A5 | Heartbeat | 5 | 3D09 | Recovered | Alternate idle heartbeat candidate |
| world-movement-client-10-byte-family | Movement | 10 | 80BA | Recovered | Movement family recovered, fields unknown |

## Evidence-Only Candidates

| Packet | Family | Length | Opcode Candidate | Confidence | Status |
| --- | --- | ---: | --- | --- | --- |
| world-npc-interaction-client-8-byte-candidate | NPC | 8 | 7758 | Inferred | Raw candidate only |
| world-merchant-interaction-client-8-byte-candidate | Merchant | 8 | 77B5 | Inferred | Visual verified, raw semantic fields unknown |
| world-logout-client-5-byte-candidate | Logout | 5 | AC9D | Inferred | Raw candidate only |

## Rule

Opcode candidates remain candidates until raw evidence proves stable semantic meaning. Do not build gameplay logic on an evidence-only opcode.
