Set-StrictMode -Version 2.0

$script:God2DeepDomainDefinitions = @(
    [ordered]@{ Domain='Network'; Probe='WinsockTransport'; IntendedEvent='PacketBoundary'; Group='Fundamental'; Confirmed=$true },
    [ordered]@{ Domain='Parser'; Probe='PacketDecode.FrameBoundary'; IntendedEvent='ParserRead'; Group='Fundamental'; Confirmed=$true },
    [ordered]@{ Domain='Serializer'; Probe='OutboundEnqueue.FrameBuilder'; IntendedEvent='SerializerWrite'; Group='Fundamental'; Confirmed=$true },
    [ordered]@{ Domain='Handler'; Probe='Battle.HandlerRecordLength'; IntendedEvent='HandlerInvocation'; Group='Fundamental'; Confirmed=$true },
    [ordered]@{ Domain='Object'; Probe='ObjectResolver'; IntendedEvent='ObjectResolved'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='Allocation'; Probe='AllocationProbe'; IntendedEvent='ObjectAllocated'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='VTable'; Probe='VTableProbe'; IntendedEvent='ObjectResolved'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='Factory'; Probe='FactoryProbe'; IntendedEvent='ObjectAllocated'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='ManagerLookup'; Probe='ManagerLookupProbe'; IntendedEvent='ObjectLookup'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='Registry'; Probe='RegistryProbe'; IntendedEvent='RegistryEnumerated'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='ResourceDecode'; Probe='ResourceDecodeProbe'; IntendedEvent='ResourceDeserialized'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='Mutation'; Probe='MutationProbe'; IntendedEvent='StateMutation'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='TaintSeed'; Probe='TaintSeedProbe'; IntendedEvent='TaintSeed'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='FormulaOperand'; Probe='FormulaOperandProbe'; IntendedEvent='FormulaOperand'; Group='Fundamental'; Confirmed=$false },
    [ordered]@{ Domain='Quest'; Probe='QuestProbe'; IntendedEvent='StateMutation'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Map'; Probe='MapProbe'; IntendedEvent='ObjectResolved'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Portal'; Probe='PortalProbe'; IntendedEvent='SnapshotEdge'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='NPC'; Probe='NPCProbe'; IntendedEvent='ObjectResolved'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Monster'; Probe='MonsterProbe'; IntendedEvent='ObjectResolved'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Battle'; Probe='BattleProbe'; IntendedEvent='StateMutation'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Inventory'; Probe='InventoryProbe'; IntendedEvent='StateMutation'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Item'; Probe='ItemProbe'; IntendedEvent='ObjectResolved'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Skill'; Probe='SkillProbe'; IntendedEvent='FormulaOperand'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Pet/Mount'; Probe='PetMountProbe'; IntendedEvent='ObjectResolved'; Group='Content'; Confirmed=$false },
    [ordered]@{ Domain='Snapshot'; Probe='SnapshotProbe'; IntendedEvent='SnapshotObject'; Group='Fundamental'; Confirmed=$false }
)

$script:God2SemanticEventTypes = @(
    'PacketBoundary','ParserRead','SerializerWrite','HandlerInvocation','HandlerArgument',
    'ObjectAllocated','ObjectDestroyed','ObjectResolved','ObjectLookup','RegistryLocated',
    'RegistryEnumerated','ResourceRead','ResourceDecoded','ResourceDeserialized','StateMutation',
    'TaintSeed','TaintPropagation','ValueFlow','FormulaOperand','FormulaResult',
    'FunctionRoleCandidate','UIAnchor','SnapshotObject','SnapshotEdge','ProbeDiagnostic'
)

$script:God2PromotionGates = @(
    [ordered]@{ Gate='ExactTargetIdentity'; Category='Promotion' },
    [ordered]@{ Gate='ExecutableSection'; Category='Promotion' },
    [ordered]@{ Gate='ExactCandidateBytes'; Category='Promotion' },
    [ordered]@{ Gate='CallingConventionVerified'; Category='Promotion' },
    [ordered]@{ Gate='TypedRuntimeEvidence'; Category='Promotion' },
    [ordered]@{ Gate='RepeatedCausalObservation'; Category='Promotion' },
    [ordered]@{ Gate='ContradictionsResolved'; Category='Promotion' },
    [ordered]@{ Gate='StableObjectOrContext'; Category='Promotion' },
    [ordered]@{ Gate='VerifiedConsumerOrMutation'; Category='Promotion' },
    [ordered]@{ Gate='SensitiveMaskContract'; Category='Promotion' },
    [ordered]@{ Gate='ArgumentContractVerified'; Category='AbiSafety' },
    [ordered]@{ Gate='ReturnValueLifetimeVerified'; Category='AbiSafety' },
    [ordered]@{ Gate='ThreadContextVerified'; Category='AbiSafety' },
    [ordered]@{ Gate='ReentrancyRiskVerified'; Category='AbiSafety' }
)

function Get-God2DeepProperty([object]$Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Write-God2DeepJson([string]$Path, [object]$Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent)
    }
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 16) + "`n"), $utf8)
}

function Write-God2DeepJsonLines([string]$Path, [object[]]$Rows) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent)
    }
    $builder = [Text.StringBuilder]::new()
    foreach ($row in $Rows) {
        [void]$builder.AppendLine(($row | ConvertTo-Json -Depth 12 -Compress))
    }
    [IO.File]::WriteAllText($Path, $builder.ToString(), [Text.UTF8Encoding]::new($false, $true))
}

function Add-God2AuthorityFields([Collections.Specialized.OrderedDictionary]$Row,
                                 [object]$Authority) {
    foreach ($name in @(
            'AuthorityProfile','SessionId','SourceArtifact','SourceSHA256','TargetExecutable',
            'TargetVersion','TargetSHA256','TargetProcessId','TargetProcessCreationTime',
            'CurrentSessionObserved','HistoricalEvidenceReused','FixtureOnly','PromotionEligible')) {
        if ($Row.Contains($name)) { continue }
        $Row[$name] = Get-God2DeepProperty $Authority $name
    }
    return $Row
}

function Get-God2DomainRuntimeEvents([object[]]$Events, [object]$Definition) {
    return @($Events | Where-Object {
        [string](Get-God2DeepProperty $_ 'EventType') -ceq [string]$Definition.IntendedEvent -and
        [string](Get-God2DeepProperty $_ 'SourceToken') -ceq [string]$Definition.Probe
    })
}

function New-God2DeepRecoveryEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$CandidateMap,
        [Parameter(Mandatory)][object[]]$SemanticEvents,
        [Parameter(Mandatory)][object]$AuthorityRecord,
        [Parameter(Mandatory)][string]$OutputRoot,
        [Parameter(Mandatory)][string]$CurrentExecutableSHA256,
        [string]$GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    )

    if ($script:God2DeepDomainDefinitions.Count -ne 25 -or
        $script:God2SemanticEventTypes.Count -ne 25 -or
        $script:God2PromotionGates.Count -ne 14) {
        throw 'Deep recovery evidence definitions are incomplete.'
    }
    if (-not (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $OutputRoot)
    }

    $candidateSchemaVersion = [string](Get-God2DeepProperty $CandidateMap 'SchemaVersion')
    $candidateMapIsV3 = $candidateSchemaVersion -ceq 'god2-deep-probe-candidate-map-v3'
    $candidateDomains = if ($candidateMapIsV3) { @() } else {
        @(Get-God2DeepProperty $CandidateMap 'Domains')
    }
    $staticCandidates = if ($candidateMapIsV3) {
        @(Get-God2DeepProperty $CandidateMap 'Candidates')
    } else { @() }
    $candidateTargetSha256 = if ($candidateMapIsV3) {
        [string](Get-God2DeepProperty $CandidateMap 'TargetSHA256')
    } else { [string](Get-God2DeepProperty $CandidateMap 'ClientSHA256') }
    $candidateAuthority = if ($candidateMapIsV3) {
        Get-God2DeepProperty $CandidateMap 'Authority'
    } else { $AuthorityRecord }
    if ($candidateMapIsV3 -and
        ([string](Get-God2DeepProperty $candidateAuthority 'AuthorityProfile') -cne
            'ExactBinaryStaticAnalysis' -or
         [bool](Get-God2DeepProperty $candidateAuthority 'PromotionEligible') -or
         [bool](Get-God2DeepProperty $candidateAuthority 'CurrentSessionObserved'))) {
        throw 'Candidate map v3 authority is not fail-closed ExactBinaryStaticAnalysis.'
    }
    $capabilityRows = [Collections.Generic.List[object]]::new()
    $runtimeRows = [Collections.Generic.List[object]]::new()
    $candidateV3Rows = [Collections.Generic.List[object]]::new()
    $promotionRows = [Collections.Generic.List[object]]::new()
    $observationRows = [Collections.Generic.List[object]]::new()
    $abiRows = [Collections.Generic.List[object]]::new()
    $threadRows = [Collections.Generic.List[object]]::new()
    $reentrancyRows = [Collections.Generic.List[object]]::new()
    $objectStabilityRows = [Collections.Generic.List[object]]::new()
    $consumerRows = [Collections.Generic.List[object]]::new()

    for ($domainIndex = 0; $domainIndex -lt $script:God2DeepDomainDefinitions.Count; ++$domainIndex) {
        $definition = $script:God2DeepDomainDefinitions[$domainIndex]
        $candidateDomain = @($candidateDomains | Where-Object {
            [string](Get-God2DeepProperty $_ 'Domain') -ceq [string]$definition.Domain
        }) | Select-Object -First 1
        $verification = Get-God2DeepProperty $candidateDomain 'VerificationContract'
        $candidateRows = if ([bool]$definition.Confirmed) {
            @()
        } elseif ($candidateMapIsV3) {
            @($staticCandidates | Where-Object {
                [string](Get-God2DeepProperty $_ 'Domain') -ceq [string]$definition.Domain
            })
        } else {
            @(Get-God2DeepProperty $candidateDomain 'Candidates')
        }
        $candidateRows = @($candidateRows)
        # The four fundamental contracts are independently confirmed. The 14-gate
        # promotion ledger applies to discovered deep-probe candidates only.
        $allGatesPassed = [bool]$definition.Confirmed
        if (-not [bool]$definition.Confirmed -and -not $candidateMapIsV3) {
            $allGatesPassed = $null -ne $verification
            foreach ($gate in $script:God2PromotionGates) {
                if (-not [bool](Get-God2DeepProperty $verification $gate.Gate)) {
                    $allGatesPassed = $false
                }
            }
        }
        $runtimeEvents = @(Get-God2DomainRuntimeEvents $SemanticEvents $definition)
        $runtimeCount = [uint64]$runtimeEvents.Count
        # The native engine now contains a bounded producer or semantic adapter
        # for every domain. Contract confirmation and runtime activation remain
        # separate evidence gates and are never inferred from implementation.
        $producerImplemented = $true
        $currentObserved = [bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved') -and
            $runtimeCount -gt 0
        $historicalObserved = [bool](Get-God2DeepProperty $AuthorityRecord 'HistoricalEvidenceReused') -and
            $runtimeCount -gt 0
        $promotionEligible = $allGatesPassed -and $runtimeCount -gt 0 -and
            [bool](Get-God2DeepProperty $AuthorityRecord 'PromotionEligible')
        $domainStatus = if ($promotionEligible) { 'LIVE_OBSERVED_VERIFIED' }
            elseif ($runtimeCount -gt 0) { 'LIVE_OBSERVED_CANDIDATE' }
            elseif ([bool]$definition.Confirmed) { 'CONTRACT_CONFIRMED_NOT_OBSERVED' }
            elseif ($candidateRows.Count -gt 0 -or $null -ne $candidateDomain) { 'IMPLEMENTED_UNCONFIRMED' }
            else { 'NOT_IMPLEMENTED' }
        $blockReason = if ($promotionEligible) { $null }
            elseif ($runtimeCount -eq 0) { 'NoTypedRuntimeProducerEventObserved' }
            elseif (-not $allGatesPassed) { 'DeepProbePromotionOrAbiGateIncomplete' }
            else { 'EvidenceAuthorityNotPromotionEligible' }

        $capability = [ordered]@{
            Index = $domainIndex
            Domain = $definition.Domain
            Probe = $definition.Probe
            DomainGroup = $definition.Group
            CodeImplemented = [bool]$definition.Confirmed -or $candidateRows.Count -gt 0 -or
                $null -ne $candidateDomain
            ExactContractStatus = if ($definition.Confirmed) { 'CONFIRMED' } else { 'UNCONFIRMED' }
            ActivationAllowed = [bool]$allGatesPassed
            SafeABI = [bool]$allGatesPassed
            ProducerImplemented = $producerImplemented
            ProducerImplementation = if ($domainIndex -lt 4) {
                'NativeExactBuildCaptureProducer'
            } elseif ($domainIndex -in @(4,5,6,7,8,9,10,11,12,13,24)) {
                'UltimateDeepRuntimeProducer'
            } else {
                'UltimateGameplaySemanticAdapter'
            }
            PromotionEligible = $false
            Status = $domainStatus
            BlockReason = $blockReason
        }
        [void]$capabilityRows.Add([pscustomobject](Add-God2AuthorityFields $capability $AuthorityRecord))

        $sequences = @($runtimeEvents | ForEach-Object { Get-God2DeepProperty $_ 'Sequence' } |
            Where-Object { $null -ne $_ } | Sort-Object)
        $runtime = [ordered]@{
            Index = $domainIndex
            Domain = $definition.Domain
            Probe = $definition.Probe
            CurrentOfficialSessionObserved = [bool]$currentObserved
            HistoricalOfficialSessionObserved = [bool]$historicalObserved
            RuntimeEventCount = $runtimeCount
            FirstSequence = if ($sequences.Count -eq 0) { $null } else { [uint64]$sequences[0] }
            LastSequence = if ($sequences.Count -eq 0) { $null } else { [uint64]$sequences[-1] }
             TargetIdentityStatus = if ([string](Get-God2DeepProperty $AuthorityRecord 'TargetSHA256') -ceq
                 $candidateTargetSha256) { 'EXACT' } else { 'MISMATCH' }
            LossStatus = if ([bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved')) {
                'REQUIRES_PRODUCTION_HEALTH_BINDING'
            } else { 'NOT_CURRENT_SESSION' }
            PromotionStatus = if ($promotionEligible) { 'VERIFIED' } else { 'EVIDENCE_BLOCKED' }
            PromotionEligible = [bool]$promotionEligible
            Status = $domainStatus
            BlockReason = $blockReason
        }
        [void]$runtimeRows.Add([pscustomobject](Add-God2AuthorityFields $runtime $AuthorityRecord))

        for ($candidateIndex = 0; $candidateIndex -lt $candidateRows.Count; ++$candidateIndex) {
            $candidate = $candidateRows[$candidateIndex]
            $candidateVerification = if ($candidateMapIsV3) {
                Get-God2DeepProperty $candidate 'PromotionGates'
            } else { $verification }
            $candidateId = if ($candidateMapIsV3) {
                [string](Get-God2DeepProperty $candidate 'CandidateId')
            } else {
                '{0}-{1:D2}-{2:D2}' -f $definition.Domain.Replace('/','-'),
                    $domainIndex,$candidateIndex
            }
            $callsiteValues = if ($candidateMapIsV3) {
                @(Get-God2DeepProperty $candidate 'CallsiteRVAs')
            } else { @([string](Get-God2DeepProperty $candidate 'CallsiteRva')) }
            $callsiteValues = @($callsiteValues | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            })
            $callsite = if ($callsiteValues.Count -eq 0) { $null } else {
                [string]$callsiteValues[0]
            }
            $target = if ($candidateMapIsV3) {
                [string](Get-God2DeepProperty $candidate 'RVA')
            } else { [string](Get-God2DeepProperty $candidate 'TargetRva') }
            $bytes = if ($candidateMapIsV3) {
                [string](Get-God2DeepProperty $candidate 'Bytes')
            } else { [string](Get-God2DeepProperty $candidate 'Signature') }
            $mask = if ($candidateMapIsV3) {
                [string](Get-God2DeepProperty $candidate 'ByteMask')
            } else { [string](Get-God2DeepProperty $candidate 'SignatureMask') }
            $staticPassed = 0
            foreach ($gateName in @('ExactTargetIdentity','ExecutableSection','ExactCandidateBytes')) {
                if ([bool](Get-God2DeepProperty $candidateVerification $gateName)) { ++$staticPassed }
            }
            $argumentCandidateValues = [object[]]@()
            $contradictionValues = [object[]]@()
            if ($candidateMapIsV3) {
                $argumentCandidateValues = [object[]]@(
                    Get-God2DeepProperty $candidate 'ArgumentCandidates')
                $contradictionValues = [object[]]@(
                    Get-God2DeepProperty $candidate 'Contradictions')
            }
            $v3 = [ordered]@{
                Domain = $definition.Domain
                CandidateId = $candidateId
                Module = 'God2_opt.exe'
                RVA = $target
                CallsiteRVAs = $callsiteValues
                ExecutableSection = if ($candidateMapIsV3) {
                    [string](Get-God2DeepProperty $candidate 'ExecutableSection')
                } else { [string](Get-God2DeepProperty $candidate 'ModuleSection') }
                Bytes = $bytes
                ByteMask = $mask
                FunctionBoundary = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'FunctionBoundary'
                } else { [ordered]@{ Status='EVIDENCE_BLOCKED_NOT_VERIFIED'; StartRVA=$null; EndRVA=$null } }
                PrologueEpilogue = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'PrologueEpilogue'
                } else { 'EVIDENCE_BLOCKED_NOT_VERIFIED' }
                CallingConventionCandidate = if ($candidateMapIsV3) {
                    [string](Get-God2DeepProperty $candidate 'CallingConventionCandidate')
                } else { [string](Get-God2DeepProperty $candidate 'CallingConventionState') }
                ArgumentCandidates = $argumentCandidateValues
                ReturnCandidate = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'ReturnCandidate'
                } else { [string](Get-God2DeepProperty $candidate 'ReturnValueLifetimeState') }
                ThreadContextCandidate = if ($candidateMapIsV3) {
                    [string](Get-God2DeepProperty $candidate 'ThreadContextCandidate')
                } else { [string](Get-God2DeepProperty $candidate 'ThreadContextState') }
                ReentrancyRisk = if ($candidateMapIsV3) {
                    [string](Get-God2DeepProperty $candidate 'ReentrancyRisk')
                } else { [string](Get-God2DeepProperty $candidate 'ReentrancyRiskState') }
                ReaderWriterRelation = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'ReaderWriterRelation'
                } else { [string](Get-God2DeepProperty (Get-God2DeepProperty $candidateDomain 'DiscoveryPlan') 'SeedDomain') }
                ConsumerRelation = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'ConsumerRelation'
                } else { [string](Get-God2DeepProperty (Get-God2DeepProperty $candidateDomain 'DiscoveryPlan') 'CausalEvidenceRequired') }
                ControlFlowSummary = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'ControlFlowSummary'
                } else { $null }
                DataFlowSummary = if ($candidateMapIsV3) {
                    Get-God2DeepProperty $candidate 'DataFlowSummary'
                } else { $null }
                StaticScore = if ($candidateMapIsV3) {
                    [double](Get-God2DeepProperty $candidate 'StaticScore')
                } else { [math]::Round(($staticPassed / 14.0) * 100.0, 2) }
                RuntimeObservationCount = [uint64](Get-God2DeepProperty $candidate 'RuntimeObservationCount')
                Contradictions = $contradictionValues
                PromotionGateStatus = if ($candidateMapIsV3) {
                    [string](Get-God2DeepProperty $candidate 'PromotionGateStatus')
                } elseif ($allGatesPassed) { 'PASS' } else { 'EVIDENCE_BLOCKED' }
                PromotionGates = $candidateVerification
                ActivationAllowed = $false
                PromotionEligible = $false
            }
            [void]$candidateV3Rows.Add([pscustomobject](Add-God2AuthorityFields $v3 $candidateAuthority))

            $abi = [ordered]@{
                Domain=$definition.Domain; CandidateId=$candidateId
                CallingConventionCandidate=[string]$v3.CallingConventionCandidate
                ArgumentContractVerified=[bool](Get-God2DeepProperty $candidateVerification 'ArgumentContractVerified')
                ReturnValueLifetimeVerified=[bool](Get-God2DeepProperty $candidateVerification 'ReturnValueLifetimeVerified')
                ThreadContextVerified=[bool](Get-God2DeepProperty $candidateVerification 'ThreadContextVerified')
                ReentrancyRiskVerified=[bool](Get-God2DeepProperty $candidateVerification 'ReentrancyRiskVerified')
                RuntimeObservationCount=0; PromotionEligible=$false
                Status='EVIDENCE_BLOCKED_ABI_UNVERIFIED'
            }
            [void]$abiRows.Add([pscustomobject](Add-God2AuthorityFields $abi $candidateAuthority))
            $thread = [ordered]@{
                Domain=$definition.Domain; CandidateId=$candidateId
                Candidate=[string]$v3.ThreadContextCandidate
                ObservedThreadIds=@(); ObservationCount=0
                PromotionEligible=$false
                Status='EVIDENCE_BLOCKED_THREAD_CONTEXT_UNOBSERVED'
            }
            [void]$threadRows.Add([pscustomobject](Add-God2AuthorityFields $thread $candidateAuthority))
            $reentrancy = [ordered]@{
                Domain=$definition.Domain; CandidateId=$candidateId
                Candidate=[string]$v3.ReentrancyRisk
                NestedEntryCount=0; ConcurrentEntryCount=0; ObservationCount=0
                PromotionEligible=$false
                Status='EVIDENCE_BLOCKED_REENTRANCY_UNOBSERVED'
            }
            [void]$reentrancyRows.Add([pscustomobject](Add-God2AuthorityFields $reentrancy $candidateAuthority))
            $stability = [ordered]@{
                Domain=$definition.Domain; CandidateId=$candidateId
                StableObjectOrContext=[bool](Get-God2DeepProperty $candidateVerification 'StableObjectOrContext')
                AllocationGenerationObserved=$false; AddressReuseObserved=$false
                ReturnLifetimeObserved=$false; ObservationCount=0
                PromotionEligible=$false
                Status='EVIDENCE_BLOCKED_OBJECT_STABILITY_UNOBSERVED'
            }
            [void]$objectStabilityRows.Add([pscustomobject](Add-God2AuthorityFields $stability $candidateAuthority))
            $consumer = [ordered]@{
                Domain=$definition.Domain; CandidateId=$candidateId
                ReaderWriterRelation=[string](Get-God2DeepProperty (Get-God2DeepProperty $candidateDomain 'DiscoveryPlan') 'SeedDomain')
                ConsumerRelation=[string](Get-God2DeepProperty (Get-God2DeepProperty $candidateDomain 'DiscoveryPlan') 'CausalEvidenceRequired')
                StaticCallsiteRVAs=$callsiteValues; StaticTargetRVA=$target
                RuntimeObservation=$false; VerifiedConsumerOrMutation=$false
                PromotionEligible=$false
                Status='EVIDENCE_BLOCKED_CONSUMER_LINK_UNOBSERVED'
            }
            [void]$consumerRows.Add([pscustomobject](Add-God2AuthorityFields $consumer $candidateAuthority))

            foreach ($gate in $script:God2PromotionGates) {
                $passed = [bool](Get-God2DeepProperty $candidateVerification $gate.Gate)
                $ledger = [ordered]@{
                    Domain = $definition.Domain
                    CandidateId = $candidateId
                    Gate = $gate.Gate
                    Category = $gate.Category
                    Passed = $passed
                    Failed = $false
                    EvidenceBlocked = -not $passed
                    EvidenceRefs = @([string](Get-God2DeepProperty $AuthorityRecord 'SourceArtifact'))
                    TestedAtUtc = $GeneratedAtUtc
                    CurrentBuildSHA256 = $CurrentExecutableSHA256
                    ActivationAllowed = $false
                    PromotionEligible = $false
                }
                [void]$promotionRows.Add([pscustomobject](Add-God2AuthorityFields $ledger $candidateAuthority))
            }
        }

        $roleEvents = @($SemanticEvents | Where-Object {
            [string](Get-God2DeepProperty $_ 'EventType') -ceq 'FunctionRoleCandidate' -and
            [string](Get-God2DeepProperty (Get-God2DeepProperty $_ 'Payload') 'Domain') -ceq
                [string]$definition.Domain
        })
        foreach ($roleEvent in $roleEvents) {
            $observation = [ordered]@{
                Domain = $definition.Domain
                Probe = $definition.Probe
                EventId = [string](Get-God2DeepProperty $roleEvent 'EventId')
                Sequence = [uint64](Get-God2DeepProperty $roleEvent 'Sequence')
                RuntimeObservation = $false
                ActiveHook = $false
                Status = 'EVIDENCE_BLOCKED_CANDIDATE_DIAGNOSTIC_ONLY'
                RegisterEvidenceCaptured = $false
                StackEvidenceCaptured = $false
                ReturnLifetimeEvidenceCaptured = $false
                ObjectReuseEvidenceCaptured = $false
                PromotionEligible = $false
            }
            [void]$observationRows.Add([pscustomobject](Add-God2AuthorityFields $observation $AuthorityRecord))
        }
    }

    $capabilityReport = [ordered]@{
        SchemaVersion='god2-probe-capability-matrix-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; DomainCount=25; Rows=@($capabilityRows)
        ImplementedCount=@($capabilityRows | Where-Object CodeImplemented).Count
        ConfirmedContractCount=@($capabilityRows | Where-Object ExactContractStatus -eq 'CONFIRMED').Count
        ActivationAllowedCount=@($capabilityRows | Where-Object ActivationAllowed).Count
        ProducerImplementedCount=@($capabilityRows | Where-Object ProducerImplemented).Count
        DeepFundamentalProducerImplementedCount=@($capabilityRows | Where-Object {
            $_.Index -in @(4,5,6,7,8,9,10,11,12,13,24) -and $_.ProducerImplemented
        }).Count
        GameplayAdapterImplementedCount=@($capabilityRows | Where-Object {
            $_.Index -ge 14 -and $_.Index -le 23 -and $_.ProducerImplemented
        }).Count
    }
    $runtimeReport = [ordered]@{
        SchemaVersion='god2-probe-runtime-evidence-matrix-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; DomainCount=25; Rows=@($runtimeRows)
        CurrentOfficialLiveProducerCount=0
        HistoricalOfficialLiveProducerCount=@($runtimeRows | Where-Object HistoricalOfficialSessionObserved).Count
        CurrentOfficialLiveVerifiedProducerCount=0
        CurrentOfficialLiveDeepProducerCount=@($runtimeRows | Where-Object CurrentOfficialSessionObserved).Count
        CurrentOfficialLiveVerifiedDeepProducerCount=@($runtimeRows | Where-Object {
            $_.CurrentOfficialSessionObserved -and $_.PromotionStatus -eq 'VERIFIED'
        }).Count
        VerifiedDeepDomainCount=@($runtimeRows | Where-Object PromotionStatus -eq 'VERIFIED').Count
        BlockedCount=@($runtimeRows | Where-Object PromotionStatus -eq 'EVIDENCE_BLOCKED').Count
    }

    $verifiedLiveProducerTypes=@('PacketBoundary','ParserRead','SerializerWrite','HandlerInvocation')
    $semanticRows = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $script:God2SemanticEventTypes.Count; ++$index) {
        $eventType = $script:God2SemanticEventTypes[$index]
        $events = @($SemanticEvents | Where-Object {
            [string](Get-God2DeepProperty $_ 'EventType') -ceq $eventType
        })
        $candidateOnly = $eventType -in @('FunctionRoleCandidate','ProbeDiagnostic')
        $currentLive = [bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved') -and
            $events.Count -gt 0
        $verifiedCurrentLive = $currentLive -and $eventType -in $verifiedLiveProducerTypes -and
            @($events | Where-Object {
            [string](Get-God2DeepProperty $_ 'AuthorityHint') -ceq 'VERIFIED'
        }).Count -gt 0
        $deepEvent = $eventType -in @('ObjectAllocated','ObjectDestroyed','ObjectResolved','ObjectLookup',
            'RegistryLocated','RegistryEnumerated','ResourceRead','ResourceDecoded',
            'ResourceDeserialized','StateMutation','TaintSeed','TaintPropagation','ValueFlow',
            'FormulaOperand','FormulaResult','SnapshotObject','SnapshotEdge')
        $eligible = $events.Count -gt 0 -and -not $candidateOnly -and
            [bool](Get-God2DeepProperty $AuthorityRecord 'PromotionEligible')
        $row = [ordered]@{
            Index=$index; EventType=$eventType; SchemaImplemented=$true; FixtureProducer=$true
            NativeSelfTestProducer=$true
            RuntimeProducerImplemented=$true
            CurrentOfficialLiveProducer=$currentLive
            CurrentOfficialLiveVerifiedProducer=$verifiedCurrentLive
            CurrentOfficialLiveDeepProducer=($currentLive -and $deepEvent)
            CurrentOfficialLiveVerifiedDeepProducer=($verifiedCurrentLive -and $deepEvent -and $eligible)
            HistoricalOfficialLiveProducer=([bool](Get-God2DeepProperty $AuthorityRecord 'HistoricalEvidenceReused') -and $events.Count -gt 0)
            RuntimeEventCount=$events.Count; PromotionEligible=[bool]$eligible
            BlockReason=if($eligible){$null}elseif($candidateOnly){'NoPromotionEventType'}else{'NoPromotionEligibleLiveProducerEvidence'}
        }
        [void]$semanticRows.Add([pscustomobject](Add-God2AuthorityFields $row $AuthorityRecord))
    }
    $semanticAllCount=@($semanticRows | Where-Object CurrentOfficialLiveProducer).Count
    $semanticVerifiedCount=@($semanticRows | Where-Object CurrentOfficialLiveVerifiedProducer).Count
    $semanticDeepCount=@($semanticRows | Where-Object CurrentOfficialLiveDeepProducer).Count
    $semanticVerifiedDeepCount=@($semanticRows | Where-Object CurrentOfficialLiveVerifiedDeepProducer).Count
    $semanticReport = [ordered]@{
        SchemaVersion='god2-semantic-event-runtime-matrix-v2'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; EventTypeCount=25; Rows=@($semanticRows)
        CurrentOfficialLiveProducerCount=$semanticAllCount
        CurrentOfficialLiveVerifiedProducerCount=$semanticVerifiedCount
        CurrentOfficialLiveDeepProducerCount=$semanticDeepCount
        CurrentOfficialLiveVerifiedDeepProducerCount=$semanticVerifiedDeepCount
        ProducerMetricConsistency=($semanticVerifiedCount -le $semanticAllCount -and
            $semanticDeepCount -le $semanticAllCount -and
            $semanticVerifiedDeepCount -le $semanticDeepCount)
        HistoricalOfficialLiveProducerCount=@($semanticRows | Where-Object HistoricalOfficialLiveProducer).Count
        PromotionEligibleCount=@($semanticRows | Where-Object PromotionEligible).Count
    }
    $runtimeReport.CurrentOfficialLiveProducerCount=$semanticReport.CurrentOfficialLiveProducerCount
    $runtimeReport.CurrentOfficialLiveVerifiedProducerCount=$semanticReport.CurrentOfficialLiveVerifiedProducerCount
    $runtimeReport.CurrentOfficialLiveDeepProducerCount=$semanticReport.CurrentOfficialLiveDeepProducerCount
    $runtimeReport.CurrentOfficialLiveVerifiedDeepProducerCount=$semanticReport.CurrentOfficialLiveVerifiedDeepProducerCount
    $runtimeReport.ProducerMetricConsistency=$semanticReport.ProducerMetricConsistency
    $eventAuthorityRows = [Collections.Generic.List[object]]::new()
    foreach ($event in $SemanticEvents) {
        $eventType = [string](Get-God2DeepProperty $event 'EventType')
        $payload = Get-God2DeepProperty $event 'Payload'
        $eventFixtureOnly = [bool](Get-God2DeepProperty $payload 'FixtureOnly')
        $eventPromotionEligible = -not $eventFixtureOnly -and
            $eventType -notin @('FunctionRoleCandidate','ProbeDiagnostic') -and
            [string](Get-God2DeepProperty $event 'AuthorityHint') -ceq 'VERIFIED' -and
            [bool](Get-God2DeepProperty $AuthorityRecord 'PromotionEligible')
        $eventAuthority = [ordered]@{
            EventId=[string](Get-God2DeepProperty $event 'EventId')
            EventType=$eventType
            Sequence=Get-God2DeepProperty $event 'Sequence'
            RuntimeObservation=(-not $eventFixtureOnly -and
                ([bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved') -or
                 [bool](Get-God2DeepProperty $AuthorityRecord 'HistoricalEvidenceReused')))
            FixtureOnly=$eventFixtureOnly
            PromotionEligible=[bool]$eventPromotionEligible
        }
        [void]$eventAuthorityRows.Add([pscustomobject](
            Add-God2AuthorityFields $eventAuthority $AuthorityRecord))
    }
    $candidateMapV3 = [ordered]@{
        SchemaVersion='god2-deep-probe-candidate-map-v3'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$candidateAuthority; TargetExecutable='God2_opt.exe'; Architecture='x86'
        TargetSHA256=$candidateTargetSha256
        DiscoveryPolicy=if($candidateMapIsV3){
            [string](Get-God2DeepProperty $CandidateMap 'DiscoveryPolicy')
        }else{'LegacyV2BoundedExecutableDirectCalls;NoWritableMemoryScan;NoAutomaticActivation'}
        AnalysisBounds=if($candidateMapIsV3){Get-God2DeepProperty $CandidateMap 'AnalysisBounds'}else{$null}
        PE=if($candidateMapIsV3){Get-God2DeepProperty $CandidateMap 'PE'}else{$null}
        SeedFunctions=if($candidateMapIsV3){@(Get-God2DeepProperty $CandidateMap 'SeedFunctions')}else{@()}
        CandidateCount=$candidateV3Rows.Count; Candidates=@($candidateV3Rows)
        ActivationAllowedCount=0; PromotionEligibleCount=0
        Status='EVIDENCE_BLOCKED_STATIC_HYPOTHESES_REQUIRE_CONTRACT_ACQUISITION'
    }

    $acquisitionReports = [ordered]@{
        Abi=[ordered]@{
            SchemaVersion='god2-candidate-abi-report-v1'; GeneratedAtUtc=$GeneratedAtUtc
            Authority=$AuthorityRecord; CandidateCount=$abiRows.Count; VerifiedCount=0
            Status='EVIDENCE_BLOCKED_ABI_OBSERVATIONS_REQUIRED'; Rows=@($abiRows)
        }
        Thread=[ordered]@{
            SchemaVersion='god2-candidate-thread-context-v1'; GeneratedAtUtc=$GeneratedAtUtc
            Authority=$AuthorityRecord; CandidateCount=$threadRows.Count; VerifiedCount=0
            Status='EVIDENCE_BLOCKED_THREAD_CONTEXT_OBSERVATIONS_REQUIRED'; Rows=@($threadRows)
        }
        Reentrancy=[ordered]@{
            SchemaVersion='god2-candidate-reentrancy-report-v1'; GeneratedAtUtc=$GeneratedAtUtc
            Authority=$AuthorityRecord; CandidateCount=$reentrancyRows.Count; VerifiedCount=0
            Status='EVIDENCE_BLOCKED_REENTRANCY_OBSERVATIONS_REQUIRED'; Rows=@($reentrancyRows)
        }
        ObjectStability=[ordered]@{
            SchemaVersion='god2-candidate-object-stability-v1'; GeneratedAtUtc=$GeneratedAtUtc
            Authority=$AuthorityRecord; CandidateCount=$objectStabilityRows.Count; VerifiedCount=0
            Status='EVIDENCE_BLOCKED_OBJECT_STABILITY_OBSERVATIONS_REQUIRED'; Rows=@($objectStabilityRows)
        }
    }
    $promotionResult = [ordered]@{
        SchemaVersion='god2-candidate-promotion-result-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; CandidateCount=$candidateV3Rows.Count
        GateCountPerCandidate=14; LedgerRowCount=$promotionRows.Count
        PassedGateRowCount=@($promotionRows | Where-Object Passed).Count
        FailedGateRowCount=@($promotionRows | Where-Object Failed).Count
        EvidenceBlockedGateRowCount=@($promotionRows | Where-Object EvidenceBlocked).Count
        ActivationAllowedCount=@($candidateV3Rows | Where-Object ActivationAllowed).Count
        Status=if(@($candidateV3Rows | Where-Object ActivationAllowed).Count -eq 0) {
            'EVIDENCE_BLOCKED_NO_DEEP_CANDIDATE_PROMOTED'
        } else { 'PROMOTION_CANDIDATES_AVAILABLE' }
    }
    $exactIdentity = [ordered]@{
        SchemaVersion='god2-exact-target-identity-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; Executable='God2_opt.exe'; Architecture='x86'; Version='1.0.0.1'
        ExpectedSHA256=$candidateTargetSha256
        ObservedSHA256=[string](Get-God2DeepProperty $AuthorityRecord 'TargetSHA256')
        ExactIdentity=([string](Get-God2DeepProperty $AuthorityRecord 'TargetSHA256') -ceq
            $candidateTargetSha256)
        CurrentSessionObserved=[bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved')
        Status=if([bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved')) {
            'CURRENT_EXACT_TARGET_IDENTITY'
        } else { 'HISTORICAL_EXACT_TARGET_IDENTITY_CURRENT_SESSION_NOT_OBSERVED' }
    }
    $contentRows = @($runtimeRows | Where-Object {
        $domain = [string](Get-God2DeepProperty $_ 'Domain')
        @($script:God2DeepDomainDefinitions | Where-Object {
            [string]$_.Domain -ceq $domain -and [string]$_.Group -ceq 'Content'
        }).Count -eq 1
    })
    $contentSummary = [ordered]@{
        SchemaVersion='god2-content-domain-summary-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; DomainCount=$contentRows.Count
        AdapterImplementedCount=@($capabilityRows | Where-Object {
            $_.Index -ge 14 -and $_.Index -le 23 -and $_.ProducerImplemented
        }).Count
        LiveObservedCount=@($contentRows | Where-Object { $_.RuntimeEventCount -gt 0 }).Count
        VerifiedCount=@($contentRows | Where-Object PromotionStatus -eq 'VERIFIED').Count
        BlockedCount=@($contentRows | Where-Object PromotionStatus -eq 'EVIDENCE_BLOCKED').Count
        Rows=$contentRows
    }
    $verifiedDeepCount = @($runtimeRows | Where-Object {
        $_.Index -ge 4 -and $_.PromotionStatus -ceq 'VERIFIED'
    }).Count
    $recoveryReadiness = [ordered]@{
        SchemaVersion='god2-recovery-readiness-v1'; GeneratedAtUtc=$GeneratedAtUtc
        Authority=$AuthorityRecord; VerifiedDeepDomainCount=$verifiedDeepCount
        CurrentOfficialSessionObserved=[bool](Get-God2DeepProperty $AuthorityRecord 'CurrentSessionObserved')
        SharedRingProductionHealthBound=$false; SemanticEvidenceIncomplete=$true
        ServerReady=$false; DatabaseReady=$false
        Status='EVIDENCE_BLOCKED_CURRENT_DEEP_RUNTIME_AND_RING_HEALTH_REQUIRED'
        MissingEvidence=@('CurrentOfficialClientLive','VerifiedDeepRuntimeProducer','SharedRingProductionHealth')
    }

    $paths = [ordered]@{
        Capability = Join-Path $OutputRoot 'probe-capability-matrix.json'
        Runtime = Join-Path $OutputRoot 'probe-runtime-evidence-matrix.json'
        Semantic = Join-Path $OutputRoot 'semantic-event-runtime-matrix-v2.json'
        CandidateMapV3 = Join-Path $OutputRoot 'deep-probe-candidate-map-v3.json'
        PromotionLedger = Join-Path $OutputRoot 'deep-probe-promotion-ledger.jsonl'
        RuntimeObservations = Join-Path $OutputRoot 'candidate-runtime-observations.jsonl'
        SemanticEventAuthority = Join-Path $OutputRoot 'semantic-event-authority.jsonl'
        CandidateAbi = Join-Path $OutputRoot 'candidate-abi-report.json'
        CandidateThread = Join-Path $OutputRoot 'candidate-thread-context.json'
        CandidateReentrancy = Join-Path $OutputRoot 'candidate-reentrancy-report.json'
        CandidateObjectStability = Join-Path $OutputRoot 'candidate-object-stability.json'
        CandidateConsumerLinks = Join-Path $OutputRoot 'candidate-consumer-links.jsonl'
        CandidatePromotionResult = Join-Path $OutputRoot 'candidate-promotion-result.json'
        ExactTargetIdentity = Join-Path $OutputRoot 'exact-target-identity.json'
        ContentDomainSummary = Join-Path $OutputRoot 'content-domain-summary.json'
        RecoveryReadiness = Join-Path $OutputRoot 'recovery-readiness.json'
    }
    Write-God2DeepJson $paths.Capability $capabilityReport
    Write-God2DeepJson $paths.Runtime $runtimeReport
    Write-God2DeepJson $paths.Semantic $semanticReport
    Write-God2DeepJson $paths.CandidateMapV3 $candidateMapV3
    Write-God2DeepJsonLines $paths.PromotionLedger @($promotionRows)
    Write-God2DeepJsonLines $paths.RuntimeObservations @($observationRows)
    Write-God2DeepJsonLines $paths.SemanticEventAuthority @($eventAuthorityRows)
    Write-God2DeepJson $paths.CandidateAbi $acquisitionReports.Abi
    Write-God2DeepJson $paths.CandidateThread $acquisitionReports.Thread
    Write-God2DeepJson $paths.CandidateReentrancy $acquisitionReports.Reentrancy
    Write-God2DeepJson $paths.CandidateObjectStability $acquisitionReports.ObjectStability
    Write-God2DeepJsonLines $paths.CandidateConsumerLinks @($consumerRows)
    Write-God2DeepJson $paths.CandidatePromotionResult $promotionResult
    Write-God2DeepJson $paths.ExactTargetIdentity $exactIdentity
    Write-God2DeepJson $paths.ContentDomainSummary $contentSummary
    Write-God2DeepJson $paths.RecoveryReadiness $recoveryReadiness

    $hashes = [ordered]@{}
    foreach ($property in $paths.GetEnumerator()) {
        $hashes[$property.Key] = (Get-FileHash -LiteralPath $property.Value -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{
        Paths=$paths; SHA256=$hashes; Capability=$capabilityReport; Runtime=$runtimeReport
        Semantic=$semanticReport; CandidateMapV3=$candidateMapV3
        PromotionLedgerCount=$promotionRows.Count; RuntimeObservationCount=$observationRows.Count
        PromotionResult=$promotionResult; ExactTargetIdentity=$exactIdentity
        ContentDomainSummary=$contentSummary; RecoveryReadiness=$recoveryReadiness
        CurrentExecutableSHA256=$CurrentExecutableSHA256
    }
}
