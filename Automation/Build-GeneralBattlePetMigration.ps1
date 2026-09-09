param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$sourceUrl = "https://forum.gamer.com.tw/G2.php?bsn=8395&sn=1405"
$outputPath = Join-Path $repoRoot "Database\schema\091_publish_general_battle_pet_skills.sql"

$skillNames = @{
    '刀一'='獸之精  刀一  破星斬。瞬斬'; '刀二'='獸之精  刀二  破星斬。二連斬'; '刀三'='獸之精  刀三  破星斬。三連斬'
    '劍一'='獸之精  劍一  蒼穹訣。劍氣'; '劍二'='獸之精  劍二  蒼穹訣。十字劍氣'; '劍三'='獸之精  劍三  蒼穹訣。劍罡'
    '槍一'='獸之精  槍一  颶風槍。連突'; '槍二'='獸之精  槍二  颶風槍。百裂'; '槍三'='獸之精  槍三  颶風槍。幻虛'
    '杖一'='獸之精  杖一  破鎧擊。撤'; '杖二'='獸之精  杖二  破鎧擊。碾'; '杖三'='獸之精  杖三  破鎧擊。碎'
    '斧一'='獸之精  斧一  斧盾。守護一'; '斧二'='獸之精  斧二  斧盾。守護二'; '斧三'='獸之精  斧三  斧盾。守護三'
    '鞭一'='獸之精  鞭一  圓舞鞭。痕'; '鞭二'='獸之精  鞭二  圓舞鞭。卷'; '鞭三'='獸之精  鞭三  圓舞鞭。幻'
    '弓一'='獸之精  弓一  紅塵箭。星火'; '弓二'='獸之精  弓二  紅塵箭。浪濤'; '弓三'='獸之精  弓三  紅塵箭。爆焰'
    '飛刀一'='獸之精  飛刀一  暗器。疾影'; '飛刀二'='獸之精  飛刀二  暗器。迭影'; '飛刀三'='獸之精  飛刀三  暗器。鬼火'
    '金一'='獸之神  金一-天雷一'; '金二'='獸之神  金二-天雷二'; '金三'='獸之神  金三-金化身'
    '木一'='獸之神  木一-龍捲一'; '木二'='獸之神  木二-龍捲二'; '木三'='獸之神  木三-木化身'
    '水一'='獸之神  水一-冰晶一'; '水二'='獸之神  水二-冰晶二'; '水三'='獸之神  水三-水化身'
    '火一'='獸之神  火一-炎球一'; '火二'='獸之神  火二-炎球二'; '火三'='獸之神  火三-火化身'
    '毒一'='獸之氣  毒一-毒擊'; '毒二'='獸之氣  毒二-蠱'
    '眠一'='獸之氣  眠一-沉鄉'; '眠二'='獸之氣  眠二-靜夜'
    '亂一'='獸之氣  混亂一-失魂'; '亂二'='獸之氣  混亂二-離魄'
    '石一'='獸之氣  石化一-僵化'; '石二'='獸之氣  石化二-石磊'
    '封一'='獸之氣  神封一-封印'; '封二'='獸之氣  神封二-封禁'
    '力一'='獸之氣  力量一-夜叉'; '力二'='獸之氣  力量二-羅剎'
    '體一'='獸之氣  體力一-練精化氣'; '體二'='獸之氣  體力二-練氣化神'
    '智一'='獸之氣  智力一-擬識'; '智二'='獸之氣  智力二-天心'
    '速一'='獸之氣  速度一-迅雷'; '速二'='獸之氣  速度二-疾風'
    '毒解一'='獸之氣  中毒解除Lv一'; '毒解二'='獸之氣  中毒解除Lv二'
    '眠解一'='獸之氣  睡眠解除Lv一'; '眠解二'='獸之氣  睡眠解除Lv二'
    '亂解一'='獸之氣  混亂解除Lv一'; '亂解二'='獸之氣  混亂解除Lv二'
    '石解一'='獸之氣  石化解除Lv一'; '石解二'='獸之氣  石化解除Lv二'
    '封解一'='獸之氣  神封解除Lv一'; '封解二'='獸之氣  神封解除Lv二'
}

$groupStarts = @{
    '小蘑菇'=@(15,'菇類'); '綠蝸螺'=@(64,'蝸牛類'); '跳蚤'=@(14,'跳蚤類'); '大蒼蜂'=@(42,'蜂類')
    '大蜻蛉'=@(10,'蜻蜓類'); '棕熊'=@(43,'熊類'); '平原鯊豹'=@(3,'豹類'); '蒼蠅'=@(20,'眼蠅類')
    '幽魂'=@(36,'幽魂類'); '平原馬賊'=@(37,'馬賊類'); '小野兔'=@(44,'兔類'); '麵包花'=@(23,'花類')
    '雙眼草'=@(7,'草類'); '青犀獸'=@(32,'犀獸類'); '巨甲兵'=@(45,'甲兵類'); '怨靈妖'=@(46,'妖類')
    '巨鷹'=@(6,'鷹類'); '帝王蟹'=@(30,'螃蟹類'); '黑岩樹精'=@(38,'樹類'); '琵琶半妖'=@(35,'半妖類')
    '石怪'=@(47,'石怪類'); '黑皮怪'=@(8,'皮皮類'); '幽冥白狼'=@(48,'狼類'); '古代鯨龍'=@(49,'鯨龍類')
    '大蒼鯨'=@(50,'蒼鯨類'); '老米蟲'=@(29,'蟲類'); '大蝴蝶'=@(27,'蝴蝶類'); '尖耳怪'=@(51,'黑木怪類')
    '犄角虎'=@(25,'角虎類'); '雙頭猛虎'=@(18,'虎類'); '仙狐'=@(1,'狐類'); '野鴨'=@(52,'鴨類')
    '矮人'=@(31,'矮人類'); '巨型穿山甲'=@(22,'穿山甲類'); '肥水蛭'=@(21,'水蛭類'); '雲鶴'=@(9,'鶴類')
    '靈猿'=@(11,'猴類'); '青魅'=@(4,'火魅類'); '蚌精'=@(17,'蚌類'); '大頭鬼'=@($null,'鬼類')
    '百礦神'=@(53,'神類'); '惡靈師'=@(24,'法師類'); '浪人武士'=@(19,'武士類'); '噬血山叉'=@(2,'山叉類')
    '荒塚骷髏'=@(33,'骷髏類'); '化骨殭屍'=@(34,'殭屍類'); '魔法道士長'=@(41,'魔法道士')
    '板角青牛'=@(26,'板牛類'); '五色神牛'=@(54,'五色牛類'); '長腳獸'=@(57,'火足獸')
    '巨鍬仙'=@(13,'天牛類'); '天雷魔女'=@($null,'魔女類')
}
$withoutElement = @('跳蚤','蒼蠅','帝王蟹','黑岩樹精','矮人','肥水蛭')

function Convert-SkillName {
    param([AllowNull()] [string] $Abbreviation)

    if ([string]::IsNullOrWhiteSpace($Abbreviation) -or $Abbreviation.Trim() -eq '無') {
        return $null
    }
    $key = $Abbreviation.Trim().Replace('１','一').Replace('２','二').Replace('３','三').Replace('1','一').Replace('2','二').Replace('3','三')
    if (-not $skillNames.ContainsKey($key)) {
        throw "No official full battle-pet skill name is mapped for '$Abbreviation' (normalized '$key')."
    }
    return [string]$skillNames[$key]
}

function Escape-Sql {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return 'NULL' }
    return "'" + ([string]$Value).Replace("'", "''") + "'"
}

$html = (Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -TimeoutSec 30).Content
$match = [regex]::Match($html, '<div class="c-article__content">(?<body>.*?)</div>\s*</article>', 'Singleline')
if (-not $match.Success) { throw "The Bahamut article body could not be parsed." }
$lines = [regex]::Split($match.Groups['body'].Value, '<br\s*/?>', 'IgnoreCase') | ForEach-Object {
    [Net.WebUtility]::HtmlDecode([regex]::Replace($_, '<[^>]+>', '')).Trim()
}

$records = [Collections.Generic.List[object]]::new()
$currentCategoryId = $null
$currentFamily = $null
foreach ($line in $lines) {
    if ($line -match '●\s*太極境\s*●') { break }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }

    $name = $null
    $element = $null
    $skillText = $null
    if ($line -match '^(?<name>[^【]{1,30}?)\s*【(?<element>全|金|木|水|火|土|\s*)】\s*(?<skills>.+)$') {
        $name = $Matches.name.Trim()
        $element = if ([string]::IsNullOrWhiteSpace($Matches.element)) { '未知' } else { $Matches.element.Trim() }
        $skillText = $Matches.skills
    }
    elseif ($line -match ('^(?<name>' + (($withoutElement | ForEach-Object {[regex]::Escape($_)}) -join '|') + ')[　\s]{2,}(?<skills>.+)$')) {
        $name = $Matches.name.Trim()
        $element = '未知'
        $skillText = $Matches.skills
    }
    else {
        continue
    }

    if ($groupStarts.ContainsKey($name)) {
        $currentCategoryId = $groupStarts[$name][0]
        $currentFamily = $groupStarts[$name][1]
    }
    if ([string]::IsNullOrWhiteSpace([string]$currentFamily)) {
        throw "Pet '$name' appeared before a known family group."
    }

    $cleanSkills = [regex]::Replace($skillText, '【.*$', '').Trim()
    $parts = @($cleanSkills -split '[　\s]{2,}' | Where-Object {-not [string]::IsNullOrWhiteSpace($_)})
    if ($parts.Count -lt 1 -or $parts.Count -gt 3) {
        throw "Pet '$name' has an unsupported skill column count: $($parts.Count)."
    }
    while ($parts.Count -lt 3) { $parts += '無' }

    $records.Add([pscustomobject]@{
        Name = $name
        Element = $element
        CategoryId = $currentCategoryId
        Family = $currentFamily
        Level20 = Convert-SkillName $parts[0]
        Level40 = Convert-SkillName $parts[1]
        Level60 = Convert-SkillName $parts[2]
    })
}

if ($records.Count -ne 315) {
    throw "Expected 315 general battle-pet rows before the Tai Chi Realm section, but parsed $($records.Count)."
}

$sql = [Text.StringBuilder]::new()
[void]$sql.AppendLine('-- Generated from Bahamut God2 archive SN 1405. General battle pets only.')
[void]$sql.AppendLine('-- Tai Chi Realm, Jiuli, event, shop, package and other late-game sections are excluded.')
[void]$sql.AppendLine()
[void]$sql.AppendLine("DELETE FROM ``god2_game``.``pet_templates`` WHERE ``source_article_sn``=1405;")
[void]$sql.AppendLine()
[void]$sql.AppendLine('INSERT INTO `god2_game`.`pet_templates`')
[void]$sql.AppendLine('    (`pet_template_id`,`name_zh_tw`,`name_original`,`pet_family`,`pet_category_id`,`element_zh_tw`,`base_level`,')
[void]$sql.AppendLine('     `level_20_skill_name_zh_tw`,`level_40_skill_name_zh_tw`,`level_60_skill_name_zh_tw`,')
[void]$sql.AppendLine('     `source_section_zh_tw`,`source_article_sn`,`evidence_status`,`enabled`,`admin_note`)')
[void]$sql.AppendLine('VALUES')
for ($index = 0; $index -lt $records.Count; $index++) {
    $record = $records[$index]
    $values = @(
        ($index + 1), (Escape-Sql $record.Name), (Escape-Sql $record.Name), (Escape-Sql $record.Family),
        $(if ($null -eq $record.CategoryId) { 'NULL' } else { [string]$record.CategoryId }),
        (Escape-Sql $record.Element), 1,
        (Escape-Sql $record.Level20), (Escape-Sql $record.Level40), (Escape-Sql $record.Level60),
        (Escape-Sql '一般戰寵'), 1405, (Escape-Sql 'BahamutVerified'), 1,
        (Escape-Sql '20、40、60級技能關係來自巴哈精華區；技能全名以官方客戶端戰寵技能書資料展開。')
    )
    $suffix = if ($index -eq $records.Count - 1) { ';' } else { ',' }
    [void]$sql.AppendLine('    (' + ($values -join ',') + ')' + $suffix)
}

[IO.File]::WriteAllText($outputPath, $sql.ToString(), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    status = 'GENERAL_BATTLE_PET_MIGRATION_GENERATED'
    outputPath = $outputPath
    rowCount = $records.Count
    firstPet = $records[0].Name
    lastPet = $records[$records.Count - 1].Name
    excludedFrom = '太極境'
}
