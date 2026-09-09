param(
    [int] $LauncherPid = 548,
    [int] $ClientPid = 15988
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$reportDir = Join-Path $repoRoot ("Artifacts\ClientInstrumentation\ElevatedAutomationHost\failure-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

$report = [pscustomobject]@{
    schemaVersion = 1
    savedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    blocker = "Medium controller cannot SendInput to High God2_opt"
    self = Get-God2ProcessSnapshot -ProcessId $PID
    launcher = Get-God2ProcessSnapshot -ProcessId $LauncherPid
    client = Get-God2ProcessSnapshot -ProcessId $ClientPid
    launcherKeptAlive = $true
    clientKeptAlive = $true
}

$path = Join-Path $reportDir "uac-integrity-failure.json"
Write-God2AtomicJson -Value $report -Path $path
Get-Content -LiteralPath $path -Raw
