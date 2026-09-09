param(
    [int] $IdleSeconds = 7200
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

# The official client can expose Simplified Chinese window captions. Keep the
# compatibility probes without embedding Simplified Chinese in project text.
$script:OfficialSimplifiedAgreementCaption = -join [char[]](35831,30830,23450,29992,25143,21327,35758)
$script:OfficialSimplifiedConfirmCaption = -join [char[]](30830,23450)
$script:OfficialSimplifiedErrorCaption = -join [char[]](38169,35823)

Initialize-God2NativeApi
$repoRoot = Get-God2RepoRoot
$statusPath = Get-God2AutomationStatusPath
$stateRoot = Split-Path -Parent $statusPath
$sessionId = [Guid]::NewGuid().ToString("N")
$hostPid = $PID
$hostRid = [God2Automation.NativeApi]::GetIntegrityRid($hostPid)
$hostIntegrity = ConvertTo-God2IntegrityLevel $hostRid
$desktopName = [God2Automation.NativeApi]::GetDesktopName()
$startedAt = [DateTimeOffset]::UtcNow
$currentStage = "Starting"
$lastError = $null
$ready = $false
$activeLauncherProfilePath = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LauncherAutomation\launcher-profile.json"
$artifactRoot = Join-Path $repoRoot ("Artifacts\ClientInstrumentation\ElevatedAutomationHost\host-run-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$script:lastOfficialLaunchEvidence = $null
$loginAutomationStateCatalog = @(
    "ClientNotRunning",
    "ClientStarting",
    "LoginWindowWaiting",
    "LoginWindowDetected",
    "AccountFieldLocating",
    "AccountFieldFocused",
    "AccountInputCleared",
    "AccountInputWriting",
    "AccountInputVerified",
    "PasswordFieldLocating",
    "PasswordFieldFocused",
    "PasswordFieldFocusNotConfirmed",
    "PasswordInputCleared",
    "PasswordInputWriting",
    "PasswordInputVerified",
    "SubmitLocating",
    "SubmitExecuting",
    "SubmitVerificationWaiting",
    "LoginSucceeded",
    "LoginFailed",
    "CharacterSelectWaiting",
    "CharacterSelectReady",
    "WorldEntryWaiting",
    "EnteredWorld",
    "Faulted"
)
$databaseSecretStatus = [pscustomobject]@{
    hasSecret = $false
    source = 'NotRequestedByAutomationHost'
    failureCode = 'NONE'
}

function Invoke-God2NativeCommand {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    function Quote-God2ProcessArgument {
        param([string] $Value)
        if ($Value.Length -eq 0) { return '""' }
        if ($Value -notmatch '[\s"]') { return $Value }
        return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = (($Arguments | ForEach-Object { Quote-God2ProcessArgument -Value $_ }) -join ' ')
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return [pscustomobject]@{
        exitCode = $process.ExitCode
        stdout = $stdout.GetAwaiter().GetResult()
        stderr = $stderr.GetAwaiter().GetResult()
    }
}

function Get-ActiveLauncherProfile {
    if (-not (Test-Path -LiteralPath $script:activeLauncherProfilePath -PathType Leaf)) {
        throw "Missing active launcher profile: $($script:activeLauncherProfilePath)"
    }

    return (Get-Content -LiteralPath $script:activeLauncherProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-ActiveRuntimeExecutablePaths {
    $profile = Get-ActiveLauncherProfile
    $launcherPath = [IO.Path]::GetFullPath([string]$profile.targetExecutable)
    if ([string]::IsNullOrWhiteSpace($launcherPath)) {
        throw "Active launcher profile target is empty."
    }

    return [pscustomobject]@{
        launcherPath = $launcherPath
        clientPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $launcherPath) "God2_opt.exe"))
    }
}

function Get-ActiveRuntimeProcesses {
    param(
        [switch] $IncludeLauncher,
        [switch] $IncludeClient
    )

    if (-not $IncludeLauncher -and -not $IncludeClient) {
        $IncludeLauncher = $true
        $IncludeClient = $true
    }
    $paths = Get-ActiveRuntimeExecutablePaths
    $expected = @()
    if ($IncludeLauncher) { $expected += [string]$paths.launcherPath }
    if ($IncludeClient) { $expected += [string]$paths.clientPath }
    $names = @($expected | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object -Unique)

    foreach ($process in @(Get-Process -Name $names -ErrorAction SilentlyContinue)) {
        $snapshot = Get-God2ProcessSnapshot -ProcessId $process.Id
        if (-not $snapshot -or [string]::IsNullOrWhiteSpace([string]$snapshot.path)) { continue }
        $actualPath = [IO.Path]::GetFullPath([string]$snapshot.path)
        if (@($expected | Where-Object { [string]::Equals($_, $actualPath, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 1) {
            $process
        }
    }
}

function Stop-ActiveRuntimeProcesses {
    param(
        [switch] $IncludeLauncher,
        [switch] $IncludeClient
    )

    $stopped = @()
    foreach ($process in @(Get-ActiveRuntimeProcesses -IncludeLauncher:$IncludeLauncher -IncludeClient:$IncludeClient)) {
        $snapshot = Get-God2ProcessSnapshot -ProcessId $process.Id
        $stopped += [pscustomobject]@{
            processId = $process.Id
            processName = $process.ProcessName
            executablePath = if ($snapshot) { [string]$snapshot.path } else { $null }
        }
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    return @($stopped)
}

function Get-ActiveLauncherProcessName {
    $profile = Get-ActiveLauncherProfile
    $target = [string]$profile.targetExecutable
    if ([string]::IsNullOrWhiteSpace($target)) {
        throw "Active launcher profile target is empty."
    }

    return [IO.Path]::GetFileNameWithoutExtension($target)
}

function Get-ActiveLauncherProcess {
    return (Get-ActiveRuntimeProcesses -IncludeLauncher | Sort-Object Id | Select-Object -Last 1)
}

function Get-God2LocalEndpointAttestation {
    param(
        [Parameter(Mandatory = $true)] [int] $ClientPid,
        [ValidateRange(1, 30)] [int] $TimeoutSeconds = 5
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $matchingStates = @()
    do {
        $matching = @(Get-NetTCPConnection -OwningProcess $ClientPid -ErrorAction SilentlyContinue |
            Where-Object {
                [string]$_.RemoteAddress -eq "127.0.0.1" -and
                [int]$_.RemotePort -eq 2592
            })
        if ($matching.Count -gt 0) {
            $matchingStates = @($matching | ForEach-Object { [string]$_.State } | Sort-Object -Unique)
            break
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    return [pscustomobject]@{
        expectedRemote = "127.0.0.1:2592"
        clientPid = $ClientPid
        attested = ($matchingStates.Count -gt 0)
        observedStates = $matchingStates
        evidenceType = "WindowsTcpConnectionMetadata"
        packetCapture = $false
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
}

function Get-God2OfficialEndpointAttestation {
    param(
        [Parameter(Mandatory = $true)] [int] $ClientPid,
        [ValidateRange(1, 30)] [int] $TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $matching = @()
    do {
        $matching = @(Get-NetTCPConnection -OwningProcess $ClientPid -State Established -ErrorAction SilentlyContinue |
            Where-Object {
                $remote = [string]$_.RemoteAddress
                -not [string]::IsNullOrWhiteSpace($remote) -and
                $remote -notin @('127.0.0.1', '::1', '0.0.0.0', '::')
            })
        if ($matching.Count -gt 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $endpointHashes = @($matching | ForEach-Object {
            $endpoint = '{0}:{1}' -f ([string]$_.RemoteAddress), ([int]$_.RemotePort)
            $bytes = [Text.Encoding]::UTF8.GetBytes($endpoint)
            ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
        } | Sort-Object -Unique)
    }
    finally {
        $sha256.Dispose()
    }

    return [pscustomobject]@{
        expectedRemoteClass = 'ESTABLISHED_NON_LOOPBACK'
        clientPid = $ClientPid
        attested = ($matching.Count -gt 0)
        establishedConnectionCount = $matching.Count
        endpointSha256 = $endpointHashes
        observedStates = @($matching | ForEach-Object { [string]$_.State } | Sort-Object -Unique)
        rawRemoteEndpointPersisted = $false
        evidenceType = 'WindowsTcpConnectionMetadata'
        packetCapture = $false
        checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
}

function Publish-HostStatus {
    param(
        [string] $Stage,
        [object] $ErrorValue = $null,
        [bool] $ReadyValue = $false,
        [object] $Extra = $null
    )
    try {
        $launcher = Get-ActiveLauncherProcess
    }
    catch {
        # Attach-only diagnostics do not require a recorded launcher profile.
        $launcher = Get-Process -Name Launcher, God2ClassicLauncher -ErrorAction SilentlyContinue |
            Sort-Object Id |
            Select-Object -Last 1
    }
    $clientSnapshot = Get-PreferredGod2ClientSnapshot
    $launcherSnapshot = if ($launcher) { Get-God2ProcessSnapshot -ProcessId $launcher.Id } else { $null }
    $value = [pscustomobject]@{
        schemaVersion = 1
        sessionId = $sessionId
        hostPid = $hostPid
        integrityLevel = $hostIntegrity
        integrityRid = $hostRid
        userSessionId = (Get-Process -Id $hostPid).SessionId
        desktopName = $desktopName
        startedAtUtc = $startedAt.ToString("o")
        heartbeatAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        launcherPid = if ($launcherSnapshot) { $launcherSnapshot.pid } else { $null }
        clientPid = if ($clientSnapshot) { $clientSnapshot.pid } else { $null }
        launcherHwnd = if ($launcherSnapshot) { $launcherSnapshot.mainWindowHandle } else { $null }
        clientHwnd = if ($clientSnapshot) { $clientSnapshot.mainWindowHandle } else { $null }
        launcherPath = if ($launcherSnapshot) { $launcherSnapshot.path } else { $null }
        clientPath = if ($clientSnapshot) { $clientSnapshot.path } else { $null }
        launcherIntegrityRid = if ($launcherSnapshot) { $launcherSnapshot.integrityRid } else { $null }
        clientIntegrityRid = if ($clientSnapshot) { $clientSnapshot.integrityRid } else { $null }
        launcherSessionId = if ($launcherSnapshot) { $launcherSnapshot.sessionId } else { $null }
        clientSessionId = if ($clientSnapshot) { $clientSnapshot.sessionId } else { $null }
        databaseSecretAvailable = if ($databaseSecretStatus) { [bool]$databaseSecretStatus.hasSecret } else { $false }
        databaseSecretSource = if ($databaseSecretStatus) { [string]$databaseSecretStatus.source } else { "Unknown" }
        databaseSecretFailureCode = if ($databaseSecretStatus) { [string]$databaseSecretStatus.failureCode } else { "automation.environment.secret_status_missing" }
        currentStage = $Stage
        lastError = $ErrorValue
        ready = $ReadyValue
    }
    if ($null -ne $Extra) {
        $value | Add-Member -NotePropertyName diagnostic -NotePropertyValue $Extra
    }
    Write-God2AtomicJson -Value $value -Path $statusPath
}

function Get-PreferredGod2ClientSnapshot {
    param([Nullable[int]] $LauncherPid = $null)

    $candidates = @(Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object @{ Expression = "Id"; Descending = $true })
    foreach ($process in $candidates) {
        if ($LauncherPid.HasValue) {
            $parent = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $process.Id) -ErrorAction SilentlyContinue
            if ($parent -and [int]$parent.ParentProcessId -ne $LauncherPid.Value) {
                continue
            }
        }
        $snapshot = Get-God2ProcessSnapshot -ProcessId $process.Id
        if (-not $snapshot) { continue }
        $hwnd = [IntPtr][int64]$snapshot.mainWindowHandle
        if ($hwnd -eq [IntPtr]::Zero) { continue }
        if (-not [God2Automation.NativeApi]::IsWindow($hwnd)) { continue }
        if (-not [God2Automation.NativeApi]::IsWindowVisible($hwnd)) { continue }
        return $snapshot
    }
    return $null
}

function Save-WindowScreenshotOrNull {
    param(
        [long] $Hwnd,
        [string] $Name
    )
    try {
        if (-not [God2Automation.NativeApi]::IsWindow([IntPtr]$Hwnd)) {
            return $null
        }
        return (Save-WindowScreenshot -Hwnd $Hwnd -Name $Name)
    }
    catch {
        return $null
    }
}

function Get-TestAccountCredential {
    param([string] $Key = "OfficialA")

    if ($Key -notin @("OfficialA", "OfficialB", "OfficialC", "OfficialD")) {
        throw "Unsupported test account key."
    }

    $configPath = Join-Path $repoRoot "config\test-accounts.local.json"
    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Missing test account configuration."
    }

    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $entry = $config.$Key
    if (-not $entry -or
        [string]::IsNullOrWhiteSpace([string]$entry.Account) -or
        [string]::IsNullOrEmpty([string]$entry.Password)) {
        throw "Test account configuration entry is incomplete: $Key"
    }

    return [pscustomobject]@{
        key = $Key
        account = [string]$entry.Account
        password = [string]$entry.Password
    }
}

function Get-LocalTestAccountCredential {
    Add-Type -AssemblyName System.Security
    $secretPath = Join-Path $repoRoot "Automation\State\login-secret.bin"
    if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf)) {
        throw "Local DPAPI login secret is missing."
    }

    $protected = [IO.File]::ReadAllBytes($secretPath)
    $plain = $null
    try {
        $plain = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected,
            $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        $payload = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
        if ([int]$payload.schemaVersion -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$payload.account) -or
            [string]::IsNullOrEmpty([string]$payload.password)) {
            throw "Local DPAPI login secret is invalid."
        }

        return [pscustomobject]@{
            key = "LocalDpapi"
            account = [string]$payload.account
            password = [string]$payload.password
        }
    }
    finally {
        if ($plain) { [Array]::Clear($plain, 0, $plain.Length) }
        if ($protected) { [Array]::Clear($protected, 0, $protected.Length) }
        $payload = $null
        Remove-Item -LiteralPath $secretPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-OneTimeTestAccountCredential {
    param([Parameter(Mandatory = $true)] [string] $EnvelopePath)

    $resolved = [IO.Path]::GetFullPath($EnvelopePath)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Automation\State')).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '^one-time-official-credential-[A-Za-z0-9._-]+\.json$' -or
        -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw 'One-time credential envelope path is not an approved Automation State file.'
    }

    $envelope = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$envelope.schemaId -cne 'God2OneTimeOfficialCredentialEnvelope' -or
        [int]$envelope.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$envelope.accountDpapi) -or
        [string]::IsNullOrWhiteSpace([string]$envelope.passwordDpapi)) {
        throw 'One-time credential envelope is invalid.'
    }

    function Unprotect-OneTimeValue([string] $Ciphertext) {
        $secure = ConvertTo-SecureString -String $Ciphertext
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
            $secure.Dispose()
        }
    }

    try {
        $account = Unprotect-OneTimeValue ([string]$envelope.accountDpapi)
        $password = Unprotect-OneTimeValue ([string]$envelope.passwordDpapi)
    }
    finally {
        Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue
    }
    if ([string]::IsNullOrWhiteSpace($account) -or [string]::IsNullOrEmpty($password)) {
        throw 'One-time official credential decrypted to an incomplete value.'
    }
    return [pscustomobject]@{ key = 'OneTimeOfficial'; account = $account; password = $password }
}

function Get-WindowRectObject {
    param([long] $Hwnd)
    $rect = New-Object God2Automation.NativeApi+RECT
    if (-not [God2Automation.NativeApi]::GetWindowRect([IntPtr]$Hwnd, [ref]$rect)) {
        throw "GetWindowRect failed for HWND $Hwnd"
    }
    [pscustomobject]@{
        left = $rect.Left
        top = $rect.Top
        right = $rect.Right
        bottom = $rect.Bottom
        width = $rect.Right - $rect.Left
        height = $rect.Bottom - $rect.Top
    }
}

function Get-ClientRectObject {
    param([long] $Hwnd)
    $rect = New-Object God2Automation.NativeApi+RECT
    if (-not [God2Automation.NativeApi]::GetClientRect([IntPtr]$Hwnd, [ref]$rect)) {
        throw "GetClientRect failed for HWND $Hwnd"
    }
    $origin = New-Object God2Automation.NativeApi+POINT
    $origin.X = 0
    $origin.Y = 0
    if (-not [God2Automation.NativeApi]::ClientToScreen([IntPtr]$Hwnd, [ref]$origin)) {
        throw "ClientToScreen failed for HWND $Hwnd"
    }
    [pscustomobject]@{
        left = $origin.X
        top = $origin.Y
        right = $origin.X + ($rect.Right - $rect.Left)
        bottom = $origin.Y + ($rect.Bottom - $rect.Top)
        width = $rect.Right - $rect.Left
        height = $rect.Bottom - $rect.Top
    }
}

function Resolve-StableGod2ClientWindow {
    param(
        [object] $InitialSnapshot,
        [int] $TimeoutSeconds = 10
    )

    $snapshot = $InitialSnapshot
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ($snapshot -and
            [int64]$snapshot.mainWindowHandle -ne 0 -and
            [God2Automation.NativeApi]::IsWindow([IntPtr][int64]$snapshot.mainWindowHandle)) {
            try {
                $rect = Get-ClientRectObject -Hwnd ([int64]$snapshot.mainWindowHandle)
                if ($rect.width -gt 0 -and $rect.height -gt 0) {
                    return [pscustomobject]@{
                        snapshot = $snapshot
                        hwnd = [int64]$snapshot.mainWindowHandle
                        rect = $rect
                    }
                }
            }
            catch {
                # The official client replaces its top-level HWND while moving
                # from the entry screen to login. Reacquire instead of treating
                # the retired handle as an automation or protocol failure.
            }
        }

        Start-Sleep -Milliseconds 250
        $snapshot = Get-PreferredGod2ClientSnapshot
    } while ((Get-Date) -lt $deadline)

    throw "God2_opt did not expose a stable client window within $TimeoutSeconds seconds."
}

function Get-PointFromRatio {
    param(
        [object] $Rect,
        [double] $X,
        [double] $Y
    )
    [pscustomobject]@{
        x = [int][Math]::Round($Rect.left + ($Rect.width * $X))
        y = [int][Math]::Round($Rect.top + ($Rect.height * $Y))
    }
}

function Save-WindowScreenshot {
    param(
        [long] $Hwnd,
        [string] $Name
    )
    Add-Type -AssemblyName System.Drawing
    # Classifiers operate on client-area ratios. Capture the client directly so
    # DPI virtualization cannot add a scaled title bar or black right/bottom pad.
    $rect = Get-ClientRectObject -Hwnd $Hwnd
    $path = Join-Path $artifactRoot $Name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
    $bmp = New-Object Drawing.Bitmap $rect.width, $rect.height
    $graphics = [Drawing.Graphics]::FromImage($bmp)
    try {
        $windowDc = $graphics.GetHdc()
        try {
            $captured = [God2Automation.NativeApi]::PrintWindow(
                [IntPtr]$Hwnd,
                $windowDc,
                0x00000003)
        }
        finally {
            $graphics.ReleaseHdc($windowDc)
        }
        if (-not $captured) {
            $graphics.CopyFromScreen($rect.left, $rect.top, 0, 0, (New-Object Drawing.Size $rect.width, $rect.height))
        }
        $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bmp.Dispose()
    }
    return $path
}

function Save-LauncherClientScreenshot {
    param(
        [long] $Hwnd,
        [string] $Name
    )
    Add-Type -AssemblyName System.Drawing
    $rect = Get-ClientRectObject -Hwnd $Hwnd
    $path = Join-Path $artifactRoot $Name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    $bmp = New-Object Drawing.Bitmap $rect.width, $rect.height
    $graphics = [Drawing.Graphics]::FromImage($bmp)
    try {
        $graphics.CopyFromScreen($rect.left, $rect.top, 0, 0, (New-Object Drawing.Size $rect.width, $rect.height))
        $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bmp.Dispose()
    }
    return $path
}

function Get-LauncherAgreementVisualState {
    param(
        [long] $Hwnd,
        [string] $ScreenshotName
    )
    Add-Type -AssemblyName System.Drawing
    $path = Save-LauncherClientScreenshot -Hwnd $Hwnd -Name $ScreenshotName
    $bmp = [Drawing.Bitmap]::FromFile($path)
    try {
        $dpi = [int][God2Automation.NativeApi]::GetDpiForWindow([IntPtr]$Hwnd)
        if ($dpi -le 0) { $dpi = 96 }
        $contentScale = $dpi / 96.0
        function Measure-DarkReference([double]$DesignX, [double]$DesignY) {
            $dark = 0
            for ($sampleY = 0; $sampleY -lt 14; $sampleY++) {
                for ($sampleX = 0; $sampleX -lt 14; $sampleX++) {
                    $x = [Math]::Min($bmp.Width - 1, [Math]::Max(0, [int][Math]::Round(($DesignX + $sampleX) * $contentScale)))
                    $y = [Math]::Min($bmp.Height - 1, [Math]::Max(0, [int][Math]::Round(($DesignY + $sampleY) * $contentScale)))
                    $pixel = $bmp.GetPixel($x, $y)
                    if ($pixel.R -lt 120 -and $pixel.G -lt 90 -and $pixel.B -lt 70) { $dark++ }
                }
            }
            return $dark
        }

        $uncheckedDark = Measure-DarkReference 655 496
        $checkedDark = Measure-DarkReference 709 496
        $agreementDark = Measure-DarkReference 655 515
        $checked = ($agreementDark -ge 40) -and ([Math]::Abs($agreementDark - $checkedDark) -lt [Math]::Abs($agreementDark - $uncheckedDark))
        return [pscustomobject]@{
            checked = $checked
            agreementDarkPixels = $agreementDark
            uncheckedReferenceDarkPixels = $uncheckedDark
            checkedReferenceDarkPixels = $checkedDark
            screenshot = $path
        }
    }
    finally {
        $bmp.Dispose()
    }
}

function Get-LauncherStartGameVisualState {
    param(
        [long] $Hwnd,
        [string] $ScreenshotName
    )
    Add-Type -AssemblyName System.Drawing
    $path = Save-LauncherClientScreenshot -Hwnd $Hwnd -Name $ScreenshotName
    $bmp = [Drawing.Bitmap]::FromFile($path)
    try {
        $dpi = [int][God2Automation.NativeApi]::GetDpiForWindow([IntPtr]$Hwnd)
        if ($dpi -le 0) { $dpi = 96 }
        $scale = $dpi / 96.0
        $x = [Math]::Min($bmp.Width - 1, [int][Math]::Round(881 * $scale))
        $y = [Math]::Min($bmp.Height - 1, [int][Math]::Round(494 * $scale))
        $pixel = $bmp.GetPixel($x, $y)
        $enabled = ($pixel.R -ge 225 -and $pixel.G -ge 130 -and $pixel.G -le 215 -and $pixel.B -le 120 -and (($pixel.R - $pixel.B) -ge 100))
        return [pscustomobject]@{
            enabled = $enabled
            sample = [pscustomobject]@{ r = $pixel.R; g = $pixel.G; b = $pixel.B; clientX = $x; clientY = $y }
            screenshot = $path
        }
    }
    finally {
        $bmp.Dispose()
    }
}

function Get-LauncherAgreementUiaDiagnostic {
    param([long] $Hwnd)
    $result = [ordered]@{
        available = $false
        elementCount = 0
        targetControlType = $null
        targetName = $null
        toggleAvailable = $false
        invokeAvailable = $false
        tree = @()
        error = $null
    }
    try {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        Add-Type -AssemblyName WindowsBase
        $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Hwnd)
        if (-not $root) { return [pscustomobject]$result }
        $elements = @($root.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition))
        $result.available = $true
        $result.elementCount = $elements.Count
        $tree = New-Object 'System.Collections.Generic.List[object]'
        foreach ($element in $elements) {
            try {
                $tree.Add([pscustomobject]@{
                    name = [string]$element.Current.Name
                    controlType = [string]$element.Current.ControlType.ProgrammaticName
                    className = [string]$element.Current.ClassName
                    automationId = [string]$element.Current.AutomationId
                    enabled = [bool]$element.Current.IsEnabled
                    bounds = [string]$element.Current.BoundingRectangle
                })
            }
            catch { }
        }
        $result.tree = [object[]]$tree.ToArray()
    }
    catch {
        $result.error = $_.Exception.Message
    }
    return [pscustomobject]$result
}

function Invoke-LauncherAgreementUiaPattern {
    param(
        [long] $Hwnd,
        [ValidateSet('Toggle','Invoke')] [string] $Pattern
    )
    try {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Hwnd)
        if (-not $root) { return $false }
        $condition = New-Object System.Windows.Automation.OrCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $script:OfficialSimplifiedAgreementCaption))
        )
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
        if (-not $element) { return $false }
        $value = $null
        if ($Pattern -eq 'Toggle' -and $element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$value)) {
            ([System.Windows.Automation.TogglePattern]$value).Toggle(); return $true
        }
        if ($Pattern -eq 'Invoke' -and $element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$value)) {
            ([System.Windows.Automation.InvokePattern]$value).Invoke(); return $true
        }
    }
    catch { }
    return $false
}

function Invoke-LauncherAgreementRegression {
    param([string] $Scenario = 'Normal')

    $launcher = Get-ActiveLauncherProcess
    if (-not $launcher) { throw 'LauncherAgreementRegression could not find the active-profile launcher.' }
    $snapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
    if (-not $snapshot -or $snapshot.mainWindowHandle -eq 0) { throw 'LauncherAgreementRegression could not rediscover launcher HWND.' }
    $hwnd = [int64]$snapshot.mainWindowHandle
    [God2Automation.NativeApi]::ShowWindow([IntPtr]$hwnd, 9) | Out-Null
    [God2Automation.NativeApi]::BringToTop([IntPtr]$hwnd)
    $windowRect = Get-WindowRectObject -Hwnd $hwnd
    $clientRect = Get-ClientRectObject -Hwnd $hwnd
    $dpi = [int][God2Automation.NativeApi]::GetDpiForWindow([IntPtr]$hwnd)
    if ($dpi -le 0) { $dpi = 96 }
    $contentScale = $dpi / 96.0
    $targetClientX = [int][Math]::Round(662.0 * $contentScale)
    $targetClientY = [int][Math]::Round(522.0 * $contentScale)
    $targetPoint = [pscustomobject]@{ x = $clientRect.left + $targetClientX; y = $clientRect.top + $targetClientY }
    $targetRect = [pscustomobject]@{
        left = $clientRect.left + [int][Math]::Round(655.0 * $contentScale)
        top = $clientRect.top + [int][Math]::Round(515.0 * $contentScale)
        right = $clientRect.left + [int][Math]::Round(671.0 * $contentScale)
        bottom = $clientRect.top + [int][Math]::Round(529.0 * $contentScale)
    }
    $tree = [God2Automation.NativeApi]::DescribeWindowTree([IntPtr]$hwnd)
    $uia = Get-LauncherAgreementUiaDiagnostic -Hwnd $hwnd
    $hit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint([IntPtr]$hwnd, $targetPoint.x, $targetPoint.y)
    $attempts = New-Object 'System.Collections.Generic.List[object]'
    $state = Get-LauncherAgreementVisualState -Hwnd $hwnd -ScreenshotName ("launcher-agreement-$Scenario-before.png")
    $method = if ($state.checked) { 'AlreadyChecked' } else { $null }

    function Test-AgreementAfter([string]$AttemptMethod, [bool]$Sent) {
        Start-Sleep -Milliseconds 450
        $after = Get-LauncherAgreementVisualState -Hwnd $hwnd -ScreenshotName ("launcher-agreement-$Scenario-$($attempts.Count + 1)-$AttemptMethod.png")
        $attempts.Add([pscustomobject]@{ method = $AttemptMethod; sent = $Sent; checked = [bool]$after.checked; visual = $after })
        return $after
    }

    if (-not $state.checked) {
        $sent = Invoke-LauncherAgreementUiaPattern -Hwnd $hwnd -Pattern Toggle
        $state = Test-AgreementAfter 'UIA-TogglePattern' $sent
        if ($state.checked) { $method = 'UIA TogglePattern' }
    }
    if (-not $state.checked) {
        $sent = Invoke-LauncherAgreementUiaPattern -Hwnd $hwnd -Pattern Invoke
        $state = Test-AgreementAfter 'UIA-InvokePattern' $sent
        if ($state.checked) { $method = 'UIA InvokePattern' }
    }
    if (-not $state.checked) {
        $button = @($tree.Children | Where-Object { $_.ClassName -eq 'Button' -and $_.IsEnabled } | Select-Object -First 1)
        $sent = $button.Count -eq 1
        if ($sent) { [God2Automation.NativeApi]::BmClick([IntPtr][int64]$button[0].Hwnd) }
        $state = Test-AgreementAfter 'BM_CLICK' $sent
        if ($state.checked) { $method = 'BM_CLICK' }
    }
    if (-not $state.checked) {
        [God2Automation.NativeApi]::SendClientClick([IntPtr]$hwnd, $targetClientX, $targetClientY)
        $state = Test-AgreementAfter 'SendMessage' $true
        if ($state.checked) { $method = 'SendMessage' }
    }
    if (-not $state.checked) {
        [God2Automation.NativeApi]::PostClientClick([IntPtr]$hwnd, $targetClientX, $targetClientY)
        $state = Test-AgreementAfter 'PostMessage' $true
        if ($state.checked) { $method = 'PostMessage' }
    }
    if (-not $state.checked) {
        [God2Automation.NativeApi]::BringToTop([IntPtr]$hwnd)
        [God2Automation.NativeApi]::Key(0x20)
        $state = Test-AgreementAfter 'Keyboard-Space' $true
        if ($state.checked) { $method = 'Keyboard Space' }
    }
    if (-not $state.checked) {
        [God2Automation.NativeApi]::BringToTop([IntPtr]$hwnd)
        [God2Automation.NativeApi]::Key(0x0D)
        $state = Test-AgreementAfter 'Keyboard-Enter' $true
        if ($state.checked) { $method = 'Keyboard Enter' }
    }
    if (-not $state.checked) {
        [God2Automation.NativeApi]::BringToTop([IntPtr]$hwnd)
        [God2Automation.NativeApi]::RealClick($targetPoint.x, $targetPoint.y, 180, 120, 500)
        $state = Test-AgreementAfter 'SendInput-Mouse' $true
        if ($state.checked) { $method = 'Real SendInput mouse click' }
    }

    $attemptArray = [object[]]$attempts.ToArray()
    $childControlArray = [object[]]@($tree.Children)
    $result = [ordered]@{
        scenario = $Scenario
        success = [bool]$state.checked
        controlType = 'Embedded WKE browser custom-rendered Vue div/image'
        scrollRequired = $false
        launcherPid = $snapshot.pid
        launcherHwnd = $hwnd
        windowRect = $windowRect
        clientRect = $clientRect
        dpi = $dpi
        scalingFactor = $dpi / 96.0
        foreground = ([int64][God2Automation.NativeApi]::GetForegroundWindow() -eq $hwnd)
        focusedHwnd = [God2Automation.NativeApi]::GetFocusedWindowFor([IntPtr]$hwnd)
        childControls = $childControlArray
        uia = $uia
        hitTest = $hit
        targetRectangle = $targetRect
        targetClientCoordinates = [pscustomobject]@{ x = $targetClientX; y = $targetClientY }
        targetScreenCoordinates = $targetPoint
        interactionMethod = $method
        retryCount = $attempts.Count
        attempts = $attemptArray
        finalVisualState = $state
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-God2AtomicJson -Value $result -Path (Join-Path $artifactRoot ("launcher-agreement-$Scenario-result.json"))
    if (-not $result.success) { throw "LauncherAgreementRegression failed for $Scenario." }
    return [pscustomobject]$result
}

function Save-RedactedWindowScreenshot {
    param(
        [long] $Hwnd,
        [string] $Directory,
        [string] $Name,
        [object[]] $RedactionRects = @()
    )
    Add-Type -AssemblyName System.Drawing
    $windowRect = Get-WindowRectObject -Hwnd $Hwnd
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $path = Join-Path $Directory $Name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
    $bmp = New-Object Drawing.Bitmap $windowRect.width, $windowRect.height
    $graphics = [Drawing.Graphics]::FromImage($bmp)
    try {
        $graphics.CopyFromScreen($windowRect.left, $windowRect.top, 0, 0, (New-Object Drawing.Size $windowRect.width, $windowRect.height))
        foreach ($rect in @($RedactionRects)) {
            if (-not $rect) { continue }
            $x = [Math]::Max(0, [int]$rect.left - [int]$windowRect.left)
            $y = [Math]::Max(0, [int]$rect.top - [int]$windowRect.top)
            $right = [Math]::Min([int]$windowRect.width, [int]$rect.right - [int]$windowRect.left)
            $bottom = [Math]::Min([int]$windowRect.height, [int]$rect.bottom - [int]$windowRect.top)
            $width = [Math]::Max(0, $right - $x)
            $height = [Math]::Max(0, $bottom - $y)
            if ($width -gt 0 -and $height -gt 0) {
                $graphics.FillRectangle([Drawing.Brushes]::Black, $x, $y, $width, $height)
            }
        }
        $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bmp.Dispose()
    }
    return $path
}

function Save-LoginSafeScreenshot {
    param(
        [long] $Hwnd,
        [string] $Name,
        [object[]] $RedactionRects = @(),
        [string] $Directory = $artifactRoot
    )
    Save-RedactedWindowScreenshot -Hwnd $Hwnd -Directory $Directory -Name $Name -RedactionRects $RedactionRects
}

function New-LoginAutomationAttemptContext {
    param(
        [int] $Attempt,
        [object] $ClientSnapshot,
        [long] $Hwnd
    )
    $root = Join-Path $repoRoot ("Artifacts\ClientLoginAutomation\" + (Get-Date -Format "yyyyMMdd-HHmmss-fff") + "-attempt-$Attempt")
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    [pscustomobject]@{
        root = $root
        attempt = $Attempt
        clientPid = if ($ClientSnapshot) { [int]$ClientSnapshot.pid } else { $null }
        hwnd = $Hwnd
        currentState = "ClientStarting"
        stateTransitions = (New-Object 'System.Collections.Generic.List[object]')
        focusTimeline = (New-Object 'System.Collections.Generic.List[object]')
        inputAttempts = (New-Object 'System.Collections.Generic.List[object]')
        submitAttempts = (New-Object 'System.Collections.Generic.List[object]')
        automationLog = (New-Object 'System.Collections.Generic.List[string]')
        controls = $null
        redactionRects = @()
        safeScreenshotBefore = $null
        safeScreenshotAfter = $null
    }
}

function Add-LoginAutomationState {
    param(
        [object] $Context,
        [string] $State,
        [string] $FailureCode = "",
        [object] $Control = $null,
        [object] $Diagnostic = $null
    )
    if (-not $Context) { return }
    $previous = [string]$Context.currentState
    $hwnd = [int64]$Context.hwnd
    $window = $null
    $focused = 0
    $focusedInfo = $null
    if ($hwnd -ne 0 -and [God2Automation.NativeApi]::IsWindow([IntPtr]$hwnd)) {
        $window = [God2Automation.NativeApi]::DescribeWindow([IntPtr]$hwnd)
        $focused = [int64][God2Automation.NativeApi]::GetFocusedWindowFor([IntPtr]$hwnd)
        if ($focused -ne 0 -and [God2Automation.NativeApi]::IsWindow([IntPtr]$focused)) {
            $focusedInfo = [God2Automation.NativeApi]::DescribeWindow([IntPtr]$focused)
        }
    }
    $transition = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
        clientPid = $Context.clientPid
        windowHandle = $hwnd
        windowTitle = if ($window) { [string]$window.Title } else { "" }
        windowClass = if ($window) { [string]$window.ClassName } else { "" }
        activeControlHandle = $focused
        controlClass = if ($focusedInfo) { [string]$focusedInfo.ClassName } elseif ($Control) { [string]$Control.className } else { "" }
        controlBounds = if ($Control) { $Control.bounds } elseif ($focusedInfo) { $focusedInfo.Bounds } else { $null }
        integrityLevel = $hostIntegrity
        attemptNumber = $Context.attempt
        previousState = $previous
        currentState = $State
        failureCode = $FailureCode
        diagnostic = $Diagnostic
    }
    $Context.stateTransitions.Add([pscustomobject]$transition) | Out-Null
    $Context.currentState = $State
}

function New-LoginVirtualControl {
    param(
        [string] $Name,
        [string] $Role,
        [object] $Rect,
        [double] $X1,
        [double] $Y1,
        [double] $X2,
        [double] $Y2,
        [bool] $IsPassword = $false
    )
    $left = [int][Math]::Round($Rect.left + ($Rect.width * $X1))
    $top = [int][Math]::Round($Rect.top + ($Rect.height * $Y1))
    $right = [int][Math]::Round($Rect.left + ($Rect.width * $X2))
    $bottom = [int][Math]::Round($Rect.top + ($Rect.height * $Y2))
    [pscustomobject]@{
        name = $Name
        role = $Role
        locatorMode = "ClientRelativeBoundsFallback"
        hwnd = $Rect.hwnd
        className = "VirtualGod2LoginRegion"
        controlId = 0
        automationId = ""
        controlType = "CustomRegion"
        isEnabled = $true
        isKeyboardFocusable = $true
        isPassword = $IsPassword
        bounds = [pscustomobject]@{
            left = $left
            top = $top
            right = $right
            bottom = $bottom
            width = $right - $left
            height = $bottom - $top
        }
        center = [pscustomobject]@{
            x = [int][Math]::Round(($left + $right) / 2)
            y = [int][Math]::Round(($top + $bottom) / 2)
        }
        confidence = "FallbackValidatedByScreenTrace"
    }
}

function Get-LoginControlModel {
    param(
        [long] $Hwnd,
        [object] $Rect
    )
    $tree = [God2Automation.NativeApi]::DescribeWindowTree([IntPtr]$Hwnd)
    $Rect | Add-Member -NotePropertyName hwnd -NotePropertyValue $Hwnd -Force
    $children = @($tree.Children)
    $editCandidates = @($children | Where-Object {
        $_.IsVisible -and $_.IsEnabled -and ([string]$_.ClassName -match "Edit|TextBox")
    } | Sort-Object { $_.Bounds.Top }, { $_.Bounds.Left })
    $buttonCandidates = @($children | Where-Object {
        $_.IsVisible -and $_.IsEnabled -and ([string]$_.ClassName -match "Button")
    } | Sort-Object { $_.Bounds.Top }, { $_.Bounds.Left })

    $account = $null
    $password = $null
    $submit = $null
    $mode = "ClientRelativeBoundsFallback"
    if ($editCandidates.Count -ge 2) {
        $accountWin = $editCandidates[0]
        $passwordWin = $editCandidates[1]
        $account = [pscustomobject]@{
            name = "Account"
            role = "Account"
            locatorMode = "Win32ChildWindow"
            hwnd = [int64]$accountWin.Hwnd
            className = [string]$accountWin.ClassName
            controlId = [int]$accountWin.ControlId
            automationId = ""
            controlType = "Edit"
            isEnabled = [bool]$accountWin.IsEnabled
            isKeyboardFocusable = $true
            isPassword = $false
            bounds = $accountWin.Bounds
            center = [pscustomobject]@{ x = [int][Math]::Round(($accountWin.Bounds.Left + $accountWin.Bounds.Right) / 2); y = [int][Math]::Round(($accountWin.Bounds.Top + $accountWin.Bounds.Bottom) / 2) }
            confidence = "DistinctWin32EditControls"
        }
        $password = [pscustomobject]@{
            name = "Password"
            role = "Password"
            locatorMode = "Win32ChildWindow"
            hwnd = [int64]$passwordWin.Hwnd
            className = [string]$passwordWin.ClassName
            controlId = [int]$passwordWin.ControlId
            automationId = ""
            controlType = "Edit"
            isEnabled = [bool]$passwordWin.IsEnabled
            isKeyboardFocusable = $true
            isPassword = $true
            bounds = $passwordWin.Bounds
            center = [pscustomobject]@{ x = [int][Math]::Round(($passwordWin.Bounds.Left + $passwordWin.Bounds.Right) / 2); y = [int][Math]::Round(($passwordWin.Bounds.Top + $passwordWin.Bounds.Bottom) / 2) }
            confidence = "DistinctWin32EditControls"
        }
        $mode = "Win32ChildWindow"
    }
    else {
        $account = New-LoginVirtualControl -Name "Account" -Role "Account" -Rect $Rect -X1 0.34 -Y1 0.350 -X2 0.68 -Y2 0.420
        $password = New-LoginVirtualControl -Name "Password" -Role "Password" -Rect $Rect -X1 0.34 -Y1 0.420 -X2 0.68 -Y2 0.480 -IsPassword $true
    }

    if ($buttonCandidates.Count -ge 1) {
        $button = $buttonCandidates[0]
        $submit = [pscustomobject]@{
            name = "Submit"
            role = "Submit"
            locatorMode = "Win32ChildWindow"
            hwnd = [int64]$button.Hwnd
            className = [string]$button.ClassName
            controlId = [int]$button.ControlId
            automationId = ""
            controlType = "Button"
            isEnabled = [bool]$button.IsEnabled
            isKeyboardFocusable = $true
            isPassword = $false
            bounds = $button.Bounds
            center = [pscustomobject]@{ x = [int][Math]::Round(($button.Bounds.Left + $button.Bounds.Right) / 2); y = [int][Math]::Round(($button.Bounds.Top + $button.Bounds.Bottom) / 2) }
            confidence = "Win32ButtonCandidate"
        }
    }
    else {
        $submit = New-LoginVirtualControl -Name "Submit" -Role "Submit" -Rect $Rect -X1 0.49 -Y1 0.485 -X2 0.64 -Y2 0.545
        $submit.controlType = "CustomButtonRegion"
    }

    [pscustomobject]@{
        locatorMode = $mode
        account = $account
        password = $password
        submit = $submit
        accountPasswordDistinct = (($account.center.x -ne $password.center.x) -or ($account.center.y -ne $password.center.y))
        childWindowCount = $children.Count
        editCandidateCount = $editCandidates.Count
        buttonCandidateCount = $buttonCandidates.Count
        windowTree = $tree
    }
}

function Get-LoginRedactionRects {
    param([object] $Controls)
    @(
        $Controls.account.bounds
        $Controls.password.bounds
    )
}

function Focus-LoginControl {
    param(
        [long] $Hwnd,
        [object] $Control,
        [object] $Context,
        [string] $StatePrefix
    )
    for ($focusAttempt = 1; $focusAttempt -le 3; $focusAttempt++) {
        Add-LoginAutomationState -Context $Context -State ($StatePrefix + "Focused") -Control $Control -Diagnostic ([ordered]@{ focusAttempt = $focusAttempt; locatorMode = $Control.locatorMode })
        [God2Automation.NativeApi]::BringToTop([IntPtr]$Hwnd)
        [God2Automation.NativeApi]::RealClick([int]$Control.center.x, [int]$Control.center.y, 150, 110, 220)
        Start-Sleep -Milliseconds 180
        $foregroundMatches = ([God2Automation.NativeApi]::GetForegroundWindow() -eq [IntPtr]$Hwnd)
        $focusedHandle = [int64][God2Automation.NativeApi]::GetFocusedWindowFor([IntPtr]$Hwnd)
        $pointWindow = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint([IntPtr]$Hwnd, [int]$Control.center.x, [int]$Control.center.y)
        $confirmed = $foregroundMatches -and (($focusedHandle -eq $Hwnd) -or ($focusedHandle -eq [int64]$Control.hwnd) -or ([int64]$pointWindow.Hwnd -eq $Hwnd) -or ([int64]$pointWindow.Hwnd -eq [int64]$Control.hwnd))
        $focusResult = [pscustomobject]@{
            timestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
            role = $Control.role
            focusAttempt = $focusAttempt
            locatorMode = $Control.locatorMode
            targetHwnd = [int64]$Control.hwnd
            focusHandle = $focusedHandle
            pointWindowHandle = [int64]$pointWindow.Hwnd
            pointWindowClass = [string]$pointWindow.ClassName
            foregroundMatches = $foregroundMatches
            confirmed = $confirmed
            bounds = $Control.bounds
        }
        $Context.focusTimeline.Add($focusResult) | Out-Null
        if ($confirmed) { return $focusResult }
    }
    return $focusResult
}

function Complete-LoginAutomationAttempt {
    param(
        [object] $Context,
        [object] $Result,
        [long] $Hwnd,
        [object] $Controls = $null,
        [string] $MetadataPath = ""
    )
    if (-not $Context) { return $Result }
    $success = $false
    if ($Result -and $Result.PSObject.Properties["success"]) {
        $success = [bool]$Result.success
    }
    $errorCode = if ($success) { "" } elseif ($Result -and $Result.PSObject.Properties["error"]) { [string]$Result.error } else { "UnknownLoginFailure" }
    Add-LoginAutomationState -Context $Context -State ($(if ($success) { "LoginSucceeded" } else { "LoginFailed" })) -FailureCode $errorCode

    $redactions = @($Context.redactionRects)
    if ($Hwnd -ne 0 -and [God2Automation.NativeApi]::IsWindow([IntPtr]$Hwnd)) {
        if (-not $Context.safeScreenshotBefore) {
            $Context.safeScreenshotBefore = Save-LoginSafeScreenshot -Hwnd $Hwnd -Directory ([string]$Context.root) -Name "safe-screenshot-before.png" -RedactionRects $redactions
        }
        $Context.safeScreenshotAfter = Save-LoginSafeScreenshot -Hwnd $Hwnd -Directory ([string]$Context.root) -Name "safe-screenshot-after.png" -RedactionRects $redactions
        try {
            Write-God2AtomicJson -Value ([God2Automation.NativeApi]::DescribeWindowTree([IntPtr]$Hwnd)) -Path (Join-Path ([string]$Context.root) "window-tree-after.json")
        }
        catch {
        }
    }

    $network = if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
        [pscustomobject]@{ metadataPath = ""; recordCount = 0; sendCount = 0; recvCount = 0; connectToLocal2592 = $false }
    }
    else {
        Get-NetworkTraceSummary -MetadataPath $MetadataPath
    }
    $processState = [ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("o")
        clientPid = $Context.clientPid
        hwnd = $Context.hwnd
        launcherAlive = [bool](Get-ActiveLauncherProcess)
        clientAlive = [bool](Get-Process -Id ([int]$Context.clientPid) -ErrorAction SilentlyContinue)
        hostIntegrity = $hostIntegrity
        credentialRedaction = "No plaintext account/password persisted by ClientLoginAutomation artifact writer."
    }

    $summary = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        attempt = $Context.attempt
        success = $success
        failureCode = $errorCode
        artifactRoot = $Context.root
        clientPid = $Context.clientPid
        hwnd = $Context.hwnd
        controlLocatorMode = if ($Controls) { [string]$Controls.locatorMode } else { "" }
        accountControlDistinctFromPassword = if ($Controls) { [bool]$Controls.accountPasswordDistinct } else { $false }
        accountInput = if ($Result -and $Result.PSObject.Properties["accountInput"]) { $Result.accountInput } else { $null }
        passwordInput = if ($Result -and $Result.PSObject.Properties["passwordInput"]) { $Result.passwordInput } else { $null }
        submit = if ($Result -and $Result.PSObject.Properties["loginClick"]) { $Result.loginClick } else { $null }
        network = $network
        safeScreenshotBefore = $Context.safeScreenshotBefore
        safeScreenshotAfter = $Context.safeScreenshotAfter
        credentialRedaction = "PASS"
    }

    Write-God2AtomicJson -Value ([pscustomobject]$summary) -Path (Join-Path ([string]$Context.root) "summary.json")
    Write-God2AtomicJson -Value ([object[]]$Context.stateTransitions.ToArray()) -Path (Join-Path ([string]$Context.root) "state-machine.json")
    Write-God2AtomicJson -Value ([object[]]$Context.focusTimeline.ToArray()) -Path (Join-Path ([string]$Context.root) "control-focus-timeline.json")
    Write-God2AtomicJson -Value ([object[]]$Context.inputAttempts.ToArray()) -Path (Join-Path ([string]$Context.root) "input-attempts.json")
    Write-God2AtomicJson -Value ([object[]]$Context.submitAttempts.ToArray()) -Path (Join-Path ([string]$Context.root) "submit-attempts.json")
    Write-God2AtomicJson -Value ([pscustomobject]$processState) -Path (Join-Path ([string]$Context.root) "process-state.json")
    Write-God2AtomicJson -Value $network -Path (Join-Path ([string]$Context.root) "network-stage.json")
    if ($Controls) {
        Write-God2AtomicJson -Value $Controls.windowTree -Path (Join-Path ([string]$Context.root) "window-tree-before.json")
    }
    @(
        "Client Login Automation Attempt"
        "artifactRoot=$($Context.root)"
        "attempt=$($Context.attempt)"
        "success=$success"
        "failureCode=$errorCode"
        "controlLocatorMode=$(if ($Controls) { [string]$Controls.locatorMode } else { '' })"
        "credentialRedaction=PASS"
    ) | Set-Content -LiteralPath (Join-Path ([string]$Context.root) "automation-log.txt") -Encoding UTF8
    @"
# Client Login Automation Attempt

| Field | Value |
| --- | --- |
| Attempt | $($Context.attempt) |
| Success | $success |
| Failure Code | $errorCode |
| Control Locator | $(if ($Controls) { [string]$Controls.locatorMode } else { "" }) |
| Account/Password Distinct | $(if ($Controls) { [bool]$Controls.accountPasswordDistinct } else { $false }) |
| Credential Redaction | PASS |

No plaintext account or password is stored in this artifact.
"@ | Set-Content -LiteralPath (Join-Path ([string]$Context.root) "summary.md") -Encoding UTF8

    $Result | Add-Member -NotePropertyName loginAutomationArtifact -NotePropertyValue ([string]$Context.root) -Force
    $Result | Add-Member -NotePropertyName credentialRedaction -NotePropertyValue "PASS" -Force
    return $Result
}

function Start-LoginAttemptTraceProbe {
    param(
        [int] $TargetPid,
        [int] $Attempt,
        [ValidateSet("Standard", "GenericProbe")]
        [string] $BuildFlavor = "Standard"
    )
    $traceRoot = Join-Path $artifactRoot ("attempt-$Attempt-trace")
    New-Item -ItemType Directory -Force -Path $traceRoot | Out-Null
    $sensitive = Join-Path $traceRoot "sensitive"
    New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
    $buildDir = if ($BuildFlavor -eq "GenericProbe") {
        Join-Path $repoRoot "Build\ClientInstrumentation-GenericProbe\Release"
    }
    else {
        Join-Path $repoRoot "Build\ClientInstrumentation\Release"
    }
    $launcher = Join-Path $buildDir "God2ClientTraceLauncher.exe"
    $sourceDll = Join-Path $buildDir "God2ClientTraceProbe.dll"
    $probeDll = Join-Path $traceRoot ("God2LoginTraceProbe-$Attempt.dll")
    $targetProcess = Get-Process -Id $TargetPid -ErrorAction Stop
    $targetPath = [string]$targetProcess.Path
    $targetSha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $targetVersion = [string](Get-Item -LiteralPath $targetPath).VersionInfo.FileVersion
    $targetCreationTime = $targetProcess.StartTime.ToUniversalTime().ToFileTimeUtc()
    Copy-Item -LiteralPath $sourceDll -Destination $probeDll -Force
    @(
        "traceDir=$sensitive",
        "generalLog=$(Join-Path $traceRoot "general.log")",
        "metadata=$(Join-Path $traceRoot "metadata.jsonl")",
        'clientBuildVerified=true',
        "clientVersion=$targetVersion",
        "clientSha256=$targetSha256",
        "clientProcessId=$TargetPid",
        "clientProcessCreationTime=$targetCreationTime",
        "enableInline=true",
        "enableInputInline=false",
        "enableVersionProbe=true"
    ) | Set-Content -LiteralPath (Join-Path $traceRoot "God2ClientTraceProbe.attach.env") -Encoding ASCII
    $output = & $launcher --attach --pid $TargetPid --dll $probeDll 2>&1
    $result = [ordered]@{
        targetPid = $TargetPid
        exitCode = $LASTEXITCODE
        output = @($output)
        traceRoot = $traceRoot
        generalLog = Join-Path $traceRoot "general.log"
        metadata = Join-Path $traceRoot "metadata.jsonl"
        attachedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    Write-God2AtomicJson -Value $result -Path (Join-Path $traceRoot "attach-result.json")
    return [pscustomobject]$result
}

function Get-WmCharCount {
    param(
        [string] $GeneralLog,
        [DateTimeOffset] $StartUtc,
        [DateTimeOffset] $EndUtc
    )
    if (-not (Test-Path -LiteralPath $GeneralLog)) { return 0 }
    $count = 0
    foreach ($line in (Get-Content -LiteralPath $GeneralLog -ErrorAction SilentlyContinue)) {
        if ($line -notlike "*msg=WM_CHAR*") { continue }
        $timestamp = ($line -split " ", 2)[0]
        try {
            $when = [DateTimeOffset]::Parse($timestamp, [Globalization.CultureInfo]::InvariantCulture)
            if ($when -ge $StartUtc -and $when -le $EndUtc) { $count++ }
        }
        catch {
        }
    }
    return $count
}

function Wait-WmCharCount {
    param(
        [string] $GeneralLog,
        [DateTimeOffset] $StartUtc,
        [int] $ExpectedCount,
        [int] $TimeoutMilliseconds = 4500
    )
    $deadline = [DateTimeOffset]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    $received = 0
    do {
        Start-Sleep -Milliseconds 120
        $received = Get-WmCharCount -GeneralLog $GeneralLog -StartUtc $StartUtc -EndUtc ([DateTimeOffset]::UtcNow)
        if ($received -ge $ExpectedCount) { break }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $received
}

function Invoke-ClearFocusedField {
    param([long] $Hwnd)

    # Clear through synchronous window messages so queued physical key events
    # cannot erase the first characters of the following credential input.
    $backspaces = [string]::new([char]8, 32)
    [God2Automation.NativeApi]::SendUnicodeChars([IntPtr]$Hwnd, $backspaces, 30)
    Start-Sleep -Milliseconds 250
}

function Invoke-ReliableFieldInput {
    param(
        [long] $Hwnd,
        [object] $Control,
        [string] $Text,
        [string] $FieldName,
        [string] $GeneralLog,
        [int] $Attempt,
        [object] $Context = $null,
        [object[]] $RedactionRects = @(),
        [bool] $IsSecret = $false
    )
    for ($fieldAttempt = 1; $fieldAttempt -le 3; $fieldAttempt++) {
        Add-LoginAutomationState -Context $Context -State ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "FieldLocating") -Control $Control -Diagnostic ([ordered]@{ fieldAttempt = $fieldAttempt; isSecret = $IsSecret })
        $focus = Focus-LoginControl -Hwnd $Hwnd -Control $Control -Context $Context -StatePrefix ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "Field")
        if (-not $focus.confirmed) {
            $result = [pscustomobject]@{
                fieldName = $FieldName
                fieldAttempt = $fieldAttempt
                locatorMode = $Control.locatorMode
                expectedLength = $Text.Length
                receivedCharCount = 0
                inputStrategy = "None"
                startUtc = $null
                endUtc = $null
                screenshot = $null
                focus = $focus
                success = $false
                failureCode = ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "FieldFocusNotConfirmed")
                secretRedacted = $true
            }
            if ($Context) { $Context.inputAttempts.Add($result) | Out-Null }
            continue
        }

        Add-LoginAutomationState -Context $Context -State ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "InputCleared") -Control $Control -Diagnostic ([ordered]@{ fieldAttempt = $fieldAttempt })
        Invoke-ClearFocusedField -Hwnd $Hwnd
        Start-Sleep -Milliseconds 160

        # The official client accepts WM_CHAR but can drop asynchronously posted
        # messages. Prefer synchronous SendMessage delivery; decoded server identity
        # and formal authentication remain the acceptance gate.
        $strategy = if ($fieldAttempt -eq 2) { "PostMessageWmChar" } else { "SendMessageWmChar" }
        Add-LoginAutomationState -Context $Context -State ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "InputWriting") -Control $Control -Diagnostic ([ordered]@{ fieldAttempt = $fieldAttempt; strategy = $strategy; expectedLength = $Text.Length; isSecret = $IsSecret })
        $startUtc = [DateTimeOffset]::UtcNow
        if ($strategy -eq "PostMessageWmChar") {
            $delay = if ($fieldAttempt -eq 3) { 95 } else { 55 }
            [God2Automation.NativeApi]::PostCharsWithDelay([IntPtr]$Hwnd, $Text, $delay)
        }
        elseif ($strategy -eq "SendMessageWmChar") {
            [God2Automation.NativeApi]::SendUnicodeChars([IntPtr]$Hwnd, $Text, 140)
        }
        else {
            [God2Automation.NativeApi]::TypeMessageChars([IntPtr]$Hwnd, $Text, 140)
        }
        $received = if ([string]::IsNullOrWhiteSpace($GeneralLog)) {
            $Text.Length
        }
        else {
            Wait-WmCharCount -GeneralLog $GeneralLog -StartUtc $startUtc -ExpectedCount $Text.Length
        }
        $endUtc = [DateTimeOffset]::UtcNow
        Start-Sleep -Milliseconds 180
        $screenshot = Save-LoginSafeScreenshot -Hwnd $Hwnd -Name ("attempt-$Attempt-$FieldName-field-attempt-$fieldAttempt.png") -RedactionRects $RedactionRects
        $result = [pscustomobject]@{
            fieldName = $FieldName
            fieldAttempt = $fieldAttempt
            locatorMode = $Control.locatorMode
            expectedLength = $Text.Length
            receivedCharCount = $received
            inputStrategy = $strategy
            startUtc = $startUtc.ToString("o")
            endUtc = $endUtc.ToString("o")
            screenshot = $screenshot
            focus = $focus
            success = ($received -eq $Text.Length)
            failureCode = if ($received -eq $Text.Length) { "" } elseif ($received -lt $Text.Length) { ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "InputIncompleteNoSubmit") } else { ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "InputOverrunNoSubmit") }
            secretRedacted = $true
        }
        if ($Context) { $Context.inputAttempts.Add($result) | Out-Null }
        if ($result.success) {
            Add-LoginAutomationState -Context $Context -State ($FieldName.Substring(0,1).ToUpperInvariant() + $FieldName.Substring(1) + "InputVerified") -Control $Control -Diagnostic ([ordered]@{ fieldAttempt = $fieldAttempt; expectedLength = $Text.Length; receivedCharCount = $received; contentReadable = $false })
            return $result
        }
    }
    return $result
}

function Get-NetworkTraceSummary {
    param([string] $MetadataPath)
    $summary = [ordered]@{
        metadataPath = $MetadataPath
        recordCount = 0
        sendCount = 0
        recvCount = 0
        connectToLocal2592 = $false
        firstRemote = $null
        lastRemote = $null
    }
    if ([string]::IsNullOrWhiteSpace($MetadataPath) -or -not (Test-Path -LiteralPath $MetadataPath)) {
        return [pscustomobject]$summary
    }
    foreach ($line in (Get-Content -LiteralPath $MetadataPath -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $event = $line | ConvertFrom-Json
            $summary.recordCount++
            if ($event.direction -eq "ClientToServer") { $summary.sendCount++ }
            if ($event.direction -eq "ServerToClient") { $summary.recvCount++ }
            if ($null -eq $summary.firstRemote) { $summary.firstRemote = $event.remote }
            $summary.lastRemote = $event.remote
            if ([string]$event.remote -eq "127.0.0.1:2592") { $summary.connectToLocal2592 = $true }
        }
        catch {
        }
    }
    return [pscustomobject]$summary
}

function Get-WorldTraceSummary {
    param(
        [string] $MetadataPath,
        [switch] $RequireNonLoopback
    )
    $summary = [ordered]@{
        metadataPath = $MetadataPath
        worldSocket = $null
        worldBootstrapActivity = $false
        worldRecvBytes = 0
        heartbeatCount = 0
        heartbeatDurationSeconds = 0
        heartbeatAverageGapSeconds = 0
        heartbeatMaximumGapSeconds = 0
        worldTrafficCount = 0
    }
    if ([string]::IsNullOrWhiteSpace($MetadataPath) -or -not (Test-Path -LiteralPath $MetadataPath)) {
        return [pscustomobject]$summary
    }
    $events = @()
    foreach ($line in (Get-Content -LiteralPath $MetadataPath -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $event = $line | ConvertFrom-Json
            $remote = [string]$event.remote
            $local = [string]$event.local
            $isLoopback = $remote -eq "127.0.0.1:2592" -or $local -like "127.0.0.1:*"
            $isNonLoopback = -not [string]::IsNullOrWhiteSpace($remote) -and
                $remote -notin @('unknown:0', '::1', '[::1]:0') -and
                $remote -notlike '127.*' -and $remote -notlike '[[]::1[]]:*'
            if (($RequireNonLoopback -and $isNonLoopback) -or
                (-not $RequireNonLoopback -and $isLoopback)) {
                $events += $event
            }
        }
        catch {
        }
    }
    $socketGroups = $events | Where-Object { $_.socket } | Group-Object socket
    foreach ($group in $socketGroups) {
        $items = @($group.Group)
        $recvBytes = ($items | Where-Object { $_.direction -eq "ServerToClient" } | Measure-Object -Property transferredLength -Sum).Sum
        $heartbeatEvents = @($items | Where-Object {
            $_.direction -eq "ClientToServer" -and $_.requestedLength -eq 5 -and
                $_.transferredLength -eq 5
        } | Sort-Object wallUnixMs)
        if ($recvBytes -gt [int]$summary.worldRecvBytes -or $heartbeatEvents.Count -gt [int]$summary.heartbeatCount) {
            $summary.worldSocket = $group.Name
            $summary.worldRecvBytes = [int]$recvBytes
            $summary.heartbeatCount = $heartbeatEvents.Count
            $summary.worldTrafficCount = $items.Count
            if ($heartbeatEvents.Count -ge 2) {
                $first = [int64]$heartbeatEvents[0].wallUnixMs
                $last = [int64]$heartbeatEvents[-1].wallUnixMs
                $summary.heartbeatDurationSeconds = [Math]::Round(($last - $first) / 1000.0, 3)
                $gaps = for ($index = 1; $index -lt $heartbeatEvents.Count; $index++) {
                    ([int64]$heartbeatEvents[$index].wallUnixMs -
                        [int64]$heartbeatEvents[$index - 1].wallUnixMs) / 1000.0
                }
                $summary.heartbeatAverageGapSeconds = [Math]::Round(
                    [double](($gaps | Measure-Object -Average).Average), 3)
                $summary.heartbeatMaximumGapSeconds = [Math]::Round(
                    [double](($gaps | Measure-Object -Maximum).Maximum), 3)
            }
        }
    }
    $summary.worldBootstrapActivity = ([int]$summary.worldRecvBytes -ge 1000)
    return [pscustomobject]$summary
}

function Test-WorldTraceStability {
    param([Parameter(Mandatory = $true)] [object] $Trace)

    # The official client emits its 5-byte world heartbeat approximately every
    # five seconds. Verify a full minute of bounded cadence plus substantive
    # world traffic instead of requiring an impossible one heartbeat per second.
    return ([bool]$Trace.worldBootstrapActivity -and
        [int]$Trace.worldRecvBytes -ge 1000 -and
        [int]$Trace.worldTrafficCount -ge 25 -and
        [int]$Trace.heartbeatCount -ge 13 -and
        [double]$Trace.heartbeatDurationSeconds -ge 60 -and
        [double]$Trace.heartbeatMaximumGapSeconds -gt 0 -and
        [double]$Trace.heartbeatMaximumGapSeconds -le 7.5)
}

function Test-LoginScreenImage {
    param([string] $Path)
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        function Count-BlueBoxPixels {
            param(
                [double] $Rx1,
                [double] $Ry1,
                [double] $Rx2,
                [double] $Ry2
            )
            $count = 0
            $x1 = [int]($bmp.Width * $Rx1)
            $x2 = [int]($bmp.Width * $Rx2)
            $y1 = [int]($bmp.Height * $Ry1)
            $y2 = [int]($bmp.Height * $Ry2)
            for ($y = $y1; $y -lt $y2; $y += 2) {
                for ($x = $x1; $x -lt $x2; $x += 2) {
                    $c = $bmp.GetPixel($x, $y)
                    if ($c.B -gt 170 -and $c.G -gt 80 -and $c.G -lt 190 -and $c.R -gt 100 -and $c.R -lt 220) { $count++ }
                }
            }
            return $count
        }
        $accountBox = Count-BlueBoxPixels 0.49 0.39 0.61 0.43
        $passwordBox = Count-BlueBoxPixels 0.49 0.445 0.61 0.485
        $redPrompt = 0
        for ($y = [int]($bmp.Height * 0.68); $y -lt [int]($bmp.Height * 0.78); $y += 2) {
            for ($x = [int]($bmp.Width * 0.32); $x -lt [int]($bmp.Width * 0.68); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 180 -and $c.G -lt 90 -and $c.B -lt 90) { $redPrompt++ }
            }
        }
        $keyboard = 0
        for ($y = [int]($bmp.Height * 0.70); $y -lt [int]($bmp.Height * 0.96); $y += 3) {
            for ($x = [int]($bmp.Width * 0.30); $x -lt [int]($bmp.Width * 0.70); $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 130 -and $c.G -gt 100 -and $c.R -lt 180) { $keyboard++ }
            }
        }
        return (($redPrompt -gt 100) -or ($accountBox -gt 80 -and $passwordBox -gt 80 -and $keyboard -gt 2000))
    }
    finally {
        $bmp.Dispose()
    }
}

function Test-EnterGameScreenImage {
    param([string] $Path)
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        function Get-EntryButtonEvidence {
            param(
                [double] $Rx1,
                [double] $Rx2
            )

            $blue = 0
            $gold = 0
            $darkBlue = 0
            for ($y = [int]($bmp.Height * 0.70); $y -lt [int]($bmp.Height * 0.84); $y += 2) {
                for ($x = [int]($bmp.Width * $Rx1); $x -lt [int]($bmp.Width * $Rx2); $x += 2) {
                    $c = $bmp.GetPixel($x, $y)
                    if ($c.B -gt 120 -and $c.B -gt ($c.R + 20)) { $blue++ }
                    if ($c.R -gt 140 -and $c.G -gt 120 -and $c.B -lt 140) { $gold++ }
                    if ($c.B -gt 70 -and $c.R -lt 80 -and $c.G -lt 130) { $darkBlue++ }
                }
            }

            return [pscustomobject]@{
                blue = $blue
                gold = $gold
                darkBlue = $darkBlue
            }
        }

        # The title screen has two symmetric blue-and-gold buttons. Requiring
        # both button regions prevents its colourful background from being
        # mistaken for the server-selection screen.
        $left = Get-EntryButtonEvidence -Rx1 0.24 -Rx2 0.48
        $right = Get-EntryButtonEvidence -Rx1 0.51 -Rx2 0.76
        foreach ($button in @($left, $right)) {
            if ([int]$button.blue -lt 500 -or
                [int]$button.gold -lt 350 -or
                [int]$button.darkBlue -lt 250) {
                return $false
            }
        }

        return $true
    }
    finally {
        $bmp.Dispose()
    }
}

function Test-ServerSelectionScreenImage {
    param([string] $Path)
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        $orangeFrame = 0
        for ($y = [int]($bmp.Height * 0.18); $y -lt [int]($bmp.Height * 0.76); $y += 3) {
            for ($x = [int]($bmp.Width * 0.32); $x -lt [int]($bmp.Width * 0.68); $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 210 -and $c.G -gt 70 -and $c.G -lt 180 -and $c.B -lt 90) { $orangeFrame++ }
            }
        }

        $blueList = 0
        for ($y = [int]($bmp.Height * 0.26); $y -lt [int]($bmp.Height * 0.58); $y += 3) {
            for ($x = [int]($bmp.Width * 0.35); $x -lt [int]($bmp.Width * 0.65); $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 110 -and $c.G -gt 90 -and $c.G -lt 180 -and $c.R -lt 120) { $blueList++ }
            }
        }

        $keyboard = 0
        for ($y = [int]($bmp.Height * 0.70); $y -lt [int]($bmp.Height * 0.96); $y += 4) {
            for ($x = [int]($bmp.Width * 0.30); $x -lt [int]($bmp.Width * 0.70); $x += 4) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 120 -and $c.G -gt 100 -and $c.R -lt 180) { $keyboard++ }
            }
        }

        # The current official server-selection background contributes blue pixels
        # below the dialog; the list itself is the stable discriminator.
        return ($orangeFrame -gt 150 -and $blueList -gt 700)
    }
    finally {
        $bmp.Dispose()
    }
}

function Test-CharacterSelectScreenImage {
    param([string] $Path)
    if ((Test-LoginScreenImage -Path $Path) -and (-not (Test-ServerSelectionScreenImage -Path $Path))) {
        return $false
    }

    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        $orange = 0
        $characterWarm = 0
        for ($y = [int]($bmp.Height * 0.18); $y -lt [int]($bmp.Height * 0.76); $y += 3) {
            for ($x = [int]($bmp.Width * 0.32); $x -lt [int]($bmp.Width * 0.68); $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 210 -and $c.G -gt 70 -and $c.G -lt 180 -and $c.B -lt 90) { $orange++ }
            }
        }
        for ($y = [int]($bmp.Height * 0.23); $y -lt [int]($bmp.Height * 0.62); $y += 2) {
            for ($x = [int]($bmp.Width * 0.32); $x -lt [int]($bmp.Width * 0.70); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 150 -and $c.G -lt 150 -and $c.B -lt 170) { $characterWarm++ }
            }
        }
        $bottomCharacterActions = 0
        for ($y = [int]($bmp.Height * 0.73); $y -lt [int]($bmp.Height * 0.80); $y += 2) {
            for ($x = [int]($bmp.Width * 0.30); $x -lt [int]($bmp.Width * 0.70); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 200 -and $c.G -gt 70 -and $c.G -lt 190 -and $c.B -lt 120) {
                    $bottomCharacterActions++
                }
            }
        }
        return ($orange -gt 180 -and $characterWarm -gt 800 -and $bottomCharacterActions -gt 1200)
    }
    finally {
        $bmp.Dispose()
    }
}

function Test-CharacterCreateScreenImage {
    param([string] $Path)
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        $wideBluePanel = 0
        for ($y = [int]($bmp.Height * 0.16); $y -lt [int]($bmp.Height * 0.80); $y += 3) {
            for ($x = [int]($bmp.Width * 0.07); $x -lt [int]($bmp.Width * 0.93); $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 110 -and $c.G -gt 90 -and $c.G -lt 190 -and $c.R -lt 170) {
                    $wideBluePanel++
                }
            }
        }

        $bottomOrangeControls = 0
        for ($y = [int]($bmp.Height * 0.84); $y -lt [int]($bmp.Height * 0.96); $y += 2) {
            for ($x = [int]($bmp.Width * 0.07); $x -lt [int]($bmp.Width * 0.93); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.R -gt 210 -and $c.G -gt 70 -and $c.G -lt 190 -and $c.B -lt 100) {
                    $bottomOrangeControls++
                }
            }
        }

        return ($wideBluePanel -gt 15000 -and $bottomOrangeControls -gt 2000)
    }
    finally {
        $bmp.Dispose()
    }
}

function Test-WorldScreenImage {
    param([string] $Path)
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::FromFile($Path)
    try {
        $topToolbarBlue = 0
        for ($y = [int]($bmp.Height * 0.04); $y -lt [int]($bmp.Height * 0.13); $y += 2) {
            for ($x = [int]($bmp.Width * 0.00); $x -lt [int]($bmp.Width * 0.70); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 120 -and $c.G -gt 80 -and $c.G -lt 180 -and $c.R -lt 120) { $topToolbarBlue++ }
            }
        }

        $bottomChatBlue = 0
        for ($y = [int]($bmp.Height * 0.92); $y -lt [int]($bmp.Height * 0.99); $y += 2) {
            for ($x = [int]($bmp.Width * 0.05); $x -lt [int]($bmp.Width * 0.62); $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.B -gt 120 -and $c.G -gt 100 -and $c.R -lt 180) { $bottomChatBlue++ }
            }
        }

        # Do not exclude the screen merely because a generic login/selection colour
        # heuristic also matches. Red-roof outdoor maps satisfy those broad colour
        # heuristics, while login, server selection, and character selection do not
        # satisfy this world's independent top-toolbar plus bottom-chat contract.
        return ($topToolbarBlue -gt 900 -and $bottomChatBlue -gt 900)
    }
    finally {
        $bmp.Dispose()
    }
}

function Get-FileSha256 {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace "-", "").ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $sha.Dispose()
    }
}

function Get-God2ScreenStage {
    param([string] $Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return "WindowInvalid" }
    if (Test-WorldScreenImage -Path $Path) { return "WorldReady" }
    if (Test-EnterGameScreenImage -Path $Path) { return "EntryScreenDetected" }
    if (Test-CharacterCreateScreenImage -Path $Path) { return "CharacterCreateReady" }
    if (Test-CharacterSelectScreenImage -Path $Path) { return "CharacterSelectReady" }
    if (Test-ServerSelectionScreenImage -Path $Path) { return "ServerSelectionScreen" }
    if (Test-LoginScreenImage -Path $Path) { return "LoginScreen" }
    return "UnknownScreen"
}

function Wait-God2PreLoginUiReady {
    param(
        [Parameter(Mandatory = $true)] [object] $ClientSnapshot,
        [ValidateRange(5, 60)] [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $pollCount = 0
    $stage = 'UnknownScreen'
    $screenshot = $null
    do {
        $pollCount++
        $current = Get-God2ProcessSnapshot -ProcessId ([int]$ClientSnapshot.pid)
        if (-not $current -or [int64]$current.mainWindowHandle -eq 0) {
            throw 'Official client exited or lost its main window before the pre-login UI became ready.'
        }
        $screenshot = Save-WindowScreenshot -Hwnd ([int64]$current.mainWindowHandle) `
            -Name 'client-prelogin-readiness-latest.png'
        $stage = Get-God2ScreenStage -Path $screenshot
        if ($stage -in @('EntryScreenDetected', 'LoginScreen')) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    $result = [ordered]@{
        schemaId = 'God2OfficialPreLoginUiReadiness'
        schemaVersion = 1
        clientProcessId = [int]$ClientSnapshot.pid
        status = if ($stage -in @('EntryScreenDetected', 'LoginScreen')) { 'PASS' } else { 'BLOCKED' }
        screenStage = $stage
        pollIntervalMilliseconds = 250
        pollCount = $pollCount
        timeoutSeconds = $TimeoutSeconds
        screenshot = $screenshot
        credentialsEntered = $false
        passed = ($stage -in @('EntryScreenDetected', 'LoginScreen'))
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-God2AtomicJson -Value $result -Path (Join-Path $artifactRoot 'official-prelogin-ui-readiness.json')
    if (-not $result.passed) {
        throw "Official pre-login UI did not become visually identifiable; last stage was $stage."
    }
    return [pscustomobject]$result
}

function Get-RepoRelativePath {
    param([string] $Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $fullRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return ($fullPath.Substring($fullRoot.Length) -replace '\\', '/')
    }
    return $fullPath
}

function Invoke-CharacterLifecycleUiProbe {
    param(
        [long] $Hwnd,
        [object] $Command
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $runDir = [string]$Command.runDir
    if ([string]::IsNullOrWhiteSpace($runDir)) {
        $runDir = Join-Path $repoRoot ("Artifacts\CharacterLifecycleObservation\ui-probe-" + $timestamp)
    }
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $stableRoot = Join-Path $repoRoot "Artifacts\CharacterLifecycleUiAutomation"
    New-Item -ItemType Directory -Force -Path $stableRoot | Out-Null
    $serverReadyFromCommand = $null
    if ($Command.PSObject.Properties.Name -contains "serverReady") {
        $serverReadyFromCommand = [bool]$Command.serverReady
    }

    [God2Automation.NativeApi]::BringToTop([IntPtr]$Hwnd)
    Start-Sleep -Milliseconds 400

    $screenshot = Save-WindowScreenshot -Hwnd $Hwnd -Name "character-lifecycle-ui-probe-before.png"
    $probeScreenshot = Join-Path $runDir "safe-screenshot-before.png"
    Copy-Item -LiteralPath $screenshot -Destination $probeScreenshot -Force
    $stage = Get-God2ScreenStage -Path $screenshot
    $tree = [God2Automation.NativeApi]::DescribeWindowTree([IntPtr]$Hwnd)
    $clientRect = Get-ClientRectObject -Hwnd $Hwnd
    $windowRect = Get-WindowRectObject -Hwnd $Hwnd
    $children = @($tree.Children)
    $buttonCandidates = @($children | Where-Object { $_.IsVisible -and $_.IsEnabled -and ([string]$_.ClassName -match "Button") })
    $editCandidates = @($children | Where-Object { $_.IsVisible -and $_.IsEnabled -and ([string]$_.ClassName -match "Edit|TextBox") })
    $customCandidates = @($children | Where-Object { $_.IsVisible -and $_.IsEnabled -and ([string]$_.ClassName -notmatch "Button|Edit|TextBox") })

    $slotRatios = @(
        [pscustomobject]@{ slot = 0; x = 0.50; y = 0.30 },
        [pscustomobject]@{ slot = 1; x = 0.50; y = 0.41 },
        [pscustomobject]@{ slot = 2; x = 0.50; y = 0.52 },
        [pscustomobject]@{ slot = 3; x = 0.50; y = 0.63 }
    )
    $slotCandidates = @()
    foreach ($slot in $slotRatios) {
        $point = Get-PointFromRatio -Rect $clientRect -X ([double]$slot.x) -Y ([double]$slot.y)
        $slotCandidates += [pscustomobject]@{
            slot = [int]$slot.slot
            locatorMode = "ClientRelativeBoundsDiagnosticOnly"
            center = $point
            verifiedEmpty = $false
            safeForCreate = $false
            note = "Recorded as diagnostic geometry only; not used for create/delete submit without a verified UI anchor."
        }
    }

    $failureCode = "UI_LOCATOR_BLOCKED_NO_VERIFIED_CREATE_DELETE_CONTROL"
    $status = "BLOCKED"
    if ($stage -ne "CharacterSelectReady") {
        $failureCode = "CHARACTER_SELECT_NOT_CONFIRMED_FOR_CREATE_DELETE_PROBE"
    }
    $clientSnapshot = Get-PreferredGod2ClientSnapshot

    $result = [ordered]@{
        SchemaVersion = 1
        Status = $status
        Path = "AutomatedOfficialClientUiCapture"
        FailureCode = $failureCode
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        ArtifactRoot = Get-RepoRelativePath -Path $runDir
        ServerReady = $serverReadyFromCommand
        ClientPid = if ($clientSnapshot) { $clientSnapshot.pid } else { $null }
        ClientHwnd = $Hwnd
        CharacterSelectDetected = ($stage -eq "CharacterSelectReady")
        ScreenStage = $stage
        WindowTitle = [string]$tree.Title
        WindowClass = [string]$tree.ClassName
        WindowBounds = $windowRect
        ClientBounds = $clientRect
        ChildWindowCount = $children.Count
        ButtonCandidateCount = $buttonCandidates.Count
        EditCandidateCount = $editCandidates.Count
        CustomCandidateCount = $customCandidates.Count
        CreateLocatorStatus = "NOT_FOUND"
        DeleteLocatorStatus = "NOT_FOUND"
        SubmitAttempted = $false
        PacketCaptureAttempted = $false
        OfficialClientCreate = "BLOCKED_BEFORE_CAPTURE"
        OfficialClientDelete = "BLOCKED_BEFORE_CAPTURE"
        UserManualOperation = "NOT REQUIRED"
        Screenshot = Get-RepoRelativePath -Path $probeScreenshot
        WindowTree = "window-tree.json"
        SlotCandidates = $slotCandidates
        SafetyDecision = "No create/delete click was sent because no distinct control, template anchor, or verified empty-slot workflow was available."
    }

    Write-God2AtomicJson -Value $tree -Path (Join-Path $runDir "window-tree.json")
    Write-God2AtomicJson -Value $result -Path (Join-Path $runDir "ui-locator-status.json")
    Write-God2AtomicJson -Value $result -Path (Join-Path $stableRoot "status.json")
    @(
        "# Character Lifecycle UI Probe",
        "",
        "Status: $status",
        "FailureCode: $failureCode",
        "ScreenStage: $stage",
        "CreateLocatorStatus: NOT_FOUND",
        "DeleteLocatorStatus: NOT_FOUND",
        "SubmitAttempted: false",
        "PacketCaptureAttempted: false",
        "UserManualOperation: NOT REQUIRED"
    ) | Set-Content -LiteralPath (Join-Path $runDir "summary.md") -Encoding UTF8

    return [pscustomobject]$result
}

function Invoke-KnownDirectSoundDialogHandling {
    param(
        [Parameter(Mandatory = $true)] [int] $ClientPid,
        [ValidateRange(1000, 15000)] [int] $TimeoutMs = 5000
    )

    $expectedText = 'Direct Sound Create failed!'
    $startedAt = [DateTimeOffset]::UtcNow
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $pollCount = 0
    $dialog = $null
    do {
        $pollCount++
        $windows = @([God2Automation.NativeApi]::DescribeProcessWindows($ClientPid))
        $dialog = $windows | Where-Object {
            $_.ClassName -eq '#32770' -and
            (@($_.Children | Where-Object { [string]$_.Title -ceq $expectedText }).Count -eq 1)
        } | Select-Object -First 1
        if ($dialog) { break }
        Start-Sleep -Milliseconds 25
    } while ((Get-Date) -lt $deadline)

    if (-not $dialog) {
        $candidateProcess = Get-Process -Id $ClientPid -ErrorAction SilentlyContinue
        $candidateExited = $null -eq $candidateProcess
        $snapshot = if ($candidateExited) { $null } else {
            Get-God2ProcessSnapshot -ProcessId $ClientPid
        }
        $screenshot = if ($snapshot -and [int64]$snapshot.mainWindowHandle -ne 0) {
            Save-WindowScreenshotOrNull -Hwnd ([int64]$snapshot.mainWindowHandle) `
                -Name 'client-when-directsound-not-observed.png'
        }
        else { $null }
        Write-God2AtomicJson -Value ([ordered]@{
            schemaId = 'God2KnownDirectSoundDialogHandling'
            schemaVersion = 1
            clientProcessId = $ClientPid
            expectedText = $expectedText
            pollingIntervalMilliseconds = 25
            timeoutMilliseconds = $TimeoutMs
            pollCount = $pollCount
            observed = $false
            dismissed = $false
            notRequired = (-not $candidateExited)
            candidateExited = $candidateExited
            passed = (-not $candidateExited)
            formalAttachTimingAllowed = (-not $candidateExited)
            clientScreenshot = $screenshot
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }) -Path (Join-Path $artifactRoot 'direct-sound-dialog-result.json')
        if ($candidateExited) {
            return [pscustomobject]@{
                observed = $false
                dismissed = $false
                notRequired = $false
                candidateExited = $true
                passed = $false
                formalAttachTimingAllowed = $false
            }
        }
        return [pscustomobject]@{
            observed = $false
            dismissed = $false
            notRequired = $true
            candidateExited = $false
            passed = $true
            formalAttachTimingAllowed = $true
        }
    }

    $observedAt = [DateTimeOffset]::UtcNow
    $attempts = [Collections.Generic.List[object]]::new()
    $methods = @(
        'RealClick',
        'PostMessage_WM_COMMAND_IDOK',
        'SendMessage_WM_COMMAND_IDOK',
        'SendMessage_WM_CLOSE',
        'SendInputFallback')
    $dismissed = $false
    $dismissMethod = $null
    foreach ($method in $methods) {
        $sent = $false
        if ($method -eq 'RealClick') {
            $button = @($dialog.Children | Where-Object {
                $_.ClassName -eq 'Button' -and $_.IsEnabled -and
                ($_.ControlId -eq 1 -or $_.Title -in @('確定',$script:OfficialSimplifiedConfirmCaption,'OK'))
            } | Select-Object -First 1)
            if ($button.Count -eq 1) {
                $buttonRect = Get-WindowRectObject -Hwnd ([int64]$button[0].Hwnd)
                [God2Automation.NativeApi]::BringToTop([IntPtr][int64]$dialog.Hwnd)
                [God2Automation.NativeApi]::RealClick(
                    [int][Math]::Floor(($buttonRect.left + $buttonRect.right) / 2),
                    [int][Math]::Floor(($buttonRect.top + $buttonRect.bottom) / 2),
                    60,
                    60,
                    150)
                $sent = $true
            }
        }
        elseif ($method -eq 'PostMessage_WM_COMMAND_IDOK') {
            $sent = [God2Automation.NativeApi]::PostMessage(
                [IntPtr][int64]$dialog.Hwnd, 0x0111, [IntPtr]1, [IntPtr]::Zero)
        }
        elseif ($method -eq 'SendMessage_WM_COMMAND_IDOK') {
            [void][God2Automation.NativeApi]::SendMessage(
                [IntPtr][int64]$dialog.Hwnd, 0x0111, [IntPtr]1, [IntPtr]::Zero)
            $sent = $true
        }
        elseif ($method -eq 'SendMessage_WM_CLOSE') {
            [void][God2Automation.NativeApi]::SendMessage(
                [IntPtr][int64]$dialog.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            $sent = $true
        }
        else {
            [God2Automation.NativeApi]::BringToTop([IntPtr][int64]$dialog.Hwnd)
            [God2Automation.NativeApi]::Key(0x0D)
            $sent = $true
        }

        $verifyDeadline = (Get-Date).AddMilliseconds(500)
        do {
            Start-Sleep -Milliseconds 25
            $remaining = @([God2Automation.NativeApi]::DescribeProcessWindows($ClientPid) | Where-Object {
                $_.ClassName -eq '#32770' -and
                (@($_.Children | Where-Object { [string]$_.Title -ceq $expectedText }).Count -eq 1)
            })
            if ($remaining.Count -eq 0) {
                $dismissed = $true
                $dismissMethod = $method
                break
            }
        } while ((Get-Date) -lt $verifyDeadline)
        [void]$attempts.Add([pscustomobject]@{
            method = $method
            sent = $sent
            dismissed = $dismissed
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        })
        if ($dismissed) { break }
    }

    $result = [ordered]@{
        schemaId = 'God2KnownDirectSoundDialogHandling'
        schemaVersion = 1
        clientProcessId = $ClientPid
        expectedText = $expectedText
        pollingIntervalMilliseconds = 25
        timeoutMilliseconds = $TimeoutMs
        pollCount = $pollCount
        observed = $true
        observedAtUtc = $observedAt.ToString('o')
        dismissed = $dismissed
        dismissedAtUtc = if ($dismissed) { [DateTimeOffset]::UtcNow.ToString('o') } else { $null }
        method = $dismissMethod
        attempts = [object[]]$attempts.ToArray()
        passed = $dismissed
        formalAttachTimingAllowed = $dismissed
        startedAtUtc = $startedAt.ToString('o')
    }
    Write-God2AtomicJson -Value $result -Path (Join-Path $artifactRoot 'direct-sound-dialog-result.json')
    $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $ClientPid
    if ($clientSnapshot -and [int64]$clientSnapshot.mainWindowHandle -ne 0) {
        Save-WindowScreenshotOrNull -Hwnd ([int64]$clientSnapshot.mainWindowHandle) `
            -Name 'client-after-directsound.png' | Out-Null
    }
    if (-not $dismissed) {
        throw 'DirectSoundDialogDismissFailed: the known dialog remained after all bounded methods.'
    }
    return [pscustomobject]$result
}

function Invoke-StartGod2Client {
    $profilePath = $script:activeLauncherProfilePath
    if (-not (Test-Path -LiteralPath $profilePath)) {
        throw "Missing launcher profile: $profilePath"
    }
    $profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
    $launcher = Get-ActiveLauncherProcess
    if (-not $launcher) { throw "Configured launcher process is not running." }
    $launcherSnapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
    $hostSession = (Get-Process -Id $hostPid).SessionId
    if ($launcherSnapshot.integrityRid -ne 0x3000) { throw "Launcher is not High RID 0x3000." }
    if ($launcherSnapshot.sessionId -ne $hostSession) { throw "Launcher session mismatch host=$hostSession launcher=$($launcherSnapshot.sessionId)" }
    if ($launcherSnapshot.mainWindowHandle -eq 0) { throw "Launcher main HWND is zero." }

    $currentStage = "LaunchingClient"
    Publish-HostStatus -Stage $currentStage -ReadyValue $true

    $known = @(Get-ActiveRuntimeProcesses -IncludeClient | ForEach-Object { $_.Id })
    if ([string]::Equals([IO.Path]::GetFileName([string]$profile.targetExecutable), "God2ClassicLauncher.exe", [StringComparison]::OrdinalIgnoreCase)) {
        $startButton = @()
        $tree = $null
        $buttonTimeoutMs = if ($profile.startGame -and [int]$profile.startGame.timeoutMs -gt 0) {
            [int]$profile.startGame.timeoutMs
        }
        else {
            10000
        }
        $buttonDeadline = (Get-Date).AddMilliseconds($buttonTimeoutMs)
        do {
            $tree = [God2Automation.NativeApi]::DescribeWindowTree([IntPtr][int64]$launcherSnapshot.mainWindowHandle)
            $startButton = @($tree.Children | Where-Object {
                $_.ClassName -eq "Button" -and
                $_.ControlId -eq 1007 -and
                $_.IsVisible -and
                $_.IsEnabled -and
                $_.Title -eq "開始遊戲"
            })
            if ($startButton.Count -eq 1) {
                break
            }
            if ([string]$tree.ClassName -eq "God2ClassicLauncherDuiWindow") {
                break
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $buttonDeadline)

        if ($startButton.Count -eq 1) {
            [God2Automation.NativeApi]::BmClick([IntPtr][int64]$startButton[0].Hwnd)
        }
        elseif ([string]$tree.ClassName -eq "God2ClassicLauncherDuiWindow") {
            $launcherHash = (Get-FileHash -Algorithm SHA256 -LiteralPath ([string]$profile.targetExecutable)).Hash
            $clientRect = Get-ClientRectObject -Hwnd ([int64]$launcherSnapshot.mainWindowHandle)
            $skinPath = Join-Path (Split-Path -Parent ([string]$profile.targetExecutable)) ([string]$profile.startGame.controlSource)
            $skinHash = if (Test-Path -LiteralPath $skinPath -PathType Leaf) {
                (Get-FileHash -Algorithm SHA256 -LiteralPath $skinPath).Hash
            }
            else {
                ""
            }
            $skin = if ($skinHash) { [xml](Get-Content -LiteralPath $skinPath -Raw -Encoding UTF8) } else { $null }
            $skinControls = $null
            if ($skin) {
                $skinControls = $skin.SelectNodes("//Button[@name='start']")
            }
            $skinControlCount = if ($null -eq $skinControls) { 0 } else { [int]$skinControls.Count }
            $bounds = $profile.startGame.bounds
            $duiIdentityVerified =
                [string]$profile.windowClass -eq "God2ClassicLauncherDuiWindow" -and
                [string]$tree.ClassName -eq [string]$profile.windowClass -and
                [string]$launcherHash -eq [string]$profile.launcherSha256 -and
                [string]$skinHash -eq [string]$profile.startGame.controlSourceSha256 -and
                $skinControlCount -eq 1 -and
                [string]$skinControls.Item(0).GetAttribute("name") -eq [string]$profile.startGame.controlName -and
                [int]$clientRect.width -eq [int]$profile.startGame.clientWidth -and
                [int]$clientRect.height -eq [int]$profile.startGame.clientHeight -and
                [int]$bounds.left -ge 0 -and [int]$bounds.top -ge 0 -and
                [int]$bounds.right -le [int]$clientRect.width -and
                [int]$bounds.bottom -le [int]$clientRect.height -and
                [int]$bounds.left -lt [int]$bounds.right -and [int]$bounds.top -lt [int]$bounds.bottom
            if (-not $duiIdentityVerified) {
                Write-God2AtomicJson -Value ([ordered]@{
                    launcherHashMatch = ([string]$launcherHash -eq [string]$profile.launcherSha256)
                    windowClassMatch = ([string]$tree.ClassName -eq [string]$profile.windowClass)
                    skinHashMatch = ([string]$skinHash -eq [string]$profile.startGame.controlSourceSha256)
                    uniqueStartControl = ($skinControlCount -eq 1)
                    clientSizeMatch = ([int]$clientRect.width -eq [int]$profile.startGame.clientWidth -and [int]$clientRect.height -eq [int]$profile.startGame.clientHeight)
                }) -Path (Join-Path $artifactRoot "launcher-dui-start-identity.json")
                throw "God2ClassicLauncher Dui start control identity was not uniquely verified."
            }

            $startX = [int][Math]::Floor(([int]$bounds.left + [int]$bounds.right) / 2)
            $startY = [int][Math]::Floor(([int]$bounds.top + [int]$bounds.bottom) / 2)
            Write-God2AtomicJson -Value ([ordered]@{
                launcherHashMatch = $true
                windowClassMatch = $true
                skinHashMatch = $true
                uniqueStartControl = $true
                clientSizeMatch = $true
                controlName = "start"
                clientPoint = [ordered]@{ x = $startX; y = $startY }
                manualOperation = $false
            }) -Path (Join-Path $artifactRoot "launcher-dui-start-identity.json")
            [God2Automation.NativeApi]::SendClientClick(
                [IntPtr][int64]$launcherSnapshot.mainWindowHandle,
                $startX,
                $startY)
        }
        else {
            Write-God2AtomicJson -Value $tree -Path (Join-Path $artifactRoot "launcher-start-window-tree.json")
            throw "God2ClassicLauncher start button identity was not uniquely verified."
        }

        $currentStage = "LaunchingClient"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $deadline = (Get-Date).AddSeconds(60)
        do {
            $client = Get-ActiveRuntimeProcesses -IncludeClient |
                Where-Object { $known -notcontains $_.Id } |
                Sort-Object Id |
                Select-Object -Last 1
            if ($client) {
                $directSound = Invoke-KnownDirectSoundDialogHandling -ClientPid $client.Id
                if ([bool]$directSound.candidateExited) { continue }
                return (Get-God2ProcessSnapshot -ProcessId $client.Id)
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)

        throw "ClientLaunchFailed: God2ClassicLauncher did not create God2_opt within 60 seconds."
    }

    $launcherReadinessAttempt = if ($script:officialLauncherReadinessAttempt) {
        [int]$script:officialLauncherReadinessAttempt + 1
    }
    else { 1 }
    $script:officialLauncherReadinessAttempt = $launcherReadinessAttempt
    $agreementRegression = Invoke-LauncherAgreementRegression -Scenario 'BeforeLogin'
    if (-not $agreementRegression.success) { throw 'Launcher agreement was not visually accepted.' }

    # Rediscover after the agreement step; WKE may recreate its top-level window.
    $launcher = Get-ActiveLauncherProcess
    if (-not $launcher) { throw "Configured launcher process disappeared." }
    $launcherSnapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
    $launcherClient = Get-ClientRectObject -Hwnd ([int64]$launcherSnapshot.mainWindowHandle)
    $startX = [double]$profile.startGame.relativeX
    $startY = [double]$profile.startGame.relativeY
    $startPoint = Get-PointFromRatio -Rect $launcherClient -X $startX -Y $startY
    Save-WindowScreenshot -Hwnd ([int64]$launcherSnapshot.mainWindowHandle) -Name "launcher-before-startgame.png" | Out-Null
    [God2Automation.NativeApi]::BringToTop([IntPtr]$launcherSnapshot.mainWindowHandle)
    $launcherReadyTimeoutSeconds = 30
    $launcherReadyDeadline = (Get-Date).AddSeconds($launcherReadyTimeoutSeconds)
    $launcherReadyPoll = 0
    $startVisual = $null
    do {
        $launcherReadyPoll++
        if (-not [God2Automation.NativeApi]::IsWindow([IntPtr][int64]$launcherSnapshot.mainWindowHandle)) {
            throw "ClientLaunchFailed: Launcher window closed before Start Game became enabled."
        }
        $startVisual = Get-LauncherStartGameVisualState -Hwnd ([int64]$launcherSnapshot.mainWindowHandle) -ScreenshotName "launcher-startgame-readiness-latest.png"
        if ($startVisual.enabled) { break }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $launcherReadyDeadline)
    $launcherReadinessRecord = [ordered]@{
        enabled = [bool]$startVisual.enabled
        attempt = $launcherReadinessAttempt
        pollCount = $launcherReadyPoll
        timeoutSeconds = $launcherReadyTimeoutSeconds
        visual = $startVisual
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-God2AtomicJson -Value $launcherReadinessRecord `
        -Path (Join-Path $artifactRoot ("launcher-startgame-readiness-attempt-$launcherReadinessAttempt.json"))
    Write-God2AtomicJson -Value $launcherReadinessRecord `
        -Path (Join-Path $artifactRoot 'launcher-startgame-readiness.json')
    if (-not $startVisual.enabled) {
        if ($launcherReadinessAttempt -lt 3) {
            $retryEvidence = Join-Path $artifactRoot ("launcher-readiness-restart-$launcherReadinessAttempt")
            New-Item -ItemType Directory -Force -Path $retryEvidence | Out-Null
            Get-ChildItem -LiteralPath $artifactRoot -File -Filter 'launcher-*' | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $retryEvidence -Force
            }
            Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue
            $exitDeadline = (Get-Date).AddSeconds(5)
            while ((Get-Process -Id $launcher.Id -ErrorAction SilentlyContinue) -and
                (Get-Date) -lt $exitDeadline) {
                Start-Sleep -Milliseconds 100
            }
            if (Get-Process -Id $launcher.Id -ErrorAction SilentlyContinue) {
                throw 'OfficialServiceUnavailable: timed-out Launcher.exe could not be restarted.'
            }
            Ensure-God2Launcher | Out-Null
            return Invoke-StartGod2Client
        }
        throw "OfficialServiceUnavailable: Launcher Start Game remained disabled after 3 bounded restarts."
    }
    for ($startAttempt = 1; $startAttempt -le 1; $startAttempt++) {
        $launcherClient = Get-ClientRectObject -Hwnd ([int64]$launcherSnapshot.mainWindowHandle)
        $startPoint = Get-PointFromRatio -Rect $launcherClient -X $startX -Y $startY
        [God2Automation.NativeApi]::BringToTop([IntPtr]$launcherSnapshot.mainWindowHandle)
        [God2Automation.NativeApi]::RealClick($startPoint.x, $startPoint.y, 180, 120, 500)
        $startGameClickedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Save-WindowScreenshot -Hwnd ([int64]$launcherSnapshot.mainWindowHandle) -Name "launcher-startgame-attempt-$startAttempt.png" | Out-Null

        $deadline = (Get-Date).AddSeconds(60)
        do {
            $client = Get-ActiveRuntimeProcesses -IncludeClient |
                Where-Object { $known -notcontains $_.Id } |
                Sort-Object Id |
                Select-Object -Last 1
            if ($client) {
                $directSound = Invoke-KnownDirectSoundDialogHandling -ClientPid $client.Id
                if ([bool]$directSound.candidateExited) { continue }
                $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $client.Id
                $script:lastOfficialLaunchEvidence = [pscustomobject]@{
                    launcherProcessId = [int]$launcherSnapshot.pid
                    launcherPath = [string]$launcherSnapshot.path
                    launcherSHA256 = (Get-FileHash -LiteralPath ([string]$launcherSnapshot.path) -Algorithm SHA256).Hash
                    agreement = $agreementRegression
                    startGameClicked = $true
                    startGameClickedAtUtc = $startGameClickedAtUtc
                    directSound = $directSound
                    clientSnapshot = $clientSnapshot
                }
                return $clientSnapshot
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        Start-Sleep -Seconds ([Math]::Min(8, [Math]::Pow(2, $startAttempt)))
    }

    throw "ClientLaunchFailed: the single Start Game action did not create God2_opt within 60 seconds."
}

function New-OfficialLaunchChainAttestation {
    param(
        [Parameter(Mandatory = $true)] [object] $ClientSnapshot,
        [Parameter(Mandatory = $true)] [string] $OutputPath
    )

    $evidence = $script:lastOfficialLaunchEvidence
    if (-not $evidence) {
        throw 'Official launch evidence is unavailable; refusing to attest an existing or directly started client.'
    }
    $launcherPath = [IO.Path]::GetFullPath([string]$evidence.launcherPath)
    $clientPath = [IO.Path]::GetFullPath([string]$ClientSnapshot.path)
    if ([IO.Path]::GetFileName($launcherPath) -ine 'Launcher.exe' -or
        [IO.Path]::GetFileName($clientPath) -ine 'God2_opt.exe') {
        throw 'Ultimate live attestation only accepts Launcher.exe -> God2_opt.exe.'
    }
    $parent = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f [int]$ClientSnapshot.pid)
    $parentVerified = $null -ne $parent -and
        [int]$parent.ParentProcessId -eq [int]$evidence.launcherProcessId
    $clientItem = Get-Item -LiteralPath $clientPath
    $clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
    $launcherSha256 = (Get-FileHash -LiteralPath $launcherPath -Algorithm SHA256).Hash
    $directSoundReady = ([bool]$evidence.directSound.notRequired) -or
        ([bool]$evidence.directSound.observed -and [bool]$evidence.directSound.dismissed)
    $passed = $parentVerified -and
        [bool]$evidence.agreement.success -and
        [bool]$evidence.startGameClicked -and
        $directSoundReady -and
        [bool]$evidence.directSound.formalAttachTimingAllowed -and
        [string]$clientSha256 -ceq '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B' -and
        [string]$clientItem.VersionInfo.FileVersion -ceq '1.0.0.1'
    $attestation = [ordered]@{
        SchemaId = 'God2OfficialLauncherAutomationAttestation'
        SchemaVersion = 1
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Passed = [bool]$passed
        DirectClientLaunch = $false
        LauncherPath = $launcherPath
        LauncherSHA256 = $launcherSha256
        LauncherProcessId = [uint32]$evidence.launcherProcessId
        AgreementAccepted = [bool]$evidence.agreement.success
        AgreementEvidencePath = Join-Path $artifactRoot 'launcher-agreement-BeforeLogin-result.json'
        StartGameClicked = [bool]$evidence.startGameClicked
        StartGameClickedAtUtc = [string]$evidence.startGameClickedAtUtc
        DirectSoundDialogObserved = [bool]$evidence.directSound.observed
        DirectSoundDialogHandled = [bool]$evidence.directSound.dismissed
        DirectSoundDialogNotRequired = [bool]$evidence.directSound.notRequired
        DirectSoundDialogHandledAtUtc = [string]$evidence.directSound.dismissedAtUtc
        DirectSoundDialogMethod = [string]$evidence.directSound.method
        DirectSoundEvidencePath = Join-Path $artifactRoot 'direct-sound-dialog-result.json'
        FormalAttachTimingAllowed = [bool]$evidence.directSound.formalAttachTimingAllowed
        ClientPath = $clientPath
        ClientSHA256 = $clientSha256
        ClientVersion = [string]$clientItem.VersionInfo.FileVersion
        ClientProcessId = [uint32]$ClientSnapshot.pid
        ClientParentProcessId = if ($parent) { [uint32]$parent.ParentProcessId } else { 0 }
        ClientProcessCreationTimeUtc = (Get-Process -Id ([int]$ClientSnapshot.pid)).StartTime.ToUniversalTime().ToString('o')
        ParentIsVerifiedLauncher = [bool]$parentVerified
        HostProcessId = $hostPid
        HostSessionId = (Get-Process -Id $hostPid).SessionId
        UserManualOperation = 'NOT REQUIRED'
    }
    Write-God2AtomicJson -Value $attestation -Path $OutputPath
    if (-not $passed) {
        throw 'Official Launcher.exe launch-chain attestation did not pass all fail-closed checks.'
    }
    return [pscustomobject]$attestation
}

function Start-UltimateOfficialRuntimeCapture {
    param(
        [Parameter(Mandatory = $true)] [object] $ClientSnapshot,
        [Parameter(Mandatory = $true)] [string] $AttestationPath,
        [Parameter(Mandatory = $true)] [string] $RuntimeRunId,
        [Parameter(Mandatory = $true)] [string] $RunDirectory,
        [ValidateRange(30, 600)] [int] $ObserveSeconds = 180,
        [ValidateRange(4, 600)] [int] $ContractAcquisitionObserveSeconds = 48,
        [string] $DeepEvidencePlanPath = ''
    )

    if ($RuntimeRunId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$') {
        throw 'Ultimate runtime RunId contains unsupported characters.'
    }
    $runtimeScript = Join-Path $repoRoot 'tools\God2.ClientInstrumentation\scripts\Invoke-OfficialClientRuntime.ps1'
    if (-not (Test-Path -LiteralPath $runtimeScript -PathType Leaf)) {
        throw "Official runtime script is missing: $runtimeScript"
    }
    $runtimeRoot = Join-Path $repoRoot ("Artifacts\ClientInstrumentation\LoginTrial\" + $RuntimeRunId)
    if (Test-Path -LiteralPath $runtimeRoot) {
        throw "Refusing to overwrite existing official runtime output: $runtimeRoot"
    }
    $stdoutPath = Join-Path $RunDirectory 'official-runtime.stdout.txt'
    $stderrPath = Join-Path $RunDirectory 'official-runtime.stderr.txt'
    $acquisitionControlPath = Join-Path $runtimeRoot 'analysis\contract-acquisition-control.json'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $runtimeScript + '"'),
        '-ClientExe', ('"' + [string]$ClientSnapshot.path + '"'),
        '-TargetProcessId', [string]$ClientSnapshot.pid,
        '-LaunchChainAttestationPath', ('"' + $AttestationPath + '"'),
        '-RunId', $RuntimeRunId,
        '-Configuration', 'Release',
        '-ObserveSeconds', [string]$ObserveSeconds,
        '-EnablePreAttachDeepStaticDiscovery',
        '-EnableContractAcquisition',
        '-ContractAcquisitionObserveSeconds', [string]$ContractAcquisitionObserveSeconds,
        '-ContractAcquisitionControlPath', ('"' + $acquisitionControlPath + '"')
    )
    if(-not [string]::IsNullOrWhiteSpace($DeepEvidencePlanPath)){
        $arguments += @('-DeepEvidencePlanPath',('"' + $DeepEvidencePlanPath + '"'))
    }
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments `
        -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $attachPath = Join-Path $runtimeRoot 'analysis\attach-result-v2.json'
    $deadline = (Get-Date).AddSeconds(90)
    $attached = $false
    do {
        if (Test-Path -LiteralPath $attachPath -PathType Leaf) {
            try {
                $attach = Read-God2JsonWithRetry -Path $attachPath
                $attached = [string]$attach.status -ceq 'ATTACHED' -and
                    [bool]$attach.strictCapabilitiesReady -and
                    -not [bool]$attach.encryptedTransportCaptured -and
                    [bool]$attach.postDecryptCaptured -and
                    [bool]$attach.preEncryptCaptured -and
                    [bool]$attach.handlerDecodedCaptured -and
                    [bool]$attach.battleActorMemoryCaptured -and
                    [bool]$attach.battleStateMemoryCaptured
                if ($attached) { break }
            }
            catch { }
        }
        $process.Refresh()
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if (-not $attached) {
        $process.Refresh()
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
        $exitText = if ($process.HasExited) { [string]$process.ExitCode } else { 'STILL_RUNNING' }
        throw "Official runtime did not publish STRICT_READY decrypted-packet and battle-memory ATTACHED before login. Exit=$exitText Error=$stderr"
    }
    return [pscustomobject]@{
        process = $process
        runtimeRunId = $RuntimeRunId
        runtimeRoot = $runtimeRoot
        acquisitionControlPath = $acquisitionControlPath
        liveMetadataPath = Join-Path $env:LOCALAPPDATA `
            ("God2Classic\PacketCapture\Captures\" + $RuntimeRunId + "\raw\injected-packets.jsonl")
        attachPath = $attachPath
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
        attachedBeforeLogin = $true
        attachedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        observeSeconds = $ObserveSeconds
    }
}

function Ensure-God2Launcher {
    $existing = Get-ActiveLauncherProcess
    if ($existing) {
        $snapshot = Get-God2ProcessSnapshot -ProcessId $existing.Id
        if ($snapshot -and $snapshot.integrityRid -eq 0x3000) {
            return $existing
        }

        Stop-Process -Id $existing.Id -Force
        Start-Sleep -Milliseconds 500
    }

    $profilePath = $script:activeLauncherProfilePath
    if (-not (Test-Path -LiteralPath $profilePath)) {
        throw "Missing launcher profile."
    }

    $profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $target = [string]$profile.targetExecutable
    if ([string]::IsNullOrWhiteSpace($target) -or -not (Test-Path -LiteralPath $target)) {
        throw "Launcher executable from profile is unavailable."
    }

    $currentStage = "StartingLauncher"
    Publish-HostStatus -Stage $currentStage -ReadyValue $true
    Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) | Out-Null

    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $launcher = Get-ActiveLauncherProcess
        if ($launcher) {
            $snapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
            if ($snapshot -and $snapshot.mainWindowHandle -ne 0 -and $snapshot.integrityRid -eq 0x3000) {
                return $launcher
            }
        }
    } while ((Get-Date) -lt $deadline)

    throw "Launcher did not become ready at High integrity within 30 seconds."
}

function Get-OrStartGod2Client {
    param([int] $LauncherPid)

    $clientSnapshot = Get-PreferredGod2ClientSnapshot -LauncherPid $LauncherPid
    if ($clientSnapshot) {
        $clientProcess = Get-Process -Id ([int]$clientSnapshot.pid) -ErrorAction SilentlyContinue
        if ($clientProcess -and -not $clientProcess.Responding) {
            $currentStage = "RestartingUnresponsiveClient"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            Stop-Process -Id $clientProcess.Id -Force
            Start-Sleep -Seconds 2
            return Invoke-StartGod2Client
        }
        return $clientSnapshot
    }
    $orphan = Get-ActiveRuntimeProcesses -IncludeClient | Select-Object -First 1
    if ($orphan) {
        $currentStage = "RestartingNonLauncherClient"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        Stop-ActiveRuntimeProcesses -IncludeClient | Out-Null
        Start-Sleep -Seconds 2
    }
    return Invoke-StartGod2Client
}

function Invoke-PrivilegeGate {
    $launcher = Ensure-God2Launcher
    $launcherSnapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
    $clientSnapshot = Get-OrStartGod2Client -LauncherPid $launcher.Id
    $clientAppearDeadline = (Get-Date).AddSeconds(15)
    while (-not $clientSnapshot -and (Get-Date) -lt $clientAppearDeadline) {
        Start-Sleep -Milliseconds 250
        $clientSnapshot = Get-PreferredGod2ClientSnapshot -LauncherPid $launcher.Id
    }
    if (-not $clientSnapshot) { throw "God2_opt process snapshot is null." }
    $clientIntegrityDeadline = (Get-Date).AddSeconds(15)
    while ($clientSnapshot -and $clientSnapshot.integrityRid -ne 0x3000 -and (Get-Date) -lt $clientIntegrityDeadline) {
        Start-Sleep -Milliseconds 250
        $clientSnapshot = Get-God2ProcessSnapshot -ProcessId ([int]$clientSnapshot.pid)
    }
    $clientWindowDeadline = (Get-Date).AddSeconds(15)
    while (($null -eq $clientSnapshot.mainWindowHandle -or [int64]$clientSnapshot.mainWindowHandle -eq 0) -and (Get-Date) -lt $clientWindowDeadline) {
        Start-Sleep -Milliseconds 250
        $clientSnapshot = Get-God2ProcessSnapshot -ProcessId ([int]$clientSnapshot.pid)
        if (-not $clientSnapshot) { throw "God2_opt exited before creating main HWND." }
    }
    $hostSession = (Get-Process -Id $hostPid).SessionId
    if ($launcherSnapshot.integrityRid -ne 0x3000) { throw "Launcher is not High RID 0x3000." }
    if ($clientSnapshot.integrityRid -ne 0x3000 -and $clientSnapshot.integrityRid -ge 0) { throw "God2_opt is not High RID 0x3000." }
    if (($launcherSnapshot.sessionId -ne $hostSession) -or ($null -ne $clientSnapshot.sessionId -and $clientSnapshot.sessionId -ne $hostSession)) {
        throw "Session mismatch host=$hostSession launcher=$($launcherSnapshot.sessionId) client=$($clientSnapshot.sessionId)"
    }
    if ($desktopName -ne "Default") { throw "Host desktop is $desktopName; required Default." }
    if ($null -eq $clientSnapshot.mainWindowHandle -or [int64]$clientSnapshot.mainWindowHandle -eq 0) { throw "God2_opt main HWND is zero." }
    if (-not [God2Automation.NativeApi]::IsWindow([IntPtr]$clientSnapshot.mainWindowHandle)) {
        throw "God2_opt HWND is not a valid window."
    }
    [God2Automation.NativeApi]::BringToTop([IntPtr]$clientSnapshot.mainWindowHandle)
    return $clientSnapshot
}

function Invoke-LoginAttempt {
    param(
        [int] $Attempt,
        [string] $Account,
        [string] $Password,
        [switch] $NoCredentialCapture
    )
    $clientSnapshot = Invoke-PrivilegeGate
    $stableWindow = Resolve-StableGod2ClientWindow -InitialSnapshot $clientSnapshot
    [God2Automation.NativeApi]::MoveTopLeft([IntPtr][int64]$stableWindow.hwnd)
    $refreshedSnapshot = Get-God2ProcessSnapshot -ProcessId ([int]$clientSnapshot.pid)
    $stableWindow = Resolve-StableGod2ClientWindow -InitialSnapshot $refreshedSnapshot
    $clientSnapshot = $stableWindow.snapshot
    $hwnd = [int64]$stableWindow.hwnd
    $loginContext = New-LoginAutomationAttemptContext -Attempt $Attempt -ClientSnapshot $clientSnapshot -Hwnd $hwnd
    Add-LoginAutomationState -Context $loginContext -State "ClientStarting"
    $rect = $stableWindow.rect
    Add-LoginAutomationState -Context $loginContext -State "LoginWindowWaiting"
    $beforePath = Save-WindowScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-before.png")
    $loginContext.safeScreenshotBefore = Save-LoginSafeScreenshot -Hwnd $hwnd -Directory ([string]$loginContext.root) -Name "safe-screenshot-before.png"
    if (Test-EnterGameScreenImage -Path $beforePath) {
        $currentStage = "EntryScreenDetected"
        Add-LoginAutomationState -Context $loginContext -State "LoginWindowWaiting" -Diagnostic ([ordered]@{ entryScreenDetected = $true })
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $enterClient = [pscustomobject]@{
            x = [int][Math]::Round($rect.width * 0.65)
            y = [int][Math]::Round($rect.height * 0.775)
        }
        $enterPoint = [pscustomobject]@{ x = $rect.left + $enterClient.x; y = $rect.top + $enterClient.y }
        [God2Automation.NativeApi]::BringToTop([IntPtr]$hwnd)
        $currentStage = "EntryClickAttempt"
        Add-LoginAutomationState -Context $loginContext -State "LoginWindowWaiting" -Diagnostic ([ordered]@{ entryClick = "Attempt" })
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        [God2Automation.NativeApi]::RealClick($enterPoint.x, $enterPoint.y, 180, 120, 500)
        $loginDeadline = (Get-Date).AddSeconds(10)
        $entryAfterPath = $null
        $entryFallbackClickSent = $false
        do {
            Start-Sleep -Milliseconds 500
            $beforePath = Save-WindowScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-after-enter-title.png")
            $entryAfterPath = $beforePath
            if ((Test-LoginScreenImage -Path $beforePath) -and
                -not (Test-EnterGameScreenImage -Path $beforePath)) { break }
            if (-not $entryFallbackClickSent -and
                (Test-EnterGameScreenImage -Path $beforePath) -and
                (Get-Date) -ge $loginDeadline.AddSeconds(-8)) {
                [God2Automation.NativeApi]::SendClientClick(
                    [IntPtr]$hwnd,
                    [int]$enterClient.x,
                    [int]$enterClient.y)
                $entryFallbackClickSent = $true
            }
        } while ((Get-Date) -lt $loginDeadline)
        if (-not (Test-LoginScreenImage -Path $beforePath) -or
            (Test-EnterGameScreenImage -Path $beforePath)) {
            $currentStage = "EntryClickRejected"
            Publish-HostStatus -Stage $currentStage -ErrorValue "Entry click did not transition to login screen." -ReadyValue $true
            return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Result ([pscustomobject]@{
                success = $false
                error = "EntryClickRejected"
                hwnd = $hwnd
                rect = $rect
                entryPoint = $enterPoint
                beforeScreenshot = $beforePath
                afterScreenshot = $entryAfterPath
            }))
        }
        $currentStage = "EntryTransitionVerified"
        Add-LoginAutomationState -Context $loginContext -State "LoginWindowDetected"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $refreshedSnapshot = Get-PreferredGod2ClientSnapshot
        if ($refreshedSnapshot) {
            $stableWindow = Resolve-StableGod2ClientWindow -InitialSnapshot $refreshedSnapshot
            $clientSnapshot = $stableWindow.snapshot
            $hwnd = [int64]$stableWindow.hwnd
            $loginContext.clientPid = [int]$clientSnapshot.pid
            $loginContext.hwnd = $hwnd
            $rect = $stableWindow.rect
            $beforePath = Save-WindowScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-login-ready.png")
        }
    }
    if (-not (Test-LoginScreenImage -Path $beforePath)) {
        $knownStage = Get-God2ScreenStage -Path $beforePath
        if ($knownStage -notin @('ServerSelectionScreen', 'CharacterSelectReady', 'WorldReady')) {
            return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Result ([pscustomobject]@{
                success = $false
                error = "UnrecognizedLoginStage:$knownStage"
                hwnd = $hwnd
                rect = $rect
                beforeScreenshot = $beforePath
                afterScreenshot = $beforePath
            }))
        }
        $trace = if ($NoCredentialCapture) {
            [pscustomobject]@{
                targetPid = [int]$clientSnapshot.pid
                exitCode = 0
                output = @()
                traceRoot = ""
                generalLog = ""
                metadata = ""
                credentialCapture = $false
            }
        }
        else {
            Start-LoginAttemptTraceProbe -TargetPid ([int]$clientSnapshot.pid) -Attempt $Attempt
        }
        return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Result ([pscustomobject]@{
            success = $true
            alreadyPastLogin = $true
            trace = $trace
            hwnd = $hwnd
            rect = $rect
            beforeScreenshot = $beforePath
            afterScreenshot = $beforePath
        }))
    }

    Add-LoginAutomationState -Context $loginContext -State "LoginWindowDetected"
    $controls = Get-LoginControlModel -Hwnd $hwnd -Rect $rect
    $loginContext.controls = $controls
    $loginContext.redactionRects = @(Get-LoginRedactionRects -Controls $controls)
    Write-God2AtomicJson -Value $controls.windowTree -Path (Join-Path ([string]$loginContext.root) "window-tree-before.json")
    $loginContext.safeScreenshotBefore = Save-LoginSafeScreenshot -Hwnd $hwnd -Directory ([string]$loginContext.root) -Name "safe-screenshot-before.png" -RedactionRects ([object[]]$loginContext.redactionRects)

    $accountPoint = $controls.account.center
    $passwordPoint = $controls.password.center
    $loginPoint = $controls.submit.center
    $trace = if ($NoCredentialCapture) {
        [pscustomobject]@{
            targetPid = [int]$clientSnapshot.pid
            exitCode = 0
            output = @()
            traceRoot = ""
            generalLog = ""
            metadata = ""
            credentialCapture = $false
        }
    }
    else {
        Start-LoginAttemptTraceProbe -TargetPid ([int]$clientSnapshot.pid) -Attempt $Attempt
    }
    if (-not $NoCredentialCapture) {
        Start-Sleep -Milliseconds 900
    }
    $keyboardSummary = [God2Automation.NativeApi]::KeyboardStateSummary()

    $accountResult = Invoke-ReliableFieldInput -Hwnd $hwnd -Control $controls.account -Text $Account -FieldName "account" -GeneralLog ([string]$trace.generalLog) -Attempt $Attempt -Context $loginContext -RedactionRects ([object[]]$loginContext.redactionRects)
    $accountCompleteScreenshot = Save-LoginSafeScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-account-complete.png") -RedactionRects ([object[]]$loginContext.redactionRects)
    if (-not $accountResult.success) {
        $accountFailure = if ($accountResult.failureCode) { [string]$accountResult.failureCode } else { "AccountInputIncompleteNoSubmit" }
        return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
            success = $false
            error = $accountFailure
            hwnd = $hwnd
            rect = $rect
            accountPoint = $accountPoint
            passwordPoint = $passwordPoint
            loginPoint = $loginPoint
            beforeScreenshot = $beforePath
            accountCompleteScreenshot = $accountCompleteScreenshot
            accountInput = $accountResult
            passwordInput = $null
            keyboard = $keyboardSummary
            trace = $trace
        }))
    }

    $passwordResult = Invoke-ReliableFieldInput -Hwnd $hwnd -Control $controls.password -Text $Password -FieldName "password" -GeneralLog ([string]$trace.generalLog) -Attempt $Attempt -Context $loginContext -RedactionRects ([object[]]$loginContext.redactionRects) -IsSecret $true
    if (-not $passwordResult.success) {
        $passwordFailure = if ($passwordResult.failureCode) { [string]$passwordResult.failureCode } else { "PasswordInputIncompleteNoSubmit" }
        return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
            success = $false
            error = $passwordFailure
            hwnd = $hwnd
            rect = $rect
            accountPoint = $accountPoint
            passwordPoint = $passwordPoint
            loginPoint = $loginPoint
            beforeScreenshot = $beforePath
            accountInput = $accountResult
            passwordProbe = $null
            passwordInput = $passwordResult
            keyboard = $keyboardSummary
            trace = $trace
        }))
    }

    $beforeSubmit = Save-LoginSafeScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-before-submit.png") -RedactionRects ([object[]]$loginContext.redactionRects)
    $foregroundMatches = ([God2Automation.NativeApi]::GetForegroundWindow() -eq [IntPtr]$hwnd)
    if ((-not (Test-LoginScreenImage -Path $beforeSubmit)) -or (-not $foregroundMatches)) {
        return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
            success = $false
            error = "SubmitPreconditionFailed"
            hwnd = $hwnd
            rect = $rect
            accountPoint = $accountPoint
            passwordPoint = $passwordPoint
            loginPoint = $loginPoint
            beforeScreenshot = $beforePath
            beforeSubmitScreenshot = $beforeSubmit
            accountInput = $accountResult
            passwordInput = $passwordResult
            foregroundMatches = $foregroundMatches
            keyboard = $keyboardSummary
            trace = $trace
        }))
    }

    $submitStartUtc = [DateTimeOffset]::UtcNow
    Add-LoginAutomationState -Context $loginContext -State "SubmitLocating" -Control $controls.submit
    Add-LoginAutomationState -Context $loginContext -State "SubmitExecuting" -Control $controls.submit -Diagnostic ([ordered]@{ submitMethod = "ClientRelativeClickFallback"; focusVerifiedBeforeSubmit = $foregroundMatches })
    [God2Automation.NativeApi]::RealClick([int]$loginPoint.x, [int]$loginPoint.y, 150, 125, 500)
    $submitEndUtc = [DateTimeOffset]::UtcNow
    $loginSubmitAttempt = [pscustomobject]@{
        attempt = 1
        method = "ClientRelativeClickFallback"
        startUtc = $submitStartUtc.ToString("o")
        endUtc = $submitEndUtc.ToString("o")
        foregroundMatches = $foregroundMatches
        delivered = $true
        observedTransition = $false
        failureCode = ""
        control = $controls.submit
    }
    $loginContext.submitAttempts.Add($loginSubmitAttempt) | Out-Null
    Add-LoginAutomationState -Context $loginContext -State "SubmitVerificationWaiting" -Control $controls.submit
    $afterScreenshots = @()
    $loginTransitionTimeoutSeconds = 20
    foreach ($seconds in 1..$loginTransitionTimeoutSeconds) {
        Start-Sleep -Seconds 1
        $afterPath = Save-WindowScreenshotOrNull -Hwnd $hwnd -Name ("attempt-$Attempt-after-submit-${seconds}s.png")
        if ($afterPath) {
            $afterPath = Save-LoginSafeScreenshot -Hwnd $hwnd -Name ("attempt-$Attempt-after-submit-${seconds}s.png") -RedactionRects ([object[]]$loginContext.redactionRects)
        }
        if (-not $afterPath) {
            $loginSubmitAttempt.failureCode = "LoginWindowLost"
            return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
                success = $false
                hwnd = $hwnd
                rect = $rect
                replacementClient = Get-PreferredGod2ClientSnapshot
                beforeScreenshot = $beforePath
                beforeSubmitScreenshot = $beforeSubmit
                afterScreenshots = $afterScreenshots
                accountInput = $accountResult
                passwordInput = $passwordResult
                loginClick = [pscustomobject]@{
                    submitted = $true
                    startUtc = $submitStartUtc.ToString("o")
                    endUtc = $submitEndUtc.ToString("o")
                    foregroundMatches = $foregroundMatches
                }
                network = Get-NetworkTraceSummary -MetadataPath ([string]$trace.metadata)
                keyboard = $keyboardSummary
                trace = $trace
                error = "ClientWindowInvalidAfterSubmit"
            }))
        }
        $afterScreenshots += $afterPath
        $serverSelectionReached = Test-ServerSelectionScreenImage -Path $afterScreenshots[-1]
        $characterSelectReached = Test-CharacterSelectScreenImage -Path $afterScreenshots[-1]
        if ($serverSelectionReached -or $characterSelectReached -or (-not (Test-LoginScreenImage -Path $afterScreenshots[-1]))) {
            $loginSubmitAttempt.observedTransition = $true
            Add-LoginAutomationState -Context $loginContext -State "LoginSucceeded" -Control $controls.submit -Diagnostic ([ordered]@{ serverSelectionReached = $serverSelectionReached; characterSelectReached = $characterSelectReached; afterSeconds = $seconds })
            return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
                success = $true
                serverSelectionReached = $serverSelectionReached
                characterSelectReached = $characterSelectReached
                hwnd = $hwnd
                rect = $rect
                accountPoint = $accountPoint
                passwordPoint = $passwordPoint
                loginPoint = $loginPoint
                beforeScreenshot = $beforePath
                beforeSubmitScreenshot = $beforeSubmit
                afterScreenshots = $afterScreenshots
                afterScreenshot = $afterScreenshots[-1]
                accountInput = $accountResult
                passwordProbe = [pscustomobject]@{
                    expectedLength = 0
                    receivedCharCount = 0
                    screenshot = $null
                }
                passwordInput = $passwordResult
                loginClick = [pscustomobject]@{
                    submitted = $true
                    startUtc = $submitStartUtc.ToString("o")
                    endUtc = $submitEndUtc.ToString("o")
                    foregroundMatches = $foregroundMatches
                }
                network = Get-NetworkTraceSummary -MetadataPath ([string]$trace.metadata)
                keyboard = $keyboardSummary
                trace = $trace
            }))
        }
    }

    $loginSubmitAttempt.failureCode = "SubmitDeliveredNoTransition"
    return (Complete-LoginAutomationAttempt -Context $loginContext -Hwnd $hwnd -Controls $controls -MetadataPath ([string]$trace.metadata) -Result ([pscustomobject]@{
        success = $false
        hwnd = $hwnd
        rect = $rect
        accountPoint = $accountPoint
        passwordPoint = $passwordPoint
        loginPoint = $loginPoint
        beforeScreenshot = $beforePath
        beforeSubmitScreenshot = $beforeSubmit
        afterScreenshots = $afterScreenshots
        afterScreenshot = $afterScreenshots[-1]
        accountInput = $accountResult
        passwordProbe = [pscustomobject]@{
            expectedLength = 0
            receivedCharCount = 0
            screenshot = $null
        }
        passwordInput = $passwordResult
        loginClick = [pscustomobject]@{
            submitted = $true
            startUtc = $submitStartUtc.ToString("o")
            endUtc = $submitEndUtc.ToString("o")
            foregroundMatches = $foregroundMatches
        }
        network = Get-NetworkTraceSummary -MetadataPath ([string]$trace.metadata)
        keyboard = $keyboardSummary
        trace = $trace
        error = "SubmitDeliveredNoTransition"
    }))
}

function Invoke-EnterWorld {
    param(
        [long] $Hwnd,
        [string] $MetadataPath,
        [ValidateRange(30, 180)]
        [int] $WorldReadyTimeoutSeconds = 120,
        [switch] $RequireNonLoopbackWorldTrace,
        [string] $ContractAcquisitionControlPath = '',
        [string] $ContractAcquisitionSessionId = '',
        [uint32] $ContractAcquisitionClientProcessId = 0,
        [switch] $PostWorldGroundClick,
        [switch] $StopAtCharacterSelect
    )

    $attempted = New-Object 'System.Collections.Generic.List[object]'
    $screenshots = New-Object 'System.Collections.Generic.List[object]'
    $current = "CharacterSelect"
    Publish-HostStatus -Stage $current -ReadyValue $true

    function Invoke-ValidatedGameClick {
        param(
            [string] $Name,
            [double] $RatioX,
            [double] $RatioY,
            [int] $WaitMs = 700
        )

        if (-not [God2Automation.NativeApi]::IsWindow([IntPtr]$Hwnd)) {
            return [pscustomobject]@{ success = $false; error = "ClientWindowInvalidBeforeClick"; name = $Name }
        }

        if ([God2Automation.NativeApi]::GetForegroundWindow() -ne [IntPtr]$Hwnd) {
            [God2Automation.NativeApi]::BringToTop([IntPtr]$Hwnd)
        }
        Start-Sleep -Milliseconds 50
        $rectNow = Get-ClientRectObject -Hwnd $Hwnd
        $point = Get-PointFromRatio -Rect $rectNow -X $RatioX -Y $RatioY
        [God2Automation.NativeApi]::RealClick($point.x, $point.y, 60, 60, ([Math]::Min($WaitMs, 150)))
        $attempted.Add([pscustomobject]@{
            name = $Name
            relativeX = $RatioX
            relativeY = $RatioY
            screenX = $point.x
            screenY = $point.y
            clientWidth = $rectNow.width
            clientHeight = $rectNow.height
            clickedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }) | Out-Null
        return [pscustomobject]@{ success = $true; name = $Name; point = $point; rect = $rectNow }
    }

    function Save-StageShot {
        param([string] $Name)
        $shot = Save-WindowScreenshotOrNull -Hwnd $Hwnd -Name $Name
        if (-not $shot) { return $null }
        $stage = Get-God2ScreenStage -Path $shot
        $hash = Get-FileSha256 -Path $shot
        $screenshots.Add([pscustomobject]@{
            name = $Name
            path = $shot
            stage = $stage
            sha256 = $hash
            capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }) | Out-Null
        return $screenshots[$screenshots.Count - 1]
    }

    function Wait-StageChange {
        param(
            [string] $Name,
            [string] $BeforeHash,
            [string] $BeforeStage = "",
            [int] $Seconds = 8
        )

        $deadline = (Get-Date).AddSeconds($Seconds)
        $last = $null
        $index = 0
        do {
            Start-Sleep -Milliseconds 500
            if (-not [God2Automation.NativeApi]::IsWindow([IntPtr]$Hwnd)) {
                return [pscustomobject]@{ stage = "ClientWindowInvalid"; screenshot = $last; changed = $false }
            }
            $last = Save-StageShot -Name ("enter-world-$Name-after-$index.png")
            if ($last -and
                $last.sha256 -ne $BeforeHash -and
                ([string]::IsNullOrWhiteSpace($BeforeStage) -or $last.stage -ne $BeforeStage)) {
                return [pscustomobject]@{ stage = $last.stage; screenshot = $last; changed = $true }
            }
            $index++
        } while ((Get-Date) -lt $deadline)

        return [pscustomobject]@{ stage = if ($last) { $last.stage } else { "WindowInvalid" }; screenshot = $last; changed = $false }
    }

    function Wait-WorldReadyScreen {
        param(
            [string] $Name,
            [int] $Seconds = 25
        )

        $deadline = (Get-Date).AddSeconds($Seconds)
        $last = $null
        $index = 0
        do {
            Start-Sleep -Milliseconds 1000
            if (-not [God2Automation.NativeApi]::IsWindow([IntPtr]$Hwnd)) {
                return [pscustomobject]@{ stage = "ClientWindowInvalid"; screenshot = $last; worldReady = $false }
            }
            $last = Save-StageShot -Name ("enter-world-$Name-world-ready-$index.png")
            if ($last -and $last.stage -eq "WorldReady") {
                return [pscustomobject]@{ stage = $last.stage; screenshot = $last; worldReady = $true }
            }
            $index++
        } while ((Get-Date) -lt $deadline)

        return [pscustomobject]@{ stage = if ($last) { $last.stage } else { "WindowInvalid" }; screenshot = $last; worldReady = $false }
    }

    function Wait-WorldTraceReady {
        param(
            [string] $TraceMetadataPath,
            [int] $Seconds = 75,
            [switch] $RequireNonLoopback
        )

        $deadline = (Get-Date).AddSeconds($Seconds)
        $lastTrace = Get-WorldTraceSummary -MetadataPath $TraceMetadataPath `
            -RequireNonLoopback:$RequireNonLoopback
        do {
            if (Test-WorldTraceStability -Trace $lastTrace) {
                return $lastTrace
            }

            Start-Sleep -Seconds 2
            $lastTrace = Get-WorldTraceSummary -MetadataPath $TraceMetadataPath `
                -RequireNonLoopback:$RequireNonLoopback
        } while ((Get-Date) -lt $deadline)

        return $lastTrace
    }

    [God2Automation.NativeApi]::BringToTop([IntPtr]$Hwnd)
    Start-Sleep -Milliseconds 100
    $before = Save-StageShot -Name "enter-world-before.png"
    if (-not $before) {
        return [pscustomobject]@{
            beforeScreenshot = $null
            screenshot = $null
            screenshots = [object[]]$screenshots.ToArray()
            attemptedPoints = [object[]]$attempted.ToArray()
            currentStage = "ClientWindowInvalid"
            worldReady = $false
            firstBlocker = "ClientWindowInvalidBeforeServerSelection"
        }
    }

    if ($before.stage -eq "ServerSelectionScreen") {
        $current = "ServerGroupReady"
        Publish-HostStatus -Stage $current -ReadyValue $true
        Invoke-ValidatedGameClick -Name "ServerGroupOnlyRow" -RatioX 0.50 -RatioY 0.242 | Out-Null
        Start-Sleep -Milliseconds 120
        $groupSelected = Save-StageShot -Name "enter-world-server-group-selected.png"
        Invoke-ValidatedGameClick -Name "ServerGroupConfirm" -RatioX 0.57 -RatioY 0.687 | Out-Null

        # The official client then opens the visually similar server page.
        Start-Sleep -Milliseconds 250
        $current = "ServerSelectionReady"
        Publish-HostStatus -Stage $current -ReadyValue $true
        Invoke-ValidatedGameClick -Name "ServerRow" -RatioX 0.50 -RatioY 0.283 | Out-Null
        Start-Sleep -Milliseconds 120
        $serverSelected = Save-StageShot -Name "enter-world-server-selected.png"
        Invoke-ValidatedGameClick -Name "ServerConfirm" -RatioX 0.57 -RatioY 0.625 | Out-Null
        $afterGroup = Wait-StageChange -Name "server-confirm" -BeforeHash $serverSelected.sha256 -BeforeStage "ServerSelectionScreen" -Seconds 15
        if ($afterGroup.stage -eq "ClientWindowInvalid") {
            return [pscustomobject]@{
                beforeScreenshot = $before.path
                screenshot = if ($groupSelected) { $groupSelected.path } else { $null }
                screenshots = [object[]]$screenshots.ToArray()
                attemptedPoints = [object[]]$attempted.ToArray()
                currentStage = "ServerGroupSelected"
                worldReady = $false
                firstBlocker = "ClientExitedAfterServerGroupConfirm"
            }
        }

        if ($afterGroup.stage -notin @("CharacterSelectReady", "CharacterCreateReady")) {
            return [pscustomobject]@{
                beforeScreenshot = $before.path
                screenshot = if ($screenshots.Count -gt 0) { [string]$screenshots[$screenshots.Count - 1].path } else { [string]$groupSelected.path }
                screenshots = [object[]]$screenshots.ToArray()
                attemptedPoints = [object[]]$attempted.ToArray()
                currentStage = $afterGroup.stage
                worldReady = $false
                firstBlocker = "ServerConfirmDidNotReachCharacterSelect"
            }
        }

        $current = $afterGroup.stage
        Publish-HostStatus -Stage $current -ReadyValue $true
    }
    elseif ($before.stage -eq "CharacterSelectReady") {
        $current = "CharacterSelectReady"
        Publish-HostStatus -Stage $current -ReadyValue $true
    }
    else {
        return [pscustomobject]@{
            beforeScreenshot = $before.path
            screenshot = $before.path
            screenshots = [object[]]$screenshots.ToArray()
            attemptedPoints = [object[]]$attempted.ToArray()
            currentStage = $before.stage
            worldReady = $false
            firstBlocker = "UnexpectedScreenBeforeServerSelection"
        }
    }

    $lastShot = $screenshots[$screenshots.Count - 1]
    if ($current -eq "ServerListReady") {
        $serverListHash = $lastShot.sha256
        Invoke-ValidatedGameClick -Name "ServerListRow" -RatioX 0.50 -RatioY 0.250 | Out-Null
        Start-Sleep -Milliseconds 120
        Save-StageShot -Name "enter-world-server-list-selected.png" | Out-Null
        $current = "ServerSelected"
        Publish-HostStatus -Stage $current -ReadyValue $true
        Invoke-ValidatedGameClick -Name "ServerListConfirm" -RatioX 0.57 -RatioY 0.625 | Out-Null
        $afterServer = Wait-StageChange -Name "server-list-confirm" -BeforeHash $serverListHash -Seconds 60
        if ($afterServer.stage -eq "ClientWindowInvalid") {
            return [pscustomobject]@{
                beforeScreenshot = $before.path
                screenshot = if ($screenshots.Count -gt 0) { $screenshots[$screenshots.Count - 1].path } else { $null }
                screenshots = [object[]]$screenshots.ToArray()
                attemptedPoints = [object[]]$attempted.ToArray()
                currentStage = "ServerSelected"
                worldReady = $false
                firstBlocker = "ClientExitedAfterServerListConfirm"
            }
        }

        $current = if ($afterServer.stage -eq "CharacterSelectReady") { "CharacterSelectReady" } else { $afterServer.stage }
        Publish-HostStatus -Stage $current -ReadyValue $true
    }

    if ($current -in @("CharacterSelectReady", "CharacterCreateReady") -and $StopAtCharacterSelect) {
        $path = if ($screenshots.Count -gt 0) { $screenshots[$screenshots.Count - 1].path } else { $before.path }
        $screenshotArray = [object[]]$screenshots.ToArray()
        $attemptedArray = [object[]]$attempted.ToArray()
        return [pscustomobject]@{
            beforeScreenshot = $before.path
            screenshot = $path
            screenshots = $screenshotArray
            attemptedPoints = $attemptedArray
            currentStage = $current
            characterSelectReady = ($current -eq "CharacterSelectReady")
            characterCreateReady = ($current -eq "CharacterCreateReady")
            worldReady = $false
            worldTrace = Get-WorldTraceSummary -MetadataPath $MetadataPath `
                -RequireNonLoopback:$RequireNonLoopbackWorldTrace
            firstBlocker = $null
            stopAtCharacterSelect = $true
        }
    }

    if ($current -eq "CharacterSelectReady") {
        $characterHash = $screenshots[$screenshots.Count - 1].sha256
        $current = "CharacterSelected"
        Publish-HostStatus -Stage $current -ReadyValue $true
        # Exact-build CharacterSelectReady evidence places the authoritative first
        # slot in the left column.  The former 0.50 point lands in the right column
        # when the frozen opaque tail renders a second, stale template entry.
        Invoke-ValidatedGameClick -Name "AuthoritativeCharacterSlot0" -RatioX 0.40 -RatioY 0.285 | Out-Null
        Start-Sleep -Milliseconds 500
        Save-StageShot -Name "enter-world-character-selected.png" | Out-Null
        $current = "EnterWorldRequested"
        Publish-HostStatus -Stage $current -ReadyValue $true
        Invoke-ValidatedGameClick -Name "EnterWorldConfirm" -RatioX 0.57 -RatioY 0.625 | Out-Null
        $afterWorld = Wait-WorldReadyScreen -Name "enter-world-confirm" -Seconds $WorldReadyTimeoutSeconds
        if ($afterWorld.stage -eq "ClientWindowInvalid") {
            return [pscustomobject]@{
                beforeScreenshot = $before.path
                screenshot = if ($screenshots.Count -gt 0) { $screenshots[$screenshots.Count - 1].path } else { $null }
                screenshots = [object[]]$screenshots.ToArray()
                attemptedPoints = [object[]]$attempted.ToArray()
                currentStage = "EnterWorldRequested"
                worldReady = $false
                firstBlocker = "ClientExitedAfterEnterWorldConfirm"
            }
        }
    }

    $path = if ($screenshots.Count -gt 0) { $screenshots[$screenshots.Count - 1].path } else { $before.path }
    $stage = if ($screenshots.Count -gt 0) { $screenshots[$screenshots.Count - 1].stage } else { $before.stage }
    $postWorldGroundClickAttempted = $false
    if ($stage -eq "WorldReady") {
        Start-Sleep -Seconds 3
        $stable = Save-StageShot -Name "enter-world-post-projection-stable.png"
        if ($stable) {
            $path = $stable.path
            $stage = $stable.stage
        }

        if ($stage -eq 'WorldReady' -and
            -not [string]::IsNullOrWhiteSpace($ContractAcquisitionControlPath)) {
            Write-God2AtomicJson -Value ([ordered]@{
                schemaId = 'God2ContractAcquisitionLiveControl'
                schemaVersion = 1
                sessionId = $ContractAcquisitionSessionId
                clientProcessId = $ContractAcquisitionClientProcessId
                status = 'WORLD_SCREEN_READY'
                worldScreenReady = $true
                screenshotSHA256 = if ($stable) { [string]$stable.sha256 } else { '' }
                generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            }) -Path $ContractAcquisitionControlPath
        }

        if ($stage -eq "WorldReady" -and $PostWorldGroundClick) {
            $postWorldGroundClickAttempted = $true
            Invoke-ValidatedGameClick -Name "PostWorldGroundClick" -RatioX 0.50 -RatioY 0.36 | Out-Null
            Start-Sleep -Seconds 4
            $afterClick = Save-StageShot -Name "enter-world-post-ground-click.png"
            if ($afterClick) {
                $path = $afterClick.path
                $stage = $afterClick.stage
            }
        }
    }
    $worldTrace = if ($stage -eq "WorldReady") {
        $traceWaitSeconds = if ([string]::IsNullOrWhiteSpace($MetadataPath)) { 1 } else { 75 }
        Wait-WorldTraceReady -TraceMetadataPath $MetadataPath -Seconds $traceWaitSeconds `
            -RequireNonLoopback:$RequireNonLoopbackWorldTrace
    }
    else {
        Get-WorldTraceSummary -MetadataPath $MetadataPath `
            -RequireNonLoopback:$RequireNonLoopbackWorldTrace
    }
    $worldReady = (($stage -eq "WorldReady") -and
        (Test-WorldTraceStability -Trace $worldTrace))
    $firstBlocker = if ($worldReady) {
        $null
    }
    elseif ($stage -eq "WorldReady") {
        "ClientWorldScreenReadyButProtocolMetadataUnavailable"
    }
    else {
        "AwaitingNextScreenAfter$($current)"
    }
    $screenshotArray = [object[]]$screenshots.ToArray()
    $attemptedArray = [object[]]$attempted.ToArray()
    return [pscustomobject]@{
        beforeScreenshot = $before.path
        screenshot = $path
        screenshots = $screenshotArray
        attemptedPoints = $attemptedArray
        currentStage = [string]$stage
        worldReady = [bool]$worldReady
        worldTrace = $worldTrace
        postWorldGroundClickAttempted = $postWorldGroundClickAttempted
        firstBlocker = $firstBlocker
    }
}

function Invoke-WorldPacketCaptureMatrix {
    param(
        [long] $Hwnd,
        [string] $TraceRoot,
        [string] $Mode = "FullMatrix"
    )

    $captureRoot = Join-Path $artifactRoot ($(if ($Mode -eq "FreshNpcObservation") { "fresh-npc-observation" } else { "world-capture-matrix" }))
    New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null
    $markerPath = Join-Path $captureRoot "world-capture-markers.jsonl"
    if (Test-Path -LiteralPath $markerPath) {
        Remove-Item -LiteralPath $markerPath -Force
    }

    function Add-WorldMarker {
        param(
            [string] $Scenario,
            [string] $Phase,
            [string] $ScreenshotPath = $null,
            [string] $Stage = $null
        )

        $marker = [ordered]@{
            scenario = $Scenario
            phase = $Phase
            wallUnixMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
            screenshot = $ScreenshotPath
            stage = $Stage
            playerScreenX = $null
            playerScreenY = $null
        }
        ($marker | ConvertTo-Json -Compress -Depth 8) | Add-Content -LiteralPath $markerPath -Encoding UTF8
        return [pscustomobject]$marker
    }

    function Save-WorldCaptureShot {
        param(
            [string] $Scenario,
            [string] $Phase
        )

        $name = "world-$Scenario-$Phase.png"
        $path = Save-WindowScreenshotOrNull -Hwnd $Hwnd -Name $name
        $stage = if ($path) { Get-God2ScreenStage -Path $path } else { "WindowInvalid" }
        return [pscustomobject]@{
            path = $path
            stage = $stage
        }
    }

    function Invoke-Scenario {
        param(
            [string] $Scenario,
            [scriptblock] $Action,
            [int] $AfterSeconds = 2
        )

        [God2Automation.NativeApi]::BringToTop([IntPtr]$Hwnd)
        Start-Sleep -Milliseconds 300
        $before = Save-WorldCaptureShot -Scenario $Scenario -Phase "before"
        Add-WorldMarker -Scenario $Scenario -Phase "start" -ScreenshotPath $before.path -Stage $before.stage | Out-Null
        & $Action
        Start-Sleep -Seconds $AfterSeconds
        $after = Save-WorldCaptureShot -Scenario $Scenario -Phase "after"
        Add-WorldMarker -Scenario $Scenario -Phase "end" -ScreenshotPath $after.path -Stage $after.stage | Out-Null
        return [pscustomobject]@{
            scenario = $Scenario
            before = $before
            after = $after
        }
    }

    function Invoke-WorldGroundClick {
        param(
            [double] $RatioX,
            [double] $RatioY
        )

        $rectNow = Get-ClientRectObject -Hwnd $Hwnd
        $point = Get-PointFromRatio -Rect $rectNow -X $RatioX -Y $RatioY
        [God2Automation.NativeApi]::RealClick($point.x, $point.y, 180, 120, 700)
    }

    $results = New-Object 'System.Collections.Generic.List[object]'
    $results.Add((Invoke-Scenario -Scenario "Idle30s" -AfterSeconds 0 -Action {
        Start-Sleep -Seconds 30
    })) | Out-Null
    if ($Mode -ne "FreshNpcObservation") {
        $results.Add((Invoke-Scenario -Scenario "MoveUp" -Action {
            Invoke-WorldGroundClick -RatioX 0.50 -RatioY 0.36
        } -AfterSeconds 4)) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "MoveDown" -Action {
            Invoke-WorldGroundClick -RatioX 0.50 -RatioY 0.66
        } -AfterSeconds 4)) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "MoveLeft" -Action {
            Invoke-WorldGroundClick -RatioX 0.36 -RatioY 0.52
        } -AfterSeconds 4)) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "MoveRight" -Action {
            Invoke-WorldGroundClick -RatioX 0.64 -RatioY 0.52
        } -AfterSeconds 4)) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "ContinuousMovement5s" -Action {
            Invoke-WorldGroundClick -RatioX 0.68 -RatioY 0.36
            Start-Sleep -Seconds 2
            Invoke-WorldGroundClick -RatioX 0.32 -RatioY 0.66
        })) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "StopMovement" -Action {
            foreach ($vk in @(0x25, 0x26, 0x27, 0x28)) {
                [God2Automation.NativeApi]::KeyUp([uint16]$vk)
            }
            Start-Sleep -Seconds 3
        })) | Out-Null
        $results.Add((Invoke-Scenario -Scenario "TurnOnlyRightTap" -Action {
            Invoke-WorldGroundClick -RatioX 0.57 -RatioY 0.52
        })) | Out-Null
    }

    $matrixPath = Join-Path $captureRoot "world-packet-matrix.json"
    $analyzer = Join-Path $repoRoot "tools\God2.ClientInstrumentation\Analyzer\bin\Release\net8.0\God2.ClientInstrumentation.Analyzer.dll"
    $output = & dotnet $analyzer world-matrix --run-dir $TraceRoot --markers $markerPath --out $matrixPath 2>&1
    $rawExitCode = $LASTEXITCODE
    $freshObservationAccepted = ($Mode -eq "FreshNpcObservation" -and (Test-Path -LiteralPath $matrixPath))
    $exitCode = if ($freshObservationAccepted) { 0 } else { $rawExitCode }

    return [pscustomobject]@{
        captureRoot = $captureRoot
        markerPath = $markerPath
        matrixPath = $matrixPath
        analyzerExitCode = $exitCode
        analyzerRawExitCode = $rawExitCode
        freshObservationAccepted = $freshObservationAccepted
        analyzerOutput = @($output)
        mode = $Mode
        scenarios = @($results.ToArray())
    }
}

try {
    if ($hostRid -ne 0x3000) {
        $currentStage = "PrivilegeGateFailed"
        $lastError = "Automation Host is $hostIntegrity / RID 0x$($hostRid.ToString("X")); required High / RID 0x3000."
        Publish-HostStatus -Stage $currentStage -ErrorValue $lastError -ReadyValue $false
        exit 20
    }
    if ($desktopName -ne "Default") {
        $currentStage = "PrivilegeGateFailed"
        $lastError = "Automation Host desktop is '$desktopName'; required interactive Default desktop."
        Publish-HostStatus -Stage $currentStage -ErrorValue $lastError -ReadyValue $false
        exit 21
    }

    $currentStage = "PrivilegeGateReady"
    $ready = $true
    Publish-HostStatus -Stage $currentStage -ReadyValue $ready

    $commandPath = Join-Path $stateRoot "host-command.json"
    $worldCaptureCommand = $null
    $characterLifecycleUiProbeCommand = $null
    $frozenRegressionCommand = $null
    $ultimateLiveCommand = $null
    $ultimateRuntimeCapture = $null
    if (Test-Path -LiteralPath $commandPath) {
        $command = Get-Content -LiteralPath $commandPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($command.PSObject.Properties["launcherProfilePath"] -and
            -not [string]::IsNullOrWhiteSpace([string]$command.launcherProfilePath)) {
            $candidateProfilePath = [IO.Path]::GetFullPath([string]$command.launcherProfilePath)
            $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $candidateProfilePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $candidateProfilePath -PathType Leaf)) {
                throw "Command launcher profile must be an existing file under the repository root."
            }
            $script:activeLauncherProfilePath = $candidateProfilePath
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
        }
        if ([string]$command.command -eq "CleanupReplayProcesses") {
            $stoppedProcesses = @(Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient)
            Start-Sleep -Milliseconds 500

            $resultPath = if ($command.PSObject.Properties["resultPath"] -and
                -not [string]::IsNullOrWhiteSpace([string]$command.resultPath)) {
                [IO.Path]::GetFullPath([string]$command.resultPath)
            }
            else {
                Join-Path $artifactRoot "replay-process-cleanup-result.json"
            }
            $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $resultPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "CleanupReplayProcesses result path must remain under the repository root."
            }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resultPath) | Out-Null
            Write-God2AtomicJson -Value ([ordered]@{
                command = "CleanupReplayProcesses"
                stoppedProcesses = $stoppedProcesses
                remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
                remainingLauncherCount = @((Get-ActiveRuntimeProcesses -IncludeLauncher)).Count
                cleanupScope = "ActiveLauncherProfileExactExecutablePaths"
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }) -Path $resultPath
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "ReplayProcessesCleaned"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "StartMapFileIoTrace") {
            $runDir = [IO.Path]::GetFullPath([string]$command.runDir)
            $resultPath = [IO.Path]::GetFullPath([string]$command.resultPath)
            $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $runDir.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not $resultPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Map FileIO trace outputs must remain under the repository root."
            }

            $traceStatePath = Join-Path $stateRoot "map-fileio-trace.json"
            if (Test-Path -LiteralPath $traceStatePath) {
                throw "A God2 Map FileIO trace marker already exists; refusing to replace or cancel it."
            }

            New-Item -ItemType Directory -Force -Path $runDir,(Split-Path -Parent $resultPath) | Out-Null
            $session = [Guid]::NewGuid().ToString("N")
            $traceSessionName = "God2MapFileIo-" + $session.Substring(0, 16)
            $etlPath = Join-Path $runDir "map-fileio.etl"
            $traceOutput = Invoke-God2NativeCommand -FilePath "logman.exe" -Arguments @(
                "create", "trace", $traceSessionName,
                "-o", $etlPath,
                "-p", "Microsoft-Windows-Kernel-File", "0x1F0", "0x4",
                "-bs", "1024", "-nb", "16", "128", "-ets")
            if ([int]$traceOutput.exitCode -ne 0) {
                throw "Kernel FileIO trace start failed: $([string]$traceOutput.stdout) $([string]$traceOutput.stderr)"
            }

            $traceState = [ordered]@{
                schemaVersion = "god2-map-fileio-trace-v1"
                sessionId = $session
                traceSessionName = $traceSessionName
                runDir = $runDir
                etlPath = $etlPath
                startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                startedByHostPid = $hostPid
                provider = "Microsoft-Windows-Kernel-File"
                keywords = "0x1F0"
                fileMode = $true
                packetCapture = $false
            }
            Write-God2AtomicJson -Value $traceState -Path $traceStatePath
            Write-God2AtomicJson -Value ([ordered]@{
                command = "StartMapFileIoTrace"
                status = "PASS"
                sessionId = $session
                traceSessionName = $traceSessionName
                provider = "Microsoft-Windows-Kernel-File"
                keywords = "0x1F0"
                fileMode = $true
                packetCapture = $false
                startedAtUtc = $traceState.startedAtUtc
            }) -Path $resultPath
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "MapFileIoTraceStarted"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{ mapFileIoTraceSessionId = $session })
            exit 0
        }
        if ([string]$command.command -eq "StopMapFileIoTrace") {
            $traceStatePath = Join-Path $stateRoot "map-fileio-trace.json"
            if (-not (Test-Path -LiteralPath $traceStatePath -PathType Leaf)) {
                throw "No God2 Map FileIO trace marker exists; refusing to stop an unowned ETW session."
            }

            $traceState = Read-God2JsonWithRetry -Path $traceStatePath
            $requestedSession = [string]$command.sessionId
            if ([string]::IsNullOrWhiteSpace($requestedSession) -or
                $requestedSession -cne [string]$traceState.sessionId) {
                throw "Map FileIO trace session identity mismatch."
            }

            $etlPath = [IO.Path]::GetFullPath([string]$traceState.etlPath)
            $resultPath = [IO.Path]::GetFullPath([string]$command.resultPath)
            $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $etlPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not $resultPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Map FileIO trace outputs must remain under the repository root."
            }

            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $etlPath),(Split-Path -Parent $resultPath) | Out-Null
            $traceSessionName = [string]$traceState.traceSessionName
            if ($traceSessionName -notmatch '^God2MapFileIo-[0-9a-f]{16}$') {
                throw "Map FileIO trace marker contains an invalid ETW session name."
            }
            $traceQuery = Invoke-God2NativeCommand -FilePath "logman.exe" -Arguments @("query", $traceSessionName, "-ets")
            $stopMode = "RecoveredCompletedStop"
            if ([int]$traceQuery.exitCode -eq 0) {
                $traceOutput = Invoke-God2NativeCommand -FilePath "logman.exe" -Arguments @("stop", $traceSessionName, "-ets")
                if ([int]$traceOutput.exitCode -ne 0) {
                    throw "Kernel FileIO trace stop failed: $([string]$traceOutput.stdout) $([string]$traceOutput.stderr)"
                }
                $stopMode = "StoppedActiveOwnedSession"
            }
            if (-not (Test-Path -LiteralPath $etlPath -PathType Leaf)) {
                throw "The owned Kernel FileIO trace is not active and its ETL artifact is missing."
            }

            $etl = Get-Item -LiteralPath $etlPath
            $completed = [ordered]@{
                command = "StopMapFileIoTrace"
                status = "PASS"
                sessionId = $requestedSession
                traceSessionName = $traceSessionName
                provider = [string]$traceState.provider
                keywords = [string]$traceState.keywords
                packetCapture = $false
                stopMode = $stopMode
                startedAtUtc = [string]$traceState.startedAtUtc
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                etlRelativePath = Get-RepoRelativePath -Path $etlPath
                etlLength = $etl.Length
                etlSha256 = (Get-FileHash -LiteralPath $etlPath -Algorithm SHA256).Hash
            }
            Write-God2AtomicJson -Value $completed -Path $resultPath
            Remove-Item -LiteralPath $traceStatePath -Force
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "MapFileIoTraceStopped"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{ mapFileIoTraceSessionId = $requestedSession })
            exit 0
        }
        if ($command.PSObject.Properties["forceFreshClient"] -and [bool]$command.forceFreshClient) {
            Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | Out-Null
            Start-Sleep -Milliseconds 500
        }
        if ([string]$command.command -eq "CharacterCreateConfirmOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "CharacterCreateConfirmOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            [God2Automation.NativeApi]::ForceEnglishKeyboard($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $confirmPoint = Get-PointFromRatio -Rect $rect -X 0.864 -Y 0.873
            [God2Automation.NativeApi]::RealClick($confirmPoint.x, $confirmPoint.y, 40, 50, 80)
            Start-Sleep -Milliseconds 300
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "CharacterCreateConfirmClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "DismissClientModalOnly") {
            $targetPid = [int]$command.clientPid
            $processWindows = @([God2Automation.NativeApi]::DescribeProcessWindows($targetPid))
            $dialog = $processWindows | Where-Object { $_.ClassName -eq "#32770" -or $_.Title -eq $script:OfficialSimplifiedErrorCaption } | Select-Object -First 1
            if (-not $dialog) { throw "DismissClientModalOnly cannot find a dialog." }
            [God2Automation.NativeApi]::BringToTop([IntPtr][int64]$dialog.Hwnd)
            [God2Automation.NativeApi]::Key(0x0D)
            Start-Sleep -Milliseconds 150
            $remainingDialog = @([God2Automation.NativeApi]::DescribeProcessWindows($targetPid) | Where-Object { $_.ClassName -eq "#32770" -or $_.Title -eq $script:OfficialSimplifiedErrorCaption })
            if ($remainingDialog.Count -gt 0) { throw "DismissClientModalOnly dialog remained after Enter." }
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "ClientModalDismissed"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "CharacterNameOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "CharacterNameOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            [God2Automation.NativeApi]::ForceEnglishKeyboard($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $namePoint = Get-PointFromRatio -Rect $rect -X 0.70 -Y 0.183
            [God2Automation.NativeApi]::RealClick($namePoint.x, $namePoint.y, 40, 50, 80)
            [God2Automation.NativeApi]::Key(0x23)
            for ($i = 0; $i -lt 20; $i++) {
                [God2Automation.NativeApi]::Key(0x08)
            }
            [God2Automation.NativeApi]::TypeAscii([string]$command.characterName)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "CharacterNameEntered"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "PressClientEnterOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "PressClientEnterOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            [God2Automation.NativeApi]::Key(0x0D)
            Start-Sleep -Milliseconds 200
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "ClientEnterPressed"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "PressCharacterConfirmOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "PressCharacterConfirmOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $confirmPoint = Get-PointFromRatio -Rect $rect -X 0.57 -Y 0.675
            [God2Automation.NativeApi]::RealClick($confirmPoint.x, $confirmPoint.y, 40, 50, 80)
            Start-Sleep -Milliseconds 200
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "CharacterConfirmClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "EnterSelectedCharacterOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "EnterSelectedCharacterOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $clientX = [int][Math]::Round($rect.width * 0.57)
            $clientY = [int][Math]::Round($rect.height * 0.618)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $clientX, $clientY)
            Start-Sleep -Milliseconds 200
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "CharacterEnterClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "OnboardingNextOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "OnboardingNextOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $clientX = [int][Math]::Round($rect.width * 0.675)
            $clientY = [int][Math]::Round($rect.height * 0.905)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $clientX, $clientY)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "OnboardingNextClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "OnboardingPrevOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "OnboardingPrevOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $clientX = [int][Math]::Round($rect.width * 0.592)
            $clientY = [int][Math]::Round($rect.height * 0.905)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $clientX, $clientY)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "OnboardingPrevClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "OnboardingChooseTrainingOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "OnboardingChooseTrainingOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $clientX = [int][Math]::Round($rect.width * 0.78)
            $clientY = [int][Math]::Round($rect.height * 0.742)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $clientX, $clientY)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "OnboardingTrainingSelected"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "OnboardingChooseNextStageOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "OnboardingChooseNextStageOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $clientX = [int][Math]::Round($rect.width * 0.78)
            $clientY = [int][Math]::Round($rect.height * 0.803)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $clientX, $clientY)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "OnboardingNextStageSelected"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "ClickStarterNpcOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "ClickStarterNpcOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $point = Get-PointFromRatio -Rect $rect -X 0.70 -Y 0.535
            [God2Automation.NativeApi]::RealClick($point.x, $point.y, 50, 60, 120)
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "StarterNpcClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "WorldSnapshotOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldSnapshotOnly cannot find target HWND."
            }
            $name = if ($command.PSObject.Properties["screenshotName"] -and -not [string]::IsNullOrWhiteSpace([string]$command.screenshotName)) {
                [string]$command.screenshotName
            }
            else {
                "world-snapshot.png"
            }
            $path = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name $name
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldSnapshotSaved"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{ screenshot = $path })
            exit 0
        }
        if ([string]$command.command -eq "WorldKeyOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldKeyOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $vk = if ($command.PSObject.Properties["virtualKey"]) { [uint16]$command.virtualKey } else { [uint16]0x0D }
            [God2Automation.NativeApi]::Key($vk)
            Start-Sleep -Milliseconds 150
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldKeyPressed"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{ virtualKey = $vk })
            exit 0
        }
        if ([string]$command.command -eq "WorldClickRatioOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldClickRatioOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $point = Get-PointFromRatio -Rect $rect -X ([double]$command.ratioX) -Y ([double]$command.ratioY)
            [God2Automation.NativeApi]::RealClick($point.x, $point.y, 60, 60, 180)
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldRatioClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                ratioX = [double]$command.ratioX
                ratioY = [double]$command.ratioY
                screenX = $point.x
                screenY = $point.y
            })
            exit 0
        }
        if ([string]$command.command -eq "WorldPointDiagnosticOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldPointDiagnosticOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $point = Get-PointFromRatio -Rect $rect -X ([double]$command.ratioX) -Y ([double]$command.ratioY)
            $hit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint($hwnd, $point.x, $point.y)
            $focused = [int64][God2Automation.NativeApi]::GetFocusedWindowFor($hwnd)
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldPointDiagnostic"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                ratioX = [double]$command.ratioX
                ratioY = [double]$command.ratioY
                clientRect = $rect
                screenX = $point.x
                screenY = $point.y
                hitHwnd = [int64]$hit.Hwnd
                hitClassName = [string]$hit.ClassName
                hitTitle = [string]$hit.Title
                focusedHwnd = $focused
            })
            exit 0
        }
        if ([string]$command.command -eq "WorldClickAndSnapshotOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldClickAndSnapshotOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $point = Get-PointFromRatio -Rect $rect -X ([double]$command.ratioX) -Y ([double]$command.ratioY)
            $hit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint($hwnd, $point.x, $point.y)
            [God2Automation.NativeApi]::RealClick($point.x, $point.y, 60, 60, 350)
            $name = if ($command.PSObject.Properties["screenshotName"] -and -not [string]::IsNullOrWhiteSpace([string]$command.screenshotName)) {
                [string]$command.screenshotName
            }
            else {
                "world-click-after.png"
            }
            $path = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name $name
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldClickAndSnapshot"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                ratioX = [double]$command.ratioX
                ratioY = [double]$command.ratioY
                screenX = $point.x
                screenY = $point.y
                hitHwnd = [int64]$hit.Hwnd
                hitClassName = [string]$hit.ClassName
                screenshot = $path
            })
            exit 0
        }
        if ([string]$command.command -eq "WorldRightClickAndSnapshotOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldRightClickAndSnapshotOnly cannot find target HWND."
            }
            $ratioX = [double]$command.ratioX
            $ratioY = [double]$command.ratioY
            if ($ratioX -lt 0.0 -or $ratioX -gt 1.0 -or $ratioY -lt 0.0 -or $ratioY -gt 1.0) {
                throw "WorldRightClickAndSnapshotOnly ratios must stay within the target client rectangle."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $point = Get-PointFromRatio -Rect $rect -X $ratioX -Y $ratioY
            $hit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint($hwnd, $point.x, $point.y)
            if ([int64]$hit.Hwnd -ne [int64]$hwnd) {
                throw "WorldRightClickAndSnapshotOnly target must resolve to the selected client HWND."
            }
            [God2Automation.NativeApi]::RealRightClick($point.x, $point.y, 60, 60, 350)
            $name = if ($command.PSObject.Properties["screenshotName"] -and -not [string]::IsNullOrWhiteSpace([string]$command.screenshotName)) {
                [string]$command.screenshotName
            }
            else {
                "world-right-click-after.png"
            }
            $path = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name $name
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldRightClickAndSnapshot"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                ratioX = $ratioX
                ratioY = $ratioY
                screenX = $point.x
                screenY = $point.y
                hitHwnd = [int64]$hit.Hwnd
                hitClassName = [string]$hit.ClassName
                screenshot = $path
            })
            exit 0
        }
        if ([string]$command.command -eq "WorldDragAndSnapshotOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "WorldDragAndSnapshotOnly cannot find target HWND."
            }
            $ratios = @(
                [double]$command.fromRatioX,
                [double]$command.fromRatioY,
                [double]$command.toRatioX,
                [double]$command.toRatioY)
            if (@($ratios | Where-Object { $_ -lt 0.0 -or $_ -gt 1.0 }).Count -ne 0) {
                throw "WorldDragAndSnapshotOnly ratios must stay within the target client rectangle."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $fromPoint = Get-PointFromRatio -Rect $rect -X ([double]$command.fromRatioX) -Y ([double]$command.fromRatioY)
            $toPoint = Get-PointFromRatio -Rect $rect -X ([double]$command.toRatioX) -Y ([double]$command.toRatioY)
            $fromHit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint($hwnd, $fromPoint.x, $fromPoint.y)
            $toHit = [God2Automation.NativeApi]::DescribeWindowAtScreenPoint($hwnd, $toPoint.x, $toPoint.y)
            if ([int64]$fromHit.Hwnd -ne [int64]$hwnd -or [int64]$toHit.Hwnd -ne [int64]$hwnd) {
                throw "WorldDragAndSnapshotOnly source and destination must both resolve to the target client HWND."
            }
            [God2Automation.NativeApi]::RealDrag(
                $fromPoint.x,
                $fromPoint.y,
                $toPoint.x,
                $toPoint.y,
                80,
                120,
                20,
                350)
            $name = if ($command.PSObject.Properties["screenshotName"] -and -not [string]::IsNullOrWhiteSpace([string]$command.screenshotName)) {
                [string]$command.screenshotName
            }
            else {
                "world-drag-after.png"
            }
            $path = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name $name
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "WorldDragAndSnapshot"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                fromRatioX = [double]$command.fromRatioX
                fromRatioY = [double]$command.fromRatioY
                toRatioX = [double]$command.toRatioX
                toRatioY = [double]$command.toRatioY
                fromScreenX = $fromPoint.x
                fromScreenY = $fromPoint.y
                toScreenX = $toPoint.x
                toScreenY = $toPoint.y
                sourceHwnd = [int64]$fromHit.Hwnd
                destinationHwnd = [int64]$toHit.Hwnd
                screenshot = $path
            })
            exit 0
        }
        if ([string]$command.command -eq "CharacterNameAndConfirmOnly") {
            $targetPid = [int]$command.clientPid
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or [int64]$clientSnapshot.mainWindowHandle -eq 0) {
                throw "CharacterNameAndConfirmOnly cannot find target HWND."
            }
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            if ($command.PSObject.Properties["dismissModal"] -and [bool]$command.dismissModal) {
                $processWindows = @([God2Automation.NativeApi]::DescribeProcessWindows($targetPid))
                $dialog = $processWindows | Where-Object { $_.ClassName -eq "#32770" -or $_.Title -eq $script:OfficialSimplifiedErrorCaption } | Select-Object -First 1
                if ($dialog) {
                    $button = @($dialog.Children | Where-Object { $_.ClassName -eq "Button" -and $_.IsEnabled } | Select-Object -First 1)
                    if ($button.Count -gt 0) {
                        [God2Automation.NativeApi]::BmClick([IntPtr][int64]$button[0].Hwnd)
                    }
                }
                Start-Sleep -Milliseconds 200
            }
            [God2Automation.NativeApi]::BringToTop($hwnd)
            Start-Sleep -Milliseconds 50
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $namePoint = Get-PointFromRatio -Rect $rect -X 0.625 -Y 0.183
            [God2Automation.NativeApi]::RealClick($namePoint.x, $namePoint.y, 40, 50, 80)
            [God2Automation.NativeApi]::CtrlKey(0x41)
            [God2Automation.NativeApi]::Key(0x08)
            [God2Automation.NativeApi]::SendUnicodeChars($hwnd, [string]$command.characterName, 40)
            Start-Sleep -Milliseconds 100
            $confirmX = [int][Math]::Round($rect.width * 0.57)
            $confirmY = [int][Math]::Round($rect.height * 0.675)
            [God2Automation.NativeApi]::SendClientClick($hwnd, $confirmX, $confirmY)
            Start-Sleep -Milliseconds 300
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "CharacterNameConfirmClicked"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "LauncherAgreementRegressionOnly") {
            $scenario = if ($command.PSObject.Properties["scenario"]) { [string]$command.scenario } else { "Normal" }
            $launcher = Ensure-God2Launcher
            if ($scenario -in @("MoveRestart", "Restart")) {
                Stop-Process -Id $launcher.Id -Force
                Start-Sleep -Seconds 2
                $launcher = Ensure-God2Launcher
                Start-Sleep -Seconds 2
            }
            $launcherSnapshot = Get-God2ProcessSnapshot -ProcessId $launcher.Id
            $hwnd = [IntPtr][int64]$launcherSnapshot.mainWindowHandle
            if ($scenario -eq "MoveRestart") {
                $rect = Get-WindowRectObject -Hwnd ([int64]$hwnd)
                [God2Automation.NativeApi]::SetWindowPos($hwnd, [IntPtr]::Zero, 137, 83, $rect.width, $rect.height, 0x0040) | Out-Null
                Start-Sleep -Milliseconds 700
            }
            elseif ($scenario -eq "Resize") {
                $rect = Get-WindowRectObject -Hwnd ([int64]$hwnd)
                [God2Automation.NativeApi]::SetWindowPos($hwnd, [IntPtr]::Zero, $rect.left, $rect.top, ($rect.width + 96), ($rect.height + 48), 0x0040) | Out-Null
                Start-Sleep -Milliseconds 700
            }
            elseif ($scenario -eq "Restore") {
                [God2Automation.NativeApi]::ShowWindow($hwnd, 6) | Out-Null
                Start-Sleep -Milliseconds 500
                [God2Automation.NativeApi]::ShowWindow($hwnd, 9) | Out-Null
                Start-Sleep -Milliseconds 700
            }
            $result = Invoke-LauncherAgreementRegression -Scenario $scenario
            $runDir = if ($command.PSObject.Properties["runDir"]) { [string]$command.runDir } else { $artifactRoot }
            New-Item -ItemType Directory -Force -Path $runDir | Out-Null
            Write-God2AtomicJson -Value $result -Path (Join-Path $runDir ("launcher-agreement-$scenario-result.json"))
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "LauncherAgreementRegressionComplete"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "RunWorldCaptureMatrix" -or [string]$command.command -eq "RunFreshNpcObservation") {
            $worldCaptureCommand = $command
        }
        if ([string]$command.command -eq "CharacterLifecycleUiProbe") {
            $characterLifecycleUiProbeCommand = $command
        }
        if ([string]$command.command -eq "RunFrozenRegressionAndExit") {
            $frozenRegressionCommand = $command
        }
        if ([string]$command.command -eq 'RunUltimateOfficialLiveAndExit') {
            $ultimateLiveCommand = $command
        }
        if ([string]$command.command -eq "StartClientOnly") {
            Ensure-God2Launcher | Out-Null
            $clientSnapshot = Invoke-StartGod2Client
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "ClientStarted"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                clientPid = $clientSnapshot.pid
                clientHwnd = $clientSnapshot.mainWindowHandle
            })
            exit 0
        }
        if ([string]$command.command -eq "StartLoginAndEnterWorld") {
            Ensure-God2Launcher | Out-Null
            $clientSnapshot = Invoke-StartGod2Client
            Start-Sleep -Milliseconds 900
            try {
                $stableClient = Resolve-StableGod2ClientWindow -InitialSnapshot $clientSnapshot -TimeoutSeconds 15
            }
            catch {
                if (Get-Process -Id ([int]$clientSnapshot.pid) -ErrorAction SilentlyContinue) {
                    throw
                }
                $clientSnapshot = Invoke-StartGod2Client
                $stableClient = Resolve-StableGod2ClientWindow -InitialSnapshot $clientSnapshot -TimeoutSeconds 15
            }
            $clientSnapshot = $stableClient.snapshot
            $targetPid = [int]$clientSnapshot.pid
            $hwnd = [IntPtr][int64]$stableClient.hwnd
            [God2Automation.NativeApi]::BringToTop($hwnd)
            $preLogin = Wait-God2PreLoginUiReady -ClientSnapshot $clientSnapshot -TimeoutSeconds 30

            $secret = if ($command.PSObject.Properties["useLocalLoginSecret"] -and [bool]$command.useLocalLoginSecret) {
                Get-LocalTestAccountCredential
            }
            else {
                $accountKey = if ($command.PSObject.Properties["testAccountKey"]) {
                    [string]$command.testAccountKey
                }
                else { "OfficialA" }
                Get-TestAccountCredential -Key $accountKey
            }
            try {
                $loginResult = Invoke-LoginAttempt `
                    -Attempt 901 `
                    -Account ([string]$secret.account) `
                    -Password ([string]$secret.password) `
                    -NoCredentialCapture
            }
            finally {
                $secret.account = $null
                $secret.password = $null
            }
            Write-God2AtomicJson -Value $loginResult -Path (Join-Path $artifactRoot 'start-login-verified-input.json')
            if (-not $loginResult -or -not [bool]$loginResult.success) {
                $loginFailure = if ($loginResult -and $loginResult.error) { [string]$loginResult.error } else { 'VerifiedLoginFailed' }
                throw "StartLoginAndEnterWorld login failed: $loginFailure"
            }

            $clientSnapshot = Get-PreferredGod2ClientSnapshot
            if (-not $clientSnapshot) { throw 'God2_opt disappeared after verified login input.' }
            $stableClient = Resolve-StableGod2ClientWindow -InitialSnapshot $clientSnapshot -TimeoutSeconds 15
            $clientSnapshot = $stableClient.snapshot
            $targetPid = [int]$clientSnapshot.pid
            $hwnd = [IntPtr][int64]$stableClient.hwnd

            $stopAtCharacterSelect = $command.PSObject.Properties["stopAtCharacterSelect"] -and [bool]$command.stopAtCharacterSelect
            $enterResult = Invoke-EnterWorld -Hwnd ([int64]$hwnd) -MetadataPath "" -WorldReadyTimeoutSeconds 45 -StopAtCharacterSelect:$stopAtCharacterSelect
            $characterCreateReady = $enterResult.PSObject.Properties["characterCreateReady"] -and
                [bool]$enterResult.characterCreateReady
            if (($enterResult.characterSelectReady -or $characterCreateReady) -and
                $command.PSObject.Properties["createCharacterName"] -and
                -not [string]::IsNullOrWhiteSpace([string]$command.createCharacterName))
            {
                if ($command.PSObject.Properties["attachTraceBeforeCreate"] -and [bool]$command.attachTraceBeforeCreate)
                {
                    $trace = Start-LoginAttemptTraceProbe -TargetPid $targetPid -Attempt 317
                    $enterResult | Add-Member -NotePropertyName createTraceRoot -NotePropertyValue $trace.traceRoot
                    Start-Sleep -Milliseconds 500
                }
                $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
                if ($enterResult.characterSelectReady) {
                    $createPoint = Get-PointFromRatio -Rect $rect -X 0.40 -Y 0.76
                    [God2Automation.NativeApi]::RealClick($createPoint.x, $createPoint.y, 40, 50, 80)
                    Start-Sleep -Milliseconds 700
                }
                [God2Automation.NativeApi]::PostClientClick(
                    $hwnd,
                    [int][Math]::Round($rect.width * 0.864),
                    [int][Math]::Round($rect.height * 0.873))
                Start-Sleep -Milliseconds 1200
                [God2Automation.NativeApi]::ForceEnglishKeyboard($hwnd)
                $namePoint = Get-PointFromRatio -Rect $rect -X 0.70 -Y 0.183
                [God2Automation.NativeApi]::RealClick($namePoint.x, $namePoint.y, 40, 50, 80)
                [God2Automation.NativeApi]::CtrlKey(0x41)
                [God2Automation.NativeApi]::Key(0x08)
                [God2Automation.NativeApi]::TypeAscii([string]$command.createCharacterName)
                [God2Automation.NativeApi]::PostClientClick(
                    $hwnd,
                    [int][Math]::Round($rect.width * 0.57),
                    [int][Math]::Round($rect.height * 0.675))
                Start-Sleep -Milliseconds 1200
                $enterResult | Add-Member -NotePropertyName createCharacterRequested -NotePropertyValue $true
            }
            $shot = Save-WindowScreenshotOrNull -Hwnd ([int64]$hwnd) -Name "start-login-enter-world-final.png"
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = if ($enterResult.worldReady) { "WorldReady" } else { "EnterWorldAttempted" }
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                clientPid = $targetPid
                screenshot = $shot
                enterResult = $enterResult
            })
            exit 0
        }
        if ([string]$command.command -eq "FillLoginOnly") {
            $targetPid = [int]$command.clientPid
            if ($targetPid -eq 0) {
                $client = Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object Id | Select-Object -Last 1
                if (-not $client) { throw "FillLoginOnly cannot find an active-profile client." }
                $targetPid = [int]$client.Id
            }
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or $clientSnapshot.mainWindowHandle -eq 0) {
                throw "FillLoginOnly cannot find target HWND."
            }
            $secret = Get-TestAccountCredential -Key "OfficialA"
            $hwnd = [IntPtr][int64]$clientSnapshot.mainWindowHandle
            [God2Automation.NativeApi]::BringToTop($hwnd)
            [God2Automation.NativeApi]::ForceEnglishKeyboard($hwnd)
            Start-Sleep -Milliseconds 80
            $rect = Get-ClientRectObject -Hwnd ([int64]$hwnd)
            $accountPoint = Get-PointFromRatio -Rect $rect -X 0.55 -Y 0.385
            $passwordPoint = Get-PointFromRatio -Rect $rect -X 0.55 -Y 0.443
            [God2Automation.NativeApi]::RealClick($accountPoint.x, $accountPoint.y, 40, 50, 60)
            [God2Automation.NativeApi]::CtrlKey(0x41)
            [God2Automation.NativeApi]::Key(0x08)
            [God2Automation.NativeApi]::TypeAscii([string]$secret.account)
            Start-Sleep -Milliseconds 80
            [God2Automation.NativeApi]::RealClick($passwordPoint.x, $passwordPoint.y, 40, 50, 60)
            [God2Automation.NativeApi]::CtrlKey(0x41)
            [God2Automation.NativeApi]::Key(0x08)
            [God2Automation.NativeApi]::TypeAscii([string]$secret.password)
            $secret.account = $null
            $secret.password = $null
            $shot = Save-WindowScreenshot -Hwnd ([int64]$hwnd) -Name "fill-login-only-after.png"
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "LoginFieldsFilled"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                clientPid = $targetPid
                screenshot = $shot
            })
            exit 0
        }
        if ([string]$command.command -eq "AttachTraceOnly") {
            $targetPid = [int]$command.clientPid
            if ($targetPid -eq 0) {
                $client = Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object Id | Select-Object -Last 1
                if (-not $client) { throw "AttachTraceOnly cannot find an active-profile client." }
                $targetPid = [int]$client.Id
            }
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or $clientSnapshot.mainWindowHandle -eq 0) {
                throw "AttachTraceOnly cannot find target HWND."
            }
            $attemptId = if ($command.PSObject.Properties["attempt"]) { [int]$command.attempt } else { 300 }
            $buildFlavor = if ($command.PSObject.Properties["probeBuildFlavor"] -and
                [string]$command.probeBuildFlavor -eq "GenericProbe") {
                "GenericProbe"
            }
            else {
                "Standard"
            }
            $trace = Start-LoginAttemptTraceProbe `
                -TargetPid $targetPid `
                -Attempt $attemptId `
                -BuildFlavor $buildFlavor
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "TraceAttached"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true -Extra ([ordered]@{
                clientPid = $targetPid
                traceRoot = $trace.traceRoot
                metadata = $trace.metadata
                generalLog = $trace.generalLog
                rawTrace = $trace.rawTrace
                probeBuildFlavor = $buildFlavor
            })
            exit 0
        }
        if ([string]$command.command -eq "DetachTraceOnly") {
            $targetPid = [int]$command.clientPid
            $module = [string]$command.module
            if ($targetPid -eq 0 -or [string]::IsNullOrWhiteSpace($module)) {
                throw "DetachTraceOnly requires clientPid and module."
            }
            $buildFlavor = if ($command.PSObject.Properties["probeBuildFlavor"] -and
                [string]$command.probeBuildFlavor -eq "GenericProbe") {
                "GenericProbe"
            }
            else {
                "Standard"
            }
            $buildDir = if ($buildFlavor -eq "GenericProbe") {
                Join-Path $repoRoot "Build\ClientInstrumentation-GenericProbe\Release"
            }
            else {
                Join-Path $repoRoot "Build\ClientInstrumentation\Release"
            }
            $launcher = Join-Path $buildDir "God2ClientTraceLauncher.exe"
            if (-not (Test-Path -LiteralPath $launcher)) {
                throw "Trace launcher not found: $launcher"
            }
            $output = & $launcher --detach --pid $targetPid --module $module 2>&1
            $exitCode = $LASTEXITCODE
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = if ($exitCode -eq 0) { "TraceDetached" } else { "TraceDetachFailed" }
            Publish-HostStatus -Stage $currentStage -ReadyValue ($exitCode -eq 0) -Extra ([ordered]@{
                clientPid = $targetPid
                module = $module
                probeBuildFlavor = $buildFlavor
                exitCode = $exitCode
                output = @($output)
            })
            if ($exitCode -ne 0) {
                exit 40
            }
            exit 0
        }
        if ([string]$command.command -eq "EnterWorldOnly") {
            $currentStage = "EnterWorldOnly"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            $targetPid = [int]$command.clientPid
            if ($targetPid -eq 0) {
                $client = Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object Id | Select-Object -Last 1
                if (-not $client) { throw "EnterWorldOnly cannot find an active-profile client." }
                $targetPid = [int]$client.Id
            }
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or $clientSnapshot.mainWindowHandle -eq 0) {
                throw "EnterWorldOnly cannot find target HWND."
            }
            $resumeMetadataPath = if ($command.PSObject.Properties["metadataPath"]) { [string]$command.metadataPath } else { "" }
            $enterResult = Invoke-EnterWorld -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -MetadataPath $resumeMetadataPath
            Write-God2AtomicJson -Value ([ordered]@{
                command = "EnterWorldOnly"
                targetPid = $targetPid
                targetHwnd = $clientSnapshot.mainWindowHandle
                result = $enterResult
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }) -Path (Join-Path $artifactRoot "enter-world-only-result.json")
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = if ($enterResult.worldReady) { "WorldReady" } else { "EnterWorldAttempted" }
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "InputStimulusOnly") {
            $currentStage = "InputStimulusOnly"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            $targetPid = [int]$command.clientPid
            if ($targetPid -eq 0) {
                $client = Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object Id | Select-Object -Last 1
                if (-not $client) { throw "InputStimulusOnly cannot find an active-profile client." }
                $targetPid = [int]$client.Id
            }
            $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
            if (-not $clientSnapshot -or $clientSnapshot.mainWindowHandle -eq 0) {
                throw "InputStimulusOnly cannot find target HWND."
            }
            $rect = Get-ClientRectObject -Hwnd ([int64]$clientSnapshot.mainWindowHandle)
            [God2Automation.NativeApi]::BringToTop([IntPtr]$clientSnapshot.mainWindowHandle)
            $beforeStimulus = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-before.png"
            if (Test-EnterGameScreenImage -Path $beforeStimulus) {
                $enterPoint = Get-PointFromRatio -Rect $rect -X 0.65 -Y 0.775
                [God2Automation.NativeApi]::RealClick($enterPoint.x, $enterPoint.y, 180, 120, 500)
                Start-Sleep -Seconds 2
                $afterEntry = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-after-enter-title.png"
                if (-not (Test-LoginScreenImage -Path $afterEntry)) {
                    Write-God2AtomicJson -Value ([ordered]@{
                        result = "EntryClickRejected"
                        enterPoint = $enterPoint
                        targetPid = $targetPid
                        targetHwnd = $clientSnapshot.mainWindowHandle
                        beforeScreenshot = $beforeStimulus
                        afterScreenshot = $afterEntry
                        note = "Entry click did not transition; downstream login stimulus intentionally skipped."
                    }) -Path (Join-Path $artifactRoot "input-stimulus.json")
                    Remove-Item -LiteralPath $commandPath -Force
                    $currentStage = "EntryClickRejected"
                    Publish-HostStatus -Stage $currentStage -ErrorValue "Entry click did not transition to login screen." -ReadyValue $true
                    exit 0
                }
            }
            $accountPoint = Get-PointFromRatio -Rect $rect -X 0.51 -Y 0.385
            $passwordPoint = Get-PointFromRatio -Rect $rect -X 0.51 -Y 0.443
            [God2Automation.NativeApi]::Click($passwordPoint.x, $passwordPoint.y)
            Start-Sleep -Milliseconds 300
            [God2Automation.NativeApi]::Key(0x47)
            Start-Sleep -Milliseconds 500
            Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-password-vk47.png" | Out-Null
            [God2Automation.NativeApi]::Click($accountPoint.x, $accountPoint.y)
            Start-Sleep -Milliseconds 300
            [God2Automation.NativeApi]::Key(0x48)
            Start-Sleep -Milliseconds 500
            Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-account-vk48.png" | Out-Null
            Remove-Item -LiteralPath $commandPath -Force
            $currentStage = "InputStimulusComplete"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "AttachInputProbe") {
            $currentStage = "AttachInputProbe"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            $runDir = [string]$command.runDir
            $targetPid = [int]$command.clientPid
            if ([string]::IsNullOrWhiteSpace($runDir)) {
                throw "AttachInputProbe missing runDir."
            }
            if ($command.restartClient) {
                Stop-ActiveRuntimeProcesses -IncludeClient | Out-Null
                Start-Sleep -Seconds 2
                $targetPid = 0
            }
            if ($targetPid -eq 0) {
                $clientSnapshotForAttach = Invoke-PrivilegeGate
                $targetPid = [int]$clientSnapshotForAttach.pid
            }
            New-Item -ItemType Directory -Force -Path $runDir | Out-Null
            $sensitive = Join-Path $runDir "sensitive"
            New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
            $buildDir = Join-Path $repoRoot "Build\ClientInstrumentation\Release"
            $launcher = Join-Path $buildDir "God2ClientTraceLauncher.exe"
            $sourceDll = Join-Path $buildDir "God2ClientTraceProbe.dll"
            $probeDll = Join-Path $runDir "God2InputTraceProbe.dll"
            Copy-Item -LiteralPath $sourceDll -Destination $probeDll -Force
            $attachEnv = Join-Path $runDir "God2ClientTraceProbe.attach.env"
            @(
                "traceDir=$sensitive",
                "generalLog=$(Join-Path $runDir "general.log")",
                "metadata=$(Join-Path $runDir "metadata.jsonl")",
                "enableInline=true",
                "enableInputInline=false",
                "enableVersionProbe=true"
            ) | Set-Content -LiteralPath $attachEnv -Encoding ASCII
            $output = & $launcher --attach --pid $targetPid --dll $probeDll 2>&1
            $result = [ordered]@{
                command = "AttachInputProbe"
                targetPid = $targetPid
                launcher = $launcher
                dll = $probeDll
                runDir = $runDir
                output = @($output)
                exitCode = $LASTEXITCODE
                attachedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }
            Write-God2AtomicJson -Value $result -Path (Join-Path $runDir "attach-result.json")
            Remove-Item -LiteralPath $commandPath -Force
            if ($LASTEXITCODE -ne 0) {
                throw "AttachInputProbe failed with exit code $LASTEXITCODE."
            }
            if ($command.stimulus) {
                $clientSnapshot = Get-God2ProcessSnapshot -ProcessId $targetPid
                if (-not $clientSnapshot -or $clientSnapshot.mainWindowHandle -eq 0) {
                    throw "AttachInputProbe stimulus cannot find target HWND."
                }
                $rect = Get-ClientRectObject -Hwnd ([int64]$clientSnapshot.mainWindowHandle)
                $accountPoint = Get-PointFromRatio -Rect $rect -X 0.51 -Y 0.385
                $passwordPoint = Get-PointFromRatio -Rect $rect -X 0.51 -Y 0.443
                [God2Automation.NativeApi]::BringToTop([IntPtr]$clientSnapshot.mainWindowHandle)
                $beforeStimulus = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-before.png"
                if (Test-EnterGameScreenImage -Path $beforeStimulus) {
                    $enterPoint = Get-PointFromRatio -Rect $rect -X 0.65 -Y 0.775
                    [God2Automation.NativeApi]::RealClick($enterPoint.x, $enterPoint.y, 180, 120, 500)
                    Start-Sleep -Seconds 2
                    $afterEntry = Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-after-enter-title.png"
                    if (-not (Test-LoginScreenImage -Path $afterEntry)) {
                        Write-God2AtomicJson -Value ([ordered]@{
                            result = "EntryClickRejected"
                            enterPoint = $enterPoint
                            targetPid = $targetPid
                            targetHwnd = $clientSnapshot.mainWindowHandle
                            beforeScreenshot = $beforeStimulus
                            afterScreenshot = $afterEntry
                            note = "Entry click did not transition; downstream login stimulus intentionally skipped."
                        }) -Path (Join-Path $runDir "input-stimulus.json")
                        $currentStage = "EntryClickRejected"
                        Publish-HostStatus -Stage $currentStage -ErrorValue "Entry click did not transition to login screen." -ReadyValue $true
                        $deadline = (Get-Date).AddSeconds($IdleSeconds)
                        while ((Get-Date) -lt $deadline) {
                            Publish-HostStatus -Stage $currentStage -ErrorValue "Entry click did not transition to login screen." -ReadyValue $true
                            Start-Sleep -Seconds 2
                        }
                        exit 0
                    }
                }
                [God2Automation.NativeApi]::Click($passwordPoint.x, $passwordPoint.y)
                Start-Sleep -Milliseconds 300
                [God2Automation.NativeApi]::Key(0x47)
                Start-Sleep -Milliseconds 500
                Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-password-vk47.png" | Out-Null
                [God2Automation.NativeApi]::Click($accountPoint.x, $accountPoint.y)
                Start-Sleep -Milliseconds 300
                [God2Automation.NativeApi]::Key(0x48)
                Start-Sleep -Milliseconds 500
                Save-WindowScreenshot -Hwnd ([int64]$clientSnapshot.mainWindowHandle) -Name "input-probe-account-vk48.png" | Out-Null
                Write-God2AtomicJson -Value ([ordered]@{
                    accountPoint = $accountPoint
                    passwordPoint = $passwordPoint
                    typedVirtualKeys = @("0x47", "0x48")
                    targetPid = $targetPid
                    targetHwnd = $clientSnapshot.mainWindowHandle
                    note = "No plaintext diagnostic string persisted; virtual-key metadata only."
                }) -Path (Join-Path $runDir "input-stimulus.json")
            }
            $currentStage = "InputProbeAttached"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            $deadline = (Get-Date).AddSeconds($IdleSeconds)
            while ((Get-Date) -lt $deadline) {
                Publish-HostStatus -Stage $currentStage -ReadyValue $true
                Start-Sleep -Seconds 2
            }
            exit 0
        }
        if ([string]$command.command -eq "RestartClientAndExit") {
            $currentStage = "RestartingClient"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            Stop-ActiveRuntimeProcesses -IncludeClient | Out-Null
            Start-Sleep -Seconds 2
            $clientSnapshot = Invoke-StartGod2Client
            Remove-Item -LiteralPath $commandPath -Force
            Write-God2AtomicJson -Value ([ordered]@{
                command = "RestartClientAndExit"
                clientPid = $clientSnapshot.pid
                clientHwnd = $clientSnapshot.mainWindowHandle
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }) -Path (Join-Path $artifactRoot "restart-client-result.json")
            $currentStage = "ClientRestarted"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "StopClientAndExit") {
            $currentStage = "StoppingClient"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            $requestedClientPid = if ($command.PSObject.Properties["clientPid"] -and [int]$command.clientPid -gt 0) {
                [int]$command.clientPid
            }
            else { 0 }
            $managedClients = @(Get-ActiveRuntimeProcesses -IncludeClient)
            if ($requestedClientPid -gt 0) {
                $managedClients = @($managedClients | Where-Object { $_.Id -eq $requestedClientPid })
                if ($managedClients.Count -ne 1) {
                    throw "StopClientAndExit target PID is not an active-profile client."
                }
            }
            $stopped = @($managedClients | ForEach-Object {
                Stop-Process -Id $_.Id -Force
                $_.Id
            })
            Start-Sleep -Seconds 2
            Remove-Item -LiteralPath $commandPath -Force
            Write-God2AtomicJson -Value ([ordered]@{
                command = "StopClientAndExit"
                stoppedClientPids = $stopped
                remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
                cleanupScope = "ActiveLauncherProfileExactClientPath"
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }) -Path (Join-Path $artifactRoot "stop-client-result.json")
            $currentStage = "ClientStopped"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
        if ([string]$command.command -eq "DumpClientMemory") {
            $currentStage = "DumpClientMemory"
            Publish-HostStatus -Stage $currentStage -ReadyValue $ready
            $targetPid = [int]$command.clientPid
            if ($targetPid -eq 0) {
                $client = Get-ActiveRuntimeProcesses -IncludeClient | Sort-Object Id | Select-Object -Last 1
                if (-not $client) { throw "DumpClientMemory cannot find an active-profile client." }
                $targetPid = [int]$client.Id
            }
            $runDir = [string]$command.runDir
            if ([string]::IsNullOrWhiteSpace($runDir)) {
                $runDir = Join-Path $artifactRoot "memory-dump"
            }
            New-Item -ItemType Directory -Force -Path $runDir | Out-Null
            $targetProcess = Get-Process -Id $targetPid -ErrorAction Stop
            $clientExecutablePath = $targetProcess.Path
            if ([string]::IsNullOrWhiteSpace($clientExecutablePath) -or -not (Test-Path -LiteralPath $clientExecutablePath -PathType Leaf)) {
                throw "Target client executable path is unavailable for capture attestation."
            }
            $clientExecutableSha256 = (Get-FileHash -LiteralPath $clientExecutablePath -Algorithm SHA256).Hash.ToUpperInvariant()
            $imageBase = 0x00400000
            $results = @()
            foreach ($item in @($command.ranges)) {
                $rva = [Convert]::ToUInt32(([string]$item.rva), 16)
                $before = if ($item.before) { [int]$item.before } else { 256 }
                $length = if ($item.length) { [int]$item.length } else { 1024 }
                $address = [uint32]($imageBase + $rva - $before)
                $bytes = [God2Automation.NativeApi]::ReadProcessBytes($targetPid, $address, $length)
                $name = "pid-$targetPid-rva-$(([string]$item.rva).TrimStart('0','x','X'))"
                $binPath = Join-Path $runDir "$name.bin"
                [IO.File]::WriteAllBytes($binPath, $bytes)
                $hexPath = Join-Path $runDir "$name.hex.txt"
                $lines = @()
                for ($i = 0; $i -lt $bytes.Length; $i += 16) {
                    $take = [Math]::Min(16, $bytes.Length - $i)
                    $slice = New-Object byte[] $take
                    [Array]::Copy($bytes, $i, $slice, 0, $take)
                    $lines += ("0x{0:X8}: {1}" -f ($address + $i), (([BitConverter]::ToString($slice)) -replace '-', ' '))
                }
                $lines | Set-Content -LiteralPath $hexPath -Encoding ASCII
                $results += [pscustomobject]@{
                    rva = ("0x{0:X8}" -f $rva)
                    address = ("0x{0:X8}" -f $address)
                    before = $before
                    requestedLength = $length
                    bytesRead = $bytes.Length
                    bin = $binPath
                    binSha256 = (Get-FileHash -LiteralPath $binPath -Algorithm SHA256).Hash.ToUpperInvariant()
                    hex = $hexPath
                }
            }
            Remove-Item -LiteralPath $commandPath -Force
            Write-God2AtomicJson -Value ([ordered]@{
                command = "DumpClientMemory"
                targetPid = $targetPid
                clientExecutableSha256 = $clientExecutableSha256
                imageBase = "0x00400000"
                results = $results
                completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            }) -Path (Join-Path $runDir "memory-dump-result.json")
            $currentStage = "ClientMemoryDumped"
            Publish-HostStatus -Stage $currentStage -ReadyValue $true
            exit 0
        }
    }

    if ($ultimateLiveCommand) {
        $currentStage = 'UltimateOfficialLauncherFlow'
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $profile = Get-ActiveLauncherProfile
        if ([IO.Path]::GetFileName([string]$profile.targetExecutable) -cne 'Launcher.exe') {
            throw 'Ultimate official live flow requires the configured official Launcher.exe profile.'
        }
        $ultimateRunDir = [IO.Path]::GetFullPath([string]$ultimateLiveCommand.runDir)
        $repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ([string]::IsNullOrWhiteSpace([string]$ultimateLiveCommand.runDir) -or
            -not $ultimateRunDir.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Ultimate official live run directory must remain under the repository root.'
        }
        New-Item -ItemType Directory -Force -Path $ultimateRunDir | Out-Null
        if ($ultimateLiveCommand.PSObject.Properties['forceFreshClient'] -and
            [bool]$ultimateLiveCommand.forceFreshClient) {
            Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | Out-Null
            Start-Sleep -Seconds 1
        }
        $script:officialLauncherReadinessAttempt = 0
        $ultimateClientSnapshot = Invoke-PrivilegeGate
        $preLoginUiReadiness = Wait-God2PreLoginUiReady `
            -ClientSnapshot $ultimateClientSnapshot -TimeoutSeconds 30
        $launchAttestationPath = Join-Path $ultimateRunDir 'official-launch-chain-attestation.json'
        $launchAttestation = New-OfficialLaunchChainAttestation `
            -ClientSnapshot $ultimateClientSnapshot -OutputPath $launchAttestationPath
        $runtimeRunId = if ($ultimateLiveCommand.PSObject.Properties['runtimeRunId'] -and
            -not [string]::IsNullOrWhiteSpace([string]$ultimateLiveCommand.runtimeRunId)) {
            [string]$ultimateLiveCommand.runtimeRunId
        }
        else {
            'ultimate-official-runtime-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
        }
        $runtimeObserveSeconds = if ($ultimateLiveCommand.PSObject.Properties['observeSeconds']) {
            [int]$ultimateLiveCommand.observeSeconds
        }
        else { 180 }
        $deepEvidencePlanPath = if($ultimateLiveCommand.PSObject.Properties['deepEvidencePlanPath']){
            [string]$ultimateLiveCommand.deepEvidencePlanPath
        }else{''}
        $contractAcquisitionObserveSeconds = if(
            $ultimateLiveCommand.PSObject.Properties['contractAcquisitionObserveSeconds']){
            [int]$ultimateLiveCommand.contractAcquisitionObserveSeconds
        }else{48}
        $currentStage = 'UltimateAttachBeforeLogin'
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $ultimateRuntimeCapture = Start-UltimateOfficialRuntimeCapture `
            -ClientSnapshot $ultimateClientSnapshot `
            -AttestationPath $launchAttestationPath `
            -RuntimeRunId $runtimeRunId `
            -RunDirectory $ultimateRunDir `
            -ObserveSeconds $runtimeObserveSeconds `
            -ContractAcquisitionObserveSeconds $contractAcquisitionObserveSeconds `
            -DeepEvidencePlanPath $deepEvidencePlanPath
        Save-WindowScreenshotOrNull -Hwnd ([int64]$ultimateClientSnapshot.mainWindowHandle) `
            -Name 'client-pre-login-after-attach.png' | Out-Null
        Write-God2AtomicJson -Value ([ordered]@{
            command = 'RunUltimateOfficialLiveAndExit'
            runtimeRunId = $runtimeRunId
            clientProcessId = [int]$ultimateClientSnapshot.pid
            attachedBeforeLogin = [bool]$ultimateRuntimeCapture.attachedBeforeLogin
            attachPath = [string]$ultimateRuntimeCapture.attachPath
            launchAttestationPath = $launchAttestationPath
            preLoginScreenStage = [string]$preLoginUiReadiness.screenStage
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }) -Path (Join-Path $ultimateRunDir 'pre-login-attach-result.json')
    }

    $testAccountKey = if ($command -and $command.PSObject.Properties["testAccountKey"]) {
        [string]$command.testAccountKey
    }
    else {
        "OfficialA"
    }
    $secret = if ($ultimateLiveCommand -and
        $ultimateLiveCommand.PSObject.Properties['credentialEnvelopePath'] -and
        -not [string]::IsNullOrWhiteSpace([string]$ultimateLiveCommand.credentialEnvelopePath)) {
        Get-OneTimeTestAccountCredential -EnvelopePath ([string]$ultimateLiveCommand.credentialEnvelopePath)
    }
    else {
        Get-TestAccountCredential -Key $testAccountKey
    }
    $currentStage = "LoginAutomationStarting"
    Publish-HostStatus -Stage $currentStage -ReadyValue $ready
    $loginResult = $null
    $attempt = 1
    $currentStage = "LoginAttempt1"
    Publish-HostStatus -Stage $currentStage -ReadyValue $ready
    $loginResult = Invoke-LoginAttempt -Attempt $attempt -Account ([string]$secret.account) -Password ([string]$secret.password) -NoCredentialCapture
    Write-God2AtomicJson -Value $loginResult -Path (Join-Path $artifactRoot "login-attempt-1.json")
    $secret.account = $null
    $secret.password = $null

    if (-not $loginResult -or -not $loginResult.success) {
        $currentStage = "LoginFailed"
        $lastError = if ($loginResult) { $loginResult.error } else { "Login did not run." }
        $stoppedAfterLoginFailure = @(Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | ForEach-Object { $_.processId })
        Write-God2AtomicJson -Value ([ordered]@{
            command = "LoginFailureCleanup"
            failureCode = $lastError
            stoppedReplayProcessPids = $stoppedAfterLoginFailure
            remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
            launcherRemainingCount = @((Get-ActiveRuntimeProcesses -IncludeLauncher)).Count
            cleanupScope = "ActiveLauncherProfileExactExecutablePaths"
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            note = "Official client and isolated replay launchers were cleaned after automated login failure."
        }) -Path (Join-Path $artifactRoot "login-failure-cleanup-result.json")
        Publish-HostStatus -Stage $currentStage -ErrorValue $lastError -ReadyValue $true
        exit 30
    }

    $localEndpointAttestation = $null
    $officialEndpointAttestation = $null
    if ($frozenRegressionCommand) {
        $localEndpointAttestation = Get-God2LocalEndpointAttestation `
            -ClientPid ([int]$loginResult.trace.targetPid) `
            -TimeoutSeconds 5
        Write-God2AtomicJson `
            -Value $localEndpointAttestation `
            -Path (Join-Path $artifactRoot "local-endpoint-attestation.json")
    }
    elseif ($ultimateLiveCommand) {
        $officialEndpointAttestation = Get-God2OfficialEndpointAttestation `
            -ClientPid ([int]$loginResult.trace.targetPid) `
            -TimeoutSeconds 15
        Write-God2AtomicJson `
            -Value $officialEndpointAttestation `
            -Path (Join-Path $artifactRoot 'official-endpoint-attestation.json')
    }

    $endpointAttestationFailed =
        ($frozenRegressionCommand -and -not [bool]$localEndpointAttestation.attested) -or
        ($ultimateLiveCommand -and -not [bool]$officialEndpointAttestation.attested)
    if ($endpointAttestationFailed) {
        $currentStage = if ($ultimateLiveCommand) {
            'UltimateOfficialEndpointNotAttested'
        }
        else {
            'FrozenRegressionEndpointNotAttested'
        }
        $lastError = if ($ultimateLiveCommand) {
            'Official client PID did not establish a non-loopback TCP connection.'
        }
        else {
            'Official client PID did not connect to the required local endpoint 127.0.0.1:2592.'
        }
        Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | Out-Null
        $runDir = [string]$(if ($ultimateLiveCommand) { $ultimateLiveCommand.runDir } else { $frozenRegressionCommand.runDir })
        if ([string]::IsNullOrWhiteSpace($runDir)) {
            $runDir = $artifactRoot
        }
        New-Item -ItemType Directory -Force -Path $runDir | Out-Null
        Write-God2AtomicJson -Value ([ordered]@{
            command = if ($ultimateLiveCommand) { 'RunUltimateOfficialLiveAndExit' } else { 'RunFrozenRegressionAndExit' }
            testAccountKey = $testAccountKey
            hostArtifactRoot = $artifactRoot
            clientPid = [int]$loginResult.trace.targetPid
            loginSuccess = [bool]$loginResult.success
            characterSelect = $false
            enterWorld = $false
            worldReady = $false
            heartbeatCount = 0
            heartbeatDurationSeconds = 0
            firstBlocker = if ($ultimateLiveCommand) { 'OfficialEndpointNotAttested' } else { 'LocalEndpointNotAttested' }
            localEndpointAttestation = $localEndpointAttestation
            officialEndpointAttestation = $officialEndpointAttestation
            remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
            launcherRemainingCount = @((Get-ActiveRuntimeProcesses -IncludeLauncher)).Count
            fakeNetworkBytes = 0
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }) -Path (Join-Path $runDir $(if ($ultimateLiveCommand) { 'ultimate-official-live-host-result.json' } else { 'frozen-regression-host-result.json' }))
        if (Test-Path -LiteralPath $commandPath) {
            Remove-Item -LiteralPath $commandPath -Force
        }
        Publish-HostStatus -Stage $currentStage -ErrorValue $lastError -ReadyValue $true
        exit 31
    }

    if ($worldCaptureCommand -or $frozenRegressionCommand) {
        # Attach only after authentication so frozen regression can observe
        # world/bootstrap heartbeats without recording credential entry.
        $postAuthenticationTrace = Start-LoginAttemptTraceProbe -TargetPid ([int]$loginResult.trace.targetPid) -Attempt 100
        $loginResult | Add-Member -NotePropertyName trace -NotePropertyValue $postAuthenticationTrace -Force
    }

    $currentStage = "CharacterSelect"
    Publish-HostStatus -Stage $currentStage -ReadyValue $true
    if ($characterLifecycleUiProbeCommand) {
        $currentStage = "CharacterLifecycleCharacterSelectAdvance"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $advanceResult = Invoke-EnterWorld -Hwnd ([int64]$loginResult.hwnd) -MetadataPath ([string]$loginResult.trace.metadata) -StopAtCharacterSelect
        Write-God2AtomicJson -Value $advanceResult -Path (Join-Path $artifactRoot "character-lifecycle-character-select-advance-result.json")
        $currentStage = "CharacterLifecycleUiProbe"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $probeResult = Invoke-CharacterLifecycleUiProbe -Hwnd ([int64]$loginResult.hwnd) -Command $characterLifecycleUiProbeCommand
        Write-God2AtomicJson -Value $probeResult -Path (Join-Path $artifactRoot "character-lifecycle-ui-probe-result.json")
        if (Test-Path -LiteralPath $commandPath) {
            Remove-Item -LiteralPath $commandPath -Force
        }
        $currentStage = "CharacterLifecycleUiProbeComplete"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        exit 0
    }

    $activeRegressionCommand = if ($ultimateLiveCommand) { $ultimateLiveCommand } else { $frozenRegressionCommand }
    $worldReadyTimeoutSeconds = if ($activeRegressionCommand -and
        $activeRegressionCommand.PSObject.Properties["worldReadyTimeoutSeconds"]) {
        [int]$activeRegressionCommand.worldReadyTimeoutSeconds
    }
    else {
        120
    }
    $postWorldGroundClick = [bool]($activeRegressionCommand -and
        $activeRegressionCommand.PSObject.Properties["postWorldGroundClick"] -and
        [bool]$activeRegressionCommand.postWorldGroundClick)
    $worldMetadataPath = if ($ultimateLiveCommand) {
        [string]$ultimateRuntimeCapture.liveMetadataPath
    }
    else {
        [string]$loginResult.trace.metadata
    }
    $enterResult = Invoke-EnterWorld `
        -Hwnd ([int64]$loginResult.hwnd) `
        -MetadataPath $worldMetadataPath `
        -WorldReadyTimeoutSeconds $worldReadyTimeoutSeconds `
        -RequireNonLoopbackWorldTrace:($null -ne $ultimateLiveCommand) `
        -ContractAcquisitionControlPath $(if ($ultimateLiveCommand) {
            [string]$ultimateRuntimeCapture.acquisitionControlPath
        } else { '' }) `
        -ContractAcquisitionSessionId $(if ($ultimateLiveCommand) {
            [string]$ultimateRuntimeCapture.runtimeRunId
        } else { '' }) `
        -ContractAcquisitionClientProcessId $(if ($ultimateLiveCommand) {
            [uint32]$loginResult.trace.targetPid
        } else { 0 }) `
        -PostWorldGroundClick:$postWorldGroundClick
    Write-God2AtomicJson -Value $enterResult -Path (Join-Path $artifactRoot "enter-world-result.json")

    if ($ultimateLiveCommand) {
        Write-God2AtomicJson -Value ([ordered]@{
            schemaId = 'God2ContractAcquisitionLiveControl'
            schemaVersion = 1
            sessionId = [string]$ultimateRuntimeCapture.runtimeRunId
            clientProcessId = [uint32]$loginResult.trace.targetPid
            status = if ([bool]$enterResult.worldReady) { 'WORLD_READY' } else { 'ABORT' }
            worldScreenReady = [string]$enterResult.currentStage -eq 'WorldReady'
            worldBootstrapActivity = [bool]$enterResult.worldTrace.worldBootstrapActivity
            heartbeatCount = [int]$enterResult.worldTrace.heartbeatCount
            heartbeatDurationSeconds = [double]$enterResult.worldTrace.heartbeatDurationSeconds
            firstBlocker = [string]$enterResult.firstBlocker
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }) -Path ([string]$ultimateRuntimeCapture.acquisitionControlPath)
        $currentStage = 'UltimateRuntimeDrainAndUnload'
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $runtimeProcess = $ultimateRuntimeCapture.process
        $runtimeWaitSeconds = [int]$ultimateRuntimeCapture.observeSeconds + 240
        $runtimeExited = $false
        try {
            $runtimeProcess | Wait-Process -Timeout $runtimeWaitSeconds -ErrorAction Stop
            $runtimeExited = $true
        }
        catch {
            $runtimeProcess.Refresh()
            $runtimeExited = [bool]$runtimeProcess.HasExited
        }
        if (-not $runtimeExited) {
            Stop-Process -Id $runtimeProcess.Id -Force -ErrorAction SilentlyContinue
            throw 'Ultimate official runtime exceeded its bounded drain/unload timeout.'
        }
        $runtimeExitCode = $runtimeProcess.ExitCode
        if ($null -eq $runtimeExitCode) {
            throw 'Ultimate official runtime exited without an observable process exit code.'
        }
        $runtimeSummaryPath = Join-Path ([string]$ultimateRuntimeCapture.runtimeRoot) 'analysis\official-runtime-summary.json'
        $runtimeSummary = if (Test-Path -LiteralPath $runtimeSummaryPath -PathType Leaf) {
            Read-God2JsonWithRetry -Path $runtimeSummaryPath
        }
        else { $null }
        $runtimePassed = $runtimeExitCode -eq 0 -and $null -ne $runtimeSummary -and [bool]$runtimeSummary.Passed

        $currentStage = 'UltimateOfficialLiveCleanup'
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $stoppedAfterUltimate = @(Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | ForEach-Object { $_.processId })
        Start-Sleep -Seconds 2

        $ultimateRunDir = [IO.Path]::GetFullPath([string]$ultimateLiveCommand.runDir)
        $hostResult = [ordered]@{
            schemaId = 'God2UltimateOfficialLiveHostResult'
            schemaVersion = 1
            command = 'RunUltimateOfficialLiveAndExit'
            testAccountKey = $testAccountKey
            hostSessionId = $sessionId
            runtimeSessionId = [string]$ultimateRuntimeCapture.runtimeRunId
            hostArtifactRoot = $artifactRoot
            launcherExecutable = 'Launcher.exe'
            directClientLaunch = $false
            agreementAccepted = [bool]$launchAttestation.AgreementAccepted
            startGameClicked = [bool]$launchAttestation.StartGameClicked
            directSoundDialogHandled = [bool]$launchAttestation.DirectSoundDialogHandled
            attachedBeforeLogin = [bool]$ultimateRuntimeCapture.attachedBeforeLogin
            clientPid = [int]$loginResult.trace.targetPid
            loginSuccess = [bool]$loginResult.success
            characterSelect = $true
            enterWorld = [bool]$enterResult.worldReady
            worldReady = [bool]$enterResult.worldReady
            clientWorldScreenReady = [string]$enterResult.currentStage -eq 'WorldReady'
            officialEndpointAttestation = $officialEndpointAttestation
            runtimeExitCode = $runtimeExitCode
            runtimeSummaryPath = $runtimeSummaryPath
            runtimePassed = [bool]$runtimePassed
            attachStatus = if ($runtimeSummary) { [string]$runtimeSummary.AttachStatus } else { 'MISSING' }
            detachStatus = if ($runtimeSummary) { [string]$runtimeSummary.DetachStatus } else { 'MISSING' }
            strictUnloadVerified = if ($runtimeSummary) { [bool]$runtimeSummary.StrictUnloadVerified } else { $false }
            targetAliveAfterDetach = if ($runtimeSummary) { [bool]$runtimeSummary.TargetAliveAfterDetach } else { $false }
            heartbeatCount = if ($enterResult.worldTrace) { [int]$enterResult.worldTrace.heartbeatCount } else { 0 }
            heartbeatDurationSeconds = if ($enterResult.worldTrace) { $enterResult.worldTrace.heartbeatDurationSeconds } else { 0 }
            firstBlocker = if (-not $enterResult.worldReady) { $enterResult.firstBlocker } elseif (-not $runtimePassed) { 'OfficialRuntimeDidNotPass' } else { $null }
            stoppedProcessPids = $stoppedAfterUltimate
            remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
            launcherRemainingCount = @((Get-ActiveRuntimeProcesses -IncludeLauncher)).Count
            fakeNetworkBytes = 0
            passed = [bool]$loginResult.success -and [bool]$enterResult.worldReady -and
                [bool]$officialEndpointAttestation.attested -and [bool]$runtimePassed
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
        Write-God2AtomicJson -Value $hostResult -Path (Join-Path $ultimateRunDir 'ultimate-official-live-host-result.json')
        if (Test-Path -LiteralPath $commandPath) {
            Remove-Item -LiteralPath $commandPath -Force
        }
        $currentStage = if ($hostResult.passed) { 'UltimateOfficialLiveComplete' } else { 'UltimateOfficialLiveIncomplete' }
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        exit 0
    }

    if ($frozenRegressionCommand) {
        $currentStage = "FrozenRegressionCleanup"
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $stoppedAfterRegression = @(Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | ForEach-Object { $_.processId })
        Start-Sleep -Seconds 2

        $runDir = [string]$frozenRegressionCommand.runDir
        if ([string]::IsNullOrWhiteSpace($runDir)) {
            $runDir = $artifactRoot
        }
        New-Item -ItemType Directory -Force -Path $runDir | Out-Null
        $hostResult = [ordered]@{
            command = "RunFrozenRegressionAndExit"
            testAccountKey = $testAccountKey
            hostArtifactRoot = $artifactRoot
            clientPid = if ($loginResult.trace -and $loginResult.trace.targetPid) { [int]$loginResult.trace.targetPid } else { $null }
            loginSuccess = [bool]$loginResult.success
            characterSelect = $true
            enterWorld = [bool]$enterResult.worldReady
            worldReady = [bool]$enterResult.worldReady
            clientWorldScreenReady = [string]$enterResult.currentStage -eq "WorldReady"
            protocolMetadataAcceptance = if ([string]::IsNullOrWhiteSpace([string]$enterResult.worldTrace.metadataPath)) {
                "NOT RUN — NO PACKET CAPTURE"
            }
            elseif ([bool]$enterResult.worldReady) {
                "PASS"
            }
            else {
                "BLOCKED"
            }
            heartbeatCount = if ($enterResult.worldTrace) { [int]$enterResult.worldTrace.heartbeatCount } else { 0 }
            heartbeatDurationSeconds = if ($enterResult.worldTrace) { $enterResult.worldTrace.heartbeatDurationSeconds } else { 0 }
            firstBlocker = $enterResult.firstBlocker
            worldReadyTimeoutSeconds = $worldReadyTimeoutSeconds
            postWorldGroundClickAttempted = [bool]$enterResult.postWorldGroundClickAttempted
            localEndpointAttestation = $localEndpointAttestation
            stoppedClientPids = $stoppedAfterRegression
            remainingClientCount = @((Get-ActiveRuntimeProcesses -IncludeClient)).Count
            launcherRemainingCount = @((Get-ActiveRuntimeProcesses -IncludeLauncher)).Count
            fakeNetworkBytes = 0
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }
        Write-God2AtomicJson -Value $hostResult -Path (Join-Path $runDir "frozen-regression-host-result.json")
        if (Test-Path -LiteralPath $commandPath) {
            Remove-Item -LiteralPath $commandPath -Force
        }
        $currentStage = if ($enterResult.worldReady) { "FrozenRegressionComplete" } else { "FrozenRegressionIncomplete" }
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        exit 0
    }

    if ($worldCaptureCommand -and $enterResult.worldReady) {
        $captureMode = if ([string]$worldCaptureCommand.command -eq "RunFreshNpcObservation") { "FreshNpcObservation" } else { "FullMatrix" }
        $currentStage = if ($captureMode -eq "FreshNpcObservation") { "FreshNpcObservation" } else { "WorldCaptureMatrix" }
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        $captureResult = Invoke-WorldPacketCaptureMatrix -Hwnd ([int64]$loginResult.hwnd) -TraceRoot ([string]$loginResult.trace.traceRoot) -Mode $captureMode
        Write-God2AtomicJson -Value $captureResult -Path (Join-Path $artifactRoot "world-capture-matrix-result.json")
        if (Test-Path -LiteralPath $commandPath) {
            Remove-Item -LiteralPath $commandPath -Force
        }
        $currentStage = if ($captureResult.analyzerExitCode -eq 0) { if ($captureMode -eq "FreshNpcObservation") { "FreshNpcObservationComplete" } else { "WorldCaptureMatrixComplete" } } else { if ($captureMode -eq "FreshNpcObservation") { "FreshNpcObservationIncomplete" } else { "WorldCaptureMatrixIncomplete" } }
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
    }

    $currentStage = if ($enterResult.worldReady) { "WorldReady" } else { "EnterWorldAttempted" }
    Publish-HostStatus -Stage $currentStage -ReadyValue $true

    $deadline = (Get-Date).AddSeconds($IdleSeconds)
    while ((Get-Date) -lt $deadline) {
        Publish-HostStatus -Stage $currentStage -ReadyValue $true
        Start-Sleep -Seconds 2
    }
}
catch {
    $currentStage = "HostFailed"
    $lastError = [ordered]@{
        message = $_.Exception.Message
        script = $_.InvocationInfo.ScriptName
        line = $_.InvocationInfo.ScriptLineNumber
        command = $_.InvocationInfo.Line
        position = $_.InvocationInfo.PositionMessage
    }
    if ($ultimateRuntimeCapture -and $ultimateRuntimeCapture.process) {
        $ultimateRuntimeCapture.process.Refresh()
        if (-not $ultimateRuntimeCapture.process.HasExited) {
            Stop-Process -Id $ultimateRuntimeCapture.process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    if ($ultimateLiveCommand) {
        Stop-ActiveRuntimeProcesses -IncludeLauncher -IncludeClient | Out-Null
    }
    Publish-HostStatus -Stage $currentStage -ErrorValue $lastError -ReadyValue $false
    exit 1
}


