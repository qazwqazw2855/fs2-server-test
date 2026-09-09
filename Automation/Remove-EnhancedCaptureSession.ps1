param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$target = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$rootPrefix = $captureRoot.TrimEnd('\') + '\'

if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $target -eq $captureRoot) {
    throw "Refusing to remove a path outside the enhanced capture session root: $target"
}
if (-not (Test-Path -LiteralPath $target -PathType Container)) {
    throw "Enhanced capture session does not exist: $target"
}

$statusPath = Join-Path $target "reports\headless-enhanced-capture.json"
if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf)) {
    throw "Strict unload status is missing: $statusPath"
}
$status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$status.Status -cne "DETACHED" -or -not [bool]$status.StrictUnloadVerified) {
    throw "Enhanced capture is not safely detached; raw data was preserved."
}

$monsterPolicyPath = Join-Path $target "reports\monster-capture-policy.json"
if (Test-Path -LiteralPath $monsterPolicyPath -PathType Leaf) {
    & (Join-Path $PSScriptRoot "Test-MonsterCaptureCompleteness.ps1") -SessionId $SessionId | Out-Null
    $gatePath = Join-Path $target "reports\monster-extraction-gate.json"
    $gate = Get-Content -LiteralPath $gatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$gate.status -cne "PASS" -or -not [bool]$gate.purgeAllowed) {
        throw "Monster extraction gate did not authorize deletion; raw data was preserved."
    }
    $commitPath = Join-Path $target "reports\monster-import-commit.json"
    if (-not (Test-Path -LiteralPath $commitPath -PathType Leaf)) {
        throw "Monster extraction was verified but not committed; raw data was preserved."
    }
    $commit = Get-Content -LiteralPath $commitPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$commit.status -cne "COMMITTED" -or
        [string]$commit.sessionId -cne $SessionId) {
        throw "Monster import commit does not match this session; raw data was preserved."
    }
}

$statePath = Join-Path $repoRoot "Automation\State\packet-capture-active.json"
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.sessionId -ceq $SessionId -and
        (Get-Process -Id ([int]$state.workerPid) -ErrorAction SilentlyContinue)) {
        throw "Enhanced capture worker is still running; raw data was preserved."
    }
}

$bytes = (Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum
Remove-Item -LiteralPath $target -Recurse -Force

[pscustomobject]@{
    status = "PURGED"
    sessionId = $SessionId
    removedBytes = [long]$bytes
    sessionStillExists = Test-Path -LiteralPath $target
} | ConvertTo-Json
