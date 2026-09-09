param(
    [int] $ClientPid = 0,
    [string] $Label = "manual",
    [ValidateSet("Standard", "GenericProbe")]
    [string] $ProbeBuildFlavor = "Standard"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$stateRoot = Join-Path $PSScriptRoot "State"
$commandPath = Join-Path $stateRoot "host-command.json"
$statusPath = Join-Path $stateRoot "host-status.json"
$activePath = Join-Path $stateRoot "packet-capture-active.json"
$lastPath = Join-Path $stateRoot "packet-capture-last.json"

New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

if (Test-Path -LiteralPath $activePath) {
    $active = Get-Content -LiteralPath $activePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$active.captureMode -ceq "ContinuousEnhanced" -and
        [string]$active.schemaVersion -ceq "god2-continuous-enhanced-capture-state-v1" -and
        -not [bool]$active.active) {
        # A completed enhanced session intentionally leaves its final state for diagnostics.
        # The packet-only start may replace that inactive, schema-identified state.
    }
    elseif ([string]$active.captureMode -ceq "ContinuousEnhanced") {
        throw "Continuous enhanced capture is active. Run Automation\Stop-ContinuousEnhancedCapture.ps1 first."
    }
    elseif ([string]$active.captureMode -ceq "PacketCaptureOnly" -and
        [string]$active.schemaVersion -ceq "god2-packet-only-capture-state-v1") {
        throw "Packet capture already active: $($active.capture.traceRoot). Run Automation\Stop-PacketCaptureOnly.ps1 first."
    }
    else {
        throw "The shared capture state has an unknown schema; refusing to overwrite it."
    }
}

if ($ClientPid -eq 0) {
    $client = Get-Process -Name God2_opt -ErrorAction Stop | Sort-Object Id | Select-Object -Last 1
    $ClientPid = [int]$client.Id
}
else {
    $client = Get-Process -Id $ClientPid -ErrorAction Stop
}

$status = $null
if (Test-Path -LiteralPath $statusPath) {
    $candidateStatus = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $heartbeatAge = ([DateTimeOffset]::UtcNow -
        [DateTimeOffset]::Parse([string]$candidateStatus.heartbeatAtUtc)).TotalSeconds
    if ($candidateStatus.currentStage -eq "TraceAttached" -and
        [int]$candidateStatus.diagnostic.clientPid -eq $ClientPid -and
        [string]$candidateStatus.diagnostic.probeBuildFlavor -eq $ProbeBuildFlavor -and
        $heartbeatAge -le 120 -and
        (Test-Path -LiteralPath ([string]$candidateStatus.diagnostic.traceRoot) -PathType Container)) {
        $status = $candidateStatus
    }
}

if ($null -eq $status) {
    $attempt = [int](Get-Date -Format "HHmmss")
    @{
        command = "AttachTraceOnly"
        clientPid = $ClientPid
        attempt = $attempt
        probeBuildFlavor = $ProbeBuildFlavor
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $commandPath -Encoding UTF8

    & (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds 20 | Out-Null
    $deadline = (Get-Date).AddSeconds(45)
    do {
        if (Test-Path -LiteralPath $statusPath) {
            $status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($status.currentStage -in @("TraceAttached", "TraceAttachFailed", "HostFailed", "PrivilegeGateFailed")) {
                break
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
}

if ($status.currentStage -ne "TraceAttached") {
    throw "Trace attach failed. Stage=$($status.currentStage) Error=$($status.lastError | ConvertTo-Json -Compress)"
}

$traceRoot = [string]$status.diagnostic.traceRoot
$attachResultPath = Join-Path $traceRoot "attach-result.json"
$attachResult = Get-Content -LiteralPath $attachResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
$moduleLine = @($attachResult.output) | Where-Object { $_ -match '^MODULE=' } | Select-Object -First 1
if (-not $moduleLine) {
    throw "Trace attached but MODULE line was not found in attach-result.json."
}
$module = ($moduleLine -replace '^MODULE=', '').Trim()

$clientPath = [string]$client.Path
$clientSha256 = if ($clientPath -and (Test-Path -LiteralPath $clientPath)) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $clientPath).Hash
}
else {
    ""
}

$runId = Split-Path -Leaf (Split-Path -Parent $traceRoot)
$sourceManifest = [ordered]@{
    schemaVersion = "god2-packet-only-capture-state-v1"
    captureMode = "PacketCaptureOnly"
    runId = $runId
    label = $Label
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    client = [ordered]@{
        pid = $ClientPid
        exePath = $clientPath
        architecture = "x86"
        sha256 = $clientSha256
    }
    capture = [ordered]@{
        traceRoot = $traceRoot
        metadata = [string]$status.diagnostic.metadata
        generalLog = [string]$status.diagnostic.generalLog
        traceBin = (Join-Path $traceRoot "sensitive\trace.bin")
        liveValidationPairs = (Join-Path $traceRoot "sensitive\live-validation-pairs.jsonl")
        markers = (Join-Path $traceRoot "markers.jsonl")
        module = $module
        probeBuildFlavor = $ProbeBuildFlavor
    }
}
$sourceManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $traceRoot "source-manifest.json") -Encoding UTF8
$sourceManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $traceRoot "active-run.json") -Encoding UTF8
$sourceManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $activePath -Encoding UTF8
$sourceManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $lastPath -Encoding UTF8

Write-Host "PACKET CAPTURE STARTED"
Write-Host "PID: $ClientPid"
Write-Host "MODULE: $module"
Write-Host "RUN: $traceRoot"
