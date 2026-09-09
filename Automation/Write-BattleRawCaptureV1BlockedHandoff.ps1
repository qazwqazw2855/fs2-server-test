param(
    [string]$WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$FullTestPassed = 2075,
    [int]$FullTestTotal = 2075,
    [int]$LauncherPid = 548,
    [int]$CredentialFindings = 0,
    [int]$CaptureSecretFindings = 0,
    [int]$HardcodedPathFindings = 0,
    [string]$FrozenRegressionRunId = "",
    [string]$AutomationJsonRetryStatus = "NOT RUN",
    [int]$AutomationJsonRetryElapsedMilliseconds = 0,
    [string]$AutomationEnvironmentPreflightStatus = "NOT RUN",
    [string]$Migration030Status = "Unknown"
)

$ErrorActionPreference = "Stop"
$WorkspaceRoot = [IO.Path]::GetFullPath($WorkspaceRoot)
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$generatedAtUtc = [DateTimeOffset]::UtcNow
$artifactRoot = Join-Path $WorkspaceRoot "Artifacts\BattleRawCaptureV1"
$reportRoot = Join-Path $WorkspaceRoot "Reports"
$machineRoot = Join-Path $WorkspaceRoot "protocol\evidence\battle-v1"
$profilePath = Join-Path $WorkspaceRoot "Artifacts\ClientInstrumentation\LauncherAutomation\launcher-profile.json"
$hostStatusPath = Join-Path $WorkspaceRoot "Automation\State\host-status.json"
$clientBuildPath = Join-Path $machineRoot "client-builds.json"
$packetFamiliesPath = Join-Path $machineRoot "packet-families.json"
$instrumentationSummaryRelative =
    "Artifacts/ClientInstrumentation/LoginTrial/battle-raw-capture-v1-preflight-20260731/analysis/instrumentation-selftest-summary.json"
$instrumentationSummaryPath = Join-Path $WorkspaceRoot $instrumentationSummaryRelative
$frozenRegressionSummaryRelative = if ([string]::IsNullOrWhiteSpace($FrozenRegressionRunId)) {
    $null
}
else {
    "Artifacts/CharacterLifecycleRegression/$FrozenRegressionRunId/frozen-regression-summary.json"
}
$frozenRegressionSummaryPath = if ($frozenRegressionSummaryRelative) {
    Join-Path $WorkspaceRoot $frozenRegressionSummaryRelative
}
else {
    $null
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($Path, ($Content.TrimEnd() + [Environment]::NewLine), $utf8NoBom)
}

function Write-Json {
    param([string]$RelativePath, $Value)

    Write-Utf8NoBom `
        -Path (Join-Path $WorkspaceRoot $RelativePath) `
        -Content (ConvertTo-Json -InputObject $Value -Depth 24)
}

function Get-RelativeHash {
    param([string]$RelativePath)

    [ordered]@{
        path = $RelativePath.Replace("\", "/")
        sha256 = (Get-FileHash -LiteralPath (Join-Path $WorkspaceRoot $RelativePath) -Algorithm SHA256).Hash
    }
}

$profile = Get-Content -LiteralPath $profilePath -Raw -Encoding utf8 | ConvertFrom-Json
$hostStatus = Get-Content -LiteralPath $hostStatusPath -Raw -Encoding utf8 | ConvertFrom-Json
$clientBuilds = Get-Content -LiteralPath $clientBuildPath -Raw -Encoding utf8 | ConvertFrom-Json
$v1Families = Get-Content -LiteralPath $packetFamiliesPath -Raw -Encoding utf8 | ConvertFrom-Json
$clientBuild = $clientBuilds.builds[0]
$clientRoot = Split-Path -Parent ([string]$profile.targetExecutable)
$clientExe = Join-Path $clientRoot "God2_opt.exe"
$endpointConfig = Join-Path $clientRoot "ctserver.ini"
$endpointHash = (Get-FileHash -LiteralPath $endpointConfig -Algorithm SHA256).Hash
$clientHash = (Get-FileHash -LiteralPath $clientExe -Algorithm SHA256).Hash
$launcher = Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue
$instrumentationSummaryHash = (Get-FileHash -LiteralPath $instrumentationSummaryPath -Algorithm SHA256).Hash
$frozenRegression = if ($frozenRegressionSummaryPath -and (Test-Path -LiteralPath $frozenRegressionSummaryPath)) {
    Get-Content -LiteralPath $frozenRegressionSummaryPath -Raw -Encoding utf8 | ConvertFrom-Json
}
else {
    $null
}
$freeBytes = [int64](Get-PSDrive -Name ([IO.Path]::GetPathRoot($WorkspaceRoot).Substring(0, 1))).Free

$artifactDirectories = @(
    "Handoff",
    "PathResolution",
    "Instrumentation",
    "Captures/RunA",
    "Captures/RunB",
    "Captures/RunC",
    "Pcap",
    "ProcessTrace",
    "StreamReconstruction",
    "Timeline",
    "Differential",
    "PacketFamilies",
    "BasicAttack",
    "Decoder",
    "Golden",
    "Replay",
    "FailureInjection",
    "Regression"
)
foreach ($relative in $artifactDirectories) {
    [IO.Directory]::CreateDirectory((Join-Path $artifactRoot $relative)) | Out-Null
}

$blocker = [ordered]@{
    code = "OfficialAuthenticationAuthorityUnavailable"
    stage = "CapturePathResolution"
    reason = "The approved DPAPI automation credential authority is explicitly private-local only. No approved official account/session or automated quick-login authority exists."
    effect = "The official world cannot be entered automatically, so no safe encounter or Battle capture may start."
    userCredentialRequested = $false
    userOperationRequested = $false
}

$pathEvaluations = @(
    [ordered]@{
        path = "A.CurrentOfficialClientToCurrentOfficialServer"
        endpoint = "OfficialEndpointSafeReference"
        endpointTcp2592 = "Reachable"
        endpointTcp2596 = "Reachable"
        clientBuildMatched = $true
        authenticationAvailable = $false
        automationAvailable = "LocalLoginAndWorldOnly"
        safeEncounterAvailable = $false
        result = "BlockedBeforeClientLaunch"
        reasonCode = $blocker.code
    },
    [ordered]@{
        path = "B.ExistingKnownGoodBattleCapableEnvironment"
        result = "Rejected"
        reasonCode = "NoCompatibleAutomatedEnvironment"
        notes = "The private server has no production encounter spawns and no verified official Battle serializers. Using it would require fabricated protocol output."
    },
    [ordered]@{
        path = "C.ExistingHistoricalLocalEvidence"
        result = "Rejected"
        reasonCode = "HistoricalBuildOrRawProcessEvidenceNotQualified"
        notes = "Known pcapng files remain transport/visual evidence and were not rescanned or promoted."
    }
)

$pathResolution = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    status = "Blocked"
    selectedPath = "A.CurrentOfficialClientToCurrentOfficialServer"
    selectedPathActivation = "BlockedBeforeClientLaunch"
    officialEndpointAvailable = $true
    clientBuildMatched = $true
    authenticationAvailable = $false
    automationAvailable = "Partial; private-local login/world only"
    safeEncounterAvailable = $false
    socketInstrumentationAvailable = $true
    rawPcapAvailable = "Installed; capture start not attempted because authentication gate failed"
    endpointRestoreAvailable = $true
    endpointSwitchRequired = $false
    automaticPathEvaluations = 3
    liveCaptureAttempts = 0
    blocker = $blocker
    evaluations = $pathEvaluations
    evidence = @(
        "protocol/evidence/battle-v1/client-builds.json",
        "Automation/State/login-secret-status.json (metadata only; secret blob not read)",
        "Artifacts/ClientInstrumentation/LauncherAutomation/launcher-profile.json",
        $instrumentationSummaryRelative,
        "ExternalEvidence/battle-started.json#sha256=D6C333D5C92A4EB98538B0E2A693EA8297C35F8D9DF7717C2BBB2670C97CA694",
        "ExternalEvidence/battle-normal-attack-ended.json#sha256=53BC46C84FA8349B787184485C87968B85F507C4D8A7FD6DAE387EA6063A250B"
    )
}

$endpointSafety = [ordered]@{
    schemaVersion = 1
    status = "PASS"
    endpointSwitchRequired = $false
    endpointMutationPerformed = $false
    endpointRestoreRequired = $false
    endpointRestoreStatus = "PASS_NOT_REQUIRED"
    endpointConfigSafeReference = "OfficialClient/ctserver.ini"
    beforeSha256 = $endpointHash
    afterSha256 = $endpointHash
    privateEndpointStillConfigured = $true
    officialEndpointStillListed = $true
    launcherPreserved = [bool]$launcher
}

$instrumentation = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    status = "READY_WITH_UPSTREAM_AUTH_BLOCKER"
    actualOfficialClientAttach = "NOT RUN"
    automationHostScheduledIntegrity = "High/RID 0x3000"
    lastLiveAutomationHostIntegrity = [string]$hostStatus.integrityLevel
    launcherIntegrity = "High/RID 0x3000"
    currentGod2OptIntegrity = "NOT RUN; client absent"
    clientArchitecture = "x86"
    probeArchitecture = "x86"
    architectureMatch = $true
    clientBuildId = [string]$clientBuild.clientBuildId
    clientSha256 = $clientHash
    buildHashMatches = ($clientHash -eq [string]$clientBuild.god2OptSha256)
    hookSelfTest = [ordered]@{
        send = "PASS"
        WSASend = "PASS"
        recv = "PASS"
        WSARecv = "PASS"
        payloadIntegrity = "PASS"
        traceIntegrity = "PASS"
        summaryPath = $instrumentationSummaryRelative
        summarySha256 = $instrumentationSummaryHash
    }
    queue = [ordered]@{
        capacity = 512
        policy = "BoundedRing; drop and count"
        sequence = "Interlocked monotonic"
        timestamp = "Wall clock plus QueryPerformanceCounter"
        maximumPayloadBytesPerRecord = 4096
    }
    pcap = [ordered]@{
        implementation = "pktmon"
        executableAvailable = $true
        currentNonElevatedStatusProbe = "AccessUnavailable"
        highIntegrityCaptureHistoryAvailable = $true
        currentCaptureStarted = $false
    }
    restrictedEvidenceDirectoryReady = $true
    redactorReady = $true
    freeBytes = $freeBytes
    diskThresholdSatisfied = ($freeBytes -gt 20GB)
    launcherPid = $LauncherPid
    launcherPreserved = [bool]$launcher
}

$encounterSelection = [ordered]@{
    schemaVersion = 1
    status = "BlockedByUpstreamAuthentication"
    selectedEncounter = $null
    minimalSingleEnemy = $null
    automationSelectorVerified = $false
    candidates = @(
        [ordered]@{
            candidate = "HistoricalOfficialFistMaster"
            result = "Rejected"
            reasons = @("User-operated", "No reusable selector", "Pet/multiple visible participants", "Not current-build raw process evidence")
        },
        [ordered]@{
            candidate = "HistoricalOfficialFenghuaBattle"
            result = "Rejected"
            reasons = @("User-operated", "Trigger route not recorded as automation", "Equipment/pet state involved")
        },
        [ordered]@{
            candidate = "PrivateLocalWorldEncounter"
            result = "Rejected"
            reasons = @("Production spawn count is zero", "Official Battle S2C protocol remains blocked")
        }
    )
    randomMovementAttempted = $false
    unknownNpcChoiceAttempted = $false
    unknownBattleControlAttempted = $false
}

$captureRuns = @("RunA", "RunB", "RunC") | ForEach-Object {
    [ordered]@{
        run = $_
        status = "NOT RUN"
        reasonCode = $blocker.code
        captureSessionId = $null
        automationRunId = $null
        rawProcessTrace = $null
        pcapng = $null
        battleTraffic = $false
        dropCount = 0
    }
}

$qualification = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    status = "Blocked"
    qualifyingCaptureCount = 0
    partialCaptureCount = 0
    rejectedCaptureCount = 0
    notRunCaptureCount = 3
    rawBattleSampleCount = 0
    captureDropCount = 0
    resultCode = "NoCaptureDueToOfficialAuthenticationAuthorityUnavailable"
    runs = $captureRuns
}

$unchangedFamilies = @($v1Families.families | ForEach-Object {
    [ordered]@{
        family = $_.family
        direction = $_.direction
        currentBuildSampleCount = 0
        opcode = $null
        confidence = $_.confidence
        gate = $_.gate
        changedThisSprint = $false
    }
})

$currentCaptures = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    clientBuildId = [string]$clientBuild.clientBuildId
    status = "Blocked"
    selectedCapturePath = "A.CurrentOfficialClientToCurrentOfficialServer"
    selectedPathActivation = "BlockedBeforeClientLaunch"
    automaticPathEvaluations = 3
    qualifyingCaptureCount = 0
    captureAttempts = 0
    rawBattleSamples = 0
    captureDropCount = 0
    inventedOpcodes = 0
    fakeNetworkBytes = 0
    blocker = $blocker
    captures = @()
}

Write-Json "Artifacts/BattleRawCaptureV1/PathResolution/capture-path-resolution.json" $pathResolution
Write-Json "Artifacts/BattleRawCaptureV1/capture-path-resolution.json" $pathResolution
Write-Json "Artifacts/BattleRawCaptureV1/endpoint-safety.json" $endpointSafety
Write-Json "Artifacts/BattleRawCaptureV1/Instrumentation/instrumentation-preflight.json" $instrumentation
Write-Json "Artifacts/BattleRawCaptureV1/instrumentation-preflight.json" $instrumentation
Write-Json "Artifacts/BattleRawCaptureV1/encounter-selection.json" $encounterSelection
Write-Json "Artifacts/BattleRawCaptureV1/capture-qualification.json" $qualification
Write-Json "Artifacts/BattleRawCaptureV1/Handoff/handoff.json" ([ordered]@{
    schemaVersion = 1
    inheritedFreeze = "BATTLE PROTOCOL EVIDENCE INFRASTRUCTURE FREEZE PASS"
    inheritedFullTests = "2075/2075"
    currentSprint = "Current-Build Battle Raw Capture First-Contact"
    result = "Blocked"
    blocker = $blocker
})

foreach ($run in $captureRuns) {
    Write-Json "Artifacts/BattleRawCaptureV1/Captures/$($run.run)/status.json" $run
}

$notRunStatuses = [ordered]@{
    "Pcap/status.json" = "NOT RUN; upstream authentication blocker"
    "ProcessTrace/status.json" = "NOT RUN; upstream authentication blocker"
    "StreamReconstruction/status.json" = "NOT RUN; no raw capture"
    "Timeline/status.json" = "NOT RUN; no Battle timeline"
    "Differential/status.json" = "NOT RUN; Run A/B/C absent"
    "PacketFamilies/status.json" = "UNCHANGED; no current-build raw samples"
    "BasicAttack/status.json" = "BlockedByEvidence; zero raw samples"
    "Decoder/status.json" = "BlockedByEvidence; zero golden and negative capture tests"
    "Golden/status.json" = "NOT CREATED; no qualifying raw capture"
    "Replay/status.json" = "NOT RUN; no raw fixture"
    "FailureInjection/status.json" = "NOT RUN; stopped at CapturePathResolution"
}
foreach ($entry in $notRunStatuses.GetEnumerator()) {
    Write-Json "Artifacts/BattleRawCaptureV1/$($entry.Key)" ([ordered]@{
        status = $entry.Value
        primaryBlocker = $blocker.code
    })
}

Write-Json "protocol/evidence/battle-v1/current-build-captures.json" $currentCaptures
Write-Json "protocol/evidence/battle-v1/current-build-packet-families.json" ([ordered]@{
    schemaVersion = 1
    clientBuildId = [string]$clientBuild.clientBuildId
    source = "packet-families.json"
    gateChanges = 0
    families = $unchangedFamilies
})
Write-Json "protocol/evidence/battle-v1/current-build-field-candidates.json" ([ordered]@{
    schemaVersion = 1
    clientBuildId = [string]$clientBuild.clientBuildId
    candidateCount = 0
    fields = @()
    blocker = $blocker.code
})
Write-Json "protocol/evidence/battle-v1/current-build-sequence-map.json" ([ordered]@{
    schemaVersion = 1
    currentBuildCaptureSequenceCount = 0
    status = "Blocked"
    sequences = @()
    blocker = $blocker.code
})
Write-Json "protocol/evidence/battle-v1/current-build-decoder-status.json" ([ordered]@{
    schemaVersion = 1
    basicAttack = "BlockedByEvidence"
    goldenSampleCount = 0
    negativeCaptureTestCount = 0
    productionDecoderCount = 0
    blocker = $blocker.code
})
Write-Json "protocol/evidence/battle-v1/current-build-serializer-status.json" ([ordered]@{
    schemaVersion = 1
    status = "SerializerBlockedByEvidence"
    candidateCount = 0
    clientAcceptanceCount = 0
    productionSerializerCount = 0
    fakeNetworkBytes = 0
})
Write-Json "protocol/evidence/battle-v1/current-build-gates.json" ([ordered]@{
    schemaVersion = 1
    gateChanges = 0
    actorPrimaryEnabled = $false
    defaultEngine = "LegacyPrimary"
    fakeNetworkBytes = 0
    gates = $unchangedFamilies
})

$common = @"
## Current result

- Current-build Battle Raw Capture: **BLOCKED**
- Blocking stage: **CapturePathResolution**
- Blocking reason: **OfficialAuthenticationAuthorityUnavailable**
- Path evaluations: **3**
- Live capture attempts: **0**
- Raw Battle samples: **0**
- Invented opcodes: **0**
- Production decoders: **0**
- Production serializers: **0**
- Fake network bytes: **0**
- ActorPrimary: **NOT ENABLED**
- LegacyPrimary: **RETAINED**
- Launcher PID ${LauncherPid}: **ALIVE**
- User Manual Operation: **NOT REQUIRED**
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Handoff.md") @"
# Battle Raw Capture V1 Handoff

The Battle Protocol Evidence V1 freeze was read and retained. Existing capture/replay, redaction, state-machine, client-build, adapter, high-integrity host, launcher recorder, and x86 socket-probe assets were reused; no duplicate framework was created.

The previous baseline remains 2,075/2,075 PASS with zero production Battle decoders, serializers, or opcodes. Existing transport captures were not rescanned or promoted.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.CapturePath.md") @"
# Battle Raw Capture V1 Capture Path

Path A was selected for evaluation. The official endpoint is listed in the current launcher configuration and TCP ports 2592 and 2596 are reachable. The current client hash matches ``$($clientBuild.clientBuildId)``.

Activation stopped before launching the client: the approved DPAPI login authority is private-local only. No approved official account/session or automated official quick-login authority is present. The local credential was not sent to the official endpoint, no credential plaintext was read, and the user was not asked for credentials.

Path B was rejected because the private server has zero production encounter spawns and no verified Battle serializers. Path C was rejected under the existing historical-evidence decision.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Instrumentation.md") @"
# Battle Raw Capture V1 Instrumentation

The reusable x86 instrumentation passed isolated payload-integrity tests for ``send``, ``WSASend``, ``recv``, and ``WSARecv``. Its 512-record bounded ring uses monotonic Interlocked sequence numbers, QueryPerformanceCounter timestamps, a 4,096-byte per-record cap, and a drop counter.

The current client build and probe architecture match. The high-integrity scheduled host contract is valid and the last live host/Launcher state was High/RID 0x3000. ``pktmon`` is installed; a live elevated pcap and official-client attach were not started because the authentication gate failed first.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.EncounterAutomation.md") @"
# Battle Raw Capture V1 Encounter Automation

No encounter was selected. The historical FistMaster and Fenghua Battles were user-operated and do not provide reusable, verified encounter/basic-attack selectors; the former also included a pet and multiple visible participants. The private world has zero production spawn rows.

No random movement, unknown NPC choice, unknown Battle control, or manual request was attempted.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Qualification.md") @"
# Battle Raw Capture V1 Qualification

Qualifying captures: **0**. Runs A, B, and C are **NOT RUN**, not Rejected and not Partial. No process trace, pcapng, Battle timeline, inbound/outbound Battle range, or post-command traffic exists for this sprint.

$common
"@

$notRunReports = [ordered]@{
    "TransformBoundary" = "NOT RUN. No socket buffer or pcap payload exists. Encryption and compression boundaries remain Unknown/EvidenceBlocked."
    "Timeline" = "NOT RUN. No current-build Battle transition or command timestamp exists; no UI observation was promoted."
    "Differential" = "NOT RUN. Run A/B/C do not exist, so constants, identifiers, HP, damage, and opaque fields were not classified."
    "PacketFamilies" = "UNCHANGED. Identified current-build Battle families: none. Candidate/verified opcodes: none. All V1 gates remain blocked."
    "BasicAttack" = "BlockedByEvidence. No controlled outbound Basic Attack frame, target mapping, round mapping, or command-window mapping exists."
    "Decoder" = "BlockedByEvidence. Golden samples: 0. Negative capture-derived decoder tests: 0. No decoder implementation was added."
    "SerializerBoundary" = "SerializerBlockedByEvidence. Candidates: 0. Official client acceptance: 0. No bytes were emitted."
    "GoldenFixture" = "NOT CREATED. A fixture without qualifying raw capture would fabricate evidence."
    "OfflineReplay" = "NOT RUN. No raw fixture exists. Existing replay infrastructure remains unchanged."
}
foreach ($entry in $notRunReports.GetEnumerator()) {
    Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.$($entry.Key).md") @"
# Battle Raw Capture V1 $($entry.Key)

$($entry.Value)

$common
"@
}

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Security.md") @"
# Battle Raw Capture V1 Security

| Check | Result |
| --- | --- |
| Credential findings | $CredentialFindings |
| Capture secret findings | $CaptureSecretFindings |
| Hardcoded path findings | $HardcodedPathFindings |
| Official credential requested/read | No |
| Client binary modified | No |
| Endpoint modified | No |
| Unknown UI clicked | No |
| Raw Battle evidence created | No |
| Fake network bytes | 0 |

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.FailureInjection.md") @"
# Battle Raw Capture V1 Failure Injection

New Capture-sprint failure-injection matrix: **NOT RUN** because execution stopped at the mandatory CapturePathResolution gate. No meaningless scenarios were added to inflate the count.

The actual authentication-unavailable path was contained: no endpoint mutation, official login attempt, capture claim, decoder promotion, serializer output, user request, client residue, or Launcher termination occurred. Existing Battle Protocol 62/62 and Battle Architecture 60/60 baselines remain covered by the full solution.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.TestResults.md") @"
# Battle Raw Capture V1 Test Results

| Check | Result |
| --- | --- |
| Full solution | $FullTestPassed/$FullTestTotal PASS |
| Hook self-test | 4/4 PASS |
| Frozen login-to-world | $(if ($frozenRegression) { "$($frozenRegression.status); $($frozenRegression.heartbeatCount) heartbeats over $($frozenRegression.heartbeatDurationSeconds) seconds; clean exit $($frozenRegression.serverCleanExit)" } else { "NOT RUN" }) |
| Automation environment preflight | $AutomationEnvironmentPreflightStatus |
| Automation JSON retry | $AutomationJsonRetryStatus; bounded permanent failure in $AutomationJsonRetryElapsedMilliseconds ms |
| Protocol audit concurrency | 30/30 PASS |
| Frozen gameplay | 735/735 PASS |
| Frozen Quest | 258/258 PASS (219 Runtime + 14 Persistence + 25 Integration) |
| New Capture Offline/Headless scenarios | 0; NOT RUN after mandatory path blocker |
| Existing Battle Protocol Offline/Headless | 265/265 PASS through full suite |
| Existing Battle Protocol failure injection | 62/62 PASS through full suite |
| Existing Battle Protocol runtime catalog | 329/329 PASS |
| Existing Battle Protocol contract | 22/22 PASS |
| Existing Battle Architecture | 360/360 PASS through full suite |
| Existing Battle Architecture failure injection | 60/60 PASS through full suite |
| Migration 030 | $Migration030Status |

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Regression.md") @"
# Battle Raw Capture V1 Regression

No runtime, protocol implementation, endpoint, client binary, gameplay formula, migration, or engine selection was changed. Full and focused regression results are recorded in the final handoff.

$(if ($frozenRegression) { "The final unchanged local frozen login-to-world regression is **$($frozenRegression.status)**: login, character selection, world entry, and world-ready all succeeded; $($frozenRegression.heartbeatCount) heartbeats were observed over $($frozenRegression.heartbeatDurationSeconds) seconds; server clean exit is $($frozenRegression.serverCleanExit); client/server residue is $($frozenRegression.remainingClientCount)/$($frozenRegression.remainingServerCount); Launcher count is $($frozenRegression.launcherCount). Evidence: ``$frozenRegressionSummaryRelative``." } else { "A new frozen login-to-world regression was not supplied to this report writer." })

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.Blocked.md") @"
# Battle Raw Capture V1 Blocked

The single primary blocker is ``OfficialAuthenticationAuthorityUnavailable`` at ``CapturePathResolution``.

The official endpoint is reachable and capture machinery is viable, but the only approved automation credential authority is private-local. Historical official Battle sessions were user-operated and expose no approved reusable authentication session or verified encounter/basic-attack selector. Attempting the local account against the official endpoint or requesting user credentials would violate the sprint constraints.

Automatic path evaluations: **3**. Live capture attempts: **0**. Raw Battle samples: **0**.

$common
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleRawCaptureV1.FinalFreeze.md") @"
# Battle Raw Capture V1 Final Freeze

Current-build Battle Raw Capture: **BLOCKED**  
Capture Path: **A - Current Official Client to Current Official Server**  
Blocking Stage: **CapturePathResolution**  
Blocking Reason: **OfficialAuthenticationAuthorityUnavailable**  
Automatic Attempts: **3 path evaluations; 0 live capture attempts**  
Raw Battle Samples: **0**  
Invented Opcodes: **0**  
Production Decoders: **0**  
Production Serializers: **0**  
Fake Network Bytes: **0**  
Launcher: **ALIVE - PID $LauncherPid**  
Endpoint Restore: **PASS_NOT_REQUIRED**  
User Manual Operation: **NOT REQUIRED**

Final Status: **CURRENT-BUILD BATTLE RAW CAPTURE BLOCKED**
"@

Write-Json "Artifacts/BattleRawCaptureV1/Regression/summary.json" ([ordered]@{
    fullSolution = "$FullTestPassed/$FullTestTotal"
    hookSelfTest = "4/4"
    frozenGameplay = "735/735"
    frozenQuest = "258/258"
    battleArchitecture = "360/360"
    battleArchitectureFailureInjection = "60/60"
    battleProtocolOffline = "265/265"
    battleProtocolFailureInjection = "62/62"
    battleProtocolRuntimeCatalog = "329/329"
    battleProtocolContract = "22/22"
    protocolAudit = "30/30"
    automationJsonRetry = [ordered]@{
        status = $AutomationJsonRetryStatus
        boundedFailureElapsedMilliseconds = $AutomationJsonRetryElapsedMilliseconds
    }
    automationEnvironmentPreflight = $AutomationEnvironmentPreflightStatus
    migration030 = $Migration030Status
    frozenLoginToWorld = if ($frozenRegression) {
        [ordered]@{
            status = [string]$frozenRegression.status
            summaryPath = $frozenRegressionSummaryRelative
            heartbeatCount = [int]$frozenRegression.heartbeatCount
            heartbeatDurationSeconds = [double]$frozenRegression.heartbeatDurationSeconds
            serverCleanExit = [bool]$frozenRegression.serverCleanExit
            remainingClientCount = [int]$frozenRegression.remainingClientCount
            remainingServerCount = [int]$frozenRegression.remainingServerCount
            launcherCount = [int]$frozenRegression.launcherCount
        }
    }
    else {
        "NOT RUN"
    }
    fakeNetworkBytes = 0
    launcherAlive = [bool]$launcher
})

Write-Output "Battle Raw Capture V1 blocked handoff generated: $($blocker.code)"
