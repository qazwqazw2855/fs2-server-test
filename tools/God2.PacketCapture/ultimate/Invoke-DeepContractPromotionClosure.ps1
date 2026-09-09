[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$OfficialRuntimeEvidence,
    [Parameter(Mandatory)][string]$CandidateMapV3,
    [Parameter(Mandatory)][string]$ContractAcquisitionRoot,
    [Parameter(Mandatory)][string]$SemanticEventsPath,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$ReleaseExecutable = 'artifacts/release/God2SemanticRecoveryEngine.exe',
    [switch]$CreatePortablePackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
$repoPrefix = $RepositoryRoot.TrimEnd('\') + '\'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$officialSha256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$gateNames = @(
    'ExactTargetIdentity','ExecutableSection','ExactCandidateBytes',
    'CallingConventionVerified','TypedRuntimeEvidence','RepeatedCausalObservation',
    'ContradictionsResolved','StableObjectOrContext','VerifiedConsumerOrMutation',
    'SensitiveMaskContract','ArgumentContractVerified','ReturnValueLifetimeVerified',
    'ThreadContextVerified','ReentrancyRiskVerified')

. (Join-Path $PSScriptRoot 'SemanticEventJson.ps1')

function Resolve-InputPath([string]$Path) {
    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    } else {
        [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
    }
    if (-not $candidate.StartsWith($repoPrefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $candidate)) {
        throw "Input must exist below the repository root: $Path"
    }
    return $candidate
}

function Get-PortablePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($repoPrefix,[StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository root: $full"
    }
    return $full.Substring($repoPrefix.Length).Replace('\','/')
}

function Get-Property([object]$Object,[string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Read-Json([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM is forbidden: $Path"
    }
    return $utf8.GetString($bytes) | ConvertFrom-Json
}

function Write-Json([string]$Name,[object]$Value) {
    $path = Join-Path $OutputRoot $Name
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite output: $path" }
    [IO.File]::WriteAllText($path,(($Value | ConvertTo-Json -Depth 24) + "`n"),$utf8)
    return $path
}

function Write-JsonLines([string]$Name,[object[]]$Rows) {
    $path = Join-Path $OutputRoot $Name
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite output: $path" }
    $builder = [Text.StringBuilder]::new()
    foreach ($row in $Rows) {
        [void]$builder.AppendLine(($row | ConvertTo-Json -Depth 20 -Compress))
    }
    [IO.File]::WriteAllText($path,$builder.ToString(),$utf8)
    return $path
}

function Get-DistinctStrings([object[]]$Values) {
    return @($Values | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_)
    } | ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Get-ByteSha256([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-','') }
    finally { $algorithm.Dispose() }
}

function Get-ValueSummary([object[]]$Values) {
    $Values = @($Values)
    $classes = @(Get-DistinctStrings @($Values | ForEach-Object { Get-Property $_ 'ValueClass' }))
    $tokens = @(Get-DistinctStrings @($Values | ForEach-Object { Get-Property $_ 'StableToken' }))
    return [pscustomobject][ordered]@{
        ObservationCount = $Values.Count
        ValueClasses = $classes
        TokenObservationCount = @($Values | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string](Get-Property $_ 'StableToken'))
        }).Count
        DistinctTokenCount = $tokens.Count
        StableSingleToken = $Values.Count -ge 3 -and $tokens.Count -eq 1
        Tokens = $tokens
    }
}

function Get-EvidenceReferences([object[]]$Rows,[string[]]$Additional = @()) {
    $ids = @($Rows | ForEach-Object { [string](Get-Property $_ 'ObservationId') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $bounded = @($ids | Select-Object -First 16 | ForEach-Object {
        'candidate-runtime-observations.jsonl#ObservationId=' + $_
    })
    return @($bounded + $Additional)
}

$officialPath = Resolve-InputPath $OfficialRuntimeEvidence
$mapPath = Resolve-InputPath $CandidateMapV3
$acquisitionPath = Resolve-InputPath $ContractAcquisitionRoot
$semanticPath = Resolve-InputPath $SemanticEventsPath
$releasePath = Resolve-InputPath $ReleaseExecutable
$observationPath = Join-Path $acquisitionPath 'candidate-runtime-observations.jsonl'
$campaignPath = Join-Path $acquisitionPath 'contract-acquisition-campaign-manifest.json'
foreach ($required in @($observationPath,$campaignPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required acquisition artifact is missing: $required"
    }
}
if (Test-Path -LiteralPath $OutputRoot) {
    throw "Refusing to overwrite Deep Contract closure output: $OutputRoot"
}
[void](New-Item -ItemType Directory -Path $OutputRoot)
$OutputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path

$official = Read-Json $officialPath
$map = Read-Json $mapPath
$campaign = Read-Json $campaignPath
$sessionId = [string](Get-Property $official 'SessionId')
if ((Get-Property $official 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
    -not [bool](Get-Property $official 'Passed') -or
    (Get-Property $official 'ClientSHA256') -cne $officialSha256 -or
    (Get-Property $map 'SchemaVersion') -cne 'god2-deep-probe-candidate-map-v3' -or
    (Get-Property $map 'TargetSHA256') -cne $officialSha256 -or
    [bool](Get-Property $campaign 'MemoryWritten') -or
    [bool](Get-Property $campaign 'GameplayStateModified') -or
    [bool](Get-Property $campaign 'ExtraNetworkTrafficGenerated')) {
    throw 'Official authority, exact target identity, or read-only acquisition contract failed.'
}

$observations = [Collections.Generic.List[object]]::new()
foreach ($line in Get-Content -LiteralPath $observationPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    [void]$observations.Add(($line | ConvertFrom-Json))
}
$runtimeObservations = @($observations | Where-Object {
    [bool](Get-Property $_ 'RuntimeObservation')
})
if ($runtimeObservations.Count -ne [int](Get-Property $official 'ContractAcquisitionRuntimeObservationCount') -or
    @($runtimeObservations | Where-Object {
        (Get-Property $_ 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
        -not [bool](Get-Property $_ 'CurrentSessionObserved') -or
        [bool](Get-Property $_ 'HistoricalEvidenceReused') -or
        [bool](Get-Property $_ 'FixtureOnly')
    }).Count -ne 0) {
    throw 'Runtime observation count or CurrentOfficialClientLive authority binding failed.'
}

$semanticEvents = [Collections.Generic.List[object]]::new()
foreach ($line in Get-Content -LiteralPath $semanticPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    [void]$semanticEvents.Add((ConvertFrom-God2SemanticEventJson -Json $line))
}
if ($semanticEvents.Count -ne [int](Get-Property $official 'RuntimeEventCount')) {
    throw 'Semantic event count does not match the official runtime summary.'
}

$semanticThreadIds = @(Get-DistinctStrings @($semanticEvents | ForEach-Object {
    [string](Get-Property $_ 'ThreadId')
}))
$semanticEventTypes = @(Get-DistinctStrings @($semanticEvents | ForEach-Object {
    [string](Get-Property $_ 'EventType')
}))
$currentOfficialLiveProducerCount = $semanticEventTypes.Count
$currentOfficialLiveVerifiedProducerCount = @($semanticEventTypes | Where-Object {
    $_ -cin @('PacketBoundary','ParserRead','SerializerWrite','HandlerInvocation')
}).Count
$deepProducerEventTypes = @('ObjectAllocated','ObjectDestroyed','ObjectResolved','ObjectLookup',
    'RegistryLocated','RegistryEnumerated','ResourceRead','ResourceDecoded',
    'ResourceDeserialized','StateMutation','TaintSeed','TaintPropagation','ValueFlow',
    'FormulaOperand','FormulaResult','SnapshotObject','SnapshotEdge')
$currentOfficialLiveDeepProducerCount = @($semanticEventTypes | Where-Object {
    $_ -cin $deepProducerEventTypes
}).Count
$candidateMapById = @{}
foreach ($candidate in @(Get-Property $map 'Candidates')) {
    $candidateMapById[[string](Get-Property $candidate 'CandidateId')] = $candidate
}
$campaignDomains = @(Get-Property $campaign 'Domains')
$candidateIds = @($campaignDomains | ForEach-Object {
    [string](Get-Property $_ 'CandidateId')
})
if ($candidateIds.Count -ne 11 -or @($candidateIds | Sort-Object -Unique).Count -ne 11) {
    throw 'Campaign must bind exactly 11 unique fundamental candidates.'
}

$analysisRows = [Collections.Generic.List[object]]::new()
$abiRows = [Collections.Generic.List[object]]::new()
$threadRows = [Collections.Generic.List[object]]::new()
$reentrancyRows = [Collections.Generic.List[object]]::new()
$stabilityRows = [Collections.Generic.List[object]]::new()
$stabilityClusterRows = [Collections.Generic.List[object]]::new()
$consumerRows = [Collections.Generic.List[object]]::new()
$causalRows = [Collections.Generic.List[object]]::new()
$typedRows = [Collections.Generic.List[object]]::new()
$priorityRows = [Collections.Generic.List[object]]::new()
$ledgerRows = [Collections.Generic.List[object]]::new()
$missingRows = [Collections.Generic.List[object]]::new()
$now = [DateTimeOffset]::UtcNow

foreach ($candidateId in $candidateIds) {
    $candidate = $candidateMapById[$candidateId]
    if ($null -eq $candidate) { throw "Candidate map binding is missing: $candidateId" }
    $domain = [string](Get-Property $candidate 'Domain')
    $candidateRva = [string](Get-Property $candidate 'RVA')
    $boundary = Get-Property $candidate 'FunctionBoundary'
    $functionStartRva = [string](Get-Property $boundary 'StartRVA')
    $rows = @($runtimeObservations | Where-Object {
        (Get-Property $_ 'CandidateId') -ceq $candidateId
    })
    $entries = @($rows | Where-Object { (Get-Property $_ 'Phase') -ceq 'Entry' })
    $returns = @($rows | Where-Object { (Get-Property $_ 'Phase') -ceq 'Return' })
    $consumerSteps = @($rows | Where-Object { (Get-Property $_ 'Phase') -ceq 'ConsumerStep' })
    $observedProbeRvas = @(Get-DistinctStrings @($entries | ForEach-Object {
        $probe = Get-Property $_ 'ProbeRVA'
        if ([string]::IsNullOrWhiteSpace([string]$probe)) { Get-Property $_ 'RVA' } else { $probe }
    }))
    $atFunctionEntry = $entries.Count -gt 0 -and $observedProbeRvas.Count -eq 1 -and
        $observedProbeRvas[0] -ceq $functionStartRva
    $entryIds = @(Get-DistinctStrings @($entries | ForEach-Object { Get-Property $_ 'InvocationId' }))
    $returnIds = @(Get-DistinctStrings @($returns | ForEach-Object { Get-Property $_ 'InvocationId' }))
    $pairedIds = @($entryIds | Where-Object { $_ -cin $returnIds })
    $threadIds = @(Get-DistinctStrings @($rows | ForEach-Object { Get-Property $_ 'ThreadId' }))
    $returnRvas = @(Get-DistinctStrings @($rows | ForEach-Object { Get-Property $_ 'ReturnRVA' }))
    $cleanup = @($returns | ForEach-Object { Get-Property $_ 'StackCleanupBytes' } |
        Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ } | Sort-Object -Unique)
    $maxDepth = if ($rows.Count -eq 0) { 0 } else {
        [int](($rows | Measure-Object -Property InvocationDepth -Maximum).Maximum)
    }
    $nestedCount = @($rows | Where-Object { [bool](Get-Property $_ 'NestedInvocation') }).Count
    $recursionCount = @($rows | Where-Object { [bool](Get-Property $_ 'SameCandidateRecursion') }).Count
    $semanticThreadOverlap = @($threadIds | Where-Object { $_ -cin $semanticThreadIds })
    $registers = [ordered]@{}
    foreach ($register in @('EAX','EBX','ECX','EDX','ESI','EDI','EBP')) {
        $registers[$register] = Get-ValueSummary @($entries | ForEach-Object {
            Get-Property (Get-Property $_ 'RegisterState') $register
        })
    }
    $stack = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt 8; ++$index) {
        [void]$stack.Add([pscustomobject][ordered]@{
            ArgIndex = $index
            Source = 'Stack'
            RegisterOrStackOffset = ('ESP+0x{0:X2}' -f (4 * ($index + 1)))
            Width = 4
            Evidence = Get-ValueSummary @($entries | ForEach-Object {
                $arguments = @(Get-Property $_ 'StackArguments')
                if ($arguments.Count -gt $index) { $arguments[$index] } else { $null }
            } | Where-Object { $null -ne $_ })
            ContractVerified = $false
        })
    }
    $returnEax = Get-ValueSummary @($returns | ForEach-Object {
        Get-Property (Get-Property $_ 'RegisterState') 'EAX'
    })
    $thisSummary = Get-ValueSummary @($entries | ForEach-Object {
        Get-Property $_ 'ThisPointerCandidate'
    })
    $rawPointerPersisted = @($rows | Where-Object {
        $null -ne $_.PSObject.Properties['RawValue'] -or
        [bool](Get-Property $_ 'SensitivePayloadPersisted')
    }).Count -gt 0
    $sensitiveMaskVerified = $rows.Count -gt 0 -and -not $rawPointerPersisted

    $callingConventionCandidate = [string](Get-Property $candidate 'CallingConventionCandidate')
    $callingConventionVerified = $atFunctionEntry -and $entries.Count -ge 3 -and
        $returns.Count -ge 3 -and $cleanup.Count -eq 1
    $argumentVerified = $callingConventionVerified -and $false
    $threadVerified = $entries.Count -ge 3 -and $threadIds.Count -eq 1 -and
        $semanticThreadOverlap.Count -gt 0 -and $false
    $reentrancyVerified = $entries.Count -ge 3 -and $nestedCount -eq 0 -and
        $recursionCount -eq 0 -and $atFunctionEntry -and $false
    $stableObject = $atFunctionEntry -and $thisSummary.StableSingleToken -and $false
    $consumerInvocationIds = @(Get-DistinctStrings @($consumerSteps | ForEach-Object {
        Get-Property $_ 'InvocationId'
    }))
    $returnLifetimeVerified = $atFunctionEntry -and $returns.Count -ge 3 -and
        $consumerInvocationIds.Count -ge 3 -and @($consumerSteps | Where-Object {
            [string]::IsNullOrWhiteSpace([string](Get-Property $_ 'InstructionRVA')) -or
            [string]::IsNullOrWhiteSpace([string](Get-Property $_ 'InstructionBytesHex'))
        }).Count -eq 0
    $consumerVerified = $false
    $typedRuntimeVerified = $false
    $causalVerified = $false
    $contradictions = [Collections.Generic.List[string]]::new()
    foreach ($value in @(Get-Property $candidate 'Contradictions')) {
        [void]$contradictions.Add([string]$value)
    }
    if (-not $atFunctionEntry) { [void]$contradictions.Add('ProbeRvaDoesNotMatchFunctionStart') }
    if ($semanticThreadOverlap.Count -eq 0 -and $entries.Count -gt 0) {
        [void]$contradictions.Add('NoSameThreadParserHandlerSemanticCorrelation')
    }
    if ($returns.Count -gt 0 -and -not $returnLifetimeVerified) {
        [void]$contradictions.Add('ReturnRegisterObservedButLifetimeNotTracked')
    }
    if ($entries.Count -gt 0 -and $consumerSteps.Count -eq 0) {
        [void]$contradictions.Add('ConsumerOrMutationNotObserved')
    } elseif ($consumerSteps.Count -gt 0) {
        [void]$contradictions.Add('CallerInstructionsObservedButReturnConsumptionNotSemanticallyDecoded')
    }
    $contradictionsResolved = $contradictions.Count -eq 0

    $gate = [ordered]@{
        ExactTargetIdentity = $true
        ExecutableSection = -not [string]::IsNullOrWhiteSpace([string](Get-Property $candidate 'ExecutableSection'))
        ExactCandidateBytes = [string](Get-Property $candidate 'ByteMask') -cmatch '^(FF ?)+$'
        CallingConventionVerified = $callingConventionVerified
        TypedRuntimeEvidence = $typedRuntimeVerified
        RepeatedCausalObservation = $causalVerified
        ContradictionsResolved = $contradictionsResolved
        StableObjectOrContext = $stableObject
        VerifiedConsumerOrMutation = $consumerVerified
        SensitiveMaskContract = $sensitiveMaskVerified
        ArgumentContractVerified = $argumentVerified
        ReturnValueLifetimeVerified = $returnLifetimeVerified
        ThreadContextVerified = $threadVerified
        ReentrancyRiskVerified = $reentrancyVerified
    }
    $passedGateCount = @($gate.GetEnumerator() | Where-Object Value).Count
    $promotionEligible = $passedGateCount -eq $gateNames.Count

    $semanticLeverage = switch ($domain) {
        'Object' { 12 } 'ManagerLookup' { 11 } 'Registry' { 10 }
        'Allocation' { 8 } 'VTable' { 8 } 'Factory' { 8 }
        'Mutation' { 7 } 'ResourceDecode' { 7 } default { 4 }
    }
    $score = [Math]::Round(
        ([Math]::Min(40,$entries.Count) * 1.5) +
        $(if($entries.Count -ge 3){20}else{0}) +
        $(if($threadIds.Count -eq 1 -and $entries.Count -gt 0){10}else{0}) +
        $(if($returnRvas.Count -gt 0 -and $returnRvas.Count -le 3){8}else{0}) +
        $(if($cleanup.Count -eq 1 -and $returns.Count -ge 3){8}else{0}) +
        $(if($nestedCount -eq 0 -and $recursionCount -eq 0 -and $entries.Count -gt 0){6}else{0}) +
        $semanticLeverage - $(if(-not $atFunctionEntry -and $entries.Count -gt 0){25}else{0}),2)

    $evidenceRefs = Get-EvidenceReferences $rows @(
        'deep-probe-candidate-map-v3.json#CandidateId=' + $candidateId,
        'official-runtime-summary.json#SessionId=' + $sessionId)
    $missing = @($gateNames | Where-Object { -not [bool]$gate[$_] })
    [void]$analysisRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;CandidateRVA=$candidateRva
        FunctionStartRVA=$functionStartRva;ObservedProbeRVAs=$observedProbeRvas
        ObservationAtFunctionEntry=$atFunctionEntry
        HitCount=$rows.Count;EntryCount=$entries.Count;ReturnCount=$returns.Count
        ConsumerStepCount=$consumerSteps.Count
        PairedInvocationCount=$pairedIds.Count;FirstQpc=if($rows.Count){[int64](($rows|Measure-Object Qpc -Minimum).Minimum)}else{$null}
        LastQpc=if($rows.Count){[int64](($rows|Measure-Object Qpc -Maximum).Maximum)}else{$null}
        ThreadIds=$threadIds;CallerRVASet=$returnRvas;StackCleanupBytes=$cleanup
        Registers=$registers;StackArguments=@($stack);ReturnValue=$returnEax
        MaxInvocationDepth=$maxDepth;NestedObservationCount=$nestedCount
        SameCandidateRecursionCount=$recursionCount;SemanticThreadOverlap=$semanticThreadOverlap
        Contradictions=@($contradictions);EvidenceRefs=$evidenceRefs
    })
    [void]$abiRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ObservationCount=$rows.Count
        CandidateRVA=$candidateRva;FunctionStartRVA=$functionStartRva
        ObservationAtFunctionEntry=$atFunctionEntry
        CallingConventionCandidate=$callingConventionCandidate
        CallingConventionVerified=$callingConventionVerified
        CallerCleanupObserved=$false;CalleeCleanupBytes=$cleanup
        EcxThisPointerCandidate=$thisSummary;ArgumentContracts=@($stack)
        ArgumentContractVerified=$argumentVerified;ReturnValue=$returnEax
        ReturnValueLifetimeVerified=$returnLifetimeVerified
        EvidenceRefs=$evidenceRefs;Contradictions=@($contradictions)
        Status=if($rows.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}elseif(-not $atFunctionEntry){
            'EVIDENCE_BLOCKED_OBSERVATION_POINT_NOT_FUNCTION_ENTRY'
        }else{'EVIDENCE_BLOCKED_ABI_GATES_INCOMPLETE'}
    })
    [void]$threadRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ThreadIds=$threadIds
        SemanticEventThreadIds=$semanticThreadIds;SameThreadSemanticCorrelation=$semanticThreadOverlap
        ThreadStartAddressCandidate=$null;CallContext='HardwareBreakpointCurrentOfficialClientLive'
        ObservedCount=$entries.Count;ThreadContextVerified=$threadVerified
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'EVIDENCE_BLOCKED_THREAD_ROLE_OR_START_ADDRESS_REQUIRED'}
    })
    [void]$reentrancyRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;EntryObservationCount=$entries.Count
        ReentrantObserved=($nestedCount -gt 0 -or $recursionCount -gt 0)
        MaxDepth=$maxDepth;NestedCandidateSet=@();SameCandidateRecursionCount=$recursionCount
        UnsafeHookRisk=if(-not $atFunctionEntry){'HIGH_UNTIL_TRUE_FUNCTION_ENTRY_IS_OBSERVED'}else{'UNKNOWN'}
        ReentrancyRiskVerified=$reentrancyVerified
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'EVIDENCE_BLOCKED_BOUNDED_ABSENCE_IS_NOT_PROOF'}
    })
    [void]$stabilityRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ThisPointerCandidate=$thisSummary
        StableVtableObserved=$false;StablePropertyLayoutObserved=$false
        RepeatedConsumerObserved=$false;ObjectLifetimeObserved=$false
        ObjectAddressReuseObserved=$false;StableObjectOrContext=$stableObject
        EvidenceRefs=$evidenceRefs
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'EVIDENCE_BLOCKED_RAW_POINTER_FREE_TOKENS_LACK_VTABLE_AND_LIFETIME_PROOF'}
    })
    [void]$stabilityClusterRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ObservationCount=$entries.Count
        ObjectCandidateSource='ECX';ValueClasses=$thisSummary.ValueClasses
        StableTokenCount=$thisSummary.DistinctTokenCount
        StableTokens=$thisSummary.Tokens;RepeatedTokenObserved=$thisSummary.StableSingleToken
        VTableVerified=$false;PropertyLayoutVerified=$false;LifetimeVerified=$false
        AddressReuseVerified=$false;StableObjectOrContext=$stableObject
        EvidenceRefs=$evidenceRefs
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'TOKEN_CLUSTER_OBSERVED_OBJECT_CONTRACT_EVIDENCE_BLOCKED'}
    })
    [void]$consumerRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ImmediateCallerRVAs=$returnRvas
        StaticConsumerRelation=Get-Property $candidate 'ConsumerRelation'
        RuntimeConsumerObserved=($consumerSteps.Count -gt 0)
        ConsumerInstructionRVAs=@(Get-DistinctStrings @($consumerSteps | ForEach-Object {
            Get-Property $_ 'InstructionRVA'
        }))
        ConsumerInstructionBytesSHA256=@($consumerSteps | ForEach-Object {
            $hex=[string](Get-Property $_ 'InstructionBytesHex')
            if($hex -cmatch '^[0-9A-F]{16}$'){
                $bytes=for($i=0;$i -lt 16;$i+=2){[Convert]::ToByte($hex.Substring($i,2),16)}
                Get-ByteSha256 ([byte[]]$bytes)
            }
        } | Sort-Object -Unique)
        MemoryReadObserved=$false;MemoryWriteObserved=$false
        BranchUsageObserved=$false;DownstreamStateObserved=$false
        VerifiedConsumerOrMutation=$consumerVerified;EvidenceRefs=$evidenceRefs
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'EVIDENCE_BLOCKED_CONSUMER_OR_MUTATION_TRACE_REQUIRED'}
    })
    [void]$causalRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;ObservationCount=$rows.Count
        IndependentObservationCount=$pairedIds.Count;StableStructureObserved=$false
        SameCausalRelationObserved=$false;PacketOrHandlerCorrelationCount=0
        RepeatedCausalObservation=$causalVerified;EvidenceRefs=$evidenceRefs
        Status=if($entries.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}else{
            'EVIDENCE_BLOCKED_NO_TYPED_SOURCE_TO_CONSUMER_CHAIN'}
    })
    if ($rows.Count -gt 0) {
        [void]$typedRows.Add([pscustomobject][ordered]@{
            CandidateId=$candidateId;Domain=$domain
            RuntimeValueType=@($registers.Values | ForEach-Object { $_.ValueClasses } | Sort-Object -Unique)
            Source='CurrentOfficialClientLiveHardwareBreakpoint'
            Consumer=$null;ObjectCandidate=$thisSummary
            ObservationCount=$rows.Count;EvidenceRefs=$evidenceRefs
            TypedRuntimeEvidence=$false
            Status='EVIDENCE_BLOCKED_VALUE_CLASSES_ARE_NOT_A_SEMANTIC_CONTRACT'
        })
    }
    [void]$priorityRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;PromotionPriorityScore=$score
        HitCount=$rows.Count;RepeatedObservation=($pairedIds.Count -ge 3)
        StableThreadContext=($threadIds.Count -eq 1 -and $entries.Count -gt 0)
        StableCallsite=($returnRvas.Count -gt 0 -and $returnRvas.Count -le 3)
        StableStackShape=($cleanup.Count -eq 1 -and $returns.Count -ge 3)
        StableEcxThisCandidate=$thisSummary.StableSingleToken
        StableReturnBehavior=($returnEax.ValueClasses.Count -eq 1 -and $returns.Count -ge 3)
        ParserHandlerCorrelation=($semanticThreadOverlap.Count -gt 0)
        StrongConsumerRelation=$consumerVerified;LowReentrancyRisk=$reentrancyVerified
        SemanticLeverage=$semanticLeverage;MissingGates=$missing
    })
    foreach ($gateName in $gateNames) {
        $passed = [bool]$gate[$gateName]
        [void]$ledgerRows.Add([pscustomobject][ordered]@{
            CandidateId=$candidateId;Domain=$domain;Gate=$gateName
            PreviousStatus='EVIDENCE_BLOCKED';CurrentStatus=if($passed){'PASS'}else{'EVIDENCE_BLOCKED'}
            AuthorityProfile='CurrentOfficialClientLive';SessionId=$sessionId
            EvidenceRefs=$evidenceRefs;ObservationCount=$rows.Count
            Contradictions=@($contradictions)
            Decision=if($passed){'PROMOTION_GATE_SATISFIED_BY_BOUND_EVIDENCE'}else{'KEEP_BLOCKED'}
        })
    }
    [void]$missingRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId;Domain=$domain;Priority=$score
        MissingGates=$missing;ObservedCount=$rows.Count
        RequiredTrigger=if($rows.Count -eq 0){'Normal gameplay path that invokes the candidate'}else{
            'Repeat normal gameplay while observing the candidate-map function entry and downstream consumer'}
        ProbeRVA=$functionStartRva;CandidateRVA=$candidateRva
        Reason=if(-not $atFunctionEntry -and $rows.Count -gt 0){
            'Previous breakpoint was inside the function and cannot establish the ABI boundary.'
        }elseif($rows.Count -eq 0){'Candidate was not triggered in the current session.'}else{
            'Typed consumer, lifetime, object, and causal evidence remain incomplete.'}
    })
}

$priority = @($priorityRows | Sort-Object `
    @{ Expression = 'PromotionPriorityScore'; Descending = $true },
    @{ Expression = 'CandidateId'; Descending = $false })
$missingQueue = @($missingRows | Sort-Object `
    @{ Expression = 'Priority'; Descending = $true },
    @{ Expression = 'CandidateId'; Descending = $false })
$promotionEligibleIds = @($candidateIds | Where-Object {
    $id = $_
    @($ledgerRows | Where-Object { $_.CandidateId -ceq $id -and $_.CurrentStatus -cne 'PASS' }).Count -eq 0
})
$promotionEligibleCount = $promotionEligibleIds.Count
$activationAllowedCount = $promotionEligibleCount
$verifiedFundamentalDomainCount = @($promotionEligibleIds | ForEach-Object {
    [string](Get-Property $candidateMapById[$_] 'Domain')
} | Sort-Object -Unique).Count
$finalStatus = if ($promotionEligibleCount -gt 0) {
    'ULTIMATE_FIRST_DEEP_CONTRACT_PROMOTED'
} else {
    'DEEP_CONTRACT_PROMOTION_EXTERNAL_EVIDENCE_REQUIRED'
}
$exactFinalStatus = if ($promotionEligibleCount -gt 0) {
    'ULTIMATE_FIRST_DEEP_CONTRACT_PROMOTED'
} else {
    'ULTIMATE_CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_PENDING'
}

$authority = [ordered]@{
    AuthorityProfile='CurrentOfficialClientLive';SessionId=$sessionId
    TargetExecutable='God2_opt.exe';TargetVersion='1.0.0.1';TargetSHA256=$officialSha256
    SourceArtifact=Get-PortablePath $officialPath
    SourceSHA256=(Get-FileHash -LiteralPath $officialPath -Algorithm SHA256).Hash
    CurrentSessionObserved=$true;HistoricalEvidenceReused=$false;FixtureOnly=$false
    PromotionEligible=($promotionEligibleCount -gt 0)
}

$paths = [ordered]@{}
$paths.ObservationAnalysis = Write-Json 'official-runtime-observation-analysis.json' ([ordered]@{
    SchemaVersion='god2-official-runtime-observation-analysis-v1';GeneratedAtUtc=$now.ToString('o')
    Authority=$authority;InputObservationCount=$runtimeObservations.Count
    OfflineCorrelatedObservationCount=[int](($analysisRows | Measure-Object HitCount -Sum).Sum)
    CandidateCount=$candidateIds.Count;RuntimeObservedCandidateCount=@($analysisRows|Where-Object HitCount -gt 0).Count
    SemanticEventCount=$semanticEvents.Count;SemanticThreadIds=$semanticThreadIds
    CrossObservationCorrelation=$true;PerObservationIndependenceAssumed=$false
    Rows=@($analysisRows);Status='PASS'
})
$paths.Priority = Write-Json 'candidate-promotion-priority.json' ([ordered]@{
    SchemaVersion='god2-candidate-promotion-priority-v1';Authority=$authority
    CandidateCount=$candidateIds.Count;TopCandidateCount=[Math]::Min(2,$priority.Count)
    Rows=$priority;Status='PASS'
})
$paths.Abi = Write-Json 'candidate-abi-report.json' ([ordered]@{
    SchemaVersion='god2-candidate-abi-report-v4';Authority=$authority;CandidateCount=$candidateIds.Count
    RuntimeObservedCount=@($abiRows|Where-Object ObservationCount -gt 0).Count
    CallingConventionVerifiedCount=@($abiRows|Where-Object CallingConventionVerified).Count
    ArgumentContractVerifiedCount=@($abiRows|Where-Object ArgumentContractVerified).Count
    ReturnValueLifetimeVerifiedCount=@($abiRows|Where-Object ReturnValueLifetimeVerified).Count
    Rows=@($abiRows);Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_BLOCKED'}
})
$paths.Thread = Write-Json 'candidate-thread-context.json' ([ordered]@{
    SchemaVersion='god2-candidate-thread-context-v4';Authority=$authority;CandidateCount=$candidateIds.Count
    ThreadContextVerifiedCount=@($threadRows|Where-Object ThreadContextVerified).Count
    Rows=@($threadRows);Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_BLOCKED'}
})
$paths.Reentrancy = Write-Json 'candidate-reentrancy-report.json' ([ordered]@{
    SchemaVersion='god2-candidate-reentrancy-report-v4';Authority=$authority;CandidateCount=$candidateIds.Count
    ReentrancyRiskVerifiedCount=@($reentrancyRows|Where-Object ReentrancyRiskVerified).Count
    Rows=@($reentrancyRows);Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_BLOCKED'}
})
$paths.Stability = Write-Json 'candidate-object-stability.json' ([ordered]@{
    SchemaVersion='god2-candidate-object-stability-v4';Authority=$authority;CandidateCount=$candidateIds.Count
    StableObjectOrContextCount=@($stabilityRows|Where-Object StableObjectOrContext).Count
    Rows=@($stabilityRows);Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_BLOCKED'}
})
$paths.StabilityClusters = Write-JsonLines 'object-stability-clusters.jsonl' @($stabilityClusterRows)
$paths.Consumer = Write-JsonLines 'candidate-consumer-links.jsonl' @($consumerRows)
$paths.Causal = Write-Json 'causal-observation-groups.json' ([ordered]@{
    SchemaVersion='god2-causal-observation-groups-v1';Authority=$authority
    CandidateCount=$candidateIds.Count
    RepeatedCausalObservationCount=@($causalRows|Where-Object RepeatedCausalObservation).Count
    Rows=@($causalRows);Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_BLOCKED'}
})
$paths.Typed = Write-JsonLines 'typed-runtime-evidence.jsonl' @($typedRows)
$paths.Ledger = Write-JsonLines 'deep-probe-promotion-ledger.jsonl' @($ledgerRows)
$paths.Promotion = Write-Json 'candidate-promotion-result.json' ([ordered]@{
    SchemaVersion='god2-candidate-promotion-result-v3';Authority=$authority
    CandidateCount=$candidateIds.Count;GateCountPerCandidate=$gateNames.Count
    LedgerRowCount=$ledgerRows.Count;PassedGateRowCount=@($ledgerRows|Where-Object CurrentStatus -ceq 'PASS').Count
    FailedGateRowCount=0;EvidenceBlockedGateRowCount=@($ledgerRows|Where-Object CurrentStatus -ceq 'EVIDENCE_BLOCKED').Count
    PromotionEligibleCount=$promotionEligibleCount;ActivationAllowedCount=$activationAllowedCount
    ConfirmedDeepContractCount=$promotionEligibleCount
    VerifiedFundamentalDomainCount=$verifiedFundamentalDomainCount
    PromotedCandidates=$promotionEligibleIds;Status=$finalStatus
})
$paths.Producers = Write-Json 'producer-authority-matrix.json' ([ordered]@{
    SchemaVersion='god2-producer-authority-matrix-v1';Authority=$authority
    ProducerImplementedCount=25;NativeSelfTestProducerCount=25
    CurrentOfficialLiveProducerCount=$currentOfficialLiveProducerCount
    HistoricalOfficialLiveProducerCount=0
    CurrentOfficialLiveVerifiedProducerCount=$currentOfficialLiveVerifiedProducerCount
    CurrentOfficialLiveDeepProducerCount=$currentOfficialLiveDeepProducerCount
    CurrentOfficialLiveVerifiedDeepProducerCount=$promotionEligibleCount
    FixtureProducerCount=25;NamesAreMutuallyExclusive=$true
    ProducerMetricConsistency=($currentOfficialLiveVerifiedProducerCount -le $currentOfficialLiveProducerCount -and
        $currentOfficialLiveDeepProducerCount -le $currentOfficialLiveProducerCount -and
        $promotionEligibleCount -le $currentOfficialLiveDeepProducerCount)
    ServerReady=$false;DatabaseReady=$false
    Status='ULTIMATE_INFRASTRUCTURE_PASS_DEEP_LIVE_PENDING'
})
$paths.Missing = Write-Json 'missing-evidence-queue.json' ([ordered]@{
    SchemaVersion='god2-missing-evidence-queue-v1';Authority=$authority
    CandidateCount=$candidateIds.Count;QueueCount=@($missingQueue|Where-Object MissingGates).Count
    Rows=$missingQueue;Status=if($promotionEligibleCount){'PASS'}else{'EVIDENCE_REQUIRED'}
})
$topPlan = @($missingQueue | Where-Object { $_.MissingGates.Count -gt 0 } | Select-Object -First 2)
$planRows = @($topPlan | ForEach-Object {
    [pscustomobject][ordered]@{
        CandidateId=$_.CandidateId;Domain=$_.Domain;Priority=$_.Priority
        CandidateRVA=$_.CandidateRVA;ProbeRVA=$_.ProbeRVA;MissingGates=$_.MissingGates
        RequiredTrigger=$_.RequiredTrigger;EstimatedObservationNeed=64
        Phases=@('TrueFunctionEntryAndReturn','CallerCleanupAndArgumentShape',
            'ReturnLifetimeAndConsumer','ObjectStabilityAndCausalReplay')
        CanaryAllowed=$false
    }
})
$paths.NextPlan = Write-Json 'next-evidence-plan.json' ([ordered]@{
    SchemaVersion='god2-next-evidence-plan-v1';Authority=$authority
    EvidenceDrivenRotation=$true;DebuggerAttachCount=1;DebuggerDetachCount=1
    MaximumConcurrentCandidateBreakpoints=2;SelectedCandidateCount=$planRows.Count
    Rows=$planRows;Status=if($promotionEligibleCount){'NOT_REQUIRED'}else{'READY_FOR_EXTERNAL_LIVE_EVIDENCE'}
})
$paths.Correlation = Write-Json 'official-live-offline-correlation.json' ([ordered]@{
    SchemaVersion='god2-official-live-offline-correlation-v1';Authority=$authority
    RuntimeObservationCount=$runtimeObservations.Count
    OfflineCorrelatedObservationCount=[int](($analysisRows | Measure-Object HitCount -Sum).Sum)
    SemanticEventCount=$semanticEvents.Count;CandidateCountAnalyzed=$candidateIds.Count
    RuntimeObservedCandidateCount=@($analysisRows|Where-Object HitCount -gt 0).Count
    CallingConventionVerifiedCount=@($abiRows|Where-Object CallingConventionVerified).Count
    ArgumentContractVerifiedCount=@($abiRows|Where-Object ArgumentContractVerified).Count
    ThreadContextVerifiedCount=@($threadRows|Where-Object ThreadContextVerified).Count
    ReturnLifetimeVerifiedCount=@($abiRows|Where-Object ReturnValueLifetimeVerified).Count
    ReentrancyRiskVerifiedCount=@($reentrancyRows|Where-Object ReentrancyRiskVerified).Count
    StableObjectOrContextCount=@($stabilityRows|Where-Object StableObjectOrContext).Count
    VerifiedConsumerOrMutationCount=@($consumerRows|Where-Object VerifiedConsumerOrMutation).Count
    PromotionEligibleCount=$promotionEligibleCount;ActivationAllowedCount=$activationAllowedCount
    ConfirmedDeepContractCount=$promotionEligibleCount
    VerifiedFundamentalDomainCount=$verifiedFundamentalDomainCount
    CurrentOfficialLiveProducerCount=$currentOfficialLiveProducerCount
    ServerReady=$false;DatabaseReady=$false
    RemainingBlockers=@($topPlan | ForEach-Object {
        [pscustomobject]@{CandidateId=$_.CandidateId;MissingGates=$_.MissingGates;Reason=$_.Reason}
    });Status=$finalStatus
})

$reportPath = Join-Path $OutputRoot 'CONTRACT-PROMOTION-CLOSURE-REPORT.md'
$reportLines = @(
    '# Contract Promotion Closure Report','',
    ('Generated: ' + $now.ToString('o')),
    ('Authority: CurrentOfficialClientLive / ' + $sessionId),
    ('Target SHA256: ' + $officialSha256),'',
    '## Result','',
    ('- Status: ' + $finalStatus),
    ('- Exact final status: ' + $exactFinalStatus),
    ('- Runtime observations: ' + $runtimeObservations.Count),
    ('- Offline-correlated observations: ' + [int](($analysisRows | Measure-Object HitCount -Sum).Sum)),
    ('- Candidates analyzed: ' + $candidateIds.Count),
    ('- Promotion eligible: ' + $promotionEligibleCount),
    ('- Activation allowed: ' + $activationAllowedCount),
    ('- Verified fundamental domains: ' + $verifiedFundamentalDomainCount),
    '- ServerReady: false','- DatabaseReady: false','',
    '## Decisive Blocker','',
    'The two observed candidates were sampled at RVAs inside their inferred function bounds, not at true function entry.',
    'Their stack and return observations therefore cannot establish a calling convention, argument ABI, or return lifetime.',
    'No UNKNOWN or EVIDENCE_BLOCKED gate was promoted to PASS without bound evidence.','',
    '## Next Evidence','',
    'The next plan targets the candidate-map function-start RVAs for the highest-priority candidates, then follows caller cleanup,',
    'return consumption, object stability, and causal consumer evidence. Production canary remains disabled until all 14 gates pass.'
    )
[IO.File]::WriteAllLines($reportPath,$reportLines,$utf8)
$paths.Report = $reportPath

$packagePath = if ($CreatePortablePackage) {
    Join-Path $OutputRoot 'UltimateDeepContractPromotionValidationPackage.zip'
} else {
    $null
}
$result = [pscustomobject][ordered]@{
    SchemaVersion='god2-deep-contract-promotion-closure-result-v1'
    Status=$finalStatus;ExactFinalStatus=$exactFinalStatus
    ExecutableVersion=(Get-Item $releasePath).VersionInfo.FileVersion
    ExecutableSize=[uint64](Get-Item $releasePath).Length
    ExecutableSHA256=(Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
    ExistingOfficialRuntimeObservationCount=$runtimeObservations.Count
    OfflineCorrelatedObservationCount=[int](($analysisRows | Measure-Object HitCount -Sum).Sum)
    CandidateCountAnalyzed=$candidateIds.Count
    CallingConventionVerifiedCount=@($abiRows|Where-Object CallingConventionVerified).Count
    ArgumentContractVerifiedCount=@($abiRows|Where-Object ArgumentContractVerified).Count
    ThreadContextVerifiedCount=@($threadRows|Where-Object ThreadContextVerified).Count
    ReturnLifetimeVerifiedCount=@($abiRows|Where-Object ReturnValueLifetimeVerified).Count
    ReentrancyRiskVerifiedCount=@($reentrancyRows|Where-Object ReentrancyRiskVerified).Count
    StableObjectOrContextCount=@($stabilityRows|Where-Object StableObjectOrContext).Count
    VerifiedConsumerOrMutationCount=@($consumerRows|Where-Object VerifiedConsumerOrMutation).Count
    PromotionEligibleCount=$promotionEligibleCount;ActivationAllowedCount=$activationAllowedCount
    ConfirmedDeepContractCount=$promotionEligibleCount
    VerifiedFundamentalDomainCount=$verifiedFundamentalDomainCount
    CurrentOfficialLiveProducerCount=$currentOfficialLiveProducerCount
    ServerReady=$false;DatabaseReady=$false
    PortableValidationPackagePath=if($packagePath){Get-PortablePath $packagePath}else{$null}
    PortableValidationPackageSHA256=$null
    OutputRoot=Get-PortablePath $OutputRoot
}
$resultPath = Write-Json 'deep-contract-promotion-closure-result.json' $result
$paths.Result = $resultPath

$packageSha256 = $null
if ($CreatePortablePackage) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage = Join-Path $OutputRoot 'portable-package'
    [void](New-Item -ItemType Directory -Path $stage)
    foreach ($path in $paths.Values) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $stage ([IO.Path]::GetFileName($path)))
    }
    Copy-Item -LiteralPath $releasePath -Destination (Join-Path $stage 'God2SemanticRecoveryEngine.exe')
    $runner = @(
        '@echo off','setlocal','cd /d "%~dp0"',
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Invoke-Portable-Deep-Contract-Capture.ps1"',
        'set "EXIT_CODE=%ERRORLEVEL%"','echo.','pause','exit /b %EXIT_CODE%')
    [IO.File]::WriteAllLines((Join-Path $stage 'Start-Deep-Contract-Promotion-Validation.cmd'),$runner,$utf8)
    $portableRunner = @'
$ErrorActionPreference='Stop'
$cursor=Get-Item -LiteralPath $PSScriptRoot
$workspace=$null
while($cursor){
    $candidate=Join-Path $cursor.FullName 'Automation\Invoke-DeepContractPromotionLiveCapture.ps1'
    if(Test-Path -LiteralPath $candidate -PathType Leaf){$workspace=$cursor.FullName;break}
    $cursor=$cursor.Parent
}
if(-not $workspace){throw 'Extract this package anywhere under the God2 Classic Server workspace.'}
$sourcePlan=Join-Path $PSScriptRoot 'next-evidence-plan.json'
$planRoot=Join-Path $workspace ('Artifacts\DeepContractPlans\portable-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[void](New-Item -ItemType Directory -Path $planRoot)
$workspacePlan=Join-Path $planRoot 'next-evidence-plan.json'
Copy-Item -LiteralPath $sourcePlan -Destination $workspacePlan
& (Join-Path $workspace 'Automation\Invoke-DeepContractPromotionLiveCapture.ps1') `
    -EvidencePlanPath $workspacePlan
exit $LASTEXITCODE
'@
    [IO.File]::WriteAllText((Join-Path $stage 'Invoke-Portable-Deep-Contract-Capture.ps1'),
        $portableRunner,$utf8)
    $inventory = @()
    foreach ($item in Get-ChildItem -LiteralPath $stage -File | Sort-Object Name) {
        $inventory += [pscustomobject][ordered]@{
            Path=$item.Name;Bytes=[uint64]$item.Length
            SHA256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        }
    }
    $packageManifest = [ordered]@{
        SchemaVersion='god2-deep-contract-promotion-validation-package-v1'
        ManifestSelfEntryPolicy='Manifest is present in the ZIP and excluded from Files because a recursive self-hash is undefined.'
        GeneratedAtUtc=$now.ToString('o');Authority=$authority
        ReleaseSHA256=(Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
        Status=$finalStatus;PromotionEligibleCount=$promotionEligibleCount
        ActivationAllowedCount=$activationAllowedCount;Files=$inventory
    }
    [IO.File]::WriteAllText((Join-Path $stage 'validation-package-manifest.json'),
        (($packageManifest|ConvertTo-Json -Depth 12)+"`n"),$utf8)
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$packagePath,
        [IO.Compression.CompressionLevel]::Optimal,$false)
    $packageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    $result.PortableValidationPackageSHA256 = $packageSha256
    [IO.File]::WriteAllText($resultPath,(($result | ConvertTo-Json -Depth 12) + "`n"),$utf8)
}

$result
