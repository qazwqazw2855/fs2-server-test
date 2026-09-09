[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ClientExe,
    [Parameter(Mandatory = $true)]
    [uint32] $TargetProcessId,
    [Parameter(Mandatory = $true)]
    [string] $LaunchChainAttestationPath,
    [string] $RunId = "official-runtime-" + (Get-Date -Format "yyyyMMdd-HHmmss"),
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateRange(4, 600)]
    [int] $ObserveSeconds = 8,
    [switch] $EnablePreAttachDeepStaticDiscovery,
    [switch] $EnableContractAcquisition,
    [ValidateRange(4, 600)]
    [int] $ContractAcquisitionObserveSeconds = 48,
    [string] $ContractAcquisitionControlPath = '',
    [string] $DeepEvidencePlanPath = '',
    [string] $RingSessionRoot = ''
)

$ErrorActionPreference = "Stop"
$expectedSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"
$expectedVersion = "1.0.0.1"

function Get-PeMachine([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x40) { return 0 }
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($pe -lt 0 -or $pe + 6 -gt $bytes.Length) { return 0 }
    return [BitConverter]::ToUInt16($bytes, $pe + 4)
}

function Write-Utf8NoBom([string] $Path, [string] $Content) {
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false, $true))
}

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
. (Join-Path $PSScriptRoot 'OfficialRuntimeControl.ps1')
. (Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\SemanticEventJson.ps1')
$clientPath = (Resolve-Path -LiteralPath $ClientExe).Path
$clientItem = Get-Item -LiteralPath $clientPath
$clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
$clientVersion = $clientItem.VersionInfo.FileVersion
if ([IO.Path]::GetFileName($clientPath) -ine "God2_opt.exe" -or
    $clientSha256 -cne $expectedSha256 -or
    $clientVersion -cne $expectedVersion -or
    (Get-PeMachine $clientPath) -ne 0x14c) {
    throw "Official client identity mismatch. Expected God2_opt.exe/x86/$expectedVersion/$expectedSha256."
}
$attestationSourcePath = (Resolve-Path -LiteralPath $LaunchChainAttestationPath).Path
$repoPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
if (-not $attestationSourcePath.StartsWith($repoPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Launcher automation attestation must be stored under the repository root.'
}
$launchChain = Get-Content -LiteralPath $attestationSourcePath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ([string]$launchChain.SchemaId -cne 'God2OfficialLauncherAutomationAttestation' -or
    [int]$launchChain.SchemaVersion -ne 1 -or
    -not [bool]$launchChain.Passed -or [bool]$launchChain.DirectClientLaunch -or
    -not [bool]$launchChain.AgreementAccepted -or
    -not [bool]$launchChain.StartGameClicked -or
    -not [bool]$launchChain.DirectSoundDialogObserved -or
    -not [bool]$launchChain.DirectSoundDialogHandled -or
    -not [bool]$launchChain.FormalAttachTimingAllowed -or
    [uint32]$launchChain.ClientProcessId -ne $TargetProcessId -or
    [string]$launchChain.ClientSHA256 -cne $expectedSha256 -or
    [string]$launchChain.ClientVersion -cne $expectedVersion -or
    [IO.Path]::GetFileName([string]$launchChain.LauncherPath) -ine 'Launcher.exe') {
    throw 'Trusted official Launcher.exe agreement/login-chain attestation is invalid or incomplete.'
}
$launcherPath = (Resolve-Path -LiteralPath ([string]$launchChain.LauncherPath)).Path
$launcherSha256 = (Get-FileHash -LiteralPath $launcherPath -Algorithm SHA256).Hash
if ($launcherSha256 -cne [string]$launchChain.LauncherSHA256) {
    throw 'Launcher.exe changed after the automation host attested the launch chain.'
}
$launcherProcessId = [uint32]$launchChain.LauncherProcessId

$engine = Join-Path $repoRoot 'Release\God2SemanticRecoveryEngine.exe'
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    $engine = Join-Path $repoRoot 'artifacts\release\God2SemanticRecoveryEngine.exe'
}
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    $engine = Join-Path $repoRoot 'God2SemanticRecoveryEngine.exe'
}
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    throw "Release semantic recovery engine is missing: $engine"
}
if ($EnableContractAcquisition -and -not $EnablePreAttachDeepStaticDiscovery) {
    throw 'Contract Acquisition requires pre-attach deep static discovery.'
}
if (-not [string]::IsNullOrWhiteSpace($DeepEvidencePlanPath) -and
    -not $EnableContractAcquisition) {
    throw 'A deep evidence plan requires Contract Acquisition.'
}
$resolvedDeepEvidencePlanPath = $null
if (-not [string]::IsNullOrWhiteSpace($DeepEvidencePlanPath)) {
    $resolvedDeepEvidencePlanPath = (Resolve-Path -LiteralPath $DeepEvidencePlanPath).Path
    if (-not $resolvedDeepEvidencePlanPath.StartsWith($repoPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Deep evidence plan must be stored under the repository root.'
    }
}

$runDir = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LoginTrial\$RunId"
if (Test-Path -LiteralPath $runDir) {
    throw "Refusing to overwrite an existing official runtime: $runDir"
}
$sensitiveDir = Join-Path $runDir "sensitive"
$analysisDir = Join-Path $runDir "analysis"
New-Item -ItemType Directory -Path $runDir, $sensitiveDir, $analysisDir | Out-Null
$generalLog = Join-Path $runDir "general.log"
$metadata = Join-Path $runDir "metadata.jsonl"
$resultPath = Join-Path $runDir "injector-result-v2.json"
$attachPath = Join-Path $analysisDir "attach-result-v2.json"
$summaryPath = Join-Path $analysisDir "official-runtime-summary.json"
$packagePath = Join-Path $analysisDir "current-session-evidence.zip"
$launchChainPath = Join-Path $analysisDir "launcher-client-chain-v1.json"
$ringSessionRoot = if ([string]::IsNullOrWhiteSpace($RingSessionRoot)) {
    Join-Path $env:LOCALAPPDATA ("God2Classic\PacketCapture\Captures\" + $RunId)
} else {
    $resolvedRingSessionRoot = [IO.Path]::GetFullPath($RingSessionRoot)
    if (-not $resolvedRingSessionRoot.StartsWith($repoPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Explicit production-ring session root must be stored under the repository root.'
    }
    $resolvedRingSessionRoot
}
if (Test-Path -LiteralPath $ringSessionRoot) {
    throw "Refusing to overwrite an existing production-ring session: $ringSessionRoot"
}
$headlessControllerPath = Join-Path $ringSessionRoot 'reports\headless-enhanced-capture.json'
$ringGeneralLog = Join-Path $ringSessionRoot 'reports\enhanced-capture.log'
$sharedRingHealthPath = Join-Path $ringSessionRoot 'reports\semantic-shared-ring.json'
$segmentManifestPath = Join-Path $ringSessionRoot 'reports\semantic-segment-manifest.json'
$productionRingHealthPath = Join-Path $analysisDir 'shared-ring-production-health.json'
$expectedAcquisitionControlPath = Join-Path $analysisDir 'contract-acquisition-control.json'
if (-not [string]::IsNullOrWhiteSpace($ContractAcquisitionControlPath)) {
    $ContractAcquisitionControlPath = [IO.Path]::GetFullPath($ContractAcquisitionControlPath)
    if (-not $ContractAcquisitionControlPath.Equals(
            [IO.Path]::GetFullPath($expectedAcquisitionControlPath),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Contract Acquisition control path is outside the current official runtime analysis directory.'
    }
}
$ringSemanticPath = Join-Path $ringSessionRoot 'raw\semantic-events.jsonl'
$semanticPath = Join-Path $sensitiveDir 'semantic-events.jsonl'
$game = $null
$injectorProcess = $null
$headlessStopEvent = $null
$knownDirectSoundDialogDismissed = [bool]$launchChain.DirectSoundDialogHandled
$dialogDismissedAtUtc = [string]$launchChain.DirectSoundDialogHandledAtUtc
$formalAttachTimingStartedAtUtc = $null
$attachObservedAtUtc = $null
$detachObservedAtUtc = $null
$preAttachDiscoveryPath = $null
$preAttachDiscoveryBindingPath = $null
$acquisitionRoot = $null
$acquisitionManifestPath = $null
$acquisitionResult = $null
$launcherParentVerified = [bool]$launchChain.ParentIsVerifiedLauncher
$attachEstablished = $false
$strictControllerShutdownComplete = $false
$headlessObserveSeconds = 0

try {
    $game = Get-Process -Id $TargetProcessId -ErrorAction Stop
    $game.Refresh()
    $actualGamePath = $game.Path
    if (-not [string]::Equals([IO.Path]::GetFullPath($actualGamePath),
            [IO.Path]::GetFullPath($clientPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Launcher created an unexpected client path: $actualGamePath"
    }
    $gameProcessInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$($game.Id)"
    $launcherParentVerified = $launcherParentVerified -and $null -ne $gameProcessInfo -and
        [uint32]$gameProcessInfo.ParentProcessId -eq $launcherProcessId -and
        [uint32]$launchChain.ClientParentProcessId -eq $launcherProcessId
    if (-not $launcherParentVerified) {
        throw "God2_opt.exe parent PID was not the attested official Launcher.exe PID $launcherProcessId."
    }
    if (-not $knownDirectSoundDialogDismissed) {
        throw 'Automation Host did not attest DirectSound dialog handling; formal attach timing was not started.'
    }
    Write-Utf8NoBom $launchChainPath ((Get-Content -LiteralPath $attestationSourcePath -Raw -Encoding UTF8).TrimEnd() + "`n")
    Start-Sleep -Milliseconds 1500
    $game.Refresh()
    if ($game.HasExited) { throw "Official client exited before instrumentation." }
    $creationTime = $game.StartTime.ToUniversalTime().ToFileTimeUtc()
    # Capture the bounded runtime image before installing any hook. Otherwise
    # the exact seed callsites and entry bytes describe our instrumentation
    # patches instead of the official client image.
    if ($EnablePreAttachDeepStaticDiscovery) {
        $discoveryScript = Join-Path $repoRoot `
            'tools\God2.PacketCapture\ultimate\Invoke-DeepProbeStaticDiscovery.ps1'
        if (-not (Test-Path -LiteralPath $discoveryScript -PathType Leaf)) {
            throw "Deep static discovery script is missing: $discoveryScript"
        }
        $preAttachDiscoveryPath = Join-Path $analysisDir `
            'deep-probe-candidate-map-v3.json'
        $discoveryResult = & $discoveryScript -TargetExecutable $clientPath `
            -TargetProcessId $game.Id -OutputPath $preAttachDiscoveryPath
        if (-not [bool]$discoveryResult.Passed -or
            [int]$discoveryResult.CandidateCount -lt 21 -or
            [int]$discoveryResult.ActivationAllowedCount -ne 0) {
            throw 'Pre-attach deep static discovery did not satisfy its fail-closed contract.'
        }
        $preAttachDiscoveryBindingPath = Join-Path $analysisDir `
            'deep-probe-candidate-map-v3.binding.json'
        $discoveryBinding = [ordered]@{
            SchemaId = 'God2DeepStaticDiscoveryBinding'
            SchemaVersion = 1
            GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            SessionId = $RunId
            AuthorityProfile = 'ExactBinaryStaticAnalysis'
            ClientProcessId = [uint32]$game.Id
            ClientProcessCreationTime = $game.StartTime.ToUniversalTime().ToString('o')
            TargetExecutable = 'God2_opt.exe'
            TargetVersion = $expectedVersion
            TargetSHA256 = $expectedSha256
            CandidateMapPath = [IO.Path]::GetFullPath($preAttachDiscoveryPath).Substring(
                [IO.Path]::GetFullPath($repoRoot).TrimEnd('\').Length + 1).Replace('\','/')
            CandidateMapSHA256 = [string]$discoveryResult.OutputSHA256
            CandidateCount = [int]$discoveryResult.CandidateCount
            ActivationAllowedCount = [int]$discoveryResult.ActivationAllowedCount
            MemoryWritten = $false
            InstrumentationAttachedDuringRead = $false
            Passed = $true
        }
        Write-Utf8NoBom $preAttachDiscoveryBindingPath `
            (($discoveryBinding | ConvertTo-Json -Depth 6) + "`n")
    }

    # The host does not enter credentials until ATTACHED is published, so the
    # pre-attach snapshot cannot consume a live authenticated game session.
    $formalAttachTimingStartedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $headlessStopEventName = 'Local\God2SemanticRecovery.OfficialHeadlessStop.' +
        [guid]::NewGuid().ToString('N')
    $headlessStopEventCreated = $false
    $headlessStopEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset,
        $headlessStopEventName, [ref]$headlessStopEventCreated)
    if (-not $headlessStopEventCreated) {
        throw 'Official headless stop event identity unexpectedly already existed.'
    }
    $headlessObserveSeconds = [Math]::Min(600, $(if ($EnableContractAcquisition) {
        $ContractAcquisitionObserveSeconds + 150
    } else { $ObserveSeconds + 30 }))
    $arguments = @(
        '--internal-headless-enhanced-capture',
        '--session', ('"' + $ringSessionRoot + '"'),
        '--session-id', $RunId,
        '--game', ('"' + $clientPath + '"'),
        '--pid', $game.Id,
        '--stop-event', ('"' + $headlessStopEventName + '"'),
        '--observe-seconds', $headlessObserveSeconds
    )
    $injectorProcess = Start-Process -FilePath $engine `
        -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runDir 'headless-engine.stdout.txt') `
        -RedirectStandardError (Join-Path $runDir 'headless-engine.stderr.txt')

    $deadline = (Get-Date).AddSeconds(90)
    $attachRecord = $null
    do {
        if (Test-Path -LiteralPath $headlessControllerPath) {
            try {
                $raw = [IO.File]::ReadAllText($headlessControllerPath)
                $record = $raw | ConvertFrom-Json
                $strictCapabilityReady = $false
                if (Test-Path -LiteralPath $ringGeneralLog -PathType Leaf) {
                    try {
                        $capabilityText = [IO.File]::ReadAllText($ringGeneralLog)
                        $strictCapabilityReady = $capabilityText -match
                            'evidence capabilities status=STRICT_READY encryptedTransport=0 postDecrypt=1 preEncrypt=1 handlerDecoded=1 battleActorMemory=1 battleStateMemory=1 networkOnly=0 sharedRingRequired=1'
                    } catch [System.IO.IOException] { }
                }
                if ([string]$record.Status -ceq 'ATTACHED' -and
                    [bool]$record.Attached -and $strictCapabilityReady) {
                    $attachRecord = [pscustomobject][ordered]@{
                        schemaVersion='god2-official-headless-attach-v1';status='ATTACHED'
                        code=0;targetProcessId=[uint32]$game.Id
                        sessionId=$RunId;productionRingHost='God2SemanticRecoveryEngine.exe'
                        sharedRingBound=$true;encryptedTransportCaptured=$false
                        postDecryptCaptured=$true;preEncryptCaptured=$true
                        handlerDecodedCaptured=$true;battleActorMemoryCaptured=$true
                        battleStateMemoryCaptured=$true;strictCapabilitiesReady=$true
                        observedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
                    }
                    Write-Utf8NoBom $attachPath (($attachRecord | ConvertTo-Json -Depth 6) + "`n")
                    $attachObservedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                    $attachEstablished = $true
                    break
                }
                if ([string]$record.Status -like 'EVIDENCE_BLOCKED*') {
                    throw "Release engine returned $($record.Status): $($record.Error)"
                }
            } catch [System.IO.IOException] { }
        }
        Start-Sleep -Milliseconds 200
        $injectorProcess.Refresh()
    } while ((Get-Date) -lt $deadline -and -not $injectorProcess.HasExited)
    if ($null -eq $attachRecord) { throw 'Release engine did not publish ATTACHED with STRICT_READY decrypted-packet and battle-memory capabilities within 90 seconds.' }

    if ($EnableContractAcquisition) {
        if (-not [string]::IsNullOrWhiteSpace($ContractAcquisitionControlPath)) {
            [void](Wait-God2OfficialRuntimeControl `
                -Path $ContractAcquisitionControlPath -SessionId $RunId `
                -ClientProcessId ([uint32]$game.Id) `
                -AcceptedStatuses @('WORLD_SCREEN_READY','WORLD_READY') `
                -TimeoutSeconds 240 -Phase 'WORLD_SCREEN_READY')
        }
        $acquisitionScript = Join-Path $repoRoot $(if($resolvedDeepEvidencePlanPath){
            'tools\God2.PacketCapture\ultimate\Invoke-DeepContractEvidenceCampaign.ps1'
        }else{
            'tools\God2.PacketCapture\ultimate\Invoke-ContractAcquisitionCampaign.ps1'
        })
        if (-not (Test-Path -LiteralPath $acquisitionScript -PathType Leaf)) {
            throw "Contract Acquisition script is missing: $acquisitionScript"
        }
        $acquisitionRoot = Join-Path $analysisDir 'contract-acquisition'
        if($resolvedDeepEvidencePlanPath){
            $acquisitionResult = & $acquisitionScript -TargetExecutable $clientPath `
                -TargetProcessId $game.Id -CandidateMapV3 $preAttachDiscoveryPath `
                -EvidencePlanPath $resolvedDeepEvidencePlanPath `
                -OutputRoot $acquisitionRoot -SessionId $RunId `
                -ObserveSeconds $ContractAcquisitionObserveSeconds
        }else{
            $acquisitionResult = & $acquisitionScript -TargetExecutable $clientPath `
                -TargetProcessId $game.Id -CandidateMapV3 $preAttachDiscoveryPath `
                -OutputRoot $acquisitionRoot -SessionId $RunId `
                -ObserveSeconds $ContractAcquisitionObserveSeconds
        }
        if (-not [bool]$acquisitionResult.Passed -or
            [int]$acquisitionResult.FundamentalDomainCount -ne 11 -or
            [int]$acquisitionResult.CandidateCount -ne 11 -or
            [int]$acquisitionResult.ActivationAllowedCount -ne 0 -or
            [int]$acquisitionResult.PromotionEligibleCount -ne 0) {
            throw 'Contract Acquisition failed or attempted candidate promotion.'
        }
        $acquisitionManifestPath = Join-Path $acquisitionRoot `
            'contract-acquisition-campaign-manifest.json'
        if (-not [string]::IsNullOrWhiteSpace($ContractAcquisitionControlPath)) {
            # Keep capture active after the bounded acquisition campaign. The host
            # still needs the same packet stream to complete its 60-second world
            # cadence and bootstrap checks before strict drain/unload may begin.
            [void](Wait-God2OfficialRuntimeControl `
                -Path $ContractAcquisitionControlPath -SessionId $RunId `
                -ClientProcessId ([uint32]$game.Id) -AcceptedStatuses @('WORLD_READY') `
                -TimeoutSeconds 120 -Phase 'final WORLD_READY')
        }
    } else {
        Start-Sleep -Seconds $ObserveSeconds
    }
    if (-not $headlessStopEvent.Set()) {
        throw 'Could not signal the same-session Release engine Stop/drain/unload event.'
    }
    if (-not $injectorProcess.WaitForExit(90000)) {
        throw 'Release engine did not finish strict Stop/drain/unload within 90 seconds after acquisition.'
    }
    $controller = Get-Content -LiteralPath $headlessControllerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$controller.Status -cne 'DETACHED' -or
        -not [bool]$controller.StrictUnloadVerified) {
        throw 'Release engine exited without verified DETACHED strict unload evidence.'
    }
    $strictControllerShutdownComplete = $true
    $targetAliveAfterDetach = $null -ne (Get-Process -Id $game.Id -ErrorAction SilentlyContinue)
    $detachRecord = [pscustomobject][ordered]@{
        schemaVersion='god2-official-headless-detach-v1';status=[string]$controller.Status
        code=[int]$injectorProcess.ExitCode;targetProcessId=[uint32]$game.Id
        strictUnloadVerified=[bool]$controller.StrictUnloadVerified
        moduleAbsent=[bool]$controller.StrictUnloadVerified
        targetAliveAfterDetach=$targetAliveAfterDetach
        productionRingDrained=([string]$controller.Status -ceq 'DETACHED')
        observedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-Utf8NoBom $resultPath (($detachRecord | ConvertTo-Json -Depth 6) + "`n")
    $detachObservedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $ringNativePath = Join-Path $ringSessionRoot 'raw\enhanced-x86\native-probe-selftest.json'
    $ringCandidatePath = Join-Path $ringSessionRoot 'raw\enhanced-x86\deep-probe-candidate-map.json'
    $ringMetadata = Join-Path $ringSessionRoot 'raw\injected-packets.jsonl'
    $nativePath = Join-Path $sensitiveDir 'native-probe-selftest.json'
    $candidatePath = Join-Path $sensitiveDir 'deep-probe-candidate-map.json'
    foreach ($copy in @(
        @($ringNativePath,$nativePath), @($ringCandidatePath,$candidatePath),
        @($ringSemanticPath,$semanticPath), @($ringGeneralLog,$generalLog),
        @($ringMetadata,$metadata),
        @($sharedRingHealthPath,(Join-Path $analysisDir 'semantic-shared-ring-v4.json')),
        @($segmentManifestPath,(Join-Path $analysisDir 'semantic-segment-manifest.json'))
    )) {
        if (Test-Path -LiteralPath $copy[0] -PathType Leaf) {
            Copy-Item -LiteralPath $copy[0] -Destination $copy[1]
        }
    }
    $relativePrefixLength = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\').Length + 1
    $readyLines = if (Test-Path -LiteralPath $generalLog) {
        @(Get-Content -LiteralPath $generalLog | Where-Object {
            $_ -match "probe ready|capture-activation|ExtendedReady"
        } | ForEach-Object {
            # Get-Content decorates strings with PSPath/PSDrive metadata.
            # Copy the scalar so ConvertTo-Json cannot recurse through those
            # provider objects and consume unbounded memory.
            [string]::Copy([string]$_)
        })
    } else { @() }

    if (-not (Test-Path -LiteralPath $sharedRingHealthPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $segmentManifestPath -PathType Leaf)) {
        throw 'Production shared-ring health or segment manifest was not emitted by the release engine.'
    }
    $ring = Get-Content -LiteralPath $sharedRingHealthPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $segments = Get-Content -LiteralPath $segmentManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $pendingAfterDrain = [int64]$ring.Accepted - [int64]$ring.Consumed
    $sequenceGaps = if ([bool]$segments.SequenceContinuity) { 0 } else { 1 }
    $firstSequence = if (@($segments.Segments).Count -gt 0) { [uint64]$segments.Segments[0].FirstSequence } else { 0 }
    $lastSequence = if (@($segments.Segments).Count -gt 0) { [uint64]$segments.Segments[-1].LastSequence } else { 0 }
    $ringPassed = [string]$ring.SchemaVersion -ceq 'god2-semantic-shared-ring-health-v4' -and
        [string]$ring.Lifecycle -ceq 'StoppedAndDrained' -and
        [int64]$ring.Attempted -gt 0 -and [int64]$ring.Accepted -eq [int64]$ring.Consumed -and
        [int64]$ring.Lane00Dropped -eq 0 -and [int64]$ring.Lane01Dropped -eq 0 -and
        [int64]$ring.WriteFailures -eq 0 -and $sequenceGaps -eq 0 -and
        $pendingAfterDrain -eq 0 -and -not [bool]$ring.ConsumerIoFailure -and
        [int64]$ring.InvalidPayloads -eq 0 -and [string]$segments.Status -ceq 'PASS'
    $productionRing = [ordered]@{
        SchemaVersion='god2-shared-ring-production-health-v1'
        GeneratedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        AuthorityProfile='CurrentOfficialClientLive';SessionId=$RunId
        ClientProcessId=[uint32]$game.Id;ClientSHA256=$expectedSha256
        SourceSchemaVersion=[string]$ring.SchemaVersion
        SourceHealthSHA256=(Get-FileHash -LiteralPath $sharedRingHealthPath -Algorithm SHA256).Hash
        Attempted=[uint64]$ring.Attempted;Accepted=[uint64]$ring.Accepted
        Consumed=[uint64]$ring.Consumed
        DroppedP0=[uint64]$ring.Lane00Dropped;DroppedP1=[uint64]$ring.Lane01Dropped
        DroppedP2=[uint64]$ring.Lane02Dropped;DroppedP3=[uint64]$ring.Lane03Dropped
        WriteFailures=[uint64]$ring.WriteFailures
        P0WriteFailures=if([int64]$ring.WriteFailures -eq 0){0}else{$null}
        P1WriteFailures=if([int64]$ring.WriteFailures -eq 0){0}else{$null}
        SequenceGaps=$sequenceGaps;FirstSequence=$firstSequence;LastSequence=$lastSequence
        ConsumerLag=[int64]$ring.ConsumerLag;QueueHighWater=[uint64]$ring.HighWaterMark
        PendingAfterDrain=$pendingAfterDrain;SegmentCount=[int]$segments.SegmentCount
        SemanticIncomplete=(-not $ringPassed);ProductionHealthBound=$true
        ServerReady=$false;DatabaseReady=$false
        Status=if($ringPassed){'PASS'}else{'EVIDENCE_BLOCKED_PRODUCTION_RING_HEALTH'}
        Passed=[bool]$ringPassed
    }
    Write-Utf8NoBom $productionRingHealthPath (($productionRing | ConvertTo-Json -Depth 8) + "`n")
    $nativeRelativePath = if (Test-Path -LiteralPath $nativePath) {
        [IO.Path]::GetFullPath($nativePath).Substring(
            $relativePrefixLength).Replace('\', '/')
    } else { "" }
    $candidateRelativePath = if (Test-Path -LiteralPath $candidatePath) {
        [IO.Path]::GetFullPath($candidatePath).Substring(
            $relativePrefixLength).Replace('\', '/')
    } else { "" }
    $semanticRelativePath = if (Test-Path -LiteralPath $semanticPath) {
        [IO.Path]::GetFullPath($semanticPath).Substring(
            $relativePrefixLength).Replace('\', '/')
    } else { "" }
    $runtimeEvents = [Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $semanticPath -PathType Leaf) {
        foreach ($eventLine in Get-Content -LiteralPath $semanticPath) {
            if ([string]::IsNullOrWhiteSpace($eventLine)) { continue }
            [void]$runtimeEvents.Add((ConvertFrom-God2SemanticEventJson -Json $eventLine))
        }
    }
    $promotionEventTypes = @(
        'ObjectAllocated','ObjectDestroyed','ObjectResolved','ObjectLookup',
        'RegistryLocated','RegistryEnumerated','ResourceRead','ResourceDecoded',
        'ResourceDeserialized','StateMutation','TaintSeed','TaintPropagation',
        'ValueFlow','FormulaOperand','FormulaResult','SnapshotObject','SnapshotEdge'
    )
    $promotionEligible = @($runtimeEvents | Where-Object {
        [string]$_.EventType -cin $promotionEventTypes -and
        [string]$_.AuthorityHint -ceq 'VERIFIED'
    }).Count -gt 0
    $packageInputs = @(@($generalLog,$metadata,$resultPath,$attachPath,$launchChainPath,$nativePath,
        $candidatePath,$semanticPath,$preAttachDiscoveryPath,$preAttachDiscoveryBindingPath,
        $productionRingHealthPath,(Join-Path $analysisDir 'semantic-shared-ring-v4.json'),
        (Join-Path $analysisDir 'semantic-segment-manifest.json')) | Where-Object {
            Test-Path -LiteralPath $_ -PathType Leaf
        })
    if ($packageInputs.Count -eq 0) {
        throw "Current official session produced no package inputs."
    }
    $packageStaging = Join-Path $analysisDir 'current-session-evidence-staging'
    $packageCore = Join-Path $packageStaging 'core'
    New-Item -ItemType Directory -Path $packageStaging,$packageCore | Out-Null
    foreach ($packageInput in $packageInputs) {
        Copy-Item -LiteralPath $packageInput -Destination `
            (Join-Path $packageCore ([IO.Path]::GetFileName($packageInput)))
    }
    if ($acquisitionRoot -and (Test-Path -LiteralPath $acquisitionRoot -PathType Container)) {
        Copy-Item -LiteralPath $acquisitionRoot -Destination `
            (Join-Path $packageStaging 'contract-acquisition') -Recurse
    }
    Compress-Archive -Path (Join-Path $packageStaging '*') -DestinationPath $packagePath `
        -CompressionLevel Optimal
    $packageRelativePath = [IO.Path]::GetFullPath($packagePath).Substring(
        $relativePrefixLength).Replace('\', '/')
    $summary = [ordered]@{
        SchemaId = "God2OfficialClientRuntime"
        SchemaVersion = 2
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        AuthorityProfile = "CurrentOfficialClientLive"
        SessionId = $RunId
        ClientPath = $clientPath
        ClientArchitecture = "x86"
        ClientSHA256 = $clientSha256
        ClientVersion = $clientVersion
        ClientProcessId = $game.Id
        ClientProcessCreationTime = [string]$creationTime
        LauncherPath = $launcherPath
        LauncherSHA256 = $launcherSha256
        LauncherProcessId = $launcherProcessId
        LauncherClientChainPath = [IO.Path]::GetFullPath($launchChainPath).Substring(
            $relativePrefixLength).Replace('\', '/')
        LauncherClientChainSHA256 = (Get-FileHash -LiteralPath $launchChainPath -Algorithm SHA256).Hash
        LauncherParentVerified = [bool]$launcherParentVerified
        DirectClientLaunch = $false
        KnownDirectSoundDialogDismissed = $knownDirectSoundDialogDismissed
        DialogDismissedAtUtc = $dialogDismissedAtUtc
        FormalAttachTimingStartedAtUtc = $formalAttachTimingStartedAtUtc
        AttachObservedAtUtc = $attachObservedAtUtc
        DetachObservedAtUtc = $detachObservedAtUtc
        CurrentSessionObserved = $true
        HistoricalEvidenceReused = $false
        FixtureOnly = $false
        PromotionEligible = [bool]$promotionEligible
        RuntimeEventCount = [uint64]$runtimeEvents.Count
        ContractAcquisitionEnabled = [bool]$EnableContractAcquisition
        DeepEvidencePlanEnabled = ($null -ne $resolvedDeepEvidencePlanPath)
        DeepEvidencePlanPath = if($resolvedDeepEvidencePlanPath){
            $resolvedDeepEvidencePlanPath.Substring($relativePrefixLength).Replace('\','/')
        }else{''}
        DeepEvidencePlanSHA256 = if($resolvedDeepEvidencePlanPath){
            (Get-FileHash -LiteralPath $resolvedDeepEvidencePlanPath -Algorithm SHA256).Hash
        }else{''}
        ContractAcquisitionObserveSeconds = if ($EnableContractAcquisition) {
            $ContractAcquisitionObserveSeconds
        } else { 0 }
        ContractAcquisitionCandidateCount = if ($acquisitionResult) { [int]$acquisitionResult.CandidateCount } else { 0 }
        ContractAcquisitionFundamentalDomainCount = if ($acquisitionResult) { [int]$acquisitionResult.FundamentalDomainCount } else { 0 }
        ContractAcquisitionRuntimeObservationCount = if ($acquisitionResult) { [int]$acquisitionResult.RuntimeObservationCount } else { 0 }
        ContractAcquisitionRuntimeObservedDomainCount = if ($acquisitionResult) { [int]$acquisitionResult.RuntimeObservedDomainCount } else { 0 }
        ContractAcquisitionActivationAllowedCount = if ($acquisitionResult) { [int]$acquisitionResult.ActivationAllowedCount } else { 0 }
        ContractAcquisitionPromotionEligibleCount = if ($acquisitionResult) { [int]$acquisitionResult.PromotionEligibleCount } else { 0 }
        ContractAcquisitionStatus = if ($acquisitionResult) { [string]$acquisitionResult.Status } else { 'NOT_RUN' }
        ContractAcquisitionManifestPath = if ($acquisitionManifestPath) {
            [IO.Path]::GetFullPath($acquisitionManifestPath).Substring($relativePrefixLength).Replace('\', '/')
        } else { '' }
        ContractAcquisitionManifestSHA256 = if ($acquisitionManifestPath) {
            (Get-FileHash -LiteralPath $acquisitionManifestPath -Algorithm SHA256).Hash
        } else { '' }
        ProductionRingHealthPath = [IO.Path]::GetFullPath($productionRingHealthPath).Substring(
            $relativePrefixLength).Replace('\', '/')
        ProductionRingHealthSHA256 = (Get-FileHash -LiteralPath $productionRingHealthPath -Algorithm SHA256).Hash
        ProductionRingHealthPassed = [bool]$productionRing.Passed
        ProductionRingAttempted = [uint64]$productionRing.Attempted
        ProductionRingAccepted = [uint64]$productionRing.Accepted
        ProductionRingConsumed = [uint64]$productionRing.Consumed
        ProductionRingDroppedP0 = [uint64]$productionRing.DroppedP0
        ProductionRingDroppedP1 = [uint64]$productionRing.DroppedP1
        ProductionRingSequenceGaps = [uint64]$productionRing.SequenceGaps
        ProductionRingPendingAfterDrain = [int64]$productionRing.PendingAfterDrain
        ProductionRingSegmentCount = [uint32]$productionRing.SegmentCount
        AttachStatus = [string]$attachRecord.status
        DetachStatus = [string]$detachRecord.status
        AttachRecordPath = [IO.Path]::GetFullPath($attachPath).Substring(
            $relativePrefixLength).Replace('\', '/')
        AttachRecordSHA256 = (Get-FileHash -LiteralPath $attachPath -Algorithm SHA256).Hash
        DetachRecordPath = [IO.Path]::GetFullPath($resultPath).Substring(
            $relativePrefixLength).Replace('\', '/')
        DetachRecordSHA256 = (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash
        StrictUnloadVerified = [bool]$detachRecord.strictUnloadVerified
        ModuleAbsent = [bool]$detachRecord.moduleAbsent
        TargetAliveAfterDetach = $targetAliveAfterDetach
        NativeReportPath = $nativeRelativePath
        NativeReportSHA256 = if (Test-Path -LiteralPath $nativePath) {
            (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
        } else { "" }
        CandidateMapPath = $candidateRelativePath
        CandidateMapSHA256 = if (Test-Path -LiteralPath $candidatePath) {
            (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
        } else { "" }
        SemanticEventsPath = $semanticRelativePath
        SemanticEventsSHA256 = if (Test-Path -LiteralPath $semanticPath) {
            (Get-FileHash -LiteralPath $semanticPath -Algorithm SHA256).Hash
        } else { "" }
        ResultPackagePath = $packageRelativePath
        ResultPackageSHA256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        ReadyLines = $readyLines
        Passed = [string]$attachRecord.status -ceq "ATTACHED" -and
            [string]$detachRecord.status -ceq "DETACHED" -and
            $knownDirectSoundDialogDismissed -and
            $launcherParentVerified -and
            [bool]$detachRecord.strictUnloadVerified -and
            [bool]$detachRecord.moduleAbsent -and $targetAliveAfterDetach -and
            [bool]$productionRing.Passed -and
            (-not $EnableContractAcquisition -or
                ([bool]$acquisitionResult.Passed -and
                 [int]$acquisitionResult.FundamentalDomainCount -eq 11 -and
                 [int]$acquisitionResult.ActivationAllowedCount -eq 0 -and
                 [int]$acquisitionResult.PromotionEligibleCount -eq 0)) -and
            $runtimeEvents.Count -gt 0 -and
            (Test-Path -LiteralPath $packagePath -PathType Leaf)
    }
    Write-Utf8NoBom $summaryPath (($summary | ConvertTo-Json -Depth 8) + "`n")
    $summary | ConvertTo-Json -Depth 8
    if (-not $summary.Passed) { exit 1 }
} finally {
    $cleanupFailure = $null
    if ($null -ne $headlessStopEvent) {
        try {
            if (-not $headlessStopEvent.Set()) {
                $cleanupFailure = 'Could not signal the same-session Stop/drain/unload event during cleanup.'
            }
        } catch {
            $cleanupFailure = 'Could not signal the same-session Stop/drain/unload event during cleanup: ' +
                $_.Exception.Message
        }
    }
    if ($null -ne $injectorProcess) {
        $injectorProcess.Refresh()
        if (-not $injectorProcess.HasExited) {
            $cleanupWaitMilliseconds = [Math]::Max(90000,
                ([Math]::Min(630, $headlessObserveSeconds + 30) * 1000))
            if (-not $injectorProcess.WaitForExit($cleanupWaitMilliseconds)) {
                $cleanupFailure = 'Release engine did not finish Stop/drain/unload during cleanup; refusing unsafe forced termination.'
            }
        }
        $injectorProcess.Refresh()
        if ($injectorProcess.HasExited -and $attachEstablished -and
            -not $strictControllerShutdownComplete -and
            (Test-Path -LiteralPath $headlessControllerPath -PathType Leaf)) {
            try {
                $cleanupController = Get-Content -LiteralPath $headlessControllerPath -Raw -Encoding UTF8 |
                    ConvertFrom-Json
                $strictControllerShutdownComplete =
                    [string]$cleanupController.Status -ceq 'DETACHED' -and
                    [bool]$cleanupController.StrictUnloadVerified
            } catch {
                $cleanupFailure = 'Could not verify controller cleanup evidence: ' + $_.Exception.Message
            }
        }
        if ($attachEstablished -and -not $strictControllerShutdownComplete -and
            [string]::IsNullOrWhiteSpace($cleanupFailure)) {
            $cleanupFailure = 'Attached controller did not publish verified DETACHED strict unload evidence during cleanup.'
        }
    }
    if ($null -ne $headlessStopEvent) {
        $headlessStopEvent.Dispose()
    }
    # Launcher and client lifetime are owned by the elevated Automation Host.
    # This attach-only runner must never terminate either process.
    if (-not [string]::IsNullOrWhiteSpace($cleanupFailure)) {
        throw $cleanupFailure
    }
}
