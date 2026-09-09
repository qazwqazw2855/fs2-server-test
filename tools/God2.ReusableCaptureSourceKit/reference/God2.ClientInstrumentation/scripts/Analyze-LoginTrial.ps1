param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$project = Join-Path $toolRoot "Analyzer\God2.ClientInstrumentation.Analyzer.csproj"
$report = Join-Path $repoRoot "Reports\ClientLoginInstrumentationReport.md"

dotnet run --project $project -c Release -- analyze --run-dir $RunDir --report $report
if ($LASTEXITCODE -ne 0) {
    throw "Login trial analysis failed."
}

