param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    Get-God2RepoRoot
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$release = Test-God2ServerReleaseManifest
if (-not $release.succeeded) {
    throw "Published release verification failed: $($release.errorCode): $($release.diagnostic)"
}
$secret = Initialize-God2DatabasePasswordEnvironment
if (-not $secret.hasSecret) {
    throw "Formal database secret is unavailable: $($secret.failureCode)"
}

$runId = "formal-" + (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$runDirectory = Join-Path $repoRoot "Artifacts\PublicBetaCompatibility\$runId"
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$stdoutPath = Join-Path $runDirectory "server.stdout.log"
$stderrPath = Join-Path $runDirectory "server.stderr.log"
$stopPath = Join-Path $runDirectory "stop.signal"
$acceptancePath = Join-Path $runDirectory "acceptance.json"
$executable = Join-Path $release.releaseDirectory "God2 Classic Server.exe"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$process = $null
$processStarted = $false
$stdoutTask = $null
$stderrTask = $null
$client = $null
$stdout = ""
$stderr = ""
$handshakeHex = ""
$listenerReady = $false
$cleanShutdown = $false
$exitCode = $null
$listenerReleased = $false

function Connect-FormalEndpoint {
    param([int] $TimeoutMilliseconds)

    $candidate = [Net.Sockets.TcpClient]::new()
    try {
        $task = $candidate.ConnectAsync("127.0.0.1", 2592)
        if (-not $task.Wait($TimeoutMilliseconds) -or -not $candidate.Connected) {
            $candidate.Dispose()
            return $null
        }
        return $candidate
    }
    catch {
        $candidate.Dispose()
        return $null
    }
}

try {
    $occupied = Connect-FormalEndpoint -TimeoutMilliseconds 250
    if ($null -ne $occupied) {
        $occupied.Dispose()
        throw "Formal endpoint 127.0.0.1:2592 is already occupied."
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executable
    # The formal executable is always the manifest-verified published binary;
    # runtime configuration remains repository-owned and is resolved from repoRoot.
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = '--stop-file "{0}"' -f ($stopPath -replace '"', '\"')

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Formal server process could not be started."
    }
    $processStarted = $true
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline -and $null -eq $client) {
        if ($process.HasExited) {
            break
        }
        $client = Connect-FormalEndpoint -TimeoutMilliseconds 250
        if ($null -eq $client) {
            Start-Sleep -Milliseconds 100
        }
    }
    $listenerReady = $null -ne $client -and $client.Connected
    if (-not $listenerReady) {
        throw "Formal server listener did not become ready."
    }

    $stream = $client.GetStream()
    $stream.ReadTimeout = 5000
    $received = [Collections.Generic.List[byte]]::new(19)
    $buffer = [byte[]]::new(19)
    while ($received.Count -lt 19) {
        $read = $stream.Read($buffer, 0, 19 - $received.Count)
        if ($read -le 0) {
            break
        }
        for ($index = 0; $index -lt $read; $index++) {
            $received.Add($buffer[$index])
        }
    }
    $handshakeHex = [BitConverter]::ToString($received.ToArray()).Replace("-", "")
    $client.Dispose()
    $client = $null

    [IO.File]::WriteAllBytes($stopPath, [byte[]]::new(0))
    if (-not $process.WaitForExit(30000)) {
        throw "Formal server did not honor the stop-file within 30 seconds."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $cleanShutdown = $exitCode -eq 0 -and $stderr.Length -eq 0

    $releaseDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $probe = Connect-FormalEndpoint -TimeoutMilliseconds 200
        if ($null -eq $probe) {
            $listenerReleased = $true
            break
        }
        $probe.Dispose()
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $releaseDeadline)
}
finally {
    if ($null -ne $client) {
        $client.Dispose()
    }
    if ($null -ne $process -and $processStarted) {
        if (-not $process.HasExited) {
            try { [IO.File]::WriteAllBytes($stopPath, [byte[]]::new(0)) } catch { }
            if (-not $process.WaitForExit(5000)) {
                $process.Kill($true)
                $process.WaitForExit()
            }
        }
        if ([string]::IsNullOrEmpty($stdout) -and $null -ne $stdoutTask) {
            $stdout = $stdoutTask.GetAwaiter().GetResult()
        }
        if ([string]::IsNullOrEmpty($stderr) -and $null -ne $stderrTask) {
            $stderr = $stderrTask.GetAwaiter().GetResult()
        }
        if ($null -eq $exitCode) {
            $exitCode = $process.ExitCode
        }
        $process.Dispose()
    }
    elseif ($null -ne $process) {
        $process.Dispose()
    }
    [IO.File]::WriteAllText($stdoutPath, $stdout, $utf8NoBom)
    [IO.File]::WriteAllText($stderrPath, $stderr, $utf8NoBom)
    Remove-Item Env:GOD2_DB_PASSWORD -ErrorAction SilentlyContinue
}

$acceptance = [ordered]@{
    runDirectory = $runDirectory
    buildId = $release.buildId
    listenerReady = $listenerReady
    handshakeHex = $handshakeHex
    expectedHandshake = "1300405FD0401BB55367D34D90DF1D929883DD"
    ready = $listenerReady -and ($handshakeHex -cne "")
    cleanShutdown = $cleanShutdown
    exitCode = $exitCode
    stderrLength = $stderr.Length
    listenerReleased = $listenerReleased
}
[IO.File]::WriteAllText(
    $acceptancePath,
    ($acceptance | ConvertTo-Json -Depth 5),
    $utf8NoBom)

$failed = @($acceptance.GetEnumerator() | Where-Object {
    $_.Key -notin @("runDirectory", "buildId", "handshakeHex", "expectedHandshake", "exitCode", "stderrLength") -and
    $_.Value -ne $true
})
if ($handshakeHex -cne $acceptance.expectedHandshake -or
    $exitCode -ne 0 -or $stderr.Length -ne 0 -or $failed.Count -ne 0) {
    throw "Formal public-beta compatibility acceptance failed; inspect $acceptancePath"
}

$acceptance | ConvertTo-Json -Depth 5
