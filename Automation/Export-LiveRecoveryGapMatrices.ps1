[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
}
if ([string]::IsNullOrWhiteSpace($TraceRoot)) {
    $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $TraceRoot = [string]$state.capture.traceRoot
}

$analysisRoot = Join-Path $TraceRoot "analysis"
. (Join-Path $PSScriptRoot "LiveRecoveryStageEvidence.ps1")
$catalog = Get-Content -LiteralPath (Join-Path $analysisRoot "protocol-candidate-catalog.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$fields = Get-Content -LiteralPath (Join-Path $analysisRoot "live-field-clusters.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$battlePath = Join-Path $analysisRoot "battle-readiness.json"
$battle = if (Test-Path -LiteralPath $battlePath -PathType Leaf) {
    Get-Content -LiteralPath $battlePath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage2Path = Join-Path $analysisRoot "stage2-skill-evidence.json"
$stage2 = if (Test-Path -LiteralPath $stage2Path -PathType Leaf) {
    Get-Content -LiteralPath $stage2Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage3Path = Join-Path $analysisRoot "stage3-portal-evidence.json"
$stage3 = if (Test-Path -LiteralPath $stage3Path -PathType Leaf) {
    Get-Content -LiteralPath $stage3Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage4Path = Join-Path $analysisRoot "stage4-merchant-evidence.json"
$stage4 = if (Test-Path -LiteralPath $stage4Path -PathType Leaf) {
    Get-Content -LiteralPath $stage4Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage5Path = Join-Path $analysisRoot "stage5-purchase-evidence.json"
$stage5 = if (Test-Path -LiteralPath $stage5Path -PathType Leaf) {
    Get-Content -LiteralPath $stage5Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage6Path = Join-Path $analysisRoot "stage6-sale-evidence.json"
$stage6 = if (Test-Path -LiteralPath $stage6Path -PathType Leaf) {
    Get-Content -LiteralPath $stage6Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage7Path = Join-Path $analysisRoot "stage7-merchant-close-evidence.json"
$stage7 = if (Test-Path -LiteralPath $stage7Path -PathType Leaf) {
    Get-Content -LiteralPath $stage7Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage8Path = Join-Path $analysisRoot "stage8-positioning-evidence.json"
$stage8 = if (Test-Path -LiteralPath $stage8Path -PathType Leaf) {
    Get-Content -LiteralPath $stage8Path -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stage3Observed = Test-LiveEvidenceStatus -Evidence $stage3 -AcceptedStatus @("STAGE_3_CORRELATION_COMPLETE_MUTATION_BLOCKED")
$stage4Observed = Test-LiveEvidenceStatus -Evidence $stage4 -AcceptedStatus @("STAGE_4_MERCHANT_OPEN_CORRELATED_CATALOG_BLOCKED")
$stage5Observed = Test-LiveEvidenceStatus -Evidence $stage5 -AcceptedStatus @("STAGE_5_PURCHASE_CORRELATED_MUTATION_BLOCKED")
$stage6Observed = Test-LiveEvidenceStatus -Evidence $stage6 -AcceptedStatus @("STAGE_6_SALE_CORRELATED_WALLET_DELTA_DERIVED_MUTATION_BLOCKED")
$stage7Observed = (Test-LiveEvidenceStatus -Evidence $stage7 -AcceptedStatus @(
        "STAGE_7_MERCHANT_CLOSE_REQUEST_CORRELATED_RESPONSE_UNOBSERVED",
        "STAGE_7_MERCHANT_CLOSE_CORRELATED_SERVER_MAPPING_CLOSED_UI_MUTATION_BLOCKED"
    )) -and $stage7.gate.merchantCloseRequestObserved -eq $true
$stage8Ready = (Test-LiveEvidenceStatus -Evidence $stage8 -AcceptedStatus @("STAGE_8_POSITIONING_CORRELATED_INTERACTION_BASELINE_READY")) -and
    $stage8.gate.interactionBaselineReady -eq $true

function Write-AtomicJson {
    param([string]$Path, [object]$Value)
    $temporary = "$Path.tmp-$PID"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Get-ExistingServerEvidence {
    param([string]$Opcode)

    switch ($Opcode) {
        "0x5C" {
            return [ordered]@{
                status = "PRIOR_STRUCTURAL_FAMILY_ONLY"
                candidate = "VariableLengthServerFamily"
                priorFrameLengths = @(26, 36, 37, 42, 44, 46, 48, 69, 78)
                liveOverlapFrameLengths = @(78)
                compatibility = "PARTIAL_LENGTH_OVERLAP_SEMANTICS_UNRESOLVED"
                source = "src/God2.ClassicServer.Protocol/OfficialEvidencePackage20260807SupplementCatalog.cs"
                productionAuthority = $false
            }
        }
        "0x5E" {
            return [ordered]@{
                status = "NO_DIRECT_PRIOR_MAPPING"
                candidate = $null
                priorFrameLengths = @()
                liveOverlapFrameLengths = @()
                compatibility = "LIVE_EVIDENCE_ONLY"
                source = $null
                productionAuthority = $false
            }
        }
        "0x6F" {
            return [ordered]@{
                status = "PRIOR_SEMANTIC_CANDIDATE_CONFLICT"
                candidate = "MapBootstrapCandidate"
                priorFrameLengths = @(2560)
                liveOverlapFrameLengths = @()
                compatibility = "OPCODE_MATCH_FRAME_SHAPE_MISMATCH"
                source = "src/God2.ClassicServer.Protocol/OfficialCurrentBuildPacketEvidence.cs"
                note = "The prior catalog names a 2,560-byte bootstrap candidate. This session observes 132-byte frames with a different static consumer chain, so the prior semantic name cannot be inherited."
                productionAuthority = $false
            }
        }
        "0x72" {
            return [ordered]@{
                status = "EXISTING_RUNTIME_SPECIALIZATION_CONFLICT"
                candidate = "NpcSpawn"
                priorFrameLengths = @(24)
                liveOverlapFrameLengths = @()
                compatibility = "OPCODE_MATCH_FRAME_SHAPE_MISMATCH"
                source = "src/God2.ClassicServer.Runtime/OfficialNpcReplicationWireCodec.cs"
                note = "The existing serializer emits a 24-byte specialized spawn frame. This session repeatedly observes 45-byte frames, so the live family must remain independently staged."
                productionAuthority = $false
            }
        }
        "0x73" {
            return [ordered]@{
                status = "PRIOR_STRUCTURAL_FAMILY_ONLY"
                candidate = "EntityRemovalOrDeactivation"
                priorFrameLengths = @(8)
                liveOverlapFrameLengths = @(8)
                compatibility = "STRUCTURAL_LENGTH_MATCH_SEMANTICS_UNRESOLVED"
                source = "src/God2.ClassicServer.Protocol/OfficialEvidencePackage20260807SupplementCatalog.cs"
                note = "An earlier package contains two 8-byte frames, while this session also observes 50-byte variants. The existing NPC despawn serializer uses opcode 0x75, not 0x73."
                productionAuthority = $false
            }
        }
        default {
            return [ordered]@{
                status = "NO_DIRECT_PRIOR_MAPPING"
                candidate = $null
                priorFrameLengths = @()
                liveOverlapFrameLengths = @()
                compatibility = "LIVE_EVIDENCE_ONLY"
                source = $null
                productionAuthority = $false
            }
        }
    }
}

$serverRows = @()
foreach ($entry in $catalog.s2c) {
    $entryFields = @($fields.fields | Where-Object { $_.opcode -eq $entry.opcode })
    $existingEvidence = Get-ExistingServerEvidence -Opcode ([string]$entry.opcode)
    $serverRows += [ordered]@{
        direction = "ServerToClient"
        opcode = [string]$entry.opcode
        runtimePackets = [int]$entry.runtimeObservations
        plaintextAuthority = "OBSERVED_RUNTIME_PACKET"
        parserEnvelope = "VERIFIED_PROTOCOL_ENVELOPE"
        staticHandlerRva = [string]$entry.handlerRva
        handlerBinding = "STATIC_HANDLER_CANDIDATE"
        runtimeHandler = "UNOBSERVED"
        semanticCandidate = [string]$entry.semanticCandidate
        fieldCandidates = $entryFields.Count
        objectConsumerCandidates = @($entry.objectConsumerRvas).Count
        mutationCandidates = @($entry.mutationCandidateRvas).Count
        existingServerEvidence = $existingEvidence
        compatibilityPromotionBlocked = $true
        firstBrokenEdge = "StaticHandlerRva -> RuntimeHandlerInvocation"
        status = "EVIDENCE_BLOCKED"
        productionEnabled = $false
    }
}
foreach ($entry in $catalog.c2s) {
    $serverRows += [ordered]@{
        direction = "ClientToServer"
        opcode = [string]$entry.opcode
        runtimePackets = [int]$entry.runtimeObservations
        plaintextAuthority = "OBSERVED_CORRELATED"
        serializerRva = [string]$entry.serializerRva
        exactLogicalPacketContext = $false
        firstBrokenEdge = "PreEncrypt -> ExactLogicalPacketContext -> Transport"
        status = "EVIDENCE_BLOCKED"
        productionEnabled = $false
    }
}
$serverMatrix = [ordered]@{
    schema = "God2LiveServerGapMatrix/2"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    rows = $serverRows
    battle = [ordered]@{
        runtimeObserved = ($null -ne $battle -and $battle.battleRuntimeObserved -eq $true)
        actionCount = if ($null -ne $battle) { [int]$battle.actionCount } else { 0 }
        closedLoopActionCount = if ($null -ne $battle) { [int]$battle.closedLoopActionCount } else { 0 }
        primaryClosedLoopActionCount = if ($null -ne $battle) { [int]$battle.primaryClosedLoopActionCount } else { 0 }
        handlerRuntimeCount = if ($null -ne $battle) { [int]$battle.handlerRuntimeCount } else { 0 }
        effectDeltaCount = if ($null -ne $battle) { [int]$battle.effectDeltaCount } else { 0 }
        negativeEffectDeltaCount = if ($null -ne $battle) { [int]$battle.negativeEffectDeltaCount } else { 0 }
        hpMutationCount = if ($null -ne $battle) { [int]$battle.hpMutationCount } else { 0 }
        mpMutationCount = if ($null -ne $battle) { [int]$battle.mpMutationCount } else { 0 }
        formulaCandidateCount = if ($null -ne $battle) { [int]$battle.formulaCandidateCount } else { 0 }
        stage2Status = if ($null -ne $stage2) { [string]$stage2.status } else { "UNOBSERVED" }
        stage2HandlerClosedLoops = if ($null -ne $stage2) { [int]$stage2.gate.runtimeHandlerClosedLoops } else { 0 }
        basicActionCode = if ($null -ne $stage2) { [int]$stage2.comparison.basicActionCode } else { $null }
        skillActionCode = if ($null -ne $stage2) { [int]$stage2.comparison.skillActionCode } else { $null }
        skillIdAuthority = if ($null -ne $stage2) { [string]$stage2.comparison.skillIdAuthority } else { "UNOBSERVED" }
        formulaAuthority = "UNOBSERVED"
        firstBrokenEdge = if ($null -ne $battle -and [int]$battle.closedLoopActionCount -gt 0) {
            "RuntimeHandlerRecord -> TypedHpMutation"
        } else { "NaturalBattleAction -> HandlerInvocation" }
        status = if ($null -ne $battle -and [int]$battle.closedLoopActionCount -gt 0) {
            "RUNTIME_CLOSED_LOOP_PARTIAL_MUTATION_BLOCKED"
        } else { "EVIDENCE_BLOCKED" }
        productionEnabled = $false
    }
    portal = [ordered]@{
        status = if ($null -ne $stage3) { [string]$stage3.status } else { "UNOBSERVED" }
        triggerKind = if ($null -ne $stage3) { [string]$stage3.transfer.triggerKind } else { "UNOBSERVED" }
        sourceMapId = if ($null -ne $stage3) { [int]$stage3.transfer.source.clientMapId } else { $null }
        sourceX = if ($null -ne $stage3) { [int]$stage3.transfer.source.x } else { $null }
        sourceY = if ($null -ne $stage3) { [int]$stage3.transfer.source.y } else { $null }
        targetMapId = if ($null -ne $stage3) { [int]$stage3.transfer.target.clientMapId } else { $null }
        targetAreaId = if ($null -ne $stage3) { [int]$stage3.transfer.target.areaId } else { $null }
        targetX = if ($null -ne $stage3) { [int]$stage3.transfer.target.x } else { $null }
        targetY = if ($null -ne $stage3) { [int]$stage3.transfer.target.y } else { $null }
        runtimeWorldHandlerRecords = if ($null -ne $stage3) { [int]$stage3.gate.runtimeWorldHandlerRecords } else { 0 }
        typedMapMutationObserved = if ($null -ne $stage3) { $stage3.gate.typedMapMutationObserved -eq $true } else { $false }
        typedCoordinateMutationObserved = if ($null -ne $stage3) { $stage3.gate.typedCoordinateMutationObserved -eq $true } else { $false }
        firstBrokenEdge = if ($null -ne $stage3) { [string]$stage3.firstBrokenEdge } else { "PortalAction -> S2C 0x61" }
        productionEnabled = $false
    }
    merchant = [ordered]@{
        status = if ($stage4Observed) { [string]$stage4.status } else { "UNOBSERVED" }
        objectToken = if ($stage4Observed) { [int]$stage4.selectedChain.objectToken } else { $null }
        interactionOpcode = "0x37"
        dialogOpcode = "0x7A"
        selectionOpcode = "0x85"
        shopResponseOpcode = "0x68"
        controlledActionIsolationSatisfied = if ($stage4Observed) {
            $stage4.contamination.controlledActionIsolationSatisfied -eq $true
        } else { $false }
        shopCatalogObserved = if ($stage4Observed) { $stage4.gate.shopCatalogObserved -eq $true } else { $false }
        priceObserved = if ($stage4Observed) { $stage4.gate.priceObserved -eq $true } else { $false }
        purchaseStatus = if ($stage5Observed) { [string]$stage5.status } else { "UNOBSERVED" }
        purchasedItemTemplateCandidate = if ($stage5Observed) { [uint32]$stage5.purchase.request.itemTemplateCandidate } else { $null }
        purchaseQuantityCandidate = if ($stage5Observed) { [int]$stage5.purchase.request.quantityCandidate } else { $null }
        purchasePriceOrWalletCandidate = if ($stage5Observed) { [uint32]$stage5.purchase.priceCandidate.value } else { $null }
        purchaseTypedInventoryMutationObserved = if ($stage5Observed) {
            $stage5.gate.typedInventoryMutationObserved -eq $true
        } else { $false }
        saleStatus = if ($stage6Observed) { [string]$stage6.status } else { "UNOBSERVED" }
        saleOperationMode = if ($stage6Observed) { [int]$stage6.sale.request.operationModeCandidate } else { $null }
        walletBalanceAfterPurchaseCandidate = if ($stage6Observed) { [uint32]$stage6.sale.wallet.balanceAfterPurchaseCandidate } else { $null }
        walletBalanceAfterSaleCandidate = if ($stage6Observed) { [uint32]$stage6.sale.wallet.balanceAfterSaleCandidate } else { $null }
        saleWalletDeltaCandidate = if ($stage6Observed) { [int64]$stage6.sale.wallet.observedDelta } else { $null }
        saleUnitPriceCandidate = if ($stage6Observed) { [decimal]$stage6.sale.wallet.saleUnitPriceCandidate } else { $null }
        saleTypedWalletMutationObserved = if ($stage6Observed) { $stage6.gate.typedWalletMutationObserved -eq $true } else { $false }
        closeStatus = if ($stage7Observed) { [string]$stage7.status } else { "UNOBSERVED" }
        closeOpcode = if ($stage7Observed) { "0x39" } else { $null }
        closeMerchantObjectToken = if ($stage7Observed) { [uint32]$stage7.close.request.merchantObjectToken } else { $null }
        closeResponseObserved = if ($stage7Observed) { $stage7.close.responseObserved -eq $true } else { $false }
        closeExpectedResponse = if ($stage7Observed) { "NONE" } else { "UNRESOLVED" }
        closeServerMappingStatus = if ($stage7Observed) { "EXISTING_EXACT_BUILD_RUNTIME_VALIDATED" } else { "UNOBSERVED" }
        closeServerCodec = if ($stage7Observed) { "OfficialNpcInteractionWireCodec.DecodeClose" } else { $null }
        closeServerHandler = if ($stage7Observed) { "OfficialNpcInteractionClosedLoop.ExecuteClose" } else { $null }
        closeTypedUiMutationObserved = if ($stage7Observed) { $stage7.gate.typedMerchantUiMutationObserved -eq $true } else { $false }
        firstBrokenEdge = if ($stage4Observed) { [string]$stage4.firstBrokenEdge } else { "MerchantInteraction -> ShopResponse" }
        productionEnabled = $false
    }
    questPreparation = [ordered]@{
        status = if ($null -ne $stage8) { [string]$stage8.status } else { "UNOBSERVED" }
        movementCount = if ($null -ne $stage8) { [int]$stage8.movement.count } else { 0 }
        finalXCandidate = if ($stage8Ready) { [int]$stage8.movement.finalPositionCandidate.x } else { $null }
        finalYCandidate = if ($stage8Ready) { [int]$stage8.movement.finalPositionCandidate.y } else { $null }
        interactionBaselineReady = $stage8Ready
        authority = if ($stage8Ready) { "OBSERVED_MOVEMENT_PLUS_PLAYER_DECLARATION" } else { "UNCONFIRMED" }
        productionEnabled = $false
    }
    unknownServerOnly = @("authoritative encounter state", "drop roll", "hidden AI state", "server-side validation state")
    compatibilityPolicy = "Prior opcode or frame-length matches are supporting evidence only. Frame-shape conflicts prevent semantic inheritance and production promotion."
    productionWritesPerformed = $false
}

$databaseRows = @(
    [ordered]@{
        entity = "WorldEntityIdentity"
        evidence = "OID16 protocol identity candidates"
        candidateCount = @($fields.objectTokens).Count
        crossOpcodeCandidates = @($fields.crossOpcodeObjectTokens).Count
        missing = @("instance versus template identity", "lifetime", "map ownership", "verified object pointer", "mutation consumer")
        authority = "OBSERVED_PROTOCOL_ID_CANDIDATE"
        status = "EVIDENCE_BLOCKED"
        productionEnabled = $false
    },
    [ordered]@{
        entity = "IndexedVisualStateRecord"
        evidence = "0x72 stable indexed record and static 0x5E-stride consumer"
        candidateCount = @($fields.fields | Where-Object { $_.opcode -eq "0x72" }).Count
        missing = @("record semantic name", "registry owner identity", "runtime handler hit", "before/after mutation witness")
        authority = "STATIC_OBJECT_LAYOUT_CANDIDATE"
        status = "EVIDENCE_BLOCKED"
        productionEnabled = $false
    },
    [ordered]@{
        entity = "BattleStateAndFormula"
        evidence = if ($null -ne $battle -and [int]$battle.closedLoopActionCount -gt 0) {
            "Observed C2S action to S2C PostDecrypt and HandlerDecoded runtime loops"
        } else { "Static length policy only" }
        candidateCount = if ($null -ne $battle) { [int]$battle.actionCount } else { 0 }
        missing = if ($null -ne $battle -and [int]$battle.closedLoopActionCount -gt 0) {
            @("typed HP/MP mutation", "verified target object", "damage consumer", "skill consumer", "formula operands and result")
        } else { @("natural battle runtime", "HP/MP mutation", "damage consumer", "skill consumer", "formula operands and result") }
        authority = if ($null -ne $battle -and [int]$battle.closedLoopActionCount -gt 0) {
            "OBSERVED_RUNTIME_CLOSED_LOOP_PARTIAL"
        } else { "UNOBSERVED" }
        status = "EVIDENCE_BLOCKED"
        productionEnabled = $false
    },
    [ordered]@{
        entity = "PortalDestinationCandidate"
        evidence = if ($stage3Observed) {
            "Controlled movement trigger and checksum-valid S2C 0x61 destination"
        } else { "No controlled portal transition" }
        candidateCount = if ($stage3Observed) { 1 } else { 0 }
        missing = @("runtime typed map mutation", "runtime typed coordinate mutation", "authoritative portal registry identity", "server-only destination validation")
        authority = if ($stage3Observed) { "OBSERVED_RUNTIME_TRANSFER_CORRELATION" } else { "UNOBSERVED" }
        status = if ($stage3Observed) { "STAGING_ONLY" } else { "UNOBSERVED" }
        productionEnabled = $false
    },
    [ordered]@{
        entity = "MerchantCatalogCandidate"
        evidence = if ($stage4Observed) {
            "Object-matched 0x37/0x7A/0x85/0x68 merchant-open chain"
        } else { "No controlled merchant-open chain" }
        candidateCount = if ($stage4Observed) { 1 } else { 0 }
        missing = @("merchant template identity", "shop catalog rows", "item template identity", "price semantics", "typed merchant UI mutation")
        authority = if ($stage4Observed) { "OBSERVED_RUNTIME_MERCHANT_OPEN_CORRELATION" } else { "UNOBSERVED" }
        status = if ($stage4Observed) { "STAGING_ONLY" } else { "UNOBSERVED" }
        productionEnabled = $false
    },
    [ordered]@{
        entity = "MerchantPurchaseCandidate"
        evidence = if ($stage5Observed) {
            "Single isolated 0x38 request and 0x3B response with repeated merchant/item/quantity/index candidates"
        } else { "No controlled merchant purchase" }
        candidateCount = if ($stage5Observed) { 1 } else { 0 }
        missing = @("verified unit price", "wallet before/after", "typed inventory mutation", "authoritative item template mapping")
        authority = if ($stage5Observed) { "OBSERVED_RUNTIME_PURCHASE_CORRELATION" } else { "UNOBSERVED" }
        status = if ($stage5Observed) { "STAGING_ONLY" } else { "UNOBSERVED" }
        productionEnabled = $false
    },
    [ordered]@{
        entity = "MerchantSaleCandidate"
        evidence = if ($stage6Observed) {
            "Single isolated 0x38 mode-2 request and 0x41 response with consecutive wallet-balance candidates"
        } else { "No controlled merchant sale" }
        candidateCount = if ($stage6Observed) { 1 } else { 0 }
        missing = @("typed wallet mutation", "typed inventory mutation", "authoritative item template mapping", "verified buy price")
        authority = if ($stage6Observed) { "OBSERVED_RUNTIME_SALE_WITH_DERIVED_WALLET_DELTA" } else { "UNOBSERVED" }
        status = if ($stage6Observed) { "STAGING_ONLY" } else { "UNOBSERVED" }
        productionEnabled = $false
    }
)
$databaseMatrix = [ordered]@{
    schema = "God2LiveDatabaseGapMatrix/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    rows = $databaseRows
    productionWritesPerformed = $false
    productionDatabaseTouched = $false
}

$stagingRows = @($fields.fields | ForEach-Object {
    [ordered]@{
        candidateId = "field:$($_.opcode):$($_.frameOffset):$($_.type)"
        opcode = [string]$_.opcode
        frameOffset = [int]$_.frameOffset
        type = [string]$_.type
        semanticCandidate = [string]$_.semanticCandidate
        observations = [int]$_.observations
        distinctValues = [int]$_.distinctValues
        stable = [bool]$_.stable
        authority = [string]$_.authority
        status = "STAGING_ONLY"
        productionEnabled = $false
    }
})
$staging = [ordered]@{
    schema = "God2LiveStagingCandidates/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    rows = $stagingRows
    objectTokens = @($fields.objectTokens)
    productionWritesPerformed = $false
}

Write-AtomicJson -Path (Join-Path $analysisRoot "server-gap-matrix.json") -Value $serverMatrix
Write-AtomicJson -Path (Join-Path $analysisRoot "database-gap-matrix.json") -Value $databaseMatrix
Write-AtomicJson -Path (Join-Path $analysisRoot "staging-candidates.json") -Value $staging

[ordered]@{
    serverRows = $serverRows.Count
    databaseRows = $databaseRows.Count
    stagingRows = $stagingRows.Count
    productionWritesPerformed = $false
} | ConvertTo-Json -Depth 4
