param(
    [Parameter(Mandatory = $false)]
    [string] $SourceZipPath = '',

    [Parameter(Mandatory = $false)]
    [string] $ReanalyzedZipPath = '',

    [Parameter(Mandatory = $false)]
    [string] $RepositoryRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
if ([string]::IsNullOrWhiteSpace($SourceZipPath)) {
    $SourceZipPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'God2Evidence_20260807_112752_C6A8BF34-73F6-4859-AAA0-6FE49530CEC9.zip'
}
if ([string]::IsNullOrWhiteSpace($ReanalyzedZipPath)) {
    $ReanalyzedZipPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'God2Classic\PacketCapture\Sessions\Repackages\repackage-776F4756-9200-4F02-B7F1-663D82D5E27B\God2Evidence_20260807_123608_C6A8BF34-73F6-4859-AAA0-6FE49530CEC9.zip'
}

$expectedSourceSha256 = '20F8D5A60119CE9FA4F82C0CB0F546F832E594E7B3DBF5679B2464044F497F27'
$expectedReanalysisSha256 = 'E5E24500DADB5D30F564EF685379DF7A70543E29D669BD3F37233E2E3AEFCF54'
$evidenceSourceId = 'god2-evidence-package-20260807-112752'
$evidenceReference = 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.112752.json'
$importReference = 'db/imports/evidence/packet_capture/god2_evidence_package_20260807_112752.database.json'

function Assert-FileHash([string] $Path, [string] $Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required evidence package is missing: $Path"
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected) {
        throw "Evidence package hash mismatch: $Path expected=$Expected actual=$actual"
    }

    return $actual
}

function Read-ZipText([System.IO.Compression.ZipArchive] $Archive, [string] $EntryName) {
    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "ZIP entry is missing: $EntryName"
    }

    $reader = [System.IO.StreamReader]::new($entry.Open())
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Read-ZipJsonLines([System.IO.Compression.ZipArchive] $Archive, [string] $EntryName) {
    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "ZIP entry is missing: $EntryName"
    }

    $items = [System.Collections.Generic.List[object]]::new()
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try {
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $items.Add(($line | ConvertFrom-Json))
            }
        }
    }
    finally {
        $reader.Dispose()
    }

    return $items.ToArray()
}

function Convert-HexByte([string] $Value) {
    return [Convert]::ToByte($Value.Substring(2), 16)
}

function Convert-HexToBytes([string] $Hex) {
    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

function Test-FrameChecksum([byte[]] $Frame) {
    $checksum = 0
    for ($index = 0; $index -lt $Frame.Length - 1; $index++) {
        $checksum = ($checksum + $Frame[$index] + 0x3C) -band 0xFF
    }
    return $checksum -eq $Frame[$Frame.Length - 1]
}

function ConvertTo-CompactJson($Value, [int] $Depth = 20) {
    return ConvertTo-Json -InputObject $Value -Depth $Depth -Compress
}

function Escape-Sql([string] $Value) {
    return $Value.Replace("'", "''")
}

function Write-Utf8NoBom([string] $Path, [string] $Text) {
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

Assert-FileHash -Path $SourceZipPath -Expected $expectedSourceSha256 | Out-Null
Assert-FileHash -Path $ReanalyzedZipPath -Expected $expectedReanalysisSha256 | Out-Null

$sourceArchive = [System.IO.Compression.ZipFile]::OpenRead($SourceZipPath)
$reanalyzedArchive = [System.IO.Compression.ZipFile]::OpenRead($ReanalyzedZipPath)
try {
    $captureInfo = Read-ZipText $sourceArchive 'capture-info.json' | ConvertFrom-Json
    $captureHealth = Read-ZipText $sourceArchive 'capture-health.json' | ConvertFrom-Json
    $handoff = Read-ZipText $sourceArchive 'codex-handoff.json' | ConvertFrom-Json
    $reanalyzedInfo = Read-ZipText $reanalyzedArchive 'capture-info.json' | ConvertFrom-Json
    $actionSummary = Read-ZipText $reanalyzedArchive 'PrimarySession/analysis/action-pattern-summary.json' | ConvertFrom-Json
    $decoded = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/decoded/decoded-messages.jsonl')
    $handlers = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/decoded/handler-observations.jsonl')
    $transport = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/raw/transport-chunks.jsonl')
    $transportMappings = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/mapping/transport-to-decrypted.jsonl')
    $handlerMappings = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/mapping/decrypted-to-handler.jsonl')
    $sourcePatterns = @(Read-ZipJsonLines $reanalyzedArchive 'PrimarySession/analysis/action-patterns.jsonl')
}
finally {
    $sourceArchive.Dispose()
    $reanalyzedArchive.Dispose()
}

if ($decoded.Count -ne 1778 -or $handlers.Count -ne 214 -or $sourcePatterns.Count -ne 116) {
    throw "Unexpected evidence counts: decoded=$($decoded.Count), handlers=$($handlers.Count), patterns=$($sourcePatterns.Count)"
}

$decodedFamilies = @(
    $decoded |
        Group-Object { "$($_.Direction)|$($_.Opcode)|$($_.PlaintextLength)" } |
        ForEach-Object {
            $first = $_.Group[0]
            [pscustomobject][ordered]@{
                direction = [string] $first.Direction
                opcode = [string] $first.Opcode
                frameLength = [int] $first.PlaintextLength
                observedCount = [int] $_.Count
                uniqueFrameCount = [int] @($_.Group.PayloadSHA256 | Sort-Object -Unique).Count
            }
        } |
        Sort-Object @{ Expression = { if ($_.direction -eq 'ClientToServer') { 0 } else { 1 } } },
                    @{ Expression = { Convert-HexByte $_.opcode } }, frameLength
)

$knownKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$baseCatalogPath = Join-Path $RepositoryRoot 'src/God2.ClassicServer.Protocol/OfficialEvidencePackage20260806Catalog.cs'
$baseCatalogText = Get-Content -LiteralPath $baseCatalogPath
foreach ($line in $baseCatalogText) {
    if ($line -match '^\s*([CS])\(0x([0-9A-F]{2}),\s*\d+,\s*\d+,\s*([0-9,\s]+)\),?\s*$') {
        $direction = if ($Matches[1] -eq 'C') { 'ClientToServer' } else { 'ServerToClient' }
        $opcode = "0x$($Matches[2])"
        foreach ($lengthText in $Matches[3].Split(',')) {
            $length = [int] $lengthText.Trim()
            $knownKeys.Add("$direction|$opcode|$length") | Out-Null
        }
    }
}

$priorSupplementPath = Join-Path $RepositoryRoot 'src/God2.ClassicServer.Protocol/Evidence/OfficialProtocolCompletion/God2Evidence.20260807.Supplement.json'
$priorSupplement = Get-Content -Raw -LiteralPath $priorSupplementPath | ConvertFrom-Json
foreach ($family in $priorSupplement.supplementalDecodedFrameEvidence.families) {
    $knownKeys.Add("$($family.direction)|$($family.opcode)|$($family.frameLength)") | Out-Null
}

$newFamilies = @($decodedFamilies | Where-Object {
    -not $knownKeys.Contains("$($_.direction)|$($_.opcode)|$($_.frameLength)")
})
if ($newFamilies.Count -ne 64 -or ($newFamilies | Measure-Object observedCount -Sum).Sum -ne 88 -or
    ($newFamilies | Measure-Object uniqueFrameCount -Sum).Sum -ne 83) {
    throw 'The new decoded-frame structural delta no longer matches the reviewed 64/88/83 evidence boundary.'
}

$stageEvidence = @(
    foreach ($direction in @('ClientToServer', 'ServerToClient')) {
        $records = @($decoded | Where-Object Direction -eq $direction)
        $opcodeCatalog = @(
            $records | Group-Object Opcode | ForEach-Object {
                [pscustomobject][ordered]@{
                    opcode = [string] $_.Name
                    count = [int] $_.Count
                    uniqueFrameCount = [int] @($_.Group.PayloadSHA256 | Sort-Object -Unique).Count
                    frameLengths = @($_.Group.PlaintextLength | Sort-Object -Unique)
                }
            } | Sort-Object { Convert-HexByte $_.opcode }
        )
        [pscustomobject][ordered]@{
            captureStage = if ($direction -eq 'ClientToServer') { 'PreEncrypt' } else { 'PostDecrypt' }
            direction = $direction
            recordCount = $records.Count
            uniquePayloadCount = @($records.PayloadSHA256 | Sort-Object -Unique).Count
            opcodeFamilyCount = $opcodeCatalog.Count
            minimumFrameLength = ($records.PlaintextLength | Measure-Object -Minimum).Minimum
            maximumFrameLength = ($records.PlaintextLength | Measure-Object -Maximum).Maximum
            opcodeCatalog = $opcodeCatalog
        }
    }
)

$handlerFamilies = @(
    $handlers | Group-Object {
        $payloadLength = $_.PayloadHex.Length / 2
        "$($_.PayloadHex.Substring(0,2))|$payloadLength|$($_.HandlerAddress)"
    } | ForEach-Object {
        $first = $_.Group[0]
        [pscustomobject][ordered]@{
            opcode = "0x$($first.PayloadHex.Substring(0,2))"
            payloadLength = [int] ($first.PayloadHex.Length / 2)
            observedCount = [int] $_.Count
            uniquePayloadCount = [int] @($_.Group.PayloadSHA256 | Sort-Object -Unique).Count
            handlerAddress = [string] $first.HandlerAddress
        }
    } | Sort-Object { Convert-HexByte $_.opcode }
)
if ($handlerFamilies.Count -ne 8 -or ($handlerFamilies | Measure-Object observedCount -Sum).Sum -ne 214) {
    throw 'Handler evidence no longer matches the reviewed 8-family/214-observation boundary.'
}

$patterns = @(
    $sourcePatterns | ForEach-Object {
        [pscustomobject][ordered]@{
            actionPatternId = [string] $_.ActionPatternId
            structuralPatternKey = [string] $_.StructuralPatternKey
            triggerOpcode = [string] $_.TriggerOpcode
            triggerLength = [int] $_.TriggerLength
            responseOpcodeSequence = @($_.ResponseOpcodeSequence)
            responseLengthPattern = @($_.ResponseLengthPattern)
            handlerOpcodeSequence = @($_.HandlerOpcodeSequence)
            occurrences = [int] $_.Occurrences
            medianLatencyMs = [decimal] $_.MedianLatencyMs
            p95LatencyMs = [decimal] $_.P95LatencyMs
            relatedOpcodeWorkItemIds = @($_.RelatedOpcodeWorkItemIds)
            semanticStatus = 'Unknown'
            productionEligible = $false
        }
    }
)

$createFrames = @($decoded | Where-Object { $_.Direction -eq 'ClientToServer' -and $_.Opcode -eq '0x17' -and $_.PlaintextLength -eq 48 })
$slotFrames = @($decoded | Where-Object { $_.Direction -eq 'ClientToServer' -and $_.Opcode -eq '0x18' -and $_.PlaintextLength -eq 5 })
if ($createFrames.Count -ne 2 -or $slotFrames.Count -ne 1) {
    throw 'Expected lifecycle structural observations were not found.'
}

$createNames = [System.Collections.Generic.List[string]]::new()
$createOpaqueHashes = [System.Collections.Generic.List[string]]::new()
$allLifecycleFrames = @($createFrames) + @($slotFrames)
foreach ($record in $allLifecycleFrames) {
    $frame = Convert-HexToBytes $record.PlaintextHex
    if (-not (Test-FrameChecksum $frame)) {
        throw "Lifecycle frame checksum failed: $($record.PayloadSHA256)"
    }
    if ($record.Opcode -eq '0x17') {
        $nameBytes = $frame[3..31]
        $terminator = [Array]::IndexOf($nameBytes, [byte] 0)
        if ($terminator -lt 1) { throw 'Character-name candidate is not null terminated.' }
        $createNames.Add([Text.Encoding]::ASCII.GetString($nameBytes, 0, $terminator))
        $opaqueBytes = $frame[32..46]
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $createOpaqueHashes.Add(([BitConverter]::ToString($sha.ComputeHash($opaqueBytes))).Replace('-', '')) } finally { $sha.Dispose() }
    }
}

$lifecycleCandidates = @(
    [pscustomobject][ordered]@{
        candidateId = 'character-create-name-field-candidate'
        operationCandidate = 'CharacterCreateRequestCandidate'
        opcode = '0x17'
        frameLength = 48
        fieldName = 'CharacterNameCandidate'
        offset = 3
        length = 29
        observedFrameCount = 2
        distinctObservedValueCount = @($createNames | Sort-Object -Unique).Count
        evidenceStatus = 'Candidate'
        valuesRedacted = $true
        productionEnabled = $false
    },
    [pscustomobject][ordered]@{
        candidateId = 'character-create-settings-opaque'
        operationCandidate = 'CharacterCreateRequestCandidate'
        opcode = '0x17'
        frameLength = 48
        fieldName = 'OpaqueCreateSettings'
        offset = 32
        length = 15
        observedFrameCount = 2
        distinctObservedValueCount = @($createOpaqueHashes | Sort-Object -Unique).Count
        evidenceStatus = 'Unknown'
        valuesRedacted = $true
        productionEnabled = $false
    },
    [pscustomobject][ordered]@{
        candidateId = 'character-lifecycle-slot-field-candidate'
        operationCandidate = 'CharacterLifecycleSlotActionCandidate'
        opcode = '0x18'
        frameLength = 5
        fieldName = 'SlotOrIndexCandidate'
        offset = 3
        length = 1
        observedFrameCount = 1
        distinctObservedValueCount = 1
        evidenceStatus = 'Candidate'
        valuesRedacted = $false
        productionEnabled = $false
    }
)

$transportC2S = @($transport | Where-Object Direction -eq 'ClientToServer')
$transportS2C = @($transport | Where-Object Direction -eq 'ServerToClient')
$transportBytes = ($transport | Measure-Object ChunkLength -Sum).Sum
$preEncryptMappings = @($transportMappings | Where-Object { $_.DecodedMessageId -in @($decoded | Where-Object Direction -eq 'ClientToServer').DecodedMessageId }).Count
$postDecryptMappings = @($transportMappings | Where-Object { $_.DecodedMessageId -in @($decoded | Where-Object Direction -eq 'ServerToClient').DecodedMessageId }).Count
$postDecryptHandlerMappings = $handlerMappings.Count
if ($preEncryptMappings -ne 898 -or $postDecryptMappings -ne 853 -or $postDecryptHandlerMappings -ne 197) {
    throw "Unexpected correlation counts: pre=$preEncryptMappings post=$postDecryptMappings handler=$postDecryptHandlerMappings"
}

$evidence = [pscustomobject][ordered]@{
    schemaVersion = 'god2-evidence-package-20260807-112752-v1'
    generatedAtUtc = ([datetime] $reanalyzedInfo.PackagedAtUtc).ToUniversalTime().ToString('o')
    evidenceSourceId = $evidenceSourceId
    sourcePackage = [pscustomobject][ordered]@{
        pathLabel = 'Desktop/God2Evidence_20260807_112752_C6A8BF34-73F6-4859-AAA0-6FE49530CEC9.zip'
        sha256 = $expectedSourceSha256
        toolVersion = [string] $captureInfo.ToolVersion
        packageValidationStatus = [string] $captureHealth.OverallHealthStatus
        blockers = @($captureHealth.Blockers)
    }
    deterministicReanalysis = [pscustomobject][ordered]@{
        sha256 = $expectedReanalysisSha256
        toolVersion = '1.0.5'
        sourcePackageSha256 = $expectedSourceSha256
        actionGroupingGenerated = $true
    }
    clientIdentity = [pscustomobject][ordered]@{
        fileName = [string] $captureInfo.ClientFileName
        sha256 = [string] $captureInfo.ClientSHA256
        architecture = [string] $captureInfo.ClientArchitecture
        fileVersion = [string] $captureInfo.ClientFileVersion
        launcherSha256 = [string] $captureInfo.LauncherSHA256
    }
    session = [pscustomobject][ordered]@{
        sessionId = [string] $captureInfo.SessionId
        runtimeSessionId = '917B958E-FAE9-4AF2-9925-EDB88736BB44'
        analysisRunId = [string] $captureInfo.AnalysisRunId
        packageRunId = [string] $captureInfo.PackageRunId
        targetProcessId = 2660
        captureRecordCount = [int] $captureInfo.CaptureRecords
        transportChunkCount = $transport.Count
        clientToServerTransportCount = $transportC2S.Count
        serverToClientTransportCount = $transportS2C.Count
        transportPayloadBytes = [long] $transportBytes
        decodedMessageCount = $decoded.Count
        handlerObservationCount = $handlers.Count
        candidateProtocolFrameCount = [int] $captureInfo.CandidateProtocolFrames
        verifiedFrameCount = [int] $captureInfo.ProtocolFrames
        unknownVerifiedFrameCount = [int] $captureInfo.UnknownVerifiedProtocolFrameCount
        packetLossCount = [int] $captureHealth.PacketLossCount
    }
    decodedStageEvidence = $stageEvidence
    newDecodedFrameEvidence = [pscustomobject][ordered]@{
        comparisonBasis = 'Existing 2026-08-06 catalog plus 2026-08-07 supplement'
        familyCount = $newFamilies.Count
        observationCount = [int] ($newFamilies | Measure-Object observedCount -Sum).Sum
        uniqueFrameCount = [int] ($newFamilies | Measure-Object uniqueFrameCount -Sum).Sum
        families = $newFamilies
    }
    handlerEvidence = [pscustomobject][ordered]@{
        familyCount = $handlerFamilies.Count
        observationCount = [int] ($handlerFamilies | Measure-Object observedCount -Sum).Sum
        families = $handlerFamilies
    }
    correlationEvidence = [pscustomobject][ordered]@{
        preEncryptToTransportClientToServer = $preEncryptMappings
        transportToPostDecryptServerToClient = $postDecryptMappings
        postDecryptToHandlerServerToClient = $postDecryptHandlerMappings
        correlationLevel = 'Strong'
        exactCorrelationCount = 0
    }
    actionGroupingEvidence = [pscustomobject][ordered]@{
        triggerCandidateCount = [int] $actionSummary.ActionTriggerCandidateCount
        instanceCount = [int] $actionSummary.ActionInstanceCount
        patternCount = [int] $actionSummary.ActionPatternCount
        groupedTriggerCount = [int] $actionSummary.GroupedTriggerCount
        ungroupedTriggerCount = [int] $actionSummary.UngroupedTriggerCount
        candidateActionCount = [int] $actionSummary.CandidateActionCount
        ambiguousActionCount = [int] $actionSummary.AmbiguousActionCount
        unknownActionPatternCount = [int] $actionSummary.UnknownActionPatternCount
        verifiedGameplayMappedPatternCount = [int] $actionSummary.VerifiedGameplayMappedPatternCount
        coveragePercent = [decimal] $actionSummary.ActionClusteringCoveragePercent
        medianLatencyMs = [decimal] $actionSummary.SessionLatencyMedianMs
        p95LatencyMs = [decimal] $actionSummary.SessionLatencyP95Ms
        adaptiveLatencyLimitMs = [decimal] $actionSummary.SessionAdaptiveLatencyLimitMs
        patterns = $patterns
    }
    lifecycleFieldCandidates = $lifecycleCandidates
    serverApplication = [pscustomobject][ordered]@{
        catalogClass = 'God2.ClassicServer.Protocol.OfficialEvidencePackage20260807ActionCatalog'
        decodedStructuralFamiliesAdded = $newFamilies.Count
        unknownActionPatternsAdded = $patterns.Count
        lifecycleFieldCandidatesAdded = $lifecycleCandidates.Count
        productionHandlersAdded = 0
        verifiedGameplayPromotions = 0
        productionEnabled = $false
        runtimeMutationReason = 'All action semantics are Unknown; lifecycle fields remain Candidate or Unknown.'
    }
    sensitiveDataPolicy = [pscustomobject][ordered]@{
        rawPayloadRetainedInRepository = $false
        credentialTextRetainedInRepository = $false
        characterNamesRetainedInRepository = $false
        sourcePackageRemainsExternal = $true
    }
}

$evidencePath = Join-Path $RepositoryRoot ($evidenceReference.Replace('/', '\'))
Write-Utf8NoBom $evidencePath (($evidence | ConvertTo-Json -Depth 30) + [Environment]::NewLine)
$evidenceSha256 = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash

$databaseImport = [pscustomobject][ordered]@{
    schemaVersion = $evidence.schemaVersion
    evidenceSourceId = $evidenceSourceId
    evidenceSha256 = $evidenceSha256
    sourcePackageSha256 = $expectedSourceSha256
    deterministicReanalysisSha256 = $expectedReanalysisSha256
    session = $evidence.session
    decodedStages = $stageEvidence
    newDecodedFrameFamilies = $newFamilies
    handlerFamilies = $handlerFamilies
    actionGrouping = $evidence.actionGroupingEvidence
    lifecycleFieldCandidates = $lifecycleCandidates
    evidenceReference = $evidenceReference
    serverApplication = $evidence.serverApplication
}
$importPath = Join-Path $RepositoryRoot ($importReference.Replace('/', '\'))
Write-Utf8NoBom $importPath (($databaseImport | ConvertTo-Json -Depth 30) + [Environment]::NewLine)

$sql = [System.Text.StringBuilder]::new()
[void] $sql.AppendLine('-- Validated v1.0.4 capture plus deterministic v1.0.5 action-grouping reanalysis.')
[void] $sql.AppendLine('-- Additive EvidenceOnly/Candidate import. No gameplay/runtime mutation is enabled.')
[void] $sql.AppendLine(@'
CREATE TABLE IF NOT EXISTS `packet_capture_frame_variant_catalog_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Direction` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Opcode` tinyint unsigned NOT NULL,
    `FrameLength` int unsigned NOT NULL,
    `ObservedCount` int unsigned NOT NULL,
    `UniqueFrameCount` int unsigned NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `FieldSemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `GameplaySemanticsStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'NotVerified',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `Direction`, `Opcode`, `FrameLength`),
    CONSTRAINT `FK_PacketCaptureFrameVariant_Source` FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureFrameVariant_Direction` CHECK (`Direction` IN ('ClientToServer', 'ServerToClient')),
    CONSTRAINT `CK_PacketCaptureFrameVariant_Counts` CHECK (`FrameLength` >= 3 AND `ObservedCount` > 0 AND `UniqueFrameCount` > 0 AND `UniqueFrameCount` <= `ObservedCount`),
    CONSTRAINT `CK_PacketCaptureFrameVariant_Authority` CHECK (`EvidenceStatus` = 'EvidenceOnly' AND `FieldSemanticsStatus` = 'Unknown' AND `GameplaySemanticsStatus` = 'NotVerified' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_action_grouping_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `TriggerCandidateCount` int unsigned NOT NULL,
    `InstanceCount` int unsigned NOT NULL,
    `PatternCount` int unsigned NOT NULL,
    `GroupedTriggerCount` int unsigned NOT NULL,
    `UngroupedTriggerCount` int unsigned NOT NULL,
    `UnknownPatternCount` int unsigned NOT NULL,
    `VerifiedGameplayMappedPatternCount` int unsigned NOT NULL,
    `CoveragePercent` decimal(8,3) NOT NULL,
    `MedianLatencyMs` decimal(12,3) NOT NULL,
    `P95LatencyMs` decimal(12,3) NOT NULL,
    `AdaptiveLatencyLimitMs` decimal(12,3) NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'EvidenceOnly',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`),
    CONSTRAINT `FK_PacketCaptureActionGrouping_Source` FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureActionGrouping_Partition` CHECK (`TriggerCandidateCount` = `GroupedTriggerCount` + `UngroupedTriggerCount` AND `InstanceCount` = `GroupedTriggerCount` AND `UnknownPatternCount` = `PatternCount`),
    CONSTRAINT `CK_PacketCaptureActionGrouping_Authority` CHECK (`VerifiedGameplayMappedPatternCount` = 0 AND `EvidenceStatus` = 'EvidenceOnly' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_action_pattern_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ActionPatternId` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `StructuralPatternKey` varchar(768) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `TriggerOpcode` tinyint unsigned NOT NULL,
    `TriggerLength` int unsigned NOT NULL,
    `ResponseOpcodeSequenceJson` json NOT NULL,
    `ResponseLengthPatternJson` json NOT NULL,
    `HandlerOpcodeSequenceJson` json NOT NULL,
    `Occurrences` int unsigned NOT NULL,
    `MedianLatencyMs` decimal(12,3) NOT NULL,
    `P95LatencyMs` decimal(12,3) NOT NULL,
    `RelatedOpcodeWorkItemIdsJson` json NOT NULL,
    `SemanticStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Unknown',
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `ActionPatternId`),
    KEY `IX_PacketCaptureActionPattern_Trigger` (`TriggerOpcode`, `TriggerLength`),
    CONSTRAINT `FK_PacketCaptureActionPattern_Source` FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureActionPattern_Counts` CHECK (`TriggerLength` >= 3 AND `Occurrences` > 0),
    CONSTRAINT `CK_PacketCaptureActionPattern_Json` CHECK (JSON_VALID(`ResponseOpcodeSequenceJson`) AND JSON_VALID(`ResponseLengthPatternJson`) AND JSON_VALID(`HandlerOpcodeSequenceJson`) AND JSON_VALID(`RelatedOpcodeWorkItemIdsJson`)),
    CONSTRAINT `CK_PacketCaptureActionPattern_Authority` CHECK (`SemanticStatus` = 'Unknown' AND `ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `packet_capture_lifecycle_field_candidate_evidence` (
    `EvidenceSourceId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `CandidateId` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `OperationCandidate` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Opcode` tinyint unsigned NOT NULL,
    `FrameLength` int unsigned NOT NULL,
    `FieldName` varchar(96) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `FieldOffset` int unsigned NOT NULL,
    `FieldLength` int unsigned NOT NULL,
    `ObservedFrameCount` int unsigned NOT NULL,
    `DistinctObservedValueCount` int unsigned NOT NULL,
    `EvidenceStatus` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ValuesRedacted` tinyint(1) NOT NULL,
    `ProductionEnabled` tinyint(1) NOT NULL DEFAULT 0,
    `EvidenceReference` varchar(768) NOT NULL,
    PRIMARY KEY (`EvidenceSourceId`, `CandidateId`),
    CONSTRAINT `FK_PacketCaptureLifecycleCandidate_Source` FOREIGN KEY (`EvidenceSourceId`) REFERENCES `packet_capture_evidence_sources` (`EvidenceSourceId`) ON DELETE CASCADE,
    CONSTRAINT `CK_PacketCaptureLifecycleCandidate_Status` CHECK (`EvidenceStatus` IN ('Candidate', 'Unknown')),
    CONSTRAINT `CK_PacketCaptureLifecycleCandidate_Counts` CHECK (`FrameLength` >= 3 AND `FieldOffset` + `FieldLength` < `FrameLength` AND `ObservedFrameCount` > 0 AND `DistinctObservedValueCount` > 0),
    CONSTRAINT `CK_PacketCaptureLifecycleCandidate_Authority` CHECK (`ProductionEnabled` = 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
'@)

$generatedAtSql = ([datetime] $captureInfo.PackagedAtUtc).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss.ffffff')
$sourceInsert = @"
INSERT INTO ``packet_capture_evidence_sources``
    (``EvidenceSourceId``, ``EvidencePath``, ``ImportPath``, ``SchemaVersion``, ``EvidencePolicy``, ``GeneratedAtUtc``, ``EvidenceSha256``, ``PackageSha256``, ``SessionCount``, ``RawTransportRecords``, ``FrameCount``, ``UnknownFrameCount``, ``CandidateFrameCount``, ``SemanticCandidateClusterCount``, ``HighConfidenceSemanticCandidateCount``, ``RepresentativeCandidateCount``, ``AppliedKnowledgeCount``, ``VerifiedGameplayClassificationCount``)
VALUES
    ('$evidenceSourceId', '$evidenceReference', '$importReference', 'god2-evidence-package-20260807-112752-v1', 'Candidate and Unknown action/lifecycle evidence remains EvidenceOnly; decoded structural variants have no production authority.', '$generatedAtSql', '$($evidenceSha256.ToLowerInvariant())', '$($expectedSourceSha256.ToLowerInvariant())', 1, $($transport.Count), $($decoded.Count), $($decoded.Count), $($captureInfo.CandidateProtocolFrames), $($patterns.Count), 0, $($patterns.Count), $($newFamilies.Count), 0)
ON DUPLICATE KEY UPDATE ``EvidencePath``=VALUES(``EvidencePath``), ``ImportPath``=VALUES(``ImportPath``), ``SchemaVersion``=VALUES(``SchemaVersion``), ``EvidencePolicy``=VALUES(``EvidencePolicy``), ``GeneratedAtUtc``=VALUES(``GeneratedAtUtc``), ``EvidenceSha256``=VALUES(``EvidenceSha256``), ``PackageSha256``=VALUES(``PackageSha256``), ``SessionCount``=VALUES(``SessionCount``), ``RawTransportRecords``=VALUES(``RawTransportRecords``), ``FrameCount``=VALUES(``FrameCount``), ``UnknownFrameCount``=VALUES(``UnknownFrameCount``), ``CandidateFrameCount``=VALUES(``CandidateFrameCount``), ``SemanticCandidateClusterCount``=VALUES(``SemanticCandidateClusterCount``), ``HighConfidenceSemanticCandidateCount``=VALUES(``HighConfidenceSemanticCandidateCount``), ``RepresentativeCandidateCount``=VALUES(``RepresentativeCandidateCount``), ``AppliedKnowledgeCount``=VALUES(``AppliedKnowledgeCount``), ``VerifiedGameplayClassificationCount``=VALUES(``VerifiedGameplayClassificationCount``);

INSERT INTO ``packet_capture_sessions``
    (``EvidenceSourceId``, ``SessionId``, ``RuntimeSessionId``, ``AnalysisRunId``, ``SourceLabel``, ``Availability``, ``TargetExecutable``, ``TargetProcessId``, ``TargetArchitecture``, ``CaptureSource``, ``Transport``, ``InjectionSequence``, ``ReadinessHandshake``, ``CleanupStatus``, ``RawTransportRecords``, ``CaptureRecordCount``, ``DecodedMessageCount``, ``HandlerObservationCount``, ``LengthPrefixValidatedCount``, ``ClientToServerRecords``, ``ServerToClientRecords``, ``PayloadBytes``, ``FrameCount``, ``UnknownFrameCount``, ``CandidateFrameCount``, ``SemanticCandidateClusterCount``, ``HighConfidenceSemanticCandidateCount``, ``VerifiedGameplayClassificationCount``, ``EvidenceStatus``, ``ProductionEnabled``, ``EvidenceReference``)
VALUES
    ('$evidenceSourceId', '$($captureInfo.SessionId)', '917B958E-FAE9-4AF2-9925-EDB88736BB44', '$($captureInfo.AnalysisRunId)', 'User-submitted validated God2PacketCapture v1.0.4 package with deterministic v1.0.5 action grouping', 'External package; payload-free evidence retained', 'God2_opt.exe', 2660, 'x86', 'OptInX86Dll', 'InjectedWinsock+PreEncrypt+PostDecrypt+HandlerDecoded', 'OptInRemoteThreadInjectionAfterLauncherStartedGame', 'God2TraceProbeWaitReady', 'Completed', $($transport.Count), $($captureInfo.CaptureRecords), $($decoded.Count), $($handlers.Count), $($decoded.Count), $($transportC2S.Count), $($transportS2C.Count), $transportBytes, $($decoded.Count), $($decoded.Count), $($captureInfo.CandidateProtocolFrames), $($patterns.Count), 0, 0, 'EvidenceOnly', 0, '$evidenceReference')
ON DUPLICATE KEY UPDATE ``RuntimeSessionId``=VALUES(``RuntimeSessionId``), ``AnalysisRunId``=VALUES(``AnalysisRunId``), ``SourceLabel``=VALUES(``SourceLabel``), ``Availability``=VALUES(``Availability``), ``TargetProcessId``=VALUES(``TargetProcessId``), ``CaptureSource``=VALUES(``CaptureSource``), ``Transport``=VALUES(``Transport``), ``InjectionSequence``=VALUES(``InjectionSequence``), ``ReadinessHandshake``=VALUES(``ReadinessHandshake``), ``CleanupStatus``=VALUES(``CleanupStatus``), ``RawTransportRecords``=VALUES(``RawTransportRecords``), ``CaptureRecordCount``=VALUES(``CaptureRecordCount``), ``DecodedMessageCount``=VALUES(``DecodedMessageCount``), ``HandlerObservationCount``=VALUES(``HandlerObservationCount``), ``LengthPrefixValidatedCount``=VALUES(``LengthPrefixValidatedCount``), ``ClientToServerRecords``=VALUES(``ClientToServerRecords``), ``ServerToClientRecords``=VALUES(``ServerToClientRecords``), ``PayloadBytes``=VALUES(``PayloadBytes``), ``FrameCount``=VALUES(``FrameCount``), ``UnknownFrameCount``=VALUES(``UnknownFrameCount``), ``CandidateFrameCount``=VALUES(``CandidateFrameCount``), ``SemanticCandidateClusterCount``=VALUES(``SemanticCandidateClusterCount``), ``HighConfidenceSemanticCandidateCount``=VALUES(``HighConfidenceSemanticCandidateCount``), ``VerifiedGameplayClassificationCount``=VALUES(``VerifiedGameplayClassificationCount``), ``EvidenceStatus``=VALUES(``EvidenceStatus``), ``ProductionEnabled``=VALUES(``ProductionEnabled``), ``EvidenceReference``=VALUES(``EvidenceReference``);
"@
[void] $sql.AppendLine($sourceInsert)

foreach ($stage in $stageEvidence) {
    $catalogJson = Escape-Sql (ConvertTo-CompactJson $stage.opcodeCatalog 20)
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_decoded_stage_evidence`` (``EvidenceSourceId``, ``CaptureStage``, ``Direction``, ``RecordCount``, ``UniquePayloadCount``, ``OpcodeFamilyCount``, ``LengthPrefixValidatedCount``, ``MinimumFrameLength``, ``MaximumFrameLength``, ``OpcodeCatalogJson``, ``EvidenceStatus``, ``FieldSemanticsStatus``, ``GameplaySemanticsStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($stage.captureStage)', '$($stage.direction)', $($stage.recordCount), $($stage.uniquePayloadCount), $($stage.opcodeFamilyCount), $($stage.recordCount), $($stage.minimumFrameLength), $($stage.maximumFrameLength), '$catalogJson', 'EvidenceOnly', 'Unknown', 'NotVerified', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``RecordCount``=VALUES(``RecordCount``), ``UniquePayloadCount``=VALUES(``UniquePayloadCount``), ``OpcodeFamilyCount``=VALUES(``OpcodeFamilyCount``), ``LengthPrefixValidatedCount``=VALUES(``LengthPrefixValidatedCount``), ``MinimumFrameLength``=VALUES(``MinimumFrameLength``), ``MaximumFrameLength``=VALUES(``MaximumFrameLength``), ``OpcodeCatalogJson``=VALUES(``OpcodeCatalogJson``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

foreach ($family in $handlerFamilies) {
    $address = if ($family.handlerAddress -match '\+0x([0-9A-F]+)$') { 0x00400000 + [Convert]::ToUInt32($Matches[1], 16) } else { throw "Unexpected handler address: $($family.handlerAddress)" }
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_handler_family_evidence`` (``EvidenceSourceId``, ``HandlerOpcode``, ``ObservedCount``, ``UniquePayloadCount``, ``PayloadLength``, ``HandlerAddress``, ``EvidenceStatus``, ``FieldSemanticsStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', $(Convert-HexByte $family.opcode), $($family.observedCount), $($family.uniquePayloadCount), $($family.payloadLength), $address, 'EvidenceOnly', 'Unknown', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``ObservedCount``=VALUES(``ObservedCount``), ``UniquePayloadCount``=VALUES(``UniquePayloadCount``), ``PayloadLength``=VALUES(``PayloadLength``), ``HandlerAddress``=VALUES(``HandlerAddress``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

foreach ($family in $newFamilies) {
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_frame_variant_catalog_evidence`` (``EvidenceSourceId``, ``Direction``, ``Opcode``, ``FrameLength``, ``ObservedCount``, ``UniqueFrameCount``, ``EvidenceStatus``, ``FieldSemanticsStatus``, ``GameplaySemanticsStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($family.direction)', $(Convert-HexByte $family.opcode), $($family.frameLength), $($family.observedCount), $($family.uniqueFrameCount), 'EvidenceOnly', 'Unknown', 'NotVerified', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``ObservedCount``=VALUES(``ObservedCount``), ``UniqueFrameCount``=VALUES(``UniqueFrameCount``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

[void] $sql.AppendLine("INSERT INTO ``packet_capture_action_grouping_evidence`` (``EvidenceSourceId``, ``TriggerCandidateCount``, ``InstanceCount``, ``PatternCount``, ``GroupedTriggerCount``, ``UngroupedTriggerCount``, ``UnknownPatternCount``, ``VerifiedGameplayMappedPatternCount``, ``CoveragePercent``, ``MedianLatencyMs``, ``P95LatencyMs``, ``AdaptiveLatencyLimitMs``, ``EvidenceStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', $($actionSummary.ActionTriggerCandidateCount), $($actionSummary.ActionInstanceCount), $($actionSummary.ActionPatternCount), $($actionSummary.GroupedTriggerCount), $($actionSummary.UngroupedTriggerCount), $($actionSummary.UnknownActionPatternCount), 0, $($actionSummary.ActionClusteringCoveragePercent), $($actionSummary.SessionLatencyMedianMs), $($actionSummary.SessionLatencyP95Ms), $($actionSummary.SessionAdaptiveLatencyLimitMs), 'EvidenceOnly', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``TriggerCandidateCount``=VALUES(``TriggerCandidateCount``), ``InstanceCount``=VALUES(``InstanceCount``), ``PatternCount``=VALUES(``PatternCount``), ``GroupedTriggerCount``=VALUES(``GroupedTriggerCount``), ``UngroupedTriggerCount``=VALUES(``UngroupedTriggerCount``), ``UnknownPatternCount``=VALUES(``UnknownPatternCount``), ``VerifiedGameplayMappedPatternCount``=VALUES(``VerifiedGameplayMappedPatternCount``), ``CoveragePercent``=VALUES(``CoveragePercent``), ``MedianLatencyMs``=VALUES(``MedianLatencyMs``), ``P95LatencyMs``=VALUES(``P95LatencyMs``), ``AdaptiveLatencyLimitMs``=VALUES(``AdaptiveLatencyLimitMs``), ``EvidenceReference``=VALUES(``EvidenceReference``);")

foreach ($pattern in $patterns) {
    $responseOpcodes = Escape-Sql (ConvertTo-CompactJson @($pattern.responseOpcodeSequence))
    $responseLengths = Escape-Sql (ConvertTo-CompactJson @($pattern.responseLengthPattern))
    $handlerOpcodes = Escape-Sql (ConvertTo-CompactJson @($pattern.handlerOpcodeSequence))
    $workItems = Escape-Sql (ConvertTo-CompactJson @($pattern.relatedOpcodeWorkItemIds))
    $structuralKey = Escape-Sql $pattern.structuralPatternKey
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_action_pattern_evidence`` (``EvidenceSourceId``, ``ActionPatternId``, ``StructuralPatternKey``, ``TriggerOpcode``, ``TriggerLength``, ``ResponseOpcodeSequenceJson``, ``ResponseLengthPatternJson``, ``HandlerOpcodeSequenceJson``, ``Occurrences``, ``MedianLatencyMs``, ``P95LatencyMs``, ``RelatedOpcodeWorkItemIdsJson``, ``SemanticStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($pattern.actionPatternId)', '$structuralKey', $(Convert-HexByte $pattern.triggerOpcode), $($pattern.triggerLength), '$responseOpcodes', '$responseLengths', '$handlerOpcodes', $($pattern.occurrences), $($pattern.medianLatencyMs), $($pattern.p95LatencyMs), '$workItems', 'Unknown', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``StructuralPatternKey``=VALUES(``StructuralPatternKey``), ``TriggerOpcode``=VALUES(``TriggerOpcode``), ``TriggerLength``=VALUES(``TriggerLength``), ``ResponseOpcodeSequenceJson``=VALUES(``ResponseOpcodeSequenceJson``), ``ResponseLengthPatternJson``=VALUES(``ResponseLengthPatternJson``), ``HandlerOpcodeSequenceJson``=VALUES(``HandlerOpcodeSequenceJson``), ``Occurrences``=VALUES(``Occurrences``), ``MedianLatencyMs``=VALUES(``MedianLatencyMs``), ``P95LatencyMs``=VALUES(``P95LatencyMs``), ``RelatedOpcodeWorkItemIdsJson``=VALUES(``RelatedOpcodeWorkItemIdsJson``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

foreach ($candidate in $lifecycleCandidates) {
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_lifecycle_field_candidate_evidence`` (``EvidenceSourceId``, ``CandidateId``, ``OperationCandidate``, ``Opcode``, ``FrameLength``, ``FieldName``, ``FieldOffset``, ``FieldLength``, ``ObservedFrameCount``, ``DistinctObservedValueCount``, ``EvidenceStatus``, ``ValuesRedacted``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($candidate.candidateId)', '$($candidate.operationCandidate)', $(Convert-HexByte $candidate.opcode), $($candidate.frameLength), '$($candidate.fieldName)', $($candidate.offset), $($candidate.length), $($candidate.observedFrameCount), $($candidate.distinctObservedValueCount), '$($candidate.evidenceStatus)', $([int]$candidate.valuesRedacted), 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``OperationCandidate``=VALUES(``OperationCandidate``), ``Opcode``=VALUES(``Opcode``), ``FrameLength``=VALUES(``FrameLength``), ``FieldName``=VALUES(``FieldName``), ``FieldOffset``=VALUES(``FieldOffset``), ``FieldLength``=VALUES(``FieldLength``), ``ObservedFrameCount``=VALUES(``ObservedFrameCount``), ``DistinctObservedValueCount``=VALUES(``DistinctObservedValueCount``), ``EvidenceStatus``=VALUES(``EvidenceStatus``), ``ValuesRedacted``=VALUES(``ValuesRedacted``), ``ProductionEnabled``=VALUES(``ProductionEnabled``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

foreach ($correlation in @(
    @('PreEncrypt','Transport','ClientToServer',924,$transportC2S.Count,$preEncryptMappings,'ProbeIssuedSameThreadStageContext'),
    @('Transport','PostDecrypt','ServerToClient',$transportS2C.Count,854,$postDecryptMappings,'ProbeIssuedSameThreadStageContext'),
    @('PostDecrypt','HandlerDecoded','ServerToClient',854,214,$postDecryptHandlerMappings,'SameThreadLatestPostDecrypt')
)) {
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_stage_correlation_evidence`` (``EvidenceSourceId``, ``FromStage``, ``ToStage``, ``Direction``, ``CorrelationLevel``, ``SourceRecordCount``, ``TargetRecordCount``, ``MappingCount``, ``CorrelationBasis``, ``EvidenceStatus``, ``FieldSemanticsStatus``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($correlation[0])', '$($correlation[1])', '$($correlation[2])', 'Strong', $($correlation[3]), $($correlation[4]), $($correlation[5]), '$($correlation[6])', 'EvidenceOnly', 'Unknown', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``CorrelationLevel``=VALUES(``CorrelationLevel``), ``SourceRecordCount``=VALUES(``SourceRecordCount``), ``TargetRecordCount``=VALUES(``TargetRecordCount``), ``MappingCount``=VALUES(``MappingCount``), ``CorrelationBasis``=VALUES(``CorrelationBasis``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

$gates = @(
    @('DecodedFrameStructure','EvidenceOnly',1778,'All plaintext records have a valid u16le envelope; 64 exact structural variants are new to the repository catalog.'),
    @('UnknownActionGrouping','EvidenceOnly',655,'655 triggers group into 116 repeatable Unknown patterns; timing and correlation do not prove gameplay semantics.'),
    @('CharacterLifecycle','Candidate',3,'Opcode 0x17/48 exposes a null-terminated name candidate and opaque settings; opcode 0x18/5 exposes a slot/index candidate. Operation and remaining fields are not Verified.'),
    @('CombatFormula','EvidenceBlocked',2,'Two S2C opcode 0xE6 observations exist, but attacker, target, skill, damage, hit, critical, element and turn-order fields are not verified.'),
    @('InventoryEquipmentMount','EvidenceBlocked',0,'No item identity, equipment slot, mount identity or equip/unequip semantic was verified by this unlabeled session.'),
    @('GameplayRuntimeMutation','EvidenceBlocked',0,'All 116 action patterns remain Unknown and all lifecycle fields remain Candidate or Unknown; no production mutation is authorized.')
)
foreach ($gate in $gates) {
    [void] $sql.AppendLine("INSERT INTO ``packet_capture_content_application_gates`` (``EvidenceSourceId``, ``ContentDomain``, ``ApplicationStatus``, ``ObservedEvidenceCount``, ``Reason``, ``ProductionEnabled``, ``EvidenceReference``) VALUES ('$evidenceSourceId', '$($gate[0])', '$($gate[1])', $($gate[2]), '$($gate[3])', 0, '$evidenceReference') ON DUPLICATE KEY UPDATE ``ApplicationStatus``=VALUES(``ApplicationStatus``), ``ObservedEvidenceCount``=VALUES(``ObservedEvidenceCount``), ``Reason``=VALUES(``Reason``), ``ProductionEnabled``=VALUES(``ProductionEnabled``), ``EvidenceReference``=VALUES(``EvidenceReference``);")
}

$migrationPath = Join-Path $RepositoryRoot 'database/schema/059_god2_evidence_package_20260807_112752.sql'
Write-Utf8NoBom $migrationPath ($sql.ToString())

[pscustomobject]@{
    EvidencePath = $evidencePath
    EvidenceSha256 = $evidenceSha256
    ImportPath = $importPath
    MigrationPath = $migrationPath
    DecodedMessages = $decoded.Count
    NewFamilies = $newFamilies.Count
    NewFamilyObservations = ($newFamilies | Measure-Object observedCount -Sum).Sum
    ActionPatterns = $patterns.Count
    LifecycleCandidates = $lifecycleCandidates.Count
} | Format-List
