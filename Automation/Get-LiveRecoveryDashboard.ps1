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

function Read-JsonLinesShared {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [scriptblock]$OnObject
    )

    $result = [ordered]@{ parsed = 0; malformed = 0 }
    if (-not (Test-Path -LiteralPath $Path)) {
        return $result
    }

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true, 65536, $false)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }
                try {
                    $item = $line | ConvertFrom-Json
                    & $OnObject $item
                    $result.parsed++
                }
                catch {
                    $result.malformed++
                }
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return $result
}

function Read-LinesShared {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [scriptblock]$OnLine
    )

    $result = [ordered]@{ parsed = 0; malformed = 0 }
    if (-not (Test-Path -LiteralPath $Path)) {
        return $result
    }

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true, 65536, $false)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }
                try {
                    & $OnLine $line
                    $result.parsed++
                }
                catch {
                    $result.malformed++
                }
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return $result
}

function Increment-Count {
    param([hashtable]$Table, [string]$Key)
    if ([string]::IsNullOrWhiteSpace($Key)) {
        $Key = "(none)"
    }
    if ($Table.ContainsKey($Key)) {
        $Table[$Key]++
    }
    else {
        $Table[$Key] = 1
    }
}

function Convert-CountTable {
    param([hashtable]$Table)
    return @($Table.GetEnumerator() |
        Sort-Object Name |
        ForEach-Object { [ordered]@{ key = [string]$_.Name; count = [int64]$_.Value } })
}

function Get-TraceRelativePath {
    param([string]$Root, [string]$Path)
    $prefix = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Path.Substring($prefix.Length).Replace('\', '/')
    }
    return $Path.Replace('\', '/')
}

if ([string]::IsNullOrWhiteSpace($TraceRoot)) {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        throw "Active capture state was not found: $StatePath"
    }
    $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $TraceRoot = [string]$state.capture.traceRoot
}

$TraceRoot = [System.IO.Path]::GetFullPath($TraceRoot)
if (-not (Test-Path -LiteralPath $TraceRoot -PathType Container)) {
    throw "Trace root was not found: $TraceRoot"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TraceRoot "analysis\live-recovery-dashboard.json"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$metadataPath = Join-Path $TraceRoot "metadata.jsonl"
$pairsPath = Join-Path $TraceRoot "sensitive\live-validation-pairs.jsonl"
$semanticPath = Join-Path $TraceRoot "sensitive\semantic-events.jsonl"
$protocolCatalogPath = Join-Path $TraceRoot "analysis\protocol-candidate-catalog.json"
$fieldClustersPath = Join-Path $TraceRoot "analysis\live-field-clusters.json"
$battleReadinessPath = Join-Path $TraceRoot "analysis\battle-readiness.json"
$serverGapPath = Join-Path $TraceRoot "analysis\server-gap-matrix.json"
$databaseGapPath = Join-Path $TraceRoot "analysis\database-gap-matrix.json"
$stagingCandidatesPath = Join-Path $TraceRoot "analysis\staging-candidates.json"
$probePlanStatusPath = Join-Path $TraceRoot "analysis\probe-plan-status.json"
$campaignPath = Join-Path $TraceRoot "analysis\controlled-gameplay-campaign.json"
$stage2EvidencePath = Join-Path $TraceRoot "analysis\stage2-skill-evidence.json"
$stage3EvidencePath = Join-Path $TraceRoot "analysis\stage3-portal-evidence.json"
$stage4EvidencePath = Join-Path $TraceRoot "analysis\stage4-merchant-evidence.json"
$stage5EvidencePath = Join-Path $TraceRoot "analysis\stage5-purchase-evidence.json"
$stage6EvidencePath = Join-Path $TraceRoot "analysis\stage6-sale-evidence.json"
$stage7EvidencePath = Join-Path $TraceRoot "analysis\stage7-merchant-close-evidence.json"
$stage8EvidencePath = Join-Path $TraceRoot "analysis\stage8-positioning-evidence.json"
$environmentPath = Join-Path $TraceRoot "God2ClientTraceProbe.attach.env"
$generalLogPath = Join-Path $TraceRoot "general.log"

$metadataStages = @{}
$metadataDirections = @{}
$plaintextOpcodes = @{}
$plaintextStageOpcodes = @{}
$transportApis = @{}
$metadataLastSequence = $null
$metadataMaximumSequence = $null
$metadataSequenceRegressions = 0
$metadataIncomplete = 0
$metadataSensitiveRedacted = 0
$metadataFirstTimestamp = $null
$metadataLastTimestamp = $null
$handlerDecodedRecords = 0
$handlerDecodedOpcodes = @{}

$metadataRead = Read-JsonLinesShared -Path $metadataPath -OnObject {
    param($item)
    Increment-Count $metadataStages ([string]$item.CaptureStage)
    Increment-Count $metadataDirections ([string]$item.PacketDirection)
    if ([string]$item.CaptureStage -eq "Transport") {
        Increment-Count $transportApis ([string]$item.Api)
    }
    if ($item.Plaintext -eq $true -and -not [string]::IsNullOrWhiteSpace([string]$item.Opcode)) {
        $opcodeKey = "{0}|{1}" -f [string]$item.PacketDirection, [string]$item.Opcode
        $stageKey = "{0}|{1}|{2}" -f [string]$item.CaptureStage, [string]$item.PacketDirection, [string]$item.Opcode
        Increment-Count $plaintextOpcodes $opcodeKey
        Increment-Count $plaintextStageOpcodes $stageKey
        if ([string]$item.CaptureStage -eq "HandlerDecoded") {
            $script:handlerDecodedRecords++
            Increment-Count $script:handlerDecodedOpcodes ([string]$item.Opcode)
        }
    }
    if ($item.EvidenceIncomplete -eq $true) { $script:metadataIncomplete++ }
    if ($item.SensitivePayloadRedacted -eq $true) { $script:metadataSensitiveRedacted++ }
    if ($null -ne $item.sequence) {
        $sequence = [int64]$item.sequence
        if ($null -ne $script:metadataLastSequence -and $sequence -le $script:metadataLastSequence) {
            $script:metadataSequenceRegressions++
        }
        $script:metadataLastSequence = $sequence
        if ($null -eq $script:metadataMaximumSequence -or $sequence -gt $script:metadataMaximumSequence) {
            $script:metadataMaximumSequence = $sequence
        }
    }
    if ($null -ne $item.ObservedAtUnixMs) {
        $timestamp = [int64]$item.ObservedAtUnixMs
        if ($null -eq $script:metadataFirstTimestamp) { $script:metadataFirstTimestamp = $timestamp }
        $script:metadataLastTimestamp = $timestamp
    }
}

$pairTransforms = @{}
$pairStatuses = @{}
$pairOpcodes = @{}
$keyMaterialPersisted = 0
$sensitiveAuthenticationFrames = 0
$pairsRead = Read-JsonLinesShared -Path $pairsPath -OnObject {
    param($item)
    Increment-Count $pairTransforms ([string]$item.transform)
    Increment-Count $pairStatuses ("{0}|{1}" -f [string]$item.transform, [string]$item.pairStatus)
    Increment-Count $pairOpcodes ("{0}|{1}|{2}" -f [string]$item.direction, [string]$item.transform, [string]$item.opcode)
    if ($item.keyMaterialPersisted -eq $true) { $script:keyMaterialPersisted++ }
    if ($item.sensitiveAuthenticationFrame -eq $true) { $script:sensitiveAuthenticationFrames++ }
}

$semanticTypes = @{}
$semanticAuthorities = @{}
$semanticProbeCategories = @{}
$objectTokens = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$protocolFrames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$semanticRead = Read-LinesShared -Path $semanticPath -OnLine {
    param($line)
    if ($line -notmatch '"EventType":"([^"\\]*)"') {
        throw "Semantic event does not contain EventType."
    }
    Increment-Count $semanticTypes $matches[1]
    if ($line -match '"AuthorityHint":"([^"\\]*)"') {
        Increment-Count $semanticAuthorities $matches[1]
    }
    else {
        Increment-Count $semanticAuthorities "(none)"
    }
    if ($line -match '"ProbeCategory":"([^"\\]*)"') {
        Increment-Count $semanticProbeCategories $matches[1]
    }
    else {
        Increment-Count $semanticProbeCategories "(none)"
    }
    if ($line -match '"ObjectToken":"([^"\\]+)"') {
        $null = $script:objectTokens.Add($matches[1])
    }
    if ($line -match '"ProtocolFrameId":"([^"\\]+)"') {
        $null = $script:protocolFrames.Add($matches[1])
    }
}

$environment = @{}
if (Test-Path -LiteralPath $environmentPath) {
    foreach ($line in Get-Content -LiteralPath $environmentPath -Encoding UTF8) {
        if ($line -match '^\s*([^#=]+)=(.*)$') {
            $environment[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
}

$hookStatus = [ordered]@{
    winsockSend = if ($transportApis.ContainsKey("send") -and $transportApis["send"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    winsockWSASend = "EVIDENCE_BLOCKED"
    winsockRecv = if ($transportApis.ContainsKey("recv") -and $transportApis["recv"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    winsockWSARecv = "EVIDENCE_BLOCKED"
    iocpCompletion = "EVIDENCE_BLOCKED"
    postDecrypt = if ($metadataStages.ContainsKey("PostDecrypt") -and $metadataStages["PostDecrypt"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    preEncrypt = if ($metadataStages.ContainsKey("PreEncrypt") -and $metadataStages["PreEncrypt"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    parser = if ($semanticTypes.ContainsKey("ParserRead") -and $semanticTypes["ParserRead"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    serializer = if ($semanticTypes.ContainsKey("SerializerWrite") -and $semanticTypes["SerializerWrite"] -gt 0) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    handler = if ($handlerDecodedRecords -gt 0 -or
        ($semanticTypes.ContainsKey("HandlerInvocation") -and $semanticTypes["HandlerInvocation"] -gt 0)) { "ACTIVE" } else { "EVIDENCE_BLOCKED" }
    battleActorSnapshot = "EVIDENCE_BLOCKED"
    battleStateSnapshot = "EVIDENCE_BLOCKED"
}
if (Test-Path -LiteralPath $generalLogPath) {
    $logText = Get-Content -LiteralPath $generalLogPath -Raw -Encoding UTF8
    if ($logText -match 'send hotpatch installed=1') { $hookStatus.winsockSend = "ACTIVE" }
    if ($logText -match 'WSASend hotpatch installed=1') { $hookStatus.winsockWSASend = "ACTIVE" }
    if ($logText -match 'recv hotpatch installed=1') { $hookStatus.winsockRecv = "ACTIVE" }
    if ($logText -match 'WSARecv hotpatch installed=1') { $hookStatus.winsockWSARecv = "ACTIVE" }
    if ($logText -match 'WSAGetOverlappedResult hotpatch installed=1|GQCS hotpatch installed=1|GQCSEx hotpatch installed=1') {
        $hookStatus.iocpCompletion = "ACTIVE"
    }
    if ($logText -match 'probe ready .*WSASend=0/1') { $hookStatus.winsockWSASend = "READY_UNOBSERVED" }
    if ($logText -match 'probe ready .*WSARecv=0/1') { $hookStatus.winsockWSARecv = "READY_UNOBSERVED" }
    if ($logText -match 'plaintext probes postDecrypt=1 preEncrypt=1 handlerDecoded=1') {
        $hookStatus.handler = if ($handlerDecodedRecords -gt 0 -or
            ($semanticTypes.ContainsKey("HandlerInvocation") -and $semanticTypes["HandlerInvocation"] -gt 0)) {
            "ACTIVE"
        }
        else {
            "READY_UNOBSERVED"
        }
    }
    if ($logText -match 'battleActorSnapshot=1') { $hookStatus.battleActorSnapshot = "READY_UNOBSERVED" }
    if ($logText -match 'battleStateSnapshot=1') { $hookStatus.battleStateSnapshot = "READY_UNOBSERVED" }
}

$pidValue = if ($environment.ContainsKey("clientProcessId")) { [int]$environment["clientProcessId"] } else { 0 }
$processAlive = $false
if ($pidValue -gt 0) {
    $processAlive = $null -ne (Get-Process -Id $pidValue -ErrorAction SilentlyContinue)
}

$files = @()
foreach ($path in @($metadataPath, $pairsPath, $semanticPath, (Join-Path $TraceRoot "sensitive\trace.bin"))) {
    if (Test-Path -LiteralPath $path) {
        $file = Get-Item -LiteralPath $path
        $files += [ordered]@{
            path = Get-TraceRelativePath -Root $TraceRoot -Path $path
            bytes = [int64]$file.Length
            lastWriteUtc = $file.LastWriteTimeUtc.ToString("O")
        }
    }
}

$protocolExpansion = [ordered]@{
    catalogStatus = "EVIDENCE_BLOCKED_NOT_GENERATED"
    observedC2SOpcodes = @()
    observedS2COpcodes = @()
    discoveredNeighborOpcodes = @()
    parserBound = 0
    serializerBound = 0
    handlerBoundStatic = 0
    handlerBoundRuntime = 0
    objectBoundStatic = 0
    mutationBoundStatic = 0
    battleObserved = $false
    battleStatus = "READY_UNOBSERVED"
    battleActionCount = 0
    battlePrimaryActionCount = 0
    battleExcessActionCount = 0
    battleClosedLoopActionCount = 0
    battlePrimaryClosedLoopActionCount = 0
    battleHandlerRuntimeCount = 0
    battleEffectDeltaCount = 0
    battleNegativeEffectDeltaCount = 0
    hpMutationCount = 0
    mpMutationCount = 0
    formulaCandidateCount = 0
    formulaVerifiedCount = 0
    formulaDerivedCount = 0
    newC2SOpcodeCount = 0
    newS2COpcodeCount = 0
    parserRuntimeCount = if ($semanticTypes.ContainsKey("ParserRead")) { [int]$semanticTypes["ParserRead"] } else { 0 }
    serializerRuntimeCount = if ($semanticTypes.ContainsKey("SerializerWrite")) { [int]$semanticTypes["SerializerWrite"] } else { 0 }
    handlerRuntimeCount = $handlerDecodedRecords
    handlerRuntimeOpcodeCount = $handlerDecodedOpcodes.Count
    objectRuntimeCount = if ($semanticTypes.ContainsKey("SnapshotObject")) { [int]$semanticTypes["SnapshotObject"] } else { 0 }
    mutationRuntimeCount = if ($semanticTypes.ContainsKey("StateMutation")) { [int]$semanticTypes["StateMutation"] } else { 0 }
    discoveredProtocolCount = 0
    verifiedProtocolCount = 0
    serverGapCount = 0
    databaseGapCount = 0
    stagingCandidateCount = 0
    genericProbeApplied = $false
    campaignStatus = "NOT_STARTED"
    stage2Status = "NOT_STARTED"
    stage2HandlerClosedLoops = 0
    stage2BasicActionCode = $null
    stage2SkillActionCode = $null
    stage2BasicActionParameter = $null
    stage2SkillActionParameter = $null
    stage2TypedHpMutationObserved = $false
    stage2TypedMpMutationObserved = $false
    stage2SkillIdAuthority = "UNOBSERVED"
    stage3Status = "NOT_STARTED"
    stage3SourceMapId = $null
    stage3TargetMapId = $null
    stage3TargetX = $null
    stage3TargetY = $null
    stage3RuntimeWorldHandlerRecords = 0
    stage3TypedWorldMutationObserved = $false
    stage4Status = "NOT_STARTED"
    stage4MerchantObjectToken = $null
    stage4InteractionRequestCount = 0
    stage4WorldTransitionCount = 0
    stage4ControlledActionIsolationSatisfied = $false
    stage4ShopCatalogObserved = $false
    stage4PriceObserved = $false
    stage5Status = "NOT_STARTED"
    stage5ItemTemplateCandidate = $null
    stage5QuantityCandidate = $null
    stage5PriceOrWalletCandidate = $null
    stage5ControlledActionIsolationSatisfied = $false
    stage5TypedInventoryMutationObserved = $false
    stage5WalletDeltaObserved = $false
    stage6Status = "NOT_STARTED"
    stage6SaleOperationMode = $null
    stage6WalletBalanceAfterPurchaseCandidate = $null
    stage6WalletBalanceAfterSaleCandidate = $null
    stage6WalletDeltaCandidate = $null
    stage6SaleUnitPriceCandidate = $null
    stage6ControlledActionIsolationSatisfied = $false
    stage6TypedWalletMutationObserved = $false
    stage7Status = "NOT_STARTED"
    stage7CloseOpcode = $null
    stage7MerchantObjectToken = $null
    stage7ResponseObserved = $false
    stage7ExpectedResponse = "UNRESOLVED"
    stage7ExpectedNoResponseValidated = $false
    stage7ServerMappingStatus = "UNOBSERVED"
    stage7ServerMappingClosed = $false
    stage7ControlledActionIsolationSatisfied = $false
    stage7TypedMerchantUiMutationObserved = $false
    stage8Status = "NOT_STARTED"
    stage8MovementCount = 0
    stage8FinalXCandidate = $null
    stage8FinalYCandidate = $null
    stage8PositioningTrafficReady = $false
    stage8PlayerConfirmedPositioned = $false
    stage8InteractionBaselineReady = $false
    objectTokenCandidates = 0
    crossOpcodeObjectTokens = 0
}
if (Test-Path -LiteralPath $battleReadinessPath) {
    try {
        $battleReadiness = Get-Content -LiteralPath $battleReadinessPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.battleObserved = $battleReadiness.battleRuntimeObserved -eq $true
        $protocolExpansion.battleStatus = [string]$battleReadiness.status
        $protocolExpansion.battleActionCount = [int]$battleReadiness.actionCount
        $protocolExpansion.battlePrimaryActionCount = [int]$battleReadiness.primaryActionCount
        $protocolExpansion.battleExcessActionCount = [int]$battleReadiness.excessActionCount
        $protocolExpansion.battleClosedLoopActionCount = [int]$battleReadiness.closedLoopActionCount
        $protocolExpansion.battlePrimaryClosedLoopActionCount = [int]$battleReadiness.primaryClosedLoopActionCount
        $protocolExpansion.battleHandlerRuntimeCount = [int]$battleReadiness.handlerRuntimeCount
        $protocolExpansion.battleEffectDeltaCount = [int]$battleReadiness.effectDeltaCount
        $protocolExpansion.battleNegativeEffectDeltaCount = [int]$battleReadiness.negativeEffectDeltaCount
        $protocolExpansion.hpMutationCount = [int]$battleReadiness.hpMutationCount
        $protocolExpansion.mpMutationCount = [int]$battleReadiness.mpMutationCount
        $protocolExpansion.formulaCandidateCount = [int]$battleReadiness.formulaCandidateCount
        $protocolExpansion.newC2SOpcodeCount = @($battleReadiness.newC2SOpcodes).Count
        $protocolExpansion.newS2COpcodeCount = @($battleReadiness.newS2COpcodes).Count
    }
    catch {
        $protocolExpansion.battleStatus = "EVIDENCE_BLOCKED_PARSE_ERROR"
    }
}
if (Test-Path -LiteralPath $fieldClustersPath) {
    try {
        $fieldClusters = Get-Content -LiteralPath $fieldClustersPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.objectTokenCandidates = @($fieldClusters.objectTokens).Count
        $protocolExpansion.crossOpcodeObjectTokens = @($fieldClusters.crossOpcodeObjectTokens).Count
    }
    catch {
        $protocolExpansion.catalogStatus = "EVIDENCE_BLOCKED_FIELD_CLUSTER_PARSE_ERROR"
    }
}
if (Test-Path -LiteralPath $protocolCatalogPath) {
    try {
        $catalog = Get-Content -LiteralPath $protocolCatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.catalogStatus = "ACTIVE"
        $protocolExpansion.observedC2SOpcodes = @($catalog.c2s | ForEach-Object { [string]$_.opcode } | Sort-Object -Unique)
        $protocolExpansion.observedS2COpcodes = @($catalog.s2c | ForEach-Object { [string]$_.opcode } | Sort-Object -Unique)
        $protocolExpansion.discoveredNeighborOpcodes = @($catalog.s2c.discoveredNeighbors | ForEach-Object { [string]$_.opcode } | Sort-Object -Unique)
        $protocolExpansion.discoveredProtocolCount = $protocolExpansion.discoveredNeighborOpcodes.Count
        $protocolExpansion.verifiedProtocolCount = @($catalog.s2c | Where-Object { [string]$_.authority -eq "OBSERVED_RUNTIME_PACKET" }).Count
        $protocolExpansion.parserBound = @($catalog.s2c).Count
        $protocolExpansion.serializerBound = @($catalog.c2s).Count
        $protocolExpansion.handlerBoundStatic = @($catalog.s2c | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.handlerRva) }).Count
        $protocolExpansion.handlerBoundRuntime = [Math]::Max(
            @($catalog.s2c | Where-Object { $_.runtimeHandlerObserved -eq $true }).Count,
            $handlerDecodedOpcodes.Count)
        $protocolExpansion.objectBoundStatic = @($catalog.s2c | Where-Object { @($_.objectConsumerRvas).Count -gt 0 }).Count
        $protocolExpansion.mutationBoundStatic = @($catalog.s2c | Where-Object { @($_.mutationCandidateRvas).Count -gt 0 }).Count
        $protocolExpansion.battleObserved = $catalog.battle.runtimeObserved -eq $true
    }
    catch {
        $protocolExpansion.catalogStatus = "EVIDENCE_BLOCKED_PARSE_ERROR"
    }
}
$observedC2SFromMetadata = @($plaintextStageOpcodes.Keys | Where-Object {
    $_ -match '^PreEncrypt\|ClientToServer\|(0x[0-9A-Fa-f]{2})$'
} | ForEach-Object { ($_ -split '\|')[-1].ToUpperInvariant().Replace('0X', '0x') } | Sort-Object -Unique)
$observedS2CFromMetadata = @($plaintextStageOpcodes.Keys | Where-Object {
    $_ -match '^PostDecrypt\|ServerToClient\|(0x[0-9A-Fa-f]{2})$'
} | ForEach-Object { ($_ -split '\|')[-1].ToUpperInvariant().Replace('0X', '0x') } | Sort-Object -Unique)
$protocolExpansion.observedC2SOpcodes = @($protocolExpansion.observedC2SOpcodes + $observedC2SFromMetadata | Sort-Object -Unique)
$protocolExpansion.observedS2COpcodes = @($protocolExpansion.observedS2COpcodes + $observedS2CFromMetadata | Sort-Object -Unique)
if (Test-Path -LiteralPath $serverGapPath) {
    try { $protocolExpansion.serverGapCount = @((Get-Content -LiteralPath $serverGapPath -Raw -Encoding UTF8 | ConvertFrom-Json).rows).Count } catch { }
}
if (Test-Path -LiteralPath $databaseGapPath) {
    try { $protocolExpansion.databaseGapCount = @((Get-Content -LiteralPath $databaseGapPath -Raw -Encoding UTF8 | ConvertFrom-Json).rows).Count } catch { }
}
if (Test-Path -LiteralPath $stagingCandidatesPath) {
    try { $protocolExpansion.stagingCandidateCount = @((Get-Content -LiteralPath $stagingCandidatesPath -Raw -Encoding UTF8 | ConvertFrom-Json).rows).Count } catch { }
}
if (Test-Path -LiteralPath $probePlanStatusPath) {
    try { $protocolExpansion.genericProbeApplied = (Get-Content -LiteralPath $probePlanStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json).applied -eq $true } catch { }
}
if (Test-Path -LiteralPath $campaignPath) {
    try { $protocolExpansion.campaignStatus = [string](Get-Content -LiteralPath $campaignPath -Raw -Encoding UTF8 | ConvertFrom-Json).status } catch { }
}
if (Test-Path -LiteralPath $stage2EvidencePath) {
    try {
        $stage2 = Get-Content -LiteralPath $stage2EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage2Status = [string]$stage2.status
        $protocolExpansion.stage2HandlerClosedLoops = [int]$stage2.gate.runtimeHandlerClosedLoops
        $protocolExpansion.stage2BasicActionCode = [int]$stage2.comparison.basicActionCode
        $protocolExpansion.stage2SkillActionCode = [int]$stage2.comparison.skillActionCode
        $protocolExpansion.stage2BasicActionParameter = [uint32]$stage2.comparison.basicActionParameter
        $protocolExpansion.stage2SkillActionParameter = [uint32]$stage2.comparison.skillActionParameter
        $protocolExpansion.stage2TypedHpMutationObserved = $stage2.gate.typedHpMutationObserved -eq $true
        $protocolExpansion.stage2TypedMpMutationObserved = $stage2.gate.typedMpMutationObserved -eq $true
        $protocolExpansion.stage2SkillIdAuthority = [string]$stage2.comparison.skillIdAuthority
    }
    catch { $protocolExpansion.stage2Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage3EvidencePath) {
    try {
        $stage3 = Get-Content -LiteralPath $stage3EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage3Status = [string]$stage3.status
        $protocolExpansion.stage3SourceMapId = [int]$stage3.transfer.source.clientMapId
        $protocolExpansion.stage3TargetMapId = [int]$stage3.transfer.target.clientMapId
        $protocolExpansion.stage3TargetX = [int]$stage3.transfer.target.x
        $protocolExpansion.stage3TargetY = [int]$stage3.transfer.target.y
        $protocolExpansion.stage3RuntimeWorldHandlerRecords = [int]$stage3.gate.runtimeWorldHandlerRecords
        $protocolExpansion.stage3TypedWorldMutationObserved =
            $stage3.gate.typedMapMutationObserved -eq $true -and $stage3.gate.typedCoordinateMutationObserved -eq $true
    }
    catch { $protocolExpansion.stage3Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage4EvidencePath) {
    try {
        $stage4 = Get-Content -LiteralPath $stage4EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage4Status = [string]$stage4.status
        $protocolExpansion.stage4MerchantObjectToken = [int]$stage4.selectedChain.objectToken
        $protocolExpansion.stage4InteractionRequestCount = [int]$stage4.contamination.interactionRequestCount
        $protocolExpansion.stage4WorldTransitionCount = [int]$stage4.contamination.worldTransitionCount
        $protocolExpansion.stage4ControlledActionIsolationSatisfied = $stage4.contamination.controlledActionIsolationSatisfied -eq $true
        $protocolExpansion.stage4ShopCatalogObserved = $stage4.gate.shopCatalogObserved -eq $true
        $protocolExpansion.stage4PriceObserved = $stage4.gate.priceObserved -eq $true
    }
    catch { $protocolExpansion.stage4Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage5EvidencePath) {
    try {
        $stage5 = Get-Content -LiteralPath $stage5EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage5Status = [string]$stage5.status
        $protocolExpansion.stage5ItemTemplateCandidate = [uint32]$stage5.purchase.request.itemTemplateCandidate
        $protocolExpansion.stage5QuantityCandidate = [int]$stage5.purchase.request.quantityCandidate
        $protocolExpansion.stage5PriceOrWalletCandidate = [uint32]$stage5.purchase.priceCandidate.value
        $protocolExpansion.stage5ControlledActionIsolationSatisfied = $stage5.isolation.controlledActionIsolationSatisfied -eq $true
        $protocolExpansion.stage5TypedInventoryMutationObserved = $stage5.gate.typedInventoryMutationObserved -eq $true
        $protocolExpansion.stage5WalletDeltaObserved = $stage5.gate.walletDeltaObserved -eq $true
    }
    catch { $protocolExpansion.stage5Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage6EvidencePath) {
    try {
        $stage6 = Get-Content -LiteralPath $stage6EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage6Status = [string]$stage6.status
        $protocolExpansion.stage6SaleOperationMode = [int]$stage6.sale.request.operationModeCandidate
        $protocolExpansion.stage6WalletBalanceAfterPurchaseCandidate = [uint32]$stage6.sale.wallet.balanceAfterPurchaseCandidate
        $protocolExpansion.stage6WalletBalanceAfterSaleCandidate = [uint32]$stage6.sale.wallet.balanceAfterSaleCandidate
        $protocolExpansion.stage6WalletDeltaCandidate = [int64]$stage6.sale.wallet.observedDelta
        $protocolExpansion.stage6SaleUnitPriceCandidate = [decimal]$stage6.sale.wallet.saleUnitPriceCandidate
        $protocolExpansion.stage6ControlledActionIsolationSatisfied = $stage6.isolation.controlledActionIsolationSatisfied -eq $true
        $protocolExpansion.stage6TypedWalletMutationObserved = $stage6.gate.typedWalletMutationObserved -eq $true
    }
    catch { $protocolExpansion.stage6Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage7EvidencePath) {
    try {
        $stage7 = Get-Content -LiteralPath $stage7EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage7Status = [string]$stage7.status
        $protocolExpansion.stage7CloseOpcode = "0x39"
        $protocolExpansion.stage7MerchantObjectToken = [uint32]$stage7.close.request.merchantObjectToken
        $protocolExpansion.stage7ResponseObserved = $stage7.close.responseObserved -eq $true
        $protocolExpansion.stage7ExpectedResponse = [string]$stage7.close.expectedResponse
        $protocolExpansion.stage7ExpectedNoResponseValidated = $stage7.gate.expectedNoResponseValidated -eq $true
        $protocolExpansion.stage7ServerMappingStatus = [string]$stage7.serverMapping.status
        $protocolExpansion.stage7ServerMappingClosed = $stage7.gate.serverMappingClosed -eq $true
        $protocolExpansion.stage7ControlledActionIsolationSatisfied = $stage7.isolation.controlledActionIsolationSatisfied -eq $true
        $protocolExpansion.stage7TypedMerchantUiMutationObserved = $stage7.gate.typedMerchantUiMutationObserved -eq $true
    }
    catch { $protocolExpansion.stage7Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}
if (Test-Path -LiteralPath $stage8EvidencePath) {
    try {
        $stage8 = Get-Content -LiteralPath $stage8EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $protocolExpansion.stage8Status = [string]$stage8.status
        $protocolExpansion.stage8MovementCount = [int]$stage8.movement.count
        if ($null -ne $stage8.movement.finalPositionCandidate) {
            $protocolExpansion.stage8FinalXCandidate = [int]$stage8.movement.finalPositionCandidate.x
            $protocolExpansion.stage8FinalYCandidate = [int]$stage8.movement.finalPositionCandidate.y
        }
        $protocolExpansion.stage8PositioningTrafficReady = $stage8.isolation.positioningTrafficReady -eq $true
        $protocolExpansion.stage8PlayerConfirmedPositioned = $stage8.confirmation.playerConfirmedPositioned -eq $true
        $protocolExpansion.stage8InteractionBaselineReady = $stage8.gate.interactionBaselineReady -eq $true
    }
    catch { $protocolExpansion.stage8Status = "EVIDENCE_BLOCKED_PARSE_ERROR" }
}

$dashboard = [ordered]@{
    schema = "God2LiveRecoveryDashboard/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    session = [ordered]@{
        traceRoot = $TraceRoot
        processId = $pidValue
        processAlive = $processAlive
        exactBuildVerified = ($environment["clientBuildVerified"] -eq "true")
        clientVersion = $environment["clientVersion"]
        clientSha256 = $environment["clientSha256"]
        mode = "PASSIVE_READ_ONLY"
        loadedProbeGeneration = "LEGACY_DEEP_SENSOR"
        genericProbePlan = "AVAILABLE_NEXT_SESSION"
    }
    hooks = $hookStatus
    observedRuntime = [ordered]@{
        authority = "OBSERVED_RUNTIME_PACKET"
        metadataRecords = $metadataRead.parsed
        metadataMalformedLines = $metadataRead.malformed
        stages = Convert-CountTable $metadataStages
        directions = Convert-CountTable $metadataDirections
        transportApis = Convert-CountTable $transportApis
        plaintextOpcodes = Convert-CountTable $plaintextOpcodes
        plaintextStageOpcodes = Convert-CountTable $plaintextStageOpcodes
        firstObservedUnixMs = $metadataFirstTimestamp
        lastObservedUnixMs = $metadataLastTimestamp
        maximumMetadataSequence = $metadataMaximumSequence
        metadataSequenceRegressions = $metadataSequenceRegressions
        incompleteTransportEvidence = $metadataIncomplete
        sensitivePayloadsRedacted = $metadataSensitiveRedacted
    }
    validationPairs = [ordered]@{
        records = $pairsRead.parsed
        malformedLines = $pairsRead.malformed
        transforms = Convert-CountTable $pairTransforms
        statuses = Convert-CountTable $pairStatuses
        opcodes = Convert-CountTable $pairOpcodes
        keyMaterialPersisted = $keyMaterialPersisted
        sensitiveAuthenticationFrames = $sensitiveAuthenticationFrames
    }
    semanticEvidence = [ordered]@{
        records = $semanticRead.parsed
        malformedLines = $semanticRead.malformed
        eventTypes = Convert-CountTable $semanticTypes
        authorityHints = Convert-CountTable $semanticAuthorities
        probeCategories = Convert-CountTable $semanticProbeCategories
        protocolFrameTokens = $protocolFrames.Count
        objectTokens = $objectTokens.Count
    }
    protocolExpansion = $protocolExpansion
    formalGates = [ordered]@{
        p0Loss = "UNKNOWN_ACTIVE_SESSION"
        p1Loss = "UNKNOWN_ACTIVE_SESSION"
        sequenceGap = "UNKNOWN_ACTIVE_SESSION"
        pendingAfterDrain = "UNKNOWN_ACTIVE_SESSION"
        metadataWriteOrderRegressions = $metadataSequenceRegressions
        reason = "The loaded legacy probe does not expose final lane counters before session drain. Metadata sequences share a global multi-thread producer with other sinks, so gaps or write-order regressions in this file cannot be classified as loss."
    }
    privacy = [ordered]@{
        keyMaterialPersisted = ($keyMaterialPersisted -gt 0)
        authenticationSecretsPersisted = "NOT_OBSERVED"
        fragmentedTransportPolicy = "FAIL_CLOSED_REDACTION"
    }
    files = $files
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryPath = "$OutputPath.tmp-$PID"
$json = $dashboard | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryPath -Destination $OutputPath -Force

$dashboard | ConvertTo-Json -Depth 12
