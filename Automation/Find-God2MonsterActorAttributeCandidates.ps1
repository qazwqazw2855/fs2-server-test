param(
    [Parameter(Mandatory = $true)]
    [string] $TracePath,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedTrace = [IO.Path]::GetFullPath($TracePath)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $resolvedTrace.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $resolvedOutput.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Attribute-candidate input and output must remain under the workspace root.'
}
if (-not (Test-Path -LiteralPath $resolvedTrace -PathType Leaf)) {
    throw 'Attribute-candidate trace does not exist.'
}

$strictCp936 = [Text.Encoding]::GetEncoding(
    936, [Text.EncoderFallback]::ExceptionFallback,
    [Text.DecoderFallback]::ExceptionFallback)
$actors = [Collections.Generic.List[object]]::new()
$states = [Collections.Generic.List[object]]::new()
$descriptors = @{}
$identities = @{}
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
        $battlePosition = [BitConverter]::ToInt32($record, 48)
        $capturedLength = [BitConverter]::ToUInt32($record, 64)
        if ($capturedLength -gt 4096 -or
            $stream.Length - $stream.Position -lt $capturedLength) {
            throw 'Trace payload length is invalid.'
        }
        $payload = $reader.ReadBytes([int]$capturedLength)
        if ($api -eq 7 -and $payload.Length -eq 45 -and
            $payload[0] -eq 0x1C -and $payload[1] -lt 28) {
            $position = [int]$payload[1]
            $identities.Remove($position)
            $descriptors[$position] = [pscustomobject]@{
                sequence = $sequence
                level = [int]$payload[2]
                entityId = [uint16][BitConverter]::ToUInt16($payload, 3)
                kindFlags = [uint16][BitConverter]::ToUInt16($payload, 5)
                localId = [uint16][BitConverter]::ToUInt16($payload, 7)
            }
            continue
        }
        if ($api -eq 9 -and $payload.Length -eq 0x290) {
            $snapshotIdentity = $battlePosition
            $position = $snapshotIdentity -band 0xFF
            $phase = ($snapshotIdentity -shr 8) -band 0xFF
            if ($position -ge 14 -and $position -lt 28 -and
                $identities.ContainsKey($position) -and
                $descriptors.ContainsKey($position)) {
                $identity = $identities[$position]
                $descriptor = $descriptors[$position]
                if ([uint64]$identity.descriptorSequence -eq
                        [uint64]$descriptor.sequence -and
                    [uint64]$descriptor.sequence -lt $sequence) {
                    $states.Add([pscustomobject]@{
                        sequence = $sequence
                        battlePosition = $position
                        phase = $phase
                        name = [string]$identity.name
                        level = [int]$identity.level
                        localId = [uint16]$identity.localId
                        groupKey = [string]$identity.groupKey
                        bytes = $payload
                    })
                }
            }
            continue
        }
        if ($api -ne 8 -or $payload.Length -ne 0xE00 -or
            $battlePosition -lt 14 -or $battlePosition -ge 28 -or
            -not $descriptors.ContainsKey($battlePosition)) { continue }
        $descriptor = $descriptors[$battlePosition]
        if ([uint64]$descriptor.sequence -ge $sequence -or
            ([uint16]$descriptor.kindFlags -band 0x6000) -ne 0 -or
            [BitConverter]::ToUInt16($payload, 0xDB0) -ne [uint16]$descriptor.entityId -or
            [int]$payload[0xDB7] -ne [int]$descriptor.level -or
            [BitConverter]::ToUInt16($payload, 0xDBC) -ne [uint16]$descriptor.localId) {
            continue
        }
        $nameLength = 0
        while ($nameLength -lt 32 -and $payload[0xDBE + $nameLength] -ne 0) {
            $nameLength++
        }
        if ($nameLength -eq 0 -or $nameLength -eq 32) { continue }
        try { $name = $strictCp936.GetString($payload, 0xDBE, $nameLength) }
        catch { continue }
        if ([string]::IsNullOrWhiteSpace($name) -or $name -match '[\p{Cc}\p{Cs}]') { continue }
        $actorRow = [pscustomobject]@{
            sequence = $sequence
            battlePosition = $battlePosition
            name = $name
            level = [int]$descriptor.level
            localId = [uint16]$descriptor.localId
            groupKey = ('{0}/{1}/{2}' -f $name, ([int]$descriptor.level),
                ([uint16]$descriptor.localId))
            bytes = $payload
            descriptorSequence = [uint64]$descriptor.sequence
        }
        $actors.Add($actorRow)
        $identities[$battlePosition] = $actorRow
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

$groups = @($actors | Group-Object groupKey | Where-Object { $_.Count -ge 2 })
if ($groups.Count -lt 2) {
    throw 'At least two repeated named monster groups are required for field differentiation.'
}

function Get-ScalarValue([byte[]] $Bytes, [int] $Offset, [int] $Width) {
    switch ($Width) {
        1 { return [uint64]$Bytes[$Offset] }
        2 { return [uint64][BitConverter]::ToUInt16($Bytes, $Offset) }
        4 { return [uint64][BitConverter]::ToUInt32($Bytes, $Offset) }
        default { throw 'Unsupported scalar width.' }
    }
}

$stateGroups = @($states | Group-Object groupKey | Where-Object {
    $_.Count -ge 2
})
$stateCandidates = [Collections.Generic.List[object]]::new()
foreach ($width in @(1, 2, 4)) {
    [uint64]$maximum = if ($width -eq 1) { 255 }
        elseif ($width -eq 2) { 65535 } else { 100000000 }
    for ($offset = 0; $offset + $width -le 0x290; $offset += $width) {
        $groupRows = [Collections.Generic.List[object]]::new()
        foreach ($group in $stateGroups) {
            $values = @($group.Group | ForEach-Object {
                Get-ScalarValue -Bytes $_.bytes -Offset $offset -Width $width
            } | Select-Object -Unique)
            if ($values.Count -ne 1 -or $values[0] -gt $maximum -or
                $values[0] -in @(0xCD, 0xCDCD, 0xCDCDCDCD)) { continue }
            $sample = $group.Group[0]
            $groupRows.Add([pscustomobject]@{
                name = [string]$sample.name
                level = [int]$sample.level
                localId = [uint16]$sample.localId
                sampleCount = $group.Count
                value = [uint64]$values[0]
            })
        }
        if ($groupRows.Count -lt 3) { continue }
        $distinctValues = @($groupRows | Select-Object -ExpandProperty value -Unique)
        if ($distinctValues.Count -lt 2) { continue }
        $stateCandidates.Add([pscustomobject][ordered]@{
            offset = ('0x{0:X3}' -f $offset)
            widthBytes = $width
            repeatedMonsterGroupCount = $groupRows.Count
            distinctValueCount = $distinctValues.Count
            values = @($groupRows)
            candidateClass = 'StableInAtLeastThreeRepeatedNamedMonsterStateGroups_DistinguishesGroups'
            semanticStatus = 'RejectedKnownRenderSpriteStateNegativeControl'
            promotionRequirement = 'NotEligibleForPrimaryAttributePromotion'
        })
    }
}

$candidates = [Collections.Generic.List[object]]::new()
foreach ($width in @(1, 2, 4)) {
    [uint64]$maximum = if ($width -eq 1) { 255 }
        elseif ($width -eq 2) { 65535 } else { 100000000 }
    for ($offset = 0; $offset + $width -le 0xE00; $offset += $width) {
        # The identity/name/vital tail is already classified and is not a new
        # primary-attribute candidate.  Exclude it from this hunt.
        if ($offset -ge 0xDB0) { continue }
        $groupRows = [Collections.Generic.List[object]]::new()
        foreach ($group in $groups) {
            $values = @($group.Group | ForEach-Object {
                Get-ScalarValue -Bytes $_.bytes -Offset $offset -Width $width
            } | Select-Object -Unique)
            if ($values.Count -ne 1 -or $values[0] -gt $maximum -or
                $values[0] -in @(0xCD, 0xCDCD, 0xCDCDCDCD)) {
                continue
            }
            $sample = $group.Group[0]
            $groupRows.Add([pscustomobject]@{
                name = [string]$sample.name
                level = [int]$sample.level
                localId = [uint16]$sample.localId
                sampleCount = $group.Count
                value = [uint64]$values[0]
            })
        }
        if ($groupRows.Count -lt 3) { continue }
        $distinctValues = @($groupRows | Select-Object -ExpandProperty value -Unique)
        if ($distinctValues.Count -lt 2) { continue }
        $candidates.Add([pscustomobject][ordered]@{
            offset = ('0x{0:X3}' -f $offset)
            widthBytes = $width
            repeatedMonsterGroupCount = $groupRows.Count
            distinctValueCount = $distinctValues.Count
            values = @($groupRows)
            candidateClass = 'StableInAtLeastThreeRepeatedNamedMonsterGroups_DistinguishesGroups'
            semanticStatus = 'UnclassifiedPrimaryAttributeOrDerivedStatCandidate'
            promotionRequirement = 'ExactBuildWriteSourcePlusBattleConsumerPlusRepeatedControlledAgreement'
        })
    }
}

$identityRows = @($groups | ForEach-Object {
    $sample = $_.Group[0]
    [pscustomobject][ordered]@{
        name = [string]$sample.name
        level = [int]$sample.level
        localId = [uint16]$sample.localId
        sampleCount = $_.Count
        representativePrefixHex = [BitConverter]::ToString(
            [byte[]]$sample.bytes[0..0x3F]).Replace('-', '')
    }
})
$output = [ordered]@{
    schemaVersion = 'god2-monster-actor-attribute-candidate-scan-v1'
    sourceTrace = $resolvedTrace.Substring($repoRoot.Length + 1).Replace('\', '/')
    sourceTraceSha256 = (Get-FileHash -LiteralPath $resolvedTrace -Algorithm SHA256).Hash
    actorSize = '0xE00'
    namedActorSnapshotCount = $actors.Count
    repeatedMonsterGroups = $identityRows
    candidateCount = $candidates.Count
    candidates = @($candidates)
    battleStateSize = '0x290'
    namedBattleStateSnapshotCount = $states.Count
    repeatedMonsterStateGroupCount = $stateGroups.Count
    battleStateCandidateCount = $stateCandidates.Count
    battleStateCandidates = @($stateCandidates)
    battleStateEvidenceBoundary = 'The 0x290 pool has exact-build render/sprite consumers and is retained only as a negative control, never as primary-stat evidence.'
    requiredTargets = @('Strength', 'Constitution', 'Intelligence', 'Speed')
    evidenceStatus = 'CANDIDATES_ONLY_CONSUMER_PROOF_REQUIRED'
    gameMemoryWritten = $false
    rawPointersPersisted = $false
    networkBytesEmitted = $false
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($output | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
$output | ConvertTo-Json -Depth 8
