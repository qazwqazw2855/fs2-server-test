[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputRoot,
    [string]$ReleaseExecutable = 'Artifacts/Ultimate/release/God2SemanticRecoveryEngine.exe',
    [string]$ProbePayload = 'tools/God2.PacketCapture/payload/x86/God2PacketCaptureProbe.dll',
    [string]$InjectorPayload = 'tools/God2.PacketCapture/payload/x86/God2PacketCaptureInjector.exe',
    [string]$NetworkSelfTestSummary = 'Artifacts/ClientInstrumentation/LoginTrial/deep-probe-v2-network-selftest/analysis/instrumentation-selftest-summary.json',
    [string]$ProductSelfTestSummary = 'Artifacts/ClientInstrumentation/LoginTrial/deep-probe-v2-product-selftest/analysis/product-injector-selftest-summary.json',
    [string]$EvidenceFixtureReportJson = '',
    [string]$RestoreFaultLifecycleReport = '',
    [string]$OfficialRuntimeEvidence = '',
    [string]$DeepStaticCandidateMapV3 = '',
    [string]$GuiSmokeEvidence = '',
    [switch]$Final,
    [switch]$ContractSelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
. (Join-Path $PSScriptRoot '..\ultimate\EvidenceAuthority.ps1')
. (Join-Path $PSScriptRoot '..\ultimate\DeepRecoveryEvidence.ps1')
. (Join-Path $PSScriptRoot '..\ultimate\SemanticEventJson.ps1')

$script:ExpectedProbeDomains = @(
    [ordered]@{ Domain = 'NetworkProbe'; Probe = 'WinsockTransport' },
    [ordered]@{ Domain = 'ParserProbe'; Probe = 'PacketDecode.FrameBoundary' },
    [ordered]@{ Domain = 'SerializerProbe'; Probe = 'OutboundEnqueue.FrameBuilder' },
    [ordered]@{ Domain = 'HandlerProbe'; Probe = 'Battle.HandlerRecordLength' },
    [ordered]@{ Domain = 'ObjectProbe'; Probe = 'ObjectResolver' },
    [ordered]@{ Domain = 'AllocationProbe'; Probe = 'AllocationProbe' },
    [ordered]@{ Domain = 'VTableProbe'; Probe = 'VTableProbe' },
    [ordered]@{ Domain = 'FactoryProbe'; Probe = 'FactoryProbe' },
    [ordered]@{ Domain = 'ManagerLookupProbe'; Probe = 'ManagerLookupProbe' },
    [ordered]@{ Domain = 'RegistryProbe'; Probe = 'RegistryProbe' },
    [ordered]@{ Domain = 'ResourceDecodeProbe'; Probe = 'ResourceDecodeProbe' },
    [ordered]@{ Domain = 'MutationProbe'; Probe = 'MutationProbe' },
    [ordered]@{ Domain = 'TaintSeedProbe'; Probe = 'TaintSeedProbe' },
    [ordered]@{ Domain = 'FormulaOperandProbe'; Probe = 'FormulaOperandProbe' },
    [ordered]@{ Domain = 'QuestProbe'; Probe = 'QuestProbe' },
    [ordered]@{ Domain = 'MapProbe'; Probe = 'MapProbe' },
    [ordered]@{ Domain = 'PortalProbe'; Probe = 'PortalProbe' },
    [ordered]@{ Domain = 'NpcProbe'; Probe = 'NPCProbe' },
    [ordered]@{ Domain = 'MonsterProbe'; Probe = 'MonsterProbe' },
    [ordered]@{ Domain = 'BattleProbe'; Probe = 'BattleProbe' },
    [ordered]@{ Domain = 'InventoryProbe'; Probe = 'InventoryProbe' },
    [ordered]@{ Domain = 'ItemProbe'; Probe = 'ItemProbe' },
    [ordered]@{ Domain = 'SkillProbe'; Probe = 'SkillProbe' },
    [ordered]@{ Domain = 'PetMountProbe'; Probe = 'PetMountProbe' },
    [ordered]@{ Domain = 'SnapshotProbe'; Probe = 'SnapshotProbe' }
)

$script:ExpectedNativeProbeDomains = @(
    'Network','Parser','Serializer','Handler','Object','Allocation','VTable','Factory',
    'ManagerLookup','Registry','ResourceDecode','Mutation','TaintSeed','FormulaOperand',
    'Quest','Map','Portal','NPC','Monster','Battle','Inventory','Item','Skill','Pet/Mount',
    'Snapshot'
)

$script:ExpectedCandidateGates = @(
    [ordered]@{ Gate = 'ExactTargetIdentity'; Category = 'Promotion' },
    [ordered]@{ Gate = 'ExecutableSection'; Category = 'Promotion' },
    [ordered]@{ Gate = 'ExactCandidateBytes'; Category = 'Promotion' },
    [ordered]@{ Gate = 'CallingConventionVerified'; Category = 'Promotion' },
    [ordered]@{ Gate = 'TypedRuntimeEvidence'; Category = 'Promotion' },
    [ordered]@{ Gate = 'RepeatedCausalObservation'; Category = 'Promotion' },
    [ordered]@{ Gate = 'ContradictionsResolved'; Category = 'Promotion' },
    [ordered]@{ Gate = 'StableObjectOrContext'; Category = 'Promotion' },
    [ordered]@{ Gate = 'VerifiedConsumerOrMutation'; Category = 'Promotion' },
    [ordered]@{ Gate = 'SensitiveMaskContract'; Category = 'Promotion' },
    [ordered]@{ Gate = 'ArgumentContractVerified'; Category = 'AbiSafety' },
    [ordered]@{ Gate = 'ReturnValueLifetimeVerified'; Category = 'AbiSafety' },
    [ordered]@{ Gate = 'ThreadContextVerified'; Category = 'AbiSafety' },
    [ordered]@{ Gate = 'ReentrancyRiskVerified'; Category = 'AbiSafety' }
)

$script:ExpectedUnloadNeutralBridgeProperties = @(
    'SchemaId','SchemaVersion','AbiVersion','ArenaCount','MaxRxPages','ActualRxPages','StatePageCount','CodeBytes',
    'CodeSHA256','CodeProtection','StateProtection','WritableExecutablePageCount','Capacity','HighWater','CapacityDrops','Phase',
    'Generation','CaptureObserverRetired','ObserverPointerNull','ObserverRundown','PreOriginalRundown','BridgeFrames','PendingApplicationCallbacks','DllPointersRemaining',
    'DllPointerCount','BridgeNoDllPointers','PersistentOriginalPointerCount','InvalidPersistentOriginalPointerCount','ExactPersistentOriginalPointerCount','PersistentOriginalIdentityMismatchCount','FirstPersistentOriginalIdentityMismatch','MissingRequiredPersistentOriginalCount',
    'FirstMissingRequiredPersistentOriginal','SealedEntryPointerCount','InvalidEntryPointerCount','ExactEntryPointerCount','EntryIdentityMismatchCount','FirstEntryIdentityMismatch','SealedCompletionThunkCount','MissingCompletionThunkCount',
    'InvalidCompletionThunkCount','ExactCompletionThunkCount','CompletionThunkIdentityMismatchCount','FirstCompletionThunkIdentityMismatch','ActiveApplicationCallbackPointerCount','InvalidApplicationCallbackPointerCount','IatRestored','HotpatchRestored',
    'State','Retained','ArenaReused','ObserverArmSelfTestPassed','ObserverDisarmSelfTestPassed','ObserverAcquireCount','ObserverReleaseCount','ObserverCallsAfterDisarm',
    'ArenaValidationFailures','PointerIdentityFixturePassed','ExactPatchTransactionFixturePassed','ExactPatchTransactionFixtureCount','ExactPatchTransactionPassedCount','ExactPatchTransactionUncertainCount','ExactPatchRestoredExactCount','ExactPatchForeignPreservedCount',
    'ExactPatchOwnershipLostCount','ExactPatchOwnershipUnsafeCount','ExactPatchFirstRestoredExactSite','ExactPatchFirstForeignPreservedSite','ExactPatchFirstOwnershipUnsafeSite','HotpatchPrefixForeignPreservedCount','ExactPatchThirdPartyNoClobberFixturePassed','ExactPatchThirdPartyDirectNoClobberPassed',
    'ExactPatchThirdPartyHotpatchEntryNoClobberPassed','ExactPatchThirdPartyHotpatchPrefixNoClobberPassed','ExactPatchThirdPartyHotpatchCombinedNoClobberPassed','ExactPatchThirdPartyProtectionRestoredPassed','ExactPatchThirdPartyUnsafeRetainedPassed','HookLockContentionFixturePassed','HookLockContentionFixtureCount','HookLockContentionFixturePassedCount',
    'HookLockContentionMaxElapsedMicros','RingLockContentionDrops','PendingRecvLockContentionDrops','PrivacyLockContentionDrops','LocalPlayerLockContentionDrops','AbiFixtureCount','AbiFixturePassedCount','ObserverTamperFixturePassed',
    'ObserverFaultFixturePassed','CrossStageFaultFixturePassed','CrossStageUnexpectedPostCount','CompletionRouteFixturePassed','CompletionDuplicateBeforeReusePassed','CompletionDeterministicAbaPassed','CompletionAbaBarrierReached','CompletionAbaOldToken',
    'CompletionAbaNewToken','CompletionAbaOldCasRejected','CompletionAbaNewRouteIntact','CompletionAbaApplicationCallbacks','RetainedOwnerUnloadGuardPassed','WSARecvCommitStageFixturePassed','WSARecvCommitStageFixtureCount','WSARecvCommitStagePassedCount',
    'WSARecvCommitStagePassedMask','WSARecvCommitStageCallbacks','WSARecvCommitStagePrivatePendingFinal','WSARecvCommitStageLiveStateUntouched','WSARecvSameIdentityIsolationPassed','WSARecvNoCallbackIdentityIsolationPassed','WSARecvPostRepostIsolationPassed','WSARecvPostFaultRepostIsolationPassed',
    'WSARecvFinalPurgeRacePassed','WSARecvFinalPurgeLateRegistrationObserved','WSARecvFinalPurgePurgedCount','PendingNeutralRetirements','PendingPurgedAtDisarm','BridgeCoverageResults','DisabledApiResults'
)
$script:ExpectedSensitiveOutboundPrivacyProperties = @(
    'SchemaId','SchemaVersion','Capacity','FixtureCount','FixturePassedCount','FixturePassedMask','FixturePassed','PrivateNonAliasing',
    'AllContextPointersNonAliasing','PointerAliasNegativePassed','LiveSnapshotUnchanged','LiveStateUntouched','LiveActiveTransactions','LivePendingTransactions','CreatedTransactions','CompletedTransactions',
    'RetryCount','PartialCount','CrossThreadCompletionCount','AmbiguousCount','UnverifiableCount','CapacityDrops','EpochExhaustedCount','StaleCompletionCount',
    'OpaqueFailClosed','PendingAtDisarm','PurgedAtDisarm','EarlyCompletionRedactionCarryPassed','EarlyCompletionPrivateSinkNonAliasing','EarlyCompletionSensitivePayloadSuppressed','EarlyCompletionCapturedLength','EarlyCompletionPayloadBytesPersisted',
    'TestOnlyLiveIntegration','FixtureResults'
)
$script:ExpectedSensitiveOutboundIntegrationProperties = @(
    'SchemaId','SchemaVersion','Invoked','Passed','Stage','LastError','TestOnlyExactFixture','OfficialRuntime',
    'SerializerAdapterObserved','SerializerEntryObserved','SendBridgeObserved','WSASendBridgeObserved','RetryObserved','ActualPartialObserved','DeterministicPartialFixturePassed','PendingObserved',
    'CompletionObserved','CreatedDelta','CompletedDelta','RetryDelta','PartialDelta','SinkAdmissionObserved','SuccessfulRetrySinkAdmitted','SuccessfulRetrySinkSequence',
    'SuccessfulRetryCapturedLength','SuccessfulRetrySensitivePayloadSuppressed','WsaSendPendingSinkAdmitted','WsaSendPendingSinkSequence','WsaSendPendingCapturedLength','WsaSendPendingSensitivePayloadSuppressed','SuccessfulRetrySameShapeDecoyRejected','WsaSendPendingSameShapeDecoyRejected',
    'SuccessfulRetrySameShapeDecoySequence','WsaSendPendingSameShapeDecoySequence','DecoyCannotSubstituteDroppedReal','SuccessfulRetryIdentity','WsaSendPendingIdentity','PendingFinal','ActiveFinal'
)
$script:ExpectedSensitiveOutboundIdentityProperties = @(
    'Matched','ExpectedThreadId','ObservedThreadId','ExpectedSocket','ObservedSocket','ExpectedTransactionId','ObservedTransactionId',
    'ExpectedProducerGuard','ObservedProducerGuard','ExpectedFrameFingerprint','ObservedFrameFingerprint','ExpectedFirstBuffer','ObservedFirstBuffer',
    'ExpectedOverlapped','ObservedOverlapped','ExpectedBridgeGeneration','ObservedBridgeGeneration','ExpectedSegmentCount','ObservedSegmentCount',
    'ExpectedCallerReturnAddress','ObservedCallerReturnAddress'
)
$script:ExpectedBridgeCoverageProperties = @(
    'Index','Api','InstalledToBridge','OriginalBound','PreObserverObserved','PostObserverObserved','BlockingFrameOutsideDll','ResultPreserved',
    'LastErrorPreserved','CallbackTailTransferRequired','CallbackTailTransfer','CallbackForwardedExactlyOnce'
)

$script:ExpectedSemanticEventTypes = @(
    [ordered]@{ EventType = 'PacketBoundary'; DispatchSink = 'ProtocolRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ParserRead'; DispatchSink = 'ProtocolRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'SerializerWrite'; DispatchSink = 'ProtocolRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'HandlerInvocation'; DispatchSink = 'ProtocolRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'HandlerArgument'; DispatchSink = 'ProtocolRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ObjectAllocated'; DispatchSink = 'ObjectRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ObjectDestroyed'; DispatchSink = 'ObjectRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ObjectResolved'; DispatchSink = 'ObjectRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ObjectLookup'; DispatchSink = 'ObjectRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'RegistryLocated'; DispatchSink = 'RegistryRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'RegistryEnumerated'; DispatchSink = 'RegistryRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ResourceRead'; DispatchSink = 'ResourceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ResourceDecoded'; DispatchSink = 'ResourceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ResourceDeserialized'; DispatchSink = 'ResourceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'StateMutation'; DispatchSink = 'MutationRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'TaintSeed'; DispatchSink = 'ValueProvenanceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'TaintPropagation'; DispatchSink = 'ValueProvenanceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ValueFlow'; DispatchSink = 'ValueProvenanceRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'FormulaOperand'; DispatchSink = 'FormulaRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'FormulaResult'; DispatchSink = 'FormulaRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'FunctionRoleCandidate'; DispatchSink = 'CandidateLedger'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'UIAnchor'; DispatchSink = 'ContentRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'SnapshotObject'; DispatchSink = 'SnapshotRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'SnapshotEdge'; DispatchSink = 'SnapshotRecovery'; RuntimeProducer = $true },
    [ordered]@{ EventType = 'ProbeDiagnostic'; DispatchSink = 'DiagnosticLedger'; RuntimeProducer = $true }
)

$script:ExpectedGuiSourceSha256 = 'A926CEDF8A7E4A7CCFE5285C40229622CE813BB1B46A196EF476430F2BAD13C7'
$script:ExpectedClientSha256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$script:ExpectedMainSourceSha256 = 'A538AE061331AFA96076708AF35E28865C59AAD049857A79CCF1EA4EDC11F409'
$script:ExpectedEnhancedCaptureStatusSchemaSha256 = '85C19A1D6963FC6CD99DA54DAD21325EAD7E554873B3928EB22AF8D0B16B7F01'
$script:ExpectedBridgeAwareStatusV3SchemaSha256 = '17A5F64EFC2418BA34B6FA46D97812E6BD84F12191671A4401B3F7D77CE032DD'
$script:ExpectedEvidenceSourceSha256 = '030C27FF76FDBF0F262087DD147C166CFFE037E72AE2CDD9F7C7EA4FCDBD40AA'
$script:ExpectedSharedHealthAvailabilitySchemaSha256 = 'CE259621B2E7C4A6A74FB6E5C0BBC417A6C55BE61375AAE51E01539CAE14F2B0'
$script:ExpectedCandidateMapAvailabilitySchemaSha256 = '2C6E012269AAD9C7D4BF05B512568169DDB9C54711DD4775E85FA6FDFEC70B33'

function Get-JsonPropertyValue([object]$Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-JsonBooleanValue([object]$Value, [bool]$Expected) {
    return $null -ne $Value -and $Value -is [bool] -and $Value -eq $Expected
}

function Test-JsonIntegerValue([object]$Value) {
    return $null -ne $Value -and (
        $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64])
}

function Test-RequiredJsonProperties([object]$Object, [string[]]$Names) {
    if ($null -eq $Object) { return $false }
    foreach ($name in $Names) {
        if ($null -eq $Object.PSObject.Properties[$name]) { return $false }
    }
    return $true
}

function Test-ExactJsonProperties([object]$Object, [string[]]$Names) {
    if (-not (Test-RequiredJsonProperties $Object $Names)) { return $false }
    $actualNames = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actualNames.Count -ne $Names.Count) { return $false }
    return @($actualNames | Where-Object { $Names -cnotcontains $_ }).Count -eq 0
}

function Test-JsonDeepEqual([object]$Left, [object]$Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $null -eq $Left -and $null -eq $Right }
    return ($Left | ConvertTo-Json -Depth 40 -Compress) -ceq
        ($Right | ConvertTo-Json -Depth 40 -Compress)
}

function Skip-StrictJsonWhitespace([string]$Text, [ref]$Index) {
    while ($Index.Value -lt $Text.Length -and
        ($Text[$Index.Value] -eq ' ' -or $Text[$Index.Value] -eq "`t" -or
         $Text[$Index.Value] -eq "`r" -or $Text[$Index.Value] -eq "`n")) {
        $Index.Value++
    }
}

function Read-StrictJsonString([string]$Text, [ref]$Index) {
    if ($Index.Value -ge $Text.Length -or $Text[$Index.Value] -ne '"') {
        throw [FormatException]::new("JSON string expected at offset $($Index.Value).")
    }
    $start = $Index.Value
    $Index.Value++
    while ($Index.Value -lt $Text.Length) {
        $character = $Text[$Index.Value]
        if ($character -eq '"') {
            $Index.Value++
            $token = $Text.Substring($start, $Index.Value - $start)
            try { return ($token | ConvertFrom-Json) }
            catch { throw [FormatException]::new("Invalid JSON string at offset $start.") }
        }
        if ([int][char]$character -lt 0x20) {
            throw [FormatException]::new("Unescaped control character in JSON string at offset $($Index.Value).")
        }
        if ($character -eq '\') {
            $Index.Value++
            if ($Index.Value -ge $Text.Length) {
                throw [FormatException]::new("Incomplete JSON escape at offset $($Index.Value).")
            }
            $escape = $Text[$Index.Value]
            if ('"\/bfnrt'.IndexOf($escape) -lt 0) {
                if ($escape -ne 'u' -or $Index.Value + 4 -ge $Text.Length -or
                    $Text.Substring($Index.Value + 1, 4) -cnotmatch '^[0-9A-Fa-f]{4}$') {
                    throw [FormatException]::new("Invalid JSON escape at offset $($Index.Value).")
                }
                $Index.Value += 4
            }
        }
        $Index.Value++
    }
    throw [FormatException]::new("Unterminated JSON string at offset $start.")
}

function Read-StrictJsonValue([string]$Text, [ref]$Index) {
    Skip-StrictJsonWhitespace $Text $Index
    if ($Index.Value -ge $Text.Length) {
        throw [FormatException]::new('Unexpected end of JSON input.')
    }
    $character = $Text[$Index.Value]
    if ($character -eq '{') {
        $Index.Value++
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        Skip-StrictJsonWhitespace $Text $Index
        if ($Index.Value -lt $Text.Length -and $Text[$Index.Value] -eq '}') {
            $Index.Value++
            return
        }
        while ($true) {
            Skip-StrictJsonWhitespace $Text $Index
            $key = Read-StrictJsonString $Text $Index
            if (-not $keys.Add([string]$key)) {
                throw [FormatException]::new("Duplicate JSON object key '$key'.")
            }
            Skip-StrictJsonWhitespace $Text $Index
            if ($Index.Value -ge $Text.Length -or $Text[$Index.Value] -ne ':') {
                throw [FormatException]::new("JSON colon expected at offset $($Index.Value).")
            }
            $Index.Value++
            Read-StrictJsonValue $Text $Index
            Skip-StrictJsonWhitespace $Text $Index
            if ($Index.Value -ge $Text.Length) {
                throw [FormatException]::new('Unterminated JSON object.')
            }
            if ($Text[$Index.Value] -eq '}') {
                $Index.Value++
                return
            }
            if ($Text[$Index.Value] -ne ',') {
                throw [FormatException]::new("JSON comma expected at offset $($Index.Value).")
            }
            $Index.Value++
        }
    }
    if ($character -eq '[') {
        $Index.Value++
        Skip-StrictJsonWhitespace $Text $Index
        if ($Index.Value -lt $Text.Length -and $Text[$Index.Value] -eq ']') {
            $Index.Value++
            return
        }
        while ($true) {
            Read-StrictJsonValue $Text $Index
            Skip-StrictJsonWhitespace $Text $Index
            if ($Index.Value -ge $Text.Length) {
                throw [FormatException]::new('Unterminated JSON array.')
            }
            if ($Text[$Index.Value] -eq ']') {
                $Index.Value++
                return
            }
            if ($Text[$Index.Value] -ne ',') {
                throw [FormatException]::new("JSON comma expected at offset $($Index.Value).")
            }
            $Index.Value++
        }
    }
    if ($character -eq '"') {
        [void](Read-StrictJsonString $Text $Index)
        return
    }
    foreach ($literal in @('true','false','null')) {
        if ($Text.Length - $Index.Value -ge $literal.Length -and
            $Text.Substring($Index.Value, $literal.Length) -ceq $literal) {
            $Index.Value += $literal.Length
            return
        }
    }
    $number = [regex]::Match($Text.Substring($Index.Value),
        '^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?')
    if (-not $number.Success) {
        throw [FormatException]::new("Invalid JSON value at offset $($Index.Value).")
    }
    $Index.Value += $number.Length
}

function Test-StrictJsonTextContract([string]$Text) {
    $issues = [Collections.Generic.List[string]]::new()
    try {
        $index = 0
        Read-StrictJsonValue $Text ([ref]$index)
        Skip-StrictJsonWhitespace $Text ([ref]$index)
        if ($index -ne $Text.Length) {
            throw [FormatException]::new("Trailing JSON content at offset $index.")
        }
    } catch {
        $issues.Add($_.Exception.Message)
    }
    return Complete-ContractValidation $issues
}

function Test-InjectorResultV2Contract([object]$Record, [string]$ExpectedKind) {
    $issues = [Collections.Generic.List[string]]::new()
    $required = @(
        'schemaVersion','status','code','injectionAttempted','moduleWasEverLoaded',
        'moduleLoadStateVerified','moduleSnapshotVerified','moduleAbsent','targetProcessExited',
        'targetIdentityVerified','probeReady','stopSucceeded','unloadSafe','moduleUnloaded',
        'moduleResidentInactive','strictUnloadVerified','cleanupRetriable','injectionMode',
        'suspendedThreadCount','primaryThreadId','extraReferenceRequested','extraReferenceLoaded',
        'extraReferenceNegativeFirstFreeLibraryStillPresent',
        'extraReferenceNegativeNotClaimedUnloaded','extraReferenceReleasedThenModuleAbsent'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $Record $required) 'exact-properties'
    $schemaVersion = Get-JsonPropertyValue $Record 'schemaVersion'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $schemaVersion) -and
        [int64]$schemaVersion -eq 2) 'schemaVersion'
    $code = Get-JsonPropertyValue $Record 'code'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $code) -and [int64]$code -ge 0 -and
        [uint64]$code -le [uint64][uint32]::MaxValue) 'code.type-range'
    foreach ($name in @(
            'injectionAttempted','moduleWasEverLoaded','moduleLoadStateVerified',
            'moduleSnapshotVerified','moduleAbsent','targetProcessExited','targetIdentityVerified',
            'probeReady','stopSucceeded','unloadSafe','moduleUnloaded','moduleResidentInactive',
            'strictUnloadVerified','cleanupRetriable','extraReferenceRequested',
            'extraReferenceLoaded','extraReferenceNegativeFirstFreeLibraryStillPresent',
            'extraReferenceNegativeNotClaimedUnloaded','extraReferenceReleasedThenModuleAbsent')) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $Record $name) -is [bool]) "$name.type"
    }
    $suspendedCount = Get-JsonPropertyValue $Record 'suspendedThreadCount'
    $primaryThreadId = Get-JsonPropertyValue $Record 'primaryThreadId'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $suspendedCount) -and
        [int64]$suspendedCount -ge 0 -and [int64]$suspendedCount -le 1) 'suspendedThreadCount.type-range'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $primaryThreadId) -and
        [int64]$primaryThreadId -ge 0 -and [uint64]$primaryThreadId -le [uint64][uint32]::MaxValue) 'primaryThreadId.type-range'

    $expectedStatus = $null
    [int64]$expectedCode = 0
    $expectedMode = @()
    [int64]$expectedSuspendedCount = 0
    $primaryThreadMustBePositive = $false
    $expectedFlags = [ordered]@{}
    switch -CaseSensitive ($ExpectedKind) {
        'Attach' {
            $expectedStatus = 'ATTACHED'
            $expectedMode = @('PausePrimaryRemoteThreadResume','PauseResumeThenRemoteThreadComplete')
            $expectedSuspendedCount = 1
            $primaryThreadMustBePositive = $true
            $expectedFlags = [ordered]@{
                injectionAttempted=$true; moduleWasEverLoaded=$true; moduleLoadStateVerified=$true
                moduleSnapshotVerified=$false; moduleAbsent=$false; targetProcessExited=$false
                targetIdentityVerified=$true; probeReady=$true; stopSucceeded=$false
                unloadSafe=$false; moduleUnloaded=$false; moduleResidentInactive=$false
                strictUnloadVerified=$false; cleanupRetriable=$true
                extraReferenceRequested=$true; extraReferenceLoaded=$true
                extraReferenceNegativeFirstFreeLibraryStillPresent=$false
                extraReferenceNegativeNotClaimedUnloaded=$false
                extraReferenceReleasedThenModuleAbsent=$false
            }
        }
        'Detach' {
            $expectedStatus = 'DETACHED'
            $expectedMode = @('PausePrimaryRemoteThreadResume','PauseResumeThenRemoteThreadComplete')
            $expectedSuspendedCount = 1
            $primaryThreadMustBePositive = $true
            $expectedFlags = [ordered]@{
                injectionAttempted=$true; moduleWasEverLoaded=$true; moduleLoadStateVerified=$true
                moduleSnapshotVerified=$true; moduleAbsent=$true; targetProcessExited=$false
                targetIdentityVerified=$true; probeReady=$false; stopSucceeded=$true
                unloadSafe=$true; moduleUnloaded=$true; moduleResidentInactive=$false
                strictUnloadVerified=$true; cleanupRetriable=$false
                extraReferenceRequested=$true; extraReferenceLoaded=$true
                extraReferenceNegativeFirstFreeLibraryStillPresent=$true
                extraReferenceNegativeNotClaimedUnloaded=$true
                extraReferenceReleasedThenModuleAbsent=$true
            }
        }
        'IdentityBlocked' {
            $expectedStatus = 'EVIDENCE_BLOCKED_BUILD_MISMATCH'
            $expectedCode = 193
            $expectedMode = @('None')
            $expectedFlags = [ordered]@{
                injectionAttempted=$false; moduleWasEverLoaded=$false; moduleLoadStateVerified=$true
                moduleSnapshotVerified=$true; moduleAbsent=$true; targetProcessExited=$false
                targetIdentityVerified=$false; probeReady=$false; stopSucceeded=$false
                unloadSafe=$false; moduleUnloaded=$false; moduleResidentInactive=$false
                strictUnloadVerified=$true; cleanupRetriable=$false
                extraReferenceRequested=$false; extraReferenceLoaded=$false
                extraReferenceNegativeFirstFreeLibraryStillPresent=$false
                extraReferenceNegativeNotClaimedUnloaded=$false
                extraReferenceReleasedThenModuleAbsent=$false
            }
        }
        default {
            $issues.Add('expected-kind')
            return Complete-ContractValidation $issues
        }
    }
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Record 'status') -ceq $expectedStatus) 'status'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $code) -and
        [int64]$code -eq $expectedCode) 'code'
    $mode = Get-JsonPropertyValue $Record 'injectionMode'
    Add-ContractIssue $issues ($mode -is [string] -and $expectedMode -ccontains $mode) 'injectionMode'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $suspendedCount) -and
        [int64]$suspendedCount -eq $expectedSuspendedCount) 'suspendedThreadCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $primaryThreadId) -and
        $(if ($primaryThreadMustBePositive) { [int64]$primaryThreadId -gt 0 } else { [int64]$primaryThreadId -eq 0 })) 'primaryThreadId'
    foreach ($entry in $expectedFlags.GetEnumerator()) {
        $actualFlag = Get-JsonPropertyValue $Record ([string]$entry.Key)
        $flagMatches = Test-JsonBooleanValue $actualFlag ([bool]$entry.Value)
        Add-ContractIssue $issues $flagMatches ([string]$entry.Key)
    }
    return Complete-ContractValidation $issues
}

function Add-ContractIssue([Collections.Generic.List[string]]$Issues, [bool]$Condition, [string]$Message) {
    if (-not $Condition) { $Issues.Add($Message) }
}

function Complete-ContractValidation([Collections.Generic.List[string]]$Issues) {
    return [pscustomobject]@{
        Passed = $Issues.Count -eq 0
        Issues = @($Issues)
    }
}

function Get-PortableTextContractIssues([string]$Text) {
    $issues = [Collections.Generic.List[string]]::new()
    if ($Text.IndexOf([char]0, [StringComparison]::Ordinal) -ge 0) {
        $issues.Add('NUL')
    }
    if ($Text -match '(?<![A-Za-z])[A-Za-z]:[\\/]' -or $Text -match 'file:[\\/]{2}' -or
        $Text -match '\\\\[^\\]') {
        $issues.Add('ABSOLUTE_OR_UNC_PATH')
    }
    if ($Text -match '[\u9225\u9286\u951B\u9359\u6D60\uFFFD]') {
        $issues.Add('MOJIBAKE_OR_REPLACEMENT')
    }
    if ($Text -match '[\x01-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F\p{Cf}]') {
        $issues.Add('CONTROL_OR_FORMAT_CHARACTER')
    }
    return @($issues)
}

function Test-ContractIssueGroup([object]$Validation, [string[]]$Prefixes) {
    if ($null -eq $Validation) { return $false }
    $issues = @($Validation.Issues)
    if (@($issues | Where-Object { $_ -match '\.required-properties$' }).Count -ne 0) {
        return $false
    }
    foreach ($issue in $issues) {
        foreach ($prefix in $Prefixes) {
            if ([string]$issue -clike "$prefix*") { return $false }
        }
    }
    return $true
}

function Test-EvidenceFixtureContract([object]$EvidenceFixture) {
    $issues = [Collections.Generic.List[string]]::new()
    $requiredTop = @(
        'SchemaVersion','TestedAtUtc','SessionId','FixturePath','CheckCount','Passed',
        'Failed','PackagePath','PackageSHA256','Checks'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $EvidenceFixture $requiredTop) 'evidence-fixture.exact-properties'
    $schemaVersion = Get-JsonPropertyValue $EvidenceFixture 'SchemaVersion'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $schemaVersion) -and
        [int64]$schemaVersion -eq 11) 'evidence-fixture.SchemaVersion'
    $testedAtUtc = Get-JsonPropertyValue $EvidenceFixture 'TestedAtUtc'
    $parsedTestedAt = [DateTimeOffset]::MinValue
    $testedAtValid = $testedAtUtc -is [string] -and $testedAtUtc -cmatch 'Z$' -and
        [DateTimeOffset]::TryParse($testedAtUtc, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsedTestedAt)
    Add-ContractIssue $issues $testedAtValid 'evidence-fixture.TestedAtUtc'
    foreach ($name in @('SessionId','FixturePath')) {
        $value = Get-JsonPropertyValue $EvidenceFixture $name
        Add-ContractIssue $issues ($value -is [string] -and
            $value -cmatch '^[A-Za-z0-9][A-Za-z0-9._-]+$' -and
            $value -cnotin @('.','..')) "evidence-fixture.$name.portable-token"
    }
    $packagePath = Get-JsonPropertyValue $EvidenceFixture 'PackagePath'
    Add-ContractIssue $issues ($packagePath -is [string] -and
        $packagePath -cmatch '^[A-Za-z0-9][A-Za-z0-9._-]+\.zip$' -and
        @(Get-PortableTextContractIssues $packagePath).Count -eq 0) 'evidence-fixture.PackagePath.portable-filename'
    $packageSha = Get-JsonPropertyValue $EvidenceFixture 'PackageSHA256'
    Add-ContractIssue $issues ($packageSha -is [string] -and
        $packageSha -cmatch '^[0-9A-F]{64}$') 'evidence-fixture.PackageSHA256'

    $checkCount = Get-JsonPropertyValue $EvidenceFixture 'CheckCount'
    $passed = Get-JsonPropertyValue $EvidenceFixture 'Passed'
    $failed = Get-JsonPropertyValue $EvidenceFixture 'Failed'
    $checks = @(Get-JsonPropertyValue $EvidenceFixture 'Checks')
    Add-ContractIssue $issues ((Test-JsonIntegerValue $checkCount) -and
        [int64]$checkCount -ge 77 -and $checks.Count -eq [int64]$checkCount) 'evidence-fixture.CheckCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $passed) -and
        (Test-JsonIntegerValue $checkCount) -and [int64]$passed -eq [int64]$checkCount) 'evidence-fixture.Passed'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $failed) -and
        [int64]$failed -eq 0) 'evidence-fixture.Failed'
    $checkNames = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $checks.Count; $index++) {
        $row = $checks[$index]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @('Name','Status','Evidence')) "evidence-fixture.Checks.$index.exact-properties"
        $name = Get-JsonPropertyValue $row 'Name'
        $status = Get-JsonPropertyValue $row 'Status'
        $evidence = Get-JsonPropertyValue $row 'Evidence'
        Add-ContractIssue $issues ($name -is [string] -and
            $name -cmatch '^[a-z0-9][a-z0-9_]+$') "evidence-fixture.Checks.$index.Name"
        if ($name -is [string]) { $checkNames.Add($name) }
        Add-ContractIssue $issues ($status -ceq 'PASS') "evidence-fixture.Checks.$index.Status"
        Add-ContractIssue $issues ($evidence -is [string] -and
            -not [string]::IsNullOrWhiteSpace($evidence) -and
            @(Get-PortableTextContractIssues $evidence).Count -eq 0) "evidence-fixture.Checks.$index.Evidence"
    }
    Add-ContractIssue $issues (@($checkNames | Select-Object -Unique).Count -eq $checkNames.Count) 'evidence-fixture.Checks.unique-names'
    foreach ($requiredName in @(
            '10a_legacy_or_detail_only_unload_spoof_rejected',
            '10b_versioned_strict_unload_positive_accepted',
            '10c_cleanup_schema_requires_typed_complete_inventory',
            '10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown',
            '14e_missing_shared_health_uses_nonpromotable_availability_schema',
            '14f_missing_candidate_map_never_emits_stale_v1_placeholder')) {
        Add-ContractIssue $issues (@($checks | Where-Object {
                    (Get-JsonPropertyValue $_ 'Name') -ceq $requiredName -and
                    (Get-JsonPropertyValue $_ 'Status') -ceq 'PASS'
                }).Count -eq 1) "evidence-fixture.required-check.$requiredName"
    }
    return Complete-ContractValidation $issues
}

function Test-NetworkRuntimeContract([object]$Network) {
    $issues = [Collections.Generic.List[string]]::new()
    $requiredTop = @('configuration','runDir','cases','pass')
    Add-ContractIssue $issues (Test-RequiredJsonProperties $Network $requiredTop) 'network.required-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Network 'configuration') -ceq 'Release') 'network.configuration'
    $runDir = Get-JsonPropertyValue $Network 'runDir'
    Add-ContractIssue $issues ($runDir -is [string] -and
        $runDir -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+$' -and
        $runDir -cnotmatch '^[A-Za-z]:|\\|\.\.') 'network.runDir.portable'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Network 'pass') $true) 'network.pass'

    $requiredCases = @(
        [ordered]@{ Name = 'send'; Result = 208; Bytes = 208 },
        [ordered]@{ Name = 'WSASend'; Result = 0; Bytes = 208 },
        [ordered]@{ Name = 'recv'; Result = 64; Bytes = 64 },
        [ordered]@{ Name = 'WSARecv'; Result = 0; Bytes = 64 },
        [ordered]@{ Name = 'WSARecvOverlapped'; Result = 0; Bytes = 64 }
    )
    $cases = @(Get-JsonPropertyValue $Network 'cases')
    Add-ContractIssue $issues ($cases.Count -eq 5) 'network.cases.count'
    foreach ($expectedCase in $requiredCases) {
        $caseName = [string]$expectedCase.Name
        $matches = @($cases | Where-Object { (Get-JsonPropertyValue $_ 'Case') -ceq $caseName })
        Add-ContractIssue $issues ($matches.Count -eq 1) "network.case.$caseName.unique"
        if ($matches.Count -ne 1) { continue }
        $case = $matches[0]
        Add-ContractIssue $issues (Test-RequiredJsonProperties $case @(
                'Case','HookInstalled','PayloadMatch','AnalyzerParseCompleted','TraceIntegrity',
                'ApiCallResult','ApiCallResultExpected','BytesTransferred','TraceRecordCount',
                'MatchingRecordCount','AnalyzerError','TraceFileSize')) "network.case.$caseName.required-properties"
        foreach ($flag in @('HookInstalled','PayloadMatch','AnalyzerParseCompleted','TraceIntegrity')) {
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $case $flag) $true) "network.case.$caseName.$flag"
        }
        $actual = Get-JsonPropertyValue $case 'ApiCallResult'
        $expected = Get-JsonPropertyValue $case 'ApiCallResultExpected'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $actual) -and (Test-JsonIntegerValue $expected) -and
            [int64]$actual -eq [int64]$expected -and [int64]$actual -eq [int64]$expectedCase.Result) "network.case.$caseName.ApiCallResult"
        $bytesTransferred = Get-JsonPropertyValue $case 'BytesTransferred'
        $traceRecords = Get-JsonPropertyValue $case 'TraceRecordCount'
        $matchingRecords = Get-JsonPropertyValue $case 'MatchingRecordCount'
        $traceFileSize = Get-JsonPropertyValue $case 'TraceFileSize'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $bytesTransferred) -and
            [int64]$bytesTransferred -eq [int64]$expectedCase.Bytes) "network.case.$caseName.BytesTransferred"
        Add-ContractIssue $issues ((Test-JsonIntegerValue $traceRecords) -and [int64]$traceRecords -gt 0 -and
            (Test-JsonIntegerValue $matchingRecords) -and [int64]$matchingRecords -gt 0 -and
            [int64]$matchingRecords -le [int64]$traceRecords) "network.case.$caseName.RecordCounts"
        Add-ContractIssue $issues ((Test-JsonIntegerValue $traceFileSize) -and
            [int64]$traceFileSize -gt 24) "network.case.$caseName.TraceFileSize"
        Add-ContractIssue $issues ($null -eq (Get-JsonPropertyValue $case 'AnalyzerError')) "network.case.$caseName.AnalyzerError"
    }
    return Complete-ContractValidation $issues
}

function Test-NativeProbeRuntimeContract([object]$NativeReport) {
    $issues = [Collections.Generic.List[string]]::new()
    $requiredTop = @(
        'SchemaId','SchemaVersion','ProcessId','SemanticEventProducerFixtureCount',
        'SharedTransportBatchMaximumItems','SharedTransportBatchAccepted','SharedTransportBatchCount','SharedTransportBatchEventSignalDelta',
        'SharedTransportBatchLockAcquisitionDelta','SharedTransportBatchContractPassed','SharedTransportBatchValidationWriteFailureCount','SharedTransportDomainWriteFailureTotal',
        'SharedTransportConsumerDrainVerified','SharedTransportPriorityContractPassed','SemanticAdmissionCapContractPassed','SemanticAdmissionCapSharedLedgerPassed',
        'SemanticAdmissionCapLiveStateUntouched','SemanticAdmissionHardLimit','SemanticAdmissionLowPriorityCeiling','FaultDiagnosticPriorityPolicyPassed',
        'FaultDiagnosticSelectedPriority','StartupProbeDiagnosticSelectedPriority','FaultDiagnosticAcceptedAfterLowPriorityCeiling','FaultDiagnosticAcceptedBeyondLegacyHardLimit',
        'FaultDiagnosticBeyondLegacyLimitDropReason','FaultDiagnosticAdmissionLiveStateUntouched','FaultDiagnosticProducerPublishReturned','RestoreFaultResidentNegativePassed',
        'CompletionRoutineObserved','WSAGetOverlappedResultObserved','GetQueuedCompletionStatusObserved','GetQueuedCompletionStatusExObserved',
        'OriginalRecvEntered','OriginalRecvReturned','OriginalWsaWaitEntered','OriginalWsaWaitReturned',
        'OriginalGqcsEntered','OriginalGqcsReturned','OriginalGqcsExEntered','OriginalGqcsExReturned',
        'PendingAtStopDeferredCount','PendingAtStopRetryDrainedCount','PendingAtStopDeferredAttempts','FaultIsolationPassed',
        'AsyncDiagnosticQueuePassed','AsyncDiagnosticEnqueued','AsyncDiagnosticFlushed','AsyncDiagnosticDropCount',
        'AsyncDiagnosticWriteFailureCount','AsyncDiagnosticHighWater','AsyncDiagnosticQueueCapacity','AsyncDiagnosticPending',
        'HookThreadFileIoOperations','HookThreadRingNullFallbackPassed','HookThreadRingNullSemanticLossCount','HookThreadRingNullLiveStateUntouched',
        'HookThreadRingNullPrivateSinkNonAliasing','HookThreadRingNullFormattedLineSinkPassed','HookThreadRingNullSchemaBatchPassed','HookThreadRingNullSchemaSingleFormattedCount',
        'HookThreadRingNullSchemaSingleDurableCount','HookThreadRingNullSchemaSingleLossCount','HookThreadRingNullSchemaBatchFormattedCount','HookThreadRingNullSchemaBatchDurableCount',
        'HookThreadRingNullSchemaBatchLossCount','HookThreadRingNullSchemaBatchDomainLossCount','HookThreadRingNullSchemaBatchFileIoCount','SemanticSessionFallbackFixturePassed',
        'SemanticSelfTestCheckCount','SemanticSelfTestPassedCheckCount','SemanticSelfTestFailedCheckCount','SemanticSelfTestFirstFailedCheckId',
        'AsyncDiagnosticPrivateNonAliasing','PrivacyFragmentPrivateNonAliasing','RestoreFaultPrivateControllerPassed','CapturePrerequisitePolicyFixturePassed',
        'SharedRingRequested','SharedRingPrerequisiteReady','SharedRingProducerPublished','TraceHeaderReady',
        'FlushWorkerHandleOwned','FlushWorkerStartupState','FlushWorkerStartupAcknowledged','ExportedSelfTestOwnerPolicyFixturePassed',
        'ExportedSelfTestOwnerState','ExportedSelfTestThreadHandleRetired','ExportedSelfTestOwnerRetired','IatOwnerLifecycleSelfTestPassed',
        'IatOwnerGoneRetired','IatAddressReuseNotClobbered','IatReplacementCasRestored','IatOriginalNotClobbered',
        'IatThirdPartyNotClobbered','IdentityProfile','SensitiveOutboundPrivacy','UnloadNeutralBridge',
        'SharedTransportPriorityResults','SemanticAdmissionCapResults','FaultIsolationOnlyAffectedNegativePassed','FaultIsolationMeasuredPositivePassed',
        'FaultIsolationResults','AsyncDiagnosticDomainResults','SharedTransportDomainResults'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $NativeReport $requiredTop) 'native.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $NativeReport 'SchemaId') -ceq 'God2NativeProbeSelfTest') 'native.SchemaId'
    foreach ($contract in @(
            @('SchemaVersion',1),@('SemanticEventProducerFixtureCount',25),
            @('SharedTransportBatchMaximumItems',8),
            @('SharedTransportBatchValidationWriteFailureCount',2),
            @('SharedTransportDomainWriteFailureTotal',2),
            @('AsyncDiagnosticDropCount',25),@('AsyncDiagnosticWriteFailureCount',0),
            @('AsyncDiagnosticPending',0),@('HookThreadFileIoOperations',0),
            @('HookThreadRingNullSemanticLossCount',1))) {
        $name = [string]$contract[0]
        $value = Get-JsonPropertyValue $NativeReport $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -eq [int64]$contract[1]) "native.$name"
    }
    $processId = Get-JsonPropertyValue $NativeReport 'ProcessId'
    $batchAccepted = Get-JsonPropertyValue $NativeReport 'SharedTransportBatchAccepted'
    $batchCount = Get-JsonPropertyValue $NativeReport 'SharedTransportBatchCount'
    $batchSignalDelta = Get-JsonPropertyValue $NativeReport 'SharedTransportBatchEventSignalDelta'
    $batchLockDelta = Get-JsonPropertyValue $NativeReport 'SharedTransportBatchLockAcquisitionDelta'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $processId) -and [int64]$processId -gt 0) 'native.ProcessId'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $batchAccepted) -and [int64]$batchAccepted -gt 1) 'native.SharedTransportBatchAccepted'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $batchCount) -and [int64]$batchCount -gt 0 -and
        (Test-JsonIntegerValue $batchSignalDelta) -and [int64]$batchSignalDelta -eq [int64]$batchCount -and
        (Test-JsonIntegerValue $batchLockDelta) -and [int64]$batchLockDelta -gt 0 -and
        [int64]$batchLockDelta -le 256) 'native.SharedTransportBatchSingleSignalAndBoundedLaneLocks'
    foreach ($flag in @('SharedTransportBatchContractPassed','SharedTransportPriorityContractPassed',
            'SharedTransportConsumerDrainVerified','SemanticAdmissionCapContractPassed',
            'SemanticAdmissionCapSharedLedgerPassed','SemanticAdmissionCapLiveStateUntouched',
            'RestoreFaultResidentNegativePassed','IatOwnerLifecycleSelfTestPassed',
            'IatOwnerGoneRetired','IatAddressReuseNotClobbered','IatReplacementCasRestored',
            'IatOriginalNotClobbered','IatThirdPartyNotClobbered',
            'FaultIsolationPassed','AsyncDiagnosticQueuePassed',
            'HookThreadRingNullFallbackPassed')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $NativeReport $flag) $true) "native.$flag"
    }
    foreach ($contract in @(@('SemanticAdmissionHardLimit',2000000),
            @('SemanticAdmissionLowPriorityCeiling',1500000))) {
        $name = [string]$contract[0]
        $value = Get-JsonPropertyValue $NativeReport $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -eq [int64]$contract[1]) "native.$name"
    }
    $capRows = @(Get-JsonPropertyValue $NativeReport 'SemanticAdmissionCapResults')
    Add-ContractIssue $issues ($capRows.Count -eq 2) 'native.SemanticAdmissionCapResults.count'
    if ($capRows.Count -eq 2) {
        $low = $capRows[0]
        $authoritative = $capRows[1]
        Add-ContractIssue $issues (Test-ExactJsonProperties $low @(
                'Priority','DomainIndex','Domain','AcceptedAtBoundary','DroppedAtBoundary',
                'DropReason','FirstDroppedSequence','LastDroppedSequence')) 'native.SemanticAdmissionCapResults.0.exact-properties'
        Add-ContractIssue $issues (Test-ExactJsonProperties $authoritative @(
                'Priority','DomainIndex','Domain','AcceptedBeyondLegacyHardLimit',
                'DroppedBeyondLegacyHardLimit','DropReason','FirstDroppedSequence','LastDroppedSequence')) 'native.SemanticAdmissionCapResults.1.exact-properties'
        foreach ($contract in @(@($low,'Priority',3),@($low,'DomainIndex',24),
                @($low,'AcceptedAtBoundary',1),@($low,'DroppedAtBoundary',1),
                @($low,'DropReason',8),@($authoritative,'Priority',0),
                @($authoritative,'DomainIndex',0),
                @($authoritative,'AcceptedBeyondLegacyHardLimit',1),
                @($authoritative,'DroppedBeyondLegacyHardLimit',0),
                @($authoritative,'DropReason',0))) {
            $row = $contract[0]; $name = [string]$contract[1]; $expected = [int64]$contract[2]
            $value = Get-JsonPropertyValue $row $name
            Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
                [int64]$value -eq $expected) "native.SemanticAdmissionCapResults.$name"
        }
        Add-ContractIssue $issues ((Get-JsonPropertyValue $low 'Domain') -ceq 'Snapshot') 'native.SemanticAdmissionCapResults.0.Domain'
        Add-ContractIssue $issues ((Get-JsonPropertyValue $authoritative 'Domain') -ceq 'Network') 'native.SemanticAdmissionCapResults.1.Domain'
        $lowFirst = Get-JsonPropertyValue $low 'FirstDroppedSequence'
        $lowLast = Get-JsonPropertyValue $low 'LastDroppedSequence'
        $highFirst = Get-JsonPropertyValue $authoritative 'FirstDroppedSequence'
        $highLast = Get-JsonPropertyValue $authoritative 'LastDroppedSequence'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $lowFirst) -and
            (Test-JsonIntegerValue $lowLast) -and [int64]$lowFirst -gt 0 -and
            [int64]$lowLast -eq [int64]$lowFirst) 'native.SemanticAdmissionCapResults.0.sequence'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $highFirst) -and
            (Test-JsonIntegerValue $highLast) -and [int64]$highFirst -eq 0 -and
            [int64]$highLast -eq 0) 'native.SemanticAdmissionCapResults.1.zero-loss-sequence'
    }
    foreach ($name in @('CompletionRoutineObserved','WSAGetOverlappedResultObserved',
            'GetQueuedCompletionStatusObserved','GetQueuedCompletionStatusExObserved')) {
        $value = Get-JsonPropertyValue $NativeReport $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -gt 0) "native.$name"
    }
    $pendingDeferred = Get-JsonPropertyValue $NativeReport 'PendingAtStopDeferredCount'
    $pendingDrained = Get-JsonPropertyValue $NativeReport 'PendingAtStopRetryDrainedCount'
    $pendingAttempts = Get-JsonPropertyValue $NativeReport 'PendingAtStopDeferredAttempts'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $pendingDeferred) -and [int64]$pendingDeferred -eq 0 -and
        (Test-JsonIntegerValue $pendingDrained) -and [int64]$pendingDrained -eq 0 -and
        (Test-JsonIntegerValue $pendingAttempts) -and [int64]$pendingAttempts -eq 0) 'native.PendingAtStopDrainReconciliation'

    $priorityRows = @(Get-JsonPropertyValue $NativeReport 'SharedTransportPriorityResults')
    Add-ContractIssue $issues ($priorityRows.Count -eq 4) 'native.SharedTransportPriorityResults.count'
    for ($priority = 0; $priority -lt 4; $priority++) {
        $matches = @($priorityRows | Where-Object { (Get-JsonPropertyValue $_ 'Priority') -eq $priority })
        Add-ContractIssue $issues ($matches.Count -eq 1) "native.SharedTransportPriorityResults.$priority.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'Priority','LaneAccepted','LaneDropped','LaneDropReason',
                'SamplingProbeDropped','SamplingProbeDropReason')) "native.SharedTransportPriorityResults.$priority.required-properties"
        $samplingDropped = if ($priority -lt 2) { 0 } else { 1 }
        $samplingReason = if ($priority -lt 2) { 0 } else { 10 }
        foreach ($contract in @(
                @('LaneAccepted',1),@('LaneDropped',0),@('LaneDropReason',0),
                @('SamplingProbeDropped',$samplingDropped),
                @('SamplingProbeDropReason',$samplingReason))) {
            $name = [string]$contract[0]
            $value = Get-JsonPropertyValue $row $name
            Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
                [int64]$value -eq [int64]$contract[1]) "native.SharedTransportPriorityResults.$priority.$name"
        }
    }

    $faultRows = @(Get-JsonPropertyValue $NativeReport 'FaultIsolationResults')
    Add-ContractIssue $issues ($faultRows.Count -eq 25) 'native.FaultIsolationResults.count'
    for ($index = 0; $index -lt 25; $index++) {
        $matches = @($faultRows | Where-Object { (Get-JsonPropertyValue $_ 'DomainIndex') -eq $index })
        Add-ContractIssue $issues ($matches.Count -eq 1) "native.FaultIsolationResults.$index.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'DomainIndex','Domain','FaultInjected','AffectedDomainDisabled',
                'PrivateDiagnosticAttributed','PrivateDiagnosticSinkWritten','RuntimeDiagnosticEmitted',
                'OtherDomainsEnabledAtFault','OtherDomainsEnabledMask',
                'OtherDomainsRemainEnabledMeasured','OtherDomainContinued')) "native.FaultIsolationResults.$index.required-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Domain') -ceq
            $script:ExpectedNativeProbeDomains[$index]) "native.FaultIsolationResults.$index.Domain"
        foreach ($flag in @('FaultInjected','AffectedDomainDisabled','PrivateDiagnosticAttributed',
                'PrivateDiagnosticSinkWritten','OtherDomainsRemainEnabledMeasured','OtherDomainContinued')) {
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row $flag) $true) "native.FaultIsolationResults.$index.$flag"
        }
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'RuntimeDiagnosticEmitted') $false) "native.FaultIsolationResults.$index.RuntimeDiagnosticEmitted"
        $otherCount = Get-JsonPropertyValue $row 'OtherDomainsEnabledAtFault'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $otherCount) -and [int64]$otherCount -eq 1) "native.FaultIsolationResults.$index.OtherDomainsEnabledAtFault"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'OtherDomainsEnabledMask') -is [string] -and
            (Get-JsonPropertyValue $row 'OtherDomainsEnabledMask') -cmatch '^0x[0-9A-F]{8}$') "native.FaultIsolationResults.$index.OtherDomainsEnabledMask"
    }

    $diagnosticRows = @(Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticDomainResults')
    Add-ContractIssue $issues ($diagnosticRows.Count -eq 25) 'native.AsyncDiagnosticDomainResults.count'
    [int64]$diagnosticDrops = 0; [int64]$diagnosticWriteFailures = 0
    for ($index = 0; $index -lt 25; $index++) {
        $matches = @($diagnosticRows | Where-Object { (Get-JsonPropertyValue $_ 'DomainIndex') -eq $index })
        Add-ContractIssue $issues ($matches.Count -eq 1) "native.AsyncDiagnosticDomainResults.$index.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'DomainIndex','Domain','Dropped','WriteFailures','EvidenceIncomplete')) "native.AsyncDiagnosticDomainResults.$index.required-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Domain') -ceq
            $script:ExpectedNativeProbeDomains[$index]) "native.AsyncDiagnosticDomainResults.$index.Domain"
        $dropped = Get-JsonPropertyValue $row 'Dropped'
        $writeFailures = Get-JsonPropertyValue $row 'WriteFailures'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $dropped) -and [int64]$dropped -eq 1) "native.AsyncDiagnosticDomainResults.$index.Dropped"
        Add-ContractIssue $issues ((Test-JsonIntegerValue $writeFailures) -and [int64]$writeFailures -eq 1) "native.AsyncDiagnosticDomainResults.$index.WriteFailures"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'EvidenceIncomplete') $true) "native.AsyncDiagnosticDomainResults.$index.EvidenceIncomplete"
        if ((Test-JsonIntegerValue $dropped) -and (Test-JsonIntegerValue $writeFailures)) {
            $diagnosticDrops += [int64]$dropped
            $diagnosticWriteFailures += [int64]$writeFailures
        }
    }
    Add-ContractIssue $issues ($diagnosticDrops -eq 25 -and
        $diagnosticDrops -eq [int64](Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticDropCount')) 'native.AsyncDiagnosticDropCount.reconciliation'
    Add-ContractIssue $issues ($diagnosticWriteFailures -eq 25 -and
        [int64](Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticWriteFailureCount') -eq 0 -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticPrivateNonAliasing') $true)) 'native.AsyncDiagnosticWriteFailureCount.private-reconciliation'
    $queueCapacity = Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticQueueCapacity'
    $queueEnqueued = Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticEnqueued'
    $queueFlushed = Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticFlushed'
    $queueHighWater = Get-JsonPropertyValue $NativeReport 'AsyncDiagnosticHighWater'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $queueCapacity) -and [int64]$queueCapacity -ge 16 -and
        [int64]$queueCapacity -le 65536 -and (Test-JsonIntegerValue $queueEnqueued) -and
        (Test-JsonIntegerValue $queueFlushed) -and (Test-JsonIntegerValue $queueHighWater) -and
        [int64]$queueEnqueued -eq [int64]$queueCapacity -and
        [int64]$queueFlushed -eq [int64]$queueCapacity -and
        [int64]$queueHighWater -gt 0 -and [int64]$queueHighWater -le [int64]$queueCapacity) 'native.AsyncDiagnosticQueueMeasuredBounds'

    $domainRows = @(Get-JsonPropertyValue $NativeReport 'SharedTransportDomainResults')
    Add-ContractIssue $issues ($domainRows.Count -eq 25) 'native.SharedTransportDomainResults.count'
    [int64]$sharedWriteFailures = 0
    for ($index = 0; $index -lt 25; $index++) {
        $matches = @($domainRows | Where-Object { (Get-JsonPropertyValue $_ 'DomainIndex') -eq $index })
        Add-ContractIssue $issues ($matches.Count -eq 1) "native.SharedTransportDomainResults.$index.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'DomainIndex','Domain','Accepted','Dropped','HighWater','Pending','WriteFailures')) "native.SharedTransportDomainResults.$index.required-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Domain') -ceq
            $script:ExpectedNativeProbeDomains[$index]) "native.SharedTransportDomainResults.$index.Domain"
        foreach ($name in @('Accepted','HighWater')) {
            $value = Get-JsonPropertyValue $row $name
            Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -gt 0) "native.SharedTransportDomainResults.$index.$name"
        }
        $domainDropped = Get-JsonPropertyValue $row 'Dropped'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $domainDropped) -and
            [int64]$domainDropped -ge 0) "native.SharedTransportDomainResults.$index.Dropped"
        $pending = Get-JsonPropertyValue $row 'Pending'
        $writeFailures = Get-JsonPropertyValue $row 'WriteFailures'
        $expectedWriteFailures = if ($index -ge 23) { 1 } else { 0 }
        Add-ContractIssue $issues ((Test-JsonIntegerValue $pending) -and [int64]$pending -eq 0) "native.SharedTransportDomainResults.$index.Pending"
        Add-ContractIssue $issues ((Test-JsonIntegerValue $writeFailures) -and
            [int64]$writeFailures -eq $expectedWriteFailures) "native.SharedTransportDomainResults.$index.WriteFailures"
        if (Test-JsonIntegerValue $writeFailures) { $sharedWriteFailures += [int64]$writeFailures }
    }
    Add-ContractIssue $issues ($sharedWriteFailures -eq 2 -and
        $sharedWriteFailures -eq [int64](Get-JsonPropertyValue $NativeReport 'SharedTransportDomainWriteFailureTotal')) 'native.SharedTransportDomainWriteFailureTotal.reconciliation'

    foreach ($flag in @('FaultDiagnosticPriorityPolicyPassed','FaultDiagnosticAcceptedAfterLowPriorityCeiling',
            'FaultDiagnosticAcceptedBeyondLegacyHardLimit','FaultDiagnosticAdmissionLiveStateUntouched','FaultDiagnosticProducerPublishReturned',
            'HookThreadRingNullLiveStateUntouched','HookThreadRingNullPrivateSinkNonAliasing',
            'HookThreadRingNullFormattedLineSinkPassed','HookThreadRingNullSchemaBatchPassed',
            'SemanticSessionFallbackFixturePassed','AsyncDiagnosticPrivateNonAliasing','PrivacyFragmentPrivateNonAliasing',
            'RestoreFaultPrivateControllerPassed','CapturePrerequisitePolicyFixturePassed','SharedRingPrerequisiteReady',
            'SharedRingProducerPublished','TraceHeaderReady','FlushWorkerHandleOwned','FlushWorkerStartupAcknowledged',
            'ExportedSelfTestOwnerPolicyFixturePassed','ExportedSelfTestThreadHandleRetired','ExportedSelfTestOwnerRetired',
            'FaultIsolationOnlyAffectedNegativePassed','FaultIsolationMeasuredPositivePassed')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $NativeReport $flag) $true) "native.$flag"
    }
    foreach ($contract in @(@('FaultDiagnosticSelectedPriority',0),@('StartupProbeDiagnosticSelectedPriority',3),
            @('FaultDiagnosticBeyondLegacyLimitDropReason',0),@('HookThreadRingNullSchemaSingleFormattedCount',1),
            @('HookThreadRingNullSchemaSingleDurableCount',0),@('HookThreadRingNullSchemaSingleLossCount',1),
            @('HookThreadRingNullSchemaBatchFormattedCount',2),@('HookThreadRingNullSchemaBatchDurableCount',0),
            @('HookThreadRingNullSchemaBatchLossCount',2),@('HookThreadRingNullSchemaBatchDomainLossCount',2),
            @('HookThreadRingNullSchemaBatchFileIoCount',0),@('SemanticSelfTestFailedCheckCount',0),
            @('SemanticSelfTestFirstFailedCheckId',0),@('FlushWorkerStartupState',2),@('ExportedSelfTestOwnerState',4))) {
        $name = [string]$contract[0]; $value = Get-JsonPropertyValue $NativeReport $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -eq [int64]$contract[1]) "native.$name"
    }
    $semanticChecks = Get-JsonPropertyValue $NativeReport 'SemanticSelfTestCheckCount'
    $semanticPassed = Get-JsonPropertyValue $NativeReport 'SemanticSelfTestPassedCheckCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $semanticChecks) -and [int64]$semanticChecks -ge 83 -and
        (Test-JsonIntegerValue $semanticPassed) -and [int64]$semanticPassed -eq [int64]$semanticChecks) 'native.SemanticSelfTest.reconciliation'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $NativeReport 'IdentityProfile') -ceq 'TestOnlyExactFixture') 'native.IdentityProfile'

    $bridge = Get-JsonPropertyValue $NativeReport 'UnloadNeutralBridge'
    Add-ContractIssue $issues (Test-ExactJsonProperties $bridge $script:ExpectedUnloadNeutralBridgeProperties) 'native.UnloadNeutralBridge.exact-properties'
    foreach ($contract in @(@('SchemaId','god2-unload-neutral-bridge-v1'),@('CodeProtection','ExecuteRead'),
            @('StateProtection','ReadWrite'),@('Phase','Detached'),@('State','RetainedPassThrough'))) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $bridge $contract[0]) -ceq $contract[1]) "native.UnloadNeutralBridge.$($contract[0])"
    }
    foreach ($contract in @(@('SchemaVersion',1),@('WritableExecutablePageCount',0),@('ObserverRundown',0),
            @('PreOriginalRundown',0),@('PendingApplicationCallbacks',0),@('DllPointerCount',0),
            @('InvalidPersistentOriginalPointerCount',0),@('PersistentOriginalIdentityMismatchCount',0),
            @('MissingRequiredPersistentOriginalCount',0),@('InvalidEntryPointerCount',0),@('EntryIdentityMismatchCount',0),
            @('MissingCompletionThunkCount',0),@('InvalidCompletionThunkCount',0),@('CompletionThunkIdentityMismatchCount',0),
            @('ActiveApplicationCallbackPointerCount',0),@('InvalidApplicationCallbackPointerCount',0),
            @('ExactPatchOwnershipLostCount',0),@('ExactPatchOwnershipUnsafeCount',0),@('CrossStageUnexpectedPostCount',0),
            @('WSARecvCommitStagePrivatePendingFinal',0),@('PersistentOriginalPointerCount',7),
            @('ExactPersistentOriginalPointerCount',7),@('SealedEntryPointerCount',11),@('ExactEntryPointerCount',11),
            @('SealedCompletionThunkCount',256),@('ExactCompletionThunkCount',256),@('HookLockContentionFixtureCount',4),
            @('HookLockContentionFixturePassedCount',4),@('AbiFixtureCount',10),@('AbiFixturePassedCount',10),
            @('WSARecvCommitStageFixtureCount',8),@('WSARecvCommitStagePassedCount',8),@('WSARecvCommitStagePassedMask',255))) {
        $name = [string]$contract[0]; $value = Get-JsonPropertyValue $bridge $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -eq [int64]$contract[1]) "native.UnloadNeutralBridge.$name"
    }
    foreach ($flag in @('CaptureObserverRetired','ObserverPointerNull','BridgeNoDllPointers','IatRestored','HotpatchRestored',
            'PointerIdentityFixturePassed','ExactPatchTransactionFixturePassed','ExactPatchThirdPartyNoClobberFixturePassed',
            'HookLockContentionFixturePassed','ObserverTamperFixturePassed','ObserverFaultFixturePassed','CrossStageFaultFixturePassed',
            'CompletionRouteFixturePassed','CompletionDuplicateBeforeReusePassed','CompletionDeterministicAbaPassed',
            'CompletionAbaBarrierReached','CompletionAbaOldCasRejected','CompletionAbaNewRouteIntact','RetainedOwnerUnloadGuardPassed',
            'WSARecvCommitStageFixturePassed','WSARecvCommitStageLiveStateUntouched','WSARecvSameIdentityIsolationPassed',
            'WSARecvNoCallbackIdentityIsolationPassed','WSARecvPostRepostIsolationPassed','WSARecvPostFaultRepostIsolationPassed',
            'WSARecvFinalPurgeRacePassed','WSARecvFinalPurgeLateRegistrationObserved')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $bridge $flag) $true) "native.UnloadNeutralBridge.$flag"
    }
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $bridge 'DllPointersRemaining') $false) 'native.UnloadNeutralBridge.DllPointersRemaining'
    $coverage = @(Get-JsonPropertyValue $bridge 'BridgeCoverageResults')
    $expectedBridgeApis = @('send','WSASend','recv','WSARecv','WSAGetOverlappedResult','GetQueuedCompletionStatus',
        'GetQueuedCompletionStatusEx','WSARecvCompletionRoutine','Parser','Serializer','Handler')
    Add-ContractIssue $issues ($coverage.Count -eq 11) 'native.UnloadNeutralBridge.BridgeCoverageResults.count'
    for ($index = 0; $index -lt [Math]::Min(11,$coverage.Count); $index++) {
        $row = $coverage[$index]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row $script:ExpectedBridgeCoverageProperties) "native.UnloadNeutralBridge.BridgeCoverageResults.$index.exact-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Api') -ceq $expectedBridgeApis[$index]) "native.UnloadNeutralBridge.BridgeCoverageResults.$index.Api"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'InstalledToBridge') ($index -lt 8)) "native.UnloadNeutralBridge.BridgeCoverageResults.$index.InstalledToBridge"
    }

    $privacy = Get-JsonPropertyValue $NativeReport 'SensitiveOutboundPrivacy'
    Add-ContractIssue $issues (Test-ExactJsonProperties $privacy $script:ExpectedSensitiveOutboundPrivacyProperties) 'native.SensitiveOutboundPrivacy.exact-properties'
    foreach ($contract in @(@('FixtureCount',15),@('FixturePassedCount',15),@('FixturePassedMask',32767),
            @('LiveActiveTransactions',0),@('LivePendingTransactions',0),@('EarlyCompletionCapturedLength',0),
            @('EarlyCompletionPayloadBytesPersisted',0))) {
        $name=[string]$contract[0]; $value=Get-JsonPropertyValue $privacy $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -eq [int64]$contract[1]) "native.SensitiveOutboundPrivacy.$name"
    }
    foreach ($flag in @('FixturePassed','PrivateNonAliasing','AllContextPointersNonAliasing','PointerAliasNegativePassed',
            'LiveSnapshotUnchanged','LiveStateUntouched','EarlyCompletionRedactionCarryPassed',
            'EarlyCompletionPrivateSinkNonAliasing','EarlyCompletionSensitivePayloadSuppressed')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $privacy $flag) $true) "native.SensitiveOutboundPrivacy.$flag"
    }
    $integration = Get-JsonPropertyValue $privacy 'TestOnlyLiveIntegration'
    Add-ContractIssue $issues (Test-ExactJsonProperties $integration $script:ExpectedSensitiveOutboundIntegrationProperties) 'native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.exact-properties'
    foreach ($flag in @('Invoked','Passed','TestOnlyExactFixture','SerializerAdapterObserved','SendBridgeObserved','WSASendBridgeObserved',
            'RetryObserved','DeterministicPartialFixturePassed','PendingObserved','CompletionObserved','SinkAdmissionObserved',
            'SuccessfulRetrySinkAdmitted','SuccessfulRetrySensitivePayloadSuppressed','WsaSendPendingSinkAdmitted',
            'WsaSendPendingSensitivePayloadSuppressed','SuccessfulRetrySameShapeDecoyRejected','WsaSendPendingSameShapeDecoyRejected',
            'DecoyCannotSubstituteDroppedReal')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $integration $flag) $true) "native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.$flag"
    }
    foreach ($flag in @('OfficialRuntime','SerializerEntryObserved')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $integration $flag) $false) "native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.$flag"
    }
    foreach ($identityName in @('SuccessfulRetryIdentity','WsaSendPendingIdentity')) {
        $identity = Get-JsonPropertyValue $integration $identityName
        Add-ContractIssue $issues (Test-ExactJsonProperties $identity $script:ExpectedSensitiveOutboundIdentityProperties) "native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.$identityName.exact-properties"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $identity 'Matched') $true) "native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.$identityName.Matched"
        foreach ($prefix in @('ThreadId','Socket','TransactionId','ProducerGuard','FrameFingerprint','FirstBuffer','Overlapped','BridgeGeneration','SegmentCount','CallerReturnAddress')) {
            Add-ContractIssue $issues ((Get-JsonPropertyValue $identity "Expected$prefix") -ceq (Get-JsonPropertyValue $identity "Observed$prefix")) "native.SensitiveOutboundPrivacy.TestOnlyLiveIntegration.$identityName.$prefix"
        }
    }
    return Complete-ContractValidation $issues
}

function Test-NativeSemanticWireVerificationContract(
        [object]$Report, [string]$ExpectedSha256, [string]$ExpectedDigest) {
    $issues = [Collections.Generic.List[string]]::new()
    Add-ContractIssue $issues (Test-ExactJsonProperties $Report @(
            'schemaId','schemaVersion','inputPath','inputSHA256','eventTypeDigest',
            'inputLineCount','allEventTypesUnique','passed','totals','rows','error')) 'wire-verifier.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Report 'schemaId') -ceq
        'God2NativeSemanticWireVerification') 'wire-verifier.schemaId'
    $schemaVersion = Get-JsonPropertyValue $Report 'schemaVersion'
    $lineCount = Get-JsonPropertyValue $Report 'inputLineCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $schemaVersion) -and
        [int64]$schemaVersion -eq 1) 'wire-verifier.schemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Report 'inputPath') -ceq
        'dll-native-semantic-event-v2.jsonl') 'wire-verifier.inputPath'
    Add-ContractIssue $issues ($ExpectedSha256 -cmatch '^[0-9A-F]{64}$' -and
        (Get-JsonPropertyValue $Report 'inputSHA256') -ceq $ExpectedSha256) 'wire-verifier.inputSHA256'
    Add-ContractIssue $issues ($ExpectedDigest -cmatch '^[0-9A-F]{64}$' -and
        (Get-JsonPropertyValue $Report 'eventTypeDigest') -ceq $ExpectedDigest) 'wire-verifier.eventTypeDigest'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $lineCount) -and
        [int64]$lineCount -eq 25) 'wire-verifier.inputLineCount'
    Add-ContractIssue $issues (Test-JsonBooleanValue `
        (Get-JsonPropertyValue $Report 'allEventTypesUnique') $true) 'wire-verifier.allEventTypesUnique'
    Add-ContractIssue $issues (Test-JsonBooleanValue `
        (Get-JsonPropertyValue $Report 'passed') $true) 'wire-verifier.passed'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Report 'error') -ceq '') 'wire-verifier.error'
    $totals = Get-JsonPropertyValue $Report 'totals'
    $totalNames = @('readerAcceptedCount','sourceTokenBoundCount','fixturePayloadBoundCount',
        'authorityFailClosedCount','dispatchCount','roundTripCount','corruptionRejectedCount',
        'versionRejectedCount','promotionPolicyCount')
    Add-ContractIssue $issues (Test-ExactJsonProperties $totals $totalNames) 'wire-verifier.totals.exact-properties'
    foreach ($name in $totalNames) {
        $value = Get-JsonPropertyValue $totals $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -eq 25) "wire-verifier.totals.$name"
    }
    $rows = @(Get-JsonPropertyValue $Report 'rows')
    Add-ContractIssue $issues ($rows.Count -eq 25) 'wire-verifier.rows.count'
    for ($index = 0; $index -lt 25; $index++) {
        $matches = @($rows | Where-Object {
            (Get-JsonPropertyValue $_ 'definitionIndex') -eq $index
        })
        Add-ContractIssue $issues ($matches.Count -eq 1) "wire-verifier.rows.$index.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'wireLine','definitionIndex','eventType','readerAccepted','sourceTokenBound',
                'fixturePayloadBound','authorityFailClosed','dispatchPassed','roundTripPassed',
                'corruptionRejected','versionRejected','promotionPolicyPassed','dispatchSink',
                'promotionPolicy')) "wire-verifier.rows.$index.exact-properties"
        $wireLine = Get-JsonPropertyValue $row 'wireLine'
        $definitionIndex = Get-JsonPropertyValue $row 'definitionIndex'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $wireLine) -and
            [int64]$wireLine -eq ($index + 1)) "wire-verifier.rows.$index.wireLine"
        Add-ContractIssue $issues ((Test-JsonIntegerValue $definitionIndex) -and
            [int64]$definitionIndex -eq $index) "wire-verifier.rows.$index.definitionIndex"
        $expected = $script:ExpectedSemanticEventTypes[$index]
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'eventType') -ceq
            $expected.EventType) "wire-verifier.rows.$index.eventType"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'dispatchSink') -ceq
            $expected.DispatchSink) "wire-verifier.rows.$index.dispatchSink"
        foreach ($flag in @('readerAccepted','sourceTokenBound','fixturePayloadBound',
                'authorityFailClosed','dispatchPassed','roundTripPassed',
                'corruptionRejected','versionRejected','promotionPolicyPassed')) {
            Add-ContractIssue $issues (Test-JsonBooleanValue `
                (Get-JsonPropertyValue $row $flag) $true) "wire-verifier.rows.$index.$flag"
        }
        $expectedPolicy = if ($expected.EventType -ceq 'FunctionRoleCandidate' -or
            $expected.EventType -ceq 'ProbeDiagnostic') { 'NoPromotion' } else { 'StandardEvidenceGates' }
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'promotionPolicy') -ceq
            $expectedPolicy) "wire-verifier.rows.$index.promotionPolicy"
    }
    return Complete-ContractValidation $issues
}

function Test-DeepProbeCandidateMapContract([object]$CandidateMap) {
    $issues = [Collections.Generic.List[string]]::new()
    $requiredTop = @(
        'SchemaVersion','ClientSHA256','ClientSha256Expected','IdentityProfile','Architecture',
        'DiscoveryPolicy','CandidateVerificationEngine','UnconfirmedProbePolicy',
        'CandidateSchemaVersion','TargetIdentityVerified','EngineBounds','DomainCount','Domains'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $CandidateMap $requiredTop) 'candidate-map.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'SchemaVersion') -ceq
        'god2-deep-probe-candidate-map-v2') 'candidate-map.SchemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'CandidateSchemaVersion') -ceq
        'god2-deep-probe-candidate-v1') 'candidate-map.CandidateSchemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'Architecture') -ceq 'x86') 'candidate-map.Architecture'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'DiscoveryPolicy') -ceq
        'ExecutableCodeAndExactSignaturesOnly;NoWritableMemoryScan;NoSensitiveValueLogging') 'candidate-map.DiscoveryPolicy'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'CandidateVerificationEngine') -ceq
        'BoundedExecutableCallGraphPlusExactCandidateBytesPlusPromotionHardGate') 'candidate-map.CandidateVerificationEngine'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $CandidateMap 'UnconfirmedProbePolicy') -ceq
        'EvidenceBlockedUnconfirmedProbe') 'candidate-map.UnconfirmedProbePolicy'
    $clientSha = Get-JsonPropertyValue $CandidateMap 'ClientSHA256'
    $expectedClientSha = Get-JsonPropertyValue $CandidateMap 'ClientSha256Expected'
    $identityProfile = Get-JsonPropertyValue $CandidateMap 'IdentityProfile'
    $identityVerified = Get-JsonPropertyValue $CandidateMap 'TargetIdentityVerified'
    Add-ContractIssue $issues ($identityProfile -in @('OfficialExactClient','TestOnlyExactFixture')) 'candidate-map.IdentityProfile'
    Add-ContractIssue $issues ($clientSha -is [string] -and $clientSha -cmatch '^[0-9A-F]{64}$' -and
        $expectedClientSha -is [string] -and $expectedClientSha -cmatch '^[0-9A-F]{64}$') 'candidate-map.ClientSHA256.shape'
    if ($identityProfile -ceq 'TestOnlyExactFixture') {
        Add-ContractIssue $issues (Test-JsonBooleanValue $identityVerified $true) 'candidate-map.TargetIdentityVerified.test-only'
        Add-ContractIssue $issues ($clientSha -ceq $expectedClientSha) 'candidate-map.ClientSHA256.test-only-exact'
    } else {
        Add-ContractIssue $issues (Test-JsonBooleanValue $identityVerified $false) 'candidate-map.TargetIdentityVerified.official-mismatch'
        Add-ContractIssue $issues ($clientSha -cne $expectedClientSha) 'candidate-map.ClientSHA256.official-mismatch'
    }
    $domainCount = Get-JsonPropertyValue $CandidateMap 'DomainCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $domainCount) -and
        [int64]$domainCount -eq 25) 'candidate-map.DomainCount'

    $bounds = Get-JsonPropertyValue $CandidateMap 'EngineBounds'
    Add-ContractIssue $issues (Test-ExactJsonProperties $bounds @(
            'ScanRadiusBytes','MaximumCandidatesPerDomain','SignatureBytes','WritableMemoryScanned')) 'candidate-map.EngineBounds.exact-properties'
    foreach ($contract in @(@('ScanRadiusBytes',256),@('MaximumCandidatesPerDomain',4),
            @('SignatureBytes',8))) {
        $name = [string]$contract[0]
        $value = Get-JsonPropertyValue $bounds $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -eq [int64]$contract[1]) "candidate-map.EngineBounds.$name"
    }
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $bounds 'WritableMemoryScanned') $false) 'candidate-map.EngineBounds.WritableMemoryScanned'

    $domains = @(Get-JsonPropertyValue $CandidateMap 'Domains')
    Add-ContractIssue $issues ($domains.Count -eq 25) 'candidate-map.Domains.count'
    $gateNames = @($script:ExpectedCandidateGates | ForEach-Object { $_.Gate })
    for ($index = 0; $index -lt [Math]::Min(25, $domains.Count); $index++) {
        $row = $domains[$index]
        $expected = $script:ExpectedProbeDomains[$index]
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Domain') -ceq
            $script:ExpectedNativeProbeDomains[$index]) "candidate-map.Domains.$index.Domain"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Probe') -ceq
            $expected.Probe) "candidate-map.Domains.$index.Probe"
        if ($index -lt 4) {
            Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                    'Domain','Probe','Status','Authority','Candidates','CandidateCount',
                    'VerificationStatus')) "candidate-map.Domains.$index.exact-properties"
            $expectedStatus = if ($identityVerified) {
                if ($index -eq 0) { 'Active' } else { 'ConfiguredInactiveNetworkOnly' }
            } else { 'CurrentTargetIdentityBlocked' }
            $expectedAuthority = if ($identityVerified) { 'VERIFIED' } else { 'UNKNOWN' }
            $expectedVerification = if ($identityVerified) {
                if ($index -eq 0) { 'PASS' } else { 'NotApplicableConfiguredInactiveNetworkOnly' }
            } else { 'EvidenceBlockedExactIdentityMismatch' }
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Status') -ceq $expectedStatus) "candidate-map.Domains.$index.Status"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Authority') -ceq $expectedAuthority) "candidate-map.Domains.$index.Authority"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'VerificationStatus') -ceq $expectedVerification) "candidate-map.Domains.$index.VerificationStatus"
            $expectedCandidateCount = if ($index -eq 0) { 4 } else { 1 }
            $candidateCount = Get-JsonPropertyValue $row 'CandidateCount'
            Add-ContractIssue $issues ((Test-JsonIntegerValue $candidateCount) -and
                [int64]$candidateCount -eq $expectedCandidateCount -and
                @(Get-JsonPropertyValue $row 'Candidates').Count -eq $expectedCandidateCount) "candidate-map.Domains.$index.Candidates"
            continue
        }
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'Domain','Probe','Status','Authority','DiscoveryPlan','Candidates',
                'CandidateCount','VerificationContract','VerificationStatus',
                'InstallationPolicy','SafeNextAction')) "candidate-map.Domains.$index.exact-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Status') -ceq
            'EvidenceBlockedUnconfirmedProbe') "candidate-map.Domains.$index.Status"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Authority') -ceq
            'UNKNOWN') "candidate-map.Domains.$index.Authority"
        $candidateCount = Get-JsonPropertyValue $row 'CandidateCount'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $candidateCount) -and
            [int64]$candidateCount -eq 0 -and @(Get-JsonPropertyValue $row 'Candidates').Count -eq 0) "candidate-map.Domains.$index.Candidates"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'VerificationStatus') -ceq
            'EvidenceBlockedNoExecutableCallCandidate') "candidate-map.Domains.$index.VerificationStatus"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'InstallationPolicy') -ceq
            'NeverInstallUntilAllVerificationGatesPass') "candidate-map.Domains.$index.InstallationPolicy"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'SafeNextAction') -ceq
            'CollectExactBuildRepeatedTypedRuntimeEvidenceAndVerifyCallingConvention') "candidate-map.Domains.$index.SafeNextAction"
        $plan = Get-JsonPropertyValue $row 'DiscoveryPlan'
        Add-ContractIssue $issues (Test-ExactJsonProperties $plan @(
                'SeedDomain','Strategy','TypedEvidenceRequired','CausalEvidenceRequired')) "candidate-map.Domains.$index.DiscoveryPlan.exact-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $plan 'Strategy') -ceq
            'BoundedDirectCallTargetsFromVerifiedSeed') "candidate-map.Domains.$index.DiscoveryPlan.Strategy"
        foreach ($name in @('SeedDomain','TypedEvidenceRequired','CausalEvidenceRequired')) {
            Add-ContractIssue $issues ((Get-JsonPropertyValue $plan $name) -is [string] -and
                -not [string]::IsNullOrWhiteSpace((Get-JsonPropertyValue $plan $name))) "candidate-map.Domains.$index.DiscoveryPlan.$name"
        }
        $verification = Get-JsonPropertyValue $row 'VerificationContract'
        $verificationNames = @($gateNames + @('PromotionGateCount','AbiSafetyGateCount','TotalGateCount'))
        Add-ContractIssue $issues (Test-ExactJsonProperties $verification $verificationNames) "candidate-map.Domains.$index.VerificationContract.exact-properties"
        foreach ($gate in $gateNames) {
            $expectedGateValue = [bool]$identityVerified -and $gate -ceq 'ExactTargetIdentity'
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $verification $gate) $expectedGateValue) "candidate-map.Domains.$index.VerificationContract.$gate"
        }
        foreach ($contract in @(@('PromotionGateCount',10),@('AbiSafetyGateCount',4),@('TotalGateCount',14))) {
            $name = [string]$contract[0]
            $value = Get-JsonPropertyValue $verification $name
            Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
                [int64]$value -eq [int64]$contract[1]) "candidate-map.Domains.$index.VerificationContract.$name"
        }
    }
    return Complete-ContractValidation $issues
}

function Test-RestoreFaultLifecycleContract([object]$Report) {
    $issues = [Collections.Generic.List[string]]::new()
    $required = @(
        'SchemaId','SchemaVersion','ProcessId','ModuleBase','Requested','ArmExportResolved','ArmCallCompleted','ArmIdentityAuthorized',
        'ArmSucceeded','FirstStopCallCompleted','FirstStopRejected','FirstStopIdentityAuthorized','FirstCanUnloadCallCompleted','FirstCanUnloadIdentityAuthorized','FirstCanUnloadRejected','FirstSnapshotVerified',
        'FirstSnapshotModulePresent','FirstSnapshotModuleIdentityMatched','NoFreeLibraryBeforeRetry','FreeLibraryCallCountAtEntry','FreeLibraryCallCountBeforeRetry','FreeLibraryCallCountDeltaBeforeRetry','DllExportThreadCountAtEntry','DllExportThreadCountAtExit',
        'DllExportThreadCountDelta','DllExportIdentityRejectDelta','WaitReadyAuthorizationAttemptCount','WaitReadyAuthorizationRejectedCount','WaitReadyRemoteThreadCreateCount','ArmAuthorizationAttemptCount','ArmAuthorizationRejectedCount','ArmRemoteThreadCreateCount',
        'StopAuthorizationAttemptCount','StopAuthorizationRejectedCount','StopRemoteThreadCreateCount','CanUnloadAuthorizationAttemptCount','CanUnloadAuthorizationRejectedCount','CanUnloadRemoteThreadCreateCount','CleanupOwnerRetained','RetryStopCallCompleted',
        'RetryStopSucceeded','RetryStopIdentityAuthorized','RetryCanUnloadCallCompleted','RetryCanUnloadIdentityAuthorized','RetryCanUnloadSucceeded','FreeLibraryThreadCreated','FreeLibraryThreadCompleted','FreeLibrarySucceeded',
        'FreeLibraryCallCount','FreeLibraryCallCountAtExit','ReleaseIdentityVerified','ReleaseTokenConsumedOnce','VerifiedAbsentSnapshotCount','ModuleAbsent','TargetAliveAfter','Passed'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $Report $required) 'restore-fault.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Report 'SchemaId') -ceq 'God2RestoreFaultLifecycle') 'restore-fault.SchemaId'
    foreach ($contract in @(@('SchemaVersion',1),@('FreeLibraryCallCountAtEntry',0),
            @('FreeLibraryCallCountBeforeRetry',0),@('FreeLibraryCallCountDeltaBeforeRetry',0),
            @('DllExportThreadCountAtEntry',2),@('DllExportThreadCountAtExit',6),@('DllExportThreadCountDelta',4),
            @('DllExportIdentityRejectDelta',0),@('WaitReadyAuthorizationAttemptCount',1),
            @('WaitReadyAuthorizationRejectedCount',0),@('WaitReadyRemoteThreadCreateCount',1),
            @('ArmAuthorizationAttemptCount',1),@('ArmAuthorizationRejectedCount',0),@('ArmRemoteThreadCreateCount',1),
            @('StopAuthorizationAttemptCount',2),@('StopAuthorizationRejectedCount',0),@('StopRemoteThreadCreateCount',2),
            @('CanUnloadAuthorizationAttemptCount',2),@('CanUnloadAuthorizationRejectedCount',0),@('CanUnloadRemoteThreadCreateCount',2),
            @('FreeLibraryCallCount',1),@('FreeLibraryCallCountAtExit',1),@('VerifiedAbsentSnapshotCount',2))) {
        $name=[string]$contract[0]; $value=Get-JsonPropertyValue $Report $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -eq [int64]$contract[1]) "restore-fault.$name"
    }
    foreach ($flag in @('Requested','ArmExportResolved','ArmCallCompleted','ArmIdentityAuthorized','ArmSucceeded',
            'FirstStopCallCompleted','FirstStopRejected','FirstStopIdentityAuthorized','FirstCanUnloadCallCompleted',
            'FirstCanUnloadIdentityAuthorized','FirstCanUnloadRejected','FirstSnapshotVerified','FirstSnapshotModulePresent',
            'FirstSnapshotModuleIdentityMatched','NoFreeLibraryBeforeRetry','CleanupOwnerRetained','RetryStopCallCompleted',
            'RetryStopSucceeded','RetryStopIdentityAuthorized','RetryCanUnloadCallCompleted','RetryCanUnloadIdentityAuthorized',
            'RetryCanUnloadSucceeded','FreeLibraryThreadCreated','FreeLibraryThreadCompleted','FreeLibrarySucceeded',
            'ReleaseIdentityVerified','ReleaseTokenConsumedOnce','ModuleAbsent','TargetAliveAfter','Passed')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Report $flag) $true) "restore-fault.$flag"
    }
    $reportProcessId=Get-JsonPropertyValue $Report 'ProcessId'; $reportModuleBase=Get-JsonPropertyValue $Report 'ModuleBase'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $reportProcessId) -and [uint64]$reportProcessId -gt 0) 'restore-fault.ProcessId'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $reportModuleBase) -and [uint64]$reportModuleBase -gt 0) 'restore-fault.ModuleBase'
    return Complete-ContractValidation $issues
}

function Test-BridgeAwareStatusV3SchemaContract([object]$Schema) {
    $issues = [Collections.Generic.List[string]]::new()
    $expected = @(
        'SchemaVersion','GeneratedAtUtc','AuthorityProfile','OfficialRuntimeObserved',
        'ExtendedReady','NetworkBridgeReady','InternalBridgeReady','StrictUnloadVerified',
        'ModuleAbsent','TargetAliveAfterUnload','BridgeSchemaId','BridgePhase','BridgeState',
        'BridgeNoDllPointers','DllPointerCount','WritableExecutablePageCount','ObserverRundown',
        'PendingApplicationCallbacks','ExactPersistentOriginalPointerCount','ExactEntryPointerCount',
        'ExactCompletionThunkCount','IatRestored','HotpatchRestored','RestoreFaultLifecyclePassed',
        'RestoreFaultFreeLibraryCallCount','RestoreFaultVerifiedAbsentSnapshotCount',
        'ProductEvidenceSHA256','NativeEvidenceSHA256','RestoreFaultEvidenceSHA256'
    )
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Schema '$id') -ceq
        'https://god2.local/schemas/enhanced-capture-status-v3.schema.json') 'status-v3-schema.id'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Schema 'type') -ceq 'object' -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $Schema 'additionalProperties') $false)) 'status-v3-schema.closed-root'
    $required = @(Get-JsonPropertyValue $Schema 'required')
    $properties = Get-JsonPropertyValue $Schema 'properties'
    Add-ContractIssue $issues ($required.Count -eq $expected.Count -and
        @($required | Where-Object { $expected -cnotcontains $_ }).Count -eq 0) 'status-v3-schema.required-exact'
    Add-ContractIssue $issues (Test-ExactJsonProperties $properties $expected) 'status-v3-schema.properties-exact'
    foreach ($contract in @(
            @('SchemaVersion','god2-enhanced-capture-status-v3'),
            @('NetworkBridgeReady',$true),@('StrictUnloadVerified',$true),
            @('ModuleAbsent',$true),@('TargetAliveAfterUnload',$true),
            @('BridgeSchemaId','god2-unload-neutral-bridge-v1'),
            @('BridgePhase','Detached'),@('BridgeState','RetainedPassThrough'),
            @('BridgeNoDllPointers',$true),@('DllPointerCount',0),
            @('WritableExecutablePageCount',0),@('ObserverRundown',0),
            @('PendingApplicationCallbacks',0),@('ExactPersistentOriginalPointerCount',7),
            @('ExactEntryPointerCount',11),@('ExactCompletionThunkCount',256),
            @('IatRestored',$true),@('HotpatchRestored',$true),
            @('RestoreFaultLifecyclePassed',$true),@('RestoreFaultFreeLibraryCallCount',1),
            @('RestoreFaultVerifiedAbsentSnapshotCount',2))) {
        $field = Get-JsonPropertyValue $properties ([string]$contract[0])
        $actual = Get-JsonPropertyValue $field 'const'
        $expectedValue = $contract[1]
        $passed = if ($expectedValue -is [bool]) {
            Test-JsonBooleanValue $actual ([bool]$expectedValue)
        } else { $actual -ceq $expectedValue }
        Add-ContractIssue $issues $passed "status-v3-schema.const.$($contract[0])"
    }
    $profiles = @(Get-JsonPropertyValue $Schema 'allOf')
    Add-ContractIssue $issues ($profiles.Count -eq 2) 'status-v3-schema.profile-count'
    $profileText = $profiles | ConvertTo-Json -Depth 20 -Compress
    foreach ($token in @('OfficialExactClient','TestOnlyExactFixture','OfficialRuntimeObserved','ExtendedReady','InternalBridgeReady')) {
        Add-ContractIssue $issues ($profileText.IndexOf($token, [StringComparison]::Ordinal) -ge 0) "status-v3-schema.profile.$token"
    }
    return Complete-ContractValidation $issues
}

function Test-BridgeAwareStatusV3Contract([object]$Status) {
    $issues = [Collections.Generic.List[string]]::new()
    $expected = @(
        'SchemaVersion','GeneratedAtUtc','AuthorityProfile','OfficialRuntimeObserved',
        'ExtendedReady','NetworkBridgeReady','InternalBridgeReady','StrictUnloadVerified',
        'ModuleAbsent','TargetAliveAfterUnload','BridgeSchemaId','BridgePhase','BridgeState',
        'BridgeNoDllPointers','DllPointerCount','WritableExecutablePageCount','ObserverRundown',
        'PendingApplicationCallbacks','ExactPersistentOriginalPointerCount','ExactEntryPointerCount',
        'ExactCompletionThunkCount','IatRestored','HotpatchRestored','RestoreFaultLifecyclePassed',
        'RestoreFaultFreeLibraryCallCount','RestoreFaultVerifiedAbsentSnapshotCount',
        'ProductEvidenceSHA256','NativeEvidenceSHA256','RestoreFaultEvidenceSHA256'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $Status $expected) 'status-v3.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Status 'SchemaVersion') -ceq 'god2-enhanced-capture-status-v3') 'status-v3.version'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Status 'GeneratedAtUtc') -is [string] -and
        [string](Get-JsonPropertyValue $Status 'GeneratedAtUtc') -cmatch '^\d{4}-\d{2}-\d{2}T') 'status-v3.generated-at'
    $profile = Get-JsonPropertyValue $Status 'AuthorityProfile'
    Add-ContractIssue $issues ($profile -cin @('OfficialExactClient','TestOnlyExactFixture')) 'status-v3.profile'
    foreach ($field in @('NetworkBridgeReady','StrictUnloadVerified','ModuleAbsent','TargetAliveAfterUnload',
            'BridgeNoDllPointers','IatRestored','HotpatchRestored','RestoreFaultLifecyclePassed')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Status $field) $true) "status-v3.$field"
    }
    foreach ($field in @('OfficialRuntimeObserved','ExtendedReady','InternalBridgeReady')) {
        $expectedValue = $profile -ceq 'OfficialExactClient'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Status $field) $expectedValue) "status-v3.$field"
    }
    foreach ($contract in @(
            @('BridgeSchemaId','god2-unload-neutral-bridge-v1'),@('BridgePhase','Detached'),
            @('BridgeState','RetainedPassThrough'),@('DllPointerCount',0),
            @('WritableExecutablePageCount',0),@('ObserverRundown',0),
            @('PendingApplicationCallbacks',0),@('ExactPersistentOriginalPointerCount',7),
            @('ExactEntryPointerCount',11),@('ExactCompletionThunkCount',256),
            @('RestoreFaultFreeLibraryCallCount',1),@('RestoreFaultVerifiedAbsentSnapshotCount',2))) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $Status ([string]$contract[0])) -ceq $contract[1]) "status-v3.$($contract[0])"
    }
    foreach ($field in @('ProductEvidenceSHA256','NativeEvidenceSHA256','RestoreFaultEvidenceSHA256')) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $Status $field) -is [string] -and
            [string](Get-JsonPropertyValue $Status $field) -cmatch '^[0-9A-F]{64}$') "status-v3.$field"
    }
    return Complete-ContractValidation $issues
}

function Test-ProductRuntimeContract([object]$Product) {
    $issues = [Collections.Generic.List[string]]::new()
    $requiredTop = @(
        'Status','FailureCount','Failures','AttachResultRecord',
        'AttachResultPath','AttachResultSHA256','DetachResultRecord','DetachResultPath',
        'DetachResultSHA256','TargetExecutable','TargetArchitecture','ReadinessHandshake',
        'StopHandshake','SafeUnloadHandshake','ModuleUnloaded','ModuleResidentInactive',
        'ModuleSnapshotVerified','ModuleAbsent','UnloadSafe','ExtraReferenceNegativeFirstFreeLibraryStillPresent',
        'ExtraReferenceNegativeNotClaimedUnloaded','ExtraReferenceReleasedThenModuleAbsent','BlockingTargetAliveAtDetach','BlockingAllReturnedAtDetach',
        'BlockingModuleAbsentBeforeRelease','BlockingReleaseIssued','BlockingAllReturnedAfterRelease','BlockingDetachObservedUtc',
        'BlockingReleaseIssuedUtc','BlockingAllReturnedObservedUtc','BlockingTargetStdoutPath','BlockingTargetStdoutSHA256',
        'BlockingPostUnloadResultPath','BlockingPostUnloadResultSHA256','BlockingPostUnloadResult','BlockingPostUnloadDuplicateNegativeCount',
        'MetadataPath','MetadataSHA256','MetadataRecordCount','MetadataSequenceContractPassed',
        'MatchingTransmittedSendCount','ExpectedTransmittedSendPayloadSHA256','MetadataReadableWhileAttached','SensitiveOutboundSinkRecordsPassed',
        'SensitiveOutboundSinkRecords','ResidualInjectorProcess','ResidualGameProcess','ProductionIdentityGateRecord',
        'ProductionIdentityGatePath','ProductionIdentityGateSHA256','StrictInjectorResultParserNegativeCount','ProductionWrongIdentityRejectedBeforeInjection',
        'WrongIdentityNeverPublishedDerivedFromInjectorNoLoad','WrongIdentitySampledSnapshotSupportOnly','WrongIdentitySampledSnapshotPath','WrongIdentitySampledSnapshotSHA256',
        'WrongIdentitySampledSnapshotRecord','WrongIdentitySnapshotDuplicateNegativeCount','PositiveLifecycleIdentityMode','PositiveLifecycleIdentityVerified',
        'PositiveLifecycleClientVersion','PositiveLifecycleClientSHA256','PositiveLifecycleProcessId','PositiveLifecycleProcessCreationTime',
        'PositiveLifecycleProbeSHA256','ProductionProbeSHA256','TestInjectorSHA256','ProductionInjectorSHA256',
        'OfficialClientPositiveRuntimeObserved','SharedTransportVersion','SharedTransportProducerReady','SharedTransportProducerClosed',
        'SharedTransportConsumerReady','SharedTransportConsumerClosed','SharedTransportConsumerFailure',
        'SharedTransportConsumerDrainVerified','SharedTransportDomainPendingTotal','TargetProcessId','SharedTransportProducerProcessId',
        'SharedTransportConsumerProcessId','NativeProbeProcessId','NativeSemanticWireProcessIds','SharedTransportCrossProcess',
        'SharedTransportCrossProcessNegativeCount','SharedTransportAttempted','SharedTransportAccepted','SharedTransportDropped',
        'SharedTransportSampled','SharedTransportHighWaterMark','SharedTransportConsumerLag',
        'SharedTransportLaneContractPassed','SharedTransportLaneResults',
        'SharedTransportDomainDropTotal','SharedTransportDomainWriteFailureTotal','SharedTransportDomainPendingAfterDrain','SharedTransportDomainsWithAcceptedHighWater',
        'SharedTransportDomainsWithDropAccounting','SharedTransportConsumedPayloads','SharedTransportSequenceOrdered','SharedTransportInvalidPayloads',
        'SharedTransportWirePriorityCounts','SharedTransportPriorityMismatches','SharedTransportBatchMaximumItems','SharedTransportBatchAccepted',
        'SharedTransportBatchCount','SharedTransportBatchEventSignalDelta','SharedTransportBatchLockAcquisitionDelta','SharedTransportBatchContractVerifiedByNativeExport',
        'SemanticEventTypeFixtureCount','SemanticEventTypeDistinctCount','SemanticEventTypeNativeCount','SemanticEventTypeDigestSHA256',
        'NativeProbeSelfTestPath','NativeProbeSelfTestSHA256','NativeProbeDuplicateNegativeCount','NativeProbeSelfTestSchemaId',
        'NativeProbeSelfTestSchemaVersion','NativeProbeIdentityProfile','SensitiveOutboundPrivacyPassed','SensitiveOutboundPrivacyRecord',
        'NativeSemanticWirePath','NativeSemanticWireSHA256','NativeSemanticWireCount','NativeSemanticWireVerificationPath',
        'NativeSemanticWireVerificationSHA256','NativeSemanticWireVerificationSchemaId','NativeSemanticWireVerificationSchemaVersion','NativeSemanticWireVerificationReport',
        'UltimateNativeWireReportPath','UltimateNativeWireReportSHA256','UltimateNativeWireReport','DeepProbeCandidateMapPath',
        'DeepProbeCandidateMapSHA256','DeepProbeCandidateMapSchemaVersion','DeepProbeCandidateMapDuplicateNegativeCount','ProbeDomainCount',
        'ConfirmedContractDomainCount','CandidateOnlyBlockedDomainCount','ProbeDomainWirePath','ProbeDomainWireSHA256',
        'ProbeDomainWireDuplicateNegativeCount','ProbeDomainResults','CandidatePromotionGateCount','CandidateAbiSafetyGateCount',
        'CandidateTotalGateCount','CandidateActivationGateMatrixValid','CandidateActivationPassedGateCount','CandidateActivationRejectedGateCount',
        'CandidateGateResults','SemanticAdmissionCapContractPassed','SemanticAdmissionCapSharedLedgerPassed','SemanticAdmissionCapLiveStateUntouched',
        'SemanticAdmissionHardLimit','SemanticAdmissionLowPriorityCeiling','SemanticAdmissionCapResults','FaultDiagnosticConsumerAckPath',
        'FaultDiagnosticConsumerAckSHA256','FaultDiagnosticConsumerAckArtifactVerified','FaultDiagnosticConsumerAck','FaultDiagnosticCrossProcessObserved',
        'FaultDiagnosticPriorityPolicyPassed','FaultDiagnosticSelectedPriority','StartupProbeDiagnosticSelectedPriority','FaultDiagnosticAcceptedAfterLowPriorityCeiling',
        'FaultDiagnosticAcceptedBeyondLegacyHardLimit','FaultDiagnosticBeyondLegacyLimitDropReason','NativeProbeSelfTestReport'
    )
    Add-ContractIssue $issues (Test-ExactJsonProperties $Product $requiredTop) 'product.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'Status') -ceq 'PASS') 'product.Status'
    $failureCount = Get-JsonPropertyValue $Product 'FailureCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $failureCount) -and
        [int64]$failureCount -eq 0) 'product.FailureCount'
    Add-ContractIssue $issues (@(Get-JsonPropertyValue $Product 'Failures').Count -eq 0) 'product.Failures'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'TargetExecutable') -ceq 'God2_opt.exe') 'product.TargetExecutable'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'TargetArchitecture') -ceq 'x86') 'product.TargetArchitecture'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'ReadinessHandshake') -ceq 'God2TraceProbeWaitReady') 'product.ReadinessHandshake'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'StopHandshake') -ceq 'God2TraceProbeStop') 'product.StopHandshake'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'SafeUnloadHandshake') -ceq 'God2TraceProbeCanUnload') 'product.SafeUnloadHandshake'
    $attachResult = Get-JsonPropertyValue $Product 'AttachResultRecord'
    $detachResult = Get-JsonPropertyValue $Product 'DetachResultRecord'
    $identityGate = Get-JsonPropertyValue $Product 'ProductionIdentityGateRecord'
    $injectorContracts = @(
        @('AttachResultRecord',$attachResult,'Attach'),
        @('DetachResultRecord',$detachResult,'Detach'),
        @('ProductionIdentityGateRecord',$identityGate,'IdentityBlocked')
    )
    foreach ($contractSpec in $injectorContracts) {
        $contract = Test-InjectorResultV2Contract $contractSpec[1] $contractSpec[2]
        foreach ($issue in @($contract.Issues)) {
            $issues.Add("product.$($contractSpec[0]).$issue")
        }
    }
    foreach ($rawBinding in @(
            @('AttachResultPath','AttachResultSHA256','attach-result-v2\.json'),
            @('DetachResultPath','DetachResultSHA256','detach-result-v2\.json'),
            @('ProductionIdentityGatePath','ProductionIdentityGateSHA256','production-identity-gate-result-v2\.json'))) {
        $pathName = [string]$rawBinding[0]
        $shaName = [string]$rawBinding[1]
        $path = Get-JsonPropertyValue $Product $pathName
        $sha = Get-JsonPropertyValue $Product $shaName
        Add-ContractIssue $issues ($path -is [string] -and
            $path -cmatch "^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/analysis/$($rawBinding[2])$" -and
            $path -cnotmatch '^[A-Za-z]:|\\|\.\.') "product.$pathName"
        Add-ContractIssue $issues ($sha -is [string] -and
            $sha -cmatch '^[0-9A-F]{64}$') "product.$shaName"
    }
    $strictNegativeCount = Get-JsonPropertyValue $Product 'StrictInjectorResultParserNegativeCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $strictNegativeCount) -and
        [int64]$strictNegativeCount -ge 10) 'product.StrictInjectorResultParserNegativeCount'
    foreach ($name in @('injectionMode','suspendedThreadCount','primaryThreadId',
            'moduleWasEverLoaded','moduleLoadStateVerified','targetIdentityVerified',
            'extraReferenceRequested','extraReferenceLoaded')) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $attachResult $name) -ceq
            (Get-JsonPropertyValue $detachResult $name)) "product.AttachDetach.$name.reconciliation"
    }
    foreach ($binding in @(
            @('ModuleUnloaded','moduleUnloaded'),@('ModuleResidentInactive','moduleResidentInactive'),
            @('ModuleSnapshotVerified','moduleSnapshotVerified'),@('ModuleAbsent','moduleAbsent'),
            @('UnloadSafe','unloadSafe'),
            @('ExtraReferenceNegativeFirstFreeLibraryStillPresent','extraReferenceNegativeFirstFreeLibraryStillPresent'),
            @('ExtraReferenceNegativeNotClaimedUnloaded','extraReferenceNegativeNotClaimedUnloaded'),
            @('ExtraReferenceReleasedThenModuleAbsent','extraReferenceReleasedThenModuleAbsent'))) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $Product $binding[0]) -ceq
            (Get-JsonPropertyValue $detachResult $binding[1])) "product.DetachResultRecord.$($binding[0]).top-reconciliation"
    }

    foreach ($flag in @(
            'ModuleUnloaded','ModuleSnapshotVerified','ModuleAbsent','UnloadSafe',
            'ExtraReferenceNegativeFirstFreeLibraryStillPresent',
            'ExtraReferenceNegativeNotClaimedUnloaded','ExtraReferenceReleasedThenModuleAbsent',
            'MetadataReadableWhileAttached','SharedTransportProducerReady',
            'SharedTransportProducerClosed','SharedTransportConsumerClosed',
            'SharedTransportConsumerDrainVerified','SharedTransportLaneContractPassed',
            'SharedTransportSequenceOrdered',
            'SharedTransportBatchContractVerifiedByNativeExport','SharedTransportCrossProcess',
            'CandidateActivationGateMatrixValid','SemanticAdmissionCapContractPassed',
            'SemanticAdmissionCapSharedLedgerPassed','SemanticAdmissionCapLiveStateUntouched')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product $flag) $true) "product.$flag"
    }
    foreach ($flag in @('ModuleResidentInactive','ResidualInjectorProcess','ResidualGameProcess',
            'SharedTransportConsumerReady')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product $flag) $false) "product.$flag"
    }
    foreach ($countContract in @(
            @('ProbeDomainCount',25),@('ConfirmedContractDomainCount',4),
            @('CandidateOnlyBlockedDomainCount',21),
            @('SemanticEventTypeFixtureCount',25),@('SemanticEventTypeDistinctCount',25),
            @('SemanticEventTypeNativeCount',25),@('CandidatePromotionGateCount',10),
            @('CandidateAbiSafetyGateCount',4),@('CandidateTotalGateCount',14),
            @('SharedTransportVersion',4),@('SharedTransportConsumerFailure',0),
            @('SharedTransportConsumerLag',0),@('SharedTransportDomainWriteFailureTotal',2),
            @('SharedTransportDomainPendingTotal',0),
            @('SharedTransportDomainPendingAfterDrain',0),
            @('SharedTransportDomainsWithAcceptedHighWater',25),
            @('SharedTransportInvalidPayloads',0),@('SharedTransportPriorityMismatches',0),
            @('SharedTransportCrossProcessNegativeCount',2),
            @('SemanticAdmissionHardLimit',2000000),
            @('SemanticAdmissionLowPriorityCeiling',1500000))) {
        $name = [string]$countContract[0]
        $value = Get-JsonPropertyValue $Product $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -eq [int64]$countContract[1]) "product.$name"
    }
    $metadataRecordCount = Get-JsonPropertyValue $Product 'MetadataRecordCount'
    $matchingSendCount = Get-JsonPropertyValue $Product 'MatchingTransmittedSendCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $metadataRecordCount) -and
        [int64]$metadataRecordCount -gt 0) 'product.MetadataRecordCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $matchingSendCount) -and
        [int64]$matchingSendCount -eq 1) 'product.MatchingTransmittedSendCount'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'SemanticEventTypeDigestSHA256') -ceq
        'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C') 'product.SemanticEventTypeDigestSHA256'

    $targetPid = Get-JsonPropertyValue $Product 'TargetProcessId'
    $producerPid = Get-JsonPropertyValue $Product 'SharedTransportProducerProcessId'
    $consumerPid = Get-JsonPropertyValue $Product 'SharedTransportConsumerProcessId'
    $nativePid = Get-JsonPropertyValue $Product 'NativeProbeProcessId'
    foreach ($pidContract in @(@('TargetProcessId',$targetPid),
            @('SharedTransportProducerProcessId',$producerPid),
            @('SharedTransportConsumerProcessId',$consumerPid),
            @('NativeProbeProcessId',$nativePid))) {
        Add-ContractIssue $issues ((Test-JsonIntegerValue $pidContract[1]) -and
            [uint64]$pidContract[1] -gt 0 -and [uint64]$pidContract[1] -le [uint32]::MaxValue) "product.$($pidContract[0])"
    }
    Add-ContractIssue $issues ((Test-JsonIntegerValue $targetPid) -and
        (Test-JsonIntegerValue $producerPid) -and (Test-JsonIntegerValue $nativePid) -and
        [uint64]$producerPid -eq [uint64]$targetPid -and
        [uint64]$nativePid -eq [uint64]$targetPid) 'product.SharedTransport.producer-native-target-pid-binding'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $consumerPid) -and
        (Test-JsonIntegerValue $targetPid) -and [uint64]$consumerPid -ne [uint64]$targetPid) 'product.SharedTransport.consumer-pid-separation'
    $wirePids = @(Get-JsonPropertyValue $Product 'NativeSemanticWireProcessIds')
    Add-ContractIssue $issues ($wirePids.Count -eq 25) 'product.NativeSemanticWireProcessIds.count'
    for ($index = 0; $index -lt $wirePids.Count; $index++) {
        Add-ContractIssue $issues ((Test-JsonIntegerValue $wirePids[$index]) -and
            (Test-JsonIntegerValue $targetPid) -and
            [uint64]$wirePids[$index] -eq [uint64]$targetPid) "product.NativeSemanticWireProcessIds.$index"
    }

    $nativePath = Get-JsonPropertyValue $Product 'NativeProbeSelfTestPath'
    $nativeSha = Get-JsonPropertyValue $Product 'NativeProbeSelfTestSHA256'
    Add-ContractIssue $issues ($nativePath -is [string] -and
        $nativePath -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/raw/trace/native-probe-selftest\.json$' -and
        $nativePath -cnotmatch '^[A-Za-z]:|\\|\.\.') 'product.NativeProbeSelfTestPath'
    Add-ContractIssue $issues ($nativeSha -is [string] -and
        $nativeSha -cmatch '^[0-9A-F]{64}$') 'product.NativeProbeSelfTestSHA256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'NativeProbeSelfTestSchemaId') -ceq
        'God2NativeProbeSelfTest') 'product.NativeProbeSelfTestSchemaId'
    $nativeSchemaVersion = Get-JsonPropertyValue $Product 'NativeProbeSelfTestSchemaVersion'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $nativeSchemaVersion) -and
        [int64]$nativeSchemaVersion -eq 1) 'product.NativeProbeSelfTestSchemaVersion'
    $native = Get-JsonPropertyValue $Product 'NativeProbeSelfTestReport'
    $nativeContract = Test-NativeProbeRuntimeContract $native
    foreach ($issue in @($nativeContract.Issues)) {
        $issues.Add("product.NativeProbeSelfTestReport.$issue")
    }
    Add-ContractIssue $issues ((Get-JsonPropertyValue $native 'ProcessId') -ceq $nativePid) 'product.NativeProbeSelfTestReport.ProcessId.pid-binding'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'NativeProbeIdentityProfile') -ceq 'TestOnlyExactFixture') 'product.NativeProbeIdentityProfile'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'PositiveLifecycleIdentityMode') -ceq 'TestOnlyExactFixture') 'product.PositiveLifecycleIdentityMode'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product 'PositiveLifecycleIdentityVerified') $true) 'product.PositiveLifecycleIdentityVerified'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product 'OfficialClientPositiveRuntimeObserved') $false) 'product.OfficialClientPositiveRuntimeObserved'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product 'SensitiveOutboundPrivacyPassed') $true) 'product.SensitiveOutboundPrivacyPassed'
    $productPrivacy = Get-JsonPropertyValue $Product 'SensitiveOutboundPrivacyRecord'
    $nativePrivacy = Get-JsonPropertyValue $native 'SensitiveOutboundPrivacy'
    Add-ContractIssue $issues (Test-JsonDeepEqual $productPrivacy $nativePrivacy) 'product.SensitiveOutboundPrivacyRecord.native-binding'
    foreach ($flag in @('MetadataSequenceContractPassed','SensitiveOutboundSinkRecordsPassed',
            'BlockingTargetAliveAtDetach','BlockingModuleAbsentBeforeRelease','BlockingReleaseIssued','BlockingAllReturnedAfterRelease')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product $flag) $true) "product.$flag"
    }
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Product 'BlockingAllReturnedAtDetach') $false) 'product.BlockingAllReturnedAtDetach'
    $blockingDuplicateNegatives = Get-JsonPropertyValue $Product 'BlockingPostUnloadDuplicateNegativeCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $blockingDuplicateNegatives) -and [int64]$blockingDuplicateNegatives -ge 3) 'product.BlockingPostUnloadDuplicateNegativeCount'
    foreach ($binding in @(@('MetadataPath','MetadataSHA256'),@('BlockingTargetStdoutPath','BlockingTargetStdoutSHA256'),
            @('BlockingPostUnloadResultPath','BlockingPostUnloadResultSHA256'))) {
        $pathValue=Get-JsonPropertyValue $Product $binding[0]; $shaValue=Get-JsonPropertyValue $Product $binding[1]
        Add-ContractIssue $issues ($pathValue -is [string] -and $pathValue -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/' -and
            $pathValue -cnotmatch '^[A-Za-z]:|\\|\.\.') "product.$($binding[0])"
        Add-ContractIssue $issues ($shaValue -is [string] -and $shaValue -cmatch '^[0-9A-F]{64}$') "product.$($binding[1])"
    }
    $blocking = Get-JsonPropertyValue $Product 'BlockingPostUnloadResult'
    Add-ContractIssue $issues (Test-ExactJsonProperties $blocking @('schemaId','schemaVersion','targetProcessId',
        'moduleAbsentBeforeRelease','targetAliveAtDetach','allReturnedAtDetach','releaseIssued','allReturnedAfterRelease','rows')) 'product.BlockingPostUnloadResult.exact-properties'
    foreach ($flag in @('moduleAbsentBeforeRelease','targetAliveAtDetach','releaseIssued','allReturnedAfterRelease')) {
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $blocking $flag) $true) "product.BlockingPostUnloadResult.$flag"
    }
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $blocking 'allReturnedAtDetach') $false) 'product.BlockingPostUnloadResult.allReturnedAtDetach'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $blocking 'targetProcessId') -ceq $targetPid) 'product.BlockingPostUnloadResult.targetProcessId'
    $blockingRows=@(Get-JsonPropertyValue $blocking 'rows'); $expectedBlockingApis=@('GetQueuedCompletionStatus','GetQueuedCompletionStatusEx','recv','WSAGetOverlappedResult')
    Add-ContractIssue $issues ($blockingRows.Count -eq 4) 'product.BlockingPostUnloadResult.rows.count'
    for($index=0;$index-lt[Math]::Min(4,$blockingRows.Count);$index++){
        $row=$blockingRows[$index]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @('SchemaId','SchemaVersion','Index','Api','ReleaseBarrierObserved',
            'Returned','ResultPreserved','ResourceReusable','ObservedResult','ObservedTransferred','ObservedFlags','ObservedRemoved',
            'ObservedCompletionKey','InputOverlapped','OutputOverlapped','InputResource','BaselineResource','EntryLastError',
            'BaselineLastError','ObservedLastError','PayloadMatched')) "product.BlockingPostUnloadResult.rows.$index.exact-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Api') -ceq $expectedBlockingApis[$index]) "product.BlockingPostUnloadResult.rows.$index.Api"
        foreach($flag in @('ReleaseBarrierObserved','Returned','ResultPreserved','ResourceReusable','PayloadMatched')){
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row $flag) $true) "product.BlockingPostUnloadResult.rows.$index.$flag"
        }
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'InputResource') -ceq (Get-JsonPropertyValue $row 'BaselineResource')) "product.BlockingPostUnloadResult.rows.$index.resource-identity"
    }
    $bridgeCoverage=@(Get-JsonPropertyValue (Get-JsonPropertyValue $native 'UnloadNeutralBridge') 'BridgeCoverageResults')
    foreach($coverageIndex in @(2,4,5,6)){
        if($coverageIndex -ge $bridgeCoverage.Count){ Add-ContractIssue $issues $false "product.BlockingCoverage.$coverageIndex.missing"; continue }
        $pre=Get-JsonPropertyValue $bridgeCoverage[$coverageIndex] 'PreObserverObserved'; $post=Get-JsonPropertyValue $bridgeCoverage[$coverageIndex] 'PostObserverObserved'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $pre) -and (Test-JsonIntegerValue $post) -and [int64]$pre -gt [int64]$post) "product.BlockingCoverage.$coverageIndex.pre-greater-post"
    }

    $probeResults = @(Get-JsonPropertyValue $Product 'ProbeDomainResults')
    Add-ContractIssue $issues ($probeResults.Count -eq 25) 'product.ProbeDomainResults.count'
    for ($index = 0; $index -lt 25; $index++) {
        $matches = @($probeResults | Where-Object {
            (Get-JsonPropertyValue $_ 'Index') -eq $index
        })
        Add-ContractIssue $issues ($matches.Count -eq 1) "product.ProbeDomainResults.$index.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'Index','Domain','Probe','Status','SlotSequence','SlotDomain','SlotPriority',
                'WireSHA256','PayloadSHA256','EventId','ProcessId','ClientBuildId','SessionId',
                'SourceToken','AuthorityHint','SensitiveMaskStatus','ContractStatus',
                'ActivationAllowedByContract','RuntimeDiagnosticObserved',
                'RuntimeActivationObserved','TargetIdentityVerified')) "product.ProbeDomainResults.$index.required-properties"
        $indexValue = Get-JsonPropertyValue $row 'Index'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $indexValue) -and
            [int64]$indexValue -eq $index) "product.ProbeDomainResults.$index.Index"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Domain') -ceq
            $script:ExpectedNativeProbeDomains[$index]) "product.ProbeDomainResults.$index.Domain"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Probe') -ceq
            $script:ExpectedProbeDomains[$index].Probe) "product.ProbeDomainResults.$index.Probe"
        $expectedStatus = if ($index -lt 4) { 'ConfirmedContract' } else { 'CandidateOnlyBlockedContract' }
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'ContractStatus') -ceq
            $expectedStatus) "product.ProbeDomainResults.$index.ContractStatus"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'ActivationAllowedByContract') ($index -lt 4)) "product.ProbeDomainResults.$index.ActivationAllowedByContract"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'RuntimeDiagnosticObserved') $true) "product.ProbeDomainResults.$index.RuntimeDiagnosticObserved"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'RuntimeActivationObserved') ($index -eq 0)) "product.ProbeDomainResults.$index.RuntimeActivationObserved"
        $expectedIdentityVerified = (Get-JsonPropertyValue $Product 'NativeProbeIdentityProfile') -ceq 'TestOnlyExactFixture'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'TargetIdentityVerified') $expectedIdentityVerified) "product.ProbeDomainResults.$index.TargetIdentityVerified"
    }

    $gateResults = @(Get-JsonPropertyValue $Product 'CandidateGateResults')
    Add-ContractIssue $issues ($gateResults.Count -eq 14) 'product.CandidateGateResults.count'
    foreach ($expectedGate in $script:ExpectedCandidateGates) {
        $matches = @($gateResults | Where-Object {
            (Get-JsonPropertyValue $_ 'Gate') -ceq $expectedGate.Gate
        })
        Add-ContractIssue $issues ($matches.Count -eq 1) "product.CandidateGateResults.$($expectedGate.Gate).unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'Gate','Category','PassedOnAllCandidateDomains','RejectedOnAllCandidateDomains')) "product.CandidateGateResults.$($expectedGate.Gate).required-properties"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'Category') -ceq
            $expectedGate.Category) "product.CandidateGateResults.$($expectedGate.Gate).Category"
        $isIdentityGate = $expectedGate.Gate -ceq 'ExactTargetIdentity'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'PassedOnAllCandidateDomains') $isIdentityGate) "product.CandidateGateResults.$($expectedGate.Gate).PassedOnAllCandidateDomains"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'RejectedOnAllCandidateDomains') (-not $isIdentityGate)) "product.CandidateGateResults.$($expectedGate.Gate).RejectedOnAllCandidateDomains"
    }

    $attempted = Get-JsonPropertyValue $Product 'SharedTransportAttempted'
    $accepted = Get-JsonPropertyValue $Product 'SharedTransportAccepted'
    $dropped = Get-JsonPropertyValue $Product 'SharedTransportDropped'
    $sampled = Get-JsonPropertyValue $Product 'SharedTransportSampled'
    $consumed = Get-JsonPropertyValue $Product 'SharedTransportConsumedPayloads'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $attempted) -and
        (Test-JsonIntegerValue $accepted) -and (Test-JsonIntegerValue $dropped) -and
        (Test-JsonIntegerValue $sampled) -and
        [int64]$attempted -ge 257 -and [int64]$accepted -gt 0 -and
        [int64]$dropped -ge 0 -and [int64]$sampled -eq [int64]$dropped -and
        [int64]$attempted -eq ([int64]$accepted + [int64]$dropped)) 'product.SharedTransport.reconciliation'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $consumed) -and
        (Test-JsonIntegerValue $accepted) -and
        [int64]$consumed -eq [int64]$accepted) 'product.SharedTransportConsumedPayloads'
    $domainDropTotal = Get-JsonPropertyValue $Product 'SharedTransportDomainDropTotal'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $domainDropTotal) -and
        (Test-JsonIntegerValue $dropped) -and
        [int64]$domainDropTotal -eq [int64]$dropped) 'product.SharedTransportDomainDropTotal'
    $domainsWithDropAccounting = Get-JsonPropertyValue $Product 'SharedTransportDomainsWithDropAccounting'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $domainsWithDropAccounting) -and
        [int64]$domainsWithDropAccounting -ge 0 -and
        [int64]$domainsWithDropAccounting -le 25 -and
        (([int64]$dropped -eq 0 -and [int64]$domainsWithDropAccounting -eq 0) -or
         ([int64]$dropped -gt 0 -and [int64]$domainsWithDropAccounting -gt 0))) `
        'product.SharedTransportDomainsWithDropAccounting'
    $transportHighWater = Get-JsonPropertyValue $Product 'SharedTransportHighWaterMark'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $transportHighWater) -and
        [int64]$transportHighWater -ge 0 -and [int64]$transportHighWater -le 256) `
        'product.SharedTransportHighWaterMark'
    $laneRows = @(Get-JsonPropertyValue $Product 'SharedTransportLaneResults')
    Add-ContractIssue $issues ($laneRows.Count -eq 4) 'product.SharedTransportLaneResults.count'
    [int64]$laneAcceptedTotal = 0; [int64]$laneDroppedTotal = 0; [int64]$laneSampledTotal = 0
    for ($priority = 0; $priority -lt 4; $priority++) {
        $matches = @($laneRows | Where-Object { (Get-JsonPropertyValue $_ 'Priority') -eq $priority })
        Add-ContractIssue $issues ($matches.Count -eq 1) "product.SharedTransportLaneResults.$priority.unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'Priority','Accepted','Consumed','Dropped','Sampled','FirstDroppedSequence',
                'LastDroppedSequence','LastDropReason','HighWaterMark','ConsumerLag','Passed')) `
            "product.SharedTransportLaneResults.$priority.exact-properties"
        foreach ($name in @('Accepted','Consumed','Dropped','Sampled','FirstDroppedSequence',
                'LastDroppedSequence','LastDropReason','HighWaterMark','ConsumerLag')) {
            Add-ContractIssue $issues (Test-JsonIntegerValue (Get-JsonPropertyValue $row $name)) `
                "product.SharedTransportLaneResults.$priority.$name.type"
        }
        $laneAccepted = [int64](Get-JsonPropertyValue $row 'Accepted')
        $laneConsumed = [int64](Get-JsonPropertyValue $row 'Consumed')
        $laneDropped = [int64](Get-JsonPropertyValue $row 'Dropped')
        $laneSampled = [int64](Get-JsonPropertyValue $row 'Sampled')
        $laneFirst = [int64](Get-JsonPropertyValue $row 'FirstDroppedSequence')
        $laneLast = [int64](Get-JsonPropertyValue $row 'LastDroppedSequence')
        $laneReason = [int64](Get-JsonPropertyValue $row 'LastDropReason')
        $laneHighWater = [int64](Get-JsonPropertyValue $row 'HighWaterMark')
        $laneLag = [int64](Get-JsonPropertyValue $row 'ConsumerLag')
        $noLoss = $laneDropped -eq 0 -and $laneSampled -eq 0 -and
            $laneFirst -eq 0 -and $laneLast -eq 0 -and $laneReason -eq 0
        $sampledLoss = $laneDropped -gt 0 -and $laneSampled -eq $laneDropped -and
            $laneFirst -gt 0 -and $laneLast -ge $laneFirst -and $laneReason -eq 10
        Add-ContractIssue $issues ($laneAccepted -gt 0 -and $laneConsumed -eq $laneAccepted -and
            $laneHighWater -ge 0 -and $laneHighWater -le 64 -and $laneLag -eq 0 -and
            ($noLoss -or ($priority -ge 2 -and $sampledLoss)) -and
            (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'Passed') $true)) `
            "product.SharedTransportLaneResults.$priority.contract"
        $laneAcceptedTotal += $laneAccepted
        $laneDroppedTotal += $laneDropped
        $laneSampledTotal += $laneSampled
    }
    Add-ContractIssue $issues ($laneAcceptedTotal -eq [int64]$accepted -and
        $laneDroppedTotal -eq [int64]$dropped -and
        $laneSampledTotal -eq [int64]$sampled) 'product.SharedTransportLaneResults.reconciliation'
    $priorityCounts = @(Get-JsonPropertyValue $Product 'SharedTransportWirePriorityCounts')
    Add-ContractIssue $issues ($priorityCounts.Count -eq 4) 'product.SharedTransportWirePriorityCounts.count'
    for ($priority = 0; $priority -lt 4; $priority++) {
        $value = if ($priority -lt $priorityCounts.Count) { $priorityCounts[$priority] } else { $null }
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
            [int64]$value -gt 0) "product.SharedTransportWirePriorityCounts.$priority"
    }
    foreach ($binding in @(
            @('SharedTransportBatchMaximumItems','SharedTransportBatchMaximumItems'),
            @('SharedTransportBatchAccepted','SharedTransportBatchAccepted'),
            @('SharedTransportBatchCount','SharedTransportBatchCount'),
            @('SharedTransportBatchEventSignalDelta','SharedTransportBatchEventSignalDelta'),
            @('SharedTransportBatchLockAcquisitionDelta','SharedTransportBatchLockAcquisitionDelta'),
            @('SharedTransportDomainWriteFailureTotal','SharedTransportDomainWriteFailureTotal'))) {
        $productName = [string]$binding[0]
        $nativeName = [string]$binding[1]
        $productValue = Get-JsonPropertyValue $Product $productName
        $nativeValue = Get-JsonPropertyValue $native $nativeName
        Add-ContractIssue $issues ((Test-JsonIntegerValue $productValue) -and
            (Test-JsonIntegerValue $nativeValue) -and
            [int64]$productValue -eq [int64]$nativeValue) "product.$productName.native-binding"
    }
    $nativeDomainRows = @(Get-JsonPropertyValue $native 'SharedTransportDomainResults')
    [int64]$nativeAcceptedTotal = 0
    [int64]$nativeDroppedTotal = 0
    foreach ($row in $nativeDomainRows) {
        $rowAccepted = Get-JsonPropertyValue $row 'Accepted'
        $rowDropped = Get-JsonPropertyValue $row 'Dropped'
        if (Test-JsonIntegerValue $rowAccepted) { $nativeAcceptedTotal += [int64]$rowAccepted }
        if (Test-JsonIntegerValue $rowDropped) { $nativeDroppedTotal += [int64]$rowDropped }
    }
    Add-ContractIssue $issues ((Test-JsonIntegerValue $accepted) -and
        [int64]$accepted -eq $nativeAcceptedTotal) 'product.SharedTransportAccepted.native-domain-reconciliation'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $dropped) -and
        (Test-JsonIntegerValue $domainDropTotal) -and
        [int64]$dropped -eq $nativeDroppedTotal -and
        [int64]$domainDropTotal -eq $nativeDroppedTotal) 'product.SharedTransportDropped.native-domain-reconciliation'

    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'SharedTransportConsumerDrainVerified') -ceq
        (Get-JsonPropertyValue $native 'SharedTransportConsumerDrainVerified')) 'product.SharedTransportConsumerDrainVerified.native-binding'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'SharedTransportDomainPendingTotal') -ceq
        (Get-JsonPropertyValue $Product 'SharedTransportDomainPendingAfterDrain')) 'product.SharedTransportDomainPendingTotal.reconciliation'

    $wirePath = Get-JsonPropertyValue $Product 'NativeSemanticWirePath'
    $wireSha = Get-JsonPropertyValue $Product 'NativeSemanticWireSHA256'
    $wireCount = Get-JsonPropertyValue $Product 'NativeSemanticWireCount'
    Add-ContractIssue $issues ($wirePath -is [string] -and
        $wirePath -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/analysis/dll-native-semantic-event-v2\.jsonl$' -and
        $wirePath -cnotmatch '^[A-Za-z]:|\\|\.\.') 'product.NativeSemanticWirePath'
    Add-ContractIssue $issues ($wireSha -is [string] -and
        $wireSha -cmatch '^[0-9A-F]{64}$') 'product.NativeSemanticWireSHA256'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $wireCount) -and
        [int64]$wireCount -eq 25) 'product.NativeSemanticWireCount'

    $wireVerificationPath = Get-JsonPropertyValue $Product 'NativeSemanticWireVerificationPath'
    $wireVerificationSha = Get-JsonPropertyValue $Product 'NativeSemanticWireVerificationSHA256'
    Add-ContractIssue $issues ($wireVerificationPath -is [string] -and
        $wireVerificationPath -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/analysis/native-semantic-wire-verification\.json$' -and
        $wireVerificationPath -cnotmatch '^[A-Za-z]:|\\|\.\.') 'product.NativeSemanticWireVerificationPath'
    Add-ContractIssue $issues ($wireVerificationSha -is [string] -and
        $wireVerificationSha -cmatch '^[0-9A-F]{64}$') 'product.NativeSemanticWireVerificationSHA256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'NativeSemanticWireVerificationSchemaId') -ceq
        'God2NativeSemanticWireVerification') 'product.NativeSemanticWireVerificationSchemaId'
    $wireSchemaVersion = Get-JsonPropertyValue $Product 'NativeSemanticWireVerificationSchemaVersion'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $wireSchemaVersion) -and
        [int64]$wireSchemaVersion -eq 1) 'product.NativeSemanticWireVerificationSchemaVersion'
    $wireVerification = Test-NativeSemanticWireVerificationContract `
        -Report (Get-JsonPropertyValue $Product 'NativeSemanticWireVerificationReport') `
        -ExpectedSha256 $wireSha `
        -ExpectedDigest (Get-JsonPropertyValue $Product 'SemanticEventTypeDigestSHA256')
    foreach ($issue in @($wireVerification.Issues)) {
        $issues.Add("product.NativeSemanticWireVerificationReport.$issue")
    }

    $ultimateWirePath = Get-JsonPropertyValue $Product 'UltimateNativeWireReportPath'
    $ultimateWireSha = Get-JsonPropertyValue $Product 'UltimateNativeWireReportSHA256'
    Add-ContractIssue $issues ($ultimateWirePath -is [string] -and
        $ultimateWirePath -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/analysis/u\.json$' -and
        $ultimateWirePath -cnotmatch '^[A-Za-z]:|\\|\.\.') 'product.UltimateNativeWireReportPath'
    Add-ContractIssue $issues ($ultimateWireSha -is [string] -and
        $ultimateWireSha -cmatch '^[0-9A-F]{64}$') 'product.UltimateNativeWireReportSHA256'
    $ultimateWire = Get-JsonPropertyValue $Product 'UltimateNativeWireReport'
    $ultimateWireContract = Test-UltimateRuntimeContract $ultimateWire 0
    foreach ($issue in @($ultimateWireContract.Issues)) {
        $issues.Add("product.UltimateNativeWireReport.$issue")
    }
    $ultimateWireBinding = Get-JsonPropertyValue `
        (Get-JsonPropertyValue $ultimateWire 'semanticEventMatrix') 'nativeWireBinding'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $ultimateWireBinding 'inputSHA256') -ceq
        $wireSha) 'product.UltimateNativeWireReport.nativeWireBinding.inputSHA256'

    $candidatePath = Get-JsonPropertyValue $Product 'DeepProbeCandidateMapPath'
    $candidateSha = Get-JsonPropertyValue $Product 'DeepProbeCandidateMapSHA256'
    Add-ContractIssue $issues ($candidatePath -is [string] -and
        $candidatePath -cmatch '^Artifacts/ClientInstrumentation/LoginTrial/[A-Za-z0-9._-]+/raw/trace/deep-probe-candidate-map\.json$' -and
        $candidatePath -cnotmatch '^[A-Za-z]:|\\|\.\.') 'product.DeepProbeCandidateMapPath'
    Add-ContractIssue $issues ($candidateSha -is [string] -and
        $candidateSha -cmatch '^[0-9A-F]{64}$') 'product.DeepProbeCandidateMapSHA256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Product 'DeepProbeCandidateMapSchemaVersion') -ceq
        'god2-deep-probe-candidate-map-v2') 'product.DeepProbeCandidateMapSchemaVersion'

    foreach ($bindingName in @('SemanticAdmissionCapContractPassed',
            'SemanticAdmissionCapSharedLedgerPassed','SemanticAdmissionCapLiveStateUntouched',
            'SemanticAdmissionHardLimit','SemanticAdmissionLowPriorityCeiling')) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue $Product $bindingName) -ceq
            (Get-JsonPropertyValue $native $bindingName)) "product.$bindingName.native-binding"
    }
    Add-ContractIssue $issues (Test-JsonDeepEqual `
        (Get-JsonPropertyValue $Product 'SemanticAdmissionCapResults') `
        (Get-JsonPropertyValue $native 'SemanticAdmissionCapResults')) 'product.SemanticAdmissionCapResults.native-binding'
    return Complete-ContractValidation $issues
}
function Test-UltimateRuntimeContract([object]$Ultimate, [int]$ExitCode) {
    $issues = [Collections.Generic.List[string]]::new()
    Add-ContractIssue $issues ($ExitCode -eq 0) 'ultimate.exit-code'
    Add-ContractIssue $issues (Test-ExactJsonProperties $Ultimate @(
            'schemaVersion','evidenceSource','liveEvidenceClaimed','total','passed','failed',
            'bundleZipSha256','importerGateStatus','importerExitCode','semanticEventMatrix','tests')) 'ultimate.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Ultimate 'schemaVersion') -ceq 'god2-ultimate-selftest-report-v1') 'ultimate.schemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Ultimate 'evidenceSource') -ceq
        'DeterministicSyntheticContractFixture') 'ultimate.evidenceSource'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $Ultimate 'liveEvidenceClaimed') $false) 'ultimate.liveEvidenceClaimed'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Ultimate 'bundleZipSha256') -is [string] -and
        (Get-JsonPropertyValue $Ultimate 'bundleZipSha256') -cmatch '^[0-9A-F]{64}$') 'ultimate.bundleZipSha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Ultimate 'importerGateStatus') -ceq
        'RECOVERY_BUNDLE_IMPORTER_EXPECTED_IDENTITY_BLOCK_PASS') 'ultimate.importerGateStatus'
    $importerExitCode = Get-JsonPropertyValue $Ultimate 'importerExitCode'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $importerExitCode) -and
        [int64]$importerExitCode -eq 4) 'ultimate.importerExitCode'
    $total = Get-JsonPropertyValue $Ultimate 'total'; $passed = Get-JsonPropertyValue $Ultimate 'passed'; $failed = Get-JsonPropertyValue $Ultimate 'failed'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $total) -and (Test-JsonIntegerValue $passed) -and
        (Test-JsonIntegerValue $failed) -and [int64]$total -ge 53 -and [int64]$passed -eq [int64]$total -and [int64]$failed -eq 0) 'ultimate.counts'
    $matrix = Get-JsonPropertyValue $Ultimate 'semanticEventMatrix'
    Add-ContractIssue $issues (Test-ExactJsonProperties $matrix @(
            'schemaId','schemaVersion','rows','totals','nativeWireBinding')) 'ultimate.semanticEventMatrix.exact-properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $matrix 'schemaId') -ceq
        'God2SemanticEventMatrix') 'ultimate.semanticEventMatrix.schemaId'
    $matrixSchemaVersion = Get-JsonPropertyValue $matrix 'schemaVersion'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $matrixSchemaVersion) -and
        [int64]$matrixSchemaVersion -eq 2) 'ultimate.semanticEventMatrix.schemaVersion'
    $matrixTotals = Get-JsonPropertyValue $matrix 'totals'
    Add-ContractIssue $issues (Test-ExactJsonProperties $matrixTotals @(
            'fixtureProducerCount','producerImplementedCount','nativeSelfTestProducerCount','blockedCapabilityCount',
            'readerAcceptedCount','dispatchCount','roundTripCount','corruptionRejectedCount',
            'versionRejectedCount','v1CompatibleCount','noPromotionEnforcedCount',
            'nativeWireReaderAcceptedCount','nativeWireDispatchCount')) 'ultimate.semanticEventMatrix.totals.exact-properties'
    foreach ($countContract in @(
            @('fixtureProducerCount',25),@('producerImplementedCount',25),
            @('nativeSelfTestProducerCount',25),
            @('blockedCapabilityCount',20),@('readerAcceptedCount',25),
            @('dispatchCount',25),@('roundTripCount',25),
            @('corruptionRejectedCount',25),@('versionRejectedCount',25),
            @('v1CompatibleCount',25),@('noPromotionEnforcedCount',2),
            @('nativeWireReaderAcceptedCount',25),@('nativeWireDispatchCount',25))) {
        $name = [string]$countContract[0]
        $expected = [int64]$countContract[1]
        $value = Get-JsonPropertyValue $matrixTotals $name
        Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and [int64]$value -eq $expected) "ultimate.semanticEventMatrix.totals.$name"
    }
    $semanticResults = @(Get-JsonPropertyValue $matrix 'rows')
    Add-ContractIssue $issues ($semanticResults.Count -eq 25) 'ultimate.semanticEventMatrix.rows.count'
    for ($index = 0; $index -lt $script:ExpectedSemanticEventTypes.Count; $index++) {
        $expectedEvent = $script:ExpectedSemanticEventTypes[$index]
        $matches = @($semanticResults | Where-Object {
            (Get-JsonPropertyValue $_ 'index') -eq $index -and
            (Get-JsonPropertyValue $_ 'eventType') -ceq $expectedEvent.EventType
        })
        Add-ContractIssue $issues ($matches.Count -eq 1) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).unique"
        if ($matches.Count -ne 1) { continue }
        $row = $matches[0]
        Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                'index','eventType','fixtureProducer','producerImplemented','nativeSelfTestProducer','blockedCapability',
                'readerAccepted','roundTripPassed','corruptionRejected','versionRejected',
                'v1Compatible','dispatchPassed','dispatchSink','promotionPolicy',
                'noPromotionEnforced','nativeWireReaderAccepted','nativeWireDispatchPassed')) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).exact-properties"
        $rowIndex = Get-JsonPropertyValue $row 'index'
        Add-ContractIssue $issues ((Test-JsonIntegerValue $rowIndex) -and
            [int64]$rowIndex -eq $index) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).index"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'dispatchSink') -ceq $expectedEvent.DispatchSink) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).dispatchSink"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'producerImplemented') ([bool]$expectedEvent.RuntimeProducer)) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).producerImplemented"
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'blockedCapability') ($index -ge 5)) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).blockedCapability"
        foreach ($flag in @('fixtureProducer','nativeSelfTestProducer','readerAccepted','roundTripPassed','corruptionRejected',
                'versionRejected','v1Compatible','dispatchPassed','nativeWireReaderAccepted',
                'nativeWireDispatchPassed')) {
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $row $flag) $true) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).$flag"
        }
        $noPromotionExpected = $expectedEvent.EventType -in @('FunctionRoleCandidate','ProbeDiagnostic')
        $expectedPromotion = if ($noPromotionExpected) {
            'NoPromotion'
        } else { 'StandardEvidenceGates' }
        Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'promotionPolicy') -ceq $expectedPromotion) "ultimate.semanticEventMatrix.$($expectedEvent.EventType).promotionPolicy"
        $noPromotionObserved = Get-JsonPropertyValue $row 'noPromotionEnforced'
        $noPromotionMatches = Test-JsonBooleanValue $noPromotionObserved $noPromotionExpected
        Add-ContractIssue $issues $noPromotionMatches "ultimate.semanticEventMatrix.$($expectedEvent.EventType).noPromotionEnforced"
    }
    $nativeWireBinding = Get-JsonPropertyValue $matrix 'nativeWireBinding'
    Add-ContractIssue $issues (Test-ExactJsonProperties $nativeWireBinding @(
            'bound','verified','inputSHA256','eventTypeDigest','inputLineCount')) 'ultimate.semanticEventMatrix.nativeWireBinding.exact-properties'
    $nativeWireBound = Get-JsonPropertyValue $nativeWireBinding 'bound'
    $nativeWireVerified = Get-JsonPropertyValue $nativeWireBinding 'verified'
    Add-ContractIssue $issues (Test-JsonBooleanValue $nativeWireBound $true) 'ultimate.semanticEventMatrix.nativeWireBinding.bound'
    Add-ContractIssue $issues (Test-JsonBooleanValue $nativeWireVerified $true) 'ultimate.semanticEventMatrix.nativeWireBinding.verified'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $nativeWireBinding 'inputSHA256') -is [string] -and
        (Get-JsonPropertyValue $nativeWireBinding 'inputSHA256') -cmatch '^[0-9A-F]{64}$') 'ultimate.semanticEventMatrix.nativeWireBinding.inputSHA256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $nativeWireBinding 'eventTypeDigest') -ceq
        'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C') 'ultimate.semanticEventMatrix.nativeWireBinding.eventTypeDigest'
    $nativeWireLineCount = Get-JsonPropertyValue $nativeWireBinding 'inputLineCount'
    Add-ContractIssue $issues ((Test-JsonIntegerValue $nativeWireLineCount) -and
        [int64]$nativeWireLineCount -eq 25) 'ultimate.semanticEventMatrix.nativeWireBinding.inputLineCount'
    $requiredTests = @(
        '01 SemanticEvent v2 round-trip',
        '02 SemanticEvent corruption rejection',
        '02b SemanticEvent 25-type roundtrip corruption version dispatch matrix',
        '02c DLL native 25-wire cross-runtime reader dispatch binding',
        '03 Shared ring ordering','04 Ring overflow accounting','05 Per-domain backpressure',
        '06 Probe exception isolation','06b Deep-probe candidate promotion hard gate',
        '34 Stop/detach/unload bounded-work contract'
    )
    $tests = @(Get-JsonPropertyValue $Ultimate 'tests')
    foreach ($name in $requiredTests) {
        $matches = @($tests | Where-Object { (Get-JsonPropertyValue $_ 'name') -ceq $name })
        Add-ContractIssue $issues ($matches.Count -eq 1) "ultimate.test.$name.unique"
        if ($matches.Count -eq 1) {
            Add-ContractIssue $issues ((Get-JsonPropertyValue $matches[0] 'status') -ceq 'PASS' -and
                (Get-JsonPropertyValue $matches[0] 'detail') -ceq 'ContractVerifiedWithDeterministicSyntheticFixture') "ultimate.test.$name.pass"
        }
    }
    Add-ContractIssue $issues (@($tests | Where-Object { (Get-JsonPropertyValue $_ 'status') -cne 'PASS' }).Count -eq 0) 'ultimate.all-tests-pass'
    return Complete-ContractValidation $issues
}

function Test-SchemaObjectClosure([object]$Node, [string]$Path,
                                  [Collections.Generic.List[string]]$Issues) {
    if ($null -eq $Node) { return }
    if ($Node -is [array]) {
        for ($index = 0; $index -lt $Node.Count; $index++) {
            Test-SchemaObjectClosure $Node[$index] "$Path[$index]" $Issues
        }
        return
    }
    if ($Node -isnot [pscustomobject]) { return }
    if ((Get-JsonPropertyValue $Node 'type') -ceq 'object') {
        $additional = $Node.PSObject.Properties['additionalProperties']
        if ($null -eq $additional -or -not (Test-JsonBooleanValue $additional.Value $false)) {
            $Issues.Add("schema.open-object:$Path")
        }
    }
    foreach ($property in $Node.PSObject.Properties) {
        Test-SchemaObjectClosure $property.Value "$Path.$($property.Name)" $Issues
    }
}

function Test-AcceptanceSchemaContract([object]$Schema) {
    $issues = [Collections.Generic.List[string]]::new()
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Schema '$id') -ceq
        'https://god2.local/schemas/enhanced-capture-acceptance.schema.json') 'schema.id'
    $properties = Get-JsonPropertyValue $Schema 'properties'
    $schemaVersion = Get-JsonPropertyValue $properties 'schemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $schemaVersion 'const') -ceq
        'god2-enhanced-capture-acceptance-v2') 'schema.version'
    $topRequired = @(Get-JsonPropertyValue $Schema 'required')
    foreach ($name in @('authorityConsistency','deepRecoveryEvidence','runtimeEvidence','inputBindings','freshness','guiVisibility')) {
        Add-ContractIssue $issues ($topRequired -ccontains $name) "schema.required.$name"
    }
    $topProperties = Get-JsonPropertyValue $Schema 'properties'
    $inputBindingsSchema = Get-JsonPropertyValue $topProperties 'inputBindings'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $inputBindingsSchema 'minItems') -eq 40 -and
        (Get-JsonPropertyValue $inputBindingsSchema 'maxItems') -eq 41 -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $inputBindingsSchema 'uniqueItems') $true)) 'schema.input-bindings.count-unique'
    $criticalInputRoles = @('ATTACH_RESULT_EVIDENCE','DETACH_RESULT_EVIDENCE',
        'PRODUCTION_IDENTITY_GATE_EVIDENCE','NATIVE_PROBE_SELFTEST_EVIDENCE',
        'NATIVE_SEMANTIC_WIRE_EVIDENCE','NATIVE_SEMANTIC_WIRE_VERIFICATION_EVIDENCE',
        'ULTIMATE_NATIVE_WIRE_REPORT_EVIDENCE','DEEP_PROBE_CANDIDATE_MAP_EVIDENCE',
        'CANONICAL_V1_SEMANTIC_WIRE_EVIDENCE',
        'CANONICAL_V1_SEMANTIC_WIRE_BINDING_EVIDENCE',
        'NONCANONICAL_V1_COMPATIBILITY_FIXTURE_EVIDENCE',
        'RESTORE_FAULT_LIFECYCLE_EVIDENCE','BRIDGE_AWARE_STATUS_V3_SCHEMA')
    $inputContains = @(Get-JsonPropertyValue $inputBindingsSchema 'allOf')
    Add-ContractIssue $issues ($inputContains.Count -eq 13) 'schema.input-bindings.critical-contains-count'
    foreach ($role in $criticalInputRoles) {
        $matchCount = 0
        foreach ($containsRule in $inputContains) {
            $containsSchema = Get-JsonPropertyValue $containsRule 'contains'
            $containsProperties = Get-JsonPropertyValue $containsSchema 'properties'
            $roleSchema = Get-JsonPropertyValue $containsProperties 'role'
            if ((Get-JsonPropertyValue $roleSchema 'const') -ceq $role -and
                (Get-JsonPropertyValue $containsRule 'minContains') -eq 1 -and
                (Get-JsonPropertyValue $containsRule 'maxContains') -eq 1) {
                $matchCount++
            }
        }
        Add-ContractIssue $issues ($matchCount -eq 1) "schema.input-bindings.critical-role.$role"
    }
    $selfTestsSchema = Get-JsonPropertyValue $topProperties 'selfTests'
    $selfTestsRequired = @(Get-JsonPropertyValue $selfTestsSchema 'required')
    Add-ContractIssue $issues ($selfTestsRequired -ccontains 'evidenceFixture') 'schema.selfTests.required.evidenceFixture'
    $defs = Get-JsonPropertyValue $Schema '$defs'
    $portablePath = Get-JsonPropertyValue $defs 'portablePath'
    $portablePattern = [string](Get-JsonPropertyValue $portablePath 'pattern')
    Add-ContractIssue $issues ($portablePattern -cmatch '\(\?!.*\\\.\\\.' -and
        $portablePattern -cmatch '\(\?!\.\*//\)') 'schema.portable-path.no-parent-or-empty-segment'
    foreach ($name in @(
            'runtimeEvidence','evidencePackageFixture','nativeProbeBinding','strictBoundRecordArtifact',
            'candidateMapArtifactBinding','productArtifactBindings','nativeSemanticWire',
            'probeDomains','probeDomainRuntimeRow','nativeEventProducers','semanticEventMatrix','semanticEventMatrixTotals',
            'semanticEventResult','nativeSemanticWireBinding','activationGates',
            'candidateGateResult','networkCompletion','networkCase','sharedRing','priorityResult',
            'domainCounter','semanticAdmissionCap','semanticAdmissionLowPriorityResult',
            'semanticAdmissionAuthoritativeResult','injectorResultV2','injectorAttachResultV2','injectorDetachResultV2',
            'injectorIdentityBlockedResultV2','lifecycle','faultIsolation','faultIsolationRow','asyncDiagnostics',
             'asyncDiagnosticDomainRow','enhancedCaptureStatusSchemaBinding','bridgeAwareStatusV3Binding',
             'bridgeAwareStatusV3','authorityConsistencyBinding','authorityConsistencyReport',
              'evidenceAuthorityRecord','deepRecoveryEvidenceBinding',
              'deepRecoveryArtifactBinding','inputBinding',
            'freshness','guiVisibility')) {
        Add-ContractIssue $issues ($null -ne $defs.PSObject.Properties[$name]) "schema.def.$name"
    }
    $gui = Get-JsonPropertyValue $defs 'guiVisibility'
    $guiProperties = Get-JsonPropertyValue $gui 'properties'
    $guiRequired = @(Get-JsonPropertyValue $gui 'required')
    $guiKeys = Get-JsonPropertyValue $guiProperties 'expectedSmokeKeys'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $guiKeys 'minItems') -eq 11 -and
        (Get-JsonPropertyValue $guiKeys 'maxItems') -eq 11) 'schema.gui.expectedSmokeKeys.count'
    foreach ($name in @('DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract',
            'DllStrictUnloadFinalizationContract','DllCancelResultReadBeforeCleanupContract',
            'DllEnhancedCaptureConsumerStateLifecycle',
            'ThreeLanguageDllStrictUnloadContract',
            'SharedTransportSlotReuseDomainAccounting','enhancedCaptureStatusSchemaVersion',
            'initialStrictUnloadVerified','expectedExecutableSha256','observedExecutableSha256',
            'executableSha256Bound','unloadPolicy')) {
        Add-ContractIssue $issues ($null -ne $guiProperties.PSObject.Properties[$name]) "schema.gui.$name"
        Add-ContractIssue $issues ($guiRequired -ccontains $name) "schema.gui.required.$name"
    }
    $expectedGuiKeys = @(
        'DllEnhancedCaptureStatusVisible','DllEnhancedCaptureAutoEnabled',
        'ThreeLanguageDllEnhancedCaptureStatus','ThreeLanguageDllEnhancedCaptureHelp',
        'DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract',
        'ThreeLanguageDllStrictUnloadContract','SharedTransportSlotReuseDomainAccounting',
        'DllStrictUnloadFinalizationContract','DllCancelResultReadBeforeCleanupContract',
        'DllEnhancedCaptureConsumerStateLifecycle'
    )
    $guiKeyEnums = @((Get-JsonPropertyValue (Get-JsonPropertyValue $guiKeys 'items') 'enum'))
    Add-ContractIssue $issues ($guiKeyEnums.Count -eq 11 -and
        @($expectedGuiKeys | Where-Object { $guiKeyEnums -cnotcontains $_ }).Count -eq 0 -and
        @($guiKeyEnums | Where-Object { $expectedGuiKeys -cnotcontains $_ }).Count -eq 0) 'schema.gui.expectedSmokeKeys.exact-enum'
    $statusVersionAlternatives = @(Get-JsonPropertyValue (Get-JsonPropertyValue $guiProperties 'enhancedCaptureStatusSchemaVersion') 'oneOf')
    Add-ContractIssue $issues ($statusVersionAlternatives.Count -eq 2 -and
        @($statusVersionAlternatives | Where-Object { (Get-JsonPropertyValue $_ 'const') -ceq 'god2-enhanced-capture-status-v2' }).Count -eq 1 -and
        @($statusVersionAlternatives | Where-Object { (Get-JsonPropertyValue $_ 'type') -ceq 'null' }).Count -eq 1) 'schema.gui.status-schema-version-v2'
    $nativeProducer = Get-JsonPropertyValue $defs 'nativeEventProducers'
    $nativeProperties = Get-JsonPropertyValue $nativeProducer 'properties'
    $digest = Get-JsonPropertyValue (Get-JsonPropertyValue $nativeProperties 'sortedNameDigestSha256') 'const'
    Add-ContractIssue $issues ($digest -ceq 'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C') 'schema.native-event-digest'
    $matrixDef = Get-JsonPropertyValue $defs 'semanticEventMatrix'
    $matrixRequired = @(Get-JsonPropertyValue $matrixDef 'required')
    $matrixProperties = Get-JsonPropertyValue $matrixDef 'properties'
    $matrixRootNames = @('schemaId','schemaVersion','rows','totals','nativeWireBinding')
    Add-ContractIssue $issues ($matrixRequired.Count -eq 5 -and
        @($matrixRootNames | Where-Object { $matrixRequired -cnotcontains $_ }).Count -eq 0 -and
        @($matrixProperties.PSObject.Properties).Count -eq 5) 'schema.semantic-matrix.actual-writer-root-shape'
    $matrixRowsSchema = Get-JsonPropertyValue $matrixProperties 'rows'
    $matrixTotalsSchema = Get-JsonPropertyValue $matrixProperties 'totals'
    $matrixNativeBindingSchema = Get-JsonPropertyValue $matrixProperties 'nativeWireBinding'
    $matrixSchemaIdSchema = Get-JsonPropertyValue $matrixProperties 'schemaId'
    $matrixVersionSchema = Get-JsonPropertyValue $matrixProperties 'schemaVersion'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $matrixSchemaIdSchema 'const') -ceq 'God2SemanticEventMatrix' -and
        (Get-JsonPropertyValue $matrixVersionSchema 'const') -eq 2 -and
        (Get-JsonPropertyValue $matrixRowsSchema 'minItems') -eq 25 -and
        (Get-JsonPropertyValue $matrixRowsSchema 'maxItems') -eq 25 -and
        (Get-JsonPropertyValue $matrixTotalsSchema '$ref') -ceq '#/$defs/semanticEventMatrixTotals' -and
        (Get-JsonPropertyValue $matrixNativeBindingSchema '$ref') -ceq '#/$defs/nativeSemanticWireBinding') 'schema.semantic-matrix.actual-writer-bindings'
    $matrixTotalsDef = Get-JsonPropertyValue $defs 'semanticEventMatrixTotals'
    $matrixTotalsRequired = @(Get-JsonPropertyValue $matrixTotalsDef 'required')
    $matrixTotalsProperties = Get-JsonPropertyValue $matrixTotalsDef 'properties'
    $matrixTotalNames = @(
        'fixtureProducerCount','producerImplementedCount','nativeSelfTestProducerCount','blockedCapabilityCount',
        'readerAcceptedCount','dispatchCount','roundTripCount','corruptionRejectedCount',
        'versionRejectedCount','v1CompatibleCount','noPromotionEnforcedCount',
        'nativeWireReaderAcceptedCount','nativeWireDispatchCount')
    Add-ContractIssue $issues ($matrixTotalsRequired.Count -eq 13 -and
        @($matrixTotalNames | Where-Object { $matrixTotalsRequired -cnotcontains $_ }).Count -eq 0 -and
        @($matrixTotalsProperties.PSObject.Properties).Count -eq 13) 'schema.semantic-matrix.actual-writer-totals-shape'
    $semanticRowDef = Get-JsonPropertyValue $defs 'semanticEventResult'
    $semanticRowRequired = @(Get-JsonPropertyValue $semanticRowDef 'required')
    $semanticRowProperties = Get-JsonPropertyValue $semanticRowDef 'properties'
    $semanticRowNames = @(
        'index','eventType','fixtureProducer','producerImplemented','nativeSelfTestProducer','blockedCapability',
        'readerAccepted','roundTripPassed','corruptionRejected','versionRejected',
        'v1Compatible','dispatchPassed','dispatchSink','promotionPolicy','noPromotionEnforced',
        'nativeWireReaderAccepted','nativeWireDispatchPassed')
    Add-ContractIssue $issues ($semanticRowRequired.Count -eq 17 -and
        @($semanticRowNames | Where-Object { $semanticRowRequired -cnotcontains $_ }).Count -eq 0 -and
        @($semanticRowProperties.PSObject.Properties).Count -eq 17) 'schema.semantic-matrix.actual-writer-row-shape'
    $nativeWireBindingDef = Get-JsonPropertyValue $defs 'nativeSemanticWireBinding'
    $nativeWireBindingRequired = @(Get-JsonPropertyValue $nativeWireBindingDef 'required')
    $nativeWireBindingProperties = Get-JsonPropertyValue $nativeWireBindingDef 'properties'
    $nativeWireBindingNames = @('bound','verified','inputSHA256','eventTypeDigest','inputLineCount')
    Add-ContractIssue $issues ($nativeWireBindingRequired.Count -eq 5 -and
        @($nativeWireBindingNames | Where-Object { $nativeWireBindingRequired -cnotcontains $_ }).Count -eq 0 -and
        @($nativeWireBindingProperties.PSObject.Properties).Count -eq 5) 'schema.semantic-matrix.native-wire-binding-shape'
    $nativeWireDigestSchema = Get-JsonPropertyValue $nativeWireBindingProperties 'eventTypeDigest'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $nativeWireDigestSchema 'const') -ceq
        'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C') 'schema.semantic-matrix.native-wire-digest'
    foreach ($flag in @('bound','verified')) {
        $flagSchema = Get-JsonPropertyValue $nativeWireBindingProperties $flag
        $flagConst = Get-JsonPropertyValue $flagSchema 'const'
        Add-ContractIssue $issues (Test-JsonBooleanValue $flagConst $true) "schema.semantic-matrix.native-wire-$flag"
    }
    $nativeWireLineSchema = Get-JsonPropertyValue $nativeWireBindingProperties 'inputLineCount'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $nativeWireLineSchema 'const') -eq 25) 'schema.semantic-matrix.native-wire-line-count'
    $productArtifactsDef = Get-JsonPropertyValue $defs 'productArtifactBindings'
    $productArtifactsRequired = @(Get-JsonPropertyValue $productArtifactsDef 'required')
    $productArtifactsProperties = Get-JsonPropertyValue $productArtifactsDef 'properties'
    $productArtifactNames = @('productEvidenceSha256','allStrictBound','allGeneratedAfterBoundInputs',
        'attachResult','detachResult','productionIdentityGate','nativeProbe','candidateMap')
    Add-ContractIssue $issues ($productArtifactsRequired.Count -eq 8 -and
        @($productArtifactNames | Where-Object { $productArtifactsRequired -cnotcontains $_ }).Count -eq 0 -and
        @($productArtifactsProperties.PSObject.Properties).Count -eq 8) 'schema.product-artifact-bindings.exact-shape'
    foreach ($flag in @('allStrictBound','allGeneratedAfterBoundInputs')) {
        $flagSchema = Get-JsonPropertyValue $productArtifactsProperties $flag
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $flagSchema 'const') $true) "schema.product-artifact-bindings.$flag"
    }
    $nativeWireDef = Get-JsonPropertyValue $defs 'nativeSemanticWire'
    $nativeWireRequired = @(Get-JsonPropertyValue $nativeWireDef 'required')
    $nativeWireProperties = Get-JsonPropertyValue $nativeWireDef 'properties'
    $nativeWireNames = @(
        'path','sha256','lineCount','eventTypeDigestSha256','targetProcessId',
        'producerProcessId','consumerProcessId','nativeProcessId','wireProcessIds',
        'crossProcess','crossProcessNegativeCount','strictJsonlContractPassed',
        'verificationPath','verificationSha256','verificationSchemaId',
        'verificationSchemaVersion','verificationEmbeddedReportExact','ultimateReportPath',
        'ultimateReportSha256','ultimateEmbeddedReportExact')
    Add-ContractIssue $issues ($nativeWireRequired.Count -eq 20 -and
        @($nativeWireNames | Where-Object { $nativeWireRequired -cnotcontains $_ }).Count -eq 0 -and
        @($nativeWireProperties.PSObject.Properties).Count -eq 20) 'schema.native-semantic-wire.exact-shape'
    foreach ($contract in @(@('lineCount',25),@('crossProcessNegativeCount',2),
            @('verificationSchemaVersion',1))) {
        $field = Get-JsonPropertyValue $nativeWireProperties ([string]$contract[0])
        Add-ContractIssue $issues ((Get-JsonPropertyValue $field 'const') -eq $contract[1]) "schema.native-semantic-wire.$($contract[0])"
    }
    foreach ($flag in @('crossProcess','strictJsonlContractPassed',
            'verificationEmbeddedReportExact','ultimateEmbeddedReportExact')) {
        $field = Get-JsonPropertyValue $nativeWireProperties $flag
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $field 'const') $true) "schema.native-semantic-wire.$flag"
    }
    $semanticCapDef = Get-JsonPropertyValue $defs 'semanticAdmissionCap'
    $semanticCapRequired = @(Get-JsonPropertyValue $semanticCapDef 'required')
    $semanticCapProperties = Get-JsonPropertyValue $semanticCapDef 'properties'
    $semanticCapNames = @('productEvidenceSha256','nativeEvidenceSha256','contractPassed',
        'sharedLedgerPassed','liveStateUntouched','hardLimit','lowPriorityCeiling','results')
    Add-ContractIssue $issues ($semanticCapRequired.Count -eq 8 -and
        @($semanticCapNames | Where-Object { $semanticCapRequired -cnotcontains $_ }).Count -eq 0 -and
        @($semanticCapProperties.PSObject.Properties).Count -eq 8) 'schema.semantic-admission-cap.exact-shape'
    foreach ($flag in @('contractPassed','sharedLedgerPassed','liveStateUntouched')) {
        $flagSchema = Get-JsonPropertyValue $semanticCapProperties $flag
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $flagSchema 'const') $true) "schema.semantic-admission-cap.$flag"
    }
    $hardLimitSchema = Get-JsonPropertyValue $semanticCapProperties 'hardLimit'
    $lowCeilingSchema = Get-JsonPropertyValue $semanticCapProperties 'lowPriorityCeiling'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $hardLimitSchema 'const') -eq 2000000) 'schema.semantic-admission-cap.hardLimit'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $lowCeilingSchema 'const') -eq 1500000) 'schema.semantic-admission-cap.lowPriorityCeiling'
    $sharedRingDef = Get-JsonPropertyValue $defs 'sharedRing'
    $sharedRingRequired = @(Get-JsonPropertyValue $sharedRingDef 'required')
    foreach ($name in @('batchCount','producerReady','producerClosed','consumerDrainVerified',
            'domainPendingTotal','targetProcessId','producerProcessId','consumerProcessId',
            'nativeProcessId','crossProcessNegativeCount')) {
        Add-ContractIssue $issues ($sharedRingRequired -ccontains $name) "schema.shared-ring.required.$name"
    }
    $asyncDef = Get-JsonPropertyValue $defs 'asyncDiagnostics'
    $asyncRequired = @(Get-JsonPropertyValue $asyncDef 'required')
    foreach ($name in @('ringNullFallbackPassed','ringNullSemanticLossCount')) {
        Add-ContractIssue $issues ($asyncRequired -ccontains $name) "schema.async.required.$name"
    }
    $lifecycleDef = Get-JsonPropertyValue $defs 'lifecycle'
    $lifecycleRequired = @(Get-JsonPropertyValue $lifecycleDef 'required')
    $lifecycleProperties = Get-JsonPropertyValue $lifecycleDef 'properties'
    $injectorResultRequired = @(
        'schemaVersion','status','code','injectionAttempted','moduleWasEverLoaded',
        'moduleLoadStateVerified','moduleSnapshotVerified','moduleAbsent','targetProcessExited',
        'targetIdentityVerified','probeReady','stopSucceeded','unloadSafe','moduleUnloaded',
        'moduleResidentInactive','strictUnloadVerified','cleanupRetriable','injectionMode',
        'suspendedThreadCount','primaryThreadId','extraReferenceRequested','extraReferenceLoaded',
        'extraReferenceNegativeFirstFreeLibraryStillPresent',
        'extraReferenceNegativeNotClaimedUnloaded','extraReferenceReleasedThenModuleAbsent'
    )
    $injectorBase = Get-JsonPropertyValue $defs 'injectorResultV2'
    $injectorBaseRequired = @(Get-JsonPropertyValue $injectorBase 'required')
    $injectorBaseProperties = Get-JsonPropertyValue $injectorBase 'properties'
    $injectorBaseAdditional = Get-JsonPropertyValue $injectorBase 'additionalProperties'
    $injectorBaseClosed = Test-JsonBooleanValue $injectorBaseAdditional $false
    Add-ContractIssue $issues ($injectorBaseClosed -and
        $injectorBaseRequired.Count -eq 25 -and
        @($injectorResultRequired | Where-Object { $injectorBaseRequired -cnotcontains $_ }).Count -eq 0 -and
        @($injectorBaseRequired | Where-Object { $injectorResultRequired -cnotcontains $_ }).Count -eq 0 -and
        @($injectorBaseProperties.PSObject.Properties).Count -eq 25) 'schema.injector-v2.exact-25'
    foreach ($name in @(
            'injectionAttempted','moduleWasEverLoaded','moduleLoadStateVerified',
            'moduleSnapshotVerified','moduleAbsent','targetProcessExited','targetIdentityVerified',
            'probeReady','stopSucceeded','unloadSafe','moduleUnloaded','moduleResidentInactive',
            'strictUnloadVerified','cleanupRetriable','extraReferenceRequested',
            'extraReferenceLoaded','extraReferenceNegativeFirstFreeLibraryStillPresent',
            'extraReferenceNegativeNotClaimedUnloaded','extraReferenceReleasedThenModuleAbsent')) {
        $fieldSchema = Get-JsonPropertyValue $injectorBaseProperties $name
        Add-ContractIssue $issues ((Get-JsonPropertyValue $fieldSchema 'type') -ceq 'boolean') "schema.injector-v2.boolean.$name"
    }
    $injectorSpecializations = @(
        @('injectorAttachResultV2','ATTACHED',0,'Attach'),
        @('injectorDetachResultV2','DETACHED',0,'Detach'),
        @('injectorIdentityBlockedResultV2','EVIDENCE_BLOCKED_BUILD_MISMATCH',193,'IdentityBlocked')
    )
    foreach ($specialization in $injectorSpecializations) {
        $definition = Get-JsonPropertyValue $defs $specialization[0]
        $allOf = @(Get-JsonPropertyValue $definition 'allOf')
        $relation = if ($allOf.Count -eq 2) { $allOf[1] } else { $null }
        $relationProperties = Get-JsonPropertyValue $relation 'properties'
        $baseReference = if ($allOf.Count -gt 0) { Get-JsonPropertyValue $allOf[0] '$ref' } else { $null }
        $relationAdditional = Get-JsonPropertyValue $relation 'additionalProperties'
        $relationClosed = Test-JsonBooleanValue $relationAdditional $false
        Add-ContractIssue $issues ($allOf.Count -eq 2 -and
            $baseReference -ceq '#/$defs/injectorResultV2' -and $relationClosed -and
            @($relationProperties.PSObject.Properties).Count -eq 25) "schema.$($specialization[0]).closed-relation"
        $statusSchema = Get-JsonPropertyValue $relationProperties 'status'
        $codeSchema = Get-JsonPropertyValue $relationProperties 'code'
        Add-ContractIssue $issues ((Get-JsonPropertyValue $statusSchema 'const') -ceq $specialization[1]) "schema.$($specialization[0]).status"
        Add-ContractIssue $issues ((Get-JsonPropertyValue $codeSchema 'const') -eq $specialization[2]) "schema.$($specialization[0]).code"
        $expectedRecord = New-InjectorResultV2Fixture $specialization[3]
        foreach ($expectedProperty in $expectedRecord.PSObject.Properties) {
            $fieldSchema = Get-JsonPropertyValue $relationProperties $expectedProperty.Name
            if ($expectedProperty.Name -ceq 'injectionMode' -and $specialization[3] -cne 'IdentityBlocked') {
                $allowedModes = @((Get-JsonPropertyValue $fieldSchema 'enum'))
                Add-ContractIssue $issues ($allowedModes.Count -eq 2 -and
                    $allowedModes -ccontains 'PausePrimaryRemoteThreadResume' -and
                    $allowedModes -ccontains 'PauseResumeThenRemoteThreadComplete') "schema.$($specialization[0]).injectionMode"
            } elseif ($expectedProperty.Name -ceq 'primaryThreadId' -and
                    $specialization[3] -cne 'IdentityBlocked') {
                Add-ContractIssue $issues ((Get-JsonPropertyValue $fieldSchema 'type') -ceq 'integer' -and
                    (Get-JsonPropertyValue $fieldSchema 'minimum') -eq 1) "schema.$($specialization[0]).primaryThreadId"
            } else {
                Add-ContractIssue $issues ((Get-JsonPropertyValue $fieldSchema 'const') -ceq
                    $expectedProperty.Value) "schema.$($specialization[0]).$($expectedProperty.Name)"
            }
        }
    }
    foreach ($binding in @(
            @('attachResult','#/$defs/injectorAttachResultV2'),
            @('detachResult','#/$defs/injectorDetachResultV2'),
            @('productionIdentityGate','#/$defs/injectorIdentityBlockedResultV2'))) {
        Add-ContractIssue $issues ($lifecycleRequired -ccontains $binding[0]) "schema.lifecycle.required.$($binding[0])"
        $bindingSchema = Get-JsonPropertyValue $lifecycleProperties $binding[0]
        Add-ContractIssue $issues ((Get-JsonPropertyValue $bindingSchema '$ref') -ceq $binding[1]) "schema.lifecycle.ref.$($binding[0])"
    }
    foreach ($name in @('attachResultPath','attachResultSha256','detachResultPath',
            'detachResultSha256','productionIdentityGatePath','productionIdentityGateSha256',
            'strictInjectorResultParserNegativeCount','restoreFaultLifecyclePath',
            'restoreFaultLifecycleSha256','restoreFaultLifecycle')) {
        Add-ContractIssue $issues ($lifecycleRequired -ccontains $name) "schema.lifecycle.required.$name"
        Add-ContractIssue $issues ($null -ne $lifecycleProperties.PSObject.Properties[$name]) "schema.lifecycle.property.$name"
    }
    foreach ($name in @(
            'iatOwnerLifecycleSelfTestPassed','iatOwnerGoneRetired','iatAddressReuseNotClobbered',
            'iatReplacementCasRestored','iatOriginalNotClobbered','iatThirdPartyNotClobbered')) {
        Add-ContractIssue $issues ($lifecycleRequired -ccontains $name) "schema.lifecycle.required.$name"
        $fieldSchema = Get-JsonPropertyValue $lifecycleProperties $name
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $fieldSchema 'const') $true) "schema.lifecycle.const.$name"
    }
    $runtimeEvidence = Get-JsonPropertyValue $defs 'runtimeEvidence'
    $runtimeRequired = @(Get-JsonPropertyValue $runtimeEvidence 'required')
    $runtimeProperties = Get-JsonPropertyValue $runtimeEvidence 'properties'
    foreach ($name in @('officialClientLiveEvidenceClaimed','officialClientLiveGateStatus',
            'evidencePackageFixture','productArtifactBindings','nativeSemanticWire','semanticAdmissionCap',
            'bridgeAwareStatusV3')) {
        Add-ContractIssue $issues ($runtimeRequired -ccontains $name) "schema.runtime.required.$name"
        Add-ContractIssue $issues ($null -ne $runtimeProperties.PSObject.Properties[$name]) "schema.runtime.property.$name"
    }
    $liveClaim = Get-JsonPropertyValue $runtimeProperties 'officialClientLiveEvidenceClaimed'
    $liveGate = Get-JsonPropertyValue $runtimeProperties 'officialClientLiveGateStatus'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $liveClaim 'type') -ceq 'boolean') 'schema.runtime.live-claim.typed'
    $liveGateValues = @((Get-JsonPropertyValue $liveGate 'enum'))
    foreach ($gate in @('PASS_OFFICIAL_EXACT_CLIENT_RUNTIME',
            'PASS_HISTORICAL_OFFICIAL_EXACT_CLIENT_RUNTIME','FAIL_OFFICIAL_EXACT_CLIENT_RUNTIME')) {
        Add-ContractIssue $issues ($liveGateValues -ccontains $gate) "schema.runtime.live-gate.$gate"
    }
    $authorityRecord = Get-JsonPropertyValue $defs 'evidenceAuthorityRecord'
    $authorityRequired = @(Get-JsonPropertyValue $authorityRecord 'required')
    $authorityProperties = Get-JsonPropertyValue $authorityRecord 'properties'
    foreach ($name in @('AuthorityProfile','SessionId','SourceArtifact','SourceSHA256',
            'TargetExecutable','TargetVersion','TargetSHA256','TargetProcessId',
            'TargetProcessCreationTime','CurrentSessionObserved','HistoricalEvidenceReused',
            'FixtureOnly','PromotionEligible','AttachStatus','DetachStatus','PackageSHA256',
            'RuntimeEventCount')) {
        Add-ContractIssue $issues ($authorityRequired -ccontains $name) "schema.authority.required.$name"
        Add-ContractIssue $issues ($null -ne $authorityProperties.PSObject.Properties[$name]) "schema.authority.property.$name"
    }
    $authorityProfiles = @((Get-JsonPropertyValue (Get-JsonPropertyValue $authorityProperties 'AuthorityProfile') 'enum'))
    foreach ($profile in $script:God2EvidenceAuthorityProfiles) {
        Add-ContractIssue $issues ($authorityProfiles -ccontains $profile) "schema.authority.profile.$profile"
    }
    $evidenceFixtureDef = Get-JsonPropertyValue $defs 'evidencePackageFixture'
    $evidenceFixtureProperties = Get-JsonPropertyValue $evidenceFixtureDef 'properties'
    Add-ContractIssue $issues ((Get-JsonPropertyValue (Get-JsonPropertyValue $evidenceFixtureProperties 'expectedCommandContract') 'const') -ceq
        'God2SemanticRecoveryEngine.exe --internal-package-fixture-test --session <fresh-5412-fixture-session> --report <repo-relative-report>') 'schema.evidence-fixture.command'
    Add-ContractIssue $issues ((Get-JsonPropertyValue (Get-JsonPropertyValue $evidenceFixtureProperties 'expectedExitCode') 'const') -eq 0) 'schema.evidence-fixture.exit'
    Add-ContractIssue $issues ((Get-JsonPropertyValue (Get-JsonPropertyValue $evidenceFixtureProperties 'checkCount') 'minimum') -eq 77) 'schema.evidence-fixture.minimum-count'
    Add-ContractIssue $issues ((Get-JsonPropertyValue (Get-JsonPropertyValue $evidenceFixtureProperties 'passed') 'minimum') -eq 77) 'schema.evidence-fixture.minimum-passed'
    $requiredChecksSchema = Get-JsonPropertyValue $evidenceFixtureProperties 'requiredChecks'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $requiredChecksSchema 'minItems') -eq 6 -and
        (Get-JsonPropertyValue $requiredChecksSchema 'maxItems') -eq 6 -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $requiredChecksSchema 'uniqueItems') $true)) 'schema.evidence-fixture.required-checks'
    $requiredCheckEnums = @((Get-JsonPropertyValue (Get-JsonPropertyValue $requiredChecksSchema 'items') 'enum'))
    foreach ($requiredName in @(
            '10a_legacy_or_detail_only_unload_spoof_rejected',
            '10b_versioned_strict_unload_positive_accepted',
            '10c_cleanup_schema_requires_typed_complete_inventory',
            '10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown',
            '14e_missing_shared_health_uses_nonpromotable_availability_schema',
            '14f_missing_candidate_map_never_emits_stale_v1_placeholder')) {
        Add-ContractIssue $issues ($requiredCheckEnums -ccontains $requiredName) "schema.evidence-fixture.required-check.$requiredName"
    }
    $freshness = Get-JsonPropertyValue $defs 'freshness'
    $freshnessRequired = @(Get-JsonPropertyValue $freshness 'required')
    $freshnessProperties = Get-JsonPropertyValue $freshness 'properties'
    foreach ($name in @('evidenceFixtureGeneratedAfterRelease','evidenceFixtureLastWriteTimeUtc',
            'nativeProbeEvidenceAfterAllBoundInputs','nativeProbeEvidenceLastWriteTimeUtc',
            'guiSourceSha256','mainSourceSha256','evidenceSourceSha256','enhancedCaptureStatusSchemaSha256',
            'bridgeAwareStatusV3SchemaSha256',
            'sharedHealthAvailabilitySchemaSha256','candidateMapAvailabilitySchemaSha256')) {
        Add-ContractIssue $issues ($freshnessRequired -ccontains $name) "schema.freshness.required.$name"
        Add-ContractIssue $issues ($null -ne $freshnessProperties.PSObject.Properties[$name]) "schema.freshness.property.$name"
    }
    $nativeFresh = Get-JsonPropertyValue $freshnessProperties 'nativeProbeEvidenceAfterAllBoundInputs'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $nativeFresh 'const') $true) 'schema.freshness.nativeProbe.const'
    $evidenceFresh = Get-JsonPropertyValue $freshnessProperties 'evidenceFixtureGeneratedAfterRelease'
    Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $evidenceFresh 'const') $true) 'schema.freshness.evidenceFixture.const'
    $statusSchemaSha = Get-JsonPropertyValue $freshnessProperties 'enhancedCaptureStatusSchemaSha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $statusSchemaSha 'const') -ceq
        $script:ExpectedEnhancedCaptureStatusSchemaSha256) 'schema.freshness.status-schema-sha-lock'
    $statusV3SchemaSha = Get-JsonPropertyValue $freshnessProperties 'bridgeAwareStatusV3SchemaSha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $statusV3SchemaSha 'const') -ceq
        $script:ExpectedBridgeAwareStatusV3SchemaSha256) 'schema.freshness.status-v3-schema-sha-lock'
    $guiSourceSha = Get-JsonPropertyValue $freshnessProperties 'guiSourceSha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $guiSourceSha 'const') -ceq
        $script:ExpectedGuiSourceSha256) 'schema.freshness.gui-source-sha-lock'
    $mainSourceSha = Get-JsonPropertyValue $freshnessProperties 'mainSourceSha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $mainSourceSha 'const') -ceq
        $script:ExpectedMainSourceSha256) 'schema.freshness.main-source-sha-lock'
    foreach ($contract in @(
            @('evidenceSourceSha256',$script:ExpectedEvidenceSourceSha256),
            @('sharedHealthAvailabilitySchemaSha256',$script:ExpectedSharedHealthAvailabilitySchemaSha256),
            @('candidateMapAvailabilitySchemaSha256',$script:ExpectedCandidateMapAvailabilitySchemaSha256))) {
        $property = Get-JsonPropertyValue $freshnessProperties ([string]$contract[0])
        Add-ContractIssue $issues ((Get-JsonPropertyValue $property 'const') -ceq
            [string]$contract[1]) "schema.freshness.$($contract[0])-lock"
    }
    $statusBinding = Get-JsonPropertyValue $defs 'enhancedCaptureStatusSchemaBinding'
    $statusBindingSha = Get-JsonPropertyValue (Get-JsonPropertyValue $statusBinding 'properties') 'sha256'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $statusBindingSha 'const') -ceq
        $script:ExpectedEnhancedCaptureStatusSchemaSha256) 'schema.status-binding.sha-lock'
    $inputBinding = Get-JsonPropertyValue $defs 'inputBinding'
    $bindingProperties = Get-JsonPropertyValue $inputBinding 'properties'
    $bindingRoles = @((Get-JsonPropertyValue (Get-JsonPropertyValue $bindingProperties 'role') 'enum'))
    foreach ($role in @('EVIDENCE_FIXTURE_REPORT','RESTORE_FAULT_LIFECYCLE_EVIDENCE','ATTACH_RESULT_EVIDENCE',
            'DETACH_RESULT_EVIDENCE','PRODUCTION_IDENTITY_GATE_EVIDENCE',
            'NATIVE_PROBE_SELFTEST_EVIDENCE','NATIVE_SEMANTIC_WIRE_EVIDENCE',
            'NATIVE_SEMANTIC_WIRE_VERIFICATION_EVIDENCE',
            'ULTIMATE_NATIVE_WIRE_REPORT_EVIDENCE','DEEP_PROBE_CANDIDATE_MAP_EVIDENCE',
            'CANONICAL_V1_SEMANTIC_WIRE_EVIDENCE',
            'CANONICAL_V1_SEMANTIC_WIRE_BINDING_EVIDENCE',
            'NONCANONICAL_V1_COMPATIBILITY_FIXTURE_EVIDENCE',
            'ENHANCED_CAPTURE_STATUS_SCHEMA','SHARED_HEALTH_AVAILABILITY_SCHEMA',
            'CANDIDATE_MAP_AVAILABILITY_SCHEMA','BRIDGE_AWARE_STATUS_V3_SCHEMA')) {
        Add-ContractIssue $issues ($bindingRoles -ccontains $role) "schema.input-binding.$role"
    }
    $finalPassRelationsNode = Get-JsonPropertyValue $Schema 'allOf'
    $finalPassRelations = if ($null -ne $finalPassRelationsNode) {
        $finalPassRelationsNode | ConvertTo-Json -Depth 30 -Compress
    } else { '' }
    foreach ($token in @('DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS','standalonePayloadStatus',
            'finalExeUiStatus','evidenceFixture','nativeProbe','DllStrictUnloadFinalizationContract',
            'DllEnhancedCaptureConsumerStateLifecycle',
            'DllCancelResultReadBeforeCleanupContract','guiSmokeGeneratedAfterRelease',
            'god2-enhanced-capture-status-v2',
            '"failed":{"const":0}','"pending":{"const":0}')) {
        Add-ContractIssue $issues ($finalPassRelations.IndexOf($token, [StringComparison]::Ordinal) -ge 0) "schema.final-pass-relation.$token"
    }
    Test-SchemaObjectClosure $Schema '$' $issues
    return Complete-ContractValidation $issues
}

function Test-EnhancedCaptureStatusSchemaContract([object]$Schema) {
    $issues = [Collections.Generic.List[string]]::new()
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Schema '$id') -ceq
        'https://god2.local/schemas/enhanced-capture-status.schema.json') 'status-schema.id'
    Add-ContractIssue $issues ((Get-JsonPropertyValue $Schema 'type') -ceq 'object' -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $Schema 'additionalProperties') $false)) 'status-schema.closed-root'
    $expectedProperties = @(
        'SchemaVersion','UpdatedAtUtc','Requested','Status','Detail','WasEverAttached',
        'ProbeReady','StrictUnloadVerified','FailureKind','InjectionAttempted',
        'ModuleWasEverLoaded','ModuleLoadStateVerified','ModuleSnapshotVerified',
        'ModuleAbsent','TargetProcessExited','TargetIdentityVerified','InjectorPresent',
        'ProbePresent','InjectorPayloadValidated','ProbePayloadValidated','TargetExecutable',
        'TargetArchitecture','ProbeScope','ProbeDomainCount','ProbeDomains',
        'ConfirmedExactBuildDomainCount','ConfirmedExactBuildDomains',
        'CandidateOnlyDomainCount','CandidateOnlyStatus','DeepProbeCandidateMapSchema',
        'CandidatePromotionGateCount','CandidateAbiSafetyGateCount',
        'CandidateActivationGateCount','CandidateAbiSafetyContract','SemanticEventContract',
        'SharedTransportContract','InjectionSequence','ReadinessHandshake','PayloadStaging',
        'PayloadDacl','PayloadMandatoryLabel','SemanticIpcBinding','PayloadLaunchMode',
        'UnloadPolicy','Apis','InputAndWindowHooks'
    )
    $required = @(Get-JsonPropertyValue $Schema 'required')
    $properties = Get-JsonPropertyValue $Schema 'properties'
    Add-ContractIssue $issues ($required.Count -eq 46 -and
        @($expectedProperties | Where-Object { $required -cnotcontains $_ }).Count -eq 0 -and
        @($required | Where-Object { $expectedProperties -cnotcontains $_ }).Count -eq 0) 'status-schema.required.exact-46'
    Add-ContractIssue $issues (Test-ExactJsonProperties $properties $expectedProperties) 'status-schema.properties.exact-46'
    foreach ($contract in @(
            @('SchemaVersion','god2-enhanced-capture-status-v2'),
            @('TargetExecutable','God2_opt.exe'),@('TargetArchitecture','x86'),
            @('ProbeScope','WinsockTransport+PostDecrypt+PreEncrypt+HandlerDecoded'),
            @('ProbeDomainCount',25),@('ConfirmedExactBuildDomainCount',4),
            @('ProbeDomains','Network,Parser,Serializer,Handler,Object,Allocation,VTable,Factory,ManagerLookup,Registry,ResourceDecode,Mutation,TaintSeed,FormulaOperand,Quest,Map,Portal,NPC,Monster,Battle,Inventory,Item,Skill,Pet/Mount,Snapshot'),
            @('ConfirmedExactBuildDomains','Network,Parser,Serializer,Handler'),
            @('CandidateOnlyDomainCount',21),@('CandidateOnlyStatus','EvidenceBlockedUnconfirmedProbe'),
            @('CandidatePromotionGateCount',10),
            @('CandidateAbiSafetyGateCount',4),@('CandidateActivationGateCount',14),
            @('DeepProbeCandidateMapSchema','god2-deep-probe-candidate-map-v2'),
            @('CandidateAbiSafetyContract','ArgumentContract;ReturnValueLifetime;ThreadContext;ReentrancyRisk'),
            @('SemanticEventContract','God2SemanticEvent;SchemaVersion=2;StrictTypes;25EventTypes'),
            @('SharedTransportContract','VersionedBounded25DomainPriorityRing;Priority0AuthoritativeTo3Candidate;BatchPublishing;Sequence;PerDomainDropAndWriteFailureCounters;FirstLastDroppedSequence;Reason;ConsumerLag'),
            @('InjectionSequence','PauseVerifiedPrimaryThread,RemoteThreadLoadLibraryW,ResumePrimaryThread'),
            @('ReadinessHandshake','God2TraceProbeWaitReady'),
            @('PayloadStaging','ProtectedProgramDataRandomDirectory'),
            @('PayloadDacl','Protected:SYSTEM+AdministratorsFull;CurrentUserReadExecuteOnly;EveryoneAbsent'),
            @('PayloadMandatoryLabel','MediumNoWriteUp'),
            @('SemanticIpcBinding','DuplicatedTargetHandlesV2;NoNamedObjectAuthority'),
            @('PayloadLaunchMode','AlreadyElevatedCreateProcessW'),
            @('UnloadPolicy','QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped'))) {
        $property = Get-JsonPropertyValue $properties ([string]$contract[0])
        Add-ContractIssue $issues ((Get-JsonPropertyValue $property 'const') -ceq
            $contract[1]) "status-schema.const.$($contract[0])"
    }
    foreach ($name in @(
            'Requested','WasEverAttached','ProbeReady','StrictUnloadVerified','InjectionAttempted',
            'ModuleWasEverLoaded','ModuleLoadStateVerified','ModuleSnapshotVerified','ModuleAbsent',
            'TargetProcessExited','TargetIdentityVerified','InjectorPresent','ProbePresent',
            'InjectorPayloadValidated','ProbePayloadValidated')) {
        Add-ContractIssue $issues ((Get-JsonPropertyValue (Get-JsonPropertyValue $properties $name) 'type') -ceq
            'boolean') "status-schema.boolean.$name"
    }
    $statusValues = @((Get-JsonPropertyValue (Get-JsonPropertyValue $properties 'Status') 'enum'))
    $expectedStatusValues = @('WaitingForVerifiedX86Process','Disabled','Active',
        'StoppedDuringAttach','Stopped','EvidenceBlocked')
    Add-ContractIssue $issues ($statusValues.Count -eq $expectedStatusValues.Count -and
        @($expectedStatusValues | Where-Object { $statusValues -cnotcontains $_ }).Count -eq 0) 'status-schema.Status.enum'
    $relations = @(Get-JsonPropertyValue $Schema 'allOf')
    $activeRelations = @($relations | Where-Object {
            $selector = Get-JsonPropertyValue (Get-JsonPropertyValue (Get-JsonPropertyValue $_ 'if') 'properties') 'Status'
            (Get-JsonPropertyValue $selector 'const') -ceq 'Active'
        })
    Add-ContractIssue $issues ($activeRelations.Count -eq 1) 'status-schema.relation.Active.unique'
    if ($activeRelations.Count -eq 1) {
        $activeProperties = Get-JsonPropertyValue (Get-JsonPropertyValue $activeRelations[0] 'then') 'properties'
        foreach ($contract in @(
                @('Requested',$true),@('WasEverAttached',$true),@('ProbeReady',$true),
                @('StrictUnloadVerified',$false),@('InjectionAttempted',$true),
                @('ModuleWasEverLoaded',$true),@('ModuleLoadStateVerified',$true),
                @('ModuleAbsent',$false),@('TargetProcessExited',$false),
                @('TargetIdentityVerified',$true),@('InjectorPayloadValidated',$true),
                @('ProbePayloadValidated',$true))) {
            $value = Get-JsonPropertyValue (Get-JsonPropertyValue $activeProperties ([string]$contract[0])) 'const'
            Add-ContractIssue $issues (Test-JsonBooleanValue $value ([bool]$contract[1])) "status-schema.relation.Active.$($contract[0])"
        }
    }
    $stoppedRelations = @($relations | Where-Object {
            $selector = Get-JsonPropertyValue (Get-JsonPropertyValue (Get-JsonPropertyValue $_ 'if') 'properties') 'Status'
            $values = @(Get-JsonPropertyValue $selector 'enum')
            $values.Count -eq 2 -and $values -ccontains 'Stopped' -and $values -ccontains 'StoppedDuringAttach'
        })
    Add-ContractIssue $issues ($stoppedRelations.Count -eq 1) 'status-schema.relation.Stopped.unique'
    if ($stoppedRelations.Count -eq 1) {
        $stoppedThen = Get-JsonPropertyValue $stoppedRelations[0] 'then'
        $stoppedProperties = Get-JsonPropertyValue $stoppedThen 'properties'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue (Get-JsonPropertyValue $stoppedProperties 'ProbeReady') 'const') $false) 'status-schema.relation.Stopped.ProbeReady'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue (Get-JsonPropertyValue $stoppedProperties 'StrictUnloadVerified') 'const') $true) 'status-schema.relation.Stopped.StrictUnloadVerified'
        $cleanupCases = @(Get-JsonPropertyValue $stoppedThen 'anyOf')
        $cleanupText = $cleanupCases | ConvertTo-Json -Depth 20 -Compress
        foreach ($token in @('ModuleWasEverLoaded','ModuleLoadStateVerified','ModuleSnapshotVerified',
                'ModuleAbsent','TargetProcessExited','TargetIdentityVerified')) {
            Add-ContractIssue $issues ($cleanupText.IndexOf($token, [StringComparison]::Ordinal) -ge 0) "status-schema.relation.Stopped.$token"
        }
        Add-ContractIssue $issues ($cleanupCases.Count -eq 2 -and
            $cleanupText -cmatch '"ModuleWasEverLoaded":\{"const":true\}' -and
            $cleanupText -cmatch '"ModuleWasEverLoaded":\{"const":false\}' -and
            $cleanupText -cmatch '"TargetProcessExited":\{"const":true\}' -and
            $cleanupText -cmatch '"ModuleSnapshotVerified":\{"const":true\}') 'status-schema.relation.Stopped.provenance-cases'
    }
    $blockedRelations = @($relations | Where-Object {
            $selector = Get-JsonPropertyValue (Get-JsonPropertyValue (Get-JsonPropertyValue $_ 'if') 'properties') 'Status'
            (Get-JsonPropertyValue $selector 'const') -ceq 'EvidenceBlocked'
        })
    Add-ContractIssue $issues ($blockedRelations.Count -eq 1) 'status-schema.relation.EvidenceBlocked.unique'
    if ($blockedRelations.Count -eq 1) {
        $blockedProperties = Get-JsonPropertyValue (Get-JsonPropertyValue $blockedRelations[0] 'then') 'properties'
        Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue (Get-JsonPropertyValue $blockedProperties 'ProbeReady') 'const') $false) 'status-schema.relation.EvidenceBlocked.ProbeReady'
    }
    Test-SchemaObjectClosure $Schema '$' $issues
    return Complete-ContractValidation $issues
}

function Copy-ContractFixture([object]$Fixture) {
    return ($Fixture | ConvertTo-Json -Depth 20 -Compress | ConvertFrom-Json)
}

function New-EvidenceFixtureContractFixture {
    $checks = [Collections.Generic.List[object]]::new()
    foreach ($name in @(
            '10a_legacy_or_detail_only_unload_spoof_rejected',
            '10b_versioned_strict_unload_positive_accepted',
            '10c_cleanup_schema_requires_typed_complete_inventory',
            '10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown',
            '14e_missing_shared_health_uses_nonpromotable_availability_schema',
            '14f_missing_candidate_map_never_emits_stale_v1_placeholder')) {
        $checks.Add([pscustomobject]@{ Name = $name; Status = 'PASS'; Evidence = 'measured fixture predicate passed' })
    }
    for ($index = 0; $index -lt 71; $index++) {
        $checks.Add([pscustomobject]@{
            Name = ('fixture_contract_{0:D2}' -f $index)
            Status = 'PASS'
            Evidence = 'measured fixture predicate passed'
        })
    }
    return [pscustomobject]@{
        SchemaVersion = 11
        TestedAtUtc = '2026-08-09T04:00:00.000Z'
        SessionId = 'fixture-session'
        FixturePath = 'fresh-5412-fixture-session'
        CheckCount = 77
        Passed = 77
        Failed = 0
        PackagePath = 'God2UltimateRecovery_fixture.zip'
        PackageSHA256 = 'E' * 64
        Checks = @($checks)
    }
}

function New-NetworkContractFixture {
    $cases = @()
    foreach ($expectedCase in @(
            [ordered]@{ Name = 'send'; Result = 208; Bytes = 208 },
            [ordered]@{ Name = 'WSASend'; Result = 0; Bytes = 208 },
            [ordered]@{ Name = 'recv'; Result = 64; Bytes = 64 },
            [ordered]@{ Name = 'WSARecv'; Result = 0; Bytes = 64 },
            [ordered]@{ Name = 'WSARecvOverlapped'; Result = 0; Bytes = 64 })) {
        $caseName = [string]$expectedCase.Name
        $cases += [pscustomobject]@{
            Case = $caseName
            HookInstalled = $true
            ApiCallResult = [int]$expectedCase.Result
            ApiCallResultExpected = [int]$expectedCase.Result
            BytesTransferred = [int]$expectedCase.Bytes
            TraceRecordCount = 3
            MatchingRecordCount = if ($caseName -in @('WSARecv','WSARecvOverlapped')) { 3 } else { 1 }
            PayloadMatch = $true
            AnalyzerParseCompleted = $true
            TraceIntegrity = $true
            AnalyzerError = $null
            TraceFileSize = if ([int]$expectedCase.Bytes -eq 208) { 804 } else { 660 }
        }
    }
    return [pscustomobject]@{
        configuration = 'Release'
        runDir = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture'
        cases = $cases
        pass = $true
    }
}

function New-NativeProbeContractFixture {
    $priorityRows = @()
    for ($priority = 0; $priority -lt 4; $priority++) {
        $priorityRows += [pscustomobject]@{
            Priority = $priority
            LaneAccepted = 1
            LaneDropped = 0
            LaneDropReason = 0
            SamplingProbeDropped = if ($priority -lt 2) { 0 } else { 1 }
            SamplingProbeDropReason = if ($priority -lt 2) { 0 } else { 10 }
        }
    }
    $faultRows = @()
    $diagnosticRows = @()
    $domainRows = @()
    for ($index = 0; $index -lt 25; $index++) {
        $domain = $script:ExpectedNativeProbeDomains[$index]
        $faultRows += [pscustomobject]@{
            DomainIndex = $index; Domain = $domain; FaultInjected = $true
            AffectedDomainDisabled = $true; PrivateDiagnosticAttributed = $true
            PrivateDiagnosticSinkWritten = $true; RuntimeDiagnosticEmitted = $false
            OtherDomainsEnabledAtFault = 1
            OtherDomainsEnabledMask = ('0x{0:X8}' -f ([uint32]1 -shl (($index + 1) % 25)))
            OtherDomainsRemainEnabledMeasured = $true; OtherDomainContinued = $true
        }
        $diagnosticRows += [pscustomobject]@{
            DomainIndex = $index; Domain = $domain; Dropped = 1
            WriteFailures = 1; EvidenceIncomplete = $true
        }
        $domainRows += [pscustomobject]@{
            DomainIndex = $index; Domain = $domain
            Accepted = if ($index -lt 11) { 14 } else { 13 }
            Dropped = if ($index -lt 8) { 1 } else { 0 }
            HighWater = if ($index -lt 11) { 14 } else { 13 }
            Pending = 0; WriteFailures = if ($index -ge 23) { 1 } else { 0 }
        }
    }
    $capRows = @(
        [pscustomobject]@{
            Priority = 3; DomainIndex = 24; Domain = 'Snapshot'
            AcceptedAtBoundary = 1; DroppedAtBoundary = 1; DropReason = 8
            FirstDroppedSequence = 2000001; LastDroppedSequence = 2000001
        },
        [pscustomobject]@{
            Priority = 0; DomainIndex = 0; Domain = 'Network'
            AcceptedBeyondLegacyHardLimit = 1; DroppedBeyondLegacyHardLimit = 0; DropReason = 0
            FirstDroppedSequence = 0; LastDroppedSequence = 0
        }
    )
    $bridgeFixture = [ordered]@{}
    foreach ($name in $script:ExpectedUnloadNeutralBridgeProperties) { $bridgeFixture[$name] = 0 }
    $bridgeFixture.SchemaId = 'god2-unload-neutral-bridge-v1'; $bridgeFixture.SchemaVersion = 1
    $bridgeFixture.AbiVersion = 1; $bridgeFixture.ArenaCount = 1
    $bridgeFixture.MaxRxPages = 3; $bridgeFixture.ActualRxPages = 3; $bridgeFixture.StatePageCount = 2
    $bridgeFixture.CodeBytes = 8192; $bridgeFixture.CodeSHA256 = 'A' * 64
    $bridgeFixture.CodeProtection = 'ExecuteRead'; $bridgeFixture.StateProtection = 'ReadWrite'
    $bridgeFixture.Capacity = 256; $bridgeFixture.HighWater = 1; $bridgeFixture.Phase = 'Detached'
    foreach ($name in @('CaptureObserverRetired','ObserverPointerNull','BridgeNoDllPointers','IatRestored','HotpatchRestored',
            'Retained','ObserverArmSelfTestPassed','ObserverDisarmSelfTestPassed','PointerIdentityFixturePassed',
            'ExactPatchTransactionFixturePassed','ExactPatchThirdPartyNoClobberFixturePassed',
            'ExactPatchThirdPartyDirectNoClobberPassed','ExactPatchThirdPartyHotpatchEntryNoClobberPassed',
            'ExactPatchThirdPartyHotpatchPrefixNoClobberPassed','ExactPatchThirdPartyHotpatchCombinedNoClobberPassed',
            'ExactPatchThirdPartyProtectionRestoredPassed','ExactPatchThirdPartyUnsafeRetainedPassed',
            'HookLockContentionFixturePassed','ObserverTamperFixturePassed','ObserverFaultFixturePassed',
            'CrossStageFaultFixturePassed','CompletionRouteFixturePassed','CompletionDuplicateBeforeReusePassed',
            'CompletionDeterministicAbaPassed','CompletionAbaBarrierReached','CompletionAbaOldCasRejected',
            'CompletionAbaNewRouteIntact','RetainedOwnerUnloadGuardPassed','WSARecvCommitStageFixturePassed',
            'WSARecvCommitStageLiveStateUntouched','WSARecvSameIdentityIsolationPassed',
            'WSARecvNoCallbackIdentityIsolationPassed','WSARecvPostRepostIsolationPassed',
            'WSARecvPostFaultRepostIsolationPassed','WSARecvFinalPurgeRacePassed',
            'WSARecvFinalPurgeLateRegistrationObserved')) { $bridgeFixture[$name] = $true }
    $bridgeFixture.DllPointersRemaining = $false; $bridgeFixture.ArenaReused = $false
    $bridgeFixture.State = 'RetainedPassThrough'; $bridgeFixture.ObserverAcquireCount = 1; $bridgeFixture.ObserverReleaseCount = 1
    $bridgeFixture.PersistentOriginalPointerCount = 7; $bridgeFixture.ExactPersistentOriginalPointerCount = 7
    $bridgeFixture.FirstPersistentOriginalIdentityMismatch = -1; $bridgeFixture.FirstMissingRequiredPersistentOriginal = -1
    $bridgeFixture.SealedEntryPointerCount = 11; $bridgeFixture.ExactEntryPointerCount = 11; $bridgeFixture.FirstEntryIdentityMismatch = -1
    $bridgeFixture.SealedCompletionThunkCount = 256; $bridgeFixture.ExactCompletionThunkCount = 256; $bridgeFixture.FirstCompletionThunkIdentityMismatch = -1
    $bridgeFixture.ExactPatchTransactionFixtureCount = 7; $bridgeFixture.ExactPatchTransactionPassedCount = 7
    $bridgeFixture.ExactPatchTransactionUncertainCount = 3; $bridgeFixture.ExactPatchRestoredExactCount = 4
    $bridgeFixture.ExactPatchFirstRestoredExactSite = 100; $bridgeFixture.ExactPatchFirstForeignPreservedSite = -1
    $bridgeFixture.ExactPatchFirstOwnershipUnsafeSite = -1
    $bridgeFixture.HookLockContentionFixtureCount = 4; $bridgeFixture.HookLockContentionFixturePassedCount = 4
    $bridgeFixture.AbiFixtureCount = 10; $bridgeFixture.AbiFixturePassedCount = 10
    $bridgeFixture.CompletionAbaOldToken = 14; $bridgeFixture.CompletionAbaNewToken = 18; $bridgeFixture.CompletionAbaApplicationCallbacks = 2
    $bridgeFixture.WSARecvCommitStageFixtureCount = 8; $bridgeFixture.WSARecvCommitStagePassedCount = 8
    $bridgeFixture.WSARecvCommitStagePassedMask = 255; $bridgeFixture.WSARecvCommitStageCallbacks = 4
    $bridgeFixture.WSARecvFinalPurgePurgedCount = 1; $bridgeFixture.PendingNeutralRetirements = 2; $bridgeFixture.PendingPurgedAtDisarm = 2
    $bridgeApis = @('send','WSASend','recv','WSARecv','WSAGetOverlappedResult','GetQueuedCompletionStatus',
        'GetQueuedCompletionStatusEx','WSARecvCompletionRoutine','Parser','Serializer','Handler')
    $bridgeFixture.BridgeCoverageResults = @(0..10 | ForEach-Object {
        [pscustomobject][ordered]@{ Index = $_; Api = $bridgeApis[$_]; InstalledToBridge = $_ -lt 8
            OriginalBound = $_ -lt 7; PreObserverObserved = if ($_ -lt 7) { 1 } else { 0 }
            PostObserverObserved = if ($_ -in @(2,4,5,6)) { 0 } elseif ($_ -lt 8) { 1 } else { 0 }; BlockingFrameOutsideDll = $false
            ResultPreserved = $false; LastErrorPreserved = $false
            CallbackTailTransferRequired = $_ -eq 7; CallbackTailTransfer = $false
            CallbackForwardedExactlyOnce = $false }
    })
    $bridgeFixture.DisabledApiResults = @('connect','WSAConnect','socket','closesocket' | ForEach-Object {
        [pscustomobject][ordered]@{ Api = $_; Status = 'DisabledNotInstalled'; Installed = $false }
    })

    $privacyIdentity = [ordered]@{}
    foreach ($name in $script:ExpectedSensitiveOutboundIdentityProperties) { $privacyIdentity[$name] = 1 }
    $privacyIdentity.Matched = $true
    $privacyIntegration = [ordered]@{}
    foreach ($name in $script:ExpectedSensitiveOutboundIntegrationProperties) { $privacyIntegration[$name] = 0 }
    $privacyIntegration.SchemaId = 'God2SensitiveOutboundIntegration'; $privacyIntegration.SchemaVersion = 1
    foreach ($name in @('Invoked','Passed','TestOnlyExactFixture','SerializerAdapterObserved','SendBridgeObserved',
            'WSASendBridgeObserved','RetryObserved','DeterministicPartialFixturePassed','PendingObserved',
            'CompletionObserved','SinkAdmissionObserved','SuccessfulRetrySinkAdmitted',
            'SuccessfulRetrySensitivePayloadSuppressed','WsaSendPendingSinkAdmitted',
            'WsaSendPendingSensitivePayloadSuppressed','SuccessfulRetrySameShapeDecoyRejected',
            'WsaSendPendingSameShapeDecoyRejected','DecoyCannotSubstituteDroppedReal')) { $privacyIntegration[$name] = $true }
    $privacyIntegration.OfficialRuntime = $false; $privacyIntegration.SerializerEntryObserved = $false
    $privacyIntegration.ActualPartialObserved = $false; $privacyIntegration.Stage = 5
    $privacyIntegration.CreatedDelta = 2; $privacyIntegration.CompletedDelta = 2; $privacyIntegration.RetryDelta = 1
    $privacyIntegration.SuccessfulRetrySinkSequence = 52; $privacyIntegration.WsaSendPendingSinkSequence = 55
    $privacyIntegration.SuccessfulRetrySameShapeDecoySequence = 49; $privacyIntegration.WsaSendPendingSameShapeDecoySequence = 54
    $privacyIntegration.SuccessfulRetryIdentity = [pscustomobject]$privacyIdentity
    $privacyIntegration.WsaSendPendingIdentity = [pscustomobject]$privacyIdentity
    $privacyFixture = [ordered]@{}
    foreach ($name in $script:ExpectedSensitiveOutboundPrivacyProperties) { $privacyFixture[$name] = 0 }
    $privacyFixture.SchemaId = 'God2SensitiveOutboundPrivacy'; $privacyFixture.SchemaVersion = 1; $privacyFixture.Capacity = 256
    $privacyFixture.FixtureCount = 15; $privacyFixture.FixturePassedCount = 15; $privacyFixture.FixturePassedMask = 32767
    foreach ($name in @('FixturePassed','PrivateNonAliasing','AllContextPointersNonAliasing','PointerAliasNegativePassed',
            'LiveSnapshotUnchanged','LiveStateUntouched','EarlyCompletionRedactionCarryPassed',
            'EarlyCompletionPrivateSinkNonAliasing','EarlyCompletionSensitivePayloadSuppressed')) { $privacyFixture[$name] = $true }
    $privacyFixture.CreatedTransactions = 2; $privacyFixture.CompletedTransactions = 2; $privacyFixture.RetryCount = 1
    $privacyFixture.StaleCompletionCount = 1; $privacyFixture.OpaqueFailClosed = $false
    $privacyFixture.TestOnlyLiveIntegration = [pscustomobject]$privacyIntegration
    $privacyCases = @('ErrorZeroRetry','MultiWsabufPartial','PendingCrossThread','CancelRetainsIntent','SameAddressMutation',
        'UnboundMultipleIntents','CapacityAndEpoch','StaleCompletionAfterReuse','FaultBeforeAndAfterCommit',
        'NestedSameLengthBeforeReal','CrossThreadProducerTokenGate','PendingPublicationOrdering',
        'EarlyIocpCompletionBeforePost','GqcsExInternalFailureRetainsIntent','CompletingOwnershipNotStolen')
    $privacyFixture.FixtureResults = @(0..14 | ForEach-Object {
        [pscustomobject][ordered]@{ Index = $_; Case = $privacyCases[$_]; Passed = $true }
    })
    return [pscustomobject]@{
        SchemaId = 'God2NativeProbeSelfTest'; SchemaVersion = 1; ProcessId = 4242
        SemanticEventProducerFixtureCount = 25
        SharedTransportBatchMaximumItems = 8; SharedTransportBatchAccepted = 248
        SharedTransportBatchCount = 32
        SharedTransportBatchEventSignalDelta = 32; SharedTransportBatchLockAcquisitionDelta = 256
        SharedTransportBatchContractPassed = $true
        SharedTransportBatchValidationWriteFailureCount = 2
        SharedTransportDomainWriteFailureTotal = 2; SharedTransportConsumerDrainVerified = $true
        SharedTransportPriorityContractPassed = $true
        SemanticAdmissionCapContractPassed = $true
        SemanticAdmissionCapSharedLedgerPassed = $true
        SemanticAdmissionCapLiveStateUntouched = $true
        SemanticAdmissionHardLimit = 2000000; SemanticAdmissionLowPriorityCeiling = 1500000
        FaultDiagnosticPriorityPolicyPassed = $true; FaultDiagnosticSelectedPriority = 0
        StartupProbeDiagnosticSelectedPriority = 3
        FaultDiagnosticAcceptedAfterLowPriorityCeiling = $true
        FaultDiagnosticAcceptedBeyondLegacyHardLimit = $true
        FaultDiagnosticBeyondLegacyLimitDropReason = 0
        FaultDiagnosticAdmissionLiveStateUntouched = $true
        FaultDiagnosticProducerPublishReturned = $true
        RestoreFaultResidentNegativePassed = $true
        IatOwnerLifecycleSelfTestPassed = $true; IatOwnerGoneRetired = $true
        IatAddressReuseNotClobbered = $true; IatReplacementCasRestored = $true
        IatOriginalNotClobbered = $true; IatThirdPartyNotClobbered = $true
        CompletionRoutineObserved = 1; WSAGetOverlappedResultObserved = 1
        GetQueuedCompletionStatusObserved = 1; GetQueuedCompletionStatusExObserved = 1
        OriginalRecvEntered = 1; OriginalRecvReturned = 1
        OriginalWsaWaitEntered = 1; OriginalWsaWaitReturned = 1
        OriginalGqcsEntered = 1; OriginalGqcsReturned = 1
        OriginalGqcsExEntered = 1; OriginalGqcsExReturned = 1
        PendingAtStopDeferredCount = 0; PendingAtStopRetryDrainedCount = 0
        PendingAtStopDeferredAttempts = 0
        FaultIsolationPassed = $true; AsyncDiagnosticQueuePassed = $true
        AsyncDiagnosticEnqueued = 256; AsyncDiagnosticFlushed = 256
        AsyncDiagnosticDropCount = 25; AsyncDiagnosticWriteFailureCount = 0
        AsyncDiagnosticHighWater = 256; AsyncDiagnosticQueueCapacity = 256
        AsyncDiagnosticPending = 0; HookThreadFileIoOperations = 0
        HookThreadRingNullFallbackPassed = $true; HookThreadRingNullSemanticLossCount = 1
        HookThreadRingNullLiveStateUntouched = $true
        HookThreadRingNullPrivateSinkNonAliasing = $true
        HookThreadRingNullFormattedLineSinkPassed = $true
        HookThreadRingNullSchemaBatchPassed = $true
        HookThreadRingNullSchemaSingleFormattedCount = 1
        HookThreadRingNullSchemaSingleDurableCount = 0
        HookThreadRingNullSchemaSingleLossCount = 1
        HookThreadRingNullSchemaBatchFormattedCount = 2
        HookThreadRingNullSchemaBatchDurableCount = 0
        HookThreadRingNullSchemaBatchLossCount = 2
        HookThreadRingNullSchemaBatchDomainLossCount = 2
        HookThreadRingNullSchemaBatchFileIoCount = 0
        SemanticSessionFallbackFixturePassed = $true
        SemanticSelfTestCheckCount = 83; SemanticSelfTestPassedCheckCount = 83
        SemanticSelfTestFailedCheckCount = 0; SemanticSelfTestFirstFailedCheckId = 0
        AsyncDiagnosticPrivateNonAliasing = $true; PrivacyFragmentPrivateNonAliasing = $true
        RestoreFaultPrivateControllerPassed = $true; CapturePrerequisitePolicyFixturePassed = $true
        SharedRingRequested = $true; SharedRingPrerequisiteReady = $true
        SharedRingProducerPublished = $true; TraceHeaderReady = $true
        FlushWorkerHandleOwned = $true; FlushWorkerStartupState = 2
        FlushWorkerStartupAcknowledged = $true
        ExportedSelfTestOwnerPolicyFixturePassed = $true; ExportedSelfTestOwnerState = 4
        ExportedSelfTestThreadHandleRetired = $true; ExportedSelfTestOwnerRetired = $true
        IdentityProfile = 'TestOnlyExactFixture'
        SensitiveOutboundPrivacy = [pscustomobject]$privacyFixture
        UnloadNeutralBridge = [pscustomobject]$bridgeFixture
        SharedTransportPriorityResults = $priorityRows
        SemanticAdmissionCapResults = $capRows
        FaultIsolationOnlyAffectedNegativePassed = $true
        FaultIsolationMeasuredPositivePassed = $true
        FaultIsolationResults = $faultRows
        AsyncDiagnosticDomainResults = $diagnosticRows
        SharedTransportDomainResults = $domainRows
    }
}
function New-InjectorResultV2Fixture([string]$Kind) {
    $record = [ordered]@{
        schemaVersion = 2
        status = ''
        code = 0
        injectionAttempted = $false
        moduleWasEverLoaded = $false
        moduleLoadStateVerified = $true
        moduleSnapshotVerified = $false
        moduleAbsent = $false
        targetProcessExited = $false
        targetIdentityVerified = $false
        probeReady = $false
        stopSucceeded = $false
        unloadSafe = $false
        moduleUnloaded = $false
        moduleResidentInactive = $false
        strictUnloadVerified = $true
        cleanupRetriable = $false
        injectionMode = 'None'
        suspendedThreadCount = 0
        primaryThreadId = 0
        extraReferenceRequested = $false
        extraReferenceLoaded = $false
        extraReferenceNegativeFirstFreeLibraryStillPresent = $false
        extraReferenceNegativeNotClaimedUnloaded = $false
        extraReferenceReleasedThenModuleAbsent = $false
    }
    switch -CaseSensitive ($Kind) {
        'Attach' {
            $record.status = 'ATTACHED'
            $record.injectionAttempted = $true
            $record.moduleWasEverLoaded = $true
            $record.targetIdentityVerified = $true
            $record.probeReady = $true
            $record.strictUnloadVerified = $false
            $record.cleanupRetriable = $true
            $record.injectionMode = 'PausePrimaryRemoteThreadResume'
            $record.suspendedThreadCount = 1
            $record.primaryThreadId = 4242
            $record.extraReferenceRequested = $true
            $record.extraReferenceLoaded = $true
        }
        'Detach' {
            $record.status = 'DETACHED'
            $record.injectionAttempted = $true
            $record.moduleWasEverLoaded = $true
            $record.moduleSnapshotVerified = $true
            $record.moduleAbsent = $true
            $record.targetIdentityVerified = $true
            $record.stopSucceeded = $true
            $record.unloadSafe = $true
            $record.moduleUnloaded = $true
            $record.injectionMode = 'PausePrimaryRemoteThreadResume'
            $record.suspendedThreadCount = 1
            $record.primaryThreadId = 4242
            $record.extraReferenceRequested = $true
            $record.extraReferenceLoaded = $true
            $record.extraReferenceNegativeFirstFreeLibraryStillPresent = $true
            $record.extraReferenceNegativeNotClaimedUnloaded = $true
            $record.extraReferenceReleasedThenModuleAbsent = $true
        }
        'IdentityBlocked' {
            $record.status = 'EVIDENCE_BLOCKED_BUILD_MISMATCH'
            $record.code = 193
            $record.moduleSnapshotVerified = $true
            $record.moduleAbsent = $true
        }
        default { throw "Unknown injector result fixture kind: $Kind" }
    }
    return [pscustomobject]$record
}

function New-NativeSemanticWireVerificationFixture {
    $rows = @()
    for ($index = 0; $index -lt 25; $index++) {
        $expected = $script:ExpectedSemanticEventTypes[$index]
        $rows += [pscustomobject]@{
            wireLine = $index + 1
            definitionIndex = $index
            eventType = $expected.EventType
            readerAccepted = $true
            sourceTokenBound = $true
            fixturePayloadBound = $true
            authorityFailClosed = $true
            dispatchPassed = $true
            roundTripPassed = $true
            corruptionRejected = $true
            versionRejected = $true
            promotionPolicyPassed = $true
            dispatchSink = $expected.DispatchSink
            promotionPolicy = if ($expected.EventType -ceq 'FunctionRoleCandidate' -or
                $expected.EventType -ceq 'ProbeDiagnostic') { 'NoPromotion' } else { 'StandardEvidenceGates' }
        }
    }
    return [pscustomobject]@{
        schemaId = 'God2NativeSemanticWireVerification'; schemaVersion = 1
        inputPath = 'dll-native-semantic-event-v2.jsonl'
        inputSHA256 = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
        eventTypeDigest = 'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C'
        inputLineCount = 25; allEventTypesUnique = $true; passed = $true
        totals = [pscustomobject]@{
            readerAcceptedCount = 25; sourceTokenBoundCount = 25
            fixturePayloadBoundCount = 25; authorityFailClosedCount = 25
            dispatchCount = 25; roundTripCount = 25; corruptionRejectedCount = 25
            versionRejectedCount = 25; promotionPolicyCount = 25
        }
        rows = $rows; error = ''
    }
}

function New-DeepProbeCandidateMapFixture {
    $domains = [Collections.Generic.List[object]]::new()
    $confirmedCandidates = @(
        ,@('send','WSASend','recv','WSARecv')
        ,@([pscustomobject][ordered]@{ callsiteRva='0x00078A48'; targetRva='0x00078D70'; verification='ExactCallTarget' })
        ,@([pscustomobject][ordered]@{ targetRva='0x0007FC10'; signature='55 8B EC 56 57'; verification='ExactInstructionBytes' })
        ,@([pscustomobject][ordered]@{ callsiteRvas=@('0x00147096','0x001470AE'); targetRva='0x0007F940'; verification='BothExactCallTargets' })
    )
    for ($index = 0; $index -lt 4; $index++) {
        $domains.Add([pscustomobject][ordered]@{
            Domain = $script:ExpectedNativeProbeDomains[$index]
            Probe = $script:ExpectedProbeDomains[$index].Probe
            Status = 'CurrentTargetIdentityBlocked'
            Authority = 'UNKNOWN'
            Candidates = @($confirmedCandidates[$index])
            CandidateCount = @($confirmedCandidates[$index]).Count
            VerificationStatus = 'EvidenceBlockedExactIdentityMismatch'
        })
    }
    $gateNames = @($script:ExpectedCandidateGates | ForEach-Object { $_.Gate })
    for ($index = 4; $index -lt 25; $index++) {
        $verification = [ordered]@{}
        foreach ($gate in $gateNames) { $verification[$gate] = $false }
        $verification.PromotionGateCount = 10
        $verification.AbiSafetyGateCount = 4
        $verification.TotalGateCount = 14
        $domains.Add([pscustomobject][ordered]@{
            Domain = $script:ExpectedNativeProbeDomains[$index]
            Probe = $script:ExpectedProbeDomains[$index].Probe
            Status = 'EvidenceBlockedUnconfirmedProbe'
            Authority = 'UNKNOWN'
            DiscoveryPlan = [pscustomobject][ordered]@{
                SeedDomain = 'Parser'
                Strategy = 'BoundedDirectCallTargetsFromVerifiedSeed'
                TypedEvidenceRequired = 'TypedDomainEvidence'
                CausalEvidenceRequired = 'RepeatedCausalEvidence'
            }
            Candidates = @()
            CandidateCount = 0
            VerificationContract = [pscustomobject]$verification
            VerificationStatus = 'EvidenceBlockedNoExecutableCallCandidate'
            InstallationPolicy = 'NeverInstallUntilAllVerificationGatesPass'
            SafeNextAction = 'CollectExactBuildRepeatedTypedRuntimeEvidenceAndVerifyCallingConvention'
        })
    }
    return [pscustomobject][ordered]@{
        SchemaVersion = 'god2-deep-probe-candidate-map-v2'
        ClientSHA256 = 'A' * 64
        ClientSha256Expected = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
        IdentityProfile = 'OfficialExactClient'
        Architecture = 'x86'
        DiscoveryPolicy = 'ExecutableCodeAndExactSignaturesOnly;NoWritableMemoryScan;NoSensitiveValueLogging'
        CandidateVerificationEngine = 'BoundedExecutableCallGraphPlusExactCandidateBytesPlusPromotionHardGate'
        UnconfirmedProbePolicy = 'EvidenceBlockedUnconfirmedProbe'
        CandidateSchemaVersion = 'god2-deep-probe-candidate-v1'
        TargetIdentityVerified = $false
        EngineBounds = [pscustomobject][ordered]@{
            ScanRadiusBytes = 256
            MaximumCandidatesPerDomain = 4
            SignatureBytes = 8
            WritableMemoryScanned = $false
        }
        DomainCount = 25
        Domains = @($domains)
    }
}

function New-ProductContractFixture {
    $nativeReport = New-NativeProbeContractFixture
    $attachRecord = New-InjectorResultV2Fixture 'Attach'
    $detachRecord = New-InjectorResultV2Fixture 'Detach'
    $identityRecord = New-InjectorResultV2Fixture 'IdentityBlocked'
    $wireVerification = New-NativeSemanticWireVerificationFixture
    $ultimateWire = New-UltimateContractFixture
    $domainResults = @()
    for ($index = 0; $index -lt 25; $index++) {
        $domainResults += [pscustomobject]@{
            Index = $index
            Domain = $script:ExpectedNativeProbeDomains[$index]
            Probe = $script:ExpectedProbeDomains[$index].Probe
            Status = if ($index -eq 0) { 'Active' } elseif ($index -lt 4) { 'ConfiguredInactiveNetworkOnly' } else { 'CandidateOnlyNeverInstalled' }
            SlotSequence = $index + 1; SlotDomain = $index; SlotPriority = 3
            WireSHA256 = 'A' * 64; PayloadSHA256 = 'B' * 64
            EventId = ('PD-4242-{0}' -f ($index + 1)); ProcessId = 4242
            ClientBuildId = 'C' * 64; SessionId = 'contract-fixture-session'
            SourceToken = $script:ExpectedProbeDomains[$index].Probe
            AuthorityHint = if ($index -lt 4) { 'VERIFIED' } else { 'UNKNOWN' }
            SensitiveMaskStatus = 'NotSensitive'
            ContractStatus = if ($index -lt 4) { 'ConfirmedContract' } else { 'CandidateOnlyBlockedContract' }
            ActivationAllowedByContract = $index -lt 4
            RuntimeDiagnosticObserved = $true
            RuntimeActivationObserved = $index -eq 0
            TargetIdentityVerified = $true
        }
    }
    $candidateGateResults = @($script:ExpectedCandidateGates | ForEach-Object {
        [pscustomobject]@{
            Gate = $_.Gate
            Category = $_.Category
            PassedOnAllCandidateDomains = $_.Gate -ceq 'ExactTargetIdentity'
            RejectedOnAllCandidateDomains = $_.Gate -cne 'ExactTargetIdentity'
        }
    })
    $laneAcceptedCounts = @(72, 74, 66, 124)
    $laneResults = @(0..3 | ForEach-Object {
        $priority = $_
        $laneDropped = if ($priority -lt 2) { 0 } else { 4 }
        [pscustomobject][ordered]@{
            Priority = $priority
            Accepted = $laneAcceptedCounts[$priority]
            Consumed = $laneAcceptedCounts[$priority]
            Dropped = $laneDropped
            Sampled = $laneDropped
            FirstDroppedSequence = if ($laneDropped -eq 0) { 0 } else { 313 + ($priority - 2) }
            LastDroppedSequence = if ($laneDropped -eq 0) { 0 } else { 325 + ($priority - 2) }
            LastDropReason = if ($laneDropped -eq 0) { 0 } else { 10 }
            HighWaterMark = 64
            ConsumerLag = 0
            Passed = $true
        }
    })
    $blockingApis = @('GetQueuedCompletionStatus','GetQueuedCompletionStatusEx','recv','WSAGetOverlappedResult')
    $blockingRows = @(0..3 | ForEach-Object {
        [pscustomobject][ordered]@{
            SchemaId = 'God2BlockingPostUnloadResult'; SchemaVersion = 1; Index = $_; Api = $blockingApis[$_]
            ReleaseBarrierObserved = $true; Returned = $true; ResultPreserved = $true; ResourceReusable = $true
            ObservedResult = 1; ObservedTransferred = if ($_ -lt 2) { 7 } else { 1 }
            ObservedFlags = 0; ObservedRemoved = if ($_ -eq 1) { 1 } else { 0 }
            ObservedCompletionKey = if ($_ -lt 2) { '0x47324E52' } else { '0x00000000' }
            InputOverlapped = if ($_ -eq 3) { '0x00001000' } else { '0x00000000' }
            OutputOverlapped = if ($_ -lt 2) { '0x00002000' } else { '0x00000000' }
            InputResource = ('0x{0:X8}' -f (0x100 + $_)); BaselineResource = ('0x{0:X8}' -f (0x100 + $_))
            EntryLastError = 1369505793 + $_; BaselineLastError = if ($_ -lt 2) { 1369505793 + $_ } else { 0 }
            ObservedLastError = if ($_ -lt 2) { 1369505793 + $_ } else { 0 }; PayloadMatched = $true
        }
    })
    $blockingMatrix = [pscustomobject][ordered]@{
        schemaId = 'God2BlockingPostUnloadMatrix'; schemaVersion = 1; targetProcessId = 4242
        moduleAbsentBeforeRelease = $true; targetAliveAtDetach = $true; allReturnedAtDetach = $false
        releaseIssued = $true; allReturnedAfterRelease = $true; rows = $blockingRows
    }
    return [pscustomobject]@{
        Status = 'PASS'; FailureCount = 0; Failures = @()
        AttachResultRecord = $attachRecord
        AttachResultPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/attach-result-v2.json'
        AttachResultSHA256 = 'F' * 64
        DetachResultRecord = $detachRecord
        DetachResultPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/detach-result-v2.json'
        DetachResultSHA256 = '1' * 64
        ReadinessHandshake = 'God2TraceProbeWaitReady'
        StopHandshake = 'God2TraceProbeStop'; SafeUnloadHandshake = 'God2TraceProbeCanUnload'
        TargetExecutable = 'God2_opt.exe'; TargetArchitecture = 'x86'
        ModuleUnloaded = $true; ModuleResidentInactive = $false
        ModuleSnapshotVerified = $true; ModuleAbsent = $true; UnloadSafe = $true
        ExtraReferenceNegativeFirstFreeLibraryStillPresent = $true
        ExtraReferenceNegativeNotClaimedUnloaded = $true
        ExtraReferenceReleasedThenModuleAbsent = $true
        BlockingTargetAliveAtDetach = $true; BlockingAllReturnedAtDetach = $false
        BlockingModuleAbsentBeforeRelease = $true; BlockingReleaseIssued = $true
        BlockingAllReturnedAfterRelease = $true
        BlockingDetachObservedUtc = '2026-08-10T00:00:00.0000000Z'
        BlockingReleaseIssuedUtc = '2026-08-10T00:00:01.0000000Z'
        BlockingAllReturnedObservedUtc = '2026-08-10T00:00:02.0000000Z'
        BlockingTargetStdoutPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/raw/blocking-target.stdout.log'
        BlockingTargetStdoutSHA256 = '3' * 64
        BlockingPostUnloadResultPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/blocking-post-unload.json'
        BlockingPostUnloadResultSHA256 = '4' * 64
        BlockingPostUnloadResult = $blockingMatrix
        BlockingPostUnloadDuplicateNegativeCount = 3
        MetadataPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/raw/metadata.jsonl'
        MetadataSHA256 = '5' * 64
        MetadataRecordCount = 4; MatchingTransmittedSendCount = 1
        MetadataSequenceContractPassed = $true
        ExpectedTransmittedSendPayloadSHA256 = '6' * 64
        MetadataReadableWhileAttached = $true; SensitiveOutboundSinkRecordsPassed = $true
        SensitiveOutboundSinkRecords = @()
        ResidualInjectorProcess = $false; ResidualGameProcess = $false
        ProductionIdentityGateRecord = $identityRecord
        ProductionIdentityGatePath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/production-identity-gate-result-v2.json'
        ProductionIdentityGateSHA256 = '2' * 64
        StrictInjectorResultParserNegativeCount = 10
        ProductionWrongIdentityRejectedBeforeInjection = $true
        WrongIdentityNeverPublishedDerivedFromInjectorNoLoad = $true
        WrongIdentitySampledSnapshotSupportOnly = $true
        WrongIdentitySampledSnapshotPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/wrong-identity-snapshot.json'
        WrongIdentitySampledSnapshotSHA256 = '7' * 64
        WrongIdentitySampledSnapshotRecord = [pscustomobject]@{ SchemaId = 'God2WrongIdentitySnapshot'; SchemaVersion = 1 }
        WrongIdentitySnapshotDuplicateNegativeCount = 3
        PositiveLifecycleIdentityMode = 'TestOnlyExactFixture'; PositiveLifecycleIdentityVerified = $true
        PositiveLifecycleClientVersion = 'God2TraceSelfTestClient/1'; PositiveLifecycleClientSHA256 = '8' * 64
        PositiveLifecycleProcessId = 4242; PositiveLifecycleProcessCreationTime = '134000000000000000'
        PositiveLifecycleProbeSHA256 = '9' * 64; ProductionProbeSHA256 = 'A' * 64
        TestInjectorSHA256 = 'B' * 64; ProductionInjectorSHA256 = 'C' * 64
        OfficialClientPositiveRuntimeObserved = $false
        ProbeDomainCount = 25; ConfirmedContractDomainCount = 4
        CandidateOnlyBlockedDomainCount = 21; ProbeDomainResults = $domainResults
        SemanticEventTypeFixtureCount = 25; SemanticEventTypeDistinctCount = 25
        SemanticEventTypeNativeCount = 25
        SemanticEventTypeDigestSHA256 = 'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C'
        CandidatePromotionGateCount = 10; CandidateAbiSafetyGateCount = 4
        CandidateTotalGateCount = 14; CandidateActivationGateMatrixValid = $true
        CandidateActivationPassedGateCount = 1; CandidateActivationRejectedGateCount = 13
        CandidateGateResults = $candidateGateResults
        SharedTransportVersion = 4; SharedTransportProducerReady = $true
        SharedTransportProducerClosed = $true; SharedTransportConsumerReady = $false
        SharedTransportConsumerClosed = $true; SharedTransportConsumerFailure = 0
        SharedTransportConsumerDrainVerified = $true
        SharedTransportDomainPendingTotal = 0
        TargetProcessId = 4242; SharedTransportProducerProcessId = 4242
        SharedTransportConsumerProcessId = 31337; NativeProbeProcessId = 4242
        NativeSemanticWireProcessIds = @(0..24 | ForEach-Object { 4242 })
        SharedTransportCrossProcess = $true; SharedTransportCrossProcessNegativeCount = 2
        SharedTransportAttempted = 344
        SharedTransportAccepted = 336; SharedTransportDropped = 8
        SharedTransportSampled = 8; SharedTransportHighWaterMark = 256
        SharedTransportConsumerLag = 0
        SharedTransportLaneContractPassed = $true; SharedTransportLaneResults = $laneResults
        SharedTransportDomainDropTotal = 8; SharedTransportDomainWriteFailureTotal = 2
        SharedTransportDomainPendingAfterDrain = 0
        SharedTransportDomainsWithAcceptedHighWater = 25
        SharedTransportDomainsWithDropAccounting = 8
        SharedTransportConsumedPayloads = 336; SharedTransportSequenceOrdered = $true
        SharedTransportInvalidPayloads = 0
        SharedTransportWirePriorityCounts = $laneAcceptedCounts
        SharedTransportPriorityMismatches = 0
        SharedTransportBatchMaximumItems = 8
        SharedTransportBatchAccepted = 248
        SharedTransportBatchCount = 32
        SharedTransportBatchEventSignalDelta = 32
        SharedTransportBatchLockAcquisitionDelta = 256
        SharedTransportBatchContractVerifiedByNativeExport = $true
        NativeProbeSelfTestPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/raw/trace/native-probe-selftest.json'
        NativeProbeSelfTestSHA256 = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
        NativeProbeSelfTestSchemaId = 'God2NativeProbeSelfTest'
        NativeProbeSelfTestSchemaVersion = 1
        NativeProbeDuplicateNegativeCount = 3
        NativeProbeIdentityProfile = 'TestOnlyExactFixture'
        SensitiveOutboundPrivacyPassed = $true
        SensitiveOutboundPrivacyRecord = $nativeReport.SensitiveOutboundPrivacy
        NativeSemanticWirePath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/dll-native-semantic-event-v2.jsonl'
        NativeSemanticWireSHA256 = 'B' * 64; NativeSemanticWireCount = 25
        NativeSemanticWireVerificationPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/native-semantic-wire-verification.json'
        NativeSemanticWireVerificationSHA256 = 'C' * 64
        NativeSemanticWireVerificationSchemaId = 'God2NativeSemanticWireVerification'
        NativeSemanticWireVerificationSchemaVersion = 1
        NativeSemanticWireVerificationReport = $wireVerification
        UltimateNativeWireReportPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/u.json'
        UltimateNativeWireReportSHA256 = 'D' * 64; UltimateNativeWireReport = $ultimateWire
        DeepProbeCandidateMapPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/raw/trace/deep-probe-candidate-map.json'
        DeepProbeCandidateMapSHA256 = 'E' * 64
        DeepProbeCandidateMapSchemaVersion = 'god2-deep-probe-candidate-map-v2'
        DeepProbeCandidateMapDuplicateNegativeCount = 3
        ProbeDomainWirePath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/probe-domain-wire.jsonl'
        ProbeDomainWireSHA256 = 'D' * 64; ProbeDomainWireDuplicateNegativeCount = 3
        SemanticAdmissionCapContractPassed = $true
        SemanticAdmissionCapSharedLedgerPassed = $true
        SemanticAdmissionCapLiveStateUntouched = $true
        SemanticAdmissionHardLimit = 2000000; SemanticAdmissionLowPriorityCeiling = 1500000
        SemanticAdmissionCapResults = $nativeReport.SemanticAdmissionCapResults
        FaultDiagnosticConsumerAckPath = 'Artifacts/ClientInstrumentation/LoginTrial/contract-fixture/analysis/fault-ack.json'
        FaultDiagnosticConsumerAckSHA256 = 'E' * 64
        FaultDiagnosticConsumerAckArtifactVerified = $true
        FaultDiagnosticConsumerAck = [pscustomobject]@{ schemaId = 'God2FaultDiagnosticConsumerAck'; schemaVersion = 1; passed = $true }
        FaultDiagnosticCrossProcessObserved = $true
        FaultDiagnosticPriorityPolicyPassed = $true; FaultDiagnosticSelectedPriority = 0
        StartupProbeDiagnosticSelectedPriority = 3
        FaultDiagnosticAcceptedAfterLowPriorityCeiling = $true
        FaultDiagnosticAcceptedBeyondLegacyHardLimit = $true
        FaultDiagnosticBeyondLegacyLimitDropReason = 0
        NativeProbeSelfTestReport = $nativeReport
    }
}
function New-UltimateContractFixture {
    $requiredNames = @(
        '01 SemanticEvent v2 round-trip',
        '02 SemanticEvent corruption rejection',
        '02b SemanticEvent 25-type roundtrip corruption version dispatch matrix',
        '02c DLL native 25-wire cross-runtime reader dispatch binding',
        '03 Shared ring ordering','04 Ring overflow accounting','05 Per-domain backpressure',
        '06 Probe exception isolation','06b Deep-probe candidate promotion hard gate',
        '34 Stop/detach/unload bounded-work contract'
    )
    $tests = @($requiredNames | ForEach-Object { [pscustomobject]@{ name = $_; status = 'PASS'; detail = 'ContractVerifiedWithDeterministicSyntheticFixture' } })
    for ($index = $tests.Count; $index -lt 53; $index++) {
        $tests += [pscustomobject]@{ name = ('Fixture filler {0:D2}' -f $index); status = 'PASS'; detail = 'ContractVerifiedWithDeterministicSyntheticFixture' }
    }
    $matrixResults = @()
    for ($index = 0; $index -lt $script:ExpectedSemanticEventTypes.Count; $index++) {
        $expected = $script:ExpectedSemanticEventTypes[$index]
        $matrixResults += [pscustomobject]@{
            index = $index
            eventType = $expected.EventType
            producerImplemented = [bool]$expected.RuntimeProducer
            nativeSelfTestProducer = $true
            fixtureProducer = $true
            blockedCapability = $index -ge 5
            readerAccepted = $true
            dispatchSink = $expected.DispatchSink
            roundTripPassed = $true
            corruptionRejected = $true
            versionRejected = $true
            v1Compatible = $true
            dispatchPassed = $true
            promotionPolicy = if ($expected.EventType -in @('FunctionRoleCandidate','ProbeDiagnostic')) { 'NoPromotion' } else { 'StandardEvidenceGates' }
            noPromotionEnforced = $expected.EventType -in @('FunctionRoleCandidate','ProbeDiagnostic')
            nativeWireReaderAccepted = $true
            nativeWireDispatchPassed = $true
        }
    }
    return [pscustomobject]@{
        schemaVersion = 'god2-ultimate-selftest-report-v1'
        evidenceSource = 'DeterministicSyntheticContractFixture'
        liveEvidenceClaimed = $false
        total = 53; passed = 53; failed = 0
        bundleZipSha256 = 'A' * 64
        importerGateStatus = 'RECOVERY_BUNDLE_IMPORTER_EXPECTED_IDENTITY_BLOCK_PASS'
        importerExitCode = 4
        semanticEventMatrix = [pscustomobject]@{
            schemaId = 'God2SemanticEventMatrix'; schemaVersion = 2
            rows = $matrixResults
            totals = [pscustomobject]@{
                fixtureProducerCount = 25; producerImplementedCount = 25
                nativeSelfTestProducerCount = 25
                blockedCapabilityCount = 20; readerAcceptedCount = 25
                dispatchCount = 25; roundTripCount = 25; corruptionRejectedCount = 25
                versionRejectedCount = 25; v1CompatibleCount = 25
                noPromotionEnforcedCount = 2
                nativeWireReaderAcceptedCount = 25; nativeWireDispatchCount = 25
            }
            nativeWireBinding = [pscustomobject]@{
                bound = $true; verified = $true; inputSHA256 = 'B' * 64
                eventTypeDigest = 'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C'
                inputLineCount = 25
            }
        }
        tests = $tests
    }
}
function Invoke-EnhancedCaptureContractSelfTest {
    if ($Final) { throw '-ContractSelfTest cannot be combined with -Final.' }
    $testResults = [Collections.Generic.List[object]]::new()
    function Add-SelfTestResult([string]$Name, [bool]$Passed, [string]$Detail) {
        $testResults.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    }
    $evidenceFixture = New-EvidenceFixtureContractFixture
    $network = New-NetworkContractFixture
    $product = New-ProductContractFixture
    $native = Get-JsonPropertyValue $product 'NativeProbeSelfTestReport'
    $candidateMap = New-DeepProbeCandidateMapFixture
    $ultimate = New-UltimateContractFixture
    $schema = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'enhanced-capture-acceptance.schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $statusSchema = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\ultimate\assets\schemas\enhanced-capture-status.schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $statusV3Schema = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\ultimate\assets\schemas\enhanced-capture-status-v3.schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $statusV3 = [pscustomobject][ordered]@{
        SchemaVersion='god2-enhanced-capture-status-v3'; GeneratedAtUtc=[DateTime]::UtcNow.ToString('o')
        AuthorityProfile='TestOnlyExactFixture'; OfficialRuntimeObserved=$false
        ExtendedReady=$false; NetworkBridgeReady=$true; InternalBridgeReady=$false
        StrictUnloadVerified=$true; ModuleAbsent=$true; TargetAliveAfterUnload=$true
        BridgeSchemaId='god2-unload-neutral-bridge-v1'; BridgePhase='Detached'; BridgeState='RetainedPassThrough'
        BridgeNoDllPointers=$true; DllPointerCount=0; WritableExecutablePageCount=0; ObserverRundown=0
        PendingApplicationCallbacks=0; ExactPersistentOriginalPointerCount=7; ExactEntryPointerCount=11
        ExactCompletionThunkCount=256; IatRestored=$true; HotpatchRestored=$true
        RestoreFaultLifecyclePassed=$true; RestoreFaultFreeLibraryCallCount=1
        RestoreFaultVerifiedAbsentSnapshotCount=2; ProductEvidenceSHA256='A' * 64
        NativeEvidenceSHA256='B' * 64; RestoreFaultEvidenceSHA256='C' * 64
    }
    $evidenceFixturePositive = Test-EvidenceFixtureContract $evidenceFixture
    $networkPositive = Test-NetworkRuntimeContract $network
    $nativePositive = Test-NativeProbeRuntimeContract $native
    $productPositive = Test-ProductRuntimeContract $product
    $candidateMapPositive = Test-DeepProbeCandidateMapContract $candidateMap
    $ultimatePositive = Test-UltimateRuntimeContract $ultimate 0
    $schemaPositive = Test-AcceptanceSchemaContract $schema
    $statusSchemaPositive = Test-EnhancedCaptureStatusSchemaContract $statusSchema
    $statusV3SchemaPositive = Test-BridgeAwareStatusV3SchemaContract $statusV3Schema
    $statusV3Positive = Test-BridgeAwareStatusV3Contract $statusV3
    $guiSourcePath = Join-Path $PSScriptRoot '..\Gui.cpp'
    $mainSourcePath = Join-Path $PSScriptRoot '..\Main.cpp'
    $statusSchemaPath = Join-Path $PSScriptRoot '..\ultimate\assets\schemas\enhanced-capture-status.schema.json'
    $evidenceSourcePath = Join-Path $PSScriptRoot '..\EvidencePackage.cpp'
    $sharedAvailabilityPath = Join-Path $PSScriptRoot '..\ultimate\assets\schemas\semantic-shared-ring-health-availability.schema.json'
    $candidateAvailabilityPath = Join-Path $PSScriptRoot '..\ultimate\assets\schemas\deep-probe-candidate-map-availability.schema.json'
    $guiSourceSha = (Get-FileHash -LiteralPath $guiSourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $mainSourceSha = (Get-FileHash -LiteralPath $mainSourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $statusSchemaSha = (Get-FileHash -LiteralPath $statusSchemaPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $evidenceSourceSha = (Get-FileHash -LiteralPath $evidenceSourcePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $sharedAvailabilitySha = (Get-FileHash -LiteralPath $sharedAvailabilityPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $candidateAvailabilitySha = (Get-FileHash -LiteralPath $candidateAvailabilityPath -Algorithm SHA256).Hash.ToUpperInvariant()
    Add-SelfTestResult 'positive.evidence-fixture-contract' $evidenceFixturePositive.Passed ($evidenceFixturePositive.Issues -join ',')
    Add-SelfTestResult 'positive.network-runtime-contract' $networkPositive.Passed ($networkPositive.Issues -join ',')
    Add-SelfTestResult 'positive.native-probe-runtime-contract' $nativePositive.Passed ($nativePositive.Issues -join ',')
    Add-SelfTestResult 'positive.product-runtime-contract' $productPositive.Passed ($productPositive.Issues -join ',')
    Add-SelfTestResult 'positive.candidate-map-runtime-contract' $candidateMapPositive.Passed ($candidateMapPositive.Issues -join ',')
    Add-SelfTestResult 'positive.ultimate-runtime-contract' $ultimatePositive.Passed ($ultimatePositive.Issues -join ',')
    Add-SelfTestResult 'positive.acceptance-schema-contract' $schemaPositive.Passed ($schemaPositive.Issues -join ',')
    Add-SelfTestResult 'positive.enhanced-status-schema-contract' $statusSchemaPositive.Passed ($statusSchemaPositive.Issues -join ',')
    Add-SelfTestResult 'positive.bridge-aware-status-v3-schema-contract' $statusV3SchemaPositive.Passed ($statusV3SchemaPositive.Issues -join ',')
    Add-SelfTestResult 'positive.bridge-aware-status-v3-runtime-contract' $statusV3Positive.Passed ($statusV3Positive.Issues -join ',')
    Add-SelfTestResult 'positive.gui-status-evidence-availability-source-locks' (
        $guiSourceSha -ceq $script:ExpectedGuiSourceSha256 -and
        $mainSourceSha -ceq $script:ExpectedMainSourceSha256 -and
        $statusSchemaSha -ceq $script:ExpectedEnhancedCaptureStatusSchemaSha256 -and
        $evidenceSourceSha -ceq $script:ExpectedEvidenceSourceSha256 -and
        $sharedAvailabilitySha -ceq $script:ExpectedSharedHealthAvailabilitySchemaSha256 -and
        $candidateAvailabilitySha -ceq $script:ExpectedCandidateMapAvailabilitySchemaSha256
    ) "Gui=$guiSourceSha; Main=$mainSourceSha; statusSchema=$statusSchemaSha; Evidence=$evidenceSourceSha; sharedAvailability=$sharedAvailabilitySha; candidateAvailability=$candidateAvailabilitySha"
    Add-SelfTestResult 'positive.portable-text-contract' (@(Get-PortableTextContractIssues "portable`r`ntext`tvalue").Count -eq 0) 'CR/LF/TAB are permitted portable text'
    $productJson = $product | ConvertTo-Json -Depth 20 -Compress
    Add-SelfTestResult 'positive.product-strict-json-no-duplicate-keys' (
        (Test-StrictJsonTextContract $productJson).Passed) 'typed product fixture is strict JSON without duplicate object keys'

    $tamperStatusV3 = Copy-ContractFixture $statusV3
    $tamperStatusV3.OfficialRuntimeObserved = $true
    Add-SelfTestResult 'negative.status-v3-test-profile-official-spoof' (-not (Test-BridgeAwareStatusV3Contract $tamperStatusV3).Passed) 'test-only profile cannot claim official runtime'
    $tamperStatusV3 = Copy-ContractFixture $statusV3
    $tamperStatusV3.DllPointerCount = 1
    Add-SelfTestResult 'negative.status-v3-dll-pointer-remains' (-not (Test-BridgeAwareStatusV3Contract $tamperStatusV3).Passed) 'retained bridge cannot carry a DLL pointer'
    $tamperStatusV3 = Copy-ContractFixture $statusV3
    $tamperStatusV3.RestoreFaultFreeLibraryCallCount = 2
    Add-SelfTestResult 'negative.status-v3-duplicate-free-library' (-not (Test-BridgeAwareStatusV3Contract $tamperStatusV3).Passed) 'restore lifecycle permits exactly one owned FreeLibrary call'
    $tamperStatusV3Schema = Copy-ContractFixture $statusV3Schema
    $tamperStatusV3Schema.additionalProperties = $true
    Add-SelfTestResult 'negative.status-v3-schema-open-root' (-not (Test-BridgeAwareStatusV3SchemaContract $tamperStatusV3Schema).Passed) 'bridge-aware status root must remain closed'

    Add-SelfTestResult 'negative.portable-text-nul' ((Get-PortableTextContractIssues ("prefix" + [char]0 + "suffix")) -contains 'NUL') 'NUL rejected'
    Add-SelfTestResult 'negative.portable-text-c1' ((Get-PortableTextContractIssues ("prefix" + [char]0x85 + "suffix")) -contains 'CONTROL_OR_FORMAT_CHARACTER') 'C1 control rejected'
    Add-SelfTestResult 'negative.portable-text-zero-width' ((Get-PortableTextContractIssues ("prefix" + [char]0x200B + "suffix")) -contains 'CONTROL_OR_FORMAT_CHARACTER') 'Unicode format/zero-width rejected'
    Add-SelfTestResult 'negative.portable-text-mojibake' ((Get-PortableTextContractIssues ("prefix" + [char]0x9225 + "suffix")) -contains 'MOJIBAKE_OR_REPLACEMENT') 'known mojibake marker rejected'
    Add-SelfTestResult 'negative.portable-text-absolute-path' ((Get-PortableTextContractIssues 'leak=C:\local\identity') -contains 'ABSOLUTE_OR_UNC_PATH') 'absolute local path rejected'

    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks = @($tamperEvidenceFixture.Checks | Select-Object -First 76)
    $tamperEvidenceFixture.CheckCount = 76
    $tamperEvidenceFixture.Passed = 76
    Add-SelfTestResult 'negative.evidence-fixture-old-count' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'internally consistent pre-10d count rejected without locking exact77'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks[1].Status = 'FAIL'
    $tamperEvidenceFixture.Failed = 0
    Add-SelfTestResult 'negative.evidence-fixture-failed-row-spoof' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'failed named row cannot coexist with Failed=0'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks[2].Name = '10c_removed'
    Add-SelfTestResult 'negative.evidence-fixture-required-10c-missing' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'typed cleanup inventory check removed'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks[3].Name = '10d_removed'
    Add-SelfTestResult 'negative.evidence-fixture-required-10d-missing' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'verified no-module cleanup distinction check removed'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks[5].Name = '14f_removed'
    Add-SelfTestResult 'negative.evidence-fixture-required-14f-missing' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'stale candidate-map placeholder rejection check removed'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.FixturePath = 'C:\local\fixture'
    Add-SelfTestResult 'negative.evidence-fixture-absolute-path' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'fixture path leaked local absolute path'
    $tamperEvidenceFixture = Copy-ContractFixture $evidenceFixture
    $tamperEvidenceFixture.Checks[76].Name = $tamperEvidenceFixture.Checks[75].Name
    Add-SelfTestResult 'negative.evidence-fixture-duplicate-check' (-not (Test-EvidenceFixtureContract $tamperEvidenceFixture).Passed) 'duplicate check name rejected'

    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.required = @($tamperSchema.required | Where-Object { $_ -cne 'runtimeEvidence' })
    Add-SelfTestResult 'negative.schema-runtime-evidence-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'runtimeEvidence removed from top required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.guiVisibility.properties.expectedSmokeKeys.minItems = 10
    $tamperSchema.'$defs'.guiVisibility.properties.expectedSmokeKeys.maxItems = 10
    Add-SelfTestResult 'negative.schema-old-ten-key-gui' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'GUI schema regressed to pre-consumer-lifecycle ten keys'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.properties.inputBindings.minItems = 26
    $tamperSchema.properties.inputBindings.maxItems = 27
    Add-SelfTestResult 'negative.schema-availability-source-bindings-omitted' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'availability schema source bindings removed from input inventory count'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.properties.inputBindings.uniqueItems = $false
    Add-SelfTestResult 'negative.schema-input-binding-duplicates-allowed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'input binding array cannot permit duplicate identities'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.properties.inputBindings.allOf = @($tamperSchema.properties.inputBindings.allOf | Select-Object -Skip 1)
    Add-SelfTestResult 'negative.schema-critical-raw-input-role-contains-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'one exact-once critical raw evidence role constraint removed'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.runtimeEvidence.required = @(
        $tamperSchema.'$defs'.runtimeEvidence.required | Where-Object { $_ -cne 'productArtifactBindings' })
    Add-SelfTestResult 'negative.schema-product-artifact-bindings-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'strict raw Product artifact bindings removed from runtime required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.productArtifactBindings.properties.allStrictBound.const = $false
    Add-SelfTestResult 'negative.schema-product-artifact-bindings-false' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'schema permits unbound Product raw artifacts'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.nativeSemanticWire.required = @(
        $tamperSchema.'$defs'.nativeSemanticWire.required | Where-Object { $_ -cne 'strictJsonlContractPassed' })
    Add-SelfTestResult 'negative.schema-native-wire-strict-contract-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'strict native JSONL validation made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.nativeSemanticWire.properties.crossProcessNegativeCount.const = 1
    Add-SelfTestResult 'negative.schema-native-wire-cross-process-negatives-reduced' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'one of two PID binding negatives removed'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticAdmissionCap.required = @(
        $tamperSchema.'$defs'.semanticAdmissionCap.required | Where-Object { $_ -cne 'liveStateUntouched' })
    Add-SelfTestResult 'negative.schema-semantic-cap-live-state-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'private admission-cap fixture no longer must prove live state untouched'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticAdmissionCap.properties.hardLimit.const = 1999999
    Add-SelfTestResult 'negative.schema-semantic-cap-hard-limit-spoof' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'semantic process cap constant replaced'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.sharedRing.required = @(
        $tamperSchema.'$defs'.sharedRing.required | Where-Object { $_ -cne 'consumerDrainVerified' })
    Add-SelfTestResult 'negative.schema-ring-consumer-drain-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'producer-close drain proof made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.lifecycle.required = @(
        $tamperSchema.'$defs'.lifecycle.required | Where-Object { $_ -cne 'attachResultSha256' })
    Add-SelfTestResult 'negative.schema-attach-raw-sha-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'typed attach raw SHA binding made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.guiVisibility.required = @(
        $tamperSchema.'$defs'.guiVisibility.required |
        Where-Object { $_ -cne 'DllEnhancedCaptureConsumerStateLifecycle' })
    Add-SelfTestResult 'negative.schema-consumer-lifecycle-gui-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'consumer lifecycle direct result removed from GUI required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.guiVisibility.properties.enhancedCaptureStatusSchemaVersion.oneOf[0].const = 'god2-enhanced-capture-status-v1'
    Add-SelfTestResult 'negative.schema-old-enhanced-status-v1' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'GUI schema accepts obsolete enhanced status v1'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.lifecycle.additionalProperties = $true
    Add-SelfTestResult 'negative.schema-open-lifecycle-object' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'lifecycle object opened to unknown properties'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.lifecycle.properties.detachResult = [pscustomobject]@{
        type = 'string'; pattern = '^DETACHED'
    }
    Add-SelfTestResult 'negative.schema-legacy-plaintext-detach-result' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'acceptance lifecycle schema cannot restore legacy token-string authority'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.injectorResultV2.additionalProperties = $true
    Add-SelfTestResult 'negative.schema-open-injector-v2-object' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'injector v2 object opened to unknown fields'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.injectorResultV2.required = @(
        $tamperSchema.'$defs'.injectorResultV2.required | Where-Object { $_ -cne 'strictUnloadVerified' })
    Add-SelfTestResult 'negative.schema-injector-v2-missing-field-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'one of exact 25 injector fields removed from required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.injectorResultV2.properties.moduleUnloaded.type = 'string'
    Add-SelfTestResult 'negative.schema-injector-v2-quoted-boolean' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'injector v2 moduleUnloaded changed from boolean to string'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.injectorDetachResultV2.allOf[1].properties.stopSucceeded.const = $false
    Add-SelfTestResult 'negative.schema-injector-v2-detach-semantic-spoof' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'DETACHED schema no longer requires successful Stop'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.injectorIdentityBlockedResultV2.allOf[1].properties.injectionAttempted.const = $true
    Add-SelfTestResult 'negative.schema-injector-v2-identity-semantic-spoof' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'wrong-identity schema permits attempted injection'
    foreach ($iatField in @(
            'iatOwnerLifecycleSelfTestPassed','iatOwnerGoneRetired','iatAddressReuseNotClobbered',
            'iatReplacementCasRestored','iatOriginalNotClobbered','iatThirdPartyNotClobbered')) {
        $tamperSchema = Copy-ContractFixture $schema
        $tamperSchema.'$defs'.lifecycle.required = @(
            $tamperSchema.'$defs'.lifecycle.required | Where-Object { $_ -cne $iatField })
        Add-SelfTestResult "negative.schema-lifecycle-$iatField-optional" (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) "$iatField removed from lifecycle required"
        $tamperSchema = Copy-ContractFixture $schema
        $tamperSchema.'$defs'.lifecycle.properties.$iatField.const = $false
        Add-SelfTestResult "negative.schema-lifecycle-$iatField-false" (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) "$iatField no longer requires true"
    }
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.nativeEventProducers.properties.sortedNameDigestSha256.const = '0' * 64
    Add-SelfTestResult 'negative.schema-native-digest-spoof' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'native event digest constant replaced'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticEventMatrix.required = @('schemaId','schemaVersion','results','totals')
    Add-SelfTestResult 'negative.schema-semantic-matrix-legacy-results-shape' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'schema no longer matches the actual Ultimate rows property'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticEventMatrixTotals.required = @(
        $tamperSchema.'$defs'.semanticEventMatrixTotals.required |
        Where-Object { $_ -cne 'versionRejectedCount' })
    Add-SelfTestResult 'negative.schema-semantic-matrix-total-missing' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'actual writer total made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticEventResult.required = @(
        $tamperSchema.'$defs'.semanticEventResult.required | Where-Object { $_ -cne 'index' })
    Add-SelfTestResult 'negative.schema-semantic-matrix-row-index-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'actual writer row index made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.semanticEventMatrix.required = @(
        $tamperSchema.'$defs'.semanticEventMatrix.required | Where-Object { $_ -cne 'nativeWireBinding' })
    Add-SelfTestResult 'negative.schema-semantic-native-wire-binding-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'cross-runtime native wire binding made optional'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.nativeSemanticWireBinding.properties.verified.const = $false
    Add-SelfTestResult 'negative.schema-semantic-native-wire-verification-false' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'schema permits an unverified native wire binding'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.nativeSemanticWireBinding.properties.eventTypeDigest.const = '0' * 64
    Add-SelfTestResult 'negative.schema-semantic-native-wire-digest-spoof' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'schema accepts a replaced DLL event-type digest'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.freshness.required = @($tamperSchema.'$defs'.freshness.required |
        Where-Object { $_ -cne 'nativeProbeEvidenceAfterAllBoundInputs' })
    Add-SelfTestResult 'negative.schema-native-freshness-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'native evidence freshness removed from required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.freshness.properties.enhancedCaptureStatusSchemaSha256.const = '0' * 64
    Add-SelfTestResult 'negative.schema-status-schema-sha-unbound' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'status schema hash no longer uses canonical SHA-256 definition'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.freshness.properties.guiSourceSha256.const = '0' * 64
    Add-SelfTestResult 'negative.schema-gui-source-sha-unbound' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'GUI source hash no longer matches source lock'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.freshness.properties.mainSourceSha256.const = '0' * 64
    Add-SelfTestResult 'negative.schema-main-source-sha-unbound' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'Main source hash no longer matches source lock'
    foreach ($sourceLockField in @('evidenceSourceSha256','sharedHealthAvailabilitySchemaSha256',
            'candidateMapAvailabilitySchemaSha256')) {
        $tamperSchema = Copy-ContractFixture $schema
        $tamperSchema.'$defs'.freshness.properties.$sourceLockField.const = '0' * 64
        Add-SelfTestResult "negative.schema-$sourceLockField-unbound" (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) "$sourceLockField no longer matches source lock"
    }
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.enhancedCaptureStatusSchemaBinding.properties.sha256.const = '0' * 64
    Add-SelfTestResult 'negative.schema-status-runtime-binding-sha-unbound' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'runtime status schema binding hash no longer matches source lock'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.inputBinding.properties.role.enum = @(
        $tamperSchema.'$defs'.inputBinding.properties.role.enum |
        Where-Object { $_ -cne 'NATIVE_PROBE_SELFTEST_EVIDENCE' })
    Add-SelfTestResult 'negative.schema-native-input-binding-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'native evidence input role removed'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.inputBinding.properties.role.enum = @(
        $tamperSchema.'$defs'.inputBinding.properties.role.enum |
        Where-Object { $_ -cne 'NATIVE_SEMANTIC_WIRE_VERIFICATION_EVIDENCE' })
    Add-SelfTestResult 'negative.schema-native-wire-verifier-input-binding-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'native wire verifier raw evidence input role removed'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.inputBinding.properties.role.enum = @(
        $tamperSchema.'$defs'.inputBinding.properties.role.enum |
        Where-Object { $_ -cne 'CANONICAL_V1_SEMANTIC_WIRE_BINDING_EVIDENCE' })
    Add-SelfTestResult 'negative.schema-canonical-v1-binding-input-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'fresh canonical-v1 SHA/digest binding input role removed'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.PSObject.Properties.Remove('allOf')
    Add-SelfTestResult 'negative.schema-final-pass-relations-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'final PASS no longer forces all evidence/UI/check/freshness gates PASS'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.runtimeEvidence.properties.officialClientLiveEvidenceClaimed.type = 'string'
    Add-SelfTestResult 'negative.schema-official-runtime-live-claim-untyped' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'official runtime observation flag is no longer a typed boolean'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.runtimeEvidence.properties.officialClientLiveGateStatus.enum = @(
        $tamperSchema.'$defs'.runtimeEvidence.properties.officialClientLiveGateStatus.enum |
        Where-Object { $_ -cne 'PASS_HISTORICAL_OFFICIAL_EXACT_CLIENT_RUNTIME' })
    Add-SelfTestResult 'negative.schema-historical-runtime-profile-removed' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'historical official evidence can no longer be distinguished from current live'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.portablePath.pattern = '^(\\.|[A-Za-z0-9_.-][A-Za-z0-9_./ -]*)$'
    Add-SelfTestResult 'negative.schema-portable-path-parent-traversal' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'portable path schema accepts parent/empty segments'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.runtimeEvidence.required = @(
        $tamperSchema.'$defs'.runtimeEvidence.required |
        Where-Object { $_ -cne 'evidencePackageFixture' })
    Add-SelfTestResult 'negative.schema-evidence-fixture-runtime-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'Evidence fixture runtime binding removed from required'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.evidencePackageFixture.properties.checkCount.minimum = 76
    Add-SelfTestResult 'negative.schema-evidence-fixture-old-minimum' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'Evidence fixture schema accepts pre-no-hardcoded-pass count'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.evidencePackageFixture.properties.passed.minimum = 76
    Add-SelfTestResult 'negative.schema-evidence-fixture-old-passed-minimum' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'Evidence fixture schema accepts pre-10d passed count'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.evidencePackageFixture.properties.requiredChecks.items.enum = @(
        $tamperSchema.'$defs'.evidencePackageFixture.properties.requiredChecks.items.enum |
        Where-Object { $_ -cne '14f_missing_candidate_map_never_emits_stale_v1_placeholder' })
    Add-SelfTestResult 'negative.schema-evidence-fixture-14f-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) '14f stale candidate-map placeholder check removed from schema'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.'$defs'.evidencePackageFixture.properties.requiredChecks.items.enum = @(
        $tamperSchema.'$defs'.evidencePackageFixture.properties.requiredChecks.items.enum |
        Where-Object { $_ -cne '10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown' })
    Add-SelfTestResult 'negative.schema-evidence-fixture-10d-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) '10d verified no-module cleanup distinction removed from schema'
    $tamperSchema = Copy-ContractFixture $schema
    $tamperSchema.properties.selfTests.required = @(
        $tamperSchema.properties.selfTests.required |
        Where-Object { $_ -cne 'evidenceFixture' })
    Add-SelfTestResult 'negative.schema-evidence-fixture-selftest-optional' (-not (Test-AcceptanceSchemaContract $tamperSchema).Passed) 'Evidence fixture omitted from top selfTests'
    $tamperStatusSchema = Copy-ContractFixture $statusSchema
    $tamperStatusSchema.properties.StrictUnloadVerified.type = 'string'
    Add-SelfTestResult 'negative.status-schema-untyped-unload-proof' (-not (Test-EnhancedCaptureStatusSchemaContract $tamperStatusSchema).Passed) 'StrictUnloadVerified changed from boolean to string'
    $tamperStatusSchema = Copy-ContractFixture $statusSchema
    $tamperStatusSchema.additionalProperties = $true
    Add-SelfTestResult 'negative.status-schema-open-root' (-not (Test-EnhancedCaptureStatusSchemaContract $tamperStatusSchema).Passed) 'status schema root accepts unknown fields'

    $tamperCandidate = Copy-ContractFixture $candidateMap
    $tamperCandidate | Add-Member -NotePropertyName UnknownRoot -NotePropertyValue $true
    Add-SelfTestResult 'negative.candidate-map-unknown-root-field' (-not (Test-DeepProbeCandidateMapContract $tamperCandidate).Passed) 'candidate map root is closed to unknown fields'
    $tamperCandidate = Copy-ContractFixture $candidateMap
    $tamperCandidate.TargetIdentityVerified = $true
    Add-SelfTestResult 'negative.candidate-map-fixture-identity-promoted' (-not (Test-DeepProbeCandidateMapContract $tamperCandidate).Passed) 'wrong-identity fixture cannot claim target verification'
    $tamperCandidate = Copy-ContractFixture $candidateMap
    $tamperCandidate.Domains[4].VerificationContract.TypedRuntimeEvidence = $true
    Add-SelfTestResult 'negative.candidate-map-one-gate-promoted' (-not (Test-DeepProbeCandidateMapContract $tamperCandidate).Passed) 'candidate-only domain cannot claim one verification gate passed'
    $tamperCandidate = Copy-ContractFixture $candidateMap
    $tamperCandidate.Domains[8].CandidateCount = 1
    Add-SelfTestResult 'negative.candidate-map-unbound-candidate-count' (-not (Test-DeepProbeCandidateMapContract $tamperCandidate).Passed) 'candidate count must exactly reconcile the wrong-identity empty candidate list'
    $tamperCandidate = Copy-ContractFixture $candidateMap
    $tamperCandidate.EngineBounds.WritableMemoryScanned = $true
    Add-SelfTestResult 'negative.candidate-map-writable-memory-scan' (-not (Test-DeepProbeCandidateMapContract $tamperCandidate).Passed) 'candidate discovery cannot scan writable memory'

    $tamper = Copy-ContractFixture $product
    $tamper.PSObject.Properties.Remove('ModuleUnloaded')
    Add-SelfTestResult 'negative.missing-required-property' (-not (Test-ProductRuntimeContract $tamper).Passed) 'ModuleUnloaded removed'
    $tamper = Copy-ContractFixture $product
    $tamper.ProbeDomainResults[24].Index = 23
    Add-SelfTestResult 'negative.duplicate-domain-index' (-not (Test-ProductRuntimeContract $tamper).Passed) 'domain index duplicated'
    $tamper = Copy-ContractFixture $product
    $tamper.ProbeDomainResults[4].RuntimeDiagnosticObserved = $false
    Add-SelfTestResult 'negative.domain-runtime-diagnostic-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'runtime diagnostic false'
    $tamper = Copy-ContractFixture $product
    $tamper.SemanticEventTypeDigestSHA256 = '0' * 64
    Add-SelfTestResult 'negative.native-event-type-digest-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) '25-count digest replaced'
    $tamper = Copy-ContractFixture $product
    $tamper.CandidateTotalGateCount = 13
    Add-SelfTestResult 'negative.activation-gate-count' (-not (Test-ProductRuntimeContract $tamper).Passed) 'total gate count 13'
    $tamper = Copy-ContractFixture $product
    $tamper.CandidateGateResults[9].RejectedOnAllCandidateDomains = $false
    Add-SelfTestResult 'negative.activation-gate-row-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'one of 14 gate rows not rejected on all candidate domains'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestSHA256 = 'a' * 64
    Add-SelfTestResult 'negative.native-report-hash-shape' (-not (Test-ProductRuntimeContract $tamper).Passed) 'native report SHA not canonical uppercase'
    $tamper = Copy-ContractFixture $product
    $tamper | Add-Member -NotePropertyName UnknownProductField -NotePropertyValue $true
    Add-SelfTestResult 'negative.product-unknown-top-field' (-not (Test-ProductRuntimeContract $tamper).Passed) 'unknown Product summary field rejected by exact 100-key contract'
    $tamper = Copy-ContractFixture $product
    $tamper.PSObject.Properties.Remove('AttachResultPath')
    Add-SelfTestResult 'negative.product-attach-raw-path-missing' (-not (Test-ProductRuntimeContract $tamper).Passed) 'typed attach record cannot omit its immutable raw path'
    $tamper = Copy-ContractFixture $product
    $tamper.AttachResultPath = '../attach-result-v2.json'
    Add-SelfTestResult 'negative.product-attach-raw-path-traversal' (-not (Test-ProductRuntimeContract $tamper).Passed) 'attach raw path parent traversal rejected'
    $tamper = Copy-ContractFixture $product
    $tamper.DetachResultSHA256 = 'a' * 64
    Add-SelfTestResult 'negative.product-detach-raw-hash-lowercase' (-not (Test-ProductRuntimeContract $tamper).Passed) 'detach raw SHA must be uppercase canonical SHA-256'
    $tamper = Copy-ContractFixture $product
    $tamper.StrictInjectorResultParserNegativeCount = 9
    Add-SelfTestResult 'negative.product-injector-parser-negative-count' (-not (Test-ProductRuntimeContract $tamper).Passed) 'all ten strict injector-result parser negatives are required'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportProducerProcessId = 4243
    Add-SelfTestResult 'negative.product-producer-pid-target-mismatch' (-not (Test-ProductRuntimeContract $tamper).Passed) 'shared producer PID must equal target/native producer process'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportConsumerProcessId = 4242
    Add-SelfTestResult 'negative.product-consumer-pid-not-cross-process' (-not (Test-ProductRuntimeContract $tamper).Passed) 'consumer PID cannot equal target producer PID'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeSemanticWireProcessIds[7] = 7000
    Add-SelfTestResult 'negative.product-native-wire-pid-mismatch' (-not (Test-ProductRuntimeContract $tamper).Passed) 'every one of 25 native wire rows must bind the target producer PID'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportCrossProcessNegativeCount = 1
    Add-SelfTestResult 'negative.product-cross-process-negative-count' (-not (Test-ProductRuntimeContract $tamper).Passed) 'consumer-alias and wrong-wire-PID negatives are both required'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportConsumerDrainVerified = $false
    Add-SelfTestResult 'negative.product-consumer-drain-false' (-not (Test-ProductRuntimeContract $tamper).Passed) 'producer-close consumer drain proof cannot be false'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportConsumerClosed = $false
    Add-SelfTestResult 'negative.product-consumer-lifecycle-open' (-not (Test-ProductRuntimeContract $tamper).Passed) 'consumer lifecycle must close after the producer drain'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportLaneResults[0].Dropped = 1
    $tamper.SharedTransportLaneResults[0].Sampled = 1
    $tamper.SharedTransportLaneResults[0].FirstDroppedSequence = 345
    $tamper.SharedTransportLaneResults[0].LastDroppedSequence = 345
    $tamper.SharedTransportLaneResults[0].LastDropReason = 10
    Add-SelfTestResult 'negative.product-p0-lane-sampling-loss' (-not (Test-ProductRuntimeContract $tamper).Passed) 'P0 cannot use the low-priority sampling exception'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportDomainPendingTotal = 1
    Add-SelfTestResult 'negative.product-domain-pending-total-nonzero' (-not (Test-ProductRuntimeContract $tamper).Passed) 'post-close domain pending total must reconcile to zero'
    $tamper = Copy-ContractFixture $product
    $tamper.SemanticAdmissionCapLiveStateUntouched = $false
    Add-SelfTestResult 'negative.product-semantic-cap-live-state-touched' (-not (Test-ProductRuntimeContract $tamper).Passed) 'private admission-cap fixture must not mutate live transport state'
    $tamper = Copy-ContractFixture $product
    $tamper.SemanticAdmissionHardLimit = 1999999
    Add-SelfTestResult 'negative.product-semantic-cap-hard-limit-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'semantic admission hard limit changed'
    $tamper = Copy-ContractFixture $product
    $tamper.SemanticAdmissionCapResults[1].FirstDroppedSequence = 1
    Add-SelfTestResult 'negative.product-semantic-cap-sequence-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'P0 continuity beyond the legacy limit cannot claim a drop sequence'
    $tamper = Copy-ContractFixture $product
    $tamper.SemanticAdmissionCapResults[1].AcceptedBeyondLegacyHardLimit = 0
    $tamper.SemanticAdmissionCapResults[1].DroppedBeyondLegacyHardLimit = 1
    $tamper.SemanticAdmissionCapResults[1].DropReason = 8
    Add-SelfTestResult 'negative.product-p0-legacy-hard-limit-drop' (-not (Test-ProductRuntimeContract $tamper).Passed) 'P0 loss at the legacy 2M boundary is a release blocker'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeSemanticWireVerificationReport.rows[8].dispatchPassed = $false
    Add-SelfTestResult 'negative.product-native-wire-verifier-row-false' (-not (Test-ProductRuntimeContract $tamper).Passed) 'one native wire verifier dispatch failure blocks the Product contract'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeSemanticWireVerificationSHA256 = 'c' * 64
    Add-SelfTestResult 'negative.product-native-wire-verifier-sha-lowercase' (-not (Test-ProductRuntimeContract $tamper).Passed) 'native wire verifier SHA must be canonical uppercase'
    $tamper = Copy-ContractFixture $product
    $tamper.UltimateNativeWireReport.semanticEventMatrix.nativeWireBinding.inputSHA256 = '0' * 64
    Add-SelfTestResult 'negative.product-ultimate-wire-input-sha-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'Product Ultimate report must bind the same native JSONL SHA'
    $tamper = Copy-ContractFixture $product
    $tamper.DeepProbeCandidateMapSchemaVersion = 'god2-deep-probe-candidate-map-v1'
    Add-SelfTestResult 'negative.product-candidate-map-schema-v1' (-not (Test-ProductRuntimeContract $tamper).Passed) 'candidate map v1 cannot satisfy Product runtime evidence'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport | Add-Member -NotePropertyName UnknownNativeField -NotePropertyValue $true
    Add-SelfTestResult 'negative.native-report-unknown-top-field' (-not (Test-ProductRuntimeContract $tamper).Passed) 'unknown native report top-level field rejected until its exact contract is source-locked'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportBatchMaximumItems = 1
    Add-SelfTestResult 'negative.product-native-batch-mismatch' (-not (Test-ProductRuntimeContract $tamper).Passed) 'flattened batch maximum disagrees with native report'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportBatchEventSignalDelta = 31
    Add-SelfTestResult 'negative.product-native-signal-mismatch' (-not (Test-ProductRuntimeContract $tamper).Passed) 'flattened signal delta disagrees with native report'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.SharedTransportBatchCount = 31
    Add-SelfTestResult 'negative.native-batch-single-signal-lock-mismatch' (-not (Test-ProductRuntimeContract $tamper).Passed) 'measured batch count disagrees with signal/lock deltas'
    $tamper = Copy-ContractFixture $product
    $tamper.SharedTransportAttempted = 401
    $tamper.SharedTransportDropped = 276
    $tamper.SharedTransportDomainDropTotal = 276
    Add-SelfTestResult 'negative.product-native-domain-drop-reconciliation' (-not (Test-ProductRuntimeContract $tamper).Passed) 'top drop ledger disagrees with 25 native domain rows'
    $tamper = Copy-ContractFixture $product
    $tamper.ModuleUnloaded = $false
    Add-SelfTestResult 'negative.actual-unload-tamper' (-not (Test-ProductRuntimeContract $tamper).Passed) 'module unloaded false'
    foreach ($legacyResult in @(
            @('AttachResultRecord','ATTACHED code=0 mode=PausePrimaryRemoteThreadResume'),
            @('DetachResultRecord','DETACHED code=0 moduleUnloaded=true moduleResidentInactive=false moduleSnapshotVerified=true moduleAbsent=true unloadSafe=true'),
            @('ProductionIdentityGateRecord','EVIDENCE_BLOCKED_BUILD_MISMATCH code=193'))) {
        $tamper = Copy-ContractFixture $product
        $tamper.($legacyResult[0]) = $legacyResult[1]
        Add-SelfTestResult "negative.legacy-$($legacyResult[0].ToLowerInvariant())-plaintext" (-not (Test-ProductRuntimeContract $tamper).Passed) 'legacy token-rich plaintext is never injector-result evidence'
    }
    $tamper = Copy-ContractFixture $product
    $tamper.DetachResultRecord.PSObject.Properties.Remove('strictUnloadVerified')
    Add-SelfTestResult 'negative.injector-v2-missing-field' (-not (Test-ProductRuntimeContract $tamper).Passed) 'typed detach result is missing one of the exact 25 fields'
    $tamper = Copy-ContractFixture $product
    $tamper.AttachResultRecord | Add-Member -NotePropertyName legacyDetail -NotePropertyValue 'ATTACHED'
    Add-SelfTestResult 'negative.injector-v2-unknown-field' (-not (Test-ProductRuntimeContract $tamper).Passed) 'typed attach result contains an unknown plaintext compatibility field'
    $tamper = Copy-ContractFixture $product
    $tamper.DetachResultRecord.moduleUnloaded = 'true'
    Add-SelfTestResult 'negative.injector-v2-quoted-boolean' (-not (Test-ProductRuntimeContract $tamper).Passed) 'quoted boolean cannot satisfy the typed detach contract'
    $tamper = Copy-ContractFixture $product
    $tamper.AttachResultRecord.status = 'PASS_X86_PAYLOAD_STRUCTURE'
    Add-SelfTestResult 'negative.injector-v2-structural-selftest-not-attach' (-not (Test-ProductRuntimeContract $tamper).Passed) 'payload structural self-test status is never a production attach result'
    $tamper = Copy-ContractFixture $product
    $tamper.DetachResultRecord.stopSucceeded = $false
    Add-SelfTestResult 'negative.injector-v2-detach-semantic-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'DETACHED cannot claim Stop failure'
    $tamper = Copy-ContractFixture $product
    $tamper.ProductionIdentityGateRecord.injectionAttempted = $true
    Add-SelfTestResult 'negative.injector-v2-identity-gate-semantic-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'wrong-identity gate cannot claim injection was attempted'
    $tamper = Copy-ContractFixture $product
    $tamper.ProductionIdentityGateRecord.moduleAbsent = $false
    Add-SelfTestResult 'negative.injector-v2-identity-gate-verified-no-module-drift' (-not (Test-ProductRuntimeContract $tamper).Passed) 'verified never-loaded identity gate must report module absent'
    $tamper = Copy-ContractFixture $product
    $tamper.DetachResultRecord.primaryThreadId = 4243
    Add-SelfTestResult 'negative.injector-v2-attach-detach-cross-record-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'attach and detach records must bind the same primary thread'
    $duplicateInjectorJson = '{"AttachResultRecord":{"schemaVersion":2,"status":"ATTACHED","status":"ATTACHED"}}'
    Add-SelfTestResult 'negative.injector-v2-duplicate-key' (-not (Test-StrictJsonTextContract $duplicateInjectorJson).Passed) 'duplicate injector result key is rejected before ConvertFrom-Json can collapse it'
    $tamper = Copy-ContractFixture $product
    $tamper.ModuleSnapshotVerified = $false
    Add-SelfTestResult 'negative.module-self-report-without-snapshot' (-not (Test-ProductRuntimeContract $tamper).Passed) 'FreeLibrary self-report true but module snapshot not verified'
    $tamper = Copy-ContractFixture $product
    $tamper.ExtraReferenceNegativeNotClaimedUnloaded = $false
    Add-SelfTestResult 'negative.extra-reference-false-unload-claim' (-not (Test-ProductRuntimeContract $tamper).Passed) 'extra-reference negative incorrectly claimed unload'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.RestoreFaultResidentNegativePassed = $false
    Add-SelfTestResult 'negative.restore-fault-resident-negative' (-not (Test-ProductRuntimeContract $tamper).Passed) 'native restore-fault resident negative false'
    foreach ($iatField in @(
            'IatOwnerLifecycleSelfTestPassed','IatOwnerGoneRetired','IatAddressReuseNotClobbered',
            'IatReplacementCasRestored','IatOriginalNotClobbered','IatThirdPartyNotClobbered')) {
        $tamper = Copy-ContractFixture $product
        $tamper.NativeProbeSelfTestReport.$iatField = $false
        Add-SelfTestResult "negative.native-$($iatField.ToLowerInvariant())-false" (-not (Test-ProductRuntimeContract $tamper).Passed) "$iatField false"
        $tamper = Copy-ContractFixture $product
        $tamper.NativeProbeSelfTestReport.PSObject.Properties.Remove($iatField)
        Add-SelfTestResult "negative.native-$($iatField.ToLowerInvariant())-missing" (-not (Test-ProductRuntimeContract $tamper).Passed) "$iatField removed"
    }
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.FaultIsolationResults[12].OtherDomainContinued = $false
    Add-SelfTestResult 'negative.per-domain-fault-isolation' (-not (Test-ProductRuntimeContract $tamper).Passed) 'other domain stopped during one actual injection'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.HookThreadFileIoOperations = 1
    Add-SelfTestResult 'negative.hook-thread-file-io' (-not (Test-ProductRuntimeContract $tamper).Passed) 'instrumented hook-thread file IO count 1'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.HookThreadRingNullFallbackPassed = $false
    Add-SelfTestResult 'negative.hook-thread-ring-null-fallback' (-not (Test-ProductRuntimeContract $tamper).Passed) 'ring-null hook path did not account semantic loss without file fallback'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.AsyncDiagnosticDropCount = 0
    Add-SelfTestResult 'negative.diagnostic-overflow-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) '25 per-domain overflow rows but aggregate zero'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.SharedTransportPriorityResults[2].SamplingProbeDropReason = 0
    Add-SelfTestResult 'negative.priority-direction-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'P2 sampling loss without SamplingPolicy reason is rejected'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.SharedTransportDomainResults[7].Pending = 1
    Add-SelfTestResult 'negative.per-domain-pending-tamper' (-not (Test-ProductRuntimeContract $tamper).Passed) 'native domain pending depth 1'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.GetQueuedCompletionStatusExObserved = 0
    Add-SelfTestResult 'negative.iocp-runtime-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'native GQCSEx observation removed'
    $tamper = Copy-ContractFixture $product
    $tamper.NativeProbeSelfTestReport.PendingAtStopDeferredCount = 1
    $tamper.NativeProbeSelfTestReport.PendingAtStopDeferredAttempts = 1
    Add-SelfTestResult 'negative.pending-stop-final-purge-spoof' (-not (Test-ProductRuntimeContract $tamper).Passed) 'unload-neutral final purge left a deferred receive behind'

    $tamperNetwork = Copy-ContractFixture $network
    $tamperNetwork.cases[4].TraceIntegrity = $false
    Add-SelfTestResult 'negative.network-overlapped-trace-integrity' (-not (Test-NetworkRuntimeContract $tamperNetwork).Passed) 'overlapped baseline trace integrity false'
    $tamperNetwork = Copy-ContractFixture $network
    $tamperNetwork.cases[4].PSObject.Properties.Remove('TraceFileSize')
    Add-SelfTestResult 'negative.network-required-evidence-missing' (-not (Test-NetworkRuntimeContract $tamperNetwork).Passed) 'network case trace size removed'
    $tamperNetwork = Copy-ContractFixture $network
    $tamperNetwork.runDir = 'C:\local\identity'
    Add-SelfTestResult 'negative.network-absolute-path' (-not (Test-NetworkRuntimeContract $tamperNetwork).Passed) 'network evidence contains absolute local path'

    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.rows = @($tamperUltimate.semanticEventMatrix.rows | Select-Object -First 24)
    Add-SelfTestResult 'negative.semantic-count-only-spoof' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'top counts remain 25 but array has 24'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.rows[3].corruptionRejected = $false
    Add-SelfTestResult 'negative.semantic-per-type-tamper' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'one corruption rejection false'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.rows[3] | Add-Member -NotePropertyName unsupportedVersionRejected -NotePropertyValue $true
    Add-SelfTestResult 'negative.semantic-legacy-row-field-spoof' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'writer row cannot regain legacy acceptance-only field names'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.totals.PSObject.Properties.Remove('versionRejectedCount')
    Add-SelfTestResult 'negative.semantic-matrix-total-missing' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'actual writer totals shape is incomplete'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.rows[7].index = '7'
    Add-SelfTestResult 'negative.semantic-matrix-index-string' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'matrix row index must remain a typed integer'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.nativeWireBinding.bound = $false
    Add-SelfTestResult 'negative.semantic-native-wire-unbound' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'DLL-produced native wire was not bound into the release reader run'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.rows[9].nativeWireDispatchPassed = $false
    Add-SelfTestResult 'negative.semantic-native-wire-per-type-dispatch' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'one native DLL wire event failed release dispatch'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.semanticEventMatrix.nativeWireBinding.eventTypeDigest = '0' * 64
    Add-SelfTestResult 'negative.semantic-native-wire-digest-spoof' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'native DLL wire event type digest was replaced'
    $tamperUltimate = Copy-ContractFixture $ultimate
    $tamperUltimate.tests[2].detail = 'counts not proven'
    Add-SelfTestResult 'negative.ultimate-detail-spoof' (-not (Test-UltimateRuntimeContract $tamperUltimate 0).Passed) 'required test detail not exact deterministic contract proof'

    $authoritySelfTest = Test-God2EvidenceAuthorityContract
    Add-SelfTestResult 'authority.profile-consistency-positive-and-negatives' `
        ([bool]$authoritySelfTest.Passed -and $authoritySelfTest.CheckCount -eq 4 -and
         $authoritySelfTest.PassedCount -eq 4) `
        'fixture/current, historical/current and incomplete-current authority conflicts are rejected'

    $failed = @($testResults | Where-Object { -not $_.Passed })
    foreach ($test in $testResults) {
        Write-Output ('{0} {1} - {2}' -f $(if ($test.Passed) { 'PASS' } else { 'FAIL' }), $test.Name, $test.Detail)
    }
    Write-Output "contractSelfTest=$($testResults.Count) passed=$($testResults.Count - $failed.Count) failed=$($failed.Count)"
    if ($failed.Count -ne 0) { exit 4 }
    exit 0
}
if ($ContractSelfTest) {
    Invoke-EnhancedCaptureContractSelfTest
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
    throw "Repository root does not exist: $RepositoryRoot"
}

function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}
function Get-PortablePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $rootWithSeparator = $RepositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($resolved.Equals($RepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) { return '.' }
    if (-not $resolved.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Acceptance input/output escapes repository root: $resolved"
    }
    return $resolved.Substring($rootWithSeparator.Length).Replace('\', '/')
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $OutputRoot = Join-Path $RepositoryRoot "Artifacts/UltimateIntermediateValidation/EnhancedCaptureAcceptance-$stamp"
} else {
    $OutputRoot = Resolve-RepositoryPath $OutputRoot
}
[void](Get-PortablePath $OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) {
    throw "Output root already exists; refusing to mix acceptance runs: $OutputRoot"
}
New-Item -ItemType Directory -Path $OutputRoot | Out-Null

$ReleaseExecutable = Resolve-RepositoryPath $ReleaseExecutable
$ProbePayload = Resolve-RepositoryPath $ProbePayload
$InjectorPayload = Resolve-RepositoryPath $InjectorPayload
$NetworkSelfTestSummary = Resolve-RepositoryPath $NetworkSelfTestSummary
$ProductSelfTestSummary = Resolve-RepositoryPath $ProductSelfTestSummary
if ([string]::IsNullOrWhiteSpace($EvidenceFixtureReportJson)) {
    throw '-EvidenceFixtureReportJson is required and must name a fresh final-EXE package fixture report.'
}
$EvidenceFixtureReportJson = Resolve-RepositoryPath $EvidenceFixtureReportJson
if ([string]::IsNullOrWhiteSpace($RestoreFaultLifecycleReport)) {
    throw '-RestoreFaultLifecycleReport is required and must name the fresh exact-owner restore-fault lifecycle report.'
}
$RestoreFaultLifecycleReport = Resolve-RepositoryPath $RestoreFaultLifecycleReport
if ([string]::IsNullOrWhiteSpace($OfficialRuntimeEvidence)) {
    throw '-OfficialRuntimeEvidence is required and must name the fresh exact official-client runtime summary.'
}
$OfficialRuntimeEvidence = Resolve-RepositoryPath $OfficialRuntimeEvidence
if (-not [string]::IsNullOrWhiteSpace($GuiSmokeEvidence)) {
    $GuiSmokeEvidence = Resolve-RepositoryPath $GuiSmokeEvidence
}

$requiredInputs = @($ReleaseExecutable, $ProbePayload, $InjectorPayload,
    $NetworkSelfTestSummary, $ProductSelfTestSummary, $EvidenceFixtureReportJson,
    $RestoreFaultLifecycleReport, $OfficialRuntimeEvidence)
foreach ($inputPath in $requiredInputs) {
    [void](Get-PortablePath $inputPath)
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required acceptance input does not exist: $inputPath"
    }
}
if (-not [string]::IsNullOrWhiteSpace($GuiSmokeEvidence)) {
    [void](Get-PortablePath $GuiSmokeEvidence)
}

$checks = New-Object Collections.ArrayList
function Add-Check([string]$Id, [bool]$Passed, [string]$Scope, [string]$Evidence) {
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    [void]$checks.Add([ordered]@{ id = $Id; status = $status; scope = $Scope; evidence = $Evidence })
}
function Add-Pending([string]$Id, [string]$Scope, [string]$Evidence) {
    [void]$checks.Add([ordered]@{ id = $Id; status = 'PENDING'; scope = $Scope; evidence = $Evidence })
}
function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}
function Get-ByteSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Test-SemanticV1SelfTestArtifacts(
        [string]$CanonicalWirePath, [string]$CanonicalBindingPath,
        [string]$CompatibilityInputPath) {
    $issues = [Collections.Generic.List[string]]::new()
    foreach ($contract in @(
            @('canonical-wire',$CanonicalWirePath),
            @('canonical-binding',$CanonicalBindingPath),
            @('compatibility-input',$CompatibilityInputPath))) {
        if (-not (Test-Path -LiteralPath $contract[1] -PathType Leaf)) {
            $issues.Add("semantic-v1.$($contract[0]).missing")
        }
    }
    if ($issues.Count -ne 0) { return Complete-ContractValidation $issues }

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $wireBytes = [IO.File]::ReadAllBytes($CanonicalWirePath)
    $bindingBytes = [IO.File]::ReadAllBytes($CanonicalBindingPath)
    $compatibilityBytes = [IO.File]::ReadAllBytes($CompatibilityInputPath)
    foreach ($contract in @(
            @('canonical-wire',$wireBytes),
            @('canonical-binding',$bindingBytes),
            @('compatibility-input',$compatibilityBytes))) {
        $bytes = [byte[]]$contract[1]
        Add-ContractIssue $issues (-not ($bytes.Length -ge 3 -and
            $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) `
            "semantic-v1.$($contract[0]).bom"
    }
    try {
        $wireText = $utf8.GetString($wireBytes)
        $bindingText = $utf8.GetString($bindingBytes)
        $compatibilityText = $utf8.GetString($compatibilityBytes)
    } catch {
        $issues.Add('semantic-v1.strict-utf8')
        return Complete-ContractValidation $issues
    }
    Add-ContractIssue $issues ($wireText.EndsWith("`n", [StringComparison]::Ordinal) -and
        -not $wireText.Contains("`r")) 'semantic-v1.canonical-wire.lf'
    $wireBody = if ($wireText.EndsWith("`n", [StringComparison]::Ordinal)) {
        $wireText.Substring(0, $wireText.Length - 1)
    } else { $wireText }
    $lines = @($wireBody -split "`n")
    Add-ContractIssue $issues ($lines.Count -eq 25 -and
        @($lines | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) `
        'semantic-v1.canonical-wire.line-count'
    $eventTypes = @()
    $canonicalProperties = @(
        'SchemaId','SchemaVersion','EventType','SemanticEventId','Sequence','Timestamp',
        'ThreadId','ProcessId','SessionId','ClientBuildId','ModuleId','RVA','CallsiteRVA',
        'SensitiveValue')
    for ($index = 0; $index -lt $lines.Count; ++$index) {
        $strict = Test-StrictJsonTextContract $lines[$index]
        Add-ContractIssue $issues ([bool]$strict.Passed) "semantic-v1.canonical-wire.line-$index.strict-json"
        if (-not $strict.Passed) { continue }
        try { $row = $lines[$index] | ConvertFrom-Json } catch {
            $issues.Add("semantic-v1.canonical-wire.line-$index.parse")
            continue
        }
        Add-ContractIssue $issues (Test-ExactJsonProperties $row $canonicalProperties) `
            "semantic-v1.canonical-wire.line-$index.exact-14"
        $eventType = Get-JsonPropertyValue $row 'EventType'
        $eventTypes += [string]$eventType
        $sequence = Get-JsonPropertyValue $row 'Sequence'
        $threadId = Get-JsonPropertyValue $row 'ThreadId'
        $processId = Get-JsonPropertyValue $row 'ProcessId'
        $rva = Get-JsonPropertyValue $row 'RVA'
        $callsiteRva = Get-JsonPropertyValue $row 'CallsiteRVA'
        Add-ContractIssue $issues (
            (Get-JsonPropertyValue $row 'SchemaId') -ceq 'God2SemanticEvent' -and
            (Test-JsonIntegerValue (Get-JsonPropertyValue $row 'SchemaVersion')) -and
            [int64](Get-JsonPropertyValue $row 'SchemaVersion') -eq 1 -and
            $eventType -is [string] -and
            $script:ExpectedSemanticEventTypes.EventType -ccontains $eventType -and
            (Get-JsonPropertyValue $row 'SemanticEventId') -is [string] -and
            -not [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $row 'SemanticEventId')) -and
            (Test-JsonIntegerValue $sequence) -and [int64]$sequence -eq (2000 + $index) -and
            (Get-JsonPropertyValue $row 'Timestamp') -is [string] -and
            (Test-JsonIntegerValue $threadId) -and [int64]$threadId -eq 7 -and
            (Test-JsonIntegerValue $processId) -and [int64]$processId -eq 42 -and
            (Get-JsonPropertyValue $row 'SessionId') -ceq 'V1Matrix' -and
            (Get-JsonPropertyValue $row 'ClientBuildId') -ceq $script:ExpectedClientSha256 -and
            (Get-JsonPropertyValue $row 'ModuleId') -ceq 'God2_opt.exe' -and
            (Test-JsonIntegerValue $rva) -and [int64]$rva -eq 0 -and
            (Test-JsonIntegerValue $callsiteRva) -and [int64]$callsiteRva -eq 0 -and
            (Test-JsonBooleanValue (Get-JsonPropertyValue $row 'SensitiveValue') $false)) `
            "semantic-v1.canonical-wire.line-$index.typed-profile"
    }
    Add-ContractIssue $issues (@($eventTypes | Sort-Object -Unique).Count -eq 25 -and
        @($script:ExpectedSemanticEventTypes.EventType | Where-Object {
            $eventTypes -cnotcontains $_ }).Count -eq 0) 'semantic-v1.canonical-wire.event-types'

    $bindingStrict = Test-StrictJsonTextContract $bindingText
    $binding = if ($bindingStrict.Passed) {
        try { $bindingText | ConvertFrom-Json } catch { $null }
    } else { $null }
    $bindingProperties = @(
        'SchemaId','SchemaVersion','EvidenceSource','LiveEvidenceClaimed','FixtureOnly',
        'Profile','Canonical','Promotable','CanonicalizeToV2BeforePromotion',
        'RelativePath','SHA256','EventTypeDigest','LineCount')
    Add-ContractIssue $issues ($bindingStrict.Passed -and $null -ne $binding -and
        (Test-ExactJsonProperties $binding $bindingProperties)) `
        'semantic-v1.canonical-binding.exact-properties'
    $sortedEventTypes = @($eventTypes | Sort-Object) -join "`n"
    $eventTypeDigest = Get-ByteSha256 $utf8.GetBytes($sortedEventTypes)
    Add-ContractIssue $issues ($null -ne $binding -and
        (Get-JsonPropertyValue $binding 'SchemaId') -ceq
            'God2SemanticEventProfileArtifactBinding' -and
        (Test-JsonIntegerValue (Get-JsonPropertyValue $binding 'SchemaVersion')) -and
        [int64](Get-JsonPropertyValue $binding 'SchemaVersion') -eq 1 -and
        (Get-JsonPropertyValue $binding 'EvidenceSource') -ceq
            'DeterministicSyntheticContractFixture' -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $binding 'LiveEvidenceClaimed') $false) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $binding 'FixtureOnly') $true) -and
        (Get-JsonPropertyValue $binding 'Profile') -ceq 'CanonicalV1WriterProfile' -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $binding 'Canonical') $true) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $binding 'Promotable') $false) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $binding 'CanonicalizeToV2BeforePromotion') $true) -and
        (Get-JsonPropertyValue $binding 'RelativePath') -ceq
            'canonical-v1-semantic-wire.jsonl' -and
        (Get-JsonPropertyValue $binding 'SHA256') -ceq (Get-Sha256 $CanonicalWirePath) -and
        (Get-JsonPropertyValue $binding 'EventTypeDigest') -ceq $eventTypeDigest -and
        $eventTypeDigest -ceq
            'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C' -and
        (Test-JsonIntegerValue (Get-JsonPropertyValue $binding 'LineCount')) -and
        [int64](Get-JsonPropertyValue $binding 'LineCount') -eq 25) `
        'semantic-v1.canonical-binding.deep-read'

    $compatibilityStrict = Test-StrictJsonTextContract $compatibilityText
    $compatibility = if ($compatibilityStrict.Passed) {
        try { $compatibilityText | ConvertFrom-Json } catch { $null }
    } else { $null }
    $compatibilityProperties = @(
        'SchemaVersion','EventType','EventId','FixtureOnly','LiveEvidenceClaimed','Profile',
        'Canonical','Promotable','WrongCausalFixture','LegacyProducerExtension')
    Add-ContractIssue $issues ($compatibilityStrict.Passed -and $null -ne $compatibility -and
        (Test-ExactJsonProperties $compatibility $compatibilityProperties) -and
        (Test-JsonIntegerValue (Get-JsonPropertyValue $compatibility 'SchemaVersion')) -and
        [int64](Get-JsonPropertyValue $compatibility 'SchemaVersion') -eq 1 -and
        (Get-JsonPropertyValue $compatibility 'EventType') -ceq 'ObjectResolution' -and
        (Get-JsonPropertyValue $compatibility 'EventId') -is [string] -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $compatibility 'FixtureOnly') $true) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $compatibility 'LiveEvidenceClaimed') $false) -and
        (Get-JsonPropertyValue $compatibility 'Profile') -ceq
            'NonCanonicalV1CompatibilityInput' -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $compatibility 'Canonical') $false) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $compatibility 'Promotable') $false) -and
        (Test-JsonBooleanValue (Get-JsonPropertyValue $compatibility 'WrongCausalFixture') $true)) `
        'semantic-v1.compatibility-input.deep-read'
    return Complete-ContractValidation $issues
}
function Get-FileIdentity([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        path = Get-PortablePath $item.FullName
        length = [long]$item.Length
        sha256 = Get-Sha256 $item.FullName
        lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
    }
}
function Read-BoundJsonArtifact(
        [string]$Label, [object]$DeclaredPath, [object]$DeclaredSha256,
        [object]$EmbeddedValue, [bool]$RequireEmbeddedValue,
        [scriptblock]$ContractValidator) {
    $issues = [Collections.Generic.List[string]]::new()
    $resolvedPath = $null
    $item = $null
    $text = $null
    $value = $null
    $actualSha256 = $null
    if ($DeclaredPath -isnot [string] -or [string]::IsNullOrWhiteSpace($DeclaredPath)) {
        $issues.Add("$Label.path.missing")
    } else {
        try {
            $resolvedPath = Resolve-RepositoryPath $DeclaredPath
            $portablePath = Get-PortablePath $resolvedPath
            if ($portablePath -cne $DeclaredPath) {
                $issues.Add("$Label.path.noncanonical:$portablePath")
            }
            if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
                $issues.Add("$Label.path.not-found")
            } else {
                $item = Get-Item -LiteralPath $resolvedPath
                if ($item.Length -le 0 -or $item.Length -gt 32MB) {
                    $issues.Add("$Label.length.out-of-range:$($item.Length)")
                }
                $bytes = [IO.File]::ReadAllBytes($resolvedPath)
                if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
                    $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                    $issues.Add("$Label.utf8-bom")
                }
                $actualSha256 = Get-Sha256 $resolvedPath
                if ($DeclaredSha256 -isnot [string] -or
                    $DeclaredSha256 -cnotmatch '^[0-9A-F]{64}$' -or
                    $actualSha256 -cne $DeclaredSha256) {
                    $issues.Add("$Label.sha256.mismatch:$actualSha256")
                }
                try {
                    $strictReader = [Text.UTF8Encoding]::new($false, $true)
                    $text = [IO.File]::ReadAllText($resolvedPath, $strictReader)
                } catch {
                    $issues.Add("$Label.utf8.invalid:$($_.Exception.Message)")
                }
                if ($null -ne $text) {
                    foreach ($portableIssue in @(Get-PortableTextContractIssues $text)) {
                        $issues.Add("$Label.text.$portableIssue")
                    }
                    $jsonTextContract = Test-StrictJsonTextContract $text
                    foreach ($jsonIssue in @($jsonTextContract.Issues)) {
                        $issues.Add("$Label.json.$jsonIssue")
                    }
                    try { $value = $text | ConvertFrom-Json }
                    catch { $issues.Add("$Label.json.invalid:$($_.Exception.Message)") }
                }
            }
        } catch {
            $issues.Add("$Label.path.invalid:$($_.Exception.Message)")
        }
    }
    if ($RequireEmbeddedValue) {
        if ($null -eq $value -or $null -eq $EmbeddedValue -or
            -not (Test-JsonDeepEqual $value $EmbeddedValue)) {
            $issues.Add("$Label.embedded.deep-equal")
        }
    }
    if ($null -ne $ContractValidator -and $null -ne $value) {
        try {
            $contract = & $ContractValidator $value
            if ($null -eq $contract -or -not [bool]$contract.Passed) {
                foreach ($issue in @($contract.Issues)) {
                    $issues.Add("$Label.contract.$issue")
                }
                if ($null -eq $contract) { $issues.Add("$Label.contract.missing-result") }
            }
        } catch {
            $issues.Add("$Label.contract.exception:$($_.Exception.Message)")
        }
    }
    $validation = Complete-ContractValidation $issues
    return [pscustomobject]@{
        Passed = [bool]$validation.Passed
        Issues = @($validation.Issues)
        Path = $resolvedPath
        Item = $item
        Text = $text
        Value = $value
        Sha256 = $actualSha256
    }
}
function Read-NativeSemanticWireArtifact(
        [object]$DeclaredPath, [object]$DeclaredSha256,
        [object]$ExpectedProcessId, [object]$ExpectedDigest) {
    $issues = [Collections.Generic.List[string]]::new()
    $resolvedPath = $null
    $item = $null
    $text = $null
    $actualSha256 = $null
    $rows = [Collections.Generic.List[object]]::new()
    if ($DeclaredPath -isnot [string] -or [string]::IsNullOrWhiteSpace($DeclaredPath)) {
        $issues.Add('native-wire.path.missing')
    } else {
        try {
            $resolvedPath = Resolve-RepositoryPath $DeclaredPath
            $portablePath = Get-PortablePath $resolvedPath
            if ($portablePath -cne $DeclaredPath) {
                $issues.Add("native-wire.path.noncanonical:$portablePath")
            }
            if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
                $issues.Add('native-wire.path.not-found')
            } else {
                $item = Get-Item -LiteralPath $resolvedPath
                $bytes = [IO.File]::ReadAllBytes($resolvedPath)
                if ($bytes.Length -le 0 -or $bytes.Length -gt 2MB) {
                    $issues.Add("native-wire.length.out-of-range:$($bytes.Length)")
                }
                if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
                    $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                    $issues.Add('native-wire.utf8-bom')
                }
                $actualSha256 = Get-Sha256 $resolvedPath
                if ($DeclaredSha256 -isnot [string] -or
                    $DeclaredSha256 -cnotmatch '^[0-9A-F]{64}$' -or
                    $actualSha256 -cne $DeclaredSha256) {
                    $issues.Add("native-wire.sha256.mismatch:$actualSha256")
                }
                try {
                    $strictReader = [Text.UTF8Encoding]::new($false, $true)
                    $text = [IO.File]::ReadAllText($resolvedPath, $strictReader)
                } catch {
                    $issues.Add("native-wire.utf8.invalid:$($_.Exception.Message)")
                }
            }
        } catch {
            $issues.Add("native-wire.path.invalid:$($_.Exception.Message)")
        }
    }
    if ($null -ne $text) {
        foreach ($portableIssue in @(Get-PortableTextContractIssues $text)) {
            $issues.Add("native-wire.text.$portableIssue")
        }
        if ($text.IndexOf("`r", [StringComparison]::Ordinal) -ge 0) {
            $issues.Add('native-wire.cr-not-allowed')
        }
        if (-not $text.EndsWith("`n", [StringComparison]::Ordinal) -or
            $text.EndsWith("`n`n", [StringComparison]::Ordinal)) {
            $issues.Add('native-wire.exact-single-final-lf')
        }
        $splitLines = @($text.Split([char]10))
        $lineCount = if ($text.EndsWith("`n", [StringComparison]::Ordinal)) {
            $splitLines.Count - 1
        } else { $splitLines.Count }
        $lines = if ($lineCount -gt 0) { @($splitLines[0..($lineCount - 1)]) } else { @() }
        if ($lines.Count -ne 25 -or @($lines | Where-Object { $_.Length -eq 0 }).Count -ne 0) {
            $issues.Add("native-wire.line-count:$($lines.Count)")
        }
        [uint64]$previousSequence = 0
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = [string]$lines[$index]
            $lineContract = Test-StrictJsonTextContract $line
            foreach ($issue in @($lineContract.Issues)) {
                $issues.Add("native-wire.rows.$index.json.$issue")
            }
            $row = $null
            try { $row = $line | ConvertFrom-Json }
            catch { $issues.Add("native-wire.rows.$index.json.invalid:$($_.Exception.Message)") }
            if ($null -eq $row) { continue }
            $rows.Add($row)
            Add-ContractIssue $issues (Test-ExactJsonProperties $row @(
                    'SchemaId','SchemaVersion','EventType','EventId','Sequence','Timestamp',
                    'ThreadId','ProcessId','SessionId','ClientBuildId','ModuleId','RVA',
                    'CallsiteRVA','ParentEventId','ContextId','ActionId','ObjectToken',
                    'ValueToken','SourceToken','AuthorityHint','SensitiveMaskStatus','Payload')) "native-wire.rows.$index.exact-properties"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'SchemaId') -ceq
                'God2SemanticEvent') "native-wire.rows.$index.SchemaId"
            $schemaVersion = Get-JsonPropertyValue $row 'SchemaVersion'
            Add-ContractIssue $issues ((Test-JsonIntegerValue $schemaVersion) -and
                [int64]$schemaVersion -eq 2) "native-wire.rows.$index.SchemaVersion"
            if ($index -lt $script:ExpectedSemanticEventTypes.Count) {
                Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'EventType') -ceq
                    $script:ExpectedSemanticEventTypes[$index].EventType) "native-wire.rows.$index.EventType"
            }
            foreach ($name in @('RVA','CallsiteRVA','ParentEventId','ContextId','ActionId',
                    'ObjectToken','ValueToken')) {
                Add-ContractIssue $issues ($null -eq (Get-JsonPropertyValue $row $name)) "native-wire.rows.$index.$name"
            }
            $processId = Get-JsonPropertyValue $row 'ProcessId'
            Add-ContractIssue $issues ((Test-JsonIntegerValue $processId) -and
                (Test-JsonIntegerValue $ExpectedProcessId) -and
                [uint64]$processId -eq [uint64]$ExpectedProcessId) "native-wire.rows.$index.ProcessId"
            $sequence = Get-JsonPropertyValue $row 'Sequence'
            Add-ContractIssue $issues ((Test-JsonIntegerValue $sequence) -and
                [uint64]$sequence -gt $previousSequence) "native-wire.rows.$index.Sequence"
            if (Test-JsonIntegerValue $sequence) { $previousSequence = [uint64]$sequence }
            foreach ($name in @('Timestamp','ThreadId')) {
                $value = Get-JsonPropertyValue $row $name
                Add-ContractIssue $issues ((Test-JsonIntegerValue $value) -and
                    [int64]$value -gt 0) "native-wire.rows.$index.$name"
            }
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'EventId') -is [string] -and
                (Get-JsonPropertyValue $row 'EventId') -cmatch '^GE-[0-9]+-[0-9]+-[0-9]+$') "native-wire.rows.$index.EventId"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'SessionId') -is [string] -and
                -not [string]::IsNullOrWhiteSpace((Get-JsonPropertyValue $row 'SessionId'))) "native-wire.rows.$index.SessionId"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'ClientBuildId') -is [string] -and
                (Get-JsonPropertyValue $row 'ClientBuildId') -cmatch '^[0-9A-F]{64}$') "native-wire.rows.$index.ClientBuildId"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'ModuleId') -ceq
                'God2_opt.exe') "native-wire.rows.$index.ModuleId"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'SourceToken') -ceq
                'NativeSemanticTypeFixture') "native-wire.rows.$index.SourceToken"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'AuthorityHint') -ceq
                'UNKNOWN') "native-wire.rows.$index.AuthorityHint"
            Add-ContractIssue $issues ((Get-JsonPropertyValue $row 'SensitiveMaskStatus') -ceq
                'NotSensitive') "native-wire.rows.$index.SensitiveMaskStatus"
            $payload = Get-JsonPropertyValue $row 'Payload'
            Add-ContractIssue $issues (Test-ExactJsonProperties $payload @(
                    'FixtureOnly','RuntimeObservation','DefinitionIndex','ExpectedEventType')) "native-wire.rows.$index.Payload.exact-properties"
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $payload 'FixtureOnly') $true) "native-wire.rows.$index.Payload.FixtureOnly"
            Add-ContractIssue $issues (Test-JsonBooleanValue (Get-JsonPropertyValue $payload 'RuntimeObservation') $false) "native-wire.rows.$index.Payload.RuntimeObservation"
            $definitionIndex = Get-JsonPropertyValue $payload 'DefinitionIndex'
            Add-ContractIssue $issues ((Test-JsonIntegerValue $definitionIndex) -and
                [int64]$definitionIndex -eq $index) "native-wire.rows.$index.Payload.DefinitionIndex"
            if ($index -lt $script:ExpectedSemanticEventTypes.Count) {
                Add-ContractIssue $issues ((Get-JsonPropertyValue $payload 'ExpectedEventType') -ceq
                    $script:ExpectedSemanticEventTypes[$index].EventType) "native-wire.rows.$index.Payload.ExpectedEventType"
            }
        }
        if ($rows.Count -eq 25) {
            $sortedNames = @($rows | ForEach-Object { [string](Get-JsonPropertyValue $_ 'EventType') } | Sort-Object)
            $digest = Get-ByteSha256 ([Text.Encoding]::UTF8.GetBytes(($sortedNames -join "`n")))
            if ($ExpectedDigest -isnot [string] -or $digest -cne $ExpectedDigest -or
                $digest -cne 'A354EB5BB9E7632C8712DA37E6DADDFEC1ED8ED0984C38E2FCBB32C100C6958C') {
                $issues.Add("native-wire.event-type-digest:$digest")
            }
        }
    }
    $validation = Complete-ContractValidation $issues
    return [pscustomobject]@{
        Passed = [bool]$validation.Passed
        Issues = @($validation.Issues)
        Path = $resolvedPath
        Item = $item
        Text = $text
        Rows = @($rows)
        Sha256 = $actualSha256
    }
}
function Read-U16([byte[]]$Bytes, [int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) { throw "PE UInt16 read out of range: $Offset" }
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}
function Read-U32([byte[]]$Bytes, [int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) { throw "PE UInt32 read out of range: $Offset" }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}
function Convert-RvaToOffset([byte[]]$Bytes, [uint32]$Rva, [int]$PeOffset) {
    $sectionCount = Read-U16 $Bytes ($PeOffset + 6)
    $optionalSize = Read-U16 $Bytes ($PeOffset + 20)
    $sectionOffset = $PeOffset + 24 + $optionalSize
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $entry = $sectionOffset + ($index * 40)
        $virtualSize = Read-U32 $Bytes ($entry + 8)
        $virtualAddress = Read-U32 $Bytes ($entry + 12)
        $rawSize = Read-U32 $Bytes ($entry + 16)
        $rawAddress = Read-U32 $Bytes ($entry + 20)
        $span = [Math]::Max([uint64]$virtualSize, [uint64]$rawSize)
        if ([uint64]$Rva -ge [uint64]$virtualAddress -and
            [uint64]$Rva -lt ([uint64]$virtualAddress + $span)) {
            $offset = [uint64]$rawAddress + ([uint64]$Rva - [uint64]$virtualAddress)
            if ($offset -ge [uint64]$Bytes.Length) { throw 'PE RVA maps beyond file' }
            return [int]$offset
        }
    }
    throw ('PE RVA 0x{0:X8} did not map to a section' -f $Rva)
}
function Read-AsciiZ([byte[]]$Bytes, [int]$Offset) {
    if ($Offset -lt 0 -or $Offset -ge $Bytes.Length) { throw 'PE string offset out of range' }
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    if ($end -eq $Bytes.Length -or $end - $Offset -gt 1024) { throw 'Invalid PE export string' }
    return [Text.Encoding]::ASCII.GetString($Bytes, $Offset, $end - $Offset)
}
function Get-PeIdentity([byte[]]$Bytes) {
    if ($Bytes.Length -lt 256 -or $Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) {
        throw 'Missing DOS MZ signature'
    }
    $peOffset = [int](Read-U32 $Bytes 0x3C)
    if ($peOffset -lt 0x40 -or $peOffset + 24 -gt $Bytes.Length -or
        $Bytes[$peOffset] -ne 0x50 -or $Bytes[$peOffset + 1] -ne 0x45 -or
        $Bytes[$peOffset + 2] -ne 0 -or $Bytes[$peOffset + 3] -ne 0) {
        throw 'Missing PE signature'
    }
    $machineValue = Read-U16 $Bytes ($peOffset + 4)
    $characteristics = Read-U16 $Bytes ($peOffset + 22)
    $optionalOffset = $peOffset + 24
    $optionalMagic = Read-U16 $Bytes $optionalOffset
    $exportRva = Read-U32 $Bytes ($optionalOffset + 96)
    $exports = New-Object Collections.Generic.List[string]
    if ($exportRva -ne 0) {
        $exportOffset = Convert-RvaToOffset $Bytes $exportRva $peOffset
        $nameCount = Read-U32 $Bytes ($exportOffset + 24)
        $namesRva = Read-U32 $Bytes ($exportOffset + 32)
        if ($nameCount -gt 4096) { throw 'Unreasonable PE export-name count' }
        if ($nameCount -gt 0) {
            $namesOffset = Convert-RvaToOffset $Bytes $namesRva $peOffset
            for ($index = 0; $index -lt $nameCount; $index++) {
                $nameRva = Read-U32 $Bytes ($namesOffset + (4 * $index))
                $nameOffset = Convert-RvaToOffset $Bytes $nameRva $peOffset
                $exports.Add((Read-AsciiZ $Bytes $nameOffset))
            }
        }
    }
    return [ordered]@{
        machineValue = [int]$machineValue
        machine = if ($machineValue -eq 0x14C) { 'I386' } else { ('0x{0:X4}' -f $machineValue) }
        optionalMagic = [int]$optionalMagic
        optionalHeader = if ($optionalMagic -eq 0x10B) { 'PE32' } else { ('0x{0:X4}' -f $optionalMagic) }
        isDll = (($characteristics -band 0x2000) -ne 0)
        exports = @($exports | Sort-Object -Unique)
    }
}

if (-not ('God2.EnhancedCapture.ResourceReader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace God2.EnhancedCapture {
    public sealed class RcDataItem {
        public string Type { get; set; }
        public string Name { get; set; }
        public ushort Language { get; set; }
        public byte[] Bytes { get; set; }
    }

    public static class ResourceReader {
        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
        private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindResourceExW(IntPtr module, IntPtr type,
            IntPtr name, ushort language);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resourceData);
        private delegate bool EnumResourceTypeProc(IntPtr module, IntPtr type,
            IntPtr parameter);
        private delegate bool EnumResourceNameProc(IntPtr module, IntPtr type,
            IntPtr name, IntPtr parameter);
        private delegate bool EnumResourceLanguageProc(IntPtr module, IntPtr type,
            IntPtr name, ushort language, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumResourceTypesW(IntPtr module,
            EnumResourceTypeProc callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumResourceNamesW(IntPtr module, IntPtr type,
            EnumResourceNameProc callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumResourceLanguagesW(IntPtr module, IntPtr type,
            IntPtr name, EnumResourceLanguageProc callback, IntPtr parameter);

        private static string ResourceName(IntPtr name) {
            ulong value = unchecked((ulong)name.ToInt64());
            return (value >> 16) == 0 ? "#" + (value & 0xFFFF).ToString() :
                Marshal.PtrToStringUni(name);
        }

        public static RcDataItem[] ReadAllResourceLeaves(string path) {
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero,
                LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                var items = new List<RcDataItem>();
                int failure = 0;
                EnumResourceLanguageProc languageCallback = delegate(IntPtr owner,
                        IntPtr type, IntPtr name, ushort language, IntPtr parameter) {
                    IntPtr resource = FindResourceExW(owner, type, name, language);
                    if (resource == IntPtr.Zero) {
                        failure = Marshal.GetLastWin32Error();
                        return false;
                    }
                    uint size = SizeofResource(owner, resource);
                    IntPtr loaded = LoadResource(owner, resource);
                    IntPtr locked = loaded != IntPtr.Zero ? LockResource(loaded) : IntPtr.Zero;
                    if (size == 0 || loaded == IntPtr.Zero || locked == IntPtr.Zero) {
                        failure = Marshal.GetLastWin32Error();
                        if (failure == 0) failure = 13;
                        return false;
                    }
                    byte[] bytes = new byte[size];
                    Marshal.Copy(locked, bytes, 0, checked((int)size));
                    items.Add(new RcDataItem {
                        Type = ResourceName(type), Name = ResourceName(name),
                        Language = language, Bytes = bytes
                    });
                    return true;
                };
                EnumResourceNameProc nameCallback = delegate(IntPtr owner, IntPtr type,
                        IntPtr name, IntPtr parameter) {
                    if (!EnumResourceLanguagesW(owner, type, name, languageCallback,
                            IntPtr.Zero)) {
                        if (failure == 0) failure = Marshal.GetLastWin32Error();
                        return false;
                    }
                    return true;
                };
                EnumResourceTypeProc typeCallback = delegate(IntPtr owner, IntPtr type,
                        IntPtr parameter) {
                    if (!EnumResourceNamesW(owner, type, nameCallback, IntPtr.Zero)) {
                        if (failure == 0) failure = Marshal.GetLastWin32Error();
                        return false;
                    }
                    return true;
                };
                if (!EnumResourceTypesW(module, typeCallback, IntPtr.Zero)) {
                    if (failure == 0) failure = Marshal.GetLastWin32Error();
                    throw new Win32Exception(failure);
                }
                items.Sort((left, right) => {
                    int byType = StringComparer.Ordinal.Compare(left.Type, right.Type);
                    if (byType != 0) return byType;
                    int byName = StringComparer.Ordinal.Compare(left.Name, right.Name);
                    return byName != 0 ? byName : left.Language.CompareTo(right.Language);
                });
                return items.ToArray();
            } finally {
                FreeLibrary(module);
            }
        }

        public static byte[] ReadRcData(string path, int resourceId) {
            IntPtr module = LoadLibraryExW(path, IntPtr.Zero,
                LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr resource = FindResourceW(module, new IntPtr(resourceId), new IntPtr(10));
                if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                uint size = SizeofResource(module, resource);
                if (size == 0) throw new InvalidOperationException("Embedded RCDATA is empty");
                IntPtr loaded = LoadResource(module, resource);
                if (loaded == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr locked = LockResource(loaded);
                if (locked == IntPtr.Zero) throw new InvalidOperationException("LockResource failed");
                byte[] bytes = new byte[size];
                Marshal.Copy(locked, bytes, 0, checked((int)size));
                return bytes;
            } finally {
                FreeLibrary(module);
            }
        }
    }
}
'@
}

$resourceHeaderPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/Resource.h'
$resourceScriptPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/EmbeddedResources.rc'
$payloadSyncScriptPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/scripts/Sync-EmbeddedInstrumentationPayloads.ps1'
$packetCaptureProjectPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/God2.PacketCapture.vcxproj'
$packetCaptureCmakePath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/CMakeLists.txt'
$acceptanceSchemaPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/validation/enhanced-capture-acceptance.schema.json'
$enhancedStatusSchemaPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/ultimate/assets/schemas/enhanced-capture-status.schema.json'
$bridgeAwareStatusV3SchemaPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/ultimate/assets/schemas/enhanced-capture-status-v3.schema.json'
$sharedHealthAvailabilitySchemaPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/ultimate/assets/schemas/semantic-shared-ring-health-availability.schema.json'
$candidateMapAvailabilitySchemaPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/ultimate/assets/schemas/deep-probe-candidate-map-availability.schema.json'
$ultimateHeaderPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/UltimateRecovery.h'
$ultimateSourcePath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/UltimateRecovery.cpp'
$ringHeaderPath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/SharedSemanticRing.h'
$guiSourcePath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/Gui.cpp'
$mainSourcePath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/Main.cpp'
$evidenceSourcePath = Join-Path $RepositoryRoot 'tools/God2.PacketCapture/EvidencePackage.cpp'
$probeSourcePath = Join-Path $RepositoryRoot 'tools/God2.ClientInstrumentation/src/God2ClientTraceProbe.cpp'
$injectorSourcePath = Join-Path $RepositoryRoot 'tools/God2.ClientInstrumentation/src/God2PacketCaptureInjector.cpp'
$selfTestClientPath = Join-Path $RepositoryRoot 'tools/God2.ClientInstrumentation/src/God2TraceSelfTestClient.cpp'
$networkSelfTestScriptPath = Join-Path $RepositoryRoot 'tools/God2.ClientInstrumentation/scripts/Invoke-InstrumentationSelfTest.ps1'
$productSelfTestScriptPath = Join-Path $RepositoryRoot 'tools/God2.ClientInstrumentation/scripts/Invoke-ProductInjectorSelfTest.ps1'
$acceptanceScriptPath = [IO.Path]::GetFullPath($PSCommandPath)
$builtProbePath = Join-Path $RepositoryRoot 'Artifacts/ClientInstrumentation/LoginTrial/build/Release/God2ClientTraceProbe.dll'
$builtInjectorPath = Join-Path $RepositoryRoot 'Artifacts/ClientInstrumentation/LoginTrial/build/Release/God2PacketCaptureInjector.exe'

$requiredSourcePaths = @(
    $resourceHeaderPath, $resourceScriptPath, $payloadSyncScriptPath,
    $packetCaptureProjectPath, $packetCaptureCmakePath,
    $acceptanceSchemaPath, $enhancedStatusSchemaPath, $bridgeAwareStatusV3SchemaPath,
    $sharedHealthAvailabilitySchemaPath, $candidateMapAvailabilitySchemaPath,
    $acceptanceScriptPath,
    $guiSourcePath, $mainSourcePath, $evidenceSourcePath, $ultimateHeaderPath,
    $ultimateSourcePath, $ringHeaderPath, $probeSourcePath, $injectorSourcePath,
    $selfTestClientPath, $networkSelfTestScriptPath, $productSelfTestScriptPath
)
foreach ($sourcePath in $requiredSourcePaths) {
    [void](Get-PortablePath $sourcePath)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required acceptance source input does not exist: $sourcePath"
    }
}

$resourceHeader = [IO.File]::ReadAllText($resourceHeaderPath)
$resourceScript = [IO.File]::ReadAllText($resourceScriptPath)
$payloadSyncScript = [IO.File]::ReadAllText($payloadSyncScriptPath)
$packetCaptureProject = [IO.File]::ReadAllText($packetCaptureProjectPath)
$packetCaptureCmake = [IO.File]::ReadAllText($packetCaptureCmakePath)
$ultimateHeader = [IO.File]::ReadAllText($ultimateHeaderPath)
$ultimateSource = [IO.File]::ReadAllText($ultimateSourcePath)
$ringHeader = [IO.File]::ReadAllText($ringHeaderPath)
$probeSource = [IO.File]::ReadAllText($probeSourcePath)
$injectorSource = [IO.File]::ReadAllText($injectorSourcePath)
$selfTestClient = [IO.File]::ReadAllText($selfTestClientPath)
$acceptanceSchema = Get-Content -LiteralPath $acceptanceSchemaPath -Raw | ConvertFrom-Json
$acceptanceSchemaContract = Test-AcceptanceSchemaContract $acceptanceSchema
$enhancedStatusSchema = Get-Content -LiteralPath $enhancedStatusSchemaPath -Raw | ConvertFrom-Json
$enhancedStatusSchemaContract = Test-EnhancedCaptureStatusSchemaContract $enhancedStatusSchema
$bridgeAwareStatusV3Schema = Get-Content -LiteralPath $bridgeAwareStatusV3SchemaPath -Raw | ConvertFrom-Json
$bridgeAwareStatusV3SchemaContract = Test-BridgeAwareStatusV3SchemaContract $bridgeAwareStatusV3Schema
$guiSourceLockSha = Get-Sha256 $guiSourcePath
$mainSourceLockSha = Get-Sha256 $mainSourcePath
$enhancedStatusSchemaLockSha = Get-Sha256 $enhancedStatusSchemaPath
$bridgeAwareStatusV3SchemaLockSha = Get-Sha256 $bridgeAwareStatusV3SchemaPath
$evidenceSourceLockSha = Get-Sha256 $evidenceSourcePath
$sharedHealthAvailabilitySchemaLockSha = Get-Sha256 $sharedHealthAvailabilitySchemaPath
$candidateMapAvailabilitySchemaLockSha = Get-Sha256 $candidateMapAvailabilitySchemaPath

Add-Check 'output.schema-registry-id' (
    $acceptanceSchema.'$id' -ceq 'https://god2.local/schemas/enhanced-capture-acceptance.schema.json' -and
    [IO.Path]::GetFileName($acceptanceSchemaPath) -ceq 'enhanced-capture-acceptance.schema.json'
) 'RELEASE_EXE' "Schema registry ID=$($acceptanceSchema.'$id'); filename=$([IO.Path]::GetFileName($acceptanceSchemaPath))."
Add-Check 'output.schema-v2-strict-contract' $acceptanceSchemaContract.Passed 'RELEASE_EXE' "Acceptance schema v2 closed-object/runtime/hash/freshness contract issues=$($acceptanceSchemaContract.Issues -join ',')."
Add-Check 'output.enhanced-status-schema-v2-strict-contract' (
    $enhancedStatusSchemaContract.Passed -and
    [IO.Path]::GetFileName($enhancedStatusSchemaPath) -ceq 'enhanced-capture-status.schema.json'
) 'RELEASE_EXE' "Enhanced status schema ID=$($enhancedStatusSchema.'$id'); filename=$([IO.Path]::GetFileName($enhancedStatusSchemaPath)); issues=$($enhancedStatusSchemaContract.Issues -join ',')."
Add-Check 'source.gui-status-evidence-availability-immutable-lock' (
    $guiSourceLockSha -ceq $script:ExpectedGuiSourceSha256 -and
    $mainSourceLockSha -ceq $script:ExpectedMainSourceSha256 -and
    $enhancedStatusSchemaLockSha -ceq $script:ExpectedEnhancedCaptureStatusSchemaSha256 -and
    $evidenceSourceLockSha -ceq $script:ExpectedEvidenceSourceSha256 -and
    $sharedHealthAvailabilitySchemaLockSha -ceq $script:ExpectedSharedHealthAvailabilitySchemaSha256 -and
    $candidateMapAvailabilitySchemaLockSha -ceq $script:ExpectedCandidateMapAvailabilitySchemaSha256
) 'RELEASE_EXE' "Gui.cpp=$guiSourceLockSha; Main.cpp=$mainSourceLockSha; statusSchema=$enhancedStatusSchemaLockSha; EvidencePackage.cpp=$evidenceSourceLockSha; sharedAvailability=$sharedHealthAvailabilitySchemaLockSha; candidateAvailability=$candidateMapAvailabilitySchemaLockSha."

Add-Check 'resource.contract.ids-and-paths' (
    $resourceHeader -match 'IDR_X86_PACKET_PROBE\s+201' -and
    $resourceHeader -match 'IDR_X86_PACKET_INJECTOR\s+202' -and
    $resourceScript -match 'IDR_X86_PACKET_PROBE\s+RCDATA\s+"payload\\\\x86\\\\God2PacketCaptureProbe\.dll"' -and
    $resourceScript -match 'IDR_X86_PACKET_INJECTOR\s+RCDATA\s+"payload\\\\x86\\\\God2PacketCaptureInjector\.exe"'
) 'RELEASE_EXE' 'Resource.h and EmbeddedResources.rc bind exactly RCDATA 201/202 to the single x86 probe/injector payloads.'
Add-Check 'resource.prebuild-atomic-sync-contract' (
    $packetCaptureProject -match
        'BeforeTargets="ResourceCompile"' -and
    $packetCaptureProject -match
        'Sync-EmbeddedInstrumentationPayloads\.ps1' -and
    $packetCaptureCmake -match
        'add_dependencies\(God2SemanticRecoveryEngine\s+SyncEmbeddedInstrumentationPayloads\)' -and
    $payloadSyncScript -match
        'God2ClientTraceProbe\.dll' -and
    $payloadSyncScript -match
        'God2PacketCaptureInjector\.exe' -and
    $payloadSyncScript -match
        '\[IO\.File\]::Replace' -and
    $payloadSyncScript -match
        'sourceHash\s+-cne\s+\$destinationHash' -and
    $payloadSyncScript -match
        '0x014C' -and $payloadSyncScript -match '0x010B'
) 'RELEASE_EXE' 'Both Visual Studio and CMake builds run the atomic PE32/I386 payload sync before resource compilation, then require byte-exact SHA-256 equality.'

$probeBytes = [God2.EnhancedCapture.ResourceReader]::ReadRcData($ReleaseExecutable, 201)
$injectorBytes = [God2.EnhancedCapture.ResourceReader]::ReadRcData($ReleaseExecutable, 202)
$probeEmbeddedHash = Get-ByteSha256 $probeBytes
$injectorEmbeddedHash = Get-ByteSha256 $injectorBytes
$probeStandalone = Get-FileIdentity $ProbePayload
$injectorStandalone = Get-FileIdentity $InjectorPayload
$releaseIdentity = Get-FileIdentity $ReleaseExecutable
$probePe = Get-PeIdentity $probeBytes
$injectorPe = Get-PeIdentity $injectorBytes
$probeStandalonePe = Get-PeIdentity ([IO.File]::ReadAllBytes($ProbePayload))
$injectorStandalonePe = Get-PeIdentity ([IO.File]::ReadAllBytes($InjectorPayload))
$rcDataInventory = @([God2.EnhancedCapture.ResourceReader]::ReadAllResourceLeaves(
    $ReleaseExecutable))
$rcDataPeRows = [Collections.Generic.List[object]]::new()
$rcDataInvalidMzCount = 0
foreach ($item in $rcDataInventory) {
    if ($item.Bytes.Length -ge 2 -and $item.Bytes[0] -eq 0x4D -and
        $item.Bytes[1] -eq 0x5A) {
        try {
            $identity = Get-PeIdentity $item.Bytes
            $rcDataPeRows.Add([pscustomobject]@{
                Type = [string]$item.Type
                Name = [string]$item.Name
                Language = [uint16]$item.Language
                MachineValue = [int]$identity.machineValue
                OptionalMagic = [int]$identity.optionalMagic
                IsDll = [bool]$identity.isDll
                SHA256 = Get-ByteSha256 $item.Bytes
            })
        } catch {
            $rcDataInvalidMzCount++
        }
    }
}
$rcDataDllRows = @($rcDataPeRows | Where-Object IsDll)
$probeResourceLeaves = @($rcDataInventory | Where-Object {
    [string]$_.Type -ceq '#10' -and [string]$_.Name -ceq '#201'
})
$injectorResourceLeaves = @($rcDataInventory | Where-Object {
    [string]$_.Type -ceq '#10' -and [string]$_.Name -ceq '#202'
})
$requiredExports = @(
    'God2TraceProbeWaitReady', 'God2TraceProbeStop', 'God2TraceProbeCanUnload',
    'God2TraceProbeSemanticSelfTest', 'God2TraceProbeSharedTransportSelfTest'
)
$missingExports = @($requiredExports | Where-Object { $probePe.exports -notcontains $_ })
$embeddedExportDelta = @(
    @($probePe.exports | Where-Object { $probeStandalonePe.exports -notcontains $_ }) +
    @($probeStandalonePe.exports | Where-Object { $probePe.exports -notcontains $_ })
)

Add-Check 'resource.201.byte-exact' (
    $probeBytes.Length -eq $probeStandalone.length -and
    $probeEmbeddedHash -eq $probeStandalone.sha256
) 'RELEASE_EXE' "RCDATA201=$probeEmbeddedHash/$($probeBytes.Length); standalone=$($probeStandalone.sha256)/$($probeStandalone.length)."
Add-Check 'resource.202.byte-exact' (
    $injectorBytes.Length -eq $injectorStandalone.length -and
    $injectorEmbeddedHash -eq $injectorStandalone.sha256
) 'RELEASE_EXE' "RCDATA202=$injectorEmbeddedHash/$($injectorBytes.Length); standalone=$($injectorStandalone.sha256)/$($injectorStandalone.length)."
Add-Check 'resource.201.pe-abi-exports' (
    $probePe.machineValue -eq 0x14C -and $probePe.optionalMagic -eq 0x10B -and
    $probePe.isDll -and $missingExports.Count -eq 0 -and $embeddedExportDelta.Count -eq 0
) 'DLL' "RCDATA201 machine=$($probePe.machine), optional=$($probePe.optionalHeader), DLL=$($probePe.isDll), exports=$($probePe.exports -join ','); missing-required=$($missingExports -join ','); embedded/standalone-delta=$($embeddedExportDelta -join ',')."
Add-Check 'resource.202.pe-abi' (
    $injectorPe.machineValue -eq 0x14C -and $injectorPe.optionalMagic -eq 0x10B -and
    -not $injectorPe.isDll
) 'DLL' "RCDATA202 machine=$($injectorPe.machine), optional=$($injectorPe.optionalHeader), DLL=$($injectorPe.isDll)."
Add-Check 'resource.single-embedded-x86-dll' (
    $rcDataInvalidMzCount -eq 0 -and $rcDataDllRows.Count -eq 1 -and
    $probeResourceLeaves.Count -eq 1 -and $injectorResourceLeaves.Count -eq 1 -and
    [string]$rcDataDllRows[0].Type -ceq '#10' -and
    [string]$rcDataDllRows[0].Name -ceq '#201' -and
    [int]$rcDataDllRows[0].MachineValue -eq 0x14C -and
    [int]$rcDataDllRows[0].OptionalMagic -eq 0x10B -and
    [string]$rcDataDllRows[0].SHA256 -ceq $probeEmbeddedHash
) 'DLL' "resource leaves=$(@($rcDataInventory | ForEach-Object { '{0}/{1}/lang={2}' -f $_.Type,$_.Name,$_.Language }) -join ','); PE rows=$(@($rcDataPeRows | ForEach-Object { '{0}/{1}/lang={2}/DLL={3}/{4}' -f $_.Type,$_.Name,$_.Language,$_.IsDll,$_.SHA256 }) -join ','); invalid-MZ=$rcDataInvalidMzCount."

$probeSignature = (Get-AuthenticodeSignature -LiteralPath $ProbePayload).Status.ToString()
$injectorSignature = (Get-AuthenticodeSignature -LiteralPath $InjectorPayload).Status.ToString()

$expectedDomains = @($script:ExpectedProbeDomains | ForEach-Object { $_.Domain })
$domainOrderValid = $true
$domainCursor = -1
foreach ($domain in $expectedDomains) {
    $next = $ultimateHeader.IndexOf($domain, $domainCursor + 1, [StringComparison]::Ordinal)
    if ($next -lt 0) { $domainOrderValid = $false; break }
    $domainCursor = $next
}
$domainOrderValid = $domainOrderValid -and
    $ultimateHeader -match 'kUltimateProbeDomainCount\s*=\s*25' -and
    $ringHeader -match 'kSemanticDomainCount\s*=\s*25'
Add-Check 'domains.inventory.exact-25' $domainOrderValid 'DLL' 'UltimateProbeDomain order and both compile-time domain counts are exactly 25.'

$blockedProbesExpected = @($script:ExpectedProbeDomains | Select-Object -Skip 4 | ForEach-Object { $_.Probe })
$descriptorRegionMatch = [regex]::Match($probeSource,
    'constexpr DeepProbeDomainDescriptor kBlockedDeepProbeDomains\[\]\s*=\s*\{(?<body>[\s\S]*?)\};')
$descriptorMatches = if ($descriptorRegionMatch.Success) {
    [regex]::Matches($descriptorRegionMatch.Groups['body'].Value,
        '\{"(?<domain>[^"]+)",\s*"(?<probe>[^"]+)",\s*DeepProbeSeed::(?<seed>Parser|Serializer|Handler)')
} else { @() }
$blockedProbesActual = @($descriptorMatches | ForEach-Object { $_.Groups['probe'].Value })
$blockedInventoryValid = $blockedProbesActual.Count -eq 21 -and
    (@($blockedProbesExpected | Where-Object { $blockedProbesActual -notcontains $_ }).Count -eq 0) -and
    $probeSource -match '_countof\(kBlockedDeepProbeDomains\)\s*==\s*21'
Add-Check 'domains.candidate-only.exact-21' $blockedInventoryValid 'DLL' "Blocked descriptor count=$($blockedProbesActual.Count); descriptors=$($blockedProbesActual -join ',')."

$confirmedProbes = @($script:ExpectedProbeDomains | Select-Object -First 4 | ForEach-Object { $_.Probe })
$domainInventory = New-Object Collections.ArrayList
for ($index = 0; $index -lt $expectedDomains.Count; $index++) {
    $displayDomain = $expectedDomains[$index]
    $probe = if ($index -lt 4) { $confirmedProbes[$index] } else { $blockedProbesExpected[$index - 4] }
    [void]$domainInventory.Add([ordered]@{
        index = $index
        domain = $displayDomain
        probe = $probe
        contractStatus = if ($index -lt 4) { 'CONFIRMED_EXACT_BUILD_CONTRACT' } else { 'CANDIDATE_ONLY_EVIDENCE_BLOCKED' }
        activationAllowed = ($index -lt 4)
    })
}
Add-Check 'domains.no-unconfirmed-hooks' (
    $probeSource -match 'const volatile bool allowUnconfirmedTargetHooks\s*=\s*false' -and
    $probeSource -notmatch 'CanActivateDeepProbeCandidate' -and
    $probeSource.Contains('\"UnconfirmedProbePolicy\":\"EvidenceBlockedUnconfirmedProbe\"') -and
    $probeSource.Contains('\"Status\":\"EvidenceBlockedUnconfirmedProbe\"')
) 'DLL' 'DLL contains no deep-candidate activation call; legacy unconfirmed target hook switch is immutable false and every candidate-only domain is emitted blocked/UNKNOWN.'

$hardGateTokens = @(
    'module_section.empty','callsite_rva != 0U','target_rva != 0U','valid_signature',
    'FF FF FF FF FF FF FF FF','BoundedExecutableDirectCall','calling_convention_state',
    'exact_target_identity','executable_section','exact_candidate_bytes',
    'calling_convention_verified','typed_runtime_evidence','repeated_causal_observations >= 2U',
    'contradictions == 0U','stable_object_or_context','verified_consumer_or_mutation',
    'sensitive_mask_contract'
)
$missingHardGateTokens = @($hardGateTokens | Where-Object { $ultimateSource.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
Add-Check 'candidate-v2.discovery-and-hard-activation' (
    $missingHardGateTokens.Count -eq 0 -and
    $probeSource -match 'god2-deep-probe-candidate-map-v2' -and
    $probeSource -match 'kDeepProbeScanRadius\s*=\s*256' -and
    $probeSource -match 'kDeepProbeMaximumCandidatesPerDomain\s*=\s*4' -and
    $probeSource -match 'kDeepProbeSignatureBytes\s*=\s*8' -and
    $probeSource -match 'NoWritableMemoryScan;NoSensitiveValueLogging'
) 'DLL' "Missing activation predicates: $($missingHardGateTokens -join ','); discovery bounds=256/4/8 and executable-only/no-sensitive logging required."

$exactSignatureGate = $probeSource -match '0x00078A48' -and $probeSource -match '0x00078D70' -and
    $probeSource -match '0x0007FC10' -and $probeSource -match '55 8B EC 56 57' -and
    $probeSource -match '0x00147096' -and $probeSource -match '0x001470AE' -and
    $probeSource -match '0x0007F940' -and $probeSource -match 'verifyInstalledRelativeBranch'
Add-Check 'target.exact-rva-signature-gate' $exactSignatureGate 'DLL' 'Parser 0x78A48->0x78D70, serializer 0x7FC10/55 8B EC 56 57, and both handler callsites ->0x7F940 are exact verified identities.'

$expectedTargetSha = $script:ExpectedClientSha256
$identityContract = $injectorSource.Contains($expectedTargetSha) -and
    $injectorSource -match 'kExpectedTargetVersion\[\]\s*=\s*L"1\.0\.0\.1"' -and
    $injectorSource -match 'God2_opt\.exe' -and $injectorSource -match 'IMAGE_FILE_MACHINE_I386' -and
    $probeSource.Contains($expectedTargetSha) -and $probeSource -match 'g_clientBuildVerified' -and
    $probeSource -match 'g_configuredClientCreationTime\s*==\s*observedCreationTime'
Add-Check 'target.build-architecture-process-identity' $identityContract 'DLL' 'God2_opt.exe/x86/1.0.0.1/full SHA-256/PID+creation-time are all required before deep hooks; mismatch forces network-only.'

$semanticFields = @(
    'schema_version = 2','event_type','event_id','sequence','timestamp','thread_id','process_id',
    'session_id','client_build_id','module_id','rva','callsite_rva','parent_event_id',
    'context_id','action_id','object_token','value_token','source_token','authority_hint',
    'sensitive_mask_status','payload'
)
$missingSemanticFields = @($semanticFields | Where-Object { $ultimateHeader.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
Add-Check 'semantic-event-v2.contract' (
    $missingSemanticFields.Count -eq 0 -and
    $ultimateHeader -match 'kUltimateSemanticEventTypeCount\s*=\s*25' -and
    $ultimateSource -match 'SerializeSemanticEventV2' -and $ultimateSource -match 'ReadSemanticEvent'
) 'DLL' "SemanticEvent v2 missing fields: $($missingSemanticFields -join ','); 25 event types and v1-compatible reader/strict v2 writer are compiled."

$ringTokens = @(
    'kSemanticRingMagic = 0x34525347u','kSemanticRingVersion = 4',
    'kSemanticDomainCount = 25','kSemanticLaneSlotCount = 64',
    'kSemanticPriorityCount = 4','kSemanticPayloadBytes = 8192',
    'Authoritative = 0','ObjectEvidence = 1','SupportingTrace = 2','CandidateTelemetry = 3',
    'first_dropped_sequence','last_dropped_sequence','last_drop_reason','high_water_mark',
    'consumer_lag','write_failures','producer_ready','producer_closed','PriorityBackpressure',
    'BatchValidationFailed','TransportWriteFailure','sizeof(SemanticDomainCounters) == 32u',
    'sizeof(SemanticPriorityLane) == 36u','offsetof(SemanticSharedRing, slots) == 1032u',
    'sizeof(SemanticSharedRing) == 2106376u',
    'g_sharedRingLaneLocks[god2::shared::kSemanticPriorityCount]',
    'losslessPriority','SemanticPriority::ObjectEvidence',
    'EnterCriticalSection(laneLock)','TryEnterCriticalSection(laneLock)',
    'while (losslessPriority','ConsumerUnavailable','SamplingPolicy',
    'MemoryBarrier','InterlockedExchange(&slot.state, 2)','slot.sequence','slot.domain','slot.priority'
)
$ringCombined = $ringHeader + "`n" + $probeSource
$missingRingTokens = @($ringTokens | Where-Object { $ringCombined.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
Add-Check 'ipc.ring-v4-source-contract' ($missingRingTokens.Count -eq 0) 'DLL' "Missing shared-ring v4 predicates: $($missingRingTokens -join ','); four isolated 64x8192 lanes, P0/P1 lossless backpressure, P2/P3 sampling, 32-byte per-domain counters, batch validation and write-failure accounting required."

$strictUtf8Reader = New-Object Text.UTF8Encoding($false, $true)
$evidenceFixture = [IO.File]::ReadAllText($EvidenceFixtureReportJson, $strictUtf8Reader) | ConvertFrom-Json
$evidenceFixtureContract = Test-EvidenceFixtureContract $evidenceFixture
$evidenceFixtureItem = Get-Item -LiteralPath $EvidenceFixtureReportJson
Add-Check 'evidence.package-fixture-schema-count-named-checks' $evidenceFixtureContract.Passed 'RELEASE_EXE' "Fresh package fixture requires SchemaVersion=11, CheckCount>=77, Passed=CheckCount, Failed=0, unique all-PASS rows, and exact 10a/10b/10c/10d/14e/14f named checks; issues=$($evidenceFixtureContract.Issues -join ',')."

$restoreFaultRaw = [IO.File]::ReadAllText($RestoreFaultLifecycleReport, $strictUtf8Reader)
$restoreFaultJsonContract = Test-StrictJsonTextContract $restoreFaultRaw
$restoreFaultReport = $null
try { $restoreFaultReport = $restoreFaultRaw | ConvertFrom-Json } catch { }
$restoreFaultTypedContract = Test-RestoreFaultLifecycleContract $restoreFaultReport
$restoreFaultIssues = @($restoreFaultJsonContract.Issues) + @($restoreFaultTypedContract.Issues)
$restoreFaultContractPassed = $restoreFaultIssues.Count -eq 0
Add-Check 'lifecycle.restore-fault-raw-exact-owner-contract' $restoreFaultContractPassed 'DLL' "Fresh restore-fault report is strict JSON and proves exact-owner Ready/Arm/Stop/CanUnload, first rejection with resident owner, zero FreeLibrary before retry, exactly one release, two absence snapshots and live target; issues=$($restoreFaultIssues -join ',')."

$network = Get-Content -LiteralPath $NetworkSelfTestSummary -Raw | ConvertFrom-Json
$networkContract = Test-NetworkRuntimeContract $network
$networkCasesValid = [bool]$networkContract.Passed
$networkItem = Get-Item -LiteralPath $NetworkSelfTestSummary
$payloadItem = Get-Item -LiteralPath $ProbePayload
$networkFresh = $networkItem.LastWriteTimeUtc -ge $payloadItem.LastWriteTimeUtc
Add-Check 'winsock.five-case-runtime' ($networkCasesValid -and $networkFresh) 'DLL' "Fresh baseline send/WSASend/recv/WSARecv/WSARecvOverlapped hook/payload/analyzer/trace evidence; issues=$($networkContract.Issues -join ','); summaryUtc=$($networkItem.LastWriteTimeUtc.ToString('o')); payloadUtc=$($payloadItem.LastWriteTimeUtc.ToString('o'))."
Add-Check 'selftest.export-execution-binding' (
    $selfTestClient -match 'GetProcAddress\s*\(\s*probe\s*,\s*"God2TraceProbeWaitReady"\s*\)' -and
    $selfTestClient -match 'waitReady\s*\(\s*nullptr\s*\)\s*!=\s*1u' -and
    $selfTestClient -match 'GetProcAddress\s*\(\s*probe\s*,\s*"God2TraceProbeSemanticSelfTest"\s*\)' -and
    $selfTestClient -match 'semanticSelfTest\s*\(\s*nullptr\s*\)\s*!=\s*1u' -and
    $selfTestClient -match 'GetProcAddress\s*\(\s*probe\s*,\s*"God2TraceProbeSharedTransportSelfTest"\s*\)' -and
    $selfTestClient -match 'sharedTransportSelfTest\s*\(\s*nullptr\s*\)' -and
    $networkCasesValid
) 'DLL' 'Every successful five-case runtime process first executes WaitReady, the internal ABI/privacy/lifecycle SemanticSelfTest, and the shared-transport export.'

$productRaw = [IO.File]::ReadAllText($ProductSelfTestSummary, $strictUtf8Reader)
$productJsonTextContract = Test-StrictJsonTextContract $productRaw
$product = $null
try { $product = $productRaw | ConvertFrom-Json }
catch { }
$typedProductContract = Test-ProductRuntimeContract $product
$productContractIssues = [Collections.Generic.List[string]]::new()
foreach ($issue in @($productJsonTextContract.Issues)) {
    $productContractIssues.Add("product.json.$issue")
}
foreach ($issue in @($typedProductContract.Issues)) {
    $productContractIssues.Add([string]$issue)
}
$productContract = Complete-ContractValidation $productContractIssues
$attachBinding = Read-BoundJsonArtifact 'attach-result' `
    (Get-JsonPropertyValue $product 'AttachResultPath') `
    (Get-JsonPropertyValue $product 'AttachResultSHA256') `
    (Get-JsonPropertyValue $product 'AttachResultRecord') $true `
    { param($value) Test-InjectorResultV2Contract $value 'Attach' }
$detachBinding = Read-BoundJsonArtifact 'detach-result' `
    (Get-JsonPropertyValue $product 'DetachResultPath') `
    (Get-JsonPropertyValue $product 'DetachResultSHA256') `
    (Get-JsonPropertyValue $product 'DetachResultRecord') $true `
    { param($value) Test-InjectorResultV2Contract $value 'Detach' }
$identityBinding = Read-BoundJsonArtifact 'identity-result' `
    (Get-JsonPropertyValue $product 'ProductionIdentityGatePath') `
    (Get-JsonPropertyValue $product 'ProductionIdentityGateSHA256') `
    (Get-JsonPropertyValue $product 'ProductionIdentityGateRecord') $true `
    { param($value) Test-InjectorResultV2Contract $value 'IdentityBlocked' }
$nativeEvidenceBinding = Read-BoundJsonArtifact 'native-probe' `
    (Get-JsonPropertyValue $product 'NativeProbeSelfTestPath') `
    (Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256') `
    (Get-JsonPropertyValue $product 'NativeProbeSelfTestReport') $true `
    { param($value) Test-NativeProbeRuntimeContract $value }
$nativeEvidencePath = $nativeEvidenceBinding.Path
$nativeEvidence = $nativeEvidenceBinding.Value
$bridgeStatus = Get-JsonPropertyValue $nativeEvidence 'UnloadNeutralBridge'
$bridgeCoverage = @(Get-JsonPropertyValue $bridgeStatus 'BridgeCoverageResults')
$networkBridgeReady = $bridgeCoverage.Count -eq 11 -and
    @($bridgeCoverage | Select-Object -First 8 | Where-Object {
        -not (Test-JsonBooleanValue (Get-JsonPropertyValue $_ 'InstalledToBridge') $true)
    }).Count -eq 0
$bridgeAwareStatusV3 = [ordered]@{
    SchemaVersion = 'god2-enhanced-capture-status-v3'
    GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    AuthorityProfile = 'TestOnlyExactFixture'
    OfficialRuntimeObserved = $false
    ExtendedReady = $false
    NetworkBridgeReady = [bool]$networkBridgeReady
    InternalBridgeReady = $false
    StrictUnloadVerified = [bool]((Get-JsonPropertyValue $product 'UnloadSafe') -eq $true)
    ModuleAbsent = [bool]((Get-JsonPropertyValue $product 'ModuleAbsent') -eq $true)
    TargetAliveAfterUnload = [bool]((Get-JsonPropertyValue $restoreFaultReport 'TargetAliveAfter') -eq $true)
    BridgeSchemaId = Get-JsonPropertyValue $bridgeStatus 'SchemaId'
    BridgePhase = Get-JsonPropertyValue $bridgeStatus 'Phase'
    BridgeState = Get-JsonPropertyValue $bridgeStatus 'State'
    BridgeNoDllPointers = Get-JsonPropertyValue $bridgeStatus 'BridgeNoDllPointers'
    DllPointerCount = Get-JsonPropertyValue $bridgeStatus 'DllPointerCount'
    WritableExecutablePageCount = Get-JsonPropertyValue $bridgeStatus 'WritableExecutablePageCount'
    ObserverRundown = Get-JsonPropertyValue $bridgeStatus 'ObserverRundown'
    PendingApplicationCallbacks = Get-JsonPropertyValue $bridgeStatus 'PendingApplicationCallbacks'
    ExactPersistentOriginalPointerCount = Get-JsonPropertyValue $bridgeStatus 'ExactPersistentOriginalPointerCount'
    ExactEntryPointerCount = Get-JsonPropertyValue $bridgeStatus 'ExactEntryPointerCount'
    ExactCompletionThunkCount = Get-JsonPropertyValue $bridgeStatus 'ExactCompletionThunkCount'
    IatRestored = Get-JsonPropertyValue $bridgeStatus 'IatRestored'
    HotpatchRestored = Get-JsonPropertyValue $bridgeStatus 'HotpatchRestored'
    RestoreFaultLifecyclePassed = Get-JsonPropertyValue $restoreFaultReport 'Passed'
    RestoreFaultFreeLibraryCallCount = Get-JsonPropertyValue $restoreFaultReport 'FreeLibraryCallCount'
    RestoreFaultVerifiedAbsentSnapshotCount = Get-JsonPropertyValue $restoreFaultReport 'VerifiedAbsentSnapshotCount'
    ProductEvidenceSHA256 = Get-Sha256 $ProductSelfTestSummary
    NativeEvidenceSHA256 = Get-Sha256 $nativeEvidencePath
    RestoreFaultEvidenceSHA256 = Get-Sha256 $RestoreFaultLifecycleReport
}
$bridgeAwareStatusV3ValidationObject =
    ($bridgeAwareStatusV3 | ConvertTo-Json -Depth 8 -Compress) | ConvertFrom-Json
$bridgeAwareStatusV3Contract = Test-BridgeAwareStatusV3Contract $bridgeAwareStatusV3ValidationObject
$bridgeAwareStatusV3Passed = $bridgeAwareStatusV3SchemaContract.Passed -and $bridgeAwareStatusV3Contract.Passed
$bridgeAwareStatusV3Path = Join-Path $OutputRoot 'enhanced-capture-status-v3.json'
$statusV3Utf8 = New-Object Text.UTF8Encoding($false, $true)
[IO.File]::WriteAllText($bridgeAwareStatusV3Path,
    (($bridgeAwareStatusV3 | ConvertTo-Json -Depth 8) + "`n"), $statusV3Utf8)
$bridgeAwareStatusV3Sha256 = Get-Sha256 $bridgeAwareStatusV3Path
Add-Check 'status.bridge-aware-v3-closed-authority' $bridgeAwareStatusV3Passed 'RELEASE_EXE' "Closed v3 status binds the test-only identity boundary, network bridge, zero DLL pointers/RWX/rundown/pending callbacks, exact 7/11/256 pointer inventory, strict unload and actual restore-fault lifecycle; schemaIssues=$($bridgeAwareStatusV3SchemaContract.Issues -join ','); reportIssues=$($bridgeAwareStatusV3Contract.Issues -join ',')."
$nativeEvidenceContract = if ($null -ne $nativeEvidence) {
    Test-NativeProbeRuntimeContract $nativeEvidence
} else { $null }
$nativeWireBinding = Read-NativeSemanticWireArtifact `
    (Get-JsonPropertyValue $product 'NativeSemanticWirePath') `
    (Get-JsonPropertyValue $product 'NativeSemanticWireSHA256') `
    (Get-JsonPropertyValue $product 'TargetProcessId') `
    (Get-JsonPropertyValue $product 'SemanticEventTypeDigestSHA256')
$nativeSemanticWirePath = $nativeWireBinding.Path
$wireVerifierBinding = Read-BoundJsonArtifact 'native-wire-verifier' `
    (Get-JsonPropertyValue $product 'NativeSemanticWireVerificationPath') `
    (Get-JsonPropertyValue $product 'NativeSemanticWireVerificationSHA256') `
    (Get-JsonPropertyValue $product 'NativeSemanticWireVerificationReport') $true `
    { param($value) Test-NativeSemanticWireVerificationContract $value `
        (Get-JsonPropertyValue $product 'NativeSemanticWireSHA256') `
        (Get-JsonPropertyValue $product 'SemanticEventTypeDigestSHA256') }
$ultimateWireBinding = Read-BoundJsonArtifact 'ultimate-native-wire' `
    (Get-JsonPropertyValue $product 'UltimateNativeWireReportPath') `
    (Get-JsonPropertyValue $product 'UltimateNativeWireReportSHA256') `
    (Get-JsonPropertyValue $product 'UltimateNativeWireReport') $true `
    { param($value) Test-UltimateRuntimeContract $value 0 }
$candidateMapBinding = Read-BoundJsonArtifact 'candidate-map' `
    (Get-JsonPropertyValue $product 'DeepProbeCandidateMapPath') `
    (Get-JsonPropertyValue $product 'DeepProbeCandidateMapSHA256') `
    $null $false { param($value) Test-DeepProbeCandidateMapContract $value }
$candidateMapEvidence = $candidateMapBinding.Value
$productArtifactBindingIssues = [Collections.Generic.List[string]]::new()
foreach ($binding in @($attachBinding,$detachBinding,$identityBinding,$nativeEvidenceBinding,
        $nativeWireBinding,$wireVerifierBinding,$ultimateWireBinding,$candidateMapBinding)) {
    foreach ($issue in @($binding.Issues)) { $productArtifactBindingIssues.Add([string]$issue) }
}
if ($null -ne $nativeEvidence -and
    ((Get-JsonPropertyValue $nativeEvidence 'SchemaId') -cne
        (Get-JsonPropertyValue $product 'NativeProbeSelfTestSchemaId') -or
     (Get-JsonPropertyValue $nativeEvidence 'SchemaVersion') -ne
        (Get-JsonPropertyValue $product 'NativeProbeSelfTestSchemaVersion'))) {
    $productArtifactBindingIssues.Add('native.schema.product-binding-mismatch')
}
$nativeBindingValid = [bool]$nativeEvidenceBinding.Passed
$productArtifactBindingsValid = $productArtifactBindingIssues.Count -eq 0
$productValid = [bool]$productContract.Passed -and $productArtifactBindingsValid
$productIssueText = (@($productContract.Issues) + @($productArtifactBindingIssues)) -join ','
Add-Check 'selftest.product-raw-artifact-path-hash-shape-binding' $productArtifactBindingsValid 'DLL' "Product summary binds immutable strict UTF-8/no-BOM typed attach/detach/identity/native/candidate/wire/verifier/Ultimate artifacts by canonical repository path, exact SHA and deep-equal records; issues=$($productArtifactBindingIssues -join ',')."
Add-Check 'selftest.native-report-path-hash-schema-binding' $nativeBindingValid 'DLL' "Product summary binds one repository-relative native-probe-selftest.json by exact SHA/schema/full embedded shape; issues=$($nativeEvidenceBinding.Issues -join ',')."
Add-Check 'domains.runtime-evidence-exact-25' (Test-ContractIssueGroup $productContract @('product.ProbeDomain','product.ProbeDomainCount','product.ConfirmedContractDomainCount','product.CandidateOnlyBlockedDomainCount')) 'DLL' "Product runtime emitted 25 unique domain diagnostics classified 4 confirmed contracts plus 21 candidate-only blocked; issues=$productIssueText."
Add-Check 'candidate.activation-gates-runtime-10-plus-4' (Test-ContractIssueGroup $productContract @('product.Candidate')) 'DLL' "Product runtime requires all 10 promotion and 4 ABI-safety gates, individually negative-tested; issues=$productIssueText."
Add-Check 'winsock.completion-runtime' ($nativeBindingValid -and (Test-ContractIssueGroup $productContract @('product.NativeProbeSelfTestReport.native.Completion','product.NativeProbeSelfTestReport.native.WSAGet','product.NativeProbeSelfTestReport.native.GetQueued'))) 'DLL' "Fresh native x86 runtime observed CompletionRoutine, WSAGetOverlappedResult, GQCS and GQCSEx; issues=$productIssueText."
Add-Check 'ipc.ring-v3-cross-process-runtime' ($nativeBindingValid -and (Test-ContractIssueGroup $productContract @('product.SharedTransport','product.NativeProbeSelfTestReport.native.SharedTransport'))) 'DLL' "Ring v3 proves measured priority 0..3 direction, batch>1 with one signal/lock per batch, ordered cross-process transport, and 25-domain drop/write/pending/high-water accounting; issues=$productIssueText."
Add-Check 'lifecycle.actual-unload-and-restore-fault-runtime' ($nativeBindingValid -and (Test-ContractIssueGroup $productContract @('product.AttachResultRecord','product.DetachResultRecord','product.AttachDetach','product.SafeUnloadHandshake','product.Module','product.Unload','product.ExtraReference','product.Residual','product.NativeProbeSelfTestReport.native.Restore','product.NativeProbeSelfTestReport.native.PendingAtStop'))) 'DLL' "Exact typed v2 attach/detach records, actual module snapshot absence, extra-reference false-positive negative, restore-fault resident negative and pending-at-stop retry drain are required; issues=$productIssueText."
Add-Check 'fault-isolation.per-domain-runtime' ($nativeBindingValid -and (Test-ContractIssueGroup $productContract @('product.NativeProbeSelfTestReport.native.FaultIsolation'))) 'DLL' "Every one of 25 domains is fault-injected independently; affected domain disables, diagnostic emits, and another domain continues; issues=$productIssueText."
Add-Check 'diagnostics.bounded-async-no-hook-file-io-runtime' ($nativeBindingValid -and (Test-ContractIssueGroup $productContract @('product.NativeProbeSelfTestReport.native.HookThread','product.NativeProbeSelfTestReport.native.AsyncDiagnostic'))) 'DLL' "Instrumented hook-thread synchronous file IO remains zero; bounded async diagnostic queue proves per-domain overflow attribution and complete drain; issues=$productIssueText."
Add-Check 'product.runtime-contract-complete' $productValid 'DLL' "Strict product evidence contract issues=$productIssueText."
Add-Check 'target.production-activation-fail-closed' (Test-ContractIssueGroup $productContract @('product.ProductionIdentityGateRecord','product.TargetExecutable','product.TargetArchitecture')) 'DLL' "Current deterministic fixture is not the official client and its exact typed v2 identity result remained blocked: $((Get-JsonPropertyValue $product 'ProductionIdentityGateRecord') | ConvertTo-Json -Depth 4 -Compress)."
$bridgeRuntimePassed = $nativeBindingValid -and (Test-ContractIssueGroup $productContract @(
    'product.NativeProbeSelfTestReport.native.UnloadNeutralBridge','product.BlockingCoverage','product.BlockingPostUnload'))
Add-Check 'lifecycle.unload-neutral-bridge-runtime-source-lock' $bridgeRuntimePassed 'DLL' "The immutable native report proves a closed 119-field bridge ledger, RX/RW-NX separation, exact 7 originals/11 entries/256 thunks, observer retirement, zero DLL pointers, no-clobber transactions, WSA route/ABA/commit/final-purge fixtures, and four target-side blocked calls completing only after DLL absence; issues=$productIssueText."
$officialIssues = [Collections.Generic.List[string]]::new()
$officialRuntime = $null
try {
    $officialBytes = [IO.File]::ReadAllBytes($OfficialRuntimeEvidence)
    if ($officialBytes.Length -ge 3 -and $officialBytes[0] -eq 0xEF -and
        $officialBytes[1] -eq 0xBB -and $officialBytes[2] -eq 0xBF) {
        $officialIssues.Add('official-runtime.utf8-bom')
    }
    $officialRaw = [Text.UTF8Encoding]::new($false, $true).GetString($officialBytes)
    $officialStrict = Test-StrictJsonTextContract $officialRaw
    foreach ($issue in @($officialStrict.Issues)) {
        $officialIssues.Add("official-runtime.json.$issue")
    }
    $officialRuntime = $officialRaw | ConvertFrom-Json
} catch {
    $officialIssues.Add("official-runtime.read:$($_.Exception.Message)")
}

$officialSummaryKeysV1 = @(
    'SchemaId','SchemaVersion','GeneratedAtUtc','ClientPath','ClientSHA256',
    'ClientVersion','ClientProcessId','KnownDirectSoundDialogDismissed',
    'AttachStatus','DetachStatus','AttachRecordPath','AttachRecordSHA256',
    'DetachRecordPath','DetachRecordSHA256','StrictUnloadVerified','ModuleAbsent',
    'TargetAliveAfterDetach','NativeReportPath','NativeReportSHA256',
    'CandidateMapPath','CandidateMapSHA256','ReadyLines','Passed')
$officialSummaryKeysV2 = @($officialSummaryKeysV1 + @(
    'AuthorityProfile','SessionId','ClientArchitecture','ClientProcessCreationTime',
    'LauncherPath','LauncherSHA256','LauncherProcessId','LauncherClientChainPath',
    'LauncherClientChainSHA256','LauncherParentVerified','DirectClientLaunch',
    'DialogDismissedAtUtc','FormalAttachTimingStartedAtUtc','AttachObservedAtUtc',
    'DetachObservedAtUtc','CurrentSessionObserved','HistoricalEvidenceReused',
    'FixtureOnly','PromotionEligible','RuntimeEventCount','ContractAcquisitionEnabled',
    'ContractAcquisitionObserveSeconds','ContractAcquisitionCandidateCount',
    'ContractAcquisitionFundamentalDomainCount','ContractAcquisitionRuntimeObservationCount',
    'ContractAcquisitionRuntimeObservedDomainCount','ContractAcquisitionActivationAllowedCount',
    'ContractAcquisitionPromotionEligibleCount','ContractAcquisitionStatus',
    'ContractAcquisitionManifestPath','ContractAcquisitionManifestSHA256',
    'ProductionRingHealthPath','ProductionRingHealthSHA256','ProductionRingHealthPassed',
    'ProductionRingAttempted','ProductionRingAccepted','ProductionRingConsumed',
    'ProductionRingDroppedP0','ProductionRingDroppedP1','ProductionRingSequenceGaps',
    'ProductionRingPendingAfterDrain','ProductionRingSegmentCount','SemanticEventsPath',
    'SemanticEventsSHA256','ResultPackagePath','ResultPackageSHA256'))
if ($null -ne $officialRuntime) {
    $observedOfficialSchemaVersion = [int](Get-JsonPropertyValue $officialRuntime 'SchemaVersion')
    $expectedOfficialKeys = if ($observedOfficialSchemaVersion -eq 2) {
        $officialSummaryKeysV2
    } else { $officialSummaryKeysV1 }
    if (-not (Test-ExactJsonProperties $officialRuntime $expectedOfficialKeys)) {
        $officialIssues.Add('official-runtime.exact-properties')
    }
    if ((Get-JsonPropertyValue $officialRuntime 'SchemaId') -cne
            'God2OfficialClientRuntime' -or
        $observedOfficialSchemaVersion -notin @(1,2)) {
        $officialIssues.Add('official-runtime.schema')
    }
    if ((Get-JsonPropertyValue $officialRuntime 'ClientSHA256') -cne
            '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B' -or
        (Get-JsonPropertyValue $officialRuntime 'ClientVersion') -cne '1.0.0.1' -or
        [IO.Path]::GetFileName([string](Get-JsonPropertyValue $officialRuntime 'ClientPath')) -ine
            'God2_opt.exe') {
        $officialIssues.Add('official-runtime.client-identity')
    }
    foreach ($flag in @('KnownDirectSoundDialogDismissed','StrictUnloadVerified',
            'ModuleAbsent','TargetAliveAfterDetach','Passed')) {
        if (-not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime $flag) $true)) {
            $officialIssues.Add("official-runtime.$flag")
        }
    }
    if ((Get-JsonPropertyValue $officialRuntime 'AttachStatus') -cne 'ATTACHED' -or
        (Get-JsonPropertyValue $officialRuntime 'DetachStatus') -cne 'DETACHED') {
        $officialIssues.Add('official-runtime.attach-detach-status')
    }
    if ($observedOfficialSchemaVersion -eq 2) {
        $officialSessionFromPath = Split-Path -Leaf (Split-Path -Parent (
            Split-Path -Parent $OfficialRuntimeEvidence))
        if ((Get-JsonPropertyValue $officialRuntime 'AuthorityProfile') -cne
                'CurrentOfficialClientLive' -or
            (Get-JsonPropertyValue $officialRuntime 'SessionId') -cne $officialSessionFromPath -or
            (Get-JsonPropertyValue $officialRuntime 'ClientArchitecture') -cne 'x86' -or
            [string]::IsNullOrWhiteSpace([string](Get-JsonPropertyValue $officialRuntime 'ClientProcessCreationTime')) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'CurrentSessionObserved') $true) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'HistoricalEvidenceReused') $false) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'FixtureOnly') $false) -or
            (Get-JsonPropertyValue $officialRuntime 'PromotionEligible') -isnot [bool] -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'RuntimeEventCount') -eq 0) {
            $officialIssues.Add('official-runtime.current-authority')
        }
        $officialLauncherPath = [string](Get-JsonPropertyValue $officialRuntime 'LauncherPath')
        $officialLauncherSha = [string](Get-JsonPropertyValue $officialRuntime 'LauncherSHA256')
        try {
            $resolvedOfficialLauncher = [IO.Path]::GetFullPath($officialLauncherPath)
            if ([IO.Path]::GetFileName($resolvedOfficialLauncher) -ine 'Launcher.exe' -or
                -not (Test-Path -LiteralPath $resolvedOfficialLauncher -PathType Leaf) -or
                $officialLauncherSha -cnotmatch '^[0-9A-F]{64}$' -or
                (Get-Sha256 $resolvedOfficialLauncher) -cne $officialLauncherSha -or
                [uint32](Get-JsonPropertyValue $officialRuntime 'LauncherProcessId') -eq 0 -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'LauncherParentVerified') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'DirectClientLaunch') $false)) {
                $officialIssues.Add('official-runtime.launcher-identity-chain')
            }
        } catch {
            $officialIssues.Add('official-runtime.launcher-identity-chain')
        }
        $runtimeEventCount = [uint64](Get-JsonPropertyValue $officialRuntime 'RuntimeEventCount')
        if (-not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionEnabled') $true) -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionObserveSeconds') -ne 48 -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionCandidateCount') -ne 11 -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionFundamentalDomainCount') -ne 11 -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionRuntimeObservationCount') -eq 0 -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionRuntimeObservedDomainCount') -eq 0 -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionActivationAllowedCount') -ne 0 -or
            [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionPromotionEligibleCount') -ne 0 -or
            (Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionStatus') -cne
                'CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_GATES_PENDING') {
            $officialIssues.Add('official-runtime.contract-acquisition-state')
        }
        if (-not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'ProductionRingHealthPassed') $true) -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingAttempted') -ne $runtimeEventCount -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingAccepted') -ne $runtimeEventCount -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingConsumed') -ne $runtimeEventCount -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingDroppedP0') -ne 0 -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingDroppedP1') -ne 0 -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingSequenceGaps') -ne 0 -or
            [uint64](Get-JsonPropertyValue $officialRuntime 'ProductionRingPendingAfterDrain') -ne 0 -or
            [uint32](Get-JsonPropertyValue $officialRuntime 'ProductionRingSegmentCount') -eq 0) {
            $officialIssues.Add('official-runtime.production-ring-state')
        }
        try {
            $dialogUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'DialogDismissedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $formalUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'FormalAttachTimingStartedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $attachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'AttachObservedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $detachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'DetachObservedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            if ($dialogUtc -gt $formalUtc -or $formalUtc -gt $attachUtc -or
                $attachUtc -gt $detachUtc) {
                $officialIssues.Add('official-runtime.dialog-attach-detach-order')
            }
        } catch {
            $officialIssues.Add('official-runtime.dialog-attach-detach-time')
        }
        foreach ($bindingName in @('LauncherClientChain','ContractAcquisitionManifest',
                'ProductionRingHealth','SemanticEvents','ResultPackage')) {
            $declaredPath = [string](Get-JsonPropertyValue $officialRuntime ($bindingName + 'Path'))
            $declaredSha = [string](Get-JsonPropertyValue $officialRuntime ($bindingName + 'SHA256'))
            try {
                $resolvedBinding = Resolve-RepositoryPath $declaredPath
                if ((Get-PortablePath $resolvedBinding) -cne $declaredPath -or
                    -not (Test-Path -LiteralPath $resolvedBinding -PathType Leaf) -or
                    $declaredSha -cnotmatch '^[0-9A-F]{64}$' -or
                    (Get-Sha256 $resolvedBinding) -cne $declaredSha) {
                    $officialIssues.Add("official-runtime.$bindingName-binding")
                } elseif ($bindingName -ceq 'SemanticEvents' -and
                    [uint64]@(Get-Content -LiteralPath $resolvedBinding | Where-Object {
                        -not [string]::IsNullOrWhiteSpace($_)
                    }).Count -ne [uint64](Get-JsonPropertyValue $officialRuntime 'RuntimeEventCount')) {
                    $officialIssues.Add('official-runtime.SemanticEvents-count')
                }
            } catch {
                $officialIssues.Add("official-runtime.$bindingName-binding")
            }
        }

        # Launcher attestation intentionally records the exact local launcher,
        # client, and screenshot paths. Validate its JSON and outer hash without
        # applying the portable-text rule used by redistributable artifacts.
        $launcherChainBinding = $null
        try {
            $launcherChainPath = Resolve-RepositoryPath `
                (Get-JsonPropertyValue $officialRuntime 'LauncherClientChainPath')
            $launcherChainBytes = [IO.File]::ReadAllBytes($launcherChainPath)
            if ($launcherChainBytes.Length -ge 3 -and $launcherChainBytes[0] -eq 0xEF -and
                $launcherChainBytes[1] -eq 0xBB -and $launcherChainBytes[2] -eq 0xBF) {
                throw 'launcher chain contains a UTF-8 BOM'
            }
            $launcherChainRaw = [Text.UTF8Encoding]::new($false,$true).GetString($launcherChainBytes)
            $launcherChainStrict = Test-StrictJsonTextContract $launcherChainRaw
            if (-not $launcherChainStrict.Passed) {
                throw "launcher chain strict JSON failed: $($launcherChainStrict.Issues -join ',')"
            }
            $launcherChainBinding = [pscustomobject]@{
                Passed = $true
                Issues = @()
                Path = $launcherChainPath
                Value = ($launcherChainRaw | ConvertFrom-Json)
            }
        } catch {
            $launcherChainBinding = [pscustomobject]@{
                Passed = $false
                Issues = @($_.Exception.Message)
                Path = $null
                Value = $null
            }
        }
        $acquisitionManifestBinding = Read-BoundJsonArtifact 'official-contract-acquisition' `
            (Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionManifestPath') `
            (Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionManifestSHA256') $null $false $null
        $ringHealthBinding = Read-BoundJsonArtifact 'official-production-ring-health' `
            (Get-JsonPropertyValue $officialRuntime 'ProductionRingHealthPath') `
            (Get-JsonPropertyValue $officialRuntime 'ProductionRingHealthSHA256') $null $false $null
        foreach ($supplementalBinding in @($launcherChainBinding,$acquisitionManifestBinding,$ringHealthBinding)) {
            if ($null -eq $supplementalBinding -or -not [bool]$supplementalBinding.Passed) {
                $officialIssues.Add('official-runtime.supplemental-binding')
            }
        }
        if ($null -ne $launcherChainBinding -and $launcherChainBinding.Passed) {
            $chain = $launcherChainBinding.Value
            $chainKeys = @('SchemaId','SchemaVersion','GeneratedAtUtc','Passed','DirectClientLaunch',
                'LauncherPath','LauncherSHA256','LauncherProcessId','AgreementAccepted','AgreementEvidencePath',
                'StartGameClicked','StartGameClickedAtUtc','DirectSoundDialogObserved','DirectSoundDialogHandled',
                'DirectSoundDialogHandledAtUtc','DirectSoundDialogMethod','DirectSoundEvidencePath',
                'FormalAttachTimingAllowed','ClientPath','ClientSHA256','ClientVersion','ClientProcessId',
                'ClientParentProcessId','ClientProcessCreationTimeUtc','ParentIsVerifiedLauncher','HostProcessId',
                'HostSessionId','UserManualOperation')
            if (-not (Test-ExactJsonProperties $chain $chainKeys) -or
                (Get-JsonPropertyValue $chain 'SchemaId') -cne 'God2OfficialLauncherAutomationAttestation' -or
                [int](Get-JsonPropertyValue $chain 'SchemaVersion') -ne 1 -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'Passed') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'DirectClientLaunch') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'AgreementAccepted') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'StartGameClicked') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'DirectSoundDialogObserved') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'DirectSoundDialogHandled') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'FormalAttachTimingAllowed') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $chain 'ParentIsVerifiedLauncher') $true) -or
                [string](Get-JsonPropertyValue $chain 'LauncherSHA256') -cne $officialLauncherSha -or
                [uint32](Get-JsonPropertyValue $chain 'LauncherProcessId') -ne
                    [uint32](Get-JsonPropertyValue $officialRuntime 'LauncherProcessId') -or
                [uint32](Get-JsonPropertyValue $chain 'ClientProcessId') -ne
                    [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
                [uint32](Get-JsonPropertyValue $chain 'ClientParentProcessId') -ne
                    [uint32](Get-JsonPropertyValue $officialRuntime 'LauncherProcessId') -or
                [string](Get-JsonPropertyValue $chain 'ClientSHA256') -cne $script:God2OfficialSha256) {
                $officialIssues.Add('official-runtime.launcher-chain-contract')
            }
        }
        if ($null -ne $acquisitionManifestBinding -and $acquisitionManifestBinding.Passed) {
            $campaign = $acquisitionManifestBinding.Value
            $campaignKeys = @('SchemaVersion','GeneratedAtUtc','Authority','Mode','ValidationOnly',
                'ProductionHookInstalled','FundamentalDomainCount','CandidateCount','BatchCount','StartedAtUtc',
                'EndedAtUtc','ObserveSeconds','RuntimeObservationCount','RuntimeObservedDomainCount',
                'NotTriggeredInSessionCount','MemoryWritten','GameplayStateModified','ExtraNetworkTrafficGenerated',
                'DebuggerAttachCount','DebuggerDetachCount','AllBatchesAttached','AllBatchesDetached',
                'AllDebugRegistersCleared','ActivationAllowedCount','PromotionEligibleCount','Status','Batches',
                'Domains','Artifacts')
            $campaignAuthority = Get-JsonPropertyValue $campaign 'Authority'
            if (-not (Test-ExactJsonProperties $campaign $campaignKeys) -or
                (Get-JsonPropertyValue $campaign 'SchemaVersion') -cne
                    'god2-contract-acquisition-campaign-manifest-v1' -or
                (Get-JsonPropertyValue $campaignAuthority 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
                (Get-JsonPropertyValue $campaignAuthority 'SessionId') -cne
                    (Get-JsonPropertyValue $officialRuntime 'SessionId') -or
                [uint32](Get-JsonPropertyValue $campaignAuthority 'TargetProcessId') -ne
                    [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'ValidationOnly') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'ProductionHookInstalled') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'MemoryWritten') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'GameplayStateModified') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'ExtraNetworkTrafficGenerated') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'AllBatchesAttached') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'AllBatchesDetached') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $campaign 'AllDebugRegistersCleared') $true) -or
                [int](Get-JsonPropertyValue $campaign 'ObserveSeconds') -ne
                    [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionObserveSeconds') -or
                [int](Get-JsonPropertyValue $campaign 'CandidateCount') -ne
                    [int](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionCandidateCount') -or
                [uint64](Get-JsonPropertyValue $campaign 'RuntimeObservationCount') -ne
                    [uint64](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionRuntimeObservationCount') -or
                [int](Get-JsonPropertyValue $campaign 'ActivationAllowedCount') -ne 0 -or
                [int](Get-JsonPropertyValue $campaign 'PromotionEligibleCount') -ne 0 -or
                [string](Get-JsonPropertyValue $campaign 'Status') -cne
                    [string](Get-JsonPropertyValue $officialRuntime 'ContractAcquisitionStatus')) {
                $officialIssues.Add('official-runtime.contract-acquisition-contract')
            }
        }
        if ($null -ne $ringHealthBinding -and $ringHealthBinding.Passed) {
            $ringHealth = $ringHealthBinding.Value
            $ringKeys = @('SchemaVersion','GeneratedAtUtc','AuthorityProfile','SessionId','ClientProcessId',
                'ClientSHA256','SourceSchemaVersion','SourceHealthSHA256','Attempted','Accepted','Consumed',
                'DroppedP0','DroppedP1','DroppedP2','DroppedP3','WriteFailures','P0WriteFailures',
                'P1WriteFailures','SequenceGaps','FirstSequence','LastSequence','ConsumerLag','QueueHighWater',
                'PendingAfterDrain','SegmentCount','SemanticIncomplete','ProductionHealthBound','ServerReady',
                'DatabaseReady','Status','Passed')
            if (-not (Test-ExactJsonProperties $ringHealth $ringKeys) -or
                (Get-JsonPropertyValue $ringHealth 'SchemaVersion') -cne
                    'god2-shared-ring-production-health-v1' -or
                (Get-JsonPropertyValue $ringHealth 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
                (Get-JsonPropertyValue $ringHealth 'SessionId') -cne
                    (Get-JsonPropertyValue $officialRuntime 'SessionId') -or
                [uint32](Get-JsonPropertyValue $ringHealth 'ClientProcessId') -ne
                    [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
                [string](Get-JsonPropertyValue $ringHealth 'ClientSHA256') -cne $script:God2OfficialSha256 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'Attempted') -ne $runtimeEventCount -or
                [uint64](Get-JsonPropertyValue $ringHealth 'Accepted') -ne $runtimeEventCount -or
                [uint64](Get-JsonPropertyValue $ringHealth 'Consumed') -ne $runtimeEventCount -or
                [uint64](Get-JsonPropertyValue $ringHealth 'DroppedP0') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'DroppedP1') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'DroppedP2') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'DroppedP3') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'WriteFailures') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'SequenceGaps') -ne 0 -or
                [uint64](Get-JsonPropertyValue $ringHealth 'PendingAfterDrain') -ne 0 -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $ringHealth 'SemanticIncomplete') $false) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $ringHealth 'ProductionHealthBound') $true) -or
                -not (Test-JsonBooleanValue (Get-JsonPropertyValue $ringHealth 'Passed') $true) -or
                (Get-JsonPropertyValue $ringHealth 'Status') -cne 'PASS') {
                $officialIssues.Add('official-runtime.production-ring-contract')
            }
        }
    }
}

$officialAttachBinding = $null
$officialDetachBinding = $null
$officialNativeBinding = $null
$officialCandidateBinding = $null
$officialMap = $null
if ($null -ne $officialRuntime) {
    $officialAttachBinding = Read-BoundJsonArtifact 'official-attach' `
        (Get-JsonPropertyValue $officialRuntime 'AttachRecordPath') `
        (Get-JsonPropertyValue $officialRuntime 'AttachRecordSHA256') $null $false $null
    $officialDetachBinding = Read-BoundJsonArtifact 'official-detach' `
        (Get-JsonPropertyValue $officialRuntime 'DetachRecordPath') `
        (Get-JsonPropertyValue $officialRuntime 'DetachRecordSHA256') $null $false $null
    $officialNativeBinding = Read-BoundJsonArtifact 'official-native' `
        (Get-JsonPropertyValue $officialRuntime 'NativeReportPath') `
        (Get-JsonPropertyValue $officialRuntime 'NativeReportSHA256') $null $false $null
    $officialCandidateBinding = Read-BoundJsonArtifact 'official-candidate-map' `
        (Get-JsonPropertyValue $officialRuntime 'CandidateMapPath') `
        (Get-JsonPropertyValue $officialRuntime 'CandidateMapSHA256') $null $false $null
    foreach ($binding in @($officialAttachBinding,$officialDetachBinding,
            $officialNativeBinding,$officialCandidateBinding)) {
        if ($null -eq $binding -or -not [bool]$binding.Passed) {
            if ($null -ne $binding) {
                foreach ($issue in @($binding.Issues)) {
                    $officialIssues.Add("official-runtime.binding.$issue")
                }
            } else { $officialIssues.Add('official-runtime.binding.missing') }
        }
    }
}

$officialInjectorKeys = @(
    'schemaVersion','status','code','injectionAttempted','moduleWasEverLoaded',
    'moduleLoadStateVerified','moduleSnapshotVerified','moduleAbsent',
    'targetProcessExited','targetIdentityVerified','probeReady','stopSucceeded',
    'unloadSafe','moduleUnloaded','moduleResidentInactive','strictUnloadVerified',
    'cleanupRetriable','injectionMode','suspendedThreadCount','primaryThreadId',
    'extraReferenceRequested','extraReferenceLoaded',
    'extraReferenceNegativeFirstFreeLibraryStillPresent',
    'extraReferenceNegativeNotClaimedUnloaded',
    'extraReferenceReleasedThenModuleAbsent')
if ($null -ne $officialAttachBinding -and $officialAttachBinding.Passed -and
    $null -ne $officialDetachBinding -and $officialDetachBinding.Passed) {
    $officialAttach = $officialAttachBinding.Value
    $officialDetach = $officialDetachBinding.Value
    if ($observedOfficialSchemaVersion -eq 2) {
        $officialHeadlessAttachKeys = @('schemaVersion','status','code','targetProcessId',
            'sessionId','productionRingHost','sharedRingBound','observedAtUtc')
        $officialHeadlessDetachKeys = @('schemaVersion','status','code','targetProcessId',
            'strictUnloadVerified','moduleAbsent','targetAliveAfterDetach',
            'productionRingDrained','observedAtUtc')
        if (-not (Test-ExactJsonProperties $officialAttach $officialHeadlessAttachKeys) -or
            -not (Test-ExactJsonProperties $officialDetach $officialHeadlessDetachKeys)) {
            $officialIssues.Add('official-runtime.headless.exact-properties')
        }
        if ((Get-JsonPropertyValue $officialAttach 'schemaVersion') -cne
                'god2-official-headless-attach-v1' -or
            (Get-JsonPropertyValue $officialAttach 'status') -cne 'ATTACHED' -or
            [int](Get-JsonPropertyValue $officialAttach 'code') -ne 0 -or
            [uint32](Get-JsonPropertyValue $officialAttach 'targetProcessId') -ne
                [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
            (Get-JsonPropertyValue $officialAttach 'sessionId') -cne
                (Get-JsonPropertyValue $officialRuntime 'SessionId') -or
            (Get-JsonPropertyValue $officialAttach 'productionRingHost') -cne
                'God2SemanticRecoveryEngine.exe' -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialAttach 'sharedRingBound') $true)) {
            $officialIssues.Add('official-runtime.headless.attach-contract')
        }
        if ((Get-JsonPropertyValue $officialDetach 'schemaVersion') -cne
                'god2-official-headless-detach-v1' -or
            (Get-JsonPropertyValue $officialDetach 'status') -cne 'DETACHED' -or
            [int](Get-JsonPropertyValue $officialDetach 'code') -ne 0 -or
            [uint32](Get-JsonPropertyValue $officialDetach 'targetProcessId') -ne
                [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialDetach 'strictUnloadVerified') $true) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialDetach 'moduleAbsent') $true) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialDetach 'targetAliveAfterDetach') $true) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $officialDetach 'productionRingDrained') $true)) {
            $officialIssues.Add('official-runtime.headless.detach-contract')
        }
        try {
            $headlessAttachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialAttach 'observedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $headlessDetachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialDetach 'observedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $summaryAttachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'AttachObservedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            $summaryDetachUtc = [DateTimeOffset]::ParseExact(
                [string](Get-JsonPropertyValue $officialRuntime 'DetachObservedAtUtc'), 'o',
                [Globalization.CultureInfo]::InvariantCulture)
            if ($headlessAttachUtc -gt $headlessDetachUtc -or
                [Math]::Abs(($summaryAttachUtc - $headlessAttachUtc).TotalSeconds) -gt 5 -or
                [Math]::Abs(($summaryDetachUtc - $headlessDetachUtc).TotalSeconds) -gt 5) {
                $officialIssues.Add('official-runtime.headless.timestamp-binding')
            }
        } catch {
            $officialIssues.Add('official-runtime.headless.timestamp-binding')
        }
    } else {
        if (-not (Test-ExactJsonProperties $officialAttach $officialInjectorKeys) -or
            -not (Test-ExactJsonProperties $officialDetach $officialInjectorKeys)) {
            $officialIssues.Add('official-runtime.injector.exact-properties')
        }
        foreach ($record in @($officialAttach,$officialDetach)) {
            $recordInjectionAttempted = Test-JsonBooleanValue `
                (Get-JsonPropertyValue $record 'injectionAttempted') $true
            $recordEverLoaded = Test-JsonBooleanValue `
                (Get-JsonPropertyValue $record 'moduleWasEverLoaded') $true
            $recordLoadVerified = Test-JsonBooleanValue `
                (Get-JsonPropertyValue $record 'moduleLoadStateVerified') $true
            $recordIdentityVerified = Test-JsonBooleanValue `
                (Get-JsonPropertyValue $record 'targetIdentityVerified') $true
            $recordTargetAlive = Test-JsonBooleanValue `
                (Get-JsonPropertyValue $record 'targetProcessExited') $false
            if ((Get-JsonPropertyValue $record 'schemaVersion') -ne 2 -or
                (Get-JsonPropertyValue $record 'code') -ne 0 -or
                -not $recordInjectionAttempted -or -not $recordEverLoaded -or
                -not $recordLoadVerified -or -not $recordIdentityVerified -or
                -not $recordTargetAlive) {
                $officialIssues.Add('official-runtime.injector.common-state')
            }
        }
        $officialAttachReady = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialAttach 'probeReady') $true
        $officialAttachPresent = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialAttach 'moduleAbsent') $false
        $officialDetachStopped = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialDetach 'stopSucceeded') $true
        $officialDetachSafe = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialDetach 'unloadSafe') $true
        $officialDetachUnloaded = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialDetach 'moduleUnloaded') $true
        $officialDetachAbsent = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialDetach 'moduleAbsent') $true
        $officialDetachStrict = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $officialDetach 'strictUnloadVerified') $true
        if ((Get-JsonPropertyValue $officialAttach 'status') -cne 'ATTACHED' -or
            -not $officialAttachReady -or -not $officialAttachPresent -or
            (Get-JsonPropertyValue $officialDetach 'status') -cne 'DETACHED' -or
            -not $officialDetachStopped -or -not $officialDetachSafe -or
            -not $officialDetachUnloaded -or -not $officialDetachAbsent -or
            -not $officialDetachStrict) {
            $officialIssues.Add('official-runtime.injector.lifecycle')
        }
    }
}

if ($null -ne $officialCandidateBinding -and $officialCandidateBinding.Passed) {
    $officialMap = $officialCandidateBinding.Value
    $officialMapKeys = @('SchemaVersion','ClientSHA256','ClientSha256Expected',
        'IdentityProfile','Architecture','DiscoveryPolicy',
        'CandidateVerificationEngine','UnconfirmedProbePolicy',
        'CandidateSchemaVersion','TargetIdentityVerified','EngineBounds',
        'DomainCount','Domains')
    if (-not (Test-ExactJsonProperties $officialMap $officialMapKeys) -or
        (Get-JsonPropertyValue $officialMap 'SchemaVersion') -cne
            'god2-deep-probe-candidate-map-v2' -or
        (Get-JsonPropertyValue $officialMap 'DomainCount') -ne 25 -or
        (Get-JsonPropertyValue $officialMap 'ClientSHA256') -cne
            '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B' -or
        (Get-JsonPropertyValue $officialMap 'ClientSha256Expected') -cne
            '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B') {
        $officialIssues.Add('official-runtime.candidate.root')
    }
    $officialIdentityVerified = Test-JsonBooleanValue `
        (Get-JsonPropertyValue $officialMap 'TargetIdentityVerified') $true
    if ((Get-JsonPropertyValue $officialMap 'IdentityProfile') -cne
            'OfficialExactClient' -or
        -not $officialIdentityVerified) {
        $officialIssues.Add('official-runtime.candidate.identity')
    }
    $officialDomains = @(Get-JsonPropertyValue $officialMap 'Domains')
    $expectedOfficialDomains = @('Network','Parser','Serializer','Handler')
    for ($index = 0; $index -lt 4; ++$index) {
        if ($officialDomains.Count -le $index -or
            (Get-JsonPropertyValue $officialDomains[$index] 'Domain') -cne
                $expectedOfficialDomains[$index] -or
            (Get-JsonPropertyValue $officialDomains[$index] 'Status') -cne 'Active' -or
            (Get-JsonPropertyValue $officialDomains[$index] 'VerificationStatus') -cne
                'PASS') {
            $officialIssues.Add("official-runtime.candidate.domain-$index")
        }
    }
    for ($index = 4; $index -lt 25; ++$index) {
        $row = $officialDomains[$index]
        $verification = Get-JsonPropertyValue $row 'VerificationContract'
        $candidates = @(Get-JsonPropertyValue $row 'Candidates')
        $typedRuntimeBlocked = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $verification 'TypedRuntimeEvidence') $false
        $causalBlocked = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $verification 'RepeatedCausalObservation') $false
        $callingConventionBlocked = Test-JsonBooleanValue `
            (Get-JsonPropertyValue $verification 'CallingConventionVerified') $false
        if ((Get-JsonPropertyValue $row 'Status') -cne
                'EvidenceBlockedUnconfirmedProbe' -or
            (Get-JsonPropertyValue $row 'Authority') -cne 'UNKNOWN' -or
            (Get-JsonPropertyValue $row 'CandidateCount') -ne $candidates.Count -or
            -not $typedRuntimeBlocked -or -not $causalBlocked -or
            -not $callingConventionBlocked) {
            $officialIssues.Add("official-runtime.candidate.blocked-$index")
        }
    }
}

if ($null -ne $officialNativeBinding -and $officialNativeBinding.Passed) {
    $officialBridge = Get-JsonPropertyValue $officialNativeBinding.Value 'UnloadNeutralBridge'
    $coverage = @(Get-JsonPropertyValue $officialBridge 'BridgeCoverageResults')
    foreach ($requiredIndex in @(0,1,2,3,7,8,9,10)) {
        $row = @($coverage | Where-Object {
            (Get-JsonPropertyValue $_ 'Index') -eq $requiredIndex })
        $rowInstalled = $row.Count -eq 1 -and
            (Test-JsonBooleanValue `
                (Get-JsonPropertyValue $row[0] 'InstalledToBridge') $true)
        if (-not $rowInstalled) {
            $officialIssues.Add("official-runtime.bridge.index-$requiredIndex")
        }
    }
    $officialDllPointersClear = Test-JsonBooleanValue `
        (Get-JsonPropertyValue $officialBridge 'DllPointersRemaining') $false
    $officialIatRestored = Test-JsonBooleanValue `
        (Get-JsonPropertyValue $officialBridge 'IatRestored') $true
    $officialHotpatchRestored = Test-JsonBooleanValue `
        (Get-JsonPropertyValue $officialBridge 'HotpatchRestored') $true
    if ((Get-JsonPropertyValue $officialBridge 'Phase') -cne 'Detached' -or
        -not $officialDllPointersClear -or -not $officialIatRestored -or
        -not $officialHotpatchRestored) {
        $officialIssues.Add('official-runtime.bridge.detach-state')
    }
}

$officialRuntimeObserved = $officialIssues.Count -eq 0
Add-Check 'readiness.extended-ready-derived-coverage-official-client' `
    $officialRuntimeObserved 'DLL' `
    "Exact God2_opt.exe runtime binds ATTACHED/DETACHED records, official candidate map, native bridge ledger, immediate DirectSound dialog acknowledgement, four Winsock entries and Parser/Serializer/Handler Active/PASS; issues=$($officialIssues -join ',')."

$productSessionId = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $ProductSelfTestSummary))
$productAuthority = New-God2EvidenceAuthorityRecord `
    -AuthorityProfile ExactSelfTestClient `
    -SessionId $productSessionId `
    -SourceArtifact (Get-PortablePath $ProductSelfTestSummary) `
    -SourceSHA256 (Get-Sha256 $ProductSelfTestSummary) `
    -TargetExecutable 'God2TraceSelfTestClient.exe' `
    -TargetVersion ([string](Get-JsonPropertyValue $product 'PositiveLifecycleClientVersion')) `
    -TargetSHA256 ([string](Get-JsonPropertyValue $product 'PositiveLifecycleClientSHA256')) `
    -TargetProcessId ([uint32](Get-JsonPropertyValue $product 'TargetProcessId')) `
    -TargetProcessCreationTime ([string](Get-JsonPropertyValue $product 'PositiveLifecycleProcessCreationTime')) `
    -CurrentSessionObserved $false -HistoricalEvidenceReused $false `
    -FixtureOnly $true -PromotionEligible $false `
    -AttachStatus 'ATTACHED' -DetachStatus 'DETACHED' `
    -RuntimeEventCount ([uint64](Get-JsonPropertyValue $product 'NativeSemanticWireCount'))

$officialSchemaVersion = if ($null -eq $officialRuntime) { 0 } else {
    [int](Get-JsonPropertyValue $officialRuntime 'SchemaVersion')
}
$declaredOfficialProfile = if ($officialSchemaVersion -ge 2) {
    [string](Get-JsonPropertyValue $officialRuntime 'AuthorityProfile')
} else { 'HistoricalOfficialClientLive' }
$officialIsCurrent = $declaredOfficialProfile -ceq 'CurrentOfficialClientLive' -and
    (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'CurrentSessionObserved') $true)
$officialSessionId = if ($officialSchemaVersion -ge 2) {
    [string](Get-JsonPropertyValue $officialRuntime 'SessionId')
} else {
    Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $OfficialRuntimeEvidence))
}
$officialSemanticPath = Join-Path (Split-Path -Parent (Split-Path -Parent $OfficialRuntimeEvidence)) `
    'sensitive\semantic-events.jsonl'
$officialRuntimeEventCount = if ($officialSchemaVersion -ge 2) {
    [uint64](Get-JsonPropertyValue $officialRuntime 'RuntimeEventCount')
} elseif (Test-Path -LiteralPath $officialSemanticPath -PathType Leaf) {
    [uint64]@(Get-Content -LiteralPath $officialSemanticPath).Count
} else { [uint64]0 }
$officialPackageSha = if ($officialSchemaVersion -ge 2) {
    [string](Get-JsonPropertyValue $officialRuntime 'ResultPackageSHA256')
} else { $null }
$officialPromotionEligible = $officialSchemaVersion -ge 2 -and
    (Test-JsonBooleanValue (Get-JsonPropertyValue $officialRuntime 'PromotionEligible') $true)
$officialAuthority = New-God2EvidenceAuthorityRecord `
    -AuthorityProfile $(if ($officialIsCurrent) { 'CurrentOfficialClientLive' } else { 'HistoricalOfficialClientLive' }) `
    -SessionId $officialSessionId `
    -SourceArtifact (Get-PortablePath $OfficialRuntimeEvidence) `
    -SourceSHA256 (Get-Sha256 $OfficialRuntimeEvidence) `
    -TargetExecutable 'God2_opt.exe' -TargetVersion '1.0.0.1' `
    -TargetSHA256 $script:God2OfficialSha256 `
    -TargetProcessId ([uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId')) `
    -TargetProcessCreationTime $(if ($officialSchemaVersion -ge 2) {
        [string](Get-JsonPropertyValue $officialRuntime 'ClientProcessCreationTime')
    } else { $null }) `
    -CurrentSessionObserved ([bool]$officialIsCurrent) `
    -HistoricalEvidenceReused (-not [bool]$officialIsCurrent) `
    -FixtureOnly $false -PromotionEligible ([bool]$officialPromotionEligible) `
    -AttachStatus ([string](Get-JsonPropertyValue $officialRuntime 'AttachStatus')) `
    -DetachStatus ([string](Get-JsonPropertyValue $officialRuntime 'DetachStatus')) `
    -PackageSHA256 $officialPackageSha -RuntimeEventCount $officialRuntimeEventCount

$authorityConsistencyReport = New-God2AuthorityConsistencyReport `
    -Records @($productAuthority,$officialAuthority)
$authorityConsistencyPath = Join-Path $OutputRoot 'authority-consistency-report.json'
$authorityUtf8 = [Text.UTF8Encoding]::new($false, $true)
[IO.File]::WriteAllText($authorityConsistencyPath,
    (($authorityConsistencyReport | ConvertTo-Json -Depth 8) + "`n"), $authorityUtf8)
$authorityConsistencySha256 = Get-Sha256 $authorityConsistencyPath
$authorityConsistencyPassed = $authorityConsistencyReport.Status -ceq 'PASS'
Add-Check 'authority.profile-consistency-current-historical-fixture-separated' `
    $authorityConsistencyPassed 'DLL' `
    "Evidence authority profiles are explicit and contradictions fail closed; report=$(Get-PortablePath $authorityConsistencyPath); conflicts=$($authorityConsistencyReport.ConflictCount)."

$officialSemanticEvents = [Collections.Generic.List[object]]::new()
$deepEvidenceIssues = [Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $officialSemanticPath -PathType Leaf) {
    $semanticLineNumber = 0
    foreach ($semanticLine in Get-Content -LiteralPath $officialSemanticPath) {
        ++$semanticLineNumber
        if ([string]::IsNullOrWhiteSpace($semanticLine)) { continue }
        try {
            $strictSemanticLine = Test-StrictJsonTextContract $semanticLine
            if (-not $strictSemanticLine.Passed) {
                throw "Strict semantic JSON failed: $($strictSemanticLine.Issues -join ',')"
            }
            [void]$officialSemanticEvents.Add(
                (ConvertFrom-God2SemanticEventJson -Json $semanticLine))
        } catch {
            $deepEvidenceIssues.Add("official-semantic-line-$semanticLineNumber")
        }
    }
} else {
    $deepEvidenceIssues.Add('official-semantic-events-missing')
}
$deepRecoveryEvidence = $null
$deepCandidateMap = $officialMap
$deepStaticMapUsed = $false
if (-not [string]::IsNullOrWhiteSpace($DeepStaticCandidateMapV3)) {
    try {
        $deepStaticMapPath = Resolve-RepositoryPath $DeepStaticCandidateMapV3
        $deepStaticRaw = [Text.UTF8Encoding]::new($false,$true).GetString(
            [IO.File]::ReadAllBytes($deepStaticMapPath))
        $deepStaticStrict = Test-StrictJsonTextContract $deepStaticRaw
        if (-not $deepStaticStrict.Passed) {
            foreach ($issue in @($deepStaticStrict.Issues)) {
                $deepEvidenceIssues.Add("static-v3.json.$issue")
            }
        }
        $deepStaticMap = $deepStaticRaw | ConvertFrom-Json
        $deepStaticAuthority = Get-JsonPropertyValue $deepStaticMap 'Authority'
        $deepStaticBounds = Get-JsonPropertyValue $deepStaticMap 'AnalysisBounds'
        $deepStaticCandidates = @(Get-JsonPropertyValue $deepStaticMap 'Candidates')
        $officialRuntimeAnalysisRoot = Split-Path -Parent $OfficialRuntimeEvidence
        $officialRuntimeSessionRoot = Split-Path -Parent $officialRuntimeAnalysisRoot
        $deepStaticSessionRoot = [IO.Path]::GetFullPath(
            $officialRuntimeSessionRoot).TrimEnd('\')
        $deepStaticActualPath = [IO.Path]::GetFullPath($deepStaticMapPath)
        $seedFunctions = @(Get-JsonPropertyValue $deepStaticMap 'SeedFunctions')
        $staticDomainCount = @($deepStaticCandidates | Group-Object {
            [string](Get-JsonPropertyValue $_ 'Domain')
        }).Count
        $staticAllBlocked = @($deepStaticCandidates | Where-Object {
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $_ 'ActivationAllowed') $false) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $_ 'PromotionEligible') $false)
        }).Count -eq 0
        $staticSeedsVerified = $seedFunctions.Count -eq 3 -and
            @($seedFunctions[0].ExactCallsitesVerified).Count -eq 1 -and
            [string]$seedFunctions[1].Bytes -cmatch '^55 8B EC 56 57' -and
            @($seedFunctions[2].ExactCallsitesVerified).Count -eq 2
        if ((Get-JsonPropertyValue $deepStaticMap 'SchemaVersion') -cne
                'god2-deep-probe-candidate-map-v3' -or
            (Get-JsonPropertyValue $deepStaticMap 'TargetSHA256') -cne
                $script:God2OfficialSha256 -or
            (Get-JsonPropertyValue $deepStaticAuthority 'AuthorityProfile') -cne
                'ExactBinaryStaticAnalysis' -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $deepStaticAuthority 'CurrentSessionObserved') $false) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $deepStaticAuthority 'PromotionEligible') $false) -or
            [uint32](Get-JsonPropertyValue $deepStaticAuthority 'TargetProcessId') -ne
                [uint32](Get-JsonPropertyValue $officialRuntime 'ClientProcessId') -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $deepStaticBounds 'RuntimeImageObserved') $true) -or
            -not (Test-JsonBooleanValue (Get-JsonPropertyValue $deepStaticBounds 'MemoryWritten') $false) -or
            [int](Get-JsonPropertyValue $deepStaticMap 'CandidateCount') -ne
                $deepStaticCandidates.Count -or $deepStaticCandidates.Count -lt 21 -or
            [int](Get-JsonPropertyValue $deepStaticMap 'ActivationAllowedCount') -ne 0 -or
            $staticDomainCount -ne 21 -or -not $staticAllBlocked -or
            -not $staticSeedsVerified -or
            -not $deepStaticActualPath.StartsWith($deepStaticSessionRoot + '\',
                [StringComparison]::OrdinalIgnoreCase)) {
            $deepEvidenceIssues.Add('static-v3.contract-or-session-binding')
        } else {
            $deepCandidateMap = $deepStaticMap
            $deepStaticMapUsed = $true
        }
    } catch {
        $deepEvidenceIssues.Add("static-v3.read:$($_.Exception.Message)")
    }
} elseif ($Final) {
    $deepEvidenceIssues.Add('static-v3.required-for-final')
}
if ($null -eq $deepCandidateMap) {
    $deepEvidenceIssues.Add('official-candidate-map-unavailable')
} elseif ($deepEvidenceIssues.Count -eq 0) {
    try {
        $deepRecoveryEvidence = New-God2DeepRecoveryEvidence `
            -CandidateMap $deepCandidateMap `
            -SemanticEvents @($officialSemanticEvents) `
            -AuthorityRecord $officialAuthority `
            -OutputRoot $OutputRoot `
            -CurrentExecutableSHA256 $releaseIdentity.sha256
    } catch {
        $deepEvidenceIssues.Add("generation:$($_.Exception.Message)")
    }
}
$deepCapabilityPassed = $null -ne $deepRecoveryEvidence -and
    $deepRecoveryEvidence.Capability.DomainCount -eq 25 -and
    $deepRecoveryEvidence.Capability.Rows.Count -eq 25 -and
    $deepRecoveryEvidence.Capability.ConfirmedContractCount -eq 4 -and
    $deepRecoveryEvidence.Capability.ImplementedCount -eq 25 -and
    $deepRecoveryEvidence.Capability.ActivationAllowedCount -eq 4 -and
    $deepRecoveryEvidence.Capability.ProducerImplementedCount -eq 25 -and
    $deepRecoveryEvidence.Capability.DeepFundamentalProducerImplementedCount -eq 11 -and
    $deepRecoveryEvidence.Capability.GameplayAdapterImplementedCount -eq 10 -and
    $deepRecoveryEvidence.ContentDomainSummary.AdapterImplementedCount -eq 10
$deepRuntimePassed = $null -ne $deepRecoveryEvidence -and
    $deepRecoveryEvidence.Runtime.DomainCount -eq 25 -and
    $deepRecoveryEvidence.Runtime.Rows.Count -eq 25 -and
    $deepRecoveryEvidence.Runtime.BlockedCount -eq 25 -and
    $deepRecoveryEvidence.Runtime.VerifiedDeepDomainCount -eq 0 -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveProducerCount -eq
        $deepRecoveryEvidence.Semantic.CurrentOfficialLiveProducerCount -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveVerifiedProducerCount -eq
        $deepRecoveryEvidence.Semantic.CurrentOfficialLiveVerifiedProducerCount -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveDeepProducerCount -eq
        $deepRecoveryEvidence.Semantic.CurrentOfficialLiveDeepProducerCount -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveVerifiedDeepProducerCount -eq
        $deepRecoveryEvidence.Semantic.CurrentOfficialLiveVerifiedDeepProducerCount -and
    [bool]$deepRecoveryEvidence.Runtime.ProducerMetricConsistency -and
    [bool]$deepRecoveryEvidence.Semantic.ProducerMetricConsistency -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveDeepProducerCount -le
        $deepRecoveryEvidence.Runtime.CurrentOfficialLiveProducerCount -and
    $deepRecoveryEvidence.Runtime.CurrentOfficialLiveVerifiedDeepProducerCount -le
        $deepRecoveryEvidence.Runtime.CurrentOfficialLiveDeepProducerCount
$deepPromotionPassed = $null -ne $deepRecoveryEvidence -and
    $deepRecoveryEvidence.CandidateMapV3.CandidateCount -gt 0 -and
    $deepRecoveryEvidence.PromotionLedgerCount -eq
        ($deepRecoveryEvidence.CandidateMapV3.CandidateCount * 14) -and
    $deepRecoveryEvidence.PromotionResult.ActivationAllowedCount -eq 0 -and
    $deepRecoveryEvidence.PromotionResult.EvidenceBlockedGateRowCount -gt 0 -and
    (-not $Final -or $deepStaticMapUsed) -and
    (-not $deepStaticMapUsed -or
        $deepRecoveryEvidence.CandidateMapV3.Authority.AuthorityProfile -ceq
            'ExactBinaryStaticAnalysis')
$deepAuthorityPassed = $null -ne $deepRecoveryEvidence -and
    $deepRecoveryEvidence.Semantic.PromotionEligibleCount -eq 0 -and
    $deepRecoveryEvidence.RecoveryReadiness.ServerReady -eq $false -and
    $deepRecoveryEvidence.RecoveryReadiness.DatabaseReady -eq $false
Add-Check 'domains.capability-runtime-authority-separated' $deepCapabilityPassed 'DLL' `
    "Capability matrix is 25 implemented producers/adapters, including 11 deep fundamentals and 10 gameplay adapters, while exact contracts/activations remain evidence-bound; issues=$($deepEvidenceIssues -join ',')."
Add-Check 'domains.current-official-runtime-not-inferred' $deepRuntimePassed 'DLL' `
    "Historical candidate diagnostics leave 25 runtime rows evidence-blocked and zero verified; issues=$($deepEvidenceIssues -join ',')."
Add-Check 'candidate.deep-v3-fourteen-gate-ledger-fail-closed' $deepPromotionPassed 'DLL' `
    "Every deep candidate has exactly 14 promotion/ABI rows and any unknown gate leaves ActivationAllowed=false; issues=$($deepEvidenceIssues -join ',')."
Add-Check 'recovery.readiness-requires-live-producer-and-ring-health' $deepAuthorityPassed 'DLL' `
    "Historical/diagnostic evidence cannot become promotion, Server-ready, or Database-ready; issues=$($deepEvidenceIssues -join ',')."
Add-Check 'hooks.nonblocking-lock-contention-runtime' $bridgeRuntimePassed 'DLL' "Four private lock-contention cases are source-bound inside the bridge report and the external blocking matrix proves recv/WSAGet/GQCS/GQCSEx stayed outside the DLL with exact resource/result/error semantics through unload; issues=$productIssueText."
Add-Check 'lifecycle.actual-restore-fault-exported-state-machine' $restoreFaultContractPassed 'DLL' "The separately bound exact-owner lifecycle proves first Stop/CanUnload rejection while resident, zero FreeLibrary before retry, authorized retry, one release token/thread, two complete absence snapshots and live target; issues=$($restoreFaultIssues -join ',')."
$ringNullPassed = $nativeBindingValid -and (Test-ContractIssueGroup $productContract @(
    'product.NativeProbeSelfTestReport.native.HookThreadRingNull','product.NativeProbeSelfTestReport.native.AsyncDiagnosticPrivateNonAliasing'))
Add-Check 'diagnostics.ring-null-private-live-state-isolation' $ringNullPassed 'DLL' "Private non-aliased formatted-line and schema single/multi sinks prove formatted 1/2, durable 0/0, loss 1/2, file IO 0 and live state untouched; issues=$productIssueText."
$lifecycleTokens = @(
    'canFinalizeShutdown','canUnloadProbeState','importCapacityFixture[128]',
    'importSlots[129]','g_restoreFaultInjection','injectedRestoreFailed','restoreRetrySucceeded',
    'g_bridgeCompletionDuplicateBeforeReusePassed','g_bridgeCompletionDeterministicAbaPassed',
    'runBridgeWSARecvCommitStageSelfTest','runIatOwnerLifecycleSelfTest','waitForActiveHooks','g_pendingRecvCount',
    'restoreComplete','God2TraceProbeStop','God2TraceProbeCanUnload'
)
$missingLifecycleTokens = @($lifecycleTokens | Where-Object { $probeSource.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
Add-Check 'lifecycle.iat-overlapped-reentrant-stop-unload' (
    $missingLifecycleTokens.Count -eq 0 -and $networkCasesValid -and $productValid
) 'DLL' "Missing lifecycle assertions: $($missingLifecycleTokens -join ','); the native report must prove IAT owner retirement, address-reuse safety, replacement CAS restoration, and original/third-party non-clobbering in addition to capacity, restore retry, blocking/reentrant completion, Stop and CanUnload."

$builtOutputsExact = (Test-Path -LiteralPath $builtProbePath -PathType Leaf) -and
    (Test-Path -LiteralPath $builtInjectorPath -PathType Leaf) -and
    (Get-Sha256 $builtProbePath) -eq $probeStandalone.sha256 -and
    (Get-Sha256 $builtInjectorPath) -eq $injectorStandalone.sha256
Add-Check 'payload.latest-build-binding' $builtOutputsExact 'DLL' 'Standalone payloads byte-match the latest x86 Release build outputs used by the fresh product/network self-tests.'

$privacyRoots = @(
    (Split-Path -Parent (Split-Path -Parent $NetworkSelfTestSummary)),
    (Split-Path -Parent (Split-Path -Parent $ProductSelfTestSummary))
)
$privacyNeedles = @(
    'ULTIMATE_ACCOUNT_AUTH_CANARY_20260808', 'ULTIMATE_ENCRYPTED_AUTH_CANARY',
    'SECRET-CANARY', 'ACCOUNT-CANARY', 'key256="', 'rawauth=', 'password='
)
$privacyLeaks = New-Object Collections.ArrayList
foreach ($privacyRoot in $privacyRoots) {
    if (-not (Test-Path -LiteralPath $privacyRoot -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $privacyRoot -Recurse -File -ErrorAction SilentlyContinue) {
        if ($file.Extension -match '^\.(exe|dll|pdb|lib|obj|zip)$') { continue }
        if ($file.Length -gt 32MB) { continue }
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        foreach ($needle in $privacyNeedles) {
            if ($text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                [void]$privacyLeaks.Add("$(Get-PortablePath $file.FullName):$needle")
            }
        }
    }
}
Add-Check 'privacy.no-auth-persistence' ($privacyLeaks.Count -eq 0) 'DLL' "Generated non-PE self-test artifacts scanned for authentication canaries/credential markers; leaks=$($privacyLeaks -join ';')."

$freshRoot = Join-Path $OutputRoot 't'
New-Item -ItemType Directory -Path $freshRoot | Out-Null
$semanticReportPath = Join-Path $freshRoot 's.json'
$ultimateReportPath = Join-Path $freshRoot 'u.json'
$ultimateArtifactsPath = Join-Path $freshRoot 'a'

function Invoke-HiddenProcess([string]$FilePath, [string[]]$Arguments) {
    $quotedArguments = @($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' '
    $process = Start-Process -FilePath $FilePath -ArgumentList $quotedArguments -PassThru -WindowStyle Hidden
    $process.WaitForExit()
    return [int]$process.ExitCode
}
$semanticExit = Invoke-HiddenProcess $ReleaseExecutable @('--internal-semantic-selftest','--report',$semanticReportPath)
$ultimateExit = if ($null -ne $nativeSemanticWirePath) {
    Invoke-HiddenProcess $ReleaseExecutable @(
        '--internal-ultimate-selftest','--report',$ultimateReportPath,'--artifacts',$ultimateArtifactsPath,
        '--native-semantic-wire',$nativeSemanticWirePath)
} else { -1 }
$semantic = if (Test-Path -LiteralPath $semanticReportPath) {
    Get-Content -LiteralPath $semanticReportPath -Raw | ConvertFrom-Json
} else { $null }
$ultimate = if (Test-Path -LiteralPath $ultimateReportPath) {
    Get-Content -LiteralPath $ultimateReportPath -Raw | ConvertFrom-Json
} else { $null }
$canonicalV1WirePath = Join-Path $ultimateArtifactsPath 'canonical-v1-semantic-wire.jsonl'
$canonicalV1BindingPath = Join-Path $ultimateArtifactsPath 'canonical-v1-semantic-wire.binding.json'
$compatibilityV1InputPath = Join-Path $ultimateArtifactsPath 'noncanonical-v1-compatibility-input.json'
$semanticV1ArtifactsContract = Test-SemanticV1SelfTestArtifacts `
    $canonicalV1WirePath $canonicalV1BindingPath $compatibilityV1InputPath
$semanticValid = $null -ne $semantic -and $semanticExit -eq 0 -and
    [int]$semantic.CheckCount -ge 29 -and [int]$semantic.Failed -eq 0 -and
    [int]$semantic.Passed -eq [int]$semantic.CheckCount -and
    @($semantic.Checks | Where-Object { $_.Status -cne 'PASS' }).Count -eq 0
Add-Check 'engine.semantic-v2-fresh-selftest' $semanticValid 'RELEASE_EXE' "exit=$semanticExit, total=$(if($null -ne $semantic){$semantic.CheckCount}else{0}), failed=$(if($null -ne $semantic){$semantic.Failed}else{-1})."

$hardGateRuntime = $null -ne $ultimate -and
    @($ultimate.tests | Where-Object { $_.name -ceq '06b Deep-probe candidate promotion hard gate' -and $_.status -ceq 'PASS' }).Count -eq 1
$ultimateContract = Test-UltimateRuntimeContract $ultimate $ultimateExit
$freshUltimateNativeBinding = Get-JsonPropertyValue `
    (Get-JsonPropertyValue $ultimate 'semanticEventMatrix') 'nativeWireBinding'
$freshUltimateWireBound = $null -ne $freshUltimateNativeBinding -and
    (Get-JsonPropertyValue $freshUltimateNativeBinding 'inputSHA256') -ceq
        (Get-JsonPropertyValue $product 'NativeSemanticWireSHA256') -and
    (Get-JsonPropertyValue $freshUltimateNativeBinding 'eventTypeDigest') -ceq
        (Get-JsonPropertyValue $product 'SemanticEventTypeDigestSHA256')
$ultimateValid = [bool]$ultimateContract.Passed -and $hardGateRuntime -and
    $freshUltimateWireBound -and [bool]$semanticV1ArtifactsContract.Passed
Add-Check 'engine.semantic-event-25-type-matrix' (Test-ContractIssueGroup $ultimateContract @('ultimate.semanticEventMatrix')) 'RELEASE_EXE' "Fresh final EXE directly binds the original 25-line DLL native wire by SHA/digest, reports ProducerImplementedCount=25, NativeSelfTestProducerCount=25, and blockedCapability=20, plus strict roundtrip/corruption/version/v1 compatibility/no-promotion=2; issues=$($ultimateContract.Issues -join ',')."
Add-Check 'engine.semantic-v1-profile-artifact-binding' ([bool]$semanticV1ArtifactsContract.Passed) 'RELEASE_EXE' "Fresh final EXE atomically emitted a 25-line canonical-v1 writer artifact with exact14 rows, SHA-256 and event-type digest, plus a separately labelled fixture-only NonCanonical/NonPromotable compatibility input; issues=$($semanticV1ArtifactsContract.Issues -join ',')."
Add-Check 'engine.ultimate-fresh-selftest-with-06b' $ultimateValid 'RELEASE_EXE' "exit=$ultimateExit, total=$(if($null -ne $ultimate){Get-JsonPropertyValue $ultimate 'total'}else{0}), failed=$(if($null -ne $ultimate){Get-JsonPropertyValue $ultimate 'failed'}else{-1}), hardGate06b=$hardGateRuntime, nativeWireBound=$freshUltimateWireBound, issues=$($ultimateContract.Issues -join ',')."

$releaseItem = Get-Item -LiteralPath $ReleaseExecutable
$releaseInputs = @($requiredSourcePaths + @($ProbePayload, $InjectorPayload, $builtProbePath, $builtInjectorPath))
$newestInputUtc = ($releaseInputs | ForEach-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } |
    Sort-Object -Descending | Select-Object -First 1)
$releaseFresh = $releaseItem.LastWriteTimeUtc -ge $newestInputUtc
$evidenceFixtureTestedAtUtc = ([DateTimeOffset]::Parse(
        [string](Get-JsonPropertyValue $evidenceFixture 'TestedAtUtc'),
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal)).UtcDateTime
$evidenceFixtureFresh = $evidenceFixtureContract.Passed -and
    $evidenceFixtureItem.LastWriteTimeUtc -ge $releaseItem.LastWriteTimeUtc -and
    $evidenceFixtureTestedAtUtc -ge $releaseItem.LastWriteTimeUtc
$networkFreshInputs = @(
    $ProbePayload, $builtProbePath, $ringHeaderPath, $probeSourcePath,
    $selfTestClientPath, $networkSelfTestScriptPath, $acceptanceScriptPath, $acceptanceSchemaPath
)
$productFreshInputs = @(
    $ProbePayload, $InjectorPayload, $builtProbePath, $builtInjectorPath, $ringHeaderPath,
    $probeSourcePath, $injectorSourcePath, $selfTestClientPath, $productSelfTestScriptPath,
    $acceptanceScriptPath, $acceptanceSchemaPath
)
$newestNetworkInputUtc = ($networkFreshInputs | ForEach-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } |
    Sort-Object -Descending | Select-Object -First 1)
$newestProductInputUtc = ($productFreshInputs | ForEach-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } |
    Sort-Object -Descending | Select-Object -First 1)
$networkFresh = $networkItem.LastWriteTimeUtc -ge $newestNetworkInputUtc
$productItem = Get-Item -LiteralPath $ProductSelfTestSummary
$nativeEvidenceItem = $nativeEvidenceBinding.Item
$nativeEvidenceFresh = $null -ne $nativeEvidenceItem -and
    $nativeEvidenceItem.LastWriteTimeUtc -ge $newestProductInputUtc -and
    $productItem.LastWriteTimeUtc -ge $nativeEvidenceItem.LastWriteTimeUtc
$productArtifactItems = @(
    $attachBinding.Item,$detachBinding.Item,$identityBinding.Item,$nativeEvidenceBinding.Item,
    $nativeWireBinding.Item,$wireVerifierBinding.Item,$ultimateWireBinding.Item,$candidateMapBinding.Item
) | Where-Object { $null -ne $_ }
$productArtifactFresh = $productArtifactItems.Count -eq 8 -and
    @($productArtifactItems | Where-Object {
        $_.LastWriteTimeUtc -lt $newestProductInputUtc -or
        $_.LastWriteTimeUtc -gt $productItem.LastWriteTimeUtc
    }).Count -eq 0
$productFresh = $productItem.LastWriteTimeUtc -ge $newestProductInputUtc -and
    $nativeEvidenceFresh -and $productArtifactFresh
$semanticItem = if (Test-Path -LiteralPath $semanticReportPath -PathType Leaf) { Get-Item -LiteralPath $semanticReportPath } else { $null }
$ultimateItem = if (Test-Path -LiteralPath $ultimateReportPath -PathType Leaf) { Get-Item -LiteralPath $ultimateReportPath } else { $null }
$semanticV1ArtifactItems = @(
    $canonicalV1WirePath,$canonicalV1BindingPath,$compatibilityV1InputPath |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    ForEach-Object { Get-Item -LiteralPath $_ })
$freshExecutableSelfTests = $null -ne $semanticItem -and $null -ne $ultimateItem -and
    $semanticItem.LastWriteTimeUtc -ge $releaseItem.LastWriteTimeUtc -and
    $ultimateItem.LastWriteTimeUtc -ge $releaseItem.LastWriteTimeUtc -and
    $semanticV1ArtifactItems.Count -eq 3 -and
    @($semanticV1ArtifactItems | Where-Object {
        $_.LastWriteTimeUtc -lt $releaseItem.LastWriteTimeUtc -or
        $_.LastWriteTimeUtc -gt $ultimateItem.LastWriteTimeUtc
    }).Count -eq 0
Add-Check 'release.source-payload-validator-freshness' $releaseFresh 'RELEASE_EXE' "releaseUtc=$($releaseItem.LastWriteTimeUtc.ToString('o')); newestRequiredSourcePayloadValidatorUtc=$($newestInputUtc.ToString('o'))."
Add-Check 'evidence.package-fixture-final-executable-freshness-binding' $evidenceFixtureFresh 'RELEASE_EXE' "Expected CLI=God2SemanticRecoveryEngine.exe --internal-package-fixture-test --session <fresh-5412-fixture-session> --report <repo-relative-report>; expectedExit=0; releaseSHA=$($releaseIdentity.sha256); releaseUtc=$($releaseItem.LastWriteTimeUtc.ToString('o')); reportSHA=$((Get-Sha256 $EvidenceFixtureReportJson)); testedUtc=$($evidenceFixtureTestedAtUtc.ToString('o')); reportUtc=$($evidenceFixtureItem.LastWriteTimeUtc.ToString('o'))."
Add-Check 'selftest.network-source-hash-freshness' $networkFresh 'DLL' "networkSummaryUtc=$($networkItem.LastWriteTimeUtc.ToString('o')); newestNetworkInputUtc=$($newestNetworkInputUtc.ToString('o')); SHA=$((Get-Sha256 $NetworkSelfTestSummary))."
Add-Check 'selftest.product-source-hash-freshness' $productFresh 'DLL' "productSummaryUtc=$($productItem.LastWriteTimeUtc.ToString('o')); nativeReportUtc=$(if($null-ne$nativeEvidenceItem){$nativeEvidenceItem.LastWriteTimeUtc.ToString('o')}else{'missing'}); newestProductInputUtc=$($newestProductInputUtc.ToString('o')); productSHA=$((Get-Sha256 $ProductSelfTestSummary)); nativeFresh=$nativeEvidenceFresh; boundArtifactCount=$($productArtifactItems.Count); allBoundArtifactsFresh=$productArtifactFresh."
Add-Check 'selftest.final-executable-fresh-run-binding' $freshExecutableSelfTests 'RELEASE_EXE' "releaseUtc=$($releaseItem.LastWriteTimeUtc.ToString('o')); semanticUtc=$(if($null-ne$semanticItem){$semanticItem.LastWriteTimeUtc.ToString('o')}else{'missing'}); ultimateUtc=$(if($null-ne$ultimateItem){$ultimateItem.LastWriteTimeUtc.ToString('o')}else{'missing'})."

$bindingSpecs = @(
    [ordered]@{ role = 'GUI_SOURCE'; path = $guiSourcePath },
    [ordered]@{ role = 'MAIN_SOURCE'; path = $mainSourcePath },
    [ordered]@{ role = 'EVIDENCE_SOURCE'; path = $evidenceSourcePath },
    [ordered]@{ role = 'SHARED_RING_HEADER'; path = $ringHeaderPath },
    [ordered]@{ role = 'ULTIMATE_HEADER'; path = $ultimateHeaderPath },
    [ordered]@{ role = 'ULTIMATE_SOURCE'; path = $ultimateSourcePath },
    [ordered]@{ role = 'RESOURCE_HEADER'; path = $resourceHeaderPath },
    [ordered]@{ role = 'RESOURCE_SCRIPT'; path = $resourceScriptPath },
    [ordered]@{ role = 'PROBE_SOURCE'; path = $probeSourcePath },
    [ordered]@{ role = 'INJECTOR_SOURCE'; path = $injectorSourcePath },
    [ordered]@{ role = 'SELFTEST_CLIENT_SOURCE'; path = $selfTestClientPath },
    [ordered]@{ role = 'NETWORK_SELFTEST_SCRIPT'; path = $networkSelfTestScriptPath },
    [ordered]@{ role = 'PRODUCT_SELFTEST_SCRIPT'; path = $productSelfTestScriptPath },
    [ordered]@{ role = 'ACCEPTANCE_SCRIPT'; path = $acceptanceScriptPath },
    [ordered]@{ role = 'ACCEPTANCE_SCHEMA'; path = $acceptanceSchemaPath },
    [ordered]@{ role = 'ENHANCED_CAPTURE_STATUS_SCHEMA'; path = $enhancedStatusSchemaPath },
    [ordered]@{ role = 'BRIDGE_AWARE_STATUS_V3_SCHEMA'; path = $bridgeAwareStatusV3SchemaPath },
    [ordered]@{ role = 'SHARED_HEALTH_AVAILABILITY_SCHEMA'; path = $sharedHealthAvailabilitySchemaPath },
    [ordered]@{ role = 'CANDIDATE_MAP_AVAILABILITY_SCHEMA'; path = $candidateMapAvailabilitySchemaPath },
    [ordered]@{ role = 'PROBE_PAYLOAD'; path = $ProbePayload },
    [ordered]@{ role = 'INJECTOR_PAYLOAD'; path = $InjectorPayload },
    [ordered]@{ role = 'BUILT_PROBE'; path = $builtProbePath },
    [ordered]@{ role = 'BUILT_INJECTOR'; path = $builtInjectorPath },
    [ordered]@{ role = 'NETWORK_EVIDENCE'; path = $NetworkSelfTestSummary },
    [ordered]@{ role = 'PRODUCT_EVIDENCE'; path = $ProductSelfTestSummary },
    [ordered]@{ role = 'EVIDENCE_FIXTURE_REPORT'; path = $EvidenceFixtureReportJson },
    [ordered]@{ role = 'RESTORE_FAULT_LIFECYCLE_EVIDENCE'; path = $RestoreFaultLifecycleReport },
    [ordered]@{ role = 'SEMANTIC_SELFTEST_EVIDENCE'; path = $semanticReportPath },
    [ordered]@{ role = 'ULTIMATE_SELFTEST_EVIDENCE'; path = $ultimateReportPath }
)
foreach ($v1ArtifactSpec in @(
        @('CANONICAL_V1_SEMANTIC_WIRE_EVIDENCE',$canonicalV1WirePath),
        @('CANONICAL_V1_SEMANTIC_WIRE_BINDING_EVIDENCE',$canonicalV1BindingPath),
        @('NONCANONICAL_V1_COMPATIBILITY_FIXTURE_EVIDENCE',$compatibilityV1InputPath))) {
    if (Test-Path -LiteralPath $v1ArtifactSpec[1] -PathType Leaf) {
        $bindingSpecs += [ordered]@{ role = [string]$v1ArtifactSpec[0]; path = $v1ArtifactSpec[1] }
    }
}
foreach ($artifactSpec in @(
        @('ATTACH_RESULT_EVIDENCE',$attachBinding),
        @('DETACH_RESULT_EVIDENCE',$detachBinding),
        @('PRODUCTION_IDENTITY_GATE_EVIDENCE',$identityBinding),
        @('NATIVE_PROBE_SELFTEST_EVIDENCE',$nativeEvidenceBinding),
        @('NATIVE_SEMANTIC_WIRE_EVIDENCE',$nativeWireBinding),
        @('NATIVE_SEMANTIC_WIRE_VERIFICATION_EVIDENCE',$wireVerifierBinding),
        @('ULTIMATE_NATIVE_WIRE_REPORT_EVIDENCE',$ultimateWireBinding),
        @('DEEP_PROBE_CANDIDATE_MAP_EVIDENCE',$candidateMapBinding))) {
    $binding = $artifactSpec[1]
    if ($null -ne $binding.Item) {
        $bindingSpecs += [ordered]@{ role = [string]$artifactSpec[0]; path = $binding.Path }
    }
}
$inputBindings = @($bindingSpecs | ForEach-Object {
    [ordered]@{ role = $_.role; file = Get-FileIdentity $_.path }
})

$guiObserved = -not [string]::IsNullOrWhiteSpace($GuiSmokeEvidence) -and
    (Test-Path -LiteralPath $GuiSmokeEvidence -PathType Leaf)
if ($guiObserved) {
    $inputBindings += [ordered]@{ role = 'GUI_SMOKE_EVIDENCE'; file = Get-FileIdentity $GuiSmokeEvidence }
}
$dllTwentyFiveDomainStatusContract = 'PENDING'
$dllStrictUnloadResultContract = 'PENDING'
$dllStrictUnloadFinalizationContract = 'PENDING'
$dllCancelResultReadBeforeCleanupContract = 'PENDING'
$dllEnhancedCaptureConsumerStateLifecycle = 'PENDING'
$threeLanguageDllStrictUnloadContract = 'PENDING'
$sharedTransportSlotReuseDomainAccounting = 'PENDING'
$observedEnhancedCaptureStatusSchemaVersion = $null
$observedInitialStrictUnloadVerified = $null
$guiObservedExecutableSha256 = $null
$guiExecutableSha256Bound = 'PENDING'
$strictUnloadPolicyExpected = 'QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped'
$observedStrictUnloadPolicy = $null
$expectedGuiSmokeKeys = @(
    'DllEnhancedCaptureStatusVisible', 'DllEnhancedCaptureAutoEnabled',
    'ThreeLanguageDllEnhancedCaptureStatus', 'ThreeLanguageDllEnhancedCaptureHelp',
    'DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract','ThreeLanguageDllStrictUnloadContract',
    'SharedTransportSlotReuseDomainAccounting','DllStrictUnloadFinalizationContract',
    'DllCancelResultReadBeforeCleanupContract','DllEnhancedCaptureConsumerStateLifecycle'
)
$guiGatePassed = $false
if ($Final) {
    $guiEvidence = if ($guiObserved) {
        Get-Content -LiteralPath $GuiSmokeEvidence -Raw | ConvertFrom-Json
    } else { $null }
    $guiKeysPassed = $null -ne $guiEvidence
    foreach ($key in $expectedGuiSmokeKeys) {
        $property = if ($null -ne $guiEvidence) { $guiEvidence.PSObject.Properties[$key] } else { $null }
        $guiKeysPassed = $guiKeysPassed -and $null -ne $property -and $property.Value -ceq 'PASS'
    }
    $dllDomainProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllTwentyFiveDomainStatusContract']
    } else { $null }
    $dllTwentyFiveDomainStatusContract = if ($null -ne $dllDomainProperty -and
        $dllDomainProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $dllStrictUnloadProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllStrictUnloadResultContract']
    } else { $null }
    $dllStrictUnloadResultContract = if ($null -ne $dllStrictUnloadProperty -and
        $dllStrictUnloadProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $dllStrictUnloadFinalizationProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllStrictUnloadFinalizationContract']
    } else { $null }
    $dllStrictUnloadFinalizationContract = if ($null -ne $dllStrictUnloadFinalizationProperty -and
        $dllStrictUnloadFinalizationProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $dllCancelResultProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllCancelResultReadBeforeCleanupContract']
    } else { $null }
    $dllCancelResultReadBeforeCleanupContract = if ($null -ne $dllCancelResultProperty -and
        $dllCancelResultProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $consumerStateProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllEnhancedCaptureConsumerStateLifecycle']
    } else { $null }
    $dllEnhancedCaptureConsumerStateLifecycle = if ($null -ne $consumerStateProperty -and
        $consumerStateProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $threeLanguageUnloadProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['ThreeLanguageDllStrictUnloadContract']
    } else { $null }
    $threeLanguageDllStrictUnloadContract = if ($null -ne $threeLanguageUnloadProperty -and
        $threeLanguageUnloadProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $slotReuseProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['SharedTransportSlotReuseDomainAccounting']
    } else { $null }
    $sharedTransportSlotReuseDomainAccounting = if ($null -ne $slotReuseProperty -and
        $slotReuseProperty.Value -ceq 'PASS') { 'PASS' } else { 'FAIL' }
    $strictUnloadPolicyProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllStrictUnloadPolicy']
    } else { $null }
    $observedStrictUnloadPolicy = if ($null -ne $strictUnloadPolicyProperty) {
        [string]$strictUnloadPolicyProperty.Value
    } else { $null }
    $statusSchemaProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllEnhancedCaptureStatusSchemaVersion']
    } else { $null }
    $observedEnhancedCaptureStatusSchemaVersion = if ($null -ne $statusSchemaProperty) {
        [string]$statusSchemaProperty.Value
    } else { $null }
    $initialStrictUnloadProperty = if ($null -ne $guiEvidence) {
        $guiEvidence.PSObject.Properties['DllEnhancedCaptureInitialStrictUnloadVerified']
    } else { $null }
    $observedInitialStrictUnloadVerified = if ($null -ne $initialStrictUnloadProperty) {
        $initialStrictUnloadProperty.Value
    } else { $null }
    $strictUnloadPolicyPassed = $observedStrictUnloadPolicy -ceq $strictUnloadPolicyExpected -and
        $observedStrictUnloadPolicy -cnotmatch 'ResidentInactiveOnlyOnUncertainRestoreOrCachedReference'
    $enhancedStatusSchemaPassed = $observedEnhancedCaptureStatusSchemaVersion -ceq
        'god2-enhanced-capture-status-v2' -and
        (Test-JsonBooleanValue $observedInitialStrictUnloadVerified $false)
    $guiAutomatedPassed = $null -ne $guiEvidence -and
        $guiEvidence.AutomatedStatus -ceq 'PASS' -and [int]$guiEvidence.FailureCount -eq 0 -and
        [int]$guiEvidence.AutomatedCheckCount -ge 72
    $guiIdentityBound = $false
    $guiIdentityEvidence = 'ExecutableSha256/ExecutableSha256Bound missing'
    if ($null -ne $guiEvidence) {
        $shaProperty = $guiEvidence.PSObject.Properties['ExecutableSha256']
        $boundProperty = $guiEvidence.PSObject.Properties['ExecutableSha256Bound']
        $guiIdentityBound = $null -ne $shaProperty -and $null -ne $boundProperty -and
            ([string]$shaProperty.Value).ToUpperInvariant() -eq $releaseIdentity.sha256 -and
            $boundProperty.Value -ceq 'PASS'
        $guiObservedExecutableSha256 = if ($null -ne $shaProperty) { ([string]$shaProperty.Value).ToUpperInvariant() } else { $null }
        $guiExecutableSha256Bound = if ($guiIdentityBound) { 'PASS' } else { 'FAIL' }
        $guiIdentityEvidence = "ExecutableSha256=$(if($null -ne $shaProperty){$shaProperty.Value}else{'missing'}); ExecutableSha256Bound=$(if($null -ne $boundProperty){$boundProperty.Value}else{'missing'}); expected=$($releaseIdentity.sha256)"
    }
    $guiFresh = $guiObserved -and (Get-Item -LiteralPath $GuiSmokeEvidence).LastWriteTimeUtc -ge $releaseItem.LastWriteTimeUtc
    $guiGatePassed = $guiObserved -and $guiKeysPassed -and $guiAutomatedPassed -and
        $guiIdentityBound -and $guiFresh -and $strictUnloadPolicyPassed -and
        $enhancedStatusSchemaPassed
    Add-Check 'gui.smoke-visibility-unload-slot-reuse' $guiGatePassed 'GUI' "finalMode=true; observed=$guiObserved; required 11 keys PASS=$guiKeysPassed; AutomatedStatus PASS/FailureCount0/AutomatedCheckCount>=72=$guiAutomatedPassed; executable binding=$guiIdentityBound ($guiIdentityEvidence); freshAfterRelease=$guiFresh; strictUnloadPolicy=$strictUnloadPolicyPassed; statusSchemaV2/initialStrictUnloadFalse=$enhancedStatusSchemaPassed."
} else {
    Add-Pending 'gui.smoke-visibility-unload-slot-reuse' 'GUI' "Final rebuilt EXE/UI smoke visibility is deliberately pending; required keys=$($expectedGuiSmokeKeys -join ','); rerun with -Final -GuiSmokeEvidence <path> to permit exact final PASS."
}

$networkStatus = if ($networkCasesValid -and $networkFresh) { 'PASS' } else { 'BLOCKED' }
$productStatus = if ($productValid -and $productFresh) { 'PASS' } else { 'BLOCKED' }
$evidenceFixtureStatus = if ($evidenceFixtureContract.Passed -and $evidenceFixtureFresh) { 'PASS' } else { 'BLOCKED' }
$semanticStatus = if ($semanticValid) { 'PASS' } else { 'BLOCKED' }
$ultimateStatus = if ($ultimateValid) { 'PASS' } else { 'BLOCKED' }
$networkEvidence = [ordered]@{
    path = Get-PortablePath $NetworkSelfTestSummary; sha256 = Get-Sha256 $NetworkSelfTestSummary
    status = $networkStatus; total = 5; passed = if ($networkCasesValid -and $networkFresh) { 5 } else { 0 }; failed = if ($networkCasesValid -and $networkFresh) { 0 } else { 5 }
}
$productEvidence = [ordered]@{
    path = Get-PortablePath $ProductSelfTestSummary; sha256 = Get-Sha256 $ProductSelfTestSummary
    status = $productStatus; total = 1; passed = if ($productValid -and $productFresh) { 1 } else { 0 }; failed = if ($productValid -and $productFresh) { 0 } else { 1 }
}
$evidenceFixtureEvidence = [ordered]@{
    path = Get-PortablePath $EvidenceFixtureReportJson
    sha256 = Get-Sha256 $EvidenceFixtureReportJson
    status = $evidenceFixtureStatus
    total = [int](Get-JsonPropertyValue $evidenceFixture 'CheckCount')
    passed = [int](Get-JsonPropertyValue $evidenceFixture 'Passed')
    failed = [int](Get-JsonPropertyValue $evidenceFixture 'Failed')
}
$nativeProbeEvidence = [ordered]@{
    path = if ($null -ne $nativeEvidenceItem) { Get-PortablePath $nativeEvidencePath } else { 'MISSING_NATIVE_PROBE_SELFTEST_EVIDENCE' }
    sha256 = if ($null -ne $nativeEvidenceItem) { Get-Sha256 $nativeEvidencePath } else { '0' * 64 }
    status = if ($nativeBindingValid -and $nativeEvidenceFresh) { 'PASS' } else { 'BLOCKED' }
    total = 1
    passed = if ($nativeBindingValid -and $nativeEvidenceFresh) { 1 } else { 0 }
    failed = if ($nativeBindingValid -and $nativeEvidenceFresh) { 0 } else { 1 }
}
$semanticEvidence = [ordered]@{
    path = Get-PortablePath $semanticReportPath
    sha256 = if (Test-Path -LiteralPath $semanticReportPath) { Get-Sha256 $semanticReportPath } else { '0' * 64 }
    status = $semanticStatus
    total = if ($null -ne $semantic) { [int]$semantic.CheckCount } else { 0 }
    passed = if ($null -ne $semantic) { [int]$semantic.Passed } else { 0 }
    failed = if ($null -ne $semantic) { [int]$semantic.Failed } else { 1 }
}
$ultimateEvidence = [ordered]@{
    path = Get-PortablePath $ultimateReportPath
    sha256 = if (Test-Path -LiteralPath $ultimateReportPath) { Get-Sha256 $ultimateReportPath } else { '0' * 64 }
    status = $ultimateStatus
    total = if ($null -ne $ultimate) { [int]$ultimate.total } else { 0 }
    passed = if ($null -ne $ultimate) { [int]$ultimate.passed } else { 0 }
    failed = if ($null -ne $ultimate) { [int]$ultimate.failed } else { 1 }
}
$canonicalProbeDomains = @(@(Get-JsonPropertyValue $product 'ProbeDomainResults') | ForEach-Object {
    [ordered]@{
        index = Get-JsonPropertyValue $_ 'Index'
        domain = Get-JsonPropertyValue $_ 'Domain'
        probe = Get-JsonPropertyValue $_ 'Probe'
        contractStatus = Get-JsonPropertyValue $_ 'ContractStatus'
        activationAllowedByContract = Get-JsonPropertyValue $_ 'ActivationAllowedByContract'
        runtimeDiagnosticObserved = Get-JsonPropertyValue $_ 'RuntimeDiagnosticObserved'
        runtimeActivationObserved = Get-JsonPropertyValue $_ 'RuntimeActivationObserved'
        targetIdentityVerified = Get-JsonPropertyValue $_ 'TargetIdentityVerified'
    }
})
$canonicalCandidateGates = @(@(Get-JsonPropertyValue $product 'CandidateGateResults') | ForEach-Object {
    [ordered]@{
        gate = Get-JsonPropertyValue $_ 'Gate'
        category = Get-JsonPropertyValue $_ 'Category'
        passedOnAllCandidateDomains = Get-JsonPropertyValue $_ 'PassedOnAllCandidateDomains'
        rejectedOnAllCandidateDomains = Get-JsonPropertyValue $_ 'RejectedOnAllCandidateDomains'
    }
})
$canonicalNetworkCases = @(@(Get-JsonPropertyValue $network 'cases') | ForEach-Object {
    [ordered]@{
        case = Get-JsonPropertyValue $_ 'Case'
        hookInstalled = Get-JsonPropertyValue $_ 'HookInstalled'
        apiCallResult = Get-JsonPropertyValue $_ 'ApiCallResult'
        apiCallResultExpected = Get-JsonPropertyValue $_ 'ApiCallResultExpected'
        bytesTransferred = Get-JsonPropertyValue $_ 'BytesTransferred'
        traceRecordCount = Get-JsonPropertyValue $_ 'TraceRecordCount'
        matchingRecordCount = Get-JsonPropertyValue $_ 'MatchingRecordCount'
        payloadMatch = Get-JsonPropertyValue $_ 'PayloadMatch'
        analyzerParseCompleted = Get-JsonPropertyValue $_ 'AnalyzerParseCompleted'
        traceIntegrity = Get-JsonPropertyValue $_ 'TraceIntegrity'
        analyzerError = Get-JsonPropertyValue $_ 'AnalyzerError'
        traceFileSize = Get-JsonPropertyValue $_ 'TraceFileSize'
    }
})
$canonicalPriorityResults = @(@(Get-JsonPropertyValue $nativeEvidence 'SharedTransportPriorityResults') | ForEach-Object {
    [ordered]@{
        priority = Get-JsonPropertyValue $_ 'Priority'
        laneAccepted = Get-JsonPropertyValue $_ 'LaneAccepted'
        laneDropped = Get-JsonPropertyValue $_ 'LaneDropped'
        laneDropReason = Get-JsonPropertyValue $_ 'LaneDropReason'
        samplingProbeDropped = Get-JsonPropertyValue $_ 'SamplingProbeDropped'
        samplingProbeDropReason = Get-JsonPropertyValue $_ 'SamplingProbeDropReason'
    }
})
$canonicalLaneResults = @(@(Get-JsonPropertyValue $product 'SharedTransportLaneResults') | ForEach-Object {
    [ordered]@{
        priority = Get-JsonPropertyValue $_ 'Priority'
        accepted = Get-JsonPropertyValue $_ 'Accepted'
        consumed = Get-JsonPropertyValue $_ 'Consumed'
        dropped = Get-JsonPropertyValue $_ 'Dropped'
        sampled = Get-JsonPropertyValue $_ 'Sampled'
        firstDroppedSequence = Get-JsonPropertyValue $_ 'FirstDroppedSequence'
        lastDroppedSequence = Get-JsonPropertyValue $_ 'LastDroppedSequence'
        lastDropReason = Get-JsonPropertyValue $_ 'LastDropReason'
        highWaterMark = Get-JsonPropertyValue $_ 'HighWaterMark'
        consumerLag = Get-JsonPropertyValue $_ 'ConsumerLag'
        passed = Get-JsonPropertyValue $_ 'Passed'
    }
})
$canonicalDomainCounters = @(@(Get-JsonPropertyValue $nativeEvidence 'SharedTransportDomainResults') | ForEach-Object {
    [ordered]@{
        domainIndex = Get-JsonPropertyValue $_ 'DomainIndex'
        domain = Get-JsonPropertyValue $_ 'Domain'
        accepted = Get-JsonPropertyValue $_ 'Accepted'
        dropped = Get-JsonPropertyValue $_ 'Dropped'
        highWater = Get-JsonPropertyValue $_ 'HighWater'
        pending = Get-JsonPropertyValue $_ 'Pending'
        writeFailures = Get-JsonPropertyValue $_ 'WriteFailures'
    }
})
$canonicalFaultResults = @(@(Get-JsonPropertyValue $nativeEvidence 'FaultIsolationResults') | ForEach-Object {
    [ordered]@{
        domainIndex = Get-JsonPropertyValue $_ 'DomainIndex'
        domain = Get-JsonPropertyValue $_ 'Domain'
        faultInjected = Get-JsonPropertyValue $_ 'FaultInjected'
        affectedDomainDisabled = Get-JsonPropertyValue $_ 'AffectedDomainDisabled'
        privateDiagnosticAttributed = Get-JsonPropertyValue $_ 'PrivateDiagnosticAttributed'
        privateDiagnosticSinkWritten = Get-JsonPropertyValue $_ 'PrivateDiagnosticSinkWritten'
        runtimeDiagnosticEmitted = Get-JsonPropertyValue $_ 'RuntimeDiagnosticEmitted'
        otherDomainsEnabledAtFault = Get-JsonPropertyValue $_ 'OtherDomainsEnabledAtFault'
        otherDomainsEnabledMask = Get-JsonPropertyValue $_ 'OtherDomainsEnabledMask'
        otherDomainsRemainEnabledMeasured = Get-JsonPropertyValue $_ 'OtherDomainsRemainEnabledMeasured'
        otherDomainContinued = Get-JsonPropertyValue $_ 'OtherDomainContinued'
    }
})
$canonicalDiagnosticDomains = @(@(Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticDomainResults') | ForEach-Object {
    [ordered]@{
        domainIndex = Get-JsonPropertyValue $_ 'DomainIndex'
        domain = Get-JsonPropertyValue $_ 'Domain'
        dropped = Get-JsonPropertyValue $_ 'Dropped'
        writeFailures = Get-JsonPropertyValue $_ 'WriteFailures'
        evidenceIncomplete = Get-JsonPropertyValue $_ 'EvidenceIncomplete'
    }
})
$canonicalAdmissionCapResults = @()
$nativeAdmissionRows = @(Get-JsonPropertyValue $nativeEvidence 'SemanticAdmissionCapResults')
if ($nativeAdmissionRows.Count -eq 2) {
    $canonicalAdmissionCapResults = @(
        [ordered]@{
            priority = Get-JsonPropertyValue $nativeAdmissionRows[0] 'Priority'
            domainIndex = Get-JsonPropertyValue $nativeAdmissionRows[0] 'DomainIndex'
            domain = Get-JsonPropertyValue $nativeAdmissionRows[0] 'Domain'
            acceptedAtBoundary = Get-JsonPropertyValue $nativeAdmissionRows[0] 'AcceptedAtBoundary'
            droppedAtBoundary = Get-JsonPropertyValue $nativeAdmissionRows[0] 'DroppedAtBoundary'
            dropReason = Get-JsonPropertyValue $nativeAdmissionRows[0] 'DropReason'
            firstDroppedSequence = Get-JsonPropertyValue $nativeAdmissionRows[0] 'FirstDroppedSequence'
            lastDroppedSequence = Get-JsonPropertyValue $nativeAdmissionRows[0] 'LastDroppedSequence'
        },
        [ordered]@{
            priority = Get-JsonPropertyValue $nativeAdmissionRows[1] 'Priority'
            domainIndex = Get-JsonPropertyValue $nativeAdmissionRows[1] 'DomainIndex'
            domain = Get-JsonPropertyValue $nativeAdmissionRows[1] 'Domain'
            acceptedBeyondLegacyHardLimit = Get-JsonPropertyValue $nativeAdmissionRows[1] 'AcceptedBeyondLegacyHardLimit'
            droppedBeyondLegacyHardLimit = Get-JsonPropertyValue $nativeAdmissionRows[1] 'DroppedBeyondLegacyHardLimit'
            dropReason = Get-JsonPropertyValue $nativeAdmissionRows[1] 'DropReason'
            firstDroppedSequence = Get-JsonPropertyValue $nativeAdmissionRows[1] 'FirstDroppedSequence'
            lastDroppedSequence = Get-JsonPropertyValue $nativeAdmissionRows[1] 'LastDroppedSequence'
        }
    )
}
$runtimeEvidence = [ordered]@{
    officialClientLiveEvidenceClaimed = [bool]($officialRuntimeObserved -and $officialIsCurrent)
    officialClientLiveGateStatus = if ($officialRuntimeObserved -and $officialIsCurrent) {
        'PASS_OFFICIAL_EXACT_CLIENT_RUNTIME'
    } elseif ($officialRuntimeObserved) {
        'PASS_HISTORICAL_OFFICIAL_EXACT_CLIENT_RUNTIME'
    } else { 'FAIL_OFFICIAL_EXACT_CLIENT_RUNTIME' }
    evidencePackageFixture = [ordered]@{
        reportPath = Get-PortablePath $EvidenceFixtureReportJson
        reportSha256 = Get-Sha256 $EvidenceFixtureReportJson
        executableSha256 = $releaseIdentity.sha256
        expectedCommandContract = 'God2SemanticRecoveryEngine.exe --internal-package-fixture-test --session <fresh-5412-fixture-session> --report <repo-relative-report>'
        expectedExitCode = 0
        generatedAfterExecutable = [bool]$evidenceFixtureFresh
        schemaVersion = Get-JsonPropertyValue $evidenceFixture 'SchemaVersion'
        checkCount = Get-JsonPropertyValue $evidenceFixture 'CheckCount'
        passed = Get-JsonPropertyValue $evidenceFixture 'Passed'
        failed = Get-JsonPropertyValue $evidenceFixture 'Failed'
        packageSha256 = Get-JsonPropertyValue $evidenceFixture 'PackageSHA256'
        requiredChecks = @(
            '10a_legacy_or_detail_only_unload_spoof_rejected',
            '10b_versioned_strict_unload_positive_accepted',
            '10c_cleanup_schema_requires_typed_complete_inventory',
            '10d_verified_no_module_loaded_cleanup_is_distinct_from_unknown',
            '14e_missing_shared_health_uses_nonpromotable_availability_schema',
            '14f_missing_candidate_map_never_emits_stale_v1_placeholder'
        )
    }
    nativeProbeBinding = [ordered]@{
        path = Get-JsonPropertyValue $product 'NativeProbeSelfTestPath'
        sha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        schemaId = Get-JsonPropertyValue $product 'NativeProbeSelfTestSchemaId'
        schemaVersion = Get-JsonPropertyValue $product 'NativeProbeSelfTestSchemaVersion'
        productEmbeddedReportExact = [bool]$nativeBindingValid
    }
    productArtifactBindings = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        allStrictBound = [bool]$productArtifactBindingsValid
        allGeneratedAfterBoundInputs = [bool]$productArtifactFresh
        attachResult = [ordered]@{
            path = Get-JsonPropertyValue $product 'AttachResultPath'
            sha256 = Get-JsonPropertyValue $product 'AttachResultSHA256'
            embeddedRecordExact = [bool]$attachBinding.Passed
        }
        detachResult = [ordered]@{
            path = Get-JsonPropertyValue $product 'DetachResultPath'
            sha256 = Get-JsonPropertyValue $product 'DetachResultSHA256'
            embeddedRecordExact = [bool]$detachBinding.Passed
        }
        productionIdentityGate = [ordered]@{
            path = Get-JsonPropertyValue $product 'ProductionIdentityGatePath'
            sha256 = Get-JsonPropertyValue $product 'ProductionIdentityGateSHA256'
            embeddedRecordExact = [bool]$identityBinding.Passed
        }
        nativeProbe = [ordered]@{
            path = Get-JsonPropertyValue $product 'NativeProbeSelfTestPath'
            sha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
            embeddedRecordExact = [bool]$nativeEvidenceBinding.Passed
        }
        candidateMap = [ordered]@{
            path = Get-JsonPropertyValue $product 'DeepProbeCandidateMapPath'
            sha256 = Get-JsonPropertyValue $product 'DeepProbeCandidateMapSHA256'
            schemaVersion = Get-JsonPropertyValue $product 'DeepProbeCandidateMapSchemaVersion'
            strictContractPassed = [bool]$candidateMapBinding.Passed
        }
    }
    probeDomains = [ordered]@{
        evidenceSha256 = $productEvidence.sha256
        domainCount = Get-JsonPropertyValue $product 'ProbeDomainCount'
        confirmedContractDomainCount = Get-JsonPropertyValue $product 'ConfirmedContractDomainCount'
        candidateOnlyBlockedDomainCount = Get-JsonPropertyValue $product 'CandidateOnlyBlockedDomainCount'
        officialTargetIdentityVerified = [bool]($officialRuntimeObserved -and $officialIsCurrent)
        results = $canonicalProbeDomains
    }
    nativeEventProducers = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        fixtureCount = Get-JsonPropertyValue $product 'SemanticEventTypeFixtureCount'
        distinctCount = Get-JsonPropertyValue $product 'SemanticEventTypeDistinctCount'
        nativeCount = Get-JsonPropertyValue $product 'SemanticEventTypeNativeCount'
        sortedNameDigestSha256 = Get-JsonPropertyValue $product 'SemanticEventTypeDigestSHA256'
    }
    semanticEventMatrix = Get-JsonPropertyValue $ultimate 'semanticEventMatrix'
    nativeSemanticWire = [ordered]@{
        path = Get-JsonPropertyValue $product 'NativeSemanticWirePath'
        sha256 = Get-JsonPropertyValue $product 'NativeSemanticWireSHA256'
        lineCount = Get-JsonPropertyValue $product 'NativeSemanticWireCount'
        eventTypeDigestSha256 = Get-JsonPropertyValue $product 'SemanticEventTypeDigestSHA256'
        targetProcessId = Get-JsonPropertyValue $product 'TargetProcessId'
        producerProcessId = Get-JsonPropertyValue $product 'SharedTransportProducerProcessId'
        consumerProcessId = Get-JsonPropertyValue $product 'SharedTransportConsumerProcessId'
        nativeProcessId = Get-JsonPropertyValue $product 'NativeProbeProcessId'
        wireProcessIds = @(Get-JsonPropertyValue $product 'NativeSemanticWireProcessIds')
        crossProcess = Get-JsonPropertyValue $product 'SharedTransportCrossProcess'
        crossProcessNegativeCount = Get-JsonPropertyValue $product 'SharedTransportCrossProcessNegativeCount'
        strictJsonlContractPassed = [bool]$nativeWireBinding.Passed
        verificationPath = Get-JsonPropertyValue $product 'NativeSemanticWireVerificationPath'
        verificationSha256 = Get-JsonPropertyValue $product 'NativeSemanticWireVerificationSHA256'
        verificationSchemaId = Get-JsonPropertyValue $product 'NativeSemanticWireVerificationSchemaId'
        verificationSchemaVersion = Get-JsonPropertyValue $product 'NativeSemanticWireVerificationSchemaVersion'
        verificationEmbeddedReportExact = [bool]$wireVerifierBinding.Passed
        ultimateReportPath = Get-JsonPropertyValue $product 'UltimateNativeWireReportPath'
        ultimateReportSha256 = Get-JsonPropertyValue $product 'UltimateNativeWireReportSHA256'
        ultimateEmbeddedReportExact = [bool]$ultimateWireBinding.Passed
    }
    activationGates = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        promotionGateCount = Get-JsonPropertyValue $product 'CandidatePromotionGateCount'
        abiSafetyGateCount = Get-JsonPropertyValue $product 'CandidateAbiSafetyGateCount'
        totalGateCount = Get-JsonPropertyValue $product 'CandidateTotalGateCount'
        matrixValid = Get-JsonPropertyValue $product 'CandidateActivationGateMatrixValid'
        passedGateCount = Get-JsonPropertyValue $product 'CandidateActivationPassedGateCount'
        rejectedGateCount = Get-JsonPropertyValue $product 'CandidateActivationRejectedGateCount'
        results = $canonicalCandidateGates
    }
    networkCompletion = [ordered]@{
        baselineEvidenceSha256 = $networkEvidence.sha256
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        completionRoutineObserved = Get-JsonPropertyValue $nativeEvidence 'CompletionRoutineObserved'
        wsaGetOverlappedResultObserved = Get-JsonPropertyValue $nativeEvidence 'WSAGetOverlappedResultObserved'
        getQueuedCompletionStatusObserved = Get-JsonPropertyValue $nativeEvidence 'GetQueuedCompletionStatusObserved'
        getQueuedCompletionStatusExObserved = Get-JsonPropertyValue $nativeEvidence 'GetQueuedCompletionStatusExObserved'
        cases = $canonicalNetworkCases
    }
    sharedRing = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        version = Get-JsonPropertyValue $product 'SharedTransportVersion'
        producerReady = Get-JsonPropertyValue $product 'SharedTransportProducerReady'
        producerClosed = Get-JsonPropertyValue $product 'SharedTransportProducerClosed'
        consumerReady = Get-JsonPropertyValue $product 'SharedTransportConsumerReady'
        consumerClosed = Get-JsonPropertyValue $product 'SharedTransportConsumerClosed'
        consumerFailure = Get-JsonPropertyValue $product 'SharedTransportConsumerFailure'
        consumerDrainVerified = Get-JsonPropertyValue $product 'SharedTransportConsumerDrainVerified'
        domainPendingTotal = Get-JsonPropertyValue $product 'SharedTransportDomainPendingTotal'
        targetProcessId = Get-JsonPropertyValue $product 'TargetProcessId'
        producerProcessId = Get-JsonPropertyValue $product 'SharedTransportProducerProcessId'
        consumerProcessId = Get-JsonPropertyValue $product 'SharedTransportConsumerProcessId'
        nativeProcessId = Get-JsonPropertyValue $product 'NativeProbeProcessId'
        crossProcessNegativeCount = Get-JsonPropertyValue $product 'SharedTransportCrossProcessNegativeCount'
        attempted = Get-JsonPropertyValue $product 'SharedTransportAttempted'
        accepted = Get-JsonPropertyValue $product 'SharedTransportAccepted'
        dropped = Get-JsonPropertyValue $product 'SharedTransportDropped'
        sampled = Get-JsonPropertyValue $product 'SharedTransportSampled'
        highWaterMark = Get-JsonPropertyValue $product 'SharedTransportHighWaterMark'
        consumerLag = Get-JsonPropertyValue $product 'SharedTransportConsumerLag'
        consumedPayloads = Get-JsonPropertyValue $product 'SharedTransportConsumedPayloads'
        batchMaximumItems = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchMaximumItems'
        batchAccepted = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchAccepted'
        batchCount = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchCount'
        batchEventSignalDelta = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchEventSignalDelta'
        batchLockAcquisitionDelta = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchLockAcquisitionDelta'
        batchContractPassed = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchContractPassed'
        priorityContractPassed = Get-JsonPropertyValue $nativeEvidence 'SharedTransportPriorityContractPassed'
        priorityResults = $canonicalPriorityResults
        laneContractPassed = Get-JsonPropertyValue $product 'SharedTransportLaneContractPassed'
        laneResults = $canonicalLaneResults
        wirePriorityCounts = @(Get-JsonPropertyValue $product 'SharedTransportWirePriorityCounts')
        priorityMismatches = Get-JsonPropertyValue $product 'SharedTransportPriorityMismatches'
        sequenceOrdered = Get-JsonPropertyValue $product 'SharedTransportSequenceOrdered'
        invalidPayloads = Get-JsonPropertyValue $product 'SharedTransportInvalidPayloads'
        domainDropTotal = Get-JsonPropertyValue $product 'SharedTransportDomainDropTotal'
        domainWriteFailureTotal = Get-JsonPropertyValue $nativeEvidence 'SharedTransportDomainWriteFailureTotal'
        batchValidationWriteFailureCount = Get-JsonPropertyValue $nativeEvidence 'SharedTransportBatchValidationWriteFailureCount'
        domainPendingAfterDrain = Get-JsonPropertyValue $product 'SharedTransportDomainPendingAfterDrain'
        domainsWithAcceptedHighWater = Get-JsonPropertyValue $product 'SharedTransportDomainsWithAcceptedHighWater'
        domainsWithDropAccounting = Get-JsonPropertyValue $product 'SharedTransportDomainsWithDropAccounting'
        crossProcess = Get-JsonPropertyValue $product 'SharedTransportCrossProcess'
        domainResults = $canonicalDomainCounters
    }
    semanticAdmissionCap = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        contractPassed = Get-JsonPropertyValue $product 'SemanticAdmissionCapContractPassed'
        sharedLedgerPassed = Get-JsonPropertyValue $product 'SemanticAdmissionCapSharedLedgerPassed'
        liveStateUntouched = Get-JsonPropertyValue $product 'SemanticAdmissionCapLiveStateUntouched'
        hardLimit = Get-JsonPropertyValue $product 'SemanticAdmissionHardLimit'
        lowPriorityCeiling = Get-JsonPropertyValue $product 'SemanticAdmissionLowPriorityCeiling'
        results = $canonicalAdmissionCapResults
    }
    lifecycle = [ordered]@{
        productEvidenceSha256 = $productEvidence.sha256
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        attachResult = Get-JsonPropertyValue $product 'AttachResultRecord'
        attachResultPath = Get-JsonPropertyValue $product 'AttachResultPath'
        attachResultSha256 = Get-JsonPropertyValue $product 'AttachResultSHA256'
        detachResult = Get-JsonPropertyValue $product 'DetachResultRecord'
        detachResultPath = Get-JsonPropertyValue $product 'DetachResultPath'
        detachResultSha256 = Get-JsonPropertyValue $product 'DetachResultSHA256'
        productionIdentityGate = Get-JsonPropertyValue $product 'ProductionIdentityGateRecord'
        productionIdentityGatePath = Get-JsonPropertyValue $product 'ProductionIdentityGatePath'
        productionIdentityGateSha256 = Get-JsonPropertyValue $product 'ProductionIdentityGateSHA256'
        strictInjectorResultParserNegativeCount = Get-JsonPropertyValue $product 'StrictInjectorResultParserNegativeCount'
        moduleUnloaded = Get-JsonPropertyValue $product 'ModuleUnloaded'
        moduleResidentInactive = Get-JsonPropertyValue $product 'ModuleResidentInactive'
        moduleSnapshotVerified = Get-JsonPropertyValue $product 'ModuleSnapshotVerified'
        moduleAbsent = Get-JsonPropertyValue $product 'ModuleAbsent'
        unloadSafe = Get-JsonPropertyValue $product 'UnloadSafe'
        extraReferenceNegativeFirstFreeLibraryStillPresent = Get-JsonPropertyValue $product 'ExtraReferenceNegativeFirstFreeLibraryStillPresent'
        extraReferenceNegativeNotClaimedUnloaded = Get-JsonPropertyValue $product 'ExtraReferenceNegativeNotClaimedUnloaded'
        extraReferenceReleasedThenModuleAbsent = Get-JsonPropertyValue $product 'ExtraReferenceReleasedThenModuleAbsent'
        restoreFaultLifecyclePath = Get-PortablePath $RestoreFaultLifecycleReport
        restoreFaultLifecycleSha256 = Get-Sha256 $RestoreFaultLifecycleReport
        restoreFaultLifecycle = $restoreFaultReport
        restoreFaultResidentNegativePassed = Get-JsonPropertyValue $nativeEvidence 'RestoreFaultResidentNegativePassed'
        iatOwnerLifecycleSelfTestPassed = Get-JsonPropertyValue $nativeEvidence 'IatOwnerLifecycleSelfTestPassed'
        iatOwnerGoneRetired = Get-JsonPropertyValue $nativeEvidence 'IatOwnerGoneRetired'
        iatAddressReuseNotClobbered = Get-JsonPropertyValue $nativeEvidence 'IatAddressReuseNotClobbered'
        iatReplacementCasRestored = Get-JsonPropertyValue $nativeEvidence 'IatReplacementCasRestored'
        iatOriginalNotClobbered = Get-JsonPropertyValue $nativeEvidence 'IatOriginalNotClobbered'
        iatThirdPartyNotClobbered = Get-JsonPropertyValue $nativeEvidence 'IatThirdPartyNotClobbered'
        pendingAtStopDeferredCount = Get-JsonPropertyValue $nativeEvidence 'PendingAtStopDeferredCount'
        pendingAtStopRetryDrainedCount = Get-JsonPropertyValue $nativeEvidence 'PendingAtStopRetryDrainedCount'
        pendingAtStopDeferredAttempts = Get-JsonPropertyValue $nativeEvidence 'PendingAtStopDeferredAttempts'
    }
    faultIsolation = [ordered]@{
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        passed = Get-JsonPropertyValue $nativeEvidence 'FaultIsolationPassed'
        results = $canonicalFaultResults
    }
    asyncDiagnostics = [ordered]@{
        nativeEvidenceSha256 = Get-JsonPropertyValue $product 'NativeProbeSelfTestSHA256'
        hookThreadFileIoOperations = Get-JsonPropertyValue $nativeEvidence 'HookThreadFileIoOperations'
        ringNullFallbackPassed = Get-JsonPropertyValue $nativeEvidence 'HookThreadRingNullFallbackPassed'
        ringNullSemanticLossCount = Get-JsonPropertyValue $nativeEvidence 'HookThreadRingNullSemanticLossCount'
        queuePassed = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticQueuePassed'
        capacity = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticQueueCapacity'
        enqueued = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticEnqueued'
        flushed = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticFlushed'
        dropped = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticDropCount'
        writeFailures = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticWriteFailureCount'
        highWater = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticHighWater'
        pending = Get-JsonPropertyValue $nativeEvidence 'AsyncDiagnosticPending'
        domainResults = $canonicalDiagnosticDomains
    }
    enhancedCaptureStatusSchema = [ordered]@{
        path = Get-PortablePath $enhancedStatusSchemaPath
        sha256 = Get-Sha256 $enhancedStatusSchemaPath
        schemaId = Get-JsonPropertyValue $enhancedStatusSchema '$id'
    }
    bridgeAwareStatusV3 = [ordered]@{
        schemaPath = Get-PortablePath $bridgeAwareStatusV3SchemaPath
        schemaSha256 = $bridgeAwareStatusV3SchemaLockSha
        schemaId = Get-JsonPropertyValue $bridgeAwareStatusV3Schema '$id'
        reportPath = Get-PortablePath $bridgeAwareStatusV3Path
        reportSha256 = $bridgeAwareStatusV3Sha256
        report = $bridgeAwareStatusV3
    }
}
$freshness = [ordered]@{
    releaseBuiltAfterAllBoundSources = [bool]$releaseFresh
    networkEvidenceAfterAllBoundInputs = [bool]$networkFresh
    productEvidenceAfterAllBoundInputs = [bool]$productFresh
    evidenceFixtureGeneratedAfterRelease = [bool]$evidenceFixtureFresh
    nativeProbeEvidenceAfterAllBoundInputs = [bool]$nativeEvidenceFresh
    executableSelfTestsGeneratedAfterRelease = [bool]$freshExecutableSelfTests
    guiSmokeGeneratedAfterRelease = if ($Final) { [bool]$guiFresh } else { $null }
    releaseLastWriteTimeUtc = $releaseItem.LastWriteTimeUtc.ToString('o')
    newestBoundSourceLastWriteTimeUtc = $newestInputUtc.ToString('o')
    networkEvidenceLastWriteTimeUtc = $networkItem.LastWriteTimeUtc.ToString('o')
    newestNetworkInputLastWriteTimeUtc = $newestNetworkInputUtc.ToString('o')
    productEvidenceLastWriteTimeUtc = $productItem.LastWriteTimeUtc.ToString('o')
    evidenceFixtureLastWriteTimeUtc = $evidenceFixtureItem.LastWriteTimeUtc.ToString('o')
    nativeProbeEvidenceLastWriteTimeUtc = if ($null -ne $nativeEvidenceItem) { $nativeEvidenceItem.LastWriteTimeUtc.ToString('o') } else { $null }
    newestProductInputLastWriteTimeUtc = $newestProductInputUtc.ToString('o')
    acceptanceScriptSha256 = Get-Sha256 $acceptanceScriptPath
    acceptanceSchemaSha256 = Get-Sha256 $acceptanceSchemaPath
    guiSourceSha256 = $guiSourceLockSha
    mainSourceSha256 = $mainSourceLockSha
    evidenceSourceSha256 = $evidenceSourceLockSha
    enhancedCaptureStatusSchemaSha256 = Get-Sha256 $enhancedStatusSchemaPath
    bridgeAwareStatusV3SchemaSha256 = $bridgeAwareStatusV3SchemaLockSha
    sharedHealthAvailabilitySchemaSha256 = $sharedHealthAvailabilitySchemaLockSha
    candidateMapAvailabilitySchemaSha256 = $candidateMapAvailabilitySchemaLockSha
}

Add-Check 'output.portability-policy' $true 'RELEASE_EXE' 'All report paths are repository-relative forward-slash paths; JSON/Markdown use UTF-8 without BOM and receive a post-serialization identity/control/mojibake scan.'

$deepGeneratedArtifacts = [Collections.Generic.List[object]]::new()
if ($null -ne $deepRecoveryEvidence) {
    foreach ($artifact in $deepRecoveryEvidence.Paths.GetEnumerator()) {
        [void]$deepGeneratedArtifacts.Add([pscustomobject][ordered]@{
            role = [string]$artifact.Key
            path = Get-PortablePath ([string]$artifact.Value)
            sha256 = [string]$deepRecoveryEvidence.SHA256[$artifact.Key]
        })
    }
}
$deepVerifiedDomainCount = if ($null -ne $deepRecoveryEvidence) {
    [int]$deepRecoveryEvidence.Runtime.VerifiedDeepDomainCount
} else { 0 }
$deepVerifiedCandidateDomainCount = if ($null -ne $deepRecoveryEvidence) {
    @($deepRecoveryEvidence.Runtime.Rows | Where-Object {
        $_.Index -ge 4 -and $_.PromotionStatus -ceq 'VERIFIED'
    }).Count
} else { 0 }
$candidateOnlyBlockedDomainCount = if ($null -ne $deepRecoveryEvidence) {
    @($deepRecoveryEvidence.Runtime.Rows | Where-Object {
        $_.Index -ge 4 -and $_.PromotionStatus -ne 'VERIFIED'
    }).Count
} else { 21 }
$deepRecoveryBinding = [ordered]@{
    status = if ($null -ne $deepRecoveryEvidence -and
        $deepCapabilityPassed -and $deepPromotionPassed -and $deepAuthorityPassed) {
        'PASS_FAIL_CLOSED_EVIDENCE_GENERATION'
    } else { 'EVIDENCE_BLOCKED_GENERATION_FAILED' }
    authorityProfile = [string]$officialAuthority.AuthorityProfile
    domainCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.DomainCount } else { 0 }
    confirmedContractCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Capability.ConfirmedContractCount } else { 0 }
    currentOfficialLiveProducerCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.CurrentOfficialLiveProducerCount } else { 0 }
    historicalOfficialLiveProducerCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.HistoricalOfficialLiveProducerCount } else { 0 }
    currentOfficialLiveVerifiedProducerCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.CurrentOfficialLiveVerifiedProducerCount } else { 0 }
    currentOfficialLiveDeepProducerCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.CurrentOfficialLiveDeepProducerCount } else { 0 }
    currentOfficialLiveVerifiedDeepProducerCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.Runtime.CurrentOfficialLiveVerifiedDeepProducerCount } else { 0 }
    producerMetricConsistency = [bool]$deepRuntimePassed
    verifiedDeepDomainCount = $deepVerifiedDomainCount
    candidateOnlyBlockedDomainCount = $candidateOnlyBlockedDomainCount
    candidateCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.CandidateMapV3.CandidateCount } else { 0 }
    promotionLedgerRowCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.PromotionLedgerCount } else { 0 }
    activationAllowedCount = if ($null -ne $deepRecoveryEvidence) { [int]$deepRecoveryEvidence.PromotionResult.ActivationAllowedCount } else { 0 }
    serverReady = if ($null -ne $deepRecoveryEvidence) { [bool]$deepRecoveryEvidence.RecoveryReadiness.ServerReady } else { $false }
    databaseReady = if ($null -ne $deepRecoveryEvidence) { [bool]$deepRecoveryEvidence.RecoveryReadiness.DatabaseReady } else { $false }
    artifacts = @($deepGeneratedArtifacts)
}

$failedChecks = @($checks | Where-Object { $_.status -eq 'FAIL' })
$pendingChecks = @($checks | Where-Object { $_.status -eq 'PENDING' })
$passedChecks = @($checks | Where-Object { $_.status -eq 'PASS' })
$dllFailures = @($checks | Where-Object { $_.scope -in @('DLL','RELEASE_EXE') -and $_.status -eq 'FAIL' })
$standaloneFailures = @($checks | Where-Object { $_.scope -eq 'DLL' -and $_.status -eq 'FAIL' })
$guiFailures = @($checks | Where-Object { $_.scope -eq 'GUI' -and $_.status -eq 'FAIL' })
$overallStatus = if ($dllFailures.Count -gt 0 -or $guiFailures.Count -gt 0) {
    'DLL_ENHANCED_CAPTURE_ACCEPTANCE_BLOCKED'
} elseif ($Final -and $guiGatePassed) {
    'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS'
} else {
    'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS_FINAL_EXE_UI_PENDING'
}
$finalExeUiStatus = if (@($checks | Where-Object { $_.scope -eq 'RELEASE_EXE' -and $_.status -eq 'FAIL' }).Count -gt 0) {
    'BLOCKED_RELEASE_EXECUTABLE_GATE'
} elseif ($guiFailures.Count -gt 0) {
    'BLOCKED_GUI_SMOKE_GATE'
} elseif ($Final -and $guiGatePassed) {
    'PASS'
} else { 'PENDING_GUI_SMOKE_VISIBILITY_INPUT' }

$report = [ordered]@{
    schemaVersion = 'god2-enhanced-capture-acceptance-v2'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    repositoryRoot = '.'
    status = $overallStatus
    standalonePayloadStatus = if ($standaloneFailures.Count -eq 0) { 'PASS' } else { 'BLOCKED' }
    finalExeUiStatus = $finalExeUiStatus
    expectedTarget = [ordered]@{
        executable = 'God2_opt.exe'; architecture = 'x86'; version = '1.0.0.1'
        sha256 = $expectedTargetSha
        activationStatus = if ($officialRuntimeObserved -and $officialIsCurrent) {
            'CURRENT_OFFICIAL_CLIENT_IDENTITY_VERIFIED'
        } elseif ($officialRuntimeObserved) {
            'HISTORICAL_OFFICIAL_CLIENT_IDENTITY_VERIFIED_CURRENT_SESSION_NOT_OBSERVED'
        } else { 'EVIDENCE_BLOCKED_OFFICIAL_CLIENT_IDENTITY_UNAVAILABLE' }
    }
    authorityConsistency = [ordered]@{
        path = Get-PortablePath $authorityConsistencyPath
        sha256 = $authorityConsistencySha256
        report = $authorityConsistencyReport
    }
    deepRecoveryEvidence = $deepRecoveryBinding
    payloads = [ordered]@{
        releaseExecutable = $releaseIdentity
        probeResource201 = [ordered]@{
            resourceId = 201; embeddedLength = [long]$probeBytes.Length; embeddedSha256 = $probeEmbeddedHash
            standalone = $probeStandalone; machine = $probePe.machine; optionalHeader = $probePe.optionalHeader
            isDll = [bool]$probePe.isDll; exports = @($probePe.exports); authenticodeStatus = $probeSignature
        }
        injectorResource202 = [ordered]@{
            resourceId = 202; embeddedLength = [long]$injectorBytes.Length; embeddedSha256 = $injectorEmbeddedHash
            standalone = $injectorStandalone; machine = $injectorPe.machine; optionalHeader = $injectorPe.optionalHeader
            isDll = [bool]$injectorPe.isDll; exports = @($injectorPe.exports); authenticodeStatus = $injectorSignature
        }
    }
    domainInventory = @($domainInventory)
    runtimeEvidence = $runtimeEvidence
    inputBindings = @($inputBindings)
    freshness = $freshness
    selfTests = [ordered]@{
        network = $networkEvidence; product = $productEvidence; evidenceFixture = $evidenceFixtureEvidence
        nativeProbe = $nativeProbeEvidence
        semanticV2 = $semanticEvidence; ultimate = $ultimateEvidence
    }
    guiVisibility = [ordered]@{
        status = if ($Final -and $guiGatePassed) { 'PASS' } elseif ($Final) { 'BLOCKED_GUI_SMOKE_GATE' } else { 'PENDING_GUI_SMOKE_VISIBILITY_INPUT' }
        evidenceObserved = [bool]$guiObserved
        path = if ($guiObserved) { Get-PortablePath $GuiSmokeEvidence } else { $null }
        expectedSmokeKeys = $expectedGuiSmokeKeys
        DllTwentyFiveDomainStatusContract = $dllTwentyFiveDomainStatusContract
        DllStrictUnloadResultContract = $dllStrictUnloadResultContract
        DllStrictUnloadFinalizationContract = $dllStrictUnloadFinalizationContract
        DllCancelResultReadBeforeCleanupContract = $dllCancelResultReadBeforeCleanupContract
        DllEnhancedCaptureConsumerStateLifecycle = $dllEnhancedCaptureConsumerStateLifecycle
        ThreeLanguageDllStrictUnloadContract = $threeLanguageDllStrictUnloadContract
        SharedTransportSlotReuseDomainAccounting = $sharedTransportSlotReuseDomainAccounting
        enhancedCaptureStatusSchemaVersion = $observedEnhancedCaptureStatusSchemaVersion
        initialStrictUnloadVerified = $observedInitialStrictUnloadVerified
        unloadPolicy = $observedStrictUnloadPolicy
        expectedExecutableSha256 = $releaseIdentity.sha256
        observedExecutableSha256 = $guiObservedExecutableSha256
        executableSha256Bound = $guiExecutableSha256Bound
    }
    checks = @($checks)
    summary = [ordered]@{
        total = $checks.Count; passed = $passedChecks.Count; failed = $failedChecks.Count
        pending = $pendingChecks.Count; candidateOnlyBlockedDomains = $candidateOnlyBlockedDomainCount
        exactFinalStatus = if ($authorityConsistencyPassed -and $officialRuntimeObserved -and
            $officialIsCurrent -and $deepVerifiedCandidateDomainCount -eq 21) {
            'ULTIMATE_DEEP_RECOVERY_LIVE_PASS'
        } else { 'ULTIMATE_INFRASTRUCTURE_PASS_DEEP_LIVE_PENDING' }
    }
}

$reportPath = Join-Path $OutputRoot 'enhanced-capture-acceptance.json'
$markdownPath = Join-Path $OutputRoot 'enhanced-capture-acceptance.md'
$utf8NoBom = New-Object Text.UTF8Encoding($false, $true)
$jsonText = $report | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($reportPath, $jsonText + "`n", $utf8NoBom)

$markdown = New-Object Text.StringBuilder
[void]$markdown.AppendLine('# God2 DLL Enhanced Capture Acceptance')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Status: **$overallStatus**")
[void]$markdown.AppendLine("- Standalone payload: **$($report.standalonePayloadStatus)**")
[void]$markdown.AppendLine("- Final EXE/UI: **$finalExeUiStatus**")
[void]$markdown.AppendLine("- Checks: $($checks.Count) total / $($passedChecks.Count) pass / $($failedChecks.Count) fail / $($pendingChecks.Count) pending")
[void]$markdown.AppendLine("- Candidate-only blocked domains: **$candidateOnlyBlockedDomainCount**")
[void]$markdown.AppendLine("- Verified runtime domains: **$deepVerifiedDomainCount / 25**")
[void]$markdown.AppendLine("- Authority consistency: **$($authorityConsistencyReport.Status)** ($($authorityConsistencyReport.ConflictCount) conflicts)")
[void]$markdown.AppendLine("- Exact final status: **$($report.summary.exactFinalStatus)**")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Embedded payload identity')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- RCDATA 201: $probeEmbeddedHash ($($probeBytes.Length) bytes)")
[void]$markdown.AppendLine("- RCDATA 202: $injectorEmbeddedHash ($($injectorBytes.Length) bytes)")
[void]$markdown.AppendLine("- Release EXE: $($releaseIdentity.sha256)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Checks')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Status | Scope | Check | Evidence |')
[void]$markdown.AppendLine('|---|---|---|---|')
foreach ($check in $checks) {
    $safeEvidence = ([string]$check.evidence).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
    [void]$markdown.AppendLine("| $($check.status) | $($check.scope) | $($check.id) | $safeEvidence |")
}
[IO.File]::WriteAllText($markdownPath, $markdown.ToString(), $utf8NoBom)

$portabilityIssues = New-Object Collections.ArrayList
$identityNeedles = @($env:USERNAME, $env:COMPUTERNAME, $env:USERDOMAIN) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.Length -ge 3 } |
    Sort-Object -Unique
foreach ($artifactPath in @($reportPath, $markdownPath, $authorityConsistencyPath)) {
    $artifactBytes = [IO.File]::ReadAllBytes($artifactPath)
    if ($artifactBytes.Length -ge 3 -and $artifactBytes[0] -eq 0xEF -and
        $artifactBytes[1] -eq 0xBB -and $artifactBytes[2] -eq 0xBF) {
        [void]$portabilityIssues.Add("$(Get-PortablePath $artifactPath):UTF8_BOM")
    }
    $artifactText = $utf8NoBom.GetString($artifactBytes)
    foreach ($issue in @(Get-PortableTextContractIssues $artifactText)) {
        [void]$portabilityIssues.Add("$(Get-PortablePath $artifactPath):$issue")
    }
    foreach ($identity in $identityNeedles) {
        if ($artifactText.IndexOf($identity, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            [void]$portabilityIssues.Add("$(Get-PortablePath $artifactPath):LOCAL_IDENTITY_$identity")
        }
    }
}
if ($portabilityIssues.Count -ne 0) {
    throw "Generated acceptance artifacts failed portability scan: $($portabilityIssues -join ';')"
}

Write-Output "status=$overallStatus"
Write-Output "standalonePayloadStatus=$($report.standalonePayloadStatus)"
Write-Output "finalExeUiStatus=$finalExeUiStatus"
Write-Output "report=$reportPath"
Write-Output "markdown=$markdownPath"
Write-Output "checks=$($checks.Count) passed=$($passedChecks.Count) failed=$($failedChecks.Count) pending=$($pendingChecks.Count)"
if ($dllFailures.Count -gt 0 -or $guiFailures.Count -gt 0) { exit 4 }
exit 0
