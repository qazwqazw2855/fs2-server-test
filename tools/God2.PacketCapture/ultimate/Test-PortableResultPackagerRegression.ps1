[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageZip,
    [Parameter(Mandatory)][string]$OutputRoot
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$utf8=[Text.UTF8Encoding]::new($false,$true)
$zipPath=(Resolve-Path -LiteralPath $PackageZip).Path
$outputPath=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $outputPath){throw "Refusing to overwrite packager regression output: $outputPath"}
[void](New-Item -ItemType Directory -Path $outputPath)
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-JsonFile([string]$Path,[object]$Value){
    $parent=Split-Path -Parent $Path
    if(-not(Test-Path -LiteralPath $parent)){[void](New-Item -ItemType Directory -Path $parent -Force)}
    [IO.File]::WriteAllText($Path,(($Value|ConvertTo-Json -Depth 20)+"`n"),$utf8)
}

function New-PackagerFixture([string]$Name){
    $root=Join-Path $outputPath $Name
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath,$root)
    $sessionId='portable-packager-fixture'
    $clientPid=4242
    $runtimeRoot=Join-Path $root ('Artifacts\ClientInstrumentation\LoginTrial\'+$sessionId)
    $analysis=Join-Path $runtimeRoot 'analysis'
    $campaign=Join-Path $analysis 'contract-acquisition'
    $state=Join-Path $root ('State\'+$sessionId)
    $results=Join-Path $root 'Results'
    [void](New-Item -ItemType Directory -Path $campaign,$state,$results -Force)
    $authority=[ordered]@{
        AuthorityProfile='CurrentOfficialClientLive';SessionId=$sessionId
        TargetSHA256='6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
        CurrentSessionObserved=$true;HistoricalEvidenceReused=$false;FixtureOnly=$false
    }
    $reportNames=@('entry-breakpoint-report.json','candidate-abi-report.json',
        'candidate-thread-context.json','candidate-reentrancy-report.json',
        'candidate-object-stability.json')
    foreach($reportName in $reportNames){
        $value=[ordered]@{SchemaVersion='fixture-v1';Authority=$authority;Rows=@()}
        if($reportName -ceq 'entry-breakpoint-report.json'){
            $value.SchemaVersion='god2-entry-breakpoint-report-v1'
            $value.CandidateCount=2
            $value.FunctionStartBreakpointContractPassed=$true
            $value.ABIEntryEvidenceValidCount=0
            $value.CompleteCorrelationCount=0
            $value.CallingConventionVerifiedCount=0
            $value.ArgumentContractVerifiedCount=0
            $value.ReturnValueLifetimeVerifiedCount=0
            $value.Rows=@(
                [pscustomobject]@{CandidateId='Object-0034D980-00'},
                [pscustomobject]@{CandidateId='Registry-0034E480-00'})
        }
        Write-JsonFile (Join-Path $campaign $reportName) $value
    }
    Write-JsonFile (Join-Path $campaign 'candidate-promotion-result.json') ([ordered]@{
        SchemaVersion='fixture-v1';Authority=$authority;Rows=@()
    })
    $observation=[ordered]@{
        ObservationId=[guid]::NewGuid().ToString('D');RuntimeObservation=$false
        SessionId=$sessionId;AuthorityProfile='ExactBinaryStaticAnalysis'
        TargetProcessId=$clientPid;CandidateId='Object-0034D980-00';Domain='Object'
    }
    [IO.File]::WriteAllText((Join-Path $campaign 'candidate-runtime-observations.jsonl'),
        (($observation|ConvertTo-Json -Compress)+"`n"),$utf8)
    [IO.File]::WriteAllText((Join-Path $campaign 'candidate-consumer-links.jsonl'),"{}`n",$utf8)
    $artifactNames=@('entry-breakpoint-report.json','candidate-runtime-observations.jsonl',
        'candidate-abi-report.json','candidate-thread-context.json','candidate-reentrancy-report.json',
        'candidate-object-stability.json','candidate-consumer-links.jsonl','candidate-promotion-result.json')
    $artifacts=@($artifactNames|ForEach-Object{
        $path=Join-Path $campaign $_
        [pscustomobject][ordered]@{Name=$_;Bytes=[uint64](Get-Item $path).Length
            SHA256=(Get-FileHash $path -Algorithm SHA256).Hash}
    })
    $campaignManifest=[ordered]@{
        SchemaVersion='god2-contract-acquisition-campaign-manifest-v2'
        Authority=$authority;SelectedCandidateCount=2;ActivationAllowedCount=0
        PromotionEligibleCount=0;RuntimeObservationCount=0
        Status='EVIDENCE_BLOCKED_CANDIDATES_NOT_TRIGGERED_IN_SESSION';Artifacts=$artifacts
    }
    $campaignManifestPath=Join-Path $campaign 'contract-acquisition-campaign-manifest.json'
    Write-JsonFile $campaignManifestPath $campaignManifest
    Write-JsonFile (Join-Path $state 'exact-target-identity.json') ([ordered]@{
        SessionId=$sessionId;AuthorityProfile='CurrentOfficialClientLive';Passed=$true
        ClientProcessId=$clientPid;TargetSHA256=$authority.TargetSHA256
    })
    Write-JsonFile (Join-Path $analysis 'launcher-client-chain-v1.json') ([ordered]@{
        Passed=$true;DirectClientLaunch=$false;ParentIsVerifiedLauncher=$true
        ClientProcessId=$clientPid;ClientSHA256=$authority.TargetSHA256
    })
    Write-JsonFile (Join-Path $analysis 'shared-ring-production-health.json') ([ordered]@{
        SessionId=$sessionId;AuthorityProfile='CurrentOfficialClientLive';Passed=$true
    })
    Write-JsonFile (Join-Path $analysis 'official-runtime-summary.json') ([ordered]@{
        SchemaId='God2OfficialClientRuntime';AuthorityProfile='CurrentOfficialClientLive'
        SessionId=$sessionId;Passed=$true;ClientSHA256=$authority.TargetSHA256
        ClientProcessId=$clientPid;DirectClientLaunch=$false;LauncherParentVerified=$true
        ContractAcquisitionStatus=$campaignManifest.Status
        ContractAcquisitionManifestSHA256=(Get-FileHash $campaignManifestPath -Algorithm SHA256).Hash
    })
    [pscustomobject][ordered]@{
        Root=$root;SessionId=$sessionId;RuntimeRoot=$runtimeRoot;CampaignRoot=$campaign
        IdentityPath=(Join-Path $state 'exact-target-identity.json')
        OutputZip=(Join-Path $results ('God2DeepContractPromotionResult_'+$sessionId+'.zip'))
    }
}

$rows=[Collections.Generic.List[object]]::new()
$positive=New-PackagerFixture 'p'
$packager=Join-Path $positive.Root 'Invoke-PortableDeepContractResultPackager.ps1'
$positiveResult=& $packager -PackageRoot $positive.Root -RuntimeRoot $positive.RuntimeRoot `
    -SessionId $positive.SessionId -ExactTargetIdentityPath $positive.IdentityPath `
    -OutputZip $positive.OutputZip
[void]$rows.Add([pscustomobject][ordered]@{
    Name='positive.zero-hit-fail-closed-result';Passed=([bool]$positiveResult.Passed -and
        (Test-Path -LiteralPath $positive.OutputZip -PathType Leaf))
})

$negative=New-PackagerFixture 'n'
$abiPath=Join-Path $negative.CampaignRoot 'candidate-abi-report.json'
$abi=Get-Content -LiteralPath $abiPath -Raw -Encoding UTF8|ConvertFrom-Json
$abi.Authority.SessionId='stale-session'
Write-JsonFile $abiPath $abi
$campaignManifestPath=Join-Path $negative.CampaignRoot 'contract-acquisition-campaign-manifest.json'
$campaignManifest=Get-Content -LiteralPath $campaignManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$binding=@($campaignManifest.Artifacts|Where-Object Name -ceq 'candidate-abi-report.json')[0]
$binding.Bytes=[uint64](Get-Item $abiPath).Length
$binding.SHA256=(Get-FileHash $abiPath -Algorithm SHA256).Hash
Write-JsonFile $campaignManifestPath $campaignManifest
$summaryPath=Join-Path $negative.RuntimeRoot 'analysis\official-runtime-summary.json'
$summary=Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
$summary.ContractAcquisitionManifestSHA256=(Get-FileHash $campaignManifestPath -Algorithm SHA256).Hash
Write-JsonFile $summaryPath $summary
$negativeRejected=$false
try{
    & (Join-Path $negative.Root 'Invoke-PortableDeepContractResultPackager.ps1') `
        -PackageRoot $negative.Root -RuntimeRoot $negative.RuntimeRoot `
        -SessionId $negative.SessionId -ExactTargetIdentityPath $negative.IdentityPath `
        -OutputZip $negative.OutputZip | Out-Null
}catch{
    $negativeRejected=$_.Exception.Message -like '*authority is not bound*'
}
[void]$rows.Add([pscustomobject][ordered]@{
    Name='negative.stale-report-authority-with-rehashed-manifests';Passed=$negativeRejected
})

$failed=@($rows|Where-Object{-not $_.Passed})
$result=[ordered]@{
    SchemaVersion='god2-portable-result-packager-regression-v1'
    TestedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    Status=if($failed.Count -eq 0){'PASS'}else{'FAIL'}
    TestCount=$rows.Count;PassedCount=$rows.Count-$failed.Count;FailedCount=$failed.Count
    Rows=@($rows)
}
$reportPath=Join-Path $outputPath 'portable-result-packager-regression.json'
Write-JsonFile $reportPath $result
[pscustomobject]$result
if($failed.Count){exit 7}
