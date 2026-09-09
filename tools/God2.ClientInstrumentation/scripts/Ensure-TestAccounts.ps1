param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$project = Join-Path $toolRoot "Analyzer\God2.ClientInstrumentation.Analyzer.csproj"

dotnet run --project $project -c Release -- ensure-accounts --repo-root $repoRoot --run-dir $RunDir
if ($LASTEXITCODE -ne 0) {
    throw "Test account preparation failed."
}

