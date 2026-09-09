param(
    [Parameter(Mandatory = $true)]
    [string] $RepoRoot,

    [Parameter(Mandatory = $true)]
    [string] $HostRoot,

    [switch] $SmokeChild,

    [int] $IdleSeconds = 3600
)

$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$HostRoot = [System.IO.Path]::GetFullPath($HostRoot)
$QueueRoot = Join-Path $HostRoot "queue"
$RunningRoot = Join-Path $HostRoot "running"
$CompleteRoot = Join-Path $HostRoot "complete"
$FailedRoot = Join-Path $HostRoot "failed"
$LogRoot = Join-Path $HostRoot "logs"
New-Item -ItemType Directory -Force -Path $HostRoot,$QueueRoot,$RunningRoot,$CompleteRoot,$FailedRoot,$LogRoot | Out-Null

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

public static class IntegrityNative
{
    private const UInt32 PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const UInt32 TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(UInt32 access, bool inherit, UInt32 processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, UInt32 desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public UInt32 Attributes;
    }

    public static string GetProcessIntegrity(int pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (UInt32)pid);
        if (process == IntPtr.Zero) return "OpenProcessFailed:" + Marshal.GetLastWin32Error();
        try
        {
            IntPtr token;
            if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return "OpenProcessTokenFailed:" + Marshal.GetLastWin32Error();
            try
            {
                int length = 0;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out length);
                IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out length)) return "GetTokenInformationFailed:" + Marshal.GetLastWin32Error();
                    TOKEN_MANDATORY_LABEL label = (TOKEN_MANDATORY_LABEL)Marshal.PtrToStructure(buffer, typeof(TOKEN_MANDATORY_LABEL));
                    SecurityIdentifier sid = new SecurityIdentifier(label.Label.Sid);
                    string sidText = sid.Value;
                    string[] parts = sidText.Split('-');
                    int rid = Int32.Parse(parts[parts.Length - 1]);
                    string level = rid >= 16384 ? "System" :
                                   rid >= 12288 ? "High" :
                                   rid >= 8192 ? "Medium" :
                                   rid >= 4096 ? "Low" : "Untrusted";
                    return level + " / RID 0x" + rid.ToString("X");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
"@

function Get-IntegrityReport {
    param([int] $ProcessId)
    [pscustomobject]@{
        pid = $ProcessId
        integrity = [IntegrityNative]::GetProcessIntegrity($ProcessId)
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)] [object] $Value,
        [Parameter(Mandatory = $true)] [string] $Path
    )
    $tmp = "$Path.tmp"
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $tmp -Encoding UTF8
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function Start-HostChild {
    param(
        [Parameter(Mandatory = $true)] [pscustomobject] $Command,
        [Parameter(Mandatory = $true)] [string] $CommandPath
    )

    $id = if ($Command.id) { [string]$Command.id } else { [System.IO.Path]::GetFileNameWithoutExtension($CommandPath) }
    $exe = [string]$Command.executable
    if ([string]::IsNullOrWhiteSpace($exe)) {
        throw "Command $id has no executable."
    }
    if (-not [System.IO.Path]::IsPathRooted($exe)) {
        $exe = Join-Path $RepoRoot $exe
    }
    $exe = [System.IO.Path]::GetFullPath($exe)
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Command $id executable not found: $exe"
    }

    $cwd = if ($Command.workingDirectory) { [string]$Command.workingDirectory } else { $RepoRoot }
    if (-not [System.IO.Path]::IsPathRooted($cwd)) {
        $cwd = Join-Path $RepoRoot $cwd
    }
    $cwd = [System.IO.Path]::GetFullPath($cwd)

    $args = @()
    if ($Command.arguments) {
        foreach ($arg in $Command.arguments) {
            $args += [string]$arg
        }
    }

    function Quote-ProcessArgument {
        param([string] $Value)
        if ($Value.Length -eq 0) {
            return '""'
        }
        if ($Value -notmatch '[\s"]') {
            return $Value
        }
        return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
    }

    $childLog = Join-Path $LogRoot "$id.stdout.txt"
    $childErr = Join-Path $LogRoot "$id.stderr.txt"
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $exe
    $psi.Arguments = (($args | ForEach-Object { Quote-ProcessArgument -Value $_ }) -join " ")
    $psi.WorkingDirectory = $cwd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($Command.environment) {
        foreach ($prop in $Command.environment.PSObject.Properties) {
            $psi.EnvironmentVariables[$prop.Name] = [string]$prop.Value
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void]$process.Start()
    $childIntegrity = (Get-IntegrityReport -ProcessId $process.Id).integrity

    $outTask = $process.StandardOutput.ReadToEndAsync()
    $errTask = $process.StandardError.ReadToEndAsync()
    $started = Get-Date
    $wait = if ($null -ne $Command.wait) { [bool]$Command.wait } else { $true }
    $timeoutMs = if ($Command.timeoutMs) { [int]$Command.timeoutMs } else { 300000 }
    $timedOut = $false
    if ($wait) {
        if (-not $process.WaitForExit($timeoutMs)) {
            $timedOut = $true
            try { $process.Kill() } catch {}
            $process.WaitForExit()
        }
    }

    $stdout = $outTask.GetAwaiter().GetResult()
    $stderr = $errTask.GetAwaiter().GetResult()
    $stdout | Set-Content -LiteralPath $childLog -Encoding UTF8
    $stderr | Set-Content -LiteralPath $childErr -Encoding UTF8

    $report = [pscustomobject]@{
        id = $id
        executable = $exe
        arguments = $args
        workingDirectory = $cwd
        parentPid = $PID
        pid = $process.Id
        startedAt = $started.ToString("o")
        completedAt = (Get-Date).ToString("o")
        wait = $wait
        timedOut = $timedOut
        exitCode = if ($wait) { $process.ExitCode } else { $null }
        childIntegrity = $childIntegrity
        stdout = $childLog
        stderr = $childErr
    }
    return $report
}

$hostStatusPath = Join-Path $HostRoot "host-status.json"
$hostLogPath = Join-Path $LogRoot "host.log"
$start = Get-Date
$hostStatus = [pscustomobject]@{
    pid = $PID
    integrity = (Get-IntegrityReport -ProcessId $PID).integrity
    repoRoot = $RepoRoot
    hostRoot = $HostRoot
    queueRoot = $QueueRoot
    startedAt = $start.ToString("o")
    mode = "Running"
    runAsPerStep = $false
    acceptedCommands = @("executable", "arguments", "workingDirectory", "environment", "wait", "timeoutMs")
}
Write-JsonFile -Value $hostStatus -Path $hostStatusPath
"$($hostStatus.startedAt) host pid=$PID integrity=$($hostStatus.integrity)" | Add-Content -LiteralPath $hostLogPath -Encoding UTF8

if ($SmokeChild) {
    $smokeCommand = [pscustomobject]@{
        id = "high-inheritance-smoke"
        executable = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "`$p=[System.Diagnostics.Process]::GetCurrentProcess(); [pscustomobject]@{ pid=`$p.Id; parent=$PID; user=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name } | ConvertTo-Json")
        workingDirectory = $RepoRoot
        wait = $true
        timeoutMs = 30000
    }
    $report = Start-HostChild -Command $smokeCommand -CommandPath "smoke"
    Write-JsonFile -Value $report -Path (Join-Path $CompleteRoot "high-inheritance-smoke.result.json")
}

$deadline = (Get-Date).AddSeconds($IdleSeconds)
while ((Get-Date) -lt $deadline) {
    Get-ChildItem -LiteralPath $QueueRoot -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime |
        ForEach-Object {
            $queued = $_.FullName
            $running = Join-Path $RunningRoot $_.Name
            Move-Item -LiteralPath $queued -Destination $running -Force
            try {
                $command = Get-Content -LiteralPath $running -Raw | ConvertFrom-Json
                if ($command.stopHost -eq $true) {
                    Write-JsonFile -Value ([pscustomobject]@{ id = $_.BaseName; stopHost = $true; completedAt = (Get-Date).ToString("o") }) -Path (Join-Path $CompleteRoot "$($_.BaseName).result.json")
                    $deadline = Get-Date
                    return
                }
                $report = Start-HostChild -Command $command -CommandPath $running
                $dest = if ($report.timedOut -or (($null -ne $report.exitCode) -and $report.exitCode -ne 0)) { $FailedRoot } else { $CompleteRoot }
                Write-JsonFile -Value $report -Path (Join-Path $dest "$($report.id).result.json")
            }
            catch {
                Write-JsonFile -Value ([pscustomobject]@{ id = $_.BaseName; error = $_.Exception.Message; completedAt = (Get-Date).ToString("o") }) -Path (Join-Path $FailedRoot "$($_.BaseName).result.json")
            }
            finally {
                Remove-Item -LiteralPath $running -Force -ErrorAction SilentlyContinue
            }
        }
    Start-Sleep -Milliseconds 500
}

$finalStatus = [pscustomobject]@{
    pid = $hostStatus.pid
    integrity = $hostStatus.integrity
    repoRoot = $hostStatus.repoRoot
    hostRoot = $hostStatus.hostRoot
    queueRoot = $hostStatus.queueRoot
    startedAt = $hostStatus.startedAt
    mode = "Exited"
    exitedAt = (Get-Date).ToString("o")
    runAsPerStep = $hostStatus.runAsPerStep
    acceptedCommands = $hostStatus.acceptedCommands
}
Write-JsonFile -Value $finalStatus -Path $hostStatusPath
