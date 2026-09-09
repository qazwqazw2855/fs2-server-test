[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$CampaignPath,
    [string]$OutputPath
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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage3-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage3-portal-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$registryPath = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\client-dispatch-registry.json"
$registry = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 3

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

function Convert-WorldTransition {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 12 -or $bytes[0] -ne 12 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x61) { return $null }
    $packedMap = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
    $opaqueWord = [int]$bytes[5] -bor ([int]$bytes[6] -shl 8)
    $packedPosition = [uint32]([int]$bytes[7] -bor ([int]$bytes[8] -shl 8) -bor
        ([int]$bytes[9] -shl 16) -bor ([int]$bytes[10] -shl 24))
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        areaId = $packedMap -band 0x3F
        clientMapId = $packedMap -shr 6
        opaqueWord = ('0x{0:X4}' -f $opaqueWord)
        positionMode = [int]($packedPosition -band 0x03)
        x = [int](($packedPosition -shr 2) -band 0x7FFF)
        y = [int]($packedPosition -shr 17)
        layoutAuthority = "STATIC_0X61_CONSUMER_PLUS_OBSERVED_RUNTIME_PACKET"
    }
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
        endX = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
        endY = [int]$bytes[5] -bor ([int]$bytes[6] -shl 8)
        movementArgument = [int]$bytes[7] -bor ([int]$bytes[8] -shl 8)
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "OBSERVED_RUNTIME_MOVEMENT_TRIGGER_CANDIDATE"
    }
}

function Convert-PostTransferCandidate {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 8 -or $bytes[0] -ne 8 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x1C) { return $null }
    $token = [uint32]([int]$bytes[3] -bor ([int]$bytes[4] -shl 8) -bor
        ([int]$bytes[5] -shl 16) -bor ([int]$bytes[6] -shl 24))
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        objectTokenCandidate = $token
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "POST_TRANSFER_TEMPORAL_CANDIDATE_NOT_TRANSFER_REQUEST"
    }
}

$relevant = [Collections.Generic.List[object]]::new()
$metadataPath = Join-Path $TraceRoot "metadata.jsonl"
$stream = [IO.File]::Open($metadataPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
try {
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 65536, $false)
    try {
        while (($line = $reader.ReadLine()) -ne $null) {
            try { $item = $line | ConvertFrom-Json } catch { continue }
            if ($item.Plaintext -ne $true) { continue }
            if ([string]$item.Opcode -in @("0x1C", "0x2E", "0x61") -or
                [string]$item.CaptureStage -eq "HandlerDecoded") {
                $relevant.Add($item)
            }
        }
    }
    finally { $reader.Dispose() }
}
finally { $stream.Dispose() }

$priorTransitions = @($relevant | Where-Object {
    [int64]$_.sequence -le $baselineSequence -and [string]$_.PacketDirection -eq "ServerToClient" -and
    [string]$_.CaptureStage -eq "PostDecrypt" -and [string]$_.Opcode -eq "0x61"
} | ForEach-Object { Convert-WorldTransition -Item $_ } | Where-Object { $null -ne $_ -and $_.checksumValid -eq $true } |
    Sort-Object sequence)
$postTransitions = @($relevant | Where-Object {
    [int64]$_.sequence -gt $baselineSequence -and [int64]$_.sequence -le $upperSequence -and
    [string]$_.PacketDirection -eq "ServerToClient" -and
    [string]$_.CaptureStage -eq "PostDecrypt" -and [string]$_.Opcode -eq "0x61"
} | ForEach-Object { Convert-WorldTransition -Item $_ } | Where-Object { $null -ne $_ } | Sort-Object sequence)

$sourceWorld = $priorTransitions | Select-Object -Last 1
$transition = $postTransitions | Where-Object { $_.checksumValid -eq $true } | Select-Object -First 1
$movement = $null
$postTransferCandidate = $null
$runtimeHandlerRecords = @()
if ($null -ne $transition) {
    $movementItem = $relevant | Where-Object {
        [int64]$_.sequence -gt $baselineSequence -and [int64]$_.sequence -lt [int64]$transition.sequence -and
        [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
        [string]$_.Opcode -eq "0x2E" -and
        ([int64]$transition.timestampUnixMs - [int64]$_.ObservedAtUnixMs) -le 15000
    } | Sort-Object @{ Expression = { [int64]$_.ObservedAtUnixMs } }, @{ Expression = { [int64]$_.sequence } } |
        Select-Object -Last 1
    if ($null -ne $movementItem) { $movement = Convert-Movement -Item $movementItem }
    $postTransferItem = $relevant | Where-Object {
        [int64]$_.sequence -gt [int64]$transition.sequence -and
        [int64]$_.sequence -le $upperSequence -and
        [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
        [string]$_.Opcode -eq "0x1C" -and
        ([int64]$_.ObservedAtUnixMs - [int64]$transition.timestampUnixMs) -le 1000
    } | Sort-Object @{ Expression = { [int64]$_.ObservedAtUnixMs } }, @{ Expression = { [int64]$_.sequence } } |
        Select-Object -First 1
    if ($null -ne $postTransferItem) {
        $postTransferCandidate = Convert-PostTransferCandidate -Item $postTransferItem
    }
    $runtimeHandlerRecords = @($relevant | Where-Object {
        [int64]$_.sequence -gt $baselineSequence -and [int64]$_.sequence -le $upperSequence -and
        [string]$_.CaptureStage -eq "HandlerDecoded" -and
        ([string]$_.Opcode -eq "0x61" -or [string]$_.ContextInvocationId -eq [string]$transition.hookInvocationId)
    })
}

$staticHandler = $registry.Entries | Where-Object {
    [string]$_.State -eq "World" -and [string]$_.Direction -eq "ServerToClient" -and [string]$_.Opcode -eq "0x61"
} | Select-Object -First 1
$validPostTransitions = @($postTransitions | Where-Object { $_.checksumValid -eq $true })
$complete = $validPostTransitions.Count -eq 1 -and $null -ne $transition -and
    $null -ne $sourceWorld -and $null -ne $movement -and $movement.checksumValid -eq $true

$result = [ordered]@{
    schema = "God2LiveStage3PortalEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    status = if ($complete) { "STAGE_3_CORRELATION_COMPLETE_MUTATION_BLOCKED" } else { "STAGE_3_EVIDENCE_BLOCKED" }
    transfer = if ($null -ne $transition) {
        [ordered]@{
            triggerKind = "AUTOMATIC_PORTAL_TILE_CROSSING"
            explicitTransferRequestObserved = $false
            triggerMovement = $movement
            source = [ordered]@{
                clientMapId = if ($null -ne $sourceWorld) { [int]$sourceWorld.clientMapId } else { $null }
                areaId = if ($null -ne $sourceWorld) { [int]$sourceWorld.areaId } else { $null }
                x = if ($null -ne $movement) { [int]$movement.endX } else { $null }
                y = if ($null -ne $movement) { [int]$movement.endY } else { $null }
                worldIdentitySequence = if ($null -ne $sourceWorld) { [int64]$sourceWorld.sequence } else { $null }
                authority = "PRIOR_VALID_0X61_WORLD_IDENTITY_PLUS_FINAL_MOVEMENT_POSITION"
            }
            transition = $transition
            target = [ordered]@{
                clientMapId = [int]$transition.clientMapId
                areaId = [int]$transition.areaId
                x = [int]$transition.x
                y = [int]$transition.y
                positionMode = [int]$transition.positionMode
                authority = "OBSERVED_VALID_0X61_DECODE"
            }
            triggerToTransitionMs = if ($null -ne $movement) {
                [int64]$transition.timestampUnixMs - [int64]$movement.timestampUnixMs
            } else { $null }
            postTransferCandidate = $postTransferCandidate
            transitionToPostCandidateMs = if ($null -ne $postTransferCandidate) {
                [int64]$postTransferCandidate.timestampUnixMs - [int64]$transition.timestampUnixMs
            } else { $null }
        }
    } else { $null }
    staticEvidence = [ordered]@{
        worldHandlerRva = if ($null -ne $staticHandler) { [string]$staticHandler.HandlerRva } else { $null }
        worldHandlerEvidence = if ($null -ne $staticHandler) { [string]$staticHandler.Evidence } else { $null }
        positionConsumerRva = "0x0008D710"
        positionConsumerFormula = "mode=packed&3; x=(packed>>2)&0x7FFF; y=packed>>17"
        source = "src/God2.ClassicServer.Protocol/OfficialPortalWire.cs"
    }
    gate = [ordered]@{
        singleControlledTransitionObserved = ($validPostTransitions.Count -eq 1)
        validTransitionChecksum = ($null -ne $transition -and $transition.checksumValid -eq $true)
        triggerMovementObserved = ($null -ne $movement)
        sourceWorldIdentityResolved = ($null -ne $sourceWorld)
        targetWorldIdentityDecoded = ($null -ne $transition)
        targetCoordinatesDecoded = ($null -ne $transition)
        postTransferCandidateObserved = ($null -ne $postTransferCandidate)
        runtimeWorldHandlerRecords = $runtimeHandlerRecords.Count
        typedMapMutationObserved = $false
        typedCoordinateMutationObserved = $false
        productionPromotion = $false
    }
    classification = [ordered]@{
        observed = @("C2S 0x2E movement trigger", "S2C 0x61 plaintext transition")
        discovered = @("World 0x61 handler RVA", "packed map and position consumer")
        unresolved = @("explicit transfer request for automatic portal", "runtime typed map mutation", "runtime typed coordinate mutation", "0x1C semantic name")
    }
    firstBrokenEdge = "PostDecrypt0x61 -> RuntimeHandlerDecoded -> TypedWorldMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign.status = if ($complete) { "STAGE_3_CORRELATION_COMPLETE" } else { "STAGE_3_EVIDENCE_BLOCKED" }
$campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
$campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
$campaignTemporary = "$CampaignPath.tmp-$PID"
[IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
$result | ConvertTo-Json -Depth 14
