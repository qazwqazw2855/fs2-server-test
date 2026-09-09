param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $ReadableArtifact
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$rawRoot = [IO.Path]::GetFullPath((Join-Path $sessionRoot "raw"))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
$sessionPrefix = $sessionRoot.TrimEnd('\') + '\'
if (-not $sessionRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $rawRoot.StartsWith($sessionPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Verified purge boundary failed."
}

$artifactPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ReadableArtifact))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $artifactPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
    throw "Readable replacement artifact is missing or outside the workspace."
}
$artifact = Get-Content -LiteralPath $artifactPath -Raw -Encoding UTF8 | ConvertFrom-Json
$allowedArtifactSchemas = @(
    "god2-battle-actor-snapshot-manifest-v3",
    "god2-battle-state-snapshot-manifest-v2"
)
if ([string]$artifact.sessionId -cne $SessionId -or
    [bool]$artifact.rawPacketDataIncluded -or
    [string]$artifact.schemaVersion -cnotin $allowedArtifactSchemas) {
    throw "Readable replacement artifact does not authorize this purge."
}

$statusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$status.Status -cne "DETACHED" -or -not [bool]$status.StrictUnloadVerified) {
    throw "Strict capture unload was not verified."
}

$statePath = Join-Path $repoRoot "Automation\State\packet-capture-active.json"
$state = $null
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.sessionId -ceq $SessionId -and [bool]$state.active) {
        throw "Capture state still marks the detached purge target as active."
    }
}

$monsterPolicyPath = Join-Path $sessionRoot "reports\monster-capture-policy.json"
if (Test-Path -LiteralPath $monsterPolicyPath -PathType Leaf) {
    $monsterPolicy = Get-Content -LiteralPath $monsterPolicyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$monsterPolicy.schemaVersion -cne "god2-monster-capture-policy-v1" -or
        [string]$monsterPolicy.sessionId -cne $SessionId -or
        [string]$monsterPolicy.captureGoal -cne "all-monsters") {
        throw "Monster capture policy does not match this purge target."
    }

    & (Join-Path $PSScriptRoot "Test-MonsterDirectStateCapture.ps1") `
        -SessionId $SessionId -NoFail | Out-Null
    & (Join-Path $PSScriptRoot "Test-MonsterActorVitalsCapture.ps1") `
        -SessionId $SessionId -NoFail | Out-Null
    & (Join-Path $PSScriptRoot "Test-MonsterCaptureCompleteness.ps1") `
        -SessionId $SessionId -NoFail | Out-Null

    $readiness = Get-Content -LiteralPath (
        Join-Path $sessionRoot "reports\monster-actor-vitals-capture-readiness.json") `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $gate = Get-Content -LiteralPath (
        Join-Path $sessionRoot "reports\monster-extraction-gate.json") `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$readiness.schemaVersion -cne "god2-monster-actor-vitals-readiness-v1" -or
        [string]$readiness.sessionId -cne $SessionId -or
        -not [bool]$readiness.actorVitalsReady -or
        -not [bool]$readiness.maximumHpPromotionReady -or
        [string]$gate.status -cne "PASS" -or
        -not [bool]$gate.purgeAllowed -or
        -not [bool]$gate.globalCoverage.complete) {
        throw "Monster capture evidence is not complete and promotion-ready; raw data was preserved."
    }

    $commitPath = Join-Path $sessionRoot "reports\monster-import-commit.json"
    if (-not (Test-Path -LiteralPath $commitPath -PathType Leaf)) {
        throw "Monster capture has no matching commit receipt; raw data was preserved."
    }
    $commit = Get-Content -LiteralPath $commitPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$commit.status -cne "COMMITTED" -or
        [string]$commit.sessionId -cne $SessionId) {
        throw "Monster capture commit receipt is invalid; raw data was preserved."
    }
}

if (Test-Path -LiteralPath $rawRoot -PathType Container) {
    Get-ChildItem -LiteralPath $rawRoot -Recurse -File | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }
    Get-ChildItem -LiteralPath $rawRoot -Recurse -Directory |
        Sort-Object FullName -Descending | ForEach-Object {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force)) {
                Remove-Item -LiteralPath $_.FullName -Force
            }
        }
}

if ($null -ne $state -and [string]$state.sessionId -ceq $SessionId) {
    $state.rawPayloadRetained = $false
    $state.status = "analyzed-readable-evidence-committed-raw-purged"
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))
}

$purgeReport = [ordered]@{
    schemaVersion = "god2-detached-raw-purge-v1"
    sessionId = $SessionId
    purgedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    strictUnloadVerified = $true
    readableArtifact = $ReadableArtifact.Replace('\', '/')
    rawFileCount = @(Get-ChildItem -LiteralPath $rawRoot -Recurse -File).Count
}
[IO.File]::WriteAllText(
    (Join-Path $sessionRoot "reports\raw-purge.json"),
    ($purgeReport | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))

[pscustomobject][ordered]@{
    sessionId = $SessionId
    rawFileCount = @(Get-ChildItem -LiteralPath $rawRoot -Recurse -File).Count
    readableArtifact = $ReadableArtifact.Replace('\', '/')
    rawPayloadRetained = $false
} | ConvertTo-Json
