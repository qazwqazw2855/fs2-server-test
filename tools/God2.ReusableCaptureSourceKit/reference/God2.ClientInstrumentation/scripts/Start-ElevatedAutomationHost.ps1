param(
    [string] $HostRoot = "",
    [switch] $SmokeChild,
    [int] $IdleSeconds = 3600
)

$ErrorActionPreference = "Stop"

$scriptPath = $PSCommandPath
$toolRoot = Split-Path -Parent (Split-Path -Parent $scriptPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ([string]::IsNullOrWhiteSpace($HostRoot)) {
    $HostRoot = Join-Path $repoRoot ("Artifacts\ClientInstrumentation\ElevatedAutomationHost\host-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
$HostRoot = [System.IO.Path]::GetFullPath($HostRoot)
New-Item -ItemType Directory -Force -Path $HostRoot | Out-Null

$hostStatusPath = Join-Path $HostRoot "host-status.json"

function Get-HealthyHostStatus {
    if (-not (Test-Path -LiteralPath $hostStatusPath)) {
        return $null
    }
    try {
        $status = Get-Content -LiteralPath $hostStatusPath -Raw | ConvertFrom-Json
        if ($status.mode -ne "Running" -or $status.integrity -notmatch '^(High|System)\s*/') {
            return $null
        }
        $process = Get-Process -Id ([int]$status.pid) -ErrorAction Stop
        if ($process.HasExited) {
            return $null
        }
        return $status
    }
    catch {
        return $null
    }
}

$existingStatus = Get-HealthyHostStatus
if ($null -ne $existingStatus) {
    $existing = [pscustomobject]@{
        controllerPid = [int]$existingStatus.pid
        supervisorPid = $null
        hostRoot = $HostRoot
        hostStatus = $hostStatusPath
        queueRoot = (Join-Path $HostRoot "queue")
        completeRoot = (Join-Path $HostRoot "complete")
        failedRoot = (Join-Path $HostRoot "failed")
        launchedAt = $existingStatus.startedAt
        readyAt = (Get-Date).ToString("o")
        integrity = $existingStatus.integrity
        mode = $existingStatus.mode
        reused = $true
        runAsUsed = $false
        runAsUseCount = 0
    }
    $existing | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $HostRoot "bootstrap.json") -Encoding UTF8
    $existing | ConvertTo-Json -Depth 6
    exit 0
}

$hostScript = Join-Path $toolRoot "scripts\Invoke-ElevatedAutomationSupervisor.ps1"
$args = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $hostScript,
    "-RepoRoot", $repoRoot,
    "-HostRoot", $HostRoot,
    "-IdleSeconds", $IdleSeconds
)
if ($SmokeChild) {
    $args += "-SmokeChild"
}

function Quote-ProcessArgument {
    param([string] $Value)
    if ($Value.Length -eq 0) {
        return '""'
    }
    if ($Value -notmatch '[\s"]') {
        return $Value
    }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

$argumentLine = (($args | ForEach-Object { Quote-ProcessArgument -Value $_ }) -join " ")

$process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -ArgumentList $argumentLine `
    -Verb RunAs `
    -WindowStyle Hidden `
    -PassThru

$deadline = (Get-Date).AddSeconds(20)
$readyStatus = $null
while ((Get-Date) -lt $deadline) {
    $readyStatus = Get-HealthyHostStatus
    if ($null -ne $readyStatus) {
        break
    }
    if ($process.HasExited) {
        throw "Elevated evidence supervisor exited before the controller became ready (exit $($process.ExitCode))."
    }
    Start-Sleep -Milliseconds 100
}
if ($null -eq $readyStatus) {
    throw "Elevated evidence controller did not reach Running/High state within 20 seconds."
}

$bootstrap = [pscustomobject]@{
    controllerPid = [int]$readyStatus.pid
    supervisorPid = $process.Id
    hostRoot = $HostRoot
    hostStatus = (Join-Path $HostRoot "host-status.json")
    queueRoot = (Join-Path $HostRoot "queue")
    completeRoot = (Join-Path $HostRoot "complete")
    failedRoot = (Join-Path $HostRoot "failed")
    stdout = $null
    stderr = $null
    launchedAt = (Get-Date).ToString("o")
    readyAt = (Get-Date).ToString("o")
    integrity = $readyStatus.integrity
    mode = $readyStatus.mode
    reused = $false
    runAsUsed = $true
    runAsUseCount = 1
}
$bootstrap | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $HostRoot "bootstrap.json") -Encoding UTF8
$bootstrap | ConvertTo-Json -Depth 6
