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
if ([string]::IsNullOrWhiteSpace($CampaignPath)) { $CampaignPath = Join-Path $analysisRoot "stage2-campaign.json" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $analysisRoot "stage2-skill-evidence.json" }

$campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
$registry = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "protocol\evidence\current-build\client-dispatch-registry.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$baselineSequence = [int64]$campaign.baseline.maximumMetadataSequence
$upperSequence = Get-LiveStageUpperSequence -AnalysisRoot $analysisRoot -Stage 2
$windowMs = [int64]$campaign.correlationWindowMs

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

function Get-CommandFields {
    param($Item)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 20 -or $bytes[2] -ne 0x35) { return $null }
    $targets = [Collections.Generic.List[int]]::new()
    $masks = @(0..2 | ForEach-Object {
        $offset = 7 + ($_ * 2)
        [int]$bytes[$offset] -bor ([int]$bytes[$offset + 1] -shl 8)
    })
    for ($group = 0; $group -lt 3; $group++) {
        for ($bit = 0; $bit -lt 14; $bit++) {
            if (($masks[$group] -band (1 -shl $bit)) -ne 0) { $targets.Add(($group * 14) + $bit) }
        }
    }
    return [ordered]@{
        sequence = [int64]$Item.sequence
        timestampUnixMs = [int64]$Item.ObservedAtUnixMs
        logicalPacketId = [string]$Item.SourceFrameId
        hookInvocationId = [string]$Item.HookInvocationId
        plaintextSha256 = Get-Sha256Text ([string]$Item.PlaintextHex)
        battlePosition = [int]$bytes[3]
        actionCodeRaw = [int]$bytes[4]
        actionCodeLow7 = ([int]$bytes[4] -band 0x7F)
        continuationFlag = (([int]$bytes[4] -band 0x80) -ne 0)
        side = [int]$bytes[5]
        reservedState = [int]$bytes[6]
        targetMasks = $masks
        targetPositionCandidates = @($targets)
        battleContextValue = ([int]$bytes[13] -bor ([int]$bytes[14] -shl 8))
        actionParameterCandidate = [uint32]([int]$bytes[15] -bor ([int]$bytes[16] -shl 8) -bor
            ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 24))
        layoutAuthority = "STATIC_LAYOUT_PLUS_OBSERVED_RUNTIME_PACKET"
    }
}

function Get-EffectDelta {
    param($Item, [int[]]$TargetCandidates)
    $bytes = Convert-HexToBytes -Hex ([string]$Item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -ne 15 -or $bytes[0] -ne 0x83) { return $null }
    $raw = [int]$bytes[9] -bor ([int]$bytes[10] -shl 8)
    $delta = if ($raw -ge 0x8000) { $raw - 0x10000 } else { $raw }
    $flags = [uint32]([int]$bytes[11] -bor ([int]$bytes[12] -shl 8) -bor
        ([int]$bytes[13] -shl 16) -bor ([int]$bytes[14] -shl 24))
    return [ordered]@{
        sequence = [int64]$Item.sequence
        sourceBattlePosition = [int]$bytes[2]
        targetPositionCandidates = @($TargetCandidates)
        effectKindCandidate = [int]$bytes[3]
        observedSignedDelta = $delta
        outcomeFlags = $flags
        terminalOutcomeObserved = (($flags -band 0x0F) -eq 0x0B)
        authority = "OBSERVED_RELATION_NOT_TYPED_HP_MUTATION"
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
            if ($item.Plaintext -eq $true -and [int64]$item.sequence -gt $baselineSequence -and
                [int64]$item.sequence -le $upperSequence -and
                [string]$item.CaptureStage -in @("PreEncrypt", "PostDecrypt", "HandlerDecoded")) {
                $events.Add($item)
            }
        }
    }
    finally { $reader.Dispose() }
}
finally { $stream.Dispose() }

$commands = @($events | Where-Object {
    [string]$_.PacketDirection -eq "ClientToServer" -and [string]$_.CaptureStage -eq "PreEncrypt" -and
    [string]$_.Opcode -eq "0x35" -and [int]$_.PlaintextLength -eq 20
} | ForEach-Object { Get-CommandFields -Item $_ } | Where-Object { $null -ne $_ } | Sort-Object @{ Expression = { [int64]$_.timestampUnixMs } }, @{ Expression = { [int64]$_.sequence } })
$playerCommands = @($commands | Where-Object {
    [int]$_.battlePosition -eq [int]$campaign.playerPositionCandidate -and
    [int]$_.side -eq [int]$campaign.playerSideCandidate -and [int]$_.actionCodeLow7 -ne 0x0B
} | Select-Object -First 2)

$labelledActions = [Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $playerCommands.Count; $index++) {
    $command = $playerCommands[$index]
    $label = if ($index -eq 0) { "BasicAttack" } else { "BasicSkill" }
    $endTimestamp = if ($index + 1 -lt $playerCommands.Count) {
        [int64]$playerCommands[$index + 1].timestampUnixMs
    } else { [int64]$command.timestampUnixMs + $windowMs }
    $responses = @($events | Where-Object {
        [string]$_.PacketDirection -eq "ServerToClient" -and [string]$_.CaptureStage -eq "PostDecrypt" -and
        [int64]$_.ObservedAtUnixMs -ge [int64]$command.timestampUnixMs -and
        [int64]$_.ObservedAtUnixMs -lt $endTimestamp
    } | Sort-Object ObservedAtUnixMs, sequence)
    $responseHookIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($response in $responses) {
        if (-not [string]::IsNullOrWhiteSpace([string]$response.HookInvocationId)) {
            $null = $responseHookIds.Add([string]$response.HookInvocationId)
        }
    }
    $handlerRecords = @($events | Where-Object {
        [string]$_.CaptureStage -eq "HandlerDecoded" -and
        $responseHookIds.Contains([string]$_.ContextInvocationId)
    } | Sort-Object ObservedAtUnixMs, sequence)
    $effectDeltas = @($handlerRecords | ForEach-Object {
        Get-EffectDelta -Item $_ -TargetCandidates @($command.targetPositionCandidates)
    } | Where-Object { $null -ne $_ })
    $responseRows = @($responses | ForEach-Object {
        $response = $_
        $handler = $registry.Entries | Where-Object {
            $_.State -eq "Battle" -and $_.Opcode -eq [string]$response.Opcode
        } | Select-Object -First 1
        [ordered]@{
            sequence = [int64]$response.sequence
            elapsedMs = [int64]$response.ObservedAtUnixMs - [int64]$command.timestampUnixMs
            logicalPacketId = [string]$response.SourceFrameId
            opcode = [string]$response.Opcode
            frameLength = [int]$response.PlaintextLength
            plaintextSha256 = Get-Sha256Text ([string]$response.PlaintextHex)
            exactInboundTransportContext = -not [string]::IsNullOrWhiteSpace([string]$response.ContextInvocationId)
            staticBattleHandlerRva = if ($null -ne $handler) { [string]$handler.HandlerRva } else { $null }
            runtimeHandlerRecordCount = @($handlerRecords | Where-Object {
                [string]$_.ContextInvocationId -eq [string]$response.HookInvocationId
            }).Count
        }
    })
    $labelledActions.Add([ordered]@{
        actionId = ("BA-S2-{0}-{1}" -f $label.ToUpperInvariant(), [int64]$command.sequence)
        label = $label
        labelAuthority = "USER_DECLARED_CONTROLLED_ORDER_PLUS_RUNTIME_CORRELATION"
        command = $command
        responses = $responseRows
        handlerRuntimeCount = $handlerRecords.Count
        handlerRuntimeObserved = ($handlerRecords.Count -gt 0)
        effectDeltas = $effectDeltas
        beforeHp = $null
        afterHp = $null
        damage = @($effectDeltas | Where-Object { [int]$_.observedSignedDelta -lt 0 } | ForEach-Object { [int]$_.observedSignedDelta })
        mpBefore = $null
        mpAfter = $null
        mpCost = $null
        skillId = $null
        skillLevel = $null
        hit = "UNKNOWN"
        critical = "UNKNOWN"
        element = "UNKNOWN"
    })
}

$complete = $labelledActions.Count -eq 2 -and @($labelledActions | Where-Object { $_.handlerRuntimeObserved -ne $true }).Count -eq 0
$result = [ordered]@{
    schema = "God2LiveStage2SkillEvidence/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    campaignId = [string]$campaign.campaignId
    baselineSequence = $baselineSequence
    status = if ($complete) { "STAGE_2_CORRELATION_COMPLETE_MUTATION_BLOCKED" } else { "STAGE_2_EVIDENCE_BLOCKED" }
    actions = @($labelledActions)
    comparison = if ($labelledActions.Count -eq 2) {
        [ordered]@{
            opcodeSame = $true
            opcode = "0x35"
            basicActionCode = [int]$labelledActions[0].command.actionCodeLow7
            skillActionCode = [int]$labelledActions[1].command.actionCodeLow7
            basicActionParameter = [uint32]$labelledActions[0].command.actionParameterCandidate
            skillActionParameter = [uint32]$labelledActions[1].command.actionParameterCandidate
            actionCodeDiffers = ([int]$labelledActions[0].command.actionCodeLow7 -ne [int]$labelledActions[1].command.actionCodeLow7)
            actionParameterDiffers = ([uint32]$labelledActions[0].command.actionParameterCandidate -ne [uint32]$labelledActions[1].command.actionParameterCandidate)
            skillIdAuthority = "UNRESOLVED_ACTION_PARAMETER_CANDIDATE_ONLY"
        }
    } else { $null }
    gate = [ordered]@{
        basicAttackObserved = ($labelledActions.Count -ge 1)
        basicSkillObserved = ($labelledActions.Count -ge 2)
        runtimeHandlerClosedLoops = @($labelledActions | Where-Object { $_.handlerRuntimeObserved -eq $true }).Count
        typedHpMutationObserved = $false
        typedMpMutationObserved = $false
        formulaAuthority = "UNOBSERVED"
        productionPromotion = $false
    }
    firstBrokenEdge = "HandlerDecoded -> TypedHpOrMpMutation"
    safety = $campaign.safety
}

$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$campaign.status = if ($complete) { "STAGE_2_CORRELATION_COMPLETE" } else { "STAGE_2_EVIDENCE_BLOCKED" }
$campaign | Add-Member -NotePropertyName lastAnalyzedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString("O")) -Force
$campaign | Add-Member -NotePropertyName evidencePath -NotePropertyValue $OutputPath -Force
$campaignTemporary = "$CampaignPath.tmp-$PID"
[IO.File]::WriteAllText($campaignTemporary, ($campaign | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $campaignTemporary -Destination $CampaignPath -Force
$result | ConvertTo-Json -Depth 14
