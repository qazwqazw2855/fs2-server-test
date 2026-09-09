[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$OutputPath,
    [int64]$BaselineSequence = 0,
    [int64]$BaselineTimestampUnixMs = 0,
    [int]$BaselineMetadataRecords = -1,
    [int]$BaselineValidationPairs = -1,
    [int]$BaselineSemanticEvents = -1,
    [string[]]$BaselineObservedC2SOpcodes = @(),
    [string[]]$BaselineObservedS2COpcodes = @(),
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
}
if ([string]::IsNullOrWhiteSpace($TraceRoot)) {
    $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $TraceRoot = [string]$state.capture.traceRoot
}

$TraceRoot = [IO.Path]::GetFullPath($TraceRoot)
$analysisRoot = Join-Path $TraceRoot "analysis"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $analysisRoot "controlled-gameplay-campaign.json"
}

$dashboardPath = Join-Path $analysisRoot "live-recovery-dashboard.json"
$probeStatusPath = Join-Path $analysisRoot "probe-plan-status.json"
$monitorStatusPath = Join-Path $PSScriptRoot "State\live-recovery-monitor.json"
foreach ($requiredPath in @($dashboardPath, $probeStatusPath, $monitorStatusPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required live-session state is missing: $requiredPath"
    }
}

$dashboard = Get-Content -LiteralPath $dashboardPath -Raw -Encoding UTF8 | ConvertFrom-Json
$probeStatus = Get-Content -LiteralPath $probeStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath $monitorStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
$targetProcessId = [int]$dashboard.session.processId

if ($dashboard.session.processAlive -ne $true -or
    $null -eq (Get-Process -Id $targetProcessId -ErrorAction SilentlyContinue)) {
    throw "The live God2 process is not available."
}
if ([string]$monitor.status -ne "RUNNING" -or [int]$monitor.targetProcessId -ne $targetProcessId -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live recovery monitor is not healthy for this target."
}
if ($probeStatus.applied -eq $true -or [string]$probeStatus.status -ne "COMPILE_ONLY_NOT_APPLIED") {
    throw "The Generic Probe Plan state does not satisfy the active-session freeze policy."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    $existing = Get-Content -LiteralPath $OutputPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$existing.status -in @("WAITING_FOR_PLAYER_ACTION", "ACTION_OBSERVED_ANALYSIS_REQUIRED")) {
        throw "An active controlled gameplay campaign already exists: $($existing.campaignId)"
    }
}

if ($BaselineSequence -le 0) {
    $BaselineSequence = [int64]$dashboard.observedRuntime.maximumMetadataSequence
}
if ($BaselineTimestampUnixMs -le 0) {
    $BaselineTimestampUnixMs = [int64]$dashboard.observedRuntime.lastObservedUnixMs
}
if ($BaselineMetadataRecords -lt 0) { $BaselineMetadataRecords = [int]$dashboard.observedRuntime.metadataRecords }
if ($BaselineValidationPairs -lt 0) { $BaselineValidationPairs = [int]$dashboard.validationPairs.records }
if ($BaselineSemanticEvents -lt 0) { $BaselineSemanticEvents = [int]$dashboard.semanticEvidence.records }
if ($BaselineObservedC2SOpcodes.Count -eq 0) {
    $BaselineObservedC2SOpcodes = @($dashboard.protocolExpansion.observedC2SOpcodes)
}
if ($BaselineObservedS2COpcodes.Count -eq 0) {
    $BaselineObservedS2COpcodes = @($dashboard.protocolExpansion.observedS2COpcodes)
}

$baselineSequence = $BaselineSequence
$baselineTimestamp = $BaselineTimestampUnixMs
$campaign = [ordered]@{
    schema = "God2ControlledGameplayCampaign/1"
    campaignId = "CGC-S$baselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "WAITING_FOR_PLAYER_ACTION"
    stage = 1
    stageName = "BASIC_BATTLE"
    targetProcessId = $targetProcessId
    monitorProcessId = [int]$monitor.monitorProcessId
    traceRoot = $TraceRoot
    baseline = [ordered]@{
        maximumMetadataSequence = $baselineSequence
        lastObservedUnixMs = $baselineTimestamp
        metadataRecords = $BaselineMetadataRecords
        validationPairs = $BaselineValidationPairs
        semanticEvents = $BaselineSemanticEvents
        observedC2SOpcodes = @($BaselineObservedC2SOpcodes)
        observedS2COpcodes = @($BaselineObservedS2COpcodes)
        handlerRuntime = [int]$dashboard.protocolExpansion.handlerBoundRuntime
        battleActionCount = [int]$dashboard.protocolExpansion.battleActionCount
    }
    correlation = [ordered]@{
        responseWindowMs = 15000
        staticBasicAttackSeedOpcodes = @("0x35")
        exactC2SLogicalPacketContext = $false
        exactS2CTransportContext = $true
        newOpcodePolicy = "RECORD_AS_UNCLASSIFIED_CANDIDATE"
    }
    nextRequiredPlayerAction = [ordered]@{
        action = "ONE_BASIC_BATTLE"
        allowed = @("basic attack 1 to 3 times")
        prohibited = @("skill", "item", "defend", "flee", "special pet action", "special buff")
        stopAfterBattle = $true
    }
    stopGate = [ordered]@{
        whenBattleActionObservedAndHandlerRuntimeZero = "HANDLER_BINDING_BLOCKED"
        permitNextStageOnlyWhenHandlerRuntimePositive = $true
    }
    formalGates = $dashboard.formalGates
    safety = [ordered]@{
        genericProbeApplied = $false
        clientMemoryWritten = $false
        networkBytesEmitted = $false
        productionWrite = $false
        databaseTouched = $false
        automatedGameplay = $false
    }
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign | ConvertTo-Json -Depth 12
