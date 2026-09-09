# Ultimate test-run manifest generator

Before sealing a run, canonicalize the raw report tree into a separate portable
tree:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\God2.PacketCapture\ultimate\assets\test-run\Convert-UltimatePortableEvidence.ps1 `
  -InputRoot .\Artifacts\UltimateFinalValidation.raw `
  -OutputRoot .\Artifacts\UltimateFinalValidation
```

The converter never edits the raw tree or an existing output tree. It rewrites
`.trx` path metadata: `TestRun storage`, deployment/result-file paths and
path/code-base attributes become absent or filename-relative. It also replaces
the machine-local TestRun name/user, deployment-root and result computer-name
fields with deterministic `PORTABLE_*` tokens. Before and after
canonicalization it hashes an invariant containing TestRun ID, every test and
execution ID, outcome, duration, all test definitions and every result counter;
any semantic change is rejected. Non-TRX files are copied byte-for-byte.

After conversion, the entire portable tree is strict-UTF-8 scanned. Windows
drive paths, UNC paths, local `file:///` URIs and local `/home`, `/Users`,
`/private`, `/var/folders` or `/tmp` paths are rejected. Unicode replacement
and known mojibake marker characters are also rejected. The converter does not
silently redact JSON, analyzer output, logs or test output; their producers must
emit filename/relative/tokenized path fields. This prevents a post-validation
redaction from invalidating evidence hashes.

`New-UltimateTestRunManifest.ps1` then seals that portable validation run before
the formal release builder is invoked. It computes the exact v1.3 EXE SHA-256,
a source inventory digest and the full report-tree digest. It refuses an
unexpected exit code, absolute/output paths, reparse points, duplicate
evidence, omitted report files, residual absolute local paths and overwrite of
an existing manifest. Ordinary commands declare matching `ExitCode: 0` and
`ExpectedExitCode: 0`; the formal importer identity probe declares matching
`4` and `4`. The generated manifest JSON is path-scanned before it is written, so
declaration commands must use repository-relative commands and
`WorkingDirectory: "."`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\tools\God2.PacketCapture\ultimate\assets\test-run\New-UltimateTestRunManifest.ps1 `
  -ExecutablePath .\Artifacts\release\God2SemanticRecoveryEngine.exe `
  -RepositoryRoot . `
  -ReportRoot .\Artifacts\UltimateFinalValidation `
  -DeclarationPath .\Artifacts\ultimate-test-run-declaration.json
```

Keep the declaration outside the portable `ReportRoot`; the generator rejects an in-tree
declaration so every report-root file other than the generated manifest is
sealed as evidence.

The declaration is a UTF-8 JSON object with schema
`god2-ultimate-test-run-declaration-v1` and these fields:

```json
{
  "SchemaVersion": "god2-ultimate-test-run-declaration-v1",
  "RunId": "final-validation-20260808",
  "GeneratedAtUtc": "2026-08-08T12:00:00Z",
  "SourceFiles": ["tools/God2.PacketCapture/Main.cpp"],
  "Commands": [{
    "CommandId": "native-x64-release",
    "Command": "record the exact non-interactive command here",
    "WorkingDirectory": ".",
    "StartedAtUtc": "2026-08-08T11:59:00Z",
    "CompletedAtUtc": "2026-08-08T12:00:00Z",
    "ExitCode": 0,
    "ExpectedExitCode": 0
  }],
  "Evidence": [{
    "EvidenceId": "core-json",
    "RelativePath": "core-selftest.json",
    "Kind": "CoreSelfTestJson",
    "CommandId": "native-x64-release"
  }],
  "Tests": [{
    "TestId": "PacketCaptureCore265:0001",
    "GateName": "PacketCaptureCore265",
    "Status": "PASS",
    "CommandId": "native-x64-release",
    "EvidenceId": "core-json"
  }],
  "Gates": [{
    "Name": "PacketCaptureCore265",
    "Status": "PASS",
    "TestIds": ["PacketCaptureCore265:0001"],
    "EvidenceIds": ["core-json"]
  }]
}
```

The declaration keeps offline importer implementation proof and the formal
real-ZIP probe separate:

- `ImporterOfflineVerifierContract` binds exactly the 70 tests from
  `God2.RecoveredDatabaseImporter.Tests`, including
  `ImportAsync_OfflineCompiledSchemaMigrationAndReplayProofs_AreExecuted` and
  `ImportAsync_IntegrationContract_ProvesZeroProductionDatabaseAndNetworkMutation`.
  This proves compiled contract discovery, migration/replay execution and zero
  production database/network mutation. It is not a real importer live PASS.
- `ImporterFormalIdentityFailClosed` binds exactly the TestId
  `importer-formal:expected-exit4-output-directory-absent` and the sole
  `ImporterFormalIdentityFailClosedJson` file
  `formal-zip-blocked-output.json`. Its producing command has actual and
  expected exit code 4. The JSON must report
  `build_identity.validation_status_not_trusted` and zero production database
  connection/mutation; the TestId records that no output directory was created.
- `DllEnhancedCaptureAcceptance` binds exactly one successful
  `EnhancedCaptureAcceptanceJson` named `enhanced-capture-acceptance.json`.
  The release builder independently requires final PASS, exact release-EXE and
  embedded RCDATA 201/202 binding, all 25 domains with 21 candidate-only
  domains, the permanent three-language auto-enabled DLL status-row smoke
  contract, and zero failed or pending checks.

TRX TestIds must be `trx:<runId>:<testId>:<executionId>` in lowercase. Native
analysis evidence additionally declares `AnalyzerId`, `Architecture` and
`SourceRelativePath`; its TestId must be `analyzer:<AnalyzerId>`. The builder
independently parses both formats and rejects duplicate TRX executions or an
arbitrary XML file masquerading as analyzer output. The historical
`NativeStaticAnalysis` gate name now requires exactly 19 distinct first-party
translation units: every 15 x64 packet-capture `.cpp` file (including
`UltimateRecovery.cpp`) and all four x86 instrumentation `.cpp` files. Each
source must appear in the sealed source inventory and bind exactly one analyzer
result with the expected architecture; duplicate-source projections and any
new unregistered first-party translation unit are rejected.
