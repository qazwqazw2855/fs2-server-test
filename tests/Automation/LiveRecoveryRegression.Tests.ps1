[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
. (Join-Path $repoRoot "Automation\LiveRecoveryStageEvidence.ps1")
$script:assertions = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertions++
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Write-Json {
    param([string]$Path, [object]$Value)
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function New-ProtocolFrame {
    param([byte[]]$Body)
    $frame = [byte[]]::new($Body.Length + 3)
    [BitConverter]::GetBytes([uint16]$frame.Length).CopyTo($frame, 0)
    [Array]::Copy($Body, 0, $frame, 2, $Body.Length)
    $checksum = 0
    for ($index = 0; $index -lt ($frame.Length - 1); $index++) {
        $checksum = ($checksum + [int]$frame[$index] + 0x3C) -band 0xFF
    }
    $frame[$frame.Length - 1] = [byte]$checksum
    return ,$frame
}

function New-MetadataEvent {
    param(
        [int64]$Sequence,
        [int64]$Timestamp,
        [string]$Direction,
        [string]$Stage,
        [string]$Opcode,
        [byte[]]$Frame,
        [string]$HookInvocationId = ""
    )
    return [ordered]@{
        sequence = $Sequence
        ObservedAtUnixMs = $Timestamp
        PacketDirection = $Direction
        CaptureStage = $Stage
        Opcode = $Opcode
        Plaintext = $true
        PlaintextLength = if ($null -ne $Frame) { $Frame.Length } else { 0 }
        PlaintextHex = if ($null -ne $Frame) { ([BitConverter]::ToString($Frame)).Replace('-', '') } else { "" }
        SourceFrameId = "fixture-$Sequence"
        HookInvocationId = if ($HookInvocationId) { $HookInvocationId } else { "hook-$Sequence" }
        ContextInvocationId = $null
        caller = "module=god2_opt.exe base=00400000 rva=0x00000000"
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("god2-live-recovery-tests-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    $lifecycleRoot = Join-Path $tempRoot "lifecycle"
    $lifecycleAnalysis = Join-Path $lifecycleRoot "analysis"
    New-Item -ItemType Directory -Path $lifecycleAnalysis -Force | Out-Null
    $campaignPath = Join-Path $lifecycleAnalysis "stage99-campaign.json"
    $evidencePath = Join-Path $lifecycleAnalysis "stage99-evidence.json"
    $exporterPath = Join-Path $lifecycleRoot "fake-exporter.ps1"
    $countPath = Join-Path $lifecycleRoot "export-count.txt"
    Write-Json $campaignPath ([ordered]@{ campaignId = "campaign-A"; status = "WAITING_FOR_PLAYER_ACTION" })
    Write-Json $evidencePath ([ordered]@{ campaignId = "campaign-A"; status = "STAGE_99_EVIDENCE_BLOCKED" })
    $fakeExporter = @'
param([string]$StatePath, [string]$TraceRoot)
$analysis = Join-Path $TraceRoot "analysis"
$campaignPath = Join-Path $analysis "stage99-campaign.json"
$evidencePath = Join-Path $analysis "stage99-evidence.json"
$countPath = Join-Path $TraceRoot "export-count.txt"
$campaign = Get-Content $campaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$count = if (Test-Path $countPath) { [int](Get-Content $countPath -Raw) } else { 0 }
[IO.File]::WriteAllText($countPath, [string]($count + 1))
$ready = Test-Path (Join-Path $TraceRoot "action.ready")
$evidence = [ordered]@{
    campaignId = [string]$campaign.campaignId
    status = if ($ready) { "STAGE_99_CORRELATED" } else { "STAGE_99_EVIDENCE_BLOCKED" }
}
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
if ($ready) {
    $campaign.status = "STAGE_99_CORRELATION_COMPLETE"
    [IO.File]::WriteAllText($campaignPath, ($campaign | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
'@
    [IO.File]::WriteAllText($exporterPath, $fakeExporter, [Text.UTF8Encoding]::new($false))

    $first = Get-LiveStageEvidence -CampaignPath $campaignPath -EvidencePath $evidencePath `
        -ExporterPath $exporterPath -StatePath "unused" -TraceRoot $lifecycleRoot
    Assert-True ([string]$first.status -eq "STAGE_99_EVIDENCE_BLOCKED") "waiting campaigns must refresh blocked evidence"
    Assert-True ([int](Get-Content $countPath -Raw) -eq 1) "first refresh must invoke exporter"
    New-Item -ItemType File -Path (Join-Path $lifecycleRoot "action.ready") | Out-Null
    $second = Get-LiveStageEvidence -CampaignPath $campaignPath -EvidencePath $evidencePath `
        -ExporterPath $exporterPath -StatePath "unused" -TraceRoot $lifecycleRoot
    Assert-True ([string]$second.status -eq "STAGE_99_CORRELATED") "later action evidence must replace blocked evidence"
    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$second.analyzerSha256)) "evidence must bind its analyzer hash"
    Assert-True ([int](Get-Content $countPath -Raw) -eq 2) "second refresh must invoke exporter"
    $third = Get-LiveStageEvidence -CampaignPath $campaignPath -EvidencePath $evidencePath `
        -ExporterPath $exporterPath -StatePath "unused" -TraceRoot $lifecycleRoot
    Assert-True ([int](Get-Content $countPath -Raw) -eq 2) "completed matching evidence must not be recomputed"
    Write-Json $evidencePath ([ordered]@{ campaignId = "campaign-A"; status = "STAGE_99_EVIDENCE_BLOCKED" })
    $repaired = Get-LiveStageEvidence -CampaignPath $campaignPath -EvidencePath $evidencePath `
        -ExporterPath $exporterPath -StatePath "unused" -TraceRoot $lifecycleRoot
    Assert-True ([string]$repaired.status -eq "STAGE_99_CORRELATED") "completed campaigns must repair blocked evidence"
    Assert-True ([int](Get-Content $countPath -Raw) -eq 3) "blocked evidence must invoke exporter even when campaign is complete"
    Write-Json $campaignPath ([ordered]@{ campaignId = "campaign-B"; status = "WAITING_FOR_PLAYER_ACTION" })
    $forced = Get-LiveStageEvidence -CampaignPath $campaignPath -EvidencePath $evidencePath `
        -ExporterPath $exporterPath -StatePath "unused" -TraceRoot $lifecycleRoot
    Assert-True ([string]$forced.campaignId -eq "campaign-B") "campaign mismatch must invalidate stale evidence"
    Assert-True ([int](Get-Content $countPath -Raw) -eq 4) "forced campaign must invoke exporter"
    Assert-True (-not (Test-LiveEvidenceStatus -Evidence ([pscustomobject]@{ status = "STAGE_7_EVIDENCE_BLOCKED" }) `
        -AcceptedStatus @("STAGE_7_CORRELATED"))) "blocked evidence must not receive observed authority"

    $stage7Root = Join-Path $tempRoot "stage7"
    $stage7Analysis = Join-Path $stage7Root "analysis"
    New-Item -ItemType Directory -Path $stage7Analysis -Force | Out-Null
    Write-Json (Join-Path $stage7Analysis "stage7-campaign.json") ([ordered]@{
        campaignId = "stage7-fixture"; status = "WAITING_FOR_PLAYER_ACTION"
        baseline = [ordered]@{ maximumMetadataSequence = 100; lastObservedUnixMs = 800 }
        safety = [ordered]@{ productionWrite = $false; databaseTouched = $false }
    })
    Write-Json (Join-Path $stage7Analysis "stage8-campaign.json") ([ordered]@{
        campaignId = "stage8-boundary"; status = "WAITING_FOR_PLAYER_ACTION"
        baseline = [ordered]@{ maximumMetadataSequence = 200; lastObservedUnixMs = 4000 }
    })
    Write-Json (Join-Path $stage7Analysis "stage6-sale-evidence.json") ([ordered]@{
        sale = [ordered]@{ request = [ordered]@{ merchantObjectToken = 1504 } }
    })
    $closeFrame = New-ProtocolFrame ([byte[]](0x39, 0xE0, 0x05, 0x00, 0x00))
    $stage7Events = @(
        (New-MetadataEvent 101 900 "ServerToClient" "PostDecrypt" "0x39" ([byte[]](0x03, 0x00, 0x39)))
        (New-MetadataEvent 110 1000 "ClientToServer" "PreEncrypt" "0x39" $closeFrame)
        (New-MetadataEvent 120 2501 "ServerToClient" "PostDecrypt" "0x39" ([byte[]](0x03, 0x00, 0x39)))
        (New-MetadataEvent 130 3000 "ClientToServer" "PreEncrypt" "0x30" ([byte[]](0x03, 0x00, 0x30)))
    )
    [IO.File]::WriteAllLines((Join-Path $stage7Root "metadata.jsonl"), @($stage7Events | ForEach-Object { $_ | ConvertTo-Json -Compress }), [Text.UTF8Encoding]::new($false))
    $stage7Result = & (Join-Path $repoRoot "Automation\Export-LiveStage7MerchantCloseEvidence.ps1") `
        -TraceRoot $stage7Root | ConvertFrom-Json
    Assert-True ([string]$stage7Result.status -eq "STAGE_7_MERCHANT_CLOSE_CORRELATED_SERVER_MAPPING_CLOSED_UI_MUTATION_BLOCKED") `
        "isolated close must complete with existing server mapping"
    Assert-True ($stage7Result.close.responseObserved -eq $false -and [int]$stage7Result.close.uncorrelatedSameOpcodeResponseCount -eq 2) `
        "responses outside the request window must remain uncorrelated"
    Assert-True (@($stage7Result.classification.observed) -contains "merchant token 1504") "classification must use runtime token"
    Assert-True ((Get-LiveStageUpperSequence -AnalysisRoot $stage7Analysis -Stage 7) -eq 200) "next stage baseline must close action window"

    $stage8Root = Join-Path $tempRoot "stage8"
    $stage8Analysis = Join-Path $stage8Root "analysis"
    New-Item -ItemType Directory -Path $stage8Analysis -Force | Out-Null
    Write-Json (Join-Path $stage8Analysis "stage8-campaign.json") ([ordered]@{
        campaignId = "stage8-fixture"; status = "WAITING_FOR_PLAYER_ACTION"
        baseline = [ordered]@{ maximumMetadataSequence = 200; lastObservedUnixMs = 500 }
        safety = [ordered]@{ productionWrite = $false; databaseTouched = $false }
    })
    $movementFrame = New-ProtocolFrame ([byte[]](0x2E, 0x30, 0x00, 0x51, 0x00, 0x01, 0x00))
    $stage8Events = @(
        (New-MetadataEvent 210 1000 "ClientToServer" "PreEncrypt" "0x2E" $movementFrame)
        (New-MetadataEvent 220 3000 "ClientToServer" "PreEncrypt" "0x30" ([byte[]](0x03, 0x00, 0x30)))
    )
    [IO.File]::WriteAllLines((Join-Path $stage8Root "metadata.jsonl"), @($stage8Events | ForEach-Object { $_ | ConvertTo-Json -Compress }), [Text.UTF8Encoding]::new($false))
    $pending = & (Join-Path $repoRoot "Automation\Export-LiveStage8PositioningEvidence.ps1") `
        -TraceRoot $stage8Root | ConvertFrom-Json
    Assert-True ([string]$pending.status -eq "STAGE_8_POSITIONING_TRAFFIC_ISOLATED_PLAYER_CONFIRMATION_REQUIRED") `
        "monitor refresh must not infer NPC proximity"
    $confirmed = & (Join-Path $repoRoot "Automation\Export-LiveStage8PositioningEvidence.ps1") `
        -TraceRoot $stage8Root -PlayerConfirmedPositioned | ConvertFrom-Json
    Assert-True ([string]$confirmed.status -eq "STAGE_8_POSITIONING_CORRELATED_INTERACTION_BASELINE_READY") `
        "player confirmation must complete isolated positioning"
    Assert-True ([int]$confirmed.movement.finalPositionCandidate.x -eq 48 -and [int]$confirmed.movement.finalPositionCandidate.y -eq 81) `
        "final movement endpoint must be preserved"
    $replayed = & (Join-Path $repoRoot "Automation\Export-LiveStage8PositioningEvidence.ps1") `
        -TraceRoot $stage8Root | ConvertFrom-Json
    $persistedStage8 = Get-Content -LiteralPath (Join-Path $stage8Analysis "stage8-campaign.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ([string]$replayed.status -eq "STAGE_8_POSITIONING_CORRELATED_INTERACTION_BASELINE_READY" -and
        $replayed.confirmation.playerConfirmedPositioned -eq $true) `
        "completed Stage 8 confirmation must survive analyzer replay (status=$($replayed.status), replayConfirmed=$($replayed.confirmation.playerConfirmedPositioned), persistedConfirmed=$($persistedStage8.playerConfirmedPositioned), campaignStatus=$($persistedStage8.status))"

    [ordered]@{ status = "PASS"; assertions = $script:assertions } | ConvertTo-Json
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTemp -ne $systemTemp -and (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
