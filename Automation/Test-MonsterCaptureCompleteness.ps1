param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,
    [switch] $NoFail
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
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) {
    throw "Monster capture session does not exist: $sessionRoot"
}

$policyPath = Join-Path $sessionRoot "reports\monster-capture-policy.json"
if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) {
    throw "Session is not marked as a monster capture: $SessionId"
}
$policy = Get-Content -LiteralPath $policyPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$policy.sessionId -cne $SessionId -or
    [string]$policy.captureGoal -cne "all-monsters") {
    throw "Monster capture policy does not match the selected session."
}

$catalogPath = Join-Path $repoRoot "db\imports\official\monsters\monsters.official.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogRows = @($catalog.records)
$catalogById = @{}
foreach ($row in $catalogRows) {
    $catalogById[[int]$row.clientMonsterId] = [string]$row.name
}

function Test-CompleteMonsterObservation {
    param(
        [Parameter(Mandatory = $true)] [object] $Row,
        [Parameter(Mandatory = $true)] [hashtable] $OfficialCatalogById,
        [switch] $RequireRuntimeEligible
    )

    $clientId = 0
    $serverId = 0
    $level = 0
    $maximumHp = 0L
    $maximumMp = -1
    $strength = -1
    $constitution = -1
    $intelligence = -1
    $speed = -1
    $metal = -1
    $wood = -1
    $water = -1
    $fire = -1
    $earth = -1
    $experience = -1L
    $encounterLocalId = 0
    $officialSourceRow = 0

    $valid = [int]::TryParse([string]$Row.clientMonsterId, [ref]$clientId) -and $clientId -gt 0
    $valid = $valid -and [int]::TryParse([string]$Row.serverMonsterId, [ref]$serverId) -and $serverId -gt 0
    $valid = $valid -and [int]::TryParse([string]$Row.level, [ref]$level) -and $level -gt 0
    $valid = $valid -and [long]::TryParse([string]$Row.maximumHp, [ref]$maximumHp) -and $maximumHp -gt 0
    $valid = $valid -and [int]::TryParse([string]$Row.maximumMp, [ref]$maximumMp) -and $maximumMp -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.strength, [ref]$strength) -and $strength -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.constitution, [ref]$constitution) -and $constitution -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.intelligence, [ref]$intelligence) -and $intelligence -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.speed, [ref]$speed) -and $speed -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.metal, [ref]$metal) -and $metal -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.wood, [ref]$wood) -and $wood -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.water, [ref]$water) -and $water -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.fire, [ref]$fire) -and $fire -ge 0
    $valid = $valid -and [int]::TryParse([string]$Row.earth, [ref]$earth) -and $earth -ge 0
    $valid = $valid -and [long]::TryParse([string]$Row.experienceReward, [ref]$experience) -and $experience -ge 0
    $valid = $valid -and $OfficialCatalogById.ContainsKey($clientId)

    $isVisualTemplateName = [string]$Row.name -ceq [string]$OfficialCatalogById[$clientId]
    $hasEncounterIdentity =
        [int]::TryParse([string]$Row.encounterLocalId, [ref]$encounterLocalId) -and $encounterLocalId -gt 0 -and
        [int]::TryParse([string]$Row.officialSourceRow, [ref]$officialSourceRow) -and $officialSourceRow -gt 0 -and
        [string]$Row.officialAuthorityKey -ceq "client:encounter-name/${encounterLocalId}:${officialSourceRow}" -and
        -not [string]::IsNullOrWhiteSpace([string]$Row.name)
    $valid = $valid -and ($isVisualTemplateName -or $hasEncounterIdentity)
    $valid = $valid -and [string]$Row.authority -ceq "VERIFIED"
    $valid = $valid -and [string]$Row.correlationStatus -ceq "UniqueTemplateBattleEntrySettlementAndDirectActorVitalBinding"
    $valid = $valid -and [string]$Row.maximumHpEvidenceStatus -ceq "DirectActorObjectSnapshotVerified"
    $valid = $valid -and -not [string]::IsNullOrWhiteSpace([string]$Row.maximumHpEvidenceReference)
    $valid = $valid -and $null -ne $Row.skills -and $null -ne $Row.drops -and $null -ne $Row.spawns
    $valid = $valid -and [string]$Row.skillsCompleteness -ceq "Verified"
    $valid = $valid -and [string]$Row.dropsCompleteness -ceq "Verified"
    $valid = $valid -and [string]$Row.spawnsCompleteness -ceq "Verified"
    $valid = $valid -and [string]$Row.conflictStatus -ceq "None"
    $valid = $valid -and -not [string]::IsNullOrWhiteSpace([string]$Row.battleEntrySourceFrames)
    $valid = $valid -and -not [string]::IsNullOrWhiteSpace([string]$Row.battleSettlementSourceFrames)
    if ($RequireRuntimeEligible) {
        $valid = $valid -and [bool]$Row.runtimeEligible
    }

    return $valid
}

$extractionPath = Join-Path $sessionRoot "analysis\monster-capture-extraction.json"
$extraction = $null
if (Test-Path -LiteralPath $extractionPath -PathType Leaf) {
    $extraction = Get-Content -LiteralPath $extractionPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
$observations = if ($null -eq $extraction) { @() } else { @($extraction.observations) }
$issues = [Collections.Generic.List[string]]::new()
$validRows = [Collections.Generic.List[object]]::new()
$seenClientIds = @{}
$seenServerIds = @{}

# Refresh both evidence reports. The 0x290 direct-state report is retained only
# as render/action-transition evidence; actor+0xDE8..+0xDF4 is the HP/MP authority.
& (Join-Path $PSScriptRoot "Test-MonsterDirectStateCapture.ps1") `
    -SessionId $SessionId -NoFail | Out-Null
& (Join-Path $PSScriptRoot "Test-MonsterActorVitalsCapture.ps1") `
    -SessionId $SessionId -NoFail | Out-Null
$directStatePath = Join-Path $sessionRoot "reports\monster-direct-state-capture-readiness.json"
$directState = $null
if (Test-Path -LiteralPath $directStatePath -PathType Leaf) {
    $directState = Get-Content -LiteralPath $directStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
}
$actorVitalsPath = Join-Path $sessionRoot "reports\monster-actor-vitals-capture-readiness.json"
$actorVitals = $null
if (Test-Path -LiteralPath $actorVitalsPath -PathType Leaf) {
    $actorVitals = Get-Content -LiteralPath $actorVitalsPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
if ($null -eq $actorVitals -or
    [string]$actorVitals.schemaVersion -cne "god2-monster-actor-vitals-readiness-v1" -or
    [string]$actorVitals.sessionId -cne $SessionId -or
    -not [bool]$actorVitals.actorVitalsReady -or
    -not [bool]$actorVitals.maximumHpPromotionReady) {
    $issues.Add("Monster actor-object HP/MP evidence is missing or not promotion-ready.")
}

foreach ($row in $observations) {
    $clientId = 0
    $serverId = 0
    $valid = [int]::TryParse([string]$row.clientMonsterId, [ref]$clientId) -and $clientId -gt 0
    $valid = $valid -and [int]::TryParse([string]$row.serverMonsterId, [ref]$serverId) -and $serverId -gt 0
    $valid = $valid -and (Test-CompleteMonsterObservation -Row $row -OfficialCatalogById $catalogById)
    $valid = $valid -and $null -ne $actorVitals -and $observations.Count -eq 1 -and
        [int]$row.encounterLocalId -eq [int]$actorVitals.encounterLocalId -and
        [int]$row.level -eq [int]$actorVitals.level -and
        [long]$row.maximumHp -eq [long]$actorVitals.maximumHp -and
        [int]$row.maximumMp -eq [int]$actorVitals.maximumMp -and
        [string]$row.maximumHpEvidenceReference -ceq [string]$actorVitals.maximumHpEvidenceReference
    if ($seenClientIds.ContainsKey($clientId) -and $seenClientIds[$clientId] -ne $serverId) {
        $valid = $false
        $issues.Add("Client monster $clientId maps to conflicting server monster IDs.")
    }
    if ($seenServerIds.ContainsKey($serverId) -and $seenServerIds[$serverId] -ne $clientId) {
        $valid = $false
        $issues.Add("Server monster $serverId maps to conflicting client monster IDs.")
    }
    if ($valid) {
        $seenClientIds[$clientId] = $serverId
        $seenServerIds[$serverId] = $clientId
        $validRows.Add($row)
    }
    else {
        $issues.Add("An observation failed the readable identity/stat/evidence contract.")
    }
}

$encounterCount = if ($null -eq $extraction) { 0 } else { [int]$extraction.encounterCount }
$resolvedEncounterCount = if ($null -eq $extraction) { 0 } else { [int]$extraction.fullyResolvedEncounterCount }
$entryPackets = if ($null -eq $extraction) { 0 } else { [int]$extraction.battleEntryPacketCount }
$settlementPackets = if ($null -eq $extraction) { 0 } else { [int]$extraction.settlementPacketCount }
if ($encounterCount -le 0) { $issues.Add("No battle encounter was extracted.") }
if ($resolvedEncounterCount -ne $encounterCount) { $issues.Add("Not every encounter is fully resolved.") }
if ($entryPackets -le 0) { $issues.Add("No battle-entry snapshot was bound.") }
if ($settlementPackets -le 0) { $issues.Add("No settlement packet was bound.") }
if ($validRows.Count -le 0) { $issues.Add("No verified readable monster row is available.") }

$latestPath = Join-Path $repoRoot "db\imports\live\monsters\monsters.verified.latest.json"
$globalRows = @()
if (Test-Path -LiteralPath $latestPath -PathType Leaf) {
    $latest = Get-Content -LiteralPath $latestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $globalRows = @($latest.records)
}
$runtimeSyncPath = Join-Path $repoRoot "Artifacts\RecoveryFinal\runtime-monster-sync.json"
$readableMonsterCount = 0
if (Test-Path -LiteralPath $runtimeSyncPath -PathType Leaf) {
    $runtimeSync = Get-Content -LiteralPath $runtimeSyncPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $readableMonsterCount = [int]$runtimeSync.sourceMonsterCount
}
if ($readableMonsterCount -le 0) {
    $readableMonsterCount = $catalogRows.Count
}
$completeGlobalRows = @($globalRows | Where-Object {
    Test-CompleteMonsterObservation -Row $_ -OfficialCatalogById $catalogById -RequireRuntimeEligible
})
$coveredIds = @($completeGlobalRows | ForEach-Object { [int]$_.serverMonsterId } | Sort-Object -Unique)
$missingIds = @(1..$readableMonsterCount | Where-Object { $_ -notin $coveredIds })
if ($missingIds.Count -ne 0) {
    $issues.Add("The all-monsters global coverage target is incomplete.")
}
$purgeAllowed = $issues.Count -eq 0 -and
    $null -ne $actorVitals -and
    [bool]$actorVitals.actorVitalsReady -and
    [bool]$actorVitals.maximumHpPromotionReady -and
    $missingIds.Count -eq 0
$report = [ordered]@{
    schemaVersion = "god2-monster-capture-completeness-v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sessionId = $SessionId
    status = if ($purgeAllowed) { "PASS" } else { "EVIDENCE_BLOCKED" }
    purgeAllowed = $purgeAllowed
    directState = [ordered]@{
        rawDirectStateReady = $null -ne $directState -and [bool]$directState.rawDirectStateReady
        maximumHpPromotionReady = $null -ne $directState -and [bool]$directState.maximumHpPromotionReady
        semanticAuthority = "VerifiedRenderSpriteStateNotHpAuthority"
        report = "reports/monster-direct-state-capture-readiness.json"
    }
    actorVitals = [ordered]@{
        actorVitalsReady = $null -ne $actorVitals -and [bool]$actorVitals.actorVitalsReady
        maximumHpPromotionReady = $null -ne $actorVitals -and [bool]$actorVitals.maximumHpPromotionReady
        report = "reports/monster-actor-vitals-capture-readiness.json"
    }
    session = [ordered]@{
        encounterCount = $encounterCount
        fullyResolvedEncounterCount = $resolvedEncounterCount
        battleEntryPacketCount = $entryPackets
        settlementPacketCount = $settlementPackets
        verifiedReadableMonsterRows = $validRows.Count
    }
    globalCoverage = [ordered]@{
        readableMonsterCount = $readableMonsterCount
        visualTemplateCount = $catalogRows.Count
        verifiedCompleteMonsterCount = $coveredIds.Count
        fieldClassifiedIncompleteRowCount = $globalRows.Count - $completeGlobalRows.Count
        missingMonsterCount = $missingIds.Count
        complete = $missingIds.Count -eq 0
        missingClientMonsterIds = $missingIds
    }
    issues = @($issues | Select-Object -Unique)
}
$reportPath = Join-Path $sessionRoot "reports\monster-extraction-gate.json"
[IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 10
if (-not $purgeAllowed -and -not $NoFail) {
    throw "Monster extraction is incomplete; raw data must be preserved."
}
