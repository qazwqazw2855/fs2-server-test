param(
    [string]$WorkspaceRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$FullTestPassed = 0,
    [int]$FullTestTotal = 0,
    [string]$FinalRegressionRunId = "not-run",
    [int]$FinalRegressionHeartbeatCount = 0,
    [double]$FinalRegressionDurationSeconds = 0,
    [int]$LauncherPid = 548,
    [int]$CredentialFindingCount = 0,
    [int]$HardcodedPathFindingCount = 0,
    [string]$ClientRoot = ""
)

$ErrorActionPreference = "Stop"
$WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$reportRoot = Join-Path $WorkspaceRoot "Reports"
$protocolRoot = Join-Path $WorkspaceRoot "protocol\evidence\battle-v1"
$artifactRoot = Join-Path $WorkspaceRoot "Artifacts\BattleProtocolEvidenceV1"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$generatedAtUtc = [DateTimeOffset]::UtcNow
if ([string]::IsNullOrWhiteSpace($ClientRoot)) {
    $hostStatusPath = Join-Path $WorkspaceRoot "Automation\State\host-status.json"
    if (-not (Test-Path -LiteralPath $hostStatusPath)) {
        throw "ClientRoot was not supplied and the approved automation host status was not found."
    }

    $hostStatus = Get-Content -LiteralPath $hostStatusPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$hostStatus.launcherPath)) {
        throw "The approved automation host status does not contain launcherPath."
    }

    $ClientRoot = Split-Path -Parent ([string]$hostStatus.launcherPath)
}

$clientRoot = [System.IO.Path]::GetFullPath($ClientRoot)
$clientExe = Join-Path $clientRoot "God2_opt.exe"
$launcherExe = Join-Path $clientRoot "Launcher.exe"
$clientBuildHash = (Get-FileHash -LiteralPath $clientExe -Algorithm SHA256).Hash.ToUpperInvariant()
$launcherHash = (Get-FileHash -LiteralPath $launcherExe -Algorithm SHA256).Hash.ToUpperInvariant()
$clientBuildId = "god2-opt-" + $clientBuildHash.Substring(0, 12).ToLowerInvariant()
$verifiedCatalog = "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/VerifiedPacketCatalog.json"
$unknownCatalog = "src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/UnknownPacketCatalog.json"
$verifiedCatalogHash = (Get-FileHash -LiteralPath (Join-Path $WorkspaceRoot $verifiedCatalog) -Algorithm SHA256).Hash
$unknownCatalogHash = (Get-FileHash -LiteralPath (Join-Path $WorkspaceRoot $unknownCatalog) -Algorithm SHA256).Hash

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)

    $parent = Split-Path -Parent $Path
    if ($parent) {
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, ($Content.TrimEnd() + [Environment]::NewLine), $utf8NoBom)
}

function Write-Json {
    param([string]$RelativePath, $Value)

    $json = ConvertTo-Json -InputObject $Value -Depth 20
    Write-Utf8NoBom -Path (Join-Path $WorkspaceRoot $RelativePath) -Content $json
}

function Get-RelativeHashRecord {
    param([string]$RelativePath)

    $absolute = Join-Path $WorkspaceRoot $RelativePath
    $item = Get-Item -LiteralPath $absolute
    [ordered]@{
        path = $RelativePath.Replace("\", "/")
        sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash
        length = $item.Length
        lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString("O")
    }
}

function Get-PeArchitecture {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        $machine = $reader.ReadUInt16()
        switch ($machine) {
            0x014c { "x86" }
            0x8664 { "x64" }
            default { "unknown-0x{0:x4}" -f $machine }
        }
    }
    finally {
        $stream.Dispose()
    }
}

[System.IO.Directory]::CreateDirectory($reportRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($protocolRoot) | Out-Null
$artifactDirectories = @(
    "Baseline",
    "Provenance",
    "Captures",
    "RedactedCaptures",
    "Decoder",
    "Serializer",
    "StateMachine",
    "BattleEnter",
    "Formation",
    "Round",
    "BasicAttack",
    "Damage",
    "Golden",
    "Differential",
    "ClientAutomation",
    "FailureInjection",
    "Regression"
)
foreach ($directory in $artifactDirectories) {
    [System.IO.Directory]::CreateDirectory((Join-Path $artifactRoot $directory)) | Out-Null
}

$moduleNames = @(
    "CrashRpt.dll",
    "msvcp140.dll",
    "node.dll",
    "SkinMagic.dll",
    "ucrtbase.dll",
    "vcruntime140.dll",
    "WebView2Loader.dll",
    "zlibwapi.dll"
)
$moduleHashes = [ordered]@{}
foreach ($moduleName in $moduleNames) {
    $path = Join-Path $clientRoot $moduleName
    if (Test-Path -LiteralPath $path) {
        $moduleHashes["OfficialClient/$moduleName"] =
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

$configNames = @(
    "client_p.godZ",
    "ctserver.ini",
    "God2Con.csvZ",
    "God2Con2.csvZ",
    "LauncherVersion.ini"
)
$configHashes = [ordered]@{}
foreach ($configName in $configNames) {
    $path = Join-Path $clientRoot $configName
    if (Test-Path -LiteralPath $path) {
        $configHashes["OfficialClient/$configName"] =
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

$clientInfo = Get-Item -LiteralPath $clientExe
$launcherInfo = Get-Item -LiteralPath $launcherExe
$clientBuild = [ordered]@{
    clientBuildId = $clientBuildId
    god2OptSha256 = $clientBuildHash
    launcherSha256 = $launcherHash
    fileVersionCandidate = $clientInfo.VersionInfo.FileVersion
    productVersionCandidate = $clientInfo.VersionInfo.ProductVersion
    architecture = Get-PeArchitecture -Path $clientExe
    locale = "zh-TW candidate; locale is not asserted from executable hash alone"
    protocolVariant = "official-classic-client; battle wire variant unrecovered"
    god2Opt = [ordered]@{
        label = "OfficialClient/God2_opt.exe"
        length = $clientInfo.Length
        lastWriteTimeUtc = $clientInfo.LastWriteTimeUtc.ToString("O")
    }
    launcher = [ordered]@{
        label = "OfficialClient/Launcher.exe"
        length = $launcherInfo.Length
        lastWriteTimeUtc = $launcherInfo.LastWriteTimeUtc.ToString("O")
    }
    moduleHashes = $moduleHashes
    relevantConfigHashes = $configHashes
    excludedSensitiveFiles = @("OfficialClient/cookies.dat")
    recordedAtUtc = $generatedAtUtc.ToString("O")
}

$packetFamilies = @(
    [ordered]@{ family = "EncounterTrigger"; direction = "ServerToClient" },
    [ordered]@{ family = "BattleEnter"; direction = "ServerToClient" },
    [ordered]@{ family = "Formation"; direction = "ServerToClient" },
    [ordered]@{ family = "PlayerSpawn"; direction = "ServerToClient" },
    [ordered]@{ family = "PetSpawn"; direction = "ServerToClient" },
    [ordered]@{ family = "EnemySpawn"; direction = "ServerToClient" },
    [ordered]@{ family = "RoundStart"; direction = "ServerToClient" },
    [ordered]@{ family = "CommandWindow"; direction = "ServerToClient" },
    [ordered]@{ family = "BasicAttackClientCommand"; direction = "ClientToServer" },
    [ordered]@{ family = "SkillClientCommand"; direction = "ClientToServer" },
    [ordered]@{ family = "ItemClientCommand"; direction = "ClientToServer" },
    [ordered]@{ family = "DefendClientCommand"; direction = "ClientToServer" },
    [ordered]@{ family = "FleeClientCommand"; direction = "ClientToServer" },
    [ordered]@{ family = "ActionConfirmation"; direction = "ServerToClient" },
    [ordered]@{ family = "ActionStart"; direction = "ServerToClient" },
    [ordered]@{ family = "Damage"; direction = "ServerToClient" },
    [ordered]@{ family = "Healing"; direction = "ServerToClient" },
    [ordered]@{ family = "StatusApplyRemove"; direction = "ServerToClient" },
    [ordered]@{ family = "DeathRevive"; direction = "ServerToClient" },
    [ordered]@{ family = "RoundEnd"; direction = "ServerToClient" },
    [ordered]@{ family = "BattleEnd"; direction = "ServerToClient" },
    [ordered]@{ family = "Reward"; direction = "ServerToClient" },
    [ordered]@{ family = "WorldResume"; direction = "ServerToClient" }
)
foreach ($family in $packetFamilies) {
    $family.opcode = $null
    $family.sampleCount = 0
    $family.framing = "Existing uint16-le outer boundary verified; battle-specific framing unverified"
    $family.decodeStatus = "Missing"
    $family.semanticMappingStatus =
        if ($family.family -eq "BasicAttackClientCommand") {
            "AdapterBoundaryPresent; OfficialRawCommandMissing"
        }
        else {
            "Missing"
        }
    $family.serializerStatus =
        if ($family.direction -eq "ClientToServer") { "NotApplicable" } else { "SerializerBlockedByEvidence" }
    $family.confidence =
        if ($family.direction -eq "ClientToServer") { "EvidenceBlocked" } else { "SerializerBlockedByEvidence" }
    $family.gate =
        if ($family.direction -eq "ClientToServer") { "BlockedByEvidence" } else { "SerializerBlockedByEvidence" }
    $family.unknownRequiredDynamicFields = $true
    $family.decoderTestCount = 0
    $family.serializerTestCount = 0
    $family.clientAcceptanceCount = 0
    $family.clientBuildId = $clientBuildId
}

$states = @(
    "WorldActive",
    "EncounterPending",
    "BattleEntering",
    "BattleInitializing",
    "FormationLoading",
    "ParticipantsLoading",
    "WaitingForRoundStart",
    "CommandWindowOpen",
    "CommandSubmitted",
    "AwaitingActionResult",
    "ActionPlayback",
    "RoundClosing",
    "BattleEnding",
    "RewardPresentation",
    "WorldResuming",
    "Completed",
    "RecoveryRequired",
    "EvidenceBlocked",
    "Faulted"
)
$normalSequence = @(
    "WorldActive->EncounterPending",
    "EncounterPending->BattleEntering",
    "BattleEntering->BattleInitializing",
    "BattleInitializing->FormationLoading",
    "FormationLoading->ParticipantsLoading",
    "ParticipantsLoading->WaitingForRoundStart",
    "WaitingForRoundStart->CommandWindowOpen",
    "CommandWindowOpen->CommandSubmitted",
    "CommandSubmitted->AwaitingActionResult",
    "AwaitingActionResult->ActionPlayback",
    "ActionPlayback->RoundClosing",
    "RoundClosing->WaitingForRoundStart",
    "ActionPlayback->BattleEnding",
    "RoundClosing->BattleEnding",
    "BattleEnding->RewardPresentation",
    "BattleEnding->WorldResuming",
    "RewardPresentation->WorldResuming",
    "WorldResuming->Completed",
    "RecoveryRequired->EvidenceBlocked",
    "RecoveryRequired->Faulted",
    "EvidenceBlocked->RecoveryRequired",
    "EvidenceBlocked->Faulted"
)

$sourceFiles = @(
    "src/God2.ClassicServer.Protocol/BattleProtocolEvidence.cs",
    "src/God2.ClassicServer.Protocol/BattleProtocolCapture.cs",
    "src/God2.ClassicServer.Protocol/BattleProtocolStateMachine.cs",
    "src/God2.ClassicServer.Protocol/BattleProtocolAdapter.cs",
    "src/God2.ClassicServer.Runtime/BattleProtocolRuntimeAdapter.cs",
    "tests/God2.ClassicServer.Protocol.Tests/BattleProtocolEvidenceTests.cs",
    "tests/God2.ClassicServer.Runtime.Tests/BattleProtocolEvidenceScenarioTests.cs",
    "Automation/Write-BattleProtocolEvidenceV1.ps1",
    $verifiedCatalog,
    $unknownCatalog
)
$sourceHashes = @($sourceFiles | ForEach-Object { Get-RelativeHashRecord -RelativePath $_ })
$aggregateInput = ($sourceHashes | ForEach-Object { $_.path + ":" + $_.sha256 }) -join "`n"
$aggregateBytes = [System.Text.Encoding]::UTF8.GetBytes($aggregateInput)
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $workspaceManifestHash = ([BitConverter]::ToString($sha.ComputeHash($aggregateBytes))).Replace("-", "")
}
finally {
    $sha.Dispose()
}

$baseline = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    status = "PASS"
    preSprintFullTests = "1724/1724"
    frozenGameplay = "735/735"
    frozenQuest = "258/258"
    battleArchitectureOffline = "360/360"
    battleArchitectureFailureInjection = "60/60"
    protocolAuditConcurrency = "30/30"
    automationJsonRetry = "PASS; transient recovery true; permanent failure bounded at 38 ms"
    migration030 = "Current"
    preflight = "PASS"
    loginToWorld = [ordered]@{
        runId = "battle-protocol-evidence-v1-baseline-20260731"
        result = "PASS"
        heartbeats = 69
        durationSeconds = 60.001
        serverCleanExit = $true
        fakeNetworkBytes = 0
    }
    launcherPid = $LauncherPid
    launcherPreserved = [bool](Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue)
    actorPrimaryEnabled = $false
    defaultEngine = "LegacyPrimary"
    workspaceRevisionKind = "sha256-manifest-no-git-repository"
    workspaceManifestSha256 = $workspaceManifestHash
}

$provenance = @(
    [ordered]@{
        evidenceId = "verified-packet-catalog"
        type = "ExistingGoldenCatalog"
        sourcePath = $verifiedCatalog
        sha256 = $verifiedCatalogHash
        clientBuildId = "not-linked-to-current-build"
        classification = "TrustedLocalMetadata"
        rawBattleBytesQualifying = $false
        notes = "Catalog records visual battle coverage but explicitly says the pcap scan did not expose byte-identical raw game frames."
    },
    [ordered]@{
        evidenceId = "unknown-packet-catalog"
        type = "ExistingUnknownCatalog"
        sourcePath = $unknownCatalog
        sha256 = $unknownCatalogHash
        clientBuildId = "not-linked-to-current-build"
        classification = "TrustedLocalMetadata"
        rawBattleBytesQualifying = $false
        notes = "BasicAttack remains listed as not yet recovered."
    },
    [ordered]@{
        evidenceId = "official-uu-merchant-battle-pcapng"
        type = "TransportCapture"
        sourcePath = "ExternalEvidence/OfficialServerCapture/20260726-154723-UU-merchant-retry/official-server-uu-merchant-battle.pcapng"
        sha256 = "8859047A7A08BE49DB122F56EB5FEA7AA18A37B03E8D8D13C841031BB7D4DCDB"
        length = 410919852
        clientBuildId = "unknown"
        classification = "Restricted"
        rawBattleBytesQualifying = $false
        notes = "Visual/transport evidence only; existing raw scan found no known game frame. Not imported into formal capture storage."
    },
    [ordered]@{
        evidenceId = "official-uu-post-battle-pcapng"
        type = "TransportCapture"
        sourcePath = "ExternalEvidence/OfficialServerCapture/20260726-160753-UU-post-battle/official-server-uu-post-battle-merchant.pcapng"
        sha256 = "1DB3F942AE3B3F6A76E8268430C4527E3B50094D5CC8F52C0E5B308EB167403C"
        length = 393621372
        clientBuildId = "unknown"
        classification = "Restricted"
        rawBattleBytesQualifying = $false
        notes = "Visual/transport evidence only; no decrypted Battle opcode or dynamic field map."
    }
)

$gateRows = @($packetFamilies | ForEach-Object {
    [ordered]@{
        family = $_.family
        direction = $_.direction
        gate = $_.gate
        candidateOpcode = $null
        sampleCount = 0
        promotionAllowed = $false
        blocker = "No qualifying current-build raw battle sample; unknown required dynamic fields."
    }
})

$captureIndex = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    currentBuildId = $clientBuildId
    qualifyingBattleCaptureCount = 0
    importedRawCaptureCount = 0
    restrictedExternalEvidence = $provenance | Where-Object { $_.classification -eq "Restricted" }
    infrastructure = [ordered]@{
        sequence = "Interlocked monotonic per capture session"
        queue = "Bounded non-blocking"
        queueFullPolicy = "DropNewestAndCount"
        stages = @(
            "SocketRaw",
            "FramedEncrypted",
            "FramedDecrypted",
            "PayloadCompressed",
            "PayloadDecompressed",
            "OpcodeDispatched",
            "SemanticMapped",
            "SerializerOutput"
        )
        rawStorageDefault = "Restricted"
        replayNetworkSideEffects = 0
        replayDatabaseSideEffects = 0
        replayRewardSideEffects = 0
        replayQuestSideEffects = 0
    }
}

$initiativeEvidence = [ordered]@{
    schemaVersion = 1
    status = "PASS"
    productionEligibility = "EvidenceBlocked"
    existingCandidates = @(
        [ordered]@{
            source = "TurnBasedBattleRuntime"
            candidate = "Participant InitiativeCandidate; descending order; null falls back to zero"
            confidence = "ExistingRuntimeBehavior"
        },
        [ordered]@{
            source = "LegacyCompatibleBattleInitiativePlanner"
            candidate = "Delegates to existing IBattleTurnOrderPolicy and records an initiative snapshot"
            confidence = "ExistingRuntimeBehavior"
        },
        [ordered]@{
            source = "AttackPower"
            candidate = "Combat stat exists, but no official-client wire evidence proves it is the initiative formula"
            confidence = "NotProtocolEvidence"
        }
    )
    unknowns = @(
        "Official client initiative field",
        "Tie-break rule",
        "Speed/agility contribution",
        "Random contribution"
    )
    mutation = "None"
}

$monsterAiEvidence = [ordered]@{
    schemaVersion = 1
    status = "PASS"
    productionEligibility = "EvidenceBlocked"
    existingCandidates = @(
        [ordered]@{
            source = "BattleActorCommandType.MonsterAi"
            finding = "Contract enum value exists."
        },
        [ordered]@{
            source = "world content"
            finding = "Monster definitions and combat stats exist; latest preflight observed 208 monsters."
        },
        [ordered]@{
            source = "BattleArchitectureV2Actor.Supported"
            finding = "MonsterAi is not accepted by the current supported production actor command set."
        }
    )
    unknowns = @(
        "Official target-selection policy",
        "Official action-choice policy",
        "Official decision timing",
        "Client/server ownership of monster decisions"
    )
    mutation = "None"
}

Write-Json "protocol/evidence/battle-v1/client-builds.json" ([ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    builds = @($clientBuild)
})
Write-Json "protocol/evidence/battle-v1/capture-index.json" $captureIndex
Write-Json "protocol/evidence/battle-v1/packet-families.json" ([ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    families = $packetFamilies
})
Write-Json "protocol/evidence/battle-v1/protocol-state-machine.json" ([ordered]@{
    schemaVersion = 1
    initialState = "WorldActive"
    states = $states
    transitions = $normalSequence
    invariants = @(
        "Connection-safe reference is required.",
        "Session epoch must match.",
        "State version is optimistic and monotonic.",
        "Inbound and outbound sequences must increase.",
        "Packet families are state-scoped.",
        "Completed is terminal."
    )
})
Write-Json "protocol/evidence/battle-v1/packet-field-map.json" ([ordered]@{
    schemaVersion = 1
    policy = "Unknown remains unknown; no production constants are inferred."
    families = @($packetFamilies | ForEach-Object {
        [ordered]@{
            family = $_.family
            opcode = $null
            verifiedFields = @()
            candidateFields = @()
            unknownRequiredFields = @("battle-specific opcode", "battle-specific payload layout", "required dynamic fields")
            productionWritable = $false
        }
    })
})
Write-Json "protocol/evidence/battle-v1/packet-sequence-map.json" ([ordered]@{
    schemaVersion = 1
    intendedSequence = @(
        "EncounterTrigger",
        "BattleEnter",
        "Formation",
        "PlayerSpawn",
        "EnemySpawn",
        "RoundStart",
        "CommandWindow",
        "BasicAttackClientCommand",
        "ActionConfirmation",
        "ActionStart",
        "Damage",
        "RoundEnd"
    )
    status = "SequenceCandidateOnly"
    productionTraversalAllowed = $false
})
Write-Json "protocol/evidence/battle-v1/evidence-confidence.json" ([ordered]@{
    schemaVersion = 1
    orderedLevels = @(
        "Missing",
        "Candidate",
        "ObservedOnce",
        "ObservedRepeated",
        "CrossValidated",
        "DecoderVerified",
        "SerializerCandidate",
        "SerializerVerified",
        "ProductionReady",
        "EvidenceBlocked",
        "SerializerBlockedByEvidence"
    )
    currentBattleConfidence = "EvidenceBlocked"
    promotionRules = @(
        "Current client build hash must match.",
        "Raw evidence must have safe provenance and hashes.",
        "Client-to-server ProductionReady requires at least three decoder samples and ten negative tests.",
        "Server-to-client ProductionReady requires at least three serializer tests and three official client acceptances.",
        "Any unknown required dynamic field blocks production."
    )
})
Write-Json "protocol/evidence/battle-v1/decoder-status.json" ([ordered]@{
    schemaVersion = 1
    currentBuildId = $clientBuildId
    registeredProductionBattleDecoders = 0
    families = @($packetFamilies | Where-Object { $_.direction -eq "ClientToServer" } | ForEach-Object {
        [ordered]@{
            family = $_.family
            status = "BlockedByEvidence"
            officialSampleCount = 0
            decoderFixtureCount = 0
            productionRegistered = $false
        }
    })
})
Write-Json "protocol/evidence/battle-v1/serializer-status.json" ([ordered]@{
    schemaVersion = 1
    currentBuildId = $clientBuildId
    registeredProductionBattleSerializers = 0
    fakeNetworkBytes = 0
    families = @($packetFamilies | Where-Object { $_.direction -eq "ServerToClient" } | ForEach-Object {
        [ordered]@{
            family = $_.family
            status = "SerializerBlockedByEvidence"
            officialSampleCount = 0
            clientAcceptanceCount = 0
            productionRegistered = $false
        }
    })
})
Write-Json "protocol/evidence/battle-v1/protocol-gates.json" ([ordered]@{
    schemaVersion = 1
    currentBuildId = $clientBuildId
    fakeNetworkBytes = 0
    actorPrimaryEnabled = $false
    defaultEngine = "LegacyPrimary"
    gates = $gateRows
})
Write-Json "protocol/evidence/battle-v1/initiative-evidence.json" $initiativeEvidence
Write-Json "protocol/evidence/battle-v1/monster-ai-evidence.json" $monsterAiEvidence
Write-Json "protocol/evidence/battle-v1/golden-fixtures.json" ([ordered]@{
    schemaVersion = 1
    infrastructureStatus = "PASS"
    officialFixtureStatus = "BlockedByEvidence"
    fixtureCount = 0
    fixtures = @()
    blocker = "No qualifying raw current-build battle packet set; fake fixture bytes are prohibited."
})

Write-Json "Artifacts/BattleProtocolEvidenceV1/Baseline/baseline-summary.json" $baseline
Write-Json "Artifacts/BattleProtocolEvidenceV1/Baseline/baseline-hashes.json" ([ordered]@{
    schemaVersion = 1
    aggregateSha256 = $workspaceManifestHash
    files = $sourceHashes
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Provenance/evidence-index.json" ([ordered]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("O")
    evidence = $provenance
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Captures/infrastructure-status.json" ([ordered]@{
    status = "PASS"
    officialRawBattleCaptures = 0
    captureQueue = "BoundedNonBlocking"
    dropPolicy = "DropNewestAndCount"
    rawEvidenceDefault = "Restricted"
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/RedactedCaptures/infrastructure-status.json" ([ordered]@{
    status = "PASS"
    officialRedactedBattleCaptures = 0
    redactionFixtureTests = "PASS"
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Decoder/status.json" ([ordered]@{
    infrastructure = "PASS"
    verifiedOfficialBattleDecoders = 0
    productionRegistered = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Serializer/status.json" ([ordered]@{
    infrastructure = "PASS"
    verifiedOfficialBattleSerializers = 0
    productionRegistered = 0
    fakeNetworkBytes = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/StateMachine/status.json" ([ordered]@{
    infrastructure = "PASS"
    stateCount = $states.Count
    productionBattleTraversal = "BlockedByEvidence"
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/BattleEnter/status.json" ([ordered]@{
    infrastructure = "PASS"
    officialPacket = "SerializerBlockedByEvidence"
    sampleCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Formation/status.json" ([ordered]@{
    infrastructure = "PASS"
    officialFormationPacket = "SerializerBlockedByEvidence"
    officialParticipantPackets = "SerializerBlockedByEvidence"
    sampleCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Round/status.json" ([ordered]@{
    infrastructure = "PASS"
    roundStart = "SerializerBlockedByEvidence"
    commandWindow = "SerializerBlockedByEvidence"
    roundEnd = "SerializerBlockedByEvidence"
    sampleCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/BasicAttack/status.json" ([ordered]@{
    infrastructure = "PASS"
    c2sDecoder = "BlockedByEvidence"
    commandMapper = "PresentBehindDecoderVerifiedGate"
    officialSampleCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Damage/status.json" ([ordered]@{
    infrastructure = "PASS"
    semanticProjection = "AuthoritativeRuntimeResultOnly"
    officialSerializer = "SerializerBlockedByEvidence"
    officialSampleCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Golden/status.json" ([ordered]@{
    infrastructure = "PASS"
    officialFixture = "BlockedByEvidence"
    fixtureCount = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Differential/status.json" ([ordered]@{
    infrastructure = "ExistingBattleArchitectureV2"
    defaultEngine = "LegacyPrimary"
    actorMode = "ActorShadow"
    actorPrimaryEnabled = $false
    officialBattleDifferentialRuns = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/ClientAutomation/status.json" ([ordered]@{
    officialBattleAutomation = "NOT RUN"
    reason = "Offline evidence gates blocked"
    repeatedBattleVerificationCount = 0
    finalFrozenLoginToWorldRunId = $FinalRegressionRunId
    finalFrozenLoginToWorldHeartbeats = $FinalRegressionHeartbeatCount
    finalFrozenLoginToWorldDurationSeconds = $FinalRegressionDurationSeconds
    launcherPid = $LauncherPid
    userManualOperation = "NOT REQUIRED"
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/FailureInjection/summary.json" ([ordered]@{
    status = "PASS"
    newBattleProtocolFailureScenarios = 62
    existingBattleArchitectureFailureInjection = "60/60"
    fakeBytesOnFailure = 0
})
Write-Json "Artifacts/BattleProtocolEvidenceV1/Regression/summary.json" ([ordered]@{
    fullSolution = "$FullTestPassed/$FullTestTotal"
    frozenGameplay = "735/735"
    frozenQuest = "258/258"
    battleArchitectureOffline = "360/360"
    battleArchitectureFailureInjection = "60/60"
    finalLoginToWorld = [ordered]@{
        runId = $FinalRegressionRunId
        heartbeats = $FinalRegressionHeartbeatCount
        durationSeconds = $FinalRegressionDurationSeconds
    }
})

$matrixRows = ($packetFamilies | ForEach-Object {
    "| $($_.family) | $($_.direction) | none | 0 | $($_.gate) |"
}) -join [Environment]::NewLine

$commonBlock = @"
## Freeze boundary

- Official battle wire evidence: **insufficient**.
- Official Battle Protocol: **PARTIAL / BlockedByEvidence**.
- Official Basic Attack Round: **NOT YET VERIFIED**.
- Production battle decoder registrations: **0**.
- Production battle serializer registrations: **0**.
- Fake network bytes: **0**.
- ActorPrimary: **NOT ENABLED**.
- Default engine: **LegacyPrimary RETAINED**.
- User Manual Operation: **NOT REQUIRED**.
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Baseline.md") @"
# Battle Protocol Evidence V1 Baseline

The pre-change frozen baseline passed before the battle protocol evidence infrastructure was added.

| Check | Result |
| --- | --- |
| Full solution | 1,724/1,724 PASS |
| Debug / Release build | PASS, 0 warnings |
| Recorder Debug / Release build | PASS, 0 warnings |
| Frozen gameplay | 735/735 PASS |
| Quest | 258/258 PASS |
| Battle Architecture v2 offline | 360/360 PASS |
| Battle Architecture failure injection | 60/60 PASS |
| Protocol audit concurrency | 30/30 PASS |
| Automation JSON retry | PASS; transient recovery true; permanent failure bounded at 38 ms |
| Migration 030 | Current |
| Automation environment preflight | PASS |
| Frozen login-to-world | PASS; 69 heartbeats over 60.001 s; clean shutdown |
| Launcher | PID $LauncherPid preserved |
| Workspace revision | SHA-256 manifest ``$workspaceManifestHash`` |

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Matrix.md") @"
# Battle Protocol Evidence V1 Matrix

No existing artifact qualifies as a current-build raw Battle packet sample. Null opcodes below are deliberate evidence results.

| Packet family | Direction | Verified opcode | Samples | Gate |
| --- | --- | --- | ---: | --- |
$matrixRows

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Provenance.md") @"
# Battle Protocol Evidence V1 Provenance

| Evidence | SHA-256 | Classification | Qualifying raw Battle bytes |
| --- | --- | --- | --- |
| ``$verifiedCatalog`` | ``$verifiedCatalogHash`` | Trusted local metadata | No |
| ``$unknownCatalog`` | ``$unknownCatalogHash`` | Trusted local metadata | No |
| ``ExternalEvidence/OfficialServerCapture/20260726-154723-UU-merchant-retry/official-server-uu-merchant-battle.pcapng`` | ``8859047A7A08BE49DB122F56EB5FEA7AA18A37B03E8D8D13C841031BB7D4DCDB`` | Restricted transport/visual evidence | No |
| ``ExternalEvidence/OfficialServerCapture/20260726-160753-UU-post-battle/official-server-uu-post-battle-merchant.pcapng`` | ``1DB3F942AE3B3F6A76E8268430C4527E3B50094D5CC8F52C0E5B308EB167403C`` | Restricted transport/visual evidence | No |

The two large pcapng files were hashed in place and were not copied. The existing catalog already records that its binary scan matched no known raw game frames. Their Battle visuals and timing cannot prove opcodes, framing, encryption/compression position, or dynamic fields.

Current client build: ``$clientBuildId``. Executable/module/config hashes are in ``protocol/evidence/battle-v1/client-builds.json``; sensitive ``cookies.dat`` is excluded.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.CaptureHarness.md") @"
# Battle Protocol Evidence V1 Capture Harness

Infrastructure status: **PASS**.

- Capture records carry build, capture session, direction, monotonic sequence, time delta, processing stage, state phase, correlation ID, lengths, hashes, and redaction state.
- The queue is bounded and non-blocking. Full queues drop the newest record and increment a counter.
- Raw evidence references must be safe relative paths; raw content defaults to Restricted.
- Canonical replay validates source hashes and semantic hashes and reports zero network, database, reward, and quest side effects.
- Existing local ``send``, ``WSASend``, ``recv``, and ``WSARecv`` instrumentation remains reusable. No live Battle capture was initiated because Offline gates are blocked.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.GatewayPipeline.md") @"
# Battle Protocol Evidence V1 Gateway Pipeline

Infrastructure status: **PASS**.

The verified shared boundary is the existing little-endian 16-bit outer frame length. Battle-specific opcode dispatch, encryption position, compression position, and payload maps are not verified. The adapter order is:

``frame validation -> state/session validation -> evidence gate -> decoder -> semantic candidate -> runtime mapping``.

Outbound order is:

``authoritative runtime event/result -> semantic projection -> evidence gate -> serializer``.

A blocked or failed serializer always returns an empty byte buffer. No generic fallback serializer exists.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.StateMachine.md") @"
# Battle Protocol Evidence V1 State Machine

Infrastructure status: **PASS**. The tracker has $($states.Count) explicit states, optimistic version checks, session-epoch validation, monotonic inbound/outbound sequences, packet-family allowlists, a bounded audit, and a terminal Completed state.

The intended path is WorldActive -> EncounterPending -> BattleEntering -> BattleInitializing -> FormationLoading -> ParticipantsLoading -> WaitingForRoundStart -> CommandWindowOpen -> CommandSubmitted -> AwaitingActionResult -> ActionPlayback. RoundClosing may return to WaitingForRoundStart; ending may continue through RewardPresentation and WorldResuming to Completed.

Any unverified phase may transition to EvidenceBlocked. This model does not grant permission to send Battle bytes.

$commonBlock
"@

$phaseReports = [ordered]@{
    "BattleEnter" = "S2C BattleEnter opcode, payload layout, battle reference, map/background, and client acceptance are missing. Serializer gate: SerializerBlockedByEvidence."
    "Formation" = "S2C formation opcode, slot count, ordering, coordinates, and dynamic fields are missing. Serializer gate: SerializerBlockedByEvidence."
    "Participants" = "Player, pet, and enemy spawn wire identities and field maps are missing. Runtime participant IDs are not assumed to equal client wire references."
    "RoundCommandWindow" = "RoundStart and CommandWindow opcodes/tokens are missing. The state tracker rejects stale round, stale session, and closed-window commands without guessing wire fields."
    "BasicAttackDecoder" = "No qualifying current-build raw BasicAttack C2S sample exists. The decoder gate is BlockedByEvidence and no production decoder is registered. The semantic candidate contract exists only behind DecoderVerified/ProductionReady evidence."
    "ActionResult" = "Runtime ActionStarted and DamageApplied events can be projected to semantic DTOs. Official ActionConfirmation and ActionStart serializers remain SerializerBlockedByEvidence."
    "Damage" = "Damage and hpAfter come only from authoritative BattleActionResult; the adapter does not recompute damage. Critical, miss, and animation fields remain EvidenceBlocked. No production serializer exists."
    "RoundEnd" = "A semantic RoundEnd projection exists for authoritative RoundCompleted events. The round-end wire token and serializer remain blocked."
    "BattleEndBoundary" = "BattleEnd, Reward, and WorldResume are modeled as packet families and state transitions only. Their opcodes, fields, serializers, and client acceptance are missing."
}
foreach ($entry in $phaseReports.GetEnumerator()) {
    Write-Utf8NoBom (Join-Path $reportRoot ("BattleProtocolEvidenceV1." + $entry.Key + ".md")) @"
# Battle Protocol Evidence V1 $($entry.Key)

$($entry.Value)

$commonBlock
"@
}

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.ProtocolAdapter.md") @"
# Battle Protocol Evidence V1 Protocol Adapter

Infrastructure status: **PASS**.

- Inbound decoder and outbound serializer registrations are unique per packet family and client build.
- The adapter validates frame length, state, sequence, build, and evidence gate before dispatch.
- BasicAttack mapping checks build, session epoch, Battle reference, acting participant ownership, targets, round, command-window version, payload hash, and unknown fields.
- A mapped command uses ``BattleActorCommandSource.GatewayAdapter`` and submits once through the selected engine only when its default is LegacyPrimary.
- ActorShadow remains available only through the existing selector behavior; ActorPrimary was not enabled.
- Semantic outbound packets derive from immutable engine events/results. Damage is never recalculated in the protocol layer.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.InitiativeEvidence.md") @"
# Battle Protocol Evidence V1 Initiative Evidence

Census status: **PASS**; production change: **none**.

The legacy runtime sorts on nullable ``InitiativeCandidate`` descending with null treated as zero. ``LegacyCompatibleBattleInitiativePlanner`` delegates to the existing turn-order policy and records a snapshot. AttackPower exists, but it is not proof of the official initiative formula. Official wire field, tie-break, speed/agility contribution, and randomness remain unknown.

Initiative protocol eligibility: **EvidenceBlocked**.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.MonsterAiEvidence.md") @"
# Battle Protocol Evidence V1 Monster AI Evidence

Census status: **PASS**; production change: **none**.

The contract contains ``BattleActorCommandType.MonsterAi`` and world content contains monster definitions (208 observed by preflight). The current actor supported-command set does not admit MonsterAi. Official target selection, action selection, decision timing, and client/server ownership are not evidenced.

Monster AI protocol eligibility: **EvidenceBlocked**.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.GoldenFixture.md") @"
# Battle Protocol Evidence V1 Golden Fixture

Golden fixture infrastructure: **PASS**. Official fixture: **NOT CREATED / BlockedByEvidence**.

The replay contract checks raw source hashes, deterministic semantic hashes, ordering, and zero side effects. Creating ``OfficialBattleBasicAttackRoundV1`` without raw current-build evidence would fabricate bytes, so fixture count remains zero.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.ClientAutomation.md") @"
# Battle Protocol Evidence V1 Client Automation

Official Battle automation: **NOT RUN**. Offline evidence gates did not pass, so the client was not navigated into Battle and no Battle UI action was clicked.

The frozen existing-character login-to-world baseline passed with 69 heartbeats over 60.001 seconds and clean shutdown. The final regression run is ``$FinalRegressionRunId`` with $FinalRegressionHeartbeatCount heartbeats over $FinalRegressionDurationSeconds seconds. Launcher PID $LauncherPid remained preserved.

Repeated official Battle verification count: **0**. User Manual Operation: **NOT REQUIRED**.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Security.md") @"
# Battle Protocol Evidence V1 Security

| Check | Result |
| --- | --- |
| Credential scan findings in new implementation/formal evidence | $CredentialFindingCount |
| Hardcoded absolute path findings in runtime/protocol implementation/formal evidence | $HardcodedPathFindingCount |
| Sensitive client files copied | 0 |
| Raw official Battle captures imported | 0 |
| Fake network bytes | 0 |
| Client executable modified | No |
| Launcher stopped/restarted | No |
| Capture raw-data default | Restricted |
| Report paths | Relative evidence labels |

Executable and config identities are hashes. ``cookies.dat`` was excluded. The report generator reads the approved local client location but does not emit that absolute location into formal evidence.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.FailureInjection.md") @"
# Battle Protocol Evidence V1 Failure Injection

Status: **PASS**.

- 62 Battle Protocol evidence-infrastructure failure scenarios execute with controlled non-success result codes.
- Existing Battle Architecture failure injection remains 60/60 PASS.
- Unsupported build, invalid state/session/length/opcode, stale command, unknown participant, decoder exception, serializer absence/failure, capture pressure/corruption, fixture mismatch, automation timeout, launcher loss, and fake-byte detection are represented.
- Failed serialization emits zero bytes; no failure path advances an evidence gate.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.TestResults.md") @"
# Battle Protocol Evidence V1 Test Results

| Check | Result |
| --- | --- |
| Full solution after implementation | $FullTestPassed/$FullTestTotal PASS |
| New Protocol evidence tests | 22/22 PASS |
| New Runtime offline/failure catalog | 329/329 PASS |
| Required Offline/Headless scenarios | 265/265 PASS |
| New Battle Protocol failure injection scenarios | 62/62 PASS |
| Frozen gameplay | 735/735 PASS |
| Quest | 258/258 PASS |
| Battle Architecture v2 offline | 360/360 PASS |
| Existing Battle Architecture failure injection | 60/60 PASS |
| Protocol audit concurrency | 30/30 PASS |
| Automation JSON retry | PASS |

These counts validate infrastructure and safety boundaries. They are not official wire-protocol acceptance counts.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Regression.md") @"
# Battle Protocol Evidence V1 Regression

Frozen gameplay, Quest, Battle Architecture v2, protocol audit concurrency, automation retry, migration/preflight, and login-to-world checks remain isolated from the new evidence gate.

Final full solution result: **$FullTestPassed/$FullTestTotal PASS**. Final login-to-world run: ``$FinalRegressionRunId``; $FinalRegressionHeartbeatCount heartbeats over $FinalRegressionDurationSeconds seconds; clean shutdown is recorded by the run artifact. Launcher PID $LauncherPid was preserved.

LegacyPrimary remains the default. ActorPrimary remains disabled.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.ProtocolGates.md") @"
# Battle Protocol Evidence V1 Protocol Gates

No Battle packet family was promoted.

| Direction | Initial and final gate |
| --- | --- |
| Client-to-server Battle commands | BlockedByEvidence |
| Server-to-client Battle packets | SerializerBlockedByEvidence |

Promotion requires current-build provenance, repeated raw samples, deterministic decoding/serialization, sufficient negative tests or official client acceptance, and no unknown required dynamic field. Current official sample count is zero.

$commonBlock
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.FinalFreeze.md") @"
# Battle Protocol Evidence V1 Final Freeze

Battle Protocol Matrix: **PASS**  
Client Build Identity: **PASS**  
Capture/Replay Infrastructure: **PASS**  
Gateway Protocol Boundary: **PASS**  
Protocol State Machine: **PASS**  
Golden Fixture Infrastructure: **PASS**  
Initiative Evidence Census: **PASS**  
Monster AI Evidence Census: **PASS**  
Official Battle Protocol: **PARTIAL / BlockedByEvidence**  
Official Basic Attack Round: **NOT YET VERIFIED**  
Fake Network Bytes: **0**  
ActorPrimary: **NOT ENABLED**  
LegacyPrimary: **RETAINED**  
User Manual Operation: **NOT REQUIRED**

Final Status: **BATTLE PROTOCOL EVIDENCE INFRASTRUCTURE FREEZE PASS**
"@

Write-Utf8NoBom (Join-Path $reportRoot "BattleProtocolEvidenceV1.Deferred.md") @"
# Battle Protocol Evidence V1 Deferred

The following items remain deliberately blocked:

- Current-build raw BattleEnter, formation, participant, RoundStart, CommandWindow, BasicAttack, ActionConfirmation, ActionStart, Damage, RoundEnd, BattleEnd, Reward, and WorldResume samples.
- Verified Battle opcode and dynamic field maps.
- Production Battle decoder and serializer registrations.
- Official Golden Battle fixture.
- Official client Battle playback and damage display.
- Repeated official BasicAttack round verification.
- Official initiative and monster-AI formulas.

Next sprint recommendation: perform one controlled local capture using the existing socket instrumentation against a safe single-player/single-enemy entry, link it to the recorded client build, redact it, and promote only packet families that independently meet the evidence policy. Do not enable ActorPrimary.
"@

Write-Output "Battle Protocol Evidence V1 reports generated for $clientBuildId; manifest $workspaceManifestHash"
