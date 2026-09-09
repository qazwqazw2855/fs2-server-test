param(
    [string] $RunId = '',
    [ValidateSet('OfficialA', 'OfficialB', 'OfficialC', 'OfficialD')]
    [string] $TestAccountKey = 'OfficialA',
    [string] $LauncherProfilePath = '',
    [string] $CredentialEnvelopePath = '',
    [string] $DeepEvidencePlanPath = '',
    [ValidateRange(4, 600)]
    [int] $DeepEvidenceObserveSeconds = 180,
    [ValidateRange(30, 570)]
    [int] $ObserveSeconds = 180,
    [ValidateRange(30, 120)]
    [int] $HostReadyTimeoutSeconds = 45,
    [ValidateRange(240, 900)]
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'God2Automation.Common.ps1')

$repoRoot = Get-God2RepoRoot
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = 'ultimate-official-live-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
}
if ($RunId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$') {
    throw 'RunId contains unsupported characters.'
}

$runDir = Join-Path $repoRoot ('Artifacts\Ultimate\LiveRuns\' + $RunId)
if (Test-Path -LiteralPath $runDir) {
    throw "Refusing to overwrite existing official live output: $runDir"
}
New-Item -ItemType Directory -Path $runDir | Out-Null

$status = 'BLOCKED'
$failureCode = 'NOT_RUN'
$hostResult = $null
$hostFailure = $null
$hostStartExit = $null
$hostStartOutput = @()
$commandPath = Get-God2AutomationCommandPath
$resolvedProfilePath = $null
$resolvedLauncherExecutable = $null
$resolvedCredentialEnvelopePath = $null
$resolvedDeepEvidencePlanPath = $null

try {
    if (-not [string]::IsNullOrWhiteSpace($LauncherProfilePath)) {
        $resolvedProfilePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $LauncherProfilePath))
    }
    else {
        $resolvedProfilePath = Join-Path $repoRoot 'Artifacts\ClientInstrumentation\LauncherAutomation\launcher-profile.json'
    }
    $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedProfilePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $resolvedProfilePath -PathType Leaf)) {
        throw 'Launcher profile must be an existing file under the repository root.'
    }
    $profile = Get-Content -LiteralPath $resolvedProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFileName([string]$profile.targetExecutable) -cne 'Launcher.exe') {
        throw 'Official live capture requires the official Launcher.exe profile.'
    }
    $resolvedLauncherExecutable = [IO.Path]::GetFullPath([string]$profile.targetExecutable)
    if (-not (Test-Path -LiteralPath $resolvedLauncherExecutable -PathType Leaf)) {
        throw 'Launcher profile targetExecutable does not exist.'
    }
    if (-not [string]::IsNullOrWhiteSpace($CredentialEnvelopePath)) {
        $resolvedCredentialEnvelopePath = [IO.Path]::GetFullPath($CredentialEnvelopePath)
        $statePrefix = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Automation\State')).TrimEnd('\') + '\'
        if (-not $resolvedCredentialEnvelopePath.StartsWith($statePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedCredentialEnvelopePath) -cnotmatch '^one-time-official-credential-[A-Za-z0-9._-]+\.json$' -or
            -not (Test-Path -LiteralPath $resolvedCredentialEnvelopePath -PathType Leaf)) {
            throw 'Credential envelope must be an existing approved one-time Automation State file.'
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($DeepEvidencePlanPath)) {
        $resolvedDeepEvidencePlanPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $DeepEvidencePlanPath))
        $planPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedDeepEvidencePlanPath.StartsWith($planPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $resolvedDeepEvidencePlanPath -PathType Leaf)) {
            throw 'Deep evidence plan must be an existing file under the repository root.'
        }
    }

    Write-God2AtomicJson -Value ([ordered]@{
        command = 'RunUltimateOfficialLiveAndExit'
        testAccountKey = $TestAccountKey
        launcherProfilePath = $resolvedProfilePath
        credentialEnvelopePath = $resolvedCredentialEnvelopePath
        runDir = $runDir
        runtimeRunId = $RunId
        observeSeconds = $ObserveSeconds
        deepEvidencePlanPath = $resolvedDeepEvidencePlanPath
        contractAcquisitionObserveSeconds = if($resolvedDeepEvidencePlanPath){
            $DeepEvidenceObserveSeconds
        }else{48}
        forceFreshClient = $true
        worldReadyTimeoutSeconds = 30
        targetAuthority = 'OFFICIAL_REMOTE_SERVER'
        localServerStarted = $false
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }) -Path $commandPath

    $hostStartOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
        (Join-Path $PSScriptRoot 'Start-God2AutomationHost.ps1') `
        -TimeoutSeconds $HostReadyTimeoutSeconds 2>&1)
    $hostStartExit = $LASTEXITCODE
    if ($hostStartExit -ne 0) {
        throw "Automation Host failed to become ready: $($hostStartOutput -join ' ')"
    }

    $hostResultPath = Join-Path $runDir 'ultimate-official-live-host-result.json'
    $statusPath = Get-God2AutomationStatusPath
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $hostResultPath -PathType Leaf) {
            $hostResult = Read-God2JsonWithRetry -Path $hostResultPath
            break
        }
        if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
            $hostStatus = Read-God2JsonWithRetry -Path $statusPath
            if ([string]$hostStatus.currentStage -in @('HostFailed', 'LoginFailed', 'UltimateOfficialLiveIncomplete')) {
                $hostFailure = [string]$hostStatus.lastError
                if ([string]$hostStatus.currentStage -ne 'UltimateOfficialLiveIncomplete') { break }
            }
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $hostResult) {
        $failureCode = if ($hostFailure) { 'AUTOMATION_HOST_TERMINAL_FAILURE' } else { 'OFFICIAL_LIVE_RESULT_TIMEOUT' }
    }
    elseif ([bool]$hostResult.passed) {
        $status = 'PASS'
        $failureCode = 'NONE'
    }
    else {
        $failureCode = if ($hostResult.firstBlocker) { [string]$hostResult.firstBlocker } else { 'OFFICIAL_LIVE_HOST_RESULT_BLOCKED' }
    }
}
catch {
    $hostFailure = $_.Exception.Message
    if ($failureCode -eq 'NOT_RUN') { $failureCode = 'OFFICIAL_LIVE_SCRIPT_FAILED' }
    Write-God2AtomicJson -Value ([ordered]@{
        message = $_.Exception.Message
        script = $_.InvocationInfo.ScriptName
        line = $_.InvocationInfo.ScriptLineNumber
        command = $_.InvocationInfo.Line
        position = $_.InvocationInfo.PositionMessage
    }) -Path (Join-Path $runDir 'script-error.json')
}
finally {
    if (Test-Path -LiteralPath $commandPath -PathType Leaf) {
        try {
            $remainingCommand = Read-God2JsonWithRetry -Path $commandPath
            if ([string]$remainingCommand.command -ceq 'RunUltimateOfficialLiveAndExit' -and
                [string]$remainingCommand.runtimeRunId -ceq $RunId) {
                Remove-Item -LiteralPath $commandPath -Force
            }
        }
        catch { }
    }
    if ($resolvedCredentialEnvelopePath -and
        (Test-Path -LiteralPath $resolvedCredentialEnvelopePath -PathType Leaf)) {
        Remove-Item -LiteralPath $resolvedCredentialEnvelopePath -Force -ErrorAction SilentlyContinue
    }

    $summary = [ordered]@{
        schemaId = 'God2UltimateOfficialLiveSummary'
        schemaVersion = 1
        runId = $RunId
        status = $status
        failureCode = $failureCode
        targetAuthority = 'OFFICIAL_REMOTE_SERVER'
        officialLauncherRequired = $true
        launcherExecutable = $resolvedLauncherExecutable
        directClientLaunch = if ($hostResult) { [bool]$hostResult.directClientLaunch } else { $false }
        localServerStarted = $false
        localEndpointRequired = $false
        agreementAccepted = if ($hostResult) { [bool]$hostResult.agreementAccepted } else { $false }
        startGameClicked = if ($hostResult) { [bool]$hostResult.startGameClicked } else { $false }
        directSoundDialogHandled = if ($hostResult) { [bool]$hostResult.directSoundDialogHandled } else { $false }
        attachedBeforeLogin = if ($hostResult) { [bool]$hostResult.attachedBeforeLogin } else { $false }
        loginSuccess = if ($hostResult) { [bool]$hostResult.loginSuccess } else { $false }
        characterSelect = if ($hostResult) { [bool]$hostResult.characterSelect } else { $false }
        enterWorld = if ($hostResult) { [bool]$hostResult.enterWorld } else { $false }
        officialEndpointAttested = if ($hostResult -and $hostResult.officialEndpointAttestation) {
            [bool]$hostResult.officialEndpointAttestation.attested
        } else { $false }
        runtimePassed = if ($hostResult) { [bool]$hostResult.runtimePassed } else { $false }
        deepEvidencePlanEnabled = ($null -ne $resolvedDeepEvidencePlanPath)
        deepEvidencePlanPath = $resolvedDeepEvidencePlanPath
        deepEvidencePlanSHA256 = if($resolvedDeepEvidencePlanPath){
            (Get-FileHash -LiteralPath $resolvedDeepEvidencePlanPath -Algorithm SHA256).Hash
        }else{''}
        strictUnloadVerified = if ($hostResult) { [bool]$hostResult.strictUnloadVerified } else { $false }
        targetAliveAfterDetach = if ($hostResult) { [bool]$hostResult.targetAliveAfterDetach } else { $false }
        userManualOperation = 'NOT REQUIRED'
        hostStartExit = $hostStartExit
        hostStartOutput = $hostStartOutput
        hostFailure = $hostFailure
        hostResultPresent = ($null -ne $hostResult)
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        runDir = "Artifacts/Ultimate/LiveRuns/$RunId"
    }
    Write-God2AtomicJson -Value $summary -Path (Join-Path $runDir 'ultimate-official-live-summary.json')
    $summary | ConvertTo-Json -Depth 16
}
