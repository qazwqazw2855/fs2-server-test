$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mergeScript = Join-Path $PSScriptRoot 'Merge-God2PkDualClientVisibleStats.ps1'
$lateJoinRoot = Join-Path $repoRoot `
    'Artifacts\ClientInstrumentation\OfficialEvidenceLauncher\PKLateJoinAnalysis-20260815'
$testRoot = Join-Path $repoRoot `
    'Artifacts\AnalysisScratch\PkCombatOpponentContractMergeTests'
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Utf8Json([string] $Path, [object] $Value) {
    [IO.File]::WriteAllText(
        $Path, ($Value | ConvertTo-Json -Depth 20),
        [Text.UTF8Encoding]::new($false))
}

$manifestPath = Join-Path $lateJoinRoot `
    'combat-opponent-contract-merge-manifest.json'
$outputPath = Join-Path $testRoot 'positive-output.json'
$csvPath = Join-Path $testRoot 'positive-contract.csv'
& $mergeScript -ManifestPath $manifestPath -OutputPath $outputPath `
    -ContractCsvPath $csvPath | Out-Null

$output = Get-Content -LiteralPath $outputPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
Assert-True ($output.schemaVersion -ceq `
    'god2-pk-combat-opponent-contract-merge-v2') `
    'Positive merge schema version is wrong.'
Assert-True ([int]$output.participantCount -eq 3) `
    'Positive merge did not produce three participants.'
Assert-True ([bool]$output.allOwnerProfilesConfirmed) `
    'Not every owner profile was confirmed.'
Assert-True ([bool]$output.allOpponentProfilesAbsent) `
    'An opposing profile occurrence remained after the declared join baselines.'

$expectedIdentity = @{
    '233' = '0/11/233'
    '242' = '1/7/242'
    '254' = '1/6/254'
}
foreach ($participant in @($output.participants)) {
    $key = '{0}/{1}/{2}' -f [int]$participant.battleIdentity.side,
        [int]$participant.battleIdentity.slot,
        [int]$participant.battleIdentity.objectId
    Assert-True ($expectedIdentity[[string]$participant.battleIdentity.objectId] -ceq $key) `
        "Unexpected battle identity for $([string]$participant.name)."
    Assert-True (
        [int]$participant.evidence.ownerExactVectorOccurrenceCount -gt 0 -and
        [int]$participant.evidence.opposingExactVectorOccurrenceCount -eq 0) `
        "Evidence counts failed for $([string]$participant.name)."
}

$rows = @(Import-Csv -LiteralPath $csvPath -Encoding UTF8)
Assert-True ($rows.Count -eq 3) 'Contract CSV row count is wrong.'
$expectedHeader = 'origin,side,slot,object_id,island_index,area_id,' +
    'area_entry_index,vitality,strength,intelligence,speed,physical_attack,' +
    'physical_defense,magical_attack,magical_defense,gold,wood,water,fire,earth'
$actualHeader = Get-Content -LiteralPath $csvPath -Encoding UTF8 -TotalCount 1
Assert-True ($actualHeader -ceq $expectedHeader) `
    'Contract CSV header or field order is wrong.'
Assert-True (
    @($rows | Where-Object origin -ceq 'verified_direct_observation').Count -eq 3) `
    'Contract CSV contains an unsupported origin.'
$kero = @($rows | Where-Object object_id -ceq '254')
Assert-True (
    $kero.Count -eq 1 -and $kero[0].side -ceq '1' -and
    $kero[0].slot -ceq '6' -and $kero[0].vitality -ceq '52' -and
    $kero[0].strength -ceq '116' -and
    $kero[0].physical_attack -ceq '52' -and
    $kero[0].physical_defense -ceq '25' -and
    $kero[0].magical_attack -ceq '36' -and
    $kero[0].magical_defense -ceq '31' -and
    $kero[0].gold -ceq '0' -and $kero[0].wood -ceq '15') `
    'Kero contract values or identity are wrong.'

$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$badIdentityManifest = $sourceManifest | ConvertTo-Json -Depth 20 |
    ConvertFrom-Json
$badIdentityManifest.participants[2].identitySnapshotSequence = 81144
$badIdentityPath = Join-Path $testRoot 'bad-identity-manifest.json'
Write-Utf8Json $badIdentityPath $badIdentityManifest
$badIdentityRejected = $false
try {
    & $mergeScript -ManifestPath $badIdentityPath `
        -OutputPath (Join-Path $testRoot 'bad-identity-output.json') `
        -ContractCsvPath (Join-Path $testRoot 'bad-identity.csv') | Out-Null
}
catch {
    $badIdentityRejected = $_.Exception.Message -match `
        'did not match exactly one identity snapshot'
}
Assert-True $badIdentityRejected `
    'A mismatched actor name/sequence identity was not rejected.'

$badHashManifest = $sourceManifest | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$badHashManifest.identityPerspective.traceSha256 = '00' * 32
$badHashPath = Join-Path $testRoot 'bad-hash-manifest.json'
Write-Utf8Json $badHashPath $badHashManifest
$badHashRejected = $false
try {
    & $mergeScript -ManifestPath $badHashPath `
        -OutputPath (Join-Path $testRoot 'bad-hash-output.json') `
        -ContractCsvPath (Join-Path $testRoot 'bad-hash.csv') | Out-Null
}
catch {
    $badHashRejected = $_.Exception.Message -match `
        'trace SHA-256 does not match'
}
Assert-True $badHashRejected 'A mismatched identity trace hash was not rejected.'

$legacyManifest = Join-Path $repoRoot `
    'Artifacts\ClientInstrumentation\OfficialEvidenceLauncher\PKFourStatAnalysis-20260815\current-dual-pk-merge-manifest.json'
$legacyOutputPath = Join-Path $testRoot 'legacy-v1-output.json'
& $mergeScript -ManifestPath $legacyManifest -OutputPath $legacyOutputPath |
    Out-Null
$legacyOutput = Get-Content -LiteralPath $legacyOutputPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
Assert-True ($legacyOutput.schemaVersion -ceq `
    'god2-pk-dual-client-visible-stats-v1') `
    'Legacy v1 merge compatibility regressed.'
Assert-True ($null -eq $legacyOutput.combatOpponentStatsContract) `
    'Legacy v1 merge unexpectedly emitted a contract.'

$result = [ordered]@{
    schemaVersion = 'god2-pk-combat-opponent-contract-merge-test-v1'
    status = 'PASS'
    participantCount = 3
    positiveIdentityBindings = $expectedIdentity
    ownerProfilesConfirmed = $true
    opponentProfilesAbsentAfterBaselines = $true
    mismatchedIdentityRejected = $true
    mismatchedTraceHashRejected = $true
    legacyV1Compatibility = $true
    gameMemoryWritten = $false
    networkBytesEmitted = $false
}
$resultPath = Join-Path $testRoot 'test-result.json'
Write-Utf8Json $resultPath $result
$result | ConvertTo-Json -Depth 8
