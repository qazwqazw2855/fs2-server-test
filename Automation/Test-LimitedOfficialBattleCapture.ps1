param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,
    [ValidateRange(-1, 2147483647)]
    [int64] $FriendlyPhysicalDefense = -1,
    [switch] $NoFail
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'God2Classic\PacketCapture\Sessions\Codex'))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
if (-not $sessionRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Session path escaped the approved capture root.'
}

$packetPath = Join-Path $sessionRoot 'raw\injected-packets.jsonl'
$healthPath = Join-Path $sessionRoot 'reports\semantic-shared-ring.json'
$statusPath = Join-Path $sessionRoot 'reports\headless-enhanced-capture.json'
$policyPath = Join-Path $sessionRoot 'reports\battle-campaign-policy.json'
$uiProbePath = Join-Path $sessionRoot 'reports\battle-ui-vital-probe.json'
$captureLogPath = Join-Path $sessionRoot 'reports\enhanced-capture.log'
$actorManifestPath = Join-Path $repoRoot (
    'Artifacts\AnalysisScratch\BattleActorSnapshots\' + $SessionId + '\manifest.json')
$stateManifestPath = Join-Path $repoRoot (
    'Artifacts\AnalysisScratch\BattleStateSnapshots\' + $SessionId + '\manifest.json')

foreach ($required in @($packetPath, $healthPath, $statusPath, $policyPath,
    $uiProbePath, $captureLogPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required battle campaign artifact is missing: $required"
    }
}

$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$status.Status -cne 'DETACHED' -or -not [bool]$status.StrictUnloadVerified) {
    throw 'Battle campaign must be cleanly detached before completeness analysis.'
}
$health = Get-Content -LiteralPath $healthPath -Raw -Encoding UTF8 | ConvertFrom-Json
$uiProbe = Get-Content -LiteralPath $uiProbePath -Raw -Encoding UTF8 | ConvertFrom-Json
$uiProbeApplied = [string]$uiProbe.status -ceq 'APPLIED'
$captureLog = Get-Content -LiteralPath $captureLogPath -Raw -Encoding UTF8
$uiProbeCounterMatch = [regex]::Match($captureLog,
    'battle ui vital probe counters invoked=(\d+) accepted=(\d+) rejected=(\d+) payloadBytes=41 pointerValuesPersisted=0')
$uiProbeInvoked = if ($uiProbeCounterMatch.Success) {
    [int64]$uiProbeCounterMatch.Groups[1].Value
} else { 0 }
$uiProbeAccepted = if ($uiProbeCounterMatch.Success) {
    [int64]$uiProbeCounterMatch.Groups[2].Value
} else { 0 }
$uiProbeRejected = if ($uiProbeCounterMatch.Success) {
    [int64]$uiProbeCounterMatch.Groups[3].Value
} else { 0 }
$uiProbeRuntimeHealthy = $uiProbeCounterMatch.Success -and
    $uiProbeInvoked -gt 0 -and $uiProbeAccepted -gt 0
$actorManifest = if (Test-Path -LiteralPath $actorManifestPath -PathType Leaf) {
    Get-Content -LiteralPath $actorManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$stateManifest = if (Test-Path -LiteralPath $stateManifestPath -PathType Leaf) {
    Get-Content -LiteralPath $stateManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }

function Get-Opcode([object] $Record) {
    $value = [string]$Record.Opcode
    if ($value -match '^0x([0-9A-Fa-f]{1,2})$') {
        return [Convert]::ToInt32($Matches[1], 16)
    }
    return $null
}

function Get-Direction([object] $Record) {
    $value = [string]$Record.PacketDirection
    if ([string]::IsNullOrWhiteSpace($value)) { $value = [string]$Record.direction }
    return $value
}

function Get-RecordUnixMs([object] $Record) {
    if ($null -ne $Record.ObservedAtUnixMs) { return [int64]$Record.ObservedAtUnixMs }
    return [int64]$Record.wallUnixMs
}

function Convert-HexBytes([string] $Hex) {
    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0 -or
        $Hex -cnotmatch '^[0-9A-Fa-f]+$') {
        return [byte[]]@()
    }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

function Get-Int16LittleEndian([byte[]] $Bytes, [int] $Offset) {
    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) { return $null }
    return [BitConverter]::ToInt16($Bytes, $Offset)
}

function Get-Int32LittleEndian([byte[]] $Bytes, [int] $Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) { return $null }
    return [BitConverter]::ToInt32($Bytes, $Offset)
}

function Get-UInt32LittleEndian([byte[]] $Bytes, [int] $Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) { return $null }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Get-ByteArraySha256([byte[]] $Bytes) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hasher.ComputeHash($Bytes))).Replace('-', '')
    }
    finally { $hasher.Dispose() }
}

function Get-TenthDamage([int64] $HitPoints, [string] $Rounding) {
    switch ($Rounding) {
        'Floor' { return [int64][Math]::Floor($HitPoints / 10.0) }
        'Ceiling' { return [int64][Math]::Ceiling($HitPoints / 10.0) }
        'NearestHalfUp' { return [int64][Math]::Floor(($HitPoints / 10.0) + 0.5) }
        default { throw "Unsupported poison rounding mode: $Rounding" }
    }
}

function Get-BasicDamageCluster([object[]] $Rows) {
    $values = @($Rows | Select-Object -ExpandProperty damage -Unique | Sort-Object)
    if ($values.Count -eq 1) {
        return [pscustomobject]@{
            valid = $true
            nonCriticalDamage = [int64]$values[0]
            nonCriticalSampleCount = @($Rows | Where-Object {
                [int64]$_.damage -eq [int64]$values[0]
            }).Count
            doubleCriticalSampleCount = 0
            classification = 'SingleStableNonCriticalCluster'
        }
    }
    if ($values.Count -eq 2 -and [int64]$values[1] -eq (2 * [int64]$values[0])) {
        return [pscustomobject]@{
            valid = $true
            nonCriticalDamage = [int64]$values[0]
            nonCriticalSampleCount = @($Rows | Where-Object {
                [int64]$_.damage -eq [int64]$values[0]
            }).Count
            doubleCriticalSampleCount = @($Rows | Where-Object {
                [int64]$_.damage -eq [int64]$values[1]
            }).Count
            classification = 'NonCriticalPlusExactDoubleCriticalCandidate'
        }
    }
    return [pscustomobject]@{
        valid = $false
        nonCriticalDamage = $null
        nonCriticalSampleCount = 0
        doubleCriticalSampleCount = 0
        classification = 'UnstableOrNonDoubleDamageClusters'
    }
}

function Get-MonsterIdentityAtSequence {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Identities,
        [Parameter(Mandatory = $true)] [object[]] $Descriptors,
        [Parameter(Mandatory = $true)] [int] $BattlePosition,
        [Parameter(Mandatory = $true)] [uint64] $Sequence
    )
    # A replacement descriptor immediately closes the preceding identity epoch,
    # even when the replacement actor-name snapshot has not arrived yet.  Never
    # fall back to an older named identity for the same reused battle position.
    $currentDescriptors = @($Descriptors | Where-Object {
        [int]$_.battlePosition -eq $BattlePosition -and
        [uint64]$_.sequence -lt $Sequence
    } | Sort-Object sequence -Descending)
    if ($currentDescriptors.Count -eq 0) { return $null }
    [uint64]$currentDescriptorSequence = $currentDescriptors[0].sequence
    $matches = @($Identities | Where-Object {
        [int]$_.battlePosition -eq $BattlePosition -and
        [uint64]$_.descriptorSequence -eq $currentDescriptorSequence -and
        [uint64]$_.identitySnapshotSequence -lt $Sequence
    } | Sort-Object descriptorSequence, identitySnapshotSequence -Descending)
    if ($matches.Count -eq 0) { return $null }
    return $matches[0]
}

$records = [Collections.Generic.List[object]]::new()
$parseFailures = 0
foreach ($line in [IO.File]::ReadLines($packetPath)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $record = $line | ConvertFrom-Json
        $opcode = Get-Opcode $record
        if ($null -eq $opcode) { continue }
        $records.Add([pscustomobject]@{
            sequence = [uint64]$record.sequence
            unixMs = Get-RecordUnixMs $record
            direction = Get-Direction $record
            opcode = [int]$opcode
            payloadHex = [string]$record.PayloadHex
            captureStage = [string]$record.CaptureStage
            messageType = [string]$record.MessageType
            api = [string]$record.Api
            evidenceIncomplete = [bool]$record.EvidenceIncomplete
        })
    }
    catch { $parseFailures++ }
}
$orderedRecords = @($records | Sort-Object sequence)

$actorSnapshots = if ($null -ne $actorManifest) { @($actorManifest.snapshots) } else { @() }
$stateSnapshots = if ($null -ne $stateManifest) { @($stateManifest.snapshots) } else { @() }
$encounters = [Collections.Generic.List[object]]::new()
$primaryStarts = @($orderedRecords | Where-Object {
    $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x82 -and
    $_.captureStage -ceq 'PostDecrypt'
})
$crossVersionStarts = @($orderedRecords | Where-Object {
    $_.direction -ceq 'ServerToClient' -and $_.opcode -in @(0x8D, 0xD6)
})
$startRows = @(if ($primaryStarts.Count -gt 0) { $primaryStarts } else { $crossVersionStarts })

for ($encounterIndex = 0; $encounterIndex -lt $startRows.Count; $encounterIndex++) {
    $start = $startRows[$encounterIndex]
    $nextStart = if ($encounterIndex + 1 -lt $startRows.Count) {
        $startRows[$encounterIndex + 1]
    } else { $null }
    $windowEndSequence = if ($null -ne $nextStart) {
        [uint64]$nextStart.sequence
    } else { [uint64]::MaxValue }
    $candidateRows = @($orderedRecords | Where-Object {
        [uint64]$_.sequence -ge [uint64]$start.sequence -and
        [uint64]$_.sequence -lt $windowEndSequence
    })
    $exactTerminals = @($candidateRows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x89 -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $crossVersionTerminals = @($candidateRows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0xE6
    })
    $terminal = if ($exactTerminals.Count -gt 0) {
        $exactTerminals[0]
    } elseif ($crossVersionTerminals.Count -gt 0) {
        $crossVersionTerminals[0]
    } else { $null }
    $analysisEndUnixMs = if ($null -ne $terminal) {
        [int64]$terminal.unixMs + 5000
    } elseif ($null -ne $nextStart) {
        [int64]$nextStart.unixMs - 1
    } else { [int64]::MaxValue }
    if ($null -ne $nextStart) {
        $analysisEndUnixMs = [Math]::Min(
            $analysisEndUnixMs, [int64]$nextStart.unixMs - 1)
    }
    $campaignRows = @($candidateRows | Where-Object {
        [int64]$_.unixMs -le $analysisEndUnixMs
    })
    $encounters.Add([pscustomobject][ordered]@{
        encounterNumber = $encounterIndex + 1
        startSequence = [uint64]$start.sequence
        startUnixMs = [int64]$start.unixMs
        startOpcode = ('0x{0:X2}' -f [int]$start.opcode)
        startAuthority = if ($start.opcode -eq 0x82) {
            'ExactCurrentBuildObservedBattleEntry'
        } else { 'CrossVersionFallbackBoundary' }
        terminalSequence = if ($null -ne $terminal) { [uint64]$terminal.sequence } else { $null }
        terminalUnixMs = if ($null -ne $terminal) { [int64]$terminal.unixMs } else { $null }
        terminalOpcode = if ($null -ne $terminal) {
            '0x{0:X2}' -f [int]$terminal.opcode
        } else { $null }
        terminalAuthority = if ($null -eq $terminal) { 'Missing' }
            elseif ($terminal.opcode -eq 0x89) { 'ExactCurrentBuildHandlerDecodedSettlement' }
            else { 'CrossVersionFallbackBoundary' }
        analysisEndUnixMs = $analysisEndUnixMs
        records = $campaignRows
    })
}

$boundTerminalSequences = @($encounters | Where-Object {
    $null -ne $_.terminalSequence
} | ForEach-Object { [uint64]$_.terminalSequence })
$unboundTerminalCount = @($orderedRecords | Where-Object {
    $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x89 -and
    $_.captureStage -ceq 'HandlerDecoded' -and
    [uint64]$_.sequence -notin $boundTerminalSequences
}).Count

$encounterResults = @($encounters | ForEach-Object {
    $encounter = $_
    $rows = @($encounter.records)
    $endUnixMs = [int64]$encounter.analysisEndUnixMs
    $actors = @($actorSnapshots | Where-Object {
        $timestamp = [DateTimeOffset]::Parse([string]$_.observedAtUtc).ToUnixTimeMilliseconds()
        $timestamp -ge [int64]$encounter.startUnixMs -and $timestamp -le $endUnixMs
    })
    $states = @($stateSnapshots | Where-Object {
        $timestamp = [DateTimeOffset]::Parse([string]$_.observedAtUtc).ToUnixTimeMilliseconds()
        $timestamp -ge [int64]$encounter.startUnixMs -and $timestamp -le $endUnixMs
    })
    $bootstrapRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x82 -and
        $_.captureStage -ceq 'PostDecrypt'
    })
    $commandRows = @($rows | Where-Object {
        $_.direction -ceq 'ClientToServer' -and $_.opcode -eq 0x35
    })
    $effectRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x83 -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $terminalRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x89 -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $descriptorRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x1C -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $initializationRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x86 -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $vitalRows = @($rows | Where-Object {
        $_.direction -ceq 'ServerToClient' -and $_.opcode -eq 0x88 -and
        $_.captureStage -ceq 'HandlerDecoded'
    })
    $boundaryAcknowledgements = @($rows | Where-Object {
        $_.direction -ceq 'ClientToServer' -and $_.opcode -in @(0x36, 0x1C)
    })
    $commandDetails = @($commandRows | ForEach-Object {
        $bytes = Convert-HexBytes ([string]$_.payloadHex)
        if ($bytes.Length -eq 20 -and $bytes[2] -eq 0x35) {
            [pscustomobject]@{
                sequence = [uint64]$_.sequence
                unixMs = [int64]$_.unixMs
                battlePosition = [int]$bytes[3]
                actionCode = [int]($bytes[4] -band 0x7F)
                continuation = ($bytes[4] -band 0x80) -ne 0
                targetMask0 = [BitConverter]::ToUInt16($bytes, 7)
                targetMask1 = [BitConverter]::ToUInt16($bytes, 9)
                targetMask2 = [BitConverter]::ToUInt16($bytes, 11)
                battleContextValue = [BitConverter]::ToUInt16($bytes, 13)
                actionParameterCandidate = [BitConverter]::ToUInt32($bytes, 15)
            }
        }
    })
    $playerPositions = @($commandDetails | Select-Object -ExpandProperty battlePosition -Unique)
    $descriptorDetails = @($descriptorRows | ForEach-Object {
        $bytes = Convert-HexBytes ([string]$_.payloadHex)
        if ($bytes.Length -eq 45 -and $bytes[0] -eq 0x1C) {
            $speciesDescriptor = [byte[]]$bytes.Clone()
            $speciesDescriptor[1] = 0
            $speciesDescriptor[3] = 0
            $speciesDescriptor[4] = 0
            [pscustomobject]@{
                sequence = [uint64]$_.sequence
                battlePosition = [int]$bytes[1]
                level = [int]$bytes[2]
                actorEntityId = [uint16][BitConverter]::ToUInt16($bytes, 3)
                actorKindFlags = [uint16][BitConverter]::ToUInt16($bytes, 5)
                encounterLocalId = [uint16][BitConverter]::ToUInt16($bytes, 7)
                ordinaryStaticMonster = $bytes[1] -ge 14 -and
                    (([BitConverter]::ToUInt16($bytes, 5) -band 0x6000) -eq 0)
                descriptorSha256 = Get-ByteArraySha256 $bytes
                descriptorSpeciesSignatureSha256 = Get-ByteArraySha256 $speciesDescriptor
                side = if ($bytes[1] -ge 14) { 'Enemy' } else { 'Friendly' }
            }
        }
    })
    $enemyDescriptors = @($descriptorDetails | Where-Object {
        $_.battlePosition -ge 14
    } | Sort-Object sequence)
    $enemyIdentityConflicts = [Collections.Generic.List[object]]::new()
    $enemyIdentities = @($actors | Where-Object {
        [int]$_.battlePosition -ge 14 -and
        [bool]$_.descriptorBindingValid -and
        [bool]$_.descriptorOrdinaryStaticMonster -and
        [bool]$_.monsterNameDecodeValid -and
        [bool]$_.monsterIdentityBindingValid -and
        -not [string]::IsNullOrWhiteSpace([string]$_.monsterNameOriginal)
    } | Group-Object descriptorSequence | ForEach-Object {
        $identitySnapshots = @($_.Group | Sort-Object sequence)
        $descriptorSequence = [uint64]$_.Name
        $descriptors = @($descriptorDetails | Where-Object {
            [uint64]$_.sequence -eq $descriptorSequence
        })
        $identityTuples = @($identitySnapshots | ForEach-Object {
            '{0}/{1}/{2}/{3}/{4}' -f [int]$_.battlePosition,
                [uint16]$_.descriptorActorEntityId,
                [uint16]$_.encounterLocalId,
                [string]$_.descriptorSha256,
                [string]$_.monsterNameBytesHex
        } | Select-Object -Unique)
        if ($descriptors.Count -ne 1 -or $identityTuples.Count -ne 1) {
            $enemyIdentityConflicts.Add([pscustomobject]@{
                descriptorSequence = $descriptorSequence
                descriptorMatchCount = $descriptors.Count
                distinctIdentityTupleCount = $identityTuples.Count
                reason = 'DescriptorOrActorNameIdentityIsNotUnique'
            })
            return
        }
        $descriptor = $descriptors[0]
        $snapshot = $identitySnapshots[0]
        $matchesDescriptor = [int]$snapshot.battlePosition -eq
                [int]$descriptor.battlePosition -and
            [uint16]$snapshot.descriptorActorEntityId -eq
                [uint16]$descriptor.actorEntityId -and
            [int]$snapshot.actorLevelAt0xDB7 -eq [int]$descriptor.level -and
            [uint16]$snapshot.encounterLocalId -eq
                [uint16]$descriptor.encounterLocalId -and
            [string]$snapshot.descriptorSha256 -ceq
                [string]$descriptor.descriptorSha256 -and
            [string]$snapshot.descriptorSpeciesSignatureSha256 -ceq
                [string]$descriptor.descriptorSpeciesSignatureSha256
        if (-not $matchesDescriptor) {
            $enemyIdentityConflicts.Add([pscustomobject]@{
                descriptorSequence = $descriptorSequence
                descriptorMatchCount = 1
                distinctIdentityTupleCount = 1
                reason = 'ActorNameAndExactDescriptorFieldsDisagree'
            })
            return
        }
        $identityKey = '{0}/{1}/{2}/{3}/{4}' -f
            [int]$encounter.encounterNumber, [int]$descriptor.battlePosition,
            $descriptorSequence, [string]$descriptor.descriptorSha256,
            [string]$snapshot.monsterNameBytesSha256
        [pscustomobject]@{
            identityKey = $identityKey
            battlePosition = [int]$descriptor.battlePosition
            descriptorSequence = $descriptorSequence
            identitySnapshotSequence = [uint64]$snapshot.sequence
            descriptorSha256 = [string]$descriptor.descriptorSha256
            descriptorSpeciesSignatureSha256 =
                [string]$descriptor.descriptorSpeciesSignatureSha256
            actorEntityId = [uint16]$descriptor.actorEntityId
            actorKindFlags = [uint16]$descriptor.actorKindFlags
            level = [int]$descriptor.level
            encounterLocalId = [uint16]$descriptor.encounterLocalId
            monsterNameOriginal = [string]$snapshot.monsterNameOriginal
            monsterNameEncoding = [string]$snapshot.monsterNameEncoding
            monsterNameBytesHex = [string]$snapshot.monsterNameBytesHex
            monsterNameBytesSha256 = [string]$snapshot.monsterNameBytesSha256
            authority = 'ExactActorNameAt0xDBEPlusSamePositionDescriptorSha256'
        }
    })
    $boundDescriptorSequences = @($enemyIdentities | ForEach-Object {
        [uint64]$_.descriptorSequence
    })
    $unboundEnemyDescriptors = @($enemyDescriptors | Where-Object {
        [uint64]$_.sequence -notin $boundDescriptorSequences
    })
    $effectDetails = @($effectRows | ForEach-Object {
        $bytes = Convert-HexBytes ([string]$_.payloadHex)
        if ($bytes.Length -eq 15 -and $bytes[0] -eq 0x83) {
            [uint16]$friendlyTargetMask = [BitConverter]::ToUInt16($bytes, 4)
            [uint16]$enemyTargetMask = [BitConverter]::ToUInt16($bytes, 6)
            $targets = @(
                for ($slot = 0; $slot -lt 14; $slot++) {
                    if (($friendlyTargetMask -band (1 -shl $slot)) -ne 0) { $slot }
                    if (($enemyTargetMask -band (1 -shl $slot)) -ne 0) { 14 + $slot }
                }
            )
            [pscustomobject]@{
                sequence = [uint64]$_.sequence
                unixMs = [int64]$_.unixMs
                effectKind = [int]$bytes[1]
                sourceBattlePosition = [int]$bytes[2]
                sourceSide = if ($bytes[2] -ge 14) { 'Enemy' } else { 'Friendly' }
                friendlyTargetMask = ('0x{0:X4}' -f $friendlyTargetMask)
                enemyTargetMask = ('0x{0:X4}' -f $enemyTargetMask)
                targetBattlePositions = $targets
                signedResult = Get-Int16LittleEndian $bytes 9
                auxiliaryValue0 = [BitConverter]::ToUInt16($bytes, 11)
                auxiliaryValue1 = [BitConverter]::ToUInt16($bytes, 13)
            }
        }
    })
    $unboundMonsterEffectIdentities = [Collections.Generic.List[object]]::new()
    foreach ($effect in $effectDetails) {
        $sourceIdentity = if ([int]$effect.sourceBattlePosition -ge 14) {
            Get-MonsterIdentityAtSequence -Identities $enemyIdentities `
                -Descriptors $enemyDescriptors `
                -BattlePosition ([int]$effect.sourceBattlePosition) `
                -Sequence ([uint64]$effect.sequence)
        } else { $null }
        $enemyTargetPositions = @($effect.targetBattlePositions | Where-Object {
            [int]$_ -ge 14
        })
        $targetIdentities = @($enemyTargetPositions | ForEach-Object {
            Get-MonsterIdentityAtSequence -Identities $enemyIdentities `
                -Descriptors $enemyDescriptors `
                -BattlePosition ([int]$_) -Sequence ([uint64]$effect.sequence)
        } | Where-Object { $null -ne $_ })
        $requiredIdentityCount = $(if ([int]$effect.sourceBattlePosition -ge 14) {
            1
        } else { 0 }) + $enemyTargetPositions.Count
        $boundIdentityCount = $(if ($null -ne $sourceIdentity) { 1 } else { 0 }) +
            $targetIdentities.Count
        $bindingComplete = $boundIdentityCount -eq $requiredIdentityCount
        $effect | Add-Member -NotePropertyName sourceMonsterIdentityKey `
            -NotePropertyValue $(if ($null -ne $sourceIdentity) {
                [string]$sourceIdentity.identityKey
            } else { $null }) -Force
        $effect | Add-Member -NotePropertyName sourceMonsterNameOriginal `
            -NotePropertyValue $(if ($null -ne $sourceIdentity) {
                [string]$sourceIdentity.monsterNameOriginal
            } else { $null }) -Force
        $effect | Add-Member -NotePropertyName sourceMonsterEvidenceScope `
            -NotePropertyValue $(if ($null -ne $sourceIdentity) {
                '{0}/{1}' -f [string]$sourceIdentity.monsterNameBytesSha256,
                    [string]$sourceIdentity.descriptorSpeciesSignatureSha256
            } else { $null }) -Force
        $effect | Add-Member -NotePropertyName targetMonsterIdentityKeys `
            -NotePropertyValue @($targetIdentities | ForEach-Object {
                [string]$_.identityKey
            }) -Force
        $effect | Add-Member -NotePropertyName targetMonsterNamesOriginal `
            -NotePropertyValue @($targetIdentities | ForEach-Object {
                [string]$_.monsterNameOriginal
            }) -Force
        $effect | Add-Member -NotePropertyName targetMonsterEvidenceScopes `
            -NotePropertyValue @($targetIdentities | ForEach-Object {
                '{0}/{1}' -f [string]$_.monsterNameBytesSha256,
                    [string]$_.descriptorSpeciesSignatureSha256
            }) -Force
        $effect | Add-Member -NotePropertyName monsterIdentityBindingRequiredCount `
            -NotePropertyValue $requiredIdentityCount -Force
        $effect | Add-Member -NotePropertyName monsterIdentityBindingComplete `
            -NotePropertyValue $bindingComplete -Force
        if (-not $bindingComplete) {
            $unboundMonsterEffectIdentities.Add([pscustomobject]@{
                effectSequence = [uint64]$effect.sequence
                sourceBattlePosition = [int]$effect.sourceBattlePosition
                targetBattlePositions = @($effect.targetBattlePositions)
                requiredIdentityCount = $requiredIdentityCount
                boundIdentityCount = $boundIdentityCount
                reason = 'MonsterNamePositionDescriptorEpochBindingMissing'
            })
        }
    }
    # The official server sends the damage result, not its hidden attack/defense
    # operands.  Preserve the strongest reversible inference the current baseline
    # permits without promoting it as an official formula.  Action code 2 is the
    # controlled-capture Defend command.  Pairing the same stable enemy damage
    # family in normal and defending windows gives:
    #   normal = attack - defense
    #   defend = attack - defense - floor(defense / 2)
    # and therefore two adjacent defense/attack candidates until an independently
    # observed friendly physical-defense value selects one of them.
    $enemyDamageObservations = @($effectDetails | Where-Object {
        [bool]$_.monsterIdentityBindingComplete -and
        $_.sourceSide -ceq 'Enemy' -and [int]$_.signedResult -lt 0 -and
        @($_.targetBattlePositions).Count -eq 1 -and
        [int]$_.targetBattlePositions[0] -lt 14
    } | ForEach-Object {
        $effect = $_
        $targetPosition = [int]$effect.targetBattlePositions[0]
        $precedingCommand = @($commandDetails | Where-Object {
            [int]$_.battlePosition -eq $targetPosition -and
            [uint64]$_.sequence -lt [uint64]$effect.sequence
        } | Sort-Object sequence -Descending | Select-Object -First 1)
        [pscustomobject]@{
            sequence = [uint64]$effect.sequence
            unixMs = [int64]$effect.unixMs
            sourceBattlePosition = [int]$effect.sourceBattlePosition
            sourceMonsterIdentityKey = [string]$effect.sourceMonsterIdentityKey
            sourceMonsterNameOriginal = [string]$effect.sourceMonsterNameOriginal
            targetBattlePosition = $targetPosition
            effectKind = [int]$effect.effectKind
            auxiliaryValue0 = [uint16]$effect.auxiliaryValue0
            auxiliaryValue1 = [uint16]$effect.auxiliaryValue1
            damage = [Math]::Abs([int]$effect.signedResult)
            defenderCommandSequence = if ($precedingCommand.Count -eq 1) {
                [uint64]$precedingCommand[0].sequence
            } else { $null }
            defenderActionCode = if ($precedingCommand.Count -eq 1) {
                [int]$precedingCommand[0].actionCode
            } else { $null }
            defenderWasDefending = $precedingCommand.Count -eq 1 -and
                [int]$precedingCommand[0].actionCode -eq 2
        }
    })
    $baselinePhysicalAttackHypotheses = @($enemyDamageObservations | Group-Object {
        '{0}/{1}/{2}/{3}/{4}/{5}' -f $_.sourceMonsterIdentityKey,
            $_.sourceBattlePosition, $_.targetBattlePosition,
            $_.effectKind, $_.auxiliaryValue0, $_.auxiliaryValue1
    } | ForEach-Object {
        $family = @($_.Group)
        $normal = @($family | Where-Object { -not $_.defenderWasDefending })
        $defending = @($family | Where-Object { $_.defenderWasDefending })
        if ($normal.Count -eq 0 -or $defending.Count -eq 0) { return }
        $normalCluster = Get-BasicDamageCluster $normal
        $defendingCluster = Get-BasicDamageCluster $defending
        if (-not $normalCluster.valid -or -not $defendingCluster.valid) { return }
        [int64]$normalDamage = $normalCluster.nonCriticalDamage
        [int64]$defendingDamage = $defendingCluster.nonCriticalDamage
        if ($normalDamage -le $defendingDamage -or $defendingDamage -le 1) { return }
        [int64]$difference = $normalDamage - $defendingDamage
        $candidates = @(
            foreach ($defenseParity in 0, 1) {
                [int64]$defense = (2 * $difference) + $defenseParity
                [int64]$attack = $normalDamage + $defense
                [int64]$predictedNormal = [Math]::Max(1, $attack - $defense)
                [int64]$predictedDefend = [Math]::Max(
                    1, $attack - ($defense + [Math]::Floor($defense / 2.0)))
                if ($predictedNormal -eq $normalDamage -and
                    $predictedDefend -eq $defendingDamage) {
                    [pscustomobject]@{
                        physicalDefense = $defense
                        physicalAttack = $attack
                    }
                }
            }
        )
        $selected = @(if ($FriendlyPhysicalDefense -ge 0) {
            $candidates | Where-Object {
                [int64]$_.physicalDefense -eq $FriendlyPhysicalDefense
            }
        })
        [pscustomobject]@{
            familyKey = [string]$_.Name
            sourceMonsterIdentityKey = [string]$family[0].sourceMonsterIdentityKey
            sourceMonsterNameOriginal = [string]$family[0].sourceMonsterNameOriginal
            sourceBattlePosition = [int]$family[0].sourceBattlePosition
            targetBattlePosition = [int]$family[0].targetBattlePosition
            effectKind = [int]$family[0].effectKind
            auxiliaryValue0 = [uint16]$family[0].auxiliaryValue0
            auxiliaryValue1 = [uint16]$family[0].auxiliaryValue1
            normalDamage = $normalDamage
            defendingDamage = $defendingDamage
            normalSampleCount = [int]$normalCluster.nonCriticalSampleCount
            defendingSampleCount = [int]$defendingCluster.nonCriticalSampleCount
            normalDoubleCriticalCandidateCount = [int]$normalCluster.doubleCriticalSampleCount
            defendingDoubleCriticalCandidateCount = [int]$defendingCluster.doubleCriticalSampleCount
            normalDamageCluster = [string]$normalCluster.classification
            defendingDamageCluster = [string]$defendingCluster.classification
            stableWithinEachCondition = $true
            baselineCandidatePairs = $candidates
            suppliedFriendlyPhysicalDefense = if ($FriendlyPhysicalDefense -ge 0) {
                $FriendlyPhysicalDefense
            } else { $null }
            selectedBaselinePhysicalAttack = if ($selected.Count -eq 1) {
                [int64]$selected[0].physicalAttack
            } else { $null }
            baselinePairAgreement = $selected.Count -eq 1
            observedScopeRepeatReady = $normalCluster.nonCriticalSampleCount -ge 2 -and
                $defendingCluster.nonCriticalSampleCount -ge 2
            authority = 'DerivedBaselineHypothesis_NotOfficialFormulaEvidence'
            promotionBlockedReasons = @(
                if ($FriendlyPhysicalDefense -lt 0) {
                    'independently-observed-friendly-physical-defense-missing'
                } elseif ($selected.Count -ne 1) {
                    'supplied-friendly-physical-defense-does-not-fit-baseline-pair'
                }
                if ($normalCluster.nonCriticalSampleCount -lt 2 -or
                    $defendingCluster.nonCriticalSampleCount -lt 2) {
                    'repeat-samples-below-two-per-condition'
                }
                'enemy-action-not-yet-typed-as-unmodified-ordinary-physical-attack'
                'official-random-critical-element-skill-and-buff-operands-not-excluded'
                'baseline-formula-is-not-production-authoritative'
            )
        }
    })
    # A single skill result cannot distinguish an additive base value from a
    # multiplier: both can be fitted to one point.  Bind each friendly-origin
    # negative effect to the nearest preceding command by the same actor, retain
    # the command action parameter as a candidate (not an asserted Skill ID), and
    # compare BasicAttack (1) with Skill (3) under the same effect family.
    $friendlyDamageObservations = @($effectDetails | Where-Object {
        [bool]$_.monsterIdentityBindingComplete -and
        $_.sourceSide -ceq 'Friendly' -and [int]$_.signedResult -lt 0 -and
        @($_.targetBattlePositions).Count -eq 1 -and
        [int]$_.targetBattlePositions[0] -ge 14
    } | ForEach-Object {
        $effect = $_
        $precedingCommand = @($commandDetails | Where-Object {
            [int]$_.battlePosition -eq [int]$effect.sourceBattlePosition -and
            [uint64]$_.sequence -lt [uint64]$effect.sequence
        } | Sort-Object sequence -Descending | Select-Object -First 1)
        if ($precedingCommand.Count -ne 1) { return }
        [pscustomobject]@{
            sequence = [uint64]$effect.sequence
            sourceBattlePosition = [int]$effect.sourceBattlePosition
            targetBattlePosition = [int]$effect.targetBattlePositions[0]
            targetMonsterIdentityKey = if (@($effect.targetMonsterIdentityKeys).Count -eq 1) {
                [string]$effect.targetMonsterIdentityKeys[0]
            } else { $null }
            targetMonsterNameOriginal = if (@($effect.targetMonsterNamesOriginal).Count -eq 1) {
                [string]$effect.targetMonsterNamesOriginal[0]
            } else { $null }
            targetMonsterEvidenceScope = if (@($effect.targetMonsterEvidenceScopes).Count -eq 1) {
                [string]$effect.targetMonsterEvidenceScopes[0]
            } else { $null }
            effectKind = [int]$effect.effectKind
            auxiliaryValue0 = [uint16]$effect.auxiliaryValue0
            auxiliaryValue1 = [uint16]$effect.auxiliaryValue1
            damage = [Math]::Abs([int]$effect.signedResult)
            commandSequence = [uint64]$precedingCommand[0].sequence
            actionCode = [int]$precedingCommand[0].actionCode
            actionParameterCandidate = [uint32]$precedingCommand[0].actionParameterCandidate
        }
    })
    $skillCriticalityContradictions = @($friendlyDamageObservations |
        Where-Object { $_.actionCode -eq 3 } | Group-Object {
            '{0}/{1}/{2}/{3}/{4}/{5}/{6}' -f $_.targetMonsterIdentityKey,
                $_.sourceBattlePosition, $_.targetBattlePosition,
                $_.effectKind, $_.auxiliaryValue0, $_.auxiliaryValue1,
                $_.actionParameterCandidate
        } | ForEach-Object {
            $values = @($_.Group | Select-Object -ExpandProperty damage -Unique |
                Sort-Object)
            if ($values.Count -le 1) { return }
            [pscustomobject]@{
                familyKey = [string]$_.Name
                actionParameterCandidate = [uint32]$_.Group[0].actionParameterCandidate
                observedDamageClusters = $values
                exactDoublePattern = $values.Count -eq 2 -and
                    [int64]$values[1] -eq (2 * [int64]$values[0])
                authority = 'ContradictsStableNonCriticalSkillDamageHypothesisOrShowsUncontrolledModifiers'
            }
        })
    $skillDamagePairs = @($friendlyDamageObservations | Group-Object {
        '{0}/{1}/{2}/{3}/{4}/{5}' -f $_.targetMonsterIdentityKey,
            $_.sourceBattlePosition, $_.targetBattlePosition,
            $_.effectKind, $_.auxiliaryValue0, $_.auxiliaryValue1
    } | ForEach-Object {
        $family = @($_.Group)
        $basicRows = @($family | Where-Object { $_.actionCode -eq 1 })
        if ($basicRows.Count -eq 0) { return }
        $basicCluster = Get-BasicDamageCluster $basicRows
        if (-not $basicCluster.valid) { return }
        foreach ($skillGroup in @($family | Where-Object {
            $_.actionCode -eq 3
        } | Group-Object actionParameterCandidate)) {
            $skillRows = @($skillGroup.Group)
            $skillDamages = @($skillRows | Select-Object -ExpandProperty damage -Unique)
            if ($skillDamages.Count -ne 1) { continue }
            [int64]$basicDamage = $basicCluster.nonCriticalDamage
            [int64]$skillDamage = $skillDamages[0]
            [pscustomobject]@{
                targetMonsterIdentityKey = [string]$family[0].targetMonsterIdentityKey
                targetMonsterNameOriginal = [string]$family[0].targetMonsterNameOriginal
                targetMonsterEvidenceScope = [string]$family[0].targetMonsterEvidenceScope
                sourceBattlePosition = [int]$family[0].sourceBattlePosition
                targetBattlePosition = [int]$family[0].targetBattlePosition
                effectKind = [int]$family[0].effectKind
                actionParameterCandidate = [uint32]$skillGroup.Name
                basicDamage = $basicDamage
                skillDamage = $skillDamage
                additiveBaseDamageCandidate = $skillDamage - $basicDamage
                multiplicativeCoefficientCandidate = if ($basicDamage -gt 0) {
                    [decimal]$skillDamage / [decimal]$basicDamage
                } else { $null }
                basicSampleCount = [int]$basicCluster.nonCriticalSampleCount
                basicDoubleCriticalCandidateCount = [int]$basicCluster.doubleCriticalSampleCount
                basicDamageCluster = [string]$basicCluster.classification
                skillSampleCount = $skillRows.Count
                skillExactDoubleClusterObserved = $false
                stableWithinEachAction = $true
                modelIdentifiableFromThisPair = $false
                ambiguity = 'OneConditionFitsBothAdditiveAndMultiplicativeModels'
                authority = 'ObservedDamagePair_ActionParameterCandidate_StaticSkillBindingRequired'
            }
        }
    })
    $uiVitalDetails = @($rows | Where-Object {
        $_.api -ceq 'BattleUiVitalObservation' -and
        $_.captureStage -ceq 'BattleUiVital'
    } | ForEach-Object {
        $bytes = Convert-HexBytes ([string]$_.payloadHex)
        if ($bytes.Length -eq 41 -and $bytes[0] -in @(1, 2, 3, 4)) {
            $kind = [int]$bytes[0]
            $contextValue = Get-UInt32LittleEndian $bytes 5
            $element = Get-UInt32LittleEndian $bytes 9
            [pscustomobject]@{
                sequence = [uint64]$_.sequence
                unixMs = [int64]$_.unixMs
                kind = $kind
                kindName = @('Unknown', 'BarPercentage', 'ExactIntegerPair',
                    'VitalApplyHpMpTuple', 'VitalApplyHpOnlyTuple')[$kind]
                callerRva = ('0x{0:X8}' -f (Get-UInt32LittleEndian $bytes 1))
                context = if ($kind -in @(3, 4)) {
                    'OT32-{0:X8}' -f $contextValue
                } else { [string]$contextValue }
                element = [uint32]$element
                hpPairValid = $kind -in @(2, 3, 4)
                mpPairValid = $kind -eq 3 -and ($element -band 0x2) -ne 0
                value0 = Get-UInt32LittleEndian $bytes 13
                value1 = Get-UInt32LittleEndian $bytes 17
                value2 = Get-UInt32LittleEndian $bytes 21
                value3 = Get-UInt32LittleEndian $bytes 25
                originalThreadId = Get-UInt32LittleEndian $bytes 29
                correlatedPacketSequence = Get-UInt32LittleEndian $bytes 33
                probeEventSequence = Get-UInt32LittleEndian $bytes 37
                seriesKey = if ($kind -in @(3, 4)) {
                    'VitalApply/{0}/{1}' -f ('0x{0:X8}' -f (
                        Get-UInt32LittleEndian $bytes 1)),
                        ('OT32-{0:X8}' -f $contextValue)
                } else {
                    '{0}/{1}/{2}/{3}' -f @('Unknown', 'Bar', 'Text')[$kind],
                        ('0x{0:X8}' -f (Get-UInt32LittleEndian $bytes 1)),
                        $contextValue, $element
                }
            }
        }
    })
    $settlementDetails = @($terminalRows | ForEach-Object {
        $bytes = Convert-HexBytes ([string]$_.payloadHex)
        if ($bytes.Length -eq 57 -and $bytes[0] -eq 0x89) {
            $base = Get-Int32LittleEndian $bytes 5
            $credited = Get-Int32LittleEndian $bytes 9
            [pscustomobject]@{
                sequence = [uint64]$_.sequence
                experienceBase = $base
                experienceCredited = $credited
                experienceFieldsAgree = $null -ne $base -and $base -ge 0 -and $base -eq $credited
                rawRecordSha256 = Get-ByteArraySha256 $bytes
            }
        }
    })
    $directHpCorrelations = [Collections.Generic.List[object]]::new()
    $directSeries = @($uiVitalDetails | Where-Object {
        $_.kind -in @(2, 3, 4) -and [uint64]$_.value1 -gt 0
    } | Group-Object seriesKey)
    foreach ($series in $directSeries) {
        $samples = @($series.Group | Sort-Object unixMs, sequence)
        for ($sampleIndex = 1; $sampleIndex -lt $samples.Count; $sampleIndex++) {
            $before = $samples[$sampleIndex - 1]
            $after = $samples[$sampleIndex]
            if ([uint64]$before.value1 -ne [uint64]$after.value1) { continue }
            [int64]$delta = [int64]$after.value0 - [int64]$before.value0
            if ($delta -eq 0) { continue }
            $matchingEffects = @($effectDetails | Where-Object {
                [bool]$_.monsterIdentityBindingComplete -and
                [int64]$_.signedResult -eq $delta -and
                [int64]$_.unixMs -ge ([int64]$before.unixMs - 250) -and
                [int64]$_.unixMs -le ([int64]$after.unixMs + 750) -and
                @($_.targetBattlePositions).Count -eq 1 -and
                [int]$_.targetBattlePositions[0] -ge 14
            })
            foreach ($effect in $matchingEffects) {
                $directHpCorrelations.Add([pscustomobject]@{
                    targetBattlePosition = [int]$effect.targetBattlePositions[0]
                    targetMonsterIdentityKey = if (@($effect.targetMonsterIdentityKeys).Count -eq 1) {
                        [string]$effect.targetMonsterIdentityKeys[0]
                    } else { $null }
                    targetMonsterNameOriginal = if (@($effect.targetMonsterNamesOriginal).Count -eq 1) {
                        [string]$effect.targetMonsterNamesOriginal[0]
                    } else { $null }
                    maximumHitPoints = [uint32]$after.value1
                    beforeHitPoints = [uint32]$before.value0
                    signedResult = [int]$effect.signedResult
                    afterHitPoints = [uint32]$after.value0
                    effectSequence = [uint64]$effect.sequence
                    uiSequence = [uint64]$after.sequence
                    uiSource = [string]$after.kindName
                    uiSeriesKey = [string]$after.seriesKey
                    uiHpPairValid = [bool]$after.hpPairValid
                    uiMpPairValid = [bool]$after.mpPairValid
                    currentMagicPoints = if ([bool]$after.mpPairValid) {
                        [uint32]$after.value2
                    } else { $null }
                    maximumMagicPoints = if ([bool]$after.mpPairValid) {
                        [uint32]$after.value3
                    } else { $null }
                    exactTransitionAgreement = $true
                })
            }
        }
    }
    $directEnemyHpCandidates = @($directHpCorrelations |
        Group-Object targetMonsterIdentityKey | ForEach-Object {
            $maxima = @($_.Group | Select-Object -ExpandProperty maximumHitPoints -Unique)
            if ($maxima.Count -eq 1) {
                [pscustomobject]@{
                    targetMonsterIdentityKey = [string]$_.Name
                    targetMonsterNameOriginal = [string]$_.Group[0].targetMonsterNameOriginal
                    targetBattlePosition = [int]$_.Group[0].targetBattlePosition
                    maximumHitPoints = [uint32]$maxima[0]
                    transitionAgreementCount = $_.Count
                    authority = 'ExactUiCurrentMaximumPairAnd0x83TargetDeltaAgreement'
                }
            }
        })
    $directEnemyMpCandidates = @($directHpCorrelations | Where-Object {
        [bool]$_.uiMpPairValid -and $null -ne $_.maximumMagicPoints -and
        [uint64]$_.maximumMagicPoints -gt 0
    } | Group-Object targetMonsterIdentityKey | ForEach-Object {
        $maxima = @($_.Group | Select-Object -ExpandProperty maximumMagicPoints -Unique)
        if ($maxima.Count -eq 1) {
            [pscustomobject]@{
                targetMonsterIdentityKey = [string]$_.Name
                targetMonsterNameOriginal = [string]$_.Group[0].targetMonsterNameOriginal
                targetBattlePosition = [int]$_.Group[0].targetBattlePosition
                currentMagicPointsObservations = @($_.Group |
                    Select-Object -ExpandProperty currentMagicPoints -Unique)
                maximumMagicPoints = [uint32]$maxima[0]
                transitionAgreementCount = $_.Count
                authority = 'ExactUiSameObjectMpPairBoundByTargetedHpTransition'
            }
        }
    })

    $poisonHypotheses = [Collections.Generic.List[object]]::new()
    $singleEnemyDamage = @($effectDetails | Where-Object {
        [bool]$_.monsterIdentityBindingComplete -and
        [int]$_.signedResult -lt 0 -and
        @($_.targetBattlePositions).Count -eq 1 -and
        [int]$_.targetBattlePositions[0] -ge 14
    })
    foreach ($damageGroup in @($singleEnemyDamage | Group-Object {
        '{0}/{1}' -f [string]$_.targetMonsterIdentityKeys[0], [int]$_.effectKind
    })) {
        $ticks = @($damageGroup.Group | Sort-Object unixMs, sequence)
        if ($ticks.Count -lt 2) { continue }
        $targetPosition = [int]$ticks[0].targetBattlePositions[0]
        $targetMonsterIdentityKey = [string]$ticks[0].targetMonsterIdentityKeys[0]
        $targetMonsterNameOriginal = [string]$ticks[0].targetMonsterNamesOriginal[0]
        $priorTargetDamage = @($singleEnemyDamage | Where-Object {
            [string]$_.targetMonsterIdentityKeys[0] -ceq $targetMonsterIdentityKey -and
            [uint64]$_.sequence -lt [uint64]$ticks[0].sequence
        })
        if ($priorTargetDamage.Count -gt 0) { continue }
        $tickLimit = [Math]::Min(5, $ticks.Count)
        $tickDamages = @($ticks[0..($tickLimit - 1)] | ForEach-Object {
            [Math]::Abs([int]$_.signedResult)
        })
        if ($tickDamages[0] -le 0) { continue }
        foreach ($rounding in @('Floor', 'Ceiling', 'NearestHalfUp')) {
            [int64]$minimum = if ($rounding -ceq 'Ceiling') {
                [Math]::Max(1, (10 * [int64]$tickDamages[0]) - 9)
            } elseif ($rounding -ceq 'NearestHalfUp') {
                [Math]::Max(1, (10 * [int64]$tickDamages[0]) - 5)
            } else { 10 * [int64]$tickDamages[0] }
            [int64]$maximum = if ($rounding -ceq 'Floor') {
                (10 * [int64]$tickDamages[0]) + 9
            } elseif ($rounding -ceq 'Ceiling') {
                10 * [int64]$tickDamages[0]
            } else {
                (10 * [int64]$tickDamages[0]) + 4
            }
            for ([int64]$candidateHp = $minimum; $candidateHp -le $maximum; $candidateHp++) {
                [int64]$remaining = $candidateHp
                $matches = $true
                $expectedBarPercentages = [Collections.Generic.List[int]]::new()
                foreach ($damage in $tickDamages) {
                    $expected = Get-TenthDamage $remaining $rounding
                    if ($expected -ne [int64]$damage) {
                        $matches = $false
                        break
                    }
                    $remaining = [Math]::Max(0, $remaining - [int64]$damage)
                    $expectedBarPercentages.Add([int][Math]::Floor(
                        ($remaining * 100.0) / $candidateHp))
                }
                if ($matches) {
                    $matchingBarSeries = @($uiVitalDetails | Where-Object {
                        $_.kind -eq 1
                    } | Group-Object seriesKey | ForEach-Object {
                        $barRows = @($_.Group | Sort-Object unixMs, sequence)
                        $baseline = @($barRows | Where-Object {
                            [int]$_.value0 -eq 100 -and
                            [int64]$_.unixMs -ge [int64]$encounter.startUnixMs -and
                            [int64]$_.unixMs -le ([int64]$ticks[0].unixMs + 250)
                        })
                        if ($baseline.Count -eq 0) { return }
                        $agreementCount = 0
                        for ($tickIndex = 0; $tickIndex -lt $tickLimit; $tickIndex++) {
                            $tick = $ticks[$tickIndex]
                            $expectedPercent = $expectedBarPercentages[$tickIndex]
                            $agreement = @($barRows | Where-Object {
                                [int]$_.value0 -eq $expectedPercent -and
                                [int64]$_.unixMs -ge ([int64]$tick.unixMs - 250) -and
                                [int64]$_.unixMs -le ([int64]$tick.unixMs + 2000)
                            } | Select-Object -First 1)
                            if ($agreement.Count -gt 0) { $agreementCount++ }
                        }
                        if ($agreementCount -ge [Math]::Min(2, $tickLimit)) {
                            [pscustomobject]@{
                                seriesKey = [string]$_.Name
                                agreementCount = $agreementCount
                            }
                        }
                    })
                    $directFullHealthBaseline = @($uiVitalDetails | Where-Object {
                        $_.kind -in @(2, 3, 4) -and
                        [uint64]$_.value0 -eq [uint64]$candidateHp -and
                        [uint64]$_.value1 -eq [uint64]$candidateHp -and
                        [int64]$_.unixMs -ge [int64]$encounter.startUnixMs -and
                        [int64]$_.unixMs -le ([int64]$ticks[0].unixMs + 250)
                    })
                    if ($matchingBarSeries.Count -eq 0 -and
                        $directFullHealthBaseline.Count -eq 0) { continue }
                    $poisonHypotheses.Add([pscustomobject]@{
                        targetMonsterIdentityKey = $targetMonsterIdentityKey
                        targetMonsterNameOriginal = $targetMonsterNameOriginal
                        targetBattlePosition = $targetPosition
                        effectKind = [int]$ticks[0].effectKind
                        sourceBattlePosition = [int]$ticks[0].sourceBattlePosition
                        rounding = $rounding
                        maximumHitPoints = $candidateHp
                        observedTickDamages = $tickDamages
                        observedTickCount = $tickDamages.Count
                        firstTickIsFirstObservedTargetDamage = $true
                        fullHealthBarSeries = @($matchingBarSeries)
                        directFullHealthBaselineCount = $directFullHealthBaseline.Count
                        expectedBarPercentages = @($expectedBarPercentages)
                        authority = 'PoisonTenPercentRecurrenceCandidate'
                    })
                }
            }
        }
    }
    $poisonExactCandidates = @($poisonHypotheses |
        Group-Object targetMonsterIdentityKey | ForEach-Object {
            $solutions = @($_.Group | ForEach-Object {
                '{0}/{1}' -f $_.maximumHitPoints, $_.rounding
            } | Select-Object -Unique)
            $maxima = @($_.Group | Select-Object -ExpandProperty maximumHitPoints -Unique)
            if ($solutions.Count -eq 1 -and $maxima.Count -eq 1 -and
                [int]$_.Group[0].observedTickCount -ge 3) {
                [pscustomobject]@{
                    targetMonsterIdentityKey = [string]$_.Name
                    targetMonsterNameOriginal = [string]$_.Group[0].targetMonsterNameOriginal
                    targetBattlePosition = [int]$_.Group[0].targetBattlePosition
                    maximumHitPoints = [int64]$maxima[0]
                    rounding = [string]$_.Group[0].rounding
                    observedTickDamages = @($_.Group[0].observedTickDamages)
                    authority = 'UniqueThreeTickPoisonTenPercentIntegerSolution'
                }
            }
        })
    $crossValidatedHpCandidates = @($directEnemyHpCandidates | ForEach-Object {
        $direct = $_
        $poisonMatches = @($poisonHypotheses | Where-Object {
            [string]$_.targetMonsterIdentityKey -ceq
                [string]$direct.targetMonsterIdentityKey -and
            [int64]$_.maximumHitPoints -eq [int64]$direct.maximumHitPoints
        })
        [pscustomobject]@{
            targetMonsterIdentityKey = [string]$direct.targetMonsterIdentityKey
            targetMonsterNameOriginal = [string]$direct.targetMonsterNameOriginal
            targetBattlePosition = [int]$direct.targetBattlePosition
            maximumHitPoints = [uint32]$direct.maximumHitPoints
            directTransitionAgreementCount = [int]$direct.transitionAgreementCount
            poisonRecurrenceAgreementCount = $poisonMatches.Count
            authority = if ($poisonMatches.Count -gt 0) {
                'ExactUiPairPlus0x83DeltaPlusPoisonRecurrenceCrossValidated'
            } else { [string]$direct.authority }
        }
    })
    $matchedEffectStateTransitions = 0
    foreach ($effect in $effectDetails) {
        $match = @($states | Where-Object {
            [string]$_.phase -ceq 'ActionApplied' -and
            [Math]::Abs(
                [DateTimeOffset]::Parse([string]$_.observedAtUtc).ToUnixTimeMilliseconds() -
                [int64]$effect.unixMs) -le 500
        } | Select-Object -First 1)
        if ($match.Count -gt 0) { $matchedEffectStateTransitions++ }
    }
    $incompleteRows = @($rows | Where-Object { $_.evidenceIncomplete })
    $monsterIntelligenceCards = @($enemyIdentities | ForEach-Object {
        $identity = $_
        $hp = @($crossValidatedHpCandidates | Where-Object {
            [string]$_.targetMonsterIdentityKey -ceq [string]$identity.identityKey
        } | Select-Object -First 1)
        $poisonHp = @($poisonExactCandidates | Where-Object {
            [string]$_.targetMonsterIdentityKey -ceq [string]$identity.identityKey
        } | Select-Object -First 1)
        $mp = @($directEnemyMpCandidates | Where-Object {
            [string]$_.targetMonsterIdentityKey -ceq [string]$identity.identityKey
        } | Select-Object -First 1)
        $attack = @($baselinePhysicalAttackHypotheses | Where-Object {
            [string]$_.sourceMonsterIdentityKey -ceq [string]$identity.identityKey
        })
        $actionSignatures = @($effectDetails | Where-Object {
            [string]$_.sourceMonsterIdentityKey -ceq [string]$identity.identityKey
        } | ForEach-Object {
            'kind={0};aux0={1};aux1={2}' -f [int]$_.effectKind,
                [uint16]$_.auxiliaryValue0, [uint16]$_.auxiliaryValue1
        } | Select-Object -Unique)
        $maximumHp = if ($hp.Count -eq 1) { [uint64]$hp[0].maximumHitPoints }
            elseif ($poisonHp.Count -eq 1) { [uint64]$poisonHp[0].maximumHitPoints }
            else { $null }
        [pscustomobject][ordered]@{
            identityKey = [string]$identity.identityKey
            evidenceScope = '{0}/{1}' -f [string]$identity.monsterNameBytesSha256,
                [string]$identity.descriptorSpeciesSignatureSha256
            battlePosition = [int]$identity.battlePosition
            name = [ordered]@{
                value = [string]$identity.monsterNameOriginal
                status = 'OfficialClientActorExact'
            }
            level = [ordered]@{
                value = [int]$identity.level
                status = 'OfficialDescriptorExact'
            }
            maximumHp = [ordered]@{
                value = $maximumHp
                status = if ($hp.Count -eq 1) { [string]$hp[0].authority }
                    elseif ($poisonHp.Count -eq 1) { [string]$poisonHp[0].authority }
                    else { 'UnavailableNotGuessed' }
            }
            maximumMp = [ordered]@{
                value = if ($mp.Count -eq 1) { [uint32]$mp[0].maximumMagicPoints } else { $null }
                status = if ($mp.Count -eq 1) { [string]$mp[0].authority }
                    else { 'UnavailableNotGuessed' }
            }
            strength = [ordered]@{
                value = $null
                status = 'PkCalibrationAndLiveConsumerProofPending'
            }
            constitution = [ordered]@{
                value = $null
                status = 'PkCalibrationAndLiveConsumerProofPending'
            }
            intelligence = [ordered]@{
                value = $null
                status = 'PkCalibrationAndLiveConsumerProofPending'
            }
            speed = [ordered]@{
                value = $null
                status = 'PkCalibrationAndLiveConsumerProofPending'
            }
            metal = [ordered]@{ value = $null; status = 'PkCalibrationAndLiveConsumerProofPending' }
            wood = [ordered]@{ value = $null; status = 'PkCalibrationAndLiveConsumerProofPending' }
            water = [ordered]@{ value = $null; status = 'PkCalibrationAndLiveConsumerProofPending' }
            fire = [ordered]@{ value = $null; status = 'PkCalibrationAndLiveConsumerProofPending' }
            earth = [ordered]@{ value = $null; status = 'PkCalibrationAndLiveConsumerProofPending' }
            physicalAttack = [ordered]@{
                value = if ($attack.Count -eq 1 -and
                    [bool]$attack[0].baselinePairAgreement) {
                    [int64]$attack[0].selectedBaselinePhysicalAttack
                } else { $null }
                candidates = @($attack | ForEach-Object { @($_.baselineCandidatePairs) })
                status = if ($attack.Count -gt 0) {
                    'DerivedCandidateNotOfficialValue'
                } else { 'UnavailableNotGuessed' }
            }
            physicalDefense = [ordered]@{
                value = $null
                status = 'UnavailableNotGuessed'
            }
            observedActionSignatures = [ordered]@{
                values = $actionSignatures
                status = if ($actionSignatures.Count -gt 0) {
                    'OfficialEffectsObserved_NotYetSkillIds'
                } else { 'NotObserved' }
            }
            experience = [ordered]@{
                value = if ($settlementDetails.Count -eq 1 -and
                    $settlementDetails[0].experienceFieldsAgree -and
                    $enemyIdentities.Count -eq 1) {
                    [int]$settlementDetails[0].experienceCredited
                } else { $null }
                status = if ($settlementDetails.Count -eq 1 -and
                    $settlementDetails[0].experienceFieldsAgree -and
                    $enemyIdentities.Count -eq 1) {
                    'OfficialSingleNamedEnemySettlement'
                } else { 'EncounterTotalOrUnavailable_NotAllocated' }
            }
        }
    })
    $monsterIdentityComplete = $enemyDescriptors.Count -gt 0 -and
        $enemyIdentities.Count -eq $enemyDescriptors.Count -and
        $unboundEnemyDescriptors.Count -eq 0 -and
        $enemyIdentityConflicts.Count -eq 0 -and
        $unboundMonsterEffectIdentities.Count -eq 0
    $observableComplete = $bootstrapRows.Count -gt 0 -and
        $descriptorDetails.Count -gt 0 -and $enemyDescriptors.Count -gt 0 -and
        $monsterIdentityComplete -and
        $initializationRows.Count -gt 0 -and $commandDetails.Count -gt 0 -and
        $effectDetails.Count -gt 0 -and $vitalRows.Count -gt 0 -and
        $settlementDetails.Count -eq 1 -and
        [bool]$settlementDetails[0].experienceFieldsAgree -and
        $actors.Count -gt 0 -and $states.Count -gt 0 -and
        $matchedEffectStateTransitions -eq $effectDetails.Count -and
        $incompleteRows.Count -eq 0
    [pscustomobject][ordered]@{
        encounterNumber = [int]$encounter.encounterNumber
        startSequence = [uint64]$encounter.startSequence
        terminalSequence = $encounter.terminalSequence
        startOpcode = [string]$encounter.startOpcode
        startAuthority = [string]$encounter.startAuthority
        terminalOpcode = [string]$encounter.terminalOpcode
        terminalAuthority = [string]$encounter.terminalAuthority
        startedAtUtc = [DateTimeOffset]::FromUnixTimeMilliseconds(
            [int64]$encounter.startUnixMs).UtcDateTime.ToString('o')
        endedAtUtc = if ($null -ne $encounter.terminalUnixMs) {
            [DateTimeOffset]::FromUnixTimeMilliseconds(
                [int64]$encounter.terminalUnixMs).UtcDateTime.ToString('o')
        } else { $null }
        bootstrapCount = $bootstrapRows.Count
        initializationRecordCount = $initializationRows.Count
        commandCount = $commandDetails.Count
        commandDetails = $commandDetails
        effectCount = $effectDetails.Count
        effectDetails = $effectDetails
        monsterOriginEffectCount = @($effectDetails | Where-Object {
            $_.sourceSide -ceq 'Enemy'
        }).Count
        monsterSkillCandidateCount = @($effectDetails | Where-Object {
            $_.sourceSide -ceq 'Enemy' -and $_.effectKind -eq 3
        }).Count
        enemyDamageObservations = $enemyDamageObservations
        baselinePhysicalAttackHypotheses = $baselinePhysicalAttackHypotheses
        baselinePhysicalAttackHypothesisCount = $baselinePhysicalAttackHypotheses.Count
        friendlyDamageObservations = $friendlyDamageObservations
        skillDamagePairs = $skillDamagePairs
        skillDamagePairCount = $skillDamagePairs.Count
        skillCriticalityContradictions = $skillCriticalityContradictions
        battleUiVitalObservationCount = $uiVitalDetails.Count
        battleUiBarObservationCount = @($uiVitalDetails | Where-Object {
            $_.kind -eq 1
        }).Count
        battleUiExactPairObservationCount = @($uiVitalDetails | Where-Object {
            $_.kind -in @(2, 3, 4)
        }).Count
        battleUiVitalObservations = $uiVitalDetails
        directEnemyMaximumHpCandidates = $directEnemyHpCandidates
        directEnemyMaximumMpCandidates = $directEnemyMpCandidates
        directHpTransitionCorrelations = @($directHpCorrelations)
        poisonTenPercentHypotheses = @($poisonHypotheses)
        poisonExactMaximumHpCandidates = $poisonExactCandidates
        enemyMaximumHpCandidates = $crossValidatedHpCandidates
        enemyMaximumHpEvidenceReady = $crossValidatedHpCandidates.Count -gt 0 -or
            $poisonExactCandidates.Count -gt 0
        vitalSnapshotRecordCount = $vitalRows.Count
        terminalResultCount = $settlementDetails.Count
        settlement = if ($settlementDetails.Count -eq 1) {
            $settlementDetails[0]
        } else { $null }
        experienceAuthority = if ($settlementDetails.Count -eq 1 -and
            $settlementDetails[0].experienceFieldsAgree -and
            $enemyIdentities.Count -eq 1 -and $monsterIdentityComplete) {
            'VerifiedSingleNamedEnemySettlementCandidate'
        } elseif ($settlementDetails.Count -eq 1 -and
            $settlementDetails[0].experienceFieldsAgree) {
            'VerifiedEncounterTotalOnly_MultiEnemyAllocationBlocked'
        } else { 'EvidenceBlocked' }
        actorDescriptorCount = $descriptorDetails.Count
        enemyDescriptorCount = $enemyDescriptors.Count
        enemyDescriptors = $enemyDescriptors
        enemyIdentityCount = $enemyIdentities.Count
        enemyIdentities = $enemyIdentities
        monsterIntelligenceCards = $monsterIntelligenceCards
        monsterIdentityComplete = $monsterIdentityComplete
        enemyIdentityConflictCount = $enemyIdentityConflicts.Count
        enemyIdentityConflicts = @($enemyIdentityConflicts)
        unboundEnemyDescriptorCount = $unboundEnemyDescriptors.Count
        unboundEnemyDescriptors = $unboundEnemyDescriptors
        unboundMonsterEffectIdentityCount = $unboundMonsterEffectIdentities.Count
        unboundMonsterEffectIdentities = @($unboundMonsterEffectIdentities)
        actorSnapshotCount = $actors.Count
        stateSnapshotCount = $states.Count
        actionAppliedStateSnapshotCount = @($states | Where-Object {
            [string]$_.phase -ceq 'ActionApplied'
        }).Count
        effectToActionAppliedCorrelationCount = $matchedEffectStateTransitions
        boundaryAcknowledgementCount = $boundaryAcknowledgements.Count
        incompleteRecordCount = $incompleteRows.Count
        observedOpcodes = @($rows | Group-Object direction, opcode | ForEach-Object {
            [pscustomobject]@{
                direction = [string]$_.Group[0].direction
                opcode = ('0x{0:X2}' -f [int]$_.Group[0].opcode)
                count = $_.Count
            }
        })
        observableCaptureComplete = $observableComplete
        missingObservableEvidence = @(
            if ($bootstrapRows.Count -eq 0) { 'exact-current-battle-entry-0x82' }
            if ($descriptorDetails.Count -eq 0) { 'actor-descriptor-0x1C' }
            if ($enemyDescriptors.Count -eq 0) { 'enemy-descriptor-and-level' }
            if ($enemyDescriptors.Count -gt 0 -and $enemyIdentities.Count -eq 0) {
                'enemy-name-at-actor-0xDBE'
            }
            if ($unboundEnemyDescriptors.Count -gt 0) {
                'enemy-name-position-descriptor-epoch-binding'
            }
            if ($enemyIdentityConflicts.Count -gt 0) {
                'enemy-name-identity-conflict'
            }
            if ($unboundMonsterEffectIdentities.Count -gt 0) {
                'effect-monster-name-position-descriptor-epoch-binding'
            }
            if ($initializationRows.Count -eq 0) { 'battle-initialization-0x86' }
            if ($commandDetails.Count -eq 0) { 'client-command-0x35' }
            if ($effectDetails.Count -eq 0) { 'server-effect-0x83' }
            if ($vitalRows.Count -eq 0) { 'current-vital-snapshot-0x88' }
            if ($settlementDetails.Count -ne 1) { 'terminal-settlement-0x89' }
            elseif (-not [bool]$settlementDetails[0].experienceFieldsAgree) {
                'settlement-experience-field-agreement'
            }
            if ($actors.Count -eq 0) { 'battle-actor-snapshot' }
            if ($states.Count -eq 0) { 'battle-state-snapshot' }
            if ($effectDetails.Count -gt 0 -and
                $matchedEffectStateTransitions -ne $effectDetails.Count) {
                'effect-to-action-applied-causality'
            }
            if ($incompleteRows.Count -gt 0) { 'incomplete-capture-records' }
        )
    }
})

$ringHealthy = [int64]$health.Dropped -eq 0 -and
    [int64]$health.InvalidPayloads -eq 0 -and [int64]$health.WriteFailures -eq 0 -and
    -not [bool]$health.ConsumerIoFailure -and [string]$health.Lifecycle -ceq 'StoppedAndDrained'
$completedEncounters = @($encounterResults | Where-Object { $_.observableCaptureComplete })
$campaignObservableComplete = $encounterResults.Count -gt 0 -and
    $completedEncounters.Count -eq $encounterResults.Count -and $ringHealthy -and
    $parseFailures -eq 0 -and $unboundTerminalCount -eq 0 -and
    $uiProbeApplied -and $uiProbeRuntimeHealthy
$allEnemyMaximumHpCandidates = @($encounterResults | ForEach-Object {
    @($_.enemyMaximumHpCandidates)
})
$allPoisonExactMaximumHpCandidates = @($encounterResults | ForEach-Object {
    @($_.poisonExactMaximumHpCandidates)
})
$allSkillDamagePairs = @($encounterResults | ForEach-Object { @($_.skillDamagePairs) })
$allSkillCriticalityContradictions = @($encounterResults | ForEach-Object {
    @($_.skillCriticalityContradictions)
})
$skillDamageModelHypotheses = @($allSkillDamagePairs | Group-Object {
    '{0}/{1}/{2}/{3}' -f $_.targetMonsterEvidenceScope,
        $_.sourceBattlePosition, $_.actionParameterCandidate, $_.effectKind
} | ForEach-Object {
    $pairs = @($_.Group)
    $distinctConditions = @($pairs | Group-Object basicDamage | ForEach-Object {
        $_.Group[0]
    })
    $additiveValues = @($distinctConditions |
        Select-Object -ExpandProperty additiveBaseDamageCandidate -Unique)
    $additiveFits = $distinctConditions.Count -ge 2 -and $additiveValues.Count -eq 1
    $multiplierFits = $distinctConditions.Count -ge 2
    if ($multiplierFits) {
        $anchor = $distinctConditions[0]
        foreach ($pair in $distinctConditions | Select-Object -Skip 1) {
            if (([decimal]$pair.skillDamage * [decimal]$anchor.basicDamage) -ne
                ([decimal]$anchor.skillDamage * [decimal]$pair.basicDamage)) {
                $multiplierFits = $false
                break
            }
        }
    }
    $model = if ($additiveFits -and -not $multiplierFits) {
        'AdditiveBaseDamageCandidate'
    } elseif ($multiplierFits -and -not $additiveFits) {
        'MultiplicativeCoefficientCandidate'
    } elseif ($additiveFits -and $multiplierFits) {
        'DegenerateBothModelsFit'
    } elseif ($distinctConditions.Count -ge 2) {
        'AffineOrModifiedDamageCandidate'
    } else { 'InsufficientDistinctDamageConditions' }
    [pscustomobject]@{
        familyKey = [string]$_.Name
        targetMonsterEvidenceScope = [string]$pairs[0].targetMonsterEvidenceScope
        targetMonsterNameOriginal = [string]$pairs[0].targetMonsterNameOriginal
        sourceBattlePosition = [int]$pairs[0].sourceBattlePosition
        actionParameterCandidate = [uint32]$pairs[0].actionParameterCandidate
        effectKind = [int]$pairs[0].effectKind
        observedPairCount = $pairs.Count
        distinctBasicDamageConditionCount = $distinctConditions.Count
        model = $model
        additiveBaseDamageCandidate = if ($additiveFits) {
            [int64]$additiveValues[0]
        } else { $null }
        multiplicativeCoefficientCandidate = if ($multiplierFits) {
            [decimal]$distinctConditions[0].skillDamage /
                [decimal]$distinctConditions[0].basicDamage
        } else { $null }
        repeatReady = @($pairs | Where-Object {
            $_.basicSampleCount -lt 2 -or $_.skillSampleCount -lt 2
        }).Count -eq 0
        staticSkillIdentityBindingRequired = $true
        officialFormulaPromotionReady = $false
        authority = 'CrossConditionModelDiscriminator_NotOfficialFormulaEvidence'
    }
})
$monsterMaximumHpEvidenceReady = $allEnemyMaximumHpCandidates.Count -gt 0 -or
    $allPoisonExactMaximumHpCandidates.Count -gt 0
$allEnemyIdentities = @($encounterResults | ForEach-Object {
    @($_.enemyIdentities)
})
$allMonsterIntelligenceCards = @($encounterResults | ForEach-Object {
    @($_.monsterIntelligenceCards)
})
$monsterIdentityEvidenceReady = $encounterResults.Count -gt 0 -and
    @($encounterResults | Where-Object {
        -not [bool]$_.monsterIdentityComplete
    }).Count -eq 0
$unboundMonsterEffectIdentityCount = [int](($encounterResults | Measure-Object `
    -Property unboundMonsterEffectIdentityCount -Sum).Sum)
$totalUiVitalObservations = [int](($encounterResults | Measure-Object `
    -Property battleUiVitalObservationCount -Sum).Sum)

$report = [ordered]@{
    schemaVersion = 'god2-limited-official-battle-campaign-readiness-v4'
    sessionId = $SessionId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    status = if ($campaignObservableComplete) {
        'OBSERVABLE_BATTLE_EVIDENCE_COMPLETE'
    } else { 'EVIDENCE_INCOMPLETE_REVIEW_REQUIRED' }
    detectedEncounterCount = $encounterResults.Count
    completeEncounterCount = $completedEncounters.Count
    ringHealthy = $ringHealthy
    ringDropped = [int64]$health.Dropped
    ringInvalidPayloads = [int64]$health.InvalidPayloads
    ringWriteFailures = [int64]$health.WriteFailures
    jsonLineParseFailureCount = $parseFailures
    unboundTerminalResultCount = $unboundTerminalCount
    battleUiVitalProbeApplied = $uiProbeApplied
    battleUiVitalProbeRuntimeHealthy = $uiProbeRuntimeHealthy
    battleUiVitalProbeInvoked = $uiProbeInvoked
    battleUiVitalProbeAccepted = $uiProbeAccepted
    battleUiVitalProbeRejected = $uiProbeRejected
    battleUiVitalProbeReceiptSha256 = (Get-FileHash -LiteralPath $uiProbePath `
        -Algorithm SHA256).Hash
    battleUiVitalObservationCount = $totalUiVitalObservations
    enemyIdentities = $allEnemyIdentities
    monsterIntelligenceCards = $allMonsterIntelligenceCards
    monsterIdentityEvidenceReady = $monsterIdentityEvidenceReady
    monsterIdentityEvidenceStatus = if ($monsterIdentityEvidenceReady) {
        'ALL_MONSTER_EVIDENCE_LOCKED_TO_NAME_POSITION_DESCRIPTOR_EPOCH'
    } else { 'EVIDENCE_BLOCKED_MONSTER_NAME_BINDING_INCOMPLETE' }
    unboundMonsterEffectIdentityCount = $unboundMonsterEffectIdentityCount
    enemyMaximumHpCandidates = $allEnemyMaximumHpCandidates
    poisonExactMaximumHpCandidates = $allPoisonExactMaximumHpCandidates
    monsterMaximumHpEvidenceReady = $monsterMaximumHpEvidenceReady
    monsterMaximumHpEvidenceStatus = if ($monsterMaximumHpEvidenceReady) {
        'EXACT_ENEMY_MAXIMUM_HP_EVIDENCE_READY_FOR_REVIEW'
    } elseif ($totalUiVitalObservations -gt 0) {
        'UI_OBSERVED_BUT_ENEMY_CAUSAL_BINDING_INCOMPLETE'
    } else { 'FORMATION_OR_POISON_OBSERVATION_NOT_CAPTURED' }
    suppliedFriendlyPhysicalDefense = if ($FriendlyPhysicalDefense -ge 0) {
        $FriendlyPhysicalDefense
    } else { $null }
    baselinePhysicalAttackHypotheses = @($encounterResults | ForEach-Object {
        @($_.baselinePhysicalAttackHypotheses)
    })
    officialPhysicalAttackEvidenceReady = $false
    officialPhysicalAttackEvidenceStatus =
        'BASELINE_INVERSION_ONLY_OFFICIAL_DAMAGE_FORMULA_AND_OPERANDS_UNVERIFIED'
    skillDamagePairs = $allSkillDamagePairs
    skillDamageModelHypotheses = $skillDamageModelHypotheses
    skillCriticalityContradictions = $allSkillCriticalityContradictions
    officialSkillBaseDamageEvidenceReady = $false
    officialSkillBaseDamageEvidenceStatus =
        'MODEL_CANDIDATES_ONLY_STATIC_SKILL_BINDING_AND_OFFICIAL_OPERANDS_REQUIRED'
    skillCriticalityHypothesis = [ordered]@{
        statement = 'User confirms skills cannot critical; only basic attacks may produce exact double critical damage'
        status = 'UserConfirmedGameplayRule_CorroboratedByControlledSwordOneSamples'
        existingCorroboration = 'Three controlled SwordOne skill commands at sequences 2809/2920/3027 produced effect deltas -49/-46/-62 with no exact-double branch'
        analyzerPolicy = 'Basic exact-double cluster is separated; any unstable or exact-double skill cluster is contradiction or correlation-error evidence and blocks the stable skill pair'
    }
    exactCurrentBoundary = 'S2C PostDecrypt 0x82 -> HandlerDecoded 0x89; 0x8D/0xD6 and 0xE6 retained only as cross-version corroboration'
    monsterIdentityCausalChain = 'S2C HandlerDecoded 0x1C full descriptor SHA -> same-position actor snapshot -> ordinary enemy actor+0xDBE bounded CP936 name -> descriptor epoch -> every source/target 0x83 effect and derived HP/damage/skill row'
    causalChain = 'named monster descriptor epoch -> C2S PreEncrypt 0x35 -> S2C HandlerDecoded 0x83 target mask and signed delta -> ActionApplied state snapshot -> formation UI bar/text/vital apply observation -> S2C HandlerDecoded 0x88 -> S2C HandlerDecoded 0x89'
    poisonCausalChain = 'enemy full HP -> first poison-only damage -> two follow-up poison-only ticks -> integer tenth recurrence solution -> formation UI current/max or bar cross-check'
    actorSnapshotManifestPresent = $null -ne $actorManifest
    stateSnapshotManifestPresent = $null -ne $stateManifest
    encounters = $encounterResults
    observableCaptureComplete = $campaignObservableComplete
    serverOnlyMechanicsComplete = $false
    serverOnlyEvidenceBoundary = @(
        'enemy maximum HP is promotable only when a formation UI current/max pair agrees with a targeted 0x83 delta, or a three-tick poison recurrence has a unique integer solution',
        '天眼護符 22031/22086/22131/22231/22260 and 父親節快樂 22341..22344 are exact-current optional HP-display accelerators; captures must still pass the independent name/position/descriptor epoch gate',
        'enemy maximum MP remains blocked unless a separately target-bound exact UI tuple is observed',
        'authoritative damage, critical, element, AI, reward and drop formulas require observed diversity and cross-encounter inference',
        'one basic/skill damage pair fits both additive and multiplicative skill models; two or more distinct defense or attack conditions are required to discriminate them',
        'single-enemy 0x89 agreeing EXP fields can verify that encounter reward; multi-enemy allocation remains blocked',
        'a captured terminal/result burst does not by itself prove every reward/drop field semantic'
    )
    rawEvidenceRetained = $true
    productionMutationAllowed = $false
}
$reportPath = Join-Path $sessionRoot 'reports\battle-campaign-readiness.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 12

if (-not $campaignObservableComplete -and -not $NoFail) { exit 3 }
