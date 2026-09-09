[CmdletBinding()]
param(
    [string]$PackageRoot=$PSScriptRoot,
    [string]$ReportPath=''
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$root=[IO.Path]::GetFullPath($PackageRoot)
$rootPrefix=$root.TrimEnd('\')+'\'
$manifestPath=Join-Path $root 'package-manifest.json'
if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){
    throw 'Portable package manifest is missing.'
}
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$checks=[Collections.Generic.List[object]]::new()
. (Join-Path $root 'PortableJsonSchema.ps1')
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail){
    [void]$checks.Add([pscustomobject][ordered]@{Name=$Name;Passed=$Passed;Detail=$Detail})
}
$reparseEntries=@(Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction Stop|
    Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}|
    ForEach-Object{$_.FullName.Substring($root.Length).TrimStart('\').Replace('\','/')})
Add-Check 'package-tree.no-reparse-points' (
    ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
    $reparseEntries.Count -eq 0) ($reparseEntries -join ',')

Add-Check 'manifest.schema' ([string]$manifest.SchemaVersion -ceq 'god2-deep-contract-portable-package-v2') `
    ([string]$manifest.SchemaVersion)
$manifestSchemaResult=Test-PortableJsonSchemaFile -JsonPath $manifestPath `
    -SchemaPath (Join-Path $root 'schemas\portable-package-manifest.schema.json')
Add-Check 'manifest.json-schema' ([bool]$manifestSchemaResult.Passed) `
    (@($manifestSchemaResult.Errors) -join ',')
Add-Check 'manifest.self-entry-policy' (-not [string]::IsNullOrWhiteSpace(
    [string]$manifest.ManifestSelfEntryPolicy)) ([string]$manifest.ManifestSelfEntryPolicy)
Add-Check 'launcher.only' ([bool]$manifest.LauncherOnly -and
    -not [bool]$manifest.DirectClientLaunchAllowed) 'LauncherOnly=true; DirectClientLaunchAllowed=false'
Add-Check 'manifest.external.workspace.declaration' ([int]$manifest.ExternalWorkspaceDependencyCount -eq 0) `
    "count=$($manifest.ExternalWorkspaceDependencyCount)"
Add-Check 'manifest.external.runtime.declaration' ([int]$manifest.ExternalRuntimeScriptDependencyCount -eq 0) `
    "count=$($manifest.ExternalRuntimeScriptDependencyCount)"

$files=@($manifest.Files)
Add-Check 'manifest.file-count' ($files.Count -eq [int]$manifest.ManagedFileCount -and
    @($files.Path|Sort-Object -Unique).Count -eq $files.Count) "managed=$($manifest.ManagedFileCount); rows=$($files.Count)"
$fileFailures=[Collections.Generic.List[string]]::new()
foreach($file in $files){
    $relative=[string]$file.Path
    if($relative -match '(^|/)(\.\.?)(/|$)' -or [IO.Path]::IsPathRooted($relative)){
        [void]$fileFailures.Add("unsafe:$relative");continue
    }
    $path=[IO.Path]::GetFullPath((Join-Path $root $relative.Replace('/','\')))
    if(-not $path.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not(Test-Path -LiteralPath $path -PathType Leaf)){
        [void]$fileFailures.Add("missing:$relative");continue
    }
    $item=Get-Item -LiteralPath $path
    $cursorPath=$item.FullName
    $reparseFound=$false
    while($cursorPath.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase)){
        $cursorItem=Get-Item -LiteralPath $cursorPath
        if(($cursorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
            $reparseFound=$true;break
        }
        $parentPath=Split-Path -Parent $cursorPath
        if([string]::IsNullOrWhiteSpace($parentPath) -or $parentPath -ceq $cursorPath){break}
        $cursorPath=$parentPath
    }
    if($reparseFound){[void]$fileFailures.Add("reparse:$relative");continue}
    $sha=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if([uint64]$item.Length -ne [uint64]$file.Bytes -or $sha -cne [string]$file.SHA256){
        [void]$fileFailures.Add("identity:$relative")
    }
}
Add-Check 'manifest.file-identities' ($fileFailures.Count -eq 0) ($fileFailures -join ',')
$managedPaths=@($files.Path)
$unmanagedStaticFiles=@(Get-ChildItem -LiteralPath $root -File -Recurse|ForEach-Object{
    $relative=$_.FullName.Substring($root.Length+1).Replace('\','/')
    if($relative -ceq 'package-manifest.json' -or
        $relative -cmatch '^(Preflight|State|Results|Artifacts)/'){return}
    if($managedPaths -cnotcontains $relative){$relative}
})
Add-Check 'manifest.static-files-complete' ($unmanagedStaticFiles.Count -eq 0) `
    ($unmanagedStaticFiles -join ',')

$required=@(
    'God2SemanticRecoveryEngine.exe','Start-Deep-Contract-Promotion-Validation.cmd',
    'Invoke-Portable-Deep-Contract-Capture.ps1','Test-DeepContractPortablePackage.ps1',
    'Invoke-PortableDeepContractResultPackager.ps1','PortableLauncherChain.cs',
    'PortableJsonSchema.ps1',
    'next-evidence-plan.json','baseline/deep-probe-candidate-map-v3.json',
    'baseline/producer-authority-matrix.json',
    'tools/God2.ClientInstrumentation/scripts/Invoke-OfficialClientRuntime.ps1',
    'tools/God2.ClientInstrumentation/scripts/OfficialRuntimeControl.ps1',
    'tools/God2.PacketCapture/ultimate/Invoke-DeepProbeStaticDiscovery.ps1',
    'tools/God2.PacketCapture/ultimate/Invoke-DeepContractEvidenceCampaign.ps1',
    'tools/God2.PacketCapture/ultimate/Invoke-ContractAcquisitionMode.ps1',
    'tools/God2.PacketCapture/ultimate/ContractAcquisitionDebugger.cs',
    'tools/God2.PacketCapture/ultimate/SemanticEventJson.ps1',
    'schemas/next-evidence-plan-v2-portable.schema.json',
    'schemas/portable-package-manifest.schema.json',
    'schemas/portable-result-manifest.schema.json','schemas/entry-breakpoint-report.schema.json')
$missingRequired=@($required|Where-Object{-not(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)})
Add-Check 'runtime.dependencies.complete' ($missingRequired.Count -eq 0) ($missingRequired -join ',')

$externalScriptHits=@(Get-PortableExternalPathReferences -Root $root -Files $files)
Add-Check 'runtime.scripts.package-relative' ($externalScriptHits.Count -eq 0 -and
    [int]$manifest.ExternalRuntimeScriptDependencyCount -eq $externalScriptHits.Count) `
    (@($externalScriptHits|ForEach-Object{$_.Path+':'+$_.Detail}) -join ',')
Add-Check 'external.workspace.dependencies' ($externalScriptHits.Count -eq 0 -and
    [int]$manifest.ExternalWorkspaceDependencyCount -eq $externalScriptHits.Count) `
    (@($externalScriptHits|ForEach-Object{$_.Path+':'+$_.Detail}) -join ',')

$plan=Get-Content -LiteralPath (Join-Path $root 'next-evidence-plan.json') -Raw -Encoding UTF8|ConvertFrom-Json
$planSchemaResult=Test-PortableJsonSchemaFile -JsonPath (Join-Path $root 'next-evidence-plan.json') `
    -SchemaPath (Join-Path $root 'schemas\next-evidence-plan-v2-portable.schema.json')
Add-Check 'evidence-plan.json-schema' ([bool]$planSchemaResult.Passed) `
    (@($planSchemaResult.Errors) -join ',')
$map=Get-Content -LiteralPath (Join-Path $root 'baseline\deep-probe-candidate-map-v3.json') -Raw -Encoding UTF8|ConvertFrom-Json
$planRows=@($plan.Rows)
$entryFailures=[Collections.Generic.List[string]]::new()
foreach($row in $planRows){
    $candidate=@($map.Candidates|Where-Object CandidateId -ceq ([string]$row.CandidateId))
    if($candidate.Count -ne 1 -or [string]$row.CandidateRVA -cne [string]$candidate[0].RVA -or
        [string]$row.FunctionStartRVA -cne [string]$candidate[0].RVA -or
        [string]$row.BreakpointRVA -cne [string]$row.FunctionStartRVA -or
        [string]$row.PreviousInternalProbeRVA -cne [string]$candidate[0].FunctionBoundary.StartRVA -or
        [string]$row.ExpectedFunctionStartBytes -cne [string]$candidate[0].Bytes -or
        [bool]$row.CanaryAllowed){
        [void]$entryFailures.Add([string]$row.CandidateId)
    }
}
$entryContract=[string]$plan.SchemaVersion -ceq 'god2-next-evidence-plan-v2-portable' -and
    $planRows.Count -eq 2 -and $entryFailures.Count -eq 0
Add-Check 'function-start.breakpoint-contract' $entryContract ($entryFailures -join ',')

$producer=Get-Content -LiteralPath (Join-Path $root 'baseline\producer-authority-matrix.json') -Raw -Encoding UTF8|ConvertFrom-Json
$producerConsistent=[int]$producer.CurrentOfficialLiveProducerCount -ge
        [int]$producer.CurrentOfficialLiveVerifiedProducerCount -and
    [int]$producer.CurrentOfficialLiveProducerCount -ge
        [int]$producer.CurrentOfficialLiveDeepProducerCount -and
    [int]$producer.CurrentOfficialLiveDeepProducerCount -ge
        [int]$producer.CurrentOfficialLiveVerifiedDeepProducerCount -and
    [bool]$producer.ProducerMetricConsistency
Add-Check 'producer.metric-consistency' $producerConsistent `
    "all=$($producer.CurrentOfficialLiveProducerCount); verified=$($producer.CurrentOfficialLiveVerifiedProducerCount); deep=$($producer.CurrentOfficialLiveDeepProducerCount); verifiedDeep=$($producer.CurrentOfficialLiveVerifiedDeepProducerCount)"

$engine=Get-Item -LiteralPath (Join-Path $root 'God2SemanticRecoveryEngine.exe')
$engineSha=(Get-FileHash -LiteralPath $engine.FullName -Algorithm SHA256).Hash
Add-Check 'release.identity' ($engine.VersionInfo.FileVersion -ceq '1.3.0.0' -and
    $engineSha -ceq [string]$manifest.ReleaseSHA256) `
    "version=$($engine.VersionInfo.FileVersion); sha256=$engineSha"

$failed=@($checks|Where-Object{-not $_.Passed})
$result=[ordered]@{
    SchemaVersion='god2-portable-clean-room-preflight-v1'
    TestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Status=if($failed.Count -eq 0){'PASS'}else{'FAIL'}
    ManagedFileCount=[int]$manifest.ManagedFileCount
    ExternalWorkspaceDependencyCount=$externalScriptHits.Count
    ExternalRuntimeScriptDependencyCount=$externalScriptHits.Count
    DirectClientLaunchAllowed=[bool]$manifest.DirectClientLaunchAllowed
    LauncherOnly=[bool]$manifest.LauncherOnly
    FunctionStartBreakpointContract=if($entryContract){'PASS'}else{'FAIL'}
    ProducerMetricConsistency=if($producerConsistent){'PASS'}else{'FAIL'}
    ObjectFunctionStartRVA=[string]($planRows|Where-Object Domain -ceq 'Object'|Select-Object -First 1).FunctionStartRVA
    RegistryFunctionStartRVA=[string]($planRows|Where-Object Domain -ceq 'Registry'|Select-Object -First 1).FunctionStartRVA
    ObjectPreviousInternalProbeRVA=[string]($planRows|Where-Object Domain -ceq 'Object'|Select-Object -First 1).PreviousInternalProbeRVA
    RegistryPreviousInternalProbeRVA=[string]($planRows|Where-Object Domain -ceq 'Registry'|Select-Object -First 1).PreviousInternalProbeRVA
    FailureCount=$failed.Count;Checks=@($checks)
}
if(-not [string]::IsNullOrWhiteSpace($ReportPath)){
    $report=[IO.Path]::GetFullPath($ReportPath)
    if(Test-Path -LiteralPath $report){throw "Refusing to overwrite preflight report: $report"}
    [IO.File]::WriteAllText($report,(($result|ConvertTo-Json -Depth 10)+"`n"),$utf8)
}
[pscustomobject]$result
if($failed.Count){exit 4}
