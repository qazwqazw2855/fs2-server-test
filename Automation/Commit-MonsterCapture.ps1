param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
if (-not $sessionRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Session path escaped the approved capture root."
}

$withdrawalIndexPath = Join-Path $repoRoot "Artifacts\RecoveryFinal\monster-hp-claim-withdrawals-20260813.json"
$withdrawalsByClaim = @{}
if (Test-Path -LiteralPath $withdrawalIndexPath -PathType Leaf) {
    $withdrawalIndex = Get-Content -LiteralPath $withdrawalIndexPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($claim in @($withdrawalIndex.claims)) {
        $withdrawalsByClaim["$([int]$claim.serverMonsterId):$([long]$claim.withdrawnValue)"] = $claim
    }
}

function Set-RecordProperty {
    param(
        [Parameter(Mandatory = $true)] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Name,
        [AllowNull()] [object] $Value
    )

    $InputObject | Add-Member -MemberType NoteProperty -Name $Name -Value $Value -Force
}

function Test-DirectMaximumHpEvidence {
    param([Parameter(Mandatory = $true)] [object] $Row)

    $maximumHp = 0L
    return [long]::TryParse([string]$Row.maximumHp, [ref]$maximumHp) -and
        $maximumHp -gt 0 -and
        [string]$Row.authority -ceq "VERIFIED" -and
        [string]$Row.correlationStatus -ceq "UniqueTemplateBattleEntrySettlementAndDirectActorVitalBinding" -and
        [string]$Row.maximumHpEvidenceStatus -ceq "DirectActorObjectSnapshotVerified" -and
        -not [string]::IsNullOrWhiteSpace([string]$Row.maximumHpEvidenceReference)
}

function Remove-UnprovenMaximumHp {
    param([Parameter(Mandatory = $true)] [object] $Row)

    if (Test-DirectMaximumHpEvidence -Row $Row) {
        return $Row
    }

    $serverId = 0
    $withdrawnHp = 0L
    $hasClaimKey = [int]::TryParse([string]$Row.serverMonsterId, [ref]$serverId) -and
        [long]::TryParse([string]$Row.maximumHp, [ref]$withdrawnHp)
    $claimKey = "${serverId}:${withdrawnHp}"
    $claim = if ($hasClaimKey -and $withdrawalsByClaim.ContainsKey($claimKey)) {
        $withdrawalsByClaim[$claimKey]
    }
    else {
        $null
    }

    $existingStatus = [string]$Row.maximumHpEvidenceStatus
    $nextStatus = if ($null -ne $claim) {
        [string]$claim.fieldEvidenceStatus
    }
    elseif ($existingStatus.StartsWith("Withdrawn", [StringComparison]::Ordinal)) {
        $existingStatus
    }
    else {
        "EvidenceBlockedMissingDirectActorVitalSnapshot"
    }
    Set-RecordProperty -InputObject $Row -Name maximumHp -Value $null
    Set-RecordProperty -InputObject $Row -Name maximumHpEvidenceStatus -Value $nextStatus
    if ($null -ne $claim) {
        Set-RecordProperty -InputObject $Row -Name maximumHpEvidenceReference -Value (
            "Artifacts/RecoveryFinal/monster-hp-claim-withdrawals-20260813.json#$([string]$claim.id)")
    }
    Set-RecordProperty -InputObject $Row -Name runtimeEligible -Value $false
    return $Row
}

& (Join-Path $PSScriptRoot "Test-MonsterCaptureCompleteness.ps1") -SessionId $SessionId | Out-Null
$gatePath = Join-Path $sessionRoot "reports\monster-extraction-gate.json"
$gate = Get-Content -LiteralPath $gatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [bool]$gate.purgeAllowed -or
    -not [bool]$gate.actorVitals.actorVitalsReady -or
    -not [bool]$gate.actorVitals.maximumHpPromotionReady -or
    -not [bool]$gate.globalCoverage.complete) {
    throw "Monster capture cannot be committed because its extraction gate is blocked."
}

$extractionPath = Join-Path $sessionRoot "analysis\monster-capture-extraction.json"
$extraction = Get-Content -LiteralPath $extractionPath -Raw -Encoding UTF8 | ConvertFrom-Json
$destinationDir = Join-Path $repoRoot "db\imports\live\monsters"
[IO.Directory]::CreateDirectory($destinationDir) | Out-Null
$destination = Join-Path $destinationDir "monsters.verified.latest.json"
$byServerId = @{}
if (Test-Path -LiteralPath $destination -PathType Leaf) {
    $current = Get-Content -LiteralPath $destination -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($row in @($current.records)) {
        $safeRow = Remove-UnprovenMaximumHp -Row $row
        $byServerId[[int]$safeRow.serverMonsterId] = $safeRow
    }
}

$committedAt = [DateTimeOffset]::UtcNow.ToString("o")
foreach ($row in @($extraction.observations)) {
    $readable = [ordered]@{
        clientMonsterId = [int]$row.clientMonsterId
        serverMonsterId = [int]$row.serverMonsterId
        encounterLocalId = [int]$row.encounterLocalId
        name = [string]$row.name
        level = [int]$row.level
        maximumHp = [long]$row.maximumHp
        maximumHpEvidenceStatus = [string]$row.maximumHpEvidenceStatus
        maximumHpEvidenceReference = [string]$row.maximumHpEvidenceReference
        maximumMp = [int]$row.maximumMp
        strength = [int]$row.strength
        constitution = [int]$row.constitution
        intelligence = [int]$row.intelligence
        speed = [int]$row.speed
        metal = [int]$row.metal
        wood = [int]$row.wood
        water = [int]$row.water
        fire = [int]$row.fire
        earth = [int]$row.earth
        experienceReward = [long]$row.experienceReward
        skills = @($row.skills)
        drops = @($row.drops)
        spawns = @($row.spawns)
        skillsCompleteness = [string]$row.skillsCompleteness
        dropsCompleteness = [string]$row.dropsCompleteness
        spawnsCompleteness = [string]$row.spawnsCompleteness
        authority = "VERIFIED"
        correlationStatus = "UniqueTemplateBattleEntrySettlementAndDirectActorVitalBinding"
        conflictStatus = "None"
        battleEntrySourceFrames = [string]$row.battleEntrySourceFrames
        battleSettlementSourceFrames = [string]$row.battleSettlementSourceFrames
        officialAuthorityKey = [string]$row.officialAuthorityKey
        officialSourceRow = [int]$row.officialSourceRow
        runtimeEligible = $true
        sourceSessionId = $SessionId
        verifiedAtUtc = $committedAt
    }
    $byServerId[[int]$row.serverMonsterId] = [pscustomobject]$readable
}

$records = @($byServerId.Values | Sort-Object { [int]$_.serverMonsterId })
$document = [ordered]@{
    schemaVersion = "god2-verified-encounter-monster-latest-v3"
    generatedAtUtc = $committedAt
    replacementPolicy = "OneLatestFieldClassifiedRowPerServerMonsterId"
    recordCount = $records.Count
    records = $records
}
$temporary = "$destination.$([guid]::NewGuid().ToString('N')).tmp"
[IO.File]::WriteAllText(
    $temporary,
    ($document | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
if (Test-Path -LiteralPath $destination -PathType Leaf) {
    [IO.File]::Replace($temporary, $destination, $null, $true)
}
else {
    Move-Item -LiteralPath $temporary -Destination $destination -Force
}

$commit = [ordered]@{
    schemaVersion = "god2-monster-import-commit-v1"
    status = "COMMITTED"
    sessionId = $SessionId
    committedAtUtc = $committedAt
    committedSessionRows = @($extraction.observations).Count
    latestVerifiedMonsterRows = $records.Count
    destination = "db/imports/live/monsters/monsters.verified.latest.json"
    destinationSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    replacementPolicy = "OneLatestFieldClassifiedRowPerServerMonsterId"
}
$commitPath = Join-Path $sessionRoot "reports\monster-import-commit.json"
[IO.File]::WriteAllText(
    $commitPath,
    ($commit | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
$commit | ConvertTo-Json -Depth 8
