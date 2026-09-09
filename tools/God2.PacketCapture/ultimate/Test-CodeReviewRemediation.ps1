[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$outputPath=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $outputPath){throw "Refusing to overwrite remediation test output: $outputPath"}
[void](New-Item -ItemType Directory -Path $outputPath)

$officialPath=Join-Path $repoRoot 'tools\God2.ClientInstrumentation\scripts\Invoke-OfficialClientRuntime.ps1'
$modePath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\Invoke-ContractAcquisitionMode.ps1'
$runnerPath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\portable\Invoke-Portable-Deep-Contract-Capture.ps1'
$launcherPath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\portable\PortableLauncherChain.cs'
$packagerPath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\portable\Invoke-PortableDeepContractResultPackager.ps1'
$builderPath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\Build-DeepContractPortablePackage.ps1'
$schemaHelperPath=Join-Path $repoRoot 'tools\God2.PacketCapture\ultimate\portable\PortableJsonSchema.ps1'
$files=@($officialPath,$modePath,$runnerPath,$packagerPath,$builderPath,$schemaHelperPath)
$rows=[Collections.Generic.List[object]]::new()
function Add-Test([string]$Name,[bool]$Passed,[string]$Detail){
    [void]$rows.Add([pscustomobject][ordered]@{Name=$Name;Passed=$Passed;Detail=$Detail})
}

$parseErrors=[Collections.Generic.List[string]]::new()
foreach($file in $files){
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
    foreach($error in $errors){[void]$parseErrors.Add(
        ([IO.Path]::GetFileName($file)+':'+$error.Extent.StartLineNumber+':'+$error.Message))}
}
Add-Test 'powershell.syntax' ($parseErrors.Count -eq 0) ($parseErrors -join ';')

$official=Get-Content -LiteralPath $officialPath -Raw
$mode=Get-Content -LiteralPath $modePath -Raw
$runner=Get-Content -LiteralPath $runnerPath -Raw
$launcher=Get-Content -LiteralPath $launcherPath -Raw
$packager=Get-Content -LiteralPath $packagerPath -Raw
$builder=Get-Content -LiteralPath $builderPath -Raw
Add-Test 'cleanup.no-force-termination' ($official -cnotmatch '\bStop-Process\b' -and
    $official -cmatch 'refusing unsafe forced termination') 'controller is never force-killed'
Add-Test 'cleanup.strict-detach-verification' ($official -cmatch 'attachEstablished' -and
    $official -cmatch 'strictControllerShutdownComplete' -and
    $official -cmatch 'WaitForExit\(90000\)') 'exception cleanup waits and verifies DETACHED strict unload'
Add-Test 'runtime.package-ring-root' ($official -cmatch '\[string\] \$RingSessionRoot' -and
    $runner -cmatch '-RingSessionRoot') 'portable runtime writes its production ring under package State'
Add-Test 'directsound.close-confirmed' ($launcher -cmatch 'IsDirectSoundDialogPresent' -and
    $launcher -cmatch 'ClickDirectSoundDialog' -and $runner -cmatch 'confirmed closed') `
    'formal timing requires observed dialog, click, then window absence'
Add-Test 'abi.zero-hit-invalid' ($mode -cmatch 'ABIEntryEvidenceValid=\(\$entry.Count -gt 0' -and
    $mode -cmatch 'ABIEntryEvidenceValid=\$false') 'zero-hit and static-only rows cannot claim ABI entry evidence'
Add-Test 'result.authority-bound' ($packager -cmatch 'Campaign report authority is not bound' -and
    $packager -cmatch 'invalidObservationAuthority' -and
    $packager -cmatch 'ContractAcquisitionManifestSHA256') 'copied reports bind session, authority, process and manifest hash'
Add-Test 'result.status-evidence-driven' ($packager -cmatch 'Status=\[string\]\$campaignManifest.Status' -and
    $packager -cnotmatch "Status='ULTIMATE_CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_PENDING'") `
    'zero-hit output preserves blocked campaign status'
Add-Test 'json-schema.executed' ($packager -cmatch 'Test-PortableJsonSchemaFile' -and
    $builder -cmatch 'Test-DeepContractPortablePackage.ps1') 'package and result schemas are executable gates'
Add-Test 'build-result-preserved' ($builder -cmatch 'Join-Path \$outputPath ''portable-package-build-result.json''') `
    'build result is written outside disposable staging'

$failed=@($rows|Where-Object{-not $_.Passed})
$result=[ordered]@{
    SchemaVersion='god2-code-review-remediation-test-v1'
    TestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Status=if($failed.Count -eq 0){'PASS'}else{'FAIL'}
    TestCount=$rows.Count;PassedCount=$rows.Count-$failed.Count;FailedCount=$failed.Count
    Rows=@($rows)
}
[IO.File]::WriteAllText((Join-Path $outputPath 'code-review-remediation.json'),
    (($result|ConvertTo-Json -Depth 8)+"`n"),$utf8)
[pscustomobject]$result
if($failed.Count){exit 8}
