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
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage7-campaign.json" }

& (Join-Path $PSScriptRoot "Get-LiveRecoveryDashboard.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$dashboard = Get-Content -LiteralPath (Join-Path $analysisRoot "live-recovery-dashboard.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot "State\live-recovery-monitor.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dashboard.session.processAlive -ne $true -or [string]$monitor.status -ne "RUNNING" -or
    -not [string]::IsNullOrWhiteSpace([string]$monitor.lastError)) {
    throw "The live session is not healthy enough to establish Stage 7."
}
$stage6 = Get-LiveStageEvidence -CampaignPath (Join-Path $analysisRoot "stage6-campaign.json") `
    -EvidencePath (Join-Path $analysisRoot "stage6-sale-evidence.json") `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage6SaleEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
if ([string]$stage6.status -ne "STAGE_6_SALE_CORRELATED_WALLET_DELTA_DERIVED_MUTATION_BLOCKED") {
    throw "Stage 6 sale correlation has not completed."
}
if ((Test-Path -LiteralPath $OutputPath -PathType Leaf) -and -not $Force) {
    throw "Stage 7 campaign already exists: $OutputPath"
}

$baselineSequence = [int64]$dashboard.observedRuntime.maximumMetadataSequence
$campaign = [ordered]@{
    schema = "God2ControlledGameplayStage7/1"
    campaignId = "CGC-S7-S$baselineSequence"
    createdAtUtc = [DateTime]::UtcNow.ToString("O")
    status = "WAITING_FOR_PLAYER_ACTION"
    stage = 7
    stageName = "MERCHANT_WINDOW_CLOSE"
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
        action = "CLOSE_MERCHANT_WINDOW_ONLY"
        stopWhen = "MERCHANT_WINDOW_NOT_VISIBLE"
        prohibited = @("movement", "second NPC", "purchase", "sale", "repair", "battle", "item use", "portal", "logout")
    }
    recoveryTargets = @(
        "MerchantCloseRequest", "MerchantCloseResponse", "ClientOnlyUiTransitionCandidate", "ObjectToken", "CloseResult"
    )
    priorStage = [ordered]@{
        status = [string]$stage6.status
        merchantObjectToken = [uint32]$stage6.sale.request.merchantObjectToken
        itemTemplateCandidate = [uint32]$stage6.sale.request.itemTemplateCandidate
        walletBalanceAfterSaleCandidate = [uint32]$stage6.sale.wallet.balanceAfterSaleCandidate
    }
    interpretationPolicy = [ordered]@{
        zeroNonIdlePackets = "CLIENT_ONLY_UI_TRANSITION_CANDIDATE"
        observedPackets = "CORRELATE_WITHOUT_ASSIGNING_SEMANTICS_UNTIL_VALIDATED"
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
