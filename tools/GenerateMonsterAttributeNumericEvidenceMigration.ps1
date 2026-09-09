param(
    [Parameter(Mandatory = $true)]
    [string] $ArchivePath,

    [string] $OutputPath = "database/schema/347_import_17173_monster_attribute_numeric_evidence.sql"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$work = Join-Path $env:TEMP 'god2_monster_evidence_csv'
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work | Out-Null

$zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    foreach ($entryName in @(
        'payload/evidence/monster-attributes/17173-monster-table-normalized.csv',
        'payload/evidence/monster-attributes/17173-vitals-cross-check.csv')) {
        $entry = $zip.Entries | Where-Object FullName -eq $entryName | Select-Object -First 1
        if (-not $entry) {
            throw "Missing ZIP entry: $entryName"
        }

        [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
            $entry,
            (Join-Path $work ([IO.Path]::GetFileName($entryName))),
            $true)
    }
}
finally {
    $zip.Dispose()
}

$rows = Import-Csv -LiteralPath (Join-Path $work '17173-monster-table-normalized.csv')
$cross = Import-Csv -LiteralPath (Join-Path $work '17173-vitals-cross-check.csv')
$crossByKey = @{}
foreach ($candidate in $cross) {
    $key = $candidate.source_name_utf8 + "|$($candidate.source_level)|$($candidate.reference_hp)|$($candidate.reference_mp)"
    $crossByKey[$key] = $candidate
}

function SqlText([string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) {
        return 'NULL'
    }

    return "'" + $Value.Replace('\', '\\').Replace("'", "''") + "'"
}

function SqlNum($Value) {
    if ([string]::IsNullOrWhiteSpace([string] $Value)) {
        return 'NULL'
    }

    return [string] $Value
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('-- Imports external numeric monster HP/MP and combat attribute evidence from the 17173 evidence pack.')
$lines.Add('-- Game function: monster MP/HP and combat attribute black-box calibration evidence.')
$lines.Add('-- Source Chinese names/maps are intentionally not stored because the source text is Simplified Chinese.')
$lines.Add('CREATE TABLE IF NOT EXISTS `god2_game_meta`.`monster_attribute_external_numeric_evidence` (')
$lines.Add('  `evidence_id` bigint NOT NULL AUTO_INCREMENT,')
$lines.Add('  `source_row_number` int NOT NULL,')
$lines.Add('  `source_page` int NULL,')
$lines.Add('  `source_level` int NULL,')
$lines.Add('  `reference_hp` int NULL,')
$lines.Add('  `reference_mp` int NULL,')
$lines.Add('  `physical_attack` int NULL,')
$lines.Add('  `magic_attack` int NULL,')
$lines.Add('  `physical_defense` int NULL,')
$lines.Add('  `magic_defense` int NULL,')
$lines.Add('  `base_exp` int NULL,')
$lines.Add('  `strength` int NULL,')
$lines.Add('  `constitution` int NULL,')
$lines.Add('  `intelligence` int NULL,')
$lines.Add('  `speed` int NULL,')
$lines.Add('  `metal` int NULL,')
$lines.Add('  `wood` int NULL,')
$lines.Add('  `water` int NULL,')
$lines.Add('  `fire` int NULL,')
$lines.Add('  `earth` int NULL,')
$lines.Add('  `source_url` varchar(500) NOT NULL,')
$lines.Add('  `source_sha256` char(64) NOT NULL,')
$lines.Add('  `client_name_big5_hex` varchar(64) NULL,')
$lines.Add('  `candidate_enyname_keys` varchar(500) NULL,')
$lines.Add('  `identity_join_basis` varchar(128) NULL,')
$lines.Add('  `comparison_status` varchar(128) NOT NULL,')
$lines.Add('  `authority_note` varchar(300) NOT NULL,')
$lines.Add('  `imported_at_utc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),')
$lines.Add('  PRIMARY KEY (`evidence_id`),')
$lines.Add('  KEY `ix_monster_attribute_external_numeric_evidence_level` (`source_level`),')
$lines.Add('  KEY `ix_monster_attribute_external_numeric_evidence_candidate` (`candidate_enyname_keys`(120))')
$lines.Add(') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;')
$lines.Add('TRUNCATE TABLE `god2_game_meta`.`monster_attribute_external_numeric_evidence`;')
$lines.Add('INSERT INTO `god2_game_meta`.`monster_attribute_external_numeric_evidence` (`source_row_number`,`source_page`,`source_level`,`reference_hp`,`reference_mp`,`physical_attack`,`magic_attack`,`physical_defense`,`magic_defense`,`base_exp`,`strength`,`constitution`,`intelligence`,`speed`,`metal`,`wood`,`water`,`fire`,`earth`,`source_url`,`source_sha256`,`client_name_big5_hex`,`candidate_enyname_keys`,`identity_join_basis`,`comparison_status`,`authority_note`) VALUES')

$values = [System.Collections.Generic.List[string]]::new()
$index = 0
foreach ($row in $rows) {
    $index++
    $key = $row.name + "|$($row.level)|$($row.hp)|$($row.mp)"
    $candidate = $crossByKey[$key]
    $status = if ($null -eq $candidate) { 'external_public_source_only' } else { $candidate.comparison_status }
    $valueParts = @(
        $index,
        (SqlNum $row.page),
        (SqlNum $row.level),
        (SqlNum $row.hp),
        (SqlNum $row.mp),
        (SqlNum $row.physical_attack),
        (SqlNum $row.magic_attack),
        (SqlNum $row.physical_defense),
        (SqlNum $row.magic_defense),
        (SqlNum $row.base_exp),
        (SqlNum $row.strength),
        (SqlNum $row.vitality),
        (SqlNum $row.intelligence),
        (SqlNum $row.speed),
        (SqlNum $row.metal),
        (SqlNum $row.wood),
        (SqlNum $row.water),
        (SqlNum $row.fire),
        (SqlNum $row.earth),
        (SqlText $row.source_url),
        (SqlText $row.source_sha256),
        (SqlText $(if ($null -eq $candidate) { '' } else { $candidate.client_name_big5_hex })),
        (SqlText $(if ($null -eq $candidate) { '' } else { $candidate.candidate_enyname_keys })),
        (SqlText $(if ($null -eq $candidate) { '' } else { $candidate.identity_join_basis })),
        (SqlText $status),
        (SqlText 'External public monster attribute evidence; provisional calibration only; not official server capture.')
    )
    $values.Add('(' + ($valueParts -join ',') + ')')
}

$lines.Add(($values -join ",`n") + ';')
$lines.Add('CREATE OR REPLACE VIEW `god2_game_meta`.`vw_monster_attribute_external_numeric_evidence_readable` AS')
$lines.Add('SELECT `source_row_number` AS `source_row_number`, `source_page` AS `source_page`, `source_level` AS `level`, `reference_hp` AS `external_hp`, `reference_mp` AS `external_mp`, `physical_attack` AS `external_physical_attack`, `magic_attack` AS `external_magic_attack`, `physical_defense` AS `external_physical_defense`, `magic_defense` AS `external_magic_defense`, `base_exp` AS `external_exp`, `strength` AS `strength`, `constitution` AS `constitution`, `intelligence` AS `intelligence`, `speed` AS `speed`, `metal` AS `metal`, `wood` AS `wood`, `water` AS `water`, `fire` AS `fire`, `earth` AS `earth`, `candidate_enyname_keys` AS `client_candidate_keys`, `identity_join_basis` AS `identity_join_basis`, `comparison_status` AS `verification_status`, `authority_note` AS `authority_boundary` FROM `god2_game_meta`.`monster_attribute_external_numeric_evidence`;')

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
Write-Output "GENERATED`t$OutputPath`tROWS`t$($rows.Count)"
