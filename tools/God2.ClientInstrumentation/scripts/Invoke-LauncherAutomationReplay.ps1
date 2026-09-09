param(
    [Parameter(Mandatory = $true)]
    [string] $FlowPath,
    [string] $LauncherPath = $env:GOD2_LAUNCHER_PATH,
    [int] $TimeoutMs = 30000
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($LauncherPath)) {
    throw "Set GOD2_LAUNCHER_PATH or pass -LauncherPath."
}

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $toolRoot "LauncherAutomationRecorder\God2.LauncherAutomationRecorder.csproj"
dotnet run --project $project -c Release -- --replay --flow $FlowPath --launcher $LauncherPath --timeout-ms $TimeoutMs
