param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Start", "Stop")]
    [string] $Action,

    [string] $RunId = "",
    [string] $SessionId = "",
    [ValidateRange(10, 300)]
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$stateRoot = Join-Path $repoRoot "Automation\State"
$traceStatePath = Join-Path $stateRoot "map-fileio-trace.json"
$commandPath = Get-God2AutomationCommandPath
if (Test-Path -LiteralPath $commandPath) {
    throw "Automation Host already has a pending command; Map FileIO trace refused to overwrite it."
}

if ($Action -eq "Start") {
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $RunId = "map-identity-" + (Get-Date -Format "yyyyMMdd-HHmmss")
    }
    if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$') {
        throw "RunId contains unsupported characters."
    }

    $runDir = Join-Path $repoRoot ("Artifacts\MapIdentityRecovery\" + $RunId)
    $resultPath = Join-Path $runDir "fileio-start.json"
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    Write-God2AtomicJson -Value ([ordered]@{
        command = "StartMapFileIoTrace"
        runDir = $runDir
        resultPath = $resultPath
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }) -Path $commandPath
}
else {
    if (-not (Test-Path -LiteralPath $traceStatePath -PathType Leaf)) {
        throw "No God2 Map FileIO trace marker exists."
    }
    $traceState = Read-God2JsonWithRetry -Path $traceStatePath
    $activeSession = [string]$traceState.sessionId
    if (-not [string]::IsNullOrWhiteSpace($SessionId) -and $SessionId -cne $activeSession) {
        throw "Requested Map FileIO trace session does not match the active marker."
    }
    $SessionId = $activeSession
    $runDir = [IO.Path]::GetFullPath([string]$traceState.runDir)
    $resultPath = Join-Path $runDir "fileio-stop.json"
    Write-God2AtomicJson -Value ([ordered]@{
        command = "StopMapFileIoTrace"
        sessionId = $SessionId
        resultPath = $resultPath
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }) -Path $commandPath
}

try {
    $hostOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds ([Math]::Min($TimeoutSeconds, 60)) 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Automation Host start failed: $($hostOutput -join ' ')"
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            $result = Read-God2JsonWithRetry -Path $resultPath
            $result | ConvertTo-Json -Depth 12
            if ([string]$result.status -ne "PASS") { exit 1 }
            exit 0
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Automation Host did not produce the Map FileIO trace result within $TimeoutSeconds seconds."
}
finally {
    if (Test-Path -LiteralPath $commandPath) {
        Remove-Item -LiteralPath $commandPath -Force -ErrorAction SilentlyContinue
    }
}
