param(
    [string] $RunId = "",
    [int] $ServerReadyTimeoutSeconds = 45,
    [int] $HostReadyTimeoutSeconds = 35,
    [int] $ProbeTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = "path1-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

$runDir = Join-Path $repoRoot ("Artifacts\CharacterLifecycleObservation\" + $RunId)
$stableRoot = Join-Path $repoRoot "Artifacts\CharacterLifecycleUiAutomation"
$stableStatus = Join-Path $stableRoot "status.json"
New-Item -ItemType Directory -Force -Path $runDir,$stableRoot | Out-Null
Remove-Item -LiteralPath $stableStatus -Force -ErrorAction SilentlyContinue

$server = $null
$serverReady = $false
$serverStop = Join-Path $runDir "server.stop"
$serverOut = Join-Path $runDir "server.stdout.txt"
$serverErr = Join-Path $runDir "server.stderr.txt"
$hostStartOutput = @()
$hostStartExit = $null
$stopOutput = @()
$stopExit = $null
$probeStatus = $null
$hostFailure = $null
$failureCode = "NOT_RUN"
$scriptStatus = "NOT_RUN"

function Write-Path1Status {
    param(
        [string] $Status,
        [string] $FailureCode,
        [object] $ProbeStatus = $null
    )

    $value = [ordered]@{
        SchemaVersion = 1
        Status = $Status
        Path = "AutomatedOfficialClientUiCapture"
        FailureCode = $FailureCode
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        RunId = $RunId
        ArtifactRoot = ("Artifacts/CharacterLifecycleObservation/" + $RunId)
        ServerReady = $serverReady
        HostStartExit = $hostStartExit
        HostFailure = $hostFailure
        ProbeStatusFound = ($null -ne $ProbeStatus)
        ProbeStatus = $ProbeStatus
        SubmitAttempted = if ($ProbeStatus) { [bool]$ProbeStatus.SubmitAttempted } else { $false }
        PacketCaptureAttempted = if ($ProbeStatus) { [bool]$ProbeStatus.PacketCaptureAttempted } else { $false }
        OfficialClientCreate = if ($ProbeStatus) { [string]$ProbeStatus.OfficialClientCreate } else { "BLOCKED_BEFORE_CAPTURE" }
        OfficialClientDelete = if ($ProbeStatus) { [string]$ProbeStatus.OfficialClientDelete } else { "BLOCKED_BEFORE_CAPTURE" }
        UserManualOperation = "NOT REQUIRED"
    }
    Write-God2AtomicJson -Value $value -Path $stableStatus
    return [pscustomobject]$value
}

try {
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
        $scriptStatus = "BLOCKED"
        $failureCode = "SERVER_NOT_READY_FOR_CHARACTER_LIFECYCLE_UI_PROBE"
        $probeStatus = Write-Path1Status -Status $scriptStatus -FailureCode $failureCode
        return
    }

    $commandPath = Get-God2AutomationCommandPath
    Write-God2AtomicJson -Value ([ordered]@{
        command = "CharacterLifecycleUiProbe"
        runDir = $runDir
        serverReady = $serverReady
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }) -Path $commandPath

    $hostStartOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds $HostReadyTimeoutSeconds 2>&1)
    $hostStartExit = $LASTEXITCODE

    $statusPath = Get-God2AutomationStatusPath
    $deadline = (Get-Date).AddSeconds($ProbeTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $stableStatus) {
            $probeStatus = Get-Content -LiteralPath $stableStatus -Raw | ConvertFrom-Json
            break
        }

        if (Test-Path -LiteralPath $statusPath) {
            $hostStatus = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
            if ([string]$hostStatus.currentStage -eq "HostFailed") {
                $hostFailure = $hostStatus.lastError
                break
            }
        }

        Start-Sleep -Seconds 1
    }

    if ($probeStatus) {
        $scriptStatus = [string]$probeStatus.Status
        $failureCode = [string]$probeStatus.FailureCode
    }
    else {
        $scriptStatus = "BLOCKED"
        $failureCode = "AUTOMATION_HOST_DID_NOT_PRODUCE_CHARACTER_LIFECYCLE_UI_STATUS"
        if ($hostFailure -and ([string]$hostFailure.message -match "StartGame did not create")) {
            $failureCode = "START_GAME_DID_NOT_CREATE_GOD2_OPT"
        }
        $probeStatus = Write-Path1Status -Status $scriptStatus -FailureCode $failureCode
    }
}
catch {
    $scriptStatus = "BLOCKED"
    $failureCode = "CHARACTER_LIFECYCLE_UI_PROBE_SCRIPT_FAILED"
    $probeStatus = Write-Path1Status -Status $scriptStatus -FailureCode $failureCode
    Write-God2AtomicJson -Value ([ordered]@{
        message = $_.Exception.Message
        script = $_.InvocationInfo.ScriptName
        line = $_.InvocationInfo.ScriptLineNumber
        command = $_.InvocationInfo.Line
        position = $_.InvocationInfo.PositionMessage
    }) -Path (Join-Path $runDir "script-error.json")
}
finally {
    try {
        $commandPath = Get-God2AutomationCommandPath
        Write-God2AtomicJson -Value ([ordered]@{
            command = "StopClientAndExit"
            createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }) -Path $commandPath
        $stopOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Start-God2AutomationHost.ps1") -TimeoutSeconds $HostReadyTimeoutSeconds 2>&1)
        $stopExit = $LASTEXITCODE
    }
    catch {
        $stopOutput = @($_.Exception.Message)
        $stopExit = 1
    }

    if ($server -and -not $server.HasExited) {
        New-Item -ItemType File -Force -Path $serverStop | Out-Null
        if (-not $server.WaitForExit(30000)) {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
    }

    $serverExitCode = if ($server -and $server.HasExited) { $server.ExitCode } else { $null }
    $summary = [ordered]@{
        RunId = $RunId
        Status = $scriptStatus
        FailureCode = $failureCode
        RunDir = ("Artifacts/CharacterLifecycleObservation/" + $RunId)
        ServerReady = $serverReady
        ServerPid = if ($server) { $server.Id } else { $null }
        ServerExitCode = $serverExitCode
        HostStartExit = $hostStartExit
        HostStartOutput = $hostStartOutput
        HostFailure = $hostFailure
        ProbeStatusFound = ($null -ne $probeStatus)
        ProbeStatus = $probeStatus
        StopHostExit = $stopExit
        StopHostOutput = $stopOutput
        RemainingClientCount = @((Get-Process -Name God2_opt -ErrorAction SilentlyContinue)).Count
        RemainingServerCount = @((Get-Process -Name "God2 Classic Server" -ErrorAction SilentlyContinue)).Count
        LauncherCount = @((Get-Process -Name Launcher -ErrorAction SilentlyContinue)).Count
        CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    Write-God2AtomicJson -Value $summary -Path (Join-Path $runDir "path1-ui-probe-summary.json")
    $summary | ConvertTo-Json -Depth 16
}
