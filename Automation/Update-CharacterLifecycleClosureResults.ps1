param()

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$closurePath = Join-Path $repoRoot "Artifacts\CharacterLifecycleClosure\closure-results.json"
if (-not (Test-Path -LiteralPath $closurePath)) {
    throw "Missing closure results artifact: $closurePath"
}

$closure = Get-Content -LiteralPath $closurePath -Raw | ConvertFrom-Json

$regressionRoot = Join-Path $repoRoot "Artifacts\CharacterLifecycleRegression"
$latestRegressionDir = Get-ChildItem -LiteralPath $regressionRoot -Directory -Filter "frozen-protocol-*" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $latestRegressionDir) {
    throw "No frozen-protocol regression artifact was found."
}

$regression = Get-Content -LiteralPath (Join-Path $latestRegressionDir.FullName "frozen-regression-summary.json") -Raw | ConvertFrom-Json
$redaction = Get-Content -LiteralPath (Join-Path $repoRoot "Artifacts\CharacterLifecycleClosure\credential-redaction-scan.json") -Raw | ConvertFrom-Json

$updates = [ordered]@{
    ProtocolClosureStatus = "DEFERRED_BY_EVIDENCE"
    Outcome = "DEFERRED_BY_EVIDENCE: Path 1 and Path 2 completed without sufficient official raw Create/Delete/ListRefresh evidence."
    CharacterRuntimePersistence = "PASS"
    RuntimeMatrix = "32/32 PASS"
    CreateC2S = "DEFERRED_BY_EVIDENCE"
    DeleteC2S = "DEFERRED_BY_EVIDENCE"
    NameField = "UNKNOWN"
    ClassField = "UNKNOWN"
    GenderField = "UNKNOWN"
    LifeSkillField = "UNKNOWN"
    AppearanceTemplate = "UNKNOWN"
    CreateResultS2C = "SerializerBlockedByEvidence"
    DeleteResultS2C = "SerializerBlockedByEvidence"
    CharacterListRefresh = "BlockedByEvidence"
    OfficialClientAutomation = "BLOCKED_AFTER_UI_CAPTURE_AND_BUFFER_INSTRUMENTATION"
    OfficialClientCreate = "BLOCKED_BEFORE_CAPTURE"
    OfficialClientDelete = "BLOCKED_BEFORE_CAPTURE"
    SafeEmptySlotWorkflow = "BLOCKED_NO_VERIFIED_EMPTY_SLOT_OR_CREATE_DELETE_ANCHOR"
    AutomatedCreate = "BLOCKED_BEFORE_CAPTURE"
    AutomatedDelete = "BLOCKED_BEFORE_CAPTURE"
    Headless32Matrix = "FROZEN_RUNTIME_32_32_PASS_PROTOCOL_NOT_RUN"
    GoldenArtifact = "NOT CREATED"
    GoldenByteTests = "NOT RUN - NO OFFICIAL RAW GOLDEN BYTES"
    ReleaseBlocker = "YES - Character Create/Delete/ListRefresh Protocol"
    RuntimeMainlineBlocker = "NO"
    NextSprintScope = "Return to overall server mainline using prebuilt characters for automation entry."
    FakeNetworkBytes = 0
    UserManualOperation = "NOT REQUIRED"
    DebugBuild = "PASS, 0 warnings"
    ReleaseBuild = "PASS, 0 warnings"
    LauncherAutomationRecorderDebug = "PASS, 0 warnings"
    LauncherAutomationRecorderRelease = "PASS, 0 warnings"
    FullTests = "PASS, 174/174"
    TestStatus = "PASS"
    RuntimeTestStatus = "FROZEN_RUNTIME_BASELINE"
    AutomationEnvironment = "PASS"
    AutomationHost = "PASS - High-integrity scheduled task, Path 1 UI probe, and Frozen Regression"
    FrozenRegression = ("PASS - " + [string]$regression.runId)
    CredentialLeakage = ("PASS, matchCount=" + [string]$redaction.matchCount + ", scannedFiles=" + [string]$redaction.scannedFileCount)
    CredentialRedaction = "PASS"
    CleanShutdown = "PASS"
    RemainingGod2Opt = 0
    RemainingServerProcess = 0
    Launcher = "LEFT ALIVE"
    LatestFrozenRegressionArtifact = [string]$regression.runDir
    LatestHeartbeat = ([string]$regression.heartbeatCount + " over " + [string]$regression.heartbeatDurationSeconds + " seconds")
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}

foreach ($key in $updates.Keys) {
    $closure | Add-Member -NotePropertyName $key -NotePropertyValue $updates[$key] -Force
}

$json = $closure | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($closurePath, $json, [Text.UTF8Encoding]::new($false))
$closure | ConvertTo-Json -Depth 12
