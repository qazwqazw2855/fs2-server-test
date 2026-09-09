param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",
    [string] $RunId = "",
    [string] $BuildRoot = "",
    [string] $OutputRoot = "",
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($RunId.Length -eq 0) {
    $RunId = "selftest-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

if ($BuildRoot.Length -eq 0) { $BuildRoot = Join-Path $repoRoot "Build\ClientInstrumentation" }
if ($OutputRoot.Length -eq 0) { $OutputRoot = Join-Path $repoRoot "Validation\ClientInstrumentation\LoginTrial" }
$runDir = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $RunId
$analysis = Join-Path $runDir "analysis"
New-Item -ItemType Directory -Force -Path $analysis | Out-Null

if (-not $SkipBuild) {
    & (Join-Path $toolRoot "scripts\Build-Instrumentation.ps1") -Configuration $Configuration -OutRoot $BuildRoot | Out-Host
}

$buildDir = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) $Configuration
$launcher = Join-Path $buildDir "God2ClientTraceLauncher.exe"
$dll = Join-Path $buildDir "God2ClientTraceProbeTest.dll"
$selfTest = Join-Path $buildDir "God2TraceSelfTestClient.exe"
$attachConfig = Join-Path $buildDir "God2ClientTraceProbe.attach.env"
$selfTestSha256 = (Get-FileHash -LiteralPath $selfTest -Algorithm SHA256).Hash

$cases = @("send", "WSASend", "recv", "WSARecv", "WSARecvOverlapped")
$results = @()
$oldCase = $env:GOD2_TRACE_SELFTEST_CASE
try {
    foreach ($case in $cases) {
        $caseRunDir = Join-Path $runDir $case
        $sensitive = Join-Path $caseRunDir "sensitive"
        $caseAnalysis = Join-Path $caseRunDir "analysis"
        New-Item -ItemType Directory -Force -Path $sensitive,$caseAnalysis | Out-Null
        $tracePath = Join-Path $sensitive "trace.bin"
        if (Test-Path -LiteralPath $tracePath) {
            Remove-Item -LiteralPath $tracePath -Force
        }

        $generalLog = Join-Path $caseRunDir "general.log"
        $metadata = Join-Path $caseRunDir "metadata.jsonl"
        @(
            "clientBuildVerified=1"
            "clientVersion=God2TraceSelfTestClient/1"
            "clientSha256=$selfTestSha256"
            "networkOnly=1"
        ) | Set-Content -LiteralPath $attachConfig -Encoding ASCII
        $env:GOD2_TRACE_SELFTEST_CASE = $case
        $output = & $launcher --launch --self-test-exact-identity --exe $selfTest --cwd $buildDir --dll $dll --trace-dir $sensitive --general-log $generalLog --metadata $metadata --post-inject-before-resume-ms 1500
        if ($LASTEXITCODE -ne 0) {
            throw "Selftest launcher failed case=$case`: $output"
        }

        $pidLine = $output | Where-Object { $_ -match '^PID=' } | Select-Object -First 1
        $pidValue = [int]($pidLine -replace '^PID=', '')
        $deadline = (Get-Date).AddSeconds(60)
        do {
            $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if (-not $process) {
                break
            }
            Start-Sleep -Milliseconds 500
        } while ((Get-Date) -lt $deadline)
        if ($process) {
            throw "Selftest process did not exit case=$case pid=$pidValue"
        }
        Start-Sleep -Milliseconds 750

        $project = Join-Path $toolRoot "Analyzer\God2.ClientInstrumentation.Analyzer.csproj"
        dotnet run --project $project -c Release -- selftest --run-dir $caseRunDir --case $case
        if ($LASTEXITCODE -ne 0) {
            throw "Analyzer selftest failed case=$case."
        }
        $resultPath = Join-Path $caseAnalysis "instrumentation-selftest-$case.json"
        $results += (Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json)
    }
}
finally {
    Remove-Item -LiteralPath $attachConfig -Force -ErrorAction SilentlyContinue
    if ($null -eq $oldCase) {
        Remove-Item Env:\GOD2_TRACE_SELFTEST_CASE -ErrorAction SilentlyContinue
    } else {
        $env:GOD2_TRACE_SELFTEST_CASE = $oldCase
    }
}

$summary = [pscustomobject]@{
    configuration = $Configuration
    runDir = ("Validation/ClientInstrumentation/LoginTrial/" + $RunId)
    cases = $results
    pass = (($results | Where-Object { -not ($_.HookInstalled -and $_.PayloadMatch -and $_.AnalyzerParseCompleted -and $_.TraceIntegrity) }).Count -eq 0)
}
$summaryPath = Join-Path $analysis "instrumentation-selftest-summary.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 6
