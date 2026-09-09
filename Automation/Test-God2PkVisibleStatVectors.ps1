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
        throw 'PK visible-stat paths must remain under the workspace root.'
    }
    if ($MustExist -and -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "PK visible-stat input does not exist: $resolved"
    }
    return $resolved
}

$resolvedTrace = Resolve-WorkspaceFile $TracePath $true
$resolvedCalibration = Resolve-WorkspaceFile $CalibrationPath $true
$resolvedOutput = Resolve-WorkspaceFile $OutputPath $false
$calibration = Get-Content -LiteralPath $resolvedCalibration -Raw -Encoding UTF8 | ConvertFrom-Json
if ($calibration.schemaVersion -cne 'god2-primary-attribute-pk-calibration-v1') {
    throw 'Unsupported PK calibration schemaVersion.'
}

$vectorOrders = [ordered]@{
    primaryUiOrder = @('constitution', 'strength', 'intelligence', 'speed')
    primaryBonusUiOrder = @('constitutionBonus', 'strengthBonus', 'intelligenceBonus', 'speedBonus')
    primaryCanonicalOrder = @('strength', 'constitution', 'intelligence', 'speed')
    derivedUiGridOrder = @('physicalAttack', 'magicAttack', 'physicalDefense', 'magicDefense')
    derivedCanonicalOrder = @('physicalAttack', 'physicalDefense', 'magicAttack', 'magicDefense')
    derivedObservedWireOrder = @('physicalDefense', 'physicalAttack', 'magicDefense', 'magicAttack')
    elementalOrder = @('metal', 'wood', 'water', 'fire', 'earth')
    mpPair = @('currentMp', 'maximumMp')
}

function Get-EncodedVector([object] $Subject, [string[]] $Fields, [int] $Width) {
    $bytes = [Collections.Generic.List[byte]]::new()
    foreach ($field in $Fields) {
        if ($null -eq $Subject.PSObject.Properties[$field]) { return $null }
        [uint64]$value = $Subject.$field
        if (($Width -eq 2 -and $value -gt [uint16]::MaxValue) -or $value -gt [uint32]::MaxValue) {
            return $null
        }
        $encoded = if ($Width -eq 2) {
            [BitConverter]::GetBytes([uint16]$value)
        } else {
            [BitConverter]::GetBytes([uint32]$value)
        }
        $bytes.AddRange([byte[]]$encoded)
    }
    return $bytes.ToArray()
}

function Find-ByteVector([byte[]] $Payload, [byte[]] $Needle) {
    $offsets = [Collections.Generic.List[int]]::new()
    if ($null -eq $Needle -or $Needle.Length -eq 0 -or $Payload.Length -lt $Needle.Length) {
        return @()
    }
    for ($offset = 0; $offset + $Needle.Length -le $Payload.Length; $offset++) {
        $match = $true
        for ($index = 0; $index -lt $Needle.Length; $index++) {
            if ($Payload[$offset + $index] -ne $Needle[$index]) { $match = $false; break }
        }
        if ($match) { $offsets.Add($offset) }
    }
    return @($offsets)
}

$subjectResults = [ordered]@{}
foreach ($subject in @($calibration.subjects)) {
    $vectors = [ordered]@{}
    foreach ($entry in $vectorOrders.GetEnumerator()) {
        $matches = [Collections.Generic.List[object]]::new()
        foreach ($width in 2, 4) {
            $needle = Get-EncodedVector $subject ([string[]]$entry.Value) $width
            $matches.Add([pscustomobject]@{
                widthBytes = $width
                fields = @($entry.Value)
                encodedHex = if ($null -eq $needle) { '' } else {
                    [BitConverter]::ToString($needle).Replace('-', '')
                }
                occurrences = [Collections.Generic.List[object]]::new()
            })
        }
        $vectors[$entry.Key] = @($matches)
    }
    $subjectResults[[string]$subject.name] = [ordered]@{
        level = if ($subject.PSObject.Properties['level']) { [int]$subject.level } else { $null }
        localId = if ($subject.PSObject.Properties['localId']) { [uint16]$subject.localId } else { $null }
        vectors = $vectors
    }
}

$decodedFrameCount = 0
$stream = [IO.File]::Open($resolvedTrace, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($stream.Length -lt 24) { throw 'Trace header is incomplete.' }
    $header = $reader.ReadBytes(24)
    if ([Text.Encoding]::ASCII.GetString($header, 0, 7) -cne 'G2TRC01' -or $header[7] -ne 0) {
        throw 'Trace header identity is invalid.'
    }
    while ($stream.Position -lt $stream.Length) {
        if ($stream.Length - $stream.Position -lt 188) { throw 'Trace record header is truncated.' }
        $record = $reader.ReadBytes(188)
        if ([BitConverter]::ToUInt32($record, 0) -ne 0x31523247 -or
            [BitConverter]::ToUInt16($record, 4) -ne 1 -or
            [BitConverter]::ToUInt16($record, 6) -ne 188) {
            throw 'Trace record identity is invalid.'
        }
        $sequence = [BitConverter]::ToUInt64($record, 8)
        $api = [BitConverter]::ToUInt16($record, 40)
        $direction = [BitConverter]::ToUInt16($record, 42)
        $capturedLength = [BitConverter]::ToUInt32($record, 64)
        if ($capturedLength -gt 4096 -or $stream.Length - $stream.Position -lt $capturedLength) {
            throw 'Trace payload length is invalid.'
        }
        $payload = $reader.ReadBytes([int]$capturedLength)
        if ($api -ne 5 -or $direction -ne 2 -or $payload.Length -lt 4) { continue }
        $decodedFrameCount++
        $opcode = $payload[2]
        foreach ($subjectEntry in $subjectResults.GetEnumerator()) {
            foreach ($vectorEntry in $subjectEntry.Value.vectors.GetEnumerator()) {
                foreach ($candidate in @($vectorEntry.Value)) {
                    if ([string]::IsNullOrWhiteSpace([string]$candidate.encodedHex)) { continue }
                    $needle = [byte[]]::new($candidate.encodedHex.Length / 2)
                    for ($index = 0; $index -lt $needle.Length; $index++) {
                        $needle[$index] = [Convert]::ToByte($candidate.encodedHex.Substring($index * 2, 2), 16)
                    }
                    foreach ($offset in @(Find-ByteVector $payload $needle)) {
                        $candidate.occurrences.Add([pscustomobject]@{
                            sequence = $sequence
                            opcode = ('0x{0:X2}' -f $opcode)
                            frameLength = $payload.Length
                            offset = ('0x{0:X3}' -f $offset)
                        })
                    }
                }
            }
        }
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

$totalOccurrences = 0
foreach ($subjectEntry in $subjectResults.GetEnumerator()) {
    foreach ($vectorEntry in $subjectEntry.Value.vectors.GetEnumerator()) {
        foreach ($candidate in @($vectorEntry.Value)) {
            $candidate.occurrences = @($candidate.occurrences)
            $totalOccurrences += $candidate.occurrences.Count
        }
    }
}

$output = [ordered]@{
    schemaVersion = 'god2-pk-visible-stat-vector-scan-v1'
    sourceTrace = $resolvedTrace.Substring($repoRoot.Length + 1).Replace('\', '/')
    sourceTraceSha256 = (Get-FileHash -LiteralPath $resolvedTrace -Algorithm SHA256).Hash
    calibration = $resolvedCalibration.Substring($repoRoot.Length + 1).Replace('\', '/')
    calibrationSha256 = (Get-FileHash -LiteralPath $resolvedCalibration -Algorithm SHA256).Hash
    decodedServerFrameCount = $decodedFrameCount
    totalExactVectorOccurrences = $totalOccurrences
    subjects = $subjectResults
    evidenceBoundary = 'Only exact contiguous little-endian UInt16/UInt32 vectors are reported. Zero matches do not exclude encoded, split, derived, encrypted, or server-private values.'
    gameMemoryWritten = $false
    networkBytesEmitted = $false
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($output | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
$output | ConvertTo-Json -Depth 12
