[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$OutputPath,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
}
$state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($TraceRoot)) { $TraceRoot = [string]$state.capture.traceRoot }
$TraceRoot = [IO.Path]::GetFullPath($TraceRoot)
$analysisRoot = Join-Path $TraceRoot "analysis"
. (Join-Path $PSScriptRoot "LiveRecoveryStageEvidence.ps1")
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage8-campaign.json" }

& (Join-Path $PSScriptRoot "Get-LiveRecoveryDashboard.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$dashboard = Get-Content -LiteralPath (Join-Path $analysisRoot "live-recovery-dashboard.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot "State\live-recovery-monitor.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dashboard.session.processAlive -ne $true -or [string]$monitor.status -ne "RUNNING" -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live session is not healthy enough to establish Stage 8."
}
$stage7 = Get-LiveStageEvidence -CampaignPath (Join-Path $analysisRoot "stage7-campaign.json") `
    -EvidencePath (Join-Path $analysisRoot "stage7-merchant-close-evidence.json") `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage7MerchantCloseEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
if ([string]$stage7.status -ne "STAGE_7_MERCHANT_CLOSE_CORRELATED_SERVER_MAPPING_CLOSED_UI_MUTATION_BLOCKED") {
    throw "Stage 7 merchant-close correlation has not completed."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    throw "Stage 8 campaign already exists: $OutputPath"
}

$baselineSequence = [int64]$dashboard.observedRuntime.maximumMetadataSequence
$campaign = [ordered]@{
    schema = "God2ControlledGameplayStage8/1"
    campaignId = "CGC-S8-S$baselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "WAITING_FOR_PLAYER_ACTION"
    stage = 8
    stageName = "QUEST_NPC_POSITIONING"
    targetProcessId = [int]$dashboard.session.processId
    monitorProcessId = [int]$monitor.monitorProcessId
    traceRoot = $TraceRoot
    baseline = [ordered]@{
        maximumMetadataSequence = $baselineSequence
        lastObservedUnixMs = [int64]$dashboard.observedRuntime.lastObservedUnixMs
        metadataRecords = [int]$dashboard.observedRuntime.metadataRecords
        validationPairs = [int]$dashboard.validationPairs.records
        semanticEvents = [int]$dashboard.semanticEvidence.records
    }
    nextRequiredPlayerAction = [ordered]@{
        action = "MOVE_NEXT_TO_ONE_QUEST_NPC_AND_STAND"
        stopWhen = "CHARACTER_STANDING_WITHIN_INTERACTION_RANGE"
        prohibited = @("click NPC", "open dialog", "accept quest", "complete quest", "merchant", "battle", "item use", "portal", "logout")
    }
    recoveryTargets = @("MovementIsolation", "FinalPositionCandidate", "WorldTransitionExclusion", "QuestNpcInteractionBaseline")
    interpretationPolicy = [ordered]@{
        movementPackets = "EXPECTED_SETUP_TRAFFIC"
        nonMovementActionPackets = "CONTAMINATION"
        worldTransition = "CONTAMINATION"
    }
    safety = [ordered]@{
        genericProbeApplied = $false
        clientMemoryWritten = $false
        networkBytesEmitted = $false
        productionWrite = $false
        databaseTouched = $false
        automatedGameplay = $false
    }
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($campaign | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign | ConvertTo-Json -Depth 10
