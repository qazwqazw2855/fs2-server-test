param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int] $ProcessId,
    [Parameter(Mandatory = $true)]
    [uint32] $StartAddress,
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 1048576)]
    [int] $Length,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $resolvedOutput.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Read-only code output must remain inside the workspace."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'),
        "-ProcessId", $ProcessId,
        "-StartAddress", $StartAddress,
        "-Length", $Length,
        "-OutputPath", ('"' + $OutputPath + '"')
    )
    $elevated = Start-Process `
        -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($elevated.ExitCode -ne 0) {
        throw "Elevated read-only code export failed with exit code $($elevated.ExitCode)."
    }
    Get-Content -LiteralPath "$resolvedOutput.manifest.json" -Raw -Encoding UTF8
    exit 0
}

$process = Get-Process -Id $ProcessId -ErrorAction Stop
if ([string]$process.ProcessName -cne "God2_opt") {
    throw "The selected process is not God2_opt.exe."
}

. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Initialize-God2NativeApi
$bytes = [God2Automation.NativeApi]::ReadProcessBytes(
    $ProcessId,
    $StartAddress,
    $Length)
if ($bytes.Length -ne $Length) {
    throw "ReadProcessMemory returned a partial code window."
}

[IO.Directory]::CreateDirectory((Split-Path $resolvedOutput -Parent)) | Out-Null
[IO.File]::WriteAllBytes($resolvedOutput, $bytes)
$manifest = [ordered]@{
    schemaVersion = "god2-read-only-code-window-v1"
    processId = $ProcessId
    processName = "God2_opt"
    startAddress = ('0x{0:X8}' -f $StartAddress)
    length = $bytes.Length
    sha256 = (Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256).Hash
    access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
    gameMemoryWritten = $false
    exportedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$manifestPath = "$resolvedOutput.manifest.json"
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))
$manifest | ConvertTo-Json -Depth 6
