param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        "MARKER_LOGIN_BEGIN",
        "MARKER_LOGIN_COMPLETE",
        "MARKER_IDLE_BEGIN",
        "MARKER_IDLE_END",
        "MARKER_OPEN_INVENTORY",
        "MARKER_CLOSE_INVENTORY",
        "MARKER_NPC_CLICK",
        "MARKER_SHOP_OPEN",
        "MARKER_BATTLE_BEGIN",
        "MARKER_NORMAL_ATTACK",
        "MARKER_SKILL_CAST",
        "MARKER_BATTLE_END",
        "MARKER_LOGOUT",
        "MARKER_RELOGIN_BEGIN",
        "MARKER_RELOGIN_COMPLETE"
    )]
    [string] $Marker
)

$ErrorActionPreference = "Stop"
$activePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
if (-not (Test-Path -LiteralPath $activePath)) {
    throw "No active diagnostic packet capture. Run Automation\Start-PacketCaptureOnly.ps1 first."
}

$active = Get-Content -LiteralPath $activePath -Raw -Encoding UTF8 | ConvertFrom-Json
$traceRoot = [string]$active.capture.traceRoot
if ([string]::IsNullOrWhiteSpace($traceRoot) -or -not (Test-Path -LiteralPath $traceRoot)) {
    throw "The active capture trace root is missing or no longer exists."
}

$now = [DateTimeOffset]::UtcNow
$record = [ordered]@{
    schemaVersion = 1
    marker = $Marker
    markedAtUtc = $now.ToString("o")
    markerUnixMs = $now.ToUnixTimeMilliseconds()
    runId = [string]$active.runId
    clientPid = [int]$active.client.pid
    traceRoot = $traceRoot
}
$markerPath = Join-Path $traceRoot "markers.jsonl"
($record | ConvertTo-Json -Compress -Depth 6) | Add-Content -LiteralPath $markerPath -Encoding UTF8

Write-Host "PACKET CAPTURE MARKER RECORDED"
Write-Host "MARKER: $Marker"
Write-Host "RUN: $traceRoot"
