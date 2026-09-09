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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage5-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage5-purchase-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$registryPath = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\client-dispatch-registry.json"
$registry = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 5

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

function Convert-BuyRequest {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 12 -or $bytes[0] -ne 12 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x38) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        merchantObjectToken = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
        itemTemplateCandidate = [int]$bytes[5] -bor ([int]$bytes[6] -shl 8)
        quantityCandidate = [int]$bytes[7]
        operationModeCandidate = [int]$bytes[8]
        itemIndexCandidate = [int]$bytes[9] -bor ([int]$bytes[10] -shl 8)
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        callerRva = ([string]$Item.caller -replace '^.*rva=', '')
        authority = "USER_DECLARED_SINGLE_PURCHASE_PLUS_OBSERVED_RUNTIME_REQUEST"
    }
}

function Convert-PurchaseResponse {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 57 -or $bytes[0] -ne 57 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x3B) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        resultCodeCandidate = [int]$bytes[3]
        inventoryContainerCandidate = [int]$bytes[4]
        itemTemplateCandidate = [uint32]([BitConverter]::ToUInt32($bytes, 7))
        priceOrWalletValueCandidate = [uint32]([BitConverter]::ToUInt32($bytes, 40))
        merchantObjectToken = [uint32]([BitConverter]::ToUInt32($bytes, 45))
        quantityCandidate = [int]([BitConverter]::ToUInt16($bytes, 49))
        inventoryStateCandidate = [int]$bytes[51]
        itemIndexCandidate = [int]([BitConverter]::ToUInt16($bytes, 54))
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        valueSemantics = "PRICE_OR_WALLET_CANDIDATE_NOT_VERIFIED"
        authority = "USER_DECLARED_PURCHASE_SUCCESS_PLUS_OBSERVED_RUNTIME_RESPONSE"
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

$buyRequests = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x38" -and [int]$_.PlaintextLength -eq 12
} | ForEach-Object { Convert-BuyRequest -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$purchaseResponses = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x3B" -and [int]$_.PlaintextLength -eq 57
} | ForEach-Object { Convert-PurchaseResponse -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })

$request = $buyRequests | Select-Object -First 1
$response = if ($null -ne $request) {
    $purchaseResponses | Where-Object {
        [int64]$_.timestampUnixMs -ge [int64]$request.timestampUnixMs -and
        ([int64]$_.timestampUnixMs - [int64]$request.timestampUnixMs) -le 1000
    } | Select-Object -First 1
} else { $null }
$runtimeHandlerRecords = @($events | Where-Object {
    [string]$_.CaptureStage -eq "HandlerDecoded" -and
    ([string]$_.Opcode -eq "0x3B" -or ($null -ne $response -and [string]$_.ContextInvocationId -eq [string]$response.hookInvocationId))
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
$handler = $registry.Entries | Where-Object {
    [string]$_.State -eq "World" -and [string]$_.Direction -eq "ServerToClient" -and [string]$_.Opcode -eq "0x3B"
} | Select-Object -First 1

$merchantMatches = $null -ne $request -and $null -ne $response -and
    [uint32]$request.merchantObjectToken -eq [uint32]$response.merchantObjectToken
$itemMatches = $null -ne $request -and $null -ne $response -and
    [uint32]$request.itemTemplateCandidate -eq [uint32]$response.itemTemplateCandidate
$quantityMatches = $null -ne $request -and $null -ne $response -and
    [int]$request.quantityCandidate -eq [int]$response.quantityCandidate
$indexMatches = $null -ne $request -and $null -ne $response -and
    [int]$request.itemIndexCandidate -eq [int]$response.itemIndexCandidate
$isolated = $buyRequests.Count -eq 1 -and $purchaseResponses.Count -eq 1 -and
    $nonIdleOutbound.Count -eq 1 -and $movementCount -eq 0 -and $worldTransitionCount -eq 0
$complete = $isolated -and $merchantMatches -and $itemMatches -and $quantityMatches -and $indexMatches -and
    $request.checksumValid -eq $true -and $response.checksumValid -eq $true

$result = [ordered]@{
    schema = "God2LiveStage5PurchaseEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    status = if ($complete) { "STAGE_5_PURCHASE_CORRELATED_MUTATION_BLOCKED" } else { "STAGE_5_EVIDENCE_BLOCKED" }
    purchase = if ($null -ne $request) {
        [ordered]@{
            request = $request
            response = $response
            responseLatencyMs = if ($null -ne $response) {
                [int64]$response.timestampUnixMs - [int64]$request.timestampUnixMs
            } else { $null }
            correlation = [ordered]@{
                merchantObjectTokenMatches = $merchantMatches
                itemTemplateCandidateMatches = $itemMatches
                quantityCandidateMatches = $quantityMatches
                itemIndexCandidateMatches = $indexMatches
            }
            priceCandidate = if ($null -ne $response) {
                [ordered]@{
                    value = [uint32]$response.priceOrWalletValueCandidate
                    frameOffset = 40
                    quantityOneMakesUnitAndTotalNumericallyEqual = ([int]$request.quantityCandidate -eq 1)
                    authority = "OBSERVED_NUMERIC_CANDIDATE_SEMANTICS_UNVERIFIED"
                }
            } else { $null }
        }
    } else { $null }
    isolation = [ordered]@{
        controlledActionIsolationSatisfied = $isolated
        buyRequestCount = $buyRequests.Count
        purchaseResponseCount = $purchaseResponses.Count
        nonIdleOutboundCount = $nonIdleOutbound.Count
        movementCount = $movementCount
        worldTransitionCount = $worldTransitionCount
    }
    staticEvidence = [ordered]@{
        purchaseResponseHandlerRva = if ($null -ne $handler) { [string]$handler.HandlerRva } else { $null }
        priorObservationCount = if ($null -ne $handler) { [int]$handler.ObservationCount } else { 0 }
        priorObservationSessionCount = if ($null -ne $handler) { @($handler.ObservationSessions).Count } else { 0 }
        priorSemanticAssociation = "CROSS_DOMAIN_NOT_SHOP_AUTHORITY"
        registrySource = "protocol/evidence/current-build/client-dispatch-registry.json"
    }
    gate = [ordered]@{
        purchaseRequestObserved = ($null -ne $request)
        purchaseResponseObserved = ($null -ne $response)
        merchantObjectTokenCorrelated = $merchantMatches
        itemTemplateCandidateCorrelated = $itemMatches
        quantityCandidateCorrelated = $quantityMatches
        itemIndexCandidateCorrelated = $indexMatches
        priceOrWalletNumericCandidateObserved = ($null -ne $response)
        priceVerified = $false
        walletDeltaObserved = $false
        typedInventoryMutationObserved = $false
        runtimeWorldHandlerRecords = $runtimeHandlerRecords.Count
        productionPromotion = $false
    }
    classification = [ordered]@{
        observed = @("single C2S 0x38 purchase request", "single S2C 0x3B purchase response", "merchant/item/quantity/index repeated across request and response")
        discovered = @("World 0x3B handler RVA")
        unresolved = @("price versus wallet value semantics", "wallet before/after", "typed inventory mutation", "inventory container semantic", "result code enum")
    }
    firstBrokenEdge = "PostDecrypt0x3B -> RuntimeHandlerDecoded -> TypedInventoryOrWalletMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign.status = if ($complete) { "STAGE_5_CORRELATION_COMPLETE" } else { "STAGE_5_EVIDENCE_BLOCKED" }
$campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
$campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
$campaignTemporary = "$CampaignPath.tmp-$PID"
[IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
$result | ConvertTo-Json -Depth 16
