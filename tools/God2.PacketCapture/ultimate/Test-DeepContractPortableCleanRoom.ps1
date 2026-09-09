[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageZip,
    [Parameter(Mandatory)][string]$OutputRoot
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$zipPath=(Resolve-Path -LiteralPath $PackageZip).Path
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$outputPath=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $outputPath){throw "Refusing to overwrite clean-room output: $outputPath"}
[void](New-Item -ItemType Directory -Path $outputPath)
$cleanRoot=Join-Path ([IO.Path]::GetFullPath($env:TEMP)) `
    ('God2DeepValidationCleanRoom-'+[guid]::NewGuid().ToString('N'))
$extractRoot=Join-Path $cleanRoot 'extracted'
[void](New-Item -ItemType Directory -Path $extractRoot -Force)
$cleanZip=Join-Path $cleanRoot 'UltimateDeepContractPromotionValidationPackage.zip'
Copy-Item -LiteralPath $zipPath -Destination $cleanZip
$initialInputs=@(Get-ChildItem -LiteralPath $cleanRoot -File)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($cleanZip,$extractRoot)

$outsideWorkspace=-not $cleanRoot.StartsWith(
    $workspace.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)
$before=@(Get-ChildItem -LiteralPath $cleanRoot -File -Recurse|ForEach-Object{
    $_.FullName.Substring($cleanRoot.Length+1).Replace('\','/')
})
$oldPreflight=[string]$env:GOD2_PORTABLE_PREFLIGHT_ONLY
try{
    $env:GOD2_PORTABLE_PREFLIGHT_ONLY='1'
    $cmd=Join-Path $extractRoot 'Start-Deep-Contract-Promotion-Validation.cmd'
    $output=@(& cmd.exe /d /c ('"'+$cmd+'"') 2>&1|ForEach-Object{[string]$_})
    $exitCode=$LASTEXITCODE
}finally{
    $env:GOD2_PORTABLE_PREFLIGHT_ONLY=$oldPreflight
}
$reports=@(Get-ChildItem -LiteralPath (Join-Path $extractRoot 'Preflight') `
    -Filter 'portable-preflight-*.json' -File -ErrorAction SilentlyContinue|Sort-Object LastWriteTime)
$preflight=if($reports.Count){
    Get-Content -LiteralPath $reports[-1].FullName -Raw -Encoding UTF8|ConvertFrom-Json
}else{$null}
$after=@(Get-ChildItem -LiteralPath $cleanRoot -File -Recurse|ForEach-Object{
    $_.FullName.Substring($cleanRoot.Length+1).Replace('\','/')
})
$unexpected=@($after|Where-Object{
    $_ -notin $before -and $_ -cnotmatch '^extracted/Preflight/portable-preflight-[0-9-]+\.json$'
})
$passed=$outsideWorkspace -and $exitCode -eq 0 -and $null -ne $preflight -and
    [string]$preflight.Status -ceq 'PASS' -and $unexpected.Count -eq 0
$result=[ordered]@{
    SchemaVersion='god2-portable-clean-room-test-v1'
    TestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Status=if($passed){'PASS'}else{'FAIL'}
    CleanRoomPortableTest=if($passed){'PASS'}else{'FAIL'}
    CleanRoomRoot=$cleanRoot;OutsideWorkspace=$outsideWorkspace
    InitialInputCount=$initialInputs.Count
    InitialInput=if($initialInputs.Count -eq 1){$initialInputs[0].Name}else{''}
    CmdExitCode=$exitCode;PreflightStatus=if($preflight){[string]$preflight.Status}else{'MISSING'}
    ManagedFileCount=if($preflight){[int]$preflight.ManagedFileCount}else{0}
    ExternalWorkspaceDependencyCount=if($preflight){[int]$preflight.ExternalWorkspaceDependencyCount}else{-1}
    ExternalRuntimeScriptDependencyCount=if($preflight){[int]$preflight.ExternalRuntimeScriptDependencyCount}else{-1}
    DirectClientLaunchAllowed=if($preflight){[bool]$preflight.DirectClientLaunchAllowed}else{$true}
    LauncherOnly=if($preflight){[bool]$preflight.LauncherOnly}else{$false}
    FunctionStartBreakpointContract=if($preflight){[string]$preflight.FunctionStartBreakpointContract}else{'FAIL'}
    ProducerMetricConsistency=if($preflight){[string]$preflight.ProducerMetricConsistency}else{'FAIL'}
    UnexpectedCreatedFiles=$unexpected;CommandOutput=$output
}
$reportPath=Join-Path $outputPath 'portable-clean-room-test.json'
[IO.File]::WriteAllText($reportPath,(($result|ConvertTo-Json -Depth 12)+"`n"),$utf8)
[pscustomobject]$result
if(-not $passed){exit 5}
