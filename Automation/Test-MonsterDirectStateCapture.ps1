param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,
    [switch] $NoFail
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
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) {
    throw "Monster capture session does not exist: $sessionRoot"
}

$issues = [Collections.Generic.List[string]]::new()
$actorRoot = Join-Path $repoRoot ("Artifacts\AnalysisScratch\BattleActorSnapshots\" + $SessionId)
$stateRoot = Join-Path $repoRoot ("Artifacts\AnalysisScratch\BattleStateSnapshots\" + $SessionId)
$actorManifestPath = Join-Path $actorRoot "manifest.json"
$stateManifestPath = Join-Path $stateRoot "manifest.json"
$packetPath = Join-Path $sessionRoot "raw\injected-packets.jsonl"

function Test-FrameOpcode {
    param(
        [Parameter(Mandatory = $true)] [object] $Row,
        [Parameter(Mandatory = $true)] [byte] $Opcode
    )

    if ([string]$Row.Api -cne "PostDecrypt") { return $false }
    $hex = [string]$Row.PayloadHex
    if ($hex.Length -lt 6 -or ($hex.Length % 2) -ne 0 -or
        $hex -notmatch '^[0-9A-Fa-f]+$') {
        return $false
    }
    return [Convert]::ToByte($hex.Substring(4, 2), 16) -eq $Opcode
}

function Test-SnapshotArtifact {
    param(
        [Parameter(Mandatory = $true)] [object] $Row,
        [Parameter(Mandatory = $true)] [string] $Root,
        [Parameter(Mandatory = $true)] [int] $ExpectedBytes,
        [Parameter(Mandatory = $true)] [string] $Label
    )

    $name = [string]$Row.temporarySnapshotFile
    if ([string]::IsNullOrWhiteSpace($name) -or
        [IO.Path]::GetFileName($name) -cne $name) {
        $issues.Add("$Label snapshot has an unsafe or missing file name.")
        return $false
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $name))
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $issues.Add("$Label snapshot file is missing or escaped its evidence root: $name")
        return $false
    }
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($item.Length -ne $ExpectedBytes -or [int]$Row.byteLength -ne $ExpectedBytes -or
        $hash -cne [string]$Row.sha256) {
        $issues.Add("$Label snapshot length/hash binding failed: $name")
        return $false
    }
    return $true
}

$actorManifest = $null
$stateManifest = $null
$actionAppliedInvocationCount = [int64]0
$actionAppliedAcceptedSnapshotCount = [int64]0
if (Test-Path -LiteralPath $actorManifestPath -PathType Leaf) {
    $actorManifest = Get-Content -LiteralPath $actorManifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
} else {
    $issues.Add("Battle actor snapshot manifest is missing.")
}
if (Test-Path -LiteralPath $stateManifestPath -PathType Leaf) {
    $stateManifest = Get-Content -LiteralPath $stateManifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
} else {
    $issues.Add("Battle state snapshot manifest is missing.")
}

$actorSnapshots = @()
$stateSnapshots = @()
$stateTransitions = @()
$packetRows = @()
if ($null -ne $actorManifest) {
    if ([string]$actorManifest.schemaVersion -cne "god2-battle-actor-snapshot-manifest-v3" -or
        [string]$actorManifest.sessionId -cne $SessionId -or
        [string]$actorManifest.sourceApi -cne "BattleActorSnapshot" -or
        [bool]$actorManifest.gameMemoryWritten -or [bool]$actorManifest.rawPacketDataIncluded) {
        $issues.Add("Battle actor snapshot manifest contract is invalid.")
    } else {
        $actorSnapshots = @($actorManifest.snapshots)
    }
}

if (Test-Path -LiteralPath $packetPath -PathType Leaf) {
    $packetRows = @(Get-Content -LiteralPath $packetPath -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json })
} else {
    $issues.Add("Injected plaintext packet evidence is missing.")
}
$battleEntries = @($packetRows | Where-Object { Test-FrameOpcode -Row $_ -Opcode 0x82 } |
    Sort-Object { [uint64]$_.sequence })
$settlements = @($packetRows | Where-Object { Test-FrameOpcode -Row $_ -Opcode 0x89 } |
    Sort-Object { [uint64]$_.sequence })
$encounterWindows = [Collections.Generic.List[object]]::new()
foreach ($entry in $battleEntries) {
    $entrySequence = [uint64]$entry.sequence
    $nextEntry = @($battleEntries | Where-Object {
        [uint64]$_.sequence -gt $entrySequence
    } | Select-Object -First 1)
    $upperBound = if ($nextEntry.Count -eq 1) {
        [uint64]$nextEntry[0].sequence
    } else {
        [uint64]::MaxValue
    }
    $settlement = @($settlements | Where-Object {
        [uint64]$_.sequence -gt $entrySequence -and [uint64]$_.sequence -lt $upperBound
    } | Select-Object -First 1)
    if ($settlement.Count -eq 1) {
        $encounterWindows.Add([pscustomobject][ordered]@{
            entrySequence = $entrySequence
            entrySourceFrameId = [string]$entry.SourceFrameId
            settlementSequence = [uint64]$settlement[0].sequence
            settlementSourceFrameId = [string]$settlement[0].SourceFrameId
        })
    }
}
if ($null -ne $stateManifest) {
    if ([string]$stateManifest.schemaVersion -cne "god2-battle-state-snapshot-manifest-v2" -or
        [string]$stateManifest.sessionId -cne $SessionId -or
        [string]$stateManifest.sourceApi -cne "BattleStateSnapshot" -or
        [int]$stateManifest.stateRecordBytes -ne 0x290 -or
        -not [bool]$stateManifest.crossEncounterTransitionsExcluded -or
        [bool]$stateManifest.gameMemoryWritten -or [bool]$stateManifest.rawPacketDataIncluded) {
        $issues.Add("Battle state snapshot manifest contract is invalid.")
    } else {
        $stateSnapshots = @($stateManifest.snapshots)
        $stateTransitions = @($stateManifest.transitions)
        $statePropertyNames = @($stateManifest.PSObject.Properties.Name)
        if ($statePropertyNames -notcontains "actionAppliedInvocationCount" -or
            $statePropertyNames -notcontains "actionAppliedAcceptedSnapshotCount" -or
            $statePropertyNames -notcontains "actionAppliedTraceSnapshotCount") {
            $issues.Add("Battle state snapshot manifest has no ActionApplied invocation/acceptance evidence.")
        } else {
            $actionAppliedInvocationCount = [int64]$stateManifest.actionAppliedInvocationCount
            $actionAppliedAcceptedSnapshotCount = [int64]$stateManifest.actionAppliedAcceptedSnapshotCount
            $actionAppliedTraceSnapshotCount = [int64]$stateManifest.actionAppliedTraceSnapshotCount
            $phaseSnapshotCount = @($stateSnapshots | Where-Object {
                [string]$_.phase -ceq "ActionApplied"
            }).Count
            if ($actionAppliedInvocationCount -le 0) {
                $issues.Add("The ActionApplied hook was not invoked.")
            }
            if ($actionAppliedAcceptedSnapshotCount -le 0) {
                $issues.Add("The ActionApplied hook accepted no state snapshot.")
            }
            if ($actionAppliedAcceptedSnapshotCount -ne $actionAppliedTraceSnapshotCount -or
                $actionAppliedAcceptedSnapshotCount -ne $phaseSnapshotCount) {
                $issues.Add("ActionApplied accepted counters do not reconcile with preserved trace snapshots.")
            }
            if ($actionAppliedAcceptedSnapshotCount -gt ($actionAppliedInvocationCount * 28)) {
                $issues.Add("ActionApplied accepted snapshot count exceeds the per-invocation state-slot bound.")
            }
        }
    }
}

$validActorSnapshots = @($actorSnapshots | Where-Object {
    Test-SnapshotArtifact -Row $_ -Root $actorRoot -ExpectedBytes 0xE00 -Label "actor"
})
$validStateSnapshots = @($stateSnapshots | Where-Object {
    Test-SnapshotArtifact -Row $_ -Root $stateRoot -ExpectedBytes 0x290 -Label "state"
})
if ($validActorSnapshots.Count -ne $actorSnapshots.Count) {
    $issues.Add("Not every actor snapshot is preserved with its manifest-bound bytes.")
}
if ($validStateSnapshots.Count -ne $stateSnapshots.Count) {
    $issues.Add("Not every state snapshot is preserved with its manifest-bound bytes.")
}

$bindings = [Collections.Generic.List[object]]::new()
$enemyActors = @($validActorSnapshots | Where-Object {
    [string]$_.side -ceq "Enemy" -and [string]$_.phase -ceq "ActorCreated" -and
    [int]$_.battlePosition -ge 14 -and [int]$_.battlePosition -lt 28 -and
    [int]$_.stateIndexAt0x1A -ge 0 -and [int]$_.stateIndexAt0x1A -lt 0x1000
})
foreach ($actor in $enemyActors) {
    $position = [int]$actor.battlePosition
    $stateIndex = [int]$actor.stateIndexAt0x1A
    $actorSequence = [uint64]$actor.sequence
    $encounter = @($encounterWindows | Where-Object {
        [uint64]$_.entrySequence -lt $actorSequence -and
        [uint64]$_.settlementSequence -gt $actorSequence
    } | Select-Object -First 1)
    if ($encounter.Count -ne 1) { continue }
    $baseline = @($validStateSnapshots | Where-Object {
        [int]$_.battlePosition -eq $position -and [int]$_.stateIndex -eq $stateIndex -and
        [string]$_.phase -ceq "ActorCreated" -and [uint64]$_.sequence -gt $actorSequence -and
        [uint64]$_.sequence -le $actorSequence + 8
    } | Sort-Object { [uint64]$_.sequence } | Select-Object -First 1)
    if ($baseline.Count -ne 1) { continue }

    $baselineSequence = [uint64]$baseline[0].sequence
    $nextBaseline = @($validStateSnapshots | Where-Object {
        [int]$_.battlePosition -eq $position -and [int]$_.stateIndex -eq $stateIndex -and
        [string]$_.phase -ceq "ActorCreated" -and [uint64]$_.sequence -gt $baselineSequence
    } | Sort-Object { [uint64]$_.sequence } | Select-Object -First 1)
    $upperBound = if ($nextBaseline.Count -eq 1) { [uint64]$nextBaseline[0].sequence } else { [uint64]::MaxValue }
    $actionTransitions = @($stateTransitions | Where-Object {
        [int]$_.battlePosition -eq $position -and [int]$_.stateIndex -eq $stateIndex -and
        [string]$_.toPhase -ceq "ActionApplied" -and
        [uint64]$_.toSequence -gt $baselineSequence -and [uint64]$_.toSequence -lt $upperBound -and
        [int]$_.changedByteCount -gt 0
    } | Sort-Object { [uint64]$_.toSequence })
    if ($actionTransitions.Count -eq 0) { continue }

    $bindings.Add([pscustomobject][ordered]@{
        battlePosition = $position
        stateIndex = $stateIndex
        actorCreatedSequence = $actorSequence
        stateBaselineSequence = $baselineSequence
        battleEntrySequence = [uint64]$encounter[0].entrySequence
        battleEntrySourceFrameId = [string]$encounter[0].entrySourceFrameId
        battleSettlementSequence = [uint64]$encounter[0].settlementSequence
        battleSettlementSourceFrameId = [string]$encounter[0].settlementSourceFrameId
        actionAppliedTransitionCount = $actionTransitions.Count
        actionAppliedTransitions = @($actionTransitions | ForEach-Object {
            [pscustomobject][ordered]@{
                fromSequence = [uint64]$_.fromSequence
                toSequence = [uint64]$_.toSequence
                changedByteCount = [int]$_.changedByteCount
                fromSha256 = [string]$_.fromSha256
                toSha256 = [string]$_.toSha256
                fromSnapshot = [string]$_.fromTemporarySnapshotFile
                toSnapshot = [string]$_.toTemporarySnapshotFile
            }
        })
    })
}

if ($enemyActors.Count -eq 0) {
    $issues.Add("No enemy ActorCreated snapshot was preserved.")
}
if ($bindings.Count -eq 0) {
    $issues.Add("No enemy actor/state identity has a changed ActionApplied transition.")
}
if ($battleEntries.Count -le 0) {
    $issues.Add("No battle-entry packet is available for encounter correlation.")
}
if ($settlements.Count -le 0) {
    $issues.Add("No settlement packet is available for encounter correlation.")
}
if ($encounterWindows.Count -le 0) {
    $issues.Add("No ordered battle-entry to settlement encounter window is available.")
}

$rawDirectStateReady = $issues.Count -eq 0
$report = [ordered]@{
    schemaVersion = "god2-monster-direct-state-readiness-v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sessionId = $SessionId
    status = if ($rawDirectStateReady) { "RAW_DIRECT_STATE_READY" } else { "EVIDENCE_BLOCKED" }
    rawDirectStateReady = $rawDirectStateReady
    stateRecordSemanticAuthority = "VerifiedRenderSpriteStateNotHpAuthority"
    stateRecordSupportsMaximumHp = $false
    maximumHpPromotionReady = $false
    maximumHpEvidenceStatus = "EvidenceBlockedRenderStateNotHpAuthority"
    promotionBlocker = "Exact-build static consumers prove that the changed 0x290 record is render/sprite state: RVA 0x00006980 initializes render resources and RVA 0x00006D40 consumes +0x22/+0x24 as renderer X/Y. It cannot promote current HP or maximum HP. A separate verified HP authority is required."
    staticSemanticEvidence = @(
        "Artifacts/AnalysisScratch/BattleStateFieldMap/disasm-pool-00006000-plus-3000.txt:RVA 0x00006980",
        "Artifacts/AnalysisScratch/BattleStateFieldMap/disasm-pool-00006000-plus-3000.txt:RVA 0x00006D40",
        "Artifacts/AnalysisScratch/BattleStateFieldMap/disasm-0014AD20-plus-2000.txt:RVA 0x0014AD20"
    )
    actorManifest = ("Artifacts/AnalysisScratch/BattleActorSnapshots/" + $SessionId + "/manifest.json")
    stateManifest = ("Artifacts/AnalysisScratch/BattleStateSnapshots/" + $SessionId + "/manifest.json")
    preservedActorSnapshotCount = $validActorSnapshots.Count
    preservedStateSnapshotCount = $validStateSnapshots.Count
    enemyActorCreatedCount = $enemyActors.Count
    actionAppliedInvocationCount = $actionAppliedInvocationCount
    actionAppliedAcceptedSnapshotCount = $actionAppliedAcceptedSnapshotCount
    boundEnemyActionAppliedCount = $bindings.Count
    battleEntryPacketCount = $battleEntries.Count
    settlementPacketCount = $settlements.Count
    completeEncounterWindowCount = $encounterWindows.Count
    bindings = @($bindings)
    issues = @($issues | Select-Object -Unique)
}
$reportPath = Join-Path $sessionRoot "reports\monster-direct-state-capture-readiness.json"
[IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 12
if (-not $rawDirectStateReady -and -not $NoFail) {
    throw "Monster direct-state capture is not ready; all raw evidence was preserved."
}
