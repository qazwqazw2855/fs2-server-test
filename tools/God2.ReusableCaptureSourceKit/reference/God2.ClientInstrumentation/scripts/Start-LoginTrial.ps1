param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir,
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$manifest = Get-Content -Raw -LiteralPath (Join-Path $runDir "source-manifest.json") | ConvertFrom-Json
$buildDir = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LoginTrial\build\$Configuration"
$launcher = Join-Path $buildDir "God2ClientTraceLauncher.exe"
$dll = Join-Path $buildDir "God2ClientTraceProbe.dll"
$attachConfig = Join-Path $buildDir "God2ClientTraceProbe.attach.env"
if (-not (Test-Path -LiteralPath $launcher) -or -not (Test-Path -LiteralPath $dll)) {
    throw "Instrumentation build outputs are missing for $Configuration."
}
if ([string]$manifest.client.fileName -cne "God2_opt.exe" -or
    [string]$manifest.client.architecture -cne "x86" -or
    [string]$manifest.client.sha256 -cne "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B" -or
    (Get-FileHash -LiteralPath $manifest.client.exePath -Algorithm SHA256).Hash -cne
        [string]$manifest.client.sha256) {
    throw "The prepared login trial is not the exact official client identity."
}
if (Test-Path -LiteralPath $attachConfig) {
    throw "Attach configuration is already present; another instrumentation run may be active."
}

$sensitive = Join-Path $runDir "sensitive"
$generalLog = Join-Path $runDir "general.log"
$metadata = Join-Path $runDir "metadata.jsonl"
New-Item -ItemType Directory -Force -Path $sensitive | Out-Null

@(
    "traceDir=$sensitive"
    "generalLog=$generalLog"
    "metadata=$metadata"
    "sessionId=$([IO.Path]::GetFileName($runDir))"
    "clientBuildVerified=1"
    "clientVersion=1.0.0.1"
    "clientSha256=$($manifest.client.sha256)"
    "enableInline=auto"
    "enableInputInline=1"
    "enableVersionProbe=1"
    "networkOnly=0"
) | Set-Content -LiteralPath $attachConfig -Encoding ASCII
try {
    $output = & $launcher --launch --official-exact-identity `
        --exe $manifest.client.exePath --cwd $manifest.client.root --dll $dll `
        --trace-dir $sensitive --general-log $generalLog --metadata $metadata `
        --post-inject-before-resume-ms 1500
} finally {
    Remove-Item -LiteralPath $attachConfig -Force -ErrorAction SilentlyContinue
}
if ($LASTEXITCODE -ne 0) {
    throw "Client instrumentation launcher failed with exit code $LASTEXITCODE. Output: $output"
}

$pidLine = $output | Where-Object { $_ -match '^PID=' } | Select-Object -First 1
$moduleLine = $output | Where-Object { $_ -match '^MODULE=' } | Select-Object -First 1
$pidValue = [int]($pidLine -replace '^PID=', '')
$moduleValue = ($moduleLine -replace '^MODULE=', '')

$active = [ordered]@{
    runDir = $runDir
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    pid = $pidValue
    module = $moduleValue
    configuration = $Configuration
    launcher = $launcher
    dll = $dll
    generalLog = $generalLog
    metadata = $metadata
    trace = (Join-Path $sensitive "trace.bin")
}
$active | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir "active-run.json") -Encoding UTF8
$active | ConvertTo-Json -Depth 5
