[CmdletBinding()]
param(
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$schemaPath = Join-Path $PSScriptRoot 'assets\schemas\god2-semantic-event-v2.schema.json'
$headerPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'SemanticEventV2Schema.generated.h'
$utf8NoBom = New-Object Text.UTF8Encoding($false, $true)

function New-ScalarSchema {
    param([Parameter(Mandatory=$true)][ValidateSet('string','boolean','integer')][string]$Type)
    $schema = [ordered]@{ type = $Type }
    if ($Type -eq 'integer') { $schema.minimum = 0 }
    return $schema
}

function New-PayloadDefinition {
    param(
        [Parameter(Mandatory=$true)][Collections.IDictionary]$Fields,
        [string[]]$Required
    )
    if ($null -eq $Required -or $Required.Count -eq 0) {
        $Required = @($Fields.Keys | ForEach-Object { [string]$_ })
    }
    return [ordered]@{
        type = 'object'
        additionalProperties = $false
        required = $Required
        properties = $Fields
    }
}

function New-Fields {
    param([Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$Specs)
    $fields = [ordered]@{}
    foreach ($spec in $Specs) {
        $parts = $spec.Split(':', 2)
        $type = switch ($parts[1]) {
            's' { 'string' }
            'b' { 'boolean' }
            'i' { 'integer' }
            default { throw "Unknown field type: $spec" }
        }
        $fields[$parts[0]] = New-ScalarSchema $type
    }
    return $fields
}

function Set-Const {
    param(
        [Parameter(Mandatory=$true)][Collections.IDictionary]$Definition,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)]$Value
    )
    $Definition.properties[$Name].const = $Value
}

function Set-Enum {
    param(
        [Parameter(Mandatory=$true)][Collections.IDictionary]$Definition,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][object[]]$Values
    )
    $Definition.properties[$Name].enum = $Values
}

$definitions = [ordered]@{}
$branches = New-Object Collections.Generic.List[object]

function Add-Definition {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$Specs,
        [string[]]$Required
    )
    $definition = New-PayloadDefinition (New-Fields $Specs) $Required
    $definitions[$Name] = $definition
    return $definition
}

function Add-ProfileBranch {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][string]$Definition,
        [string[]]$EventTypes,
        [AllowNull()][object[]]$Sources,
        [Collections.IDictionary]$SourceSchema,
        [Collections.IDictionary]$RootProperties,
        [Collections.IDictionary]$PayloadConstraint
    )
    $properties = [ordered]@{}
    if ($null -ne $EventTypes -and $EventTypes.Count -gt 0) {
        $properties.EventType = if ($EventTypes.Count -eq 1) {
            [ordered]@{ const = $EventTypes[0] }
        } else {
            [ordered]@{ enum = $EventTypes }
        }
    }
    if ($null -ne $SourceSchema) {
        $properties.SourceToken = $SourceSchema
    } elseif ($null -ne $Sources) {
        $properties.SourceToken = [ordered]@{ enum = $Sources }
    }
    if ($null -ne $RootProperties) {
        foreach ($key in $RootProperties.Keys) { $properties[$key] = $RootProperties[$key] }
    }
    $payload = [ordered]@{ '$ref' = ('#/$defs/' + $Definition) }
    if ($null -ne $PayloadConstraint) {
        $payload = [ordered]@{ allOf = @($payload, $PayloadConstraint) }
    }
    $properties.Payload = $payload
    $branches.Add([ordered]@{
        title = $Name
        required = @($properties.Keys | ForEach-Object { [string]$_ })
        properties = $properties
    })
}

$eventTypes = @(
    'PacketBoundary','ParserRead','SerializerWrite','HandlerInvocation','HandlerArgument',
    'ObjectAllocated','ObjectDestroyed','ObjectResolved','ObjectLookup','RegistryLocated',
    'RegistryEnumerated','ResourceRead','ResourceDecoded','ResourceDeserialized',
    'StateMutation','TaintSeed','TaintPropagation','ValueFlow','FormulaOperand',
    'FormulaResult','FunctionRoleCandidate','UIAnchor','SnapshotObject','SnapshotEdge',
    'ProbeDiagnostic'
)
$emptySource = @('', $null)

$def = Add-Definition 'ultimateEmptyFixture' @()
Add-ProfileBranch 'UltimateEmptyFixture' 'ultimateEmptyFixture' `
    @('PacketBoundary','ParserRead','SnapshotObject','ProbeDiagnostic') $emptySource

$def = Add-Definition 'ultimateTwentyFiveTypeFixture' @('FixtureOnly:b','RuntimeObservation:b')
Set-Const $def 'FixtureOnly' $true
Set-Const $def 'RuntimeObservation' $false
Add-ProfileBranch 'UltimateTwentyFiveTypeFixture' 'ultimateTwentyFiveTypeFixture' `
    $eventTypes $emptySource

Add-Definition 'ultimatePacketBoundaryRecovery' @(
    'Direction:s','Opcode:s','Subsystem:s','ActionFamily:s','FrameLength:s',
    'FrameLengthRule:s','EncryptionBoundary:s') | Out-Null
Add-ProfileBranch 'UltimatePacketBoundaryRecovery' 'ultimatePacketBoundaryRecovery' `
    @('PacketBoundary') $emptySource

Add-Definition 'ultimateParserReadRecovery' @(
    'Direction:s','Opcode:s','Subsystem:s','PacketOffset:s','Width:s','ValueType:s',
    'Endian:s','SemanticCandidate:s','TypedSource:s') | Out-Null
Add-ProfileBranch 'UltimateParserReadRecovery' 'ultimateParserReadRecovery' `
    @('ParserRead') $emptySource

Add-Definition 'ultimateParserReadBoundField' @(
    'Direction:s','Opcode:s','Width:i','ValueType:s','FrameOffset:i') | Out-Null
Add-ProfileBranch 'UltimateParserReadBoundField' 'ultimateParserReadBoundField' `
    @('ParserRead') $null ([ordered]@{ type='string'; pattern='^PF-.+' })

Add-Definition 'ultimateParserReadBoundFieldNoOffset' @(
    'Direction:s','Opcode:s','Width:i','ValueType:s') | Out-Null
Add-ProfileBranch 'UltimateParserReadBoundFieldNoOffset' `
    'ultimateParserReadBoundFieldNoOffset' @('ParserRead') $null `
    ([ordered]@{ type='string'; pattern='^PF-.+' })

Add-Definition 'ultimateSerializerWriteRecovery' @(
    'Direction:s','Opcode:s','Subsystem:s','PacketOffset:s','Width:s','ValueType:s','Endian:s') | Out-Null
Add-ProfileBranch 'UltimateSerializerWriteRecovery' 'ultimateSerializerWriteRecovery' `
    @('SerializerWrite') $emptySource

Add-Definition 'ultimateObjectAllocatedRecovery' @(
    'AllocationSize:s','VTable:s','ConstructorPath:s','Factory:s') | Out-Null
Add-ProfileBranch 'UltimateObjectAllocatedRecovery' 'ultimateObjectAllocatedRecovery' `
    @('ObjectAllocated') $emptySource

Add-Definition 'ultimateObjectResolvedRecovery' @(
    'EntityFamily:s','StableTemplateId:s','Property:s','RawValue:s','NormalizedValue:s','ValueLayer:s') | Out-Null
Add-ProfileBranch 'UltimateObjectResolvedRecovery' 'ultimateObjectResolvedRecovery' `
    @('ObjectResolved') $emptySource

Add-Definition 'ultimateObjectResolvedVerifiedRecovery' @(
    'EntityFamily:s','StableTemplateId:s','Property:s','RawValue:s','NormalizedValue:s',
    'ValueLayer:s','ExactBuildBinding:s','TypedSource:s','StableContext:s','CausalPath:s',
    'VerifiedConsumerOrMutation:s') | Out-Null
Add-ProfileBranch 'UltimateObjectResolvedVerifiedRecovery' `
    'ultimateObjectResolvedVerifiedRecovery' @('ObjectResolved') $emptySource

Add-Definition 'ultimateObjectResolvedSafeFixture' @('Property:s','RawValue:s') | Out-Null
Add-ProfileBranch 'UltimateObjectResolvedSafeFixture' 'ultimateObjectResolvedSafeFixture' `
    @('ObjectResolved') @('SAFE-SOURCE-2048')

Add-Definition 'ultimateRegistryEnumeratedRecovery' @(
    'RegistryName:s','EntityFamily:s','RecordCount:s','ReadOnly:s','LookupConsumer:s') | Out-Null
Add-ProfileBranch 'UltimateRegistryEnumeratedRecovery' 'ultimateRegistryEnumeratedRecovery' `
    @('RegistryEnumerated') $emptySource

Add-Definition 'ultimateRegistryEnumeratedMeasuredFixture' @(
    'RegistryName:s','EntityFamily:s','RecordCount:i','ReadOnly:b') | Out-Null
Add-ProfileBranch 'UltimateRegistryEnumeratedMeasuredFixture' `
    'ultimateRegistryEnumeratedMeasuredFixture' @('RegistryEnumerated') $emptySource

Add-Definition 'ultimateResourceDeserializedRecovery' @(
    'RelativePath:s','FileSha256:s','DecodedBufferSha256:s','SourceOffset:s','DecodeRVA:s',
    'DeserializeRVA:s','Destination:s','RecordCount:s','Provenance:s') | Out-Null
Add-ProfileBranch 'UltimateResourceDeserializedRecovery' 'ultimateResourceDeserializedRecovery' `
    @('ResourceDeserialized') $emptySource

Add-Definition 'ultimateResourceDeserializedMeasuredFixture' @(
    'RelativePath:s','EntityFamily:s','RecordCount:i','SchemaCandidate:s') | Out-Null
Add-ProfileBranch 'UltimateResourceDeserializedMeasuredFixture' `
    'ultimateResourceDeserializedMeasuredFixture' @('ResourceDeserialized') $emptySource

Add-Definition 'ultimateStateMutationRoundTripFixture' @('Before:s','After:s') | Out-Null
Add-ProfileBranch 'UltimateStateMutationRoundTripFixture' `
    'ultimateStateMutationRoundTripFixture' @('StateMutation') @('Source:77')

Add-Definition 'ultimateStateMutationObservedFixture' @(
    'ObjectIdentity:s','PropertyCandidate:s','Type:s','BeforeValue:s','InputValue:s',
    'AfterValue:s','TriggerAction:s','WriterRVA:s') | Out-Null
Add-ProfileBranch 'UltimateStateMutationObservedFixture' `
    'ultimateStateMutationObservedFixture' @('StateMutation') $emptySource

Add-Definition 'ultimateStateMutationContradictionFixture' @(
    'ObjectIdentity:s','PropertyCandidate:s','Type:s','BeforeValue:s','InputValue:s',
    'AfterValue:s','TriggerAction:s','WriterRVA:s','Contradictions:i') | Out-Null
Add-ProfileBranch 'UltimateStateMutationContradictionFixture' `
    'ultimateStateMutationContradictionFixture' @('StateMutation') $emptySource

Add-Definition 'ultimateStateMutationQuestRecovery' @(
    'ObjectIdentity:s','PropertyCandidate:s','Type:s','BeforeValue:s','InputValue:s',
    'AfterValue:s','TriggerAction:s','WriterRVA:s','MachineId:s','PreviousState:s',
    'NextState:s','ReplayConsistent:s') | Out-Null
Add-ProfileBranch 'UltimateStateMutationQuestRecovery' 'ultimateStateMutationQuestRecovery' `
    @('StateMutation') $emptySource

Add-Definition 'ultimateStateMutationVerifiedRecovery' @(
    'ObjectIdentity:s','PropertyCandidate:s','Type:s','BeforeValue:s','InputValue:s',
    'AfterValue:s','TriggerPacket:s','TriggerHandler:s','WriterRVA:s','Consumers:s',
    'MachineId:s','PreviousState:s','NextState:s','ReplayConsistent:s','ExactBuildBinding:s',
    'TypedSource:s','StableContext:s','CausalPath:s','VerifiedConsumerOrMutation:s') | Out-Null
Add-ProfileBranch 'UltimateStateMutationVerifiedRecovery' `
    'ultimateStateMutationVerifiedRecovery' @('StateMutation') $emptySource

Add-Definition 'ultimateValueFlowRecovery' @('Operation:s','PropagationHops:s') | Out-Null
Add-ProfileBranch 'UltimateTaintSeedRecovery' 'ultimateValueFlowRecovery' `
    @('TaintSeed') @('Packet:0x41:+4')
Add-ProfileBranch 'UltimateTaintPropagationRecovery' 'ultimateValueFlowRecovery' `
    @('TaintPropagation') @('Value:MonsterId')
Add-ProfileBranch 'UltimateValueFlowRecovery' 'ultimateValueFlowRecovery' `
    @('ValueFlow') @('Object:MonsterTemplate:4102')

Add-Definition 'ultimateFormulaOperandRecovery' @(
    'FormulaId:s','FormulaCategory:s','Expression:s','Operand:s') | Out-Null
Add-ProfileBranch 'UltimateFormulaOperandRecovery' 'ultimateFormulaOperandRecovery' `
    @('FormulaOperand') $emptySource

Add-Definition 'ultimateFormulaResultRecovery' @(
    'FormulaId:s','FormulaCategory:s','Expression:s','Result:s') | Out-Null
Add-ProfileBranch 'UltimateFormulaResultRecovery' 'ultimateFormulaResultRecovery' `
    @('FormulaResult') $emptySource

Add-Definition 'ultimateFormulaCounterexampleFixture' @(
    'FormulaId:s','Expression:s','Result:s','Contradictions:s') | Out-Null
Add-ProfileBranch 'UltimateFormulaCounterexampleFixture' `
    'ultimateFormulaCounterexampleFixture' @('FormulaResult') $emptySource

$def = Add-Definition 'ultimateProbeIsolationDiagnostic' @('Status:s','Domain:s')
Set-Const $def 'Status' 'ProbeExceptionIsolated'
Add-ProfileBranch 'UltimateProbeIsolationDiagnostic' 'ultimateProbeIsolationDiagnostic' `
    @('ProbeDiagnostic') $emptySource

$def = Add-Definition 'evidenceBlockedSensitivePayload' @(
    'Status:s','OriginalPayloadPersisted:b','Authority:s')
Set-Const $def 'Status' 'EvidenceBlockedSensitivePayloadSuppressed'
Set-Const $def 'OriginalPayloadPersisted' $false
Set-Const $def 'Authority' 'UNKNOWN'
Add-ProfileBranch 'EvidenceBlockedSensitivePayload' 'evidenceBlockedSensitivePayload' `
    $eventTypes $emptySource $null `
    ([ordered]@{ SensitiveMaskStatus=[ordered]@{ const='SensitivePayloadSuppressedMetadataOnly' } })

$def = Add-Definition 'evidenceBlockedUnclassifiedPayload' @(
    'Status:s','OriginalPayloadPersisted:b','Authority:s')
Set-Const $def 'Status' 'EvidenceBlockedInvalidOrUnclassifiedPayload'
Set-Const $def 'OriginalPayloadPersisted' $false
Set-Const $def 'Authority' 'UNKNOWN'
Add-ProfileBranch 'EvidenceBlockedUnclassifiedPayload' 'evidenceBlockedUnclassifiedPayload' `
    $eventTypes $null $null `
    ([ordered]@{ SensitiveMaskStatus=[ordered]@{ const='UnclassifiedPayloadSuppressedMetadataOnly' } })

$compatSpecs = @(
    'Profile:s','Canonical:b','Promotable:b','Direction:s','Opcode:s','FrameOffset:s',
    'DestinationOffset:s','Width:s','ReadType:s','WriteType:s','ArgumentType:s','Endian:s',
    'FieldSemantic:s','HookInvocationId:s','ProtocolFrameId:s','LogicalMessageId:s','ProbeId:s',
    'EvidenceBasis:s')
$def = Add-Definition 'canonicalizedV1Compatibility' $compatSpecs `
    @('Profile','Canonical','Promotable')
Set-Const $def 'Profile' 'NonCanonicalV1CompatibilityInput'
Set-Const $def 'Canonical' $false
Set-Const $def 'Promotable' $false
Add-ProfileBranch 'CanonicalizedNonPromotableV1Compatibility' `
    'canonicalizedV1Compatibility' $eventTypes $null $null `
    ([ordered]@{ AuthorityHint=[ordered]@{ const='UNKNOWN' } })

$def = Add-Definition 'nativeSemanticTypeFixture' @(
    'FixtureOnly:b','RuntimeObservation:b','DefinitionIndex:i','ExpectedEventType:s')
Set-Const $def 'FixtureOnly' $true
Set-Const $def 'RuntimeObservation' $false
for ($index = 0; $index -lt $eventTypes.Count; ++$index) {
    Add-ProfileBranch "NativeSemanticTypeFixture.$index" 'nativeSemanticTypeFixture' `
        @($eventTypes[$index]) @('NativeSemanticTypeFixture') $null $null `
        ([ordered]@{ properties=[ordered]@{
            DefinitionIndex=[ordered]@{ const=$index }
            ExpectedEventType=[ordered]@{ const=$eventTypes[$index] }
        } })
}

$def = Add-Definition 'hookThreadSchemaBatchPrivateFixture' @('FixtureOnly:b','Row:i')
Set-Const $def 'FixtureOnly' $true
Set-Enum $def 'Row' @(1,2)
Add-ProfileBranch 'HookThreadSchemaBatchPrivateFixture' `
    'hookThreadSchemaBatchPrivateFixture' @('ProbeDiagnostic') `
    @('HookThreadSchemaBatchPrivateFixture')

$def = Add-Definition 'faultPriorityPressureFixture' @('FixtureOnly:b','FaultPriorityPressure:b')
Set-Const $def 'FixtureOnly' $true
Set-Const $def 'FaultPriorityPressure' $true
Add-ProfileBranch 'FaultPriorityPressureFixture' 'faultPriorityPressureFixture' `
    @('ProbeDiagnostic') @('PerDomainFaultIsolation')

$def = Add-Definition 'faultReservePressureFixture' @(
    'FixtureOnly:b','ReservePressure:b','RuntimeObservation:b')
Set-Const $def 'FixtureOnly' $true
Set-Const $def 'ReservePressure' $true
Set-Const $def 'RuntimeObservation' $false
Add-ProfileBranch 'FaultReservePressureFixture' 'faultReservePressureFixture' `
    @('ProbeDiagnostic') @('PerDomainFaultIsolation')

$def = Add-Definition 'perDomainFaultIsolationDiagnostic' @(
    'Status:s','DomainIndex:i','FaultCode:s','AffectedDomainDisabled:b',
    'OtherDomainsRemainEnabled:b','OtherDomainEnabledCount:i','OtherDomainEnabledMask:s',
    'EvidenceIncomplete:b','SensitivePayloadPersisted:b')
Set-Const $def 'Status' 'ProbeDomainDisabledAfterIsolatedFault'
Set-Const $def 'AffectedDomainDisabled' $true
Set-Const $def 'SensitivePayloadPersisted' $false
$def.properties.DomainIndex.maximum = 24
$def.properties.OtherDomainEnabledCount.maximum = 24
Add-ProfileBranch 'PerDomainFaultIsolationDiagnostic' 'perDomainFaultIsolationDiagnostic' `
    @('ProbeDiagnostic') @('PerDomainFaultIsolation')

$probeRows = @(
    @('Network','WinsockTransport','PacketBoundary','ProtocolBoundary'),
    @('Parser','PacketDecode.FrameBoundary','ParserRead','TypedProtocolField'),
    @('Serializer','OutboundEnqueue.FrameBuilder','SerializerWrite','TypedProtocolField'),
    @('Handler','Battle.HandlerRecordLength','HandlerArgument','HandlerDispatch'),
    @('Object','ObjectResolver','ObjectResolved','ObjectResolver'),
    @('Allocation','AllocationProbe','ObjectAllocated','ObjectResolver'),
    @('VTable','VTableProbe','FunctionRoleCandidate','CandidateDiscovery'),
    @('Factory','FactoryProbe','FunctionRoleCandidate','CandidateDiscovery'),
    @('ManagerLookup','ManagerLookupProbe','ObjectLookup','ObjectResolver'),
    @('Registry','RegistryProbe','RegistryLocated','RegistryRecovery'),
    @('ResourceDecode','ResourceDecodeProbe','ResourceDecoded','ResourceRecovery'),
    @('Mutation','MutationProbe','StateMutation','MutationRecovery'),
    @('TaintSeed','TaintSeedProbe','TaintSeed','ValueProvenance'),
    @('FormulaOperand','FormulaOperandProbe','FormulaOperand','FormulaRecovery'),
    @('Quest','QuestProbe','StateMutation','MutationRecovery'),
    @('Map','MapProbe','UIAnchor','ContentRecovery'),
    @('Portal','PortalProbe','SnapshotEdge','SnapshotRecovery'),
    @('NPC','NPCProbe','ObjectResolved','ObjectResolver'),
    @('Monster','MonsterProbe','ObjectResolved','ObjectResolver'),
    @('Battle','BattleProbe','StateMutation','MutationRecovery'),
    @('Inventory','InventoryProbe','StateMutation','MutationRecovery'),
    @('Item','ItemProbe','ObjectResolved','ObjectResolver'),
    @('Skill','SkillProbe','FormulaOperand','FormulaRecovery'),
    @('Pet/Mount','PetMountProbe','ObjectResolved','ObjectResolver'),
    @('Snapshot','SnapshotProbe','SnapshotObject','SnapshotRecovery')
)

Add-Definition 'probeDomainStatus' @(
    'Domain:s','Probe:s','Status:s','Reason:s','ContractStatus:s',
    'ActivationAllowedByContract:b','RuntimeDiagnosticObserved:b',
    'RuntimeActivationObserved:b','TargetIdentityVerified:b','FaultIsolation:s') | Out-Null
for ($index = 0; $index -lt $probeRows.Count; ++$index) {
    $row = $probeRows[$index]
    $common = [ordered]@{
        Domain=[ordered]@{ const=$row[0] }
        Probe=[ordered]@{ const=$row[1] }
        RuntimeDiagnosticObserved=[ordered]@{ const=$true }
        FaultIsolation=[ordered]@{ const='PerDomain' }
    }
    if ($index -lt 4) {
        $common.ContractStatus = [ordered]@{ const='ConfirmedContract' }
        $common.ActivationAllowedByContract = [ordered]@{ const=$true }
        $state = @(
            [ordered]@{ properties=[ordered]@{
                RuntimeActivationObserved=[ordered]@{ const=$true }
                TargetIdentityVerified=[ordered]@{ const=$true }
                Status=[ordered]@{ const='Active' }
                Reason=[ordered]@{ const='VerifiedProbeInstalled' }
            } },
            [ordered]@{ properties=[ordered]@{
                RuntimeActivationObserved=[ordered]@{ const=$false }
                Status=[ordered]@{ const='CurrentTargetIdentityBlocked' }
                Reason=[ordered]@{ const='ConfirmedContractNotActivatedForCurrentTargetIdentity' }
            } }
        )
        $constraint = [ordered]@{ allOf=@(
            [ordered]@{ properties=$common },
            [ordered]@{ oneOf=$state }
        ) }
    } else {
        $common.ContractStatus = [ordered]@{ const='CandidateOnlyBlockedContract' }
        $common.ActivationAllowedByContract = [ordered]@{ const=$false }
        $common.RuntimeActivationObserved = [ordered]@{ const=$false }
        $common.Status = [ordered]@{ const='EvidenceBlockedUnconfirmedProbe' }
        $common.Reason = [ordered]@{ const='NoVerifiedTargetRva' }
        $constraint = [ordered]@{ properties=$common }
    }
    Add-ProfileBranch "ProbeDomainStatus.$index" 'probeDomainStatus' `
        @('ProbeDiagnostic') @($row[1]) $null $null $constraint
}

$def = Add-Definition 'functionRoleCandidateStatus' @(
    'Status:s','Domain:s','Probe:s','CandidateCount:i','ActiveHook:b',
    'CandidateEventType:s','IntendedVerifiedEventType:s','DispatchCapability:s',
    'VerificationNeed:s')
Set-Const $def 'Status' 'EvidenceBlockedUnconfirmedProbe'
Set-Const $def 'ActiveHook' $false
Set-Const $def 'CandidateEventType' 'FunctionRoleCandidate'
$def.properties.CandidateCount.maximum = 4
for ($index = 4; $index -lt $probeRows.Count; ++$index) {
    $row = $probeRows[$index]
    $constraint = [ordered]@{ properties=[ordered]@{
        Domain=[ordered]@{ const=$row[0] }
        Probe=[ordered]@{ const=$row[1] }
        IntendedVerifiedEventType=[ordered]@{ const=$row[2] }
        DispatchCapability=[ordered]@{ const=$row[3] }
    } }
    Add-ProfileBranch "FunctionRoleCandidateStatus.$index" `
        'functionRoleCandidateStatus' @('FunctionRoleCandidate') @($row[1]) `
        $null $null $constraint
}

$def = Add-Definition 'winsockTransportChunkDiagnostic' @(
    'Status:s','Direction:s','Api:i','RequestedLength:i','TransferredLength:i',
    'CapturedLength:i','SensitivePayloadSuppressed:b','SemanticPayloadContainsNetworkBytes:b')
Set-Const $def 'Status' 'TransportChunkNotBoundary'
Set-Const $def 'SemanticPayloadContainsNetworkBytes' $false
Add-ProfileBranch 'WinsockTransportChunkDiagnostic' 'winsockTransportChunkDiagnostic' `
    @('ProbeDiagnostic') @('WinsockTransportChunk')

$def = Add-Definition 'verifiedPlaintextFrameBoundary' @(
    'Status:s','Direction:s','Api:i','FrameLength:i','Opcode:i','ChecksumVerified:b',
    'ExactTargetIdentity:b','SemanticPayloadContainsNetworkBytes:b')
Set-Const $def 'Status' 'VerifiedPlaintextFrameBoundary'
Set-Const $def 'ChecksumVerified' $true
Set-Const $def 'ExactTargetIdentity' $true
Set-Const $def 'SemanticPayloadContainsNetworkBytes' $false
Add-ProfileBranch 'VerifiedPlaintextFrameBoundary' 'verifiedPlaintextFrameBoundary' `
    @('PacketBoundary') @('PacketDecode.FrameBoundary','OutboundEnqueue.FrameBuilder')

Add-Definition 'verifiedTypedProtocolField' @(
    'Direction:s','Opcode:s','FrameOffset:i','Width:i','ValueType:s','RawBytes:s',
    'ParsedValue:i','FieldSemantic:s') | Out-Null
Add-ProfileBranch 'VerifiedTypedProtocolField' 'verifiedTypedProtocolField' `
    @('ParserRead','SerializerWrite') $null ([ordered]@{ type='string'; pattern='^PF-.+' })

Add-Definition 'verifiedHandlerInvocation' @(
    'ArgumentIndex:i','ArgumentType:s','ArgumentValue:i') | Out-Null
Add-ProfileBranch 'VerifiedHandlerInvocation' 'verifiedHandlerInvocation' `
    @('HandlerInvocation') $null ([ordered]@{ type=@('string','null'); pattern='^(PF-.+)?$' })

$def = Add-Definition 'verifiedHandlerArgument' @(
    'Status:s','ArgumentIndex:i','ArgumentType:s','ArgumentValue:i',
    'ObjectPointerPersisted:b','SensitiveValue:b')
Set-Const $def 'Status' 'VerifiedHandlerArgument'
Set-Const $def 'ObjectPointerPersisted' $false
Set-Const $def 'SensitiveValue' $false
Add-ProfileBranch 'VerifiedHandlerArgument' 'verifiedHandlerArgument' `
    @('HandlerArgument') @('Battle.HandlerRecordLength')

$def = Add-Definition 'sharedPriorityReserveFillFixture' @(
    'FixtureOnly:b','RuntimeObservation:b','Phase:s','Index:i','PriorityExpected:i','ProcessId:i')
Set-Const $def 'FixtureOnly' $true
Set-Const $def 'RuntimeObservation' $false
Set-Const $def 'Phase' 'ReserveFill'
Set-Const $def 'PriorityExpected' 0
$def.properties.ProcessId.minimum = 1
Add-ProfileBranch 'SharedPriorityReserveFillFixture' 'sharedPriorityReserveFillFixture' `
    @('PacketBoundary') @('SharedPriorityReserveFill')

$properties = [ordered]@{
    SchemaId = [ordered]@{ type='string'; const='God2SemanticEvent' }
    SchemaVersion = [ordered]@{ type='integer'; const=2 }
    EventType = [ordered]@{ enum=$eventTypes }
    EventId = [ordered]@{ type='string'; minLength=1 }
    Sequence = [ordered]@{ type='integer'; minimum=0 }
    Timestamp = [ordered]@{ type=@('string','number') }
    ThreadId = [ordered]@{ type='integer'; minimum=0 }
    ProcessId = [ordered]@{ type='integer'; minimum=1 }
    SessionId = [ordered]@{ type='string'; minLength=1 }
    ClientBuildId = [ordered]@{ type='string'; minLength=1 }
    ModuleId = [ordered]@{ type='string'; minLength=1 }
    RVA = [ordered]@{ type=@('integer','string','null') }
    CallsiteRVA = [ordered]@{ type=@('integer','string','null') }
    ParentEventId = [ordered]@{ type=@('string','null') }
    ContextId = [ordered]@{ type=@('string','null') }
    ActionId = [ordered]@{ type=@('string','null') }
    ObjectToken = [ordered]@{ type=@('string','null') }
    ValueToken = [ordered]@{ type=@('string','null') }
    SourceToken = [ordered]@{ type=@('string','null') }
    AuthorityHint = [ordered]@{ enum=@('VERIFIED','DERIVED','OBSERVED','HYPOTHESIS','UNKNOWN','UNKNOWN_SERVER_ONLY','REJECTED') }
    SensitiveMaskStatus = [ordered]@{ type='string'; minLength=1 }
    # Payload is required here and is fully constrained by the exactly-one
    # profile branch below.  Do not declare a second open object schema at the
    # envelope level: every accepted payload must satisfy one closed $defs
    # profile.
    Payload = [ordered]@{}
    SemanticEventId = [ordered]@{ type='string'; minLength=1 }
}
$caseSensitiveProperties = New-Object Collections.Specialized.OrderedDictionary `
    ([StringComparer]::Ordinal)
foreach ($entry in $properties.GetEnumerator()) {
    $caseSensitiveProperties.Add([string]$entry.Key, $entry.Value)
}
$properties = $caseSensitiveProperties

$companions = @(
    'ObservedAtUnixMs','Qpc','Direction','Opcode','ProtocolFrameId','LogicalMessageId',
    'ActionInstanceId','HookInvocationId','ParentInvocationId','ContextInvocationId',
    'ExactParentSemanticEventId','ContextSemanticEventId','ContextCorrelationBasis',
    'HandlerInvocationId','ProbeId','ProbeCategory','Module','Rva','CallerRva',
    'ProbeContractVersion','ClientSha256Expected','BuildBindingStatus','EvidenceBasis',
    'EvidenceLevel','FrameOffset','FrameOffsetStatus','DestinationOffset','Width',
    'ReadType','WriteType','Endian','RawBytes','ParsedValue','Value','ValueTokenId',
    'FieldSemantic','AuthorityClassification','SensitiveValue','ArgumentIndex',
    'ArgumentType','ArgumentValue','AssociatedValueTokenId','ObjectPointerPersisted')
$numericCompanions = @('ObservedAtUnixMs','Qpc','FrameOffset','DestinationOffset','Width',
    'ParsedValue','Value','ArgumentIndex','ArgumentValue')
$booleanCompanions = @('SensitiveValue','ObjectPointerPersisted')
$nullableCompanions = @('ProtocolFrameId','LogicalMessageId','ActionInstanceId',
    'ParentInvocationId','ContextInvocationId','ExactParentSemanticEventId','ContextSemanticEventId')
foreach ($name in $companions) {
    # PowerShell 5 ConvertFrom-Json is case-insensitive and rejects an object
    # containing both canonical RVA and legacy companion Rva.  Keep canonical
    # RVA in properties and admit the exact legacy spelling through a closed,
    # anchored patternProperty.
    if ($name -ceq 'Rva') { continue }
    $properties[$name] = if ($numericCompanions -contains $name) {
        [ordered]@{ type=@('number','string') }
    } elseif ($booleanCompanions -contains $name) {
        [ordered]@{ type=@('boolean','string') }
    } elseif ($nullableCompanions -contains $name) {
        [ordered]@{ type=@('string','null') }
    } else {
        [ordered]@{ type='string' }
    }
}

$branchArray = @($branches | ForEach-Object { $_ })
$schema = [ordered]@{
    '$schema' = 'https://json-schema.org/draft/2020-12/schema'
    '$id' = 'https://god2.local/schemas/god2-semantic-event-v2.schema.json'
    title = 'God2SemanticEvent v2 recursively closed canonical payload profiles'
    type = 'object'
    additionalProperties = $false
    required = @('SchemaId','SchemaVersion','EventType','EventId','Sequence','Timestamp',
        'ThreadId','ProcessId','SessionId','ClientBuildId','ModuleId','RVA','CallsiteRVA',
        'ParentEventId','ContextId','ActionId','ObjectToken','ValueToken','SourceToken',
        'AuthorityHint','SensitiveMaskStatus','Payload')
    properties = $properties
    patternProperties = [ordered]@{ '^Rva$'=[ordered]@{ type='string' } }
    allOf = @([ordered]@{ oneOf=$branchArray })
    '$defs' = $definitions
}

$schemaText = ($schema | ConvertTo-Json -Depth 100 -Compress) + "`n"
$schemaChunks = New-Object Collections.Generic.List[string]
$chunkSize = 8000
for ($offset = 0; $offset -lt $schemaText.Length; $offset += $chunkSize) {
    $length = [Math]::Min($chunkSize, $schemaText.Length - $offset)
    $schemaChunks.Add($schemaText.Substring($offset, $length))
}
$header = New-Object Text.StringBuilder
[void]$header.Append("#pragma once`n`n#include <array>`n#include <string_view>`n`n")
[void]$header.Append("namespace god2 {`n")
[void]$header.Append("inline constexpr std::array<std::string_view, $($schemaChunks.Count)> ")
[void]$header.Append("kGod2SemanticEventV2ClosedSchemaChunks{{`n")
for ($index = 0; $index -lt $schemaChunks.Count; ++$index) {
    $delimiter = "G2V2C$index"
    [void]$header.Append("    R`"$delimiter(")
    [void]$header.Append($schemaChunks[$index])
    [void]$header.Append(")$delimiter`"")
    if ($index + 1 -lt $schemaChunks.Count) { [void]$header.Append(',') }
    [void]$header.Append("`n")
}
[void]$header.Append("}};`n} // namespace god2`n")
$headerText = $header.ToString()

if ($ValidateOnly) {
    if (-not (Test-Path -LiteralPath $schemaPath) -or
        -not (Test-Path -LiteralPath $headerPath)) {
        throw 'generated semantic v2 schema/header is missing'
    }
    if ([IO.File]::ReadAllText($schemaPath, $utf8NoBom) -cne $schemaText -or
        [IO.File]::ReadAllText($headerPath, $utf8NoBom) -cne $headerText) {
        throw 'generated semantic v2 schema/header is stale'
    }
    Write-Output "semanticEventV2SchemaProfiles=41 schemaBranches=$($branches.Count) definitions=$($definitions.Count) status=PASS"
    return
}

[IO.Directory]::CreateDirectory((Split-Path $schemaPath -Parent)) | Out-Null
[IO.File]::WriteAllText($schemaPath, $schemaText, $utf8NoBom)
[IO.File]::WriteAllText($headerPath, $headerText, $utf8NoBom)
Write-Output "semanticEventV2SchemaProfiles=41 schemaBranches=$($branches.Count) definitions=$($definitions.Count) status=GENERATED"
