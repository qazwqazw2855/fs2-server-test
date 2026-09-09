[CmdletBinding()]
param(
    [string]$EvidencePlanPath = '',
    [ValidateRange(30,600)][int]$ObserveSeconds = 180,
    [ValidateRange(4,600)][int]$DeepEvidenceObserveSeconds = 180,
    [ValidateRange(300,1800)][int]$TimeoutSeconds = 900
)

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$runId='deep-contract-live-' + (Get-Date -Format 'yyyyMMdd-HHmmss')

if([string]::IsNullOrWhiteSpace($EvidencePlanPath)){
    $latest=Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Artifacts') -Directory `
        -Filter 'DeepContractPromotionClosure-*'|Sort-Object LastWriteTimeUtc -Descending|ForEach-Object{
            $candidate=Join-Path $_.FullName 'next-evidence-plan.json'
            if(Test-Path -LiteralPath $candidate -PathType Leaf){Get-Item -LiteralPath $candidate}
        }|Select-Object -First 1
    if($null -eq $latest){throw 'No deep-contract next-evidence-plan.json was found.'}
    $EvidencePlanPath=$latest.FullName
}
$planPath=(Resolve-Path -LiteralPath $EvidencePlanPath).Path
$repoPrefix=[IO.Path]::GetFullPath($repoRoot).TrimEnd('\')+'\'
if(-not $planPath.StartsWith($repoPrefix,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Evidence plan must be stored under the God2 workspace.'
}
$plan=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8|ConvertFrom-Json
if([string]$plan.SchemaVersion -cne 'god2-next-evidence-plan-v1' -or
    [string]$plan.Status -cne 'READY_FOR_EXTERNAL_LIVE_EVIDENCE' -or
    @($plan.Rows).Count -lt 1 -or @($plan.Rows).Count -gt 3){
    throw 'Evidence plan is not ready for a bounded external live campaign.'
}

$liveScript=Join-Path $PSScriptRoot 'Invoke-UltimateOfficialLiveCapture.ps1'
$liveOutput=@(& $liveScript -RunId $runId -TestAccountKey OfficialA `
    -ObserveSeconds $ObserveSeconds -TimeoutSeconds $TimeoutSeconds `
    -DeepEvidencePlanPath $planPath -DeepEvidenceObserveSeconds $DeepEvidenceObserveSeconds)
$liveSummaryPath=Join-Path $repoRoot "Artifacts\Ultimate\LiveRuns\$runId\ultimate-official-live-summary.json"
if(-not (Test-Path -LiteralPath $liveSummaryPath -PathType Leaf)){
    throw 'Launcher live capture did not produce its summary.'
}
$liveSummary=Get-Content -LiteralPath $liveSummaryPath -Raw -Encoding UTF8|ConvertFrom-Json
if([string]$liveSummary.status -cne 'PASS'){
    throw "Launcher live capture failed: $($liveSummary.failureCode)"
}

$runtimeRoot=Join-Path $repoRoot "Artifacts\ClientInstrumentation\LoginTrial\$runId"
$closureRoot=Join-Path $repoRoot "Artifacts\DeepContractPromotionClosure-$runId"
$closureScript=Join-Path $repoRoot `
    'tools\God2.PacketCapture\ultimate\Invoke-DeepContractPromotionClosure.ps1'
$closure=& $closureScript -RepositoryRoot $repoRoot `
    -OfficialRuntimeEvidence (Join-Path $runtimeRoot 'analysis\official-runtime-summary.json') `
    -CandidateMapV3 (Join-Path $runtimeRoot 'analysis\deep-probe-candidate-map-v3.json') `
    -ContractAcquisitionRoot (Join-Path $runtimeRoot 'analysis\contract-acquisition') `
    -SemanticEventsPath (Join-Path $runtimeRoot 'sensitive\semantic-events.jsonl') `
    -OutputRoot $closureRoot `
    -ReleaseExecutable (Join-Path $repoRoot 'artifacts\release\God2SemanticRecoveryEngine.exe') `
    -CreatePortablePackage

[pscustomobject][ordered]@{
    Passed=$true;RunId=$runId;LauncherFlow='Launcher.exe -> God2_opt.exe'
    DirectClientLaunch=$false;EvidencePlanPath=$planPath
    RuntimeRoot=$runtimeRoot;ClosureRoot=$closureRoot
    Status=[string]$closure.Status;ExactFinalStatus=[string]$closure.ExactFinalStatus
    PromotionEligibleCount=[int]$closure.PromotionEligibleCount
    ActivationAllowedCount=[int]$closure.ActivationAllowedCount
    PortableValidationPackagePath=[string]$closure.PortableValidationPackagePath
    PortableValidationPackageSHA256=[string]$closure.PortableValidationPackageSHA256
}
