Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\..\Automation\God2Automation.Common.ps1"

function ConvertFrom-HexUtf8 {
    param([Parameter(Mandatory=$true)][string]$Hex)

    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }

    [Text.Encoding]::UTF8.GetString($bytes)
}

function SqlString {
    param([Parameter(Mandatory=$true)][string]$Value)

    "'" + $Value.Replace("'", "''") + "'"
}

function Add-PercentEffect {
    param(
        [Parameter(Mandatory=$true)]$List,
        [Parameter(Mandatory=$true)][string]$ItemId,
        [Parameter(Mandatory=$true)][int]$Index,
        [Parameter(Mandatory=$true)][string]$Type,
        [Parameter(Mandatory=$true)][int]$Value,
        [Parameter(Mandatory=$true)][string]$Text
    )

    $List.Add([pscustomobject]@{
        ItemId = $ItemId
        Index = $Index
        Type = $Type
        Value = $Value
        Text = $Text
    }) | Out-Null
}

$config = Get-God2DatabaseConfig
$adminStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $adminStatus.hasSecret) {
    throw "Database administrator secret is unavailable: $($adminStatus.failureCode)"
}

$mariaDb = 'C:\Program Files\MariaDB 12.3\bin\mariadb.exe'
$env:MYSQL_PWD = [Environment]::GetEnvironmentVariable('GOD2_DB_ADMIN_PASSWORD', 'Process')
try {
    $query = @"
SELECT item_id, client_item_id, HEX(description_zh_tw) AS description_hex
FROM god2_game.item_registry
WHERE usable=1
  AND item_category='消耗品'
  AND item_family='消耗品'
  AND description_zh_tw REGEXP '(恢復|回覆|回復|可恢復|使用後恢復|點選.*恢復|使用.*恢復).*(HP|MP|生命|魔力|法力).*[0-9]+％|[0-9]+％.*(HP|MP|生命|魔力|法力)'
ORDER BY client_item_id;
"@

    $raw = & $mariaDb --host=$($config.host) --port=$($config.port) -uroot --ssl-verify-server-cert --default-character-set=utf8mb4 --connect-timeout=$($config.connectionTimeoutSeconds) --batch --raw --execute=$query
}
finally {
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable('GOD2_DB_ADMIN_PASSWORD', $null, 'Process')
}

$effects = [System.Collections.Generic.List[object]]::new()

foreach ($line in ($raw | Select-Object -Skip 1)) {
    $parts = $line -split "`t", 3
    if ($parts.Count -lt 3) {
        continue
    }

    $itemId = $parts[0]
    $text = ConvertFrom-HexUtf8 $parts[2]
    $hp = $null
    $mp = $null

    $setHp = {
        param([int]$Value)
        if ($null -eq $hp -or $Value -gt $hp) {
            Set-Variable -Name hp -Value $Value -Scope 1
        }
    }

    $setMp = {
        param([int]$Value)
        if ($null -eq $mp -or $Value -gt $mp) {
            Set-Variable -Name mp -Value $Value -Scope 1
        }
    }

    $bothPatterns = @(
        '恢復生命；魔力最大值的(?<n>\d+)％',
        '恢復(?<n>\d+)％的生命與魔力',
        '恢復HP與MP(?<n>\d+)％',
        '回覆生命與魔力(?<n>\d+)％',
        '恢復生命法力(?<n>\d+)％',
        '使用後恢復生命與魔力(?<n>\d+)％',
        '恢復生命、魔力(?<n>\d+)％'
    )

    foreach ($pattern in $bothPatterns) {
        foreach ($match in [regex]::Matches($text, $pattern)) {
            & $setHp ([int]$match.Groups['n'].Value)
            & $setMp ([int]$match.Groups['n'].Value)
        }
    }

    $pairPatterns = @(
        '使用可恢復生命(?<h>\d+)％、魔力(?<m>\d+)％',
        '點選兩下使用，恢復生命(?<h>\d+)％魔力(?<m>\d+)％',
        '點選兩下使用恢復生命(?<h>\d+)％；恢復魔力(?<m>\d+)％',
        '可恢復生命(?<h>\d+)％魔力(?<m>\d+)％',
        '使用回覆HP(?<h>\d+)％MP(?<m>\d+)％'
    )

    foreach ($pattern in $pairPatterns) {
        foreach ($match in [regex]::Matches($text, $pattern)) {
            & $setHp ([int]$match.Groups['h'].Value)
            & $setMp ([int]$match.Groups['m'].Value)
        }
    }

    $hpPatterns = @(
        '恢復生命最大值的(?<n>\d+)％',
        '恢復生命最大值(?<n>\d+)％',
        '恢復生命(?<n>\d+)％',
        '可恢復生命(?<n>\d+)％',
        '使用後恢復生命(?<n>\d+)％',
        '回覆(?<n>\d+)％HP',
        '可回覆(?<n>\d+)％HP',
        '恢復(?<n>\d+)％HP',
        '恢復HP(?<n>\d+)％',
        '點選使用恢復(?<n>\d+)％HP'
    )

    foreach ($pattern in $hpPatterns) {
        foreach ($match in [regex]::Matches($text, $pattern)) {
            & $setHp ([int]$match.Groups['n'].Value)
        }
    }

    $mpPatterns = @(
        '恢復魔力最大值的(?<n>\d+)％',
        '恢復魔力最大值(?<n>\d+)％',
        '恢復魔力(?<n>\d+)％',
        '可恢復魔力(?<n>\d+)％',
        '使用後恢復魔力(?<n>\d+)％',
        '回覆(?<n>\d+)％MP',
        '恢復(?<n>\d+)％MP',
        '恢復MP(?<n>\d+)％',
        '回覆魔力(?<n>\d+)％'
    )

    foreach ($pattern in $mpPatterns) {
        foreach ($match in [regex]::Matches($text, $pattern)) {
            & $setMp ([int]$match.Groups['n'].Value)
        }
    }

    if ($null -ne $hp) {
        Add-PercentEffect $effects $itemId 901 '生命百分比恢復' $hp "恢復生命 $hp%"
    }

    if ($null -ne $mp) {
        Add-PercentEffect $effects $itemId 902 '法力百分比恢復' $mp "恢復法力 $mp%"
    }
}

if ($effects.Count -eq 0) {
    throw 'No percent recovery rows were generated.'
}

$itemIds = $effects | Select-Object -ExpandProperty ItemId -Unique
$payloadSql = (($effects | ForEach-Object {
    "        SELECT $($_.ItemId) AS ``item_id``, $($_.Index) AS ``effect_index``, $(SqlString $_.Type) AS ``effect_type``, $($_.Value) AS ``numeric_value``, '世界與戰鬥' AS ``usage_scope``, $(SqlString $_.Text) AS ``effect_text_zh_tw``, 1 AS ``runtime_eligible``, 1 AS ``enabled``"
}) -join "`n        UNION ALL`n")

$idsSql = $itemIds -join ','
$migrationTemplate = @'
-- Schema 349
-- Game function: consumable item use restores HP/MP by percentage of maximum values.
-- Evidence source: Traditional Chinese client item descriptions that explicitly say use/click restores HP/MP percentage.
-- Scope note: this migration only enables instant use recovery. Carry effects, per-turn effects, and status cleansing remain disabled until separately verified.

ALTER TABLE `god2_game`.`item_effects`
    DROP CONSTRAINT IF EXISTS `ck_item_effects_type`;

ALTER TABLE `god2_game`.`item_effects`
    ADD CONSTRAINT `ck_item_effects_type` CHECK (`effect_type` in ('RestoreHp','RestoreMp','RestoreHpPercent','RestoreMpPercent','生命恢復','法力恢復','生命百分比恢復','法力百分比恢復'));

DELETE FROM `god2_game`.`item_effects`
WHERE `effect_type` IN ('生命百分比恢復','法力百分比恢復','RestoreHpPercent','RestoreMpPercent');

INSERT INTO `god2_game`.`item_effects`
    (`item_id`,`effect_index`,`effect_type`,`numeric_value`,`usage_scope`,`target_policy`,`effect_text_zh_tw`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`, payload.`effect_index`, payload.`effect_type`, payload.`numeric_value`, payload.`usage_scope`,
       CASE WHEN item_row.`use_on_other`=1 THEN '可對他人' ELSE '自身' END AS `target_policy`,
       payload.`effect_text_zh_tw`, payload.`runtime_eligible`, payload.`enabled`
FROM (
$payloadSql
) payload
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=payload.`item_id`
WHERE item_row.`item_category`='消耗品'
  AND item_row.`item_family`='消耗品'
  AND item_row.`usable`=1;

INSERT INTO `god2_game`.`item_usage_rules`
    (`item_id`,`normal_use`,`battle_use`,`equippable`,`use_on_other`,`hotkey_allowed`,`runtime_eligible`,`enabled`)
SELECT item_row.`item_id`,1,1,0,item_row.`use_on_other`,1,1,1
FROM `god2_game`.`item_registry` item_row
WHERE item_row.`item_id` IN ($idsSql)
ON DUPLICATE KEY UPDATE
    `normal_use`=1,
    `battle_use`=1,
    `runtime_eligible`=1,
    `enabled`=1;

CREATE OR REPLACE VIEW `god2_game`.`vw_percent_recovery_consumables_runtime_readable` AS
SELECT item_row.`client_item_id` AS `客戶端道具ID`,
       item_row.`code` AS `服務端代碼`,
       item_row.`name_zh_tw` AS `道具名稱`,
       effect_row.`effect_type` AS `效果類型`,
       effect_row.`numeric_value` AS `百分比`,
       effect_row.`effect_text_zh_tw` AS `效果文字`,
       rule_row.`normal_use` AS `平時可用`,
       rule_row.`battle_use` AS `戰鬥可用`,
       '文字證據：百分比使用恢復' AS `證據狀態`
FROM `god2_game`.`item_effects` effect_row
JOIN `god2_game`.`item_registry` item_row ON item_row.`item_id`=effect_row.`item_id`
LEFT JOIN `god2_game`.`item_usage_rules` rule_row ON rule_row.`item_id`=item_row.`item_id`
WHERE effect_row.`effect_type` IN ('生命百分比恢復','法力百分比恢復')
  AND effect_row.`runtime_eligible`=1
  AND effect_row.`enabled`=1;
'@

$migration = $migrationTemplate.Replace('$payloadSql', $payloadSql).Replace('$idsSql', $idsSql)

$path = Join-Path $PSScriptRoot '..\database\schema\349_activate_percent_recovery_consumable_items.sql'
Set-Content -Path $path -Value $migration -Encoding utf8

"generated_rows=$($effects.Count) generated_items=$($itemIds.Count) path=$path"
