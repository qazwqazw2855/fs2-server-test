$ErrorActionPreference='Stop'
Set-Location 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server'
$targetPid=396
$traceRoot=Join-Path 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server' ('Artifacts\ClientInstrumentation\OfficialEvidenceLauncher\fast-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-'+$targetPid)
$sensitive=Join-Path $traceRoot 'sensitive'
New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
$buildDir=Join-Path 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server' 'Build\ClientInstrumentation\Release'
$launcher=Join-Path $buildDir 'God2ClientTraceLauncher.exe'
$probeDll=Join-Path $traceRoot 'God2FastTraceProbe.dll'
Copy-Item -LiteralPath (Join-Path $buildDir 'God2ClientTraceProbe.dll') -Destination $probeDll -Force
$targetPath='C:\Users\SeiHo\Desktop\XJZ2\God2_opt.exe'
$sha=(Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToUpperInvariant()
$proc=Get-Process -Id $targetPid -ErrorAction Stop
$creation=$proc.StartTime.ToUniversalTime().ToFileTimeUtc()
@("traceDir=$sensitive","generalLog=$(Join-Path $traceRoot 'general.log')","metadata=$(Join-Path $traceRoot 'metadata.jsonl')",'clientBuildVerified=true',"clientSha256=$sha","clientProcessId=$targetPid","clientProcessCreationTime=$creation",'enableInline=true','enableInputInline=false','enableVersionProbe=true') | Set-Content -LiteralPath (Join-Path $traceRoot 'God2ClientTraceProbe.attach.env') -Encoding ASCII
$output=& $launcher --attach --pid $targetPid --dll $probeDll 2>&1
[pscustomobject]@{exit=$LASTEXITCODE;pid=$targetPid;traceRoot=$traceRoot;output=@($output)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\Users\SeiHo\Desktop\Simao\God2\God2 Classic Server\Automation\State\fast-direct-attach.out.json' -Encoding UTF8
exit $LASTEXITCODE
