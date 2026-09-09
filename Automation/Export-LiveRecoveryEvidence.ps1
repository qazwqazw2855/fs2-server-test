[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
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
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TraceRoot "analysis\semantic-evidence-graph.json"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

function Read-JsonLinesShared {
    param([string]$Path, [scriptblock]$OnObject)
    $result = [ordered]@{ parsed = 0; malformed = 0 }
    if (-not (Test-Path -LiteralPath $Path)) { return $result }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 65536, $false)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $item = $line | ConvertFrom-Json
                    & $OnObject $item
                    $result.parsed++
                }
                catch { $result.malformed++ }
            }
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    return $result
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

. (Join-Path $PSScriptRoot "LiveRecoveryStageEvidence.ps1")

$battleReadinessPath = Join-Path $TraceRoot "analysis\battle-readiness.json"
& (Join-Path $PSScriptRoot "Export-LiveBattleReadiness.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$battleReadiness = Get-Content -LiteralPath $battleReadinessPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stage2CampaignPath = Join-Path $TraceRoot "analysis\stage2-campaign.json"
$stage2EvidencePath = Join-Path $TraceRoot "analysis\stage2-skill-evidence.json"
$stage2Evidence = Get-LiveStageEvidence -CampaignPath $stage2CampaignPath -EvidencePath $stage2EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage2SkillEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage3CampaignPath = Join-Path $TraceRoot "analysis\stage3-campaign.json"
$stage3EvidencePath = Join-Path $TraceRoot "analysis\stage3-portal-evidence.json"
$stage3Evidence = Get-LiveStageEvidence -CampaignPath $stage3CampaignPath -EvidencePath $stage3EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage3PortalEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage4CampaignPath = Join-Path $TraceRoot "analysis\stage4-campaign.json"
$stage4EvidencePath = Join-Path $TraceRoot "analysis\stage4-merchant-evidence.json"
$stage4Evidence = Get-LiveStageEvidence -CampaignPath $stage4CampaignPath -EvidencePath $stage4EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage4MerchantEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage5CampaignPath = Join-Path $TraceRoot "analysis\stage5-campaign.json"
$stage5EvidencePath = Join-Path $TraceRoot "analysis\stage5-purchase-evidence.json"
$stage5Evidence = Get-LiveStageEvidence -CampaignPath $stage5CampaignPath -EvidencePath $stage5EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage5PurchaseEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage6CampaignPath = Join-Path $TraceRoot "analysis\stage6-campaign.json"
$stage6EvidencePath = Join-Path $TraceRoot "analysis\stage6-sale-evidence.json"
$stage6Evidence = Get-LiveStageEvidence -CampaignPath $stage6CampaignPath -EvidencePath $stage6EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage6SaleEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage7CampaignPath = Join-Path $TraceRoot "analysis\stage7-campaign.json"
$stage7EvidencePath = Join-Path $TraceRoot "analysis\stage7-merchant-close-evidence.json"
$stage7Evidence = Get-LiveStageEvidence -CampaignPath $stage7CampaignPath -EvidencePath $stage7EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage7MerchantCloseEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot
$stage8CampaignPath = Join-Path $TraceRoot "analysis\stage8-campaign.json"
$stage8EvidencePath = Join-Path $TraceRoot "analysis\stage8-positioning-evidence.json"
$stage8Evidence = Get-LiveStageEvidence -CampaignPath $stage8CampaignPath -EvidencePath $stage8EvidencePath `
    -ExporterPath (Join-Path $PSScriptRoot "Export-LiveStage8PositioningEvidence.ps1") -StatePath $StatePath -TraceRoot $TraceRoot

function New-Family {
    param($Item)
    return [ordered]@{
        direction = [string]$Item.PacketDirection
        opcode = [string]$Item.Opcode
        stage = [string]$Item.CaptureStage
        count = 0
        lengths = @{}
        firstObservedUnixMs = $null
        lastObservedUnixMs = $null
        uniquePlaintextHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        samplePlaintextHashes = [Collections.Generic.List[string]]::new()
        exactInboundTransportContext = 0
    }
}

$metadataPath = Join-Path $TraceRoot "metadata.jsonl"
$candidateMapPath = Join-Path $TraceRoot "sensitive\deep-probe-candidate-map.json"
$dashboardPath = Join-Path $TraceRoot "analysis\live-recovery-dashboard.json"
$families = @{}
$previousByThread = @{}
$outboundAdjacentPairs = 0
$outboundPlaintextFrames = 0
$inboundExactContextPairs = 0
$inboundPlaintextFrames = 0

$metadataRead = Read-JsonLinesShared -Path $metadataPath -OnObject {
    param($item)
    $threadKey = [string]$item.ThreadId
    if ($item.Plaintext -eq $true -and -not [string]::IsNullOrWhiteSpace([string]$item.Opcode)) {
        $key = "{0}|{1}|{2}" -f [string]$item.CaptureStage, [string]$item.PacketDirection, [string]$item.Opcode
        if (-not $script:families.ContainsKey($key)) {
            $script:families[$key] = New-Family $item
        }
        $family = $script:families[$key]
        $family.count++
        $lengthKey = [string]$item.PlaintextLength
        if ($family.lengths.ContainsKey($lengthKey)) { $family.lengths[$lengthKey]++ } else { $family.lengths[$lengthKey] = 1 }
        $timestamp = [int64]$item.ObservedAtUnixMs
        if ($null -eq $family.firstObservedUnixMs) { $family.firstObservedUnixMs = $timestamp }
        $family.lastObservedUnixMs = $timestamp
        $payloadHash = Get-Sha256Text ([string]$item.PlaintextHex)
        if ($family.uniquePlaintextHashes.Add($payloadHash) -and $family.samplePlaintextHashes.Count -lt 3) {
            $family.samplePlaintextHashes.Add($payloadHash)
        }
        if ([string]$item.CaptureStage -eq "PostDecrypt") {
            $script:inboundPlaintextFrames++
            if (-not [string]::IsNullOrWhiteSpace([string]$item.ContextInvocationId)) {
                $family.exactInboundTransportContext++
                $script:inboundExactContextPairs++
            }
        }
        elseif ([string]$item.CaptureStage -eq "PreEncrypt") {
            $script:outboundPlaintextFrames++
        }
    }

    if ([string]$item.CaptureStage -eq "Transport" -and
        [string]$item.PacketDirection -eq "ClientToServer" -and
        $script:previousByThread.ContainsKey($threadKey)) {
        $previous = $script:previousByThread[$threadKey]
        if ([string]$previous.CaptureStage -eq "PreEncrypt" -and
            [string]$previous.PacketDirection -eq "ClientToServer" -and
            [int64]$item.sequence -gt [int64]$previous.sequence -and
            ([int64]$item.ObservedAtUnixMs - [int64]$previous.ObservedAtUnixMs) -ge 0 -and
            ([int64]$item.ObservedAtUnixMs - [int64]$previous.ObservedAtUnixMs) -le 5) {
            $script:outboundAdjacentPairs++
        }
    }
    $script:previousByThread[$threadKey] = $item
}

$familyRows = @($families.GetEnumerator() | Sort-Object Name | ForEach-Object {
    $family = $_.Value
    [ordered]@{
        id = "packet-family:$($family.direction):$($family.opcode)"
        authority = "OBSERVED_RUNTIME_PACKET"
        direction = $family.direction
        opcode = $family.opcode
        stage = $family.stage
        observations = $family.count
        lengths = @($family.lengths.GetEnumerator() | Sort-Object {[int]$_.Name} | ForEach-Object {
            [ordered]@{ bytes = [int]$_.Name; count = [int]$_.Value }
        })
        uniquePlaintextPayloads = $family.uniquePlaintextHashes.Count
        samplePlaintextSha256 = @($family.samplePlaintextHashes)
        firstObservedUnixMs = $family.firstObservedUnixMs
        lastObservedUnixMs = $family.lastObservedUnixMs
        transportCorrelation = if ($family.stage -eq "PostDecrypt") {
            [ordered]@{
                status = if ($family.exactInboundTransportContext -eq $family.count) { "VERIFIED_PROTOCOL_EDGE" } else { "EVIDENCE_BLOCKED" }
                basis = "ContextInvocationId:SameThreadLatestInboundTransport"
                correlated = $family.exactInboundTransportContext
                total = $family.count
            }
        }
        else {
            [ordered]@{
                status = "SUPPORTING_TRACE_ONLY"
                basis = "SameThreadAdjacentWithin5ms; no exported ContextInvocationId"
                correlated = $outboundAdjacentPairs
                total = $outboundPlaintextFrames
            }
        }
        semanticClosure = "EVIDENCE_BLOCKED_HANDLER_CONSUMER_NOT_OBSERVED"
    }
})

$verifiedDomains = @()
$blockedDomains = @()
if (Test-Path -LiteralPath $candidateMapPath) {
    $candidateMap = Get-Content -LiteralPath $candidateMapPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($domain in $candidateMap.Domains) {
        $row = [ordered]@{
            domain = [string]$domain.Domain
            probe = [string]$domain.Probe
            status = [string]$domain.Status
            authority = [string]$domain.Authority
            candidateCount = [int]$domain.CandidateCount
            verificationStatus = [string]$domain.VerificationStatus
        }
        if ([string]$domain.Authority -eq "VERIFIED") { $verifiedDomains += $row } else { $blockedDomains += $row }
    }
}

$dashboard = if (Test-Path -LiteralPath $dashboardPath) {
    Get-Content -LiteralPath $dashboardPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }

$graph = [ordered]@{
    schema = "God2SemanticEvidenceGraph/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    session = [ordered]@{
        processId = if ($null -ne $dashboard) { $dashboard.session.processId } else { $null }
        clientSha256 = if ($null -ne $dashboard) { $dashboard.session.clientSha256 } else { $null }
        traceRoot = $TraceRoot
        mode = "PASSIVE_READ_ONLY"
        secretPolicy = "HASH_OR_MASK_ONLY_IN_ANALYSIS_OUTPUT"
    }
    packetFamilies = $familyRows
    closedLoop = [ordered]@{
        inbound = [ordered]@{
            path = @("Winsock", "PostDecrypt", "Parser")
            status = if ($inboundPlaintextFrames -gt 0 -and $inboundExactContextPairs -eq $inboundPlaintextFrames) {
                "VERIFIED_PROTOCOL_ENVELOPE"
            } else { "EVIDENCE_BLOCKED" }
            exactTransportToPlaintext = $inboundExactContextPairs
            plaintextFrames = $inboundPlaintextFrames
            handlerConsumer = "EVIDENCE_BLOCKED_RUNTIME_INVOCATION_NOT_OBSERVED"
        }
        outbound = [ordered]@{
            path = @("Serializer", "PreEncrypt", "Winsock")
            status = "OBSERVED_RUNTIME_PACKET_WITH_SUPPORTING_TRANSPORT_CORRELATION"
            sameThreadAdjacentWithin5ms = $outboundAdjacentPairs
            plaintextFrames = $outboundPlaintextFrames
            exactContextEdge = "EVIDENCE_BLOCKED_NOT_EXPORTED_BY_LOADED_PROBE"
        }
        battle = [ordered]@{
            status = [string]$battleReadiness.status
            campaignId = [string]$battleReadiness.campaignId
            primaryActions = [int]$battleReadiness.primaryActionCount
            excessActions = [int]$battleReadiness.excessActionCount
            primaryClosedLoops = [int]$battleReadiness.primaryClosedLoopActionCount
            handlerRuntimeRecords = [int]$battleReadiness.handlerRuntimeCount
            effectDeltaRelations = [int]$battleReadiness.effectDeltaCount
            typedHpMutations = [int]$battleReadiness.hpMutationCount
            formulaCandidates = [int]$battleReadiness.formulaCandidateCount
            firstBrokenEdge = if ([int]$battleReadiness.primaryClosedLoopActionCount -gt 0) {
                "HandlerDecoded -> TypedHpMutation"
            } else { "BattleAction -> RuntimeHandler" }
            productionPromotion = $false
        }
        stage2 = if ($null -ne $stage2Evidence) {
            [ordered]@{
                status = [string]$stage2Evidence.status
                actionCount = @($stage2Evidence.actions).Count
                basicAttackActionCode = [int]$stage2Evidence.comparison.basicActionCode
                basicSkillActionCode = [int]$stage2Evidence.comparison.skillActionCode
                basicAttackParameter = [uint32]$stage2Evidence.comparison.basicActionParameter
                basicSkillParameter = [uint32]$stage2Evidence.comparison.skillActionParameter
                runtimeHandlerClosedLoops = [int]$stage2Evidence.gate.runtimeHandlerClosedLoops
                typedHpMutationObserved = $stage2Evidence.gate.typedHpMutationObserved -eq $true
                typedMpMutationObserved = $stage2Evidence.gate.typedMpMutationObserved -eq $true
                skillIdAuthority = [string]$stage2Evidence.comparison.skillIdAuthority
                formulaAuthority = [string]$stage2Evidence.gate.formulaAuthority
                firstBrokenEdge = [string]$stage2Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
        stage3 = if ($null -ne $stage3Evidence) {
            [ordered]@{
                status = [string]$stage3Evidence.status
                triggerKind = [string]$stage3Evidence.transfer.triggerKind
                explicitTransferRequestObserved = $stage3Evidence.transfer.explicitTransferRequestObserved -eq $true
                sourceMapId = [int]$stage3Evidence.transfer.source.clientMapId
                sourceX = [int]$stage3Evidence.transfer.source.x
                sourceY = [int]$stage3Evidence.transfer.source.y
                targetMapId = [int]$stage3Evidence.transfer.target.clientMapId
                targetAreaId = [int]$stage3Evidence.transfer.target.areaId
                targetX = [int]$stage3Evidence.transfer.target.x
                targetY = [int]$stage3Evidence.transfer.target.y
                triggerToTransitionMs = [int64]$stage3Evidence.transfer.triggerToTransitionMs
                postTransferObjectTokenCandidate = [uint32]$stage3Evidence.transfer.postTransferCandidate.objectTokenCandidate
                runtimeWorldHandlerRecords = [int]$stage3Evidence.gate.runtimeWorldHandlerRecords
                typedMapMutationObserved = $stage3Evidence.gate.typedMapMutationObserved -eq $true
                typedCoordinateMutationObserved = $stage3Evidence.gate.typedCoordinateMutationObserved -eq $true
                firstBrokenEdge = [string]$stage3Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
        stage4 = if ($null -ne $stage4Evidence) {
            [ordered]@{
                status = [string]$stage4Evidence.status
                merchantObjectToken = [int]$stage4Evidence.selectedChain.objectToken
                interactionOpcode = "0x37"
                dialogOpcode = "0x7A"
                selectionOpcode = "0x85"
                shopResponseOpcode = "0x68"
                interactionToDialogMs = [int64]$stage4Evidence.selectedChain.interactionToDialogMs
                selectionToShopResponseMs = [int64]$stage4Evidence.selectedChain.selectionToShopResponseMs
                controlledActionIsolationSatisfied = $stage4Evidence.contamination.controlledActionIsolationSatisfied -eq $true
                interactionRequestCount = [int]$stage4Evidence.contamination.interactionRequestCount
                worldTransitionCount = [int]$stage4Evidence.contamination.worldTransitionCount
                shopCatalogObserved = $stage4Evidence.gate.shopCatalogObserved -eq $true
                priceObserved = $stage4Evidence.gate.priceObserved -eq $true
                firstBrokenEdge = [string]$stage4Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
        stage5 = if ($null -ne $stage5Evidence) {
            [ordered]@{
                status = [string]$stage5Evidence.status
                merchantObjectToken = [int]$stage5Evidence.purchase.request.merchantObjectToken
                itemTemplateCandidate = [uint32]$stage5Evidence.purchase.request.itemTemplateCandidate
                quantityCandidate = [int]$stage5Evidence.purchase.request.quantityCandidate
                itemIndexCandidate = [int]$stage5Evidence.purchase.request.itemIndexCandidate
                priceOrWalletCandidate = [uint32]$stage5Evidence.purchase.priceCandidate.value
                responseLatencyMs = [int64]$stage5Evidence.purchase.responseLatencyMs
                controlledActionIsolationSatisfied = $stage5Evidence.isolation.controlledActionIsolationSatisfied -eq $true
                typedInventoryMutationObserved = $stage5Evidence.gate.typedInventoryMutationObserved -eq $true
                walletDeltaObserved = $stage5Evidence.gate.walletDeltaObserved -eq $true
                firstBrokenEdge = [string]$stage5Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
        stage6 = if ($null -ne $stage6Evidence) {
            [ordered]@{
                status = [string]$stage6Evidence.status
                merchantObjectToken = [int]$stage6Evidence.sale.request.merchantObjectToken
                itemTemplateCandidate = [uint32]$stage6Evidence.sale.request.itemTemplateCandidate
                quantityCandidate = [int]$stage6Evidence.sale.request.quantityCandidate
                saleOperationMode = [int]$stage6Evidence.sale.request.operationModeCandidate
                purchaseBalanceCandidate = [uint32]$stage6Evidence.sale.wallet.balanceAfterPurchaseCandidate
                saleBalanceCandidate = [uint32]$stage6Evidence.sale.wallet.balanceAfterSaleCandidate
                walletDeltaCandidate = [int64]$stage6Evidence.sale.wallet.observedDelta
                saleUnitPriceCandidate = [decimal]$stage6Evidence.sale.wallet.saleUnitPriceCandidate
                controlledActionIsolationSatisfied = $stage6Evidence.isolation.controlledActionIsolationSatisfied -eq $true
                typedWalletMutationObserved = $stage6Evidence.gate.typedWalletMutationObserved -eq $true
                firstBrokenEdge = [string]$stage6Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
        stage7 = if ($null -ne $stage7Evidence) {
            [ordered]@{
                status = [string]$stage7Evidence.status
                closeOpcode = "0x39"
                merchantObjectToken = [uint32]$stage7Evidence.close.request.merchantObjectToken
                responseObserved = $stage7Evidence.close.responseObserved -eq $true
                expectedResponse = [string]$stage7Evidence.close.expectedResponse
                expectedNoResponseValidated = $stage7Evidence.gate.expectedNoResponseValidated -eq $true
                serverMappingStatus = [string]$stage7Evidence.serverMapping.status
                serverMappingClosed = $stage7Evidence.gate.serverMappingClosed -eq $true
                controlledActionIsolationSatisfied = $stage7Evidence.isolation.controlledActionIsolationSatisfied -eq $true
                typedMerchantUiMutationObserved = $stage7Evidence.gate.typedMerchantUiMutationObserved -eq $true
                firstBrokenEdges = @($stage7Evidence.firstBrokenEdges)
                productionPromotion = $false
            }
        } else { $null }
        stage8 = if ($null -ne $stage8Evidence) {
            [ordered]@{
                status = [string]$stage8Evidence.status
                movementCount = [int]$stage8Evidence.movement.count
                finalXCandidate = if ($null -ne $stage8Evidence.movement.finalPositionCandidate) {
                    [int]$stage8Evidence.movement.finalPositionCandidate.x
                } else { $null }
                finalYCandidate = if ($null -ne $stage8Evidence.movement.finalPositionCandidate) {
                    [int]$stage8Evidence.movement.finalPositionCandidate.y
                } else { $null }
                positioningTrafficReady = $stage8Evidence.isolation.positioningTrafficReady -eq $true
                playerConfirmedPositioned = $stage8Evidence.confirmation.playerConfirmedPositioned -eq $true
                interactionBaselineReady = $stage8Evidence.gate.interactionBaselineReady -eq $true
                firstBrokenEdge = [string]$stage8Evidence.firstBrokenEdge
                productionPromotion = $false
            }
        } else { $null }
    }
    discoveredProtocol = [ordered]@{
        authority = "DISCOVERED_PROTOCOL"
        exactBuildDomains = $verifiedDomains
        dispatchRegistry = [ordered]@{
            source = "Reports/OfflineClientReverseEngineering.Dispatch.md"
            provenance = "LegacyCaptureHashBoundButNotCaptureTimeBuildAttested"
            loginOpcodes = 27
            worldOpcodes = 254
            battleOpcodes = 252
            stateSpecificRows = 533
            productionPromotion = $false
        }
    }
    evidenceBlocked = [ordered]@{
        candidateDomains = $blockedDomains
        staticBattleRecovery = [ordered]@{
            status = "EVIDENCE_BLOCKED"
            brokenNode = "Artifacts/OfflineClientReverseEngineering/runtime-capture-manifest.json"
            reason = "Required hash-bound runtime capture manifest is absent; no replacement was fabricated."
        }
        existingBattleCaptureRecovery = [ordered]@{
            status = "EVIDENCE_BLOCKED"
            brokenNode = "battle-basic-attack-level-up labelled capture"
            reason = "The required historical labelled capture set is incomplete."
        }
        serverOnly = [ordered]@{
            authority = "UNKNOWN_SERVER_ONLY"
            examples = @("authoritative encounter state", "drop roll", "hidden AI state", "server-only validation and anti-abuse state")
        }
    }
    serverDatabaseGaps = @(
        [ordered]@{ area = "Idle world telemetry 0x72/0x73/0x5C/0x5E/0x6F"; status = "EVIDENCE_BLOCKED"; need = "handler consumer, typed field layout, stable object token, mutation witness" },
        [ordered]@{ area = "C2S 0x30"; status = "EVIDENCE_BLOCKED"; need = "exact PreEncrypt-to-send context edge and server semantic handler" },
        [ordered]@{ area = "Battle"; status = "EVIDENCE_BLOCKED"; need = if ([int]$battleReadiness.primaryClosedLoopActionCount -gt 0) { "typed HP/MP mutation, verified target object and formula operands" } else { "complete labelled action capture plus S2C handler/state mutation loop" } },
        [ordered]@{ area = "Registry/resource"; status = "EVIDENCE_BLOCKED"; need = "read-only enumeration, stable registry identity, typed consumer" },
        [ordered]@{ area = "Database promotion"; status = "EVIDENCE_BLOCKED"; need = "verified protocol plus object/mutation semantics; candidates must remain non-production" }
    )
    formalGates = if ($null -ne $dashboard) { $dashboard.formalGates } else { $null }
    reader = $metadataRead
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($graph | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
& (Join-Path $PSScriptRoot "Export-LiveProtocolCandidateCatalog.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
& (Join-Path $PSScriptRoot "Export-LiveFieldClusters.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
& (Join-Path $PSScriptRoot "Export-LiveRecoveryGapMatrices.ps1") -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
$graph | ConvertTo-Json -Depth 14
