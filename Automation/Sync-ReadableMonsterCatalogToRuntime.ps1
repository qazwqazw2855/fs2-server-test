param(
    [ValidateRange(1, 1000000)]
    [int] $MinimumMonsterCount = 5000
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$configPath = Join-Path $repoRoot "config\database.json"
$secretPath = Join-Path $repoRoot "Automation\State\db-admin-secret.bin"
$artifactPath = Join-Path $repoRoot "Artifacts\RecoveryFinal\runtime-monster-sync.json"
$mariaDb = "C:\Program Files\MariaDB 12.3\bin\mariadb.exe"

if (-not (Test-Path -LiteralPath $mariaDb -PathType Leaf)) {
    throw "MariaDB client was not found: $mariaDb"
}
if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
    throw "The DPAPI-protected database administrator secret is missing."
}
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^God2 Classic Server$|God2\.ClassicServer' }) {
    throw "Stop the local God2 Classic Server before replacing its monster catalog."
}

$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
Add-Type -AssemblyName System.Security
$protected = [IO.File]::ReadAllBytes($secretPath)
$plain = [Security.Cryptography.ProtectedData]::Unprotect(
    $protected,
    $null,
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
$secret = $null

function Invoke-MariaDbQuery {
    param([Parameter(Mandatory = $true)] [string] $Sql)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $mariaDb `
            --protocol=tcp `
            --host ([string]$config.host) `
            --port ([int]$config.port) `
            --user root `
            --connect-timeout ([int]$config.connectionTimeoutSeconds) `
            --default-character-set=utf8mb4 `
            --batch `
            --skip-column-names `
            --execute $Sql 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) {
        throw "MariaDB monster catalog operation failed: $($output -join [Environment]::NewLine)"
    }

    return @($output | Where-Object { $_ -is [string] -and -not $_.StartsWith("WARNING:", [StringComparison]::OrdinalIgnoreCase) })
}

try {
    $secret = ([Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json)
    $env:MYSQL_PWD = [string]$secret.password

    $preflight = @(Invoke-MariaDbQuery -Sql @'
SELECT COUNT(*),
       COUNT(DISTINCT `名稱`),
       SUM(`名稱`='雲鶴'),
       SUM(`名稱`='褐蝸螺'),
       SUM(`名稱` IS NULL OR TRIM(`名稱`)=''),
       SUM(`名稱` REGEXP '(sha256|guid|payload|0x[0-9A-Fa-f]+|\\[byte:)')
FROM `god2_recovered`.`怪物`;
'@)
    if ($preflight.Count -ne 1) {
        throw "Readable monster preflight returned an unexpected row count."
    }

    $fields = $preflight[0] -split "`t"
    if ($fields.Count -ne 6) {
        throw "Readable monster preflight returned an unexpected column count."
    }

    $sourceCount = [int64]$fields[0]
    $distinctNames = [int64]$fields[1]
    $cloudCraneCount = [int64]$fields[2]
    $brownSnailCount = [int64]$fields[3]
    $blankNameCount = [int64]$fields[4]
    $machineNameCount = [int64]$fields[5]
    if ($sourceCount -lt $MinimumMonsterCount -or
        $sourceCount -ne $distinctNames -or
        $cloudCraneCount -ne 1 -or
        $brownSnailCount -ne 1 -or
        $blankNameCount -ne 0 -or
        $machineNameCount -ne 0) {
        throw "Readable monster catalog gate failed; no runtime data was changed."
    }

    $before = @(Invoke-MariaDbQuery -Sql @'
SELECT (SELECT COUNT(*) FROM `god2_game`.`monsters`),
       (SELECT COUNT(*) FROM `god2_game`.`monster_spawns`),
       (SELECT COUNT(*) FROM `god2_game`.`monster_drops`),
       (SELECT COUNT(*) FROM `god2_game`.`monster_skills`);
'@)
    $beforeFields = $before[0] -split "`t"

    Invoke-MariaDbQuery -Sql @'
START TRANSACTION;
DELETE FROM `god2_game`.`monster_spawns`;
DELETE FROM `god2_game`.`monster_drops`;
DELETE FROM `god2_game`.`monster_skills`;
DELETE FROM `god2_game`.`monsters`;
INSERT INTO `god2_game`.`monsters`
    (`monster_id`,`code`,`name_zh_tw`,`name_original`,`monster_family`,`resource_id`,
     `level`,`max_hp`,`max_mp`,`strength`,`constitution`,`intelligence`,`speed`,
     `metal`,`wood`,`water`,`fire`,`earth`,`physical_attack`,`physical_defense`,
     `magic_attack`,`magic_defense`,`experience_reward`,`currency_reward`,`ai_profile_id`,
     `boss`,`elite`,`aggressive`,`evidence_status`,`enabled`,`admin_note`)
SELECT `怪物編號`,NULL,`名稱`,NULL,NULL,NULL,
       `等級`,`HP`,`MP`,NULL,NULL,NULL,`速度`,
       NULL,NULL,NULL,NULL,NULL,COALESCE(`物理攻擊`,`攻擊`),COALESCE(`物理防禦`,`防禦`),
       `法術攻擊`,`法術防禦`,`經驗`,
       CASE WHEN `金錢最小`=`金錢最大` THEN `金錢最小` ELSE NULL END,NULL,
       NULL,NULL,NULL,
       CASE WHEN `等級` IS NOT NULL AND `HP` IS NOT NULL THEN 'Verified' ELSE 'Recovered' END,
       0,
       CASE WHEN `等級` IS NULL OR `HP` IS NULL
            THEN '已匯入最新繁體怪物目錄；戰鬥數值與出生點驗證完成前停用。'
            WHEN `五行屬性` IS NOT NULL
            THEN CONCAT('已匯入最新繁體怪物目錄；五行證據=',`五行屬性`,'；出生點驗證完成前停用。')
            ELSE '已匯入最新繁體怪物目錄；出生點驗證完成前停用。' END
FROM `god2_recovered`.`怪物`
ORDER BY `怪物編號`;
COMMIT;
'@ | Out-Null

    $after = @(Invoke-MariaDbQuery -Sql @'
SELECT COUNT(*),
       SUM(`name_zh_tw`='雲鶴'),
       SUM(`name_zh_tw`='褐蝸螺'),
       SUM(`enabled`=1),
       SUM(`experience_reward` IS NOT NULL)
FROM `god2_game`.`monsters`;
'@)
    $afterFields = $after[0] -split "`t"
    if ([int64]$afterFields[0] -ne $sourceCount -or [int64]$afterFields[1] -ne 1 -or [int64]$afterFields[2] -ne 1) {
        throw "Runtime monster catalog post-commit verification failed."
    }

    $artifact = [ordered]@{
        schemaVersion = "god2-runtime-monster-catalog-sync-v1"
        status = "RUNTIME_MONSTER_CATALOG_REPLACED"
        sourceDatabase = "god2_recovered"
        targetDatabase = "god2_game"
        sourceMonsterCount = $sourceCount
        sourceDistinctNameCount = $distinctNames
        oldMonsterCount = [int64]$beforeFields[0]
        oldSpawnCount = [int64]$beforeFields[1]
        oldDropCount = [int64]$beforeFields[2]
        oldSkillCount = [int64]$beforeFields[3]
        newMonsterCount = [int64]$afterFields[0]
        enabledMonsterCount = [int64]$afterFields[3]
        monstersWithExperience = [int64]$afterFields[4]
        cloudCraneZhTw = "雲鶴"
        brownSnailZhTw = "褐蝸螺"
        unknownCombatValuesRemainNull = $true
        syncedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    [IO.Directory]::CreateDirectory((Split-Path $artifactPath -Parent)) | Out-Null
    [IO.File]::WriteAllText(
        $artifactPath,
        ($artifact | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    $artifact | ConvertTo-Json -Depth 5
}
finally {
    if ($plain) {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $secret = $null
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
}
