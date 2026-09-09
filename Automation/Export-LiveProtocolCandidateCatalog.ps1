[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
    [string]$DispatchRegistryPath,
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
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DispatchRegistryPath)) {
    $DispatchRegistryPath = Join-Path $repositoryRoot "protocol\evidence\current-build\client-dispatch-registry.json"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TraceRoot "analysis\protocol-candidate-catalog.json"
}

$graphPath = Join-Path $TraceRoot "analysis\semantic-evidence-graph.json"
$graph = Get-Content -LiteralPath $graphPath -Raw -Encoding UTF8 | ConvertFrom-Json
$registry = Get-Content -LiteralPath $DispatchRegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$battleReadinessPath = Join-Path $TraceRoot "analysis\battle-readiness.json"
$battleReadiness = if (Test-Path -LiteralPath $battleReadinessPath -PathType Leaf) {
    Get-Content -LiteralPath $battleReadinessPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$battleOpcodeSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$runtimeHandlerOpcodeSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($null -ne $battleReadiness) {
    foreach ($responseFamily in @($battleReadiness.observedBattleResponseOpcodes)) {
        $null = $battleOpcodeSet.Add([string]$responseFamily.opcode)
        if ($responseFamily.runtimeHandlerObserved -eq $true) {
            $null = $runtimeHandlerOpcodeSet.Add([string]$responseFamily.opcode)
        }
    }
}

$staticHints = @{
    "0x5C" = [ordered]@{
        semanticCandidate = "TypedTextOrActionMessageConsumer"
        fieldCandidates = @(
            [ordered]@{ frameOffset = 11; type = "UInt8Low7Bits"; meaning = "message/action subtype candidate" },
            [ordered]@{ frameOffset = 15; type = "NullTerminatedOrStructuredText"; meaning = "text/entity label candidate" }
        )
        objectConsumerRvas = @("0x00520E20", "0x004987F0")
        mutationCandidateRvas = @()
    }
    "0x5E" = [ordered]@{
        semanticCandidate = "EntityManagerStateUpdate"
        fieldCandidates = @(
            [ordered]@{ frameOffset = 3; type = "UInt16LE"; meaning = "entity identity candidate" },
            [ordered]@{ frameOffset = 12; type = "PackedUInt32"; meaning = "two 11-bit coordinate candidates" }
        )
        objectConsumerRvas = @("0x0051AB50", "0x00531030", "0x0052EC40")
        mutationCandidateRvas = @("0x00531030")
    }
    "0x6F" = [ordered]@{
        semanticCandidate = "EntityMovementOrAppearanceUpdate"
        fieldCandidates = @(
            [ordered]@{ frameOffset = 3; type = "UInt16LE"; meaning = "entity identity candidate" },
            [ordered]@{ frameOffset = 15; type = "PackedUInt32"; meaning = "position/state bitfield candidate" }
        )
        objectConsumerRvas = @("0x0051AB50", "0x0053E0A0")
        mutationCandidateRvas = @()
    }
    "0x72" = [ordered]@{
        semanticCandidate = "IndexedEntityRecordAndVisualStateUpdate"
        fieldCandidates = @(
            [ordered]@{ frameOffset = 3; type = "UInt16LE"; meaning = "entity record index / fallback identity candidate" },
            [ordered]@{ frameOffset = 5; type = "UInt16LE"; meaning = "preferred entity record index candidate" },
            [ordered]@{ frameOffset = 7; type = "UInt8"; meaning = "visual/state selector candidate" },
            [ordered]@{ frameOffset = 8; type = "UInt8Low5Bits"; meaning = "visual/state selector candidate" }
        )
        objectConsumerRvas = @("0x0048D2C0")
        mutationCandidateRvas = @("0x004F8830", "0x004F9C30", "0x004F7FD0")
        objectLayout = [ordered]@{ managerCountOffset = "0x124E0"; recordsOffset = "0x124E2"; recordStride = "0x5E" }
    }
    "0x73" = [ordered]@{
        semanticCandidate = "EntityRemovalOrDeactivation"
        fieldCandidates = @(
            [ordered]@{ frameOffset = 3; type = "UInt16LE"; meaning = "entity identity candidate" }
        )
        objectConsumerRvas = @("0x0052E570", "0x004F8FB0", "0x004EFCB0")
        mutationCandidateRvas = @("0x0052E7F0")
        objectLayout = [ordered]@{ maximumRecords = 128; recordStride = "0x268"; identityOffset = "0x18" }
    }
}

$c2sConstructorHints = @{
    "0x1F" = [ordered]@{
        semanticCandidate = "ZeroPayloadControlCommand"
        constructorRvas = @("0x0007FDB0")
        callerRvas = @()
        applicationPayloadBytes = 1
        observedPayloadShape = "single zero byte"
    }
    "0x20" = [ordered]@{
        semanticCandidate = "Fixed16ByteIdentifierOrControlCommand"
        constructorRvas = @("0x000AFAA0")
        callerRvas = @("0x000DCF17", "0x001169C7")
        applicationPayloadBytes = 16
        observedPayloadShape = "fixed 16 bytes; semantic identity unverified"
    }
    "0x30" = [ordered]@{
        semanticCandidate = "HeartbeatOrKeepalive"
        constructorRvas = @("0x00079C40", "0x00079FF0")
        callerRvas = @()
        applicationPayloadBytes = 1
        observedPayloadShape = "single zero byte"
    }
    "0x35" = [ordered]@{
        semanticCandidate = "BattleCommandCandidate"
        constructorRvas = @("0x0014E7D0")
        callerRvas = @("0x0014EA8C", "0x0014EAE4")
        applicationPayloadBytes = 16
        observedPayloadShape = "battle position, action code, side, three target masks, battle context and action parameter"
    }
}

$s2c = @()
foreach ($family in @($graph.packetFamilies | Where-Object {
    $_.direction -eq "ServerToClient" -and $_.stage -eq "PostDecrypt"
})) {
    $dispatchState = if ($battleOpcodeSet.Contains([string]$family.opcode)) { "Battle" } else { "World" }
    $entry = $registry.Entries | Where-Object { $_.State -eq $dispatchState -and $_.Opcode -eq $family.opcode } | Select-Object -First 1
    $opcodeValue = [Convert]::ToInt32(([string]$family.opcode).Substring(2), 16)
    $neighborSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($value in ($opcodeValue - 2)..($opcodeValue + 2)) {
        if ($value -ge 0 -and $value -le 0xFD -and $value -ne $opcodeValue) {
            $null = $neighborSet.Add(("0x{0:X2}" -f $value))
        }
    }
    $neighbors = @($registry.Entries |
        Where-Object { $_.State -eq $dispatchState -and $neighborSet.Contains([string]$_.Opcode) } |
        Sort-Object Opcode |
        ForEach-Object {
            [ordered]@{
                opcode = [string]$_.Opcode
                handlerRva = [string]$_.HandlerRva
                usesDefaultHandler = [bool]$_.UsesDefaultHandler
                priorObservationCount = [int]$_.ObservationCount
                authority = "DISCOVERED_PROTOCOL"
            }
        })
    $hint = $staticHints[[string]$family.opcode]
    $s2c += [ordered]@{
        opcode = [string]$family.opcode
        authority = "OBSERVED_RUNTIME_PACKET"
        runtimeObservations = [int]$family.observations
        runtimeLengths = @($family.lengths)
        parserRva = "0x00078D70"
        parserCallerRva = "0x00078A48"
        dispatchFamily = $dispatchState
        dispatchRva = if ($dispatchState -eq "World") { "0x0008E536" } else { $null }
        dispatchTableRva = if ($dispatchState -eq "World") { "0x000911A8" } else { $null }
        handlerRva = if ($null -ne $entry) { [string]$entry.HandlerRva } else { $null }
        handlerAuthority = if ($null -ne $entry) { "STATIC_HANDLER_CANDIDATE" } else { "EVIDENCE_BLOCKED" }
        usesDefaultHandler = if ($null -ne $entry) { [bool]$entry.UsesDefaultHandler } else { $null }
        semanticCandidate = if ($null -ne $hint) {
            $hint.semanticCandidate
        } elseif ($dispatchState -eq "Battle") { "BattleResponseVariantCandidate" } else { "UNKNOWN" }
        fieldCandidates = if ($null -ne $hint) { $hint.fieldCandidates } else { @() }
        objectConsumerRvas = if ($null -ne $hint) { $hint.objectConsumerRvas } else { @() }
        mutationCandidateRvas = if ($null -ne $hint) { $hint.mutationCandidateRvas } else { @() }
        objectLayout = if ($null -ne $hint -and $hint.Contains("objectLayout")) { $hint.objectLayout } else { $null }
        runtimeHandlerObserved = $runtimeHandlerOpcodeSet.Contains([string]$family.opcode)
        productionPromotion = $false
        discoveredNeighbors = $neighbors
    }
}

$c2s = @()
foreach ($family in @($graph.packetFamilies | Where-Object {
    $_.direction -eq "ClientToServer" -and $_.stage -eq "PreEncrypt"
})) {
    $policy = $registry.OutboundLengthPolicies | Where-Object { $_.Opcode -eq $family.opcode } | Select-Object -First 1
    $constructor = $c2sConstructorHints[[string]$family.opcode]
    $c2s += [ordered]@{
        opcode = [string]$family.opcode
        authority = "OBSERVED_CORRELATED"
        runtimeObservations = [int]$family.observations
        runtimeLengths = @($family.lengths)
        serializerRva = "0x0007FC10"
        preEncryptObserved = $true
        exactLogicalPacketContext = $false
        constructorAuthority = if ($null -ne $constructor) { "STATIC_SERIALIZER_CANDIDATE" } else { "EVIDENCE_BLOCKED" }
        constructorRvas = if ($null -ne $constructor) { $constructor.constructorRvas } else { @() }
        constructorCallerRvas = if ($null -ne $constructor) { $constructor.callerRvas } else { @() }
        semanticCandidate = if ($null -ne $constructor) { $constructor.semanticCandidate } else { "UNKNOWN" }
        applicationPayloadBytes = if ($null -ne $constructor) { $constructor.applicationPayloadBytes } else { $null }
        observedPayloadShape = if ($null -ne $constructor) { $constructor.observedPayloadShape } else { "UNKNOWN" }
        outboundLengthPolicy = if ($null -ne $policy) {
            [ordered]@{ kind = [string]$policy.Kind; applicationBytes = [int]$policy.FixedFrameLength; source = [string]$policy.Evidence }
        } else { $null }
        productionPromotion = $false
    }
}

$catalog = [ordered]@{
    schema = "God2LiveProtocolCandidateCatalog/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    clientBuildId = [string]$registry.ClientBuildId
    sessionProcessId = $graph.session.processId
    authorityPolicy = [ordered]@{
        observedS2C = "OBSERVED_RUNTIME_PACKET"
        staticHandler = "STATIC_HANDLER_CANDIDATE"
        observedC2S = "OBSERVED_CORRELATED"
        runtimeHandlerPromotion = "Requires natural HandlerInvocation evidence"
        productionPromotion = $false
    }
    s2c = $s2c
    c2s = $c2s
    rejectedCandidates = @(
        [ordered]@{ rva = "0x0034E480"; previousRole = "ObjectResolver"; resolvedRole = "memset"; status = "REJECTED_STATIC_FALSE_POSITIVE" },
        [ordered]@{ rva = "0x0034D980"; previousRole = "ObjectResolver"; resolvedRole = "memmove"; status = "REJECTED_STATIC_FALSE_POSITIVE" }
    )
    battle = [ordered]@{
        runtimeObserved = ($null -ne $battleReadiness -and $battleReadiness.battleRuntimeObserved -eq $true)
        actionCount = if ($null -ne $battleReadiness) { [int]$battleReadiness.actionCount } else { 0 }
        closedLoopActionCount = if ($null -ne $battleReadiness) { [int]$battleReadiness.closedLoopActionCount } else { 0 }
        handlerRuntimeCount = if ($null -ne $battleReadiness) { [int]$battleReadiness.handlerRuntimeCount } else { 0 }
        handlerLengthPolicyRva = "0x0007F940"
        opcodeLengthTableRva = "0x00166360"
        formulaAuthority = "UNOBSERVED"
    }
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($catalog | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$catalog | ConvertTo-Json -Depth 14
