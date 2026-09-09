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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage6-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage6-sale-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stage5 = Get-Content -LiteralPath (Join-Path $analysisRoot "stage5-purchase-evidence.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$registryPath = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\client-dispatch-registry.json"
$registry = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 6

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

function Convert-SellRequest {
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
        merchantObjectToken = [int]([BitConverter]::ToUInt16($bytes, 3))
        itemTemplateCandidate = [int]([BitConverter]::ToUInt16($bytes, 5))
        quantityCandidate = [int]$bytes[7]
        operationModeCandidate = [int]$bytes[8]
        inventorySlotCandidate = [int]([BitConverter]::ToUInt16($bytes, 9))
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        callerRva = ([string]$Item.caller -replace '^.*rva=', '')
        authority = "USER_DECLARED_SINGLE_SALE_PLUS_OBSERVED_RUNTIME_REQUEST"
    }
}

function Convert-SaleResponse {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 25 -or $bytes[0] -ne 25 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x41) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        resultCodeCandidate = [int]([BitConverter]::ToUInt16($bytes, 3))
        inventoryContainerCandidate = [int]([BitConverter]::ToUInt16($bytes, 5))
        walletBalanceCandidate = [uint32]([BitConverter]::ToUInt32($bytes, 8))
        merchantObjectToken = [uint32]([BitConverter]::ToUInt32($bytes, 13))
        quantityCandidate = [int]([BitConverter]::ToUInt16($bytes, 17))
        inventoryStateCandidate = [int]$bytes[19]
        inventorySlotCandidate = [int]([BitConverter]::ToUInt16($bytes, 22))
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "USER_DECLARED_SALE_SUCCESS_PLUS_OBSERVED_RUNTIME_RESPONSE"
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

$sellRequests = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x38" -and [int]$_.PlaintextLength -eq 12
} | ForEach-Object { Convert-SellRequest -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$saleResponses = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x41" -and [int]$_.PlaintextLength -eq 25
} | ForEach-Object { Convert-SaleResponse -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })

$request = $sellRequests | Select-Object -First 1
$response = if ($null -ne $request) {
    $saleResponses | Where-Object {
        [int64]$_.timestampUnixMs -ge [int64]$request.timestampUnixMs -and
        ([int64]$_.timestampUnixMs - [int64]$request.timestampUnixMs) -le 1000
    } | Select-Object -First 1
} else { $null }
$runtimeHandlerRecords = @($events | Where-Object {
    [string]$_.CaptureStage -eq "HandlerDecoded" -and
    ([string]$_.Opcode -eq "0x41" -or ($null -ne $response -and [string]$_.ContextInvocationId -eq [string]$response.hookInvocationId))
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
    [string]$_.State -eq "World" -and [string]$_.Direction -eq "ServerToClient" -and [string]$_.Opcode -eq "0x41"
} | Select-Object -First 1

$priorRequest = $stage5.purchase.request
$priorResponse = $stage5.purchase.response
$priorBalance = [uint32]$priorResponse.priceOrWalletValueCandidate
$currentBalance = if ($null -ne $response) { [uint32]$response.walletBalanceCandidate } else { [uint32]0 }
$walletDelta = if ($null -ne $response) { [int64]$currentBalance - [int64]$priorBalance } else { $null }
$merchantMatches = $null -ne $request -and $null -ne $response -and
    [uint32]$request.merchantObjectToken -eq [uint32]$response.merchantObjectToken -and
    [uint32]$request.merchantObjectToken -eq [uint32]$priorRequest.merchantObjectToken
$itemMatchesPriorPurchase = $null -ne $request -and
    [uint32]$request.itemTemplateCandidate -eq [uint32]$priorRequest.itemTemplateCandidate
$quantityMatches = $null -ne $request -and $null -ne $response -and
    [int]$request.quantityCandidate -eq [int]$response.quantityCandidate -and
    [int]$request.quantityCandidate -eq [int]$priorRequest.quantityCandidate
$slotMatches = $null -ne $request -and $null -ne $response -and
    [int]$request.inventorySlotCandidate -eq [int]$response.inventorySlotCandidate
$modeDiffers = $null -ne $request -and
    [int]$priorRequest.operationModeCandidate -eq 1 -and [int]$request.operationModeCandidate -eq 2
$isolated = $sellRequests.Count -eq 1 -and $saleResponses.Count -eq 1 -and
    $nonIdleOutbound.Count -eq 1 -and $movementCount -eq 0 -and $worldTransitionCount -eq 0
$complete = $isolated -and $merchantMatches -and $itemMatchesPriorPurchase -and $quantityMatches -and $slotMatches -and
    $modeDiffers -and $walletDelta -gt 0 -and $request.checksumValid -eq $true -and $response.checksumValid -eq $true
$saleUnitPriceCandidate = if ($null -ne $walletDelta -and $null -ne $request -and [int]$request.quantityCandidate -gt 0) {
    [decimal]$walletDelta / [decimal]$request.quantityCandidate
} else { $null }

$result = [ordered]@{
    schema = "God2LiveStage6SaleEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    upperSequenceInclusive = $upperSequence
    status = if ($complete) { "STAGE_6_SALE_CORRELATED_WALLET_DELTA_DERIVED_MUTATION_BLOCKED" } else { "STAGE_6_EVIDENCE_BLOCKED" }
    sale = if ($null -ne $request) {
        [ordered]@{
            request = $request
            response = $response
            responseLatencyMs = if ($null -ne $response) {
                [int64]$response.timestampUnixMs - [int64]$request.timestampUnixMs
            } else { $null }
            correlation = [ordered]@{
                merchantObjectTokenMatches = $merchantMatches
                itemMatchesPriorPurchase = $itemMatchesPriorPurchase
                quantityMatches = $quantityMatches
                inventorySlotMatches = $slotMatches
                purchaseModeOneAndSaleModeTwo = $modeDiffers
            }
            wallet = if ($null -ne $response) {
                [ordered]@{
                    balanceAfterPurchaseCandidate = $priorBalance
                    balanceAfterSaleCandidate = $currentBalance
                    observedDelta = $walletDelta
                    saleTotalCandidate = $walletDelta
                    saleUnitPriceCandidate = $saleUnitPriceCandidate
                    authority = "DERIVED_FROM_CONSECUTIVE_CONTROLLED_TRANSACTION_BALANCE_CANDIDATES"
                    typedWalletMutationObserved = $false
                }
            } else { $null }
        }
    } else { $null }
    buySellComparison = [ordered]@{
        requestOpcode = "0x38"
        purchaseResponseOpcode = "0x3B"
        saleResponseOpcode = "0x41"
        purchaseOperationMode = [int]$priorRequest.operationModeCandidate
        saleOperationMode = if ($null -ne $request) { [int]$request.operationModeCandidate } else { $null }
        merchantObjectToken = [uint32]$priorRequest.merchantObjectToken
        itemTemplateCandidate = [uint32]$priorRequest.itemTemplateCandidate
        quantityCandidate = [int]$priorRequest.quantityCandidate
        purchaseValueReclassifiedAs = "WALLET_BALANCE_AFTER_PURCHASE_CANDIDATE"
        buyPriceAuthority = "UNRESOLVED_NO_PRE_PURCHASE_BALANCE"
    }
    isolation = [ordered]@{
        controlledActionIsolationSatisfied = $isolated
        sellRequestCount = $sellRequests.Count
        saleResponseCount = $saleResponses.Count
        nonIdleOutboundCount = $nonIdleOutbound.Count
        movementCount = $movementCount
        worldTransitionCount = $worldTransitionCount
    }
    staticEvidence = [ordered]@{
        saleResponseHandlerRva = if ($null -ne $handler) { [string]$handler.HandlerRva } else { $null }
        priorObservationCount = if ($null -ne $handler) { [int]$handler.ObservationCount } else { 0 }
        priorObservationSessionCount = if ($null -ne $handler) { @($handler.ObservationSessions).Count } else { 0 }
        historicalSessionLabelSupportPresent = ($null -ne $handler -and @($handler.ObservationSessions).Count -gt 0)
        historicalShopSellAssociationVerified = $false
        registrySource = "protocol/evidence/current-build/client-dispatch-registry.json"
    }
    gate = [ordered]@{
        saleRequestObserved = ($null -ne $request)
        saleResponseObserved = ($null -ne $response)
        buySellOperationModesDifferentiated = $modeDiffers
        itemTemplateCandidateCorrelated = $itemMatchesPriorPurchase
        quantityCandidateCorrelated = $quantityMatches
        protocolWalletDeltaCandidateObserved = ($null -ne $walletDelta -and $walletDelta -gt 0)
        saleTotalCandidateDerived = ($null -ne $walletDelta -and $walletDelta -gt 0)
        saleUnitPriceCandidateDerived = ($null -ne $walletDelta -and $walletDelta -gt 0 -and [int]$request.quantityCandidate -gt 0)
        buyPriceObserved = $false
        typedWalletMutationObserved = $false
        typedInventoryMutationObserved = $false
        runtimeWorldHandlerRecords = $runtimeHandlerRecords.Count
        productionPromotion = $false
    }
    classification = [ordered]@{
        observed = @(
            "single C2S 0x38 mode-2 sale request",
            "single S2C 0x41 sale response",
            "merchant/quantity/slot repeated",
            "consecutive balance candidates $priorBalance then $currentBalance"
        )
        derived = @(
            "sale total candidate $walletDelta",
            "sale unit price candidate $saleUnitPriceCandidate",
            "Stage 5 value $priorBalance is wallet-balance candidate"
        )
        discovered = @("World 0x41 handler RVA", "historical 0x41 observations with unpromoted session-label support")
        unresolved = @("pre-purchase wallet balance", "buy unit price", "typed wallet mutation", "typed inventory mutation", "authoritative item template mapping")
    }
    firstBrokenEdge = "PostDecrypt0x41 -> RuntimeHandlerDecoded -> TypedWalletOrInventoryMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign.status = if ($complete) { "STAGE_6_CORRELATION_COMPLETE" } else { "STAGE_6_EVIDENCE_BLOCKED" }
$campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
$campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
$campaignTemporary = "$CampaignPath.tmp-$PID"
[IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
$result | ConvertTo-Json -Depth 16
