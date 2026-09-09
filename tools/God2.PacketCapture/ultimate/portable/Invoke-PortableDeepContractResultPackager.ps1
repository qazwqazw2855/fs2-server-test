[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$RuntimeRoot,
    [Parameter(Mandatory)][string]$SessionId,
    [Parameter(Mandatory)][string]$ExactTargetIdentityPath,
    [Parameter(Mandatory)][string]$OutputZip
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$packageRootPath=[IO.Path]::GetFullPath($PackageRoot)
$runtimeRootPath=[IO.Path]::GetFullPath($RuntimeRoot)
$packagePrefix=$packageRootPath.TrimEnd('\')+'\'
if($SessionId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$'){
    throw 'Portable result SessionId contains unsupported characters.'
}
if(Test-Path -LiteralPath $OutputZip){throw "Refusing to overwrite result ZIP: $OutputZip"}
$expectedRuntimeRoot=Join-Path $packageRootPath ('Artifacts\ClientInstrumentation\LoginTrial\'+$SessionId)
if(-not $runtimeRootPath.Equals([IO.Path]::GetFullPath($expectedRuntimeRoot),
        [StringComparison]::OrdinalIgnoreCase)){
    throw 'RuntimeRoot is not the current package SessionId runtime directory.'
}
$outputZipPath=[IO.Path]::GetFullPath($OutputZip)
if(-not $outputZipPath.StartsWith((Join-Path $packageRootPath 'Results').TrimEnd('\')+'\',
        [StringComparison]::OrdinalIgnoreCase)){
    throw 'Portable result ZIP must be written under the package Results directory.'
}
$campaignRoot=Join-Path $runtimeRootPath 'analysis\contract-acquisition'
if(-not(Test-Path -LiteralPath $campaignRoot -PathType Container)){
    throw 'Contract acquisition output is missing.'
}
$stage=Join-Path (Split-Path -Parent $OutputZip) ('.result-stage-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $stage)

function Copy-Result([string]$Source,[string]$Name){
    $sourcePath=[IO.Path]::GetFullPath($Source)
    if(-not $sourcePath.StartsWith($packagePrefix,[StringComparison]::OrdinalIgnoreCase) -or
        -not(Test-Path -LiteralPath $sourcePath -PathType Leaf)){
        throw "Required result input is missing or outside the package: $Name"
    }
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $stage $Name)
}
function Write-Json([string]$Name,[object]$Value){
    [IO.File]::WriteAllText((Join-Path $stage $Name),(($Value|ConvertTo-Json -Depth 20)+"`n"),$utf8)
}
function Write-JsonLines([string]$Name,[object[]]$Rows){
    $builder=[Text.StringBuilder]::new()
    foreach($row in $Rows){[void]$builder.AppendLine(($row|ConvertTo-Json -Depth 16 -Compress))}
    [IO.File]::WriteAllText((Join-Path $stage $Name),$builder.ToString(),$utf8)
}

try{
    . (Join-Path $packageRootPath 'PortableJsonSchema.ps1')
    $exactIdentityFullPath=[IO.Path]::GetFullPath($ExactTargetIdentityPath)
    $expectedIdentityPath=[IO.Path]::GetFullPath((Join-Path $packageRootPath `
        ('State\'+$SessionId+'\exact-target-identity.json')))
    if(-not $exactIdentityFullPath.Equals($expectedIdentityPath,[StringComparison]::OrdinalIgnoreCase) -or
        -not(Test-Path -LiteralPath $exactIdentityFullPath -PathType Leaf)){
        throw 'Exact target identity is not the current package State/SessionId identity.'
    }
    $summaryPath=Join-Path $runtimeRootPath 'analysis\official-runtime-summary.json'
    $summary=Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
    $exactIdentity=Get-Content -LiteralPath $exactIdentityFullPath -Raw -Encoding UTF8|ConvertFrom-Json
    $launcherChainPath=Join-Path $runtimeRootPath 'analysis\launcher-client-chain-v1.json'
    $launcherChain=Get-Content -LiteralPath $launcherChainPath -Raw -Encoding UTF8|ConvertFrom-Json
    $healthPath=Join-Path $runtimeRootPath 'analysis\shared-ring-production-health.json'
    $health=Get-Content -LiteralPath $healthPath -Raw -Encoding UTF8|ConvertFrom-Json
    $campaignManifestPath=Join-Path $campaignRoot 'contract-acquisition-campaign-manifest.json'
    $campaignManifest=Get-Content -LiteralPath $campaignManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
    $campaignAuthority=$campaignManifest.Authority
    if([string]$summary.SchemaId -cne 'God2OfficialClientRuntime' -or
        [string]$summary.AuthorityProfile -cne 'CurrentOfficialClientLive' -or
        [string]$summary.SessionId -cne $SessionId -or -not [bool]$summary.Passed -or
        [string]$summary.ClientSHA256 -cne '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B' -or
        [bool]$summary.DirectClientLaunch -or -not [bool]$summary.LauncherParentVerified -or
        [string]$exactIdentity.SessionId -cne $SessionId -or
        [string]$exactIdentity.AuthorityProfile -cne 'CurrentOfficialClientLive' -or
        -not [bool]$exactIdentity.Passed -or
        [uint32]$exactIdentity.ClientProcessId -ne [uint32]$summary.ClientProcessId -or
        [string]$exactIdentity.TargetSHA256 -cne [string]$summary.ClientSHA256 -or
        [string]$launcherChain.ClientSHA256 -cne [string]$summary.ClientSHA256 -or
        [uint32]$launcherChain.ClientProcessId -ne [uint32]$summary.ClientProcessId -or
        -not [bool]$launcherChain.Passed -or [bool]$launcherChain.DirectClientLaunch -or
        -not [bool]$launcherChain.ParentIsVerifiedLauncher -or
        [string]$health.SessionId -cne $SessionId -or
        [string]$health.AuthorityProfile -cne 'CurrentOfficialClientLive' -or
        -not [bool]$health.Passed -or
        [string]$campaignAuthority.SessionId -cne $SessionId -or
        [string]$campaignAuthority.AuthorityProfile -cne 'CurrentOfficialClientLive' -or
        [string]$campaignAuthority.TargetSHA256 -cne [string]$summary.ClientSHA256 -or
        -not [bool]$campaignAuthority.CurrentSessionObserved -or
        [bool]$campaignAuthority.HistoricalEvidenceReused -or [bool]$campaignAuthority.FixtureOnly -or
        [int]$campaignManifest.SelectedCandidateCount -ne 2 -or
        [int]$campaignManifest.ActivationAllowedCount -ne 0 -or
        [int]$campaignManifest.PromotionEligibleCount -ne 0 -or
        [string]$summary.ContractAcquisitionStatus -cne [string]$campaignManifest.Status){
        throw 'Portable result sources are not bound to the same current official session and authority.'
    }
    $campaignManifestSha=(Get-FileHash -LiteralPath $campaignManifestPath -Algorithm SHA256).Hash
    if([string]$summary.ContractAcquisitionManifestSHA256 -cne $campaignManifestSha){
        throw 'Official runtime summary is not bound to the campaign manifest identity.'
    }

    Copy-Result $ExactTargetIdentityPath 'exact-target-identity.json'
    Copy-Result $summaryPath 'official-runtime-summary.json'
    Copy-Result $launcherChainPath 'launcher-client-chain.json'
    Copy-Result $campaignManifestPath 'contract-acquisition-campaign-manifest.json'
    $campaignArtifactNames=@('entry-breakpoint-report.json','candidate-runtime-observations.jsonl',
        'candidate-abi-report.json','candidate-thread-context.json','candidate-reentrancy-report.json',
        'candidate-object-stability.json','candidate-consumer-links.jsonl','candidate-promotion-result.json')
    foreach($name in $campaignArtifactNames){
        $source=Join-Path $campaignRoot $name
        $binding=@($campaignManifest.Artifacts|Where-Object Name -ceq $name)
        if($binding.Count -ne 1 -or [uint64](Get-Item -LiteralPath $source).Length -ne [uint64]$binding[0].Bytes -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne [string]$binding[0].SHA256){
            throw "Campaign artifact identity is not bound by its current-session manifest: $name"
        }
        Copy-Result (Join-Path $campaignRoot $name) $name
    }
    foreach($name in @('entry-breakpoint-report.json','candidate-abi-report.json',
        'candidate-thread-context.json','candidate-reentrancy-report.json',
        'candidate-object-stability.json','candidate-promotion-result.json')){
        $report=Get-Content -LiteralPath (Join-Path $campaignRoot $name) -Raw -Encoding UTF8|ConvertFrom-Json
        $authority=if($null -ne $report.PSObject.Properties['Authority']){$report.Authority}else{$report}
        if([string]$authority.SessionId -cne $SessionId -or
            [string]$authority.AuthorityProfile -cne 'CurrentOfficialClientLive'){
            throw "Campaign report authority is not bound to the current session: $name"
        }
    }
    $entrySchema=Test-PortableJsonSchemaFile `
        -JsonPath (Join-Path $campaignRoot 'entry-breakpoint-report.json') `
        -SchemaPath (Join-Path $packageRootPath 'schemas\entry-breakpoint-report.schema.json')
    if(-not [bool]$entrySchema.Passed){
        throw ('Entry breakpoint report JSON Schema validation failed: '+
            (@($entrySchema.Errors)-join ','))
    }
    Copy-Result $healthPath 'shared-ring-production-health.json'

    $observations=@(Get-Content -LiteralPath (Join-Path $campaignRoot 'candidate-runtime-observations.jsonl')|
        Where-Object{-not[string]::IsNullOrWhiteSpace($_)}|ForEach-Object{$_|ConvertFrom-Json})
    $invalidObservationAuthority=@($observations|Where-Object{
        [string]$_.SessionId -cne $SessionId -or
        [string]$_.AuthorityProfile -cnotin @('CurrentOfficialClientLive','ExactBinaryStaticAnalysis') -or
        [uint32]$_.TargetProcessId -ne [uint32]$summary.ClientProcessId
    })
    if($invalidObservationAuthority.Count -ne 0){
        throw 'One or more runtime observations are not bound to the current official process and session.'
    }
    $live=@($observations|Where-Object RuntimeObservation)
    $groups=@($live|Group-Object CandidateId,ThreadId,InvocationId|ForEach-Object{
        $entry=@($_.Group|Where-Object Phase -ceq 'Entry'|Select-Object -First 1)
        $return=@($_.Group|Where-Object Phase -ceq 'Return'|Select-Object -First 1)
        $caller=@($_.Group|Where-Object Phase -ceq 'ConsumerStep'|Sort-Object ConsumerStepIndex|Select-Object -First 1)
        [pscustomobject][ordered]@{
            CandidateId=[string]$_.Group[0].CandidateId;ThreadId=[uint32]$_.Group[0].ThreadId
            InvocationId=[int64]$_.Group[0].InvocationId
            EntryEventId=if($entry.Count){[string]$entry[0].ObservationId}else{$null}
            ReturnEventId=if($return.Count){[string]$return[0].ObservationId}else{$null}
            CallerEventId=if($caller.Count){[string]$caller[0].ObservationId}else{$null}
            EntryReturnCallerComplete=($entry.Count -eq 1 -and $return.Count -eq 1 -and $caller.Count -eq 1)
            RepeatedCausalObservation=$false;PromotionEligible=$false
        }
    })
    Write-Json 'causal-observation-groups.json' ([ordered]@{
        SchemaVersion='god2-portable-causal-observation-groups-v1';SessionId=$SessionId
        AuthorityProfile='CurrentOfficialClientLive';GroupCount=$groups.Count
        CompleteCorrelationCount=@($groups|Where-Object EntryReturnCallerComplete).Count
        RepeatedCausalVerifiedCount=0;Rows=$groups
    })
    $typed=@($live|Group-Object CandidateId|ForEach-Object{
        [pscustomobject][ordered]@{
            CandidateId=[string]$_.Name;RuntimeValueType='REGISTER_AND_BOUNDED_STACK_OBSERVED_UNVERIFIED'
            Source='EntryReturnCallerObservation';Consumer='UNVERIFIED'
            ObjectCandidate='UNVERIFIED';ObservationCount=$_.Count
            EvidenceRefs=@($_.Group.ObservationId);TypedRuntimeEvidenceVerified=$false
            PromotionEligible=$false;SessionId=$SessionId;AuthorityProfile='CurrentOfficialClientLive'
        }
    })
    Write-JsonLines 'typed-runtime-evidence.jsonl' $typed

    $promotion=Get-Content -LiteralPath (Join-Path $campaignRoot 'candidate-promotion-result.json') -Raw|ConvertFrom-Json
    $ledger=@($promotion.Rows|ForEach-Object{
        $candidateId=[string]$_.CandidateId
        [pscustomobject][ordered]@{
            CandidateId=$candidateId;Domain=[string]$_.Domain;Gate=[string]$_.Gate
            PreviousStatus='EVIDENCE_BLOCKED'
            CurrentStatus=if([bool]$_.Passed){'PASS'}else{'EVIDENCE_BLOCKED'}
            AuthorityProfile='CurrentOfficialClientLive';SessionId=$SessionId
            EvidenceRefs=@($_.EvidenceRefs);ObservationCount=@($live|Where-Object CandidateId -ceq $candidateId).Count
            Contradictions=@();Decision=if([bool]$_.Passed){
                'STATIC_EXACT_BUILD_GATE_ONLY'
            }else{'LIVE_EVIDENCE_STILL_REQUIRED'}
        }
    })
    Write-JsonLines 'deep-probe-promotion-ledger.jsonl' $ledger

    $plan=Get-Content -LiteralPath (Join-Path $packageRootPath 'next-evidence-plan.json') -Raw|ConvertFrom-Json
    $entryReport=Get-Content -LiteralPath (Join-Path $campaignRoot 'entry-breakpoint-report.json') -Raw|ConvertFrom-Json
    Write-Json 'next-evidence-plan-result.json' ([ordered]@{
        SchemaVersion='god2-next-evidence-plan-result-v1';SessionId=$SessionId
        AuthorityProfile='CurrentOfficialClientLive';SourcePlanSHA256=(Get-FileHash `
            (Join-Path $packageRootPath 'next-evidence-plan.json') -Algorithm SHA256).Hash
        FunctionStartBreakpointContractPassed=[bool]$entryReport.FunctionStartBreakpointContractPassed
        EntryReturnCallerCorrelationCount=[int]$entryReport.CompleteCorrelationCount
        PromotionEligibleCount=0;ActivationAllowedCount=0
        Rows=@($plan.Rows|ForEach-Object{
            $id=[string]$_.CandidateId
            [pscustomobject][ordered]@{
                CandidateId=$id;FunctionStartRVA=[string]$_.FunctionStartRVA
                BreakpointRVA=[string]$_.BreakpointRVA
                RuntimeObservationCount=@($live|Where-Object CandidateId -ceq $id).Count
                MissingGates=@($_.MissingGates);PromotionEligible=$false
            }
        });Status=[string]$campaignManifest.Status
    })

    $inventory=@(Get-ChildItem -LiteralPath $stage -File|Sort-Object Name|ForEach-Object{
        [pscustomobject][ordered]@{
            Path=$_.Name;Bytes=[uint64]$_.Length
            SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            SessionId=$SessionId;AuthorityProfile='CurrentOfficialClientLive'
        }
    })
    $manifest=[ordered]@{
        SchemaVersion='god2-deep-contract-portable-result-v1'
        ManifestSelfEntryPolicy='result-manifest.json is present in the ZIP and excluded from Files because a recursive self-hash is undefined.'
        GeneratedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');SessionId=$SessionId
        AuthorityProfile='CurrentOfficialClientLive';ManagedFileCount=$inventory.Count
        PromotionEligibleCount=0;ActivationAllowedCount=0;ConfirmedDeepContractCount=0
        VerifiedFundamentalDomainCount=0;Files=$inventory
    }
    Write-Json 'result-manifest.json' $manifest
    $resultSchema=Test-PortableJsonSchemaFile -JsonPath (Join-Path $stage 'result-manifest.json') `
        -SchemaPath (Join-Path $packageRootPath 'schemas\portable-result-manifest.schema.json')
    if(-not [bool]$resultSchema.Passed){
        throw ('Portable result manifest JSON Schema validation failed: '+
            (@($resultSchema.Errors)-join ','))
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$OutputZip,
        [IO.Compression.CompressionLevel]::Optimal,$false)
    [pscustomobject][ordered]@{
        Passed=$true;SessionId=$SessionId;ResultZip=[IO.Path]::GetFullPath($OutputZip)
        ResultZipBytes=[uint64](Get-Item $OutputZip).Length
        ResultZipSHA256=(Get-FileHash $OutputZip -Algorithm SHA256).Hash
        ManagedFileCount=$inventory.Count
    }
}finally{
    if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
}
