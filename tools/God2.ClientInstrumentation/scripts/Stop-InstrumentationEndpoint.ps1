param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir
)

$ErrorActionPreference = "Stop"

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$active = Join-Path $runDir "instrumentation-endpoint.active.json"
if (-not (Test-Path -LiteralPath $active)) {
    Write-Output "Instrumentation endpoint not active."
    exit 0
}

$state = Get-Content -Raw -LiteralPath $active | ConvertFrom-Json
$pids = New-Object System.Collections.Generic.List[int]
if ($state.pid) { $pids.Add([int]$state.pid) }
if ($state.parentPid) { $pids.Add([int]$state.parentPid) }

foreach ($id in @($pids.ToArray())) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$id" -ErrorAction SilentlyContinue |
        ForEach-Object { $pids.Add([int]$_.ProcessId) }
}

foreach ($id in ($pids.ToArray() | Select-Object -Unique)) {
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($process) {
        Stop-Process -Id $id -Force
        Wait-Process -Id $id -Timeout 10 -ErrorAction SilentlyContinue
    }
}

[ordered]@{
    stoppedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    pid = $state.pid
    parentPid = $state.parentPid
    stopped = $true
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runDir "instrumentation-endpoint.stop.json") -Encoding UTF8

Write-Output "Instrumentation endpoint stopped."
