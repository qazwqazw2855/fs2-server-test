[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$ReportRoot,
    [Parameter(Mandatory = $true)][string]$DeclarationPath,
    [string]$OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
. (Join-Path $PSScriptRoot 'PortableEvidence.ps1')
. (Join-Path $PSScriptRoot 'NativeAnalysisContract.ps1')

function Full([string]$Path) { [System.IO.Path]::GetFullPath($Path) }
function Slash([string]$Path) { $Path.Replace('\', '/') }
function Within([string]$Parent, [string]$Candidate) {
    (Full $Candidate).StartsWith((Full $Parent).TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)
}
function SafeRelative([string]$Path) {
    $value = Slash $Path
    if ([string]::IsNullOrWhiteSpace($value) -or [System.IO.Path]::IsPathRooted($value) -or
        $value.Contains(':') -or $value.StartsWith('/') -or $value.EndsWith('/') -or
        $value.Split('/') -contains '..' -or $value.Split('/') -contains '.' -or
        $value.Split('/') -contains '') { throw "Unsafe relative path: $Path" }
    $value
}
function NoReparse([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point rejected: $Path" }
    if ($item.PSIsContainer) {
        $bad = Get-ChildItem -LiteralPath $item.FullName -Recurse -Force |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
        if ($null -ne $bad) { throw "Reparse point rejected: $($bad.FullName)" }
    }
}
function NoReparsePath([string]$Parent, [string]$Candidate) {
    $parentFull = (Full $Parent).TrimEnd('\')
    $current = Full $Candidate
    if ($current -ne $parentFull -and -not (Within $parentFull $current)) {
        throw "Reparse ancestry candidate escaped its parent: $Candidate"
    }
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse point rejected in declared path ancestry: $current"
        }
        if ($current -eq $parentFull) { break }
        $next = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($next) -or $next -eq $current -or
            ($next -ne $parentFull -and -not (Within $parentFull $next))) {
            throw "Declared path ancestry escaped repository root: $Candidate"
        }
        $current = $next
    }
}
function HashBytes([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') } finally { $sha.Dispose() }
}
function Property([object]$Object, [string]$Name) {
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in $Object.Keys) { if ([string]$key -ceq $Name) { return $Object[$key] } }
        return $null
    }
    $property = $Object.PSObject.Properties | Where-Object { $_.Name -ceq $Name } | Select-Object -First 1
    if ($null -eq $property) { return $null }
    $property.Value
}
function InventoryHash([object[]]$Records) {
    $lines = @($Records | ForEach-Object {
        $relative = SafeRelative ([string](Property $_ 'RelativePath'))
        $size = [int64](Property $_ 'SizeBytes')
        $hash = [string](Property $_ 'SHA256')
        if ($size -lt 0 -or $hash -notmatch '^[A-F0-9]{64}$') { throw "Invalid inventory entry: $relative" }
        $relative.ToLowerInvariant() + "`0" + $size + "`0" + $hash
    } | Sort-Object)
    HashBytes ([System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n"))
}

$exe = Full $ExecutablePath
$repo = Full $RepositoryRoot
$reports = Full $ReportRoot
$declarationFile = Full $DeclarationPath
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
    -not (Test-Path -LiteralPath $repo -PathType Container) -or
    -not (Test-Path -LiteralPath $reports -PathType Container) -or
    -not (Test-Path -LiteralPath $declarationFile -PathType Leaf)) { throw 'Manifest generator input is missing' }
NoReparsePath $repo $repo
NoReparsePath $repo $exe
NoReparsePath $repo $reports
NoReparsePath $repo $declarationFile
NoReparse $exe
NoReparse $reports
NoReparse $declarationFile
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $reports 'ultimate-test-run-manifest.json' }
$output = Full $OutputPath
if (Within $reports $declarationFile) { throw 'DeclarationPath must be outside ReportRoot so it cannot become unsealed evidence' }
if (-not (Within $reports $output) -or [System.IO.Path]::GetFileName($output) -cne 'ultimate-test-run-manifest.json') {
    throw 'Output must be the exact ultimate-test-run-manifest.json under ReportRoot'
}
if (Test-Path -LiteralPath $output) { throw "Refusing to overwrite test-run manifest: $output" }
[void](Assert-UltimatePortableEvidenceTree $reports)

$declaration = Get-Content -Raw -LiteralPath $declarationFile -Encoding UTF8 | ConvertFrom-Json
if ([string](Property $declaration 'SchemaVersion') -ne 'god2-ultimate-test-run-declaration-v1') { throw 'Declaration schema mismatch' }
$runId = [string](Property $declaration 'RunId')
$generatedAtUtc = [string](Property $declaration 'GeneratedAtUtc')
$generatedAt = [DateTimeOffset]::MinValue
if ($runId -notmatch '^[A-Za-z0-9._-]{8,160}$' -or -not [DateTimeOffset]::TryParse($generatedAtUtc, [ref]$generatedAt)) {
    throw 'Declaration RunId/GeneratedAtUtc is invalid'
}

$exeInfo = Get-Item -LiteralPath $exe
$exeVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($exeVersion.FileVersion -ne '1.3.0.0' -or $exeVersion.ProductVersion -ne '1.3.0.0') {
    throw 'Declaration cannot bind a non-v1.3.0.0 executable'
}
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash

$sourceFiles = @()
$sourceSeen = @{}
foreach ($relativeValue in @(Property $declaration 'SourceFiles')) {
    $relative = SafeRelative ([string]$relativeValue)
    $sourceKey = $relative.ToLowerInvariant()
    if ($relative.StartsWith('Artifacts/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourceSeen.ContainsKey($sourceKey)) { throw "Generated/duplicate source path rejected: $relative" }
    $path = Full (Join-Path $repo ($relative.Replace('/', '\')))
    if (-not (Within $repo $path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Source file missing: $relative" }
    NoReparsePath $repo $path
    $file = Get-Item -LiteralPath $path
    $sourceFiles += [ordered]@{ RelativePath = $relative; SizeBytes = [int64]$file.Length
        SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    $sourceSeen[$sourceKey] = $true
}
if ($sourceFiles.Count -eq 0) { throw 'Declaration SourceFiles is empty' }
Assert-UltimateRepositoryNativeSourceInventory $repo
foreach ($requiredSource in @(Get-UltimateMandatoryNativeAnalysisSources)) {
    $requiredPath = [string]$requiredSource.RelativePath
    if (-not $sourceSeen.ContainsKey($requiredPath.ToLowerInvariant())) {
        throw "Declaration SourceFiles omits mandatory native analyzer source: $requiredPath"
    }
}
$sourceTreeHash = InventoryHash $sourceFiles

$commands = @()
$commandSeen = @{}
$commandExpectedExit = @{}
foreach ($command in @(Property $declaration 'Commands')) {
    $id = [string](Property $command 'CommandId')
    $startedText = [string](Property $command 'StartedAtUtc')
    $completedText = [string](Property $command 'CompletedAtUtc')
    $exitCode = [int](Property $command 'ExitCode')
    $expectedExitCode = [int](Property $command 'ExpectedExitCode')
    $started = [DateTimeOffset]::MinValue
    $completed = [DateTimeOffset]::MinValue
    if ($id -notmatch '^[A-Za-z0-9._-]{3,120}$' -or $commandSeen.ContainsKey($id) -or
        [string]::IsNullOrWhiteSpace([string](Property $command 'Command')) -or
        [string](Property $command 'WorkingDirectory') -ne '.' -or
        $expectedExitCode -notin @(0, 4) -or $exitCode -ne $expectedExitCode -or
        -not [DateTimeOffset]::TryParse($startedText, [ref]$started) -or
        -not [DateTimeOffset]::TryParse($completedText, [ref]$completed) -or $completed -lt $started) {
        throw "Command declaration is invalid: $id"
    }
    $commands += [ordered]@{ CommandId = $id; Command = [string](Property $command 'Command'); WorkingDirectory = '.'
        StartedAtUtc = $started.ToUniversalTime().ToString('o'); CompletedAtUtc = $completed.ToUniversalTime().ToString('o')
        ExitCode = $exitCode; ExpectedExitCode = $expectedExitCode
        ExecutableSHA256 = $exeHash; SourceTreeSHA256 = $sourceTreeHash }
    $commandSeen[$id] = $true
    $commandExpectedExit[$id] = $expectedExitCode
}
if ($commands.Count -eq 0) { throw 'Declaration Commands is empty' }

$evidence = @()
$evidenceSeen = @{}
$evidencePathSeen = @{}
$requiredNativeSources = @{}
foreach ($requiredSource in @(Get-UltimateMandatoryNativeAnalysisSources)) {
    $requiredNativeSources[([string]$requiredSource.RelativePath).ToLowerInvariant()] = $requiredSource
}
$declaredNativeSources = @{}
$declaredAnalyzerIds = @{}
foreach ($item in @(Property $declaration 'Evidence')) {
    $id = [string](Property $item 'EvidenceId')
    $relative = SafeRelative ([string](Property $item 'RelativePath'))
    $commandId = [string](Property $item 'CommandId')
    if ($id -notmatch '^[A-Za-z0-9._-]{3,160}$' -or $evidenceSeen.ContainsKey($id) -or
        $evidencePathSeen.ContainsKey($relative) -or -not $commandSeen.ContainsKey($commandId)) {
        throw "Evidence declaration is invalid: $id/$relative"
    }
    $path = Full (Join-Path $reports ($relative.Replace('/', '\')))
    if (-not (Within $reports $path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Evidence file missing: $relative" }
    $file = Get-Item -LiteralPath $path
    $kind = [string](Property $item 'Kind')
    $record = [ordered]@{ EvidenceId = $id; RootIndex = 1; RelativePath = $relative; SizeBytes = [int64]$file.Length
        SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; Kind = $kind; CommandId = $commandId }
    foreach ($optional in @('AnalyzerId','Architecture','SourceRelativePath')) {
        $value = Property $item $optional
        if ($null -ne $value) { $record[$optional] = [string]$value }
    }
    if ($kind -eq 'NativeCodeAnalysis') {
        $analyzerId = [string](Property $item 'AnalyzerId')
        $architecture = [string](Property $item 'Architecture')
        $sourceRelative = SafeRelative ([string](Property $item 'SourceRelativePath'))
        $sourceKey = $sourceRelative.ToLowerInvariant()
        if ($analyzerId -notmatch '^[A-Za-z0-9._-]{3,160}$' -or $declaredAnalyzerIds.ContainsKey($analyzerId) -or
            -not $requiredNativeSources.ContainsKey($sourceKey) -or $declaredNativeSources.ContainsKey($sourceKey) -or
            -not $sourceSeen.ContainsKey($sourceKey) -or
            $architecture -cne [string]$requiredNativeSources[$sourceKey].Architecture) {
            throw "Native analyzer declaration is invalid: $analyzerId/$sourceRelative/$architecture"
        }
        $declaredAnalyzerIds[$analyzerId] = $true
        $declaredNativeSources[$sourceKey] = $true
    } elseif ($null -ne (Property $item 'AnalyzerId') -or $null -ne (Property $item 'Architecture') -or
        $null -ne (Property $item 'SourceRelativePath')) {
        throw "Non-analyzer evidence contains analyzer provenance: $id"
    }
    $evidence += $record
    $evidenceSeen[$id] = $true
    $evidencePathSeen[$relative] = $true
}
if ($declaredNativeSources.Count -ne $requiredNativeSources.Count) {
    throw "Native analyzer declaration coverage mismatch: $($declaredNativeSources.Count)/$($requiredNativeSources.Count)"
}
foreach ($requiredKey in $requiredNativeSources.Keys) {
    if (-not $declaredNativeSources.ContainsKey($requiredKey)) {
        throw "Missing mandatory native analyzer declaration: $($requiredNativeSources[$requiredKey].RelativePath)"
    }
}
$observed = @(Get-ChildItem -LiteralPath $reports -Recurse -File)
if ($observed.Count -ne $evidence.Count) { throw 'Declaration Evidence does not cover the complete ReportRoot inventory' }
foreach ($file in $observed) {
    $relative = Slash $file.FullName.Substring($reports.TrimEnd('\').Length + 1)
    if (-not $evidencePathSeen.ContainsKey($relative)) { throw "Unmanifested ReportRoot file: $relative" }
}

# Declaration-level importer contracts are intentionally distinct. The 70
# offline compiled-contract/migration/replay tests prove implementation without
# production I/O; the formal real-ZIP probe proves trusted-identity fail-closed
# with expected process exit 4 and no output-directory creation assertion.
$declaredTests = @(Property $declaration 'Tests')
$declaredGates = @(Property $declaration 'Gates')
$declarationGateMap = @{}
foreach ($gate in $declaredGates) {
    $gateName = [string](Property $gate 'Name')
    if ([string]::IsNullOrWhiteSpace($gateName) -or $declarationGateMap.ContainsKey($gateName)) {
        throw "Duplicate/invalid declaration gate: $gateName"
    }
    $declarationGateMap[$gateName] = $gate
}
foreach ($requiredContractGate in @('ImporterOfflineVerifierContract','ImporterFormalIdentityFailClosed',
        'DllEnhancedCaptureAcceptance')) {
    if (-not $declarationGateMap.ContainsKey($requiredContractGate)) {
        throw "Declaration is missing required contract gate: $requiredContractGate"
    }
}
$offlineTestIds = @(Property $declarationGateMap['ImporterOfflineVerifierContract'] 'TestIds')
$formalTestIds = @(Property $declarationGateMap['ImporterFormalIdentityFailClosed'] 'TestIds')
if ($offlineTestIds.Count -ne 70) { throw 'ImporterOfflineVerifierContract must declare exactly 70 importer-suite tests' }
if ($formalTestIds.Count -ne 1 -or [string]$formalTestIds[0] -cne 'importer-formal:expected-exit4-output-directory-absent') {
    throw 'ImporterFormalIdentityFailClosed must declare the exact exit4/output-directory-absent producing assertion'
}
$declarationTestMap = @{}
foreach ($test in $declaredTests) {
    $testId = [string](Property $test 'TestId')
    if ([string]::IsNullOrWhiteSpace($testId) -or $declarationTestMap.ContainsKey($testId)) {
        throw "Duplicate/invalid declaration test: $testId"
    }
    $declarationTestMap[$testId] = $test
}
foreach ($offlineTestId in $offlineTestIds) {
    if (-not $declarationTestMap.ContainsKey([string]$offlineTestId) -or
        [string](Property $declarationTestMap[[string]$offlineTestId] 'GateName') -cne 'ImporterOfflineVerifierContract') {
        throw "Importer offline test is not bound to ImporterOfflineVerifierContract: $offlineTestId"
    }
}
$formalTest = $declarationTestMap[[string]$formalTestIds[0]]
if ($null -eq $formalTest -or [string](Property $formalTest 'GateName') -cne 'ImporterFormalIdentityFailClosed') {
    throw 'Formal importer assertion is not bound to ImporterFormalIdentityFailClosed'
}
$formalEvidenceIds = @(Property $declarationGateMap['ImporterFormalIdentityFailClosed'] 'EvidenceIds')
if ($formalEvidenceIds.Count -ne 1) { throw 'ImporterFormalIdentityFailClosed must declare exactly one evidence JSON' }
$formalEvidence = $evidence | Where-Object { [string](Property $_ 'EvidenceId') -ceq [string]$formalEvidenceIds[0] } | Select-Object -First 1
if ($null -eq $formalEvidence -or [string](Property $formalEvidence 'Kind') -cne 'ImporterFormalIdentityFailClosedJson' -or
    [System.IO.Path]::GetFileName([string](Property $formalEvidence 'RelativePath')) -cne 'formal-zip-blocked-output.json') {
    throw 'ImporterFormalIdentityFailClosed must bind formal-zip-blocked-output.json with its exact evidence kind'
}
$formalCommandId = [string](Property $formalEvidence 'CommandId')
if (-not $commandExpectedExit.ContainsKey($formalCommandId) -or [int]$commandExpectedExit[$formalCommandId] -ne 4) {
    throw 'ImporterFormalIdentityFailClosed evidence must be produced by an ExpectedExitCode=4 command'
}
$dllAcceptanceTestIds = @(Property $declarationGateMap['DllEnhancedCaptureAcceptance'] 'TestIds')
$dllAcceptanceEvidenceIds = @(Property $declarationGateMap['DllEnhancedCaptureAcceptance'] 'EvidenceIds')
if ($dllAcceptanceTestIds.Count -ne 1 -or $dllAcceptanceEvidenceIds.Count -ne 1) {
    throw 'DllEnhancedCaptureAcceptance must declare exactly one test and one evidence JSON'
}
$dllAcceptanceTest = $declarationTestMap[[string]$dllAcceptanceTestIds[0]]
if ($null -eq $dllAcceptanceTest -or [string](Property $dllAcceptanceTest 'GateName') -cne 'DllEnhancedCaptureAcceptance') {
    throw 'DLL enhanced capture acceptance test is not bound to DllEnhancedCaptureAcceptance'
}
$dllAcceptanceEvidence = $evidence | Where-Object {
    [string](Property $_ 'EvidenceId') -ceq [string]$dllAcceptanceEvidenceIds[0]
} | Select-Object -First 1
if ($null -eq $dllAcceptanceEvidence -or
    [string](Property $dllAcceptanceEvidence 'Kind') -cne 'EnhancedCaptureAcceptanceJson' -or
    [System.IO.Path]::GetFileName([string](Property $dllAcceptanceEvidence 'RelativePath')) -cne
        'enhanced-capture-acceptance.json') {
    throw 'DllEnhancedCaptureAcceptance must bind the exact EnhancedCaptureAcceptanceJson report'
}
$dllAcceptanceCommandId = [string](Property $dllAcceptanceEvidence 'CommandId')
if (-not $commandExpectedExit.ContainsKey($dllAcceptanceCommandId) -or
    [int]$commandExpectedExit[$dllAcceptanceCommandId] -ne 0) {
    throw 'DllEnhancedCaptureAcceptance evidence must be produced by a successful ExpectedExitCode=0 command'
}
$evidenceInventory = @($evidence | ForEach-Object { [ordered]@{ RelativePath = 'input-01/' + $_.RelativePath
    SizeBytes = $_.SizeBytes; SHA256 = $_.SHA256 } })

$manifest = [ordered]@{
    SchemaVersion = 'god2-ultimate-test-run-manifest-v1'; RunId = $runId
    GeneratedAtUtc = $generatedAt.ToUniversalTime().ToString('o'); Product = 'God2 Semantic Recovery Engine'; ProductVersion = '1.3.0.0'
    Executable = [ordered]@{ SHA256 = $exeHash; SizeBytes = [int64]$exeInfo.Length; FileVersion = '1.3.0.0'; ProductVersion = '1.3.0.0'; Architecture = 'x64' }
    Source = [ordered]@{ TreeSHA256 = $sourceTreeHash; FileCount = $sourceFiles.Count; Files = $sourceFiles }
    EvidenceTreeSHA256 = InventoryHash $evidenceInventory
    Commands = $commands
    Evidence = $evidence
    Tests = $declaredTests
    Gates = $declaredGates
}
$manifestJson = ($manifest | ConvertTo-Json -Depth 16) + "`n"
Assert-UltimatePortableText $manifestJson 'generated ultimate-test-run-manifest.json'
[System.IO.File]::WriteAllText($output, $manifestJson, $utf8)
[ordered]@{ Status = 'ULTIMATE_TEST_RUN_MANIFEST_GENERATED'; OutputPath = $output
    SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash; SourceTreeSHA256 = $sourceTreeHash
    EvidenceTreeSHA256 = $manifest.EvidenceTreeSHA256; EvidenceCount = $evidence.Count
    TestCount = $manifest.Tests.Count; GateCount = $manifest.Gates.Count } | ConvertTo-Json -Depth 6
