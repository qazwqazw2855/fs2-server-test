param(
    [string] $ClientRoot,
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateRange(1, 1000)]
    [int] $MinimumCsvZCount = 80,
    [switch] $RunFullSolutionGates,
    [switch] $SkipOfflineStaticAnalysis
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$artifactRoot = Join-Path $repoRoot "Artifacts\OfficialFullExtractionFactory"
$phase2Script = Join-Path $repoRoot "Automation\Invoke-GameplayContentRecoveryPhase2.ps1"
$phase3Script = Join-Path $repoRoot "Automation\Invoke-GameplayContentRecoveryPhase3.ps1"
$syncMonsterScript = Join-Path $repoRoot "Automation\Sync-ReadableMonsterCatalogToRuntime.ps1"
$migrationScript = Join-Path $repoRoot "Automation\Invoke-God2PendingSchemaMigrationsAsAdmin.ps1"
$recoveryProject = Join-Path $repoRoot "tools\God2.GameplayContentRecovery\God2.GameplayContentRecovery.csproj"
$readableProject = Join-Path $repoRoot "tools\God2.ReadableDatabaseBuilder\God2.ReadableDatabaseBuilder.csproj"
$catalogProject = Join-Path $repoRoot "tools\God2.GameCatalogBuilder\God2.GameCatalogBuilder.csproj"
$offlineProject = Join-Path $repoRoot "tools\God2.OfflineClientReverseEngineering\God2.OfflineClientReverseEngineering.csproj"

function Resolve-OfficialClientRoot {
    $candidates = @(
        $ClientRoot,
        $env:GOD2_CLIENT_ROOT,
        ("C:\Program Files (x86)\仙界{0}II" -f [char]20256),
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "XJZ2")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath (Join-Path $resolved "Data2\Patch\FightEny.csvZ") -PathType Leaf) {
            return $resolved
        }
    }

    throw "Official client root was not found or does not contain Data2\Patch\FightEny.csvZ."
}

function Invoke-CheckedDotNet {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage ExitCode=$LASTEXITCODE"
    }
}

function Get-ClientRelativePath {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $rootPrefix = $ClientRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Client source path escaped the official client root: $fullPath"
    }
    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

$ClientRoot = Resolve-OfficialClientRoot
[IO.Directory]::CreateDirectory($artifactRoot) | Out-Null

$requiredClientFiles = @(
    "Data2\Patch\FightEny.csvZ",
    "Data2\Patch\NPC.csvZ",
    "Data2\Patch\Comm\gamedata.csvZ"
)
foreach ($relativePath in $requiredClientFiles) {
    $path = Join-Path $ClientRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required official client table is missing: $relativePath"
    }
}

$csvFiles = @(Get-ChildItem -LiteralPath $ClientRoot -Filter "*.csvZ" -File -Recurse | Sort-Object FullName)
if ($csvFiles.Count -lt $MinimumCsvZCount) {
    throw "Official client table preflight failed: csvZ=$($csvFiles.Count), required=$MinimumCsvZCount."
}

$clientExecutable = Get-ChildItem -LiteralPath $ClientRoot -Filter "god2_opt.exe" -File -Recurse |
    Sort-Object FullName |
    Select-Object -First 1
if ($null -eq $clientExecutable) {
    throw "Official client executable god2_opt.exe was not found under: $ClientRoot"
}

$manifest = [ordered]@{
    schemaVersion = "god2-official-full-extraction-source-manifest-v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    clientRootIdentity = Split-Path $ClientRoot -Leaf
    clientExecutable = [ordered]@{
        relativePath = Get-ClientRelativePath -Path $clientExecutable.FullName
        length = $clientExecutable.Length
        sha256 = (Get-FileHash -LiteralPath $clientExecutable.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    csvZCount = $csvFiles.Count
    tables = @($csvFiles | ForEach-Object {
        [ordered]@{
            relativePath = Get-ClientRelativePath -Path $_.FullName
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
Write-God2AtomicJson -Value $manifest -Path (Join-Path $artifactRoot "client-source-manifest.json")

Write-Output "stage=official-client-source-manifest;csvZ=$($csvFiles.Count)"
if (-not $SkipOfflineStaticAnalysis) {
    Write-Output "stage=offline-client-binary-and-table-analysis"
    Invoke-CheckedDotNet -Arguments @(
        "run", "--project", $offlineProject, "--configuration", $Configuration, "--",
        "--client", $clientExecutable.FullName
    ) -FailureMessage "Offline client reverse engineering failed."
}

Write-Output "stage=pending-schema-migrations-as-admin"
& $migrationScript -RepositoryRoot $repoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Pending schema migration bootstrap failed. ExitCode=$LASTEXITCODE"
}

Write-Output "stage=official-client-full-static-extraction"
& $phase2Script -ClientRoot $ClientRoot -Configuration $Configuration -SkipExternalGates:(!$RunFullSolutionGates)
if ($LASTEXITCODE -ne 0) {
    throw "Gameplay content recovery Phase 2 failed. ExitCode=$LASTEXITCODE"
}

Write-Output "stage=deep-semantic-promotion"
& $phase3Script -ClientRoot $ClientRoot -Configuration $Configuration -SkipExternalGates:(!$RunFullSolutionGates)
if ($LASTEXITCODE -ne 0) {
    throw "Gameplay content recovery Phase 3 failed. ExitCode=$LASTEXITCODE"
}

$secretStatus = Initialize-God2DatabasePasswordEnvironment
if (-not $secretStatus.hasSecret) {
    throw "SECURE DATABASE SECRET PROVISIONING: BLOCKED - CREDENTIAL VALUE NOT AVAILABLE ($($secretStatus.failureCode))"
}

try {
    Write-Output "stage=latest-only-history-prune"
    Invoke-CheckedDotNet -Arguments @(
        "run", "--project", $recoveryProject, "--configuration", $Configuration, "--",
        "--repository-root", $repoRoot, "--client-root", $ClientRoot,
        "--phase", "phase2", "--prune-history", "true"
    ) -FailureMessage "Recovery history prune failed."

    Write-Output "stage=readable-database-rebuild"
    Invoke-CheckedDotNet -Arguments @(
        "run", "--project", $readableProject, "--configuration", $Configuration, "--",
        "--repository-root", $repoRoot
    ) -FailureMessage "Readable database rebuild failed."

    Write-Output "stage=formal-runtime-monster-snapshot-replacement"
    & $syncMonsterScript
    if ($LASTEXITCODE -ne 0) {
        throw "Formal monster snapshot replacement failed. ExitCode=$LASTEXITCODE"
    }

    Write-Output "stage=formal-catalog-rebuild-and-validation"
    $builderSecretStatus = Initialize-God2DatabaseBuilderPasswordEnvironment
    if (-not $builderSecretStatus.hasSecret) {
        throw "Catalog Builder secret is unavailable: $($builderSecretStatus.failureCode)"
    }
    [Environment]::SetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME", "god2_catalog_builder", "Process")
    Invoke-CheckedDotNet -Arguments @(
        "run", "--project", $catalogProject, "--configuration", $Configuration, "--",
        "rebuild", "--config", (Join-Path $repoRoot "config\database.json")
    ) -FailureMessage "Formal catalog rebuild failed."

    if (-not $RunFullSolutionGates) {
        Write-Output "stage=focused-recovery-tests"
        Invoke-CheckedDotNet -Arguments @(
            "test", (Join-Path $repoRoot "tests\God2.GameplayContentRecovery.Tests\God2.GameplayContentRecovery.Tests.csproj"),
            "--configuration", $Configuration, "--no-restore", "--nologo"
        ) -FailureMessage "Focused gameplay recovery tests failed."
        Invoke-CheckedDotNet -Arguments @(
            "test", (Join-Path $repoRoot "tests\God2.OfflineClientReverseEngineering.Tests\God2.OfflineClientReverseEngineering.Tests.csproj"),
            "--configuration", $Configuration, "--no-restore", "--nologo"
        ) -FailureMessage "Focused offline reverse-engineering tests failed."
    }

    $phase2Summary = Read-God2JsonWithRetry -Path (Join-Path $repoRoot "Artifacts\GameplayContentRecoveryPhase2\final-summary.json")
    $phase3Summary = Read-God2JsonWithRetry -Path (Join-Path $repoRoot "Artifacts\GameplayContentRecoveryPhase3\final-summary.json")
    $runtimeMonsterSync = Read-God2JsonWithRetry -Path (Join-Path $repoRoot "Artifacts\RecoveryFinal\runtime-monster-sync.json")
    $factorySummary = [ordered]@{
        schemaVersion = "god2-official-full-extraction-factory-v1"
        status = "OFFICIAL_FULL_EXTRACTION_FACTORY_COMPLETED"
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        clientRootIdentity = Split-Path $ClientRoot -Leaf
        clientExecutableSha256 = $manifest.clientExecutable.sha256
        csvZCount = $manifest.csvZCount
        phase2RunId = $phase2Summary.runId
        phase2Status = $phase2Summary.status
        phase3RunId = $phase3Summary.runId
        phase3Status = $phase3Summary.status
        formalMonsterSync = $runtimeMonsterSync
        unknownValuesRemainNull = $true
        opaquePacketDataStoredOutsideFormalDatabase = $true
        latestSnapshotOnly = $true
    }
    Write-God2AtomicJson -Value $factorySummary -Path (Join-Path $artifactRoot "final-summary.json")
    $factorySummary | ConvertTo-Json -Depth 10
}
finally {
    $config = Get-God2DatabaseConfig
    if (-not [string]::IsNullOrWhiteSpace($config.passwordEnvironmentVariable)) {
        [Environment]::SetEnvironmentVariable([string]$config.passwordEnvironmentVariable, $null, "Process")
    }
    [Environment]::SetEnvironmentVariable("GOD2_DB_BUILDER_PASSWORD", $null, "Process")
    [Environment]::SetEnvironmentVariable("GOD2_DB_BUILDER_USERNAME", $null, "Process")
}
