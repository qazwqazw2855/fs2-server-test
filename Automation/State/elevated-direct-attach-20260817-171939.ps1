$ErrorActionPreference='Stop'
Set-Location 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server'
$targetPid=19904
$attempt=[int](Get-Date -Format 'HHmmss')
$traceRoot=Join-Path 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server' ("Artifacts\ClientInstrumentation\OfficialEvidenceLauncher\live-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-19904")
$sensitive=Join-Path $traceRoot 'sensitive'
New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
$buildDir=Join-Path 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server' 'Build\ClientInstrumentation\Release'
$launcher=Join-Path $buildDir 'God2ClientTraceLauncher.exe'
$sourceDll=Join-Path $buildDir 'God2ClientTraceProbe.dll'
$probeDll=Join-Path $traceRoot "God2LoginTraceProbe-$attempt.dll"
Copy-Item -LiteralPath $sourceDll -Destination $probeDll -Force
$targetPath='C:\Users\SeiHo\Desktop\XJZ2\God2_opt.exe'
$targetSha256=(Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToUpperInvariant()
$targetVersion=[string](Get-Item -LiteralPath $targetPath).VersionInfo.FileVersion
$proc=Get-Process -Id $targetPid -ErrorAction Stop
$creation=$proc.StartTime.ToUniversalTime().ToFileTimeUtc()
@(
  "traceDir=$sensitive",
  "generalLog=$(Join-Path $traceRoot 'general.log')",
  "metadata=$(Join-Path $traceRoot 'metadata.jsonl')",
  'clientBuildVerified=true',
  "clientVersion=$targetVersion",
  "clientSha256=$targetSha256",
  "clientProcessId=$targetPid",
  "clientProcessCreationTime=$creation",
  'enableInline=true',
  'enableInputInline=false',
  'enableVersionProbe=true'
) | Set-Content -LiteralPath (Join-Path $traceRoot 'God2ClientTraceProbe.attach.env') -Encoding ASCII
$output=& $launcher --attach --pid $targetPid --dll $probeDll 2>&1
$exit=$LASTEXITCODE
[pscustomobject]@{exitCode=$exit;output=@($output);traceRoot=$traceRoot;pid=$targetPid} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Automation\State\elevated-direct-attach-20260817-171939.stdout.log' -Encoding UTF8
if($exit -ne 0){ $output | Set-Content -LiteralPath 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Automation\State\elevated-direct-attach-20260817-171939.stderr.log' -Encoding UTF8; exit $exit }
