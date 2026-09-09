[CmdletBinding(DefaultParameterSetName = 'Build')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Build')]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Build')]
    [string[]]$TestReportRoot,

    [Parameter(ParameterSetName = 'Build')]
    [string[]]$SchemaRoot = @(),

    [Parameter(ParameterSetName = 'Build')]
    [string]$ServerDbImporterRun,

    [Parameter(ParameterSetName = 'Build')]
    [string]$ValidatedRtx5070ResultPath,

    [Parameter(ParameterSetName = 'Build')]
    [string]$ValidatedRtx5070AttestationPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [switch]$ValidateOnly,

    [Parameter(Mandatory = $true, ParameterSetName = 'ParserSelfTest')]
    [switch]$ParserSelfTest,

    [Parameter(ParameterSetName = 'ParserSelfTest')]
    [string]$BuilderSelfTestExecutablePath,

    [string]$OutputRoot
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assets\test-run\PortableEvidence.ps1')
. (Join-Path $PSScriptRoot 'assets\test-run\NativeAnalysisContract.ps1')

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:ProductName = 'God2 Semantic Recovery Engine'
$script:ProductVersion = '1.3.0.0'
$script:DisplayVersion = 'v1.3.0 Ultimate'
$script:Channel = 'Ultimate Private'
$script:ValidatedInputSnapshots = $null
$script:ArtifactManifestJson = 'artifact-sha256.json'
$script:ArtifactManifestCsv = 'artifact-sha256.csv'
$script:TargetClientSha256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'
$script:TestRunManifestName = 'ultimate-test-run-manifest.json'
$script:ExactClientValidationStatus = 'ExactClientIdentityComputedAndValidated'
$script:RequiredUltimateOperations = @(
    'TraceFeatureExtraction',
    'CrossSessionCorrelation',
    'ParserFieldPatternMatching',
    'FunctionClustering',
    'ObjectClustering',
    'ClassLayoutScoring',
    'RegistryScoring',
    'HeapGraphSimilarity',
    'ValueFlowAggregation',
    'TaintGraphBatch',
    'SemanticEdgeScoring',
    'ApproximateNearestNeighbor',
    'SequenceMining',
    'FsmScoring',
    'FormulaBatchEvaluation',
    'ReplayStateComparison',
    'AiInference'
)
$script:RequiredNonAiGpuOperations = @(
    'TraceFeatureExtraction',
    'CrossSessionCorrelation',
    'ParserFieldPatternMatching',
    'FunctionClustering',
    'ObjectClustering',
    'ClassLayoutScoring',
    'RegistryScoring',
    'HeapGraphSimilarity',
    'ValueFlowAggregation',
    'TaintGraphBatch',
    'SemanticEdgeScoring',
    'ApproximateNearestNeighbor',
    'SequenceMining',
    'FsmScoring',
    'FormulaBatchEvaluation',
    'ReplayStateComparison'
)
$script:RequiredBundlePaths = @(
    'authority/derived.jsonl', 'authority/hypothesis.jsonl', 'authority/observed.jsonl',
    'authority/rejected.jsonl', 'authority/unknown-server-only.jsonl', 'authority/unknown.jsonl',
    'authority/verified.jsonl', 'codex/CODEX-APPLY-DIRECTIVE.md',
    'codex/database-gap-plan.json', 'codex/recovery-contract.json', 'codex/server-gap-plan.json',
    'codex/validation-plan.json', 'coverage/contradictions.jsonl',
    'coverage/missing-evidence-queue.json', 'coverage/recovery-coverage.json',
    'graph/god2-knowledge-graph.jsonl', 'graph/graph-index.json',
    'integration/blocked-items.json', 'integration/database-ready.json',
    'integration/migration-ready.json', 'integration/replay-ready.json',
    'integration/server-ready.json', 'manifest/build-identity.json',
    'manifest/environment.json', 'manifest/integrity.json',
    'protocol/language-neutral-idl.json', 'protocol/packet-catalog.jsonl',
    'protocol/parser-schemas.jsonl', 'protocol/protocol-state-machines.json',
    'protocol/request-response.jsonl', 'protocol/serializer-schemas.jsonl',
    'provenance/evidence-ledger.jsonl', 'provenance/resource-provenance.jsonl',
    'provenance/taint.jsonl', 'provenance/value-flow.jsonl',
    'provenance/value-provenance.jsonl', 'raw/README.json', 'replay/oracle-contract.json',
    'replay/semantic-replay/replay-index.json', 'runtime/formulas.json',
    'runtime/gameplay-rules.json', 'runtime/object-layouts.json',
    'runtime/runtime-state-machines.json', 'runtime/state-mutations.jsonl'
)
$script:ReportNames = @(
    'ULTIMATE-IMPLEMENTATION-REPORT.md',
    'ULTIMATE-ARCHITECTURE-REPORT.md',
    'ULTIMATE-PROBE-MAP-REPORT.md',
    'ULTIMATE-GPU-REPORT.md',
    'ULTIMATE-AI-ML-REPORT.md',
    'ULTIMATE-RECOVERY-BUNDLE-REPORT.md',
    'ULTIMATE-SERVER-DB-INTEGRATION-REPORT.md',
    'ULTIMATE-VALIDATION-REPORT.md',
    'ULTIMATE-DEEP-RECOVERY-CLOSURE-REPORT.md',
    'ULTIMATE-AUTHORITY-CONSISTENCY-REPORT.md',
    'ULTIMATE-DEEP-PROBE-PROMOTION-REPORT.md',
    'ULTIMATE-RING-RELIABILITY-REPORT.md',
    'ULTIMATE-GPU-ML-REPORT.md',
    'ULTIMATE-RECOVERY-READINESS-REPORT.md'
)

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-ToSlashPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return $Path.Replace('\', '/')
}

function Test-IsWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Candidate
    )
    $prefix = (Get-FullPath $Parent).TrimEnd('\') + '\'
    return (Get-FullPath $Candidate).StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $value = Convert-ToSlashPath $RelativePath
    if ([string]::IsNullOrWhiteSpace($value) -or [System.IO.Path]::IsPathRooted($value) -or
        $value.Contains(':') -or $value.StartsWith('/') -or $value.EndsWith('/')) {
        throw "Unsafe relative path: '$RelativePath'"
    }
    $segments = $value.Split('/')
    if ($segments -contains '..' -or $segments -contains '.' -or $segments -contains '') {
        throw "Unsafe relative path: '$RelativePath'"
    }
}

function Assert-NoReparsePoints {
    param([Parameter(Mandatory = $true)][string]$Root)
    $rootItem = Get-Item -LiteralPath $Root -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse-point input is prohibited: $($rootItem.FullName)"
    }
    if ($rootItem.PSIsContainer) {
        $reparse = Get-ChildItem -LiteralPath $rootItem.FullName -Recurse -Force -ErrorAction Stop |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($null -ne $reparse) { throw "Reparse point is prohibited under input root: $($reparse.FullName)" }
    }
}

function Resolve-ImporterRunRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [switch]$AllowRunRoot
    )
    $root = Get-FullPath $RunRoot
    Assert-NoReparsePoints $root
    $value = Convert-ToSlashPath $RelativePath
    if ($value -eq '.') {
        if (-not $AllowRunRoot) { throw "Importer path may not resolve to run root: '$RelativePath'" }
        return $root
    }
    Assert-SafeRelativePath $value
    $resolved = Get-FullPath (Join-Path $root ($value.Replace('/', '\')))
    if (-not (Test-IsWithin $root $resolved)) {
        throw "Importer path escapes candidate run root: '$RelativePath'"
    }
    return $resolved
}

function Write-NewUtf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    if (Test-Path -LiteralPath $Path) { throw "Refusing to overwrite: $Path" }
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory)
    }
    [System.IO.File]::WriteAllText($Path, $Content, $script:Utf8NoBom)
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '')
    } finally {
        $sha.Dispose()
    }
}

function Get-InputSnapshotKey {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FullPath $Path).ToLowerInvariant()
}

function New-InputFileSnapshotMap {
    param([Parameter(Mandatory = $true)][object[]]$Files)
    $snapshots = @{}
    foreach ($candidate in $Files) {
        $path = if ($candidate -is [System.IO.FileSystemInfo]) { $candidate.FullName } else { [string]$candidate }
        $full = Get-FullPath $path
        $key = Get-InputSnapshotKey $full
        if ($snapshots.ContainsKey($key)) { continue }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Snapshot input is missing: $full" }
        Assert-NoReparsePoints $full
        $before = Get-Item -LiteralPath $full -Force
        $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
        $after = Get-Item -LiteralPath $full -Force
        if ($before.Length -ne $after.Length -or
            $before.CreationTimeUtc.Ticks -ne $after.CreationTimeUtc.Ticks -or
            $before.LastWriteTimeUtc.Ticks -ne $after.LastWriteTimeUtc.Ticks) {
            throw "Input changed while its validated snapshot was created: $full"
        }
        $snapshots[$key] = [pscustomobject][ordered]@{
            Path = $full
            PathIdentity = $key
            SizeBytes = [int64]$after.Length
            SHA256 = $hash
            CreationTimeUtcTicks = [int64]$after.CreationTimeUtc.Ticks
            LastWriteTimeUtcTicks = [int64]$after.LastWriteTimeUtc.Ticks
        }
    }
    return $snapshots
}

function Assert-InputFileMatchesSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Snapshots
    )
    $full = Get-FullPath $Path
    $key = Get-InputSnapshotKey $full
    if (-not $Snapshots.ContainsKey($key)) { throw "Input was not part of the validated snapshot: $full" }
    $expected = $Snapshots[$key]
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Validated input disappeared: $full" }
    Assert-NoReparsePoints $full
    $item = Get-Item -LiteralPath $full -Force
    if ([int64]$item.Length -ne [int64]$expected.SizeBytes -or
        [int64]$item.CreationTimeUtc.Ticks -ne [int64]$expected.CreationTimeUtcTicks -or
        [int64]$item.LastWriteTimeUtc.Ticks -ne [int64]$expected.LastWriteTimeUtcTicks -or
        (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne [string]$expected.SHA256) {
        throw "Validated input changed after inspection: $full"
    }
    return $expected
}

function Assert-InputSnapshotExpectedBinding {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$SHA256,
        [Parameter(Mandatory = $true)][int64]$SizeBytes,
        [Parameter(Mandatory = $true)][hashtable]$Snapshots,
        [Parameter(Mandatory = $true)][string]$Context
    )
    $record = Assert-InputFileMatchesSnapshot $Path $Snapshots
    if ([string]$record.SHA256 -ine $SHA256 -or [int64]$record.SizeBytes -ne $SizeBytes) {
        throw "Validated input snapshot no longer matches $Context`: $Path"
    }
}

function Copy-ValidatedInputFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][hashtable]$Snapshots
    )
    $expected = Assert-InputFileMatchesSnapshot $Source $Snapshots
    if (Test-Path -LiteralPath $Destination) { throw "Refusing to overwrite staged input: $Destination" }
    $directory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }
    Copy-Item -LiteralPath (Get-FullPath $Source) -Destination $Destination
    [void](Assert-InputFileMatchesSnapshot $Source $Snapshots)
    $staged = Get-Item -LiteralPath $Destination -Force
    if ([int64]$staged.Length -ne [int64]$expected.SizeBytes -or
        (Get-FileHash -LiteralPath $staged.FullName -Algorithm SHA256).Hash -ne [string]$expected.SHA256) {
        throw "Staged input bytes do not match the validated snapshot: $Source"
    }
}

function Assert-AllInputSnapshotsUnchanged {
    param([Parameter(Mandatory = $true)][hashtable]$Snapshots)
    foreach ($record in $Snapshots.Values) {
        [void](Assert-InputFileMatchesSnapshot ([string]$record.Path) $Snapshots)
    }
}

function Assert-ReportRootInventoryUnchanged {
    param(
        [Parameter(Mandatory = $true)][string[]]$ReportRoots,
        [Parameter(Mandatory = $true)][object[]]$ExpectedReportFiles
    )
    $expected = @{}
    foreach ($entry in $ExpectedReportFiles) {
        $key = ('{0}:{1}' -f $entry.RootIndex, ([string]$entry.RelativePath).ToLowerInvariant())
        if ($expected.ContainsKey($key)) { throw "Duplicate expected report inventory path: $key" }
        $expected[$key] = $true
    }
    $observed = @{}
    for ($index = 0; $index -lt $ReportRoots.Count; $index++) {
        $root = Get-FullPath $ReportRoots[$index]
        Assert-NoReparsePoints $root
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            if (-not (Test-IsWithin $root $file.FullName)) { throw "Test report path escaped during snapshot: $($file.FullName)" }
            $relative = Convert-ToSlashPath $file.FullName.Substring($root.TrimEnd('\').Length + 1)
            Assert-SafeRelativePath $relative
            $key = ('{0}:{1}' -f ($index + 1), $relative.ToLowerInvariant())
            if ($observed.ContainsKey($key)) { throw "Duplicate observed report inventory path: $key" }
            $observed[$key] = $true
        }
    }
    if ($observed.Count -ne $expected.Count) { throw 'Test-report root inventory changed after validation' }
    foreach ($key in $expected.Keys) {
        if (-not $observed.ContainsKey($key)) { throw "Test-report root inventory changed after validation: $key" }
    }
}

function Get-ObjectProperty {
    param(
        [object]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names
    )
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($name in $Names) {
            foreach ($key in $Object.Keys) {
                if ([string]$key -ieq $name) { return $Object[$key] }
            }
        }
        return $null
    }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties | Where-Object { $_.Name -ieq $name } | Select-Object -First 1
        if ($null -ne $property) { return $property.Value }
    }
    return $null
}

function Convert-ToInt64OrDefault {
    param([object]$Value, [int64]$Default = 0)
    if ($null -eq $Value) { return $Default }
    $parsed = [int64]0
    if ([int64]::TryParse([string]$Value, [ref]$parsed)) { return $parsed }
    return $Default
}

function Test-PassValue {
    param([object]$Value)
    if ($Value -is [bool]) { return [bool]$Value }
    return @('PASS', 'PASSED', 'TRUE', 'AVAILABLE', 'READY') -contains ([string]$Value).ToUpperInvariant()
}

function New-Gate {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][ValidateSet('PASS', 'FAIL', 'EVIDENCE_BLOCKED_MISSING')][string]$Status,
        [Parameter(Mandatory = $true)][string]$Evidence
    )
    return [pscustomobject][ordered]@{ Name = $Name; Status = $Status; Evidence = $Evidence }
}

function Resolve-RequiredManifestGate {
    param(
        [Parameter(Mandatory = $true)][hashtable]$GateMap,
        [Parameter(Mandatory = $true)][string]$LogicalName,
        [Parameter(Mandatory = $true)][string]$NamePattern
    )
    $matches = @($GateMap.Keys | Where-Object { [string]$_ -cmatch $NamePattern })
    if ($matches.Count -ne 1) {
        throw "Test-run manifest must contain exactly one $LogicalName gate matching $NamePattern; found $($matches.Count)"
    }
    $name = [string]$matches[0]
    return [pscustomobject][ordered]@{ LogicalName = $LogicalName; Name = $name; Definition = $GateMap[$name] }
}

function Assert-ExactObjectPropertySet {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string[]]$ExpectedNames,
        [Parameter(Mandatory = $true)][string]$Context
    )
    if ($null -eq $Object) { throw "$Context is missing" }
    $actual = if ($Object -is [System.Collections.IDictionary]) {
        @($Object.Keys | ForEach-Object { [string]$_ } | Sort-Object)
    } else {
        @($Object.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
    }
    $expected = @($ExpectedNames | Sort-Object)
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        throw "$Context property set mismatch: actual=$($actual -join ',')"
    }
}

function Assert-DllEnhancedCaptureAcceptanceEvidence {
    param(
        [Parameter(Mandatory = $true)][object[]]$JsonRecords,
        [Parameter(Mandatory = $true)][string]$ExecutableSHA256
    )
    $records = @($JsonRecords | Where-Object { $_.Kind -eq 'EnhancedCaptureAcceptanceJson' })
    if ($records.Count -ne 1) {
        throw "Implementation-complete publication requires exactly one EnhancedCaptureAcceptanceJson; found $($records.Count)"
    }
    $report = $records[0].Data
    Assert-ExactObjectPropertySet $report @('schemaVersion','generatedAtUtc','repositoryRoot','status',
        'standalonePayloadStatus','finalExeUiStatus','expectedTarget','authorityConsistency',
        'deepRecoveryEvidence','payloads','domainInventory','runtimeEvidence','inputBindings',
        'freshness','selfTests','guiVisibility','checks','summary') 'Enhanced capture acceptance root'
    $generatedAt = [DateTimeOffset]::MinValue
    if ([string](Get-ObjectProperty $report @('schemaVersion')) -cne 'god2-enhanced-capture-acceptance-v2' -or
        -not [DateTimeOffset]::TryParse([string](Get-ObjectProperty $report @('generatedAtUtc')), [ref]$generatedAt) -or
        [string](Get-ObjectProperty $report @('repositoryRoot')) -cne '.' -or
        [string](Get-ObjectProperty $report @('status')) -cne 'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS' -or
        [string](Get-ObjectProperty $report @('standalonePayloadStatus')) -cne 'PASS' -or
        [string](Get-ObjectProperty $report @('finalExeUiStatus')) -cne 'PASS') {
        throw 'DLL enhanced capture acceptance is not an exact final PASS report'
    }

    $expectedTarget = Get-ObjectProperty $report @('expectedTarget')
    Assert-ExactObjectPropertySet $expectedTarget @('executable','architecture','version','sha256','activationStatus') `
        'Enhanced capture expected target'
    if ([string](Get-ObjectProperty $expectedTarget @('executable')) -cne 'God2_opt.exe' -or
        [string](Get-ObjectProperty $expectedTarget @('architecture')) -cne 'x86' -or
        [string](Get-ObjectProperty $expectedTarget @('version')) -cne '1.0.0.1' -or
        [string](Get-ObjectProperty $expectedTarget @('sha256')) -cne $script:TargetClientSha256 -or
        [string](Get-ObjectProperty $expectedTarget @('activationStatus')) -cne
            'CURRENT_OFFICIAL_CLIENT_IDENTITY_VERIFIED') {
        throw 'DLL enhanced capture acceptance target identity/fail-closed contract mismatch'
    }

    $authorityBinding = Get-ObjectProperty $report @('authorityConsistency')
    Assert-ExactObjectPropertySet $authorityBinding @('path','sha256','report') `
        'Enhanced capture authority consistency binding'
    $authorityReport = Get-ObjectProperty $authorityBinding @('report')
    Assert-ExactObjectPropertySet $authorityReport @('SchemaVersion','GeneratedAtUtc','Status',
        'ConflictCount','Conflicts','Records') 'Enhanced capture authority consistency report'
    $authorityGeneratedAt = [DateTimeOffset]::MinValue
    $authorityRecords = @(Get-ObjectProperty $authorityReport @('Records'))
    if ([string](Get-ObjectProperty $authorityBinding @('path')) -notmatch
            '^Artifacts/[A-Za-z0-9._/-]+/authority-consistency-report\.json$' -or
        [string](Get-ObjectProperty $authorityBinding @('sha256')) -notmatch '^[A-F0-9]{64}$' -or
        [string](Get-ObjectProperty $authorityReport @('SchemaVersion')) -cne
            'god2-evidence-authority-consistency-v1' -or
        -not [DateTimeOffset]::TryParse([string](Get-ObjectProperty $authorityReport @('GeneratedAtUtc')),
            [ref]$authorityGeneratedAt) -or
        [string](Get-ObjectProperty $authorityReport @('Status')) -cne 'PASS' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $authorityReport @('ConflictCount')) -1) -ne 0 -or
        @(Get-ObjectProperty $authorityReport @('Conflicts')).Count -ne 0 -or $authorityRecords.Count -lt 2) {
        throw 'DLL enhanced capture authority consistency report is not a zero-conflict PASS'
    }
    $currentOfficialRecords = 0
    $exactFixtureRecords = 0
    foreach ($authorityRecord in $authorityRecords) {
        Assert-ExactObjectPropertySet $authorityRecord @('AuthorityProfile','SessionId','SourceArtifact',
            'SourceSHA256','TargetExecutable','TargetVersion','TargetSHA256','TargetProcessId',
            'TargetProcessCreationTime','CurrentSessionObserved','HistoricalEvidenceReused','FixtureOnly',
            'PromotionEligible','AttachStatus','DetachStatus','PackageSHA256','RuntimeEventCount') `
            'Enhanced capture authority record'
        foreach ($flag in @('CurrentSessionObserved','HistoricalEvidenceReused','FixtureOnly','PromotionEligible')) {
            if ((Get-ObjectProperty $authorityRecord @($flag)) -isnot [bool]) {
                throw "Enhanced capture authority record flag is not boolean: $flag"
            }
        }
        $profile = [string](Get-ObjectProperty $authorityRecord @('AuthorityProfile'))
        if ($profile -ceq 'CurrentOfficialClientLive') {
            $currentOfficialRecords++
            if (-not [bool](Get-ObjectProperty $authorityRecord @('CurrentSessionObserved')) -or
                [bool](Get-ObjectProperty $authorityRecord @('HistoricalEvidenceReused')) -or
                [bool](Get-ObjectProperty $authorityRecord @('FixtureOnly')) -or
                [string](Get-ObjectProperty $authorityRecord @('TargetExecutable')) -cne 'God2_opt.exe' -or
                [string](Get-ObjectProperty $authorityRecord @('TargetVersion')) -cne '1.0.0.1' -or
                [string](Get-ObjectProperty $authorityRecord @('TargetSHA256')) -cne $script:TargetClientSha256 -or
                (Convert-ToInt64OrDefault (Get-ObjectProperty $authorityRecord @('TargetProcessId')) 0) -le 0 -or
                [string]::IsNullOrWhiteSpace([string](Get-ObjectProperty $authorityRecord @('TargetProcessCreationTime'))) -or
                [string](Get-ObjectProperty $authorityRecord @('AttachStatus')) -cne 'ATTACHED' -or
                [string](Get-ObjectProperty $authorityRecord @('DetachStatus')) -cne 'DETACHED' -or
                [string](Get-ObjectProperty $authorityRecord @('PackageSHA256')) -notmatch '^[A-F0-9]{64}$' -or
                (Convert-ToInt64OrDefault (Get-ObjectProperty $authorityRecord @('RuntimeEventCount')) 0) -le 0) {
                throw 'CurrentOfficialClientLive authority record is not bound to the exact current official process/session'
            }
        } elseif ($profile -ceq 'ExactSelfTestClient') {
            $exactFixtureRecords++
            if ([bool](Get-ObjectProperty $authorityRecord @('CurrentSessionObserved')) -or
                [bool](Get-ObjectProperty $authorityRecord @('HistoricalEvidenceReused')) -or
                -not [bool](Get-ObjectProperty $authorityRecord @('FixtureOnly')) -or
                [bool](Get-ObjectProperty $authorityRecord @('PromotionEligible'))) {
                throw 'ExactSelfTestClient authority record attempted to claim current or promotable official evidence'
            }
        }
    }
    if ($currentOfficialRecords -ne 1 -or $exactFixtureRecords -lt 1) {
        throw 'Enhanced capture authority consistency must contain one current official record and an isolated exact fixture record'
    }

    $deep = Get-ObjectProperty $report @('deepRecoveryEvidence')
    Assert-ExactObjectPropertySet $deep @('status','authorityProfile','domainCount','confirmedContractCount',
        'currentOfficialLiveProducerCount','historicalOfficialLiveProducerCount',
        'currentOfficialLiveVerifiedProducerCount','verifiedDeepDomainCount',
        'candidateOnlyBlockedDomainCount','candidateCount','promotionLedgerRowCount','activationAllowedCount',
        'serverReady','databaseReady','artifacts') 'Enhanced capture deep recovery evidence'
    if ([string](Get-ObjectProperty $deep @('status')) -cne 'PASS_FAIL_CLOSED_EVIDENCE_GENERATION' -or
        [string](Get-ObjectProperty $deep @('authorityProfile')) -cne 'CurrentOfficialClientLive' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('domainCount')) -1) -ne 25 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('confirmedContractCount')) -1) -ne 4 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('currentOfficialLiveProducerCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('historicalOfficialLiveProducerCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('currentOfficialLiveVerifiedProducerCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('verifiedDeepDomainCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('candidateOnlyBlockedDomainCount')) -1) -ne 21 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('candidateCount')) -1) -le 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('promotionLedgerRowCount')) -1) -le 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('activationAllowedCount')) -1) -ne 0 -or
        (Get-ObjectProperty $deep @('serverReady')) -isnot [bool] -or
        [bool](Get-ObjectProperty $deep @('serverReady')) -or
        (Get-ObjectProperty $deep @('databaseReady')) -isnot [bool] -or
        [bool](Get-ObjectProperty $deep @('databaseReady'))) {
        throw 'DLL enhanced capture deep recovery evidence is not the required 4-confirmed/21-blocked fail-closed result'
    }
    $deepArtifactRoles = @{}
    foreach ($artifact in @(Get-ObjectProperty $deep @('artifacts'))) {
        Assert-ExactObjectPropertySet $artifact @('role','path','sha256') 'Enhanced capture deep artifact'
        $role = [string](Get-ObjectProperty $artifact @('role'))
        if ([string]::IsNullOrWhiteSpace($role) -or $deepArtifactRoles.ContainsKey($role) -or
            [string](Get-ObjectProperty $artifact @('path')) -notmatch '^Artifacts/[A-Za-z0-9._/-]+$' -or
            [string](Get-ObjectProperty $artifact @('sha256')) -notmatch '^[A-F0-9]{64}$') {
            throw "Enhanced capture deep artifact binding is invalid: $role"
        }
        $deepArtifactRoles[$role] = $true
    }
    foreach ($requiredRole in @('Capability','Runtime','Semantic','CandidateMapV3','PromotionLedger',
            'RuntimeObservations','CandidatePromotionResult','ExactTargetIdentity','RecoveryReadiness')) {
        if (-not $deepArtifactRoles.ContainsKey($requiredRole)) {
            throw "Enhanced capture deep evidence omits required artifact role: $requiredRole"
        }
    }

    $runtimeEvidence = Get-ObjectProperty $report @('runtimeEvidence')
    $probeDomains = Get-ObjectProperty $runtimeEvidence @('probeDomains')
    $bridgeAware = Get-ObjectProperty (Get-ObjectProperty $runtimeEvidence @('bridgeAwareStatusV3')) @('report')
    if ((Get-ObjectProperty $runtimeEvidence @('officialClientLiveEvidenceClaimed')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $runtimeEvidence @('officialClientLiveEvidenceClaimed')) -or
        [string](Get-ObjectProperty $runtimeEvidence @('officialClientLiveGateStatus')) -cne
            'PASS_OFFICIAL_EXACT_CLIENT_RUNTIME' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $probeDomains @('domainCount')) -1) -ne 25 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $probeDomains @('confirmedContractDomainCount')) -1) -ne 4 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $probeDomains @('candidateOnlyBlockedDomainCount')) -1) -ne 21 -or
        (Get-ObjectProperty $probeDomains @('officialTargetIdentityVerified')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $probeDomains @('officialTargetIdentityVerified')) -or
        [string](Get-ObjectProperty $bridgeAware @('AuthorityProfile')) -cne 'TestOnlyExactFixture' -or
        (Get-ObjectProperty $bridgeAware @('OfficialRuntimeObserved')) -isnot [bool] -or
        [bool](Get-ObjectProperty $bridgeAware @('OfficialRuntimeObserved')) -or
        (Get-ObjectProperty $bridgeAware @('ExtendedReady')) -isnot [bool] -or
        [bool](Get-ObjectProperty $bridgeAware @('ExtendedReady')) -or
        (Get-ObjectProperty $bridgeAware @('InternalBridgeReady')) -isnot [bool] -or
        [bool](Get-ObjectProperty $bridgeAware @('InternalBridgeReady'))) {
        throw 'DLL enhanced capture capability/runtime authority separation is inconsistent'
    }

    $inputRoles = @{}
    foreach ($binding in @(Get-ObjectProperty $report @('inputBindings'))) {
        Assert-ExactObjectPropertySet $binding @('role','file') 'Enhanced capture input binding'
        $role = [string](Get-ObjectProperty $binding @('role'))
        $file = Get-ObjectProperty $binding @('file')
        Assert-ExactObjectPropertySet $file @('path','length','sha256','lastWriteTimeUtc') `
            'Enhanced capture input file binding'
        if ([string]::IsNullOrWhiteSpace($role) -or $inputRoles.ContainsKey($role) -or
            [string](Get-ObjectProperty $file @('path')) -notmatch '^[A-Za-z0-9._/-]+$' -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $file @('length')) -1) -le 0 -or
            [string](Get-ObjectProperty $file @('sha256')) -notmatch '^[A-F0-9]{64}$') {
            throw "Enhanced capture input binding is invalid: $role"
        }
        $inputRoles[$role] = $true
    }
    foreach ($requiredRole in @('GUI_SOURCE','MAIN_SOURCE','EVIDENCE_SOURCE','ACCEPTANCE_SCRIPT',
            'ACCEPTANCE_SCHEMA','PROBE_PAYLOAD','INJECTOR_PAYLOAD','NETWORK_EVIDENCE','PRODUCT_EVIDENCE',
            'EVIDENCE_FIXTURE_REPORT','GUI_SMOKE_EVIDENCE')) {
        if (-not $inputRoles.ContainsKey($requiredRole)) {
            throw "Enhanced capture acceptance omits freshness input binding: $requiredRole"
        }
    }

    $freshness = Get-ObjectProperty $report @('freshness')
    foreach ($flag in @('releaseBuiltAfterAllBoundSources','networkEvidenceAfterAllBoundInputs',
            'productEvidenceAfterAllBoundInputs','evidenceFixtureGeneratedAfterRelease',
            'nativeProbeEvidenceAfterAllBoundInputs','executableSelfTestsGeneratedAfterRelease',
            'guiSmokeGeneratedAfterRelease')) {
        if ((Get-ObjectProperty $freshness @($flag)) -isnot [bool] -or
            -not [bool](Get-ObjectProperty $freshness @($flag))) {
            throw "Enhanced capture freshness gate is not true: $flag"
        }
    }
    foreach ($hashField in @('acceptanceScriptSha256','acceptanceSchemaSha256','guiSourceSha256',
            'mainSourceSha256','evidenceSourceSha256','enhancedCaptureStatusSchemaSha256',
            'bridgeAwareStatusV3SchemaSha256','sharedHealthAvailabilitySchemaSha256',
            'candidateMapAvailabilitySchemaSha256')) {
        if ([string](Get-ObjectProperty $freshness @($hashField)) -notmatch '^[A-F0-9]{64}$') {
            throw "Enhanced capture freshness hash is invalid: $hashField"
        }
    }

    $payloads = Get-ObjectProperty $report @('payloads')
    Assert-ExactObjectPropertySet $payloads @('releaseExecutable','probeResource201','injectorResource202') `
        'Enhanced capture payloads'
    $releaseExecutable = Get-ObjectProperty $payloads @('releaseExecutable')
    Assert-ExactObjectPropertySet $releaseExecutable @('path','length','sha256','lastWriteTimeUtc') `
        'Enhanced capture release executable'
    if ([string](Get-ObjectProperty $releaseExecutable @('sha256')) -cne $ExecutableSHA256 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $releaseExecutable @('length')) -1) -le 0) {
        throw 'DLL enhanced capture acceptance is not bound to the exact release executable'
    }
    $requiredProbeExports = @('God2TraceProbeWaitReady','God2TraceProbeStop','God2TraceProbeCanUnload',
        'God2TraceProbeSemanticSelfTest','God2TraceProbeSharedTransportSelfTest')
    foreach ($payloadContract in @(
        [pscustomobject]@{ Name = 'probeResource201'; ResourceId = 201; IsDll = $true },
        [pscustomobject]@{ Name = 'injectorResource202'; ResourceId = 202; IsDll = $false }
    )) {
        $payload = Get-ObjectProperty $payloads @($payloadContract.Name)
        Assert-ExactObjectPropertySet $payload @('resourceId','embeddedLength','embeddedSha256','standalone',
            'machine','optionalHeader','isDll','exports','authenticodeStatus') ('Enhanced capture ' + $payloadContract.Name)
        $standalone = Get-ObjectProperty $payload @('standalone')
        Assert-ExactObjectPropertySet $standalone @('path','length','sha256','lastWriteTimeUtc') `
            ('Enhanced capture ' + $payloadContract.Name + ' standalone')
        $embeddedLength = Convert-ToInt64OrDefault (Get-ObjectProperty $payload @('embeddedLength')) -1
        $standaloneLength = Convert-ToInt64OrDefault (Get-ObjectProperty $standalone @('length')) -1
        $embeddedSha = [string](Get-ObjectProperty $payload @('embeddedSha256'))
        $standaloneSha = [string](Get-ObjectProperty $standalone @('sha256'))
        if ((Convert-ToInt64OrDefault (Get-ObjectProperty $payload @('resourceId')) -1) -ne $payloadContract.ResourceId -or
            $embeddedLength -le 0 -or $embeddedLength -ne $standaloneLength -or
            $embeddedSha -notmatch '^[A-F0-9]{64}$' -or $embeddedSha -cne $standaloneSha -or
            [string](Get-ObjectProperty $payload @('machine')) -cne 'I386' -or
            [string](Get-ObjectProperty $payload @('optionalHeader')) -cne 'PE32' -or
            (Get-ObjectProperty $payload @('isDll')) -isnot [bool] -or
            [bool](Get-ObjectProperty $payload @('isDll')) -ne [bool]$payloadContract.IsDll) {
            throw "Embedded RCDATA $($payloadContract.ResourceId) does not byte-match its exact x86 standalone payload"
        }
        if ($payloadContract.ResourceId -eq 201) {
            $exports = @(Get-ObjectProperty $payload @('exports'))
            foreach ($requiredExport in $requiredProbeExports) {
                if ($requiredExport -cnotin $exports) { throw "Embedded RCDATA 201 is missing required export: $requiredExport" }
            }
        }
    }

    $domains = @(Get-ObjectProperty $report @('domainInventory'))
    $domainNames = @{}
    $candidateOnlyCount = 0
    $confirmedCount = 0
    for ($index = 0; $index -lt $domains.Count; $index++) {
        $domain = $domains[$index]
        Assert-ExactObjectPropertySet $domain @('index','domain','probe','contractStatus','activationAllowed') `
            ('Enhanced capture domain ' + $index)
        $domainName = [string](Get-ObjectProperty $domain @('domain'))
        $probeName = [string](Get-ObjectProperty $domain @('probe'))
        $contractStatus = [string](Get-ObjectProperty $domain @('contractStatus'))
        $activationAllowed = Get-ObjectProperty $domain @('activationAllowed')
        if ((Convert-ToInt64OrDefault (Get-ObjectProperty $domain @('index')) -1) -ne $index -or
            [string]::IsNullOrWhiteSpace($domainName) -or [string]::IsNullOrWhiteSpace($probeName) -or
            $domainNames.ContainsKey($domainName) -or $activationAllowed -isnot [bool]) {
            throw "DLL enhanced capture domain inventory identity mismatch at index $index"
        }
        $domainNames[$domainName] = $true
        if ($contractStatus -ceq 'CANDIDATE_ONLY_EVIDENCE_BLOCKED' -and -not [bool]$activationAllowed) {
            $candidateOnlyCount++
        } elseif ($contractStatus -ceq 'CONFIRMED_EXACT_BUILD_CONTRACT' -and [bool]$activationAllowed) {
            $confirmedCount++
        } else { throw "DLL enhanced capture domain activation/contract mismatch: $domainName" }
    }
    if ($domains.Count -ne 25 -or $candidateOnlyCount -ne 21 -or $confirmedCount -ne 4) {
        throw "DLL enhanced capture domain coverage mismatch: total=$($domains.Count), candidate=$candidateOnlyCount, confirmed=$confirmedCount"
    }

    $selfTests = Get-ObjectProperty $report @('selfTests')
    Assert-ExactObjectPropertySet $selfTests @('network','product','evidenceFixture','nativeProbe','semanticV2','ultimate') `
        'Enhanced capture self-tests'
    foreach ($selfTestName in @('network','product','evidenceFixture','nativeProbe','semanticV2','ultimate')) {
        $selfTest = Get-ObjectProperty $selfTests @($selfTestName)
        Assert-ExactObjectPropertySet $selfTest @('path','sha256','status','total','passed','failed') `
            ('Enhanced capture self-test ' + $selfTestName)
        $total = Convert-ToInt64OrDefault (Get-ObjectProperty $selfTest @('total')) -1
        if ([string](Get-ObjectProperty $selfTest @('status')) -cne 'PASS' -or $total -le 0 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $selfTest @('passed')) -1) -ne $total -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $selfTest @('failed')) -1) -ne 0 -or
            [string](Get-ObjectProperty $selfTest @('sha256')) -notmatch '^[A-F0-9]{64}$') {
            throw "DLL enhanced capture self-test is incomplete: $selfTestName"
        }
    }

    $gui = Get-ObjectProperty $report @('guiVisibility')
    Assert-ExactObjectPropertySet $gui @('status','evidenceObserved','path','expectedSmokeKeys',
        'DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract',
        'DllStrictUnloadFinalizationContract','DllCancelResultReadBeforeCleanupContract',
        'DllEnhancedCaptureConsumerStateLifecycle','ThreeLanguageDllStrictUnloadContract',
        'SharedTransportSlotReuseDomainAccounting','enhancedCaptureStatusSchemaVersion',
        'initialStrictUnloadVerified','unloadPolicy','expectedExecutableSha256',
        'observedExecutableSha256','executableSha256Bound') `
        'Enhanced capture GUI visibility'
    $expectedGuiKeys = @('DllEnhancedCaptureStatusVisible','DllEnhancedCaptureAutoEnabled',
        'ThreeLanguageDllEnhancedCaptureStatus','ThreeLanguageDllEnhancedCaptureHelp',
        'DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract',
        'ThreeLanguageDllStrictUnloadContract','SharedTransportSlotReuseDomainAccounting',
        'DllStrictUnloadFinalizationContract','DllCancelResultReadBeforeCleanupContract',
        'DllEnhancedCaptureConsumerStateLifecycle') | Sort-Object
    $actualGuiKeys = @(Get-ObjectProperty $gui @('expectedSmokeKeys')) | Sort-Object
    if ([string](Get-ObjectProperty $gui @('status')) -cne 'PASS' -or
        (Get-ObjectProperty $gui @('evidenceObserved')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $gui @('evidenceObserved')) -or
        [string]::IsNullOrWhiteSpace([string](Get-ObjectProperty $gui @('path'))) -or
        [string](Get-ObjectProperty $gui @('DllTwentyFiveDomainStatusContract')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('DllStrictUnloadResultContract')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('DllStrictUnloadFinalizationContract')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('DllCancelResultReadBeforeCleanupContract')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('DllEnhancedCaptureConsumerStateLifecycle')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('ThreeLanguageDllStrictUnloadContract')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('SharedTransportSlotReuseDomainAccounting')) -cne 'PASS' -or
        [string](Get-ObjectProperty $gui @('enhancedCaptureStatusSchemaVersion')) -cne
            'god2-enhanced-capture-status-v2' -or
        (Get-ObjectProperty $gui @('initialStrictUnloadVerified')) -isnot [bool] -or
        [bool](Get-ObjectProperty $gui @('initialStrictUnloadVerified')) -or
        [string](Get-ObjectProperty $gui @('unloadPolicy')) -cne
            'QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped' -or
        [string](Get-ObjectProperty $gui @('expectedExecutableSha256')) -cne $ExecutableSHA256 -or
        [string](Get-ObjectProperty $gui @('observedExecutableSha256')) -cne $ExecutableSHA256 -or
        [string](Get-ObjectProperty $gui @('executableSha256Bound')) -cne 'PASS' -or
        ($actualGuiKeys -join "`n") -cne ($expectedGuiKeys -join "`n")) {
        throw 'DLL enhanced capture permanent three-language GUI status row is not fully proven visible and auto-enabled'
    }

    $checks = @(Get-ObjectProperty $report @('checks'))
    $checkIds = @{}
    foreach ($check in $checks) {
        Assert-ExactObjectPropertySet $check @('id','status','scope','evidence') 'Enhanced capture check'
        $checkId = [string](Get-ObjectProperty $check @('id'))
        if ([string]::IsNullOrWhiteSpace($checkId) -or $checkIds.ContainsKey($checkId) -or
            [string](Get-ObjectProperty $check @('status')) -cne 'PASS') {
            throw "DLL enhanced capture has a duplicate/non-passing check: $checkId"
        }
        $checkIds[$checkId] = $true
    }
    foreach ($requiredCheck in @('resource.201.byte-exact','resource.202.byte-exact','domains.inventory.exact-25',
        'domains.candidate-only.exact-21','gui.smoke-visibility-unload-slot-reuse')) {
        if (-not $checkIds.ContainsKey($requiredCheck)) { throw "DLL enhanced capture acceptance omits required check: $requiredCheck" }
    }
    $summary = Get-ObjectProperty $report @('summary')
    Assert-ExactObjectPropertySet $summary @('total','passed','failed','pending','candidateOnlyBlockedDomains',
        'exactFinalStatus') `
        'Enhanced capture summary'
    if ($checks.Count -eq 0 -or (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('total')) -1) -ne $checks.Count -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('passed')) -1) -ne $checks.Count -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('failed')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('pending')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('candidateOnlyBlockedDomains')) -1) -ne 21 -or
        [string](Get-ObjectProperty $summary @('exactFinalStatus')) -cne
            'ULTIMATE_INFRASTRUCTURE_PASS_DEEP_LIVE_PENDING') {
        throw 'DLL enhanced capture acceptance summary must have zero failed/pending and exactly 21 candidate-only domains'
    }
    return [pscustomobject][ordered]@{
        Status = 'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS'
        DomainCount = 25
        ConfirmedContractCount = 4
        CandidateOnlyDomainCount = 21
        CandidateCount = Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('candidateCount')) 0
        PromotionLedgerRowCount = Convert-ToInt64OrDefault (Get-ObjectProperty $deep @('promotionLedgerRowCount')) 0
        ActivationAllowedCount = 0
        AuthorityStatus = 'PASS'
        AuthorityConflictCount = 0
        CurrentOfficialAuthorityCount = $currentOfficialRecords
        ExactFixtureAuthorityCount = $exactFixtureRecords
        ServerReady = $false
        DatabaseReady = $false
        EvidenceId = [string]$records[0].EvidenceId
    }
}

function Assert-SemanticRingContinuityEvidence {
    param([Parameter(Mandatory = $true)][object[]]$JsonRecords)
    $records = @($JsonRecords | Where-Object {
        [string](Get-ObjectProperty $_.Data @('SchemaVersion')) -ceq 'god2-semantic-continuity-stress-v1'
    })
    if ($records.Count -ne 1) {
        throw "Implementation-complete publication requires exactly one semantic continuity stress report; found $($records.Count)"
    }
    $report = $records[0].Data
    $segments = @(Get-ObjectProperty $report @('Segments'))
    $eventCount = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('EventCount')) -1
    $attempted = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('Attempted')) -1
    $accepted = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('Accepted')) -1
    $p0Accepted = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('P0Accepted')) -1
    $p1Accepted = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('P1Accepted')) -1
    $segmentCount = Convert-ToInt64OrDefault (Get-ObjectProperty $report @('SegmentCount')) -1
    if ([string](Get-ObjectProperty $report @('Status')) -cne 'PASS' -or
        (Get-ObjectProperty $report @('Passed')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $report @('Passed')) -or
        [string](Get-ObjectProperty $report @('TransportMagic')) -cne 'GSR4' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $report @('TransportVersion')) -1) -ne 4 -or
        $eventCount -ne 2000001 -or $attempted -ne $eventCount -or $accepted -ne $eventCount -or
        $p0Accepted -ne 1000001 -or $p1Accepted -ne 1000000 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $report @('P0Dropped')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $report @('P1Dropped')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $report @('WriteFailures')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $report @('PendingAfterDrain')) -1) -ne 0 -or
        (Get-ObjectProperty $report @('SequenceOrdered')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $report @('SequenceOrdered')) -or
        $segmentCount -ne 21 -or $segments.Count -ne $segmentCount -or
        [string](Get-ObjectProperty $report @('MergedSHA256')) -notmatch '^[A-F0-9]{64}$' -or
        (Get-ObjectProperty $report @('ConcatenationVerified')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $report @('ConcatenationVerified')) -or
        (Get-ObjectProperty $report @('SemanticEvidenceIncomplete')) -isnot [bool] -or
        [bool](Get-ObjectProperty $report @('SemanticEvidenceIncomplete'))) {
        throw 'Semantic continuity stress evidence is not the required lossless 2,000,001-event GSR4 result'
    }
    return [pscustomobject][ordered]@{
        Status = 'PASS'
        EventCount = $eventCount
        P0Accepted = $p0Accepted
        P1Accepted = $p1Accepted
        SegmentCount = $segmentCount
        MergedSHA256 = [string](Get-ObjectProperty $report @('MergedSHA256'))
        EvidenceId = [string]$records[0].EvidenceId
    }
}

function Assert-LocalGpuImplementationEvidence {
    param([Parameter(Mandatory = $true)][object[]]$JsonRecords)
    $contractRecords = @($JsonRecords | Where-Object { $_.Kind -eq 'GpuContractJson' })
    $benchmarkRecords = @($JsonRecords | Where-Object { $_.Kind -eq 'GpuBenchmarkJson' })
    if ($contractRecords.Count -ne 1 -or $benchmarkRecords.Count -ne 1) {
        throw "Implementation-complete publication requires exactly one GpuContractJson and one GpuBenchmarkJson; found $($contractRecords.Count)/$($benchmarkRecords.Count)"
    }
    $contract = $contractRecords[0].Data
    $checkCount = Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('CheckCount')) -1
    $passedCount = Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('Passed')) -1
    $failedCount = Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('Failed')) -1
    $checks = @(Get-ObjectProperty $contract @('Checks'))
    $checkNames = @($checks | ForEach-Object { [string](Get-ObjectProperty $_ @('Name')) })
    if ([string](Get-ObjectProperty $contract @('Status')) -ne 'PASS' -or
        $checkCount -lt 33 -or $passedCount -ne $checkCount -or $failedCount -ne 0 -or
        $checks.Count -ne $checkCount -or
        @($checks | Where-Object { [string](Get-ObjectProperty $_ @('Status')) -ne 'PASS' }).Count -ne 0 -or
        'SixteenOperationSpecificGpuContractsExactAndDistinct' -notin $checkNames -or
        'PhysicalSixteenKernelEqualityOrTruthfulHardwareBlock' -notin $checkNames -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('ImplementedOperationCount')) -1) -ne 16 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('StructuredImplementationBlockedCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $contract @('ModelBlockedCount')) -1) -ne 1 -or
        [string](Get-ObjectProperty $contract @('OperationSpecificGpuImplementationStatus')) -ne
            '16_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED') {
        throw 'GPU contract evidence does not prove the complete 16-operation non-AI implementation'
    }

    $benchmark = $benchmarkRecords[0].Data
    $physicalBenchmarkHonesty = [string](Get-ObjectProperty $benchmark @('PhysicalBenchmarkHonesty'))
    if ((Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateWorkloadCount')) -1) -ne 17 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateGpuExecutedCount')) -1) -ne 16 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateGpuImplementationBlockedCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateGpuUnavailableBlockedCount')) -1) -ne 0 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateModelBlockedCount')) -1) -ne 1 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('UltimateEvidenceBlockedCount')) -1) -ne 1 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $benchmark @('DroppedEvidence')) -1) -ne 0 -or
        (Get-ObjectProperty $benchmark @('UltimateCpuGpuEquivalent')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $benchmark @('UltimateCpuGpuEquivalent')) -or
        (Get-ObjectProperty $benchmark @('ImplementedGpuCpuEquivalent')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $benchmark @('ImplementedGpuCpuEquivalent')) -or
        (Get-ObjectProperty $benchmark @('ConcurrentBatchExecutionAvailable')) -isnot [bool] -or
        -not [bool](Get-ObjectProperty $benchmark @('ConcurrentBatchExecutionAvailable')) -or
        $physicalBenchmarkHonesty -match '(?i)(?:^|;)SequentialBatches(?:;|$)' -or
        $physicalBenchmarkHonesty -notmatch '(?:^|;)BoundedMultiStreamInFlight(?:;|$)' -or
        [string](Get-ObjectProperty $benchmark @('AiInferenceStatus')) -ne 'EvidenceBlockedModelUnavailable' -or
        [string](Get-ObjectProperty $benchmark @('Status')) -ne 'GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED') {
        throw 'GPU benchmark evidence does not prove 16/16 non-AI GPU execution with the CPU oracle preserved'
    }
    return [pscustomobject][ordered]@{
        Status = 'GPU_16_WORKLOADS_IMPLEMENTED_AND_CPU_ORACLE_VERIFIED_AI_MODEL_EVIDENCE_BLOCKED'
        ImplementedOperationCount = 16
        ImplementationBlockedCount = 0
        ModelEvidenceBlockedCount = 1
    }
}

function Select-LatestJsonRecord {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][scriptblock]$Predicate
    )
    $matches = @($Records | Where-Object $Predicate | Sort-Object EvidenceTimeUtc -Descending)
    if ($matches.Count -eq 0) { return $null }
    return $matches[0]
}

function Get-JsonRecordTime {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo]$File, [object]$Data)
    foreach ($name in @('TestedAtUtc', 'BenchmarkedAtUtc', 'CompletedAtUtc', 'GeneratedAtUtc', 'CreatedAtUtc')) {
        $value = Get-ObjectProperty $Data @($name)
        if ($null -ne $value) {
            $parsed = [DateTime]::MinValue
            if ([DateTime]::TryParse([string]$value, [ref]$parsed)) { return $parsed.ToUniversalTime() }
        }
    }
    return $File.LastWriteTimeUtc
}

function Get-ArtifactClassification {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    if ($RelativePath.StartsWith('release/')) { return 'UltimateRelease' }
    if ($RelativePath.StartsWith('schemas/')) { return 'MachineReadableSchema' }
    if ($RelativePath.StartsWith('reports/evidence/')) { return 'InputValidationEvidence' }
    if ($RelativePath.StartsWith('reports/')) { return 'UltimateReport' }
    if ($RelativePath.StartsWith('validation/')) { return 'PortableValidationPackage' }
    if ($RelativePath.StartsWith('ServerDbImporter/')) { return 'ServerDbImporterEvidence' }
    return 'UltimateArtifact'
}

function Add-Provenance {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Source
    )
    $relative = Convert-ToSlashPath $RelativePath
    Assert-SafeRelativePath $relative
    if ($Map.ContainsKey($relative)) { throw "Duplicate output path: $relative" }
    $Map[$relative] = $Source
}

function Copy-NewFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRelative,
        [Parameter(Mandatory = $true)][hashtable]$Provenance,
        [Parameter(Mandatory = $true)][string]$SourceLabel
    )
    $sourceFull = Get-FullPath $Source
    if (-not (Test-Path -LiteralPath $sourceFull -PathType Leaf)) { throw "Missing input file: $sourceFull" }
    Assert-SafeRelativePath $DestinationRelative
    $destination = Get-FullPath (Join-Path $StageRoot ($DestinationRelative.Replace('/', '\')))
    if (-not (Test-IsWithin $StageRoot $destination)) { throw "Output path escaped staging root: $DestinationRelative" }
    if (Test-Path -LiteralPath $destination) { throw "Refusing to overwrite staged file: $DestinationRelative" }
    $directory = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory) }
    if ($null -eq $script:ValidatedInputSnapshots) { throw 'Validated input snapshot is not initialized' }
    Copy-ValidatedInputFile $sourceFull $destination $script:ValidatedInputSnapshots
    Add-Provenance $Provenance $DestinationRelative $SourceLabel
}

function Write-GeneratedFile {
    param(
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRelative,
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][hashtable]$Provenance
    )
    Assert-SafeRelativePath $DestinationRelative
    $destination = Get-FullPath (Join-Path $StageRoot ($DestinationRelative.Replace('/', '\')))
    if (-not (Test-IsWithin $StageRoot $destination)) { throw "Generated path escaped staging root: $DestinationRelative" }
    Write-NewUtf8File $destination $Content
    Add-Provenance $Provenance $DestinationRelative 'GeneratedBy:Build-UltimateRelease.ps1'
}

function New-MarkdownReportContent {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$GeneratedAtUtc,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines
    )
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine('# ' + $Title)
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('GeneratedAtUtc: ' + $GeneratedAtUtc)
    [void]$builder.AppendLine('Product: ' + $script:ProductName)
    [void]$builder.AppendLine('Version: ' + $script:DisplayVersion + ' / PE ' + $script:ProductVersion)
    [void]$builder.AppendLine('Status: ' + $Status)
    [void]$builder.AppendLine()
    foreach ($line in $Lines) { [void]$builder.AppendLine($line) }
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('This report does not assert ULTIMATE PASS, SERVER RESTORED, DATABASE RESTORED, or an external live gate without matching machine-readable evidence.')
    $content = $builder.ToString()
    Assert-UltimatePortableText $content ('generated Markdown report ' + $Title)
    if ([regex]::IsMatch($content, '(?m)^[-*] [^\r\n]*:\s*\r?\n\S')) {
        throw "Generated Markdown report contains a split label/value bullet: $Title"
    }
    if ([regex]::IsMatch($content, '(?m)^(?!#)(?!\s*$).+\r?\n#{1,6}\s')) {
        throw "Generated Markdown report has a heading without a preceding blank line: $Title"
    }
    return $content
}

function Write-PackageManifest {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string]$PackageKind,
        [Parameter(Mandatory = $true)][string]$GeneratedAtUtc
    )
    $manifestName = 'validation-package-manifest.json'
    $records = @()
    foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -Recurse -File | Sort-Object FullName) {
        $relative = Convert-ToSlashPath $file.FullName.Substring((Get-FullPath $PackageRoot).TrimEnd('\').Length + 1)
        if ($relative -eq $manifestName) { continue }
        Assert-SafeRelativePath $relative
        $records += [pscustomobject][ordered]@{
            RelativePath = $relative
            SizeBytes = [int64]$file.Length
            SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    if ($records.Count -eq 0) { throw "Validation package has no artifacts: $PackageKind" }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('{')
    $lines.Add('  "SchemaVersion": 1,')
    $lines.Add('  "ValidationPackageVersion": "1.3.0",')
    $lines.Add('  "Product": "God2 Semantic Recovery Engine",')
    $lines.Add('  "ProductVersion": "1.3.0.0",')
    $lines.Add('  "PackageKind": ' + (($PackageKind | ConvertTo-Json -Compress)) + ',')
    $lines.Add('  "CreatedAtUtc": ' + (($GeneratedAtUtc | ConvertTo-Json -Compress)) + ',')
    $lines.Add('  "ManifestSelfHashPolicy": "ManifestExcludedToAvoidRecursiveDigest",')
    $lines.Add('  "ArtifactCount": ' + $records.Count + ',')
    $lines.Add('  "Artifacts": [')
    for ($index = 0; $index -lt $records.Count; $index++) {
        $suffix = if ($index -lt $records.Count - 1) { ',' } else { '' }
        $lines.Add('    ' + ($records[$index] | ConvertTo-Json -Compress) + $suffix)
    }
    $lines.Add('  ]')
    $lines.Add('}')
    Write-NewUtf8File (Join-Path $PackageRoot $manifestName) (($lines -join "`n") + "`n")
    return $records
}

function Compress-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )
    if (Test-Path -LiteralPath $ZipPath) { throw "Refusing to overwrite ZIP: $ZipPath" }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | Sort-Object FullName) {
                $relative = Convert-ToSlashPath $file.FullName.Substring((Get-FullPath $SourceRoot).TrimEnd('\').Length + 1)
                Assert-SafeRelativePath $relative
                $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [System.IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Verify-ValidationZip {
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @{}
        foreach ($entry in $archive.Entries) {
            $relative = Convert-ToSlashPath $entry.FullName
            Assert-SafeRelativePath $relative
            if ($entries.ContainsKey($relative)) { throw "Duplicate ZIP entry: $relative" }
            $entries[$relative] = $entry
        }
        if (-not $entries.ContainsKey('validation-package-manifest.json')) {
            throw "Validation ZIP has no manifest: $ZipPath"
        }
        $manifestReader = New-Object System.IO.StreamReader($entries['validation-package-manifest.json'].Open())
        try { $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json } finally { $manifestReader.Dispose() }
        $packageKind = [string](Get-ObjectProperty $manifest @('PackageKind'))
        if ([int64](Get-ObjectProperty $manifest @('SchemaVersion')) -ne 1 -or
            [string](Get-ObjectProperty $manifest @('ValidationPackageVersion')) -ne '1.3.0' -or
            [string](Get-ObjectProperty $manifest @('Product')) -ne $script:ProductName -or
            [string](Get-ObjectProperty $manifest @('ProductVersion')) -ne $script:ProductVersion -or
            $packageKind -notin @('live', 'rtx5070')) {
            throw "Validation ZIP manifest identity mismatch: $ZipPath"
        }
        $expected = @{}
        foreach ($artifact in $manifest.Artifacts) {
            $relative = Convert-ToSlashPath ([string]$artifact.RelativePath)
            Assert-SafeRelativePath $relative
            if ($expected.ContainsKey($relative)) { throw "Duplicate manifest artifact: $relative" }
            $expected[$relative] = $artifact
            if (-not $entries.ContainsKey($relative)) { throw "ZIP artifact is missing: $relative" }
            $entry = $entries[$relative]
            $memory = New-Object System.IO.MemoryStream
            $entryStream = $entry.Open()
            try { $entryStream.CopyTo($memory) } finally { $entryStream.Dispose() }
            $bytes = $memory.ToArray()
            $memory.Dispose()
            if ([int64]$bytes.Length -ne [int64]$artifact.SizeBytes -or
                (Get-Sha256Bytes $bytes) -ne [string]$artifact.SHA256) {
                throw "ZIP artifact integrity mismatch: $relative"
            }
        }
        if ([int64](Get-ObjectProperty $manifest @('ArtifactCount')) -ne $expected.Count) {
            throw "Validation ZIP declared artifact count mismatch: $ZipPath"
        }
        if ($entries.Count -ne $expected.Count + 1) { throw "ZIP contains unmanifested entries: $ZipPath" }
        $requiredPaths = @('God2SemanticRecoveryEngine.exe', 'validation-contract.json')
        if ($packageKind -eq 'live') {
            $requiredPaths += @('RUN-ULTIMATE-LIVE-VALIDATION.cmd', 'README-ULTIMATE-LIVE-zh-TW.txt',
                'schemas/ultimate-live-validation-result.schema.json', 'schemas/validation-package-manifest.schema.json',
                'schemas/ultimate-live-evidence-binding.schema.json', 'schemas/ultimate-live-result-manifest.schema.json',
                'schemas/ultimate-live-result-attestation.schema.json', 'schemas/semantic-segment-manifest.schema.json',
                'schemas/semantic-shared-ring-health-v4.schema.json',
                'schemas/validation-contract.schema.json')
        } else {
            $requiredPaths += @('RUN-RTX5070-ULTIMATE-VALIDATION.cmd', 'README-RTX5070-ULTIMATE-zh-TW.txt',
                'schemas/rtx5070-validation-result.schema.json', 'schemas/rtx5070-workload-matrix.schema.json',
                'schemas/rtx5070-validation-manifest.schema.json', 'schemas/validation-package-manifest.schema.json',
                'schemas/validation-contract.schema.json')
        }
        foreach ($required in $requiredPaths) { if (-not $entries.ContainsKey($required)) { throw "Validation ZIP required artifact missing: $required" } }
        $contractReader = New-Object System.IO.StreamReader($entries['validation-contract.json'].Open())
        try { $contract = $contractReader.ReadToEnd() | ConvertFrom-Json } finally { $contractReader.Dispose() }
        $packagedExeHash = [string](Get-ObjectProperty $contract @('PackagedExecutableSHA256'))
        if ([int64](Get-ObjectProperty $contract @('SchemaVersion')) -ne 1 -or
            [string](Get-ObjectProperty $contract @('ProductVersion')) -ne $script:ProductVersion -or
            [string](Get-ObjectProperty $contract @('PackageKind')) -ne $packageKind -or
            $packagedExeHash -notmatch '^[A-F0-9]{64}$' -or
            $packagedExeHash -ne [string](Get-ObjectProperty $expected['God2SemanticRecoveryEngine.exe'] @('SHA256')) -or
            [string](Get-ObjectProperty $contract @('IntegrityStatus')) -ne 'STRUCTURE_VERIFIED' -or
            (Get-ObjectProperty $contract @('ExternalExecutionRequired')) -isnot [bool] -or
            -not [bool](Get-ObjectProperty $contract @('ExternalExecutionRequired'))) {
            throw "Validation ZIP contract/executable binding mismatch: $ZipPath"
        }
        if ($packageKind -eq 'live' -and
            [string](Get-ObjectProperty $contract @('NativeEntrypointStatus')) -ne 'PASS') {
            throw 'Live portable package is not bound to the validated native entrypoint contract'
        }
        return [pscustomobject]@{
            Status = 'PACKAGE_INTEGRITY_PASS'
            PackageKind = $packageKind
            ArtifactCount = $expected.Count
            SHA256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
            SizeBytes = (Get-Item -LiteralPath $ZipPath).Length
            PackagedExecutableSHA256 = $packagedExeHash
        }
    } finally {
        $archive.Dispose()
    }
}

function Convert-BytesToUtf8 {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][byte[]]$Bytes)
    $text = [System.Text.Encoding]::UTF8.GetString($Bytes)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { return $text.Substring(1) }
    return $text
}

function Get-ZipFileMap {
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipFull = Get-FullPath $ZipPath
    if (-not (Test-Path -LiteralPath $zipFull -PathType Leaf)) { throw "ZIP is missing: $zipFull" }
    Assert-NoReparsePoints $zipFull
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipFull)
    try {
        if ($archive.Entries.Count -gt 4096) { throw 'ZIP entry count exceeds the bounded validation limit' }
        $files = @{}
        $totalBytes = [int64]0
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $relative = Convert-ToSlashPath $entry.FullName
            Assert-SafeRelativePath $relative
            $key = $relative.ToLowerInvariant()
            if ($files.ContainsKey($key)) { throw "Case-insensitive duplicate ZIP entry: $relative" }
            if ([int64]$entry.Length -gt 134217728) { throw "ZIP entry exceeds 128 MiB: $relative" }
            $totalBytes += [int64]$entry.Length
            if ($totalBytes -gt 1073741824) { throw 'ZIP uncompressed payload exceeds 1 GiB' }
            if ($entry.CompressedLength -eq 0 -and $entry.Length -gt 0) { throw "Invalid ZIP compression metadata: $relative" }
            if ($entry.CompressedLength -gt 0 -and $entry.Length -gt 1048576 -and
                ([double]$entry.Length / [double]$entry.CompressedLength) -gt 1000.0) {
                throw "ZIP compression ratio exceeds the validation limit: $relative"
            }
            $memory = New-Object System.IO.MemoryStream
            $stream = $entry.Open()
            try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
            $bytes = $memory.ToArray()
            $memory.Dispose()
            Assert-UltimatePortableEvidenceBytes $bytes ('ZIP entry ' + $relative)
            $files[$key] = [pscustomobject][ordered]@{
                RelativePath = $relative
                SizeBytes = [int64]$bytes.Length
                SHA256 = Get-Sha256Bytes $bytes
                Bytes = $bytes
            }
        }
        return $files
    } finally {
        $archive.Dispose()
    }
}

function Get-ZipJson {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Files,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )
    $key = (Convert-ToSlashPath $RelativePath).ToLowerInvariant()
    if (-not $Files.ContainsKey($key)) { throw "ZIP JSON artifact is missing: $RelativePath" }
    try { return (Convert-BytesToUtf8 $Files[$key].Bytes) | ConvertFrom-Json } catch {
        throw "ZIP JSON artifact is invalid: $RelativePath"
    }
}

function Get-CanonicalInventorySha256 {
    param([Parameter(Mandatory = $true)][object[]]$Records)
    $lines = @($Records | ForEach-Object {
        $relative = Convert-ToSlashPath ([string](Get-ObjectProperty $_ @('RelativePath', 'relativePath')))
        $size = Convert-ToInt64OrDefault (Get-ObjectProperty $_ @('SizeBytes', 'sizeBytes', 'Size', 'size')) -1
        $hash = [string](Get-ObjectProperty $_ @('SHA256', 'sha256'))
        Assert-SafeRelativePath $relative
        if ($size -lt 0 -or $hash -notmatch '^[A-Fa-f0-9]{64}$') { throw "Invalid inventory record: $relative" }
        $relative.ToLowerInvariant() + "`0" + $size + "`0" + $hash.ToUpperInvariant()
    } | Sort-Object)
    return Get-Sha256Bytes ([System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n"))
}

function Assert-ZipManifestInventory {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Files,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][object[]]$Entries
    )
    $manifestKey = (Convert-ToSlashPath $ManifestPath).ToLowerInvariant()
    $declared = @{}
    foreach ($entry in $Entries) {
        $relative = Convert-ToSlashPath ([string](Get-ObjectProperty $entry @('RelativePath', 'relativePath')))
        Assert-SafeRelativePath $relative
        $key = $relative.ToLowerInvariant()
        if ($key -eq $manifestKey -or $declared.ContainsKey($key)) { throw "Duplicate or self-manifest ZIP inventory path: $relative" }
        $size = Convert-ToInt64OrDefault (Get-ObjectProperty $entry @('SizeBytes', 'sizeBytes', 'Size', 'size')) -1
        $hash = [string](Get-ObjectProperty $entry @('SHA256', 'sha256'))
        if (-not $Files.ContainsKey($key) -or $size -lt 0 -or $hash -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "ZIP manifest entry is missing or invalid: $relative"
        }
        $actual = $Files[$key]
        if ($actual.SizeBytes -ne $size -or $actual.SHA256 -ne $hash.ToUpperInvariant()) {
            throw "ZIP manifest integrity mismatch: $relative"
        }
        $declared[$key] = $entry
    }
    if (-not $Files.ContainsKey($manifestKey)) { throw "ZIP manifest is missing: $ManifestPath" }
    if ($Files.Count -ne $declared.Count + 1) { throw 'ZIP full inventory count does not match its manifest' }
    foreach ($key in $Files.Keys) {
        if ($key -ne $manifestKey -and -not $declared.ContainsKey($key)) {
            throw "ZIP contains an unmanifested artifact: $($Files[$key].RelativePath)"
        }
    }
    return $declared
}

function Assert-UltimateRecoveryBundleZip {
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    $files = Get-ZipFileMap $ZipPath
    $manifestPath = 'manifest/package-manifest.json'
    $manifest = Get-ZipJson $files $manifestPath
    if ([string](Get-ObjectProperty $manifest @('schemaVersion')) -ne 'god2-ultimate-package-manifest-v1') {
        throw 'Recovery bundle manifest schema mismatch'
    }
    $packageId = [string](Get-ObjectProperty $manifest @('packageId'))
    if ($packageId -notmatch '^[A-Za-z0-9._-]{1,160}$') { throw 'Recovery bundle packageId is unsafe or missing' }
    $entries = @(Get-ObjectProperty $manifest @('files'))
    if ($entries.Count -eq 0 -or $null -ne (Get-ObjectProperty $manifest @('artifacts'))) {
        throw 'Recovery bundle must use the audited files inventory'
    }
    $declared = Assert-ZipManifestInventory $files $manifestPath $entries
    foreach ($required in $script:RequiredBundlePaths) {
        if (-not $declared.ContainsKey($required.ToLowerInvariant())) { throw "Recovery bundle required artifact is missing: $required" }
    }
    foreach ($entry in $entries) {
        $relative = [string](Get-ObjectProperty $entry @('relativePath'))
        $provenance = [string](Get-ObjectProperty $entry @('provenance'))
        $authority = [string](Get-ObjectProperty $entry @('authority'))
        $schemaVersion = [string](Get-ObjectProperty $entry @('schemaVersion'))
        if ([string]::IsNullOrWhiteSpace($provenance) -or
            $authority -notin @('VERIFIED', 'OBSERVED', 'DERIVED', 'HYPOTHESIS', 'UNKNOWN', 'UNKNOWN_SERVER_ONLY', 'REJECTED') -or
            $schemaVersion -notmatch '^god2-[a-z0-9.-]+-v[0-9]+$') {
            throw "Recovery bundle artifact metadata is incomplete: $relative"
        }
        $key = (Convert-ToSlashPath $relative).ToLowerInvariant()
        if ($key.EndsWith('.json')) {
            try { [void]((Convert-BytesToUtf8 $files[$key].Bytes) | ConvertFrom-Json) } catch { throw "Recovery bundle JSON is invalid: $relative" }
        } elseif ($key.EndsWith('.jsonl')) {
            $lineNumber = 0
            foreach ($line in (Convert-BytesToUtf8 $files[$key].Bytes) -split "`r?`n") {
                $lineNumber++
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try { [void]($line | ConvertFrom-Json) } catch { throw "Recovery bundle JSONL is invalid: $relative line $lineNumber" }
            }
        }
    }
    $client = Get-ObjectProperty $manifest @('clientBuild')
    $manifestClaimsTrusted = [string](Get-ObjectProperty $client @('executable')) -eq 'God2_opt.exe' -and
        [string](Get-ObjectProperty $client @('architecture')) -eq 'x86' -and
        [string](Get-ObjectProperty $client @('fileVersion')) -eq '1.0.0.1' -and
        [string](Get-ObjectProperty $client @('sha256')) -ieq $script:TargetClientSha256 -and
        [string](Get-ObjectProperty $client @('validationStatus')) -eq $script:ExactClientValidationStatus -and
        (Get-ObjectProperty $client @('exactBindingValidated')) -is [bool] -and
        [bool](Get-ObjectProperty $client @('exactBindingValidated')) -and
        [string](Get-ObjectProperty $client @('recoveryAttestationStatus')) -eq 'ExactRecoveryEventBuildSessionProcessAttested' -and
        [string](Get-ObjectProperty $client @('computedSha256')) -ieq $script:TargetClientSha256 -and
        [string](Get-ObjectProperty $client @('computedFileVersion')) -eq '1.0.0.1' -and
        [string](Get-ObjectProperty $client @('computedArchitecture')) -eq 'x86'
    return [pscustomobject][ordered]@{
        Status = if ($manifestClaimsTrusted) { 'EvidenceBlockedExternalProcessBindingUnavailable' } else { 'EvidenceBlockedUntrustedClientBuild' }
        StructureStatus = 'PASS'
        PackageId = $packageId
        SourceZipSHA256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
        ManifestSHA256 = $files[$manifestPath].SHA256
        ArtifactCount = $entries.Count
        ClientBuildManifestClaimsTrusted = $manifestClaimsTrusted
        ClientBuildTrusted = $false
        ExternalProcessBindingValidated = $false
    }
}

function Assert-Rtx5070ResultZip {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ReleaseExecutableSHA256
    )
    $files = Get-ZipFileMap $ZipPath
    $archiveSha256 = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
    $manifestPath = 'result-manifest.json'
    $manifest = Get-ZipJson $files $manifestPath
    $entries = @(Get-ObjectProperty $manifest @('Artifacts', 'artifacts', 'Files', 'files'))
    if ($entries.Count -eq 0) { throw 'RTX result manifest has no payload inventory' }
    [void](Assert-ZipManifestInventory $files $manifestPath $entries)
    $summary = Get-ZipJson $files 'validation-summary.json'
    $productVersion = [string](Get-ObjectProperty $summary @('ProductVersion'))
    $peVersion = [string](Get-ObjectProperty $summary @('PeFileVersion'))
    if ($peVersion -ne $script:ProductVersion -or $productVersion -notmatch '^v?1\.3\.0(?:\s+Ultimate)?$') {
        return [pscustomobject][ordered]@{
            Status = 'PENDING_FRESH_ULTIMATE_RTX5070_RUN'
            ProductVersion = $productVersion
            PeFileVersion = $peVersion
            OperationCount = 0
            ArchiveSHA256 = $archiveSha256
        }
    }
    $summaryExeHash = [string](Get-ObjectProperty $summary @('ExecutableSHA256'))
    $validationManifestPath = [string](Get-ObjectProperty $summary @('ValidationManifestPath'))
    $workloadPath = [string](Get-ObjectProperty $summary @('WorkloadMatrixPath'))
    if ($summaryExeHash -notmatch '^[A-Fa-f0-9]{64}$' -or $summaryExeHash -ine $ReleaseExecutableSHA256) {
        throw 'RTX v1.3 result is not bound to the exact release executable'
    }

    # The native runner implements and CPU-oracle verifies every non-AI Ultimate
    # operation. AI remains a truthful EvidenceBlocked row when no verified
    # provider/model is present; that optional evidence state does not turn the
    # completed deterministic GPU implementation into an implementation gap.
    if ([string]::IsNullOrWhiteSpace($validationManifestPath) -and
        [string]::IsNullOrWhiteSpace($workloadPath)) {
        $completeStatus = 'GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED'
        $completeFinal = 'RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED'
        $nativeMatrixPath = 'ultimate-workloads.json'
        $nativeMatrix = Get-ZipJson $files $nativeMatrixPath
        $packageManifestSha = [string](Get-ObjectProperty $summary @('PackageManifestSHA256'))
        if ([string](Get-ObjectProperty $summary @('FinalStatus')) -ne $completeFinal -or
            [string](Get-ObjectProperty $summary @('ObservedGpu')) -ne 'NVIDIA GeForce RTX 5070' -or
            -not (Test-PassValue (Get-ObjectProperty $summary @('PackageIntegrity'))) -or
            -not (Test-PassValue (Get-ObjectProperty $summary @('PhysicalGpuExecution'))) -or
            [string](Get-ObjectProperty $summary @('PhysicalCorrectnessStatus')) -ne 'PASS_TRACE_FEATURE_OPERATION_SPECIFIC_GPU_CPU_EXACT' -or
            [string](Get-ObjectProperty $summary @('Equivalence')) -ne 'PASS_TRACE_FEATURE_GPU_CPU_EXACT' -or
            [string](Get-ObjectProperty $summary @('Benchmark')) -ne 'PASS_TRACE_FEATURE_OPERATION_SPECIFIC_MEASUREMENT' -or
            [string](Get-ObjectProperty $summary @('AutoCrossoverStatus')) -ne 'NoPositiveCrossoverFullCpuOracleRequired' -or
            (Get-ObjectProperty $summary @('FullCpuOracleRequired')) -isnot [bool] -or
            -not [bool](Get-ObjectProperty $summary @('FullCpuOracleRequired')) -or
            (Get-ObjectProperty $summary @('PositiveCrossoverPossible')) -isnot [bool] -or
            [bool](Get-ObjectProperty $summary @('PositiveCrossoverPossible')) -or
            [string](Get-ObjectProperty $summary @('PerformanceStatus')) -ne 'TRACE_FEATURE_TIMING_CURVE_MEASURED;16_OPERATION_EQUIVALENCE_MEASURED;AI_MODEL_BLOCKED' -or
            [string](Get-ObjectProperty $summary @('BenefitGate')) -ne 'PASS_CONTRACT_16_GPU_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED' -or
            [string](Get-ObjectProperty $summary @('CpuAuthority')) -ne 'Preserved' -or
            [string](Get-ObjectProperty $summary @('AiBackend')) -ne 'EvidenceBlockedModelUnavailable' -or
            [string](Get-ObjectProperty $summary @('AiAcceleration')) -ne 'EvidenceBlockedModelUnavailable' -or
            -not (Test-PassValue (Get-ObjectProperty $summary @('ResultIntegrity'))) -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('UltimateWorkloadCount')) -1) -ne 17 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('UltimateGpuVerifiedCount')) -1) -ne 16 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('UltimateGpuImplementationBlockedCount')) -1) -ne 0 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('UltimateStructuredImplementationBlockedCount')) -1) -ne 0 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $summary @('UltimateEvidenceBlockedCount')) -1) -ne 1 -or
            [string](Get-ObjectProperty $summary @('UltimateWorkloadStatus')) -ne
                '16 GPU VERIFIED; 0 STRUCTURED GPU IMPLEMENTATION BLOCKED; 1 MODEL BLOCKED' -or
            (Get-ObjectProperty $summary @('ConcurrentBatchExecutionAvailable')) -isnot [bool] -or
            -not [bool](Get-ObjectProperty $summary @('ConcurrentBatchExecutionAvailable')) -or
            $packageManifestSha -notmatch '^[A-Fa-f0-9]{64}$') {
            throw 'RTX native 16-operation validation-summary is inconsistent'
        }
        $nativeOperations = @(Get-ObjectProperty $nativeMatrix @('Workloads'))
        if ((Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('SchemaVersion')) -1) -ne 1 -or
            [string](Get-ObjectProperty $nativeMatrix @('Status')) -ne $completeStatus -or
            [string](Get-ObjectProperty $nativeMatrix @('ExecutableSHA256')) -ine $ReleaseExecutableSHA256 -or
            [string](Get-ObjectProperty $nativeMatrix @('PackageManifestSHA256')) -ine $packageManifestSha -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('WorkloadCount')) -1) -ne 17 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('GpuVerifiedCount')) -1) -ne 16 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('GpuImplementationBlockedCount')) -1) -ne 0 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('StructuredImplementationBlockedCount')) -1) -ne 0 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('EvidenceBlockedCount')) -1) -ne 1 -or
            [string](Get-ObjectProperty $nativeMatrix @('AiInferenceStatus')) -ne 'EvidenceBlockedModelUnavailable' -or
            [string](Get-ObjectProperty $nativeMatrix @('CpuAuthority')) -ne 'Preserved' -or
            (Get-ObjectProperty $nativeMatrix @('ConcurrentBatchExecutionAvailable')) -isnot [bool] -or
            -not [bool](Get-ObjectProperty $nativeMatrix @('ConcurrentBatchExecutionAvailable')) -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $nativeMatrix @('PeakInflightBatches')) -1) -lt 2 -or
            $nativeOperations.Count -ne 17) {
            throw 'RTX native 16-operation workload matrix header is inconsistent'
        }
        $nativeSeen = @{}
        foreach ($operation in $nativeOperations) {
            $name = [string](Get-ObjectProperty $operation @('OperationClass'))
            if ($name -notin $script:RequiredUltimateOperations -or $nativeSeen.ContainsKey($name)) {
                throw "RTX native workload is unknown or duplicated: $name"
            }
            $nativeSeen[$name] = $true
            $candidateExecuted = Get-ObjectProperty $operation @('GpuCandidateExecuted')
            $primitiveExecuted = Get-ObjectProperty $operation @('GpuPrimitiveExtractionExecuted')
            $implementationAvailable = Get-ObjectProperty $operation @('OperationSpecificGpuImplementationAvailable')
            $cpuAuthorityPreserved = Get-ObjectProperty $operation @('CpuAuthorityPreserved')
            $cpuOutputStable = Get-ObjectProperty $operation @('CpuAuthorityOutputStable')
            $primitiveEquivalent = Get-ObjectProperty $operation @('PrimitiveEquivalent')
            $candidateEquivalent = Get-ObjectProperty $operation @('OperationSpecificCandidateEquivalent')
            $candidateDigest = [string](Get-ObjectProperty $operation @('GpuCandidateDigest'))
            $normalizedDigest = [string](Get-ObjectProperty $operation @('NormalizedInputDigest'))
            $cpuOnlyDigest = [string](Get-ObjectProperty $operation @('CpuOnlyOutputDigest'))
            $authoritativeDigest = [string](Get-ObjectProperty $operation @('AuthoritativeOutputDigest'))
            $primitiveGpuDigest = [string](Get-ObjectProperty $operation @('GpuPrimitiveDigest'))
            $primitiveCpuDigest = [string](Get-ObjectProperty $operation @('CpuPrimitiveDigest'))
            $inputRecords = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('InputRecords')) -1
            $inputBytes = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('InputBytes')) -1
            $gpuBatches = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('GpuBatches')) -1
            $kernels = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('GpuKernelLaunches')) -1
            $gpuRecords = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('GpuProcessedRecords')) -1
            $cpuRecords = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('CpuVerificationRecords')) -1
            if ([string]::IsNullOrWhiteSpace([string](Get-ObjectProperty $operation @('Algorithm'))) -or
                [string]::IsNullOrWhiteSpace([string](Get-ObjectProperty $operation @('NormalizedInputContract'))) -or
                [string]::IsNullOrWhiteSpace([string](Get-ObjectProperty $operation @('CandidateOutputContract'))) -or
                $cpuAuthorityPreserved -isnot [bool] -or -not [bool]$cpuAuthorityPreserved -or
                $cpuOutputStable -isnot [bool] -or -not [bool]$cpuOutputStable -or
                (Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('MismatchCount')) -1) -ne 0 -or
                $inputRecords -le 0 -or $inputBytes -le 0 -or $cpuRecords -ne $inputRecords -or
                $normalizedDigest -notmatch '^FNV1A64:[A-Fa-f0-9]{16}$' -or
                $cpuOnlyDigest -notmatch '^FNV1A64:[A-Fa-f0-9]{16}$' -or
                $authoritativeDigest -ine $cpuOnlyDigest) {
                throw "RTX native workload lacks CPU-authoritative provenance: $name"
            }
            foreach ($counterName in @('GpuBatches', 'GpuKernelLaunches', 'GpuProcessedRecords',
                    'DevicePoolAllocations', 'DevicePoolReuses', 'PinnedPoolAllocations',
                    'PinnedPoolReuses', 'AsyncTransfers', 'ConfiguredStreams', 'PeakInflightBatches',
                    'PeakGpuMemoryBytes', 'PeakPinnedHostMemoryBytes')) {
                if ((Convert-ToInt64OrDefault (Get-ObjectProperty $operation @($counterName)) -1) -lt 0) {
                    throw "RTX native workload counter is missing: $name/$counterName"
                }
            }
            if ($name -eq 'AiInference') {
                if ([string](Get-ObjectProperty $operation @('Status')) -ne 'EvidenceBlockedModelUnavailable' -or
                    [string](Get-ObjectProperty $operation @('ImplementationStatus')) -ne 'EvidenceBlockedModelUnavailable' -or
                    [string](Get-ObjectProperty $operation @('ModelStatus')) -ne 'EvidenceBlockedModelUnavailable' -or
                    $candidateDigest -ne 'NOT_RUN_NO_VERIFIED_MODEL' -or
                    $primitiveGpuDigest -ne 'NOT_RUN_NO_VERIFIED_MODEL' -or
                    $primitiveCpuDigest -ne 'NOT_COMPARED' -or
                    [string](Get-ObjectProperty $operation @('WorkloadScope')) -ne 'ExternalVerifiedModelProvider' -or
                    $candidateExecuted -isnot [bool] -or [bool]$candidateExecuted -or
                    $implementationAvailable -isnot [bool] -or [bool]$implementationAvailable -or
                    $candidateEquivalent -isnot [bool] -or [bool]$candidateEquivalent -or
                    $primitiveExecuted -isnot [bool] -or [bool]$primitiveExecuted -or
                    $gpuBatches -ne 0 -or $kernels -ne 0 -or $gpuRecords -ne 0) {
                    throw 'RTX AI operation lacks truthful unavailable-model provenance'
                }
            } elseif ($name -in $script:RequiredNonAiGpuOperations) {
                if ([string](Get-ObjectProperty $operation @('Status')) -ne 'PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT' -or
                    [string](Get-ObjectProperty $operation @('ImplementationStatus')) -ne 'OperationSpecificGpuCandidateVerified' -or
                    [string](Get-ObjectProperty $operation @('ModelStatus')) -ne 'NotApplicable' -or
                    [string](Get-ObjectProperty $operation @('WorkloadScope')) -notin @('IndependentRecordBatch','StructuredCrossRecordOrGraphBatch') -or
                    $candidateExecuted -isnot [bool] -or -not [bool]$candidateExecuted -or
                    $implementationAvailable -isnot [bool] -or -not [bool]$implementationAvailable -or
                    $candidateEquivalent -isnot [bool] -or -not [bool]$candidateEquivalent -or
                    $primitiveExecuted -isnot [bool] -or -not [bool]$primitiveExecuted -or
                    $primitiveEquivalent -isnot [bool] -or -not [bool]$primitiveEquivalent -or
                    $gpuBatches -le 0 -or $kernels -lt ($gpuBatches * 2) -or
                    $gpuRecords -ne $inputRecords -or
                    (Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('ConfiguredStreams')) -1) -lt 2 -or
                    (Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('PeakInflightBatches')) -1) -lt 2 -or
                    $primitiveGpuDigest -notmatch '^FNV1A64:[A-Fa-f0-9]{16}$' -or
                    $primitiveGpuDigest -ine $primitiveCpuDigest -or
                    $candidateDigest -notmatch '^FNV1A64:[A-Fa-f0-9]{16}$' -or
                    $candidateDigest -ine $authoritativeDigest -or $candidateDigest -ine $cpuOnlyDigest) {
                    throw "RTX implemented GPU workload lacks exact CPU-oracle equivalence: $name"
                }
            } else {
                throw "RTX native workload classification is unhandled: $name"
            }
        }
        foreach ($required in $script:RequiredUltimateOperations) {
            if (-not $nativeSeen.ContainsKey($required)) { throw "RTX native workload is missing: $required" }
        }
        return [pscustomobject][ordered]@{
            Status = 'RTX5070_PHYSICAL_VALIDATION_PASS'
            EvidenceStatus = $completeStatus
            ResultFinalStatus = $completeFinal
            ProductVersion = $productVersion
            PeFileVersion = $peVersion
            OperationCount = $nativeOperations.Count
            VerifiedOperationCount = 16
            MandatoryBlockedOperationCount = 0
            MandatoryGpuWorkloadsComplete = $true
            ArchiveSHA256 = $archiveSha256
            WorkloadMatrixSHA256 = $files[$nativeMatrixPath].SHA256
            ExecutableSHA256 = $summaryExeHash.ToUpperInvariant()
        }
    }
    if ([string]::IsNullOrWhiteSpace($validationManifestPath) -or [string]::IsNullOrWhiteSpace($workloadPath)) {
        throw 'RTX v1.3 result must bind both validation-manifest and workload matrix paths'
    }
    Assert-SafeRelativePath $validationManifestPath
    Assert-SafeRelativePath $workloadPath
    $validationManifest = Get-ZipJson $files $validationManifestPath
    $matrix = Get-ZipJson $files $workloadPath
    $matrixKey = (Convert-ToSlashPath $workloadPath).ToLowerInvariant()
    if ([string](Get-ObjectProperty $validationManifest @('SchemaVersion')) -ne 'god2-rtx5070-validation-manifest-v1' -or
        [string](Get-ObjectProperty $validationManifest @('ExecutableSHA256')) -ine $ReleaseExecutableSHA256 -or
        [string](Get-ObjectProperty $validationManifest @('WorkloadMatrixSHA256')) -ine $files[$matrixKey].SHA256) {
        throw 'RTX validation-manifest cryptographic linkage mismatch'
    }
    $operations = @(Get-ObjectProperty $matrix @('Operations', 'operations', 'UltimateWorkloads'))
    if ($operations.Count -ne $script:RequiredUltimateOperations.Count) { throw 'RTX result does not contain the exact 17-workload Ultimate matrix' }
    $seen = @{}
    $aiModelBlocked = $false
    foreach ($operation in $operations) {
        $name = [string](Get-ObjectProperty $operation @('OperationClass', 'Name', 'operationClass'))
        if ($name -notin $script:RequiredUltimateOperations -or $seen.ContainsKey($name)) { throw "RTX workload is unknown or duplicated: $name" }
        $seen[$name] = $true
        $operationStatus = [string](Get-ObjectProperty $operation @('Status'))
        $gpuExecuted = Get-ObjectProperty $operation @('GpuExecuted', 'GpuCandidateExecuted', 'OperationSpecificGpuExecuted')
        $kernels = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('GpuKernelLaunches', 'KernelLaunches')) -1
        $gpuRecords = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('GpuProcessedRecords', 'Records')) -1
        $cpuRecords = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('CpuVerificationRecords')) -1
        $mismatches = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('MismatchCount')) -1
        $backendErrors = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('BackendErrors')) -1
        $dropped = Convert-ToInt64OrDefault (Get-ObjectProperty $operation @('DroppedEvidence')) -1
        $equivalent = Get-ObjectProperty $operation @('CpuGpuEquivalent', 'Equality')
        $cpuDigest = [string](Get-ObjectProperty $operation @('CpuDigest'))
        $gpuDigest = [string](Get-ObjectProperty $operation @('GpuDigest'))
        $workloadDigest = [string](Get-ObjectProperty $operation @('WorkloadDigest'))
        $semantics = [string](Get-ObjectProperty $operation @('WorkloadSemantics'))
        if ($name -eq 'AiInference' -and $operationStatus -eq 'EvidenceBlockedModelUnavailable') {
            $aiModelBlocked = $true
            $providerExecuted = Get-ObjectProperty $operation @('ProviderInferenceExecuted')
            if ($providerExecuted -isnot [bool] -or [bool]$providerExecuted -or
                $gpuExecuted -isnot [bool] -or [bool]$gpuExecuted -or $kernels -ne 0 -or
                [string](Get-ObjectProperty $operation @('CpuAuthority')) -ne 'Preserved' -or
                [string](Get-ObjectProperty $operation @('Authority')) -ne 'HYPOTHESIS' -or
                [string](Get-ObjectProperty $operation @('BlockedReason')) -ne 'EvidenceBlockedModelUnavailable' -or
                $mismatches -ne 0 -or $backendErrors -ne 0 -or $dropped -ne 0 -or
                $workloadDigest -notmatch '^[A-Fa-f0-9]{64}$' -or [string]::IsNullOrWhiteSpace($semantics)) {
                throw 'RTX AI/ML operation lacks truthful blocked-model provenance'
            }
            continue
        }
        if ($operationStatus -ne 'PASS') { throw "RTX useful workload is not PASS: $name" }
        if ($name -eq 'AiInference') {
            if ((Get-ObjectProperty $operation @('ProviderInferenceExecuted')) -isnot [bool] -or
                -not [bool](Get-ObjectProperty $operation @('ProviderInferenceExecuted')) -or
                [string](Get-ObjectProperty $operation @('ModelName')) -eq '' -or
                [string](Get-ObjectProperty $operation @('ModelVersion')) -eq '' -or
                [string](Get-ObjectProperty $operation @('ModelLicense')) -eq '' -or
                [string](Get-ObjectProperty $operation @('ModelSHA256')) -notmatch '^[A-Fa-f0-9]{64}$') {
                throw 'RTX AI/ML operation claims PASS without verified provider inference/model identity'
            }
        }
        foreach ($counterName in @('CpuWallMicroseconds', 'GpuComputeMicroseconds', 'H2DMicroseconds',
                'D2HMicroseconds', 'SynchronizeMicroseconds', 'CpuVerificationMicroseconds',
                'RecordsPerSecond', 'BytesPerSecond', 'GraphEdgesPerSecond', 'PeakRamBytes',
                'PeakVramBytes', 'QueueHighWater')) {
            if ((Convert-ToInt64OrDefault (Get-ObjectProperty $operation @($counterName)) -1) -lt 0) {
                throw "RTX workload counter is missing: $name/$counterName"
            }
        }
        if ($gpuExecuted -isnot [bool] -or -not [bool]$gpuExecuted -or $kernels -le 0 -or
            $gpuRecords -le 0 -or $cpuRecords -ne $gpuRecords -or $mismatches -ne 0 -or
            $backendErrors -ne 0 -or $dropped -ne 0 -or
            (($equivalent -is [bool] -and -not [bool]$equivalent) -or ($equivalent -isnot [bool] -and [string]$equivalent -ne 'PASS')) -or
            $cpuDigest -notmatch '^[A-Fa-f0-9]{64}$' -or $cpuDigest -ine $gpuDigest -or
            $workloadDigest -notmatch '^[A-Fa-f0-9]{64}$' -or [string]::IsNullOrWhiteSpace($semantics) -or
            $semantics -match '(?i)synthetic|label.?only|bulkchecksum') {
            throw "RTX workload did not satisfy useful execution/equivalence: $name"
        }
    }
    foreach ($required in $script:RequiredUltimateOperations) {
        if (-not $seen.ContainsKey($required)) { throw "RTX workload is missing: $required" }
    }
    if ([string](Get-ObjectProperty $summary @('FinalStatus')) -ne 'RTX5070_PHYSICAL_VALIDATION_PASS' -or
        [string](Get-ObjectProperty $summary @('ObservedGpu')) -ne 'NVIDIA GeForce RTX 5070' -or
        -not (Test-PassValue (Get-ObjectProperty $summary @('PackageIntegrity', 'IntegrityStatus'))) -or
        -not (Test-PassValue (Get-ObjectProperty $summary @('PhysicalGpuExecution'))) -or
        -not (Test-PassValue (Get-ObjectProperty $summary @('Equivalence', 'CpuGpuEquivalence'))) -or
        [string](Get-ObjectProperty $summary @('CpuAuthority')) -ne 'Preserved' -or
        ($aiModelBlocked -and [string](Get-ObjectProperty $summary @('AiModelStatus', 'AiInferenceStatus', 'AiBackend')) -ne 'EvidenceBlockedModelUnavailable')) {
        throw 'RTX validation-summary final gate mismatch'
    }
    return [pscustomobject][ordered]@{
        Status = 'RTX5070_PHYSICAL_VALIDATION_PASS'
        ResultFinalStatus = 'RTX5070_PHYSICAL_VALIDATION_PASS'
        ProductVersion = $productVersion
        PeFileVersion = $peVersion
        OperationCount = $operations.Count
        VerifiedOperationCount = 16
        MandatoryBlockedOperationCount = 0
        MandatoryGpuWorkloadsComplete = $true
        ArchiveSHA256 = $archiveSha256
        WorkloadMatrixSHA256 = $files[$matrixKey].SHA256
        ExecutableSHA256 = $summaryExeHash.ToUpperInvariant()
    }
}

function Assert-ExactJsonPropertyContract {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string[]]$Required,
        [Parameter(Mandatory = $true)][string[]]$Allowed,
        [Parameter(Mandatory = $true)][string]$Context
    )
    if ($null -eq $Object -or $Object -isnot [System.Management.Automation.PSCustomObject]) {
        throw "$Context must be a JSON object"
    }
    $names = @($Object.PSObject.Properties | Where-Object { $_.MemberType -in @('NoteProperty', 'Property') } | ForEach-Object { $_.Name })
    foreach ($name in $Required) {
        if ($name -cnotin $names) { throw "$Context is missing required property: $name" }
    }
    foreach ($name in $names) {
        if ($name -cnotin $Allowed) { throw "$Context contains an unknown property: $name" }
    }
}

function Test-JsonIntegerValue {
    param([object]$Value)
    return $Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]
}

function Assert-NonEmptyStringArray {
    param([object]$Value, [Parameter(Mandatory = $true)][string]$Context)
    if ($Value -is [string] -or $Value -isnot [System.Collections.IEnumerable]) {
        throw "$Context must be an array of non-empty strings"
    }
    $items = @($Value)
    if ($items.Count -eq 0 -or @($items | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0) {
        throw "$Context must be a non-empty array of non-empty strings"
    }
}

function Assert-Rtx5070ExternalAttestationShape {
    param([Parameter(Mandatory = $true)][object]$Attestation)
    Assert-ExactJsonPropertyContract $Attestation `
        @('SchemaVersion','AuditStatus','AuditedAtUtc','Source','ArchiveAudit','AcceptedEvidence','Scope') `
        @('SchemaVersion','AuditStatus','AuditedAtUtc','Source','ArchiveAudit','AcceptedEvidence','Scope','Limitations','ProvenanceLimitations') `
        'RTX external attestation'
    $schemaVersion = [string]$Attestation.SchemaVersion
    $auditedAt = [DateTimeOffset]::MinValue
    if ($schemaVersion -notin @('god2-rtx5070-external-audit-v1','god2-rtx5070-external-audit-v2') -or
        $Attestation.AuditStatus -cne 'RTX5070_RESULT_EXTERNAL_AUDIT_PASS' -or
        $Attestation.AuditedAtUtc -isnot [string] -or
        -not [DateTimeOffset]::TryParse([string]$Attestation.AuditedAtUtc, [ref]$auditedAt)) {
        throw 'RTX external attestation root identity/type contract mismatch'
    }
    $limitationsProperty = $Attestation.PSObject.Properties['Limitations']
    $provenanceLimitationsProperty = $Attestation.PSObject.Properties['ProvenanceLimitations']
    $limitations = $null
    $provenanceLimitations = $null
    if ($null -ne $limitationsProperty) { $limitations = $limitationsProperty.Value }
    if ($null -ne $provenanceLimitationsProperty) { $provenanceLimitations = $provenanceLimitationsProperty.Value }
    if ($null -ne $limitations -and ($limitations -isnot [array] -or $limitations.Count -eq 0 -or
        @($limitations | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0)) {
        throw 'RTX external attestation Limitations must be an array of non-empty strings'
    }
    if ($null -ne $provenanceLimitations -and ($provenanceLimitations -isnot [array] -or $provenanceLimitations.Count -eq 0 -or
        @($provenanceLimitations | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0)) {
        throw 'RTX external attestation ProvenanceLimitations must be an array of non-empty strings'
    }

    $source = $Attestation.Source
    Assert-ExactJsonPropertyContract $source @('FileName','SizeBytes','SHA256') `
        @('FileName','SizeBytes','SHA256','LastWriteTimeUtc','SuppliedBy') 'RTX external attestation Source'
    $sourceTime = [DateTimeOffset]::MinValue
    $sourceLastWriteTime = Get-ObjectProperty $source @('LastWriteTimeUtc')
    $sourceSuppliedBy = Get-ObjectProperty $source @('SuppliedBy')
    if ($source.FileName -isnot [string] -or [string]$source.FileName -notmatch '^[^/\\:]+\.zip$' -or
        -not (Test-JsonIntegerValue $source.SizeBytes) -or [int64]$source.SizeBytes -le 0 -or
        $source.SHA256 -isnot [string] -or [string]$source.SHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        ($null -ne $sourceLastWriteTime -and ($sourceLastWriteTime -isnot [string] -or
            -not [DateTimeOffset]::TryParse([string]$sourceLastWriteTime, [ref]$sourceTime))) -or
        ($null -ne $sourceSuppliedBy -and $sourceSuppliedBy -cne 'UserExternalPhysicalValidation')) {
        throw 'RTX external attestation Source type/value contract mismatch'
    }

    $archive = $Attestation.ArchiveAudit
    Assert-ExactJsonPropertyContract $archive `
        @('ZipPathSafety','CaseInsensitiveDuplicatePathCheck','ManifestSchemaVersion','ManifestArtifactCount','ActualPayloadArtifactCount','ManifestSizeChecks','ManifestSHA256Checks','JsonParseChecks','UnmanifestedPayloadArtifacts') `
        @('ZipPathSafety','CaseInsensitiveDuplicatePathCheck','ManifestSchemaVersion','ManifestArtifactCount','ActualPayloadArtifactCount','ManifestSizeChecks','ManifestSHA256Checks','JsonParseChecks','UnmanifestedPayloadArtifacts','JsonFiles') `
        'RTX external attestation ArchiveAudit'
    foreach ($name in @('ManifestSchemaVersion','ManifestArtifactCount','ActualPayloadArtifactCount','UnmanifestedPayloadArtifacts')) {
        if (-not (Test-JsonIntegerValue $archive.$name)) { throw "RTX external attestation ArchiveAudit.$name must be an integer" }
    }
    $archiveJsonFiles = Get-ObjectProperty $archive @('JsonFiles')
    if ($archive.ZipPathSafety -cne 'PASS' -or $archive.CaseInsensitiveDuplicatePathCheck -cne 'PASS' -or
        [int64]$archive.ManifestSchemaVersion -ne 1 -or [int64]$archive.ManifestArtifactCount -le 0 -or
        [int64]$archive.ActualPayloadArtifactCount -le 0 -or [int64]$archive.UnmanifestedPayloadArtifacts -ne 0 -or
        [string]$archive.ManifestSizeChecks -notmatch '^(\d+)/\1 PASS$' -or
        [string]$archive.ManifestSHA256Checks -notmatch '^(\d+)/\1 PASS$' -or
        [string]$archive.JsonParseChecks -notmatch '^(\d+)/\1 PASS$' -or
        ($null -ne $archiveJsonFiles -and (-not (Test-JsonIntegerValue $archiveJsonFiles) -or [int64]$archiveJsonFiles -lt 0))) {
        throw 'RTX external attestation ArchiveAudit type/value contract mismatch'
    }

    $accepted = $Attestation.AcceptedEvidence
    Assert-ExactJsonPropertyContract $accepted `
        @('ProductVersion','PeFileVersion','ObservedGpu','PhysicalGpuExecution','CpuGpuEquivalence','CpuAuthority','ResultFinalStatus') `
        @('ProductVersion','PeFileVersion','ObservedOs','ObservedGpu','ObservedComputeCapability','ObservedVramBytes','PhysicalGpuExecution','CpuGpuEquivalence','CpuAuthority','GpuContract','MeasuredOperationClass','MeasuredPositiveCrossover','ResultFinalStatus','ExecutableSHA256','WorkloadMatrixSHA256','UltimateOperationCount') `
        'RTX external attestation AcceptedEvidence'
    foreach ($name in @('ProductVersion','PeFileVersion','ObservedGpu','PhysicalGpuExecution','CpuGpuEquivalence','CpuAuthority','ResultFinalStatus')) {
        if ($accepted.$name -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$accepted.$name)) {
            throw "RTX external attestation AcceptedEvidence.$name must be a non-empty string"
        }
    }
    $observedOs = Get-ObjectProperty $accepted @('ObservedOs')
    $computeCapability = Get-ObjectProperty $accepted @('ObservedComputeCapability')
    $observedVram = Get-ObjectProperty $accepted @('ObservedVramBytes')
    $gpuContract = Get-ObjectProperty $accepted @('GpuContract')
    $measuredOperation = Get-ObjectProperty $accepted @('MeasuredOperationClass')
    $measuredCrossover = Get-ObjectProperty $accepted @('MeasuredPositiveCrossover')
    if ($accepted.ObservedGpu -cne 'NVIDIA GeForce RTX 5070' -or $accepted.PhysicalGpuExecution -cne 'PASS' -or
        $accepted.CpuGpuEquivalence -cne 'PASS' -or $accepted.CpuAuthority -cne 'Preserved' -or
        $accepted.ResultFinalStatus -cnotin @('RTX5070_PHYSICAL_VALIDATION_PASS',
            'RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED') -or
        ($null -ne $observedOs -and ($observedOs -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$observedOs))) -or
        ($null -ne $computeCapability -and ($computeCapability -isnot [string] -or [string]$computeCapability -notmatch '^[0-9]+\.[0-9]+$')) -or
        ($null -ne $observedVram -and (-not (Test-JsonIntegerValue $observedVram) -or [int64]$observedVram -le 0)) -or
        ($null -ne $gpuContract -and ($gpuContract -isnot [string] -or [string]$gpuContract -notmatch '^[0-9]+/[0-9]+ PASS$')) -or
        ($null -ne $measuredOperation -and ($measuredOperation -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$measuredOperation))) -or
        ($null -ne $measuredCrossover -and $measuredCrossover -isnot [bool])) {
        throw 'RTX external attestation AcceptedEvidence type/value contract mismatch'
    }
    if ($schemaVersion -eq 'god2-rtx5070-external-audit-v2') {
        foreach ($name in @('ExecutableSHA256','WorkloadMatrixSHA256','UltimateOperationCount')) {
            if ($null -eq (Get-ObjectProperty $accepted @($name))) { throw "RTX v2 external attestation AcceptedEvidence is missing: $name" }
        }
        if ([string]$accepted.ExecutableSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
            [string]$accepted.WorkloadMatrixSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
            -not (Test-JsonIntegerValue $accepted.UltimateOperationCount) -or [int64]$accepted.UltimateOperationCount -notin @(0,17)) {
            throw 'RTX v2 external attestation cryptographic field type/value contract mismatch'
        }
    }

    $scope = $Attestation.Scope
    Assert-ExactJsonPropertyContract $scope @('UltimateRtx5070Gate') `
        @('AcceptedFor','NotAcceptedFor','UltimateRtx5070Gate','Reason') 'RTX external attestation Scope'
    $acceptedFor = Get-ObjectProperty $scope @('AcceptedFor')
    $notAcceptedForProperty = $scope.PSObject.Properties['NotAcceptedFor']
    $notAcceptedFor = $null
    if ($null -ne $notAcceptedForProperty) { $notAcceptedFor = $notAcceptedForProperty.Value }
    $reason = Get-ObjectProperty $scope @('Reason')
    if ($scope.UltimateRtx5070Gate -notin @('PENDING_FRESH_ULTIMATE_RTX5070_RUN','RTX5070_PHYSICAL_VALIDATION_PASS') -or
        ($null -ne $acceptedFor -and ($acceptedFor -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$acceptedFor))) -or
        ($null -ne $reason -and ($reason -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$reason)))) {
        throw 'RTX external attestation Scope type/value contract mismatch'
    }
    if ($null -ne $notAcceptedFor -and ($notAcceptedFor -isnot [array] -or $notAcceptedFor.Count -eq 0 -or
        @($notAcceptedFor | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0)) {
        throw 'RTX external attestation Scope.NotAcceptedFor must be an array of non-empty strings'
    }
}

function Assert-Rtx5070AttestedPair {
    param(
        [Parameter(Mandatory = $true)][string]$ResultPath,
        [Parameter(Mandatory = $true)][string]$AttestationPath,
        [Parameter(Mandatory = $true)][string]$ReleaseExecutableSHA256
    )
    $resultFull = Get-FullPath $ResultPath
    $attestationFull = Get-FullPath $AttestationPath
    if (-not (Test-Path -LiteralPath $resultFull -PathType Leaf) -or
        -not (Test-Path -LiteralPath $attestationFull -PathType Leaf)) {
        throw 'Validated RTX result/attestation pair is incomplete'
    }
    Assert-NoReparsePoints $resultFull
    Assert-NoReparsePoints $attestationFull
    $attestationBytes = [System.IO.File]::ReadAllBytes($attestationFull)
    try {
        $attestationText = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($attestationBytes)
        $attestation = $attestationText | ConvertFrom-Json
    } catch { throw 'Validated RTX attestation is not strict UTF-8 JSON' }
    Assert-UltimatePortableText $attestationText 'RTX external attestation'
    Assert-Rtx5070ExternalAttestationShape $attestation
    $attestationSha256 = Get-Sha256Bytes $attestationBytes
    $attestationSchema = [string](Get-ObjectProperty $attestation @('SchemaVersion'))
    $attestationStatus = [string](Get-ObjectProperty $attestation @('AuditStatus'))
    $source = Get-ObjectProperty $attestation @('Source')
    $archiveAudit = Get-ObjectProperty $attestation @('ArchiveAudit')
    $acceptedEvidence = Get-ObjectProperty $attestation @('AcceptedEvidence')
    $scope = Get-ObjectProperty $attestation @('Scope')
    $attestedHash = [string](Get-ObjectProperty $source @('SHA256'))
    $attestedFileName = [string](Get-ObjectProperty $source @('FileName'))
    $attestedSize = Convert-ToInt64OrDefault (Get-ObjectProperty $source @('SizeBytes')) -1
    $ultimateScope = [string](Get-ObjectProperty $scope @('UltimateRtx5070Gate'))
    $resultInfoBefore = Get-Item -LiteralPath $resultFull
    $resultHashBefore = (Get-FileHash -LiteralPath $resultFull -Algorithm SHA256).Hash
    if ($attestationSchema -notin @('god2-rtx5070-external-audit-v1', 'god2-rtx5070-external-audit-v2') -or
        $attestationStatus -ne 'RTX5070_RESULT_EXTERNAL_AUDIT_PASS' -or
        $attestedHash -notmatch '^[A-Fa-f0-9]{64}$' -or $attestedHash.ToUpperInvariant() -ne $resultHashBefore -or
        $attestedFileName -ine $resultInfoBefore.Name -or $attestedSize -ne $resultInfoBefore.Length -or
        [string](Get-ObjectProperty $archiveAudit @('ZipPathSafety')) -ne 'PASS' -or
        [string](Get-ObjectProperty $archiveAudit @('CaseInsensitiveDuplicatePathCheck')) -ne 'PASS' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $archiveAudit @('UnmanifestedPayloadArtifacts')) -1) -ne 0 -or
        $ultimateScope -notin @('PENDING_FRESH_ULTIMATE_RTX5070_RUN', 'RTX5070_PHYSICAL_VALIDATION_PASS')) {
        throw 'Validated RTX result attestation did not satisfy the exact external-audit and hash-binding contract'
    }
    $inner = Assert-Rtx5070ResultZip $resultFull $ReleaseExecutableSHA256
    $resultInfoAfter = Get-Item -LiteralPath $resultFull
    $resultHashAfter = (Get-FileHash -LiteralPath $resultFull -Algorithm SHA256).Hash
    if ($resultHashAfter -ne $resultHashBefore -or [int64]$resultInfoAfter.Length -ne [int64]$resultInfoBefore.Length -or
        [string]$inner.ArchiveSHA256 -ine $resultHashBefore) {
        throw 'Validated RTX result changed while the archive was independently inspected'
    }
    $canPromote = $false
    if ($inner.Status -eq 'RTX5070_PHYSICAL_VALIDATION_PASS') {
        if ($attestationSchema -ne 'god2-rtx5070-external-audit-v2' -or
            $ultimateScope -ne 'RTX5070_PHYSICAL_VALIDATION_PASS' -or
            [string](Get-ObjectProperty $acceptedEvidence @('ResultFinalStatus')) -ne [string]$inner.ResultFinalStatus -or
            [string](Get-ObjectProperty $acceptedEvidence @('ObservedGpu')) -ne 'NVIDIA GeForce RTX 5070' -or
            [string](Get-ObjectProperty $acceptedEvidence @('PhysicalGpuExecution')) -ne 'PASS' -or
            [string](Get-ObjectProperty $acceptedEvidence @('CpuGpuEquivalence')) -ne 'PASS' -or
            [string](Get-ObjectProperty $acceptedEvidence @('CpuAuthority')) -ne 'Preserved' -or
            [string](Get-ObjectProperty $acceptedEvidence @('ExecutableSHA256')) -ine $ReleaseExecutableSHA256 -or
            [string](Get-ObjectProperty $acceptedEvidence @('WorkloadMatrixSHA256')) -ine $inner.WorkloadMatrixSHA256 -or
            (Convert-ToInt64OrDefault (Get-ObjectProperty $acceptedEvidence @('UltimateOperationCount')) -1) -ne 17) {
            throw 'RTX v1.3 result attestation is not cryptographically bound to the exact executable and 17-workload matrix'
        }
        $canPromote = $true
    } elseif ($ultimateScope -ne 'PENDING_FRESH_ULTIMATE_RTX5070_RUN') {
        throw 'Legacy/non-Ultimate RTX evidence cannot claim the v1.3 Ultimate gate'
    }
    return [pscustomobject][ordered]@{
        Attestation = $attestation
        AttestationSchema = $attestationSchema
        AttestationSHA256 = $attestationSha256
        AttestationSizeBytes = [int64]$attestationBytes.Length
        UltimateScope = $ultimateScope
        CanPromoteUltimate = $canPromote
        Inner = $inner
        ResultSHA256 = $resultHashBefore
        ResultSizeBytes = [int64]$resultInfoBefore.Length
    }
}

function Get-XmlAttributeValue {
    param([object]$Node, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Node -or $null -eq $Node.Attributes) { return '' }
    $attribute = $Node.Attributes | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($null -eq $attribute) { return '' }
    return [string]$attribute.Value
}

function Assert-TestRunManifest {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$ManifestFile,
        [Parameter(Mandatory = $true)][object[]]$ReportFiles,
        [Parameter(Mandatory = $true)][string[]]$ReportRoots,
        [Parameter(Mandatory = $true)][string]$ExecutableSHA256,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [switch]$SkipRepositorySourceContent
    )
    foreach ($reportRoot in $ReportRoots) {
        [void](Assert-UltimatePortableEvidenceTree $reportRoot)
    }
    $manifestBytes = [System.IO.File]::ReadAllBytes($ManifestFile.FullName)
    try {
        $manifestText = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($manifestBytes)
        $manifest = $manifestText | ConvertFrom-Json
    } catch { throw "Test-run manifest is not strict UTF-8 JSON: $($ManifestFile.FullName)" }
    $manifestSha256 = Get-Sha256Bytes $manifestBytes
    if ([string](Get-ObjectProperty $manifest @('SchemaVersion')) -ne 'god2-ultimate-test-run-manifest-v1' -or
        [string](Get-ObjectProperty $manifest @('Product')) -ne $script:ProductName -or
        [string](Get-ObjectProperty $manifest @('ProductVersion')) -ne $script:ProductVersion -or
        [string](Get-ObjectProperty $manifest @('RunId')) -notmatch '^[A-Za-z0-9._-]{8,160}$') {
        throw 'Test-run manifest identity contract mismatch'
    }
    $executable = Get-ObjectProperty $manifest @('Executable')
    if ([string](Get-ObjectProperty $executable @('SHA256')) -ine $ExecutableSHA256 -or
        [string](Get-ObjectProperty $executable @('FileVersion')) -ne $script:ProductVersion -or
        [string](Get-ObjectProperty $executable @('ProductVersion')) -ne $script:ProductVersion -or
        [string](Get-ObjectProperty $executable @('Architecture')) -ne 'x64') {
        throw 'Test-run manifest is not bound to the exact packaged executable'
    }

    $source = Get-ObjectProperty $manifest @('Source')
    $sourceFiles = @(Get-ObjectProperty $source @('Files'))
    $sourceTreeHash = [string](Get-ObjectProperty $source @('TreeSHA256'))
    if ($sourceFiles.Count -eq 0 -or $sourceTreeHash -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Test-run manifest source inventory is missing'
    }
    $sourceSeen = @{}
    $sourceInventory = @()
    foreach ($entry in $sourceFiles) {
        $relative = Convert-ToSlashPath ([string](Get-ObjectProperty $entry @('RelativePath')))
        Assert-SafeRelativePath $relative
        if ($relative.StartsWith('Artifacts/', [System.StringComparison]::OrdinalIgnoreCase) -or
            $relative.StartsWith('.git/', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Test-run source inventory contains generated material: $relative"
        }
        $key = $relative.ToLowerInvariant()
        if ($sourceSeen.ContainsKey($key)) { throw "Duplicate test-run source inventory path: $relative" }
        $path = Get-FullPath (Join-Path $RepositoryRoot ($relative.Replace('/', '\')))
        $size = Convert-ToInt64OrDefault (Get-ObjectProperty $entry @('SizeBytes')) -1
        $hash = [string](Get-ObjectProperty $entry @('SHA256'))
        if (-not (Test-IsWithin $RepositoryRoot $path) -or $size -lt 0 -or $hash -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "Test-run source inventory contract is invalid: $relative"
        }
        $actual = $null
        if (-not $SkipRepositorySourceContent) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Test-run source inventory artifact is missing: $relative"
            }
            $actual = Get-Item -LiteralPath $path
        }
        if (-not $SkipRepositorySourceContent -and
            ($size -ne $actual.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash.ToUpperInvariant())) {
            throw "Test-run source inventory changed after validation: $relative"
        }
        $sourceSeen[$key] = $true
        $sourceInventory += [pscustomobject][ordered]@{
            RelativePath = $relative
            Path = $path
            File = $actual
            SizeBytes = [int64]$size
            SHA256 = $hash.ToUpperInvariant()
        }
    }
    if ((Get-CanonicalInventorySha256 $sourceFiles) -ne $sourceTreeHash.ToUpperInvariant()) {
        throw 'Test-run source tree digest mismatch'
    }
    if ((Convert-ToInt64OrDefault (Get-ObjectProperty $source @('FileCount')) -1) -ne $sourceFiles.Count) {
        throw 'Test-run source inventory count mismatch'
    }

    $commands = @(Get-ObjectProperty $manifest @('Commands'))
    if ($commands.Count -eq 0) { throw 'Test-run manifest has no command provenance' }
    $commandMap = @{}
    foreach ($command in $commands) {
        $id = [string](Get-ObjectProperty $command @('CommandId'))
        $text = [string](Get-ObjectProperty $command @('Command'))
        $workingDirectory = [string](Get-ObjectProperty $command @('WorkingDirectory'))
        $exitCode = Convert-ToInt64OrDefault (Get-ObjectProperty $command @('ExitCode')) -1
        $expectedExitCode = Convert-ToInt64OrDefault (Get-ObjectProperty $command @('ExpectedExitCode')) -1
        $startedText = [string](Get-ObjectProperty $command @('StartedAtUtc'))
        $completedText = [string](Get-ObjectProperty $command @('CompletedAtUtc'))
        $started = [DateTimeOffset]::MinValue
        $completed = [DateTimeOffset]::MinValue
        if ($id -notmatch '^[A-Za-z0-9._-]{3,120}$' -or $commandMap.ContainsKey($id) -or
            [string]::IsNullOrWhiteSpace($text) -or $workingDirectory -ne '.' -or
            $expectedExitCode -notin @(0, 4) -or $exitCode -ne $expectedExitCode -or
            [string](Get-ObjectProperty $command @('ExecutableSHA256')) -ine $ExecutableSHA256 -or
            [string](Get-ObjectProperty $command @('SourceTreeSHA256')) -ine $sourceTreeHash -or
            -not [DateTimeOffset]::TryParse($startedText, [ref]$started) -or
            -not [DateTimeOffset]::TryParse($completedText, [ref]$completed) -or $completed -lt $started) {
            throw "Test-run command provenance is invalid: $id"
        }
        $commandMap[$id] = $command
    }

    $manifestFull = Get-FullPath $ManifestFile.FullName
    $observedMap = @{}
    foreach ($sourceFile in $ReportFiles) {
        if ((Get-FullPath $sourceFile.File.FullName) -eq $manifestFull) { continue }
        $inventoryKey = ('{0}:{1}' -f $sourceFile.RootIndex, $sourceFile.RelativePath.ToLowerInvariant())
        if ($observedMap.ContainsKey($inventoryKey)) { throw "Overlapping test-report roots produced duplicate inventory: $inventoryKey" }
        $observedMap[$inventoryKey] = $sourceFile
    }
    $evidence = @(Get-ObjectProperty $manifest @('Evidence'))
    if ($evidence.Count -eq 0) { throw 'Test-run manifest has no evidence inventory' }
    $evidenceMap = @{}
    $declaredInventoryKeys = @{}
    $inventoryRecords = @()
    foreach ($entry in $evidence) {
        $id = [string](Get-ObjectProperty $entry @('EvidenceId'))
        $rootIndex = Convert-ToInt64OrDefault (Get-ObjectProperty $entry @('RootIndex')) -1
        $relative = Convert-ToSlashPath ([string](Get-ObjectProperty $entry @('RelativePath')))
        $commandId = [string](Get-ObjectProperty $entry @('CommandId'))
        $kind = [string](Get-ObjectProperty $entry @('Kind'))
        Assert-SafeRelativePath $relative
        $key = ('{0}:{1}' -f $rootIndex, $relative.ToLowerInvariant())
        if ($id -notmatch '^[A-Za-z0-9._-]{3,160}$' -or $evidenceMap.ContainsKey($id) -or
            -not $observedMap.ContainsKey($key) -or -not $commandMap.ContainsKey($commandId) -or
            $kind -notmatch '^[A-Za-z][A-Za-z0-9]+$') {
            throw "Test-run evidence provenance is invalid: $id/$relative"
        }
        $actual = $observedMap[$key].File
        $size = Convert-ToInt64OrDefault (Get-ObjectProperty $entry @('SizeBytes')) -1
        $hash = [string](Get-ObjectProperty $entry @('SHA256'))
        if ($size -ne $actual.Length -or $hash -notmatch '^[A-Fa-f0-9]{64}$' -or
            (Get-FileHash -LiteralPath $actual.FullName -Algorithm SHA256).Hash -ne $hash.ToUpperInvariant()) {
            throw "Test-run evidence integrity mismatch: $id/$relative"
        }
        $evidenceMap[$id] = [pscustomobject]@{ Definition = $entry; Source = $observedMap[$key] }
        $declaredInventoryKeys[$key] = $true
        $inventoryRecords += [pscustomobject]@{ RelativePath = ('input-{0:D2}/{1}' -f $rootIndex, $relative); SizeBytes = $size; SHA256 = $hash }
    }
    if ($observedMap.Count -ne $evidenceMap.Count) { throw 'Test-run evidence manifest does not cover the full supplied report inventory' }
    foreach ($key in $observedMap.Keys) {
        if (-not $declaredInventoryKeys.ContainsKey($key)) { throw "Unmanifested test-run report artifact: $key" }
    }
    if ((Get-CanonicalInventorySha256 $inventoryRecords) -ne [string](Get-ObjectProperty $manifest @('EvidenceTreeSHA256'))) {
        throw 'Test-run evidence tree digest mismatch'
    }

    $tests = @(Get-ObjectProperty $manifest @('Tests'))
    if ($tests.Count -eq 0) { throw 'Test-run manifest has no unique test results' }
    $testMap = @{}
    foreach ($test in $tests) {
        $testId = [string](Get-ObjectProperty $test @('TestId'))
        $gateName = [string](Get-ObjectProperty $test @('GateName'))
        $status = [string](Get-ObjectProperty $test @('Status'))
        $commandId = [string](Get-ObjectProperty $test @('CommandId'))
        $evidenceId = [string](Get-ObjectProperty $test @('EvidenceId'))
        if ($testId -notmatch '^[A-Za-z0-9][A-Za-z0-9:._|/-]{2,500}$' -or $testMap.ContainsKey($testId) -or
            $gateName -notmatch '^[A-Za-z][A-Za-z0-9]+$' -or $status -notin @('PASS', 'FAIL') -or
            -not $commandMap.ContainsKey($commandId) -or -not $evidenceMap.ContainsKey($evidenceId) -or
            [string](Get-ObjectProperty $evidenceMap[$evidenceId].Definition @('CommandId')) -ne $commandId) {
            throw "Test-run unique test result is invalid: $testId"
        }
        $testMap[$testId] = $test
    }

    $gateDefinitions = @(Get-ObjectProperty $manifest @('Gates'))
    $gateMap = @{}
    foreach ($gate in $gateDefinitions) {
        $name = [string](Get-ObjectProperty $gate @('Name'))
        $status = [string](Get-ObjectProperty $gate @('Status'))
        $testIds = @(Get-ObjectProperty $gate @('TestIds'))
        $evidenceIds = @(Get-ObjectProperty $gate @('EvidenceIds'))
        if ($name -notmatch '^[A-Za-z][A-Za-z0-9]+$' -or $gateMap.ContainsKey($name) -or
            $status -notin @('PASS', 'FAIL') -or $testIds.Count -eq 0 -or $evidenceIds.Count -eq 0) {
            throw "Test-run gate definition is invalid: $name"
        }
        $gateTestSeen = @{}
        foreach ($testIdValue in $testIds) {
            $testId = [string]$testIdValue
            if ($gateTestSeen.ContainsKey($testId) -or -not $testMap.ContainsKey($testId) -or
                [string](Get-ObjectProperty $testMap[$testId] @('GateName')) -ne $name) {
                throw "Test-run gate has an invalid or duplicate TestId: $name/$testId"
            }
            $gateTestSeen[$testId] = $true
        }
        foreach ($evidenceIdValue in $evidenceIds) {
            if (-not $evidenceMap.ContainsKey([string]$evidenceIdValue)) { throw "Test-run gate references unknown evidence: $name/$evidenceIdValue" }
        }
        $actualGateTests = @($tests | Where-Object { [string](Get-ObjectProperty $_ @('GateName')) -eq $name })
        if ($actualGateTests.Count -ne $testIds.Count -or
            ($status -eq 'PASS' -and @($actualGateTests | Where-Object { [string](Get-ObjectProperty $_ @('Status')) -ne 'PASS' }).Count -ne 0) -or
            ($status -eq 'FAIL' -and @($actualGateTests | Where-Object { [string](Get-ObjectProperty $_ @('Status')) -eq 'FAIL' }).Count -eq 0)) {
            throw "Test-run gate status/test coverage mismatch: $name"
        }
        $gateMap[$name] = $gate
    }

    # Versioned first-party suites carry their measured count in the gate name.
    # Resolve by semantic prefix so adding legitimate tests cannot make the
    # release parser stale, while retaining conservative minimum coverage.
    $minimumGateContracts = @(
        [pscustomobject]@{ LogicalName = 'PacketCaptureCore'; Pattern = '^PacketCaptureCore[0-9]+$'; Minimum = 265 },
        [pscustomobject]@{ LogicalName = 'SemanticAnalyzer'; Pattern = '^SemanticAnalyzer[0-9]+$'; Minimum = 29 },
        [pscustomobject]@{ LogicalName = 'EvidenceZipFixture'; Pattern = '^EvidenceZipFixture[0-9]+$'; Minimum = 68 },
        [pscustomobject]@{ LogicalName = 'GpuContract'; Pattern = '^GpuContract[0-9]+$'; Minimum = 43 },
        [pscustomobject]@{ LogicalName = 'GuiAutomated'; Pattern = '^GuiAutomated[0-9]+$'; Minimum = 66 },
        [pscustomobject]@{ LogicalName = 'DllEnhancedCaptureAcceptance'; Pattern = '^DllEnhancedCaptureAcceptance$'; Minimum = 1 },
        [pscustomobject]@{ LogicalName = 'X86Instrumentation'; Pattern = '^X86Instrumentation[0-9]+$'; Minimum = 5 },
        [pscustomobject]@{ LogicalName = 'InjectorLifecycle'; Pattern = '^InjectorLifecycle$'; Minimum = 1 },
        [pscustomobject]@{ LogicalName = 'DotNet'; Pattern = '^DotNet[0-9]+$'; Minimum = 1 },
        [pscustomobject]@{ LogicalName = 'NativeStaticAnalysis'; Pattern = '^NativeStaticAnalysis$'; Minimum = 19 },
        [pscustomobject]@{ LogicalName = 'UltimateRecoverySelfTest'; Pattern = '^UltimateRecoverySelfTest[0-9]+$'; Minimum = 51 },
        [pscustomobject]@{ LogicalName = 'UltimateLiveEntrypointContract'; Pattern = '^UltimateLiveEntrypointContract$'; Minimum = 1 },
        [pscustomobject]@{ LogicalName = 'InputReportParsing'; Pattern = '^InputReportParsing$'; Minimum = 1 },
        [pscustomobject]@{ LogicalName = 'ImporterOfflineVerifierContract'; Pattern = '^ImporterOfflineVerifierContract$'; Minimum = 70 },
        [pscustomobject]@{ LogicalName = 'ImporterFormalIdentityFailClosed'; Pattern = '^ImporterFormalIdentityFailClosed$'; Minimum = 1 }
    )
    $resolvedRequiredGates = @{}
    foreach ($contract in $minimumGateContracts) {
        $resolved = Resolve-RequiredManifestGate $gateMap $contract.LogicalName $contract.Pattern
        $count = @(Get-ObjectProperty $resolved.Definition @('TestIds')).Count
        if ($count -lt [int]$contract.Minimum) {
            throw "Test-run gate has too few unique tests: $($resolved.Name) ($count/$($contract.Minimum))"
        }
        $resolvedRequiredGates[$contract.LogicalName] = $resolved
    }
    if (@(Get-ObjectProperty $resolvedRequiredGates['NativeStaticAnalysis'].Definition @('TestIds')).Count -ne 19) {
        throw 'NativeStaticAnalysis must bind exactly 19 first-party translation units'
    }
    if (@(Get-ObjectProperty $resolvedRequiredGates['ImporterOfflineVerifierContract'].Definition @('TestIds')).Count -ne 70) {
        throw 'ImporterOfflineVerifierContract must bind exactly the 70-test importer suite'
    }
    if (@(Get-ObjectProperty $resolvedRequiredGates['ImporterFormalIdentityFailClosed'].Definition @('TestIds')).Count -ne 1) {
        throw 'ImporterFormalIdentityFailClosed must bind exactly one expected-block contract test'
    }

    $trxRunIds = @{}
    $trxTestIds = @{}
    $trxResultContracts = @{}
    foreach ($item in $evidenceMap.Values | Where-Object { [string](Get-ObjectProperty $_.Definition @('Kind')) -eq 'DotNetTrx' }) {
        try { [xml]$trx = Get-Content -Raw -LiteralPath $item.Source.File.FullName -Encoding UTF8 } catch { throw "TRX parse failure: $($item.Source.RelativePath)" }
        if ($trx.DocumentElement.LocalName -ne 'TestRun' -or $trx.DocumentElement.NamespaceURI -ne 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') {
            throw "TRX root contract mismatch: $($item.Source.RelativePath)"
        }
        $runId = $trx.DocumentElement.GetAttribute('id')
        if ($runId -notmatch '^[A-Fa-f0-9-]{36}$' -or $trxRunIds.ContainsKey($runId)) { throw "Duplicate or invalid TRX run ID: $runId" }
        $trxRunIds[$runId] = $true
        $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $definitionMap = @{}
        foreach ($definitionNode in @($trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns))) {
            $definitionId = (Get-XmlAttributeValue $definitionNode 'id').ToLowerInvariant()
            if ($definitionId -notmatch '^[a-f0-9-]{36}$' -or $definitionMap.ContainsKey($definitionId)) {
                throw "Duplicate or invalid TRX test definition: $definitionId"
            }
            $candidateNames = New-Object System.Collections.Generic.List[string]
            $definitionName = Get-XmlAttributeValue $definitionNode 'name'
            if (-not [string]::IsNullOrWhiteSpace($definitionName)) { $candidateNames.Add($definitionName) }
            $methodNode = $definitionNode.SelectSingleNode('t:TestMethod', $ns)
            if ($null -ne $methodNode) {
                $className = Get-XmlAttributeValue $methodNode 'className'
                $methodName = Get-XmlAttributeValue $methodNode 'name'
                if (-not [string]::IsNullOrWhiteSpace($methodName)) { $candidateNames.Add($methodName) }
                if (-not [string]::IsNullOrWhiteSpace($className) -and -not [string]::IsNullOrWhiteSpace($methodName)) {
                    $candidateNames.Add($className + '.' + $methodName)
                }
            }
            $definitionMap[$definitionId] = @($candidateNames | Select-Object -Unique)
        }
        $resultNodes = @($trx.SelectNodes('//t:Results/t:UnitTestResult', $ns))
        if ($resultNodes.Count -eq 0) { throw "TRX has no UnitTestResult nodes: $($item.Source.RelativePath)" }
        foreach ($node in $resultNodes) {
            $testId = Get-XmlAttributeValue $node 'testId'
            $executionId = Get-XmlAttributeValue $node 'executionId'
            $uniqueId = ('trx:{0}:{1}:{2}' -f $runId.ToLowerInvariant(), $testId.ToLowerInvariant(), $executionId.ToLowerInvariant())
            if ($testId -notmatch '^[A-Fa-f0-9-]{36}$' -or $executionId -notmatch '^[A-Fa-f0-9-]{36}$' -or $trxTestIds.ContainsKey($uniqueId)) {
                throw "Duplicate or invalid TRX test execution: $uniqueId"
            }
            if ((Get-XmlAttributeValue $node 'outcome') -ne 'Passed') { throw "TRX contains a non-passing test: $uniqueId" }
            $trxTestIds[$uniqueId] = $true
            $candidateNames = New-Object System.Collections.Generic.List[string]
            $resultName = Get-XmlAttributeValue $node 'testName'
            if (-not [string]::IsNullOrWhiteSpace($resultName)) { $candidateNames.Add($resultName) }
            if ($definitionMap.ContainsKey($testId.ToLowerInvariant())) {
                foreach ($candidateName in @($definitionMap[$testId.ToLowerInvariant()])) {
                    if (-not [string]::IsNullOrWhiteSpace($candidateName)) { $candidateNames.Add($candidateName) }
                }
            }
            $trxResultContracts[$uniqueId] = [pscustomobject][ordered]@{
                TestId = $uniqueId
                CandidateNames = @($candidateNames | Select-Object -Unique)
            }
        }
        $counter = $trx.SelectSingleNode('//t:ResultSummary/t:Counters', $ns)
        if ($null -eq $counter -or (Convert-ToInt64OrDefault (Get-XmlAttributeValue $counter 'total')) -ne $resultNodes.Count -or
            (Convert-ToInt64OrDefault (Get-XmlAttributeValue $counter 'passed')) -ne $resultNodes.Count -or
            (Convert-ToInt64OrDefault (Get-XmlAttributeValue $counter 'failed')) -ne 0) {
            throw "TRX counters disagree with unique UnitTestResult nodes: $($item.Source.RelativePath)"
        }
    }
    if ($trxTestIds.Count -lt 3034) { throw "TRX suite coverage regressed below 3034 unique results: $($trxTestIds.Count)" }
    $manifestTrxIds = @($tests | Where-Object { ([string](Get-ObjectProperty $_ @('TestId'))).StartsWith('trx:', [System.StringComparison]::Ordinal) } |
        ForEach-Object { [string](Get-ObjectProperty $_ @('TestId')) })
    if ($trxTestIds.Count -ne $manifestTrxIds.Count) { throw 'TRX unique result count does not match test-run manifest' }
    foreach ($id in $manifestTrxIds) { if (-not $trxTestIds.ContainsKey($id)) { throw "Test-run manifest contains a non-existent TRX test ID: $id" } }

    $requiredImporterFqns = @(
        'God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_OfflineCompiledSchemaMigrationAndReplayProofs_AreExecuted',
        'God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_IntegrationContract_ProvesZeroProductionDatabaseAndNetworkMutation'
    )
    $importerTrxIds = @($trxResultContracts.Values | Where-Object {
        @($_.CandidateNames | Where-Object { $_.StartsWith('God2.RecoveredDatabaseImporter.Tests.', [System.StringComparison]::Ordinal) }).Count -gt 0
    } | ForEach-Object { $_.TestId })
    if ($importerTrxIds.Count -ne 70) {
        throw "ImporterOfflineVerifierContract requires exactly 70 importer-suite TRX results; found $($importerTrxIds.Count)"
    }
    $offlineGateName = $resolvedRequiredGates['ImporterOfflineVerifierContract'].Name
    $offlineGateIds = @(Get-ObjectProperty $resolvedRequiredGates['ImporterOfflineVerifierContract'].Definition @('TestIds'))
    foreach ($id in $importerTrxIds) {
        if ($id -notin $offlineGateIds -or [string](Get-ObjectProperty $testMap[$id] @('GateName')) -ne $offlineGateName) {
            throw "Importer suite TRX result is not bound to ImporterOfflineVerifierContract: $id"
        }
    }
    foreach ($id in $offlineGateIds) {
        if ($id -notin $importerTrxIds) { throw "ImporterOfflineVerifierContract contains a non-importer TRX result: $id" }
    }
    foreach ($requiredFqn in $requiredImporterFqns) {
        $matches = @($trxResultContracts.Values | Where-Object { $requiredFqn -cin @($_.CandidateNames) })
        if ($matches.Count -ne 1) { throw "Importer offline verifier FQN must appear exactly once in TRX evidence: $requiredFqn ($($matches.Count))" }
        $requiredId = [string]$matches[0].TestId
        if ([string](Get-ObjectProperty $testMap[$requiredId] @('GateName')) -ne $offlineGateName) {
            throw "Importer offline verifier FQN is not bound to ImporterOfflineVerifierContract: $requiredFqn"
        }
    }

    $formalGate = $resolvedRequiredGates['ImporterFormalIdentityFailClosed'].Definition
    $formalTestId = [string](@(Get-ObjectProperty $formalGate @('TestIds'))[0])
    if ($formalTestId -cne 'importer-formal:expected-exit4-output-directory-absent') {
        throw 'ImporterFormalIdentityFailClosed must bind the producing assertion importer-formal:expected-exit4-output-directory-absent'
    }
    $formalEvidenceIds = @(Get-ObjectProperty $formalGate @('EvidenceIds'))
    if ($formalEvidenceIds.Count -ne 1) { throw 'ImporterFormalIdentityFailClosed must bind exactly one expected-block JSON' }
    $formalEvidenceId = [string]$formalEvidenceIds[0]
    $formalEvidence = $evidenceMap[$formalEvidenceId]
    $formalKind = [string](Get-ObjectProperty $formalEvidence.Definition @('Kind'))
    $formalRelativePath = Convert-ToSlashPath ([string](Get-ObjectProperty $formalEvidence.Definition @('RelativePath')))
    $formalCommandId = [string](Get-ObjectProperty $formalEvidence.Definition @('CommandId'))
    $formalCommand = $commandMap[$formalCommandId]
    if ($formalKind -cne 'ImporterFormalIdentityFailClosedJson' -or
        [System.IO.Path]::GetFileName($formalRelativePath) -cne 'formal-zip-blocked-output.json' -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $formalCommand @('ExitCode')) -1) -ne 4 -or
        (Convert-ToInt64OrDefault (Get-ObjectProperty $formalCommand @('ExpectedExitCode')) -1) -ne 4) {
        throw 'ImporterFormalIdentityFailClosed must bind formal-zip-blocked-output.json to an expected/actual exit-code 4 command'
    }
    try {
        $formalJson = Get-Content -Raw -LiteralPath $formalEvidence.Source.File.FullName -Encoding UTF8 | ConvertFrom-Json
    } catch { throw 'ImporterFormalIdentityFailClosed expected-block evidence is not valid JSON' }
    $formalPropertyNames = @($formalJson.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
    $expectedFormalPropertyNames = @('code','message','productionDatabaseConnectionAttempted',
        'productionDatabaseMutationAttempted','status') | Sort-Object
    if (($formalPropertyNames -join "`n") -cne ($expectedFormalPropertyNames -join "`n") -or
        [string](Get-ObjectProperty $formalJson @('status')) -cne 'RECOVERY_BUNDLE_IMPORTER_BLOCKED' -or
        [string](Get-ObjectProperty $formalJson @('code')) -cne 'build_identity.validation_status_not_trusted' -or
        [string](Get-ObjectProperty $formalJson @('message')) -notmatch
            'expected ExactClientIdentityComputedAndValidated; found EvidenceBlockedClientIdentityNotComputed\.' -or
        (Get-ObjectProperty $formalJson @('productionDatabaseConnectionAttempted')) -isnot [bool] -or
        [bool](Get-ObjectProperty $formalJson @('productionDatabaseConnectionAttempted')) -or
        (Get-ObjectProperty $formalJson @('productionDatabaseMutationAttempted')) -isnot [bool] -or
        [bool](Get-ObjectProperty $formalJson @('productionDatabaseMutationAttempted'))) {
        throw 'ImporterFormalIdentityFailClosed JSON does not prove trusted-identity fail-closed with zero production DB access'
    }

    $requiredAnalyzerSources = @{}
    foreach ($requiredSource in @(Get-UltimateMandatoryNativeAnalysisSources)) {
        $requiredPath = Convert-ToSlashPath ([string]$requiredSource.RelativePath)
        $requiredKey = $requiredPath.ToLowerInvariant()
        if ($requiredAnalyzerSources.ContainsKey($requiredKey)) { throw "Duplicate mandatory analyzer source: $requiredPath" }
        $requiredAnalyzerSources[$requiredKey] = $requiredSource
        if (-not $sourceSeen.ContainsKey($requiredKey)) {
            throw "Test-run source inventory omits mandatory native analyzer source: $requiredPath"
        }
    }
    if (-not $SkipRepositorySourceContent) {
        Assert-UltimateRepositoryNativeSourceInventory $RepositoryRoot
    }

    $analyzerIds = @{}
    $analyzedSourceSeen = @{}
    $x64 = 0
    $x86 = 0
    foreach ($item in $evidenceMap.Values | Where-Object { [string](Get-ObjectProperty $_.Definition @('Kind')) -eq 'NativeCodeAnalysis' }) {
        $definition = $item.Definition
        $analyzerId = [string](Get-ObjectProperty $definition @('AnalyzerId'))
        $architecture = [string](Get-ObjectProperty $definition @('Architecture'))
        $sourceRelative = Convert-ToSlashPath ([string](Get-ObjectProperty $definition @('SourceRelativePath')))
        Assert-SafeRelativePath $sourceRelative
        $sourceKey = $sourceRelative.ToLowerInvariant()
        if ($analyzerId -notmatch '^[A-Za-z0-9._-]{3,160}$' -or $analyzerIds.ContainsKey($analyzerId) -or
            $architecture -notin @('x64', 'x86') -or -not $sourceSeen.ContainsKey($sourceKey) -or
            -not $requiredAnalyzerSources.ContainsKey($sourceKey) -or $analyzedSourceSeen.ContainsKey($sourceKey) -or
            $architecture -cne [string]$requiredAnalyzerSources[$sourceKey].Architecture) {
            throw "Native analysis provenance is invalid: $analyzerId"
        }
        $sourcePath = Get-FullPath (Join-Path $RepositoryRoot ($sourceRelative.Replace('/', '\')))
        if (-not (Test-IsWithin $RepositoryRoot $sourcePath) -or
            (-not $SkipRepositorySourceContent -and -not (Test-Path -LiteralPath $sourcePath -PathType Leaf))) {
            throw "Native analysis source is missing: $sourceRelative"
        }
        try { [xml]$analysis = Get-Content -Raw -LiteralPath $item.Source.File.FullName -Encoding UTF8 } catch { throw "Native analysis XML parse failure: $analyzerId" }
        if ($analysis.DocumentElement.Name -ne 'DEFECTS') { throw "Native analysis XML root mismatch: $analyzerId" }
        $defects = @($analysis.SelectNodes('//DEFECT'))
        if ($defects.Count -ne 0) { throw "Native analysis contains defects: $analyzerId/$($defects.Count)" }
        $analyzerIds[$analyzerId] = $true
        $analyzedSourceSeen[$sourceKey] = $true
        if ($architecture -eq 'x64') { $x64++ } else { $x86++ }
    }
    $nativeGateTestIds = @(Get-ObjectProperty $gateMap['NativeStaticAnalysis'] @('TestIds'))
    if ($x64 -ne 15 -or $x86 -ne 4 -or $analyzerIds.Count -ne 19 -or
        $analyzedSourceSeen.Count -ne $requiredAnalyzerSources.Count -or
        $analyzerIds.Count -ne $nativeGateTestIds.Count) {
        throw "Native analysis coverage mismatch: x64=$x64 x86=$x86 analyzers=$($analyzerIds.Count)"
    }
    foreach ($requiredKey in $requiredAnalyzerSources.Keys) {
        if (-not $analyzedSourceSeen.ContainsKey($requiredKey)) {
            throw "Mandatory native source has no exact analyzer result: $($requiredAnalyzerSources[$requiredKey].RelativePath)"
        }
    }
    foreach ($id in $nativeGateTestIds) {
        if (-not ([string]$id).StartsWith('analyzer:') -or -not $analyzerIds.ContainsKey(([string]$id).Substring(9))) {
            throw "Native analysis test ID does not bind an analyzer: $id"
        }
    }

    foreach ($item in $evidenceMap.Values | Where-Object { ([string](Get-ObjectProperty $_.Definition @('Kind'))).EndsWith('Json') }) {
        try { [void](Get-Content -Raw -LiteralPath $item.Source.File.FullName -Encoding UTF8 | ConvertFrom-Json) } catch {
            throw "Declared JSON evidence failed parsing: $($item.Source.RelativePath)"
        }
    }
    return [pscustomobject][ordered]@{
        Manifest = $manifest
        ManifestFile = $ManifestFile
        ManifestSHA256 = $manifestSha256
        ManifestSizeBytes = [int64]$manifestBytes.Length
        SourceInventory = $sourceInventory
        SourceTreeSHA256 = $sourceTreeHash.ToUpperInvariant()
        EvidenceTreeSHA256 = [string](Get-ObjectProperty $manifest @('EvidenceTreeSHA256'))
        Commands = $commands
        EvidenceMap = $evidenceMap
        Tests = $tests
        Gates = $gateMap
        TrxUniqueTestCount = $trxTestIds.Count
        NativeAnalyzerCount = $analyzerIds.Count
    }
}

function Assert-SchemaRegistryDirectory {
    param([Parameter(Mandatory = $true)][string]$SchemaDirectory)
    $directory = Get-FullPath $SchemaDirectory
    Assert-NoReparsePoints $directory
    $registryPath = Join-Path $directory 'schema-registry.json'
    if (-not (Test-Path -LiteralPath $registryPath -PathType Leaf)) { throw "Schema registry is missing: $registryPath" }
    $registry = Get-Content -Raw -LiteralPath $registryPath -Encoding UTF8 | ConvertFrom-Json
    if ([string](Get-ObjectProperty $registry @('SchemaVersion')) -ne 'god2-ultimate-schema-registry-v1' -or
        [string](Get-ObjectProperty $registry @('ProductVersion')) -ne $script:ProductVersion) {
        throw 'Schema registry identity mismatch'
    }
    $entries = @(Get-ObjectProperty $registry @('Entries'))
    if ($entries.Count -eq 0) { throw 'Schema registry has no entries' }
    $schemaFiles = @{}
    $patterns = @{}
    $ids = @{}
    foreach ($entry in $entries) {
        $pattern = [string](Get-ObjectProperty $entry @('ArtifactPattern'))
        $fileName = [string](Get-ObjectProperty $entry @('SchemaFile'))
        $schemaId = [string](Get-ObjectProperty $entry @('SchemaId'))
        if ($pattern.Length -eq 0 -or $patterns.ContainsKey($pattern) -or
            $fileName -notmatch '^[A-Za-z0-9._-]+\.schema\.json$' -or
            $schemaFiles.ContainsKey($fileName) -or $schemaId -notmatch '^https://god2\.local/schemas/[A-Za-z0-9._-]+\.schema\.json$' -or
            $ids.ContainsKey($schemaId)) {
            throw "Schema registry entry is unsafe or duplicated: $fileName"
        }
        $path = Join-Path $directory $fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Registered schema is missing: $fileName" }
        $schema = Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json
        $isNonCanonicalSemanticV1Input =
            $fileName -ceq 'god2-semantic-event-v1-compatibility-input.schema.json'
        $additionalProperties = Get-ObjectProperty $schema @('additionalProperties')
        if ([string](Get-ObjectProperty $schema @('$schema')) -ne 'https://json-schema.org/draft/2020-12/schema' -or
            [string](Get-ObjectProperty $schema @('$id')) -ne $schemaId -or
            $additionalProperties -isnot [bool] -or
            ([bool]$additionalProperties -ne $isNonCanonicalSemanticV1Input)) {
            throw "Registered schema is not a strict draft-2020-12 schema: $fileName"
        }
        if ($isNonCanonicalSemanticV1Input -and
            ($pattern -cne 'semantic-event-input:SchemaVersion=1;Profile=NonCanonicalV1CompatibilityInput' -or
             [string](Get-ObjectProperty $schema @('title')) -cne
                'God2SemanticEvent v1 noncanonical nonpromotable compatibility input')) {
            throw 'The sole open compatibility schema is not explicitly noncanonical/nonpromotable'
        }
        $patterns[$pattern] = $true
        $schemaFiles[$fileName] = $true
        $ids[$schemaId] = $true
    }
    $observed = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.schema.json')
    if ($observed.Count -ne $schemaFiles.Count) { throw 'Schema registry does not cover every packaged schema file' }
    foreach ($file in $observed) { if (-not $schemaFiles.ContainsKey($file.Name)) { throw "Unregistered schema file: $($file.Name)" } }
    return [pscustomobject]@{ SchemaCount = $schemaFiles.Count; Registry = $registry }
}

function Assert-ExternalSchemaRootsMatchBuiltIns {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$SchemaRoots,
        [Parameter(Mandatory = $true)][string]$BuiltInDirectory
    )
    $builtInHashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $BuiltInDirectory -File) {
        $builtInHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    foreach ($root in $SchemaRoots) {
        $files = @(Get-ChildItem -LiteralPath $root -Recurse -File)
        foreach ($file in $files) {
            if (-not $builtInHashes.ContainsKey($file.Name) -or
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $builtInHashes[$file.Name]) {
                throw "External schema roots may only mirror exact registered built-ins; rejected: $($file.FullName)"
            }
        }
    }
}

function Invoke-UltimateReleaseParserSelfTests {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$BuilderExecutablePath,
        [string]$BuilderOutputRoot
    )
    $passed = New-Object System.Collections.Generic.List[string]
    function Pass([string]$Name) { $passed.Add($Name) }
    function Expect-Throw([string]$Name, [scriptblock]$Action) {
        $threw = $false
        try { & $Action } catch { $threw = $true }
        if (-not $threw) { throw "Parser self-test expected rejection: $Name" }
        Pass $Name
    }
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('God2UltimateReleaseParser-' + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $tempRoot)
    try {
        Assert-SafeRelativePath 'safe/path.json'
        Pass 'SafeRelativePathAccepted'
        Expect-Throw 'TraversalRejected' { Assert-SafeRelativePath '../escape.json' }
        Expect-Throw 'RootedPathRejected' { Assert-SafeRelativePath 'C:/escape.json' }

        $validMarkdown = New-MarkdownReportContent 'Parser self-test valid Markdown' `
            '2026-08-08T00:00:00Z' 'PASS' @('## Section', '', '- Label: value', '', 'Paragraph.', '', '## Next section', '')
        if ($validMarkdown -notmatch '(?m)^- Label: value\r?$') { throw 'Valid Markdown report fixture was not preserved' }
        Pass 'GeneratedMarkdownStructureAccepted'
        Expect-Throw 'GeneratedMarkdownSplitBulletRejected' {
            [void](New-MarkdownReportContent 'Split bullet' '2026-08-08T00:00:00Z' 'PASS' @('- Label:', 'value'))
        }
        Expect-Throw 'GeneratedMarkdownHeadingSpacingRejected' {
            [void](New-MarkdownReportContent 'Heading spacing' '2026-08-08T00:00:00Z' 'PASS' @('Paragraph.', '## Heading'))
        }
        Expect-Throw 'GeneratedMarkdownMojibakeRejected' {
            [void](New-MarkdownReportContent 'Mojibake' '2026-08-08T00:00:00Z' 'PASS' @(('PASS ' + [char]0x9225 + '? evidence')))
        }

        $logicalGateFixture = @{ GpuContract43 = [pscustomobject]@{ Status = 'PASS' } }
        $resolvedLogicalGate = Resolve-RequiredManifestGate $logicalGateFixture 'GpuContract' '^GpuContract[0-9]+$'
        if ($resolvedLogicalGate.Name -ne 'GpuContract43') { throw 'Logical gate resolver changed the exact manifest gate name' }
        Pass 'VersionedManifestGateNameResolved'
        $logicalGateFixture['GpuContract44'] = [pscustomobject]@{ Status = 'PASS' }
        Expect-Throw 'AmbiguousVersionedManifestGateRejected' {
            [void](Resolve-RequiredManifestGate $logicalGateFixture 'GpuContract' '^GpuContract[0-9]+$')
        }
        Expect-Throw 'MissingVersionedManifestGateRejected' {
            [void](Resolve-RequiredManifestGate @{} 'GpuContract' '^GpuContract[0-9]+$')
        }

        $attestationFixtureData = [ordered]@{
            SchemaVersion = 'god2-rtx5070-external-audit-v1'
            AuditStatus = 'RTX5070_RESULT_EXTERNAL_AUDIT_PASS'
            AuditedAtUtc = '2026-08-08T00:00:00Z'
            Source = [ordered]@{ FileName = 'RTX5070-result.zip'; SizeBytes = 12345; SHA256 = [string]'A' * 64
                LastWriteTimeUtc = '2026-08-08T00:00:00Z'; SuppliedBy = 'UserExternalPhysicalValidation' }
            ArchiveAudit = [ordered]@{ ZipPathSafety = 'PASS'; CaseInsensitiveDuplicatePathCheck = 'PASS'
                ManifestSchemaVersion = 1; ManifestArtifactCount = 12; ActualPayloadArtifactCount = 12
                ManifestSizeChecks = '12/12 PASS'; ManifestSHA256Checks = '12/12 PASS'; JsonParseChecks = '10/10 PASS'
                UnmanifestedPayloadArtifacts = 0 }
            AcceptedEvidence = [ordered]@{ ProductVersion = 'v1.2.0 RC'; PeFileVersion = '1.2.0.0'; ObservedOs = 'Windows 11'
                ObservedGpu = 'NVIDIA GeForce RTX 5070'; ObservedComputeCapability = '12.0'; ObservedVramBytes = [int64]12820480000
                PhysicalGpuExecution = 'PASS'; CpuGpuEquivalence = 'PASS'; CpuAuthority = 'Preserved'; GpuContract = '28/28 PASS'
                MeasuredOperationClass = 'BulkChecksum'; MeasuredPositiveCrossover = $false
                ResultFinalStatus = 'RTX5070_PHYSICAL_VALIDATION_PASS' }
            Scope = [ordered]@{ AcceptedFor = 'legacy physical CUDA evidence'; NotAcceptedFor = @('v1.3 Ultimate promotion')
                UltimateRtx5070Gate = 'PENDING_FRESH_ULTIMATE_RTX5070_RUN'; Reason = 'Legacy scope only.' }
            ProvenanceLimitations = @('No v1.3 executable binding.')
        }
        $attestationFixture = ($attestationFixtureData | ConvertTo-Json -Depth 8 -Compress) | ConvertFrom-Json
        [void](Assert-Rtx5070ExternalAttestationShape $attestationFixture)
        $attestationSchemaFixture = Get-Content -Raw -LiteralPath `
            (Join-Path $PSScriptRoot 'assets\schemas\rtx5070-external-attestation.schema.json') -Encoding UTF8 | ConvertFrom-Json
        foreach ($binding in @(
                [pscustomobject]@{ Instance = $attestationFixture; Schema = $attestationSchemaFixture },
                [pscustomobject]@{ Instance = $attestationFixture.Source; Schema = $attestationSchemaFixture.properties.Source },
                [pscustomobject]@{ Instance = $attestationFixture.ArchiveAudit; Schema = $attestationSchemaFixture.properties.ArchiveAudit },
                [pscustomobject]@{ Instance = $attestationFixture.AcceptedEvidence; Schema = $attestationSchemaFixture.properties.AcceptedEvidence },
                [pscustomobject]@{ Instance = $attestationFixture.Scope; Schema = $attestationSchemaFixture.properties.Scope })) {
            foreach ($property in $binding.Instance.PSObject.Properties) {
                if ($null -eq $binding.Schema.properties.PSObject.Properties[$property.Name]) {
                    throw "RTX attestation schema omits fixture property: $($property.Name)"
                }
            }
            if ([bool]$binding.Schema.additionalProperties) { throw 'RTX attestation schema must remain closed at every object level' }
        }
        Pass 'RtxExternalAttestationStrictShapeAndSchemaCoverageAccepted'
        $v2Attestation = (($attestationFixture | ConvertTo-Json -Depth 8 -Compress) | ConvertFrom-Json)
        $v2Attestation.SchemaVersion = 'god2-rtx5070-external-audit-v2'
        $v2Attestation.AcceptedEvidence.ResultFinalStatus = 'RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED'
        $v2Attestation.AcceptedEvidence | Add-Member -NotePropertyName ExecutableSHA256 -NotePropertyValue ([string]'B' * 64)
        $v2Attestation.AcceptedEvidence | Add-Member -NotePropertyName WorkloadMatrixSHA256 -NotePropertyValue ([string]'C' * 64)
        $v2Attestation.AcceptedEvidence | Add-Member -NotePropertyName UltimateOperationCount -NotePropertyValue 17
        $v2Attestation.Scope.UltimateRtx5070Gate = 'RTX5070_PHYSICAL_VALIDATION_PASS'
        [void](Assert-Rtx5070ExternalAttestationShape $v2Attestation)
        Pass 'RtxExternalAttestationNativeSixteenOperationFinalStatusAccepted'
        $unknownAttestation = (($attestationFixture | ConvertTo-Json -Depth 8 -Compress) | ConvertFrom-Json)
        $unknownAttestation.Source | Add-Member -NotePropertyName UnexpectedField -NotePropertyValue 'spoof'
        Expect-Throw 'RtxExternalAttestationUnknownFieldRejected' {
            [void](Assert-Rtx5070ExternalAttestationShape $unknownAttestation)
        }
        $wrongTypeAttestation = (($attestationFixture | ConvertTo-Json -Depth 8 -Compress) | ConvertFrom-Json)
        $wrongTypeAttestation.AcceptedEvidence.ObservedVramBytes = '12820480000'
        Expect-Throw 'RtxExternalAttestationWrongTypeRejected' {
            [void](Assert-Rtx5070ExternalAttestationShape $wrongTypeAttestation)
        }

        $replacementSource = Join-Path $tempRoot 'snapshot-source.bin'
        $replacementDestination = Join-Path $tempRoot 'snapshot-destination.bin'
        [System.IO.File]::WriteAllBytes($replacementSource, [byte[]](1, 2, 3, 4))
        $replacementIdentity = Get-Item -LiteralPath $replacementSource
        $replacementSnapshots = New-InputFileSnapshotMap @($replacementIdentity)
        [System.IO.File]::Delete($replacementSource)
        [System.IO.File]::WriteAllBytes($replacementSource, [byte[]](4, 3, 2, 1))
        [System.IO.File]::SetCreationTimeUtc($replacementSource, $replacementIdentity.CreationTimeUtc)
        [System.IO.File]::SetLastWriteTimeUtc($replacementSource, $replacementIdentity.LastWriteTimeUtc)
        Expect-Throw 'ValidatedInputReplacementAfterSnapshotRejected' {
            Copy-ValidatedInputFile $replacementSource $replacementDestination $replacementSnapshots
        }

        $portableTrxInput = Join-Path $tempRoot 'absolute-path-input.trx'
        $portableTrxOutput = Join-Path $tempRoot 'portable-output.trx'
        $trxFixture = @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="11111111-1111-1111-1111-111111111111" name="FixtureUser@FixtureMachine 2026-08-08" runUser="FixtureMachine\FixtureUser" storage="__ABSOLUTE_TRX_STORAGE__" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestSettings><Deployment runDeploymentRoot="FixtureUser_FixtureMachine_2026-08-08" /></TestSettings>
  <TestDefinitions><UnitTest id="22222222-2222-2222-2222-222222222222" name="portable"><Execution id="33333333-3333-3333-3333-333333333333" /><TestMethod codeBase="__ABSOLUTE_TEST_CODEBASE__" /></UnitTest></TestDefinitions>
  <Results><UnitTestResult testId="22222222-2222-2222-2222-222222222222" executionId="33333333-3333-3333-3333-333333333333" testName="portable" computerName="FixtureMachine" outcome="Passed" duration="00:00:01"><ResultFiles><ResultFile path="__ABSOLUTE_RESULT_FILE__" /></ResultFiles></UnitTestResult></Results>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="1" inProgress="0" pending="0" /></ResultSummary>
</TestRun>
'@
        $trxFixture = $trxFixture.Replace('__ABSOLUTE_TRX_STORAGE__', ('C:' + '\Users\Fixture\results\run.trx')).
            Replace('__ABSOLUTE_TEST_CODEBASE__', ('C:' + '\build\tests.dll')).
            Replace('__ABSOLUTE_RESULT_FILE__', ('C:' + '\Users\Fixture\results\result.txt'))
        [System.IO.File]::WriteAllText($portableTrxInput, $trxFixture, $script:Utf8NoBom)
        [void](Convert-UltimatePortableTrxFile $portableTrxInput $portableTrxOutput)
        [xml]$portableTrx = Get-Content -Raw -LiteralPath $portableTrxOutput -Encoding UTF8
        $portableNs = New-Object System.Xml.XmlNamespaceManager($portableTrx.NameTable)
        $portableNs.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $portableResult = $portableTrx.SelectSingleNode('//t:UnitTestResult', $portableNs)
        $portableMethod = $portableTrx.SelectSingleNode('//t:TestMethod', $portableNs)
        $portableResultFile = $portableTrx.SelectSingleNode('//t:ResultFile', $portableNs)
        $portableDeployment = $portableTrx.SelectSingleNode('//t:Deployment', $portableNs)
        if ($portableTrx.DocumentElement.HasAttribute('storage') -or
            $portableTrx.DocumentElement.GetAttribute('name') -ne 'PORTABLE_TEST_RUN' -or
            $portableTrx.DocumentElement.GetAttribute('runUser') -ne 'PORTABLE_USER' -or
            $portableDeployment.GetAttribute('runDeploymentRoot') -ne 'PORTABLE_DEPLOYMENT_ROOT' -or
            $portableResult.GetAttribute('computerName') -ne 'PORTABLE_COMPUTER' -or
            $portableMethod.GetAttribute('codeBase') -ne 'tests.dll' -or
            $portableResultFile.GetAttribute('path') -ne 'result.txt' -or
            $portableResult.GetAttribute('testId') -ne '22222222-2222-2222-2222-222222222222' -or
            $portableResult.GetAttribute('executionId') -ne '33333333-3333-3333-3333-333333333333' -or
            $portableResult.GetAttribute('outcome') -ne 'Passed' -or
            $portableResult.GetAttribute('duration') -ne '00:00:01') {
            throw 'Portable TRX canonicalizer changed protected result semantics or retained path metadata'
        }
        $portableTrxText = Get-Content -Raw -LiteralPath $portableTrxOutput -Encoding UTF8
        foreach ($forbiddenIdentity in @('FixtureUser', 'FixtureMachine')) {
            if ($portableTrxText.Contains($forbiddenIdentity)) {
                throw "Portable TRX canonicalizer retained an input identity token: $forbiddenIdentity"
            }
        }
        Pass 'PortableTrxCanonicalizerPreservesTestIdentity'

        $identityLeakRoot = Join-Path $tempRoot 'portable-trx-identity-leak'
        [void](New-Item -ItemType Directory -Path $identityLeakRoot)
        $identityLeak = $portableTrxText.Replace('PORTABLE_USER', 'FixtureMachine\FixtureUser')
        [System.IO.File]::WriteAllText((Join-Path $identityLeakRoot 'identity-leak.trx'), $identityLeak, $script:Utf8NoBom)
        Expect-Throw 'PortableTrxIdentityLeakRejected' {
            [void](Assert-UltimatePortableEvidenceTree $identityLeakRoot)
        }

        $mojibakeRoot = Join-Path $tempRoot 'portable-mojibake'
        [void](New-Item -ItemType Directory -Path $mojibakeRoot)
        [System.IO.File]::WriteAllText((Join-Path $mojibakeRoot 'mojibake.txt'),
            ('PASS ' + [char]0x9225 + '? evidence'), $script:Utf8NoBom)
        Expect-Throw 'PortableMojibakeMarkerRejected' {
            [void](Assert-UltimatePortableEvidenceTree $mojibakeRoot)
        }

        $replacementRoot = Join-Path $tempRoot 'portable-replacement-character'
        [void](New-Item -ItemType Directory -Path $replacementRoot)
        [System.IO.File]::WriteAllText((Join-Path $replacementRoot 'replacement.txt'),
            ('PASS ' + [char]0xFFFD + ' evidence'), $script:Utf8NoBom)
        Expect-Throw 'PortableReplacementCharacterRejected' {
            [void](Assert-UltimatePortableEvidenceTree $replacementRoot)
        }

        $knownMojibakeRoot = Join-Path $tempRoot 'portable-known-mojibake-set'
        [void](New-Item -ItemType Directory -Path $knownMojibakeRoot)
        [System.IO.File]::WriteAllText((Join-Path $knownMojibakeRoot 'known-mojibake.txt'),
            ('PASS ' + [char]0x9286 + [char]0x951B + [char]0x9359 + [char]0x6D60), $script:Utf8NoBom)
        Expect-Throw 'PortableKnownMojibakeSetRejected' {
            [void](Assert-UltimatePortableEvidenceTree $knownMojibakeRoot)
        }

        $controlRoot = Join-Path $tempRoot 'portable-control-character'
        [void](New-Item -ItemType Directory -Path $controlRoot)
        [System.IO.File]::WriteAllText((Join-Path $controlRoot 'control.txt'),
            ('PASS' + [char]0 + 'evidence'), $script:Utf8NoBom)
        Expect-Throw 'PortableControlCharacterRejected' {
            [void](Assert-UltimatePortableEvidenceTree $controlRoot)
        }

        $formatRoot = Join-Path $tempRoot 'portable-format-character'
        [void](New-Item -ItemType Directory -Path $formatRoot)
        [System.IO.File]::WriteAllText((Join-Path $formatRoot 'format.txt'),
            ('PASS' + [char]0x200B + 'evidence'), $script:Utf8NoBom)
        Expect-Throw 'PortableFormatCharacterRejected' {
            [void](Assert-UltimatePortableEvidenceTree $formatRoot)
        }

        $jsonArrayRoot = Join-Path $tempRoot 'portable-json-array-root'
        [void](New-Item -ItemType Directory -Path $jsonArrayRoot)
        [System.IO.File]::WriteAllText((Join-Path $jsonArrayRoot 'array.json'),
            '[{"Status":"PASS"}]', $script:Utf8NoBom)
        Expect-Throw 'PortableJsonArrayRootRejected' {
            [void](Assert-UltimatePortableEvidenceTree $jsonArrayRoot)
        }

        $commandTextRoot = Join-Path $tempRoot 'portable-command-text'
        [void](New-Item -ItemType Directory -Path $commandTextRoot)
        [System.IO.File]::WriteAllText((Join-Path $commandTextRoot 'RUN.cmd'),
            ('@echo off' + [char]0 + "`r`nexit /b 0`r`n"), $script:Utf8NoBom)
        Expect-Throw 'PortableCommandTextControlCharacterRejected' {
            [void](Assert-UltimatePortableEvidenceTree $commandTextRoot)
        }

        $environmentToken = @($script:UltimatePortableForbiddenEnvironmentTokens | Select-Object -First 1)
        if ($environmentToken.Count -eq 1) {
            $identityTokenRoot = Join-Path $tempRoot 'portable-environment-token'
            [void](New-Item -ItemType Directory -Path $identityTokenRoot)
            [System.IO.File]::WriteAllText((Join-Path $identityTokenRoot 'identity.txt'),
                ('local-identity=' + $environmentToken[0]), $script:Utf8NoBom)
            Expect-Throw 'PortableEnvironmentIdentityTokenRejected' {
                [void](Assert-UltimatePortableEvidenceTree $identityTokenRoot)
            }
        }

        $portableCliInput = Join-Path $tempRoot 'portable-cli-input'
        $portableCliOutput = Join-Path $tempRoot 'portable-cli-output'
        [void](New-Item -ItemType Directory -Path $portableCliInput)
        [System.IO.File]::Copy($portableTrxInput, (Join-Path $portableCliInput 'results.trx'))
        [System.IO.File]::WriteAllText((Join-Path $portableCliInput 'summary.json'),
            '{"ResultPath":"reports/results.trx"}', $script:Utf8NoBom)
        $portableCliResult = & (Join-Path $PSScriptRoot 'assets\test-run\Convert-UltimatePortableEvidence.ps1') `
            -InputRoot $portableCliInput -OutputRoot $portableCliOutput | ConvertFrom-Json
        if ($portableCliResult.Status -ne 'ULTIMATE_PORTABLE_EVIDENCE_CANONICALIZATION_PASS' -or
            $portableCliResult.FileCount -ne 2 -or $portableCliResult.TrxCanonicalizedCount -ne 1 -or
            -not (Test-Path -LiteralPath (Join-Path $portableCliOutput 'results.trx') -PathType Leaf)) {
            throw 'Portable evidence converter CLI did not produce the exact canonical report tree'
        }
        [void](Assert-UltimatePortableEvidenceTree $portableCliOutput)
        Pass 'PortableEvidenceConverterCliPass'

        $absoluteEvidenceRoot = Join-Path $tempRoot 'absolute-evidence'
        [void](New-Item -ItemType Directory -Path $absoluteEvidenceRoot)
        $absoluteEvidenceJson = [ordered]@{ SessionPath = ('C:' + '\Users\Fixture\AppData\run') } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText((Join-Path $absoluteEvidenceRoot 'leak.json'),
            $absoluteEvidenceJson, $script:Utf8NoBom)
        Expect-Throw 'ResidualAbsoluteEvidencePathRejected' {
            [void](Assert-UltimatePortableEvidenceTree $absoluteEvidenceRoot)
        }
        $encodedAbsoluteEvidenceRoot = Join-Path $tempRoot 'encoded-absolute-evidence'
        [void](New-Item -ItemType Directory -Path $encodedAbsoluteEvidenceRoot)
        [System.IO.File]::WriteAllText((Join-Path $encodedAbsoluteEvidenceRoot 'encoded-leak.json'),
            '{"SessionPath":"C\u003a\u005cUsers\u005cFixture\u005crun"}', $script:Utf8NoBom)
        Expect-Throw 'EncodedAbsoluteEvidencePathRejected' {
            [void](Assert-UltimatePortableEvidenceTree $encodedAbsoluteEvidenceRoot)
        }
        foreach ($localPath in @(
                ('C:' + '\Users\Fixture\run.trx'), ('{"path":"' + 'D:' + '\\temp\\run.json"}'),
                '\\server\share\run.trx', 'file:///C:/Users/Fixture/run.trx',
                '/home/fixture/run.trx', '/Users/fixture/run.trx', '/tmp/god2/run.trx')) {
            if (-not (Test-UltimateAbsoluteLocalPathText $localPath)) {
                throw "Portable path classifier missed a local path case: $localPath"
            }
        }
        foreach ($portableValue in @(
                'reports/evidence/run.trx', 'https://example.invalid/schema.json',
                'FNV1A64:ABCDEF0123456789', '${TEMP}/god2/run.trx', 'tests.dll')) {
            if (Test-UltimateAbsoluteLocalPathText $portableValue) {
                throw "Portable path classifier rejected a portable value: $portableValue"
            }
        }
        Pass 'PortablePathClassifierRejectsLocalAllowsWeb'

        $absoluteZipRoot = Join-Path $tempRoot 'absolute-zip-evidence'
        [void](New-Item -ItemType Directory -Path $absoluteZipRoot)
        $absoluteZipJson = [ordered]@{ FixturePath = ('C:' + '\Users\Fixture\bundle') } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText((Join-Path $absoluteZipRoot 'leak.json'),
            $absoluteZipJson, $script:Utf8NoBom)
        $absoluteZipPath = Join-Path $tempRoot 'absolute-zip-evidence.zip'
        Compress-DeterministicZip $absoluteZipRoot $absoluteZipPath
        Expect-Throw 'ResidualAbsoluteZipEvidencePathRejected' {
            [void](Get-ZipFileMap $absoluteZipPath)
        }

        $schemaStatus = Assert-SchemaRegistryDirectory (Join-Path $PSScriptRoot 'assets\schemas')
        if ($schemaStatus.SchemaCount -lt 15) { throw 'Parser self-test expected at least 15 registered schemas' }
        Pass 'StrictSchemaRegistryCovered'
        $externalSchema = Join-Path $tempRoot 'external-schema'
        [void](New-Item -ItemType Directory -Path $externalSchema)
        [System.IO.File]::WriteAllText((Join-Path $externalSchema 'ultimate-artifact-manifest.schema.json'), '{}', $script:Utf8NoBom)
        Expect-Throw 'ModifiedExternalSchemaRejected' {
            Assert-ExternalSchemaRootsMatchBuiltIns @($externalSchema) (Join-Path $PSScriptRoot 'assets\schemas')
        }

        $importerRoot = Join-Path $tempRoot 'importer-run'
        [void](New-Item -ItemType Directory -Path (Join-Path $importerRoot 'staging'))
        [System.IO.File]::WriteAllText((Join-Path $importerRoot 'staging\artifact.json'), '{}', $script:Utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $importerRoot 'importer-summary.json'), '{}', $script:Utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $importerRoot 'bundle-validation.json'), '{}', $script:Utf8NoBom)
        $importerEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $importerRoot -Recurse -File | Sort-Object FullName) {
            $relative = Convert-ToSlashPath $file.FullName.Substring($importerRoot.Length + 1)
            $importerEntries += [ordered]@{ relativePath = $relative; sizeBytes = [int64]$file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        $importerManifest = [ordered]@{ schemaVersion = 'god2-recovery-import-result-manifest-v1'; status = 'PASS'
            algorithm = 'SHA-256'; manifestSelfHashIncluded = $false; entries = $importerEntries }
        $importerManifestPath = Join-Path $importerRoot 'result-manifest.json'
        [System.IO.File]::WriteAllText($importerManifestPath, (($importerManifest | ConvertTo-Json -Depth 6 -Compress) + "`n"), $script:Utf8NoBom)
        [void](Resolve-ImporterRunRelativePath $importerRoot '.' -AllowRunRoot)
        [void](Resolve-ImporterRunRelativePath $importerRoot 'staging')
        Assert-ImporterResultManifest $importerRoot $importerManifestPath
        Pass 'PortableImporterPathsAndFullManifestPass'
        Expect-Throw 'ImporterAbsolutePathRejected' { [void](Resolve-ImporterRunRelativePath $importerRoot 'C:/spoof') }
        [System.IO.File]::WriteAllText((Join-Path $importerRoot 'unlisted.json'), '{}', $script:Utf8NoBom)
        Expect-Throw 'ImporterUnlistedArtifactRejected' { Assert-ImporterResultManifest $importerRoot $importerManifestPath }

        # Create a complete native bundle fixture.  It is structurally valid but
        # intentionally has no external process binding, so trust must stay blocked.
        $bundleRoot = Join-Path $tempRoot 'bundle'
        [void](New-Item -ItemType Directory -Path $bundleRoot)
        foreach ($relative in $script:RequiredBundlePaths) {
            $path = Join-Path $bundleRoot ($relative.Replace('/', '\'))
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
            $content = if ($relative.EndsWith('.json')) { '{}'} elseif ($relative.EndsWith('.jsonl')) { '' } else { 'fixture' }
            [System.IO.File]::WriteAllText($path, $content, $script:Utf8NoBom)
        }
        $bundleEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Sort-Object FullName) {
            $relative = Convert-ToSlashPath $file.FullName.Substring($bundleRoot.Length + 1)
            $bundleEntries += [ordered]@{
                relativePath = $relative
                sizeBytes = [int64]$file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                provenance = 'ParserSelfTest'
                authority = 'UNKNOWN'
                schemaVersion = 'god2-ultimate-recovery-v1'
            }
        }
        $bundleManifest = [ordered]@{
            schemaVersion = 'god2-ultimate-package-manifest-v1'
            packageId = 'ParserSelfTest'
            clientBuild = [ordered]@{
                executable = 'God2_opt.exe'; architecture = 'x86'; fileVersion = '1.0.0.1'
                sha256 = $script:TargetClientSha256; validationStatus = 'EvidenceBlockedClientIdentityNotComputed'
                exactBindingValidated = $false; recoveryAttestationStatus = 'EvidenceBlockedRecoveryBuildAttestationIncomplete'
                computedSha256 = $null; computedFileVersion = $null; computedArchitecture = $null
            }
            files = $bundleEntries
        }
        $bundleManifestPath = Join-Path $bundleRoot 'manifest\package-manifest.json'
        [System.IO.File]::WriteAllText($bundleManifestPath, (($bundleManifest | ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $bundleZip = Join-Path $tempRoot 'bundle.zip'
        Compress-DeterministicZip $bundleRoot $bundleZip
        $bundleResult = Assert-UltimateRecoveryBundleZip $bundleZip
        if ($bundleResult.StructureStatus -ne 'PASS' -or $bundleResult.ClientBuildTrusted) { throw 'Native bundle structural/trust separation failed' }
        Pass 'NativeBundleFullInventoryPassTrustBlocked'
        [System.IO.File]::WriteAllText((Join-Path $bundleRoot 'spoof.json'), '{}', $script:Utf8NoBom)
        $spoofBundleZip = Join-Path $tempRoot 'bundle-unlisted.zip'
        Compress-DeterministicZip $bundleRoot $spoofBundleZip
        Expect-Throw 'NativeBundleUnlistedEntryRejected' { [void](Assert-UltimateRecoveryBundleZip $spoofBundleZip) }

        # A valid legacy archive remains pending.  A v1.3 archive with only eight
        # labels is independently rejected regardless of its outer attestation.
        $legacyRoot = Join-Path $tempRoot 'rtx-legacy'
        [void](New-Item -ItemType Directory -Path $legacyRoot)
        [System.IO.File]::WriteAllText((Join-Path $legacyRoot 'validation-summary.json'),
            '{"SchemaVersion":1,"ProductVersion":"v1.2.0 RC","PeFileVersion":"1.2.0.0"}', $script:Utf8NoBom)
        $legacyFile = Get-Item -LiteralPath (Join-Path $legacyRoot 'validation-summary.json')
        $legacyManifest = [ordered]@{ SchemaVersion = 1; ArtifactCount = 1; Artifacts = @([ordered]@{
            RelativePath = 'validation-summary.json'; Size = [int64]$legacyFile.Length
            SHA256 = (Get-FileHash -LiteralPath $legacyFile.FullName -Algorithm SHA256).Hash
        }) }
        [System.IO.File]::WriteAllText((Join-Path $legacyRoot 'result-manifest.json'),
            (($legacyManifest | ConvertTo-Json -Depth 6 -Compress) + "`n"), $script:Utf8NoBom)
        $legacyZip = Join-Path $tempRoot 'rtx-legacy.zip'
        Compress-DeterministicZip $legacyRoot $legacyZip
        if ((Assert-Rtx5070ResultZip $legacyZip ([string]'A' * 64)).Status -ne 'PENDING_FRESH_ULTIMATE_RTX5070_RUN') {
            throw 'Legacy RTX scope was incorrectly promoted'
        }
        Pass 'LegacyRtxArchivePreservedPending'

        # Verify the native 16/0/1 matrix can promote only after every non-AI row
        # proves real operation-specific GPU execution and exact CPU-oracle
        # equivalence. AI may remain truthfully model-blocked.
        $releaseHash = [string]'A' * 64
        $packageManifestHash = [string]'B' * 64
        $blockedRtxRoot = Join-Path $tempRoot 'rtx-native-blocked'
        [void](New-Item -ItemType Directory -Path $blockedRtxRoot)
        $blockedWorkloads = @()
        foreach ($operationName in $script:RequiredUltimateOperations) {
            $isAi = $operationName -eq 'AiInference'
            $isVerified = $operationName -in $script:RequiredNonAiGpuOperations
            $candidateOutputDigest = 'FNV1A64:' + ('{0:X16}' -f ([uint64]($blockedWorkloads.Count + 1)))
            $blockedWorkloads += [ordered]@{
                OperationClass = $operationName
                Algorithm = 'UsefulFixture-' + $operationName
                NormalizedInputContract = 'FixtureInput-' + $operationName
                CandidateOutputContract = 'FixtureOutput-' + $operationName
                WorkloadScope = if ($isAi) { 'ExternalVerifiedModelProvider' } elseif ($operationName -in @('TraceFeatureExtraction','ParserFieldPatternMatching','ClassLayoutScoring','RegistryScoring','ValueFlowAggregation','FormulaBatchEvaluation')) { 'IndependentRecordBatch' } else { 'StructuredCrossRecordOrGraphBatch' }
                Status = if ($isAi) { 'EvidenceBlockedModelUnavailable' } else { 'PASS_OPERATION_SPECIFIC_GPU_CPU_EXACT' }
                ImplementationStatus = if ($isAi) { 'EvidenceBlockedModelUnavailable' } else { 'OperationSpecificGpuCandidateVerified' }
                ModelStatus = if ($isAi) { 'EvidenceBlockedModelUnavailable' } else { 'NotApplicable' }
                NormalizedInputDigest = 'FNV1A64:1111111111111111'
                GpuPrimitiveDigest = if ($isAi) { 'NOT_RUN_NO_VERIFIED_MODEL' } else { 'FNV1A64:2222222222222222' }
                CpuPrimitiveDigest = if ($isAi) { 'NOT_COMPARED' } else { 'FNV1A64:2222222222222222' }
                CpuOnlyOutputDigest = $candidateOutputDigest
                GpuCandidateDigest = if ($isAi) { 'NOT_RUN_NO_VERIFIED_MODEL' } else { $candidateOutputDigest }
                AuthoritativeOutputDigest = $candidateOutputDigest
                GpuCandidateExecuted = $isVerified
                GpuPrimitiveExtractionExecuted = -not $isAi
                OperationSpecificGpuImplementationAvailable = $isVerified
                CpuAuthorityPreserved = $true
                PrimitiveEquivalent = $true
                OperationSpecificCandidateEquivalent = $isVerified
                CpuAuthorityOutputStable = $true
                InputRecords = 8192
                InputBytes = 1310720
                GpuBatches = if ($isAi) { 0 } else { 1 }
                GpuKernelLaunches = if ($isAi) { 0 } elseif ($isVerified) { 2 } else { 1 }
                GpuProcessedRecords = if ($isAi) { 0 } else { 8192 }
                CpuVerificationRecords = 8192
                MismatchCount = 0
                DevicePoolAllocations = 0
                DevicePoolReuses = 0
                PinnedPoolAllocations = 0
                PinnedPoolReuses = 0
                AsyncTransfers = 0
                ConfiguredStreams = if ($isAi) { 0 } else { 2 }
                PeakInflightBatches = if ($isAi) { 0 } else { 2 }
                PeakGpuMemoryBytes = if ($isAi) { 0 } else { 4096 }
                PeakPinnedHostMemoryBytes = if ($isAi) { 0 } else { 4096 }
                FallbackOccurred = $false
                FallbackReason = 'None'
            }
        }
        $blockedMatrixData = [ordered]@{
            SchemaVersion = 1
            CollectedAtUtc = '2026-08-08T00:00:00Z'
            Status = 'GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED'
            ExecutableSHA256 = $releaseHash
            PackageManifestSHA256 = $packageManifestHash
            WorkloadCount = 17
            GpuVerifiedCount = 16
            GpuImplementationBlockedCount = 0
            StructuredImplementationBlockedCount = 0
            EvidenceBlockedCount = 1
            AiInferenceStatus = 'EvidenceBlockedModelUnavailable'
            CpuAuthority = 'Preserved'
            ConcurrentBatchExecutionAvailable = $true
            PeakInflightBatches = 2
            Workloads = $blockedWorkloads
        }
        $blockedMatrixFile = Join-Path $blockedRtxRoot 'ultimate-workloads.json'
        [System.IO.File]::WriteAllText($blockedMatrixFile,
            (($blockedMatrixData | ConvertTo-Json -Depth 10 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedSummary = [ordered]@{
            SchemaVersion = 1
            CompletedAtUtc = '2026-08-08T00:00:00Z'
            FinalStatus = 'RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED'
            EngineeringStatus = 'RTX5070_ULTIMATE_VALIDATION_PACKAGE_READY_FOR_EXTERNAL_RUN'
            PackageVersion = '1.3.0'
            ProductVersion = 'v1.3.0 Ultimate'
            PeFileVersion = '1.3.0.0'
            ExecutableSHA256 = $releaseHash
            PackageManifestSHA256 = $packageManifestHash
            TargetOs = 'Windows 11'
            ObservedOsGate = 'PASS'
            TargetGpu = 'NVIDIA GeForce RTX 5070'
            ObservedGpu = 'NVIDIA GeForce RTX 5070'
            ObservedGpuGate = 'PASS'
            MaximumCapabilityGate = 'PASS'
            PackageIntegrity = 'PASS'
            PhysicalGpuExecution = 'PASS'
            PhysicalCorrectnessStatus = 'PASS_TRACE_FEATURE_OPERATION_SPECIFIC_GPU_CPU_EXACT'
            UltimateWorkloadCount = 17
            UltimateGpuVerifiedCount = 16
            UltimateGpuImplementationBlockedCount = 0
            UltimateStructuredImplementationBlockedCount = 0
            UltimateEvidenceBlockedCount = 1
            UltimateWorkloadStatus = '16 GPU VERIFIED; 0 STRUCTURED GPU IMPLEMENTATION BLOCKED; 1 MODEL BLOCKED'
            PerformanceStatus = 'TRACE_FEATURE_TIMING_CURVE_MEASURED;16_OPERATION_EQUIVALENCE_MEASURED;AI_MODEL_BLOCKED'
            Equivalence = 'PASS_TRACE_FEATURE_GPU_CPU_EXACT'
            Benchmark = 'PASS_TRACE_FEATURE_OPERATION_SPECIFIC_MEASUREMENT'
            AutoCrossoverStatus = 'NoPositiveCrossoverFullCpuOracleRequired'
            FullCpuOracleRequired = $true
            PositiveCrossoverPossible = $false
            ConcurrentBatchExecutionAvailable = $true
            BenefitGate = 'PASS_CONTRACT_16_GPU_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED'
            CpuAuthority = 'Preserved'
            AiBackend = 'EvidenceBlockedModelUnavailable'
            AiAcceleration = 'EvidenceBlockedModelUnavailable'
            CaptureOrGameRequired = $false
            PersistenceInstalled = $false
            CleanupStatus = 'PASS'
            ResultIntegrity = 'PASS'
            ElapsedMilliseconds = 1
        }
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'validation-summary.json'),
            (($blockedSummary | ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $blockedRtxRoot -File | Sort-Object Name) {
            $blockedEntries += [ordered]@{ RelativePath = $file.Name; Size = [int64]$file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'result-manifest.json'),
            (([ordered]@{ SchemaVersion = 1; ArtifactCount = $blockedEntries.Count; Artifacts = $blockedEntries } |
                ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedRtxZip = Join-Path $tempRoot 'rtx-native-blocked.zip'
        Compress-DeterministicZip $blockedRtxRoot $blockedRtxZip
        $blockedResult = Assert-Rtx5070ResultZip $blockedRtxZip $releaseHash
        if ($blockedResult.Status -ne 'RTX5070_PHYSICAL_VALIDATION_PASS' -or
            $blockedResult.EvidenceStatus -ne 'GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED' -or
            $blockedResult.ResultFinalStatus -ne 'RTX5070_PHYSICAL_VALIDATION_GPU_16_VERIFIED_AI_MODEL_BLOCKED' -or
            $blockedResult.OperationCount -ne 17 -or $blockedResult.VerifiedOperationCount -ne 16 -or
            $blockedResult.MandatoryBlockedOperationCount -ne 0 -or
            -not $blockedResult.MandatoryGpuWorkloadsComplete) {
            throw 'Native complete RTX matrix was incorrectly rejected'
        }
        Pass 'RtxNativeSixteenOperationMatrixAccepted'

        $blockedMatrixData.Workloads[0].GpuCandidateDigest = 'FNV1A64:FFFFFFFFFFFFFFFF'
        [System.IO.File]::WriteAllText($blockedMatrixFile,
            (($blockedMatrixData | ConvertTo-Json -Depth 10 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $blockedRtxRoot -File |
                Where-Object { $_.Name -ne 'result-manifest.json' } | Sort-Object Name) {
            $blockedEntries += [ordered]@{ RelativePath = $file.Name; Size = [int64]$file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'result-manifest.json'),
            (([ordered]@{ SchemaVersion = 1; ArtifactCount = $blockedEntries.Count; Artifacts = $blockedEntries } |
                ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $verifiedDigestSpoofZip = Join-Path $tempRoot 'rtx-native-verified-digest-spoof.zip'
        Compress-DeterministicZip $blockedRtxRoot $verifiedDigestSpoofZip
        Expect-Throw 'RtxVerifiedCandidateDigestSpoofRejected' {
            [void](Assert-Rtx5070ResultZip $verifiedDigestSpoofZip $releaseHash)
        }
        $blockedMatrixData.Workloads[0].GpuCandidateDigest = $blockedMatrixData.Workloads[0].AuthoritativeOutputDigest
        [System.IO.File]::WriteAllText($blockedMatrixFile,
            (($blockedMatrixData | ConvertTo-Json -Depth 10 -Compress) + "`n"), $script:Utf8NoBom)

        $blockedSummary.PositiveCrossoverPossible = $true
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'validation-summary.json'),
            (($blockedSummary | ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $blockedRtxRoot -File |
                Where-Object { $_.Name -ne 'result-manifest.json' } | Sort-Object Name) {
            $blockedEntries += [ordered]@{ RelativePath = $file.Name; Size = [int64]$file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'result-manifest.json'),
            (([ordered]@{ SchemaVersion = 1; ArtifactCount = $blockedEntries.Count; Artifacts = $blockedEntries } |
                ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $autoBenefitSpoofZip = Join-Path $tempRoot 'rtx-native-auto-benefit-spoof.zip'
        Compress-DeterministicZip $blockedRtxRoot $autoBenefitSpoofZip
        Expect-Throw 'RtxAutoPositiveCrossoverSpoofRejected' {
            [void](Assert-Rtx5070ResultZip $autoBenefitSpoofZip $releaseHash)
        }
        $blockedSummary.PositiveCrossoverPossible = $false
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'validation-summary.json'),
            (($blockedSummary | ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)

        $blockedMatrixData.Workloads[1].GpuCandidateExecuted = $false
        [System.IO.File]::WriteAllText($blockedMatrixFile,
            (($blockedMatrixData | ConvertTo-Json -Depth 10 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $blockedRtxRoot -File |
                Where-Object { $_.Name -ne 'result-manifest.json' } | Sort-Object Name) {
            $blockedEntries += [ordered]@{ RelativePath = $file.Name; Size = [int64]$file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        [System.IO.File]::WriteAllText((Join-Path $blockedRtxRoot 'result-manifest.json'),
            (([ordered]@{ SchemaVersion = 1; ArtifactCount = $blockedEntries.Count; Artifacts = $blockedEntries } |
                ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $blockedSpoofZip = Join-Path $tempRoot 'rtx-native-missing-candidate-execution.zip'
        Compress-DeterministicZip $blockedRtxRoot $blockedSpoofZip
        Expect-Throw 'RtxNonAiMissingCandidateExecutionRejected' {
            [void](Assert-Rtx5070ResultZip $blockedSpoofZip $releaseHash)
        }

        $spoofRtxRoot = Join-Path $tempRoot 'rtx-eight-labels'
        [void](New-Item -ItemType Directory -Path $spoofRtxRoot)
        $eightOperations = @($script:RequiredUltimateOperations | Select-Object -First 8 | ForEach-Object { [ordered]@{ OperationClass = $_ } })
        $matrixData = [ordered]@{ SchemaVersion = 'god2-rtx5070-ultimate-workload-matrix-v1'; OperationCount = 8; Operations = $eightOperations }
        $matrixPath = Join-Path $spoofRtxRoot 'ultimate-workload-matrix.json'
        [System.IO.File]::WriteAllText($matrixPath, (($matrixData | ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $link = [ordered]@{ SchemaVersion = 'god2-rtx5070-validation-manifest-v1'; ExecutableSHA256 = $releaseHash
            WorkloadMatrixSHA256 = (Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256).Hash
            ValidationRunId = 'ParserSelfTest'; ProcessCreationTimeUtc = '2026-08-08T00:00:00Z' }
        [System.IO.File]::WriteAllText((Join-Path $spoofRtxRoot 'validation-manifest.json'),
            (($link | ConvertTo-Json -Depth 6 -Compress) + "`n"), $script:Utf8NoBom)
        $summary = [ordered]@{ ProductVersion = 'v1.3.0 Ultimate'; PeFileVersion = '1.3.0.0'; ExecutableSHA256 = $releaseHash
            ValidationManifestPath = 'validation-manifest.json'; WorkloadMatrixPath = 'ultimate-workload-matrix.json' }
        [System.IO.File]::WriteAllText((Join-Path $spoofRtxRoot 'validation-summary.json'),
            (($summary | ConvertTo-Json -Compress) + "`n"), $script:Utf8NoBom)
        $rtxEntries = @()
        foreach ($file in Get-ChildItem -LiteralPath $spoofRtxRoot -File | Sort-Object Name) {
            $rtxEntries += [ordered]@{ RelativePath = $file.Name; Size = [int64]$file.Length
                SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
        }
        [System.IO.File]::WriteAllText((Join-Path $spoofRtxRoot 'result-manifest.json'),
            (([ordered]@{ SchemaVersion = 1; ArtifactCount = $rtxEntries.Count; Artifacts = $rtxEntries } |
                ConvertTo-Json -Depth 8 -Compress) + "`n"), $script:Utf8NoBom)
        $spoofRtxZip = Join-Path $tempRoot 'rtx-eight-labels.zip'
        Compress-DeterministicZip $spoofRtxRoot $spoofRtxZip
        Expect-Throw 'RtxEightLabelProjectionRejected' { [void](Assert-Rtx5070ResultZip $spoofRtxZip $releaseHash) }

        $portableRoot = Join-Path $tempRoot 'portable-rtx'
        [void](New-Item -ItemType Directory -Path (Join-Path $portableRoot 'schemas'))
        [System.IO.File]::WriteAllBytes((Join-Path $portableRoot 'God2SemanticRecoveryEngine.exe'), [byte[]](1,2,3,4))
        [System.IO.File]::WriteAllText((Join-Path $portableRoot 'RUN-RTX5070-ULTIMATE-VALIDATION.cmd'), '@exit /b 21', $script:Utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $portableRoot 'README-RTX5070-ULTIMATE-zh-TW.txt'), 'fixture', $script:Utf8NoBom)
        foreach ($schemaName in @('rtx5070-validation-result.schema.json', 'rtx5070-workload-matrix.schema.json',
                'rtx5070-validation-manifest.schema.json', 'validation-package-manifest.schema.json',
                'validation-contract.schema.json')) {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('assets\schemas\' + $schemaName)) `
                -Destination (Join-Path $portableRoot ('schemas\' + $schemaName))
        }
        $portableExeHash = (Get-FileHash -LiteralPath (Join-Path $portableRoot 'God2SemanticRecoveryEngine.exe') -Algorithm SHA256).Hash
        $portableContract = [ordered]@{ SchemaVersion = 1; ProductVersion = $script:ProductVersion; PackageKind = 'rtx5070'
            GeneratedAtUtc = '2026-08-08T00:00:00Z'; ContractStatusAtPackaging = 'PENDING_EXTERNAL_RUN'
            ExternalExecutionRequired = $true; IntegrityStatus = 'STRUCTURE_VERIFIED'; PassMustComeFromResultPackage = $true
            PackagedExecutableSHA256 = $portableExeHash; TargetClientSHA256 = $null; TargetGpu = 'NVIDIA GeForce RTX 5070'
            NativeEntrypointStatus = 'RTX5070EntrypointPackagedForExternalExecution' }
        [System.IO.File]::WriteAllText((Join-Path $portableRoot 'validation-contract.json'),
            (($portableContract | ConvertTo-Json -Depth 5 -Compress) + "`n"), $script:Utf8NoBom)
        [void](Write-PackageManifest $portableRoot 'rtx5070' '2026-08-08T00:00:00.000Z')
        $portableZip1 = Join-Path $tempRoot 'portable-1.zip'
        $portableZip2 = Join-Path $tempRoot 'portable-2.zip'
        Compress-DeterministicZip $portableRoot $portableZip1
        Compress-DeterministicZip $portableRoot $portableZip2
        $portableHashBefore = (Get-FileHash -LiteralPath $portableZip1 -Algorithm SHA256).Hash
        [void](Verify-ValidationZip $portableZip1)
        if ($portableHashBefore -ne (Get-FileHash -LiteralPath $portableZip1 -Algorithm SHA256).Hash -or
            $portableHashBefore -ne (Get-FileHash -LiteralPath $portableZip2 -Algorithm SHA256).Hash) {
            throw 'Portable ZIP validation mutated input or deterministic ZIP digests differ'
        }
        Pass 'DeterministicPortableZipAndReadOnlyValidationPass'
        [System.IO.File]::WriteAllText((Join-Path $portableRoot 'unmanifested-spoof.json'), '{}', $script:Utf8NoBom)
        $portableSpoofZip = Join-Path $tempRoot 'portable-spoof.zip'
        Compress-DeterministicZip $portableRoot $portableSpoofZip
        Expect-Throw 'PortableZipUnmanifestedSpoofRejected' { [void](Verify-ValidationZip $portableSpoofZip) }

        # Build a fully bound generated test-run manifest with 3,034 unique TRX
        # executions, the exact 70-test importer suite contract and all 19
        # first-party native analyzer/source identities.
        $reportRoot = Join-Path $tempRoot 'test-run'
        [void](New-Item -ItemType Directory -Path $reportRoot)
        $commandId = 'validated-command'
        $formalImporterCommandId = 'importer-formal-expected-block'
        $enhancedAcceptanceDomains = @()
        for ($domainIndex = 0; $domainIndex -lt 25; $domainIndex++) {
            $enhancedAcceptanceDomains += [ordered]@{
                index = $domainIndex
                domain = ('Domain{0:D2}' -f $domainIndex)
                probe = ('Probe{0:D2}' -f $domainIndex)
                contractStatus = if ($domainIndex -lt 4) { 'CONFIRMED_EXACT_BUILD_CONTRACT' } else { 'CANDIDATE_ONLY_EVIDENCE_BLOCKED' }
                activationAllowed = ($domainIndex -lt 4)
            }
        }
        $enhancedAcceptanceChecks = @(
            [ordered]@{ id = 'resource.201.byte-exact'; status = 'PASS'; scope = 'RELEASE_EXE'; evidence = 'fixture' },
            [ordered]@{ id = 'resource.202.byte-exact'; status = 'PASS'; scope = 'RELEASE_EXE'; evidence = 'fixture' },
            [ordered]@{ id = 'domains.inventory.exact-25'; status = 'PASS'; scope = 'DLL'; evidence = 'fixture' },
            [ordered]@{ id = 'domains.candidate-only.exact-21'; status = 'PASS'; scope = 'DLL'; evidence = 'fixture' },
            [ordered]@{ id = 'gui.smoke-visibility-unload-slot-reuse'; status = 'PASS'; scope = 'GUI'; evidence = 'fixture' }
        )
        $enhancedAcceptanceTestEvidence = [ordered]@{ path = 'fixture.json'; sha256 = [string]'E' * 64
            status = 'PASS'; total = 1; passed = 1; failed = 0 }
        $enhancedAuthorityRecords = @(
            [ordered]@{ AuthorityProfile = 'ExactSelfTestClient'; SessionId = 'fixture-selftest'
                SourceArtifact = 'Artifacts/fixture/selftest.json'; SourceSHA256 = [string]'A' * 64
                TargetExecutable = 'God2TraceSelfTestClient.exe'; TargetVersion = 'God2TraceSelfTestClient/1'
                TargetSHA256 = [string]'1' * 64; TargetProcessId = 1234; TargetProcessCreationTime = '1'
                CurrentSessionObserved = $false; HistoricalEvidenceReused = $false; FixtureOnly = $true
                PromotionEligible = $false; AttachStatus = 'ATTACHED'; DetachStatus = 'DETACHED'
                PackageSHA256 = $null; RuntimeEventCount = 25 },
            [ordered]@{ AuthorityProfile = 'CurrentOfficialClientLive'; SessionId = 'fixture-official-live'
                SourceArtifact = 'Artifacts/fixture/official-runtime-summary.json'; SourceSHA256 = [string]'B' * 64
                TargetExecutable = 'God2_opt.exe'; TargetVersion = '1.0.0.1'
                TargetSHA256 = $script:TargetClientSha256; TargetProcessId = 5678; TargetProcessCreationTime = '2'
                CurrentSessionObserved = $true; HistoricalEvidenceReused = $false; FixtureOnly = $false
                PromotionEligible = $false; AttachStatus = 'ATTACHED'; DetachStatus = 'DETACHED'
                PackageSHA256 = [string]'C' * 64; RuntimeEventCount = 46 }
        )
        $enhancedDeepArtifacts = @()
        foreach ($role in @('Capability','Runtime','Semantic','CandidateMapV3','PromotionLedger',
                'RuntimeObservations','CandidatePromotionResult','ExactTargetIdentity','RecoveryReadiness')) {
            $enhancedDeepArtifacts += [ordered]@{ role = $role
                path = ('Artifacts/fixture/' + $role + '.json'); sha256 = [string]'D' * 64 }
        }
        $enhancedInputBindings = @()
        foreach ($role in @('GUI_SOURCE','MAIN_SOURCE','EVIDENCE_SOURCE','ACCEPTANCE_SCRIPT','ACCEPTANCE_SCHEMA',
                'PROBE_PAYLOAD','INJECTOR_PAYLOAD','NETWORK_EVIDENCE','PRODUCT_EVIDENCE',
                'EVIDENCE_FIXTURE_REPORT','GUI_SMOKE_EVIDENCE')) {
            $enhancedInputBindings += [ordered]@{ role = $role; file = [ordered]@{
                    path = ('fixture/' + $role + '.bin'); length = 1; sha256 = [string]'E' * 64
                    lastWriteTimeUtc = '2026-08-08T00:00:00Z' } }
        }
        $enhancedFreshness = [ordered]@{
            releaseBuiltAfterAllBoundSources = $true; networkEvidenceAfterAllBoundInputs = $true
            productEvidenceAfterAllBoundInputs = $true; evidenceFixtureGeneratedAfterRelease = $true
            nativeProbeEvidenceAfterAllBoundInputs = $true; executableSelfTestsGeneratedAfterRelease = $true
            guiSmokeGeneratedAfterRelease = $true; acceptanceScriptSha256 = [string]'1' * 64
            acceptanceSchemaSha256 = [string]'2' * 64; guiSourceSha256 = [string]'3' * 64
            mainSourceSha256 = [string]'4' * 64; evidenceSourceSha256 = [string]'5' * 64
            enhancedCaptureStatusSchemaSha256 = [string]'6' * 64
            bridgeAwareStatusV3SchemaSha256 = [string]'7' * 64
            sharedHealthAvailabilitySchemaSha256 = [string]'8' * 64
            candidateMapAvailabilitySchemaSha256 = [string]'9' * 64
        }
        $enhancedAcceptanceFixture = [ordered]@{
            schemaVersion = 'god2-enhanced-capture-acceptance-v2'; generatedAtUtc = '2026-08-08T00:00:00Z'
            repositoryRoot = '.'; status = 'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS'
            standalonePayloadStatus = 'PASS'; finalExeUiStatus = 'PASS'
            expectedTarget = [ordered]@{ executable = 'God2_opt.exe'; architecture = 'x86'; version = '1.0.0.1'
                sha256 = $script:TargetClientSha256; activationStatus = 'CURRENT_OFFICIAL_CLIENT_IDENTITY_VERIFIED' }
            authorityConsistency = [ordered]@{ path = 'Artifacts/fixture/authority-consistency-report.json'
                sha256 = [string]'F' * 64; report = [ordered]@{
                    SchemaVersion = 'god2-evidence-authority-consistency-v1'
                    GeneratedAtUtc = '2026-08-08T00:00:00Z'; Status = 'PASS'; ConflictCount = 0
                    Conflicts = @(); Records = $enhancedAuthorityRecords } }
            deepRecoveryEvidence = [ordered]@{ status = 'PASS_FAIL_CLOSED_EVIDENCE_GENERATION'
                authorityProfile = 'CurrentOfficialClientLive'; domainCount = 25; confirmedContractCount = 4
                currentOfficialLiveProducerCount = 0; historicalOfficialLiveProducerCount = 0
                currentOfficialLiveVerifiedProducerCount = 0; verifiedDeepDomainCount = 0
                candidateOnlyBlockedDomainCount = 21; candidateCount = 42; promotionLedgerRowCount = 588
                activationAllowedCount = 0; serverReady = $false; databaseReady = $false
                artifacts = $enhancedDeepArtifacts }
            payloads = [ordered]@{
                releaseExecutable = [ordered]@{ path = 'God2SemanticRecoveryEngine.exe'; length = 1
                    sha256 = [string]'B' * 64; lastWriteTimeUtc = '2026-08-08T00:00:00Z' }
                probeResource201 = [ordered]@{ resourceId = 201; embeddedLength = 100; embeddedSha256 = [string]'C' * 64
                    standalone = [ordered]@{ path = 'God2PacketCaptureProbe.dll'; length = 100; sha256 = [string]'C' * 64
                        lastWriteTimeUtc = '2026-08-08T00:00:00Z' }
                    machine = 'I386'; optionalHeader = 'PE32'; isDll = $true
                    exports = @('God2TraceProbeWaitReady','God2TraceProbeStop','God2TraceProbeCanUnload',
                        'God2TraceProbeSemanticSelfTest','God2TraceProbeSharedTransportSelfTest')
                    authenticodeStatus = 'NotSigned' }
                injectorResource202 = [ordered]@{ resourceId = 202; embeddedLength = 101; embeddedSha256 = [string]'D' * 64
                    standalone = [ordered]@{ path = 'God2PacketCaptureInjector.exe'; length = 101; sha256 = [string]'D' * 64
                        lastWriteTimeUtc = '2026-08-08T00:00:00Z' }
                    machine = 'I386'; optionalHeader = 'PE32'; isDll = $false; exports = @(); authenticodeStatus = 'NotSigned' }
            }
            domainInventory = $enhancedAcceptanceDomains
            runtimeEvidence = [ordered]@{ officialClientLiveEvidenceClaimed = $true
                officialClientLiveGateStatus = 'PASS_OFFICIAL_EXACT_CLIENT_RUNTIME'
                probeDomains = [ordered]@{ domainCount = 25; confirmedContractDomainCount = 4
                    candidateOnlyBlockedDomainCount = 21; officialTargetIdentityVerified = $true }
                bridgeAwareStatusV3 = [ordered]@{ report = [ordered]@{ AuthorityProfile = 'TestOnlyExactFixture'
                        OfficialRuntimeObserved = $false; ExtendedReady = $false; InternalBridgeReady = $false } } }
            inputBindings = $enhancedInputBindings
            freshness = $enhancedFreshness
            selfTests = [ordered]@{ network = $enhancedAcceptanceTestEvidence; product = $enhancedAcceptanceTestEvidence
                evidenceFixture = $enhancedAcceptanceTestEvidence; nativeProbe = $enhancedAcceptanceTestEvidence
                semanticV2 = $enhancedAcceptanceTestEvidence; ultimate = $enhancedAcceptanceTestEvidence }
            guiVisibility = [ordered]@{ status = 'PASS'; evidenceObserved = $true; path = 'gui-smoke.json'
                DllTwentyFiveDomainStatusContract = 'PASS'; DllStrictUnloadResultContract = 'PASS'
                DllStrictUnloadFinalizationContract = 'PASS'; DllCancelResultReadBeforeCleanupContract = 'PASS'
                DllEnhancedCaptureConsumerStateLifecycle = 'PASS'; ThreeLanguageDllStrictUnloadContract = 'PASS'
                SharedTransportSlotReuseDomainAccounting = 'PASS'
                enhancedCaptureStatusSchemaVersion = 'god2-enhanced-capture-status-v2'
                initialStrictUnloadVerified = $false
                unloadPolicy = 'QuiesceRestoreDrainAndProveModuleAbsent;OtherwiseEvidenceBlockedAndBoundedRetry;NeverReportResidentInactiveAsStopped'
                expectedExecutableSha256 = [string]'B' * 64; observedExecutableSha256 = [string]'B' * 64
                executableSha256Bound = 'PASS'
                expectedSmokeKeys = @('DllEnhancedCaptureStatusVisible','DllEnhancedCaptureAutoEnabled',
                    'ThreeLanguageDllEnhancedCaptureStatus','ThreeLanguageDllEnhancedCaptureHelp',
                    'DllTwentyFiveDomainStatusContract','DllStrictUnloadResultContract',
                    'ThreeLanguageDllStrictUnloadContract','SharedTransportSlotReuseDomainAccounting',
                    'DllStrictUnloadFinalizationContract','DllCancelResultReadBeforeCleanupContract',
                    'DllEnhancedCaptureConsumerStateLifecycle') }
            checks = $enhancedAcceptanceChecks
            summary = [ordered]@{ total = 5; passed = 5; failed = 0; pending = 0
                candidateOnlyBlockedDomains = 21
                exactFinalStatus = 'ULTIMATE_INFRASTRUCTURE_PASS_DEEP_LIVE_PENDING' }
        }
        $gateCounts = [ordered]@{
            PacketCaptureCore265 = 265; SemanticAnalyzer29 = 29; EvidenceZipFixture68 = 68
            GpuContract43 = 43; GuiAutomated66 = 66; X86Instrumentation5 = 5
            InjectorLifecycle = 1; UltimateRecoverySelfTest51 = 51; InputReportParsing = 1
            UltimateLiveEntrypointContract = 1; ImporterFormalIdentityFailClosed = 1
            DllEnhancedCaptureAcceptance = 1; SemanticRingContinuity = 1
        }
        $evidenceDefinitions = New-Object System.Collections.Generic.List[object]
        $tests = New-Object System.Collections.Generic.List[object]
        $gateDefinitions = New-Object System.Collections.Generic.List[object]
        # The builder's local implementation gate requires one structurally valid
        # recovery bundle.  Keep the parser fixture explicitly untrusted (the
        # external process binding remains unavailable), but include it so the
        # end-to-end builder test exercises the real bundle inventory validator.
        $bundleFixtureName = 'ultimate-recovery-bundle.zip'
        [System.IO.File]::Copy($bundleZip, (Join-Path $reportRoot $bundleFixtureName), $false)
        $bundleEvidenceId = 'evidence-UltimateRecoveryBundleFixture'
        $bundleTestId = 'UltimateRecoveryBundleFixture:0001'
        $evidenceDefinitions.Add([ordered]@{
            EvidenceId = $bundleEvidenceId; RootIndex = 1; RelativePath = $bundleFixtureName
            SizeBytes = 0; SHA256 = [string]'0' * 64; Kind = 'RecoveryBundleZip'
            CommandId = $commandId
        })
        $tests.Add([ordered]@{
            TestId = $bundleTestId; GateName = 'UltimateRecoveryBundleFixture'
            Status = 'PASS'; CommandId = $commandId; EvidenceId = $bundleEvidenceId
        })
        $gateDefinitions.Add([ordered]@{
            Name = 'UltimateRecoveryBundleFixture'; Status = 'PASS'
            TestIds = @($bundleTestId); EvidenceIds = @($bundleEvidenceId)
        })
        foreach ($gateName in $gateCounts.Keys) {
            $fileName = if ($gateName -eq 'ImporterFormalIdentityFailClosed') {
                'formal-zip-blocked-output.json'
            } elseif ($gateName -eq 'DllEnhancedCaptureAcceptance') {
                'enhanced-capture-acceptance.json'
            } else { $gateName + '.json' }
            $gateFixture = if ($gateName -eq 'ImporterFormalIdentityFailClosed') {
                [ordered]@{ status = 'RECOVERY_BUNDLE_IMPORTER_BLOCKED'; code = 'build_identity.validation_status_not_trusted'
                    message = 'Exact TARGET identity at manifest.clientBuild is not computed and validated: expected ExactClientIdentityComputedAndValidated; found EvidenceBlockedClientIdentityNotComputed.'
                    productionDatabaseConnectionAttempted = $false; productionDatabaseMutationAttempted = $false }
            } elseif ($gateName -eq 'DllEnhancedCaptureAcceptance') {
                $enhancedAcceptanceFixture
            } elseif ($gateName -eq 'SemanticRingContinuity') {
                $continuitySegments = @()
                for ($segmentIndex = 0; $segmentIndex -lt 21; $segmentIndex++) {
                    $continuitySegments += [ordered]@{ Index = $segmentIndex }
                }
                [ordered]@{
                    SchemaVersion = 'god2-semantic-continuity-stress-v1'
                    Status = 'PASS'; Passed = $true; TransportMagic = 'GSR4'; TransportVersion = 4
                    EventCount = 2000001; Attempted = 2000001; Accepted = 2000001
                    P0Accepted = 1000001; P1Accepted = 1000000; P0Dropped = 0; P1Dropped = 0
                    WriteFailures = 0; PendingAfterDrain = 0; SequenceOrdered = $true
                    SegmentCount = 21; Segments = $continuitySegments; MergedSHA256 = [string]'A' * 64
                    ConcatenationVerified = $true; SemanticEvidenceIncomplete = $false
                }
            } elseif ($gateName -like 'GpuContract*') {
                $fixtureChecks = @(
                    [ordered]@{ Name = 'SixteenOperationSpecificGpuContractsExactAndDistinct'; Status = 'PASS' },
                    [ordered]@{ Name = 'PhysicalSixteenKernelEqualityOrTruthfulHardwareBlock'; Status = 'PASS' }
                )
                for ($fixtureCheckIndex = 3; $fixtureCheckIndex -le 43; $fixtureCheckIndex++) {
                    $fixtureChecks += [ordered]@{ Name = ('GpuContractFixture{0:D2}' -f $fixtureCheckIndex); Status = 'PASS' }
                }
                [ordered]@{ Status = 'PASS'; CheckCount = 43; Passed = 43; Failed = 0; Checks = $fixtureChecks
                    ImplementedOperationCount = 16; StructuredImplementationBlockedCount = 0; ModelBlockedCount = 1
                    OperationSpecificGpuImplementationStatus = '16_IMPLEMENTED_0_STRUCTURED_BLOCKED_1_MODEL_BLOCKED' }
            } else { [ordered]@{ Status = 'PASS' } }
            [System.IO.File]::WriteAllText((Join-Path $reportRoot $fileName),
                (($gateFixture | ConvertTo-Json -Depth 6 -Compress) + "`n"), $script:Utf8NoBom)
            $evidenceId = 'evidence-' + $gateName
            $evidenceKind = if ($gateName -like 'GpuContract*') { 'GpuContractJson' }
                elseif ($gateName -eq 'ImporterFormalIdentityFailClosed') { 'ImporterFormalIdentityFailClosedJson' }
                elseif ($gateName -eq 'DllEnhancedCaptureAcceptance') { 'EnhancedCaptureAcceptanceJson' }
                else { 'AggregateJson' }
            $gateCommandId = if ($gateName -eq 'ImporterFormalIdentityFailClosed') { $formalImporterCommandId } else { $commandId }
            $evidenceDefinitions.Add([ordered]@{ EvidenceId = $evidenceId; RootIndex = 1; RelativePath = $fileName
                SizeBytes = 0; SHA256 = [string]'0' * 64; Kind = $evidenceKind; CommandId = $gateCommandId })
            $gateTestIds = @()
            for ($index = 1; $index -le [int]$gateCounts[$gateName]; $index++) {
                $testId = if ($gateName -eq 'ImporterFormalIdentityFailClosed') {
                    'importer-formal:expected-exit4-output-directory-absent'
                } else { ('{0}:{1:D4}' -f $gateName, $index) }
                $tests.Add([ordered]@{ TestId = $testId; GateName = $gateName; Status = 'PASS'; CommandId = $gateCommandId; EvidenceId = $evidenceId })
                $gateTestIds += $testId
            }
            $gateEvidenceIds = @($evidenceId)
            if ($gateName -like 'GpuContract*') {
                $benchmarkFileName = 'gpu-benchmark.json'
                $benchmarkFixture = [ordered]@{ UltimateWorkloadCount = 17; UltimateGpuExecutedCount = 16
                    UltimateGpuImplementationBlockedCount = 0; UltimateGpuUnavailableBlockedCount = 0
                    UltimateModelBlockedCount = 1; UltimateEvidenceBlockedCount = 1; DroppedEvidence = 0
                    UltimateCpuGpuEquivalent = $true; ImplementedGpuCpuEquivalent = $true
                    ConcurrentBatchExecutionAvailable = $true; AiInferenceStatus = 'EvidenceBlockedModelUnavailable'
                    PhysicalBenchmarkHonesty = 'SixteenOperationSpecificCudaKernelsCpuOracleVerified;AiModelBlocked;BoundedMultiStreamInFlight'
                    Status = 'GPU_16_WORKLOADS_VERIFIED_AI_MODEL_BLOCKED' }
                [System.IO.File]::WriteAllText((Join-Path $reportRoot $benchmarkFileName),
                    (($benchmarkFixture | ConvertTo-Json -Depth 5 -Compress) + "`n"), $script:Utf8NoBom)
                $benchmarkEvidenceId = 'evidence-gpu-benchmark'
                $evidenceDefinitions.Add([ordered]@{ EvidenceId = $benchmarkEvidenceId; RootIndex = 1
                    RelativePath = $benchmarkFileName; SizeBytes = 0; SHA256 = [string]'0' * 64
                    Kind = 'GpuBenchmarkJson'; CommandId = $commandId })
                $gateEvidenceIds += $benchmarkEvidenceId
            }
            $gateDefinitions.Add([ordered]@{ Name = $gateName; Status = 'PASS'; TestIds = $gateTestIds; EvidenceIds = $gateEvidenceIds })
        }
        $localGpuContractFixture = Get-Content -Raw -LiteralPath (Join-Path $reportRoot 'GpuContract43.json') | ConvertFrom-Json
        $localGpuBenchmarkFixture = Get-Content -Raw -LiteralPath (Join-Path $reportRoot 'gpu-benchmark.json') | ConvertFrom-Json
        $localGpuRecords = @(
            [pscustomobject]@{ Kind = 'GpuContractJson'; Data = $localGpuContractFixture },
            [pscustomobject]@{ Kind = 'GpuBenchmarkJson'; Data = $localGpuBenchmarkFixture }
        )
        [void](Assert-LocalGpuImplementationEvidence $localGpuRecords)
        Pass 'LocalGpuSixteenOperationImplementationEvidenceAccepted'
        $localGpuContractFixture.StructuredImplementationBlockedCount = 1
        Expect-Throw 'LocalGpuImplementationBlockedCountRejected' {
            [void](Assert-LocalGpuImplementationEvidence $localGpuRecords)
        }
        $localGpuContractFixture.StructuredImplementationBlockedCount = 0
        $localGpuBenchmarkFixture.UltimateGpuExecutedCount = 15
        Expect-Throw 'LocalGpuMissingExecutedOperationRejected' {
            [void](Assert-LocalGpuImplementationEvidence $localGpuRecords)
        }
        $localGpuBenchmarkFixture.UltimateGpuExecutedCount = 16
        $localGpuBenchmarkFixture.PhysicalBenchmarkHonesty = 'SixteenOperationSpecificCudaKernelsCpuOracleVerified;AiModelBlocked;SequentialBatches'
        Expect-Throw 'LocalGpuSequentialConcurrencyContradictionRejected' {
            [void](Assert-LocalGpuImplementationEvidence $localGpuRecords)
        }
        $localGpuBenchmarkFixture.PhysicalBenchmarkHonesty = 'SixteenOperationSpecificCudaKernelsCpuOracleVerified;AiModelBlocked;BoundedMultiStreamInFlight'

        $continuityFixture = Get-Content -Raw -LiteralPath (Join-Path $reportRoot 'SemanticRingContinuity.json') | ConvertFrom-Json
        $continuityRecords = @([pscustomobject]@{ Kind = 'AggregateJson'; Data = $continuityFixture
                EvidenceId = 'evidence-SemanticRingContinuity' })
        [void](Assert-SemanticRingContinuityEvidence $continuityRecords)
        Pass 'SemanticRingContinuityEvidenceAccepted'
        $continuityFixture.P1Dropped = 1
        Expect-Throw 'SemanticRingContinuityDropRejected' {
            [void](Assert-SemanticRingContinuityEvidence $continuityRecords)
        }
        $continuityFixture.P1Dropped = 0

        $enhancedAcceptanceRecords = @([pscustomobject]@{ Kind = 'EnhancedCaptureAcceptanceJson'
            Data = $enhancedAcceptanceFixture; EvidenceId = 'evidence-DllEnhancedCaptureAcceptance' })
        [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        Pass 'DllEnhancedCaptureAcceptanceAccepted'
        $enhancedAcceptanceFixture.status = 'DLL_ENHANCED_CAPTURE_ACCEPTANCE_BLOCKED'
        Expect-Throw 'DllEnhancedCaptureBlockedStatusRejected' {
            [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        }
        $enhancedAcceptanceFixture.status = 'DLL_ENHANCED_CAPTURE_ACCEPTANCE_PASS'
        $enhancedAcceptanceFixture.payloads.probeResource201.embeddedSha256 = [string]'F' * 64
        Expect-Throw 'DllEnhancedCaptureEmbeddedPayloadSpoofRejected' {
            [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        }
        $enhancedAcceptanceFixture.payloads.probeResource201.embeddedSha256 = [string]'C' * 64
        $enhancedAcceptanceFixture.guiVisibility.status = 'PENDING_GUI_SMOKE_VISIBILITY_INPUT'
        Expect-Throw 'DllEnhancedCaptureInvisibleGuiStatusRejected' {
            [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        }
        $enhancedAcceptanceFixture.guiVisibility.status = 'PASS'
        $enhancedAcceptanceFixture.guiVisibility.DllTwentyFiveDomainStatusContract = 'FAIL'
        Expect-Throw 'DllEnhancedCaptureTwentyFiveDomainGuiSpoofRejected' {
            [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        }
        $enhancedAcceptanceFixture.guiVisibility.DllTwentyFiveDomainStatusContract = 'PASS'
        $enhancedAcceptanceFixture.guiVisibility.executableSha256Bound = 'FAIL'
        Expect-Throw 'DllEnhancedCaptureGuiExecutableBindingSpoofRejected' {
            [void](Assert-DllEnhancedCaptureAcceptanceEvidence $enhancedAcceptanceRecords ([string]'B' * 64))
        }
        $enhancedAcceptanceFixture.guiVisibility.executableSha256Bound = 'PASS'

        $runId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
        $importerTestFqns = @(
            'God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_OfflineCompiledSchemaMigrationAndReplayProofs_AreExecuted',
            'God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_IntegrationContract_ProvesZeroProductionDatabaseAndNetworkMutation'
        )
        for ($importerIndex = 3; $importerIndex -le 70; $importerIndex++) {
            $importerTestFqns += ('God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_OfflineVerifierFixture_{0:D2}' -f $importerIndex)
        }
        $trxBuilder = New-Object System.Text.StringBuilder
        [void]$trxBuilder.Append('<?xml version="1.0" encoding="utf-8"?><TestRun id="' + $runId + '" name="PORTABLE_TEST_RUN" runUser="PORTABLE_USER" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>')
        [void]$trxBuilder.Append('<UnitTest id="00000000-0000-0000-0000-000000000001" name="portable importer display"><Execution id="10000000-0000-0000-0000-000000000001" /><TestMethod className="God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests" name="ImportAsync_OfflineCompiledSchemaMigrationAndReplayProofs_AreExecuted" /></UnitTest>')
        [void]$trxBuilder.Append('</TestDefinitions><Results>')
        $dotNetTrxTestIds = @()
        $importerTrxTestIds = @()
        for ($index = 1; $index -le 3034; $index++) {
            $testGuid = ('00000000-0000-0000-0000-{0:D12}' -f $index)
            $executionGuid = ('10000000-0000-0000-0000-{0:D12}' -f $index)
            $testName = if ($index -eq 1) { 'portable importer display' }
                elseif ($index -le 70) { [string]$importerTestFqns[$index - 1] }
                else { ('God2.Other.Tests.PortableFixture.Test_{0:D4}' -f $index) }
            [void]$trxBuilder.Append('<UnitTestResult executionId="' + $executionGuid + '" testId="' + $testGuid +
                '" testName="' + $testName + '" outcome="Passed" />')
            $unique = ('trx:{0}:{1}:{2}' -f $runId, $testGuid, $executionGuid)
            if ($index -le 70) {
                $importerTrxTestIds += $unique
                $tests.Add([ordered]@{ TestId = $unique; GateName = 'ImporterOfflineVerifierContract'; Status = 'PASS'; CommandId = $commandId; EvidenceId = 'evidence-trx' })
            } else {
                $dotNetTrxTestIds += $unique
                $tests.Add([ordered]@{ TestId = $unique; GateName = 'DotNet2964'; Status = 'PASS'; CommandId = $commandId; EvidenceId = 'evidence-trx' })
            }
        }
        [void]$trxBuilder.Append('</Results><ResultSummary outcome="Completed"><Counters total="3034" passed="3034" failed="0" /></ResultSummary></TestRun>')
        [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'), $trxBuilder.ToString(), $script:Utf8NoBom)
        $evidenceDefinitions.Add([ordered]@{ EvidenceId = 'evidence-trx'; RootIndex = 1; RelativePath = 'dotnet.trx'
            SizeBytes = 0; SHA256 = [string]'0' * 64; Kind = 'DotNetTrx'; CommandId = $commandId })
        $gateDefinitions.Add([ordered]@{ Name = 'DotNet2964'; Status = 'PASS'; TestIds = $dotNetTrxTestIds; EvidenceIds = @('evidence-trx') })
        $gateDefinitions.Add([ordered]@{ Name = 'ImporterOfflineVerifierContract'; Status = 'PASS'; TestIds = $importerTrxTestIds; EvidenceIds = @('evidence-trx') })

        $analyzerTestIds = @()
        $analyzerEvidenceIds = @()
        $mandatoryAnalyzerSources = @(Get-UltimateMandatoryNativeAnalysisSources)
        for ($index = 1; $index -le $mandatoryAnalyzerSources.Count; $index++) {
            $analyzerId = ('Analyzer{0:D2}' -f $index)
            $fileName = $analyzerId + '.nativecodeanalysis.xml'
            [System.IO.File]::WriteAllText((Join-Path $reportRoot $fileName), '<?xml version="1.0" encoding="utf-8"?><DEFECTS></DEFECTS>', $script:Utf8NoBom)
            $evidenceId = 'evidence-' + $analyzerId
            $nativeSource = $mandatoryAnalyzerSources[$index - 1]
            $architecture = [string]$nativeSource.Architecture
            $evidenceDefinitions.Add([ordered]@{ EvidenceId = $evidenceId; RootIndex = 1; RelativePath = $fileName
                SizeBytes = 0; SHA256 = [string]'0' * 64; Kind = 'NativeCodeAnalysis'; CommandId = $commandId
                AnalyzerId = $analyzerId; Architecture = $architecture
                SourceRelativePath = [string]$nativeSource.RelativePath })
            $testId = 'analyzer:' + $analyzerId
            $tests.Add([ordered]@{ TestId = $testId; GateName = 'NativeStaticAnalysis'; Status = 'PASS'; CommandId = $commandId; EvidenceId = $evidenceId })
            $analyzerTestIds += $testId
            $analyzerEvidenceIds += $evidenceId
        }
        $gateDefinitions.Add([ordered]@{ Name = 'NativeStaticAnalysis'; Status = 'PASS'; TestIds = $analyzerTestIds; EvidenceIds = $analyzerEvidenceIds })

        $sourceEntries = @($mandatoryAnalyzerSources | ForEach-Object {
            $sourceRelative = [string]$_.RelativePath
            $sourceFile = Get-Item -LiteralPath (Join-Path $RepositoryRoot ($sourceRelative.Replace('/', '\')))
            [ordered]@{ RelativePath = $sourceRelative; SizeBytes = [int64]$sourceFile.Length
                SHA256 = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash }
        })
        $sourceHash = Get-CanonicalInventorySha256 $sourceEntries
        $fixtureExecutableInfo = $null
        $fakeExeHash = [string]'B' * 64
        $fixtureExeSize = [int64]1
        if (-not [string]::IsNullOrWhiteSpace($BuilderExecutablePath)) {
            $builderExeFull = Get-FullPath $BuilderExecutablePath
            if (-not (Test-Path -LiteralPath $builderExeFull -PathType Leaf)) { throw "Builder self-test EXE is missing: $builderExeFull" }
            $fixtureExecutableInfo = Get-Item -LiteralPath $builderExeFull
            if ($fixtureExecutableInfo.VersionInfo.FileVersion -ne $script:ProductVersion -or
                $fixtureExecutableInfo.VersionInfo.ProductVersion -ne $script:ProductVersion) {
                throw 'Builder self-test requires a v1.3.0.0 EXE'
            }
            $fakeExeHash = (Get-FileHash -LiteralPath $builderExeFull -Algorithm SHA256).Hash
            $fixtureExeSize = [int64]$fixtureExecutableInfo.Length
        }
        $manifestPath = Join-Path $reportRoot $script:TestRunManifestName
        function Write-TestRunFixtureManifest {
            $enhancedAcceptanceFixture.payloads.releaseExecutable.sha256 = $fakeExeHash
            $enhancedAcceptanceFixture.payloads.releaseExecutable.length = $fixtureExeSize
            $enhancedAcceptanceFixture.guiVisibility.expectedExecutableSha256 = $fakeExeHash
            $enhancedAcceptanceFixture.guiVisibility.observedExecutableSha256 = $fakeExeHash
            [System.IO.File]::WriteAllText((Join-Path $reportRoot 'enhanced-capture-acceptance.json'),
                (($enhancedAcceptanceFixture | ConvertTo-Json -Depth 12 -Compress) + "`n"), $script:Utf8NoBom)
            foreach ($entry in $evidenceDefinitions) {
                $file = Get-Item -LiteralPath (Join-Path $reportRoot ([string]$entry.RelativePath))
                $entry.SizeBytes = [int64]$file.Length
                $entry.SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
            $inventory = @($evidenceDefinitions | ForEach-Object { [ordered]@{
                RelativePath = ('input-01/' + [string]$_.RelativePath); SizeBytes = [int64]$_.SizeBytes; SHA256 = [string]$_.SHA256
            } })
            $inventoryHash = Get-CanonicalInventorySha256 $inventory
            $evidenceArray = @($evidenceDefinitions | ForEach-Object { $_ })
            $testArray = @($tests | ForEach-Object { $_ })
            $gateArray = @($gateDefinitions | ForEach-Object { $_ })
            $fixtureManifest = [ordered]@{
                SchemaVersion = 'god2-ultimate-test-run-manifest-v1'; RunId = 'ParserSelfTestRun'
                GeneratedAtUtc = '2026-08-08T00:00:00Z'; Product = $script:ProductName; ProductVersion = $script:ProductVersion
                Executable = [ordered]@{ SHA256 = $fakeExeHash; SizeBytes = $fixtureExeSize; FileVersion = $script:ProductVersion
                    ProductVersion = $script:ProductVersion; Architecture = 'x64' }
                Source = [ordered]@{ TreeSHA256 = $sourceHash; FileCount = $sourceEntries.Count; Files = $sourceEntries }
                EvidenceTreeSHA256 = $inventoryHash
                Commands = @(
                    [ordered]@{ CommandId = $commandId; Command = 'parser-self-test'; WorkingDirectory = '.'
                        StartedAtUtc = '2026-08-08T00:00:00Z'; CompletedAtUtc = '2026-08-08T00:00:01Z'
                        ExitCode = 0; ExpectedExitCode = 0; ExecutableSHA256 = $fakeExeHash; SourceTreeSHA256 = $sourceHash },
                    [ordered]@{ CommandId = $formalImporterCommandId; Command = 'importer-formal-identity-fail-closed'; WorkingDirectory = '.'
                        StartedAtUtc = '2026-08-08T00:00:00Z'; CompletedAtUtc = '2026-08-08T00:00:01Z'
                        ExitCode = 4; ExpectedExitCode = 4; ExecutableSHA256 = $fakeExeHash; SourceTreeSHA256 = $sourceHash }
                )
                Evidence = $evidenceArray; Tests = $testArray; Gates = $gateArray
            }
            [System.IO.File]::WriteAllText($manifestPath, (($fixtureManifest | ConvertTo-Json -Depth 12 -Compress) + "`n"), $script:Utf8NoBom)
        }
        function Get-TestRunFixtureFiles {
            return @(Get-ChildItem -LiteralPath $reportRoot -File | Sort-Object Name | ForEach-Object {
                [pscustomobject]@{ RootIndex = 1; Root = $reportRoot; File = $_; RelativePath = $_.Name }
            })
        }
        Write-TestRunFixtureManifest
        [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        Pass 'GeneratedTestRunManifestE2EPass'

        # Update hashes after each tamper so failures exercise semantic parser
        # checks rather than merely the outer SHA check.
        $originalTrx = [System.IO.File]::ReadAllText((Join-Path $reportRoot 'dotnet.trx'))
        $duplicateNode = '<UnitTestResult executionId="10000000-0000-0000-0000-000000000001" testId="00000000-0000-0000-0000-000000000001" outcome="Passed" />'
        [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'), $originalTrx.Replace('</Results>', $duplicateNode + '</Results>'), $script:Utf8NoBom)
        Write-TestRunFixtureManifest
        Expect-Throw 'DuplicateTrxExecutionRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        }
        [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'), $originalTrx, $script:Utf8NoBom)
        $requiredImporterFqn = 'God2.RecoveredDatabaseImporter.Tests.RecoveryImporterTests.ImportAsync_IntegrationContract_ProvesZeroProductionDatabaseAndNetworkMutation'
        [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'),
            $originalTrx.Replace($requiredImporterFqn, $requiredImporterFqn + '_Tampered'), $script:Utf8NoBom)
        Write-TestRunFixtureManifest
        Expect-Throw 'ImporterOfflineVerifierRequiredFqnRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        }
        [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'), $originalTrx, $script:Utf8NoBom)
        $formalEvidencePath = Join-Path $reportRoot 'formal-zip-blocked-output.json'
        $originalFormalEvidence = [System.IO.File]::ReadAllText($formalEvidencePath)
        $tamperedFormalEvidence = $originalFormalEvidence.Replace('EvidenceBlockedClientIdentityNotComputed', 'ExactClientIdentityComputedAndValidated')
        [System.IO.File]::WriteAllText($formalEvidencePath, $tamperedFormalEvidence, $script:Utf8NoBom)
        Write-TestRunFixtureManifest
        Expect-Throw 'ImporterFormalIdentityPassSpoofRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        }
        [System.IO.File]::WriteAllText($formalEvidencePath, $originalFormalEvidence, $script:Utf8NoBom)
        $analyzerPath = Join-Path $reportRoot 'Analyzer01.nativecodeanalysis.xml'
        $originalAnalyzer = [System.IO.File]::ReadAllText($analyzerPath)
        [System.IO.File]::WriteAllText($analyzerPath, '<anything />', $script:Utf8NoBom)
        Write-TestRunFixtureManifest
        Expect-Throw 'ArbitraryAnalyzerXmlRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        }
        [System.IO.File]::WriteAllText($analyzerPath, $originalAnalyzer, $script:Utf8NoBom)
        $ultimateRecoveryEvidence = $evidenceDefinitions | Where-Object {
            [string](Get-ObjectProperty $_ @('SourceRelativePath')) -eq 'tools/God2.PacketCapture/UltimateRecovery.cpp'
        } | Select-Object -First 1
        $originalUltimateRecoverySource = [string]$ultimateRecoveryEvidence.SourceRelativePath
        $ultimateRecoveryEvidence.SourceRelativePath = 'tools/God2.PacketCapture/Main.cpp'
        Write-TestRunFixtureManifest
        Expect-Throw 'UltimateRecoveryAnalyzerOmissionRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) $fakeExeHash $RepositoryRoot)
        }
        $ultimateRecoveryEvidence.SourceRelativePath = $originalUltimateRecoverySource
        Write-TestRunFixtureManifest
        Expect-Throw 'WrongPackagedExecutableBindingRejected' {
            [void](Assert-TestRunManifest (Get-Item $manifestPath) (Get-TestRunFixtureFiles) @($reportRoot) ([string]'C' * 64) $RepositoryRoot)
        }

        if ($null -ne $fixtureExecutableInfo) {
            if ([string]::IsNullOrWhiteSpace($BuilderOutputRoot)) { throw 'Builder self-test requires -OutputRoot' }
            # Restore every tampered input and regenerate the exact bound manifest.
            [System.IO.File]::WriteAllText((Join-Path $reportRoot 'dotnet.trx'), $originalTrx, $script:Utf8NoBom)
            [System.IO.File]::WriteAllText($analyzerPath, $originalAnalyzer, $script:Utf8NoBom)
            Write-TestRunFixtureManifest
            $builderOutput = Get-FullPath $BuilderOutputRoot
            $repeatOutput = $builderOutput + '-repeat'
            if (Test-Path -LiteralPath $builderOutput) { throw "Builder self-test output already exists: $builderOutput" }
            if (Test-Path -LiteralPath $repeatOutput) { throw "Builder repeat output already exists: $repeatOutput" }
            $scriptPath = Join-Path $PSScriptRoot 'Build-UltimateRelease.ps1'
            $buildOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
                -ExecutablePath $fixtureExecutableInfo.FullName -TestReportRoot $reportRoot -OutputRoot $builderOutput 2>&1
            if ($LASTEXITCODE -ne 0) { throw ('Builder e2e failed: ' + (($buildOutput | ForEach-Object { [string]$_ }) -join ' | ')) }
            $beforeValidation = @{}
            foreach ($file in Get-ChildItem -LiteralPath $builderOutput -Recurse -File) {
                $beforeValidation[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
            $validationOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
                -ValidateOnly -OutputRoot $builderOutput 2>&1
            if ($LASTEXITCODE -ne 0) { throw ('Builder ValidateOnly failed: ' + (($validationOutput | ForEach-Object { [string]$_ }) -join ' | ')) }
            foreach ($entry in $beforeValidation.GetEnumerator()) {
                if ((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash -ne [string]$entry.Value) {
                    throw "ValidateOnly mutated an artifact: $($entry.Key)"
                }
            }
            $repeatBuildOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
                -ExecutablePath $fixtureExecutableInfo.FullName -TestReportRoot $reportRoot -OutputRoot $repeatOutput 2>&1
            if ($LASTEXITCODE -ne 0) { throw ('Builder repeat e2e failed: ' + (($repeatBuildOutput | ForEach-Object { [string]$_ }) -join ' | ')) }
            $inventoryOne = @()
            foreach ($file in Get-ChildItem -LiteralPath $builderOutput -Recurse -File) {
                $inventoryOne += [ordered]@{ RelativePath = Convert-ToSlashPath $file.FullName.Substring($builderOutput.TrimEnd('\').Length + 1)
                    SizeBytes = [int64]$file.Length; SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
            }
            $inventoryTwo = @()
            foreach ($file in Get-ChildItem -LiteralPath $repeatOutput -Recurse -File) {
                $inventoryTwo += [ordered]@{ RelativePath = Convert-ToSlashPath $file.FullName.Substring($repeatOutput.TrimEnd('\').Length + 1)
                    SizeBytes = [int64]$file.Length; SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
            }
            if ((Get-CanonicalInventorySha256 $inventoryOne) -ne (Get-CanonicalInventorySha256 $inventoryTwo)) {
                throw 'Identical builder inputs did not produce identical output inventories'
            }
            Pass 'BuilderDryRunValidateOnlyImmutable'
            Pass 'BuilderDeterministicRepeatOutput'
        }

        $result = [ordered]@{ Status = 'ULTIMATE_RELEASE_PARSER_SELFTEST_PASS'; Passed = $passed.Count; Failed = 0; Tests = @($passed) }
        return $result
    } finally {
        if (Test-Path -LiteralPath $tempRoot) {
            $resolvedTemp = Get-FullPath $tempRoot
            $systemTemp = (Get-FullPath ([System.IO.Path]::GetTempPath())).TrimEnd('\') + '\'
            if (-not $resolvedTemp.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not [System.IO.Path]::GetFileName($resolvedTemp).StartsWith('God2UltimateReleaseParser-')) {
                throw "Refusing unsafe parser self-test cleanup: $resolvedTemp"
            }
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
        }
    }
}

function Assert-ImporterResultManifest {
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )
    $rootFull = Get-FullPath $RunRoot
    $manifestFull = Get-FullPath $ManifestPath
    if (-not (Test-IsWithin $rootFull $manifestFull) -or
        [System.IO.Path]::GetFileName($manifestFull) -ine 'result-manifest.json') {
        throw "Importer result manifest path is unsafe: $manifestFull"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestFull | ConvertFrom-Json
    $selfHashIncluded = Get-ObjectProperty $manifest @('manifestSelfHashIncluded')
    if ([string](Get-ObjectProperty $manifest @('schemaVersion')) -ne 'god2-recovery-import-result-manifest-v1' -or
        [string](Get-ObjectProperty $manifest @('status')) -ne 'PASS' -or
        [string](Get-ObjectProperty $manifest @('algorithm')) -ne 'SHA-256' -or
        $selfHashIncluded -isnot [bool] -or [bool]$selfHashIncluded) {
        throw 'Importer result manifest header contract mismatch'
    }
    $declared = @{}
    $entries = @(Get-ObjectProperty $manifest @('entries'))
    if ($entries.Count -eq 0) { throw 'Importer result manifest has no entries' }
    foreach ($entry in $entries) {
        $relative = Convert-ToSlashPath ([string](Get-ObjectProperty $entry @('relativePath')))
        Assert-SafeRelativePath $relative
        if ($relative -ieq 'result-manifest.json' -or $declared.ContainsKey($relative)) {
            throw "Importer result manifest has a duplicate or self entry: $relative"
        }
        $size = Convert-ToInt64OrDefault (Get-ObjectProperty $entry @('sizeBytes')) -1
        $hash = [string](Get-ObjectProperty $entry @('sha256'))
        $path = Get-FullPath (Join-Path $rootFull ($relative.Replace('/', '\')))
        if (-not (Test-IsWithin $rootFull $path) -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or
            $size -lt 0 -or $hash -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "Importer result manifest entry is missing or invalid: $relative"
        }
        $file = Get-Item -LiteralPath $path
        if ($file.Length -ne $size -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash.ToUpperInvariant()) {
            throw "Importer result manifest integrity mismatch: $relative"
        }
        $declared[$relative] = $true
    }
    $observed = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File | Where-Object {
        (Get-FullPath $_.FullName) -ne $manifestFull
    } | ForEach-Object {
        Convert-ToSlashPath $_.FullName.Substring($rootFull.TrimEnd('\').Length + 1)
    })
    if ($observed.Count -ne $declared.Count) { throw 'Importer result manifest coverage count mismatch' }
    foreach ($relative in $observed) {
        if (-not $declared.ContainsKey($relative)) { throw "Unmanifested importer artifact: $relative" }
    }
}

function Verify-UltimateOutput {
    param([Parameter(Mandatory = $true)][string]$Root)
    $rootFull = Get-FullPath $Root
    Assert-NoReparsePoints $rootFull
    $manifestPath = Join-Path $rootFull $script:ArtifactManifestJson
    $csvPath = Join-Path $rootFull $script:ArtifactManifestCsv
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $csvPath -PathType Leaf)) {
        throw "Ultimate output manifests are missing: $rootFull"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ([string]$manifest.ProductVersion -ne $script:ProductVersion -or
        [int64]$manifest.SchemaVersion -ne 1) { throw 'Ultimate manifest identity mismatch' }
    $expected = @{}
    foreach ($artifact in $manifest.Artifacts) {
        $relative = Convert-ToSlashPath ([string]$artifact.RelativePath)
        Assert-SafeRelativePath $relative
        if ($expected.ContainsKey($relative)) { throw "Duplicate Ultimate manifest path: $relative" }
        $expected[$relative] = $artifact
        $path = Get-FullPath (Join-Path $rootFull ($relative.Replace('/', '\')))
        if (-not (Test-IsWithin $rootFull $path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Ultimate artifact is missing or unsafe: $relative"
        }
        $file = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ([int64]$file.Length -ne [int64]$artifact.SizeBytes -or $hash -ne [string]$artifact.SHA256) {
            throw "Ultimate artifact integrity mismatch: $relative"
        }
    }
    if ([int64]$manifest.ArtifactCount -ne $expected.Count) { throw 'Ultimate manifest declared artifact count mismatch' }
    $observed = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File | ForEach-Object {
        Convert-ToSlashPath $_.FullName.Substring($rootFull.TrimEnd('\').Length + 1)
    } | Where-Object { $_ -notin @($script:ArtifactManifestJson, $script:ArtifactManifestCsv) })
    foreach ($relative in $observed) {
        if (-not $expected.ContainsKey($relative)) { throw "Unmanifested Ultimate artifact: $relative" }
    }
    if ($observed.Count -ne $expected.Count) { throw 'Ultimate artifact count mismatch' }
    $csv = @(Import-Csv -LiteralPath $csvPath)
    if ($csv.Count -ne $expected.Count) { throw 'Ultimate CSV manifest count mismatch' }
    $csvPaths = @{}
    foreach ($row in $csv) {
        $csvRelative = Convert-ToSlashPath ([string]$row.RelativePath)
        Assert-SafeRelativePath $csvRelative
        if ($csvPaths.ContainsKey($csvRelative)) { throw "Duplicate Ultimate CSV manifest path: $csvRelative" }
        $csvPaths[$csvRelative] = $true
        if (-not $expected.ContainsKey($csvRelative)) { throw "Unknown CSV artifact: $csvRelative" }
        $jsonRow = $expected[$csvRelative]
        if ([string]$row.SHA256 -ne [string]$jsonRow.SHA256 -or [int64]$row.SizeBytes -ne [int64]$jsonRow.SizeBytes) {
            throw "Ultimate JSON/CSV manifest disagreement: $csvRelative"
        }
    }
    $exe = Join-Path $rootFull 'release\God2SemanticRecoveryEngine.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Ultimate release EXE is missing' }
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($version.FileVersion -ne $script:ProductVersion -or $version.ProductVersion -ne $script:ProductVersion) {
        throw 'Ultimate release EXE identity is not 1.3.0.0'
    }
    $releaseExeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    foreach ($report in $script:ReportNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $rootFull ('reports\' + $report)) -PathType Leaf)) {
            throw "Required Ultimate report is missing: $report"
        }
    }
    [void](Assert-SchemaRegistryDirectory (Join-Path $rootFull 'schemas'))
    $liveZip = Verify-ValidationZip (Join-Path $rootFull 'validation\UltimateDeepRecoveryLiveValidationPackage.zip')
    $rtxZip = Verify-ValidationZip (Join-Path $rootFull 'validation\RTX5070UltimateValidationPackage.zip')
    if ($liveZip.PackagedExecutableSHA256 -ne $releaseExeSha256 -or
        $rtxZip.PackagedExecutableSHA256 -ne $releaseExeSha256) {
        throw 'Portable validation packages are not bound to the exact release executable'
    }
    $evidenceSummaryPath = Join-Path $rootFull 'reports\evidence-summary.json'
    $evidenceSummary = Get-Content -Raw -LiteralPath $evidenceSummaryPath -Encoding UTF8 | ConvertFrom-Json
    if ([string](Get-ObjectProperty $manifest @('FinalEngineeringStatus')) -ne
            'ULTIMATE_DEEP_RECOVERY_IMPLEMENTATION_COMPLETE_WITH_EXTERNAL_LIVE_GATE_PENDING' -or
        [string](Get-ObjectProperty $evidenceSummary @('FinalEngineeringStatus')) -ne
            'ULTIMATE_DEEP_RECOVERY_IMPLEMENTATION_COMPLETE_WITH_EXTERNAL_LIVE_GATE_PENDING' -or
        [string](Get-ObjectProperty $evidenceSummary @('ExternalLiveGateStatus')) -ne
            'EVIDENCE_BLOCKED_EXTERNAL_LIVE_GATE_PENDING' -or
        [string](Get-ObjectProperty $evidenceSummary @('PackagedExecutableSHA256')) -ne $releaseExeSha256 -or
        [string](Get-ObjectProperty $evidenceSummary @('AiModelStatus')) -ne 'EvidenceBlockedModelUnavailable' -or
        [string](Get-ObjectProperty $evidenceSummary @('LiveRunnerContractStatus')) -ne 'PASS' -or
        [string](Get-ObjectProperty $evidenceSummary @('TestRunManifestSHA256')) -notmatch '^[A-F0-9]{64}$' -or
        [string](Get-ObjectProperty $evidenceSummary @('SourceTreeSHA256')) -notmatch '^[A-F0-9]{64}$' -or
        [string](Get-ObjectProperty $evidenceSummary @('EvidenceTreeSHA256')) -notmatch '^[A-F0-9]{64}$') {
        throw 'Ultimate evidence summary cryptographic linkage/status mismatch'
    }
    $packagedEvidenceRoot = Join-Path $rootFull 'reports\evidence'
    $packagedInputRoots = @(Get-ChildItem -LiteralPath $packagedEvidenceRoot -Directory | Sort-Object Name)
    if ($packagedInputRoots.Count -eq 0) { throw 'Ultimate output has no packaged test-report roots' }
    $packagedReportRoots = @()
    $packagedReportFiles = @()
    for ($index = 0; $index -lt $packagedInputRoots.Count; $index++) {
        $expectedName = 'input-{0:D2}' -f ($index + 1)
        if ($packagedInputRoots[$index].Name -cne $expectedName) {
            throw "Packaged test-report root sequence is invalid: $($packagedInputRoots[$index].Name)"
        }
        $packagedRoot = $packagedInputRoots[$index].FullName
        $packagedReportRoots += $packagedRoot
        foreach ($file in Get-ChildItem -LiteralPath $packagedRoot -Recurse -File | Sort-Object FullName) {
            $relative = Convert-ToSlashPath $file.FullName.Substring($packagedRoot.TrimEnd('\').Length + 1)
            $packagedReportFiles += [pscustomobject]@{
                RootIndex = $index + 1
                Root = $packagedRoot
                File = $file
                RelativePath = $relative
            }
        }
    }
    $packagedManifestCandidates = @($packagedReportFiles | Where-Object { $_.File.Name -ceq $script:TestRunManifestName })
    if ($packagedManifestCandidates.Count -ne 1) { throw 'Ultimate output does not contain exactly one packaged test-run manifest' }
    $packagedTestRun = Assert-TestRunManifest $packagedManifestCandidates[0].File $packagedReportFiles `
        $packagedReportRoots $releaseExeSha256 $repoRoot -SkipRepositorySourceContent
    if ($packagedTestRun.ManifestSHA256 -ne [string](Get-ObjectProperty $evidenceSummary @('TestRunManifestSHA256')) -or
        $packagedTestRun.SourceTreeSHA256 -ne [string](Get-ObjectProperty $evidenceSummary @('SourceTreeSHA256')) -or
        $packagedTestRun.EvidenceTreeSHA256 -ne [string](Get-ObjectProperty $evidenceSummary @('EvidenceTreeSHA256'))) {
        throw 'Packaged test-run manifest/evidence-tree linkage mismatch'
    }
    $packagedBundleEvidence = @($packagedTestRun.EvidenceMap.Values | Where-Object {
        [string](Get-ObjectProperty $_.Definition @('Kind')) -eq 'RecoveryBundleZip'
    })
    if ($packagedBundleEvidence.Count -ne 1) { throw 'Ultimate output lost the unique native recovery bundle evidence' }
    [void](Assert-UltimateRecoveryBundleZip $packagedBundleEvidence[0].Source.File.FullName)

    $externalEvidenceRoot = Join-Path $rootFull 'validation\external-evidence'
    $expectedRtxHash = [string](Get-ObjectProperty $evidenceSummary @('Rtx5070ValidatedResultSHA256'))
    $expectedAuditHash = [string](Get-ObjectProperty $evidenceSummary @('Rtx5070ExternalAuditSHA256'))
    if ([string]::IsNullOrWhiteSpace($expectedRtxHash) -and [string]::IsNullOrWhiteSpace($expectedAuditHash)) {
        if (Test-Path -LiteralPath $externalEvidenceRoot) {
            $unexpectedExternal = @(Get-ChildItem -LiteralPath $externalEvidenceRoot -File)
            if ($unexpectedExternal.Count -ne 0) { throw 'Unbound external evidence is present in Ultimate output' }
        }
    } else {
        $resultArchives = @(Get-ChildItem -LiteralPath $externalEvidenceRoot -File -Filter '*.zip')
        $auditPath = Join-Path $externalEvidenceRoot 'rtx5070-external-attestation.json'
        if ($resultArchives.Count -ne 1 -or -not (Test-Path -LiteralPath $auditPath -PathType Leaf)) {
            throw 'Packaged RTX result/attestation pair is incomplete'
        }
        $packagedRtxPair = Assert-Rtx5070AttestedPair $resultArchives[0].FullName $auditPath $releaseExeSha256
        if ($packagedRtxPair.ResultSHA256 -ne $expectedRtxHash -or
            $packagedRtxPair.AttestationSHA256 -ne $expectedAuditHash -or
            $packagedRtxPair.UltimateScope -ne [string](Get-ObjectProperty $evidenceSummary @('Rtx5070ExternalAuditScope')) -or
            $packagedRtxPair.CanPromoteUltimate -ne [bool](Get-ObjectProperty $evidenceSummary @('Rtx5070ExternalAuditPromotedUltimateGate'))) {
            throw 'Packaged RTX result/attestation cryptographic revalidation failed'
        }
    }
    $testRunArtifacts = @($expected.Keys | Where-Object { $_.EndsWith('/' + $script:TestRunManifestName, [System.StringComparison]::Ordinal) })
    if ($testRunArtifacts.Count -ne 1 -or
        [string](Get-ObjectProperty $expected[$testRunArtifacts[0]] @('SHA256')) -ne
            [string](Get-ObjectProperty $evidenceSummary @('TestRunManifestSHA256'))) {
        throw 'Ultimate output does not contain exactly one SHA-bound test-run manifest'
    }
    return [pscustomobject][ordered]@{
        Status = 'ULTIMATE_ARTIFACT_INTEGRITY_PASS'
        EngineeringStatus = [string]$manifest.FinalEngineeringStatus
        Root = $rootFull
        ArtifactCount = $expected.Count
        ManifestSHA256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        CsvManifestSHA256 = (Get-FileHash -LiteralPath $csvPath -Algorithm SHA256).Hash
        LiveValidationPackageSHA256 = $liveZip.SHA256
        Rtx5070ValidationPackageSHA256 = $rtxZip.SHA256
    }
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot '..\..\..')
$artifactsRoot = Get-FullPath (Join-Path $repoRoot 'Artifacts')
$builtInSchemaRoot = Join-Path $PSScriptRoot 'assets\schemas'
[void](Assert-SchemaRegistryDirectory $builtInSchemaRoot)
if ($ParserSelfTest) {
    Invoke-UltimateReleaseParserSelfTests $repoRoot $BuilderSelfTestExecutablePath $OutputRoot | ConvertTo-Json -Depth 8
    return
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $artifactsRoot 'Ultimate' }
$outputFull = Get-FullPath $OutputRoot
if (-not (Test-IsWithin $artifactsRoot $outputFull) -or (Get-FullPath $artifactsRoot) -eq $outputFull) {
    throw "Ultimate output must be a dedicated child of the workspace Artifacts directory: $outputFull"
}

if ($ValidateOnly) {
    if (-not (Test-Path -LiteralPath $outputFull -PathType Container)) { throw "Ultimate output does not exist: $outputFull" }
    Verify-UltimateOutput $outputFull | ConvertTo-Json -Depth 6
    return
}

# The v1.3 identity gate is evaluated before creating any output directory. A v1.2
# binary may be used only for a negative test and can never be renamed into release.
$executableFull = Get-FullPath $ExecutablePath
if (-not (Test-Path -LiteralPath $executableFull -PathType Leaf)) { throw "Built EXE is missing: $executableFull" }
Assert-NoReparsePoints $executableFull
$executableInfo = Get-Item -LiteralPath $executableFull
$executableSha256 = (Get-FileHash -LiteralPath $executableFull -Algorithm SHA256).Hash
$executableVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executableFull)
if ($executableVersion.FileVersion -ne $script:ProductVersion -or
    $executableVersion.ProductVersion -ne $script:ProductVersion) {
    throw "Refusing to publish a non-v1.3 binary. FileVersion=$($executableVersion.FileVersion), ProductVersion=$($executableVersion.ProductVersion)"
}
$peBytes = [System.IO.File]::ReadAllBytes($executableFull)
if ($peBytes.Length -lt 512) { throw 'Built EXE is not a valid PE image' }
$peOffset = [BitConverter]::ToInt32($peBytes, 0x3c)
if ($peOffset -lt 0 -or $peOffset + 96 -ge $peBytes.Length) { throw 'Built EXE has an invalid PE header offset' }
$machine = [BitConverter]::ToUInt16($peBytes, $peOffset + 4)
$optionalMagic = [BitConverter]::ToUInt16($peBytes, $peOffset + 24)
$subsystem = [BitConverter]::ToUInt16($peBytes, $peOffset + 24 + 68)
if ($machine -ne 0x8664 -or $optionalMagic -ne 0x020b -or $subsystem -ne 2) {
    throw ("Ultimate EXE must be PE32+ x64 Windows GUI. Machine=0x{0:X4}, Magic=0x{1:X4}, Subsystem={2}" -f $machine, $optionalMagic, $subsystem)
}

$resolvedReportRoots = @()
foreach ($root in $TestReportRoot) {
    $full = Get-FullPath $root
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Test report root is missing: $full" }
    Assert-NoReparsePoints $full
    $resolvedReportRoots += $full
}
$resolvedSchemaRoots = @()
foreach ($root in $SchemaRoot) {
    $full = Get-FullPath $root
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "Schema root is missing: $full" }
    Assert-NoReparsePoints $full
    $resolvedSchemaRoots += $full
}
Assert-ExternalSchemaRootsMatchBuiltIns $resolvedSchemaRoots $builtInSchemaRoot

$validatedRtxResultFull = $null
$validatedRtxAttestationFull = $null
$validatedRtxAttestation = $null
$validatedRtxUltimateScope = $null
$validatedRtxCanPromoteUltimate = $false
$validatedRtxInner = $null
$validatedRtxPair = $null
$hasRtxResult = -not [string]::IsNullOrWhiteSpace($ValidatedRtx5070ResultPath)
$hasRtxAttestation = -not [string]::IsNullOrWhiteSpace($ValidatedRtx5070AttestationPath)
if ($hasRtxResult -xor $hasRtxAttestation) {
    throw 'Validated RTX 5070 evidence requires both -ValidatedRtx5070ResultPath and -ValidatedRtx5070AttestationPath'
}
if ($hasRtxResult) {
    $validatedRtxResultFull = Get-FullPath $ValidatedRtx5070ResultPath
    $validatedRtxAttestationFull = Get-FullPath $ValidatedRtx5070AttestationPath
    # The attestation is never authoritative for archive contents. The pair
    # validator independently opens the ZIP and binds its exact bytes to the
    # attestation, executable and workload matrix.
    $validatedRtxPair = Assert-Rtx5070AttestedPair $validatedRtxResultFull `
        $validatedRtxAttestationFull $executableSha256
    $validatedRtxAttestation = $validatedRtxPair.Attestation
    $validatedRtxUltimateScope = $validatedRtxPair.UltimateScope
    $validatedRtxCanPromoteUltimate = $validatedRtxPair.CanPromoteUltimate
    $validatedRtxInner = $validatedRtxPair.Inner
}

$reportFiles = @()
for ($rootIndex = 0; $rootIndex -lt $resolvedReportRoots.Count; $rootIndex++) {
    $root = $resolvedReportRoots[$rootIndex]
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
        if (-not (Test-IsWithin $root $file.FullName)) { throw "Test report path escaped input root: $($file.FullName)" }
        $relative = Convert-ToSlashPath $file.FullName.Substring($root.TrimEnd('\').Length + 1)
        Assert-SafeRelativePath $relative
        $reportFiles += [pscustomobject]@{ RootIndex = $rootIndex + 1; Root = $root; File = $file; RelativePath = $relative }
    }
}
if ($reportFiles.Count -eq 0) { throw 'No test reports were found in the supplied roots' }
$manifestCandidates = @($reportFiles | Where-Object { $_.File.Name -ceq $script:TestRunManifestName })
if ($manifestCandidates.Count -ne 1) {
    throw "Formal publication requires exactly one generated $($script:TestRunManifestName); found $($manifestCandidates.Count)"
}
$testRun = Assert-TestRunManifest $manifestCandidates[0].File $reportFiles $resolvedReportRoots $executableSha256 $repoRoot
$testRunManifestSha256 = $testRun.ManifestSHA256

$jsonRecords = @()
$jsonParseFailures = @()
foreach ($item in $testRun.EvidenceMap.Values | Where-Object {
    ([string](Get-ObjectProperty $_.Definition @('Kind'))).EndsWith('Json')
}) {
    $data = Get-Content -Raw -LiteralPath $item.Source.File.FullName -Encoding UTF8 | ConvertFrom-Json
    $jsonRecords += [pscustomobject]@{
        File = $item.Source.File
        RelativePath = $item.Source.RelativePath
        RootIndex = $item.Source.RootIndex
        Data = $data
        EvidenceTimeUtc = Get-JsonRecordTime $item.Source.File $data
        EvidenceId = [string](Get-ObjectProperty $item.Definition @('EvidenceId'))
        Kind = [string](Get-ObjectProperty $item.Definition @('Kind'))
    }
}
$localGpuImplementation = Assert-LocalGpuImplementationEvidence $jsonRecords
$dllEnhancedCaptureAcceptance = Assert-DllEnhancedCaptureAcceptanceEvidence $jsonRecords $executableSha256
$semanticRingContinuity = Assert-SemanticRingContinuityEvidence $jsonRecords

$gates = New-Object System.Collections.Generic.List[object]
$resolvedManifestGates = [ordered]@{}
$logicalGateContracts = @(
    [pscustomobject]@{ LogicalName = 'PacketCaptureCore'; Pattern = '^PacketCaptureCore[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'SemanticAnalyzer'; Pattern = '^SemanticAnalyzer[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'EvidenceZipFixture'; Pattern = '^EvidenceZipFixture[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'GpuContract'; Pattern = '^GpuContract[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'GuiAutomated'; Pattern = '^GuiAutomated[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'DllEnhancedCaptureAcceptance'; Pattern = '^DllEnhancedCaptureAcceptance$' },
    [pscustomobject]@{ LogicalName = 'X86Instrumentation'; Pattern = '^X86Instrumentation[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'InjectorLifecycle'; Pattern = '^InjectorLifecycle$' },
    [pscustomobject]@{ LogicalName = 'DotNet'; Pattern = '^DotNet[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'ImporterOfflineVerifierContract'; Pattern = '^ImporterOfflineVerifierContract$' },
    [pscustomobject]@{ LogicalName = 'ImporterFormalIdentityFailClosed'; Pattern = '^ImporterFormalIdentityFailClosed$' },
    [pscustomobject]@{ LogicalName = 'NativeStaticAnalysis'; Pattern = '^NativeStaticAnalysis$' },
    [pscustomobject]@{ LogicalName = 'UltimateRecoverySelfTest'; Pattern = '^UltimateRecoverySelfTest[0-9]+$' },
    [pscustomobject]@{ LogicalName = 'UltimateLiveEntrypointContract'; Pattern = '^UltimateLiveEntrypointContract$' },
    [pscustomobject]@{ LogicalName = 'SemanticRingContinuity'; Pattern = '^SemanticRingContinuity$' }
)
foreach ($contract in $logicalGateContracts) {
    $resolved = Resolve-RequiredManifestGate $testRun.Gates $contract.LogicalName $contract.Pattern
    $resolvedManifestGates[$contract.LogicalName] = $resolved.Name
    $name = $resolved.Name
    $definition = $resolved.Definition
    $status = [string](Get-ObjectProperty $definition @('Status'))
    $count = @(Get-ObjectProperty $definition @('TestIds')).Count
    $gates.Add((New-Gate $name $status "UniqueTestIds=$count; TestRunManifestSHA256=$testRunManifestSha256"))
}
$trxParseFailures = 0

$bundleEvidence = @($testRun.EvidenceMap.Values | Where-Object {
    [string](Get-ObjectProperty $_.Definition @('Kind')) -eq 'RecoveryBundleZip'
})
$validatedRecoveryBundle = $null
$validatedRecoveryBundleZip = $null
if ($bundleEvidence.Count -ne 1) {
    $gates.Add((New-Gate 'UltimateRecoveryBundleIntegrity' 'EVIDENCE_BLOCKED_MISSING' "Exactly one RecoveryBundleZip is required; found $($bundleEvidence.Count)"))
    $gates.Add((New-Gate 'UltimateRecoveryBundleClientTrust' 'EVIDENCE_BLOCKED_MISSING' 'No uniquely validated recovery bundle'))
} else {
    $validatedRecoveryBundleZip = $bundleEvidence[0].Source.File.FullName
    $validatedRecoveryBundle = Assert-UltimateRecoveryBundleZip $validatedRecoveryBundleZip
    $gates.Add((New-Gate 'UltimateRecoveryBundleIntegrity' 'PASS' `
        "FullInventory=$($validatedRecoveryBundle.ArtifactCount); PackageId=$($validatedRecoveryBundle.PackageId); ZIP=$($validatedRecoveryBundle.SourceZipSHA256)"))
    $trustStatus = if ($validatedRecoveryBundle.ClientBuildTrusted) { 'PASS' } else { 'EVIDENCE_BLOCKED_MISSING' }
    $gates.Add((New-Gate 'UltimateRecoveryBundleClientTrust' $trustStatus $validatedRecoveryBundle.Status))
}

$importerGate = $null
$importerSummaryRecord = $null
$validatedImporterRunFull = $null
$importerCandidates = @()
$importerRejectReasons = @()
if (-not [string]::IsNullOrWhiteSpace($ServerDbImporterRun)) {
    $runFull = Get-FullPath $ServerDbImporterRun
    if (-not (Test-Path -LiteralPath $runFull -PathType Container)) { throw "Server/DB importer run is missing: $runFull" }
    Assert-NoReparsePoints $runFull
    $summaryPath = Join-Path $runFull 'importer-summary.json'
    if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
        $data = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
        $importerCandidates += [pscustomobject]@{ Directory = $runFull; Data = $data; File = Get-Item $summaryPath }
    }
}
foreach ($candidate in $importerCandidates) {
    $bundleValidationPath = Join-Path $candidate.Directory 'bundle-validation.json'
    $resultManifestPath = Join-Path $candidate.Directory 'result-manifest.json'
    if (-not (Test-Path -LiteralPath $bundleValidationPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $resultManifestPath -PathType Leaf)) { continue }
    try {
        $bundleValidation = Get-Content -Raw -LiteralPath $bundleValidationPath | ConvertFrom-Json
        $resultManifest = Get-Content -Raw -LiteralPath $resultManifestPath | ConvertFrom-Json
        $statusValue = [string](Get-ObjectProperty $candidate.Data @('status', 'Status'))
        $schemaValue = [string](Get-ObjectProperty $candidate.Data @('schemaVersion', 'SchemaVersion'))
        $connectionAttempted = Get-ObjectProperty $candidate.Data @('productionDatabaseConnectionAttempted')
        $mutationAttempted = Get-ObjectProperty $candidate.Data @('productionDatabaseMutationAttempted')
        $bundleConnectionAttempted = Get-ObjectProperty $bundleValidation @('productionDatabaseConnectionAttempted')
        $bundleMutationAttempted = Get-ObjectProperty $bundleValidation @('productionDatabaseMutationAttempted')
        $packageId = [string](Get-ObjectProperty $candidate.Data @('packageId'))
        $sourceZipSha256 = [string](Get-ObjectProperty $candidate.Data @('sourceZipSha256'))
        $artifactRootValue = [string](Get-ObjectProperty $candidate.Data @('artifactRoot'))
        $stagingRootValue = [string](Get-ObjectProperty $candidate.Data @('stagingRoot'))
        $stagedFileCount = Convert-ToInt64OrDefault (Get-ObjectProperty $candidate.Data @('stagedFileCount')) -1
        $gapCount = Convert-ToInt64OrDefault (Get-ObjectProperty $candidate.Data @('gapCount')) -1
        $readyDomainCount = Convert-ToInt64OrDefault (Get-ObjectProperty $candidate.Data @('readyDomainCount')) -1
        $blockedDomainCount = Convert-ToInt64OrDefault (Get-ObjectProperty $candidate.Data @('blockedDomainCount')) -1
        $summaryManifestValue = [string](Get-ObjectProperty $candidate.Data @('resultManifestPath'))
        $completedAtValue = [string](Get-ObjectProperty $candidate.Data @('completedAtUtc'))
        $completedAt = [DateTimeOffset]::MinValue
        $completedAtValid = [DateTimeOffset]::TryParse($completedAtValue, [ref]$completedAt)
        $bundleValidationSchema = [string](Get-ObjectProperty $bundleValidation @('schemaVersion', 'SchemaVersion'))
        $bundleValidationPackageId = [string](Get-ObjectProperty (Get-ObjectProperty $bundleValidation @('manifest', 'Manifest')) @('packageId'))
        $bundleValidationManifestSha256 = [string](Get-ObjectProperty $bundleValidation @('manifestSha256', 'ManifestSHA256'))
        $bundleValidationSourcePath = [string](Get-ObjectProperty $bundleValidation @('sourceZipPath', 'SourceZipPath'))
        $bundleValidationSourceSize = Convert-ToInt64OrDefault (Get-ObjectProperty $bundleValidation @('sourceZipSizeBytes', 'SourceZipSizeBytes')) -1
        Assert-NoReparsePoints $candidate.Directory
        if ($artifactRootValue -ne '.') { throw 'Importer artifactRoot must be the portable candidate-root token dot' }
        $resolvedArtifactRoot = Resolve-ImporterRunRelativePath $candidate.Directory $artifactRootValue -AllowRunRoot
        $resolvedStagingRoot = Resolve-ImporterRunRelativePath $candidate.Directory $stagingRootValue
        $resolvedSummaryManifest = Resolve-ImporterRunRelativePath $candidate.Directory $summaryManifestValue
        $artifactRootMatches = $resolvedArtifactRoot -eq (Get-FullPath $candidate.Directory)
        $manifestPathMatches = $resolvedSummaryManifest -eq (Get-FullPath $resultManifestPath)
        $stagingRootValid = Test-Path -LiteralPath $resolvedStagingRoot -PathType Container
        Assert-ImporterResultManifest $candidate.Directory $resultManifestPath
        if ($null -eq $validatedRecoveryBundle -or $null -eq $validatedRecoveryBundleZip) {
            throw 'Importer cannot be cross-linked without one independently validated native bundle ZIP'
        }
        $nativeZipInfo = Get-Item -LiteralPath $validatedRecoveryBundleZip
        if ($statusValue -eq 'RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS' -and
            $schemaValue -eq 'god2-recovery-bundle-import-result-v1' -and
            $bundleValidationSchema -eq 'god2-recovery-bundle-validation-v1' -and
            $connectionAttempted -is [bool] -and -not [bool]$connectionAttempted -and
            $mutationAttempted -is [bool] -and -not [bool]$mutationAttempted -and
            $bundleConnectionAttempted -is [bool] -and -not [bool]$bundleConnectionAttempted -and
            $bundleMutationAttempted -is [bool] -and -not [bool]$bundleMutationAttempted -and
            -not [string]::IsNullOrWhiteSpace($packageId) -and
            $sourceZipSha256 -match '^[A-Fa-f0-9]{64}$' -and
            [string](Get-ObjectProperty $bundleValidation @('SourceZipSha256')) -ieq $sourceZipSha256 -and
            $packageId -eq $validatedRecoveryBundle.PackageId -and
            $bundleValidationPackageId -eq $packageId -and
            $sourceZipSha256 -ieq $validatedRecoveryBundle.SourceZipSHA256 -and
            $bundleValidationManifestSha256 -ieq $validatedRecoveryBundle.ManifestSHA256 -and
            $bundleValidationSourceSize -eq $nativeZipInfo.Length -and
            [System.IO.Path]::GetFileName($bundleValidationSourcePath) -eq $bundleValidationSourcePath -and
            $bundleValidationSourcePath -eq $nativeZipInfo.Name -and
            $artifactRootMatches -and $stagingRootValid -and
            $stagedFileCount -gt 0 -and $gapCount -ge 0 -and $readyDomainCount -ge 0 -and
            $blockedDomainCount -ge 0 -and ($readyDomainCount + $blockedDomainCount) -eq $gapCount -and
            $manifestPathMatches -and $completedAtValid -and
            [string](Get-ObjectProperty $bundleValidation @('status', 'Status')) -eq 'PASS' -and
            [string](Get-ObjectProperty $resultManifest @('status', 'Status')) -eq 'PASS') {
            $importerSummaryRecord = $candidate
            $validatedImporterRunFull = Get-FullPath $candidate.Directory
            break
        }
        throw 'Importer summary did not satisfy the complete dry-run contract'
    } catch { $importerRejectReasons += $_.Exception.Message }
}
if ($null -eq $importerSummaryRecord) {
    $detail = if ($importerRejectReasons.Count -gt 0) { $importerRejectReasons -join ' | ' } else { 'No explicit -ServerDbImporterRun supplied' }
    $importerGate = New-Gate 'ServerDbImporterDryRun' 'EVIDENCE_BLOCKED_MISSING' `
        ('No importer run satisfied manifest coverage + portable paths + native ZIP SHA/packageId cross-binding: ' + $detail)
} else {
    $staged = Convert-ToInt64OrDefault (Get-ObjectProperty $importerSummaryRecord.Data @('stagedFileCount'))
    $importerGate = New-Gate 'ServerDbImporterDryRun' 'PASS' "RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS; StagedFileCount=$staged"
}
$gates.Add($importerGate)

if ($jsonParseFailures.Count -gt 0 -or $trxParseFailures -gt 0) {
    $gates.Add((New-Gate 'InputReportParsing' 'FAIL' "JSON parse failures=$($jsonParseFailures.Count); TRX parse failures=$trxParseFailures"))
} else {
    $gates.Add((New-Gate 'InputReportParsing' 'PASS' "JSON reports=$($jsonRecords.Count); TRX parse failures=0"))
}

# The native CLI entrypoint contract is validated by the generated test-run
# manifest, but availability alone cannot promote a live evidence result.  A
# future supplied live result must still pass independent process/path/creation-
# time, hook-map, semantic-ring, bundle ZIP and importer linkage verification.
$liveStatus = 'EVIDENCE_BLOCKED_EXTERNAL_LIVE_GATE_PENDING'
$globalLiveGateEligible = $false

# A raw result summary never promotes this gate. Promotion requires an explicit,
# separately supplied, SHA-bound audit whose scope accepts the v1.3 Ultimate workload.
# A valid legacy v1.2 physical result is retained as provenance without becoming PASS.
$rtxStatus = if (-not $hasRtxResult) {
    'PENDING_EXTERNAL_RUN'
} elseif ($validatedRtxCanPromoteUltimate) {
    'RTX5070_PHYSICAL_VALIDATION_PASS'
} else {
    'PENDING_FRESH_ULTIMATE_RTX5070_RUN'
}

$aiModelStatus = 'EvidenceBlockedModelUnavailable'
$liveRunnerDefinition = $testRun.Gates[$resolvedManifestGates['UltimateLiveEntrypointContract']]
$liveRunnerStatus = if ($null -ne $liveRunnerDefinition -and
    [string](Get-ObjectProperty $liveRunnerDefinition @('Status')) -eq 'PASS') { 'PASS' } else { 'EvidenceBlockedNativeLiveEntrypointContractMissing' }

$requiredGateNames = @($resolvedManifestGates.Values) + @(
    'UltimateRecoveryBundleIntegrity', 'ServerDbImporterDryRun', 'InputReportParsing'
)
$requiredGates = @($gates | Where-Object { $_.Name -in $requiredGateNames })
$failedGates = @($requiredGates | Where-Object { $_.Status -eq 'FAIL' })
$missingGates = @($requiredGates | Where-Object { $_.Status -eq 'EVIDENCE_BLOCKED_MISSING' })
$automatedStatus = if ($failedGates.Count -gt 0) { 'FAIL' } elseif ($missingGates.Count -gt 0) { 'EVIDENCE_BLOCKED_INCOMPLETE' } else { 'PASS' }
$localImplementationGateNames = @($resolvedManifestGates.Values) + @(
    'UltimateRecoveryBundleIntegrity', 'InputReportParsing'
)
$localImplementationGates = @($gates | Where-Object { $_.Name -in $localImplementationGateNames })
$localImplementationFailures = @($localImplementationGates | Where-Object { $_.Status -ne 'PASS' })
if ($localImplementationGates.Count -ne $localImplementationGateNames.Count -or
    $localImplementationFailures.Count -ne 0) {
    $details = @($localImplementationFailures | ForEach-Object { $_.Name + '=' + $_.Status }) -join '; '
    throw "Implementation-complete publication requires every local code/test/analysis/bundle gate to PASS: $details"
}
# All sixteen deterministic non-AI GPU workloads are implemented and verified
# against the CPU oracle. A missing optional AI provider/model is a truthful
# EvidenceBlocked evidence state, not an implementation blocker. Official-client
# live identity, fresh RTX 5070 execution and real importer integration remain
# explicit external gates and are never inferred from local implementation tests.
$mandatoryGpuWorkloadStatus = $localGpuImplementation.Status
$mandatoryGpuWorkloadsComplete = $true
$finalEngineeringStatus = 'ULTIMATE_DEEP_RECOVERY_IMPLEMENTATION_COMPLETE_WITH_EXTERNAL_LIVE_GATE_PENDING'

$allInputFiles = @($reportFiles | ForEach-Object { $_.File })
$allInputFiles += @($testRun.SourceInventory | ForEach-Object { $_.File })
$allInputFiles += @(Get-ChildItem -LiteralPath $builtInSchemaRoot -File -Filter '*.json')
$allInputFiles += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\validation') -File)
foreach ($schemaInput in $resolvedSchemaRoots) {
    $allInputFiles += @(Get-ChildItem -LiteralPath $schemaInput -Recurse -File)
}
$allInputFiles += $executableInfo
if ($hasRtxResult) {
    $allInputFiles += Get-Item -LiteralPath $validatedRtxResultFull
    $allInputFiles += Get-Item -LiteralPath $validatedRtxAttestationFull
}
if ($null -ne $validatedImporterRunFull) {
    $allInputFiles += @(Get-ChildItem -LiteralPath $validatedImporterRunFull -Recurse -File)
}
$script:ValidatedInputSnapshots = New-InputFileSnapshotMap $allInputFiles
[void](Assert-ReportRootInventoryUnchanged $resolvedReportRoots $reportFiles)
Assert-InputSnapshotExpectedBinding $executableFull $executableSha256 ([int64]$executableInfo.Length) `
    $script:ValidatedInputSnapshots 'packaged executable validation'
Assert-InputSnapshotExpectedBinding $testRun.ManifestFile.FullName $testRun.ManifestSHA256 `
    ([int64]$testRun.ManifestSizeBytes) $script:ValidatedInputSnapshots 'test-run manifest raw bytes'
foreach ($item in $testRun.EvidenceMap.Values) {
    $definition = $item.Definition
    Assert-InputSnapshotExpectedBinding $item.Source.File.FullName `
        ([string](Get-ObjectProperty $definition @('SHA256'))) `
        (Convert-ToInt64OrDefault (Get-ObjectProperty $definition @('SizeBytes')) -1) `
        $script:ValidatedInputSnapshots ('test-run evidence ' + [string](Get-ObjectProperty $definition @('EvidenceId')))
}
foreach ($sourceInput in $testRun.SourceInventory) {
    Assert-InputSnapshotExpectedBinding $sourceInput.Path $sourceInput.SHA256 ([int64]$sourceInput.SizeBytes) `
        $script:ValidatedInputSnapshots ('test-run source ' + $sourceInput.RelativePath)
}
if ($hasRtxResult) {
    Assert-InputSnapshotExpectedBinding $validatedRtxResultFull $validatedRtxPair.ResultSHA256 `
        ([int64]$validatedRtxPair.ResultSizeBytes) $script:ValidatedInputSnapshots 'RTX result ZIP'
    Assert-InputSnapshotExpectedBinding $validatedRtxAttestationFull $validatedRtxPair.AttestationSHA256 `
        ([int64]$validatedRtxPair.AttestationSizeBytes) $script:ValidatedInputSnapshots 'RTX external attestation'
}

# Re-run semantic parsers only after every input has an exact snapshot. The
# copy path below accepts only these bytes, so a validate/replace/copy race
# cannot alter the published evidence.
[void](Assert-AllInputSnapshotsUnchanged $script:ValidatedInputSnapshots)
$testRunRecheck = Assert-TestRunManifest $testRun.ManifestFile $reportFiles $resolvedReportRoots `
    $executableSha256 $repoRoot
if ($testRunRecheck.ManifestSHA256 -ne $testRun.ManifestSHA256 -or
    $testRunRecheck.SourceTreeSHA256 -ne $testRun.SourceTreeSHA256 -or
    $testRunRecheck.EvidenceTreeSHA256 -ne $testRun.EvidenceTreeSHA256 -or
    $testRunRecheck.TrxUniqueTestCount -ne $testRun.TrxUniqueTestCount -or
    $testRunRecheck.NativeAnalyzerCount -ne $testRun.NativeAnalyzerCount) {
    throw 'Test-run manifest/evidence changed between initial validation and immutable snapshot revalidation'
}
if ($null -ne $validatedRecoveryBundle -and $null -ne $validatedRecoveryBundleZip) {
    $bundleRecheck = Assert-UltimateRecoveryBundleZip $validatedRecoveryBundleZip
    if ($bundleRecheck.SourceZipSHA256 -ne $validatedRecoveryBundle.SourceZipSHA256 -or
        $bundleRecheck.ManifestSHA256 -ne $validatedRecoveryBundle.ManifestSHA256 -or
        $bundleRecheck.PackageId -ne $validatedRecoveryBundle.PackageId -or
        $bundleRecheck.ArtifactCount -ne $validatedRecoveryBundle.ArtifactCount) {
        throw 'Native recovery bundle changed between validation and immutable snapshot revalidation'
    }
}
if ($hasRtxResult) {
    $rtxPairRecheck = Assert-Rtx5070AttestedPair $validatedRtxResultFull `
        $validatedRtxAttestationFull $executableSha256
    if ($rtxPairRecheck.ResultSHA256 -ne $validatedRtxPair.ResultSHA256 -or
        $rtxPairRecheck.AttestationSHA256 -ne $validatedRtxPair.AttestationSHA256 -or
        $rtxPairRecheck.Inner.Status -ne $validatedRtxPair.Inner.Status -or
        [string](Get-ObjectProperty $rtxPairRecheck.Inner @('WorkloadMatrixSHA256')) -ne
            [string](Get-ObjectProperty $validatedRtxPair.Inner @('WorkloadMatrixSHA256')) -or
        $rtxPairRecheck.UltimateScope -ne $validatedRtxPair.UltimateScope -or
        $rtxPairRecheck.CanPromoteUltimate -ne $validatedRtxPair.CanPromoteUltimate) {
        throw 'RTX result/attestation pair changed between validation and immutable snapshot revalidation'
    }
}
[void](Assert-SchemaRegistryDirectory $builtInSchemaRoot)
Assert-ExternalSchemaRootsMatchBuiltIns $resolvedSchemaRoots $builtInSchemaRoot
[void](Assert-AllInputSnapshotsUnchanged $script:ValidatedInputSnapshots)
$latestInputTime = ($allInputFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
$generatedAtUtc = $latestInputTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

$outputParent = Split-Path -Parent $outputFull
if (-not (Test-Path -LiteralPath $outputParent)) { [void](New-Item -ItemType Directory -Path $outputParent) }
$stageRoot = Get-FullPath (Join-Path $outputParent ('.' + [System.IO.Path]::GetFileName($outputFull) + '.staging-' + [guid]::NewGuid().ToString('N')))
if (-not (Test-IsWithin $outputParent $stageRoot) -or -not [System.IO.Path]::GetFileName($stageRoot).StartsWith('.' + [System.IO.Path]::GetFileName($outputFull) + '.staging-')) {
    throw "Unsafe Ultimate staging root: $stageRoot"
}

$preexistingBackup = $null
$preexistingHashes = @{}
if (Test-Path -LiteralPath $outputFull) {
    Assert-NoReparsePoints $outputFull
    if (Test-Path -LiteralPath (Join-Path $outputFull $script:ArtifactManifestJson)) {
        throw "Ultimate output already has a manifest. Use -ValidateOnly; build will never overwrite it: $outputFull"
    }
    $allowedPrebuildInputs = @('ServerDbImporter', 'intermediate')
    $unexpected = @(Get-ChildItem -LiteralPath $outputFull -Force | Where-Object { $_.Name -notin $allowedPrebuildInputs })
    if ($unexpected.Count -gt 0) { throw "Existing Ultimate output contains unknown artifacts; refusing to overwrite: $outputFull" }
    foreach ($file in Get-ChildItem -LiteralPath $outputFull -Recurse -File) {
        $preexistingHashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}

$provenance = @{}
try {
    [void](New-Item -ItemType Directory -Path $stageRoot)

    if ($null -ne $validatedImporterRunFull) {
        $runLeaf = [System.IO.Path]::GetFileName($validatedImporterRunFull.TrimEnd('\'))
        Assert-SafeRelativePath $runLeaf
        foreach ($file in Get-ChildItem -LiteralPath $validatedImporterRunFull -Recurse -File | Sort-Object FullName) {
            $relativeChild = Convert-ToSlashPath $file.FullName.Substring($validatedImporterRunFull.TrimEnd('\').Length + 1)
            Copy-NewFile $file.FullName $stageRoot ('ServerDbImporter/' + $runLeaf + '/' + $relativeChild) $provenance `
                ('OnlyValidatedServerDbImporterRun:' + $runLeaf)
        }
    }

    Copy-NewFile $executableFull $stageRoot 'release/God2SemanticRecoveryEngine.exe' $provenance 'BuiltExecutable:v1.3.0.0'
    $productIdentity = [ordered]@{
        SchemaVersion = 1
        Product = $script:ProductName
        DisplayVersion = $script:DisplayVersion
        FileVersion = $executableVersion.FileVersion
        ProductVersion = $executableVersion.ProductVersion
        Channel = $script:Channel
        Architecture = 'x64'
        Machine = ('0x{0:X4}' -f $machine)
        OptionalHeaderMagic = ('0x{0:X4}' -f $optionalMagic)
        Subsystem = $subsystem
        SizeBytes = [int64]$executableInfo.Length
        SHA256 = $executableSha256
        TestRunManifestSHA256 = $testRunManifestSha256
        SourceTreeSHA256 = $testRun.SourceTreeSHA256
        EvidenceTreeSHA256 = $testRun.EvidenceTreeSHA256
        GeneratedAtUtc = $generatedAtUtc
    }
    Write-GeneratedFile $stageRoot 'release/product-identity.json' (($productIdentity | ConvertTo-Json -Depth 6) + "`n") $provenance

    foreach ($file in Get-ChildItem -LiteralPath $builtInSchemaRoot -File -Filter '*.json' | Sort-Object Name) {
        Copy-NewFile $file.FullName $stageRoot ('schemas/' + $file.Name) $provenance ('BuiltInSchema:' + $file.Name)
    }

    foreach ($source in $reportFiles) {
        $destination = "reports/evidence/input-{0:D2}/{1}" -f $source.RootIndex, $source.RelativePath
        Copy-NewFile $source.File.FullName $stageRoot $destination $provenance ("TestReportInput-{0:D2}:{1}" -f $source.RootIndex, $source.RelativePath)
    }
    if ($hasRtxResult) {
        $resultName = [System.IO.Path]::GetFileName($validatedRtxResultFull)
        Copy-NewFile $validatedRtxResultFull $stageRoot ('validation/external-evidence/' + $resultName) $provenance `
            ('ValidatedExternalRtx5070Result:SHA256=' + $validatedRtxPair.ResultSHA256)
        Copy-NewFile $validatedRtxAttestationFull $stageRoot 'validation/external-evidence/rtx5070-external-attestation.json' $provenance `
            'RootValidatedExternalRtx5070Attestation'
    }

    # Re-parse the exact staged bytes. This is intentionally independent of the
    # initial source-path validation so staged artifact hashes cannot bless a
    # file substituted between validation and Copy-Item.
    $stagedReportRoots = @()
    $stagedReportFiles = @()
    for ($rootIndex = 0; $rootIndex -lt $resolvedReportRoots.Count; $rootIndex++) {
        $stagedReportRoot = Join-Path $stageRoot ("reports\evidence\input-{0:D2}" -f ($rootIndex + 1))
        if (-not (Test-Path -LiteralPath $stagedReportRoot)) { [void](New-Item -ItemType Directory -Path $stagedReportRoot) }
        $stagedReportRoots += $stagedReportRoot
        foreach ($file in Get-ChildItem -LiteralPath $stagedReportRoot -Recurse -File | Sort-Object FullName) {
            $relative = Convert-ToSlashPath $file.FullName.Substring($stagedReportRoot.TrimEnd('\').Length + 1)
            $stagedReportFiles += [pscustomobject]@{
                RootIndex = $rootIndex + 1
                Root = $stagedReportRoot
                File = $file
                RelativePath = $relative
            }
        }
    }
    $stagedManifestCandidates = @($stagedReportFiles | Where-Object { $_.File.Name -ceq $script:TestRunManifestName })
    if ($stagedManifestCandidates.Count -ne 1) { throw 'Staged evidence does not contain exactly one test-run manifest' }
    $stagedTestRun = Assert-TestRunManifest $stagedManifestCandidates[0].File $stagedReportFiles `
        $stagedReportRoots $executableSha256 $repoRoot
    if ($stagedTestRun.ManifestSHA256 -ne $testRun.ManifestSHA256 -or
        $stagedTestRun.SourceTreeSHA256 -ne $testRun.SourceTreeSHA256 -or
        $stagedTestRun.EvidenceTreeSHA256 -ne $testRun.EvidenceTreeSHA256 -or
        $stagedTestRun.TrxUniqueTestCount -ne $testRun.TrxUniqueTestCount -or
        $stagedTestRun.NativeAnalyzerCount -ne $testRun.NativeAnalyzerCount) {
        throw 'Staged test-run evidence does not match the validated source snapshot'
    }
    $stagedBundleEvidence = @($stagedTestRun.EvidenceMap.Values | Where-Object {
        [string](Get-ObjectProperty $_.Definition @('Kind')) -eq 'RecoveryBundleZip'
    })
    if ($null -ne $validatedRecoveryBundle) {
        if ($stagedBundleEvidence.Count -ne 1) { throw 'Staged evidence lost the unique recovery bundle ZIP' }
        $stagedBundle = Assert-UltimateRecoveryBundleZip $stagedBundleEvidence[0].Source.File.FullName
        if ($stagedBundle.SourceZipSHA256 -ne $validatedRecoveryBundle.SourceZipSHA256 -or
            $stagedBundle.ManifestSHA256 -ne $validatedRecoveryBundle.ManifestSHA256 -or
            $stagedBundle.PackageId -ne $validatedRecoveryBundle.PackageId -or
            $stagedBundle.ArtifactCount -ne $validatedRecoveryBundle.ArtifactCount) {
            throw 'Staged native recovery bundle differs from the independently validated input'
        }
    }
    if ($null -ne $validatedImporterRunFull) {
        $stagedImporterRoot = Join-Path $stageRoot ('ServerDbImporter\' + $runLeaf)
        [void](Assert-ImporterResultManifest $stagedImporterRoot (Join-Path $stagedImporterRoot 'result-manifest.json'))
    }
    if ($hasRtxResult) {
        $stagedRtxResult = Join-Path $stageRoot ('validation\external-evidence\' + $resultName)
        $stagedRtxAttestation = Join-Path $stageRoot 'validation\external-evidence\rtx5070-external-attestation.json'
        $stagedRtxPair = Assert-Rtx5070AttestedPair $stagedRtxResult $stagedRtxAttestation $executableSha256
        if ($stagedRtxPair.ResultSHA256 -ne $validatedRtxPair.ResultSHA256 -or
            $stagedRtxPair.AttestationSHA256 -ne $validatedRtxPair.AttestationSHA256 -or
            $stagedRtxPair.Inner.Status -ne $validatedRtxPair.Inner.Status -or
            [string](Get-ObjectProperty $stagedRtxPair.Inner @('WorkloadMatrixSHA256')) -ne
                [string](Get-ObjectProperty $validatedRtxPair.Inner @('WorkloadMatrixSHA256')) -or
            $stagedRtxPair.UltimateScope -ne $validatedRtxPair.UltimateScope -or
            $stagedRtxPair.CanPromoteUltimate -ne $validatedRtxPair.CanPromoteUltimate) {
            throw 'Staged RTX result/attestation pair differs from the independently validated input'
        }
    }
    [void](Assert-AllInputSnapshotsUnchanged $script:ValidatedInputSnapshots)

    $gateArray = @($gates | ForEach-Object { $_ })
    $evidenceSummary = [ordered]@{
        SchemaVersion = 1
        GeneratedAtUtc = $generatedAtUtc
        Product = $script:ProductName
        ProductVersion = $script:ProductVersion
        PackagedExecutableSHA256 = $executableSha256
        TestRunManifestSHA256 = $testRunManifestSha256
        SourceTreeSHA256 = $testRun.SourceTreeSHA256
        EvidenceTreeSHA256 = $testRun.EvidenceTreeSHA256
        AutomatedGateStatus = $automatedStatus
        ExternalLiveGateStatus = $liveStatus
        LiveRunnerContractStatus = $liveRunnerStatus
        Rtx5070Status = $rtxStatus
        Rtx5070ExternalAttestationRequired = $true
        Rtx5070ValidatedResultSHA256 = if ($hasRtxResult) { $validatedRtxPair.ResultSHA256 } else { $null }
        Rtx5070ExternalAuditSHA256 = if ($hasRtxResult) { $validatedRtxPair.AttestationSHA256 } else { $null }
        Rtx5070ExternalAuditScope = $validatedRtxUltimateScope
        Rtx5070ExternalAuditPromotedUltimateGate = $validatedRtxCanPromoteUltimate
        AiModelStatus = $aiModelStatus
        FinalEngineeringStatus = $finalEngineeringStatus
        InputReportCount = $reportFiles.Count
        JsonReportCount = $jsonRecords.Count
        JsonParseFailureCount = $jsonParseFailures.Count
        Gates = $gateArray
        DoesNotAssert = @('ULTIMATE PASS', 'SERVER RESTORED', 'DATABASE RESTORED', 'ULTIMATE_SERVER_DB_CLOSED_LOOP_PASS')
    }
    Write-GeneratedFile $stageRoot 'reports/evidence-summary.json' (($evidenceSummary | ConvertTo-Json -Depth 10) + "`n") $provenance

    $gateLines = @('## Automated gate matrix', '')
    foreach ($gate in $gates) { $gateLines += ('- ' + $gate.Name + ': ' + $gate.Status + ' - ' + $gate.Evidence) }
    $implementationLines = @(
        '## Outcome', '',
        ('- Automated gate status: ' + $automatedStatus),
        ('- External live gate: ' + $liveStatus),
        ('- RTX 5070 gate: ' + $rtxStatus),
        ('- AI model: ' + $aiModelStatus),
        ('- Mandatory GPU workload status: ' + $mandatoryGpuWorkloadStatus),
        ('- Mandatory GPU workloads complete: ' + $mandatoryGpuWorkloadsComplete),
        ('- DLL enhanced capture acceptance: ' + $dllEnhancedCaptureAcceptance.Status + ' / 25 domains / 21 candidate-only blocked'),
        ('- Live runner contract: ' + $liveRunnerStatus),
        ('- Final engineering status: ' + $finalEngineeringStatus),
        '',
        '## Release identity', '',
        '- EXE: release/God2SemanticRecoveryEngine.exe',
        ('- SHA-256: ' + $productIdentity.SHA256),
        '- PE: x64 Windows GUI / FileVersion 1.3.0.0 / ProductVersion 1.3.0.0',
        ''
    ) + $gateLines
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-IMPLEMENTATION-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Implementation Report' $generatedAtUtc $finalEngineeringStatus $implementationLines) $provenance

    $architectureLines = @(
        '## Architecture', '',
        '- Official God2 Client: authoritative network/runtime/resource evidence source.',
        '- Embedded x86 probe: bounded deep sensor; fail closed on unconfirmed RVA/calling convention.',
        '- External x64 engine: deterministic recovery, graph, schema/formula/FSM synthesis, bundle export.',
        '- RTX 5070: optional parallel accelerator; CPU/replay/oracle remain final authority.',
        '- AI/ML: hypothesis-only; unavailable model never blocks deterministic capture/recovery.',
        '- UI: the permanent three-language DLL enhanced-capture status row is auto-enabled state, not a third user action.',
        '- Server/DB importer: staging and planning only; production mutation is prohibited.',
        '',
        'Capture, optional GPU, optional AI, graph and importer failures remain isolated. Partial recoverable evidence must be packaged rather than silently discarded.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-ARCHITECTURE-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Architecture Report' $generatedAtUtc $finalEngineeringStatus $architectureLines) $provenance

    $probeStatus = if ($liveStatus -eq 'ULTIMATE_LIVE_DEEP_RECOVERY_EVIDENCE_PASS') { 'LIVE_COMPONENT_EVIDENCE_PRESENT' } else { 'EvidenceBlockedUnconfirmedProbe' }
    $probeLines = @(
        '## Exact target', '',
        '- God2_opt.exe / x86 / version 1.0.0.1',
        ('- SHA-256: ' + $script:TargetClientSha256),
        '',
        '## Existing verified static probe identities', '',
        '- PacketDecode.FrameBoundary: callsite 0x00078A48 -> 0x00078D70.',
        '- OutboundEnqueue.FrameBuilder: RVA 0x0007FC10, signature 55 8B EC 56 57.',
        '- Battle.HandlerRecordLength: calls 0x00147096 / 0x001470AE -> 0x0007F940.',
        '',
        '- Deep-probe candidate discovery v2 is implemented as a bounded executable-code scan rooted only in verified parser/serializer/handler seeds.',
        '- Discovery emits exact candidate RVAs, signatures and provenance; it never activates a candidate without exact-build live verification.',
        '',
        ('Ultimate deep Object/Registry/Resource/Mutation/Formula probes remain ' + $probeStatus + ' unless exact-build live evidence confirms their complete hook contracts.'),
        'Unknown RVA, calling convention, argument lifetime, reentrancy or thread context must never be promoted into a production hook.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-PROBE-MAP-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Probe Map Report' $generatedAtUtc $probeStatus $probeLines) $provenance

    $gpuLines = @(
        '## GPU evidence', '',
        ('- RTX 5070 physical status: ' + $rtxStatus),
        ('- External audit scope: ' + $(if ($hasRtxResult) { $validatedRtxUltimateScope } else { 'No validated external audit supplied' })),
        ('- GPU contract gate: ' + (($gates | Where-Object Name -eq $resolvedManifestGates['GpuContract']).Status)),
        ('- Local implementation: ' + $localGpuImplementation.Status),
        '- All 16 deterministic non-AI Ultimate operations have operation-specific GPU implementations and CPU-oracle equivalence evidence.',
        '- AiInference remains EvidenceBlockedModelUnavailable until a verified provider/model is supplied; this does not block implementation completeness.',
        '- CPU Authority: mandatory and unchanged.',
        '- GPU backend error, OOM, mismatch or device reset requires CPU fallback and explicit diagnostics.',
        '',
        'A legacy physical result is preserved for provenance only; it cannot satisfy the v1.3 Ultimate RTX gate.',
        'No acceleration multiplier is claimed without a matching v1.3 Ultimate RTX 5070 result package.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-GPU-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate GPU Report' $generatedAtUtc $rtxStatus $gpuLines) $provenance

    $aiLines = @(
        '## AI/ML evidence', '',
        ('- AiModelStatus: ' + $aiModelStatus),
        '- Model output authority is always HYPOTHESIS.',
        '- A model is considered available only with model name, version, license and a 64-character SHA-256 in machine-readable evidence.',
        '- No runtime Evidence upload or fabricated AI Active state is permitted.',
        '- Deterministic CPU/GPU recovery continues when a model is unavailable.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-AI-ML-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate AI/ML Report' $generatedAtUtc $aiModelStatus $aiLines) $provenance

    $bundleGate = $gates | Where-Object Name -eq 'UltimateRecoveryBundleIntegrity'
    $bundleLines = @(
        '## Recovery Bundle', '',
        ('- Integrity gate: ' + $bundleGate.Status),
        ('- Evidence: ' + $bundleGate.Evidence),
        '- Required manifest schema: god2-ultimate-package-manifest-v1.',
        '- Every artifact is verified by safe relative path, exact size and SHA-256 before this gate can pass.',
        '- Candidate, Unknown and UNKNOWN_SERVER_ONLY material must not be promoted into verified server/database content.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-RECOVERY-BUNDLE-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Recovery Bundle Report' $generatedAtUtc $bundleGate.Status $bundleLines) $provenance

    $offlineImporterGateDefinition = $testRun.Gates[$resolvedManifestGates['ImporterOfflineVerifierContract']]
    $formalImporterGateDefinition = $testRun.Gates[$resolvedManifestGates['ImporterFormalIdentityFailClosed']]
    $serverLines = @(
        '## Server/DB importer', '',
        ('- Offline verifier contract: ' + [string](Get-ObjectProperty $offlineImporterGateDefinition @('Status')) + ' / 70 tests'),
        ('- Formal identity fail-closed contract: ' + [string](Get-ObjectProperty $formalImporterGateDefinition @('Status')) + ' / expected exit 4 / no output directory'),
        ('- Importer dry-run gate: ' + $importerGate.Status),
        ('- Evidence: ' + $importerGate.Evidence),
        '- Required status: RECOVERY_BUNDLE_IMPORTER_DRY_RUN_PASS.',
        '- bundle-validation.json and result-manifest.json must both be PASS.',
        '- Offline verifier executes 13 compiled server contracts, 59 migration contracts (additive/apply/idempotent) and deterministic semantic replay (1 executed / 0 rejected) without production access.',
        '- productionDatabaseConnectionAttempted and productionDatabaseMutationAttempted must both be false.',
        '- A local offline-verifier PASS proves implementation readiness only; exact official-client identity and real importer/live integration remain external gates.',
        '- This packaging report never asserts ULTIMATE_SERVER_DB_CLOSED_LOOP_PASS from a dry-run.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-SERVER-DB-INTEGRATION-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Server/DB Integration Report' $generatedAtUtc $importerGate.Status $serverLines) $provenance

    $validationLines = @(
        '## Validation status', '',
        ('- Automated: ' + $automatedStatus),
        ('- Live God2 session: ' + $liveStatus),
        ('- Live runner contract: ' + $liveRunnerStatus),
        ('- RTX 5070: ' + $rtxStatus),
        ('- AI model: ' + $aiModelStatus),
        ('- DLL enhanced capture: ' + $dllEnhancedCaptureAcceptance.Status),
        '- Package manifests and output manifests are verified independently.',
        '',
        'Portable packages are structural deliverables. A structurally valid ZIP is not itself live recovery or RTX 5070 PASS evidence.',
        ''
    ) + $gateLines
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-VALIDATION-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Validation Report' $generatedAtUtc $finalEngineeringStatus $validationLines) $provenance

    $deepClosureLines = @(
        '## Closure boundary', '',
        ('- DLL enhanced capture acceptance: ' + $dllEnhancedCaptureAcceptance.Status),
        ('- Confirmed exact-build contracts: ' + $dllEnhancedCaptureAcceptance.ConfirmedContractCount),
        ('- Candidate-only blocked domains: ' + $dllEnhancedCaptureAcceptance.CandidateOnlyDomainCount),
        ('- Semantic ring continuity: ' + $semanticRingContinuity.Status),
        ('- Mandatory GPU workload status: ' + $mandatoryGpuWorkloadStatus),
        ('- External live gate: ' + $liveStatus),
        ('- RTX 5070 gate: ' + $rtxStatus),
        ('- Final engineering status: ' + $finalEngineeringStatus),
        '',
        'No blocked deep domain, external live gate, RTX 5070 run, AI model, server-ready state or database-ready state is promoted by this report.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-DEEP-RECOVERY-CLOSURE-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Deep Recovery Closure Report' $generatedAtUtc $finalEngineeringStatus $deepClosureLines) $provenance

    $authorityLines = @(
        '## Authority result', '',
        ('- Consistency status: ' + $dllEnhancedCaptureAcceptance.AuthorityStatus),
        ('- Conflict count: ' + $dllEnhancedCaptureAcceptance.AuthorityConflictCount),
        ('- Current official authority records: ' + $dllEnhancedCaptureAcceptance.CurrentOfficialAuthorityCount),
        ('- Isolated exact-fixture authority records: ' + $dllEnhancedCaptureAcceptance.ExactFixtureAuthorityCount),
        '- Official target: God2_opt.exe / x86 / version 1.0.0.1.',
        ('- Official target SHA-256: ' + $script:TargetClientSha256),
        '',
        'Fixture evidence remains non-current and non-promotable. Current official runtime evidence and static hypotheses retain separate authority profiles.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-AUTHORITY-CONSISTENCY-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Authority Consistency Report' $generatedAtUtc $dllEnhancedCaptureAcceptance.AuthorityStatus $authorityLines) $provenance

    $promotionLines = @(
        '## Promotion result', '',
        ('- Candidate count: ' + $dllEnhancedCaptureAcceptance.CandidateCount),
        ('- Promotion ledger rows: ' + $dllEnhancedCaptureAcceptance.PromotionLedgerRowCount),
        ('- Activation allowed count: ' + $dllEnhancedCaptureAcceptance.ActivationAllowedCount),
        ('- Candidate-only blocked domains: ' + $dllEnhancedCaptureAcceptance.CandidateOnlyDomainCount),
        '- Promotion status: EVIDENCE_BLOCKED_RUNTIME_AND_ABI_GATES.',
        '',
        'Unknown calling convention, argument lifetime, thread context, reentrancy, typed runtime observation or contradiction keeps a candidate inactive.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-DEEP-PROBE-PROMOTION-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Deep Probe Promotion Report' $generatedAtUtc 'EVIDENCE_BLOCKED_RUNTIME_AND_ABI_GATES' $promotionLines) $provenance

    $ringLines = @(
        '## Segmented ring result', '',
        ('- Status: ' + $semanticRingContinuity.Status),
        ('- Accepted events: ' + $semanticRingContinuity.EventCount),
        ('- P0 accepted: ' + $semanticRingContinuity.P0Accepted),
        ('- P1 accepted: ' + $semanticRingContinuity.P1Accepted),
        '- P0/P1 dropped: 0 / 0.',
        '- Write failures: 0; pending after drain: 0; sequence ordered: true.',
        ('- Segment count: ' + $semanticRingContinuity.SegmentCount),
        ('- Merged SHA-256: ' + $semanticRingContinuity.MergedSHA256),
        '',
        'The report validates exact GSR4 transport version 4 continuity and byte-for-byte segment concatenation.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-RING-RELIABILITY-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Ring Reliability Report' $generatedAtUtc $semanticRingContinuity.Status $ringLines) $provenance

    $gpuMlLines = @(
        '## GPU and ML result', '',
        ('- Deterministic GPU implementation: ' + $localGpuImplementation.Status),
        ('- Implemented non-AI operations: ' + $localGpuImplementation.ImplementedOperationCount),
        ('- Implementation-blocked operations: ' + $localGpuImplementation.ImplementationBlockedCount),
        ('- AI model evidence-blocked operations: ' + $localGpuImplementation.ModelEvidenceBlockedCount),
        ('- RTX 5070 physical status: ' + $rtxStatus),
        ('- AI model status: ' + $aiModelStatus),
        '- CPU oracle authority remains preserved.',
        '',
        'No RTX 5070 performance multiplier or AI authority is claimed without the separately attested external result and verified model metadata.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-GPU-ML-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate GPU ML Report' $generatedAtUtc $rtxStatus $gpuMlLines) $provenance

    $readinessLines = @(
        '## Recovery readiness', '',
        ('- Local automated gates: ' + $automatedStatus),
        ('- Recovery bundle integrity: ' + $bundleGate.Status),
        ('- Importer offline verifier: ' + [string](Get-ObjectProperty $offlineImporterGateDefinition @('Status'))),
        ('- Formal importer identity fail-closed: ' + [string](Get-ObjectProperty $formalImporterGateDefinition @('Status'))),
        ('- Server ready: ' + $dllEnhancedCaptureAcceptance.ServerReady),
        ('- Database ready: ' + $dllEnhancedCaptureAcceptance.DatabaseReady),
        ('- External live gate: ' + $liveStatus),
        '',
        'The implementation is ready for external deep-live, RTX 5070 and trusted-identity importer validation. It does not assert restored server or database state.'
    )
    Write-GeneratedFile $stageRoot 'reports/ULTIMATE-RECOVERY-READINESS-REPORT.md' `
        (New-MarkdownReportContent 'Ultimate Recovery Readiness Report' $generatedAtUtc $finalEngineeringStatus $readinessLines) $provenance

    function Build-PortablePackage {
        param([string]$Kind, [string]$ZipName, [string]$CmdAsset, [string]$ReadmeAsset, [string]$ContractStatus)
        $packageRoot = Join-Path $stageRoot ('.package-' + $Kind + '-' + [guid]::NewGuid().ToString('N'))
        if (-not (Test-IsWithin $stageRoot $packageRoot)) { throw 'Unsafe portable package staging root' }
        [void](New-Item -ItemType Directory -Path $packageRoot)
        try {
            Copy-ValidatedInputFile $executableFull (Join-Path $packageRoot 'God2SemanticRecoveryEngine.exe') `
                $script:ValidatedInputSnapshots
            Copy-ValidatedInputFile (Join-Path $PSScriptRoot ('assets\validation\' + $CmdAsset)) `
                (Join-Path $packageRoot $CmdAsset) $script:ValidatedInputSnapshots
            Copy-ValidatedInputFile (Join-Path $PSScriptRoot ('assets\validation\' + $ReadmeAsset)) `
                (Join-Path $packageRoot $ReadmeAsset) $script:ValidatedInputSnapshots
            [void](New-Item -ItemType Directory -Path (Join-Path $packageRoot 'schemas'))
            if ($Kind -eq 'rtx5070') {
                foreach ($schemaName in @('rtx5070-validation-result.schema.json', 'rtx5070-workload-matrix.schema.json',
                        'rtx5070-validation-manifest.schema.json', 'validation-package-manifest.schema.json',
                        'validation-contract.schema.json')) {
                    Copy-ValidatedInputFile (Join-Path $PSScriptRoot ('assets\schemas\' + $schemaName)) `
                        (Join-Path $packageRoot ('schemas\' + $schemaName)) $script:ValidatedInputSnapshots
                }
            } else {
                foreach ($schemaName in @('ultimate-live-validation-result.schema.json', 'ultimate-live-evidence-binding.schema.json',
                        'ultimate-live-result-manifest.schema.json', 'ultimate-live-result-attestation.schema.json',
                        'semantic-segment-manifest.schema.json', 'semantic-shared-ring-health-v4.schema.json',
                        'ultimate-validation-result.schema.json',
                        'validation-package-manifest.schema.json',
                        'validation-contract.schema.json')) {
                    Copy-ValidatedInputFile (Join-Path $PSScriptRoot ('assets\schemas\' + $schemaName)) `
                        (Join-Path $packageRoot ('schemas\' + $schemaName)) $script:ValidatedInputSnapshots
                }
            }
            $contract = [ordered]@{
                SchemaVersion = 1
                ProductVersion = $script:ProductVersion
                PackageKind = $Kind
                GeneratedAtUtc = $generatedAtUtc
                ContractStatusAtPackaging = $ContractStatus
                ExternalExecutionRequired = $true
                IntegrityStatus = 'STRUCTURE_VERIFIED'
                PassMustComeFromResultPackage = $true
                PackagedExecutableSHA256 = $executableSha256
                TargetClientSHA256 = if ($Kind -eq 'live') { $script:TargetClientSha256 } else { $null }
                TargetGpu = if ($Kind -eq 'rtx5070') { 'NVIDIA GeForce RTX 5070' } else { $null }
                NativeEntrypointStatus = if ($Kind -eq 'live') { $liveRunnerStatus } else { 'RTX5070EntrypointPackagedForExternalExecution' }
            }
            Write-NewUtf8File (Join-Path $packageRoot 'validation-contract.json') (($contract | ConvertTo-Json -Depth 6) + "`n")
            [void](Write-PackageManifest $packageRoot $Kind $generatedAtUtc)
            $zipPath = Join-Path $stageRoot ('validation\' + $ZipName)
            $zipDirectory = Split-Path -Parent $zipPath
            if (-not (Test-Path -LiteralPath $zipDirectory)) { [void](New-Item -ItemType Directory -Path $zipDirectory) }
            Compress-DeterministicZip $packageRoot $zipPath
            $verified = Verify-ValidationZip $zipPath
            Add-Provenance $provenance ('validation/' + $ZipName) ('GeneratedPortablePackage:' + $Kind)
            return $verified
        } finally {
            if (Test-Path -LiteralPath $packageRoot) {
                if ((Test-IsWithin $stageRoot $packageRoot) -and [System.IO.Path]::GetFileName($packageRoot).StartsWith('.package-' + $Kind + '-')) {
                    Remove-Item -LiteralPath $packageRoot -Recurse -Force
                } else {
                    throw "Refusing unsafe package staging cleanup: $packageRoot"
                }
            }
        }
    }

    $livePackage = Build-PortablePackage 'live' 'UltimateDeepRecoveryLiveValidationPackage.zip' `
        'RUN-ULTIMATE-LIVE-VALIDATION.cmd' 'README-ULTIMATE-LIVE-zh-TW.txt' $liveRunnerStatus
    $rtxPackage = Build-PortablePackage 'rtx5070' 'RTX5070UltimateValidationPackage.zip' `
        'RUN-RTX5070-ULTIMATE-VALIDATION.cmd' 'README-RTX5070-ULTIMATE-zh-TW.txt' $rtxStatus

    [void](Assert-ReportRootInventoryUnchanged $resolvedReportRoots $reportFiles)
    [void](Assert-AllInputSnapshotsUnchanged $script:ValidatedInputSnapshots)

    $artifacts = @()
    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName) {
        $relative = Convert-ToSlashPath $file.FullName.Substring($stageRoot.TrimEnd('\').Length + 1)
        if ($relative -in @($script:ArtifactManifestJson, $script:ArtifactManifestCsv)) { continue }
        if (-not $provenance.ContainsKey($relative)) { throw "Output artifact has no provenance: $relative" }
        $artifacts += [pscustomobject][ordered]@{
            RelativePath = $relative
            SizeBytes = [int64]$file.Length
            SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            Classification = Get-ArtifactClassification $relative
            Source = [string]$provenance[$relative]
        }
    }
    $artifactManifest = [ordered]@{
        SchemaVersion = 1
        Product = $script:ProductName
        ProductVersion = $script:ProductVersion
        DisplayVersion = $script:DisplayVersion
        Channel = $script:Channel
        GeneratedAtUtc = $generatedAtUtc
        FinalEngineeringStatus = $finalEngineeringStatus
        AutomatedGateStatus = $automatedStatus
        ExternalLiveGateStatus = $liveStatus
        Rtx5070Status = $rtxStatus
        AiModelStatus = $aiModelStatus
        ArtifactCount = $artifacts.Count
        ManifestSelfHashPolicy = 'artifact-sha256.json and artifact-sha256.csv are excluded to avoid recursive digests.'
        ExcludedFromArtifactDigest = @($script:ArtifactManifestJson, $script:ArtifactManifestCsv)
        Artifacts = $artifacts
    }
    Write-NewUtf8File (Join-Path $stageRoot $script:ArtifactManifestJson) (($artifactManifest | ConvertTo-Json -Depth 10) + "`n")
    $csvLines = $artifacts | Select-Object RelativePath, SizeBytes, SHA256, Classification, Source | ConvertTo-Csv -NoTypeInformation
    Write-NewUtf8File (Join-Path $stageRoot $script:ArtifactManifestCsv) (($csvLines -join "`r`n") + "`r`n")

    [void](Verify-UltimateOutput $stageRoot)
    [void](Assert-ReportRootInventoryUnchanged $resolvedReportRoots $reportFiles)
    [void](Assert-AllInputSnapshotsUnchanged $script:ValidatedInputSnapshots)
    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File) { $file.IsReadOnly = $true }

    if (Test-Path -LiteralPath $outputFull) {
        foreach ($entry in $preexistingHashes.GetEnumerator()) {
            if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf) -or
                (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash -ne [string]$entry.Value) {
                throw "Preexisting Ultimate build input changed during build: $($entry.Key)"
            }
        }
        $backupLeaf = '.' + [System.IO.Path]::GetFileName($outputFull) + '-prebuild-input-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
        $preexistingBackup = Get-FullPath (Join-Path $outputParent $backupLeaf)
        if (-not (Test-IsWithin $outputParent $preexistingBackup) -or (Test-Path -LiteralPath $preexistingBackup)) {
            throw "Unsafe or existing prebuild backup path: $preexistingBackup"
        }
        Move-Item -LiteralPath $outputFull -Destination $preexistingBackup
    }
    try {
        if (Test-Path -LiteralPath $outputFull) { throw "Ultimate output appeared during commit: $outputFull" }
        Move-Item -LiteralPath $stageRoot -Destination $outputFull
    } catch {
        if ($null -ne $preexistingBackup -and -not (Test-Path -LiteralPath $outputFull) -and (Test-Path -LiteralPath $preexistingBackup)) {
            Move-Item -LiteralPath $preexistingBackup -Destination $outputFull
        }
        throw
    }

    $verification = Verify-UltimateOutput $outputFull
    $result = [ordered]@{
        Status = $verification.Status
        FinalEngineeringStatus = $finalEngineeringStatus
        AutomatedGateStatus = $automatedStatus
        ExternalLiveGateStatus = $liveStatus
        Rtx5070Status = $rtxStatus
        AiModelStatus = $aiModelStatus
        OutputRoot = $outputFull
        ArtifactCount = $verification.ArtifactCount
        ManifestSHA256 = $verification.ManifestSHA256
        CsvManifestSHA256 = $verification.CsvManifestSHA256
        LiveValidationPackageSHA256 = $livePackage.SHA256
        Rtx5070ValidationPackageSHA256 = $rtxPackage.SHA256
        PreservedPreexistingBackup = $preexistingBackup
    }
    $result | ConvertTo-Json -Depth 6
} finally {
    if (Test-Path -LiteralPath $stageRoot) {
        if ((Test-IsWithin $outputParent $stageRoot) -and [System.IO.Path]::GetFileName($stageRoot).StartsWith('.' + [System.IO.Path]::GetFileName($outputFull) + '.staging-')) {
            Remove-Item -LiteralPath $stageRoot -Recurse -Force
        } else {
            throw "Refusing unsafe Ultimate staging cleanup: $stageRoot"
        }
    }
}
