param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ByteArraySha256 {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $hasher.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $hasher.Dispose()
    }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
if (-not $sessionRoot.StartsWith($capturePrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Session path escaped the approved capture root."
}

$statusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
$tracePath = Join-Path $sessionRoot "raw\enhanced-x86\trace.bin"
$generalLogPath = Join-Path $sessionRoot "reports\enhanced-capture.log"
if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $tracePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $generalLogPath -PathType Leaf)) {
    throw "Detached capture status, trace.bin, or enhanced-capture.log is missing."
}
$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ([string]$status.Status -cne "DETACHED" -or
    -not [bool]$status.StrictUnloadVerified) {
    throw "Strict capture unload was not verified."
}
$generalLog = Get-Content -LiteralPath $generalLogPath -Raw -Encoding UTF8
$counterPattern = 'battle state snapshot counters invoked=(\d+) badManager=(\d+) nullActor=(\d+) invalidIndex=(\d+) unreadableState=(\d+) accepted=(\d+) actionAppliedInvoked=(\d+) actionAppliedAccepted=(\d+)'
$counterMatches = [regex]::Matches($generalLog, $counterPattern)
if ($counterMatches.Count -eq 0) {
    throw "Detached capture log has no BattleStateSnapshot ActionApplied counters."
}
$counterMatch = $counterMatches[$counterMatches.Count - 1]
$actionAppliedInvocationCount = [int64]$counterMatch.Groups[7].Value
$actionAppliedAcceptedSnapshotCount = [int64]$counterMatch.Groups[8].Value

$outputRoot = Join-Path $repoRoot (
    "Artifacts\AnalysisScratch\BattleStateSnapshots\" + $SessionId)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$stream = [IO.File]::Open($tracePath, [IO.FileMode]::Open,
    [IO.FileAccess]::Read,
    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
$reader = [IO.BinaryReader]::new($stream)
$snapshots = [Collections.Generic.List[object]]::new()
$transitions = [Collections.Generic.List[object]]::new()
$previousByIdentity = @{}
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
        $snapshotIdentity = [BitConverter]::ToInt32($record, 48)
        $stateIndex = [BitConverter]::ToUInt32($record, 52)
        $capturedLength = [BitConverter]::ToUInt32($record, 64)
        if ($capturedLength -gt 4096 -or
            $stream.Length - $stream.Position -lt $capturedLength) {
            throw "Trace payload length is invalid."
        }
        $payload = $reader.ReadBytes([int]$capturedLength)
        if ($api -ne 9) { continue }

        $battlePosition = $snapshotIdentity -band 0xFF
        $phase = ($snapshotIdentity -shr 8) -band 0xFF
        if ($payload.Length -ne 0x290 -or $battlePosition -ge 28 -or
            $phase -notin @(1, 2, 3) -or $stateIndex -ge 0x1000) {
            throw "Battle state snapshot shape is invalid."
        }

        $hash = Get-ByteArraySha256 -Bytes $payload
        $phaseName = if ($phase -eq 1) { "ActorCreated" }
            elseif ($phase -eq 2) { "ActionApplied" }
            else { "PeriodicChanged" }
        $fileName = "state-sequence-$sequence-position-$battlePosition-index-$stateIndex.bin"
        $snapshotPath = Join-Path $outputRoot $fileName
        [IO.File]::WriteAllBytes($snapshotPath, $payload)
        $snapshot = [pscustomobject][ordered]@{
            sequence = $sequence
            observedAtUtc = [DateTimeOffset]::FromUnixTimeMilliseconds(
                $wallUnixMs).UtcDateTime.ToString("o")
            battlePosition = $battlePosition
            side = $(if ($battlePosition -lt 14) { "Friendly" } else { "Enemy" })
            phase = $phaseName
            stateIndex = $stateIndex
            byteLength = $payload.Length
            sha256 = $hash
            temporarySnapshotFile = $fileName
        }
        $snapshots.Add($snapshot)

        $identity = "$battlePosition/$stateIndex"
        # ActorCreated is the baseline for a new actor lifetime. Never compare it
        # with a same-position/state-index record left over from an older battle.
        if ($phaseName -cne "ActorCreated" -and $previousByIdentity.ContainsKey($identity)) {
            $previous = $previousByIdentity[$identity]
            if ([string]$previous.sha256 -cne $hash) {
                $changedBytes = [Collections.Generic.List[int]]::new()
                for ($offset = 0; $offset -lt $payload.Length; $offset++) {
                    if ($previous.payload[$offset] -ne $payload[$offset]) {
                        $changedBytes.Add($offset)
                    }
                }
                $words = [Collections.Generic.List[object]]::new()
                for ($offset = 0; $offset -le $payload.Length - 2;
                     $offset += 2) {
                    $before = [BitConverter]::ToUInt16($previous.payload, $offset)
                    $after = [BitConverter]::ToUInt16($payload, $offset)
                    if ($before -ne $after) {
                        $words.Add([pscustomobject][ordered]@{
                            offset = $offset
                            before = [uint32]$before
                            after = [uint32]$after
                            delta = [int]$after - [int]$before
                        })
                    }
                }
                $dwords = [Collections.Generic.List[object]]::new()
                for ($offset = 0; $offset -le $payload.Length - 4;
                     $offset += 4) {
                    $before = [BitConverter]::ToInt32($previous.payload, $offset)
                    $after = [BitConverter]::ToInt32($payload, $offset)
                    if ($before -ne $after) {
                        $dwords.Add([pscustomobject][ordered]@{
                            offset = $offset
                            before = $before
                            after = $after
                            delta = [int64]$after - [int64]$before
                        })
                    }
                }
                $transitions.Add([pscustomobject][ordered]@{
                    battlePosition = $battlePosition
                    stateIndex = $stateIndex
                    fromSequence = [uint64]$previous.sequence
                    toSequence = $sequence
                    fromPhase = [string]$previous.phase
                    toPhase = $phaseName
                    fromSha256 = [string]$previous.sha256
                    toSha256 = $hash
                    fromTemporarySnapshotFile = [string]$previous.temporarySnapshotFile
                    toTemporarySnapshotFile = $fileName
                    changedByteCount = $changedBytes.Count
                    changedByteOffsets = @($changedBytes)
                    alignedUInt16Candidates = @($words)
                    alignedInt32Candidates = @($dwords)
                })
            }
        }
        $previousByIdentity[$identity] = [pscustomobject]@{
            sequence = $sequence
            phase = $phaseName
            sha256 = $hash
            temporarySnapshotFile = $fileName
            payload = $payload
        }
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

$actionAppliedEnemyTransitions = @($transitions | Where-Object {
    [int]$_.battlePosition -ge 14 -and [string]$_.toPhase -ceq "ActionApplied"
})
$actionAppliedSnapshots = @($snapshots | Where-Object {
    [string]$_.phase -ceq "ActionApplied"
})
$manifest = [ordered]@{
    schemaVersion = "god2-battle-state-snapshot-manifest-v2"
    sessionId = $SessionId
    sourceApi = "BattleStateSnapshot"
    stateRecordBytes = 0x290
    semanticClassification = "VerifiedRenderSpriteStateNotHpAuthority"
    stateRecordSemantics = "The 0x290 pool record is render/sprite state. It is not a current-HP or maximum-HP authority."
    maximumHpFieldPresent = $false
    staticSemanticEvidence = @(
        "God2_opt+RVA 0x00006980 initializes animation/resource/render fields in a 0x290-stride record",
        "God2_opt+RVA 0x00006D40 consumes record+0x22 and record+0x24 as renderer X/Y coordinates",
        "God2_opt+RVA 0x0014AD20 applies opcode 0x83 effects and uses effect values for presentation without establishing an HP field in the 0x290 record"
    )
    capturePoints = @(
        "RVA 0x00150818 after actor creation",
        "RVA 0x0015086E after opcode 0x83 state application"
    )
    stateBinding = "dispatcher+0x149DF8 -> +0x0C state pool; actor+0x1A state index"
    gameMemoryWritten = $false
    rawPacketDataIncluded = $false
    temporaryBinarySnapshotsIncluded = $true
    crossEncounterTransitionsExcluded = $true
    snapshotCount = $snapshots.Count
    transitionCount = $transitions.Count
    actionAppliedInvocationCount = $actionAppliedInvocationCount
    actionAppliedAcceptedSnapshotCount = $actionAppliedAcceptedSnapshotCount
    actionAppliedTraceSnapshotCount = $actionAppliedSnapshots.Count
    actionAppliedEnemyTransitionCount = $actionAppliedEnemyTransitions.Count
    snapshots = @($snapshots)
    transitions = @($transitions)
}
$manifestPath = Join-Path $outputRoot "manifest.json"
[IO.File]::WriteAllText($manifestPath,
    ($manifest | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$manifest | ConvertTo-Json -Depth 10
