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
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage4-campaign.json" }

& (Join-Path $PSScriptRoot "Get-LiveRecoveryDashboard.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$dashboard = Get-Content -LiteralPath (Join-Path $analysisRoot "live-recovery-dashboard.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot "State\live-recovery-monitor.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dashboard.session.processAlive -ne $true -or [string]$monitor.status -ne "RUNNING" -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live session is not healthy enough to establish Stage 4."
}
$stage3 = Get-LiveStageEvidence -CampaignPath (Join-Path $analysisRoot "stage3-campaign.json") `
    -EvidencePath (Join-Path $analysisRoot "stage3-portal-evidence.json") `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage3PortalEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
if ([string]$stage3.status -ne "STAGE_3_CORRELATION_COMPLETE_MUTATION_BLOCKED") {
    throw "Stage 3 correlation has not completed."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    throw "Stage 4 campaign already exists: $OutputPath"
}

$baselineSequence = [int64]$dashboard.observedRuntime.maximumMetadataSequence
$campaign = [ordered]@{
    schema = "God2ControlledGameplayStage4/1"
    campaignId = "CGC-S4-S$baselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "WAITING_FOR_PLAYER_ACTION"
    stage = 4
    stageName = "SINGLE_MERCHANT_OPEN"
    targetProcessId = [int]$dashboard.session.processId
    monitorProcessId = [int]$monitor.monitorProcessId
    traceRoot = $TraceRoot
    baseline = [ordered]@{
        maximumMetadataSequence = $baselineSequence
        lastObservedUnixMs = [int64]$dashboard.observedRuntime.lastObservedUnixMs
        metadataRecords = [int]$dashboard.observedRuntime.metadataRecords
        validationPairs = [int]$dashboard.validationPairs.records
        semanticEvents = [int]$dashboard.semanticEvidence.records
        observedC2SOpcodes = @($dashboard.protocolExpansion.observedC2SOpcodes)
        observedS2COpcodes = @($dashboard.protocolExpansion.observedS2COpcodes)
    }
    nextRequiredPlayerAction = [ordered]@{
        action = "OPEN_ONE_NORMAL_MERCHANT_WINDOW"
        stopWhen = "SHOP_WINDOW_VISIBLE"
        prohibited = @("buy", "sell", "repair", "quest", "battle", "item use", "portal", "additional NPC")
    }
    recoveryTargets = @(
        "MerchantInteractionRequest", "MerchantObjectToken", "ShopOpenResponse", "ShopIdentity",
        "ShopInventoryCandidate", "ItemTemplateCandidate", "PriceCandidate", "MerchantUiStateMutation"
    )
    priorStage = [ordered]@{
        status = [string]$stage3.status
        sourceMapId = [int]$stage3.transfer.source.clientMapId
        targetMapId = [int]$stage3.transfer.target.clientMapId
        targetX = [int]$stage3.transfer.target.x
        targetY = [int]$stage3.transfer.target.y
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
