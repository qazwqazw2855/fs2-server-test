param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
if (-not $sessionRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Session path escaped the approved capture root."
}

$statusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
$tracePath = Join-Path $sessionRoot "raw\enhanced-x86\trace.bin"
if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $tracePath -PathType Leaf)) {
    throw "Detached capture status or trace.bin is missing."
}
$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$status.Status -cne "DETACHED" -or -not [bool]$status.StrictUnloadVerified) {
    throw "Strict capture unload was not verified."
}

$outputRoot = Join-Path $repoRoot ("Artifacts\AnalysisScratch\BattleActorSnapshots\" + $SessionId)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$stream = [IO.File]::Open($tracePath, [IO.FileMode]::Open, [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$reader = [IO.BinaryReader]::new($stream)
$snapshots = [Collections.Generic.List[object]]::new()
$latestDescriptorsByPosition = @{}
$strictCp936 = [Text.Encoding]::GetEncoding(
    936, [Text.EncoderFallback]::ExceptionFallback,
    [Text.DecoderFallback]::ExceptionFallback)

function Get-ByteArraySha256 {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($Bytes)).Replace("-", "")
    }
    finally {
        $algorithm.Dispose()
    }
}
try {
    if ($stream.Length -lt 24) { throw "Trace header is incomplete." }
    $header = $reader.ReadBytes(24)
    if ([Text.Encoding]::ASCII.GetString($header, 0, 7) -cne "G2TRC01" -or
        $header[7] -ne 0) {
        throw "Trace header identity is invalid."
    }

    while ($stream.Position -lt $stream.Length) {
        if ($stream.Length - $stream.Position -lt 188) {
            throw "Trace record header is truncated."
        }
        $record = $reader.ReadBytes(188)
        if ([BitConverter]::ToUInt32($record, 0) -ne 0x31523247 -or
            [BitConverter]::ToUInt16($record, 4) -ne 1 -or
            [BitConverter]::ToUInt16($record, 6) -ne 188) {
            throw "Trace record identity is invalid."
        }
        $sequence = [BitConverter]::ToUInt64($record, 8)
        $wallUnixMs = [BitConverter]::ToInt64($record, 16)
        $api = [BitConverter]::ToUInt16($record, 40)
        $battlePosition = [BitConverter]::ToInt32($record, 48)
        $phaseValue = [BitConverter]::ToUInt32($record, 52)
        $capturedLength = [BitConverter]::ToUInt32($record, 64)
        if ($capturedLength -gt 4096 -or $stream.Length - $stream.Position -lt $capturedLength) {
            throw "Trace payload length is invalid."
        }
        $payload = $reader.ReadBytes([int]$capturedLength)
        if ($api -eq 7 -and $payload.Length -eq 45 -and $payload[0] -eq 0x1C -and
            $payload[1] -lt 28) {
            $descriptorPosition = [int]$payload[1]
            $speciesDescriptor = [byte[]]$payload.Clone()
            # Position and encounter actor entity are allocation-local.  Zero
            # only those fields to retain a stable, byte-exact species/variant
            # signature without merging distinct remaining descriptor fields.
            $speciesDescriptor[1] = 0
            $speciesDescriptor[3] = 0
            $speciesDescriptor[4] = 0
            $latestDescriptorsByPosition[$descriptorPosition] = [pscustomobject][ordered]@{
                sequence = $sequence
                battlePosition = $descriptorPosition
                level = [int]$payload[2]
                actorEntityId = [uint16][BitConverter]::ToUInt16($payload, 3)
                actorKindFlags = [uint16][BitConverter]::ToUInt16($payload, 5)
                encounterLocalId = [uint16][BitConverter]::ToUInt16($payload, 7)
                ordinaryStaticMonster = $descriptorPosition -ge 14 -and
                    (([BitConverter]::ToUInt16($payload, 5) -band 0x6000) -eq 0)
                byteLength = $payload.Length
                sha256 = Get-ByteArraySha256 -Bytes $payload
                speciesSignatureSha256 = Get-ByteArraySha256 -Bytes $speciesDescriptor
            }
            continue
        }
        if ($api -ne 8) { continue }
        if ($payload.Length -ne 0xE00 -or $battlePosition -lt 0 -or $battlePosition -ge 28 -or
            $phaseValue -notin @(1, 3)) {
            throw "Battle actor snapshot shape is invalid."
        }

        $objectBattlePosition = [BitConverter]::ToUInt16($payload, 0xDB0)
        $stateIndex = [BitConverter]::ToUInt16($payload, 0x1A)
        $actorState = [BitConverter]::ToInt16($payload, 0x1D8)
        $currentVital0 = [BitConverter]::ToInt32($payload, 0xDE8)
        $currentVital1 = [BitConverter]::ToInt32($payload, 0xDEC)
        $maximumVital0 = [BitConverter]::ToInt32($payload, 0xDF0)
        $maximumVital1 = [BitConverter]::ToInt32($payload, 0xDF4)
        $actorEntityId = [BitConverter]::ToUInt16($payload, 0xDB0)
        $actorLevel = [int]$payload[0xDB7]
        $actorEncounterLocalId = [BitConverter]::ToUInt16($payload, 0xDBC)
        $descriptor = if ($latestDescriptorsByPosition.ContainsKey($battlePosition)) {
            $latestDescriptorsByPosition[$battlePosition]
        } else { $null }
        $descriptorBindingValid = $null -ne $descriptor -and
            [uint64]$descriptor.sequence -lt $sequence -and
            [int]$descriptor.battlePosition -eq $battlePosition -and
            [int]$descriptor.level -gt 0 -and [int]$descriptor.encounterLocalId -gt 0

        # Exact-build actor construction projects an ordinary static enemy's
        # bounded CP936 display name into actor+0xDBE.  Decode it only when the
        # preceding descriptor took the ordinary-monster branch
        # (kindFlags&0x6000 == 0).  This deliberately excludes friendly actors
        # and enemy PK players from the readable manifest.
        $monsterNameOriginal = $null
        $monsterNameBytes = [byte[]]@()
        $monsterNameDecodeValid = $false
        if ($descriptorBindingValid -and [bool]$descriptor.ordinaryStaticMonster -and
            $actorEntityId -eq [uint16]$descriptor.actorEntityId -and
            $actorLevel -eq [int]$descriptor.level -and
            $actorEncounterLocalId -eq [uint16]$descriptor.encounterLocalId) {
            $nameCapacity = 32
            $nameLength = 0
            while ($nameLength -lt $nameCapacity -and
                $payload[0xDBE + $nameLength] -ne 0) {
                $nameLength++
            }
            if ($nameLength -gt 0 -and $nameLength -lt $nameCapacity) {
                $monsterNameBytes = [byte[]]::new($nameLength)
                [Array]::Copy($payload, 0xDBE, $monsterNameBytes, 0, $nameLength)
                try {
                    $decodedName = $strictCp936.GetString($monsterNameBytes)
                    if (-not [string]::IsNullOrWhiteSpace($decodedName) -and
                        $decodedName -notmatch '[\p{Cc}\p{Cs}]') {
                        $monsterNameOriginal = $decodedName
                        $monsterNameDecodeValid = $true
                    }
                }
                catch {
                    $monsterNameOriginal = $null
                    $monsterNameDecodeValid = $false
                }
            }
        }
        $monsterIdentityBindingValid = $descriptorBindingValid -and
            $battlePosition -ge 14 -and [bool]$descriptor.ordinaryStaticMonster -and
            $actorEntityId -eq [uint16]$descriptor.actorEntityId -and
            $actorLevel -eq [int]$descriptor.level -and
            $actorEncounterLocalId -eq [uint16]$descriptor.encounterLocalId -and
            $monsterNameDecodeValid

        $fileName = "actor-sequence-$sequence-position-$battlePosition.bin"
        $snapshotPath = Join-Path $outputRoot $fileName
        [IO.File]::WriteAllBytes($snapshotPath, $payload)
        $snapshots.Add([pscustomobject][ordered]@{
            sequence = $sequence
            observedAtUtc = [DateTimeOffset]::FromUnixTimeMilliseconds(
                $wallUnixMs).UtcDateTime.ToString("o")
            battlePosition = $battlePosition
            side = $(if ($battlePosition -lt 14) { "Friendly" } else { "Enemy" })
            phase = $(if ($phaseValue -eq 3) { "PeriodicChanged" } else { "ActorCreated" })
            stateIndexAt0x1A = [int]$stateIndex
            candidateActorStateAt0x1D8 = [int]$actorState
            candidatePositionAt0xDB0 = [int]$objectBattlePosition
            actorEntityIdAt0xDB0 = [uint16]$actorEntityId
            actorLevelAt0xDB7 = $actorLevel
            actorEncounterLocalIdAt0xDBC = [uint16]$actorEncounterLocalId
            currentHitPointsAt0xDE8 = [int]$currentVital0
            currentMagicPointsAt0xDEC = [int]$currentVital1
            maximumHitPointsAt0xDF0 = [int]$maximumVital0
            maximumMagicPointsAt0xDF4 = [int]$maximumVital1
            descriptorBindingValid = $descriptorBindingValid
            descriptorSequence = $(if ($null -ne $descriptor) { [uint64]$descriptor.sequence } else { $null })
            descriptorBattlePosition = $(if ($null -ne $descriptor) { [int]$descriptor.battlePosition } else { $null })
            actorLevel = $(if ($null -ne $descriptor) { [int]$descriptor.level } else { $null })
            encounterLocalId = $(if ($null -ne $descriptor) { [int]$descriptor.encounterLocalId } else { $null })
            descriptorActorEntityId = $(if ($null -ne $descriptor) { [uint16]$descriptor.actorEntityId } else { $null })
            descriptorActorKindFlags = $(if ($null -ne $descriptor) { [uint16]$descriptor.actorKindFlags } else { $null })
            descriptorOrdinaryStaticMonster = $null -ne $descriptor -and
                [bool]$descriptor.ordinaryStaticMonster
            descriptorByteLength = $(if ($null -ne $descriptor) { [int]$descriptor.byteLength } else { $null })
            descriptorSha256 = $(if ($null -ne $descriptor) { [string]$descriptor.sha256 } else { $null })
            descriptorSpeciesSignatureSha256 = $(if ($null -ne $descriptor) {
                [string]$descriptor.speciesSignatureSha256
            } else { $null })
            monsterNameOriginal = $monsterNameOriginal
            monsterNameEncoding = $(if ($monsterNameDecodeValid) { "CP936" } else { $null })
            monsterNameByteLength = $monsterNameBytes.Length
            monsterNameBytesHex = $(if ($monsterNameBytes.Length -gt 0) {
                ([BitConverter]::ToString($monsterNameBytes)).Replace("-", "")
            } else { $null })
            monsterNameBytesSha256 = $(if ($monsterNameBytes.Length -gt 0) {
                Get-ByteArraySha256 -Bytes $monsterNameBytes
            } else { $null })
            monsterNameDecodeValid = $monsterNameDecodeValid
            monsterIdentityBindingValid = $monsterIdentityBindingValid
            currentMaximumPairConsumerVerified = $battlePosition -lt 14 -and
                $maximumVital0 -gt 0 -and
                $maximumVital1 -ge 0 -and $currentVital0 -ge 0 -and
                $currentVital1 -ge 0 -and $currentVital0 -le $maximumVital0 -and
                $currentVital1 -le $maximumVital1
            hpMpIdentityStatus = $(if ($battlePosition -lt 14) {
                "FriendlyHitPointsMagicPointsConsumerVerified"
            } else {
                "EnemyHitPointsMagicPointsNotProjectedByExactBuild"
            })
            legacyCandidateInvariantMatched = $objectBattlePosition -eq $battlePosition -and
                $stateIndex -lt 0x1000 -and $actorState -ge 0
            byteLength = $payload.Length
            sha256 = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
            temporarySnapshotFile = $fileName
        })
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

$manifest = [ordered]@{
    schemaVersion = "god2-battle-actor-snapshot-manifest-v3"
    sessionId = $SessionId
    sourceApi = "BattleActorSnapshot"
    capturePoint = "RVA 0x00150818 after RVA 0x0014BDF0 returns"
    periodicCapturePoint = "20ms sampler; changed actors only; 60ms per-position throttle; 1024 snapshot session cap"
    stateBinding = "actor+0x1A contains the state-pool index used by BattleStateSnapshot"
    vitalPairBinding = "For friendly positions 0..13 only, actor+0xDE8/+0xDEC are current HP/MP and actor+0xDF0/+0xDF4 are maximum HP/MP; exact-build dispatch skips the vital writers for enemy positions 14..27"
    vitalPairSemanticStatus = "FriendlyHitPointsMagicPointsConsumerVerified"
    enemyVitalProjectionStatus = "ExactBuildDoesNotProjectEnemyMaximumHpMp"
    descriptorBinding = "The latest preceding HandlerDecoded 0x1C record for the same battle position supplies entity ID, kind flags, level and 16-bit encounter-local identity; its 45-byte SHA-256 is preserved in each snapshot row"
    monsterNameBinding = "For enemy ordinary-static-monster descriptors only (position 14..27 and kindFlags&0x6000 == 0), actor+0xDBE is decoded as a bounded null-terminated CP936 name after entity ID, level and 16-bit encounter-local ID agree with that exact descriptor. Friendly actors and enemy PK-player branches are never decoded into manifest names."
    monsterNameStaticEvidence = @(
        "God2_opt RVA 0x0014BDF0 actor builder copies the resolved ordinary-monster identity block to actor+0xDB0 and is called at RVA 0x00150818"
        "Preserved original-server actor snapshot sequence 1521 position 22 contains actor+0xDBE bytes CF C9 BA FC, exact CP936 for 仙狐, bound to its Lv6/local-ID-4 descriptor"
    )
    vitalPairStaticEvidence = @(
        "God2_opt RVA 0x0014BD37-0x0014BD70 binds 0x88 selected-position values to actor maxima"
        "God2_opt RVA 0x001177C0-0x0011783B stores the two current/maximum pairs"
        "God2_opt RVA 0x000D98F0-0x000D9A67 clamps current<=maximum, calculates current/maximum*100, and renders %d/%d"
        "God2_opt read-only data RVA 0x003F0F18 identifies the CHARHPMP domain"
        "Controlled official Skill commands at sequences 2809/2920/3027 precede MP 63->58->53->48, matching observed MP cost 5"
        "God2_opt RVA 0x0014B85A-0x0014B86F and 0x00150A73-0x00150A8D divide battle position by 14 and skip the HP/MP writers when the quotient is non-zero"
        "Preserved original-server traces keep enemy +0xDE8/+0xDEC/+0xDF0/+0xDF4 at 0xCDCDCDCD; +0xDE8 changes only to death-state zero"
    )
    gameMemoryWritten = $false
    rawPacketDataIncluded = $false
    snapshotCount = $snapshots.Count
    snapshots = @($snapshots)
}
$manifestPath = Join-Path $outputRoot "manifest.json"
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
$manifest | ConvertTo-Json -Depth 8
