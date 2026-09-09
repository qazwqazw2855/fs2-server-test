param(
    [int] $ShutdownDelayMilliseconds = 10000
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$databaseConfig = Get-God2DatabaseConfig
$secretStatus = Initialize-God2DatabasePasswordEnvironment `
    -EnvironmentVariableName ([string]$databaseConfig.passwordEnvironmentVariable)
if (-not $secretStatus.hasSecret) {
    throw $secretStatus.failureCode
}

$artifactRoot = Join-Path $repoRoot "Artifacts\BattleArchitectureV2\Migration"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$stopPath = Join-Path $artifactRoot "stop.signal"
Remove-Item -LiteralPath $stopPath -Force -ErrorAction SilentlyContinue

$serverExecutable = Get-God2ServerExecutablePath
$releaseStatus = Test-God2ServerReleaseManifest
if (-not $releaseStatus.succeeded) {
    throw "Release server verification failed: $($releaseStatus.errorCode): $($releaseStatus.diagnostic)"
}

$callback = [System.Threading.TimerCallback] {
    param($state)
    [System.IO.File]::WriteAllText([string]$state, "")
}
$timer = [System.Threading.Timer]::new(
    $callback,
    $stopPath,
    [Math]::Max(1000, $ShutdownDelayMilliseconds),
    [System.Threading.Timeout]::Infinite)

try {
    & $serverExecutable --stop-file $stopPath
    $serverExitCode = $LASTEXITCODE
}
finally {
    $timer.Dispose()
    Remove-Item -LiteralPath $stopPath -Force -ErrorAction SilentlyContinue
}

if ($serverExitCode -ne 0) {
    throw "Server migration verification failed with exit code $serverExitCode."
}

[pscustomobject]@{
    status = "PASS"
    migration = "030"
    serverExitCode = $serverExitCode
    secretSource = $secretStatus.source
    secretEmitted = $false
} | ConvertTo-Json
