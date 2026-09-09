[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [int64]$BaselineSequence,
    [int64]$BaselineTimestampUnixMs,
    [int]$BaselineMetadataRecords,
    [int]$BaselineValidationPairs,
    [int]$BaselineSemanticEvents,
    [int]$BaselineBattleActionCount,
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
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $analysisRoot "stage2-campaign.json"
}

$dashboard = Get-Content -LiteralPath (Join-Path $analysisRoot "live-recovery-dashboard.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$stage1 = Get-Content -LiteralPath (Join-Path $analysisRoot "battle-primary-evidence.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot "State\live-recovery-monitor.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dashboard.session.processAlive -ne $true -or [string]$monitor.status -ne "RUNNING" -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live session is not healthy enough to establish Stage 2."
}
if ($stage1.gate.nextStagePermitted -ne $true) {
    throw "Stage 1 has not opened the Stage 2 gate."
}
if ($BaselineSequence -le 0 -or $BaselineTimestampUnixMs -le 0) {
    throw "Stage 2 requires an explicit pre-action sequence and timestamp baseline."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    throw "Stage 2 campaign already exists: $OutputPath"
}

$playerPositionCandidate = [int]$stage1.actions[0].commandFields.battlePosition
$playerSideCandidate = [int]$stage1.actions[0].commandFields.side
$campaign = [ordered]@{
    schema = "God2ControlledGameplayStage2/1"
    campaignId = "CGC-S2-S$BaselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "ACTION_OBSERVED_ANALYSIS_REQUIRED"
    stage = 2
    stageName = "BASIC_ATTACK_VS_BASIC_SKILL"
    targetProcessId = [int]$dashboard.session.processId
    monitorProcessId = [int]$monitor.monitorProcessId
    traceRoot = $TraceRoot
    baseline = [ordered]@{
        maximumMetadataSequence = $BaselineSequence
        lastObservedUnixMs = $BaselineTimestampUnixMs
        metadataRecords = $BaselineMetadataRecords
        validationPairs = $BaselineValidationPairs
        semanticEvents = $BaselineSemanticEvents
        stage1BattleActionCount = $BaselineBattleActionCount
    }
    controlledOrder = @("BasicAttack", "BasicSkill")
    playerPositionCandidate = $playerPositionCandidate
    playerSideCandidate = $playerSideCandidate
    correlationWindowMs = 15000
    authority = "USER_DECLARED_CONTROLLED_ORDER_PLUS_RUNTIME_CORRELATION"
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
