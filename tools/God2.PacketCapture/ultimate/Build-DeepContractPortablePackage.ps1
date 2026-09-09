[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$BaselineClosureRoot,
    [Parameter(Mandatory)][string]$CandidateMapPath,
    [string]$ReleaseExecutable='',
    [string]$PackageName='UltimateDeepContractPromotionValidationPackage.zip'
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$scriptRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptRoot))
if([string]::IsNullOrWhiteSpace($ReleaseExecutable)){
    $ReleaseExecutable=Join-Path $repoRoot 'artifacts\release\God2SemanticRecoveryEngine.exe'
}
$releasePath=(Resolve-Path -LiteralPath $ReleaseExecutable).Path
$closureRoot=(Resolve-Path -LiteralPath $BaselineClosureRoot).Path
$mapPath=(Resolve-Path -LiteralPath $CandidateMapPath).Path
$outputPath=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $outputPath){throw "Refusing to overwrite package output: $outputPath"}
[void](New-Item -ItemType Directory -Path $outputPath)
$stage=Join-Path $outputPath '.package-stage'
[void](New-Item -ItemType Directory -Path $stage)

function Copy-PackageFile([string]$Source,[string]$RelativePath){
    $sourcePath=(Resolve-Path -LiteralPath $Source).Path
    $destination=Join-Path $stage $RelativePath
    $parent=Split-Path -Parent $destination
    if(-not(Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent -Force)}
    Copy-Item -LiteralPath $sourcePath -Destination $destination
}
function Write-PackageJson([string]$RelativePath,[object]$Value){
    $destination=Join-Path $stage $RelativePath
    $parent=Split-Path -Parent $destination
    if(-not(Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent -Force)}
    [IO.File]::WriteAllText($destination,(($Value|ConvertTo-Json -Depth 20)+"`n"),$utf8)
}

try{
    $portableSource=Join-Path $scriptRoot 'portable'
    foreach($name in @('Start-Deep-Contract-Promotion-Validation.cmd',
        'Invoke-Portable-Deep-Contract-Capture.ps1','Test-DeepContractPortablePackage.ps1',
        'Invoke-PortableDeepContractResultPackager.ps1','PortableLauncherChain.cs',
        'PortableJsonSchema.ps1')){
        Copy-PackageFile (Join-Path $portableSource $name) $name
    }
    Copy-PackageFile $releasePath 'God2SemanticRecoveryEngine.exe'

    $runtimeFiles=[ordered]@{
        'tools\God2.ClientInstrumentation\scripts\Invoke-OfficialClientRuntime.ps1'=
            'tools\God2.ClientInstrumentation\scripts\Invoke-OfficialClientRuntime.ps1'
        'tools\God2.ClientInstrumentation\scripts\OfficialRuntimeControl.ps1'=
            'tools\God2.ClientInstrumentation\scripts\OfficialRuntimeControl.ps1'
        'tools\God2.PacketCapture\ultimate\Invoke-DeepProbeStaticDiscovery.ps1'=
            'tools\God2.PacketCapture\ultimate\Invoke-DeepProbeStaticDiscovery.ps1'
        'tools\God2.PacketCapture\ultimate\Invoke-DeepContractEvidenceCampaign.ps1'=
            'tools\God2.PacketCapture\ultimate\Invoke-DeepContractEvidenceCampaign.ps1'
        'tools\God2.PacketCapture\ultimate\Invoke-ContractAcquisitionMode.ps1'=
            'tools\God2.PacketCapture\ultimate\Invoke-ContractAcquisitionMode.ps1'
        'tools\God2.PacketCapture\ultimate\ContractAcquisitionDebugger.cs'=
            'tools\God2.PacketCapture\ultimate\ContractAcquisitionDebugger.cs'
        'tools\God2.PacketCapture\ultimate\SemanticEventJson.ps1'=
            'tools\God2.PacketCapture\ultimate\SemanticEventJson.ps1'
    }
    foreach($entry in $runtimeFiles.GetEnumerator()){
        Copy-PackageFile (Join-Path $repoRoot $entry.Key) $entry.Value
    }
    foreach($name in @('next-evidence-plan-v2-portable.schema.json',
        'portable-package-manifest.schema.json','portable-result-manifest.schema.json',
        'entry-breakpoint-report.schema.json')){
        Copy-PackageFile (Join-Path $portableSource ('schemas\'+$name)) ('schemas\'+$name)
    }

    Copy-PackageFile $mapPath 'baseline\deep-probe-candidate-map-v3.json'
    Copy-PackageFile (Join-Path $closureRoot 'producer-authority-matrix.json') `
        'baseline\producer-authority-matrix.json'
    Copy-PackageFile (Join-Path $closureRoot 'deep-contract-promotion-closure-result.json') `
        'baseline\deep-contract-promotion-closure-result.json'

    $map=Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8|ConvertFrom-Json
    if([string]$map.TargetSHA256 -cne '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B' -or
        [string]$map.TargetVersion -cne '1.0.0.1' -or [string]$map.Architecture -cne 'x86'){
        throw 'Candidate map is not bound to the exact official x86 client.'
    }
    $targets=[ordered]@{
        'Object-0034D980-00'='Object'
        'Registry-0034E480-00'='Registry'
    }
    $rows=@()
    foreach($target in $targets.GetEnumerator()){
        $candidate=@($map.Candidates|Where-Object CandidateId -ceq $target.Key)
        if($candidate.Count -ne 1 -or [string]$candidate[0].Domain -cne $target.Value -or
            @($candidate[0].CallsiteRVAs).Count -eq 0){
            throw "Exact candidate-map function entry is missing or unreferenced: $($target.Key)"
        }
        $rows += [pscustomobject][ordered]@{
            CandidateId=$target.Key;Domain=$target.Value;CandidateRVA=[string]$candidate[0].RVA
            FunctionStartRVA=[string]$candidate[0].RVA;BreakpointRVA=[string]$candidate[0].RVA
            PreviousInternalProbeRVA=[string]$candidate[0].FunctionBoundary.StartRVA
            FunctionStartBasis='DIRECT_CALL_TARGET_EXACT_BUILD'
            ExpectedFunctionStartBytes=[string]$candidate[0].Bytes
            CandidateMapCallsiteCount=@($candidate[0].CallsiteRVAs).Count
            CandidateMapBoundaryStatus=[string]$candidate[0].FunctionBoundary.Status
            ABIEntryEvidenceValidRequired=$true;CanaryAllowed=$false
            MissingGates=@('CallingConventionVerified','ArgumentContractVerified',
                'ReturnValueLifetimeVerified','ThreadContextVerified','ReentrancyRiskVerified',
                'StableObjectOrContext','VerifiedConsumerOrMutation','RepeatedCausalObservation',
                'TypedRuntimeEvidence','ContradictionsResolved')
        }
    }
    $plan=[ordered]@{
        SchemaVersion='god2-next-evidence-plan-v2-portable'
        GeneratedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        Authority=[ordered]@{
            AuthorityProfile='CurrentOfficialClientLive';TargetExecutable='God2_opt.exe'
            TargetArchitecture='x86';TargetVersion='1.0.0.1'
            TargetSHA256='6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
            CandidateMapSHA256=(Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash
            FreshCandidateMapRequired=$true;CurrentSessionObserved=$true
            HistoricalEvidenceReused=$false
            FixtureOnly=$false;PromotionEligible=$false
        }
        EvidenceDrivenRotation=$true
        MaximumConcurrentCandidateBreakpoints=2
        SelectedCandidateCount=2
        CandidateDomains=@('Object','Registry')
        SingleDebuggerAttachDetach=$true
        EvidencePhases=@('TrueFunctionEntryAndReturn','CallerCleanupAndArgumentShape',
            'ReturnLifetimeAndConsumer','ObjectStabilityAndCausalReplay')
        RequiredCorrelationFields=@('EntryEventId','ReturnEventId','CallerEventId','ThreadId')
        Rows=$rows;PromotionEligibleCount=0;ActivationAllowedCount=0
        ConfirmedDeepContractCount=0;VerifiedFundamentalDomainCount=0
        Status='READY_FOR_EXTERNAL_LIVE_EVIDENCE'
    }
    Write-PackageJson 'next-evidence-plan.json' $plan

    $release=Get-Item -LiteralPath $releasePath
    $inventory=@(Get-ChildItem -LiteralPath $stage -File -Recurse|Sort-Object FullName|ForEach-Object{
        [pscustomobject][ordered]@{
            Path=$_.FullName.Substring($stage.Length+1).Replace('\','/')
            Bytes=[uint64]$_.Length
            SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    . (Join-Path $stage 'PortableJsonSchema.ps1')
    $externalScriptHits=@(Get-PortableExternalPathReferences -Root $stage -Files $inventory)
    if($externalScriptHits.Count -ne 0){
        throw ('Portable runtime contains external workspace dependencies: '+
            (@($externalScriptHits|ForEach-Object{$_.Path+':'+$_.Detail})-join ','))
    }
    $manifest=[ordered]@{
        SchemaVersion='god2-deep-contract-portable-package-v2'
        ManifestSelfEntryPolicy='package-manifest.json is present in the ZIP and excluded from Files because a recursive self-hash is undefined.'
        GeneratedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        ReleaseVersion=$release.VersionInfo.FileVersion;ReleaseBytes=[uint64]$release.Length
        ReleaseSHA256=(Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
        LauncherOnly=$true;DirectClientLaunchAllowed=$false
        ExternalWorkspaceDependencyCount=$externalScriptHits.Count
        ExternalRuntimeScriptDependencyCount=$externalScriptHits.Count
        RuntimePathPolicy='$PSScriptRoot_PACKAGE_RELATIVE_ONLY'
        ManagedFileCount=$inventory.Count;Files=$inventory
    }
    Write-PackageJson 'package-manifest.json' $manifest

    $validatorResult=& (Join-Path $stage 'Test-DeepContractPortablePackage.ps1') -PackageRoot $stage
    if([string]$validatorResult.Status -cne 'PASS'){
        $failureDetail=@($validatorResult.Checks|Where-Object{-not $_.Passed}|ForEach-Object{
            [string]$_.Name+': '+[string]$_.Detail
        }) -join ' | '
        throw "Staged portable package validation failed: $failureDetail"
    }
    $zipPath=Join-Path $outputPath $PackageName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zipPath,
        [IO.Compression.CompressionLevel]::Optimal,$false)
    $result=[ordered]@{
        Status='PASS';PackagePath=[IO.Path]::GetFullPath($zipPath)
        PackageBytes=[uint64](Get-Item -LiteralPath $zipPath).Length
        PackageSHA256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        ManagedFileCount=$inventory.Count
        ExternalWorkspaceDependencyCount=$externalScriptHits.Count
        ExternalRuntimeScriptDependencyCount=$externalScriptHits.Count
        DirectClientLaunchAllowed=$false;LauncherOnly=$true
        ObjectFunctionStartRVA=[string]($rows|Where-Object Domain -ceq 'Object').FunctionStartRVA
        RegistryFunctionStartRVA=[string]($rows|Where-Object Domain -ceq 'Registry').FunctionStartRVA
        ObjectPreviousInternalProbeRVA=[string]($rows|Where-Object Domain -ceq 'Object').PreviousInternalProbeRVA
        RegistryPreviousInternalProbeRVA=[string]($rows|Where-Object Domain -ceq 'Registry').PreviousInternalProbeRVA
        FunctionStartBreakpointContract='PASS';ProducerMetricConsistency='PASS'
    }
    $buildResultPath=Join-Path $outputPath 'portable-package-build-result.json'
    [IO.File]::WriteAllText($buildResultPath,(($result|ConvertTo-Json -Depth 12)+"`n"),$utf8)
    [pscustomobject]$result
}finally{
    if(Test-Path -LiteralPath $stage){
        $resolvedStage=[IO.Path]::GetFullPath($stage)
        if($resolvedStage.StartsWith($outputPath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}
