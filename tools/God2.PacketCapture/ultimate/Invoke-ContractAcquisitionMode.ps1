[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetExecutable,
    [Parameter(Mandatory)][uint32]$TargetProcessId,
    [Parameter(Mandatory)][string]$CandidateMapV3,
    [Parameter(Mandatory)][string]$OutputRoot,
    [string]$SessionId = '',
    [string]$EvidencePlanPath = '',
    [string[]]$CandidateDomains = @(),
    [ValidateRange(1,12)][int]$MaximumCandidates = 3,
    [ValidateRange(1,600)][int]$ObserveSeconds = 8,
    [ValidateRange(25,2000)][int]$IntervalMilliseconds = 250,
    [ValidateRange(1,64)][int]$MaximumObservationsPerCandidate = 32
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$expectedSha256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$expectedVersion = '1.0.0.1'

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class God2ContractReadOnlyProcess : IDisposable {
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    private IntPtr handle;

    public God2ContractReadOnlyProcess(uint processId) {
        handle = OpenProcess(0x0010u | 0x1000u, false, processId);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public byte[] Read(long address, int count) {
        if (count < 1 || count > 4096) throw new InvalidOperationException("Read bound exceeded.");
        byte[] result = new byte[count];
        UIntPtr read;
        if (!ReadProcessMemory(handle, new IntPtr(address), result,
                new UIntPtr((uint)count), out read) || read.ToUInt64() != (ulong)count)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
    public void Dispose() {
        if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
}
'@

$observerSource = Join-Path $PSScriptRoot 'ContractAcquisitionDebugger.cs'
if (-not (Test-Path -LiteralPath $observerSource -PathType Leaf)) {
    throw "Contract Acquisition observer source is missing: $observerSource"
}
if ($null -eq ('God2.ContractAcquisition.HardwareBreakpointObserver' -as [type])) {
    Add-Type -Path $observerSource
}

function Get-Property([object]$Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Convert-Rva([string]$Value) {
    if ($Value -cnotmatch '^0x[0-9A-F]{8}$') { throw "Invalid candidate RVA: $Value" }
    return [Convert]::ToUInt32($Value.Substring(2),16)
}

function Get-ByteSha256([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-','') }
    finally { $algorithm.Dispose() }
}

function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 16) + "`n"),
        [Text.UTF8Encoding]::new($false,$true))
}

function Write-JsonLines([string]$Path, [object[]]$Rows) {
    $builder = [Text.StringBuilder]::new()
    foreach ($row in $Rows) { [void]$builder.AppendLine(($row | ConvertTo-Json -Depth 12 -Compress)) }
    [IO.File]::WriteAllText($Path,$builder.ToString(),[Text.UTF8Encoding]::new($false,$true))
}

$targetPath = (Resolve-Path -LiteralPath $TargetExecutable).Path
$targetItem = Get-Item -LiteralPath $targetPath
$targetSha = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
if ([IO.Path]::GetFileName($targetPath) -ine 'God2_opt.exe' -or
    $targetItem.VersionInfo.FileVersion -cne $expectedVersion -or
    $targetSha -cne $expectedSha256) {
    throw 'Contract Acquisition target identity mismatch.'
}
$mapPath = (Resolve-Path -LiteralPath $CandidateMapV3).Path
$mapSha = (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash
$map = Get-Content -Raw -LiteralPath $mapPath | ConvertFrom-Json
if ([string](Get-Property $map 'SchemaVersion') -cne 'god2-deep-probe-candidate-map-v3' -or
    [string](Get-Property $map 'TargetSHA256') -cne $expectedSha256 -or
    [int](Get-Property $map 'ActivationAllowedCount') -ne 0 -or
    -not [bool](Get-Property (Get-Property $map 'AnalysisBounds') 'RuntimeImageObserved')) {
    throw 'Contract Acquisition requires a fail-closed pre-attach runtime-image candidate map v3.'
}

$process = Get-Process -Id $TargetProcessId -ErrorAction Stop
$process.Refresh()
if ($process.HasExited -or
    -not [IO.Path]::GetFullPath([string]$process.MainModule.FileName).Equals(
        [IO.Path]::GetFullPath($targetPath), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Contract Acquisition process identity mismatch.'
}
$imageBase = [int64]$process.MainModule.BaseAddress
$processCreated = $process.StartTime.ToUniversalTime().ToString('o')
$allCandidates = @(Get-Property $map 'Candidates')
$selected = [Collections.Generic.List[object]]::new()
$selectedRvas = @{}
$probeRvasByCandidateId = @{}
$candidateRvasByCandidateId = @{}
$functionStartRvasByCandidateId = @{}
$previousInternalProbeRvasByCandidateId = @{}
$evidencePlan = $null
$evidencePlanRows = @()
$evidencePlanSha256 = ''
$fundamentalPriority = @('Object','Registry','Mutation','Allocation','VTable',
    'Factory','ManagerLookup','ResourceDecode','TaintSeed','FormulaOperand','Snapshot')
$requestedDomains = @($CandidateDomains | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_)
} | Select-Object -Unique)
foreach ($requestedDomain in $requestedDomains) {
    if ([string]$requestedDomain -cnotin $fundamentalPriority) {
        throw "Unsupported fundamental candidate domain: $requestedDomain"
    }
}
if ($requestedDomains.Count -gt $MaximumCandidates) {
    throw 'Requested candidate domain count exceeds the bounded hardware breakpoint capacity.'
}
$orderedCandidates = @($allCandidates | Sort-Object `
    @{Expression={[double](Get-Property $_ 'StaticScore')};Descending=$true}, `
    @{Expression={[string](Get-Property $_ 'CandidateId')};Descending=$false})

if (-not [string]::IsNullOrWhiteSpace($EvidencePlanPath)) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $planPath = (Resolve-Path -LiteralPath $EvidencePlanPath).Path
    $repoPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if (-not $planPath.StartsWith($repoPrefix,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Evidence plan must be stored under the repository root.'
    }
    $evidencePlan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $evidencePlanRows = @(Get-Property $evidencePlan 'Rows')
    $planAuthority = Get-Property $evidencePlan 'Authority'
    $planSchemaVersion = [string](Get-Property $evidencePlan 'SchemaVersion')
    $portableEntryPlan = $planSchemaVersion -ceq 'god2-next-evidence-plan-v2-portable'
    if ($planSchemaVersion -notin @('god2-next-evidence-plan-v1','god2-next-evidence-plan-v2-portable') -or
        [string](Get-Property $evidencePlan 'Status') -cne 'READY_FOR_EXTERNAL_LIVE_EVIDENCE' -or
        -not [bool](Get-Property $evidencePlan 'EvidenceDrivenRotation') -or
        [string](Get-Property $planAuthority 'AuthorityProfile') -cne 'CurrentOfficialClientLive' -or
        [string](Get-Property $planAuthority 'TargetSHA256') -cne $expectedSha256 -or
        [bool](Get-Property $planAuthority 'HistoricalEvidenceReused') -or
        [bool](Get-Property $planAuthority 'FixtureOnly') -or
        $evidencePlanRows.Count -lt 1 -or $evidencePlanRows.Count -gt 3 -or
        $evidencePlanRows.Count -gt $MaximumCandidates) {
        throw 'Evidence plan identity, authority, or bounded selection contract failed.'
    }
    foreach ($planRow in $evidencePlanRows) {
        $candidateId = [string](Get-Property $planRow 'CandidateId')
        $candidate = @($allCandidates | Where-Object {
            [string](Get-Property $_ 'CandidateId') -ceq $candidateId
        })
        if ($candidate.Count -ne 1) { throw "Evidence plan candidate binding failed: $candidateId" }
        $candidate = $candidate[0]
        $candidateRva = [string](Get-Property $candidate 'RVA')
        $boundary = Get-Property $candidate 'FunctionBoundary'
        $probeRva = if($portableEntryPlan){
            [string](Get-Property $planRow 'BreakpointRVA')
        }else{[string](Get-Property $planRow 'ProbeRVA')}
        $functionStartRva = if($portableEntryPlan){
            [string](Get-Property $planRow 'FunctionStartRVA')
        }else{[string](Get-Property $boundary 'StartRVA')}
        $previousInternalProbeRva = if($portableEntryPlan){
            [string](Get-Property $planRow 'PreviousInternalProbeRVA')
        }else{[string](Get-Property $boundary 'StartRVA')}
        $entryBytesMatch = -not $portableEntryPlan -or
            [string](Get-Property $planRow 'ExpectedFunctionStartBytes') -ceq
                [string](Get-Property $candidate 'Bytes')
        $entryBindingMatch = if($portableEntryPlan){
            $candidateRva -ceq $functionStartRva -and $probeRva -ceq $functionStartRva -and
            $previousInternalProbeRva -ceq [string](Get-Property $boundary 'StartRVA') -and
            [string](Get-Property $boundary 'Status') -ceq 'HEURISTIC_NOT_RUNTIME_VERIFIED' -and
            @(Get-Property $candidate 'CallsiteRVAs').Count -gt 0 -and $entryBytesMatch
        }else{$probeRva -ceq [string](Get-Property $boundary 'StartRVA')}
        if ([string](Get-Property $planRow 'CandidateRVA') -cne $candidateRva -or
            -not $entryBindingMatch -or
            [bool](Get-Property $planRow 'CanaryAllowed') -or
            @(Get-Property $planRow 'MissingGates').Count -eq 0 -or
            $selectedRvas.ContainsKey($probeRva)) {
            throw "Evidence plan function-entry or fail-closed binding failed: $candidateId"
        }
        [void]$selected.Add($candidate)
        $selectedRvas[$probeRva] = $true
        $probeRvasByCandidateId[$candidateId] = $probeRva
        $candidateRvasByCandidateId[$candidateId] = $candidateRva
        $functionStartRvasByCandidateId[$candidateId] = $functionStartRva
        $previousInternalProbeRvasByCandidateId[$candidateId] = $previousInternalProbeRva
    }
    $requestedDomains = @($evidencePlanRows | ForEach-Object { [string](Get-Property $_ 'Domain') })
    $evidencePlanSha256 = (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash
}
else {
    $selectionPriority = if ($requestedDomains.Count -gt 0) { $requestedDomains } else { $fundamentalPriority }
    foreach ($domain in $selectionPriority) {
        foreach ($candidate in @($orderedCandidates | Where-Object {
                [string](Get-Property $_ 'Domain') -ceq $domain })) {
            $rva = [string](Get-Property $candidate 'RVA')
            if ($selectedRvas.ContainsKey($rva)) { continue }
            [void]$selected.Add($candidate)
            $selectedRvas[$rva] = $true
            $probeRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $candidateRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $functionStartRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $previousInternalProbeRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            break
        }
        if ($selected.Count -ge $MaximumCandidates) { break }
    }
    if ($requestedDomains.Count -eq 0 -and $selected.Count -lt $MaximumCandidates) {
        foreach ($candidate in $orderedCandidates) {
            $rva = [string](Get-Property $candidate 'RVA')
            if ($selectedRvas.ContainsKey($rva)) { continue }
            [void]$selected.Add($candidate)
            $selectedRvas[$rva] = $true
            $probeRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $candidateRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $functionStartRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            $previousInternalProbeRvasByCandidateId[[string](Get-Property $candidate 'CandidateId')] = $rva
            if ($selected.Count -ge $MaximumCandidates) { break }
        }
    }
}
if ($selected.Count -eq 0) { throw 'No bounded candidates were available.' }
if ($requestedDomains.Count -gt 0 -and $selected.Count -ne $requestedDomains.Count) {
    throw 'One or more requested fundamental domains had no bounded candidate.'
}
if (Test-Path -LiteralPath $OutputRoot) { throw "Refusing to overwrite acquisition output: $OutputRoot" }
[void](New-Item -ItemType Directory -Path $OutputRoot)

if ([string]::IsNullOrWhiteSpace($SessionId)) {
    $SessionId = 'acquisition-{0}-{1}' -f $TargetProcessId,[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
}
if ($SessionId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$') {
    throw 'Contract Acquisition SessionId contains unsupported characters.'
}
$authority = [ordered]@{
    AuthorityProfile='CurrentOfficialClientLive'; SessionId=$SessionId
    SourceArtifact='deep-probe-candidate-map-v3.json'; SourceSHA256=$mapSha
    TargetExecutable='God2_opt.exe'; TargetVersion=$expectedVersion; TargetSHA256=$targetSha
    TargetProcessId=$TargetProcessId; TargetProcessCreationTime=$processCreated
    CurrentSessionObserved=$true; HistoricalEvidenceReused=$false; FixtureOnly=$false
    PromotionEligible=$false
}
$staticAuthority = [ordered]@{}
foreach ($key in $authority.Keys) { $staticAuthority[$key] = $authority[$key] }
$staticAuthority.AuthorityProfile = 'ExactBinaryStaticAnalysis'
$staticAuthority.CurrentSessionObserved = $false
$staticObservations = [Collections.Generic.List[object]]::new()
$runtimeObservations = [Collections.Generic.List[object]]::new()
$staticSnapshots = @{}
$observedThreadIds = [Collections.Generic.HashSet[uint32]]::new()
$reader = [God2ContractReadOnlyProcess]::new($TargetProcessId)
$started = [DateTimeOffset]::UtcNow
$readFailureCount = 0
$totalBytesRead = [uint64]0
try {
    $process.Refresh()
    foreach ($thread in @($process.Threads)) {
        [void]$observedThreadIds.Add([uint32]$thread.Id)
    }
    foreach ($candidate in $selected) {
        $candidateId = [string](Get-Property $candidate 'CandidateId')
        $candidateRvaText = [string](Get-Property $candidate 'RVA')
        $rvaText = [string]$probeRvasByCandidateId[$candidateId]
        try {
            $code = $reader.Read($imageBase + [int64](Convert-Rva $rvaText),32)
            $totalBytesRead += [uint64]$code.Length
            $callsiteHashes = [Collections.Generic.List[object]]::new()
            foreach ($callsite in @(Get-Property $candidate 'CallsiteRVAs') | Select-Object -First 4) {
                $callsiteBytes = $reader.Read($imageBase + [int64](Convert-Rva ([string]$callsite)),8)
                $totalBytesRead += [uint64]$callsiteBytes.Length
                [void]$callsiteHashes.Add([pscustomobject][ordered]@{
                    RVA=[string]$callsite; ByteCount=8; SHA256=Get-ByteSha256 $callsiteBytes
                })
            }
            $snapshot = [pscustomobject][ordered]@{
                CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
                RVA=$rvaText; CandidateRVA=$candidateRvaText; ProbeRVA=$rvaText
                FunctionStartRVA=[string]$functionStartRvasByCandidateId[$candidateId]
                PreviousInternalProbeRVA=[string]$previousInternalProbeRvasByCandidateId[$candidateId]
                BreakpointRVA=$rvaText
                FunctionStartMatched=($rvaText -ceq [string]$functionStartRvasByCandidateId[$candidateId])
                ABIEntryEvidenceValid=$false
                FunctionEntryProbe=($rvaText -ceq [string]$functionStartRvasByCandidateId[$candidateId])
                CodeByteCount=32; CodeSHA256=Get-ByteSha256 $code
                CallsiteEvidence=@($callsiteHashes)
            }
            $staticSnapshots[$candidateId] = $snapshot
        } catch { ++$readFailureCount }
    }
} finally { $reader.Dispose() }

$observerSpecs = [Collections.Generic.List[God2.ContractAcquisition.CandidateSpec]]::new()
foreach ($candidate in $selected) {
    $spec = [God2.ContractAcquisition.CandidateSpec]::new()
    $spec.CandidateId = [string](Get-Property $candidate 'CandidateId')
    $spec.Domain = [string](Get-Property $candidate 'Domain')
    $spec.Rva = Convert-Rva ([string]$probeRvasByCandidateId[$spec.CandidateId])
    $spec.CaptureConsumerSteps = -not [string]::IsNullOrWhiteSpace($EvidencePlanPath)
    [void]$observerSpecs.Add($spec)
}
$observer = $null
$observerError = ''
try {
    $observer = [God2.ContractAcquisition.HardwareBreakpointObserver]::Observe(
        [uint32]$TargetProcessId,
        [uint32]$imageBase,
        [uint32](Get-Property $map 'SizeOfImage'),
        $observerSpecs.ToArray(),
        $ObserveSeconds,
        $MaximumObservationsPerCandidate)
} catch {
    $observerError = $_.Exception.Message
}

$reader = [God2ContractReadOnlyProcess]::new($TargetProcessId)
try {
    foreach ($candidate in $selected) {
        $candidateId = [string](Get-Property $candidate 'CandidateId')
        $snapshot = $staticSnapshots[$candidateId]
        if ($null -eq $snapshot) { continue }
        try {
            $after = $reader.Read($imageBase + [int64](Convert-Rva $snapshot.RVA),32)
            $totalBytesRead += [uint64]$after.Length
            $afterHash = Get-ByteSha256 $after
            $row = [ordered]@{
                ObservationId=[guid]::NewGuid().ToString('D')
                ObservedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
                ObservationKind='StaticCodeIntegrity'
                CandidateId=$candidateId; Domain=$snapshot.Domain; RVA=$snapshot.RVA
                CandidateRVA=$snapshot.CandidateRVA; ProbeRVA=$snapshot.ProbeRVA
                FunctionStartRVA=$snapshot.FunctionStartRVA
                PreviousInternalProbeRVA=$snapshot.PreviousInternalProbeRVA
                BreakpointRVA=$snapshot.BreakpointRVA
                FunctionStartMatched=$snapshot.FunctionStartMatched
                ABIEntryEvidenceValid=$snapshot.ABIEntryEvidenceValid
                FunctionEntryProbe=$snapshot.FunctionEntryProbe
                CodeByteCount=$snapshot.CodeByteCount; CodeSHA256=$snapshot.CodeSHA256
                CodeStable=($snapshot.CodeSHA256 -ceq $afterHash)
                AfterCodeSHA256=$afterHash; CallsiteEvidence=$snapshot.CallsiteEvidence
                RuntimeObservation=$false; RegisterEvidenceCaptured=$false
                StackEvidenceCaptured=$false; ReturnRegisterEvidenceCaptured=$false
                ReturnLifetimeEvidenceCaptured=$false; ObjectReuseEvidenceCaptured=$false
                ActiveHook=$false; HardwareBreakpointObserver=$false; ProductionHook=$false
                MemoryWritten=$false; GameplayStateModified=$false
                ExtraNetworkTrafficGenerated=$false; SensitivePayloadPersisted=$false
                PromotionEligible=$false; Status='STATIC_CODE_AND_CALLSITE_INTEGRITY_OBSERVED'
            }
            foreach ($key in $staticAuthority.Keys) { $row[$key]=$staticAuthority[$key] }
            [void]$staticObservations.Add([pscustomobject]$row)
        } catch { ++$readFailureCount }
    }
} finally { $reader.Dispose() }

if ($null -ne $observer) {
    $nativeRows = [God2.ContractAcquisition.CandidateRuntimeObservation[]]$observer.Observations
    foreach ($nativeRow in $nativeRows) {
        if ($null -eq $nativeRow) {
            throw 'Contract Acquisition observer returned a null runtime observation.'
        }
        [void]$observedThreadIds.Add([uint32]$nativeRow.ThreadId)
        $row = [ordered]@{
            ObservationId=[string]$nativeRow.ObservationId
            ObservedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
            ObservationKind=('HardwareBreakpoint' + [string]$nativeRow.Phase)
            CandidateId=[string]$nativeRow.CandidateId; Domain=[string]$nativeRow.Domain
            RVA=('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva)
            CandidateRVA=[string]$candidateRvasByCandidateId[[string]$nativeRow.CandidateId]
            ProbeRVA=('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva)
            FunctionStartRVA=[string]$functionStartRvasByCandidateId[[string]$nativeRow.CandidateId]
            PreviousInternalProbeRVA=[string]$previousInternalProbeRvasByCandidateId[[string]$nativeRow.CandidateId]
            BreakpointRVA=('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva)
            FunctionStartMatched=(('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva) -ceq
                [string]$functionStartRvasByCandidateId[[string]$nativeRow.CandidateId])
            ABIEntryEvidenceValid=(('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva) -ceq
                [string]$functionStartRvasByCandidateId[[string]$nativeRow.CandidateId])
            FunctionEntryProbe=(('0x{0:X8}' -f [uint32]$nativeRow.CandidateRva) -ceq
                [string]$functionStartRvasByCandidateId[[string]$nativeRow.CandidateId])
            Phase=[string]$nativeRow.Phase; Qpc=[int64]$nativeRow.Qpc
            DurationQpc=[int64]$nativeRow.DurationQpc
            ThreadId=[uint32]$nativeRow.ThreadId; InvocationId=[int64]$nativeRow.InvocationId
            InvocationDepth=[int]$nativeRow.InvocationDepth
            NestedInvocation=[bool]$nativeRow.NestedInvocation
            SameCandidateRecursion=[bool]$nativeRow.SameCandidateRecursion
            ReturnRVA=if([uint32]$nativeRow.ReturnRva -ne 0){'0x{0:X8}' -f [uint32]$nativeRow.ReturnRva}else{$null}
            StackCleanupBytes=$nativeRow.StackCleanupBytes
            RegisterState=[ordered]@{
                EAX=$nativeRow.Eax; EBX=$nativeRow.Ebx; ECX=$nativeRow.Ecx
                EDX=$nativeRow.Edx; ESI=$nativeRow.Esi; EDI=$nativeRow.Edi; EBP=$nativeRow.Ebp
            }
            StackArguments=@($nativeRow.StackArguments)
            ThisPointerCandidate=$nativeRow.Ecx
            RuntimeObservation=$true
            RegisterEvidenceCaptured=[bool]$nativeRow.RegisterEvidenceCaptured
            StackEvidenceCaptured=[bool]$nativeRow.StackEvidenceCaptured
            ReturnRegisterEvidenceCaptured=[bool]$nativeRow.ReturnEvidenceCaptured
            ConsumerEvidenceCaptured=[bool]$nativeRow.ConsumerEvidenceCaptured
            ConsumerStepIndex=[int]$nativeRow.ConsumerStepIndex
            InstructionRVA=if([uint32]$nativeRow.InstructionRva -ne 0){
                '0x{0:X8}' -f [uint32]$nativeRow.InstructionRva
            }else{$null}
            InstructionBytesHex=[string]$nativeRow.InstructionBytesHex
            ReturnLifetimeEvidenceCaptured=[bool]$nativeRow.ConsumerEvidenceCaptured
            ObjectReuseEvidenceCaptured=$false
            ActiveHook=$false; HardwareBreakpointObserver=$true; ProductionHook=$false
            MemoryWritten=$false; GameplayStateModified=$false
            ExtraNetworkTrafficGenerated=$false; SensitivePayloadPersisted=$false
            PromotionEligible=$false
            Status=if([string]$nativeRow.Phase -ceq 'Return'){
                'RUNTIME_RETURN_OBSERVED_LIFETIME_AND_TYPED_CONTRACT_UNVERIFIED'
            }elseif([string]$nativeRow.Phase -ceq 'ConsumerStep'){
                'RUNTIME_CALLER_CONSUMER_STEP_OBSERVED_SEMANTIC_CONTRACT_UNVERIFIED'
            }else{'RUNTIME_ENTRY_OBSERVED_ABI_AND_TYPED_CONTRACT_UNVERIFIED'}
        }
        foreach ($key in $authority.Keys) { $row[$key]=$authority[$key] }
        [void]$runtimeObservations.Add([pscustomobject]$row)
    }
}
$correlationGroups = @($runtimeObservations | Group-Object CandidateId,ThreadId,InvocationId)
foreach($group in $correlationGroups){
    $entry = @($group.Group | Where-Object Phase -ceq 'Entry' | Select-Object -First 1)
    $return = @($group.Group | Where-Object Phase -ceq 'Return' | Select-Object -First 1)
    $caller = @($group.Group | Where-Object Phase -ceq 'ConsumerStep' |
        Sort-Object ConsumerStepIndex | Select-Object -First 1)
    $entryId = if($entry.Count){[string]$entry[0].ObservationId}else{$null}
    $returnId = if($return.Count){[string]$return[0].ObservationId}else{$null}
    $callerId = if($caller.Count){[string]$caller[0].ObservationId}else{$null}
    foreach($row in $group.Group){
        $row | Add-Member -NotePropertyName EntryEventId -NotePropertyValue $entryId -Force
        $row | Add-Member -NotePropertyName ReturnEventId -NotePropertyValue $returnId -Force
        $row | Add-Member -NotePropertyName CallerEventId -NotePropertyValue $callerId -Force
        $row | Add-Member -NotePropertyName CorrelationComplete -NotePropertyValue `
            ($null -ne $entryId -and $null -ne $returnId -and $null -ne $callerId) -Force
    }
}
$ended = [DateTimeOffset]::UtcNow

$abiRows = [Collections.Generic.List[object]]::new()
$threadRows = [Collections.Generic.List[object]]::new()
$reentrancyRows = [Collections.Generic.List[object]]::new()
$stabilityRows = [Collections.Generic.List[object]]::new()
$consumerRows = [Collections.Generic.List[object]]::new()
$promotionRows = [Collections.Generic.List[object]]::new()
$gateNames = @('ExactTargetIdentity','ExecutableSection','ExactCandidateBytes',
    'CallingConventionVerified','TypedRuntimeEvidence','RepeatedCausalObservation',
    'ContradictionsResolved','StableObjectOrContext','VerifiedConsumerOrMutation',
    'SensitiveMaskContract','ArgumentContractVerified','ReturnValueLifetimeVerified',
    'ThreadContextVerified','ReentrancyRiskVerified')
foreach ($candidate in $selected) {
    $candidateId = [string](Get-Property $candidate 'CandidateId')
    $candidateStatic = @($staticObservations | Where-Object CandidateId -ceq $candidateId)
    $candidateRuntime = @($runtimeObservations | Where-Object CandidateId -ceq $candidateId)
    $entryRows = @($candidateRuntime | Where-Object {
        $_.Phase -ceq 'Entry' -and [bool]$_.ABIEntryEvidenceValid
    })
    $invalidEntryRows = @($candidateRuntime | Where-Object {
        $_.Phase -ceq 'Entry' -and -not [bool]$_.ABIEntryEvidenceValid
    })
    $returnRows = @($candidateRuntime | Where-Object Phase -ceq 'Return')
    $cleanupValues = @($returnRows | Where-Object {$null -ne $_.StackCleanupBytes} |
        ForEach-Object {[int]$_.StackCleanupBytes} | Select-Object -Unique)
    $stableCode = $candidateStatic.Count -gt 0 -and
        @($candidateStatic | Where-Object { -not $_.CodeStable }).Count -eq 0
    $registerEvidence = $entryRows.Count -gt 0 -and
        @($entryRows | Where-Object {-not $_.RegisterEvidenceCaptured}).Count -eq 0
    $stackEvidence = $entryRows.Count -gt 0 -and
        @($entryRows | Where-Object {-not $_.StackEvidenceCaptured}).Count -eq 0
    $entryThreadIds = @($entryRows | ForEach-Object { [uint32]$_.ThreadId } |
        Select-Object -Unique | Sort-Object)
    $nestedCount = @($entryRows | Where-Object NestedInvocation).Count
    $recursiveCount = @($entryRows | Where-Object SameCandidateRecursion).Count
    $thisTokens = @($entryRows | ForEach-Object {$_.ThisPointerCandidate.StableToken} |
        Where-Object {-not [string]::IsNullOrWhiteSpace([string]$_)})
    $stableThisToken = $thisTokens.Count -ge 3 -and
        @($thisTokens | Select-Object -Unique).Count -eq 1
    [void]$abiRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
        CallingConventionCandidate=[string](Get-Property $candidate 'CallingConventionCandidate')
        ArgumentCandidates=@(Get-Property $candidate 'ArgumentCandidates')
        ObservationCount=$candidateRuntime.Count; EntryObservationCount=$entryRows.Count
        InvalidEntryObservationCount=$invalidEntryRows.Count
        ReturnObservationCount=$returnRows.Count; RegisterEvidenceCaptured=$registerEvidence
        StackEvidenceCaptured=$stackEvidence; StableStackCleanupObserved=($returnRows.Count -ge 3 -and $cleanupValues.Count -eq 1)
        ObservedStackCleanupBytes=$cleanupValues; CallingConventionVerified=$false
        ArgumentContractVerified=$false
        ReturnValueLifetimeVerified=$false; PromotionEligible=$false
        Status=if($entryRows.Count -eq 0){'NOT_TRIGGERED_IN_SESSION'}elseif($returnRows.Count -eq 0){
            'EVIDENCE_BLOCKED_RETURN_OBSERVATION_REQUIRED'
        }else{'RUNTIME_ABI_OBSERVED_TYPED_ARGUMENT_AND_LIFETIME_GATES_PENDING'}
    })
    [void]$threadRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
        ProcessThreadIdsObserved=@($observedThreadIds | Sort-Object)
        CandidateEntryThreadIds=$entryThreadIds; ObservationCount=$entryRows.Count
        ThreadContextObserved=($entryRows.Count -gt 0); ThreadContextVerified=$false
        PromotionEligible=$false; Status=if($entryRows.Count -gt 0){
            'THREAD_CONTEXT_OBSERVED_SEMANTIC_ROLE_UNVERIFIED'
        }else{'NOT_TRIGGERED_IN_SESSION'}
    })
    [void]$reentrancyRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
        NestedEntryCount=$nestedCount; SameCandidateRecursionCount=$recursiveCount
        ConcurrentEntryCount=0; EntryObservationCount=$entryRows.Count
        ReentrancyObserved=($nestedCount -gt 0 -or $recursiveCount -gt 0)
        ReentrancyRiskVerified=$false; PromotionEligible=$false
        Status=if($entryRows.Count -gt 0){'REENTRANCY_OBSERVED_OR_BOUNDED_ABSENCE_NOT_PROOF'}else{
            'NOT_TRIGGERED_IN_SESSION'}
    })
    [void]$stabilityRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
        CodeStable=$stableCode; StableObjectOrContext=$false
        ThisPointerTokenObservationCount=$thisTokens.Count
        StableThisPointerTokenObserved=$stableThisToken; ObjectAddressPersisted=$false
        AllocationGenerationObserved=$false; AddressReuseObserved=$false
        ReturnObserved=($returnRows.Count -gt 0); ReturnLifetimeObserved=$false
        ObservationCount=$candidateRuntime.Count; PromotionEligible=$false
        Status=if($stableThisToken){'STABLE_SESSION_TOKEN_OBSERVED_TYPED_OBJECT_IDENTITY_UNVERIFIED'}elseif($entryRows.Count -gt 0){
            'RUNTIME_VALUES_OBSERVED_STABLE_OBJECT_IDENTITY_UNVERIFIED'
        }else{'NOT_TRIGGERED_IN_SESSION'}
    })
    $consumer = Get-Property $candidate 'ConsumerRelation'
    [void]$consumerRows.Add([pscustomobject][ordered]@{
        CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
        StaticConsumerRelation=$consumer; RuntimeConsumerObserved=$false
        VerifiedConsumerOrMutation=$false; PromotionEligible=$false
        Status=if($entryRows.Count -gt 0){'EVIDENCE_BLOCKED_CONSUMER_LINK_NOT_CAUSALLY_OBSERVED'}else{
            'NOT_TRIGGERED_IN_SESSION'}
    })
    $candidateGates = Get-Property $candidate 'PromotionGates'
    foreach ($gate in $gateNames) {
        $passed = $gate -in @('ExactTargetIdentity','ExecutableSection','ExactCandidateBytes') -and
            [bool](Get-Property $candidateGates $gate)
        [void]$promotionRows.Add([pscustomobject][ordered]@{
            CandidateId=$candidateId; Domain=[string](Get-Property $candidate 'Domain')
            Gate=$gate; Passed=$passed; Failed=$false; EvidenceBlocked=(-not $passed)
            EvidenceRefs=@('deep-probe-candidate-map-v3.json','candidate-runtime-observations.jsonl')
            TestedAtUtc=$ended.ToString('o'); ActivationAllowed=$false; PromotionEligible=$false
        })
    }
}

$entryBreakpointRows = @($selected | ForEach-Object {
    $id=[string](Get-Property $_ 'CandidateId')
    $rows=@($runtimeObservations|Where-Object CandidateId -ceq $id)
    $entry=@($rows|Where-Object Phase -ceq 'Entry')
    $returns=@($rows|Where-Object Phase -ceq 'Return')
    $callers=@($rows|Where-Object Phase -ceq 'ConsumerStep')
    $functionStart=[string]$functionStartRvasByCandidateId[$id]
    $breakpoint=[string]$probeRvasByCandidateId[$id]
    [pscustomobject][ordered]@{
        CandidateId=$id;Domain=[string](Get-Property $_ 'Domain')
        CandidateRVA=[string]$candidateRvasByCandidateId[$id]
        FunctionStartRVA=$functionStart;BreakpointRVA=$breakpoint
        PreviousInternalProbeRVA=[string]$previousInternalProbeRvasByCandidateId[$id]
        FunctionStartMatched=($functionStart -ceq $breakpoint)
        ABIEntryEvidenceValid=($entry.Count -gt 0 -and
            $functionStart -ceq $breakpoint -and
            @($entry|Where-Object ABIEntryEvidenceValid).Count -eq $entry.Count)
        EntryObservationCount=$entry.Count;ReturnObservationCount=$returns.Count
        CallerObservationCount=$callers.Count
        CompleteCorrelationCount=@($entry|Where-Object CorrelationComplete).Count
        EntryEventIds=@($entry.ObservationId);ReturnEventIds=@($returns.ObservationId)
        CallerEventIds=@($callers.ObservationId);ThreadIds=@($rows.ThreadId|Sort-Object -Unique)
        CallingConventionVerified=$false;ArgumentContractVerified=$false
        ReturnValueLifetimeVerified=$false;PromotionEligible=$false
    }
})

$paths = [ordered]@{
    RuntimeObservations=Join-Path $OutputRoot 'candidate-runtime-observations.jsonl'
    Abi=Join-Path $OutputRoot 'candidate-abi-report.json'
    Thread=Join-Path $OutputRoot 'candidate-thread-context.json'
    Reentrancy=Join-Path $OutputRoot 'candidate-reentrancy-report.json'
    ObjectStability=Join-Path $OutputRoot 'candidate-object-stability.json'
    ConsumerLinks=Join-Path $OutputRoot 'candidate-consumer-links.jsonl'
    Promotion=Join-Path $OutputRoot 'candidate-promotion-result.json'
    Observer=Join-Path $OutputRoot 'contract-acquisition-observer-result.json'
    EntryBreakpoint=Join-Path $OutputRoot 'entry-breakpoint-report.json'
    Manifest=Join-Path $OutputRoot 'contract-acquisition-manifest.json'
}
Write-JsonLines $paths.RuntimeObservations @($staticObservations + $runtimeObservations)
Write-JsonLines $paths.ConsumerLinks @($consumerRows)
Write-Json $paths.Abi ([ordered]@{SchemaVersion='god2-candidate-abi-report-v2';Authority=$authority;CandidateCount=$selected.Count;RuntimeObservedCount=@($abiRows|Where-Object {$_.EntryObservationCount -gt 0}).Count;VerifiedCount=0;Status=if(@($runtimeObservations).Count -gt 0){'RUNTIME_ABI_OBSERVED_PROMOTION_GATES_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($abiRows)})
Write-Json $paths.Thread ([ordered]@{SchemaVersion='god2-candidate-thread-context-v2';Authority=$authority;CandidateCount=$selected.Count;ObservedCount=@($threadRows|Where-Object ThreadContextObserved).Count;VerifiedCount=0;Status=if(@($runtimeObservations).Count -gt 0){'THREAD_CONTEXT_OBSERVED_ROLE_GATES_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($threadRows)})
Write-Json $paths.Reentrancy ([ordered]@{SchemaVersion='god2-candidate-reentrancy-report-v2';Authority=$authority;CandidateCount=$selected.Count;ObservedCount=@($reentrancyRows|Where-Object {$_.EntryObservationCount -gt 0}).Count;VerifiedCount=0;Status=if(@($runtimeObservations).Count -gt 0){'REENTRANCY_OBSERVATION_CAPTURED_VERIFICATION_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($reentrancyRows)})
Write-Json $paths.ObjectStability ([ordered]@{SchemaVersion='god2-candidate-object-stability-v2';Authority=$authority;CandidateCount=$selected.Count;StableTokenObservedCount=@($stabilityRows|Where-Object StableThisPointerTokenObserved).Count;VerifiedCount=0;Status=if(@($runtimeObservations).Count -gt 0){'SESSION_POINTER_STABILITY_OBSERVED_TYPED_IDENTITY_PENDING'}else{'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'};Rows=@($stabilityRows)})
Write-Json $paths.Promotion ([ordered]@{
    SchemaVersion='god2-candidate-promotion-result-v1';Authority=$authority
    CandidateCount=$selected.Count;GateCountPerCandidate=14;LedgerRowCount=$promotionRows.Count
    PassedGateRowCount=@($promotionRows|Where-Object Passed).Count
    FailedGateRowCount=0;EvidenceBlockedGateRowCount=@($promotionRows|Where-Object EvidenceBlocked).Count
    ActivationAllowedCount=0;PromotionEligibleCount=0
    Status='EVIDENCE_BLOCKED_NO_DEEP_CANDIDATE_PROMOTED';Rows=@($promotionRows)
})
Write-Json $paths.Observer ([ordered]@{
    SchemaVersion='god2-contract-acquisition-observer-result-v1';Authority=$authority
    ObserverMode='WOW64HardwareExecutionBreakpoint';MaximumCandidateBreakpoints=3
    MaximumReturnBreakpointsPerThread=1;MemoryWritten=$false
    RawPointerPersisted=$false;PointerTokenScope='CurrentSessionOnly'
    Result=$observer;ObserverError=$observerError
})
Write-Json $paths.EntryBreakpoint ([ordered]@{
    SchemaVersion='god2-entry-breakpoint-report-v1';Authority=$authority
    CandidateCount=$selected.Count
    FunctionStartBreakpointContractPassed=(@($entryBreakpointRows|Where-Object {
        -not $_.FunctionStartMatched
    }).Count -eq 0)
    ABIEntryEvidenceValidCount=@($entryBreakpointRows|Where-Object ABIEntryEvidenceValid).Count
    CompleteCorrelationCount=@($entryBreakpointRows|Measure-Object CompleteCorrelationCount -Sum).Sum
    CallingConventionVerifiedCount=0;ArgumentContractVerifiedCount=0
    ReturnValueLifetimeVerifiedCount=0;Rows=$entryBreakpointRows
})

$artifactRows = [Collections.Generic.List[object]]::new()
foreach ($entry in $paths.GetEnumerator()) {
    if ($entry.Key -ceq 'Manifest') { continue }
    [void]$artifactRows.Add([pscustomobject][ordered]@{
        Name=[IO.Path]::GetFileName([string]$entry.Value)
        SHA256=(Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash
        Bytes=[uint64](Get-Item -LiteralPath $entry.Value).Length
    })
}
$manifest = [ordered]@{
    SchemaVersion='god2-contract-acquisition-manifest-v1';GeneratedAtUtc=$ended.ToString('o')
    Authority=$authority;AuthorityProfiles=@('ExactBinaryStaticAnalysis','CurrentOfficialClientLive')
    Mode='PatchlessReadOnlyHardwareBreakpointObserver';ValidationOnly=$true;ProductionHookInstalled=$false
    TargetProcessId=$TargetProcessId;CandidateMapSHA256=$mapSha;CandidateCount=$selected.Count
    EvidenceDrivenRotation=(-not [string]::IsNullOrWhiteSpace($EvidencePlanPath))
    EvidencePlanPath=if($evidencePlan){$planPath}else{''}
    EvidencePlanSHA256=$evidencePlanSha256
    ProbeBindings=@($selected | ForEach-Object {
        $id=[string](Get-Property $_ 'CandidateId')
        [pscustomobject][ordered]@{
            CandidateId=$id;CandidateRVA=[string]$candidateRvasByCandidateId[$id]
            ProbeRVA=[string]$probeRvasByCandidateId[$id]
            FunctionStartRVA=[string]$functionStartRvasByCandidateId[$id]
            BreakpointRVA=[string]$probeRvasByCandidateId[$id]
            PreviousInternalProbeRVA=[string]$previousInternalProbeRvasByCandidateId[$id]
            FunctionStartMatched=([string]$functionStartRvasByCandidateId[$id] -ceq
                [string]$probeRvasByCandidateId[$id])
            FunctionEntryProbe=([string]$functionStartRvasByCandidateId[$id] -ceq
                [string]$probeRvasByCandidateId[$id])
        }
    })
    RequestedDomains=@($requestedDomains)
    ObservedDomains=@($selected|ForEach-Object{[string](Get-Property $_ 'Domain')})
    CandidateIds=@($selected|ForEach-Object{[string](Get-Property $_ 'CandidateId')})
    StartedAtUtc=$started.ToString('o');EndedAtUtc=$ended.ToString('o')
    ObserveSeconds=$ObserveSeconds;IntervalMilliseconds=$IntervalMilliseconds
    MaximumObservationsPerCandidate=$MaximumObservationsPerCandidate
    StaticObservationCount=$staticObservations.Count
    RuntimeObservationCount=$runtimeObservations.Count;ObservedThreadCount=$observedThreadIds.Count
    TotalBytesRead=$totalBytesRead;ReadFailureCount=$readFailureCount
    MemoryWritten=$false;GameplayStateModified=$false;ExtraNetworkTrafficGenerated=$false
    DebuggerAttached=if($null -ne $observer){[bool]$observer.Attached}else{$false}
    DebuggerDetached=if($null -ne $observer){[bool]$observer.Detached}else{$false}
    DebugRegistersCleared=if($null -ne $observer){[bool]$observer.DebugRegistersCleared}else{$false}
    RegisterEvidenceCaptured=(@($runtimeObservations|Where-Object RegisterEvidenceCaptured).Count -gt 0)
    StackEvidenceCaptured=(@($runtimeObservations|Where-Object StackEvidenceCaptured).Count -gt 0)
    ReturnRegisterEvidenceCaptured=(@($runtimeObservations|Where-Object ReturnRegisterEvidenceCaptured).Count -gt 0)
    ReturnLifetimeEvidenceCaptured=(@($runtimeObservations|Where-Object ConsumerEvidenceCaptured).Count -gt 0)
    FunctionStartBreakpointContractPassed=(@($entryBreakpointRows|Where-Object {
        -not $_.FunctionStartMatched
    }).Count -eq 0)
    EntryReturnCallerCorrelationCount=@($entryBreakpointRows|Measure-Object CompleteCorrelationCount -Sum).Sum
    ActivationAllowedCount=0;PromotionEligibleCount=0
    Status=if($null -eq $observer -or -not [bool]$observer.Attached -or
        -not [bool]$observer.Detached -or -not [bool]$observer.DebugRegistersCleared){
        'ULTIMATE_CONTRACT_ACQUISITION_EVIDENCE_BLOCKED'
    }elseif([int]$observer.EntryObservationCount -eq 0){
        'EVIDENCE_BLOCKED_CANDIDATES_NOT_TRIGGERED_IN_SESSION'
    }else{'CONTRACT_ACQUISITION_RUNTIME_OBSERVED_PROMOTION_GATES_PENDING'}
    Artifacts=@($artifactRows)
}
Write-Json $paths.Manifest $manifest

[pscustomobject]@{
    Passed=($null -ne $observer -and [bool]$observer.Attached -and
        [bool]$observer.Detached -and [bool]$observer.DebugRegistersCleared -and
        [int]$observer.ThreadConfigurationFailureCount -eq 0 -and $readFailureCount -eq 0)
    OutputRoot=(Resolve-Path -LiteralPath $OutputRoot).Path
    CandidateCount=$selected.Count;StaticObservationCount=$staticObservations.Count
    RuntimeObservationCount=$runtimeObservations.Count
    EntryObservationCount=if($null -ne $observer){[int]$observer.EntryObservationCount}else{0}
    ReturnObservationCount=if($null -ne $observer){[int]$observer.ReturnObservationCount}else{0}
    ReadFailureCount=$readFailureCount;TotalBytesRead=$totalBytesRead
    ActivationAllowedCount=0;PromotionEligibleCount=0;Status=$manifest.Status
}
