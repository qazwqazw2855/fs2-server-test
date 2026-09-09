# God2 Ultimate release tooling

`Build-UltimateRelease.ps1` assembles and independently verifies the private
God2 Semantic Recovery Engine v1.3.0 Ultimate release. It does not build native
code, rename an older binary, infer PASS from filenames, or publish when a
required local implementation gate is missing.

## Formal build

Run all final validation commands against one unchanged source tree and exact
v1.3 EXE. Convert raw reports with
`assets/test-run/Convert-UltimatePortableEvidence.ps1`, then seal the converted
tree with the generator documented in `assets/test-run/README.md`. Across every
`-TestReportRoot`, exactly one `ultimate-test-run-manifest.json` must exist.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\God2.PacketCapture\ultimate\Build-UltimateRelease.ps1 `
  -ExecutablePath .\Artifacts\release\God2SemanticRecoveryEngine.exe `
  -TestReportRoot .\Artifacts\UltimateFinalValidation `
  -OutputRoot .\Artifacts\Ultimate
```

The manifest binds the exact EXE and PE version, source and evidence inventory,
commands and run IDs, globally unique test IDs, TRX execution identities, and
all first-party native analyzer/source bindings. Gate names with numeric test
counts are resolved by their stable semantic prefix; a missing or ambiguous
gate is rejected.

Portable evidence conversion canonicalizes only TRX storage, deployment and
machine/user metadata. TestRun IDs, test IDs, execution IDs, outcomes,
durations and counters are preserved. Remaining local identities, absolute
paths, invalid UTF-8, mojibake, control/format characters or unparseable JSON
are rejected before sealing.

`-SchemaRoot` is compatibility-only. Supplied schemas must be byte-identical to
registered built-ins. Unregistered, permissive or modified schemas are never
copied.

## Recovery bundle and importer evidence

The test-run manifest must designate exactly one `RecoveryBundleZip`. The
builder independently verifies safe case-insensitive unique paths, full
inventory coverage, sizes and SHA-256, required artifacts, parseable JSON/JSONL,
authority metadata, and the declared client-build identity.

`ImporterOfflineVerifierContract` is a 70-test implementation gate. Its two
named anchor tests prove that the importer executed 13 loaded compiled server
contracts, 59 additive/apply/idempotent migration contracts and deterministic
semantic replay (one executed, zero rejected), while production database
connection/mutation and network mutation all remained false. This local
verification proves importer-code readiness; it does not claim a real importer
run, exact official-client process binding, server restoration, database
restoration or a production closed loop.

`ImporterFormalIdentityFailClosed` is separate: the real recovery ZIP is
expected to stop with exit code 4 while client identity remains untrusted. Its
machine-readable result must be `RECOVERY_BUNDLE_IMPORTER_BLOCKED` /
`build_identity.validation_status_not_trusted`, with no output directory and
zero production database connection or mutation. An expected block is not a
fake importer PASS.

An optional `-ServerDbImporterRun` must remain inside its candidate root. Its
manifest must cover every artifact and bind the exact recovery ZIP, package ID
and native manifest hash. A missing real run remains an explicit external gate
and does not negate completed local implementation.

## GPU and RTX 5070 evidence

The deterministic GPU pipeline contains exactly 17 rows:

1. TraceFeatureExtraction
2. CrossSessionCorrelation
3. ParserFieldPatternMatching
4. FunctionClustering
5. ObjectClustering
6. ClassLayoutScoring
7. RegistryScoring
8. HeapGraphSimilarity
9. ValueFlowAggregation
10. TaintGraphBatch
11. SemanticEdgeScoring
12. ApproximateNearestNeighbor
13. SequenceMining
14. FsmScoring
15. FormulaBatchEvaluation
16. ReplayStateComparison
17. AiInference

All 16 non-AI rows have operation-specific GPU implementations. Local contract
and benchmark evidence must prove:

- `ImplementedOperationCount=16` and
  `StructuredImplementationBlockedCount=0`;
- operation-specific GPU execution for all 16 rows, positive record and kernel
  counts, bounded concurrent streams/batches, and zero drop or mismatch;
- exact candidate/authoritative/CPU-only digest equality and preserved CPU
  authority;
- aggregate `GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED`;
- Auto remains fail-closed to CPU unless fresh same-input full-path calibration
  proves the required benefit margin.

`AiInference` may truthfully remain `EvidenceBlockedModelUnavailable` when no
verified provider/model is supplied. That row performs no provider inference or
GPU work and retains HYPOTHESIS authority. This optional evidence limitation
does not block implementation completeness because the GPU ML pipeline and its
fail-closed contract are implemented.

`-ValidatedRtx5070ResultPath` and
`-ValidatedRtx5070AttestationPath` must be supplied together. Fresh physical RTX
5070 promotion additionally requires Windows 11, the exact release EXE, full
archive inventory, a v2 external attestation, the exact 17-row matrix hash and
all outer OS/device/package gates. A legacy v1.2 result is retained only as
provenance and stays `PENDING_FRESH_ULTIMATE_RTX5070_RUN`.

With every local code, test, analysis, recovery-bundle and GPU implementation
gate passing, the correct engineering status is:

`ULTIMATE_DEEP_RECOVERY_IMPLEMENTATION_COMPLETE_WITH_EXTERNAL_LIVE_GATE_PENDING`

This status does not promote the still-pending official-client live session,
fresh RTX 5070 run, exact official client identity, or real importer integration.

## DLL enhanced capture UI

The compact GUI permanently shows a three-language `DLL 增強擷取` status row.
It reports idle, attaching, ready, blocked, draining and stopped lifecycle
states. Enhanced capture remains automatically enabled by the two-action master
workflow; the row is status/diagnostic output, not a third button or required
user action. `DllEnhancedCaptureAcceptance` may pass only with one exact
`EnhancedCaptureAcceptanceJson` bound to the release EXE, byte-identical embedded
RCDATA 201/202 payloads, all 25 domains (21 candidate-only and fail-closed), and
the five GUI visibility/auto-enable/25-domain smoke keys.

## Live probe and AI gates

The embedded probe implements bounded deep-probe candidate discovery v2. It
scans executable code only around verified parser, serializer and handler seeds,
emits exact candidate signatures and provenance, and never logs sensitive
values. Discovery is not activation: every candidate remains fail-closed until
an exact official build, calling convention, lifetime, reentrancy and thread
context are verified in a real live session.

The native `--internal-ultimate-live-validation --package-root` entrypoint and
portable CMD are tested contracts. UAC cancellation or a non-admin diagnostic
run produces an explicit EvidenceBlocked result. Entrypoint availability alone
cannot become live evidence. The formal release remains
`EVIDENCE_BLOCKED_EXTERNAL_LIVE_GATE_PENDING` until an independently attested
exact-target result is supplied.

Model name/version/license/hash alone never proves inference. A model PASS
requires actual provider execution and deterministic/replay validation.

## Verify and parser self-test

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\God2.PacketCapture\ultimate\Build-UltimateRelease.ps1 `
  -ValidateOnly -OutputRoot .\Artifacts\Ultimate

powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\God2.PacketCapture\ultimate\Build-UltimateRelease.ps1 `
  -ParserSelfTest
```

Validation checks JSON/CSV coverage, hashes and sizes, exact PE identity,
evidence-chain fields, schema registry coverage, generated Markdown structure,
portable ZIP inventory and the exact embedded executable. It performs no
writes. Parser tests include valid 16/0/1 RTX evidence, legacy pending scope,
blocked/spoofed GPU rows, ambiguous gate names, traversal, unlisted files,
strict attestation fields, portable identity redaction, TRX uniqueness, native
analysis bindings and wrong executable binding.

`Freeze-PublicBaseline.ps1` remains the separate immutable v1.2.0 RC baseline
tool. It validates but never overwrites that public baseline.
