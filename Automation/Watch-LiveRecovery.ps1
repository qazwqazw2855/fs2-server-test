[CmdletBinding()]
param(
    [ValidateRange(10, 3600)]
    [int]$IntervalSeconds = 30,
    [string]$StatePath,
    [string]$MonitorStatusPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $PSScriptRoot "State\packet-capture-active.json"
}
if ([string]::IsNullOrWhiteSpace($MonitorStatusPath)) {
    $MonitorStatusPath = Join-Path $PSScriptRoot "State\live-recovery-monitor.json"
}

function Write-MonitorStatus {
    param([string]$Status, [int]$TargetProcessId, [string]$TraceRoot, [string]$LastError)
    $document = [ordered]@{
        schema = "God2LiveRecoveryMonitor/1"
        monitorProcessId = $PID
        targetProcessId = $TargetProcessId
        status = $Status
        intervalSeconds = $IntervalSeconds
        traceRoot = $TraceRoot
        updatedAtUtc = [DateTime]::UtcNow.ToString("O")
        lastError = $LastError
        clientMemoryWritten = $false
        networkBytesEmitted = $false
    }
    $directory = Split-Path -Parent $MonitorStatusPath
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporary = "$MonitorStatusPath.tmp-$PID"
    [IO.File]::WriteAllText($temporary, ($document | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $MonitorStatusPath -Force
}

$targetProcessId = 0
$traceRoot = ""
$lastErrorText = ""
try {
    while (Test-Path -LiteralPath $StatePath) {
        $state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $targetProcessId = [int]$state.client.pid
        $traceRoot = [string]$state.capture.traceRoot
        if ($targetProcessId -le 0 -or $null -eq (Get-Process -Id $targetProcessId -ErrorAction SilentlyContinue)) {
            break
        }

        try {
            & (Join-Path $PSScriptRoot "Get-LiveRecoveryDashboard.ps1") -StatePath $StatePath | Out-Null
            & (Join-Path $PSScriptRoot "Export-LiveRecoveryEvidence.ps1") -StatePath $StatePath | Out-Null
            $lastErrorText = ""
            Write-MonitorStatus -Status "RUNNING" -TargetProcessId $targetProcessId -TraceRoot $traceRoot -LastError ""
        }
        catch {
            $lastErrorText = $_.Exception.Message
            Write-MonitorStatus -Status "RUNNING_WITH_LAST_REFRESH_ERROR" -TargetProcessId $targetProcessId -TraceRoot $traceRoot -LastError $lastErrorText
        }
        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    Write-MonitorStatus -Status "STOPPED" -TargetProcessId $targetProcessId -TraceRoot $traceRoot -LastError $lastErrorText
}
