param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir,
    [int] $Port = 2592,
    [int] $DurationSeconds = 1800
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$project = Join-Path $toolRoot "Analyzer\God2.ClientInstrumentation.Analyzer.csproj"
$stdout = Join-Path $runDir "instrumentation-endpoint.stdout.log"
$stderr = Join-Path $runDir "instrumentation-endpoint.stderr.log"
$active = Join-Path $runDir "instrumentation-endpoint.active.json"

$existingListener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existingListener) {
    throw "Port $Port is already listening by process $($existingListener.OwningProcess)."
}

$arguments = @(
    "run",
    "--project", "`"$project`"",
    "-c", "Release",
    "--",
    "endpoint",
    "--repo-root", "`"$repoRoot`"",
    "--run-dir", "`"$runDir`"",
    "--port", $Port,
    "--duration-seconds", $DurationSeconds
) -join " "

$process = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Start-Sleep -Seconds 2
if ($process.HasExited) {
    $errorText = if (Test-Path -LiteralPath $stderr) { Get-Content -Raw -LiteralPath $stderr } else { "" }
    throw "Instrumentation endpoint exited early with code $($process.ExitCode). $errorText"
}

$listenerPid = $null
for ($i = 0; $i -lt 20; $i++) {
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $listenerPid = [int]$listener.OwningProcess
        break
    }
    Start-Sleep -Milliseconds 250
}
if (-not $listenerPid) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "Instrumentation endpoint did not open port $Port."
}

[ordered]@{
    pid = $listenerPid
    parentPid = $process.Id
    port = $Port
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    mode = "instrumentation-handshake-only"
    noFormalAuthentication = $true
    noSuccessReplay = $true
    stdout = $stdout
    stderr = $stderr
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $active -Encoding UTF8

Get-Content -Raw -LiteralPath $active
