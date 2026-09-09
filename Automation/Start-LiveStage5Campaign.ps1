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
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage5-campaign.json" }

& (Join-Path $PSScriptRoot "Get-LiveRecoveryDashboard.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$dashboard = Get-Content -LiteralPath (Join-Path $analysisRoot "live-recovery-dashboard.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot "State\live-recovery-monitor.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dashboard.session.processAlive -ne $true -or [string]$monitor.status -ne "RUNNING" -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live session is not healthy enough to establish Stage 5."
}
$stage4 = Get-LiveStageEvidence -CampaignPath (Join-Path $analysisRoot "stage4-campaign.json") `
    -EvidencePath (Join-Path $analysisRoot "stage4-merchant-evidence.json") `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage4MerchantEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
if ([string]$stage4.status -ne "STAGE_4_MERCHANT_OPEN_CORRELATED_CATALOG_BLOCKED") {
    throw "Stage 4 merchant-open correlation has not completed."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    throw "Stage 5 campaign already exists: $OutputPath"
}

$baselineSequence = [int64]$dashboard.observedRuntime.maximumMetadataSequence
$campaign = [ordered]@{
    schema = "God2ControlledGameplayStage5/1"
    campaignId = "CGC-S5-S$baselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "WAITING_FOR_PLAYER_ACTION"
    stage = 5
    stageName = "SINGLE_MERCHANT_PURCHASE"
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
        action = "BUY_EXACTLY_ONE_UNIT_OF_ONE_INEXPENSIVE_ITEM"
        stopWhen = "PURCHASE_SUCCESS_VISIBLE"
        prohibited = @("second purchase", "sell", "repair", "close shop", "additional NPC", "movement", "battle", "item use", "portal")
    }
    recoveryTargets = @(
        "MerchantBuyRequest", "MerchantObjectToken", "ItemSlotCandidate", "ItemTemplateCandidate",
        "QuantityCandidate", "UnitPriceCandidate", "TotalPriceCandidate", "WalletDelta", "InventoryMutation", "PurchaseResult"
    )
    priorStage = [ordered]@{
        status = [string]$stage4.status
        merchantObjectToken = [int]$stage4.selectedChain.objectToken
        shopResponseOpcode = "0x68"
        controlledActionIsolationSatisfied = $stage4.contamination.controlledActionIsolationSatisfied -eq $true
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
