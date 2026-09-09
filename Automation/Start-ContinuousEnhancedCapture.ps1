param(
    [ValidateRange(30, 7200)]
    [int] $ObserveSeconds = 600,
    [string] $SessionPrefix = "uu-live",
    [string] $ExpectedClientPath = "",
    [string] $ExpectedClientSha256 = "",
    [switch] $MonsterCapture,
    [switch] $SingleMonsterCapture,
    [switch] $BattleCampaign
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$statePath = Join-Path $repoRoot "Automation\State\packet-capture-active.json"
if ($MonsterCapture -and $SingleMonsterCapture) {
    throw "Choose either all-monsters or single-monster capture, not both."
}
$specialModeCount = @(@($MonsterCapture, $SingleMonsterCapture, $BattleCampaign) |
    Where-Object { $_ }).Count
if ($specialModeCount -gt 1) {
    throw "Choose only one specialized capture mode."
}
$isMonsterCapture = $MonsterCapture -or $SingleMonsterCapture
$captureGoal = if ($BattleCampaign) { "battle-campaign" } elseif ($SingleMonsterCapture) { "single-monster" } elseif ($MonsterCapture) { "all-monsters" } else { "general" }

if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $existingState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$existingState.captureMode -ceq "PacketCaptureOnly") {
        throw "A packet-only capture owns the shared capture state. Stop it before starting enhanced capture."
    }
    if ([string]$existingState.captureMode -cne "ContinuousEnhanced" -or
        [string]$existingState.schemaVersion -cne "god2-continuous-enhanced-capture-state-v1") {
        throw "The shared capture state has an unknown schema; refusing to overwrite it."
    }
    if ([bool]$existingState.active) {
        throw "An enhanced capture is already active: $($existingState.sessionId)"
    }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", ('"' + $PSCommandPath + '"'),
        "-ObserveSeconds", $ObserveSeconds,
        "-SessionPrefix", ('"' + $SessionPrefix + '"')
    )
    if (-not [string]::IsNullOrWhiteSpace($ExpectedClientPath)) {
        $arguments += @("-ExpectedClientPath", ('"' + [IO.Path]::GetFullPath($ExpectedClientPath) + '"'))
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedClientSha256)) {
        $arguments += @("-ExpectedClientSha256", $ExpectedClientSha256)
    }
    if ($MonsterCapture) {
        $arguments += "-MonsterCapture"
    }
    if ($SingleMonsterCapture) {
        $arguments += "-SingleMonsterCapture"
    }
    if ($BattleCampaign) {
        $arguments += "-BattleCampaign"
    }
    $elevated = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($elevated.ExitCode -ne 0) {
        throw "Elevated capture launcher failed with exit code $($elevated.ExitCode)."
    }

    if (-not (Test-Path -LiteralPath $statePath)) {
        throw "Elevated capture launcher did not publish active state."
    }

    Get-Content -LiteralPath $statePath -Raw -Encoding UTF8
    exit 0
}

$resolvedExpectedClientPath = if ([string]::IsNullOrWhiteSpace($ExpectedClientPath)) {
    $null
}
else {
    [IO.Path]::GetFullPath($ExpectedClientPath)
}
$candidateProcesses = @(Get-CimInstance Win32_Process -Filter "Name='God2_opt.exe'" | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
    ($null -eq $resolvedExpectedClientPath -or
     [string]::Equals([IO.Path]::GetFullPath([string]$_.ExecutablePath), $resolvedExpectedClientPath,
        [StringComparison]::OrdinalIgnoreCase))
})
if ($candidateProcesses.Count -ne 1) {
    throw "Expected exactly one matching God2_opt.exe process; found $($candidateProcesses.Count). Pin -ExpectedClientPath when local and original clients may coexist."
}
$game = Get-Process -Id ([int]$candidateProcesses[0].ProcessId) -ErrorAction Stop
$clientPath = [IO.Path]::GetFullPath([string]$candidateProcesses[0].ExecutablePath)
if ($resolvedExpectedClientPath -and
    -not [string]::Equals($clientPath, $resolvedExpectedClientPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Selected client path does not match the pinned expected client path."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedClientSha256)) {
    if ($ExpectedClientSha256 -cnotmatch '^[0-9A-Fa-f]{64}$') {
        throw "ExpectedClientSha256 must be a 64-hex SHA-256 value."
    }
    $actualClientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
    if ($actualClientSha256 -cne $ExpectedClientSha256.ToUpperInvariant()) {
        throw "Selected client SHA-256 does not match the pinned expected build."
    }
}
$engine = Join-Path $repoRoot "Release\God2SemanticRecoveryEngine.exe"
if (-not (Test-Path -LiteralPath $engine)) {
    throw "Release semantic recovery engine is missing: $engine"
}

$suffix = [guid]::NewGuid().ToString("N")
$sessionId = "$SessionPrefix-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$($suffix.Substring(0, 8))"
$sessionRoot = Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex\$sessionId"
foreach ($relative in @("raw", "raw\enhanced-x86", "analysis", "reports")) {
    [IO.Directory]::CreateDirectory((Join-Path $sessionRoot $relative)) | Out-Null
}

$stopEventName = "Local\God2SemanticRecovery.OfficialHeadlessStop.$suffix"
$created = $false
$stopEvent = [Threading.EventWaitHandle]::new(
    $false,
    [Threading.EventResetMode]::ManualReset,
    $stopEventName,
    [ref]$created)
if (-not $created) {
    throw "Capture stop event already exists."
}

try {
    $stdout = Join-Path $sessionRoot "reports\headless-engine.stdout.txt"
    $stderr = Join-Path $sessionRoot "reports\headless-engine.stderr.txt"
    $arguments = @(
        "--internal-headless-enhanced-capture",
        "--session", ('"' + $sessionRoot + '"'),
        "--session-id", $sessionId,
        "--game", ('"' + $clientPath + '"'),
        "--pid", $game.Id,
        "--stop-event", ('"' + $stopEventName + '"'),
        "--observe-seconds", $ObserveSeconds
    )
    $worker = Start-Process -FilePath $engine -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

    $statusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
    $deadline = (Get-Date).AddSeconds(90)
    $status = $null
    do {
        Start-Sleep -Milliseconds 250
        if (Test-Path -LiteralPath $statusPath) {
            try {
                $status = [IO.File]::ReadAllText($statusPath) | ConvertFrom-Json
            }
            catch [System.IO.IOException] { }
        }
        $worker.Refresh()
    } while (($null -eq $status -or [string]$status.Status -cne "ATTACHED") -and
        -not $worker.HasExited -and (Get-Date) -lt $deadline)

    if ($null -eq $status -or [string]$status.Status -cne "ATTACHED") {
        $worker.Refresh()
        $exitDetail = if ($worker.HasExited) { "workerExitCode=$($worker.ExitCode)" } else { "workerStillRunning=true" }
        $outputText = if (Test-Path -LiteralPath $stdout) {
            [IO.File]::ReadAllText($stdout)
        }
        else { "" }
        $errorText = if (Test-Path -LiteralPath $stderr) {
            [IO.File]::ReadAllText($stderr)
        }
        else { "" }
        throw "Enhanced capture did not reach ATTACHED. $exitDetail stdout=[$outputText] stderr=[$errorText]"
    }

    $battleUiProbeReceipt = $null
    if ($BattleCampaign) {
        try {
            $game.Refresh()
            $probeModules = @($game.Modules | Where-Object {
                [string]$_.ModuleName -match '^God2(?:Client|Login)TraceProbe.*\.dll$'
            })
            [uint32]$probeModuleBase = 0
            $probeModulePath = ""
            if ($probeModules.Count -eq 1) {
                $probeModuleBase = [uint32]$probeModules[0].BaseAddress.ToInt64()
                $probeModulePath = [string]$probeModules[0].FileName
            }
            else {
                $captureLogPath = Join-Path $sessionRoot "reports\enhanced-capture.log"
                $captureLog = if (Test-Path -LiteralPath $captureLogPath -PathType Leaf) {
                    [IO.File]::ReadAllText($captureLogPath)
                } else { "" }
                $match = [regex]::Match(
                    $captureLog,
                    'packet-decode installed .*? replacement=[0-9A-Fa-f]+ module=.*? base=([0-9A-Fa-f]{8}) rva=',
                    [Text.RegularExpressions.RegexOptions]::Singleline)
                if (-not $match.Success) {
                    throw "Unable to resolve the injected probe module for the Battle UI plan."
                }
                $probeModuleBase = [Convert]::ToUInt32($match.Groups[1].Value, 16)
            }
            $planPath = Join-Path $repoRoot "Automation\BattleUiVitalProbePlan.json"
            $compiledPlanPath = Join-Path $sessionRoot "reports\battle-ui-vital-probe-plan.gpp1.bin"
            $invokePlan = Join-Path $repoRoot "Automation\Invoke-GenericProbePlan.ps1"
            $planOutput = @(& $invokePlan -Action Apply -TargetProcessId $game.Id `
                -Module ("0x{0:X8}" -f $probeModuleBase) -PlanPath $planPath `
                -BinaryOut $compiledPlanPath 2>&1)
            $planOutputPath = Join-Path $sessionRoot "reports\battle-ui-vital-probe-apply.txt"
            [IO.File]::WriteAllLines($planOutputPath, @($planOutput | ForEach-Object {
                [string]$_
            }), [Text.UTF8Encoding]::new($false))
            $battleUiProbeReceipt = [ordered]@{
                schemaVersion = "god2-battle-ui-vital-probe-apply-v1"
                status = "APPLIED"
                sessionId = $sessionId
                clientPid = $game.Id
                clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
                probeModuleBase = ("0x{0:X8}" -f $probeModuleBase)
                probeModulePath = $probeModulePath
                planPath = $planPath
                planSha256 = (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash
                compiledPlanPath = $compiledPlanPath
                targets = @(
                    "battle_ui_bar_scalar@0x0004F080",
                    "battle_ui_text_pair@0x0004F4B0",
                    "battle_ui_vital_apply@0x000D98F0"
                )
                rawPointerPersisted = $false
                appliedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }
            [IO.File]::WriteAllText(
                (Join-Path $sessionRoot "reports\battle-ui-vital-probe.json"),
                ($battleUiProbeReceipt | ConvertTo-Json -Depth 8),
                [Text.UTF8Encoding]::new($false))
        }
        catch {
            $stopEvent.Set() | Out-Null
            if (-not $worker.WaitForExit(45000)) {
                throw "Battle UI probe apply failed and the capture worker did not detach: $($_.Exception.Message)"
            }
            throw
        }
    }

    $state = [ordered]@{
        schemaVersion = "god2-continuous-enhanced-capture-state-v1"
        captureMode = "ContinuousEnhanced"
        active = $true
        sessionId = $sessionId
        sessionDir = $sessionRoot
        clientPid = $game.Id
        clientExecutablePath = $clientPath
        clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
        workerPid = $worker.Id
        stopEvent = $stopEventName
        observeSeconds = $ObserveSeconds
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        captureGoal = $captureGoal
        battleUiVitalProbeApplied = $null -ne $battleUiProbeReceipt
        officialMonsterTargetCount = if ($SingleMonsterCapture) { 1 } elseif ($MonsterCapture) { 208 } else { 0 }
        rawPayloadRetained = $true
    }
    if ($BattleCampaign) {
        $hudPolicyPath = Join-Path $repoRoot "Automation\MonsterIntelligenceHudPolicy.json"
        if (-not (Test-Path -LiteralPath $hudPolicyPath -PathType Leaf)) {
            throw "Monster intelligence HUD policy is missing."
        }
        $policy = [ordered]@{
            schemaVersion = "god2-limited-battle-campaign-policy-v4"
            sessionId = $sessionId
            captureGoal = $captureGoal
            operationPolicy = "UserPlaysOnlyAFewNormalBattles;CaptureMustRemainContinuous"
            preferredSingleBattleProtocol = "FormationHealthRevealActive;MonsterAtFullHp;PoisonIsFirstAndOnlyDamage;ObserveThreePoisonTicks;ThenFinishNormally"
            encounterSegmentation = "ExactCurrentPrimary:S2CPostDecrypt0x82-To-HandlerDecoded0x89;CrossVersion0x8D/0xD6/0xE6CorroborationOnly"
            requiredPerEncounter = @(
                "exact-current-battle-entry-0x82",
                "actor-descriptor-0x1C",
                "ordinary-enemy-name-from-same-position-actor-0xDBE",
                "monster-name-position-descriptor-epoch-binding-for-every-effect",
                "battle-initialization-0x86",
                "client-pre-encrypt-command-0x35",
                "server-handler-effect-0x83",
                "server-handler-current-vitals-0x88",
                "server-handler-settlement-0x89",
                "battle-actor-snapshot",
                "battle-state-snapshot",
                "effect-to-action-applied-causality",
                "exact-build-battle-ui-vital-probe-applied",
                "zero-capture-loss-or-write-failure"
            )
            requestedEvidence = @(
                "pre-encrypt-client-frames",
                "post-decrypt-server-frames",
                "handler-decoded-records",
                "transport-correlation",
                "actor-descriptors",
                "friendly-hp-mp-transitions",
                "battle-state-transitions",
                "terminal-result-and-five-second-post-battle-burst",
                "single-enemy-settlement-experience-field-agreement",
                "monster-origin-effect-and-skill-candidates",
                "formation-revealed-enemy-health-bar-scalars",
                "hp-valid-ui-object-even-when-mp-is-absent",
                "same-object-mp-tuple-when-present-and-valid",
                "full-health-first-poison-tick-and-follow-up-ticks",
                "direct-current-maximum-ui-pair-or-unique-integer-hp-solution"
                "same-enemy-normal-versus-defending-damage-pairs-for-baseline-physical-attack-inversion"
                "same-skill-basic-versus-skill-damage-pairs-across-distinct-damage-conditions"
            )
            completionPolicy = "EveryDetectedEncounterMustPassAllObservableGates;EveryMonsterEvidenceRowMustBindNamePlusPositionPlusDescriptorEpoch;UnknownOrChangedIdentityIsRejectedNotAggregated;UnknownServerOnlyFieldsRemainExplicitlyBlocked"
            monsterIdentityPolicy = "Decode only ordinary static enemies at actor+0xDBE as bounded CP936 after same-position entity-ID/level/16-bit-local-ID and full 0x1C SHA agree; enemy PK players and friendly actors are excluded; replacement descriptor starts a new identity epoch"
            optionalEnemyHpRevealItems = "Exact-current static catalog: 天眼護符 22031/22086/22131/22231/22260 and 父親節快樂稱號 22341..22344 reveal ordinary enemy HP; all are equippable and require no battle-time item-use action; optional accelerator only"
            monsterIntelligenceHudPolicy = [ordered]@{
                path = "Automation/MonsterIntelligenceHudPolicy.json"
                sha256 = (Get-FileHash -LiteralPath $hudPolicyPath -Algorithm SHA256).Hash
                mode = "ReadOnlyEvidenceOverlay"
            }
            maximumHpPromotionPolicy = "ExactUiCurrentMaximumPairMustAgreeWithTargeted0x83DeltaOrThreeFullHealthPoisonTicksMustYieldOneUniqueIntegerMaximum;OtherwiseBlocked"
            physicalAttackInferencePolicy = "NormalAndDefendPairsProduceBaselineCandidatesOnly;UniqueSelectionRequiresIndependentFriendlyPhysicalDefense;OfficialPromotionRemainsBlockedUntilEnemyActionAndAllDamageModifiersAreTypedAndFormulaIsObserved"
            skillDamageInferencePolicy = "OneBasicSkillPairCannotDistinguishAdditiveBaseFromMultiplier;RequireAtLeastTwoDistinctBasicDamageConditionsAndStaticSkillIdentityBinding;AffineAndModifiedModelsRemainExplicitlyRepresented"
            criticalityRule = "UserConfirmedGameplayRule_CorroboratedByControlledSwordOneSamples:SkillsNeverCritical;BasicAttackMayProduceExactDoubleCritical;AnalyzerSeparatesExactDoubleBasicClusterAndTreatsAnySkillDoubleClusterAsContradictionOrCorrelationError"
            purgePolicy = "RawEvidenceMustRemainUntilReviewedPromotionAndSeparatePurgeAuthorization"
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }
        [IO.File]::WriteAllText(
            (Join-Path $sessionRoot "reports\battle-campaign-policy.json"),
            ($policy | ConvertTo-Json -Depth 8),
            [Text.UTF8Encoding]::new($false))
    }
    if ($isMonsterCapture) {
        $policy = [ordered]@{
            schemaVersion = "god2-monster-capture-policy-v1"
            sessionId = $sessionId
            captureGoal = $captureGoal
            officialMonsterTargetCount = if ($SingleMonsterCapture) { 1 } else { 208 }
            requiredFields = @(
                "clientMonsterId", "serverMonsterId", "name", "level",
                "maximumHp", "maximumMp", "strength", "constitution",
                "intelligence", "speed", "metal", "wood", "water", "fire",
                "earth", "experienceReward", "skills", "drops", "spawns"
            )
            correlationRequirement = "EvidenceBlockedExactBuildDoesNotProjectEnemyMaximumHpMp"
            relationshipCompletenessRequirement = "SkillsDropsAndSpawnsMustBeExplicitlyVerifiedEvenWhenEmpty"
            promotionPolicy = "IdentityLevelSettlementMayBeReviewedButMaximumHpRequiresASeparateAuthoritySource"
            purgePolicy = if ($SingleMonsterCapture) {
                "SingleMonsterRawMustRemainUntilExplicitReviewedPromotionAndSeparatePurgeAuthorization"
            } else {
                "SessionRawMayBePurgedOnlyAfterEveryEncounterIsFullyResolved"
            }
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }
        [IO.File]::WriteAllText(
            (Join-Path $sessionRoot "reports\monster-capture-policy.json"),
            ($policy | ConvertTo-Json -Depth 8),
            [Text.UTF8Encoding]::new($false))
    }
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $state | ConvertTo-Json
}
finally {
    $stopEvent.Dispose()
}
