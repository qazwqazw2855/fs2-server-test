$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Get-Utf8Text([string] $Value) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value))
}

$Host.UI.RawUI.WindowTitle = Get-Utf8Text "R29kMiBDbGFzc2ljIOacjeWLmeerrw=="
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)

. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

# A visible production launch owns the single local game endpoint. Close the
# previous visible launch before validation so repeated starts cannot pile up.
$currentProcessId = $PID
$previousServerProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessId -ne $currentProcessId -and
    ($_.Name -eq "God2 Classic Server.exe" -or $_.CommandLine -match "God2 Classic Server\.exe")
})
foreach ($process in $previousServerProcesses) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
}

$previousVisibleHosts = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessId -ne $currentProcessId -and
    $_.Name -eq "powershell.exe" -and
    $_.CommandLine -notmatch '(?i)\s-(?:Command|EncodedCommand)\s' -and
    $_.CommandLine -match '(?i)(?:^|\s)-File\s+"?[^"\r\n]*\\Start-God2ServerVisible\.ps1"?'
})
foreach ($process in $previousVisibleHosts) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
do {
    $existingListener = Get-NetTCPConnection -LocalPort 2592 -State Listen -ErrorAction SilentlyContinue
    if (-not $existingListener) { break }
    Start-Sleep -Milliseconds 100
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($existingListener) {
    throw (Get-Utf8Text "6IiK5pyN5YuZ56uv5LuN5Y2g55SoIDI1OTLvvJvngrrpgb/lhY3ph43opIfllZ/li5XvvIzmnKzmrKHllZ/li5Xlt7Lmi5LntZXjgII=")
}

Write-Host (Get-Utf8Text "5q2j5Zyo6amX6K2J5q2j5byP5pyN5YuZ56uv54mI5pysLi4u") -ForegroundColor Cyan
$releaseStatus = Test-God2ServerReleaseManifest
if (-not $releaseStatus.succeeded) {
    Write-Host (Get-Utf8Text "54mI5pys5qqU5qGI6amX6K2J5aSx5pWX77yM5ouS57WV5ZWf5YuV44CC") -ForegroundColor Red
    Write-Host ("{0}: {1}" -f $releaseStatus.errorCode, $releaseStatus.diagnostic) -ForegroundColor DarkRed
    Read-Host (Get-Utf8Text "5oyJIEVudGVyIOmXnOmWiQ==")
    exit 2
}

$secretStatus = Initialize-God2DatabasePasswordEnvironment
if (-not $secretStatus.hasSecret) {
    Write-Host ((Get-Utf8Text "6LOH5paZ5bqr5a+G56K86LyJ5YWl5aSx5pWX77ya") + $secretStatus.diagnosticZh) -ForegroundColor Red
    Read-Host (Get-Utf8Text "5oyJIEVudGVyIOmXnOmWiQ==")
    exit 1
}

try {
    $migrationOutput = & (Join-Path $PSScriptRoot "Invoke-God2PendingSchemaMigrationsAsAdmin.ps1") | Out-String
    $migrationStatus = $migrationOutput | ConvertFrom-Json
    $applied = @($migrationStatus.appliedNow)
    $appliedText = if ($applied.Count -eq 0) {
        Get-Utf8Text "54Sh"
    }
    else {
        $applied -join ", "
    }
    Write-Host (Get-Utf8Text "6LOH5paZ5bqr57WQ5qeL77ya5bey5piv5pyA5paw") -ForegroundColor Green
    Write-Host ((Get-Utf8Text "5q2j5byP6LOH5paZ5bqr54mI5pys77yaezB9L3sxfQ==") -f $migrationStatus.discovered, $migrationStatus.discovered)
    Write-Host ((Get-Utf8Text "5pys5qyh5aWX55So54mI5pys77yaezB9") -f $appliedText)
}
catch {
    Write-Host ("Migration: " + $_.Exception.Message) -ForegroundColor Red
    Read-Host (Get-Utf8Text "5oyJIEVudGVyIOmXnOmWiQ==")
    exit 3
}

$server = Get-God2ServerExecutablePath
Write-Host ((Get-Utf8Text "5q2j5Zyo5ZWf5YuVIEdvZDIgQ2xhc3NpYyDmnI3li5nnq68uLi4u") + " Build: " + $releaseStatus.buildId) -ForegroundColor Cyan
try {
    [Environment]::SetEnvironmentVariable(
        "GOD2_RUNTIME_EVIDENCE_DIRECTORY",
        (Join-Path $repoRoot "runtime\protocol-evidence"),
        "Process")
    & $server
}
finally {
    [Environment]::SetEnvironmentVariable("GOD2_DB_PASSWORD", $null, "Process")
    [Environment]::SetEnvironmentVariable("GOD2_RUNTIME_EVIDENCE_DIRECTORY", $null, "Process")
}

Write-Host (Get-Utf8Text "5pyN5YuZ56uv5bey5YGc5q2i44CC") -ForegroundColor Yellow
exit 0
