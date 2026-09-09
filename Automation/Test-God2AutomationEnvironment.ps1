param(
    [int] $TimeoutSeconds = 3,
    [switch] $SkipDatabaseNetworkProbe
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$generatedAtUtc = [DateTimeOffset]::UtcNow
$artifactRoot = Join-Path $repoRoot ("Artifacts\AutomationEnvironment\preflight-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$matrix = @()
$checks = @()
$staticDataCounts = @()
$databaseProbeResult = $null
$serverReadyProbeResult = $null

function ConvertTo-God2DisplayPath {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return "<missing>"
    }

    try {
        $full = [IO.Path]::GetFullPath($Path)
        $root = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/')
        if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            return $full.Substring($root.Length).TrimStart('\', '/')
        }
    }
    catch {
    }

    return "<external-path>"
}

function Add-MatrixRow {
    param(
        [string] $SettingName,
        [string] $Source,
        [string] $Required,
        [string] $Default,
        [string] $Secret,
        [string] $ConsumedBy,
        [string] $ValidationRule,
        [string] $CurrentStatus,
        [string] $FailureCode = ""
    )

    $script:matrix += [pscustomobject]@{
        settingName = $SettingName
        source = $Source
        required = $Required
        default = $Default
        secret = $Secret
        consumedBy = $ConsumedBy
        validationRule = $ValidationRule
        currentStatus = $CurrentStatus
        failureCode = $FailureCode
    }
}

function Add-Check {
    param(
        [string] $Name,
        [ValidateSet("PASS", "WARNING", "BLOCKED")] [string] $Status,
        [string] $ErrorCode = "",
        [string] $SettingName = "",
        [string] $Source = "",
        [string] $Expected = "",
        [string] $ActualState = "",
        [string] $SafeRemediation = ""
    )

    $script:checks += [pscustomobject]@{
        name = $Name
        status = $Status
        errorCode = $ErrorCode
        settingName = $SettingName
        source = $Source
        expected = $Expected
        actualState = $ActualState
        safeRemediation = $SafeRemediation
    }
}

function Test-WritableDirectory {
    param([string] $Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    $testPath = Join-Path $Path ("preflight-write-" + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        "ok" | Set-Content -LiteralPath $testPath -Encoding ASCII
        return Test-Path -LiteralPath $testPath
    }
    finally {
        Remove-Item -LiteralPath $testPath -Force -ErrorAction SilentlyContinue
    }
}

function Redact-God2SecretText {
    param(
        [string] $Text,
        [string] $EnvironmentVariableName
    )

    $value = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, "Process")
    if (-not [string]::IsNullOrEmpty($value)) {
        return $Text.Replace($value, "<secret>")
    }

    return $Text
}

function Test-God2TcpConnect {
    param(
        [string] $HostName,
        [int] $Port,
        [int] $Timeout
    )

    $client = New-Object Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne([Math]::Max(1, $Timeout) * 1000, $false)) {
            $client.Close()
            throw "TCP connection timed out."
        }

        $client.EndConnect($async)
        return $true
    }
    finally {
        $client.Close()
    }
}

function Get-God2AutomationProbeToolPath {
    $repoRoot = Get-God2RepoRoot
    $debug = Join-Path $repoRoot "tools\God2.AutomationEnvironmentProbe\bin\Debug\net10.0\God2.AutomationEnvironmentProbe.dll"
    if (Test-Path -LiteralPath $debug) {
        return $debug
    }

    $release = Join-Path $repoRoot "tools\God2.AutomationEnvironmentProbe\bin\Release\net10.0\God2.AutomationEnvironmentProbe.dll"
    if (Test-Path -LiteralPath $release) {
        return $release
    }

    return $null
}

function Invoke-God2AutomationProbe {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $OutputPath
    )

    $tool = Get-God2AutomationProbeToolPath
    if (-not $tool) {
        throw "God2.AutomationEnvironmentProbe.dll was not found. Build God2ClassicServer.sln first."
    }

    $output = @(& dotnet $tool @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    $text | Set-Content -LiteralPath $OutputPath -Encoding UTF8

    try {
        $jsonStart = $text.IndexOf("{", [StringComparison]::Ordinal)
        if ($jsonStart -lt 0) {
            throw "Probe did not emit JSON."
        }

        $parsed = $text.Substring($jsonStart) | ConvertFrom-Json
        return [pscustomobject]@{
            exitCode = $exitCode
            rawOutput = $text
            value = $parsed
        }
    }
    catch {
        throw "Probe output was not parseable JSON. ExitCode=$exitCode; Output=$text"
    }
}

function Add-God2ProbeChecks {
    param([object] $ProbeResult)

    foreach ($check in @($ProbeResult.Checks)) {
        Add-Check `
            -Name ([string]$check.Name) `
            -Status ([string]$check.Status) `
            -ErrorCode ([string]$check.ErrorCode) `
            -SettingName ([string]$check.SettingName) `
            -Source ([string]$check.Source) `
            -Expected ([string]$check.Expected) `
            -ActualState ([string]$check.ActualState) `
            -SafeRemediation ([string]$check.SafeRemediation)
    }
}

$databaseConfig = $null
try {
    $databaseConfig = Get-God2DatabaseConfig
    Add-Check -Name "Configuration Resolve" -Status "PASS" -SettingName "Server Config Path" -Source "config/*.json" -Expected "Readable JSON config" -ActualState "config directory resolved"
}
catch {
    Add-Check -Name "Configuration Resolve" -Status "BLOCKED" -ErrorCode "configuration.missing" -SettingName "Server Config Path" -Source "config/*.json" -Expected "Readable JSON config" -ActualState $_.Exception.Message -SafeRemediation "還原 config 目錄後再啟動 Server。"
}

if ($databaseConfig) {
    $secretStatus = Initialize-God2DatabasePasswordEnvironment -EnvironmentVariableName ([string]$databaseConfig.passwordEnvironmentVariable)
    $secretFailure = if ($secretStatus.hasSecret) { "" } else { [string]$secretStatus.failureCode }
    Add-MatrixRow "GOD2_DB_PASSWORD" $secretStatus.source "Required" "None" "Secret" "ConsoleHost, MariaDB repositories, Automation Host" "Resolved before server start; non-empty; never printed" ($(if ($secretStatus.hasSecret) { "PASS" } else { "BLOCKED" })) $secretFailure
    Add-MatrixRow "DB Host" "config/database.json + GOD2_DATABASE_HOST override" "Required" "127.0.0.1" "Non-secret" "MariaDbDatabaseBootstrapper" "Non-empty hostname or IP" ($(if ([string]::IsNullOrWhiteSpace($databaseConfig.host)) { "BLOCKED" } else { "PASS" })) "configuration.required"
    Add-MatrixRow "DB Port" "config/database.json + GOD2_DATABASE_PORT override" "Required" "3306" "Non-secret" "MariaDbDatabaseBootstrapper" "1..65535" ($(if ([int]$databaseConfig.port -ge 1 -and [int]$databaseConfig.port -le 65535) { "PASS" } else { "BLOCKED" })) "configuration.range"
    Add-MatrixRow "DB Schema/Database" "config/database.json + GOD2_DATABASE_NAME override" "Required" "god2" "Non-secret" "MariaDbDatabaseBootstrapper, migrations, static loader" "Non-empty [A-Za-z0-9_]" ($(if ([string]$databaseConfig.databaseName -match "^[A-Za-z0-9_]+$") { "PASS" } else { "BLOCKED" })) "mariadb.database_name_invalid"
    Add-MatrixRow "DB Username" "config/database.json + GOD2_DATABASE_USERNAME override" "Required" "god2_server" "Non-secret" "MariaDbDatabaseBootstrapper" "Non-empty MariaDB user" ($(if ([string]::IsNullOrWhiteSpace($databaseConfig.username)) { "BLOCKED" } else { "PASS" })) "configuration.required"
    Add-MatrixRow "SSL Mode" "MariaDbDatabaseBootstrapper.BuildConnectionString" "Required" "Preferred" "Non-secret" "MySqlConnector" "Must remain a legal MySqlSslMode" "PASS" ""

    if ($secretStatus.hasSecret) {
        Add-Check -Name "Secret Resolve" -Status "PASS" -SettingName "GOD2_DB_PASSWORD" -Source $secretStatus.source -Expected "Secret available without plaintext output" -ActualState "secret available"
    }
    else {
        Add-Check -Name "Secret Resolve" -Status "BLOCKED" -ErrorCode $secretStatus.failureCode -SettingName "GOD2_DB_PASSWORD" -Source $secretStatus.source -Expected "Secret available without plaintext output" -ActualState "secret missing" -SafeRemediation $secretStatus.diagnosticZh
    }
}

$serverExe = Get-God2ServerExecutablePath
$serverExists = Test-Path -LiteralPath $serverExe
$serverReleaseStatus = Test-God2ServerReleaseManifest
$configDir = Join-Path $repoRoot "config"
$runtimeImportPath = Join-Path $repoRoot "db\imports\official"
$statusPath = Get-God2AutomationStatusPath
$commandPath = Get-God2AutomationCommandPath
$logPath = Join-Path $repoRoot "logs"
$artifactPath = Join-Path $repoRoot "Artifacts"
$launcherProfilePath = Get-God2LauncherProfilePath
$launcherProfile = $null
if (Test-Path -LiteralPath $launcherProfilePath) {
    try {
        $launcherProfile = Get-Content -LiteralPath $launcherProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
    }
}

Add-MatrixRow "Server Config Path" "Project root" "Required" "config" "Non-secret" "ConsoleHost" "Directory exists" ($(if (Test-Path -LiteralPath $configDir) { "PASS" } else { "BLOCKED" })) "configuration.missing"
Add-MatrixRow "Runtime Import Path" "Project root" "Required" "db/imports/official" "Non-secret" "Official import, runtime data recovery" "Directory exists" ($(if (Test-Path -LiteralPath $runtimeImportPath) { "PASS" } else { "BLOCKED" })) "runtime_import.path_missing"
Add-MatrixRow "Automation Status Path" "Project root" "Required" "Automation/State/host-status.json" "Non-secret" "Automation Host" "Parent directory writable" ($(if (Test-WritableDirectory -Path (Split-Path -Parent $statusPath)) { "PASS" } else { "BLOCKED" })) "automation.status_path_unwritable"
Add-MatrixRow "Automation Command Path" "Project root" "Required" "Automation/State/host-command.json" "Non-secret" "Automation Host" "Parent directory writable" ($(if (Test-WritableDirectory -Path (Split-Path -Parent $commandPath)) { "PASS" } else { "BLOCKED" })) "automation.command_path_unwritable"
Add-MatrixRow "Log Path" "Project root" "Required" "logs" "Non-secret" "Server run capture" "Directory writable" ($(if (Test-WritableDirectory -Path $logPath) { "PASS" } else { "BLOCKED" })) "automation.log_path_unwritable"
Add-MatrixRow "Artifact Path" "Project root" "Required" "Artifacts" "Non-secret" "Automation and regression evidence" "Directory writable" ($(if (Test-WritableDirectory -Path $artifactPath) { "PASS" } else { "BLOCKED" })) "automation.artifact_path_unwritable"

if ($serverExists -and $serverReleaseStatus.succeeded) {
    Add-Check -Name "Server Binary" -Status "PASS" -SettingName "Server EXE/DLL" -Source "verified release manifest" -Expected "Published server exists and all hashes match" -ActualState ((ConvertTo-God2DisplayPath $serverExe) + "; build=" + $serverReleaseStatus.buildId)
}
else {
    Add-Check -Name "Server Binary" -Status "BLOCKED" -ErrorCode $serverReleaseStatus.errorCode -SettingName "Server EXE/DLL" -Source "verified release manifest" -Expected "Published server exists and all hashes match" -ActualState (ConvertTo-God2DisplayPath $serverExe) -SafeRemediation "執行 Automation/Publish-God2ServerRelease.ps1。"
}

$launcher = Get-Process -Name Launcher -ErrorAction SilentlyContinue | Sort-Object Id | Select-Object -Last 1
$launcherStatus = "BLOCKED"
$launcherFailure = "launcher.path_missing"
if ($launcher) {
    $launcherStatus = "PASS"
    $launcherFailure = ""
    Add-Check -Name "Launcher Process" -Status "PASS" -SettingName "Launcher Path" -Source "ActiveProcess" -Expected "Launcher exists and is not closed by preflight" -ActualState "active Launcher process found"
}
elseif ($launcherProfile -and -not [string]::IsNullOrWhiteSpace([string]$launcherProfile.targetExecutable) -and (Test-Path -LiteralPath ([string]$launcherProfile.targetExecutable))) {
    $launcherStatus = "PASS"
    $launcherFailure = ""
    Add-Check -Name "Launcher Path" -Status "PASS" -SettingName "Launcher Path" -Source "launcher-profile.json" -Expected "Launcher executable exists" -ActualState "profile target exists"
}
else {
    Add-Check -Name "Launcher Path" -Status "BLOCKED" -ErrorCode "launcher.path_missing" -SettingName "Launcher Path" -Source "ActiveProcess or launcher-profile.json" -Expected "Launcher active or profile target exists" -ActualState "not found" -SafeRemediation "啟動 Launcher 或重新錄製 launcher-profile.json；不得在腳本中硬編碼本機路徑。"
}

$client = Get-Process -Name God2_opt -ErrorAction SilentlyContinue | Sort-Object Id | Select-Object -Last 1
$clientStatus = "WARNING"
$clientFailure = "client.not_running"
if ($client) {
    $clientStatus = "PASS"
    $clientFailure = ""
    Add-Check -Name "Client Process" -Status "PASS" -SettingName "Client Path" -Source "ActiveProcess" -Expected "God2_opt manageable by host" -ActualState "active client process found"
}
elseif ($launcherProfile -and -not [string]::IsNullOrWhiteSpace([string]$launcherProfile.targetExecutable)) {
    $candidateClient = Join-Path (Split-Path -Parent ([string]$launcherProfile.targetExecutable)) "God2_opt.exe"
    if (Test-Path -LiteralPath $candidateClient) {
        $clientStatus = "PASS"
        $clientFailure = ""
        Add-Check -Name "Client Path" -Status "PASS" -SettingName "Client Path" -Source "launcher-profile.json" -Expected "God2_opt.exe exists next to Launcher" -ActualState "client executable exists"
    }
    else {
        Add-Check -Name "Client Path" -Status "WARNING" -ErrorCode "client.path_not_observed" -SettingName "Client Path" -Source "launcher-profile.json" -Expected "God2_opt.exe path discoverable" -ActualState "client process not running and executable not observed" -SafeRemediation "Launcher 可稍後建立 God2_opt；若 Full Regression 失敗，重新錄製 Launcher profile。"
    }
}
else {
    Add-Check -Name "Client Path" -Status "WARNING" -ErrorCode "client.path_not_observed" -SettingName "Client Path" -Source "ActiveProcess or launcher-profile.json" -Expected "God2_opt path discoverable" -ActualState "not observed" -SafeRemediation "Launcher 可稍後建立 God2_opt；若 Full Regression 失敗，重新錄製 Launcher profile。"
}

Add-MatrixRow "Launcher Path" "ActiveProcess or launcher-profile.json" "Required for client regression" "None" "Non-secret" "Automation Host" "Active process or executable path exists" $launcherStatus $launcherFailure
Add-MatrixRow "Client Path" "ActiveProcess or launcher-profile.json" "Required for client regression" "None" "Non-secret" "Automation Host" "God2_opt running or executable path can be derived" $clientStatus $clientFailure

$taskValidation = Get-God2AutomationTaskValidation
if ($taskValidation.isCorrect) {
    Add-Check -Name "Scheduled Task Contract" -Status "PASS" -SettingName "Automation Host task" -Source "Task Scheduler" -Expected "HighestAvailable, InteractiveToken, relative action, repo working directory" -ActualState "scheduled task correct"
}
else {
    Add-Check -Name "Scheduled Task Contract" -Status "BLOCKED" -ErrorCode "automation.task_contract_invalid" -SettingName "Automation Host task" -Source "Task Scheduler" -Expected "HighestAvailable, InteractiveToken, relative action, repo working directory" -ActualState $taskValidation.diagnosticZh -SafeRemediation "執行 Automation/Register-God2AutomationHost.ps1 修復排程工作。"
}

if ($databaseConfig) {
    $tcpReady = $false
    if ($SkipDatabaseNetworkProbe) {
        Add-Check -Name "MariaDB TCP Connect" -Status "WARNING" -ErrorCode "mariadb.tcp_skipped" -SettingName "DB Host/Port" -Source "preflight parameter" -Expected "TCP connect attempted" -ActualState "skipped by caller"
    }
    else {
        try {
            Test-God2TcpConnect -HostName ([string]$databaseConfig.host) -Port ([int]$databaseConfig.port) -Timeout $TimeoutSeconds | Out-Null
            $tcpReady = $true
            Add-Check -Name "MariaDB TCP Connect" -Status "PASS" -SettingName "DB Host/Port" -Source "TCP" -Expected "Connect within timeout" -ActualState "tcp connect succeeded"
        }
        catch {
            Add-Check -Name "MariaDB TCP Connect" -Status "BLOCKED" -ErrorCode "mariadb.tcp_connect_failed" -SettingName "DB Host/Port" -Source "TCP" -Expected "Connect within timeout" -ActualState $_.Exception.Message -SafeRemediation "確認 MariaDB 已啟動、host/port 正確且防火牆允許本機連線。"
        }
    }

    if (-not $secretStatus.hasSecret) {
        Add-Check -Name "MariaDB Authentication" -Status "BLOCKED" -ErrorCode "mariadb.password_missing" -SettingName "GOD2_DB_PASSWORD" -Source $secretStatus.source -Expected "Authenticate with resolved secret" -ActualState "not run because password is missing" -SafeRemediation $secretStatus.diagnosticZh
    }
    elseif (-not $tcpReady -and -not $SkipDatabaseNetworkProbe) {
        Add-Check -Name "MariaDB Authentication" -Status "BLOCKED" -ErrorCode "mariadb.tcp_connect_failed" -SettingName "DB Host/Port" -Source "TCP" -Expected "Authenticate only after TCP connect" -ActualState "not run because TCP failed" -SafeRemediation "先修復 MariaDB TCP 連線。"
    }
    elseif ($serverExists) {
        try {
            $databaseProbe = Invoke-God2AutomationProbe `
                -Arguments @(
                    "--mode", "mariadb-preflight",
                    "--host", ([string]$databaseConfig.host),
                    "--port", ([string]$databaseConfig.port),
                    "--database", ([string]$databaseConfig.databaseName),
                    "--username", ([string]$databaseConfig.username),
                    "--password-env", ([string]$databaseConfig.passwordEnvironmentVariable),
                    "--timeout", ([string][Math]::Max(1, [int]$databaseConfig.connectionTimeoutSeconds)
                    )) `
                -OutputPath (Join-Path $artifactRoot "mariadb-preflight-probe.json")
            $databaseProbeResult = $databaseProbe.value
            Add-God2ProbeChecks -ProbeResult $databaseProbeResult

            foreach ($item in @($databaseProbeResult.StaticDataCounts)) {
                $staticDataCounts += [pscustomobject]@{
                    table = [string]$item.Table
                    count = [int64]$item.Count
                }
            }

            if ([string]$databaseProbeResult.OverallStatus -eq "PASS") {
                $serverProbe = Invoke-God2AutomationProbe `
                    -Arguments @(
                        "--mode", "server-ready",
                        "--base-directory", $repoRoot,
                        "--timeout", "60"
                    ) `
                    -OutputPath (Join-Path $artifactRoot "server-ready-probe.json")
                $serverReadyProbeResult = $serverProbe.value
                Add-God2ProbeChecks -ProbeResult $serverReadyProbeResult
            }
        }
        catch {
            Add-Check -Name "MariaDB Preflight" -Status "BLOCKED" -ErrorCode "automation.probe_failed" -SettingName "Database" -Source "God2.AutomationEnvironmentProbe" -Expected "Probe emits JSON and validates MariaDB" -ActualState (Redact-God2SecretText -Text $_.Exception.Message -EnvironmentVariableName ([string]$databaseConfig.passwordEnvironmentVariable)) -SafeRemediation "Build the automation probe and inspect its JSON artifact for loader or database diagnostics."
        }
    }
}

foreach ($row in $matrix) {
    if ($row.currentStatus -eq "PASS") {
        $row.failureCode = ""
    }
}

$blockedCount = @($checks | Where-Object { $_.status -eq "BLOCKED" }).Count + @($matrix | Where-Object { $_.currentStatus -eq "BLOCKED" }).Count
$warningCount = @($checks | Where-Object { $_.status -eq "WARNING" }).Count + @($matrix | Where-Object { $_.currentStatus -eq "WARNING" }).Count
$overall = if ($blockedCount -gt 0) { "BLOCKED" } elseif ($warningCount -gt 0) { "WARNING" } else { "PASS" }

$result = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = $generatedAtUtc.ToString("o")
    overallStatus = $overall
    blockedCount = $blockedCount
    warningCount = $warningCount
    matrix = $matrix
    checks = $checks
    staticDataCounts = $staticDataCounts
    redaction = [pscustomobject]@{
        secretValueEmitted = $false
        statusContainsSecretValue = $false
        taskArgumentsContainSecret = $false
    }
    artifactRoot = ConvertTo-God2DisplayPath $artifactRoot
}

$resultPath = Join-Path $artifactRoot "automation-environment-preflight.json"
Write-God2AtomicJson -Value $result -Path $resultPath
$result | ConvertTo-Json -Depth 12

if ($overall -eq "BLOCKED") {
    exit 1
}


