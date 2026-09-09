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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage7-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage7-merchant-close-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stage6 = Get-Content -LiteralPath (Join-Path $analysisRoot "stage6-sale-evidence.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$modelPath = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\packet-reader-writer-model.json"
$model = Get-Content -LiteralPath $modelPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 7

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

function Convert-CloseRequest {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 8 -or $bytes[0] -ne 8 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x39) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        merchantObjectToken = [uint32]([BitConverter]::ToUInt16($bytes, 3))
        reservedCandidate = [uint32]([BitConverter]::ToUInt16($bytes, 5))
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        callerRva = ([string]$Item.caller -replace '^.*rva=', '')
        authority = "USER_DECLARED_WINDOW_CLOSE_PLUS_OBSERVED_RUNTIME_REQUEST"
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

$closeRequests = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x39" -and [int]$_.PlaintextLength -eq 8
} | ForEach-Object { Convert-CloseRequest -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$allCloseResponses = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x39"
})
$nonIdleOutbound = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -notin @("0x30", "0x6D")
})
$movementCount = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x2E"
}).Count
$worldTransitionCount = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x61"
}).Count
$policy = $model.Outbound.Policies | Where-Object { [string]$_.Opcode -eq "0x39" } | Select-Object -First 1
$request = $closeRequests | Select-Object -First 1
$responseWindowMs = 1000
$closeResponses = if ($null -ne $request) {
    @($allCloseResponses | Where-Object {
        [int64]$_.ObservedAtUnixMs -ge [int64]$request.timestampUnixMs -and
        ([int64]$_.ObservedAtUnixMs - [int64]$request.timestampUnixMs) -le $responseWindowMs
    })
} else { @() }
$responseInvocationIds = @($closeResponses | ForEach-Object { [string]$_.HookInvocationId } | Where-Object { $_ })
$runtimeHandlerRecords = @($events | Where-Object {
    [string]$_.CaptureStage -eq "HandlerDecoded" -and
    [string]$_.ContextInvocationId -in $responseInvocationIds
}).Count
$priorMerchantToken = [uint32]$stage6.sale.request.merchantObjectToken
$merchantMatches = $null -ne $request -and [uint32]$request.merchantObjectToken -eq $priorMerchantToken
$policyMatches = $null -ne $policy -and [string]$policy.Kind -eq "Fixed" -and
    ([int]$policy.FixedFrameLength + 3) -eq 8
$isolated = $closeRequests.Count -eq 1 -and $nonIdleOutbound.Count -eq 1 -and
    $movementCount -eq 0 -and $worldTransitionCount -eq 0
$noResponseContractValidated = $null -ne $request -and $closeResponses.Count -eq 0
$complete = $isolated -and $merchantMatches -and $policyMatches -and $request.checksumValid -eq $true -and
    $noResponseContractValidated

$result = [ordered]@{
    schema = "God2LiveStage7MerchantCloseEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    upperSequenceInclusive = $upperSequence
    status = if ($complete) {
        "STAGE_7_MERCHANT_CLOSE_CORRELATED_SERVER_MAPPING_CLOSED_UI_MUTATION_BLOCKED"
    } else { "STAGE_7_EVIDENCE_BLOCKED" }
    close = [ordered]@{
        request = $request
        responseObserved = $closeResponses.Count -gt 0
        responseCount = $closeResponses.Count
        responseWindowMs = $responseWindowMs
        uncorrelatedSameOpcodeResponseCount = $allCloseResponses.Count - $closeResponses.Count
        expectedResponse = "NONE"
        userVisibleResult = "MERCHANT_WINDOW_CLOSED_USER_DECLARED"
        typedUiMutationObserved = $false
    }
    correlation = [ordered]@{
        merchantObjectTokenMatchesPriorStages = $merchantMatches
        fixedOutboundPolicyMatchesObservedFrame = $policyMatches
        requestChecksumValid = $null -ne $request -and $request.checksumValid -eq $true
    }
    isolation = [ordered]@{
        controlledActionIsolationSatisfied = $isolated
        closeRequestCount = $closeRequests.Count
        nonIdleOutboundCount = $nonIdleOutbound.Count
        movementCount = $movementCount
        worldTransitionCount = $worldTransitionCount
    }
    staticEvidence = [ordered]@{
        outboundPolicyKind = if ($null -ne $policy) { [string]$policy.Kind } else { $null }
        outboundPayloadLength = if ($null -ne $policy) { [int]$policy.FixedFrameLength } else { $null }
        observedEnvelopeLength = if ($null -ne $request) { 8 } else { $null }
        frameLengthRelationship = "STATIC_PAYLOAD_LENGTH_PLUS_THREE_BYTE_ENVELOPE"
        policySource = "protocol/evidence/current-build/packet-reader-writer-model.json"
    }
    gate = [ordered]@{
        merchantCloseRequestObserved = $null -ne $request
        merchantTokenCorrelated = $merchantMatches
        fixedFramePolicyValidated = $policyMatches
        serverResponseObserved = $closeResponses.Count -gt 0
        expectedNoResponseValidated = $noResponseContractValidated
        runtimeHandlerRecords = $runtimeHandlerRecords
        typedMerchantUiMutationObserved = $false
        serverMappingClosed = $complete
        productionPromotion = $false
    }
    serverMapping = [ordered]@{
        status = if ($complete) { "EXISTING_EXACT_BUILD_RUNTIME_VALIDATED" } else { "EVIDENCE_BLOCKED" }
        wireCodec = "OfficialNpcInteractionWireCodec.DecodeClose"
        closedLoopHandler = "OfficialNpcInteractionClosedLoop.ExecuteClose"
        expectedEncodedResponse = "EMPTY"
        authority = "EXISTING_CAPTURE_PINNED_SERVER_CONTRACT_PLUS_LIVE_OBSERVED_NO_RESPONSE"
        source = "src/God2.ClassicServer.Runtime/OfficialNpcInteractionWireCodec.cs"
        test = "RoadMapM4NpcInteractionTests.Merchant_close_requires_matching_active_authority_and_emits_no_response"
    }
    classification = [ordered]@{
        observed = @(
            "single C2S 0x39 merchant-close request",
            "merchant token $($request.merchantObjectToken)",
            "valid additive checksum",
            "no S2C 0x39 response within $responseWindowMs ms"
        )
        userDeclared = @("merchant window closed")
        discovered = @(
            "fixed outbound payload policy of $($policy.FixedFrameLength) bytes",
            "existing server close codec and no-response closed loop"
        )
        unresolved = @("typed merchant UI mutation")
    }
    firstBrokenEdges = @("UserVisibleMerchantClose -> TypedMerchantUiMutation")
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
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
if ($complete) {
    $campaign.status = "STAGE_7_CORRELATION_COMPLETE"
    $campaign | Add-Member -NotePropertyName completedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
    $campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
    $campaignTemporary = "$CampaignPath.tmp-$PID"
    [IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
}
$result | ConvertTo-Json -Depth 12
