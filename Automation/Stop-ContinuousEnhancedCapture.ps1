param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$statePath = Join-Path $repoRoot "Automation\State\packet-capture-active.json"
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw "No enhanced capture state exists."
}
$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$state.schemaVersion -cne "god2-continuous-enhanced-capture-state-v1" -or
    [string]$state.captureMode -cne "ContinuousEnhanced") {
    throw "The active capture state is not a continuous enhanced capture. Use its matching stop command."
}
if (-not [bool]$state.active) {
    throw "Enhanced capture is not active."
}

$stopEvent = [Threading.EventWaitHandle]::OpenExisting([string]$state.stopEvent)
try {
    $stopEvent.Set() | Out-Null
}
finally {
    $stopEvent.Dispose()
}

$worker = Get-Process -Id ([int]$state.workerPid) -ErrorAction SilentlyContinue
if ($null -ne $worker) {
    if (-not $worker.WaitForExit(45000)) {
        throw "Enhanced capture worker did not stop within 45 seconds; evidence was preserved."
    }
}

$sessionRoot = [IO.Path]::GetFullPath([string]$state.sessionDir)
$statusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$status.Status -cne "DETACHED" -or -not [bool]$status.StrictUnloadVerified) {
    throw "Strict DLL unload was not verified; evidence and active state were preserved."
}

$directStateStatus = "not-required"
$directStatePromotionReady = $false
$actorVitalsStatus = "not-required"
$actorVitalsPromotionReady = $false
$isMonsterCapture = [string]$state.captureGoal -in @("all-monsters", "single-monster")
$isBattleCampaign = [string]$state.captureGoal -ceq "battle-campaign"
$battleCampaignStatus = "not-required"
$battleCampaignCompleteEncounterCount = 0
$battleCampaignDetectedEncounterCount = 0
$battleUiVitalObservationCount = 0
$battleMonsterMaximumHpEvidenceReady = $false
if ($isMonsterCapture -or $isBattleCampaign) {
    # Export the immutable actor/state bytes immediately after verified unload,
    # before any derived offline analysis or completeness decision is made.
    & (Join-Path $PSScriptRoot "Export-God2BattleActorSnapshots.ps1") `
        -SessionId ([string]$state.sessionId) | Out-Null
    & (Join-Path $PSScriptRoot "Export-God2BattleStateSnapshots.ps1") `
        -SessionId ([string]$state.sessionId) | Out-Null
}

$engine = Join-Path $repoRoot "Release\God2SemanticRecoveryEngine.exe"
& $engine --internal-reanalyze --session $sessionRoot | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Offline reanalysis failed with exit code $LASTEXITCODE; evidence was preserved."
}

$gateStatus = "not-required"
if ($isMonsterCapture) {
    & (Join-Path $PSScriptRoot "Test-MonsterDirectStateCapture.ps1") `
        -SessionId ([string]$state.sessionId) -NoFail | Out-Null
    & (Join-Path $PSScriptRoot "Test-MonsterActorVitalsCapture.ps1") `
        -SessionId ([string]$state.sessionId) -NoFail | Out-Null
    $directStateGate = Get-Content -LiteralPath (
        Join-Path $sessionRoot "reports\monster-direct-state-capture-readiness.json") `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $directStateStatus = if ([bool]$directStateGate.rawDirectStateReady) {
        "raw-direct-state-ready"
    } else {
        "evidence-blocked"
    }
    $directStatePromotionReady = [bool]$directStateGate.maximumHpPromotionReady
    $actorVitalsGate = Get-Content -LiteralPath (
        Join-Path $sessionRoot "reports\monster-actor-vitals-capture-readiness.json") `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $actorVitalsStatus = if ([bool]$actorVitalsGate.actorVitalsReady) {
        "actor-hp-mp-ready"
    } else {
        "evidence-blocked"
    }
    $actorVitalsPromotionReady = [bool]$actorVitalsGate.maximumHpPromotionReady
    if ([string]$state.captureGoal -ceq "all-monsters") {
        & (Join-Path $PSScriptRoot "Test-MonsterCaptureCompleteness.ps1") `
            -SessionId ([string]$state.sessionId) -NoFail | Out-Null
        $gate = Get-Content -LiteralPath (Join-Path $sessionRoot "reports\monster-extraction-gate.json") `
            -Raw -Encoding UTF8 | ConvertFrom-Json
        $gateStatus = if ([bool]$gate.purgeAllowed -and
            [bool]$actorVitalsGate.actorVitalsReady -and
            [bool]$actorVitalsGate.maximumHpPromotionReady) { "pass" } else { "evidence-blocked" }
        if ($gateStatus -eq "pass") {
            & (Join-Path $PSScriptRoot "Commit-MonsterCapture.ps1") `
                -SessionId ([string]$state.sessionId) | Out-Null
        }
    }
    else {
        $gateStatus = if ([bool]$actorVitalsGate.actorVitalsReady) {
            "single-monster-review-required"
        } else {
            "evidence-blocked"
        }
    }
}
elseif ($isBattleCampaign) {
    & (Join-Path $PSScriptRoot "Test-LimitedOfficialBattleCapture.ps1") `
        -SessionId ([string]$state.sessionId) -NoFail | Out-Null
    $battleCampaignGate = Get-Content -LiteralPath (
        Join-Path $sessionRoot "reports\battle-campaign-readiness.json") `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $battleCampaignStatus = [string]$battleCampaignGate.status
    $battleCampaignCompleteEncounterCount = [int]$battleCampaignGate.completeEncounterCount
    $battleCampaignDetectedEncounterCount = [int]$battleCampaignGate.detectedEncounterCount
    $battleUiVitalObservationCount = [int]$battleCampaignGate.battleUiVitalObservationCount
    $battleMonsterMaximumHpEvidenceReady = [bool]$battleCampaignGate.monsterMaximumHpEvidenceReady
}

$summaryPath = Join-Path $sessionRoot "session-summary.json"
$summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$state.active = $false
$state | Add-Member -NotePropertyName completedAtUtc `
    -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString("o")) -Force
$commitPath = Join-Path $sessionRoot "reports\monster-import-commit.json"
$commitReceiptValid = $false
if (Test-Path -LiteralPath $commitPath -PathType Leaf) {
    $commitReceipt = Get-Content -LiteralPath $commitPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $commitReceiptValid = [string]$commitReceipt.status -ceq "COMMITTED" -and
        [string]$commitReceipt.sessionId -ceq [string]$state.sessionId
}
$nextStatus = if ($isBattleCampaign) {
    if ($battleCampaignStatus -ceq "OBSERVABLE_BATTLE_EVIDENCE_COMPLETE") {
        "analyzed-battle-campaign-observable-complete"
    } else {
        "analyzed-battle-campaign-evidence-incomplete"
    }
} elseif ([string]$state.captureGoal -ceq "single-monster") {
    if ($actorVitalsStatus -ceq "actor-hp-mp-ready") {
        "analyzed-single-monster-review-pending"
    } else {
        "analyzed-monster-extraction-pending"
    }
} elseif ($isMonsterCapture -and
    ($actorVitalsStatus -cne "actor-hp-mp-ready" -or
     -not $actorVitalsPromotionReady -or $gateStatus -cne "pass" -or
     -not $commitReceiptValid)) {
    "analyzed-monster-extraction-pending"
} else {
    "analyzed-ready-for-reviewed-purge"
}
$state | Add-Member -NotePropertyName status -NotePropertyValue $nextStatus -Force
$state | Add-Member -NotePropertyName captureRecords `
    -NotePropertyValue ([int]$summary.CaptureRecords) -Force
$requiredRawEvidence = @(
    (Join-Path $sessionRoot "raw\enhanced-x86\trace.bin"),
    (Join-Path $sessionRoot "raw\injected-packets.jsonl")
)
$rawFileCount = @(Get-ChildItem -LiteralPath (Join-Path $sessionRoot "raw") -Recurse -File -ErrorAction SilentlyContinue).Count
$rawPayloadRetained = @($requiredRawEvidence | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
}).Count -eq $requiredRawEvidence.Count
$state | Add-Member -NotePropertyName rawPayloadRetained -NotePropertyValue $rawPayloadRetained -Force
$state | Add-Member -NotePropertyName rawFileCount -NotePropertyValue $rawFileCount -Force
$state | Add-Member -NotePropertyName monsterExtractionGate -NotePropertyValue $gateStatus -Force
$state | Add-Member -NotePropertyName monsterDirectStateCapture `
    -NotePropertyValue $directStateStatus -Force
$state | Add-Member -NotePropertyName monsterActorVitalsCapture `
    -NotePropertyValue $actorVitalsStatus -Force
$commitStatus = if ($commitReceiptValid) {
    "committed"
} else {
    "not-committed"
}
$state | Add-Member -NotePropertyName monsterImport -NotePropertyValue $commitStatus -Force
$state | Add-Member -NotePropertyName battleCampaignStatus `
    -NotePropertyValue $battleCampaignStatus -Force
$state | Add-Member -NotePropertyName battleCampaignDetectedEncounterCount `
    -NotePropertyValue $battleCampaignDetectedEncounterCount -Force
$state | Add-Member -NotePropertyName battleCampaignCompleteEncounterCount `
    -NotePropertyValue $battleCampaignCompleteEncounterCount -Force
$state | Add-Member -NotePropertyName battleUiVitalObservationCount `
    -NotePropertyValue $battleUiVitalObservationCount -Force
$state | Add-Member -NotePropertyName battleMonsterMaximumHpEvidenceReady `
    -NotePropertyValue $battleMonsterMaximumHpEvidenceReady -Force
[IO.File]::WriteAllText(
    $statePath,
    ($state | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$state | ConvertTo-Json -Depth 10
