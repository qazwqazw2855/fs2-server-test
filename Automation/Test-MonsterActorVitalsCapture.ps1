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
$manifestPath = Join-Path $actorRoot "manifest.json"
$packetPath = Join-Path $sessionRoot "raw\injected-packets.jsonl"
$manifest = $null
$packetRows = @()

function Convert-HexBytes {
    param([AllowEmptyString()][string] $Hex)
    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0 -or
        $Hex -notmatch '^[0-9A-Fa-f]+$') { return [byte[]]@() }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Test-FramedOpcode {
    param([object] $Row, [byte] $Opcode, [string] $Api)
    if ([string]$Row.Api -cne $Api) { return $false }
    $bytes = Convert-HexBytes ([string]$Row.PayloadHex)
    return $bytes.Length -ge 3 -and $bytes[2] -eq $Opcode
}

if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $issues.Add("Battle actor snapshot manifest is missing.")
}
if (Test-Path -LiteralPath $packetPath -PathType Leaf) {
    $packetRows = @(Get-Content -LiteralPath $packetPath -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json })
} else {
    $issues.Add("Injected plaintext packet evidence is missing.")
}

$snapshots = @()
if ($null -ne $manifest) {
    $manifestProperties = @($manifest.PSObject.Properties.Name)
    if ($manifestProperties -notcontains "vitalPairSemanticStatus" -or
        $manifestProperties -notcontains "enemyVitalProjectionStatus" -or
        $manifestProperties -notcontains "snapshots" -or
        [string]$manifest.schemaVersion -cne "god2-battle-actor-snapshot-manifest-v3" -or
        [string]$manifest.sessionId -cne $SessionId -or
        [string]$manifest.sourceApi -cne "BattleActorSnapshot" -or
        [string]$manifest.vitalPairSemanticStatus -cne "FriendlyHitPointsMagicPointsConsumerVerified" -or
        [string]$manifest.enemyVitalProjectionStatus -cne "ExactBuildDoesNotProjectEnemyMaximumHpMp" -or
        [bool]$manifest.gameMemoryWritten -or [bool]$manifest.rawPacketDataIncluded) {
        $issues.Add("Battle actor snapshot manifest contract is invalid.")
    } else {
        $snapshots = @($manifest.snapshots)
    }
}

$validSnapshots = [Collections.Generic.List[object]]::new()
foreach ($snapshot in $snapshots) {
    $requiredProperties = @(
        "sequence", "battlePosition", "phase", "descriptorBindingValid",
        "descriptorSequence", "descriptorSha256", "descriptorByteLength",
        "actorLevel", "encounterLocalId", "currentHitPointsAt0xDE8",
        "currentMagicPointsAt0xDEC", "maximumHitPointsAt0xDF0",
        "maximumMagicPointsAt0xDF4", "byteLength", "sha256",
        "temporarySnapshotFile")
    $propertyNames = @($snapshot.PSObject.Properties.Name)
    if (@($requiredProperties | Where-Object { $_ -notin $propertyNames }).Count -ne 0) {
        $issues.Add("Actor snapshot row is missing the descriptor/vital binding contract.")
        continue
    }
    $name = [string]$snapshot.temporarySnapshotFile
    $safeName = -not [string]::IsNullOrWhiteSpace($name) -and
        [IO.Path]::GetFileName($name) -ceq $name
    $path = if ($safeName) { [IO.Path]::GetFullPath((Join-Path $actorRoot $name)) } else { "" }
    $rootPrefix = [IO.Path]::GetFullPath($actorRoot).TrimEnd('\') + '\'
    if (-not $safeName -or
        -not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $issues.Add("Actor snapshot file is missing or escaped its evidence root.")
        continue
    }
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($item.Length -ne 0xE00 -or [int]$snapshot.byteLength -ne 0xE00 -or
        $hash -cne [string]$snapshot.sha256) {
        $issues.Add("Actor snapshot length/hash binding failed: $name")
        continue
    }
    $validSnapshots.Add($snapshot)
}
if ($validSnapshots.Count -ne $snapshots.Count) {
    $issues.Add("Not every actor snapshot is preserved with its manifest-bound bytes.")
}

$entries = @($packetRows | Where-Object {
    Test-FramedOpcode -Row $_ -Opcode 0x82 -Api "PostDecrypt"
} | Sort-Object { [uint64]$_.sequence })
$settlements = @($packetRows | Where-Object {
    Test-FramedOpcode -Row $_ -Opcode 0x89 -Api "PostDecrypt"
} | Sort-Object { [uint64]$_.sequence })
$commands = @($packetRows | Where-Object {
    Test-FramedOpcode -Row $_ -Opcode 0x35 -Api "PreEncrypt"
} | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 20) {
        [pscustomobject]@{ sequence = [uint64]$_.sequence; position = [int]$bytes[3] }
    }
})
$playerPositions = @($commands | Select-Object -ExpandProperty position -Unique)
if ($entries.Count -ne 1) { $issues.Add("Exactly one battle-entry 0x82 frame is required.") }
if ($settlements.Count -ne 1) { $issues.Add("Exactly one settlement 0x89 frame is required.") }
if ($playerPositions.Count -ne 1) { $issues.Add("The command stream must identify one player battle position.") }

$entrySequence = if ($entries.Count -eq 1) { [uint64]$entries[0].sequence } else { [uint64]0 }
$settlementSequence = if ($settlements.Count -eq 1) { [uint64]$settlements[0].sequence } else { [uint64]0 }
if ($entries.Count -eq 1 -and $settlements.Count -eq 1 -and
    $settlementSequence -le $entrySequence) {
    $issues.Add("The settlement does not follow the battle entry.")
}

$playerPosition = if ($playerPositions.Count -eq 1) { [int]$playerPositions[0] } else { -1 }
$enemyCreated = @($validSnapshots | Where-Object {
    [string]$_.phase -ceq "ActorCreated" -and
    [uint64]$_.sequence -gt $entrySequence -and [uint64]$_.sequence -lt $settlementSequence -and
    [int]$_.battlePosition -ne $playerPosition -and [int]$_.battlePosition -ge 14 -and
    [int]$_.battlePosition -lt 28 -and [bool]$_.descriptorBindingValid
} | Sort-Object { [uint64]$_.sequence })
$enemyIdentities = @($enemyCreated | ForEach-Object {
    "$([int]$_.battlePosition)/$([uint64]$_.descriptorSequence)/$([int]$_.encounterLocalId)"
} | Select-Object -Unique)
if ($enemyIdentities.Count -ne 1) {
    $issues.Add("Exactly one descriptor-bound enemy actor is required in the encounter window.")
}

$boundSnapshots = @()
$selected = $null
if ($enemyCreated.Count -gt 0 -and $enemyIdentities.Count -eq 1) {
    $selected = $enemyCreated[0]
    $boundSnapshots = @($validSnapshots | Where-Object {
        [uint64]$_.sequence -ge [uint64]$selected.sequence -and
        [uint64]$_.sequence -lt $settlementSequence -and
        [int]$_.battlePosition -eq [int]$selected.battlePosition -and
        [uint64]$_.descriptorSequence -eq [uint64]$selected.descriptorSequence -and
        [string]$_.descriptorSha256 -ceq [string]$selected.descriptorSha256
    } | Sort-Object { [uint64]$_.sequence })
}

if ($null -ne $selected) {
    if ([int]$selected.actorLevel -le 0 -or [int]$selected.encounterLocalId -le 0 -or
        [int]$selected.descriptorByteLength -ne 45 -or
        [string]$selected.descriptorSha256 -notmatch '^[0-9A-F]{64}$') {
        $issues.Add("The selected enemy descriptor binding is incomplete.")
    }
    if ([int]$selected.maximumHitPointsAt0xDF0 -le 0 -or
        [int]$selected.maximumMagicPointsAt0xDF4 -lt 0) {
        $issues.Add("The selected enemy has invalid maximum HP/MP values.")
    }
    foreach ($snapshot in $boundSnapshots) {
        if ([int]$snapshot.maximumHitPointsAt0xDF0 -ne [int]$selected.maximumHitPointsAt0xDF0 -or
            [int]$snapshot.maximumMagicPointsAt0xDF4 -ne [int]$selected.maximumMagicPointsAt0xDF4 -or
            [int]$snapshot.currentHitPointsAt0xDE8 -lt 0 -or
            [int]$snapshot.currentHitPointsAt0xDE8 -gt [int]$snapshot.maximumHitPointsAt0xDF0 -or
            [int]$snapshot.currentMagicPointsAt0xDEC -lt 0 -or
            [int]$snapshot.currentMagicPointsAt0xDEC -gt [int]$snapshot.maximumMagicPointsAt0xDF4) {
            $issues.Add("The descriptor-bound actor HP/MP sequence violates the verified pair invariants.")
            break
        }
    }
}
if ($boundSnapshots.Count -eq 0) {
    $issues.Add("No manifest-bound enemy actor vital snapshot is available.")
}

$issues.Add("The exact client build does not project enemy maximum HP/MP into the battle actor object; original-server traces retain 0xCDCDCDCD sentinels for enemy maxima.")

$ready = $false
$evidenceReference = if ($null -ne $selected) {
    "Artifacts/AnalysisScratch/BattleActorSnapshots/$SessionId/$([string]$selected.temporarySnapshotFile)#$([string]$selected.sha256)"
} else { $null }
$report = [ordered]@{
    schemaVersion = "god2-monster-actor-vitals-readiness-v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sessionId = $SessionId
    status = "EVIDENCE_BLOCKED"
    actorVitalsReady = $false
    maximumHpPromotionReady = $false
    maximumHpEvidenceStatus = "EvidenceBlockedEnemyVitalProjectionUnavailable"
    correlationStatus = "EvidenceBlocked"
    enemyVitalProjectionStatus = "ExactBuildDoesNotProjectEnemyMaximumHpMp"
    actorManifest = "Artifacts/AnalysisScratch/BattleActorSnapshots/$SessionId/manifest.json"
    battleEntrySequence = $entrySequence
    battleEntrySourceFrameId = if ($entries.Count -eq 1) { [string]$entries[0].SourceFrameId } else { $null }
    battleSettlementSequence = $settlementSequence
    battleSettlementSourceFrameId = if ($settlements.Count -eq 1) { [string]$settlements[0].SourceFrameId } else { $null }
    playerBattlePosition = $playerPosition
    enemyActorCount = $enemyIdentities.Count
    battlePosition = if ($null -ne $selected) { [int]$selected.battlePosition } else { $null }
    descriptorSequence = if ($null -ne $selected) { [uint64]$selected.descriptorSequence } else { $null }
    descriptorSha256 = if ($null -ne $selected) { [string]$selected.descriptorSha256 } else { $null }
    level = if ($null -ne $selected) { [int]$selected.actorLevel } else { $null }
    encounterLocalId = if ($null -ne $selected) { [int]$selected.encounterLocalId } else { $null }
    maximumHp = if ($null -ne $selected) { [int]$selected.maximumHitPointsAt0xDF0 } else { $null }
    maximumMp = if ($null -ne $selected) { [int]$selected.maximumMagicPointsAt0xDF4 } else { $null }
    maximumHpEvidenceReference = $evidenceReference
    boundSnapshotCount = $boundSnapshots.Count
    boundSnapshots = @($boundSnapshots | ForEach-Object {
        [ordered]@{
            sequence = [uint64]$_.sequence
            currentHp = [int]$_.currentHitPointsAt0xDE8
            currentMp = [int]$_.currentMagicPointsAt0xDEC
            maximumHp = [int]$_.maximumHitPointsAt0xDF0
            maximumMp = [int]$_.maximumMagicPointsAt0xDF4
            snapshot = [string]$_.temporarySnapshotFile
            sha256 = [string]$_.sha256
        }
    })
    issues = @($issues | Select-Object -Unique)
}
$reportPath = Join-Path $sessionRoot "reports\monster-actor-vitals-capture-readiness.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 10
if (-not $ready -and -not $NoFail) {
    throw "Monster actor-vitals capture is not ready; all raw evidence was preserved."
}
