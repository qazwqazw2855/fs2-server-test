param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $MonsterNameZhTw
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$captureRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA "God2Classic\PacketCapture\Sessions\Codex"))
$sessionRoot = [IO.Path]::GetFullPath((Join-Path $captureRoot $SessionId))
$capturePrefix = $captureRoot.TrimEnd('\') + '\'
$statePath = Join-Path $repoRoot "Automation\State\packet-capture-active.json"
$secretPath = Join-Path $repoRoot "Automation\State\db-admin-secret.bin"
$mariaDb = "C:\Program Files\MariaDB 12.3\bin\mariadb.exe"

if (-not $sessionRoot.StartsWith($capturePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Session path escaped the approved capture root."
}
if (-not (Test-Path -LiteralPath $sessionRoot -PathType Container)) {
    throw "Capture session does not exist: $SessionId"
}
if (-not (Test-Path -LiteralPath $mariaDb -PathType Leaf)) {
    throw "MariaDB client was not found: $mariaDb"
}
if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
    throw "The DPAPI-protected database administrator secret is missing."
}
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $captureState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([bool]$captureState.active) {
        throw "Capture is still active; stop and strictly unload it before committing evidence."
    }
}

$captureStatusPath = Join-Path $sessionRoot "reports\headless-enhanced-capture.json"
$captureStatus = Get-Content -LiteralPath $captureStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$captureStatus.Status -cne "DETACHED" -or
    -not [bool]$captureStatus.StrictUnloadVerified) {
    throw "Strict capture unload was not verified."
}

$packetPath = Join-Path $sessionRoot "raw\injected-packets.jsonl"
if (-not (Test-Path -LiteralPath $packetPath -PathType Leaf)) {
    throw "Injected packet evidence is missing."
}

function Convert-HexBytes {
    param([Parameter(Mandatory = $true)] [string] $Hex)

    if (($Hex.Length % 2) -ne 0 -or $Hex -notmatch '^[0-9A-Fa-f]+$') {
        throw "A captured payload is not valid hexadecimal."
    }
    $bytes = [byte[]]::new($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

function Get-U16 {
    param([byte[]] $Bytes, [int] $Offset)
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Get-I16 {
    param([byte[]] $Bytes, [int] $Offset)
    return [BitConverter]::ToInt16($Bytes, $Offset)
}

function Get-I32 {
    param([byte[]] $Bytes, [int] $Offset)
    return [BitConverter]::ToInt32($Bytes, $Offset)
}

$records = [Collections.Generic.List[object]]::new()
Get-Content -LiteralPath $packetPath -Encoding UTF8 | ForEach-Object {
    if (-not [string]::IsNullOrWhiteSpace($_)) {
        $records.Add(($_ | ConvertFrom-Json))
    }
}

$postDecrypt = @($records | Where-Object { [string]$_.Api -ceq "PostDecrypt" })
$handler = @($records | Where-Object { [string]$_.Api -ceq "HandlerDecoded" })
$preEncrypt = @($records | Where-Object { [string]$_.Api -ceq "PreEncrypt" })

$battleEntry = @($postDecrypt | Where-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    $bytes.Length -ge 4 -and $bytes[2] -eq 0x82
})
$settlements = @($postDecrypt | Where-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    $bytes.Length -ge 4 -and $bytes[2] -eq 0x89
})
if ($battleEntry.Count -ne 1) {
    throw "Expected exactly one battle-entry 0x82 frame; observed $($battleEntry.Count)."
}
if ($settlements.Count -ne 1) {
    throw "Expected exactly one settlement 0x89 frame; observed $($settlements.Count)."
}

$commands = @($preEncrypt | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 20 -and $bytes[2] -eq 0x35) {
        [pscustomobject]@{
            record = $_
            bytes = $bytes
            position = [int]$bytes[3]
            action = [int]($bytes[4] -band 0x7F)
        }
    }
})
$playerPositions = @($commands | ForEach-Object { $_.position } | Sort-Object -Unique)
if ($playerPositions.Count -ne 1) {
    throw "The battle command stream does not identify one unique player position."
}
$playerPosition = [int]$playerPositions[0]

$actors = @($handler | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 45 -and $bytes[0] -eq 0x1C) {
        [pscustomobject]@{
            record = $_
            bytes = $bytes
            position = [int]$bytes[1]
            level = [int]$bytes[2]
            encounterLocalId = [int]$bytes[7]
        }
    }
})
$enemyActors = @($actors | Where-Object { $_.position -ne $playerPosition })
if ($enemyActors.Count -ne 1) {
    throw "Expected exactly one non-player battle actor; observed $($enemyActors.Count)."
}
$enemy = $enemyActors[0]
if ($enemy.level -le 0 -or $enemy.encounterLocalId -le 0) {
    throw "The enemy descriptor does not contain a valid level and encounter-local ID."
}

$basicAttacks = @($commands | Where-Object { $_.action -eq 1 })
if ($basicAttacks.Count -le 0) {
    throw "No controlled basic attack was captured."
}

$damageEffects = @($handler | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 15 -and $bytes[0] -eq 0x83 -and
        $bytes[1] -eq 1 -and $bytes[2] -eq $playerPosition) {
        $delta = Get-I16 $bytes 9
        if ($delta -lt 0) {
            [pscustomobject]@{
                record = $_
                delta = [int]$delta
                outcomeFlags = [BitConverter]::ToUInt32($bytes, 11)
            }
        }
    }
})
if ($damageEffects.Count -le 0) {
    throw "No player-source negative battle effect was captured."
}
$observedDamageUpperBound = [int64](
    ($damageEffects | Measure-Object -Property delta -Sum).Sum * -1)

$settlementBytes = Convert-HexBytes ([string]$settlements[0].PayloadHex)
if ($settlementBytes.Length -lt 16) {
    throw "Settlement frame is too short."
}
$experienceBase = Get-I32 $settlementBytes 7
$experienceCredited = Get-I32 $settlementBytes 11
if ($experienceBase -lt 0 -or $experienceBase -ne $experienceCredited) {
    throw "Settlement EXP fields are invalid or disagree."
}

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
            --host 127.0.0.1 `
            --port 3306 `
            --user root `
            --connect-timeout 2 `
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
        throw "MariaDB operation failed: $($output -join [Environment]::NewLine)"
    }
    return @($output | Where-Object {
        $_ -is [string] -and -not $_.StartsWith("WARNING:", [StringComparison]::OrdinalIgnoreCase)
    })
}

function Escape-SqlLiteral {
    param([Parameter(Mandatory = $true)] [string] $Value)
    return $Value.Replace("'", "''")
}

try {
    $secret = ([Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json)
    $env:MYSQL_PWD = [string]$secret.password
    $escapedName = Escape-SqlLiteral $MonsterNameZhTw

    $identityRows = @(Invoke-MariaDbQuery -Sql @"
SELECT JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.categoryOrModelCandidates[2]')),
       JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.sourceRow')),
       s.AuthorityKey
FROM god2.content_staging_records s
WHERE s.Domain='EncounterName'
  AND s.ConvertedText='$escapedName'
  AND JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.encounterLocalId'))='$($enemy.encounterLocalId)'
  AND s.RunId=(
      SELECT RunId FROM god2.content_recovery_runs
      WHERE Phase='GameplayContentRecoveryPhase1'
        AND CompletedAtUtc IS NOT NULL
        AND Status<>'RUNNING'
      ORDER BY CompletedAtUtc DESC LIMIT 1)
ORDER BY CAST(JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.sourceRow')) AS UNSIGNED);
"@)
    if ($identityRows.Count -ne 1) {
        throw "Official encounter identity is not unique for the captured local ID and name."
    }
    $identity = $identityRows[0] -split "`t"
    if ($identity.Count -ne 3) {
        throw "Official encounter identity returned an unexpected shape."
    }
    $clientMonsterId = [int]$identity[0]
    $sourceRow = [int]$identity[1]
    $authorityKey = [string]$identity[2]

    $serverRows = @(Invoke-MariaDbQuery -Sql @"
SELECT `monster_id`
FROM god2_game.monsters
WHERE `name_zh_tw`='$escapedName';
"@)
    if ($serverRows.Count -ne 1) {
        throw "The formal monster identity is not unique."
    }
    $serverMonsterId = [int]$serverRows[0]

    $entryFrameId = [string]$battleEntry[0].SourceFrameId
    $actorFrameId = [string]$enemy.record.SourceFrameId
    $settlementFrameId = [string]$settlements[0].SourceFrameId
    $damageFrameIds = @($damageEffects | ForEach-Object { [string]$_.record.SourceFrameId })
    $capturedAt = [DateTimeOffset]::FromUnixTimeMilliseconds([int64]$settlements[0].wallUnixMs).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff")
    $sessionEscaped = Escape-SqlLiteral $SessionId
    $authorityEscaped = Escape-SqlLiteral $authorityKey

    Invoke-MariaDbQuery -Sql @"
CREATE TABLE IF NOT EXISTS god2.verified_monster_observations (
    monster_id int NOT NULL,
    name_zh_tw varchar(256) NOT NULL,
    client_monster_id int NOT NULL,
    encounter_local_id int NOT NULL,
    level int NOT NULL,
    maximum_hp bigint NULL,
    experience_reward bigint NULL,
    source_session_id varchar(128) NOT NULL,
    official_authority_key varchar(191) NOT NULL,
    official_source_row int NOT NULL,
    battle_entry_frame varchar(128) NOT NULL,
    battle_actor_frame varchar(128) NOT NULL,
    battle_settlement_frame varchar(128) NOT NULL,
    evidence_status varchar(64) NOT NULL,
    observed_at_utc datetime(6) NOT NULL,
    PRIMARY KEY (monster_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
  COMMENT='封包驗證怪物數值證據；不屬於正式遊戲目錄';
START TRANSACTION;
DELETE FROM god2.verified_monster_observations WHERE monster_id=$serverMonsterId;
INSERT INTO god2.verified_monster_observations
    (monster_id,name_zh_tw,client_monster_id,encounter_local_id,level,maximum_hp,
     experience_reward,source_session_id,official_authority_key,official_source_row,
     battle_entry_frame,battle_actor_frame,battle_settlement_frame,evidence_status,observed_at_utc)
VALUES
    ($serverMonsterId,'$escapedName',$clientMonsterId,$($enemy.encounterLocalId),$($enemy.level),NULL,
     $experienceBase,'$sessionEscaped','$authorityEscaped',$sourceRow,
     '$(Escape-SqlLiteral $entryFrameId)','$(Escape-SqlLiteral $actorFrameId)',
     '$(Escape-SqlLiteral $settlementFrameId)','VERIFIED_LEVEL_AND_SINGLE_MONSTER_EXP','$capturedAt');
SET @god2_sync_mode=1;
UPDATE god2_game.monsters
SET level=$($enemy.level), experience_reward=$experienceBase,
    evidence_status='Verified', enabled=0,
    admin_note='單怪戰鬥已驗證等級與經驗；傷害總和包含末擊溢出可能，不能作為最大 HP。其餘屬性完成前停用。'
WHERE monster_id=$serverMonsterId AND name_zh_tw='$escapedName';
SET @god2_sync_mode=0;
COMMIT;
"@ | Out-Null

    $postCheck = @(Invoke-MariaDbQuery -Sql @"
SELECT g.monster_id,g.name_zh_tw,g.level,g.max_hp,g.experience_reward,
       g.evidence_status,g.enabled,
       (SELECT COUNT(*) FROM god2.verified_monster_observations v
        WHERE v.monster_id=g.monster_id AND v.source_session_id='$sessionEscaped')
FROM god2_game.monsters g
WHERE g.monster_id=$serverMonsterId AND g.name_zh_tw='$escapedName';
"@)
    if ($postCheck.Count -ne 1) {
        throw "Post-commit verification did not return one monster."
    }
    $post = $postCheck[0] -split "`t"
    if ($post.Count -ne 8 -or
        [int]$post[2] -ne $enemy.level -or
        [int64]$post[4] -ne $experienceBase -or
        [string]$post[5] -cne "Verified" -or [int]$post[6] -ne 0 -or [int]$post[7] -ne 1) {
        throw "Post-commit monster values failed verification."
    }

    $analysisDir = Join-Path $sessionRoot "analysis"
    [IO.Directory]::CreateDirectory($analysisDir) | Out-Null
    $observation = [ordered]@{
        clientMonsterId = $clientMonsterId
        serverMonsterId = $serverMonsterId
        encounterLocalId = [int]$enemy.encounterLocalId
        name = $MonsterNameZhTw
        level = [int]$enemy.level
        maximumHp = $null
        observedDamageUpperBound = $observedDamageUpperBound
        experienceReward = [int64]$experienceBase
        authority = "VERIFIED"
        correlationStatus = "UniqueTemplateBattleEntryAndSettlementBinding;HpRequiresDirectStateSnapshot"
        conflictStatus = "None"
        battleEntrySourceFrames = "$entryFrameId;$actorFrameId"
        battleDamageSourceFrames = ($damageFrameIds -join ';')
        battleSettlementSourceFrames = $settlementFrameId
        officialAuthorityKey = $authorityKey
        officialSourceRow = $sourceRow
    }
    $extraction = [ordered]@{
        schemaVersion = "god2-controlled-single-monster-extraction-v1"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        sessionId = $SessionId
        encounterCount = 1
        fullyResolvedEncounterCount = 0
        battleEntryPacketCount = 1
        settlementPacketCount = 1
        derivation = [ordered]@{
            playerBattlePosition = $playerPosition
            enemyBattlePosition = [int]$enemy.position
            basicAttackCount = $basicAttacks.Count
            damageDeltas = @($damageEffects | ForEach-Object { [int]$_.delta })
            hpRule = "NotPromotedBecauseFinalHitMayOverkill"
            settlementExperienceFieldsAgree = $true
            rawPayloadRetained = $true
        }
        observations = @([pscustomobject]$observation)
    }
    $extractionPath = Join-Path $analysisDir "monster-capture-extraction.json"
    [IO.File]::WriteAllText(
        $extractionPath,
        ($extraction | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false))

    $commit = [ordered]@{
        schemaVersion = "god2-controlled-single-monster-commit-v1"
        status = "COMMITTED"
        sessionId = $SessionId
        monsterId = $serverMonsterId
        nameZhTw = $MonsterNameZhTw
        level = [int]$enemy.level
        maximumHp = $null
        observedDamageUpperBound = $observedDamageUpperBound
        experienceReward = [int64]$experienceBase
        runtimeEnabled = $false
        unknownAttackDefenseRemainNull = $true
        rawPayloadRetained = $true
        committedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    $commitPath = Join-Path $sessionRoot "reports\monster-import-commit.json"
    [IO.File]::WriteAllText(
        $commitPath,
        ($commit | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    $commit | ConvertTo-Json -Depth 8
}
finally {
    if ($plain) {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $secret = $null
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
}
