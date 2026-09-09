param(
    [ValidateSet('Equipment','SingleMonster','Immortal','Combat','BattleCampaign','General')]
    [string] $CaptureKind = 'Equipment',
    [ValidateRange(60, 3600)]
    [int] $WaitForClientSeconds = 1200,
    [ValidateRange(30, 7200)]
    [int] $ObserveSeconds = 600,
    [switch] $LaunchOfficialLauncher
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'God2Automation.Common.ps1')
$repoRoot = Get-God2RepoRoot
$profilePath = Join-Path $repoRoot 'Artifacts\ClientInstrumentation\LauncherAutomation\original-server-user-operated-profile.json'
$profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json

function Assert-PinnedFile([string] $Path, [string] $ExpectedSha256, [string] $Label) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "$Label is missing: $resolved" }
    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
    if ($actual -cne $ExpectedSha256) { throw "$Label SHA-256 changed; refresh evidence before capture." }
    return $resolved
}

$launcherPath = Assert-PinnedFile ([string]$profile.launcherExecutable) ([string]$profile.launcherSha256) 'Official Launcher'
$clientPath = Assert-PinnedFile ([string]$profile.clientExecutable) ([string]$profile.clientSha256) 'Official client'
$serverConfigPath = Assert-PinnedFile ([string]$profile.serverConfigPath) ([string]$profile.serverConfigSha256) 'Official server config'
$serverConfigText = Get-Content -LiteralPath $serverConfigPath -Raw -Encoding Default
foreach ($address in @($profile.allowedRemoteServerAddresses)) {
    if ($serverConfigText -notmatch ('(?m)^server[0-9]+_ip=' + [regex]::Escape([string]$address) + '[ \t]*\r?$')) {
        throw "Pinned official endpoint is absent from ctserver.ini: $address"
    }
}
if ($serverConfigText -match '(?m)^server[0-9]+_ip=(127\.0\.0\.1|0\.0\.0\.0)[ \t]*\r?$') {
    throw 'Official server config points to a loopback endpoint; refusing original-server capture.'
}

$launcherProcesses = @(Get-CimInstance Win32_Process -Filter "Name='Launcher.exe'" | Where-Object {
    [string]::Equals([string]$_.ExecutablePath, $launcherPath, [StringComparison]::OrdinalIgnoreCase)
})
if ($launcherProcesses.Count -eq 0) {
    if (-not $LaunchOfficialLauncher) {
        throw 'Official Launcher is not running. Re-run with -LaunchOfficialLauncher, accept UAC, then perform login manually.'
    }
    Start-Process -FilePath $launcherPath -WorkingDirectory (Split-Path -Parent $launcherPath) -Verb RunAs | Out-Null
}

$deadline = (Get-Date).AddSeconds($WaitForClientSeconds)
$clientProcess = $null
do {
    Start-Sleep -Milliseconds 500
    $launcherProcesses = @(Get-CimInstance Win32_Process -Filter "Name='Launcher.exe'" | Where-Object {
        [string]::Equals([string]$_.ExecutablePath, $launcherPath, [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($launcher in $launcherProcesses) {
        $rid = [God2Automation.NativeApi]::GetIntegrityRid([int]$launcher.ProcessId)
        if ($rid -lt 12288) { throw 'Official Launcher is not running with administrator/High integrity.' }
    }
    $matches = @(Get-CimInstance Win32_Process -Filter "Name='God2_opt.exe'" | Where-Object {
        [string]::Equals([string]$_.ExecutablePath, $clientPath, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -gt 1) { throw 'More than one pinned official client is running.' }
    if ($matches.Count -eq 1) { $clientProcess = $matches[0] }
} while ($null -eq $clientProcess -and (Get-Date) -lt $deadline)
if ($null -eq $clientProcess) { throw 'Timed out waiting for the user-operated official client.' }

$clientRid = [God2Automation.NativeApi]::GetIntegrityRid([int]$clientProcess.ProcessId)
if ($clientRid -lt 12288) { throw 'Official client is not High integrity; start it from the elevated official Launcher.' }
$remoteConnections = @(Get-NetTCPConnection -OwningProcess ([int]$clientProcess.ProcessId) -State Established -ErrorAction SilentlyContinue |
    Where-Object { $_.RemoteAddress -in @($profile.allowedRemoteServerAddresses) })
if ($remoteConnections.Count -eq 0) {
    throw 'The pinned official client has no established connection to an allowed non-loopback official endpoint.'
}

$startScript = Join-Path $PSScriptRoot 'Start-ContinuousEnhancedCapture.ps1'
$startArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$startScript,
    '-ObserveSeconds',$ObserveSeconds,'-SessionPrefix',("official-{0}" -f $CaptureKind.ToLowerInvariant()),
    '-ExpectedClientPath',$clientPath,'-ExpectedClientSha256',([string]$profile.clientSha256))
if ($CaptureKind -ceq 'SingleMonster') { $startArgs += '-SingleMonsterCapture' }
if ($CaptureKind -ceq 'BattleCampaign') { $startArgs += '-BattleCampaign' }
& powershell.exe @startArgs
if ($LASTEXITCODE -ne 0) { throw "Enhanced capture start failed: $LASTEXITCODE" }

$statePath = Join-Path $repoRoot 'Automation\State\packet-capture-active.json'
$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
$intent = [ordered]@{
    schemaId = 'God2UserOperatedOfficialCaptureIntent'
    schemaVersion = 1
    sessionId = [string]$state.sessionId
    captureKind = $CaptureKind
    launcherPath = $launcherPath
    launcherIntegrityRequired = 'High/12288'
    clientPath = $clientPath
    clientSha256 = [string]$profile.clientSha256
    officialRemoteAddresses = @($remoteConnections | Select-Object -ExpandProperty RemoteAddress -Unique)
    credentialEntry = 'ManualUserOperationOnly'
    directDatabaseFixture = $false
    battleOperation = if ($CaptureKind -ceq 'BattleCampaign') {
        'KeepFormationHealthRevealActive;StartWithFullHealthMonster;UsePoisonAsFirstAndOnlyDamage;AllowThreePoisonTicks;OnLaterRoundsCaptureRepeatedNormalAndDefendIncomingHits;CompareBasicAndSameSkillDamageAcrossDistinctTargetsWhenAvailable;ThenFinishBattleNormally'
    } else { $null }
    battleAutomation = if ($CaptureKind -ceq 'BattleCampaign') {
        'CaptureAndEvidenceClassificationAutomatic;NoNpcInteractionRequired'
    } else { $null }
    stopCommand = 'powershell -NoProfile -ExecutionPolicy Bypass -File .\Automation\Stop-ContinuousEnhancedCapture.ps1'
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$intentPath = Join-Path ([string]$state.sessionDir) 'reports\user-operated-official-capture-intent.json'
[IO.File]::WriteAllText($intentPath,($intent|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$intent | ConvertTo-Json -Depth 8
