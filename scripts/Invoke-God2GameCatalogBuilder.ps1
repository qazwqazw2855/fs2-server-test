param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("rebuild", "validate", "reload", "refresh-caches", "install-triggers")]
    [string] $Command
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $repoRoot "Automation\God2Automation.Common.ps1")

$secretStatus = Initialize-God2DatabaseBuilderPasswordEnvironment
if (-not $secretStatus.hasSecret) {
    throw $secretStatus.diagnosticZh
}
$env:GOD2_DB_BUILDER_USERNAME = "god2_catalog_builder"

$project = Join-Path $repoRoot "tools\God2.GameCatalogBuilder\God2.GameCatalogBuilder.csproj"
$config = Join-Path $repoRoot "config\database.json"
Push-Location $repoRoot
try {
    & dotnet run --project $project -c Release -- --config $config $Command
    if ($LASTEXITCODE -ne 0) {
        throw "God2.GameCatalogBuilder failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
