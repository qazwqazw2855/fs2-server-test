param(
    [string] $ClientExe = "",
    [string] $RunId = "",
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"

function Get-PeArchitecture([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
    if ($machine -eq 0x14c) { return "x86" }
    if ($machine -eq 0x8664) { return "x64" }
    return ("unknown-0x{0:X4}" -f $machine)
}

$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$workspaceRoot = Split-Path -Parent $repoRoot

if ($ClientExe.Length -eq 0) {
    $candidates = Get-ChildItem -Path $workspaceRoot -Recurse -File -Filter "God2_opt.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "OfficialClientWorkingCopy" } |
        Sort-Object FullName -Descending
    if (-not $candidates -or $candidates.Count -eq 0) {
        throw "God2_opt.exe official client working copy was not found under $workspaceRoot"
    }
    $ClientExe = $candidates[0].FullName
}

$ClientExe = (Resolve-Path -LiteralPath $ClientExe).Path
$clientRoot = Split-Path -Parent $ClientExe
$arch = Get-PeArchitecture $ClientExe
if ($arch -ne "x86") {
    throw "Client architecture must be x86. Actual: $arch"
}

if ($RunId.Length -eq 0) {
    $RunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

$runDir = Join-Path $repoRoot "Artifacts\ClientInstrumentation\LoginTrial\$RunId"
$sensitiveDir = Join-Path $runDir "sensitive"
$analysisDir = Join-Path $runDir "analysis"
New-Item -ItemType Directory -Force -Path $runDir,$sensitiveDir,$analysisDir | Out-Null

if (-not $SkipBuild) {
    & (Join-Path $toolRoot "scripts\Build-Instrumentation.ps1") -Configuration Both | Out-Host
}

$sha = (Get-FileHash -LiteralPath $ClientExe -Algorithm SHA256).Hash
$manifest = [ordered]@{
    runId = $RunId
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    repoRoot = $repoRoot
    client = [ordered]@{
        exePath = $ClientExe
        root = $clientRoot
        fileName = [IO.Path]::GetFileName($ClientExe)
        architecture = $arch
        sha256 = $sha
        length = (Get-Item -LiteralPath $ClientExe).Length
        onDiskMutationAllowed = $false
    }
    instrumentation = [ordered]@{
        method = "x86 LoadLibraryW DLL injection with IAT and dynamic Winsock hooks"
        hooks = @("send", "WSASend", "recv", "WSARecv")
        generalLogPolicy = "metadata-only"
        sensitiveTraceDirectory = $sensitiveDir
    }
    scope = @("Login handshake", "208-byte Login Request", "send/WSASend boundary", "credential differential")
}

$manifestPath = Join-Path $runDir "source-manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

[pscustomobject]@{
    runDir = $runDir
    manifest = $manifestPath
    clientExe = $ClientExe
    architecture = $arch
    sha256 = $sha
} | ConvertTo-Json -Depth 4
