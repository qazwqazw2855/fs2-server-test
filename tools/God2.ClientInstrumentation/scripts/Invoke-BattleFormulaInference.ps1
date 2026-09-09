param(
    [Parameter(Mandatory = $true)][string]$ObservationPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$SearchMax = 5000,
    [double]$AllowedRandomMin = 0.70,
    [double]$AllowedRandomMax = 1.30,
    [int]$MinimumDamage = 1
)

$ErrorActionPreference = 'Stop'
$observations = @(
    Get-Content -LiteralPath $ObservationPath -ErrorAction SilentlyContinue |
        ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
        Where-Object {
            $_ -and [double]$_.damage -gt 0 -and
            -not $_.critical -and -not $_.blocked -and -not $_.miss -and
            $_.direction -in @('incoming', 'outgoing') -and
            $_.channel -in @('physical', 'magic')
        }
)

$rounders = [ordered]@{
    Floor = { param([double]$v) [Math]::Floor($v) }
    Nearest = { param([double]$v) [Math]::Round($v, 0, [MidpointRounding]::AwayFromZero) }
    Ceiling = { param([double]$v) [Math]::Ceiling($v) }
    Truncate = { param([double]$v) [Math]::Truncate($v) }
}
$models = @(
    foreach ($k in 0.5, 0.75, 1.0, 1.25, 1.5, 2.0) { [pscustomobject]@{ Family='Linear'; K=$k; C=0; Formula='max(MIN, round((A - K*D) * R))' } }
    foreach ($c in 25, 50, 100, 200, 400, 800, 1600) { [pscustomobject]@{ Family='Ratio'; K=0; C=$c; Formula='max(MIN, round(A*C/(C+D) * R))' } }
    foreach ($k in 0.5, 1.0, 1.5, 2.0, 3.0, 4.0) { [pscustomobject]@{ Family='Quadratic'; K=$k; C=0; Formula='max(MIN, round(A^2/(A+K*D) * R))' } }
)

function Get-BaseDamage($Model, [double]$Known, [double]$Hidden, [string]$Direction) {
    $attack = if ($Direction -eq 'incoming') { $Hidden } else { $Known }
    $defense = if ($Direction -eq 'incoming') { $Known } else { $Hidden }
    switch ($Model.Family) {
        'Linear' { return $attack - $Model.K * $defense }
        'Ratio' { return $attack * $Model.C / ($Model.C + $defense) }
        'Quadratic' { return $attack * $attack / ($attack + $Model.K * $defense) }
    }
}

$groups = $observations | Group-Object { "$($_.monsterId)|$($_.channel)|$($_.direction)" }
$ranked = [Collections.Generic.List[object]]::new()
foreach ($model in $models) {
    foreach ($rounding in $rounders.Keys) {
        $totalScore = 0.0
        $fits = @()
        $failed = $false
        foreach ($group in $groups) {
            $best = $null
            for ($hidden = 0; $hidden -le $SearchMax; $hidden++) {
                $ratios = @()
                foreach ($observation in $group.Group) {
                    $known = if ($observation.direction -eq 'incoming') { [double]$observation.playerDefense } else { [double]$observation.playerAttack }
                    $base = [Math]::Max($MinimumDamage, (Get-BaseDamage $model $known $hidden $observation.direction))
                    if ($base -le 0) { $ratios = @(); break }
                    $ratios += [double]$observation.damage / $base
                }
                if (-not $ratios.Count) { continue }
                $lo = ($ratios | Measure-Object -Minimum).Minimum
                $hi = ($ratios | Measure-Object -Maximum).Maximum
                if ($lo -lt $AllowedRandomMin -or $hi -gt $AllowedRandomMax) { continue }
                $width = $hi - $lo
                $centerPenalty = [Math]::Abs(1.0 - (($lo + $hi) / 2.0))
                $score = $width * 30.0 + $centerPenalty * 5.0
                if (-not $best -or $score -lt $best.Score) {
                    $best = [pscustomobject]@{ Group=$group.Name; HiddenValue=$hidden; RandomMin=$lo; RandomMax=$hi; Score=$score }
                }
            }
            if (-not $best) { $failed = $true; break }
            $fits += $best
            $totalScore += $best.Score
        }
        if (-not $failed -and $fits.Count) {
            $ranked.Add([pscustomobject]@{
                Family=$model.Family; Formula=$model.Formula; K=$model.K; C=$model.C
                Rounding=$rounding; Score=$totalScore; Fits=$fits
            })
        }
    }
}

$ordered = @($ranked | Sort-Object Score | Select-Object -First 20)
$conditions = @($observations | Group-Object { "$($_.monsterId)|$($_.channel)|$($_.direction)|$($_.playerAttack)|$($_.playerDefense)" }).Count
$result = [ordered]@{
    SchemaVersion='god2-official-formula-inference-v1'
    CompletedAt=[DateTimeOffset]::Now.ToString('o')
    ObservationCount=$observations.Count
    DistinctConditionCount=$conditions
    Identifiability=if ($conditions -lt 2) { 'InsufficientDistinctInputs' } elseif ($ordered.Count -eq 1) { 'UniqueCandidate' } elseif ($ordered.Count) { 'MultipleCandidates' } else { 'NoCandidateFits' }
    CandidateCount=$ordered.Count
    Candidates=$ordered
    EvidenceRule='No critical, fatal, blocked, missed, or incomplete observation accepted.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json -Depth 8

