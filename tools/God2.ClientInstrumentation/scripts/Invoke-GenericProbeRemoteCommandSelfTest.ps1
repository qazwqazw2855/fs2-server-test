param(
    [string] $BuildRoot = "",
    [string] $OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($BuildRoot.Length -eq 0) {
    $BuildRoot = Join-Path $repoRoot "Build\ClientInstrumentation-GenericProbe"
}
if ($OutputRoot.Length -eq 0) {
    $OutputRoot = Join-Path $repoRoot "Validation\ClientInstrumentation\GenericProbePlan\remote-command-selftest"
}
$build = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) "Release"
$run = [IO.Path]::GetFullPath($OutputRoot)
$sensitive = Join-Path $run "sensitive"
New-Item -ItemType Directory -Force -Path $run,$sensitive | Out-Null
$launcher = Join-Path $build "God2ClientTraceLauncher.exe"
$client = Join-Path $build "God2TraceSelfTestClient.exe"
$dll = Join-Path $build "God2ClientTraceProbeTest.dll"
$config = Join-Path $build "God2ClientTraceProbe.attach.env"
$sha = (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash
@(
    "clientBuildVerified=1"
    "clientVersion=God2TraceSelfTestClient/1"
    "clientSha256=$sha"
    "networkOnly=1"
) | Set-Content -LiteralPath $config -Encoding ASCII

$stdout = Join-Path $run "launcher.out"
$stderr = Join-Path $run "launcher.err"
$arguments = @(
    "--launch", "--self-test-exact-identity",
    "--exe", ('"' + $client + '"'),
    "--cwd", ('"' + $build + '"'),
    "--dll", ('"' + $dll + '"'),
    "--trace-dir", ('"' + $sensitive + '"'),
    "--general-log", ('"' + (Join-Path $run "general.log") + '"'),
    "--metadata", ('"' + (Join-Path $run "metadata.jsonl") + '"'),
    "--post-inject-before-resume-ms", "20000"
)
$launcherProcess = Start-Process -FilePath $launcher -ArgumentList $arguments `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr
try {
    $deadline = (Get-Date).AddSeconds(10)
    $pidValue = 0
    $moduleValue = 0
    do {
        Start-Sleep -Milliseconds 100
        $lines = @(Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue)
        $pidLine = $lines | Where-Object { $_ -match '^PID=' } | Select-Object -First 1
        $moduleLine = $lines | Where-Object { $_ -match '^MODULE=' } | Select-Object -First 1
        if ($pidLine -and $moduleLine) {
            $pidValue = [uint32]($pidLine -replace '^PID=', '')
            $moduleValue = [Convert]::ToUInt32(
                ($moduleLine -replace '^MODULE=0x', ''), 16)
        }
    } while (($pidValue -eq 0 -or $moduleValue -eq 0) -and
             (Get-Date) -lt $deadline)
    if ($pidValue -eq 0 -or $moduleValue -eq 0) {
        throw "Launcher did not publish the suspended fixture identity."
    }

    & $launcher --generic-plan-self-test --pid $pidValue `
        --module ("0x{0:X8}" -f $moduleValue)
    if ($LASTEXITCODE -ne 0) {
        throw "Remote Generic Probe Plan self-test failed: $LASTEXITCODE"
    }
    if (-not $launcherProcess.WaitForExit(30000)) {
        throw "Fixture launcher did not exit."
    }
    $launcherErrors = Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue
    if (-not [string]::IsNullOrWhiteSpace($launcherErrors)) {
        throw "Fixture launcher reported an error: $launcherErrors"
    }
    Write-Output "REMOTE_GENERIC_PLAN_COMMAND=PASS"
}
finally {
    Remove-Item -LiteralPath $config -Force -ErrorAction SilentlyContinue
}
