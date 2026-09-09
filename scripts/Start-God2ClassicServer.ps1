param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ServerArguments)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $repoRoot "Automation\God2Automation.Common.ps1")

$secretStatus = Initialize-God2DatabasePasswordEnvironment
if (-not $secretStatus.hasSecret) {
    throw $secretStatus.diagnosticZh
}

$server = Get-God2ServerExecutablePath
$releaseStatus = Test-God2ServerReleaseManifest
if (-not $releaseStatus.succeeded) {
    throw "God2 Classic Server release verification failed: $($releaseStatus.errorCode): $($releaseStatus.diagnostic)"
}

Push-Location $repoRoot
try {
    & $server @ServerArguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
