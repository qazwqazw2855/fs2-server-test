param(
    [string] $LauncherPath = $env:GOD2_LAUNCHER_PATH,
    [string] $OutRoot = "",
    [int] $TimeoutMs = 120000
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($LauncherPath)) {
    throw "Set GOD2_LAUNCHER_PATH or pass -LauncherPath."
}

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($OutRoot.Length -eq 0) {
    $OutRoot = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LauncherAutomation"
}

$project = Join-Path $toolRoot "LauncherAutomationRecorder\God2.LauncherAutomationRecorder.csproj"
dotnet run --project $project -c Release -- --record --launcher $LauncherPath --out $OutRoot --timeout-ms $TimeoutMs
