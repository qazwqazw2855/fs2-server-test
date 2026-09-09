[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$OutputPath,
    [ValidateRange(1000, 120000)]
    [int]$ResponseWindowMs = 15000
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
}
if ([string]::IsNullOrWhiteSpace($TraceRoot)) {
    $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $TraceRoot = [string]$state.capture.traceRoot
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TraceRoot "analysis\battle-readiness.json"
}

function Read-JsonLinesShared {
    param([string]$Path, [scriptblock]$OnObject)
    $result = [ordered]@{ parsed = 0; malformed = 0 }
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

function Get-BattleTargetPositions {
    param([byte[]]$Frame)
    $positions = [Collections.Generic.List[int]]::new()
    foreach ($group in 0..2) {
        $offset = 7 + ($group * 2)
        $mask = [int]$Frame[$offset] -bor ([int]$Frame[$offset + 1] -shl 8)
        foreach ($bit in 0..13) {
            if (($mask -band (1 -shl $bit)) -ne 0) { $positions.Add(($group * 14) + $bit) }
        }
    }
    return @($positions)
}

function Get-BattleCommandFields {
    param([string]$Hex)
    $bytes = Convert-HexToBytes -Hex $Hex
    if ($null -eq $bytes -or $bytes.Length -ne 20 -or $bytes[2] -ne 0x35) { return $null }
    $actionCodeRaw = [int]$bytes[4]
    return [ordered]@{
        battlePosition = [int]$bytes[3]
        actionCodeRaw = $actionCodeRaw
        actionCodeLow7 = ($actionCodeRaw -band 0x7F)
        continuationFlag = (($actionCodeRaw -band 0x80) -ne 0)
        side = [int]$bytes[5]
        reservedState = [int]$bytes[6]
        targetMasks = @(0..2 | ForEach-Object {
            $offset = 7 + ($_ * 2)
            [int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8)
        })
        targetPositionCandidates = @(Get-BattleTargetPositions -Frame $bytes)
        battleContextValue = ([int]$bytes[13] -bor ([int]$bytes[14] -shl 8))
        actionParameter = [uint32]([int]$bytes[15] -bor ([int]$bytes[16] -shl 8) -bor
            ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 24))
        authority = "STATIC_LAYOUT_PLUS_OBSERVED_RUNTIME_PACKET"
    }
}

function Get-BattleEffectDelta {
    param([string]$Hex, [int[]]$TargetPositionCandidates)
    $bytes = Convert-HexToBytes -Hex $Hex
    if ($null -eq $bytes -or $bytes.Length -ne 15 -or $bytes[0] -ne 0x83) { return $null }
    $rawDelta = [int]$bytes[9] -bor ([int]$bytes[10] -shl 8)
    $signedDelta = if ($rawDelta -ge 0x8000) { $rawDelta - 0x10000 } else { $rawDelta }
    $outcomeFlags = [uint32]([int]$bytes[11] -bor ([int]$bytes[12] -shl 8) -bor
        ([int]$bytes[13] -shl 16) -bor ([int]$bytes[14] -shl 24))
    return [ordered]@{
        attackerPositionCandidate = [int]$bytes[1]
        sourceBattlePosition = [int]$bytes[2]
        targetPositionCandidates = @($TargetPositionCandidates)
        effectKindCandidate = [int]$bytes[3]
        observedSignedDelta = $signedDelta
        deltaDirection = if ($signedDelta -lt 0) { "Negative" } elseif ($signedDelta -gt 0) { "Positive" } else { "Zero" }
        outcomeFlags = $outcomeFlags
        outcomeLowBits = ($outcomeFlags -band 0x0F)
        terminalOutcomeObserved = (($outcomeFlags -band 0x0F) -eq 0x0B)
        hpMutationAuthority = "OBSERVED_RELATION_NOT_TYPED_HP_MUTATION"
        authority = "STATIC_LAYOUT_PLUS_OBSERVED_RUNTIME_HANDLER_RECORD"
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$registry = Get-Content -LiteralPath (Join-Path $repositoryRoot "protocol\evidence\current-build\client-dispatch-registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$campaignPath = Join-Path $TraceRoot "analysis\controlled-gameplay-campaign.json"
$campaign = if (Test-Path -LiteralPath $campaignPath -PathType Leaf) {
    Get-Content -LiteralPath $campaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$baselineSequence = if ($null -ne $campaign) { [int64]$campaign.baseline.maximumMetadataSequence } else { 0 }
$baselineTimestampUnixMs = if ($null -ne $campaign) { [int64]$campaign.baseline.lastObservedUnixMs } else { 0 }
$baselineC2SOpcodes = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$baselineS2COpcodes = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($null -ne $campaign) {
    foreach ($opcode in @($campaign.baseline.observedC2SOpcodes)) { $null = $baselineC2SOpcodes.Add([string]$opcode) }
    foreach ($opcode in @($campaign.baseline.observedS2COpcodes)) { $null = $baselineS2COpcodes.Add([string]$opcode) }
}
$battleHandlers = @{}
foreach ($entry in $registry.Entries | Where-Object { $_.State -eq "Battle" }) {
    $battleHandlers[[string]$entry.Opcode] = [ordered]@{
        handlerRva = [string]$entry.HandlerRva
        usesDefaultHandler = [bool]$entry.UsesDefaultHandler
    }
}

$actions = [Collections.Generic.List[object]]::new()
$unboundHandlerRecords = [Collections.Generic.List[object]]::new()
$responsesByHookId = @{}
$newC2SOpcodes = @{}
$newS2COpcodes = @{}
$handlerRuntimeCount = 0
$current = $null
$metadataRead = Read-JsonLinesShared -Path (Join-Path $TraceRoot "metadata.jsonl") -OnObject {
    param($item)
    if ($item.Plaintext -ne $true) { return }
    $sequence = [int64]$item.sequence
    if ($sequence -le $script:baselineSequence) { return }
    $timestamp = [int64]$item.ObservedAtUnixMs
    $direction = [string]$item.PacketDirection
    $stage = [string]$item.CaptureStage
    $opcode = [string]$item.Opcode

    if ($stage -eq "PreEncrypt" -and $direction -eq "ClientToServer" -and
        -not $script:baselineC2SOpcodes.Contains($opcode)) {
        if ($script:newC2SOpcodes.ContainsKey($opcode)) { $script:newC2SOpcodes[$opcode]++ } else { $script:newC2SOpcodes[$opcode] = 1 }
    }
    if ($stage -eq "PostDecrypt" -and $direction -eq "ServerToClient" -and
        -not $script:baselineS2COpcodes.Contains($opcode)) {
        if ($script:newS2COpcodes.ContainsKey($opcode)) { $script:newS2COpcodes[$opcode]++ } else { $script:newS2COpcodes[$opcode] = 1 }
    }

    if ([string]$item.PacketDirection -eq "ClientToServer" -and
        [string]$item.CaptureStage -eq "PreEncrypt" -and
        [string]$item.Opcode -eq "0x35") {
        $commandFields = Get-BattleCommandFields -Hex ([string]$item.PlaintextHex)
        $script:current = [ordered]@{
            battleActionId = "BA-{0}" -f $sequence
            authority = "OBSERVED_CORRELATED"
            sequence = $sequence
            timestampUnixMs = $timestamp
            c2sLogicalPacketId = [string]$item.SourceFrameId
            c2sHookInvocationId = [string]$item.HookInvocationId
            c2sOpcode = "0x35"
            frameLength = [int]$item.PlaintextLength
            plaintextSha256 = Get-Sha256Text ([string]$item.PlaintextHex)
            serializerRva = "0x0007FC10"
            staticCommandBuilderRva = "0x0014E7D0"
            exactLogicalPacketContext = $false
            responses = [Collections.Generic.List[object]]::new()
            runtimeHandlerRecords = [Collections.Generic.List[object]]::new()
            runtimeHandlerObserved = $false
            hpMutationObserved = $false
            mpMutationObserved = $false
            formulaObserved = $false
            commandFields = $commandFields
            attackerCandidate = if ($null -ne $commandFields) {
                [ordered]@{ battlePosition = $commandFields.battlePosition; authority = "BATTLE_POSITION_CANDIDATE" }
            } else { $null }
            defenderCandidate = if ($null -ne $commandFields) {
                [ordered]@{ targetPositionCandidates = @($commandFields.targetPositionCandidates); authority = "TARGET_MASK_CANDIDATE" }
            } else { $null }
            beforeHp = $null
            afterHp = $null
            damageCandidate = $null
            effectDeltas = [Collections.Generic.List[object]]::new()
        }
        $script:actions.Add($script:current)
        return
    }

    if ($stage -eq "HandlerDecoded" -and $direction -eq "ServerToClient") {
        $script:handlerRuntimeCount++
        $handlerRecord = [ordered]@{
            sequence = $sequence
            timestampUnixMs = $timestamp
            sourceFrameId = [string]$item.SourceFrameId
            hookInvocationId = [string]$item.HookInvocationId
            contextInvocationId = [string]$item.ContextInvocationId
            contextCorrelationBasis = [string]$item.ContextCorrelationBasis
            opcode = $opcode
            recordLength = [int]$item.PlaintextLength
            plaintextSha256 = Get-Sha256Text ([string]$item.PlaintextHex)
            runtimeBridgeCaller = [string]$item.caller
            authority = "OBSERVED_RUNTIME_HANDLER_RECORD"
        }
        $targetPositions = if ($null -ne $script:current -and $null -ne $script:current.commandFields) {
            @($script:current.commandFields.targetPositionCandidates)
        } else { @() }
        $effectDelta = Get-BattleEffectDelta -Hex ([string]$item.PlaintextHex) -TargetPositionCandidates $targetPositions
        if ($null -ne $effectDelta) {
            $handlerRecord["effectDelta"] = $effectDelta
            if ($null -ne $script:current) { $script:current.effectDeltas.Add($effectDelta) }
        }
        $contextId = [string]$item.ContextInvocationId
        if (-not [string]::IsNullOrWhiteSpace($contextId) -and $script:responsesByHookId.ContainsKey($contextId)) {
            $response = $script:responsesByHookId[$contextId]
            $response.runtimeHandlerRecords.Add($handlerRecord)
            $response.runtimeHandlerObserved = $true
        }
        elseif ($null -ne $script:current) {
            $elapsed = $timestamp - [int64]$script:current.timestampUnixMs
            if ($elapsed -ge 0 -and $elapsed -le $ResponseWindowMs) {
                $script:current.runtimeHandlerRecords.Add($handlerRecord)
            }
            else {
                $script:unboundHandlerRecords.Add($handlerRecord)
            }
        }
        else {
            $script:unboundHandlerRecords.Add($handlerRecord)
        }
        if ($null -ne $script:current) {
            $elapsed = $timestamp - [int64]$script:current.timestampUnixMs
            if ($elapsed -ge 0 -and $elapsed -le $ResponseWindowMs) {
                $script:current.runtimeHandlerObserved = $true
            }
        }
        return
    }

    if ($null -eq $script:current -or $direction -ne "ServerToClient" -or $stage -ne "PostDecrypt") { return }
    $elapsed = $timestamp - [int64]$script:current.timestampUnixMs
    if ($elapsed -lt 0 -or $elapsed -gt $ResponseWindowMs) { return }
    $handler = if ($script:battleHandlers.ContainsKey($opcode)) { $script:battleHandlers[$opcode] } else { $null }
    $response = [ordered]@{
        sequence = $sequence
        elapsedMs = $elapsed
        s2cLogicalPacketId = [string]$item.SourceFrameId
        hookInvocationId = [string]$item.HookInvocationId
        opcode = $opcode
        frameLength = [int]$item.PlaintextLength
        plaintextSha256 = Get-Sha256Text ([string]$item.PlaintextHex)
        exactInboundTransportContext = -not [string]::IsNullOrWhiteSpace([string]$item.ContextInvocationId)
        staticBattleHandlerRva = if ($null -ne $handler) { $handler.handlerRva } else { $null }
        staticBattleHandlerUsesDefault = if ($null -ne $handler) { $handler.usesDefaultHandler } else { $null }
        handlerAuthority = if ($null -ne $handler) { "STATIC_HANDLER_CANDIDATE" } else { "EVIDENCE_BLOCKED" }
        runtimeHandlerObserved = $false
        runtimeHandlerRecords = [Collections.Generic.List[object]]::new()
    }
    $script:current.responses.Add($response)
    $hookId = [string]$item.HookInvocationId
    if (-not [string]::IsNullOrWhiteSpace($hookId)) { $script:responsesByHookId[$hookId] = $response }
}

$battleSemanticCounts = @{}
$semanticMalformed = 0
$semanticPath = Join-Path $TraceRoot "sensitive\semantic-events.jsonl"
if (Test-Path -LiteralPath $semanticPath) {
    $stream = [IO.File]::Open($semanticPath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 65536, $false)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ($line -notmatch '"EventType":"(HandlerInvocation|StateMutation|FormulaOperand|FormulaResult|SnapshotObject)"') { continue }
                $eventType = $matches[1]
                if ($line -notmatch '(?i)battle|combat|hp|mp|damage|skill|formula') { continue }
                if ($battleSemanticCounts.ContainsKey($eventType)) { $battleSemanticCounts[$eventType]++ } else { $battleSemanticCounts[$eventType] = 1 }
            }
        }
        finally { $reader.Dispose() }
    }
    catch { $semanticMalformed++ }
    finally { $stream.Dispose() }
}

$primaryActionLimit = 3
for ($actionIndex = 0; $actionIndex -lt $actions.Count; $actionIndex++) {
    $action = $actions[$actionIndex]
    $action["campaignRole"] = if ($actionIndex -lt $primaryActionLimit) {
        "PRIMARY_STAGE_1_ACTION"
    } else { "EXCESS_POST_GATE_OBSERVATION" }
    $negativeDeltas = @($action.effectDeltas | Where-Object { [int]$_.observedSignedDelta -lt 0 })
    $action["damageCandidate"] = if ($negativeDeltas.Count -gt 0) {
        [ordered]@{
            observedNegativeDeltas = @($negativeDeltas | ForEach-Object { [int]$_.observedSignedDelta })
            authority = "OBSERVED_RELATION_NOT_TYPED_HP_MUTATION"
        }
    } else { $null }
}

$battleObserved = $actions.Count -gt 0
$primaryActions = @($actions | Select-Object -First $primaryActionLimit)
$primaryActionCount = $primaryActions.Count
$excessActionCount = [Math]::Max(0, $actions.Count - $primaryActionCount)
$closedLoopActionCount = @($actions | Where-Object { $_.runtimeHandlerObserved -eq $true }).Count
$primaryClosedLoopActionCount = @($primaryActions | Where-Object { $_.runtimeHandlerObserved -eq $true }).Count
$effectDeltaCount = @($actions.effectDeltas).Count
$negativeEffectDeltaCount = @($actions.effectDeltas | Where-Object { [int]$_.observedSignedDelta -lt 0 }).Count
$battleResponseOpcodes = @{}
foreach ($action in $actions) {
    foreach ($response in $action.responses) {
        $key = [string]$response.opcode
        if (-not $battleResponseOpcodes.ContainsKey($key)) {
            $battleResponseOpcodes[$key] = [ordered]@{
                opcode = $key
                count = 0
                runtimeHandlerObserved = $false
                lengths = @{}
            }
        }
        $row = $battleResponseOpcodes[$key]
        $row.count++
        if ($response.runtimeHandlerObserved -eq $true) { $row.runtimeHandlerObserved = $true }
        $lengthKey = [string]$response.frameLength
        if ($row.lengths.ContainsKey($lengthKey)) { $row.lengths[$lengthKey]++ } else { $row.lengths[$lengthKey] = 1 }
    }
}
$unboundHandlerOpcodes = @{}
foreach ($record in $unboundHandlerRecords) {
    $key = [string]$record.opcode
    if ($unboundHandlerOpcodes.ContainsKey($key)) { $unboundHandlerOpcodes[$key]++ } else { $unboundHandlerOpcodes[$key] = 1 }
}
$hpMutationCount = if ($battleSemanticCounts.ContainsKey("StateMutation")) { [int]$battleSemanticCounts["StateMutation"] } else { 0 }
$mpMutationCount = 0
$formulaCandidateCount = 0
$status = if (-not $battleObserved) {
    if ($null -ne $campaign) { "WAITING_FOR_PLAYER_ACTION" } else { "READY_UNOBSERVED" }
}
elseif ($primaryClosedLoopActionCount -gt 0) {
    "BATTLE_RUNTIME_CLOSED_LOOP_OBSERVED"
}
else {
    "BATTLE_ACTION_OBSERVED_HANDLER_BINDING_BLOCKED"
}
$result = [ordered]@{
    schema = "God2LiveBattleReadiness/2"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = $status
    campaignId = if ($null -ne $campaign) { [string]$campaign.campaignId } else { $null }
    campaignBaselineSequence = $baselineSequence
    campaignBaselineTimestampUnixMs = $baselineTimestampUnixMs
    battleRuntimeObserved = $battleObserved
    actionCount = $actions.Count
    primaryActionCount = $primaryActionCount
    excessActionCount = $excessActionCount
    closedLoopActionCount = $closedLoopActionCount
    primaryClosedLoopActionCount = $primaryClosedLoopActionCount
    handlerRuntimeCount = $handlerRuntimeCount
    effectDeltaCount = $effectDeltaCount
    negativeEffectDeltaCount = $negativeEffectDeltaCount
    hpMutationCount = $hpMutationCount
    mpMutationCount = $mpMutationCount
    formulaCandidateCount = $formulaCandidateCount
    responseWindowMs = $ResponseWindowMs
    actionDetailScope = "PRIMARY_STAGE_1_ONLY"
    actions = @($primaryActions)
    excessActionSummaries = @($actions | Select-Object -Skip $primaryActionLimit | ForEach-Object {
        [ordered]@{
            battleActionId = [string]$_.battleActionId
            sequence = [int64]$_.sequence
            timestampUnixMs = [int64]$_.timestampUnixMs
            runtimeHandlerObserved = $_.runtimeHandlerObserved -eq $true
            responseCount = @($_.responses).Count
            effectDeltaCount = @($_.effectDeltas).Count
        }
    })
    observedBattleResponseOpcodes = @($battleResponseOpcodes.GetEnumerator() | Sort-Object Name | ForEach-Object {
        $row = $_.Value
        [ordered]@{
            opcode = [string]$row.opcode
            count = [int]$row.count
            runtimeHandlerObserved = $row.runtimeHandlerObserved -eq $true
            lengths = @($row.lengths.GetEnumerator() | Sort-Object { [int]$_.Name } | ForEach-Object {
                [ordered]@{ bytes = [int]$_.Name; count = [int]$_.Value }
            })
        }
    })
    unboundHandlerRecordCount = $unboundHandlerRecords.Count
    unboundHandlerOpcodes = @($unboundHandlerOpcodes.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{ opcode = [string]$_.Name; count = [int]$_.Value }
    })
    newC2SOpcodes = @($newC2SOpcodes.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{ opcode = [string]$_.Name; count = [int]$_.Value; authority = "OBSERVED_RUNTIME_PACKET_UNCLASSIFIED" }
    })
    newS2COpcodes = @($newS2COpcodes.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{ opcode = [string]$_.Name; count = [int]$_.Value; authority = "OBSERVED_RUNTIME_PACKET_UNCLASSIFIED" }
    })
    semanticEventCounts = @($battleSemanticCounts.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{ eventType = [string]$_.Name; count = [int]$_.Value }
    })
    semanticMalformed = $semanticMalformed
    readiness = [ordered]@{
        c2sSerializer = "ACTIVE"
        c2sOpcode35StaticBuilder = "DISCOVERED_PROTOCOL"
        c2sExactContext = "NEXT_DLL_REVISION"
        s2cTransportPostDecrypt = "ACTIVE_EXACT"
        parser = "ACTIVE"
        battleHandler = if ($handlerRuntimeCount -gt 0) { "OBSERVED_RUNTIME_HANDLER_RECORD" } else { "READY_UNOBSERVED" }
        hpMutation = if ($hpMutationCount -gt 0) { "OBSERVED_RELATION" } else { "READY_UNOBSERVED" }
        mpMutation = "READY_UNOBSERVED"
        formula = "UNOBSERVED"
    }
    successGate = [ordered]@{
        handlerRuntimePositive = ($handlerRuntimeCount -gt 0)
        battleObservedPositive = $battleObserved
        battleActionIdPositive = ($actions.Count -gt 0)
        runtimeClosedLoopPositive = ($primaryClosedLoopActionCount -gt 0)
        hpMutationPositive = ($hpMutationCount -gt 0)
        stage1CorrelationComplete = ($primaryClosedLoopActionCount -gt 0)
        nextStagePermitted = ($primaryClosedLoopActionCount -gt 0)
    }
    productionPromotion = $false
    networkBytesEmitted = $false
    clientMemoryWritten = $false
    reader = $metadataRead
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force

$primaryEvidence = [ordered]@{
    schema = "God2BattlePrimaryEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = if ($null -ne $campaign) { [string]$campaign.campaignId } else { $null }
    baselineSequence = $baselineSequence
    authority = "OBSERVED_RUNTIME_CLOSED_LOOP_PARTIAL"
    actions = @($primaryActions | ForEach-Object {
        $action = $_
        [ordered]@{
            battleActionId = [string]$action.battleActionId
            sequence = [int64]$action.sequence
            timestampUnixMs = [int64]$action.timestampUnixMs
            c2sLogicalPacketId = [string]$action.c2sLogicalPacketId
            c2sOpcode = [string]$action.c2sOpcode
            frameLength = [int]$action.frameLength
            plaintextSha256 = [string]$action.plaintextSha256
            commandFields = $action.commandFields
            responses = @($action.responses | ForEach-Object {
                [ordered]@{
                    sequence = [int64]$_.sequence
                    elapsedMs = [int64]$_.elapsedMs
                    s2cLogicalPacketId = [string]$_.s2cLogicalPacketId
                    opcode = [string]$_.opcode
                    frameLength = [int]$_.frameLength
                    plaintextSha256 = [string]$_.plaintextSha256
                    staticBattleHandlerRva = [string]$_.staticBattleHandlerRva
                    runtimeHandlerObserved = $_.runtimeHandlerObserved -eq $true
                    runtimeHandlerRecordCount = @($_.runtimeHandlerRecords).Count
                }
            })
            effectDeltas = @($action.effectDeltas)
            beforeHp = $null
            afterHp = $null
            hpMutationAuthority = "UNOBSERVED"
            damageAuthority = if ($null -ne $action.damageCandidate) {
                "OBSERVED_RELATION_NOT_TYPED_HP_MUTATION"
            } else { "UNOBSERVED" }
        }
    })
    gate = $result.successGate
    blocker = "HandlerDecoded -> TypedHpMutation"
    formulaAuthority = "UNOBSERVED"
    productionPromotion = $false
    networkBytesEmitted = $false
    clientMemoryWritten = $false
}
$primaryEvidencePath = Join-Path (Split-Path -Parent $OutputPath) "battle-primary-evidence.json"
$primaryTemporary = "$primaryEvidencePath.tmp-$PID"
[IO.File]::WriteAllText($primaryTemporary, ($primaryEvidence | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $primaryTemporary -Destination $primaryEvidencePath -Force

if ($null -ne $campaign) {
    $campaign.status = if ($primaryClosedLoopActionCount -gt 0) {
        "STAGE_1_CORRELATION_COMPLETE"
    }
    elseif ($battleObserved) {
        "HANDLER_BINDING_BLOCKED"
    }
    else {
        "WAITING_FOR_PLAYER_ACTION"
    }
    $campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
    $campaign | Add-Member -NotePropertyName observed -NotePropertyValue ([ordered]@{
        battleActionCount = $actions.Count
        primaryBattleActionCount = $primaryActionCount
        excessBattleActionCount = $excessActionCount
        handlerRuntimeCount = $handlerRuntimeCount
        closedLoopActionCount = $closedLoopActionCount
        primaryClosedLoopActionCount = $primaryClosedLoopActionCount
        hpMutationCount = $hpMutationCount
        mpMutationCount = $mpMutationCount
        formulaCandidateCount = $formulaCandidateCount
        effectDeltaCount = $effectDeltaCount
        negativeEffectDeltaCount = $negativeEffectDeltaCount
        newC2SOpcodeCount = $newC2SOpcodes.Count
        newS2COpcodeCount = $newS2COpcodes.Count
    }) -Force
    $campaignTemporary = "$campaignPath.tmp-$PID"
    [IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $campaignTemporary -Destination $campaignPath -Force
}
$result | ConvertTo-Json -Depth 14
