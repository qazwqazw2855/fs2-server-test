param([string] $RepositoryRoot = "")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$outputPath = Join-Path $repoRoot "Database\schema\092_publish_pet_automatic_growth_allocations.sql"

$archetypeText = @'
1|力速均衡型|1,2,3|961
2|力智均衡型|4,5|961
3|體速均衡型|6|961
4|草類智速型|7|961
5|皮鶴蜻蜓智速型|8,9,10|961
6|猴牛熊力體型|11,12,13|961
7|跳蚤力體型|14|961
8|菇企鵝混合型|15,16|961
9|蚌類混合型|17|961
10|虎類混合型|18|961
11|武士混合型|19|961
12|眼蠅混合型|20|961
13|水蛭混合型|21|961
14|穿山甲混合型|22|961
15|花類混合型|23|961
16|法師混合型|24|961
17|角虎混合型|25|961
18|板牛混合型|26|961
19|蝴蝶混合型|27|961
20|火王蠍混合型|28|961
21|蟲類混合型|29|961
22|螃蟹混合型|30|961
23|矮人混合型|31|961
24|犀獸混合型|32|961
25|四維平均型|33,34,35,36,37,38,39,40,41|961
26|蜂類成長型|42|1400
27|熊類成長型|43|1400
28|兔類成長型|44|1400
29|甲兵成長型|45|1400
30|妖類成長型|46|1400
31|石怪成長型|47|1400
32|狼類成長型|48|1400
33|鯨龍成長型|49|1400
34|蒼鯨成長型|50|1400
35|黑木怪成長型|51|1400
36|鴨類成長型|52|1400
37|神類成長型|53|1400
38|五色牛成長型|54|1400
39|海豹成長型|55|1400
40|雪人成長型|56|1400
41|火足獸成長型|57|1400
42|焚熇戰牛成長型|58|1400
43|五行與轉樂魔寵型|59,60|1400
44|小年獸成長型|61|1400
45|瓢蟲成長型|62|1400
46|蜘蛛成長型|63|1400
47|蝸牛成長型|64|1400
'@

# archetype|grade (N/T/B/L)|1-19|20-49|50-99
$profileText = @'
1|N|2力2速|3力3速|4力4速
1|T|2力3速|3力3速|4力4速
1|B|2力2速|4力4速|5力5速
1|L|2力2速|3力3速|5力5速
2|N|2力2智|3力3智|4力4智
2|T|2力3智|3力3智|4力4智
2|B|2力2智|4力4智|5力5智
2|L|2力2智|3力3智|5力5智
3|N|2體2速|3體3速|4體4速
3|T|2體3速|3體3速|4體4速
3|B|2體2速|4體4速|5體5速
3|L|2體2速|3體3速|5體5速
4|N|2智2速|3智3速|4智4速
4|T|3智2速|3智3速|4智4速
4|B|2智2速|4智4速|5智5速
4|L|2智2速|3智3速|5智5速
5|N|2智2速|3智3速|4智4速
5|T|2智3速|3智3速|4智4速
5|B|2智2速|4智4速|5智5速
5|L|2智2速|3智3速|5智5速
6|N|2力2體|3力3體|4力4體
7|N|1力3體|2力4體|3力5體
7|T|1力4體|2力4體|3力5體
7|B|1力3體|3力5體|
7|L|1力3體|2力4體|
8|N|1力2智1體|1力3智2體|2力4智2體
8|T|1力3智1體|1力3智2體|2力4智2體
8|B|1力2智1體|2力4智2體|3力5智2體
8|L|1力2智1體|1力3智2體|3力5智2體
9|N|1力2智1體|1力3智1體1速|2力4智1體1速
9|T|1力3智1體|1力3智1體1速|2力4智1體1速
9|B|1力2智1體|2力4智1體1速|3力5智1體1速
9|L|1力2智1體|1力3智1體1速|3力5智1體1速
10|N|2力1智1速|3力2智1速|4力2智1體1速
10|T|3力1智1速|3力2智1速|4力2智1體1速
10|B|2力1智1速|4力2智1體1速|5力3智1體1速
10|L|2力1智1速|3力2智1速|5力3智1體1速
11|N|2力2智|3力2智1體|4力2智1體1速
11|T|3力2智|3力2智1體|4力2智1體1速
11|B|2力2智|4力2智1體1速|5力3智1體1速
11|L|2力2智|3力2智1體|5力3智1體1速
12|N|1力1體2速|2力2體2速|3力2體3速
12|T|1力2體2速|2力2體2速|3力2體3速
12|B|1力1體2速|3力2體3速|
12|L|1力1體2速|2力2體2速|
13|N|3體1速|1力4體1速|2力5體1速
13|T|4體1速|1力4體1速|2力5體1速
13|B|3體1速|2力5體1速|2力6體2速
13|L|3體1速|1力4體1速|2力6體2速
14|N|2力1體1速|3力1智1體1速|3力1智2體2速
14|T|2力1智1體1速|3力1智1體1速|3力1智2體2速
14|B|2力1體1速|3力1智2體2速|4力2智2體2速
14|L|2力1體1速|3力1智1體1速|4力2智2體2速
15|N|2智1體1速|3智2體1速|4智2體2速
15|T|3智1體1速|3智2體1速|4智2體2速
15|B|2智1體1速|4智2體2速|5智3體2速
15|L|2智1體1速|3智2體1速|5智3體2速
16|N|3智1速|4智1體1速|5智1體1力1速
16|T|4智1速|4智1體1速|5智1體1力1速
16|B|3智1速|5智1體1力1速|6智2體1力1速
16|L|3智1速|4智1體1速|6智2體1力1速
17|N|3力1速|4力2速|5力1體2速
17|T|4力1速|4力2速|5力1體2速
17|B|3力1速|5力1體2速|6力1體3速
17|L|3力1速|4力2速|6力1體3速
18|N|2力2體|3力3體|4力3體1速
18|T|3力2體|3力3體|4力3體1速
18|B|2力2體|4力3體1速|5力4體1速
18|L|2力2體|3力3體|5力5體
19|N|1智3速|1智1體4速|2智1體5速
19|T|1智4速|1智1體4速|2智1體5速
19|B|1智3速|2智1體5速|2智2體6速
19|L|1智3速|1智1體4速|2智2體6速
20|N|2力1體1速|3力2體1速|4力1智2體1速
20|T|3力1體1速|3力2體1速|4力1智2體1速
20|B|2力1體1速|4力2體1智1速|5力1智3體1速
20|L|2力1體1速|3力2體1速|5力1智3體1速
21|N|1力2體1速|1力3體2速|1力4體3速
21|T|1力3體1速|1力3體2速|1力4體3速
21|B|1力2體1速|1力4體3速|
21|L|1力2體1速|1力3體2速|
22|N|2力2速|2力1智1體2速|3力1智1體3速
22|T|3力2速|2力1智1體2速|3力1智1體3速
22|B|2力2速|3力1智1體3速|
22|L|2力2速|2力1智1體2速|
23|N|2力2體|2力1智2體1速|3力1智3體1速
23|T|2力2體1速|2力1智2體1速|3力1智3體1速
23|B|2力2體|3力1智3體1速|
23|L|2力2體|2力1智2體1速|
24|N|2體2速|1力1智2體2速|1力1智3體3速
24|T|1力2體2速|1力1智2體2速|1力1智3體3速
24|B|2體2速|1力1智3體3速|2力2智3體3速
24|L|2體2速|1力1智2體2速|2力2智3體3速
25|N|1力1智1體1速|2力2智1體1速|2力2智2體2速
25|T|1力2智1體1速|2力2智1體1速|2力2智2體2速
25|B|1力1智1體1速|2力2智2體2速|3力3智2體2速
25|L|1力1智1體1速|2力2智1體1速|3力3智2體2速
26|N|1力3速|1體1力4速|
26|T|1力4速|1體1力4速|
26|B|1力3速|1體2力5速|
26|L|1力3速|1體1力4速|
27|N|2體1力1智|3體2力1智|3體2力2智1速
27|T|3體1力1智|3體2力1智|3體2力2智1速
27|B|2體1力1智|3體2力2智1速|4體3力2智1速
27|L|2體1力1智|3體2力1智|4體3力2智1速
28|N|2智2速|1體1力2智2速|
28|T|1體2智2速|1體1力2智2速|
28|B|2智2速|1體1力2智2速|
28|L|2智2速|1體1力3智3速|
29|N|2體1力1智|3體2力1智|
29|T|3體1力1智|3體2力1智|
29|B|2體1力1智|4體2力2智|
29|L|2體1力1智|3體2力1智|
30|N|2力2速|1體3力2速|1體4力1智2速
30|T|3力2速|1體3力2速|1體4力1智2速
30|B|2力2速|1體4力1智2速|1體5力1智3速
30|L|2力2速|1體3力2速|1體5力1智3速
31|N|2體2力|3體3力|4體4力
31|T|2體3力|3體3力|4體4力
31|B|2體2力|4體4力|5體5力
31|L|2體2力|3體3力|5體5力
32|N|1體1力2智|1體2力3智|2體2力4智
32|T|1體1力3智|1體2力3智|2體2力4智
32|B|1體1力2智|2體2力4智|
32|L|1體1力2智|1體2力3智|
33|N|2體1力1速|3體2力1速|4體3力1速
33|T|3體1力1速|3體2力1速|4體3力1速
33|B|2體1力1速|4體3力1速|
33|L|2體1力1速|3體2力1速|
34|N|2體2速|3體1力2速|
34|T||3體1力2速|
34|B|2體2速|4體1力3速|
34|L|2體2速|3體1力2速|
35|N|1力2智1速|2力3智1速|
35|T|1力3智1速|2力3智1速|
35|B|1力2智1速||
35|L|1力2智1速|2力3智1速|
36|N|2體1智1速|3體1力1智1速|
36|T|3體1智1速|3體1力1智1速|
36|B|2體1智1速|3體1力2智2速|
36|L|2體1智1速|3體1力1智1速|
37|N|2體2力|2體3力1速|
37|T|2體3力|2體3力1速|
37|B|2體2力|2體4力1智1速|
37|L|2體2力|2體3力1速|
38|N|2體1力1速|3體1力1智1速|
38|T|2體1力1智1速|3體1力1智1速|
38|B|2體1力1速|4體2力1智1速|
38|L|2體1力1速|3體1力1智1速|
39|N|2體1力1速|3體2力1速|
39|T|3體1力1速|3體2力1速|
39|B|2體1力1速||
39|L|2體1力1速|3體2力1速|
40|N|2體2力|1體3力2速|
40|T|2體3力|1體3力2速|
40|B|2體2力|1體4力1智2速|1體5力1智3速
40|L|2體2力|1體3力2速|1體5力1智3速
41|N|2體2力|1體3力2速|1體4力1智2速
41|T|2體3力|1體3力2速|1體4力1智2速
41|B|2體2力|1體4力1智2速|1體5力1智3速
41|L|2體2力|1體3力2速|1體5力1智3速
42|N|1體2力1速|1體3力1智1速|2體3力1智2速
42|T|1體2力1速|1體3力1智1速|2體3力1智2速
42|B|1體2力1速||2體4力2智2速
42|L|1體2力1速|1體3力1智1速|2體4力2智2速
43|B|3智1速|1體4智3速|1體6智3速
44|N|2智2速|3智3速|4智4速
44|T|2智3速|3智3速|4智4速
44|B|2智2速|4智4速|5智5速
44|L|2智2速|3智3速|5智5速
45|N|2智2速|3智3速|
45|T|2智3速|3智3速|
45|B|2智2速|4智4速|
45|L|2智2速|3智3速|
46|N|2智2速|3智3速|
46|T|3智2速|3智3速|
46|B|2智2速|4智4速|5智5速
46|L|2智2速|3智3速|5智5速
47|N|2體1力1速|3體1力1智1速|
47|T|2體1力1智1速|3體1力1智1速|
47|B|2體1力1速|4體2力1智1速|4體2力2智2速
47|L|2體1力1速|3體1力1智1速|4體2力2智2速
'@

$gradeNames = @{ N = 'Normal'; T = 'Top'; B = 'Breakthrough'; L = 'LateBreakthrough' }
$bands = @(
    [pscustomobject]@{ Minimum = 1; Maximum = 19 },
    [pscustomobject]@{ Minimum = 20; Maximum = 49 },
    [pscustomobject]@{ Minimum = 50; Maximum = 99 }
)

function Escape-Sql([string] $Value) {
    if ($null -eq $Value) { return "NULL" }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Parse-Stats([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $stats = [ordered]@{ Constitution = 0; Strength = 0; Intelligence = 0; Speed = 0 }
    $matches = [regex]::Matches($Value, '(?<value>[0-9]+)(?<stat>體|力|智|速)')
    if ($matches.Count -eq 0) { throw "Invalid stat expression: $Value" }
    foreach ($match in $matches) {
        $amount = [int]$match.Groups['value'].Value
        switch ($match.Groups['stat'].Value) {
            '體' { $stats.Constitution += $amount }
            '力' { $stats.Strength += $amount }
            '智' { $stats.Intelligence += $amount }
            '速' { $stats.Speed += $amount }
        }
    }
    return [pscustomobject]$stats
}

function Expected-Total([string] $Grade, [int] $BandIndex) {
    $totals = @{
        N = @(4,6,8)
        T = @(5,6,8)
        B = @(4,8,10)
        L = @(4,6,10)
    }
    return $totals[$Grade][$BandIndex]
}

$archetypes = @()
$categoryMappings = @()
foreach ($line in ($archetypeText -split "`r?`n" | Where-Object { $_ })) {
    $parts = $line.Split('|')
    $archetypes += [pscustomobject]@{ Id = [int]$parts[0]; Name = $parts[1]; Source = [int]$parts[3] }
    foreach ($categoryId in $parts[2].Split(',')) {
        $categoryMappings += [pscustomobject]@{ CategoryId = [int]$categoryId; ArchetypeId = [int]$parts[0] }
    }
}

$profiles = @{}
foreach ($line in ($profileText -split "`r?`n" | Where-Object { $_ })) {
    $parts = $line.Split('|')
    $profiles["$($parts[0]):$($parts[1])"] = @($parts[2],$parts[3],$parts[4])
}

$archetypeRows = $archetypes | ForEach-Object {
    "    ($($_.Id),$(Escape-Sql $_.Name),$($_.Source),'BahamutPublished',1)"
}
$allocationRows = [Collections.Generic.List[string]]::new()
foreach ($archetype in $archetypes) {
    foreach ($gradeCode in @('N','T','B','L')) {
        $key = "$($archetype.Id):$gradeCode"
        $values = if ($profiles.ContainsKey($key)) { $profiles[$key] } else { @('','','') }
        for ($bandIndex = 0; $bandIndex -lt 3; $bandIndex++) {
            $sourceText = $values[$bandIndex]
            $stats = Parse-Stats $sourceText
            $expected = Expected-Total $gradeCode $bandIndex
            $total = if ($null -eq $stats) { $null } else { $stats.Constitution + $stats.Strength + $stats.Intelligence + $stats.Speed }
            $status = if ($null -eq $stats) { 'SourceMissing' } elseif ($total -eq $expected) { 'BahamutVerified' } else { 'SourceConflict' }
            $enabled = if ($status -eq 'BahamutVerified') { 1 } else { 0 }
            $fields = if ($null -eq $stats) { @('NULL','NULL','NULL','NULL','NULL') } else {
                @($stats.Constitution,$stats.Strength,$stats.Intelligence,$stats.Speed,$total)
            }
            $band = $bands[$bandIndex]
            $allocationRows.Add("    ($($archetype.Id),$(Escape-Sql $gradeNames[$gradeCode]),$($band.Minimum),$($band.Maximum),$($fields -join ','),$expected,$(Escape-Sql $sourceText),$($archetype.Source),$(Escape-Sql $status),$enabled)")
        }
    }
}

$categoryCases = $categoryMappings | Sort-Object CategoryId | ForEach-Object {
    "        WHEN $($_.CategoryId) THEN $($_.ArchetypeId)"
}

$sql = @"
-- Generated from Bahamut God2 archive SN 961 and 1400.
-- Values are automatic per-level allocations. Missing or conflicting source cells remain disabled.

CREATE TABLE IF NOT EXISTS ``god2_game``.``pet_growth_archetypes`` (
    ``growth_archetype_id`` smallint NOT NULL COMMENT '連號成長原型ID',
    ``name_zh_tw`` varchar(100) NOT NULL COMMENT '成長原型繁體中文名稱',
    ``source_article_sn`` int NOT NULL COMMENT '巴哈姆特精華區文章SN',
    ``evidence_status`` varchar(30) NOT NULL COMMENT '資料證據狀態',
    ``enabled`` tinyint(1) NOT NULL DEFAULT 1 COMMENT '服務端是否啟用',
    PRIMARY KEY (``growth_archetype_id``),
    UNIQUE KEY ``ux_pet_growth_archetypes_name`` (``name_zh_tw``),
    CONSTRAINT ``ck_pet_growth_archetypes_source`` CHECK (``source_article_sn`` IN (961,1400)),
    CONSTRAINT ``ck_pet_growth_archetypes_enabled`` CHECK (``enabled`` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵族群自動配點成長原型';

ALTER TABLE ``god2_game``.``pet_categories``
    ADD COLUMN IF NOT EXISTS ``growth_archetype_id`` smallint NULL COMMENT '自動配點成長原型ID' AFTER ``growth_pattern``;

UPDATE ``god2_game``.``pet_templates``
SET ``base_constitution``=100,``base_strength``=50,``base_intelligence``=50,``base_speed``=100;

ALTER TABLE ``god2_game``.``pet_templates``
    MODIFY COLUMN ``base_constitution`` int NOT NULL DEFAULT 100 COMMENT '戰寵固定基礎體力',
    MODIFY COLUMN ``base_strength`` int NOT NULL DEFAULT 50 COMMENT '戰寵固定基礎力量',
    MODIFY COLUMN ``base_intelligence`` int NOT NULL DEFAULT 50 COMMENT '戰寵固定基礎智力',
    MODIFY COLUMN ``base_speed`` int NOT NULL DEFAULT 100 COMMENT '戰寵固定基礎速度',
    DROP CONSTRAINT IF EXISTS ``ck_pet_templates_fixed_base_stats``,
    ADD CONSTRAINT ``ck_pet_templates_fixed_base_stats`` CHECK (
        ``base_constitution``=100 AND ``base_strength``=50 AND ``base_intelligence``=50 AND ``base_speed``=100
    );

DROP TABLE IF EXISTS ``god2_game``.``pet_automatic_growth_allocations``;
ALTER TABLE ``god2_game``.``pet_categories``
    DROP FOREIGN KEY IF EXISTS ``fk_pet_categories_growth_archetype``;

DELETE FROM ``god2_game``.``pet_growth_archetypes``;
INSERT INTO ``god2_game``.``pet_growth_archetypes``
    (``growth_archetype_id``,``name_zh_tw``,``source_article_sn``,``evidence_status``,``enabled``)
VALUES
$($archetypeRows -join ",`r`n");

UPDATE ``god2_game``.``pet_categories``
SET ``growth_archetype_id``=CASE ``category_id``
$($categoryCases -join "`r`n")
        ELSE NULL
    END;

ALTER TABLE ``god2_game``.``pet_categories``
    DROP FOREIGN KEY IF EXISTS ``fk_pet_categories_growth_archetype``,
    ADD CONSTRAINT ``fk_pet_categories_growth_archetype`` FOREIGN KEY (``growth_archetype_id``)
        REFERENCES ``god2_game``.``pet_growth_archetypes`` (``growth_archetype_id``);

CREATE TABLE IF NOT EXISTS ``god2_game``.``pet_automatic_growth_allocations`` (
    ``growth_archetype_id`` smallint NOT NULL COMMENT '成長原型ID',
    ``growth_grade`` varchar(30) NOT NULL COMMENT 'Normal普通、Top頂級、Breakthrough破頂、LateBreakthrough晚破',
    ``minimum_level`` int NOT NULL COMMENT '起始等級',
    ``maximum_level`` int NOT NULL COMMENT '結束等級',
    ``constitution_delta`` int NULL COMMENT '每級自動體力',
    ``strength_delta`` int NULL COMMENT '每級自動力量',
    ``intelligence_delta`` int NULL COMMENT '每級自動智力',
    ``speed_delta`` int NULL COMMENT '每級自動速度',
    ``published_total`` int NULL COMMENT '原文四維合計',
    ``expected_total`` int NOT NULL COMMENT '品級與階段應有總點數',
    ``source_value_zh_tw`` varchar(100) NULL COMMENT '巴哈姆特原文配點',
    ``source_article_sn`` int NOT NULL COMMENT '巴哈姆特精華區文章SN',
    ``evidence_status`` varchar(30) NOT NULL COMMENT 'BahamutVerified、SourceMissing或SourceConflict',
    ``enabled`` tinyint(1) NOT NULL DEFAULT 0 COMMENT '僅已驗證且總點數相符者可供服務端使用',
    PRIMARY KEY (``growth_archetype_id``,``growth_grade``,``minimum_level``),
    CONSTRAINT ``fk_pet_auto_growth_archetype`` FOREIGN KEY (``growth_archetype_id``)
        REFERENCES ``god2_game``.``pet_growth_archetypes`` (``growth_archetype_id``),
    CONSTRAINT ``fk_pet_auto_growth_grade_band`` FOREIGN KEY (``growth_grade``,``minimum_level``)
        REFERENCES ``god2_game``.``pet_growth_grade_rules`` (``growth_grade``,``minimum_level``),
    CONSTRAINT ``ck_pet_auto_growth_levels`` CHECK (``maximum_level`` >= ``minimum_level``),
    CONSTRAINT ``ck_pet_auto_growth_values`` CHECK (
        (``evidence_status``='SourceMissing' AND ``constitution_delta`` IS NULL AND ``strength_delta`` IS NULL
            AND ``intelligence_delta`` IS NULL AND ``speed_delta`` IS NULL AND ``published_total`` IS NULL AND ``enabled``=0)
        OR (``evidence_status``='SourceConflict' AND ``published_total`` IS NOT NULL AND ``enabled``=0)
        OR (``evidence_status``='BahamutVerified' AND ``published_total``=``expected_total`` AND ``enabled``=1)
    ),
    CONSTRAINT ``ck_pet_auto_growth_enabled`` CHECK (``enabled`` IN (0,1))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='戰寵各族群品級分段自動配點';

DELETE FROM ``god2_game``.``pet_automatic_growth_allocations``;
INSERT INTO ``god2_game``.``pet_automatic_growth_allocations``
    (``growth_archetype_id``,``growth_grade``,``minimum_level``,``maximum_level``,
     ``constitution_delta``,``strength_delta``,``intelligence_delta``,``speed_delta``,
     ``published_total``,``expected_total``,``source_value_zh_tw``,``source_article_sn``,``evidence_status``,``enabled``)
VALUES
$($allocationRows -join ",`r`n");

CREATE OR REPLACE VIEW ``god2_game``.``vw_pet_category_growth_values_readable`` AS
SELECT category_row.``category_id`` AS ``類別ID``,category_row.``name_zh_tw`` AS ``戰寵族群``,
       archetype_row.``name_zh_tw`` AS ``成長原型``,
       CASE allocation.``growth_grade`` WHEN 'Normal' THEN '普通' WHEN 'Top' THEN '頂級'
            WHEN 'Breakthrough' THEN '破頂' ELSE '晚破' END AS ``戰寵品級``,
       allocation.``minimum_level`` AS ``起始等級``,allocation.``maximum_level`` AS ``結束等級``,
       allocation.``constitution_delta`` AS ``每級自動體力``,allocation.``strength_delta`` AS ``每級自動力量``,
       allocation.``intelligence_delta`` AS ``每級自動智力``,allocation.``speed_delta`` AS ``每級自動速度``,
       allocation.``published_total`` AS ``原文合計``,allocation.``expected_total`` AS ``應有合計``,
       allocation.``source_value_zh_tw`` AS ``巴哈原文配點``,
       CASE allocation.``evidence_status`` WHEN 'BahamutVerified' THEN '已核對可實裝'
            WHEN 'SourceConflict' THEN '原文總點數衝突，禁止使用' ELSE '原文缺值，等待補證' END AS ``資料狀態``,
       CONCAT('巴哈姆特封神2精華區 SN ',allocation.``source_article_sn``) AS ``資料來源``
FROM ``god2_game``.``pet_categories`` category_row
JOIN ``god2_game``.``pet_growth_archetypes`` archetype_row
  ON archetype_row.``growth_archetype_id``=category_row.``growth_archetype_id``
JOIN ``god2_game``.``pet_automatic_growth_allocations`` allocation
  ON allocation.``growth_archetype_id``=archetype_row.``growth_archetype_id``
ORDER BY category_row.``category_id``,FIELD(allocation.``growth_grade``,'Normal','Top','Breakthrough','LateBreakthrough'),allocation.``minimum_level``;
"@

[IO.File]::WriteAllText($outputPath, $sql, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    status = "PET_AUTOMATIC_GROWTH_MIGRATION_GENERATED"
    output = $outputPath
    archetypes = $archetypes.Count
    categories = $categoryMappings.Count
    allocationRows = $allocationRows.Count
} | ConvertTo-Json
