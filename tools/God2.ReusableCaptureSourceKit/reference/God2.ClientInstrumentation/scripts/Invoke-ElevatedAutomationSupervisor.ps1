param(
    [Parameter(Mandatory = $true)] [string] $RepoRoot,
    [Parameter(Mandatory = $true)] [string] $HostRoot,
    [switch] $SmokeChild,
    [int] $IdleSeconds = 3600
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$HostRoot = [System.IO.Path]::GetFullPath($HostRoot)
New-Item -ItemType Directory -Force -Path $HostRoot | Out-Null

$lockPath = Join-Path $HostRoot "supervisor.lock"
try {
    $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
}
catch {
    exit 0
}

$logRoot = Join-Path $HostRoot "logs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$supervisorLog = Join-Path $logRoot "supervisor.log"
$hostScript = Join-Path $PSScriptRoot "Invoke-ElevatedAutomationHost.ps1"

function Quote-ProcessArgument {
    param([string] $Value)
    if ($Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

try {
    while (-not (Test-Path -LiteralPath (Join-Path $HostRoot "stop-supervisor"))) {
        $arguments = @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $hostScript,
            "-RepoRoot", $RepoRoot, "-HostRoot", $HostRoot, "-IdleSeconds", $IdleSeconds
        )
        if ($SmokeChild) { $arguments += "-SmokeChild" }

        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        $psi.Arguments = (($arguments | ForEach-Object { Quote-ProcessArgument -Value $_ }) -join " ")
        $psi.WorkingDirectory = $RepoRoot
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        $child = [System.Diagnostics.Process]::Start($psi)
        "$(Get-Date -Format o) controller-start pid=$($child.Id)" | Add-Content -LiteralPath $supervisorLog -Encoding UTF8
        $child.WaitForExit()
        "$(Get-Date -Format o) controller-exit pid=$($child.Id) exit=$($child.ExitCode); restarting" | Add-Content -LiteralPath $supervisorLog -Encoding UTF8
        Start-Sleep -Milliseconds 100
    }
}
finally {
    $lock.Dispose()
}
