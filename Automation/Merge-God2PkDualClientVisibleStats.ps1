param(
    [Parameter(Mandatory = $true)] [string] $ManifestPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath,
    [string] $ContractCsvPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'

function Resolve-WorkspaceFile([string] $Path, [bool] $MustExist) {
    $candidate = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $repoRoot $Path }
    $resolved = [IO.Path]::GetFullPath($candidate)
    if (-not $resolved.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Dual-client PK merge paths must remain under the workspace root.'
    }
    if ($MustExist -and -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Dual-client PK merge input does not exist: $resolved"
    }
    return $resolved
}

function Convert-ToRelativePath([string] $Path) {
    return $Path.Substring($repoRoot.Length + 1).Replace('\', '/')
}

function Read-JsonFile([string] $Path) {
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-CalibrationSubject([object] $Calibration, [string] $Name) {
    $matches = @($Calibration.subjects | Where-Object { [string]$_.name -ceq $Name })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one calibration subject named '$Name'; found $($matches.Count)."
    }
    return $matches[0]
}

function Get-VectorOccurrences(
    [object] $Scan,
    [string] $SubjectName,
    [string] $VectorName,
    [uint64] $MinimumSequence = 0) {
    $subjectProperty = $Scan.subjects.PSObject.Properties[$SubjectName]
    if ($null -eq $subjectProperty) {
        throw "Scan does not contain subject '$SubjectName'."
    }
    $vectorProperty = $subjectProperty.Value.vectors.PSObject.Properties[$VectorName]
    if ($null -eq $vectorProperty) {
        throw "Scan subject '$SubjectName' does not contain vector '$VectorName'."
    }
    $occurrences = [Collections.Generic.List[object]]::new()
    foreach ($candidate in @($vectorProperty.Value)) {
        foreach ($occurrence in @($candidate.occurrences)) {
            if ([uint64]$occurrence.sequence -lt $MinimumSequence) {
                continue
            }
            $occurrences.Add([pscustomobject]@{
                widthBytes = [int]$candidate.widthBytes
                sequence = [uint64]$occurrence.sequence
                opcode = [string]$occurrence.opcode
                frameLength = [int]$occurrence.frameLength
                offset = [string]$occurrence.offset
            })
        }
    }
    return @($occurrences | Sort-Object sequence, offset)
}

function Get-ByteArraySha256([byte[]] $Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Read-BattleIdentitySnapshots([string] $TracePath) {
    $strictCp936 = [Text.Encoding]::GetEncoding(
        936, [Text.EncoderFallback]::ExceptionFallback,
        [Text.DecoderFallback]::ExceptionFallback)
    $descriptors = @{}
    $snapshots = [Collections.Generic.List[object]]::new()
    $stream = [IO.File]::Open(
        $TracePath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 24) {
            throw 'Identity trace header is incomplete.'
        }
        $header = $reader.ReadBytes(24)
        if ([Text.Encoding]::ASCII.GetString($header, 0, 7) -cne 'G2TRC01' -or
            $header[7] -ne 0) {
            throw 'Identity trace header is invalid.'
        }
        while ($stream.Position -lt $stream.Length) {
            if ($stream.Length - $stream.Position -lt 188) {
                throw 'Identity trace record header is truncated.'
            }
            $record = $reader.ReadBytes(188)
            if ([BitConverter]::ToUInt32($record, 0) -ne 0x31523247 -or
                [BitConverter]::ToUInt16($record, 4) -ne 1 -or
                [BitConverter]::ToUInt16($record, 6) -ne 188) {
                throw 'Identity trace record header is invalid.'
            }
            $sequence = [BitConverter]::ToUInt64($record, 8)
            $api = [BitConverter]::ToUInt16($record, 40)
            $battlePosition = [BitConverter]::ToInt32($record, 48)
            $capturedLength = [BitConverter]::ToUInt32($record, 64)
            if ($capturedLength -gt 4096 -or
                $stream.Length - $stream.Position -lt $capturedLength) {
                throw 'Identity trace payload length is invalid.'
            }
            $payload = $reader.ReadBytes([int]$capturedLength)
            if ($api -eq 7 -and $payload.Length -eq 45 -and
                $payload[0] -eq 0x1C -and $payload[1] -lt 28) {
                $descriptorPosition = [int]$payload[1]
                $descriptors[$descriptorPosition] = [pscustomobject]@{
                    sequence = $sequence
                    battlePosition = $descriptorPosition
                    level = [int]$payload[2]
                    objectId = [uint16][BitConverter]::ToUInt16($payload, 3)
                    localId = [uint16][BitConverter]::ToUInt16($payload, 7)
                    sha256 = Get-ByteArraySha256 $payload
                }
                continue
            }
            if ($api -ne 8 -or $payload.Length -ne 0xE00 -or
                $battlePosition -lt 0 -or $battlePosition -ge 28 -or
                -not $descriptors.ContainsKey($battlePosition)) {
                continue
            }
            $descriptor = $descriptors[$battlePosition]
            if ([uint64]$descriptor.sequence -ge $sequence -or
                [BitConverter]::ToUInt16($payload, 0xDB0) -ne
                    [uint16]$descriptor.objectId -or
                [int]$payload[0xDB7] -ne [int]$descriptor.level -or
                [BitConverter]::ToUInt16($payload, 0xDBC) -ne
                    [uint16]$descriptor.localId) {
                continue
            }
            $nameLength = 0
            while ($nameLength -lt 64 -and
                $payload[0xDBE + $nameLength] -ne 0) {
                $nameLength++
            }
            if ($nameLength -eq 0 -or $nameLength -eq 64) {
                continue
            }
            try {
                $actorName = $strictCp936.GetString(
                    $payload, 0xDBE, $nameLength)
            }
            catch {
                continue
            }
            if ([string]::IsNullOrWhiteSpace($actorName) -or
                $actorName -match '[\p{Cc}\p{Cs}]') {
                continue
            }
            $snapshots.Add([pscustomobject][ordered]@{
                sequence = $sequence
                descriptorSequence = [uint64]$descriptor.sequence
                battlePosition = $battlePosition
                side = [int][Math]::Floor($battlePosition / 14)
                slot = $battlePosition % 14
                objectId = [uint16]$descriptor.objectId
                level = [int]$descriptor.level
                localId = [uint16]$descriptor.localId
                actorName = $actorName
                descriptorSha256 = [string]$descriptor.sha256
            })
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    return @($snapshots)
}

function Convert-ToCsvField([object] $Value) {
    if ($null -eq $Value) { return '' }
    $text = [string]$Value
    if ($text.IndexOfAny([char[]]@(',', '"', "`r", "`n")) -ge 0) {
        return '"' + $text.Replace('"', '""') + '"'
    }
    return $text
}

$resolvedManifest = Resolve-WorkspaceFile $ManifestPath $true
$resolvedOutput = Resolve-WorkspaceFile $OutputPath $false
$manifest = Read-JsonFile $resolvedManifest
if ($manifest.schemaVersion -notin @(
        'god2-pk-dual-client-merge-manifest-v1',
        'god2-pk-dual-client-merge-manifest-v2')) {
    throw 'Unsupported dual-client PK merge manifest schemaVersion.'
}

$identityTrace = $null
$identityTraceSha256 = $null
$identitySnapshots = @()
$resolvedContractCsv = $null
if ($manifest.schemaVersion -ceq 'god2-pk-dual-client-merge-manifest-v2') {
    if ($null -eq $manifest.identityPerspective -or
        [string]::IsNullOrWhiteSpace(
            [string]$manifest.identityPerspective.sessionId) -or
        [string]::IsNullOrWhiteSpace(
            [string]$manifest.identityPerspective.trace)) {
        throw 'Manifest v2 requires an identityPerspective sessionId and trace.'
    }
    $identityTrace = Resolve-WorkspaceFile (
        [string]$manifest.identityPerspective.trace) $true
    $identityTraceSha256 = (
        Get-FileHash -LiteralPath $identityTrace -Algorithm SHA256).Hash
    if ($manifest.identityPerspective.PSObject.Properties['traceSha256'] -and
        [string]$manifest.identityPerspective.traceSha256 -cne
            $identityTraceSha256) {
        throw 'Identity perspective trace SHA-256 does not match the manifest.'
    }
    $identitySnapshots = @(Read-BattleIdentitySnapshots $identityTrace)
    if ($identitySnapshots.Count -eq 0) {
        throw 'Identity perspective trace contains no verified actor snapshots.'
    }
    if ([string]::IsNullOrWhiteSpace($ContractCsvPath)) {
        throw 'Manifest v2 requires ContractCsvPath.'
    }
    $resolvedContractCsv = Resolve-WorkspaceFile $ContractCsvPath $false
} elseif (-not [string]::IsNullOrWhiteSpace($ContractCsvPath)) {
    throw 'ContractCsvPath requires a manifest v2 identity perspective.'
}

$resolvedCalibration = Resolve-WorkspaceFile ([string]$manifest.calibration) $true
$calibration = Read-JsonFile $resolvedCalibration
if ($calibration.schemaVersion -cne 'god2-primary-attribute-pk-calibration-v1') {
    throw 'Unsupported calibration schemaVersion.'
}

$mergedParticipants = [Collections.Generic.List[object]]::new()
$contractRows = [Collections.Generic.List[object]]::new()
$identityKeys = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$vectorNames = @('primaryUiOrder', 'primaryBonusUiOrder', 'derivedObservedWireOrder', 'elementalOrder', 'mpPair')

foreach ($participant in @($manifest.participants)) {
    $subjectName = [string]$participant.calibrationSubject
    $subject = Get-CalibrationSubject $calibration $subjectName
    $resolvedOwnerScan = Resolve-WorkspaceFile ([string]$participant.ownerScan) $true
    $resolvedOpponentScan = Resolve-WorkspaceFile ([string]$participant.opponentScan) $true
    $ownerScan = Read-JsonFile $resolvedOwnerScan
    $opponentScan = Read-JsonFile $resolvedOpponentScan
    foreach ($scan in @($ownerScan, $opponentScan)) {
        if ($scan.schemaVersion -cne 'god2-pk-visible-stat-vector-scan-v1') {
            throw "Unsupported visible-stat scan schemaVersion for '$subjectName'."
        }
    }

    $ownerEvidence = [ordered]@{}
    $opponentEvidence = [ordered]@{}
    $ownerEvidenceCount = 0
    $opponentEvidenceCount = 0
    $opponentMinimumSequence = if (
        $participant.PSObject.Properties['opponentMinimumSequence']) {
        [uint64]$participant.opponentMinimumSequence
    } else { [uint64]0 }
    foreach ($vectorName in $vectorNames) {
        $ownerOccurrences = @(Get-VectorOccurrences $ownerScan $subjectName $vectorName)
        $opponentOccurrences = @(Get-VectorOccurrences `
            $opponentScan $subjectName $vectorName $opponentMinimumSequence)
        $ownerEvidence[$vectorName] = $ownerOccurrences
        $opponentEvidence[$vectorName] = $opponentOccurrences
        $ownerEvidenceCount += $ownerOccurrences.Count
        $opponentEvidenceCount += $opponentOccurrences.Count
    }

    $battleIdentity = $null
    if ($manifest.schemaVersion -ceq 'god2-pk-dual-client-merge-manifest-v2') {
        if (-not $participant.PSObject.Properties['identitySnapshotSequence'] -or
            -not $participant.PSObject.Properties['identityActorName']) {
            throw "Participant '$subjectName' lacks an exact identity snapshot binding."
        }
        $identitySequence = [uint64]$participant.identitySnapshotSequence
        $identityActorName = [string]$participant.identityActorName
        $identityMatches = @($identitySnapshots | Where-Object {
            [uint64]$_.sequence -eq $identitySequence -and
            [string]$_.actorName -ceq $identityActorName
        })
        if ($identityMatches.Count -ne 1) {
            throw "Participant '$subjectName' did not match exactly one identity snapshot."
        }
        $battleIdentity = $identityMatches[0]
        if ([int]$battleIdentity.level -ne [int]$subject.level -or
            ($subject.PSObject.Properties['localId'] -and
                [uint16]$battleIdentity.localId -ne [uint16]$subject.localId)) {
            throw "Participant '$subjectName' identity does not match calibration level/local ID."
        }
        $identityKey = '{0}/{1}/{2}' -f [int]$battleIdentity.side,
            [int]$battleIdentity.slot, [uint16]$battleIdentity.objectId
        if (-not $identityKeys.Add($identityKey)) {
            throw "Duplicate combat identity '$identityKey' in manifest."
        }
        if ($ownerEvidenceCount -eq 0) {
            throw "Participant '$subjectName' has no exact owner-profile evidence for contract export."
        }
        $contractRows.Add([pscustomobject][ordered]@{
            origin = 'verified_direct_observation'
            side = [int]$battleIdentity.side
            slot = [int]$battleIdentity.slot
            object_id = [uint16]$battleIdentity.objectId
            island_index = $null
            area_id = $null
            area_entry_index = $null
            vitality = [int]$subject.constitution
            strength = [int]$subject.strength
            intelligence = [int]$subject.intelligence
            speed = [int]$subject.speed
            physical_attack = [int]$subject.physicalAttack
            physical_defense = [int]$subject.physicalDefense
            magical_attack = [int]$subject.magicAttack
            magical_defense = [int]$subject.magicDefense
            gold = [int]$subject.metal
            wood = [int]$subject.wood
            water = [int]$subject.water
            fire = [int]$subject.fire
            earth = [int]$subject.earth
        })
    }

    $mergedParticipants.Add([pscustomobject]@{
        name = [string]$participant.displayName
        calibrationSubject = $subjectName
        level = [int]$subject.level
        ownerSessionId = [string]$participant.ownerSessionId
        opposingSessionId = [string]$participant.opponentSessionId
        battleIdentity = if ($null -eq $battleIdentity) { $null } else {
            [ordered]@{
                perspectiveSessionId = [string]$manifest.identityPerspective.sessionId
                actorName = [string]$battleIdentity.actorName
                sequence = [uint64]$battleIdentity.sequence
                descriptorSequence = [uint64]$battleIdentity.descriptorSequence
                battlePosition = [int]$battleIdentity.battlePosition
                side = [int]$battleIdentity.side
                slot = [int]$battleIdentity.slot
                objectId = [uint16]$battleIdentity.objectId
                localId = [uint16]$battleIdentity.localId
                descriptorSha256 = [string]$battleIdentity.descriptorSha256
            }
        }
        attributes = [ordered]@{
            base = [ordered]@{
                constitution = [int]$subject.constitution
                strength = [int]$subject.strength
                intelligence = [int]$subject.intelligence
                speed = [int]$subject.speed
            }
            bonus = [ordered]@{
                constitution = [int]$subject.constitutionBonus
                strength = [int]$subject.strengthBonus
                intelligence = [int]$subject.intelligenceBonus
                speed = [int]$subject.speedBonus
            }
            combat = [ordered]@{
                physicalAttack = [int]$subject.physicalAttack
                physicalDefense = [int]$subject.physicalDefense
                magicAttack = [int]$subject.magicAttack
                magicDefense = [int]$subject.magicDefense
            }
            elements = [ordered]@{
                metal = [int]$subject.metal
                wood = [int]$subject.wood
                water = [int]$subject.water
                fire = [int]$subject.fire
                earth = [int]$subject.earth
            }
            mp = [ordered]@{
                current = [int]$subject.currentMp
                maximum = [int]$subject.maximumMp
            }
        }
        evidence = [ordered]@{
            ownerScan = Convert-ToRelativePath $resolvedOwnerScan
            ownerExactVectorOccurrenceCount = $ownerEvidenceCount
            ownerOccurrences = $ownerEvidence
            opposingScan = Convert-ToRelativePath $resolvedOpponentScan
            opposingMinimumSequence = $opponentMinimumSequence
            opposingExactVectorOccurrenceCount = $opponentEvidenceCount
            opposingOccurrences = $opponentEvidence
        }
        verdict = if ($ownerEvidenceCount -gt 0 -and $opponentEvidenceCount -eq 0) {
            'OWNER_PROFILE_CONFIRMED_OPPONENT_PROFILE_ABSENT'
        } elseif ($ownerEvidenceCount -gt 0) {
            'OWNER_PROFILE_CONFIRMED_WITH_OPPOSING_OCCURRENCES_REQUIRING_REVIEW'
        } else {
            'OWNER_PROFILE_NOT_CONFIRMED'
        }
    })
}

$output = [ordered]@{
    schemaVersion = if ($manifest.schemaVersion -ceq
        'god2-pk-dual-client-merge-manifest-v2') {
        'god2-pk-combat-opponent-contract-merge-v2'
    } else { 'god2-pk-dual-client-visible-stats-v1' }
    captureId = [string]$manifest.captureId
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    manifest = Convert-ToRelativePath $resolvedManifest
    manifestSha256 = (Get-FileHash -LiteralPath $resolvedManifest -Algorithm SHA256).Hash
    calibration = Convert-ToRelativePath $resolvedCalibration
    calibrationSha256 = (Get-FileHash -LiteralPath $resolvedCalibration -Algorithm SHA256).Hash
    mergeRule = 'Each participant attributes come from that participant own client; all owner profiles are joined as one controlled PK capture.'
    participants = @($mergedParticipants)
    participantCount = $mergedParticipants.Count
    allOwnerProfilesConfirmed = (@($mergedParticipants | Where-Object { $_.evidence.ownerExactVectorOccurrenceCount -gt 0 }).Count -eq $mergedParticipants.Count)
    allOpponentProfilesAbsent = (@($mergedParticipants | Where-Object { $_.evidence.opposingExactVectorOccurrenceCount -eq 0 }).Count -eq $mergedParticipants.Count)
    identityPerspective = if ($null -eq $identityTrace) { $null } else {
        [ordered]@{
            sessionId = [string]$manifest.identityPerspective.sessionId
            trace = Convert-ToRelativePath $identityTrace
            traceSha256 = $identityTraceSha256
        }
    }
    combatOpponentStatsContract = if ($null -eq $resolvedContractCsv) { $null } else {
        [ordered]@{
            path = Convert-ToRelativePath $resolvedContractCsv
            rowCount = $contractRows.Count
            origin = 'verified_direct_observation'
            coordinateRule = 'side=floor(battlePosition/14); slot=battlePosition%14 in the declared identity perspective session'
        }
    }
    gameMemoryWritten = $false
    networkBytesEmitted = $false
}

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
if ($null -ne $resolvedContractCsv) {
    $columns = @(
        'origin', 'side', 'slot', 'object_id', 'island_index', 'area_id',
        'area_entry_index', 'vitality', 'strength', 'intelligence', 'speed',
        'physical_attack', 'physical_defense', 'magical_attack',
        'magical_defense', 'gold', 'wood', 'water', 'fire', 'earth')
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add(($columns -join ','))
    foreach ($row in $contractRows) {
        $lines.Add((@($columns | ForEach-Object {
            Convert-ToCsvField $row.$_
        }) -join ','))
    }
    [IO.Directory]::CreateDirectory(
        [IO.Path]::GetDirectoryName($resolvedContractCsv)) | Out-Null
    [IO.File]::WriteAllText(
        $resolvedContractCsv, (($lines -join "`r`n") + "`r`n"),
        [Text.UTF8Encoding]::new($false))
    $output.combatOpponentStatsContract.sha256 = (
        Get-FileHash -LiteralPath $resolvedContractCsv -Algorithm SHA256).Hash
}

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($output | ConvertTo-Json -Depth 15), [Text.UTF8Encoding]::new($false))
$output | ConvertTo-Json -Depth 15
