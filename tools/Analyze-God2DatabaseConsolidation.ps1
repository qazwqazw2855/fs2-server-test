param(
    [string]$OutputDirectory = "Reports/DatabaseConsolidation",
    [switch]$FailOnFormalResearchColumns
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

. (Join-Path $root "Automation/God2Automation.Common.ps1")

function Find-MariaDbClient {
    $candidates = @(
        "C:\Program Files\MariaDB 12.3\bin\mariadb.exe",
        "C:\Program Files\MariaDB 12.2\bin\mariadb.exe",
        "C:\Program Files\MariaDB 12.1\bin\mariadb.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $fromPath = Get-Command mariadb -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    throw "MariaDB client was not found."
}

function Invoke-MariaDbRead {
    param([Parameter(Mandatory)][string]$Sql)

    $env:MYSQL_PWD = $env:GOD2_DB_ADMIN_PASSWORD
    try {
        & $script:MariaDbClient `
            --host=$($script:DatabaseConfig.host) `
            --port=$($script:DatabaseConfig.port) `
            --user=root `
            --batch `
            --raw `
            --skip-column-names `
            -e $Sql
    }
    finally {
        Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
    }
}

function Convert-TsvRows {
    param(
        [string[]]$Rows,
        [string[]]$Columns
    )

    foreach ($row in $Rows) {
        if ([string]::IsNullOrWhiteSpace($row)) {
            continue
        }

        $parts = $row -split "`t", $Columns.Count
        $object = [ordered]@{}
        for ($i = 0; $i -lt $Columns.Count; $i++) {
            $object[$Columns[$i]] = if ($i -lt $parts.Count) { $parts[$i] } else { "" }
        }

        [pscustomobject]$object
    }
}

function Get-RuntimeTableReferences {
    $sourceRoots = @(
        (Join-Path $root "src"),
        (Join-Path $root "tools"),
        (Join-Path $root "Automation"),
        (Join-Path $root "scripts")
    ) | Where-Object { Test-Path $_ }

    $references = New-Object "System.Collections.Generic.HashSet[string]"
    $bareReferences = New-Object "System.Collections.Generic.HashSet[string]"

    $files = foreach ($sourceRoot in $sourceRoots) {
        Get-ChildItem $sourceRoot -Recurse -File -Include "*.cs","*.ps1" |
            Where-Object { $_.FullName -notmatch "\\bin\\|\\obj\\|\\.staging-" }
    }

    $files | ForEach-Object {
        $text = Get-Content $_.FullName -Raw

        foreach ($match in [regex]::Matches($text, "``(?<schema>god2|god2_game|god2_player|god2_game_meta)``\.``(?<table>[^``]+)``")) {
            [void]$references.Add("$($match.Groups["schema"].Value).$($match.Groups["table"].Value)")
        }

        foreach ($match in [regex]::Matches($text, "(FROM|JOIN|UPDATE|INTO|DELETE FROM)\s+``(?<table>[^``]+)``", "IgnoreCase")) {
            [void]$bareReferences.Add($match.Groups["table"].Value)
        }

        foreach ($match in [regex]::Matches($text, "base\(\s*""(?<table>[a-zA-Z0-9_]+)""\s*\)")) {
            [void]$bareReferences.Add($match.Groups["table"].Value)
        }
    }

    [pscustomobject]@{
        Qualified = @($references | Sort-Object)
        Bare = @($bareReferences | Sort-Object)
    }
}

function Get-Classification {
    param(
        [string]$Schema,
        [string]$Table,
        [string]$TableType,
        [long]$EstimatedRows,
        [bool]$RuntimeReferenced,
        [bool]$HasResearchColumn
    )

    $fullName = "$Schema.$Table"

    if ($Schema -eq "god2" -and $Table -eq "__schemaversion") {
        return "KeepMigrationLedger"
    }

    if ($Schema -eq "god2" -and $Table -eq "runtime_verification_transactions") {
        return "MoveToResearchArchive"
    }

    if ($Schema -eq "god2_game_meta") {
        return "KeepFormalMetadata"
    }

    if ($TableType -eq "VIEW") {
        if (-not $RuntimeReferenced -and $Table -match "(?i)(legacy|candidate|review|queue|source|gap|readiness|overview|validation|health|evidence|audit)") {
            return "MoveToResearchArchive"
        }

        return "KeepReadableView"
    }

    if ($RuntimeReferenced -and -not $HasResearchColumn) {
        return "KeepRuntimeFormal"
    }

    if ($RuntimeReferenced -and $HasResearchColumn) {
        return "RefactorRuntimeFormal"
    }

    $formalGameplayCatalogTables = @(
        "character_classes",
        "class_level_stats",
        "class_stat_growth",
        "immortal_ranks",
        "item_set_bonuses",
        "monster_combat_stat_design_rules",
        "monster_drop_design_rules",
        "monster_spawn_design_rules",
        "pet_growth_archetypes"
    )

    if ($Schema -eq "god2_game" -and -not $HasResearchColumn -and ($formalGameplayCatalogTables -contains $Table)) {
        if ($Table -like "monster_*_design_rules") {
            return "KeepFormalMetadata"
        }

        return "KeepRuntimeFormal"
    }

    if ($Table -match "(?i)(^xjz_|evidence|candidate|source_rows|observations|packet|capture|staging|payload|raw|opaque|unknown)") {
        return "MoveToResearchArchive"
    }

    if ($HasResearchColumn) {
        return "MoveOrRefactorResearchColumns"
    }

    if ($EstimatedRows -eq 0) {
        return "EmptyFormalCandidate"
    }

    if ($Schema -eq "god2_game" -or $Schema -eq "god2_player") {
        return "KeepPendingRuntimeReview"
    }

    return "Review"
}

function Test-OperationalRuntimeColumn {
    param(
        [string]$Schema,
        [string]$Table,
        [string]$Column
    )

    if ($Schema -notin @("god2", "god2_game", "god2_player")) {
        return $false
    }

    $isRuntimeDurabilityTable = $Table -match "(?i)(idempotency|audit|outbox|journal|checkpoint|finalization|actions|applications|removals|runtime_state|mutations|transactions|operations|recovery|releases)$"
    if (-not $isRuntimeDurabilityTable) {
        return $false
    }

    return $Column -match "(?i)(^IdempotencyKeyHash$|^idempotency_key_hash$|^PayloadHash$|^payload_hash$|^payload_fingerprint$|^PayloadVersion$|^ResultJson$|^PendingRewardPlanJson$|^ActionJson$|^CanonicalPayload$|^EventJson$|^DeliveryStateJson$|^PlanJson$|^ApplicationJson$|^RemovalJson$|^DetailJson$|^RecoveryState$|^StateJson$|^SnapshotJson$|^RequestJson$|^ResponseJson$|^RawMetadata$|^ContentHash$)"
}

$script:DatabaseConfig = Get-God2DatabaseConfig
$secretStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $secretStatus.HasSecret) {
    throw "Database password is unavailable: $($secretStatus.FailureCode)"
}

$script:MariaDbClient = Find-MariaDbClient
$runtimeReferences = Get-RuntimeTableReferences

$tableSql = @"
SELECT TABLE_SCHEMA,TABLE_NAME,TABLE_TYPE,COALESCE(TABLE_ROWS,0)
FROM information_schema.TABLES
WHERE TABLE_SCHEMA IN ('god2','god2_game','god2_player','god2_game_meta')
ORDER BY TABLE_SCHEMA,TABLE_NAME;
"@

$columnSql = @"
SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME,COLUMN_TYPE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA IN ('god2','god2_game','god2_player','god2_game_meta')
  AND (
    COLUMN_NAME REGEXP 'raw|opaque|unknown|payload|hex|blob|packet|evidence|candidate|machine|undecoded'
    OR COLUMN_TYPE REGEXP 'blob|binary'
  )
ORDER BY TABLE_SCHEMA,TABLE_NAME,ORDINAL_POSITION;
"@

$tables = @(Convert-TsvRows -Rows (Invoke-MariaDbRead -Sql $tableSql) -Columns @("schema","table","type","estimatedRows"))
$researchColumns = @(
    Convert-TsvRows -Rows (Invoke-MariaDbRead -Sql $columnSql) -Columns @("schema","table","column","columnType") |
        Where-Object {
            -not (Test-OperationalRuntimeColumn -Schema $_.schema -Table $_.table -Column $_.column)
        }
)

$researchColumnLookup = @{}
foreach ($column in $researchColumns) {
    $key = "$($column.schema).$($column.table)"
    if (-not $researchColumnLookup.ContainsKey($key)) {
        $researchColumnLookup[$key] = New-Object System.Collections.Generic.List[object]
    }
    $researchColumnLookup[$key].Add($column)
}

$tableReports = foreach ($table in $tables) {
    $fullName = "$($table.schema).$($table.table)"
    $runtimeReferenced =
        ($runtimeReferences.Qualified -contains $fullName) -or
        ($runtimeReferences.Bare -contains $table.table)
    $hasResearchColumn = $researchColumnLookup.ContainsKey($fullName)
    $rows = [long]$table.estimatedRows

    [pscustomobject]@{
        schema = $table.schema
        table = $table.table
        type = $table.type
        estimatedRows = $rows
        runtimeReferenced = $runtimeReferenced
        hasResearchColumn = $hasResearchColumn
        researchColumnCount = if ($hasResearchColumn) { $researchColumnLookup[$fullName].Count } else { 0 }
        classification = Get-Classification `
            -Schema $table.schema `
            -Table $table.table `
            -TableType $table.type `
            -EstimatedRows $rows `
            -RuntimeReferenced $runtimeReferenced `
            -HasResearchColumn $hasResearchColumn
    }
}

$summary = $tableReports |
    Group-Object classification |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            classification = $_.Name
            count = $_.Count
        }
    }

$snapshot = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    database = [pscustomobject]@{
        host = $DatabaseConfig.host
        port = $DatabaseConfig.port
        databaseName = $DatabaseConfig.databaseName
        runtimeUsername = $DatabaseConfig.username
        auditUsername = "root"
        passwordSource = $secretStatus.Source
    }
    runtimeQualifiedReferenceCount = $runtimeReferences.Qualified.Count
    runtimeBareReferenceCount = $runtimeReferences.Bare.Count
    tableCount = $tableReports.Count
    researchColumnCount = $researchColumns.Count
    summary = $summary
    tables = $tableReports
    researchColumns = $researchColumns
    runtimeReferences = $runtimeReferences
}

$jsonPath = Join-Path $outputRoot "database-consolidation-audit.json"
$mdPath = Join-Path $outputRoot "database-consolidation-audit.md"

$snapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# God2 database consolidation audit")
$lines.Add("")
$lines.Add("Generated UTC: $($snapshot.generatedAtUtc)")
$lines.Add("")
$lines.Add("## Summary")
$lines.Add("")
$lines.Add("| Classification | Tables |")
$lines.Add("|---|---:|")
foreach ($row in $summary) {
    $lines.Add("| $($row.classification) | $($row.count) |")
}
$lines.Add("")
$lines.Add("## Tables that need cleanup before owner-facing formal DB")
$lines.Add("")
$lines.Add("| Table | Rows | Runtime referenced | Research columns | Classification |")
$lines.Add("|---|---:|---:|---:|---|")
foreach ($table in ($tableReports | Where-Object { $_.classification -notin @("KeepRuntimeFormal","KeepFormalMetadata","KeepMigrationLedger") } | Sort-Object classification,schema,table)) {
    $lines.Add("| $($table.schema).$($table.table) | $($table.estimatedRows) | $($table.runtimeReferenced) | $($table.researchColumnCount) | $($table.classification) |")
}
$lines.Add("")
$lines.Add("## Research-looking columns still present")
$lines.Add("")
$lines.Add("| Table | Column | Type |")
$lines.Add("|---|---|---|")
foreach ($column in $researchColumns) {
    $lines.Add("| $($column.schema).$($column.table) | $($column.column) | $($column.columnType) |")
}

$lines | Set-Content -Path $mdPath -Encoding UTF8

Write-Output "Wrote $jsonPath"
Write-Output "Wrote $mdPath"
Write-Output "table_count=$($snapshot.tableCount)"
Write-Output "research_column_count=$($snapshot.researchColumnCount)"
foreach ($row in $summary) {
    Write-Output "classification.$($row.classification)=$($row.count)"
}

$formalCleanupCandidateCount = @($tableReports | Where-Object { $_.classification -in @("RefactorRuntimeFormal","MoveOrRefactorResearchColumns","MoveToResearchArchive") }).Count
if ($FailOnFormalResearchColumns -and $formalCleanupCandidateCount -gt 0) {
    throw "Formal database still contains research/archive cleanup candidates."
}
