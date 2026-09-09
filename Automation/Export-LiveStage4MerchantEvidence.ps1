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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage4-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage4-merchant-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$registryPath = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\client-dispatch-registry.json"
$registry = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 4

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

function Convert-InteractionRequest {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 8 -or $bytes[0] -ne 8 -or
        $bytes[1] -ne 0 -or $bytes[2] -ne 0x37) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        objectToken = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
        reservedWordCandidate = [int]$bytes[5] -bor ([int]$bytes[6] -shl 8)
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        callerRva = ([string]$Item.caller -replace '^.*rva=', '')
        authority = "OBSERVED_RUNTIME_INTERACTION_REQUEST"
    }
}

function Convert-DialogResponse {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -lt 10 -or $bytes[2] -ne 0x7A) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        frameLength = $bytes.Length
        payloadLengthCandidate = [int]$bytes[3]
        dialogRouteCandidate = [int]$bytes[5] -bor ([int]$bytes[6] -shl 8)
        objectToken = [int]$bytes[7] -bor ([int]$bytes[8] -shl 8)
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "OBSERVED_RUNTIME_OBJECT_MATCHED_DIALOG_RESPONSE"
    }
}

function Convert-DialogSelection {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 10 -or $bytes[2] -ne 0x85) { return $null }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        objectToken = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
        selectorByteCandidate = [int]$bytes[5]
        opaqueWordCandidate = [int]$bytes[6] -bor ([int]$bytes[7] -shl 8)
        opaqueByteCandidate = [int]$bytes[8]
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        callerRva = ([string]$Item.caller -replace '^.*rva=', '')
        authority = "OBSERVED_RUNTIME_DIALOG_SELECTION_CANDIDATE"
    }
}

function Convert-ShopOpenResponse {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 16 -or $bytes[2] -ne 0x68) { return $null }
    $valueA = [uint32]([int]$bytes[5] -bor ([int]$bytes[6] -shl 8) -bor
        ([int]$bytes[7] -shl 16) -bor ([int]$bytes[8] -shl 24))
    $valueB = [uint32]([int]$bytes[11] -bor ([int]$bytes[12] -shl 8) -bor
        ([int]$bytes[13] -shl 16) -bor ([int]$bytes[14] -shl 24))
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        objectToken = [int]$bytes[3] -bor ([int]$bytes[4] -shl 8)
        opaqueValueCandidateA = $valueA
        opaqueWordCandidate = [int]$bytes[9] -bor ([int]$bytes[10] -shl 8)
        opaqueValueCandidateB = $valueB
        valueSemantics = "UNRESOLVED_NOT_PRICE_AUTHORITY"
        checksumValid = Test-AdditiveChecksum -Bytes $bytes
        authority = "USER_DECLARED_SHOP_VISIBLE_PLUS_OBJECT_MATCHED_RUNTIME_RESPONSE"
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

$interactionRequests = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x37" -and [int]$_.PlaintextLength -eq 8
} | ForEach-Object { Convert-InteractionRequest -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$dialogs = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x7A"
} | ForEach-Object { Convert-DialogResponse -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$selections = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x85" -and [int]$_.PlaintextLength -eq 10
} | ForEach-Object { Convert-DialogSelection -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })
$shopResponses = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
    [string]$_.Opcode -eq "0x68" -and [int]$_.PlaintextLength -eq 16
} | ForEach-Object { Convert-ShopOpenResponse -Item $_ } | Where-Object { $null -ne $_ } |
    Sort-Object @{ Expression = { [int64]$_.sequence } })

$chains = [Collections.Generic.List[object]]::new()
foreach ($request in $interactionRequests) {
    $dialog = $dialogs | Where-Object {
        [int]$_.objectToken -eq [int]$request.objectToken -and [int64]$_.timestampUnixMs -ge [int64]$request.timestampUnixMs -and
        ([int64]$_.timestampUnixMs - [int64]$request.timestampUnixMs) -le 1000
    } | Select-Object -First 1
    $selection = if ($null -ne $dialog) {
        $selections | Where-Object {
            [int]$_.objectToken -eq [int]$request.objectToken -and [int64]$_.timestampUnixMs -ge [int64]$dialog.timestampUnixMs -and
            ([int64]$_.timestampUnixMs - [int64]$dialog.timestampUnixMs) -le 10000
        } | Select-Object -First 1
    } else { $null }
    $shopResponse = if ($null -ne $selection) {
        $shopResponses | Where-Object {
            [int]$_.objectToken -eq [int]$request.objectToken -and [int64]$_.timestampUnixMs -ge [int64]$selection.timestampUnixMs -and
            ([int64]$_.timestampUnixMs - [int64]$selection.timestampUnixMs) -le 1000
        } | Select-Object -First 1
    } else { $null }
    $chains.Add([ordered]@{
        chainId = "MERCHANT-S4-$($request.sequence)"
        objectToken = [int]$request.objectToken
        interactionRequest = $request
        dialogResponse = $dialog
        interactionToDialogMs = if ($null -ne $dialog) {
            [int64]$dialog.timestampUnixMs - [int64]$request.timestampUnixMs
        } else { $null }
        dialogSelection = $selection
        dialogVisibleBeforeSelectionMs = if ($null -ne $selection) {
            [int64]$selection.timestampUnixMs - [int64]$dialog.timestampUnixMs
        } else { $null }
        shopOpenResponse = $shopResponse
        selectionToShopResponseMs = if ($null -ne $shopResponse) {
            [int64]$shopResponse.timestampUnixMs - [int64]$selection.timestampUnixMs
        } else { $null }
        completeShopOpenCandidate = ($null -ne $shopResponse)
    })
}

$merchantChains = @($chains | Where-Object { $_.completeShopOpenCandidate -eq $true })
$merchantChain = $merchantChains | Select-Object -Last 1
$worldTransitionCount = @($events | Where-Object {
    [string]$_.CaptureStage -eq "PostDecrypt" -and [string]$_.PacketDirection -eq "ServerToClient" -and
    [string]$_.Opcode -eq "0x61"
}).Count
$runtimeHandlerRecords = @($events | Where-Object {
    [string]$_.CaptureStage -eq "HandlerDecoded" -and
    ([string]$_.Opcode -in @("0x68", "0x7A") -or
        ($null -ne $merchantChain -and [string]$_.ContextInvocationId -eq [string]$merchantChain.shopOpenResponse.hookInvocationId))
})
$dialogHandler = $registry.Entries | Where-Object {
    [string]$_.State -eq "World" -and [string]$_.Direction -eq "ServerToClient" -and [string]$_.Opcode -eq "0x7A"
} | Select-Object -First 1
$shopHandler = $registry.Entries | Where-Object {
    [string]$_.State -eq "World" -and [string]$_.Direction -eq "ServerToClient" -and [string]$_.Opcode -eq "0x68"
} | Select-Object -First 1
$complete = $merchantChains.Count -eq 1 -and $null -ne $merchantChain
$isolated = $interactionRequests.Count -eq 1 -and $worldTransitionCount -eq 0

$result = [ordered]@{
    schema = "God2LiveStage4MerchantEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    status = if ($complete) { "STAGE_4_MERCHANT_OPEN_CORRELATED_CATALOG_BLOCKED" } else { "STAGE_4_EVIDENCE_BLOCKED" }
    selectedChain = $merchantChain
    observedInteractionChains = @($chains)
    selectionAuthority = "LATEST_OBJECT_MATCHED_0X37_0X7A_0X85_0X68_CHAIN_BEFORE_USER_COMPLETION"
    contamination = [ordered]@{
        controlledActionIsolationSatisfied = $isolated
        interactionRequestCount = $interactionRequests.Count
        worldTransitionCount = $worldTransitionCount
        reasons = @(
            if ($interactionRequests.Count -ne 1) { "MULTIPLE_0X37_INTERACTIONS_AFTER_BASELINE" }
            if ($worldTransitionCount -gt 0) { "ADDITIONAL_0X61_WORLD_TRANSITION_AFTER_BASELINE" }
        )
        evidenceUse = "CORRELATION_ONLY_NOT_CLEAN_SINGLE_ACTION_DIFFERENTIAL"
    }
    staticEvidence = [ordered]@{
        dialogHandlerRva = if ($null -ne $dialogHandler) { [string]$dialogHandler.HandlerRva } else { $null }
        dialogObservationCount = if ($null -ne $dialogHandler) { [int]$dialogHandler.ObservationCount } else { 0 }
        dialogObservationSessionCount = if ($null -ne $dialogHandler) { @($dialogHandler.ObservationSessions).Count } else { 0 }
        shopResponseHandlerRva = if ($null -ne $shopHandler) { [string]$shopHandler.HandlerRva } else { $null }
        shopResponseObservationCount = if ($null -ne $shopHandler) { [int]$shopHandler.ObservationCount } else { 0 }
        shopResponseObservationSessionCount = if ($null -ne $shopHandler) { @($shopHandler.ObservationSessions).Count } else { 0 }
        historicalShopSessionSupports0x68 = ($null -ne $shopHandler -and [int]$shopHandler.ObservationCount -gt 0)
        historicalAssociation = "REGISTERED_LABELLED_SHOP_SESSION"
        registrySource = "protocol/evidence/current-build/client-dispatch-registry.json"
    }
    gate = [ordered]@{
        merchantInteractionObserved = ($null -ne $merchantChain)
        objectMatchedDialogObserved = ($null -ne $merchantChain -and $null -ne $merchantChain.dialogResponse)
        objectMatchedSelectionObserved = ($null -ne $merchantChain -and $null -ne $merchantChain.dialogSelection)
        objectMatchedShopResponseObserved = ($null -ne $merchantChain -and $null -ne $merchantChain.shopOpenResponse)
        runtimeWorldHandlerRecords = $runtimeHandlerRecords.Count
        typedMerchantUiMutationObserved = $false
        shopIdentityResolved = $false
        shopCatalogObserved = $false
        itemTemplateObserved = $false
        priceObserved = $false
        productionPromotion = $false
    }
    classification = [ordered]@{
        observed = @("C2S 0x37 interaction", "S2C 0x7A object-matched dialog", "C2S 0x85 selection", "S2C 0x68 object-matched shop-open candidate")
        discovered = @("World 0x7A handler RVA", "World 0x68 handler RVA", "historical 0x68 Shop session association")
        unresolved = @("merchant template identity", "shop catalog", "item template", "price", "typed merchant UI mutation", "0x68 opaque value semantics")
    }
    firstBrokenEdge = "PostDecrypt0x68 -> RuntimeHandlerDecoded -> MerchantUiOrShopCatalogMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign.status = if ($complete) { "STAGE_4_CORRELATION_COMPLETE_WITH_CONTAMINATION" } else { "STAGE_4_EVIDENCE_BLOCKED" }
$campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
$campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
$campaignTemporary = "$CampaignPath.tmp-$PID"
[IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
$result | ConvertTo-Json -Depth 16
