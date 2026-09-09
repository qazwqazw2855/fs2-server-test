$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $repoRoot "Automation\God2Automation.Common.ps1")

$secretStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $secretStatus.hasSecret) {
    throw $secretStatus.diagnosticZh
}
$env:GOD2_DB_ADMIN_USERNAME = "root"
$server = Get-God2ServerExecutablePath
$releaseStatus = Test-God2ServerReleaseManifest
if (-not $releaseStatus.succeeded) {
    throw "Published release verification failed: $($releaseStatus.errorCode): $($releaseStatus.diagnostic)"
}

Push-Location $repoRoot
try {
    & $server --migrate-only
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
