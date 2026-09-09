$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server'
$clientRoot = 'C:\Program Files (x86)\仙界傳II'
$runDir = Join-Path $repo ('Artifacts\ClientInstrumentation\OfficialEvidenceLauncher\correct-path-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$sensitive = Join-Path $runDir 'sensitive'
New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
$buildDir = Join-Path $repo 'Build\ClientInstrumentation\Release'
$launcher = Join-Path $buildDir 'God2ClientTraceLauncher.exe'
$dll = Join-Path $buildDir 'God2ClientTraceProbe.dll'
$attachConfig = Join-Path $buildDir 'God2ClientTraceProbe.attach.env'
$clientExe = Join-Path $clientRoot 'God2_opt.exe'
$sha = (Get-FileHash -LiteralPath $clientExe -Algorithm SHA256).Hash.ToUpperInvariant()
if ($sha -cne '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B') { throw "Client SHA mismatch: $sha" }
Remove-Item -LiteralPath $attachConfig -Force -ErrorAction SilentlyContinue
@(
  "traceDir=$sensitive",
  "generalLog=$(Join-Path $runDir 'general.log')",
  "metadata=$(Join-Path $runDir 'metadata.jsonl')",
  "sessionId=$(Split-Path -Leaf $runDir)",
  'clientBuildVerified=1',
  'clientVersion=1.0.0.1',
  "clientSha256=$sha",
  'enableInline=auto',
  'enableInputInline=1',
  'enableVersionProbe=1',
  'networkOnly=0'
) | Set-Content -LiteralPath $attachConfig -Encoding ASCII
try {
  $output = & $launcher --launch --official-exact-identity --exe $clientExe --cwd $clientRoot --dll $dll --trace-dir $sensitive --general-log (Join-Path $runDir 'general.log') --metadata (Join-Path $runDir 'metadata.jsonl') --post-inject-before-resume-ms 1500 2>&1
  $exit = $LASTEXITCODE
} finally {
  Remove-Item -LiteralPath $attachConfig -Force -ErrorAction SilentlyContinue
}
$pidLine = $output | Where-Object { $_ -match '^PID=' } | Select-Object -First 1
$moduleLine = $output | Where-Object { $_ -match '^MODULE=' } | Select-Object -First 1
$result = [ordered]@{
  exitCode = $exit
  output = @($output)
  pid = if ($pidLine) { [int]($pidLine -replace '^PID=', '') } else { $null }
  module = if ($moduleLine) { ($moduleLine -replace '^MODULE=', '') } else { $null }
  runDir = $runDir
  ctserver = Get-Content -LiteralPath (Join-Path $clientRoot 'ctserver.ini') -Raw
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDir 'launch-result.json') -Encoding UTF8
$result | ConvertTo-Json -Depth 8
if ($exit -ne 0) { exit $exit }
