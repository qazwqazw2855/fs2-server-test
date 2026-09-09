[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetExecutable,
    [Parameter(Mandatory)][uint32]$TargetProcessId,
    [Parameter(Mandatory)][string]$CandidateMapV3,
    [Parameter(Mandatory)][string]$EvidencePlanPath,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$SessionId,
    [ValidateRange(4,600)][int]$ObserveSeconds = 180,
    [ValidateRange(1,64)][int]$MaximumObservationsPerCandidate = 64,
    [switch]$ValidatePlanOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$utf8 = [Text.UTF8Encoding]::new($false,$true)
$expectedSha256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$fundamentalDomains = @('Object','Allocation','VTable','Factory','ManagerLookup','Registry',
    'ResourceDecode','Mutation','TaintSeed','FormulaOperand','Snapshot')

function Get-Property([object]$Object,[string]$Name) {
    if($null -eq $Object){return $null}
    $property=$Object.PSObject.Properties[$Name]
    if($null -eq $property){return $null}
    return $property.Value
}

function Write-Json([string]$Path,[object]$Value) {
    if(Test-Path -LiteralPath $Path){throw "Refusing to overwrite output: $Path"}
    [IO.File]::WriteAllText($Path,(($Value|ConvertTo-Json -Depth 20)+"`n"),$utf8)
}

if($SessionId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$'){
    throw 'Deep evidence campaign SessionId contains unsupported characters.'
}
if(Test-Path -LiteralPath $OutputRoot){throw "Refusing to overwrite campaign output: $OutputRoot"}

$planPath=(Resolve-Path -LiteralPath $EvidencePlanPath).Path
$mapPath=(Resolve-Path -LiteralPath $CandidateMapV3).Path
$targetPath=(Resolve-Path -LiteralPath $TargetExecutable).Path
$plan=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8|ConvertFrom-Json
$map=Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8|ConvertFrom-Json
$planRows=@(Get-Property $plan 'Rows')
$planAuthority=Get-Property $plan 'Authority'
$planSchemaVersion=[string](Get-Property $plan 'SchemaVersion')
$portableEntryPlan=$planSchemaVersion -ceq 'god2-next-evidence-plan-v2-portable'
if($planSchemaVersion -notin @('god2-next-evidence-plan-v1','god2-next-evidence-plan-v2-portable') -or
    [string](Get-Property $plan 'Status') -cne 'READY_FOR_EXTERNAL_LIVE_EVIDENCE' -or
    [string](Get-Property $planAuthority 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
    [string](Get-Property $planAuthority 'TargetSHA256') -cne $expectedSha256 -or
    -not [bool](Get-Property $planAuthority 'CurrentSessionObserved') -or
    [bool](Get-Property $planAuthority 'HistoricalEvidenceReused') -or
    [bool](Get-Property $planAuthority 'FixtureOnly') -or
    [string](Get-Property $map 'TargetSHA256') -cne $expectedSha256 -or
    $planRows.Count -lt 1 -or $planRows.Count -gt 3){
    throw 'Deep evidence campaign plan or exact target binding failed.'
}
$mapCandidates=@(Get-Property $map 'Candidates')
foreach($planRow in $planRows){
    $candidateId=[string](Get-Property $planRow 'CandidateId')
    $candidate=@($mapCandidates|Where-Object{
        [string](Get-Property $_ 'CandidateId') -ceq $candidateId
    })
    $candidateRva=if($candidate.Count -eq 1){[string](Get-Property $candidate[0] 'RVA')}else{''}
    $boundary=if($candidate.Count -eq 1){Get-Property $candidate[0] 'FunctionBoundary'}else{$null}
    $entryBinding=if($portableEntryPlan){
        [string](Get-Property $planRow 'CandidateRVA') -ceq $candidateRva -and
        [string](Get-Property $planRow 'FunctionStartRVA') -ceq $candidateRva -and
        [string](Get-Property $planRow 'BreakpointRVA') -ceq $candidateRva -and
        [string](Get-Property $planRow 'PreviousInternalProbeRVA') -ceq
            [string](Get-Property $boundary 'StartRVA') -and
        [string](Get-Property $planRow 'ExpectedFunctionStartBytes') -ceq
            [string](Get-Property $candidate[0] 'Bytes') -and
        @(Get-Property $candidate[0] 'CallsiteRVAs').Count -gt 0
    }else{
        [string](Get-Property $planRow 'CandidateRVA') -ceq $candidateRva -and
        [string](Get-Property $planRow 'ProbeRVA') -ceq
            [string](Get-Property $boundary 'StartRVA')
    }
    if($candidate.Count -ne 1 -or
        -not $entryBinding -or
        [bool](Get-Property $planRow 'CanaryAllowed') -or
        @(Get-Property $planRow 'MissingGates').Count -eq 0){
        throw "Deep evidence campaign candidate binding failed: $candidateId"
    }
}
if($ValidatePlanOnly){
    return [pscustomobject][ordered]@{
        Passed=$true;ValidationOnly=$true;SelectedCandidateCount=$planRows.Count
        TargetSHA256=$expectedSha256;EvidencePlanSHA256=(Get-FileHash $planPath -Algorithm SHA256).Hash
        CandidateMapSHA256=(Get-FileHash $mapPath -Algorithm SHA256).Hash
        FunctionEntryBindingsVerified=$true
        FunctionStartBreakpointContractPassed=$true;CanaryAllowed=$false
    }
}

[void](New-Item -ItemType Directory -Path $OutputRoot)
$started=[DateTimeOffset]::UtcNow
$observerRoot=Join-Path $OutputRoot 'observer-session'
$modeScript=Join-Path $PSScriptRoot 'Invoke-ContractAcquisitionMode.ps1'
$modeResult=& $modeScript -TargetExecutable $targetPath -TargetProcessId $TargetProcessId `
    -CandidateMapV3 $mapPath -EvidencePlanPath $planPath -OutputRoot $observerRoot `
    -SessionId $SessionId `
    -MaximumCandidates $planRows.Count -ObserveSeconds $ObserveSeconds `
    -MaximumObservationsPerCandidate $MaximumObservationsPerCandidate
$observerManifestPath=Join-Path $observerRoot 'contract-acquisition-manifest.json'
$observerManifest=Get-Content -LiteralPath $observerManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$entryBreakpointReport=Get-Content -LiteralPath `
    (Join-Path $observerRoot 'entry-breakpoint-report.json') -Raw -Encoding UTF8|ConvertFrom-Json
if(-not [bool]$modeResult.Passed -or -not [bool]$observerManifest.EvidenceDrivenRotation -or
    [int]$observerManifest.CandidateCount -ne $planRows.Count -or
    [string]$observerManifest.EvidencePlanSHA256 -cne (Get-FileHash $planPath -Algorithm SHA256).Hash -or
    [bool]$observerManifest.MemoryWritten -or [bool]$observerManifest.GameplayStateModified -or
    [bool]$observerManifest.ExtraNetworkTrafficGenerated -or
    -not [bool]$observerManifest.DebuggerAttached -or -not [bool]$observerManifest.DebuggerDetached -or
    -not [bool]$observerManifest.DebugRegistersCleared -or
    -not [bool]$entryBreakpointReport.FunctionStartBreakpointContractPassed){
    throw 'Evidence-driven observer failed its single-attach read-only contract.'
}

$rootArtifactNames=@('candidate-runtime-observations.jsonl','candidate-abi-report.json',
    'candidate-thread-context.json','candidate-reentrancy-report.json',
    'candidate-object-stability.json','candidate-consumer-links.jsonl',
    'candidate-promotion-result.json','contract-acquisition-observer-result.json',
    'entry-breakpoint-report.json')
foreach($name in $rootArtifactNames){
    Copy-Item -LiteralPath (Join-Path $observerRoot $name) -Destination (Join-Path $OutputRoot $name)
}
$runtimeRows=@(Get-Content -LiteralPath (Join-Path $OutputRoot 'candidate-runtime-observations.jsonl')|
    Where-Object{-not [string]::IsNullOrWhiteSpace($_)}|ForEach-Object{$_|ConvertFrom-Json})
$liveRows=@($runtimeRows|Where-Object{[bool](Get-Property $_ 'RuntimeObservation')})
$observedDomains=@($liveRows|ForEach-Object{[string](Get-Property $_ 'Domain')}|Sort-Object -Unique)
$selectedIds=@($planRows|ForEach-Object{[string](Get-Property $_ 'CandidateId')})
$domainRows=@($fundamentalDomains|ForEach-Object{
    $domain=$_
    $selected=$domain -cin @($planRows|ForEach-Object{[string](Get-Property $_ 'Domain')})
    $count=@($liveRows|Where-Object{[string](Get-Property $_ 'Domain') -ceq $domain}).Count
    [pscustomobject][ordered]@{
        Domain=$domain;SelectedByEvidencePlan=$selected
        CandidateId=if($selected){[string](@($planRows|Where-Object Domain -ceq $domain)[0].CandidateId)}else{$null}
        RuntimeObservationCount=$count
        Status=if($count -gt 0){'RUNTIME_OBSERVED_PROMOTION_GATES_PENDING'}elseif($selected){
            'SELECTED_NOT_TRIGGERED_IN_SESSION'}else{'DEFERRED_BY_EVIDENCE_PLAN'}
        ActivationAllowed=$false;PromotionEligible=$false
    }
})
$ended=[DateTimeOffset]::UtcNow
Write-Json (Join-Path $OutputRoot 'fundamental-domain-observation-summary.json') ([ordered]@{
    SchemaVersion='god2-fundamental-domain-observation-summary-v2'
    FundamentalDomainCount=11;SelectedCandidateCount=$planRows.Count
    RuntimeObservedDomainCount=$observedDomains.Count;Rows=$domainRows
    ActivationAllowedCount=0;PromotionEligibleCount=0
})

$artifacts=@($rootArtifactNames + 'fundamental-domain-observation-summary.json'|ForEach-Object{
    $path=Join-Path $OutputRoot $_
    [pscustomobject][ordered]@{Name=$_;Bytes=[uint64](Get-Item $path).Length
        SHA256=(Get-FileHash $path -Algorithm SHA256).Hash}
})
$manifest=[ordered]@{
    SchemaVersion='god2-contract-acquisition-campaign-manifest-v2'
    GeneratedAtUtc=$ended.ToString('o')
    Authority=[ordered]@{AuthorityProfile='CurrentOfficialClientLive';SessionId=$SessionId
        TargetExecutable='God2_opt.exe';TargetSHA256=$expectedSha256
        CurrentSessionObserved=$true;HistoricalEvidenceReused=$false;FixtureOnly=$false
        PromotionEligible=$false}
    Mode='EvidenceDrivenFunctionEntryAndBoundedConsumerCampaign'
    EvidenceDrivenRotation=$true;ValidationOnly=$true;ProductionHookInstalled=$false
    FundamentalDomainCount=11;CandidateCount=11;SelectedCandidateCount=$planRows.Count
    SelectedCandidateIds=$selectedIds;EvidencePlanPath=$planPath
    EvidencePlanSHA256=(Get-FileHash $planPath -Algorithm SHA256).Hash
    CandidateMapSHA256=(Get-FileHash $mapPath -Algorithm SHA256).Hash
    StartedAtUtc=$started.ToString('o');EndedAtUtc=$ended.ToString('o')
    ObserveSeconds=$ObserveSeconds;RuntimeObservationCount=$liveRows.Count
    RuntimeObservedDomainCount=$observedDomains.Count
    MemoryWritten=$false;GameplayStateModified=$false;ExtraNetworkTrafficGenerated=$false
    DebuggerAttachCount=1;DebuggerDetachCount=1
    AllBatchesAttached=$true;AllBatchesDetached=$true;AllDebugRegistersCleared=$true
    ConsumerObservationCount=@($liveRows|Where-Object Phase -ceq 'ConsumerStep').Count
    FunctionStartBreakpointContractPassed=[bool]$entryBreakpointReport.FunctionStartBreakpointContractPassed
    EntryReturnCallerCorrelationCount=[int]$entryBreakpointReport.CompleteCorrelationCount
    ActivationAllowedCount=0;PromotionEligibleCount=0
    Status=if($liveRows.Count -gt 0){'CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_GATES_PENDING'}else{
        'EVIDENCE_BLOCKED_CANDIDATES_NOT_TRIGGERED_IN_SESSION'}
    Domains=$domainRows;Artifacts=$artifacts
}
$manifestPath=Join-Path $OutputRoot 'contract-acquisition-campaign-manifest.json'
Write-Json $manifestPath $manifest

[pscustomobject][ordered]@{
    Passed=$true;OutputRoot=(Resolve-Path $OutputRoot).Path
    CandidateCount=11;SelectedCandidateCount=$planRows.Count;FundamentalDomainCount=11
    RuntimeObservationCount=$liveRows.Count;RuntimeObservedDomainCount=$observedDomains.Count
    ConsumerObservationCount=@($liveRows|Where-Object Phase -ceq 'ConsumerStep').Count
    FunctionStartBreakpointContractPassed=[bool]$entryBreakpointReport.FunctionStartBreakpointContractPassed
    EntryReturnCallerCorrelationCount=[int]$entryBreakpointReport.CompleteCorrelationCount
    ActivationAllowedCount=0;PromotionEligibleCount=0;Status=$manifest.Status
}
