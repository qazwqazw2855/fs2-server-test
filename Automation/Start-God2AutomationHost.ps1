param(
    [string] $TaskName = "God2Classic.AutomationHost",
    [int] $TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$statusPath = Get-God2AutomationStatusPath
if (Test-Path -LiteralPath $statusPath) {
    $staleRoot = Join-Path (Get-God2RepoRoot) "Artifacts\ClientInstrumentation\ElevatedAutomationHost\stale"
    New-Item -ItemType Directory -Force -Path $staleRoot | Out-Null
    Move-Item -LiteralPath $statusPath -Destination (Join-Path $staleRoot ("host-status-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".json")) -Force
}

$taskValidation = Get-God2AutomationTaskValidation -TaskName $TaskName
if (-not $taskValidation.isCorrect) {
    $registerScript = Join-Path $PSScriptRoot "Register-God2AutomationHost.ps1"
    $registration = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $registerScript -TaskName $TaskName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Automation Host 排程工作修復失敗：$($registration -join ' ')"
    }

    $taskValidation = Get-God2AutomationTaskValidation -TaskName $TaskName
    if (-not $taskValidation.isCorrect) {
        throw "Automation Host 排程工作仍不符合要求：$($taskValidation | ConvertTo-Json -Depth 6)"
    }
}

$run = & schtasks /Run /TN $TaskName 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Failed to start scheduled task $TaskName. Output: $($run -join ' ')"
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    if (Test-Path -LiteralPath $statusPath) {
        try {
            $status = Read-God2JsonWithRetry -Path $statusPath
            $heartbeat = [DateTimeOffset]::Parse([string]$status.heartbeatAtUtc)
            $ageSeconds = ([DateTimeOffset]::UtcNow - $heartbeat).TotalSeconds
            if ($status.ready -eq $true -and [int]$status.integrityRid -eq 0x3000 -and $ageSeconds -le 10) {
                $status | ConvertTo-Json -Depth 12
                exit 0
            }
        }
        catch [System.IO.FileNotFoundException] {
            Start-Sleep -Milliseconds 200
            continue
        }
        catch {
            throw "host-status.json exists but is not parseable: $($_.Exception.Message)"
        }
    }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

throw "Automation Host did not become ready within $TimeoutSeconds seconds."
