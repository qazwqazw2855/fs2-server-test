param(
    [string] $RunId = "",
    [ValidateSet("OfficialA", "OfficialB", "OfficialC", "OfficialD")]
    [string] $TestAccountKey = "OfficialA",
    [ValidateSet(19, 3)]
    [int] $ClientMapId = 19,
    [ValidateSet(
        "Dynamic",
        "GoldenCharacterId",
        "GoldenCharacterName",
        "GoldenIdentity",
        "CurrentCreateProfile",
        "CurrentCreateProfileMinimal",
        "LegacyTailOmit2",
        "LegacyTailOmit3",
        "LegacyTailOmit4",
        "LegacyTailOmit5To10",
        "LegacyTailOmit5To7",
        "LegacyTailOmit8To10",
        "LegacyTailOmit5",
        "LegacyTailOmit6",
        "LegacyTailOmit7",
        "LegacyTailOmit8",
        "LegacyTailOmit9",
        "LegacyTailOmit10",
        "StaticRecordsOmit0To14",
        "StaticRecordsOmit15To28",
        "StaticRecordsOmit0To7",
        "StaticRecordsOmit8To14",
        "StaticRecordsOmit15To22",
        "StaticRecordsOmit23To28",
        "StaticRecordsOmit15To17",
        "StaticRecordsOmit18To20",
        "StaticRecordOmit15",
        "StaticRecordOmit16",
        "StaticRecordOmit17",
        "StaticRecordOmit18",
        "StaticRecordOmit19",
        "StaticRecordOmit20",
        "SceneZero25To123",
        "SceneZero124To318",
        "SceneZero25To64",
        "SceneZero65To123",
        "SceneZero25To26",
        "SceneZero103To123",
        "SceneZero65To83",
        "SceneZero84To102",
        "SceneZero65",
        "SceneZero66To70",
        "SceneEmptyHeader26")]
    [string] $EvidenceBootstrapPlayerIdentity = "Dynamic",
    [ValidateRange(-1, 32767)]
    [int] $EvidencePositionX = -1,
    [ValidateRange(-1, 32767)]
    [int] $EvidencePositionY = -1,
    [switch] $SkipFileIoTrace,
    [string] $ClientPath = "",
    [ValidateRange(120, 600)]
    [int] $TimeoutSeconds = 360
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$expectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"
if ([string]::IsNullOrWhiteSpace($ClientPath)) {
    $ClientPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "XJZ2\God2_opt.exe"
}
$ClientPath = [IO.Path]::GetFullPath($ClientPath)
if (-not (Test-Path -LiteralPath $ClientPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $ClientPath -Algorithm SHA256).Hash -cne $expectedClientSha256) {
    throw "ClientPath is not the evidence-pinned official God2_opt.exe build."
}
$clientRoot = Split-Path -Parent $ClientPath
$launcherProfilePath = Join-Path $repoRoot "Artifacts\PostRemediationCompatibilityCheckpoint\launcher-profile.json"
if (-not (Test-Path -LiteralPath $launcherProfilePath -PathType Leaf)) {
    $launcherProfilePath = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LauncherAutomation\launcher-profile.json"
}
if (-not (Test-Path -LiteralPath $launcherProfilePath -PathType Leaf)) {
    throw "The evidence-pinned God2 Classic launcher profile is missing."
}
$launcherProfile = Get-Content -LiteralPath $launcherProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([IO.Path]::GetFileName([string]$launcherProfile.targetExecutable) -cne "God2ClassicLauncher.exe" -or
    [string]$launcherProfile.routeAuthority.expectedServerIp -cne "127.0.0.1" -or
    [int]$launcherProfile.routeAuthority.expectedServerPort -ne 2592) {
    throw "Map identity evidence requires the signed God2 Classic local launcher route 127.0.0.1:2592."
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = "map-identity-evidence-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}
if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$') {
    throw "RunId contains unsupported characters."
}
$baselinePosition = if ($ClientMapId -eq 19) {
    [ordered]@{ x = 28; y = 34 }
}
else {
    [ordered]@{ x = 196; y = 139 }
}
if (($EvidencePositionX -eq -1) -xor ($EvidencePositionY -eq -1)) {
    throw "EvidencePositionX and EvidencePositionY must be provided together."
}
if ($EvidencePositionX -eq -1) {
    $EvidencePositionX = [int]$baselinePosition.x
    $EvidencePositionY = [int]$baselinePosition.y
}
if ($ClientMapId -ne 19 -and
    ($EvidencePositionX -ne [int]$baselinePosition.x -or $EvidencePositionY -ne [int]$baselinePosition.y)) {
    throw "Arbitrary position evidence is currently locked to Client Map 19."
}
$arbitraryPositionRequested = $EvidencePositionX -ne [int]$baselinePosition.x -or
    $EvidencePositionY -ne [int]$baselinePosition.y

$runDir = Join-Path $repoRoot ("Artifacts\MapIdentityRecovery\" + $RunId)
$serverStop = Join-Path $runDir "server.stop"
$serverOut = Join-Path $runDir "server.stdout.txt"
$serverErr = Join-Path $runDir "server.stderr.txt"
$hostResultPath = Join-Path $runDir "frozen-regression-host-result.json"
$summaryPath = Join-Path $runDir "final-summary.json"
$analysisPath = Join-Path $runDir "fileio-analysis.json"
$server = $null
$traceSessionId = $null
$serverReady = $false
$traceStarted = $false
$traceStopped = $false
$hostResult = $null
$analysisExitCode = $null
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

try {
    $null = Initialize-God2DatabasePasswordEnvironment
    $probeDll = Join-Path $repoRoot "tools\God2.AutomationEnvironmentProbe\bin\Release\net10.0\God2.AutomationEnvironmentProbe.dll"
    $reverseDll = Join-Path $repoRoot "tools\God2.OfflineClientReverseEngineering\bin\Release\net10.0\God2.OfflineClientReverseEngineering.dll"
    if (-not (Test-Path -LiteralPath $probeDll -PathType Leaf) -or
        -not (Test-Path -LiteralPath $reverseDll -PathType Leaf)) {
        throw "Map identity evidence tools must be built in Release before the evidence run."
    }

    $serverArguments = @(
        '"' + $probeDll + '"',
        '--mode', 'map-identity-evidence-host',
        '--evidence-client-map', $ClientMapId,
        '--evidence-position-x', $EvidencePositionX,
        '--evidence-position-y', $EvidencePositionY,
        '--evidence-bootstrap-player-identity', $EvidenceBootstrapPlayerIdentity,
        '--base-directory', '"' + $repoRoot + '"',
        '--stop-file', '"' + $serverStop + '"'
    ) -join ' '
    $server = Start-Process `
        -FilePath "dotnet.exe" `
        -ArgumentList $serverArguments `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr `
        -WindowStyle Hidden `
        -PassThru

    $deadline = (Get-Date).AddSeconds(60)
    do {
        if ($server.HasExited) { break }
        if ((Test-Path -LiteralPath $serverOut) -and
            ((Get-Content -LiteralPath $serverOut -Raw -ErrorAction SilentlyContinue) -match "Map Identity Evidence Host Ready")) {
            $serverReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if (-not $serverReady) {
        throw "The isolated Map Identity Evidence Host did not become ready."
    }

    if (-not $SkipFileIoTrace) {
        $traceOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
            (Join-Path $PSScriptRoot "Invoke-God2MapFileIoTrace.ps1") `
            -Action Start -RunId $RunId -TimeoutSeconds 120 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Map FileIO trace start failed: $($traceOutput -join ' ')"
        }
        $traceStart = Read-God2JsonWithRetry -Path (Join-Path $runDir "fileio-start.json")
        $traceSessionId = [string]$traceStart.sessionId
        $traceStarted = [string]$traceStart.status -eq "PASS"
        if (-not $traceStarted -or [string]::IsNullOrWhiteSpace($traceSessionId)) {
            throw "Map FileIO trace did not return an owned session identity."
        }
    }

    $commandPath = Get-God2AutomationCommandPath
    if (Test-Path -LiteralPath $commandPath) {
        throw "Automation Host already has a pending command; evidence replay refused to overwrite it."
    }
    Write-God2AtomicJson -Value ([ordered]@{
        command = "RunFrozenRegressionAndExit"
        testAccountKey = $TestAccountKey
        runDir = $runDir
        launcherProfilePath = $launcherProfilePath
        serverReady = $true
        evidencePurpose = if ($arbitraryPositionRequested) {
            "StaticPositionConsumerAcceptance"
        }
        elseif ($SkipFileIoTrace) { "WorldEntryProfileIsolation" } else { "MapIdentityFileIoOnly" }
        worldReadyTimeoutSeconds = if ($EvidenceBootstrapPlayerIdentity -like "LegacyTailOmit*") { 30 } elseif ($SkipFileIoTrace) { 45 } else { 120 }
        postWorldGroundClick = $arbitraryPositionRequested
        packetCapture = $false
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }) -Path $commandPath

    $hostOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds 60 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Automation Host start failed: $($hostOutput -join ' ')"
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $hostResultPath -PathType Leaf) {
            $hostResult = Read-God2JsonWithRetry -Path $hostResultPath
            break
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    if (-not $hostResult) {
        throw "Automated official client did not produce the evidence replay result in time."
    }
}
finally {
    if ($traceStarted -and -not $traceStopped) {
        $stopOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
            (Join-Path $PSScriptRoot "Invoke-God2MapFileIoTrace.ps1") `
            -Action Stop -SessionId $traceSessionId -TimeoutSeconds 120 2>&1)
        $traceStopped = $LASTEXITCODE -eq 0
    }

    if ($server -and -not $server.HasExited) {
        New-Item -ItemType File -Force -Path $serverStop | Out-Null
        if (-not $server.WaitForExit(30000)) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
    }

    $commandPath = Get-God2AutomationCommandPath
    if (Test-Path -LiteralPath $commandPath) {
        Remove-Item -LiteralPath $commandPath -Force -ErrorAction SilentlyContinue
    }
}

$etlPath = Join-Path $runDir "map-fileio.etl"
if ($hostResult -and $traceStopped -and (Test-Path -LiteralPath $etlPath -PathType Leaf) -and $hostResult.clientPid) {
    & dotnet.exe $reverseDll `
        --client $ClientPath `
        --analyze-fileio-etl $etlPath `
        --client-root $clientRoot `
        --client-pid ([int]$hostResult.clientPid) `
        --out $analysisPath
    $analysisExitCode = $LASTEXITCODE
}

$analysis = if (Test-Path -LiteralPath $analysisPath -PathType Leaf) {
    Read-God2JsonWithRetry -Path $analysisPath
}
else {
    $null
}
$summary = [ordered]@{
    schemaVersion = "god2-map-identity-evidence-run-v1"
    runId = $RunId
    status = if ($serverReady -and ($SkipFileIoTrace -or ($traceStarted -and $traceStopped)) -and $hostResult -and
        [bool]$hostResult.localEndpointAttestation.attested -and
        [bool]$hostResult.worldReady -and ($SkipFileIoTrace -or $analysisExitCode -eq 0)) { "PASS" } else { "BLOCKED" }
    evidenceHostAuthority = "TestOnlyEvidence"
    evidenceBootstrapPlayerIdentity = $EvidenceBootstrapPlayerIdentity
    mariaDbUsage = "CredentialAndCharacterIdentityOnly"
    analysisCoordinate = [ordered]@{
        clientMapId = $ClientMapId
        clientAreaId = 4
        x = $EvidencePositionX
        y = $EvidencePositionY
    }
    baselineCoordinate = [ordered]@{
        clientMapId = $ClientMapId
        clientAreaId = 4
        x = [int]$baselinePosition.x
        y = [int]$baselinePosition.y
    }
    staticPositionConsumerEvidence = if ($ClientMapId -eq 19 -and
        ($EvidencePositionX -ne [int]$baselinePosition.x -or $EvidencePositionY -ne [int]$baselinePosition.y)) {
        "God2_opt+rva-0x0008D710"
    }
    else {
        "NOT REQUESTED"
    }
    productionMapIdentityWritten = $false
    productionCharacterPositionWritten = $false
    portalPersistence = "InMemoryEvidenceOnly"
    packetCapture = $false
    officialClientReplay = if ($hostResult) { [bool]$hostResult.worldReady } else { $false }
    postWorldGroundClickAttempted = if ($hostResult -and $hostResult.PSObject.Properties["postWorldGroundClickAttempted"]) {
        [bool]$hostResult.postWorldGroundClickAttempted
    }
    else {
        $false
    }
    clientWorldScreenReady = if ($hostResult -and $hostResult.PSObject.Properties["clientWorldScreenReady"]) {
        [bool]$hostResult.clientWorldScreenReady
    }
    else {
        $false
    }
    protocolMetadataAcceptance = if ($hostResult -and $hostResult.PSObject.Properties["protocolMetadataAcceptance"]) {
        [string]$hostResult.protocolMetadataAcceptance
    }
    else {
        "NOT REPORTED"
    }
    localEndpointAttested = if ($hostResult -and $hostResult.localEndpointAttestation) {
        [bool]$hostResult.localEndpointAttestation.attested
    }
    else {
        $false
    }
    clientPid = if ($hostResult) { $hostResult.clientPid } else { $null }
    fileIoTrace = if ($SkipFileIoTrace) { $false } else { $traceStopped }
    observedClientFileCount = if ($analysis) { [int]$analysis.fileCount } else { 0 }
    resourceEvidenceStatus = if ($SkipFileIoTrace) {
        if ($ClientMapId -eq 19) {
            "REUSED_PINNED_MAP19_RESOURCE_EVIDENCE"
        }
        else {
            "NOT COLLECTED — FILEIO TRACE SKIPPED"
        }
    }
    elseif ($analysisExitCode -eq 0 -and $analysis -and [int]$analysis.fileCount -gt 0) { "PASS" }
    else { "BLOCKED" }
    fakeNetworkBytes = 0
    manualOperation = $false
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
Write-God2AtomicJson -Value $summary -Path $summaryPath
$summary | ConvertTo-Json -Depth 8
if ([string]$summary.status -ne "PASS") { exit 1 }
