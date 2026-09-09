param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int] $ProcessId,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"
$expectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"
$maximumModuleBytes = 16MB
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $resolvedOutput.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Read-only module output must remain inside the workspace."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'),
        "-ProcessId", $ProcessId,
        "-OutputPath", ('"' + $OutputPath + '"')
    )
    $elevated = Start-Process `
        -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($elevated.ExitCode -ne 0) {
        throw "Elevated read-only module export failed with exit code $($elevated.ExitCode)."
    }
    Get-Content -LiteralPath "$resolvedOutput.manifest.json" -Raw -Encoding UTF8
    exit 0
}

$process = Get-Process -Id $ProcessId -ErrorAction Stop
if ([string]$process.ProcessName -cne "God2_opt") {
    throw "The selected process is not God2_opt.exe."
}

$mainModule = $process.MainModule
if ($null -eq $mainModule -or $mainModule.ModuleMemorySize -le 0 -or
    $mainModule.ModuleMemorySize -gt $maximumModuleBytes) {
    throw "The God2 module size is missing or exceeds the 16 MiB evidence limit."
}
$clientSha256 = (Get-FileHash -LiteralPath $mainModule.FileName -Algorithm SHA256).Hash
if (-not [string]::Equals($clientSha256, $expectedClientSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The running God2 client build does not match the approved executable SHA-256."
}

. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Initialize-God2NativeApi
$baseAddress = [uint32]$mainModule.BaseAddress.ToInt64()
$bytes = [God2Automation.NativeApi]::ReadProcessBytes(
    $ProcessId,
    $baseAddress,
    $mainModule.ModuleMemorySize)
if ($bytes.Length -ne $mainModule.ModuleMemorySize) {
    throw "ReadProcessMemory returned a partial module image."
}

[IO.Directory]::CreateDirectory((Split-Path $resolvedOutput -Parent)) | Out-Null
[IO.File]::WriteAllBytes($resolvedOutput, $bytes)
$manifest = [ordered]@{
    schemaVersion = "god2-read-only-module-code-v1"
    processId = $ProcessId
    processName = "God2_opt"
    executableSha256 = $clientSha256
    moduleBaseAddress = ('0x{0:X8}' -f $baseAddress)
    moduleBytes = $bytes.Length
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
