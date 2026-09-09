[CmdletBinding()]
param(
    [string] $RunId = "restore-fault-" + (Get-Date -Format "yyyyMMdd-HHmmss"),
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $BuildRoot = "",
    [string] $OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($BuildRoot.Length -eq 0) { $BuildRoot = Join-Path $repoRoot "Build\ClientInstrumentation" }
if ($OutputRoot.Length -eq 0) { $OutputRoot = Join-Path $repoRoot "Validation\ClientInstrumentation" }
$buildRoot = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) $Configuration
$runRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $RunId
$payloadRoot = Join-Path $runRoot "payload"
$traceRoot = Join-Path $runRoot "trace"
if (Test-Path -LiteralPath $runRoot) {
    throw "Restore-fault run already exists: $runRoot"
}
New-Item -ItemType Directory -Path $payloadRoot,$traceRoot | Out-Null

$game = Join-Path $payloadRoot "God2_opt.exe"
$probe = Join-Path $payloadRoot "God2PacketCaptureProbe.dll"
$injector = Join-Path $payloadRoot "God2PacketCaptureInjectorTest.exe"
$attachConfig = Join-Path $payloadRoot "God2ClientTraceProbe.attach.env"
$injectorResult = Join-Path $runRoot "injector-result-v2.json"
$lifecycleReport = Join-Path $runRoot "restore-fault-lifecycle.json"
Copy-Item -LiteralPath (Join-Path $buildRoot "God2TraceSelfTestClient.exe") -Destination $game
Copy-Item -LiteralPath (Join-Path $buildRoot "God2ClientTraceProbeTest.dll") -Destination $probe
Copy-Item -LiteralPath (Join-Path $buildRoot "God2PacketCaptureInjectorTest.exe") -Destination $injector

$oldCase = $env:GOD2_TRACE_SELFTEST_CASE
$oldDelay = $env:GOD2_TRACE_SELFTEST_START_DELAY_MS
$oldHold = $env:GOD2_TRACE_SELFTEST_HOLD_MS
$oldRestoreFault = $env:GOD2_TRACE_SELFTEST_RESTORE_FAULT
$target = $null
$runner = $null
$stopSignal = $null
try {
    $env:GOD2_TRACE_SELFTEST_CASE = "send"
    $env:GOD2_TRACE_SELFTEST_START_DELAY_MS = "10000"
    $env:GOD2_TRACE_SELFTEST_HOLD_MS = "60000"
    $env:GOD2_TRACE_SELFTEST_RESTORE_FAULT = "1"
    $target = Start-Process -FilePath $game -WorkingDirectory $payloadRoot `
        -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runRoot "target.out.txt") `
        -RedirectStandardError (Join-Path $runRoot "target.err.txt") -PassThru
    $clientSha256 = (Get-FileHash -LiteralPath $game -Algorithm SHA256).Hash
    $creationTime = $target.StartTime.ToUniversalTime().ToFileTimeUtc()
    @(
        "traceDir=$traceRoot"
        "generalLog=$(Join-Path $runRoot 'general.log')"
        "metadata=$(Join-Path $runRoot 'metadata.jsonl')"
        "sessionId=$RunId"
        "clientBuildVerified=1"
        "clientVersion=God2TraceSelfTestClient/1"
        "clientSha256=$clientSha256"
        "clientProcessId=$($target.Id)"
        "clientProcessCreationTime=$creationTime"
        "selfTestRestoreFault=1"
        "enableInline=auto"
        "enableInputInline=0"
        "enableVersionProbe=0"
        "networkOnly=1"
    ) | Set-Content -LiteralPath $attachConfig -Encoding ASCII

    $stopEvent = "Local\God2RestoreFault-" + [guid]::NewGuid().ToString("N")
    $createdNew = $false
    $stopSignal = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $stopEvent, [ref]$createdNew)
    if (-not $createdNew) {
        throw "Restore-fault stop event unexpectedly already existed."
    }
    $arguments = @(
        "--monitor", "--self-test-target", "--self-test-restore-fault",
        "--pid", $target.Id,
        "--expected-exe", ('"' + $game + '"'),
        "--dll", ('"' + $probe + '"'),
        "--stop-event", ('"' + $stopEvent + '"'),
        "--result", ('"' + $injectorResult + '"'),
        "--restore-fault-report", ('"' + $lifecycleReport + '"')
    )
    $runner = Start-Process -FilePath $injector -ArgumentList $arguments `
        -WorkingDirectory $payloadRoot -WindowStyle Hidden -PassThru
    $attachDeadline = [DateTime]::UtcNow.AddSeconds(30)
    $attached = $false
    while (-not $runner.HasExited -and [DateTime]::UtcNow -lt $attachDeadline) {
        if (Test-Path -LiteralPath $injectorResult -PathType Leaf) {
            try {
                $currentResult = Get-Content -LiteralPath $injectorResult -Raw |
                    ConvertFrom-Json
                if ([string]$currentResult.status -eq "ATTACHED") {
                    $attached = $true
                    [void]$stopSignal.Set()
                    break
                }
            } catch {
                # The injector replaces this small JSON file atomically enough for
                # evidence, but a polling read may still land during publication.
            }
        }
        Start-Sleep -Milliseconds 25
    }
    if (-not $attached -and -not $runner.HasExited) {
        throw "Restore-fault injector did not reach ATTACHED within 30 seconds."
    }
    if (-not $runner.WaitForExit(60000)) {
        throw "Restore-fault injector did not finish within 60 seconds."
    }
    if ($runner.ExitCode -ne 0) {
        throw "Restore-fault injector failed with exit code $($runner.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $lifecycleReport -PathType Leaf)) {
        throw "Restore-fault lifecycle report was not produced."
    }
    $report = Get-Content -LiteralPath $lifecycleReport -Raw | ConvertFrom-Json
    if (-not [bool]$report.Passed -or -not [bool]$report.ModuleAbsent -or
        -not [bool]$report.TargetAliveAfter -or [int]$report.FreeLibraryCallCount -ne 1) {
        throw "Restore-fault lifecycle did not satisfy the exact-owner contract."
    }
    [pscustomobject]@{
        RunId = $RunId
        ReportPath = "Artifacts/ClientInstrumentation/$RunId/restore-fault-lifecycle.json"
        ReportSHA256 = (Get-FileHash -LiteralPath $lifecycleReport -Algorithm SHA256).Hash
        ProcessId = $report.ProcessId
        Passed = $true
    } | ConvertTo-Json
} finally {
    if ($null -ne $stopSignal) { $stopSignal.Dispose() }
    if ($null -ne $runner -and -not $runner.HasExited) {
        Stop-Process -Id $runner.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $target -and -not $target.HasExited) {
        Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $attachConfig -Force -ErrorAction SilentlyContinue
    $env:GOD2_TRACE_SELFTEST_CASE = $oldCase
    $env:GOD2_TRACE_SELFTEST_START_DELAY_MS = $oldDelay
    $env:GOD2_TRACE_SELFTEST_HOLD_MS = $oldHold
    $env:GOD2_TRACE_SELFTEST_RESTORE_FAULT = $oldRestoreFault
}
