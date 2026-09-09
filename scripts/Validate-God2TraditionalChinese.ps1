$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $repoRoot "Automation\God2Automation.Common.ps1")

$secretStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $secretStatus.hasSecret) {
    throw $secretStatus.diagnosticZh
}

$project = Join-Path $repoRoot "tools\God2.ZhTwDatabaseViews\God2.ZhTwDatabaseViews.csproj"
try {
    & dotnet run --project $project -c Release -- `
        --repository-root $repoRoot `
        --config (Join-Path $repoRoot "config\database.json") `
        --audit-only true
    if ($LASTEXITCODE -ne 0) {
        throw "Traditional Chinese database validation failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable("GOD2_DB_ADMIN_PASSWORD", $null, "Process")
}
