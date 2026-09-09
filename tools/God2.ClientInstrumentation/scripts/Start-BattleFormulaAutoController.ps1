param(
    [string]$CaptureRoot,
    [int]$BattleEndSilenceMs = 2000,
    [int]$IdlePollMs = 250,
    [int]$PreBattleWindowSeconds = 45,
    [int]$MaximumPreBattleRecords = 512,
    [switch]$StopAfterOneBattle
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $CaptureRoot) {
    $officialRoot = Join-Path $repoRoot 'Artifacts\ClientInstrumentation\OfficialEvidenceLauncher'
    $CaptureRoot = Get-ChildItem -LiteralPath $officialRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'metadata.jsonl') } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $CaptureRoot) { throw 'No official evidence capture root was found.' }
$metadataPath = Join-Path $CaptureRoot 'metadata.jsonl'
$outputRoot = Join-Path $CaptureRoot 'FormulaInference'
$statusPath = Join-Path $outputRoot 'controller-status.json'
$inferenceScript = Join-Path $PSScriptRoot 'Invoke-BattleFormulaInference.ps1'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Write-Status([string]$Mode, [string]$BattleId, [int]$Accepted, [int]$Rejected, [int]$BaselineRecords) {
    [ordered]@{
        SchemaVersion='god2-battle-formula-auto-controller-v1'; UpdatedAt=[DateTimeOffset]::Now.ToString('o')
        Mode=$Mode; BattleId=$BattleId; AcceptedObservations=$Accepted; RejectedObservations=$Rejected
        PreBattleBaselineRecords=$BaselineRecords; PreBattleWindowSeconds=$PreBattleWindowSeconds
        IdlePollMs=$IdlePollMs; BattleEndSilenceMs=$BattleEndSilenceMs; CaptureRoot=$CaptureRoot
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statusPath -Encoding UTF8
}

function Test-PreBattleBaselineRecord($Record) {
    if ($Record.Api -in @('PostDecrypt','HandlerDecoded','BattleUiVitalObservation')) { return $true }
    $messageType = [string](Get-Value $Record @('MessageType','messageType','Type','type'))
    return $messageType -match '(?i)character|player|companion|pet|summon|status|attribute|vital'
}

function Get-RecordUnixMs($Record) {
    $value = Get-Value $Record @('wallUnixMs','ObservedAtUnixMs','observedAtUnixMs')
    if ($null -eq $value) { return [DateTimeOffset]::Now.ToUnixTimeMilliseconds() }
    return [long]$value
}

function Add-PreBattleRecord([System.Collections.Generic.Queue[object]]$Queue, [string]$Line, $Record) {
    $Queue.Enqueue([pscustomobject]@{ UnixMs=(Get-RecordUnixMs $Record); Line=$Line })
    $cutoff = [DateTimeOffset]::Now.ToUnixTimeMilliseconds() - ($PreBattleWindowSeconds * 1000L)
    while ($Queue.Count -gt 0 -and ($Queue.Count -gt $MaximumPreBattleRecords -or $Queue.Peek().UnixMs -lt $cutoff)) {
        $null = $Queue.Dequeue()
    }
}

function Write-PreBattleBaseline([System.Collections.Generic.Queue[object]]$Queue, [string]$BattleRoot) {
    $path = Join-Path $BattleRoot 'prebattle-player-companion-baseline.jsonl'
    $writer = [IO.StreamWriter]::new($path,$false,[Text.UTF8Encoding]::new($false))
    try {
        foreach ($entry in $Queue) { $writer.WriteLine($entry.Line) }
    } finally { $writer.Dispose() }
    [ordered]@{
        SchemaVersion='god2-prebattle-player-companion-baseline-v1'
        FrozenAt=[DateTimeOffset]::Now.ToString('o')
        RecordCount=$Queue.Count
        WindowSeconds=$PreBattleWindowSeconds
        EvidenceStatus=if ($Queue.Count -gt 0) { 'RuntimeObservedBaselineFrozen' } else { 'BlockedNoPreBattleBaseline' }
        Source='Bounded official-capture semantic/plaintext window; fields require verified decoder contracts'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $BattleRoot 'prebattle-baseline-status.json') -Encoding UTF8
}
function Get-Value($Object, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($property -and $null -ne $property.Value) { return $property.Value }
    }
    return $null
}
function Convert-FormulaObservation($Record) {
    $semantic = if ($Record.FormulaObservation) { $Record.FormulaObservation } else { $Record }
    $damage = Get-Value $semantic @('damage','Damage','finalDamage','FinalDamage','observedDamage','ObservedDamage')
    $direction = [string](Get-Value $semantic @('direction','Direction','attackDirection','AttackDirection'))
    $channel = [string](Get-Value $semantic @('channel','Channel','damageType','DamageType'))
    $playerAttack = Get-Value $semantic @('playerAttack','PlayerAttack','physicalAttack','PhysicalAttack','magicAttack','MagicAttack')
    $playerDefense = Get-Value $semantic @('playerDefense','PlayerDefense','physicalDefense','PhysicalDefense','magicDefense','MagicDefense')
    if (-not $damage -or $direction -notin @('incoming','outgoing') -or $channel -notin @('physical','magic') -or $null -eq $playerAttack -or $null -eq $playerDefense) { return $null }
    [ordered]@{
        timestamp=if ($Record.wallUnixMs) { [DateTimeOffset]::FromUnixTimeMilliseconds([long]$Record.wallUnixMs).ToString('o') } else { [DateTimeOffset]::Now.ToString('o') }
        monsterId=[string](Get-Value $semantic @('monsterId','MonsterId','monsterObjectId','MonsterObjectId','monsterName','MonsterName'))
        channel=$channel.ToLowerInvariant(); direction=$direction.ToLowerInvariant()
        playerAttack=[double]$playerAttack; playerDefense=[double]$playerDefense; damage=[double]$damage
        critical=[bool](Get-Value $semantic @('critical','Critical','isCritical','IsCritical'))
        blocked=[bool](Get-Value $semantic @('blocked','Blocked','isBlocked','IsBlocked'))
        miss=[bool](Get-Value $semantic @('miss','Miss','isMiss','IsMiss'))
        sourceApi=[string]$Record.Api; sourceSequence=$Record.sequence
    }
}

$stream = [IO.File]::Open($metadataPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$stream.Seek(0, [IO.SeekOrigin]::End) | Out-Null
$reader = [IO.StreamReader]::new($stream)
$active = $false
$battleId = ''
$lastSignal = [DateTimeOffset]::MinValue
$rawWriter = $null
$observationWriter = $null
$accepted = 0
$rejected = 0
$preBattleRecords = [System.Collections.Generic.Queue[object]]::new()
Write-Status 'IdleBaselineWatch' '' 0 0 0
try {
    while ($true) {
        $line = $reader.ReadLine()
        if ($null -eq $line) {
            if ($active -and ([DateTimeOffset]::Now - $lastSignal).TotalMilliseconds -ge $BattleEndSilenceMs) {
                $rawWriter.Dispose(); $observationWriter.Dispose(); $rawWriter=$null; $observationWriter=$null
                $battleRoot = Join-Path $outputRoot $battleId
                $observationPath = Join-Path $battleRoot 'formula-observations.jsonl'
                $resultPath = Join-Path $battleRoot 'formula-inference.json'
                $baselineCount = $preBattleRecords.Count
                $baselineStatusPath = Join-Path $battleRoot 'prebattle-baseline-status.json'
                $baselineReady = $false
                if (Test-Path -LiteralPath $baselineStatusPath) {
                    $baselineStatus = Get-Content -LiteralPath $baselineStatusPath -Raw | ConvertFrom-Json
                    $baselineReady = $baselineStatus.RecordCount -gt 0
                }
                if ($baselineReady -and (Get-Item $observationPath).Length -gt 0) {
                    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $inferenceScript -ObservationPath $observationPath -OutputPath $resultPath | Out-Null
                } else {
                    $reason = if (-not $baselineReady) { 'BlockedNoPreBattlePlayerCompanionBaseline' } else { 'BlockedNoVerifiedSemanticDamageObservation' }
                    [ordered]@{SchemaVersion='god2-official-formula-inference-v1';CompletedAt=[DateTimeOffset]::Now.ToString('o');ObservationCount=0;Identifiability=$reason;Candidates=@()} | ConvertTo-Json -Depth 5 | Set-Content $resultPath -Encoding UTF8
                }
                $active=$false
                $preBattleRecords.Clear()
                Write-Status 'IdleBaselineWatch' $battleId $accepted $rejected 0
                [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
                if ($StopAfterOneBattle) { break }
            }
            Start-Sleep -Milliseconds $IdlePollMs
            continue
        }
        try { $record = $line | ConvertFrom-Json } catch { continue }
        if (-not $active -and (Test-PreBattleBaselineRecord $record)) {
            Add-PreBattleRecord $preBattleRecords $line $record
        }
        if ($record.Api -eq 'BattleStateSnapshot') {
            $lastSignal = [DateTimeOffset]::Now
            if (-not $active) {
                $active=$true; $accepted=0; $rejected=0
                $battleId='battle-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff')
                $battleRoot=Join-Path $outputRoot $battleId
                New-Item -ItemType Directory -Force -Path $battleRoot | Out-Null
                $rawWriter=[IO.StreamWriter]::new((Join-Path $battleRoot 'raw-events.jsonl'),$false,[Text.UTF8Encoding]::new($false))
                $observationWriter=[IO.StreamWriter]::new((Join-Path $battleRoot 'formula-observations.jsonl'),$false,[Text.UTF8Encoding]::new($false))
                Write-PreBattleBaseline $preBattleRecords $battleRoot
                Write-Status 'BattleActiveInference' $battleId 0 0 $preBattleRecords.Count
            }
        }
        if ($active -and $record.Api -in @('BattleStateSnapshot','BattleActorSnapshot','BattleUiVitalObservation','MonsterIntelligenceConsumerSnapshot','FormulaObservation')) {
            $rawWriter.WriteLine($line); $rawWriter.Flush()
            $observation = Convert-FormulaObservation $record
            if ($observation) { $observationWriter.WriteLine(($observation | ConvertTo-Json -Compress)); $observationWriter.Flush(); $accepted++ }
            elseif ($record.Api -eq 'FormulaObservation') { $rejected++ }
        }
    }
}
finally {
    if ($rawWriter) { $rawWriter.Dispose() }
    if ($observationWriter) { $observationWriter.Dispose() }
    $reader.Dispose(); $stream.Dispose()
    $preBattleRecords.Clear()
    Write-Status 'Stopped' $battleId $accepted $rejected 0
}
