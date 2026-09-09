param(
    [string] $RunId = "",
    [ValidateSet("OfficialA", "OfficialB", "OfficialC", "OfficialD")]
    [string] $TestAccountKey = "OfficialA",
    [string] $LauncherProfilePath = "",
    [int] $ServerReadyTimeoutSeconds = 45,
    [int] $HostReadyTimeoutSeconds = 35,
    [ValidateRange(240, 600)]
    [int] $RegressionTimeoutSeconds = 270
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = "frozen-protocol-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

$runDir = Join-Path $repoRoot ("Artifacts\CharacterLifecycleRegression\" + $RunId)
if (Test-Path -LiteralPath $runDir) {
    throw "Refusing to overwrite existing regression output: $runDir"
}
New-Item -ItemType Directory -Path $runDir | Out-Null

$server = $null
$serverReady = $false
$serverStoppedByStopFile = $false
$serverStop = Join-Path $runDir "server.stop"
$serverOut = Join-Path $runDir "server.stdout.txt"
$serverErr = Join-Path $runDir "server.stderr.txt"
$hostStartOutput = @()
$hostStartExit = $null
$hostFailure = $null
$hostResult = $null
$status = "NOT_RUN"
$failureCode = "NOT_RUN"
$serverForcedStop = $false
$serverLoginAccepted = $false
$serverLoginRejected = $false
$serverLoginRejectCode = $null

try {
    # Windows treats environment-variable names case-insensitively, but the
    # process can still inherit both Path and PATH. Start-Process materializes
    # that environment into a case-insensitive dictionary and otherwise fails
    # before the isolated server can start.
    $processEnvironment = [Environment]::GetEnvironmentVariables()
    $pathKeys = @($processEnvironment.Keys | Where-Object { [string]$_ -ieq "PATH" })
    if ($pathKeys.Count -gt 1) {
        $canonicalPath = [string]$processEnvironment["Path"]
        if ([string]::IsNullOrWhiteSpace($canonicalPath)) {
            $canonicalPath = [string]$processEnvironment["PATH"]
        }

        foreach ($pathKey in $pathKeys) {
            [Environment]::SetEnvironmentVariable(
                [string]$pathKey,
                $null,
                [EnvironmentVariableTarget]::Process)
        }

        [Environment]::SetEnvironmentVariable(
            "Path",
            $canonicalPath,
            [EnvironmentVariableTarget]::Process)
    }

    # Resolve the DPAPI-backed database secret after environment normalization
    # so the resulting process variable is inherited by the server child.
    $null = Initialize-God2DatabasePasswordEnvironment

    $serverExe = Get-God2ServerExecutablePath
    $serverArgs = '--stop-file "' + $serverStop + '"'
    $server = Start-Process `
        -FilePath $serverExe `
        -ArgumentList $serverArgs `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr `
        -WindowStyle Hidden `
        -PassThru

    $deadline = (Get-Date).AddSeconds($ServerReadyTimeoutSeconds)
    do {
        if ($server.HasExited) { break }
        if (Test-God2ProcessOwnsTcpListener -ProcessId $server.Id -Port 2592 -ExpectedLocalAddress "127.0.0.1") {
            $serverReady = $true
            break
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    if (-not $serverReady) {
        $status = "BLOCKED"
        $failureCode = "SERVER_NOT_READY_FOR_FROZEN_PROTOCOL_REGRESSION"
        return
    }

    $commandPath = Get-God2AutomationCommandPath
    $resolvedLauncherProfilePath = $null
    if (-not [string]::IsNullOrWhiteSpace($LauncherProfilePath)) {
        $resolvedLauncherProfilePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $LauncherProfilePath))
        $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedLauncherProfilePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $resolvedLauncherProfilePath -PathType Leaf)) {
            throw "Launcher profile must be an existing file under the repository root."
        }
    }
    Write-God2AtomicJson -Value ([ordered]@{
        command = "RunFrozenRegressionAndExit"
        testAccountKey = $TestAccountKey
        launcherProfilePath = $resolvedLauncherProfilePath
        runDir = $runDir
        serverReady = $true
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }) -Path $commandPath

    $hostStartOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds $HostReadyTimeoutSeconds 2>&1)
    $hostStartExit = $LASTEXITCODE

    $hostResultPath = Join-Path $runDir "frozen-regression-host-result.json"
    $statusPath = Get-God2AutomationStatusPath
    $deadline = (Get-Date).AddSeconds($RegressionTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $hostResultPath) {
            $hostResult = Read-God2JsonWithRetry -Path $hostResultPath
            break
        }

        if (Test-Path -LiteralPath $statusPath) {
            $hostStatus = Read-God2JsonWithRetry -Path $statusPath
            if ([string]$hostStatus.currentStage -in @(
                "HostFailed",
                "LoginFailed",
                "EntryClickRejected",
                "FrozenRegressionIncomplete"
            )) {
                $hostFailure = $hostStatus.lastError
                break
            }
        }

        Start-Sleep -Seconds 1
    }

    $serverReplayText = if (Test-Path -LiteralPath $serverOut) {
        Get-Content -LiteralPath $serverOut -Raw -ErrorAction SilentlyContinue
    }
    else {
        ""
    }
    $serverLoginAccepted = $serverReplayText -match "code=login_success_bootstrap_sent"
    $serverLoginRejected = $serverReplayText -match "Official login authentication rejected"
    if ($serverReplayText -match "Official login authentication rejected\..*code=([A-Za-z0-9_.-]+)") {
        $serverLoginRejectCode = [string]$Matches[1]
    }

    if ($serverLoginRejected -and -not $serverLoginAccepted) {
        $status = "BLOCKED"
        $failureCode = "SERVER_LOGIN_AUTHENTICATION_REJECTED"
        $hostFailure = if ($serverLoginRejectCode) { "ServerLoginRejected:$serverLoginRejectCode" } else { "ServerLoginAuthenticationRejected" }
    }
    elseif ($hostResult) {
        $status = if ($serverLoginAccepted -and [bool]$hostResult.worldReady -and
            [int]$hostResult.heartbeatCount -ge 60) { "PASS" } else { "BLOCKED" }
        $failureCode = if ($status -eq "PASS") {
            "NONE"
        }
        elseif (-not $serverLoginAccepted) {
            "SERVER_LOGIN_SUCCESS_NOT_OBSERVED"
        }
        else {
            "FROZEN_PROTOCOL_REGRESSION_WORLD_OR_HEARTBEAT_NOT_READY"
        }
    }
    else {
        $status = "BLOCKED"
        $failureCode = if ($hostFailure) {
            "AUTOMATION_HOST_REPORTED_TERMINAL_FAILURE"
        }
        else {
            "AUTOMATION_HOST_DID_NOT_PRODUCE_FROZEN_REGRESSION_RESULT"
        }
    }
}
catch {
    $status = "BLOCKED"
    $failureCode = "FROZEN_PROTOCOL_REGRESSION_SCRIPT_FAILED"
    Write-God2AtomicJson -Value ([ordered]@{
        message = $_.Exception.Message
        script = $_.InvocationInfo.ScriptName
        line = $_.InvocationInfo.ScriptLineNumber
        command = $_.InvocationInfo.Line
        position = $_.InvocationInfo.PositionMessage
    }) -Path (Join-Path $runDir "script-error.json")
}
finally {
    if (Test-Path -LiteralPath (Get-God2AutomationCommandPath)) {
        Remove-Item -LiteralPath (Get-God2AutomationCommandPath) -Force -ErrorAction SilentlyContinue
    }

    if ($server -and -not $server.HasExited) {
        New-Item -ItemType File -Force -Path $serverStop | Out-Null
        $serverStoppedByStopFile = $server.WaitForExit(30000)
        $server.Refresh()
        if (-not $serverStoppedByStopFile) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $serverForcedStop = $true
            $server.Refresh()
        }
    }
    elseif ($server) {
        $server.Refresh()
    }

    $remainingClientCount = @((Get-Process -Name God2_opt -ErrorAction SilentlyContinue)).Count
    $remainingServerCount = @((Get-Process -Name "God2 Classic Server" -ErrorAction SilentlyContinue)).Count
    $launcherCount = @((Get-Process -Name Launcher,God2ClassicLauncher -ErrorAction SilentlyContinue)).Count
    $serverExitCode = if ($server -and $server.HasExited) { $server.ExitCode } else { $null }
    $serverStdoutText = if (Test-Path -LiteralPath $serverOut) { Get-Content -LiteralPath $serverOut -Raw -ErrorAction SilentlyContinue } else { "" }
    $serverObservedCleanShutdown = $serverStdoutText -match "Network host stopped after all connection tasks completed"
    $serverCleanExit = $serverReady -and ($remainingServerCount -eq 0) -and (-not $serverForcedStop) -and ($serverObservedCleanShutdown -or ($server -and $server.HasExited -and $serverExitCode -eq 0))
    $stopCommandCompleted = $null -ne $hostResult -and $remainingClientCount -eq 0

    $summary = [ordered]@{
        runId = $RunId
        testAccountKey = $TestAccountKey
        status = $status
        failureCode = $failureCode
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        serverReady = $serverReady
        serverCleanExit = $serverCleanExit
        serverExitCode = $serverExitCode
        serverObservedCleanShutdown = $serverObservedCleanShutdown
        serverForcedStop = $serverForcedStop
        loginSuccess = if ($hostResult) { $serverLoginAccepted -and [bool]$hostResult.loginSuccess } else { $false }
        characterSelect = if ($hostResult) { $serverLoginAccepted -and [bool]$hostResult.characterSelect } else { $false }
        enterWorld = if ($hostResult) { [bool]$hostResult.enterWorld } else { $false }
        worldReady = if ($hostResult) { [bool]$hostResult.worldReady } else { $false }
        heartbeatCount = if ($hostResult) { [int]$hostResult.heartbeatCount } else { 0 }
        heartbeatDurationSeconds = if ($hostResult) { $hostResult.heartbeatDurationSeconds } else { 0 }
        firstBlocker = if ($hostResult) { $hostResult.firstBlocker } elseif ($serverLoginRejected) { $hostFailure } else { $hostFailure }
        serverLoginAccepted = $serverLoginAccepted
        serverLoginRejected = $serverLoginRejected
        serverLoginRejectCode = $serverLoginRejectCode
        secondaryLoginRejection = ($serverLoginAccepted -and $serverLoginRejected)
        stopCommandCompleted = $stopCommandCompleted
        remainingClientCount = $remainingClientCount
        remainingServerCount = $remainingServerCount
        launcherCount = $launcherCount
        hostStartExit = $hostStartExit
        hostStartOutput = $hostStartOutput
        hostFailure = $hostFailure
        hostResultPresent = ($null -ne $hostResult)
        fakeNetworkBytes = 0
        userManualOperation = "NOT REQUIRED"
        runDir = "Artifacts/CharacterLifecycleRegression/$RunId"
    }

    Write-God2AtomicJson -Value $summary -Path (Join-Path $runDir "frozen-regression-summary.json")
    $summary | ConvertTo-Json -Depth 16
}
