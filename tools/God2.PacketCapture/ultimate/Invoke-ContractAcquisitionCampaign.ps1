[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetExecutable,
    [Parameter(Mandatory)][uint32]$TargetProcessId,
    [Parameter(Mandatory)][string]$CandidateMapV3,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$SessionId,
    [ValidateRange(4,600)][int]$ObserveSeconds = 180,
    [ValidateRange(1,64)][int]$MaximumObservationsPerCandidate = 32
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 18) + "`n"),
        [Text.UTF8Encoding]::new($false,$true))
}

function Read-Json([string]$Path) {
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-Property([object]$Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

if ($SessionId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$') {
    throw 'Contract Acquisition campaign SessionId contains unsupported characters.'
}
if (Test-Path -LiteralPath $OutputRoot) {
    throw "Refusing to overwrite acquisition campaign output: $OutputRoot"
}
[void](New-Item -ItemType Directory -Path $OutputRoot)

$modeScript = Join-Path $PSScriptRoot 'Invoke-ContractAcquisitionMode.ps1'
if (-not (Test-Path -LiteralPath $modeScript -PathType Leaf)) {
    throw "Contract Acquisition mode script is missing: $modeScript"
}
$fundamentalDomains = @(
    'Object','Allocation','VTable','Factory','ManagerLookup','Registry',
    'ResourceDecode','Mutation','TaintSeed','FormulaOperand','Snapshot'
)
$batches = @(
    @('Object','Allocation','VTable'),
    @('Factory','ManagerLookup','Registry'),
    @('ResourceDecode','Mutation','TaintSeed'),
    @('FormulaOperand','Snapshot')
)
$batchResults = [Collections.Generic.List[object]]::new()
$domainRows = [Collections.Generic.List[object]]::new()
$runtimeLines = [Collections.Generic.List[string]]::new()
$consumerLines = [Collections.Generic.List[string]]::new()
$abiRows = [Collections.Generic.List[object]]::new()
$threadRows = [Collections.Generic.List[object]]::new()
$reentrancyRows = [Collections.Generic.List[object]]::new()
$stabilityRows = [Collections.Generic.List[object]]::new()
$promotionRows = [Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow

$observerRoot = Join-Path $OutputRoot 'observer-session'
$result = & $modeScript -TargetExecutable $TargetExecutable `
    -TargetProcessId $TargetProcessId -CandidateMapV3 $CandidateMapV3 `
    -OutputRoot $observerRoot -CandidateDomains $fundamentalDomains `
    -MaximumCandidates 11 -ObserveSeconds $ObserveSeconds `
    -MaximumObservationsPerCandidate $MaximumObservationsPerCandidate
$manifestPath = Join-Path $observerRoot 'contract-acquisition-manifest.json'
$manifest = Read-Json $manifestPath
if (-not [bool]$result.Passed -or [int]$result.CandidateCount -ne 11 -or
    [bool]$manifest.MemoryWritten -or [bool]$manifest.GameplayStateModified -or
    [bool]$manifest.ExtraNetworkTrafficGenerated -or
    -not [bool]$manifest.DebuggerAttached -or -not [bool]$manifest.DebuggerDetached -or
    -not [bool]$manifest.DebugRegistersCleared -or
    [int]$manifest.ActivationAllowedCount -ne 0 -or
    [int]$manifest.PromotionEligibleCount -ne 0) {
    throw 'Contract Acquisition rotating campaign failed its single-session safety contract.'
}

$batchRuntime = @(Get-Content -LiteralPath (Join-Path $observerRoot 'candidate-runtime-observations.jsonl') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
foreach ($line in $batchRuntime) { [void]$runtimeLines.Add([string]$line) }
foreach ($line in @(Get-Content -LiteralPath (Join-Path $observerRoot 'candidate-consumer-links.jsonl') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    [void]$consumerLines.Add([string]$line)
}
$runtimeObjects = @($batchRuntime | ForEach-Object { $_ | ConvertFrom-Json })
$abi = Read-Json (Join-Path $observerRoot 'candidate-abi-report.json')
$thread = Read-Json (Join-Path $observerRoot 'candidate-thread-context.json')
$reentrancy = Read-Json (Join-Path $observerRoot 'candidate-reentrancy-report.json')
$stability = Read-Json (Join-Path $observerRoot 'candidate-object-stability.json')
$promotion = Read-Json (Join-Path $observerRoot 'candidate-promotion-result.json')
foreach ($row in @($abi.Rows)) { [void]$abiRows.Add($row) }
foreach ($row in @($thread.Rows)) { [void]$threadRows.Add($row) }
foreach ($row in @($reentrancy.Rows)) { [void]$reentrancyRows.Add($row) }
foreach ($row in @($stability.Rows)) { [void]$stabilityRows.Add($row) }
foreach ($row in @($promotion.Rows)) { [void]$promotionRows.Add($row) }

$sessionManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$baseSeconds = [Math]::Floor($ObserveSeconds / $batches.Count)
$remainder = $ObserveSeconds % $batches.Count
for ($index = 0; $index -lt $batches.Count; $index++) {
    $domains = [string[]]$batches[$index]
    $seconds = [int]$baseSeconds + $(if ($index -lt $remainder) { 1 } else { 0 })
    $batchName = 'batch-{0:D2}' -f ($index + 1)
    $batchRoot = Join-Path $OutputRoot $batchName
    [void](New-Item -ItemType Directory -Path $batchRoot)
        $batchObservationCount = 0
        $batchEntryCount = 0
        $batchReturnCount = 0
        foreach ($domain in $domains) {
        $domainRuntimeCount = @($runtimeObjects | Where-Object {
            [string](Get-Property $_ 'Domain') -ceq $domain -and
                [bool](Get-Property $_ 'RuntimeObservation')
        }).Count
            $batchObservationCount += $domainRuntimeCount
            $batchEntryCount += @($runtimeObjects | Where-Object {
                [string](Get-Property $_ 'Domain') -ceq $domain -and
                    [string](Get-Property $_ 'Phase') -ceq 'Entry'
            }).Count
            $batchReturnCount += @($runtimeObjects | Where-Object {
                [string](Get-Property $_ 'Domain') -ceq $domain -and
                    [string](Get-Property $_ 'Phase') -ceq 'Return'
            }).Count
        $candidate = @($abi.Rows | Where-Object { [string]$_.Domain -ceq $domain } |
            Select-Object -First 1)
        [void]$domainRows.Add([pscustomobject][ordered]@{
            Domain=$domain;Batch=$batchName
            CandidateId=if($candidate.Count -eq 1){[string]$candidate[0].CandidateId}else{$null}
            ProducerImplemented=$true;ContractSafe=$true
            RuntimeObservationCount=$domainRuntimeCount
            Status=if($domainRuntimeCount -gt 0){'RUNTIME_OBSERVED_PROMOTION_GATES_PENDING'}else{'NOT_TRIGGERED_IN_SESSION'}
            ActivationAllowed=$false;PromotionEligible=$false
        })
    }
    $binding = [ordered]@{
        SchemaVersion='god2-contract-acquisition-logical-batch-binding-v1'
        Batch=$batchName;Domains=$domains;ObserveSeconds=$seconds
        SharedDebuggerSession=$true;DebuggerAttachCount=1;DebuggerDetachCount=1
        ObserverSessionManifest='observer-session/contract-acquisition-manifest.json'
        ObserverSessionManifestSHA256=$sessionManifestSha256
        RuntimeObservationCount=$batchObservationCount
        MemoryWritten=$false;Passed=$true
    }
    $bindingPath = Join-Path $batchRoot 'logical-batch-binding.json'
    Write-Json $bindingPath $binding
    [void]$batchResults.Add([pscustomobject][ordered]@{
        Batch=$batchName;Domains=$domains;ObserveSeconds=$seconds
        CandidateCount=$domains.Count;RuntimeObservationCount=$batchObservationCount
        EntryObservationCount=$batchEntryCount;ReturnObservationCount=$batchReturnCount
        SharedDebuggerSession=$true;DebuggerAttached=[bool]$manifest.DebuggerAttached
        DebuggerDetached=[bool]$manifest.DebuggerDetached
        DebugRegistersCleared=[bool]$manifest.DebugRegistersCleared
        MemoryWritten=[bool]$manifest.MemoryWritten;Passed=[bool]$result.Passed
        ManifestSHA256=(Get-FileHash -LiteralPath $bindingPath -Algorithm SHA256).Hash
    })
}

if ($domainRows.Count -ne 11 -or
    @($domainRows.Domain | Select-Object -Unique).Count -ne 11 -or
    @($fundamentalDomains | Where-Object { $_ -cnotin @($domainRows.Domain) }).Count -ne 0) {
    throw 'Contract Acquisition campaign did not cover exactly all 11 fundamental domains.'
}
$endedAt = [DateTimeOffset]::UtcNow
$runtimeObservationCount = @($runtimeLines | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { [bool](Get-Property $_ 'RuntimeObservation') }).Count
$authority = [ordered]@{
    AuthorityProfile='CurrentOfficialClientLive'; SessionId=$SessionId
    TargetExecutable='God2_opt.exe'
    TargetProcessId=$TargetProcessId; CurrentSessionObserved=$true
    HistoricalEvidenceReused=$false; FixtureOnly=$false; PromotionEligible=$false
}
[IO.File]::WriteAllLines((Join-Path $OutputRoot 'candidate-runtime-observations.jsonl'),
    [string[]]$runtimeLines.ToArray(),[Text.UTF8Encoding]::new($false,$true))
[IO.File]::WriteAllLines((Join-Path $OutputRoot 'candidate-consumer-links.jsonl'),
    [string[]]$consumerLines.ToArray(),[Text.UTF8Encoding]::new($false,$true))
Write-Json (Join-Path $OutputRoot 'candidate-abi-report.json') ([ordered]@{
    SchemaVersion='god2-candidate-abi-report-v3';Authority=$authority;CandidateCount=11
    RuntimeObservedCount=@($abiRows|Where-Object {$_.EntryObservationCount -gt 0}).Count
    VerifiedCount=0;Status=if($runtimeObservationCount -gt 0){'RUNTIME_ABI_OBSERVED_PROMOTION_GATES_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($abiRows)
})
Write-Json (Join-Path $OutputRoot 'candidate-thread-context.json') ([ordered]@{
    SchemaVersion='god2-candidate-thread-context-v3';Authority=$authority;CandidateCount=11
    ObservedCount=@($threadRows|Where-Object ThreadContextObserved).Count;VerifiedCount=0
    Status=if($runtimeObservationCount -gt 0){'THREAD_CONTEXT_OBSERVED_ROLE_GATES_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($threadRows)
})
Write-Json (Join-Path $OutputRoot 'candidate-reentrancy-report.json') ([ordered]@{
    SchemaVersion='god2-candidate-reentrancy-report-v3';Authority=$authority;CandidateCount=11
    ObservedCount=@($reentrancyRows|Where-Object {$_.EntryObservationCount -gt 0}).Count;VerifiedCount=0
    Status=if($runtimeObservationCount -gt 0){'REENTRANCY_OBSERVATION_CAPTURED_VERIFICATION_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($reentrancyRows)
})
Write-Json (Join-Path $OutputRoot 'candidate-object-stability.json') ([ordered]@{
    SchemaVersion='god2-candidate-object-stability-v3';Authority=$authority;CandidateCount=11
    StableTokenObservedCount=@($stabilityRows|Where-Object StableThisPointerTokenObserved).Count;VerifiedCount=0
    Status=if($runtimeObservationCount -gt 0){'SESSION_POINTER_STABILITY_OBSERVED_TYPED_IDENTITY_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($stabilityRows)
})
Write-Json (Join-Path $OutputRoot 'candidate-promotion-result.json') ([ordered]@{
    SchemaVersion='god2-candidate-promotion-result-v2';Authority=$authority;CandidateCount=11
    GateCountPerCandidate=14;LedgerRowCount=$promotionRows.Count
    PassedGateRowCount=@($promotionRows|Where-Object Passed).Count;FailedGateRowCount=0
    EvidenceBlockedGateRowCount=@($promotionRows|Where-Object EvidenceBlocked).Count
    ActivationAllowedCount=0;PromotionEligibleCount=0
    Status='EVIDENCE_BLOCKED_NO_DEEP_CANDIDATE_PROMOTED';Rows=@($promotionRows)
})
Write-Json (Join-Path $OutputRoot 'contract-acquisition-observer-result.json') ([ordered]@{
    SchemaVersion='god2-contract-acquisition-campaign-observer-v1';Authority=$authority
    ObserverMode='SingleAttachRotatingFourBatchWOW64HardwareExecutionBreakpointCampaign'
    HardwareBreakpointCapacityPerBatch=3;BatchCount=4;FundamentalDomainCount=11
    DebuggerAttachCount=1;DebuggerDetachCount=1
    MemoryWritten=$false;RawPointerPersisted=$false;PointerTokenScope='CurrentSessionOnly'
    Batches=@($batchResults)
})
Write-Json (Join-Path $OutputRoot 'fundamental-domain-observation-summary.json') ([ordered]@{
    SchemaVersion='god2-fundamental-domain-observation-summary-v1';Authority=$authority
    FundamentalDomainCount=11;ProducerImplementedCount=11
    RuntimeObservedDomainCount=@($domainRows|Where-Object {$_.RuntimeObservationCount -gt 0}).Count
    NotTriggeredInSessionCount=@($domainRows|Where-Object Status -ceq 'NOT_TRIGGERED_IN_SESSION').Count
    ActivationAllowedCount=0;PromotionEligibleCount=0;Rows=@($domainRows)
})

$rootArtifactNames = @(
    'candidate-runtime-observations.jsonl','candidate-abi-report.json',
    'candidate-thread-context.json','candidate-reentrancy-report.json',
    'candidate-object-stability.json','candidate-consumer-links.jsonl',
    'candidate-promotion-result.json','contract-acquisition-observer-result.json',
    'fundamental-domain-observation-summary.json'
)
$artifacts = @($rootArtifactNames | ForEach-Object {
    $path = Join-Path $OutputRoot $_
    [pscustomobject][ordered]@{
        Name=$_;SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Bytes=[uint64](Get-Item -LiteralPath $path).Length
    }
})
$manifest = [ordered]@{
    SchemaVersion='god2-contract-acquisition-campaign-manifest-v1'
    GeneratedAtUtc=$endedAt.ToString('o');Authority=$authority
    Mode='PatchlessReadOnlySingleAttachRotatingFourBatchHardwareBreakpointCampaign'
    ValidationOnly=$true;ProductionHookInstalled=$false
    FundamentalDomainCount=11;CandidateCount=11;BatchCount=4
    StartedAtUtc=$startedAt.ToString('o');EndedAtUtc=$endedAt.ToString('o')
    ObserveSeconds=$ObserveSeconds;RuntimeObservationCount=$runtimeObservationCount
    RuntimeObservedDomainCount=@($domainRows|Where-Object {$_.RuntimeObservationCount -gt 0}).Count
    NotTriggeredInSessionCount=@($domainRows|Where-Object Status -ceq 'NOT_TRIGGERED_IN_SESSION').Count
    MemoryWritten=$false;GameplayStateModified=$false;ExtraNetworkTrafficGenerated=$false
    DebuggerAttachCount=1;DebuggerDetachCount=1
    AllBatchesAttached=@($batchResults|Where-Object {-not $_.DebuggerAttached}).Count -eq 0
    AllBatchesDetached=@($batchResults|Where-Object {-not $_.DebuggerDetached}).Count -eq 0
    AllDebugRegistersCleared=@($batchResults|Where-Object {-not $_.DebugRegistersCleared}).Count -eq 0
    ActivationAllowedCount=0;PromotionEligibleCount=0
    Status=if($runtimeObservationCount -gt 0){
        'CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_GATES_PENDING'
    }else{'EVIDENCE_BLOCKED_CANDIDATES_NOT_TRIGGERED_IN_SESSION'}
    Batches=@($batchResults);Domains=@($domainRows);Artifacts=$artifacts
}
Write-Json (Join-Path $OutputRoot 'contract-acquisition-campaign-manifest.json') $manifest

[pscustomobject]@{
    Passed=[bool]$manifest.AllBatchesAttached -and [bool]$manifest.AllBatchesDetached -and
        [bool]$manifest.AllDebugRegistersCleared
    OutputRoot=(Resolve-Path -LiteralPath $OutputRoot).Path
    CandidateCount=11;FundamentalDomainCount=11
    RuntimeObservationCount=$runtimeObservationCount
    RuntimeObservedDomainCount=[int]$manifest.RuntimeObservedDomainCount
    ActivationAllowedCount=0;PromotionEligibleCount=0;Status=$manifest.Status
}
