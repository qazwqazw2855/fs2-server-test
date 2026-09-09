[CmdletBinding()]
param(
    [string]$StatePath,
    [string]$TraceRoot,
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
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TraceRoot "analysis\live-field-clusters.json"
}

function Convert-HexBytes {
    param([string]$Hex)
    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0) { return $null }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

function Add-Count {
    param([hashtable]$Table, [string]$Key)
    if ($Table.ContainsKey($Key)) { $Table[$Key]++ } else { $Table[$Key] = 1 }
}

function Read-JsonLinesShared {
    param([string]$Path, [scriptblock]$OnObject)
    $result = [ordered]@{ parsed = 0; malformed = 0 }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 65536, $false)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $item = $line | ConvertFrom-Json
                    & $OnObject $item
                    $result.parsed++
                }
                catch { $result.malformed++ }
            }
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    return $result
}

$catalogPath = Join-Path $TraceRoot "analysis\protocol-candidate-catalog.json"
$metadataPath = Join-Path $TraceRoot "metadata.jsonl"
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$definitions = @{}
foreach ($entry in $catalog.s2c) {
    $definitions[[string]$entry.opcode] = @($entry.fieldCandidates)
}

$fieldStats = @{}
$identityStats = @{}
$frameCounts = @{}
$read = Read-JsonLinesShared -Path $metadataPath -OnObject {
    param($item)
    if ($item.Plaintext -ne $true -or [string]$item.PacketDirection -ne "ServerToClient") { return }
    $opcode = [string]$item.Opcode
    if (-not $script:definitions.ContainsKey($opcode)) { return }
    $bytes = Convert-HexBytes ([string]$item.PlaintextHex)
    if ($null -eq $bytes -or $bytes.Length -lt 5) { return }
    if ($script:frameCounts.ContainsKey($opcode)) { $script:frameCounts[$opcode]++ } else { $script:frameCounts[$opcode] = 1 }

    foreach ($definition in $script:definitions[$opcode]) {
        $offset = [int]$definition.frameOffset
        $type = [string]$definition.type
        $width = if ($type -like "UInt16*") { 2 } elseif ($type -eq "PackedUInt32") { 4 } elseif ($type -like "UInt8*") { 1 } else { 0 }
        $key = "$opcode|$offset|$type"
        if (-not $script:fieldStats.ContainsKey($key)) {
            $script:fieldStats[$key] = [ordered]@{
                opcode = $opcode
                frameOffset = $offset
                type = $type
                meaning = [string]$definition.meaning
                observations = 0
                unavailable = 0
                values = @{}
            }
        }
        $stat = $script:fieldStats[$key]
        if ($width -eq 0) {
            $stat.unavailable++
            continue
        }
        if ($offset -lt 0 -or $offset + $width -gt $bytes.Length - 1) {
            $stat.unavailable++
            continue
        }
        [uint64]$value = [uint64]$bytes[$offset]
        if ($width -ge 2) { $value = $value -bor ([uint64]$bytes[$offset + 1] -shl 8) }
        if ($width -ge 4) {
            $value = $value -bor ([uint64]$bytes[$offset + 2] -shl 16) -bor ([uint64]$bytes[$offset + 3] -shl 24)
        }
        if ($type -eq "UInt8Low7Bits") { $value = $value -band 0x7F }
        if ($type -eq "UInt8Low5Bits") { $value = $value -band 0x1F }
        $stat.observations++
        Add-Count $stat.values ([string]$value)
    }

    if ($opcode -in @("0x5E", "0x6F", "0x72", "0x73") -and $bytes.Length -ge 6) {
        [uint16]$identity = [uint16]([int]$bytes[3] -bor ([int]$bytes[4] -shl 8))
        if ($identity -ne 0) {
            $token = "OID16-{0:X4}" -f $identity
            if (-not $script:identityStats.ContainsKey($token)) {
                $script:identityStats[$token] = [ordered]@{ token = $token; value = [int]$identity; opcodes = @{}; observations = 0 }
            }
            $identityStat = $script:identityStats[$token]
            $identityStat.observations++
            Add-Count $identityStat.opcodes $opcode
        }
    }
    [Array]::Clear($bytes, 0, $bytes.Length)
}

$fields = @($fieldStats.GetEnumerator() | Sort-Object Name | ForEach-Object {
    $stat = $_.Value
    $top = @($stat.values.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 8 | ForEach-Object {
        $numeric = [uint64]$_.Name
        [ordered]@{
            value = $numeric
            hex = "0x{0:X}" -f $numeric
            count = [int]$_.Value
            low11 = if ($stat.type -eq "PackedUInt32") { [int]($numeric -band 0x7FF) } else { $null }
            high11 = if ($stat.type -eq "PackedUInt32") { [int](($numeric -shr 11) -band 0x7FF) } else { $null }
        }
    })
    [ordered]@{
        opcode = $stat.opcode
        frameOffset = $stat.frameOffset
        type = $stat.type
        semanticCandidate = $stat.meaning
        authority = "OBSERVED_RUNTIME_FIELD_CANDIDATE"
        observations = $stat.observations
        unavailable = $stat.unavailable
        distinctValues = $stat.values.Count
        stable = $stat.values.Count -eq 1 -and $stat.observations -gt 0
        topValues = $top
        productionPromotion = $false
    }
})

$objectTokens = @($identityStats.GetEnumerator() | Sort-Object { -[int]$_.Value.observations } | ForEach-Object {
    $stat = $_.Value
    [ordered]@{
        token = $stat.token
        value = $stat.value
        authority = "OBSERVED_PROTOCOL_ID_CANDIDATE"
        observations = $stat.observations
        opcodes = @($stat.opcodes.GetEnumerator() | Sort-Object Name | ForEach-Object {
            [ordered]@{ opcode = [string]$_.Name; count = [int]$_.Value }
        })
        crossOpcode = $stat.opcodes.Count -gt 1
        pointerObserved = $false
        objectSemanticVerified = $false
    }
})

$result = [ordered]@{
    schema = "God2LiveFieldClusters/1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    authority = "OBSERVED_RUNTIME_FIELD_CANDIDATE"
    fields = $fields
    objectTokens = $objectTokens
    crossOpcodeObjectTokens = @($objectTokens | Where-Object { $_.crossOpcode })
    frameCounts = @($frameCounts.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{ opcode = [string]$_.Name; count = [int]$_.Value }
    })
    reader = $read
    secretsPersisted = $false
    pointerValuesPersisted = $false
    productionPromotion = $false
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = "$OutputPath.tmp-$PID"
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
$result | ConvertTo-Json -Depth 12
