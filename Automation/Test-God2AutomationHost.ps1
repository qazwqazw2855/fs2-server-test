param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$statusPath = Get-God2AutomationStatusPath
$testRoot = Join-Path $repoRoot "Artifacts\ClientInstrumentation\ElevatedAutomationHost\tests"
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

$taskValidation = Get-God2AutomationTaskValidation
$registrationAttempted = $false
$registrationExitCode = $null
$registrationOutput = @()
if (-not $taskValidation.isCorrect) {
    $registrationAttempted = $true
    $registerScript = Join-Path $PSScriptRoot "Register-God2AutomationHost.ps1"
    $registrationOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $registerScript 2>&1)
    $registrationExitCode = $LASTEXITCODE
    $taskValidation = Get-God2AutomationTaskValidation
}

$stale = [pscustomobject]@{
    schemaVersion = 1
    ready = $true
    integrityRid = 0x3000
    heartbeatAtUtc = [DateTimeOffset]::UtcNow.AddSeconds(-60).ToString("o")
}
$stalePath = Join-Path $testRoot "stale-host-status.json"
Write-God2AtomicJson -Value $stale -Path $stalePath
$parsed = Get-Content -LiteralPath $stalePath -Raw | ConvertFrom-Json
$staleRejected = (([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$parsed.heartbeatAtUtc)).TotalSeconds -gt 10)

$invalidPath = Join-Path $testRoot "invalid-host-status.json"
"{" | Set-Content -LiteralPath $invalidPath -Encoding UTF8
$invalidRejected = $false
try {
    Get-Content -LiteralPath $invalidPath -Raw | ConvertFrom-Json | Out-Null
}
catch {
    $invalidRejected = $true
}

$result = [pscustomobject]@{
    atomicWrite = (Test-Path -LiteralPath $stalePath)
    staleStatusRejected = $staleRejected
    invalidJsonRejected = $invalidRejected
    noUacPath = "schtasks /Run /TN God2Classic.AutomationHost"
    scheduledTaskExists = $taskValidation.exists
    scheduledTaskRunLevelHighest = $taskValidation.runLevelHighest
    scheduledTaskActionRelative = $taskValidation.actionRelative
    scheduledTaskWorkingDirectoryCorrect = $taskValidation.workingDirectoryCorrect
    scheduledTaskInteractive = $taskValidation.logonTypeInteractive
    scheduledTaskCorrect = $taskValidation.isCorrect
    registrationAttempted = $registrationAttempted
    registrationExitCode = $registrationExitCode
    registrationOutput = $registrationOutput
    diagnosticZh = $taskValidation.diagnosticZh
}
$resultPath = Join-Path $testRoot "automation-host-tests.json"
Write-God2AtomicJson -Value $result -Path $resultPath
$result | ConvertTo-Json -Depth 6

if (-not $result.atomicWrite -or
    -not $result.staleStatusRejected -or
    -not $result.invalidJsonRejected -or
    -not $result.scheduledTaskCorrect) {
    exit 1
}
