[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$CampaignPath,
    [string]$OutputPath,
    [switch]$PlayerConfirmedPositioned
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
. (Join-Path $PSScriptRoot "LiveRecoveryStageEvidence.ps1")
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage8-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage8-positioning-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 8

function Convert-HexToBytes {
    param([string]$Hex)
    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0) { return $null }
    $bytes = [byte[]]::new($Hex.Length / 2)
    try {
        for ($index = 0; $index -lt $bytes.Length; $index++) {
            $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
        }
        return $bytes
    }
    catch { return $null }
}

function Get-Sha256Text {
    param([string]$Value)
    $bytes = [Text.Encoding]::ASCII.GetBytes($Value)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
        finally { $sha.Dispose() }
    }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Test-AdditiveChecksum {
    param([byte[]]$Bytes)
    if ($null -eq $Bytes -or $Bytes.Length -lt 2) { return $false }
    $checksum = 0
    for ($index = 0; $index -lt ($Bytes.Length - 1); $index++) {
        $checksum = ($checksum + [int]$Bytes[$index] + 0x3C) -band 0xFF
    }
    return $checksum -eq [int]$Bytes[$Bytes.Length - 1]
}

function Convert-Movement {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 10 -or $bytes[0] -ne 10 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x2E) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        endX = [int]([BitConverter]::ToUInt16($bytes, 3))
        endY = [int]([BitConverter]::ToUInt16($bytes, 5))
        movementArgument = [int]([BitConverter]::ToUInt16($bytes, 7))
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "OBSERVED_RUNTIME_MOVEMENT_ENDPOINT_CANDIDATE"
    }
}

$events = [Collections.Generic.List[object]]::new()
$metadataPath = Join-Path $TraceRoot "metadata.jsonl"
$stream = [IO.File]::Open($metadataPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
try {
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 65536, $false)
    try {
        while (($line = $reader.ReadLine()) -ne $null) {
            try { $item = $line | ConvertFrom-Json } catch { continue }
            if ([int64]$item.sequence -le $baselineSequence -or [int64]$item.sequence -gt $upperSequence) { continue }
            if ($item.Plaintext -eq $true -and [string]$item.CaptureStage -in @("PreEncrypt", "PostDecrypt", "HandlerDecoded")) {
                $events.Add($item)
            }
        }
    }
    finally { $reader.Dispose() }
}
finally { $stream.Dispose() }

$movements = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x2E"
} | ForEach-Object { Convert-Movement -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.timestampUnixMs } }, @{ Expression = { [int64]$_.sequence } })
$nonMovementActions = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -notin @("0x2E", "0x30", "0x6D")
})
$worldTransitions = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x61"
})
$latestObservedUnixMs = if ($events.Count -gt 0) {
    [int64](($events | Measure-Object ObservedAtUnixMs -Maximum).Maximum)
} else { [int64]$campaign.baseline.lastObservedUnixMs }
$finalMovement = $movements | Select-Object -Last 1
$settleWindowMs = 1500
$settledMs = if ($null -ne $finalMovement) {
    $latestObservedUnixMs - [int64]$finalMovement.timestampUnixMs
} else { 0 }
$allChecksumsValid = $movements.Count -gt 0 -and @($movements | Where-Object { $_.checksumValid -ne $true }).Count -eq 0
$trafficReady = $movements.Count -gt 0 -and $allChecksumsValid -and $nonMovementActions.Count -eq 0 -and
    $worldTransitions.Count -eq 0 -and $settledMs -ge $settleWindowMs
$isPlayerConfirmedPositioned = [bool]($PlayerConfirmedPositioned.IsPresent -or
    ($campaign.playerConfirmedPositioned -eq $true))
$complete = $trafficReady -and $isPlayerConfirmedPositioned

$result = [ordered]@{
    schema = "God2LiveStage8PositioningEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    upperSequenceInclusive = $upperSequence
    status = if ($complete) {
        "STAGE_8_POSITIONING_CORRELATED_INTERACTION_BASELINE_READY"
    } elseif ($trafficReady) {
        "STAGE_8_POSITIONING_TRAFFIC_ISOLATED_PLAYER_CONFIRMATION_REQUIRED"
    } else { "STAGE_8_EVIDENCE_BLOCKED" }
    movement = [ordered]@{
        count = $movements.Count
        finalPositionCandidate = if ($null -ne $finalMovement) {
            [ordered]@{
                x = [int]$finalMovement.endX
                y = [int]$finalMovement.endY
                sequence = [int64]$finalMovement.sequence
                timestampUnixMs = [int64]$finalMovement.timestampUnixMs
                authority = "OBSERVED_MOVEMENT_ENDPOINT_NOT_TYPED_WORLD_MUTATION"
            }
        } else { $null }
        allChecksumsValid = $allChecksumsValid
        settledMs = $settledMs
        requiredSettleWindowMs = $settleWindowMs
    }
    isolation = [ordered]@{
        positioningTrafficReady = $trafficReady
        nonMovementActionCount = $nonMovementActions.Count
        worldTransitionCount = $worldTransitions.Count
    }
    confirmation = [ordered]@{
        playerConfirmedPositioned = $isPlayerConfirmedPositioned
        npcProximityRuntimeObserved = $false
        authority = if ($isPlayerConfirmedPositioned) { "PLAYER_DECLARED" } else { "UNCONFIRMED" }
    }
    gate = [ordered]@{
        movementObserved = $movements.Count -gt 0
        movementChecksumsValid = $allChecksumsValid
        settleWindowSatisfied = $settledMs -ge $settleWindowMs
        contaminationAbsent = $nonMovementActions.Count -eq 0 -and $worldTransitions.Count -eq 0
        interactionBaselineReady = $complete
        typedWorldMutationObserved = $false
        productionPromotion = $false
    }
    firstBrokenEdge = "MovementEndpointCandidate -> TypedWorldPositionMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
if ($complete) {
    $campaign.status = "STAGE_8_CORRELATION_COMPLETE"
    $campaign | Add-Member -NotePropertyName playerConfirmedPositioned -NotePropertyValue $true -Force
    $campaign | Add-Member -NotePropertyName completedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
    $campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
    $campaignTemporary = "$CampaignPath.tmp-$PID"
    [IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
}
$result | ConvertTo-Json -Depth 12
