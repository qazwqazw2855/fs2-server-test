param(
    [Parameter(Mandatory = $true)] [string] $TracePath,
    [Parameter(Mandatory = $true)] [string] $CalibrationPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
function Resolve-WorkspaceFile([string] $Path, [bool] $MustExist) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PK calibration paths must remain under the workspace root.'
    }
    if ($MustExist -and -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "PK calibration input does not exist: $resolved"
    }
    return $resolved
}

$resolvedTrace = Resolve-WorkspaceFile $TracePath $true
$resolvedCalibration = Resolve-WorkspaceFile $CalibrationPath $true
$resolvedOutput = Resolve-WorkspaceFile $OutputPath $false
$calibration = Get-Content -LiteralPath $resolvedCalibration -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($calibration.schemaVersion -cne 'god2-primary-attribute-pk-calibration-v1') {
    throw 'Unsupported PK calibration schemaVersion.'
}
$subjects = @($calibration.subjects)
if ($subjects.Count -lt 3) {
    throw 'At least three independently known PK subjects are required.'
}
$attributeNames = @(
    'strength', 'constitution', 'intelligence', 'speed',
    'currentMp', 'maximumMp',
    'metal', 'wood', 'water', 'fire', 'earth')
$optionalCombatAttributes = @(
    'physicalAttack', 'physicalDefense', 'magicAttack', 'magicDefense')
foreach ($optionalAttribute in $optionalCombatAttributes) {
    $declaredForEverySubject = @($subjects | Where-Object {
        $null -ne $_.PSObject.Properties[$optionalAttribute]
    }).Count -eq $subjects.Count
    if ($declaredForEverySubject) { $attributeNames += $optionalAttribute }
}
foreach ($subject in $subjects) {
    if ([string]::IsNullOrWhiteSpace([string]$subject.name)) {
        throw 'Every PK calibration subject requires an exact actor name.'
    }
    foreach ($attribute in $attributeNames) {
        $value = [int64]$subject.$attribute
        if ($value -lt 0 -or $value -gt 100000000) {
            throw "Subject attribute $attribute is outside the bounded scalar range."
        }
    }
}
foreach ($attribute in $attributeNames) {
    $vectors = @($subjects | ForEach-Object { [int64]$_.$attribute } |
        Select-Object -Unique)
    if ($vectors.Count -lt 3) {
        throw "Attribute $attribute needs at least three distinct controlled values."
    }
}
$skillSubjects = @($subjects | Where-Object {
    $_.PSObject.Properties['skills'] -and @($_.skills).Count -gt 0
})
if ($skillSubjects.Count -ne 0 -and $skillSubjects.Count -lt 3) {
    throw 'Unused-skill calibration requires at least three known loadouts.'
}

$strictCp936 = [Text.Encoding]::GetEncoding(
    936, [Text.EncoderFallback]::ExceptionFallback,
    [Text.DecoderFallback]::ExceptionFallback)
$descriptors = @{}
$observations = [Collections.Generic.List[object]]::new()
function Add-ActorObservation(
    [uint64] $Sequence, [int] $Position, [byte[]] $Actor,
    [string] $Source, [object] $Consumer) {
    if ($Actor.Length -ne 0xE00) { return }
    $nameLength = 0
    while ($nameLength -lt 64 -and $Actor[0xDBE + $nameLength] -ne 0) {
        $nameLength++
    }
    if ($nameLength -eq 0 -or $nameLength -eq 64) { return }
    try { $name = $strictCp936.GetString($Actor, 0xDBE, $nameLength) }
    catch { return }
    if ([string]::IsNullOrWhiteSpace($name) -or $name -match '[\p{Cc}\p{Cs}]') {
        return
    }
    $level = [int]$Actor[0xDB7]
    $localId = [uint16][BitConverter]::ToUInt16($Actor, 0xDBC)
    $matching = @($subjects | Where-Object {
        [string]$_.name -ceq $name -and
        (-not $_.PSObject.Properties['level'] -or [int]$_.level -eq $level) -and
        (-not $_.PSObject.Properties['localId'] -or
            [uint16]$_.localId -eq $localId)
    })
    if ($matching.Count -ne 1) { return }
    $observations.Add([pscustomobject]@{
        sequence = $Sequence
        battlePosition = $Position
        subject = $matching[0]
        name = $name
        level = $level
        localId = $localId
        source = $Source
        consumer = $Consumer
        bytes = $Actor
    })
}

$stream = [IO.File]::Open($resolvedTrace, [IO.FileMode]::Open,
    [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($stream.Length -lt 24) { throw 'Trace header is incomplete.' }
    $header = $reader.ReadBytes(24)
    if ([Text.Encoding]::ASCII.GetString($header, 0, 7) -cne 'G2TRC01' -or
        $header[7] -ne 0) { throw 'Trace header identity is invalid.' }
    while ($stream.Position -lt $stream.Length) {
        if ($stream.Length - $stream.Position -lt 188) {
            throw 'Trace record header is truncated.'
        }
        $record = $reader.ReadBytes(188)
        if ([BitConverter]::ToUInt32($record, 0) -ne 0x31523247 -or
            [BitConverter]::ToUInt16($record, 4) -ne 1 -or
            [BitConverter]::ToUInt16($record, 6) -ne 188) {
            throw 'Trace record identity is invalid.'
        }
        $sequence = [BitConverter]::ToUInt64($record, 8)
        $api = [BitConverter]::ToUInt16($record, 40)
        $position = [BitConverter]::ToInt32($record, 48)
        $recordPhase = [BitConverter]::ToUInt32($record, 52)
        $capturedLength = [BitConverter]::ToUInt32($record, 64)
        if ($capturedLength -gt 4096 -or
            $stream.Length - $stream.Position -lt $capturedLength) {
            throw 'Trace payload length is invalid.'
        }
        $payload = $reader.ReadBytes([int]$capturedLength)
        if ($api -eq 7 -and $payload.Length -eq 45 -and
            $payload[0] -eq 0x1C -and $payload[1] -lt 28) {
            $descriptors[[int]$payload[1]] = [pscustomobject]@{
                sequence = $sequence
                level = [int]$payload[2]
                entityId = [uint16][BitConverter]::ToUInt16($payload, 3)
                localId = [uint16][BitConverter]::ToUInt16($payload, 7)
            }
            continue
        }
        if ($api -eq 8 -and $payload.Length -eq 0xE00 -and
            $position -ge 0 -and $position -lt 28 -and
            $descriptors.ContainsKey($position)) {
            $descriptor = $descriptors[$position]
            if ([uint64]$descriptor.sequence -lt $sequence -and
                [BitConverter]::ToUInt16($payload, 0xDB0) -eq
                    [uint16]$descriptor.entityId -and
                [int]$payload[0xDB7] -eq [int]$descriptor.level -and
                [BitConverter]::ToUInt16($payload, 0xDBC) -eq
                    [uint16]$descriptor.localId) {
                $actorSource = if ($recordPhase -eq 0) {
                    'ActorInitialSnapshot'
                } else { 'ActorChangedSnapshot' }
                Add-ActorObservation $sequence $position $payload $actorSource $null
            }
            continue
        }
        if ($api -eq 11 -and $payload.Length -eq 3688 -and
            [BitConverter]::ToUInt32($payload, 0) -eq 0x3154414D -and
            [BitConverter]::ToUInt16($payload, 4) -eq 1 -and
            [BitConverter]::ToUInt16($payload, 6) -eq 104 -and
            [BitConverter]::ToUInt32($payload, 36) -eq 0xE00) {
            $candidateLength = 0
            while ($candidateLength -lt 64 -and
                $payload[40 + $candidateLength] -ne 0) { $candidateLength++ }
            $consumer = [pscustomobject]@{
                functionRva = ('0x{0:X8}' -f [BitConverter]::ToUInt32($payload, 8))
                instructionRva = ('0x{0:X8}' -f [BitConverter]::ToUInt32($payload, 12))
                callerRva = ('0x{0:X8}' -f [BitConverter]::ToUInt32($payload, 16))
                phase = [BitConverter]::ToUInt32($payload, 20)
                targetId = [BitConverter]::ToUInt32($payload, 24)
                consumerStep = [BitConverter]::ToUInt32($payload, 28)
                candidateId = [Text.Encoding]::ASCII.GetString(
                    $payload, 40, $candidateLength)
            }
            $actor = [byte[]]::new(0xE00)
            [Array]::Copy($payload, 104, $actor, 0, 0xE00)
            Add-ActorObservation $sequence ([int][BitConverter]::ToUInt32(
                $payload, 32)) $actor 'ConsumerSnapshot' $consumer
        }
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

function Get-Scalar([byte[]] $Bytes, [int] $Offset, [int] $Width) {
    switch ($Width) {
        1 { return [uint64]$Bytes[$Offset] }
        2 { return [uint64][BitConverter]::ToUInt16($Bytes, $Offset) }
        4 { return [uint64][BitConverter]::ToUInt32($Bytes, $Offset) }
    }
    throw 'Unsupported scalar width.'
}

function Find-ExactMatches([object[]] $Rows, [string] $Attribute) {
    $matches = [Collections.Generic.List[object]]::new()
    $subjectRows = @($Rows | Group-Object { [string]$_.subject.name })
    if ($subjectRows.Count -lt 3) { return @() }
    foreach ($width in @(1, 2, 4)) {
        for ($offset = 0; $offset + $width -le 0xE00; $offset += $width) {
            $agreements = [Collections.Generic.List[object]]::new()
            foreach ($group in $subjectRows) {
                $values = @($group.Group | ForEach-Object {
                    Get-Scalar $_.bytes $offset $width
                } | Select-Object -Unique)
                $expected = [uint64]$group.Group[0].subject.$Attribute
                if ($values.Count -ne 1 -or [uint64]$values[0] -ne $expected) {
                    continue
                }
                $agreements.Add([pscustomobject]@{
                    name = [string]$group.Group[0].name
                    expected = $expected
                    observationCount = $group.Count
                })
            }
            if ($agreements.Count -eq $subjectRows.Count) {
                $consumerSites = @($Rows | Where-Object {
                    $_.source -eq 'ConsumerSnapshot'
                } | ForEach-Object { $_.consumer } |
                    Sort-Object functionRva,instructionRva,phase,consumerStep -Unique)
                $matches.Add([pscustomobject][ordered]@{
                    offset = ('0x{0:X3}' -f $offset)
                    widthBytes = $width
                    controlledSubjectCount = $agreements.Count
                    agreements = @($agreements)
                    consumerSites = $consumerSites
                })
            }
        }
    }
    return @($matches)
}

function Find-SkillSlotMatches([object[]] $Rows) {
    if ($skillSubjects.Count -lt 3) { return @() }
    $subjectRows = @($Rows | Group-Object { [string]$_.subject.name } |
        Where-Object {
            $name = [string]$_.Group[0].subject.name
            @($skillSubjects | Where-Object { [string]$_.name -ceq $name }).Count -eq 1
        })
    if ($subjectRows.Count -ne $skillSubjects.Count) { return @() }
    $slotCount = ($skillSubjects | ForEach-Object { @($_.skills).Count } |
        Measure-Object -Minimum).Minimum
    $results = [Collections.Generic.List[object]]::new()
    for ($slot = 0; $slot -lt $slotCount; $slot++) {
        foreach ($width in @(2, 4)) {
            for ($offset = 0; $offset + $width -le 0xE00; $offset += $width) {
                $agreements = [Collections.Generic.List[object]]::new()
                foreach ($group in $subjectRows) {
                    $values = @($group.Group | ForEach-Object {
                        Get-Scalar $_.bytes $offset $width
                    } | Select-Object -Unique)
                    $expected = [uint64]@($group.Group[0].subject.skills)[$slot]
                    if ($values.Count -ne 1 -or [uint64]$values[0] -ne $expected) {
                        continue
                    }
                    $agreements.Add([pscustomobject]@{
                        name = [string]$group.Group[0].name
                        expectedSkillId = $expected
                        observationCount = $group.Count
                    })
                }
                if ($agreements.Count -eq $subjectRows.Count) {
                    $results.Add([pscustomobject][ordered]@{
                        skillSlot = $slot
                        offset = ('0x{0:X3}' -f $offset)
                        widthBytes = $width
                        controlledSubjectCount = $agreements.Count
                        agreements = @($agreements)
                    })
                }
            }
        }
    }
    return @($results)
}

$snapshotRows = @($observations | Where-Object source -eq 'ActorInitialSnapshot')
$consumerRows = @($observations | Where-Object source -eq 'ConsumerSnapshot')
$coveredSubjects = @($observations | ForEach-Object { [string]$_.subject.name } |
    Sort-Object -Unique)
$attributeResults = [ordered]@{}
foreach ($attribute in $attributeNames) {
    $snapshotMatches = @(Find-ExactMatches $snapshotRows $attribute)
    $consumerMatches = @(Find-ExactMatches $consumerRows $attribute)
    $status = if ($coveredSubjects.Count -lt 3) {
        'CALIBRATION_SUBJECTS_NOT_CAPTURED'
    } elseif ($consumerMatches.Count -gt 0) {
        'EXACT_CONTROLLED_VALUES_AT_LIVE_CONSUMER'
    } elseif ($snapshotMatches.Count -gt 0) {
        'EXACT_CONTROLLED_VALUES_IN_BATTLE_ACTOR_CONSUMER_PENDING'
    } else {
        'NO_DIRECT_SCALAR_MATCH_IN_CAPTURED_BATTLE_ACTOR'
    }
    $attributeResults[$attribute] = [ordered]@{
        status = $status
        actorSnapshotMatches = $snapshotMatches
        liveConsumerMatches = $consumerMatches
    }
}
$skillActorMatches = @(Find-SkillSlotMatches $snapshotRows)
$skillConsumerMatches = @(Find-SkillSlotMatches $consumerRows)
$skillStatus = if ($skillSubjects.Count -lt 3) {
    'SKILL_CALIBRATION_NOT_DECLARED'
} elseif ($coveredSubjects.Count -lt 3) {
    'CALIBRATION_SUBJECTS_NOT_CAPTURED'
} elseif ($skillConsumerMatches.Count -gt 0) {
    'UNUSED_SKILL_LOADOUT_PRESENT_AT_LIVE_CONSUMER'
} elseif ($skillActorMatches.Count -gt 0) {
    'UNUSED_SKILL_LOADOUT_PRESENT_IN_INITIAL_BATTLE_ACTOR_CONSUMER_PENDING'
} else {
    'NO_RAW_SKILL_ID_MATCH_ENCODING_OR_SERVER_ONLY_REMAINS_POSSIBLE'
}

$output = [ordered]@{
    schemaVersion = 'god2-primary-attribute-pk-calibration-evidence-v1'
    sourceTrace = $resolvedTrace.Substring($repoRoot.Length + 1).Replace('\', '/')
    sourceTraceSha256 = (Get-FileHash $resolvedTrace -Algorithm SHA256).Hash
    calibration = $resolvedCalibration.Substring($repoRoot.Length + 1).Replace('\', '/')
    calibrationSha256 = (Get-FileHash $resolvedCalibration -Algorithm SHA256).Hash
    declaredSubjectCount = $subjects.Count
    coveredSubjectCount = $coveredSubjects.Count
    coveredSubjects = $coveredSubjects
    actorSnapshotObservationCount = $snapshotRows.Count
    consumerSnapshotObservationCount = $consumerRows.Count
    attributes = $attributeResults
    unusedSkillLoadout = [ordered]@{
        status = $skillStatus
        declaredSubjectCount = $skillSubjects.Count
        initialActorMatches = $skillActorMatches
        liveConsumerMatches = $skillConsumerMatches
        boundary = 'Initial actor snapshots precede player commands. A match proves pre-use loadout presence; no raw-ID match does not exclude an encoded list.'
    }
    evidenceBoundary = 'Exact scalar matches prove direct representation only; semantic promotion requires isolated-value controls plus a live consumer site and official-service replication.'
    gameMemoryWritten = $false
    rawPointersPersisted = $false
    networkBytesEmitted = $false
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) |
    Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($output | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
$output | ConvertTo-Json -Depth 12
