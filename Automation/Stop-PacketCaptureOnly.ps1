param(
    [switch] $NoAnalyze,
    [switch] $ForceFinalize
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$stateRoot = Join-Path $PSScriptRoot "State"
$commandPath = Join-Path $stateRoot "host-command.json"
$statusPath = Join-Path $stateRoot "host-status.json"
$activePath = Join-Path $stateRoot "packet-capture-active.json"
$lastPath = Join-Path $stateRoot "packet-capture-last.json"

if (-not (Test-Path -LiteralPath $activePath)) {
    throw "No active packet capture. Run Automation\Start-PacketCaptureOnly.ps1 first."
}

$active = Get-Content -LiteralPath $activePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$active.schemaVersion -cne "god2-packet-only-capture-state-v1" -or
    [string]$active.captureMode -cne "PacketCaptureOnly") {
    throw "The active capture state is not a packet-only capture. Use its matching stop command."
}
$traceRoot = [string]$active.capture.traceRoot
$clientPid = [int]$active.client.pid
$module = [string]$active.capture.module
$probeBuildFlavor = if ([string]$active.capture.probeBuildFlavor -eq "GenericProbe") {
    "GenericProbe"
}
else {
    "Standard"
}

if ([string]::IsNullOrWhiteSpace($traceRoot)) {
    throw "Active packet capture state is missing traceRoot."
}
New-Item -ItemType Directory -Force -Path $traceRoot | Out-Null

@{
    command = "DetachTraceOnly"
    clientPid = $clientPid
    module = $module
    probeBuildFlavor = $probeBuildFlavor
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $commandPath -Encoding UTF8

$detachExitCode = 0
$detachOutput = $null
$detachStage = $null
try {
    & (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds 45 | Out-Null
    $deadline = (Get-Date).AddSeconds(45)
    do {
        $status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($status.currentStage -in @("TraceDetached", "TraceDetachFailed", "HostFailed", "PrivilegeGateFailed")) {
            break
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    $detachStage = [string]$status.currentStage
    $detachExitCode = if ($status.diagnostic -and $null -ne $status.diagnostic.exitCode) {
        [int]$status.diagnostic.exitCode
    }
    else {
        1
    }
    $detachOutput = if ($status.diagnostic) { @($status.diagnostic.output) } else { @($status.lastError) }
}
catch {
    $detachExitCode = 1
    $detachOutput = @($_.Exception.Message)
}

$clientAliveAfterDetach = $null -ne (Get-Process -Id $clientPid -ErrorAction SilentlyContinue)
$detachSucceeded = ($detachExitCode -eq 0 -and $detachStage -eq "TraceDetached")
if (-not $detachSucceeded -and $clientAliveAfterDetach -and -not $ForceFinalize) {
    $failureSummary = [ordered]@{
        stoppedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        detach = "failed"
        detachStage = $detachStage
        detachExitCode = $detachExitCode
        detachOutput = @($detachOutput)
        clientPid = $clientPid
        clientStillRunning = $true
        activeStatePreserved = $true
        retry = "Run Stop-PacketCaptureOnly.ps1 again. Use -ForceFinalize only after the client has exited or if the active marker is stale."
    }
    $failureSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $traceRoot "stop-summary.json") -Encoding UTF8
    throw "Packet capture detach failed; active capture state was preserved so stop can be retried. Stage=$detachStage ExitCode=$detachExitCode"
}

$clientPath = [string]$active.client.exePath
$clientSha256After = if ($clientPath -and (Test-Path -LiteralPath $clientPath)) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $clientPath).Hash
}
else {
    ""
}

$files = @()
foreach ($path in @(
    (Join-Path $traceRoot "sensitive\trace.bin"),
    (Join-Path $traceRoot "sensitive\live-validation-pairs.jsonl"),
    (Join-Path $traceRoot "markers.jsonl"),
    (Join-Path $traceRoot "metadata.jsonl"),
    (Join-Path $traceRoot "general.log"),
    (Join-Path $traceRoot "attach-result.json")
)) {
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path
        $files += [ordered]@{
            path = $item.FullName
            lengthBytes = $item.Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.FullName).Hash
            lastWriteTime = $item.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
        }
    }
}

$stopSummary = [ordered]@{
    stoppedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    detach = if ($detachSucceeded) { "pass" } elseif (-not $clientAliveAfterDetach) { "client-exited" } else { "failed" }
    detachStage = $detachStage
    detachExitCode = $detachExitCode
    detachOutput = @($detachOutput)
    clientStillRunning = $clientAliveAfterDetach
    clientSha256After = $clientSha256After
    clientBinaryUnchanged = ($clientSha256After -eq [string]$active.client.sha256)
    files = @($files)
}
$stopSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $traceRoot "stop-summary.json") -Encoding UTF8

$analyzerExitCode = $null
$reportPath = Join-Path $repoRoot ("Reports\PacketCapture\" + (Split-Path -Leaf (Split-Path -Parent $traceRoot)) + "-" + (Split-Path -Leaf $traceRoot) + ".md")
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $reportPath) | Out-Null
if (-not $NoAnalyze) {
    $analyzer = Join-Path $repoRoot "tools\God2.ClientInstrumentation\Analyzer\bin\Release\net10.0\God2.ClientInstrumentation.Analyzer.exe"
    if (Test-Path -LiteralPath $analyzer) {
        & $analyzer analyze --run-dir $traceRoot --report $reportPath | Out-Null
        $analyzerExitCode = $LASTEXITCODE
    }
}

$finalSummary = [ordered]@{
    stoppedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runDir = $traceRoot
    detach = $stopSummary.detach
    detachStage = $stopSummary.detachStage
    analyzerExitCode = $analyzerExitCode
    report = if (Test-Path -LiteralPath $reportPath) { $reportPath } else { "" }
    files = @($files)
}
$finalSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $traceRoot "capture-summary.json") -Encoding UTF8
$finalSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $lastPath -Encoding UTF8
if (Test-Path -LiteralPath $activePath) {
    Remove-Item -LiteralPath $activePath -Force
}

Write-Host "PACKET CAPTURE STOPPED"
Write-Host "DETACH: $($stopSummary.detach)"
Write-Host "RUN: $traceRoot"
if ($finalSummary.report) {
    Write-Host "REPORT: $($finalSummary.report)"
}
foreach ($file in $files) {
    Write-Host "$($file.lengthBytes) bytes  $($file.path)"
}
