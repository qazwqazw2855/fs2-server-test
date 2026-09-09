param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]] $PositionName,

    [switch] $PurgeRawAfterCommit
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($PurgeRawAfterCommit) {
    throw "PurgeRawAfterCommit is disabled for incomplete multi-monster observations. Use the closed-loop monster completeness and reviewed purge workflow."
}

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
if (-not (Test-Path -LiteralPath $mariaDb -PathType Leaf) -or
    -not (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
    throw "MariaDB client or protected administrator secret is missing."
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

$positionNames = [ordered]@{}
foreach ($binding in $PositionName) {
    if ($binding -notmatch '^(?<position>\d{1,3})=(?<name>[^=]+)$') {
        throw "PositionName must use the form battlePosition=TraditionalChineseName."
    }
    $position = [int]$Matches.position
    $name = $Matches.name.Trim()
    if ($position -lt 0 -or $position -gt 41 -or [string]::IsNullOrWhiteSpace($name)) {
        throw "A battle position or monster name is invalid."
    }
    if ($positionNames.Contains($position)) {
        throw "Battle position $position was supplied more than once."
    }
    $positionNames.Add($position, $name)
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

function Escape-SqlLiteral {
    param([Parameter(Mandatory = $true)] [string] $Value)
    return $Value.Replace("'", "''")
}

$packetPath = Join-Path $sessionRoot "raw\injected-packets.jsonl"
if (-not (Test-Path -LiteralPath $packetPath -PathType Leaf)) {
    throw "Injected packet evidence is missing."
}

$records = [Collections.Generic.List[object]]::new()
Get-Content -LiteralPath $packetPath -Encoding UTF8 | ForEach-Object {
    if (-not [string]::IsNullOrWhiteSpace($_)) {
        $records.Add(($_ | ConvertFrom-Json))
    }
}
$postDecrypt = @($records | Where-Object { [string]$_.Api -ceq "PostDecrypt" })
$handler = @($records | Where-Object { [string]$_.Api -ceq "HandlerDecoded" })
$battleEntry = @($postDecrypt | Where-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    $bytes.Length -ge 4 -and $bytes[2] -eq 0x82
})
if ($battleEntry.Count -ne 1) {
    throw "Expected exactly one battle-entry frame; observed $($battleEntry.Count)."
}

$actors = @($handler | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 45 -and $bytes[0] -eq 0x1C) {
        [pscustomobject]@{
            record = $_
            position = [int]$bytes[1]
            level = [int]$bytes[2]
            encounterLocalId = [int]$bytes[7]
        }
    }
})
$effects = @($handler | ForEach-Object {
    $bytes = Convert-HexBytes ([string]$_.PayloadHex)
    if ($bytes.Length -eq 15 -and $bytes[0] -eq 0x83) {
        $delta = [BitConverter]::ToInt16($bytes, 9)
        if ($delta -lt 0) {
            $flags = [BitConverter]::ToUInt32($bytes, 11)
            [pscustomobject]@{
                record = $_
                sourcePosition = [int]$bytes[2]
                delta = [int]$delta
                terminal = (($flags -band 0x0F) -eq 0x0B)
                lowOutcomeBits = [int]($flags -band 0x0F)
            }
        }
    }
})

Add-Type -AssemblyName System.Security
$protected = [IO.File]::ReadAllBytes($secretPath)
$plain = [Security.Cryptography.ProtectedData]::Unprotect(
    $protected, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$secret = $null

function Invoke-MariaDbQuery {
    param([Parameter(Mandatory = $true)] [string] $Sql)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $mariaDb --protocol=tcp --host 127.0.0.1 --port 3306 --user root `
            --connect-timeout 2 --default-character-set=utf8mb4 --batch --skip-column-names `
            --execute $Sql 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($exitCode -ne 0) {
        throw "MariaDB operation failed: $($output -join [Environment]::NewLine)"
    }
    return @($output | Where-Object {
        $_ -is [string] -and -not $_.StartsWith("WARNING:", [StringComparison]::OrdinalIgnoreCase)
    })
}

try {
    $secret = ([Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json)
    $env:MYSQL_PWD = [string]$secret.password
    $observations = [Collections.Generic.List[object]]::new()

    foreach ($entry in $positionNames.GetEnumerator()) {
        $position = [int]$entry.Key
        $name = [string]$entry.Value
        $actorRows = @($actors | Where-Object { $_.position -eq $position })
        if ($actorRows.Count -ne 1 -or $actorRows[0].level -le 0 -or
            $actorRows[0].encounterLocalId -le 0) {
            throw "Battle position $position does not have one valid actor descriptor."
        }
        $actor = $actorRows[0]
        $escapedName = Escape-SqlLiteral $name

        $identityRows = @(Invoke-MariaDbQuery -Sql @"
SELECT JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.categoryOrModelCandidates[2]')),
       JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.sourceRow')),
       s.AuthorityKey
FROM god2.content_staging_records s
WHERE s.Domain='EncounterName'
  AND s.ConvertedText='$escapedName'
  AND JSON_UNQUOTE(JSON_EXTRACT(s.NormalizedData,'$.encounterLocalId'))='$($actor.encounterLocalId)'
  AND s.RunId=(
      SELECT RunId FROM god2.content_recovery_runs
      WHERE Phase='GameplayContentRecoveryPhase1'
        AND CompletedAtUtc IS NOT NULL AND Status<>'RUNNING'
      ORDER BY CompletedAtUtc DESC LIMIT 1);
"@)
        if ($identityRows.Count -ne 1) {
            throw "Official encounter identity is not unique for $name at position $position."
        }
        $identity = $identityRows[0] -split "`t"
        if ($identity.Count -ne 3) {
            throw "Official encounter identity returned an unexpected shape."
        }

        $serverRows = @(Invoke-MariaDbQuery -Sql @"
SELECT JSON_OBJECT('monsterId',monster_id,'level',level,'maximumHp',max_hp)
FROM god2_game.monsters WHERE name_zh_tw='$escapedName';
"@)
        if ($serverRows.Count -ne 1) {
            throw "Formal monster identity is not unique for $name."
        }
        $server = $serverRows[0] | ConvertFrom-Json
        $serverMonsterId = [int]$server.monsterId
        if ($null -ne $server.level -and [int]$server.level -ne $actor.level) {
            throw "Captured and formal levels conflict for $name."
        }

        $exactHp = $null

        $hpSql = "NULL"
        $status = "VERIFIED_LEVEL_CAPTURE_INCOMPLETE"
        $note = "多怪戰鬥已驗證等級；0x83 位置是動作來源，目標須與指令遮罩關聯，且末擊可能溢出，HP 與經驗保持空白。"
        $sessionEscaped = Escape-SqlLiteral $SessionId
        $authorityEscaped = Escape-SqlLiteral ([string]$identity[2])
        $actorFrame = Escape-SqlLiteral ([string]$actor.record.SourceFrameId)
        $entryFrame = Escape-SqlLiteral ([string]$battleEntry[0].SourceFrameId)
        $capturedAt = [DateTimeOffset]::FromUnixTimeMilliseconds(
            [int64]$actor.record.wallUnixMs).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff")

        Invoke-MariaDbQuery -Sql @"
START TRANSACTION;
INSERT INTO god2.verified_monster_observations
    (monster_id,name_zh_tw,client_monster_id,encounter_local_id,level,maximum_hp,
     experience_reward,source_session_id,official_authority_key,official_source_row,
     battle_entry_frame,battle_actor_frame,battle_settlement_frame,evidence_status,
     validation_note_zh_tw,observed_at_utc)
VALUES
    ($serverMonsterId,'$escapedName',$([int]$identity[0]),$($actor.encounterLocalId),
     $($actor.level),$hpSql,NULL,'$sessionEscaped','$authorityEscaped',$([int]$identity[1]),
     '$entryFrame','$actorFrame',NULL,'$status','$(Escape-SqlLiteral $note)','$capturedAt')
ON DUPLICATE KEY UPDATE
    level=VALUES(level),
    maximum_hp=NULL,
    experience_reward=COALESCE(VALUES(experience_reward),experience_reward),
    source_session_id=VALUES(source_session_id),
    evidence_status=VALUES(evidence_status),
    validation_note_zh_tw=VALUES(validation_note_zh_tw),
    observed_at_utc=VALUES(observed_at_utc);
SET @god2_sync_mode=1;
UPDATE god2_game.monsters
SET level=$($actor.level),
    evidence_status='Verified', enabled=0,
    admin_note='$(Escape-SqlLiteral $note)'
WHERE monster_id=$serverMonsterId AND name_zh_tw='$escapedName';
SET @god2_sync_mode=0;
COMMIT;
"@ | Out-Null

        $observations.Add([pscustomobject][ordered]@{
            nameZhTw = $name
            level = [int]$actor.level
            maximumHp = $exactHp
            observedDamageLowerBound = $null
            verification = "等級已驗證；HP 需直接狀態快照"
            experienceReward = $null
        })
    }

    $artifact = [ordered]@{
        schemaVersion = "god2-readable-multi-monster-observation-v1"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        monsters = @($observations)
        formalDatabase = "god2_game"
        rawPacketDataIncluded = $false
    }
    $artifactDir = Join-Path $repoRoot "Artifacts\RecoveryFinal"
    [IO.Directory]::CreateDirectory($artifactDir) | Out-Null
    $safeSession = $SessionId -replace '[^A-Za-z0-9._-]', '_'
    $artifactPath = Join-Path $artifactDir "verified-monsters-$safeSession.json"
    [IO.File]::WriteAllText(
        $artifactPath,
        ($artifact | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    $artifact | ConvertTo-Json -Depth 8
}
finally {
    if ($plain) {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $secret = $null
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
}
