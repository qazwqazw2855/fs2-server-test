# God2Con Legacy Offline Evidence

Date: 2026-10-12

## Scope

Offline verification of the pinned legacy God2Con.csvZ
configuration file.

This evidence does not establish endpoint precedence
for the exact-current God2_opt.exe.

## Source

Path:
`/srv/god2-reference/client-legacy/God2/God2Con.csvZ`

Packed length: 904 bytes

Source SHA256:
`8A498103D8A391AC80D49C16572C1140C485F3FCD48A8EC7798CE00CAC9284FE`

Decoded length: 1300 bytes

Decoded SHA256:
`489BCE633ED2267A52730E6905163491AFCC2343DC2510DEC1ECB316F1CD80A1`

Encoding: CP936

Decoded logical lines: 42

## Observed Configuration

Legacy Configure section:

- ExecuteProgram: `god2.exe`
- ProgramArgument: `203.69.83.72 210.244.30.58 6001`
- g_bAuto_Patch: `1`
- g_bAuto_Run: `0`

Legacy FTPServer section:

- `patch.god2.com.tw,6001`
- `210.244.30.58,6001`
- `210.64.9.132,6001`

These are legacy configuration observations,
not confirmed exact-current runtime endpoints.

## Verification

Tool:
`tools/God2.God2ConOffline/`

Final verification step: `90Q-16N`

Results:

- Pinned fixture: PASS
- Packed consumption: 904/904 PASS
- Fail-closed cases: 14/14 PASS
- Format rejection cases: 7/7 PASS
- Total negative cases: 21/21 PASS

Verified format rejection behavior includes:

- Insufficient decoded output
- Back-reference output overrun
- Invalid declared decoded length
- Trailing packed bytes
- Incomplete literal output
- Truncated literal token
- Truncated back-reference token

## Safety Boundaries

EndpointPrecedence: UNKNOWN

ClientExecuted: False

NetworkOperations: False

No production port 6001 changes.

No database changes.

No original client modifications.

No BUY/SELL evidence gate promotion.

## Evidence Limitations

The legacy God2Con.csvZ file is not evidence that
the exact-current God2_opt.exe reads this file.

The relationship among launcher arguments,
LoginServer.csvZ, and God2Con.csvZ remains unverified.

Do not infer endpoint precedence from legacy
configuration values.

Do not modify V2 protocol or runtime behavior
without authoritative evidence and baseline.

## Next Investigation

Continue exact-current endpoint precedence research
using safe, isolated, evidence-based methods.

Keep EndpointPrecedence UNKNOWN until confirmed.
